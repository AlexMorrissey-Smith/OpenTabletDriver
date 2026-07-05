//! Ensures the C# daemon is running. Replaces the old `DaemonWatchdog.cs`.
//!
//! The daemon enforces single-instance via a mutex, so spawning when one is
//! already up is harmless (the duplicate prints "already running" and exits).
//! We therefore just resolve a daemon command and spawn it best-effort; the
//! transport layer (daemon.rs) connects once it's listening.

use std::path::PathBuf;
use std::process::Command;

/// Try to start the daemon. Never panics; logs and returns on failure.
pub fn ensure_daemon() {
    match resolve_command() {
        Some((program, args)) => {
            let mut cmd = Command::new(&program);
            cmd.args(&args);
            #[cfg(windows)]
            {
                use std::os::windows::process::CommandExt;
                const CREATE_NO_WINDOW: u32 = 0x0800_0000;
                cmd.creation_flags(CREATE_NO_WINDOW);
            }
            match cmd.spawn() {
                Ok(_) => eprintln!("[watchdog] spawned daemon: {program} {args:?}"),
                Err(e) => eprintln!("[watchdog] failed to spawn daemon ({program}): {e}"),
            }
        }
        None => {
            eprintln!("[watchdog] no daemon binary found; will connect if one is already running")
        }
    }
}

/// Resolve how to launch the daemon: a bundled native binary next to the app,
/// or (in dev) the framework-dependent dll via `dotnet`.
fn resolve_command() -> Option<(String, Vec<String>)> {
    // Explicit override (dev/testing).
    if let Ok(path) = std::env::var("OTD_DAEMON") {
        return Some((path, vec![]));
    }

    let exe = std::env::current_exe().ok()?;
    let exe_dir = exe.parent()?;

    // Production: self-contained daemon bundled next to the app executable.
    let bundled = if cfg!(windows) {
        exe_dir.join("OpenTabletDriver.Daemon.exe")
    } else {
        exe_dir.join("OpenTabletDriver.Daemon")
    };
    if bundled.exists() {
        return Some((bundled.to_string_lossy().into_owned(), vec![]));
    }

    // Dev fallback: run the built dll via the dotnet host.
    if let Some(dll) = find_dev_daemon_dll(&exe) {
        return Some((
            "dotnet".to_string(),
            vec![dll.to_string_lossy().into_owned()],
        ));
    }

    None
}

/// Walk up from the exe looking for a repo-relative debug/release build of the daemon.
fn find_dev_daemon_dll(exe: &std::path::Path) -> Option<PathBuf> {
    for root in exe.ancestors() {
        for config in ["Debug", "Release"] {
            for tfm in ["net10.0", "net9.0", "net8.0"] {
                let candidate = root
                    .join("OpenTabletDriver.Daemon")
                    .join("bin")
                    .join(config)
                    .join(tfm)
                    .join("OpenTabletDriver.Daemon.dll");
                if candidate.exists() {
                    return Some(candidate);
                }
            }
        }
    }
    None
}
