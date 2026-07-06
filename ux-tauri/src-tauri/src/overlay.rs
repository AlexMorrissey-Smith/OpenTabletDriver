//! Transparent HUD window for daemon `Overlay` events (wheel mode, display
//! swap). macOS draws these natively in the daemon's Swift helpers; here the
//! GUI renders them in a click-through always-on-top webview.

use std::sync::atomic::{AtomicU64, Ordering};
use tauri::{AppHandle, Manager, WebviewUrl, WebviewWindowBuilder};

static GENERATION: AtomicU64 = AtomicU64::new(0);
const LABEL: &str = "overlay";
const DISMISS_MS: u64 = 1400;

/// Show (or refresh) the HUD with the given `OverlayRequest` JSON.
pub fn show(app: &AppHandle, request: &serde_json::Value) {
    let gen = GENERATION.fetch_add(1, Ordering::SeqCst) + 1;

    // Recreate each time: the payload rides in the URL hash, and the window is
    // tiny/transient. ponytail: reuse-and-emit if flicker ever matters.
    if let Some(win) = app.get_webview_window(LABEL) {
        let _ = win.close();
    }

    let data = urlencoding_encode(&request.to_string());
    let url = format!("index.html#overlay={data}");

    let monitor = app
        .primary_monitor()
        .ok()
        .flatten()
        .map(|m| (m.position().clone(), m.size().clone(), m.scale_factor()));
    let (pos, size, scale) = match monitor {
        Some(m) => m,
        None => return,
    };

    let builder = WebviewWindowBuilder::new(app, LABEL, WebviewUrl::App(url.into()))
        .decorations(false)
        .always_on_top(true)
        .skip_taskbar(true)
        .focused(false)
        .shadow(false)
        .position(pos.x as f64 / scale, pos.y as f64 / scale)
        .inner_size(
            size.width as f64 / scale,
            size.height as f64 / scale,
        );

    // The daemon only emits Overlay events off-macOS (Swift helpers cover
    // macOS), and `transparent` needs the private-api feature there.
    #[cfg(not(target_os = "macos"))]
    let builder = builder.transparent(true);

    match builder.build() {
        Ok(win) => {
            let _ = win.set_ignore_cursor_events(true);
            let app = app.clone();
            tauri::async_runtime::spawn(async move {
                tokio_sleep(DISMISS_MS).await;
                if GENERATION.load(Ordering::SeqCst) == gen {
                    if let Some(win) = app.get_webview_window(LABEL) {
                        let _ = win.close();
                    }
                }
            });
        }
        Err(e) => eprintln!("[overlay] failed to create window: {e}"),
    }
}

async fn tokio_sleep(ms: u64) {
    tokio::time::sleep(std::time::Duration::from_millis(ms)).await;
}

/// Percent-encode for a URL fragment (only what JSON needs).
fn urlencoding_encode(s: &str) -> String {
    let mut out = String::with_capacity(s.len() * 2);
    for b in s.bytes() {
        match b {
            b'A'..=b'Z' | b'a'..=b'z' | b'0'..=b'9' | b'-' | b'_' | b'.' | b'~' => {
                out.push(b as char)
            }
            _ => out.push_str(&format!("%{b:02X}")),
        }
    }
    out
}
