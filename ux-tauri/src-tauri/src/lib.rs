mod daemon;
mod overlay;
mod tray;
mod watchdog;

use std::sync::atomic::{AtomicBool, Ordering};

use daemon::{DaemonClient, RpcError, EVENT_CONNECTION};
use serde_json::Value;
use tauri::{Listener, Manager, State};

/// Whether the tray icon actually exists. Without it, hiding the main window
/// on close would leave an unreachable headless process, so close must quit.
static TRAY_ACTIVE: AtomicBool = AtomicBool::new(false);

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

/// Hold an NSProcessInfo activity assertion for the process lifetime so macOS
/// App Nap never throttles us. Once the window is hidden and we're an accessory
/// app, App Nap would otherwise suspend us after a few minutes idle — and since
/// we're the daemon's responsible parent, the daemon gets dragged down with us,
/// so the tablet "stops working after a while". `UserInitiatedAllowingIdleSystemSleep`
/// blocks App Nap but still lets the Mac sleep normally. The assertion is
/// intentionally leaked; it lasts until the process exits.
#[cfg(target_os = "macos")]
fn disable_app_nap() {
    use objc2_foundation::{NSActivityOptions, NSProcessInfo, NSString};
    let activity = NSProcessInfo::processInfo().beginActivityWithOptions_reason(
        NSActivityOptions::UserInitiatedAllowingIdleSystemSleep,
        &NSString::from_str("Processing tablet input in the background"),
    );
    std::mem::forget(activity);
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    let app = tauri::Builder::default()
        .plugin(tauri_plugin_single_instance::init(|app, _args, _cwd| {
            // Second launch: surface the existing window instead.
            tray::show_main(app);
        }))
        .plugin(tauri_plugin_fs::init())
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_opener::init())
        .setup(|app| {
            let handle = app.handle().clone();

            // Transport bridge; it also (re)spawns the daemon as needed.
            let client = DaemonClient::start(handle.clone());
            app.manage(client);

            // Native tray; repopulate presets whenever the daemon connects.
            match tray::setup(&handle) {
                Ok(()) => TRAY_ACTIVE.store(true, Ordering::SeqCst),
                Err(e) => eprintln!("[tray] setup failed: {e}"),
            }
            let tray_handle = handle.clone();
            app.listen(EVENT_CONNECTION, move |event| {
                if event.payload().trim() == "true" {
                    tray::refresh_presets(&tray_handle);
                }
            });

            // Launch with no window: menubar-only (no Dock/menu-bar app icon).
            // The tray "Show" / left-click / Dock-reopen brings the window up.
            #[cfg(target_os = "macos")]
            {
                let _ = handle.set_activation_policy(tauri::ActivationPolicy::Accessory);
                disable_app_nap();
            }

            Ok(())
        })
        // Close = hide to tray (main window only, and only when the tray is
        // actually there to bring it back); the tray "Quit" item fully exits.
        .on_window_event(|window, event| {
            if let tauri::WindowEvent::CloseRequested { api, .. } = event {
                if window.label() == "main" && TRAY_ACTIVE.load(Ordering::SeqCst) {
                    api.prevent_close();
                    let _ = window.hide();
                    // No window open → drop back to accessory (menubar only).
                    #[cfg(target_os = "macos")]
                    let _ = window
                        .app_handle()
                        .set_activation_policy(tauri::ActivationPolicy::Accessory);
                }
            }
        })
        .invoke_handler(tauri::generate_handler![
            rpc_call,
            daemon_connected,
            open_permission_pane
        ])
        .build(tauri::generate_context!())
        .expect("error while building tauri application");

    app.run(|app_handle, event| {
        // macOS Dock icon click while the window is hidden in the tray.
        #[cfg(target_os = "macos")]
        if let tauri::RunEvent::Reopen { .. } = event {
            tray::show_main(app_handle);
        }
        #[cfg(not(target_os = "macos"))]
        let _ = (app_handle, &event);
    });
}
