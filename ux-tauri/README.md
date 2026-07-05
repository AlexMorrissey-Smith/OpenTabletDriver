# OpenTabletDriver — Tauri UI

A Tauri v2 + React + TypeScript rewrite of the OpenTabletDriver GUI, replacing the
old C#/Eto.Forms UX. The driver/daemon backend is unchanged.

## Why

The Eto GUI shipped a second full .NET runtime (~120 MB `MonoBundle`) plus
Eto.Forms just to draw the UI. This replaces that with a small Rust binary + the
system WebView, cutting the macOS bundle from ~280 MB to ~90 MB (also drops the
redundant 77 MB Console). The tray icon is now native and actually works.

## Architecture

```
React UI ──invoke/events──▶ Rust core ──named-pipe JSON-RPC 2.0──▶ OpenTabletDriver.Daemon (C#)
(shadcn / Tailwind v4)      (transport bridge + tray + watchdog)
```

- **Transport** (`src-tauri/src/daemon.rs`): connects to the daemon's .NET named
  pipe / Unix socket (`$TMPDIR/CoreFxPipe_OpenTabletDriver.Daemon`) and speaks
  StreamJsonRpc's `Content-Length`-framed JSON-RPC 2.0. It's DTO-agnostic — raw
  JSON in/out. TypeScript (`src/lib/types.ts`) owns the wire types.
- **Commands**: `rpc_call(method, params)` + `daemon_connected`; server events
  arrive as the `daemon://notify` Tauri event.
- **Tray** (`src-tauri/src/tray.rs`): presets, Show, Detect tablet, Quit.
- **Watchdog** (`src-tauri/src/watchdog.rs`): spawns the bundled daemon sidecar
  (mutex-safe). Set `OTD_DAEMON` to override the daemon path in dev.
- **Frontend**: `src/lib/daemon.ts` (typed IDriverDaemon surface),
  `src/lib/store.ts` (Zustand + immer, debounced autosave), pages under
  `src/components/pages`, dialogs under `src/components/dialogs`.

The daemon gained a few additive read-only RPC methods (`GetPluginTypes`,
`GetLoadedPlugins`, `GetPluginMetadataRepository`, `GetPresets`/`SavePreset`/
`ApplyPreset`) because a webview can't do the in-process .NET reflection the old
GUI used to discover plugin/binding types.

## Develop

```bash
pnpm install
pnpm tauri dev        # needs a running daemon; the watchdog will spawn one
```

If a daemon isn't found automatically, point at a built one:
```bash
OTD_DAEMON="dotnet $(pwd)/../OpenTabletDriver.Daemon/bin/Debug/net10.0/OpenTabletDriver.Daemon.dll" pnpm tauri dev
```

## Build

```bash
scripts/build.sh osx-arm64     # or osx-x64 | win-x64 | linux-x64
```

Publishes the self-contained daemon into `src-tauri/binaries/` as a Tauri sidecar,
then runs `tauri build`. Output under `src-tauri/target/release/bundle/`.
