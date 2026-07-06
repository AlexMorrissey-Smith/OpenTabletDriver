mod daemon;
mod overlay;
mod tray;
mod watchdog;

use daemon::{DaemonClient, RpcError, EVENT_CONNECTION};
use serde_json::Value;
use tauri::{Listener, Manager, State};

/// Invoke a daemon method by name with positional JSON params.
#[tauri::command]
async fn rpc_call(
    state: State<'_, DaemonClient>,
    method: String,
    params: Option<Value>,
) -> Result<Value, RpcError> {
    state.call(method, params.unwrap_or(Value::Null)).await
}

/// Whether the transport currently has a live connection to the daemon.
#[tauri::command]
fn daemon_connected(state: State<'_, DaemonClient>) -> bool {
    state.is_connected()
}

/// Open a macOS privacy pane so the user can grant the daemon its permissions
/// (ports PermissionHelper.cs). `pane`: "accessibility" | "input-monitoring".
/// No-op on other platforms.
#[tauri::command]
fn open_permission_pane(pane: String) {
    #[cfg(target_os = "macos")]
    {
        let anchor = match pane.as_str() {
            "accessibility" => "Privacy_Accessibility",
            "input-monitoring" => "Privacy_ListenEvent",
            _ => return,
        };
        let _ = std::process::Command::new("open")
            .arg(format!(
                "x-apple.systempreferences:com.apple.preference.security?{anchor}"
            ))
            .spawn();
    }
    #[cfg(not(target_os = "macos"))]
    let _ = pane;
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_fs::init())
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_opener::init())
        .setup(|app| {
            let handle = app.handle().clone();

            // Start the daemon (mutex-safe) and the transport bridge.
            watchdog::ensure_daemon();
            let client = DaemonClient::start(handle.clone());
            app.manage(client);

            // Native tray; repopulate presets whenever the daemon connects.
            if let Err(e) = tray::setup(&handle) {
                eprintln!("[tray] setup failed: {e}");
            }
            let tray_handle = handle.clone();
            app.listen(EVENT_CONNECTION, move |event| {
                if event.payload().trim() == "true" {
                    tray::refresh_presets(&tray_handle);
                }
            });

            // Ensure the window is visible and focused on launch.
            if let Some(win) = handle.get_webview_window("main") {
                let _ = win.unminimize();
                let _ = win.show();
                let _ = win.set_focus();
            }

            Ok(())
        })
        // Close = hide to tray; the tray "Quit" item fully exits.
        .on_window_event(|window, event| {
            if let tauri::WindowEvent::CloseRequested { api, .. } = event {
                api.prevent_close();
                let _ = window.hide();
            }
        })
        .invoke_handler(tauri::generate_handler![
            rpc_call,
            daemon_connected,
            open_permission_pane
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
