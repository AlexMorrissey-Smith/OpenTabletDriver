//! Thin bridge between the webview and the C# OpenTabletDriver daemon.
//!
//! The daemon exposes `IDriverDaemon` over a .NET named pipe using StreamJsonRpc
//! (JSON-RPC 2.0, `Content-Length` header framing). On Windows that's a real
//! named pipe; on macOS/Linux .NET maps it to a Unix domain socket at
//! `$TMPDIR/CoreFxPipe_<name>`. We speak that protocol directly so no second
//! .NET runtime is needed for the GUI.
//!
//! This layer is intentionally DTO-agnostic: requests and responses are raw
//! JSON. TypeScript owns the wire types.

use std::collections::HashMap;
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::Arc;
use std::time::Duration;

use serde_json::{json, Value};
use tauri::{AppHandle, Emitter};
use tokio::io::{AsyncBufReadExt, AsyncRead, AsyncReadExt, AsyncWrite, AsyncWriteExt, BufReader};
use tokio::sync::{mpsc, oneshot, Mutex};

/// .NET pipe name the daemon listens on (see `RpcHost<DriverDaemon>` in the C# side).
const PIPE_NAME: &str = "OpenTabletDriver.Daemon";
/// Frontend event carrying daemon->client notifications (`{ method, params }`).
pub const EVENT_NOTIFY: &str = "daemon://notify";
/// Frontend event carrying connection state (`bool`).
pub const EVENT_CONNECTION: &str = "daemon://connection";
/// How long a single RPC call waits for its response before giving up.
const CALL_TIMEOUT: Duration = Duration::from_secs(30);

#[derive(Debug, thiserror::Error)]
pub enum RpcError {
    #[error("not connected to daemon")]
    NotConnected,
    #[error("daemon call timed out")]
    Timeout,
    #[error("connection closed before response")]
    Closed,
    #[error("daemon error: {0}")]
    Daemon(String),
}

impl serde::Serialize for RpcError {
    fn serialize<S: serde::Serializer>(&self, s: S) -> Result<S::Ok, S::Error> {
        s.serialize_str(&self.to_string())
    }
}

type Pending = oneshot::Sender<Result<Value, RpcError>>;

struct Inner {
    next_id: AtomicU64,
    connected: AtomicBool,
    pending: Mutex<HashMap<u64, Pending>>,
    /// Sender to the current connection's writer task; `None` while disconnected.
    outbound: Mutex<Option<mpsc::UnboundedSender<Vec<u8>>>>,
}

/// Cloneable handle stored in Tauri state.
#[derive(Clone)]
pub struct DaemonClient {
    inner: Arc<Inner>,
}

impl DaemonClient {
    /// Create the client and start the background connect/reconnect loop.
    pub fn start(app: AppHandle) -> Self {
        let inner = Arc::new(Inner {
            next_id: AtomicU64::new(1),
            connected: AtomicBool::new(false),
            pending: Mutex::new(HashMap::new()),
            outbound: Mutex::new(None),
        });
        let client = DaemonClient {
            inner: inner.clone(),
        };
        tauri::async_runtime::spawn(serve(inner, app));
        client
    }

    pub fn is_connected(&self) -> bool {
        self.inner.connected.load(Ordering::SeqCst)
    }

    /// Invoke a daemon method. `params` must be a positional JSON array
    /// (StreamJsonRpc marshals proxy arguments positionally).
    pub async fn call(&self, method: String, params: Value) -> Result<Value, RpcError> {
        let params = match params {
            Value::Null => json!([]),
            Value::Array(_) => params,
            other => json!([other]),
        };

        let id = self.inner.next_id.fetch_add(1, Ordering::SeqCst);
        let payload = json!({
            "jsonrpc": "2.0",
            "id": id,
            "method": method,
            "params": params,
        });
        let frame = frame_message(&payload);

        let (tx, rx) = oneshot::channel();
        self.inner.pending.lock().await.insert(id, tx);

        {
            let guard = self.inner.outbound.lock().await;
            match guard.as_ref() {
                Some(sender) if sender.send(frame).is_ok() => {}
                _ => {
                    self.inner.pending.lock().await.remove(&id);
                    return Err(RpcError::NotConnected);
                }
            }
        }

        match tokio::time::timeout(CALL_TIMEOUT, rx).await {
            Ok(Ok(result)) => result,
            Ok(Err(_)) => Err(RpcError::Closed),
            Err(_) => {
                self.inner.pending.lock().await.remove(&id);
                Err(RpcError::Timeout)
            }
        }
    }
}

/// Wrap a JSON value in StreamJsonRpc's `Content-Length` framing.
fn frame_message(value: &Value) -> Vec<u8> {
    let body = serde_json::to_vec(value).expect("json serialize");
    let mut out = format!("Content-Length: {}\r\n\r\n", body.len()).into_bytes();
    out.extend_from_slice(&body);
    out
}

/// Connect/reconnect loop. Runs for the lifetime of the app.
async fn serve(inner: Arc<Inner>, app: AppHandle) {
    loop {
        match connect().await {
            Ok(stream) => {
                run_connection(stream, &inner, &app).await;
            }
            Err(_) => {
                // daemon not up yet; watchdog will (re)spawn it. Keep trying.
            }
        }
        set_connected(&inner, &app, false);
        fail_all_pending(&inner).await;
        tokio::time::sleep(Duration::from_millis(750)).await;
    }
}

/// Drive one live connection until the read side errors/closes.
async fn run_connection<S>(stream: S, inner: &Arc<Inner>, app: &AppHandle)
where
    S: AsyncRead + AsyncWrite + Send + 'static,
{
    let (read_half, write_half) = tokio::io::split(stream);

    // Writer task: drains framed bytes to the socket.
    let (tx, mut rx) = mpsc::unbounded_channel::<Vec<u8>>();
    let writer = tauri::async_runtime::spawn(async move {
        let mut write_half = write_half;
        while let Some(bytes) = rx.recv().await {
            if write_half.write_all(&bytes).await.is_err() {
                break;
            }
            if write_half.flush().await.is_err() {
                break;
            }
        }
    });

    *inner.outbound.lock().await = Some(tx);
    set_connected(inner, app, true);

    // Reader loop: dispatch responses and notifications.
    let mut reader = BufReader::new(read_half);
    loop {
        match read_frame(&mut reader).await {
            Ok(Some(msg)) => dispatch(msg, inner, app).await,
            Ok(None) | Err(_) => break, // EOF or protocol/IO error -> reconnect
        }
    }

    // Tear down: dropping the sender ends the writer task.
    *inner.outbound.lock().await = None;
    writer.abort();
}

async fn dispatch(msg: Value, inner: &Arc<Inner>, app: &AppHandle) {
    // Response: has an id plus result/error.
    if let Some(id) = msg.get("id").and_then(Value::as_u64) {
        if msg.get("result").is_some() || msg.get("error").is_some() {
            if let Some(tx) = inner.pending.lock().await.remove(&id) {
                let outcome = if let Some(err) = msg.get("error") {
                    Err(RpcError::Daemon(
                        err.get("message")
                            .and_then(Value::as_str)
                            .unwrap_or("unknown")
                            .to_string(),
                    ))
                } else {
                    Ok(msg.get("result").cloned().unwrap_or(Value::Null))
                };
                let _ = tx.send(outcome);
            }
            return;
        }
    }

    // Notification (server event): method + params, no id.
    if let Some(method) = msg.get("method").and_then(Value::as_str) {
        let params = msg.get("params").cloned().unwrap_or(Value::Null);

        // HUD overlays are drawn by us, not the page: events must work even
        // when the main window is hidden in the tray.
        if method == "Overlay" {
            if let Some(request) = params.get(0) {
                crate::overlay::show(app, request);
            }
        }

        let _ = app.emit(EVENT_NOTIFY, json!({ "method": method, "params": params }));
    }
}

/// Read one `Content-Length`-framed JSON message. `Ok(None)` on clean EOF.
async fn read_frame<R>(reader: &mut BufReader<R>) -> std::io::Result<Option<Value>>
where
    R: AsyncRead + Unpin,
{
    let mut content_length: Option<usize> = None;

    // Headers, one per line, terminated by a blank line.
    loop {
        let mut line = String::new();
        let n = reader.read_line(&mut line).await?;
        if n == 0 {
            return Ok(None); // EOF
        }
        let trimmed = line.trim_end_matches(['\r', '\n']);
        if trimmed.is_empty() {
            break; // end of headers
        }
        if let Some((name, value)) = trimmed.split_once(':') {
            if name.trim().eq_ignore_ascii_case("content-length") {
                content_length = value.trim().parse().ok();
            }
        }
    }

    let len = content_length.ok_or_else(|| {
        std::io::Error::new(std::io::ErrorKind::InvalidData, "missing Content-Length")
    })?;
    let mut body = vec![0u8; len];
    reader.read_exact(&mut body).await?;

    serde_json::from_slice(&body)
        .map(Some)
        .map_err(|e| std::io::Error::new(std::io::ErrorKind::InvalidData, e))
}

fn set_connected(inner: &Arc<Inner>, app: &AppHandle, connected: bool) {
    let was = inner.connected.swap(connected, Ordering::SeqCst);
    if was != connected {
        let _ = app.emit(EVENT_CONNECTION, connected);
    }
}

async fn fail_all_pending(inner: &Arc<Inner>) {
    let mut pending = inner.pending.lock().await;
    for (_, tx) in pending.drain() {
        let _ = tx.send(Err(RpcError::Closed));
    }
}

// ---- platform-specific connect -------------------------------------------

#[cfg(unix)]
async fn connect() -> std::io::Result<tokio::net::UnixStream> {
    // .NET: Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + pipeName)
    let tmp = std::env::var("TMPDIR").unwrap_or_else(|_| "/tmp".to_string());
    let path = std::path::Path::new(&tmp).join(format!("CoreFxPipe_{PIPE_NAME}"));
    tokio::net::UnixStream::connect(path).await
}

#[cfg(windows)]
async fn connect() -> std::io::Result<tokio::net::windows::named_pipe::NamedPipeClient> {
    use tokio::net::windows::named_pipe::ClientOptions;
    ClientOptions::new().open(format!(r"\\.\pipe\{PIPE_NAME}"))
}
