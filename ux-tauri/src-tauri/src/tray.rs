//! Native system tray (Tauri v2). Replaces the old Eto `TrayIcon.cs`, which was
//! platform-inconsistent and disabled on Linux. Menu: dynamic presets, Show,
//! Detect tablet, Quit. Actions route through the daemon RPC bridge.

use serde_json::json;
use tauri::{
    menu::{Menu, MenuItem, PredefinedMenuItem, Submenu},
    tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent},
    AppHandle, Manager,
};

use crate::daemon::DaemonClient;

const TRAY_ID: &str = "otd-tray";

/// Build the tray icon with an initial (preset-less) menu.
pub fn setup(app: &AppHandle) -> tauri::Result<()> {
    let Some(icon) = app.default_window_icon().cloned() else {
        eprintln!("[tray] no app icon available; skipping tray");
        return Ok(());
    };
    let menu = build_menu(app, &[])?;
    TrayIconBuilder::with_id(TRAY_ID)
        .icon(icon)
        .tooltip("OpenTabletDriver")
        .menu(&menu)
        .show_menu_on_left_click(false)
        .on_menu_event(on_menu_event)
        .on_tray_icon_event(|tray, event| {
            // Left click shows/focuses the window.
            if let TrayIconEvent::Click {
                button: MouseButton::Left,
                button_state: MouseButtonState::Up,
                ..
            } = event
            {
                show_main(tray.app_handle());
            }
        })
        .build(app)?;
    Ok(())
}

fn on_menu_event(app: &AppHandle, event: tauri::menu::MenuEvent) {
    let id = event.id.as_ref();
    match id {
        "show" => show_main(app),
        "quit" => app.exit(0),
        "detect" => rpc_fire(app, "DetectTablets", json!([])),
        _ if id.starts_with("preset::") => {
            let name = id.trim_start_matches("preset::").to_string();
            rpc_fire(app, "ApplyPreset", json!([name]));
        }
        _ => {}
    }
}

/// Fire-and-forget daemon call from a menu action.
fn rpc_fire(app: &AppHandle, method: &str, params: serde_json::Value) {
    let client = app.state::<DaemonClient>().inner().clone();
    let method = method.to_string();
    tauri::async_runtime::spawn(async move {
        if let Err(e) = client.call(method.clone(), params).await {
            eprintln!("[tray] {method} failed: {e}");
        }
    });
}

/// Re-fetch presets and rebuild the tray menu. Call when the daemon connects
/// or settings change.
pub fn refresh_presets(app: &AppHandle) {
    let app = app.clone();
    let client = app.state::<DaemonClient>().inner().clone();
    tauri::async_runtime::spawn(async move {
        let presets: Vec<String> = client
            .call("GetPresets".to_string(), json!([]))
            .await
            .ok()
            .and_then(|v| serde_json::from_value(v).ok())
            .unwrap_or_default();

        if let Some(tray) = app.tray_by_id(TRAY_ID) {
            if let Ok(menu) = build_menu(&app, &presets) {
                let _ = tray.set_menu(Some(menu));
            }
        }
    });
}

fn build_menu(app: &AppHandle, presets: &[String]) -> tauri::Result<Menu<tauri::Wry>> {
    let show = MenuItem::with_id(app, "show", "Show OpenTabletDriver", true, None::<&str>)?;
    let detect = MenuItem::with_id(app, "detect", "Detect tablet", true, None::<&str>)?;
    let quit = MenuItem::with_id(app, "quit", "Quit", true, None::<&str>)?;
    let sep1 = PredefinedMenuItem::separator(app)?;
    let sep2 = PredefinedMenuItem::separator(app)?;

    // Presets submenu (disabled placeholder when empty).
    let presets_sub = if presets.is_empty() {
        let none = MenuItem::with_id(app, "presets::none", "No presets", false, None::<&str>)?;
        Submenu::with_items(app, "Presets", true, &[&none])?
    } else {
        let items: Vec<MenuItem<tauri::Wry>> = presets
            .iter()
            .map(|p| MenuItem::with_id(app, format!("preset::{p}"), p, true, None::<&str>))
            .collect::<tauri::Result<_>>()?;
        let refs: Vec<&dyn tauri::menu::IsMenuItem<tauri::Wry>> = items
            .iter()
            .map(|i| i as &dyn tauri::menu::IsMenuItem<tauri::Wry>)
            .collect();
        Submenu::with_items(app, "Presets", true, &refs)?
    };

    Menu::with_items(app, &[&show, &sep1, &detect, &presets_sub, &sep2, &quit])
}

/// Show and focus the main window (creating nothing; window is hidden on close).
pub fn show_main(app: &AppHandle) {
    if let Some(win) = app.get_webview_window("main") {
        let _ = win.show();
        let _ = win.unminimize();
        let _ = win.set_focus();
    }
}
