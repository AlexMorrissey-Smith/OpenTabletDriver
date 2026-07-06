#!/usr/bin/env bash
# Build the Tauri app with the C# daemon bundled as a sidecar.
#
# Publishes OpenTabletDriver.Daemon self-contained for the target RID, drops it
# into src-tauri/binaries/ with the Rust target-triple suffix Tauri expects, then
# runs `tauri build`. The old Console app is intentionally NOT bundled.
#
# Usage: scripts/build.sh [rid]   (rid: osx-arm64 | osx-x64 | win-x64 | linux-x64)
set -euo pipefail

RID="${1:-osx-arm64}"
case "$RID" in
  osx-arm64)  TRIPLE="aarch64-apple-darwin" ;;
  osx-x64)    TRIPLE="x86_64-apple-darwin" ;;
  win-x64)    TRIPLE="x86_64-pc-windows-msvc" ;;
  linux-x64)  TRIPLE="x86_64-unknown-linux-gnu" ;;
  *) echo "unknown rid: $RID" >&2; exit 1 ;;
esac

HERE="$(cd "$(dirname "$0")/.." && pwd)"
REPO="$(cd "$HERE/.." && pwd)"
BINARIES="$HERE/src-tauri/binaries"
EXT=""; [[ "$RID" == win-* ]] && EXT=".exe"

echo "==> Publishing daemon ($RID)"
dotnet publish "$REPO/OpenTabletDriver.Daemon/OpenTabletDriver.Daemon.csproj" \
  -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=true -p:PublishTrimmed=false -p:DebugType=none \
  -o "$HERE/.daemon-publish"

mkdir -p "$BINARIES"
cp "$HERE/.daemon-publish/OpenTabletDriver.Daemon${EXT}" \
   "$BINARIES/OpenTabletDriver.Daemon-${TRIPLE}${EXT}"
chmod +x "$BINARIES/OpenTabletDriver.Daemon-${TRIPLE}${EXT}" || true

# macOS native helpers (BLE bridge for WH851 bluetooth, wheel overlay).
if [[ "$RID" == osx-* ]] && hash swiftc 2>/dev/null; then
  echo "==> Building Swift helpers"
  swiftc "$REPO/tools/macos/WH851BleBridge/Sources/WH851BleBridge/main.swift" \
    -o "$BINARIES/OpenTabletDriver.BleBridge-${TRIPLE}" \
    -framework Foundation -framework CoreBluetooth
  swiftc "$REPO/tools/macos/WheelModeOverlay/Sources/WheelModeOverlay/main.swift" \
    -o "$BINARIES/OpenTabletDriver.WheelModeOverlay-${TRIPLE}" \
    -framework AppKit -framework Foundation
  swiftc "$REPO/tools/macos/DisplaySwapOverlay/Sources/DisplaySwapOverlay/main.swift" \
    -o "$BINARIES/OpenTabletDriver.DisplaySwapOverlay-${TRIPLE}" \
    -framework AppKit -framework Foundation
fi

echo "==> Building Tauri app"
cd "$HERE"
pnpm install --frozen-lockfile

# Signing: pick Developer ID if present (permissions/TCC grants stick to signed builds).
if [[ "$RID" == osx-* && -z "${APPLE_SIGNING_IDENTITY:-}" ]]; then
  ID="$(security find-identity -v -p codesigning | sed -n 's/.*"\(Developer ID Application:.*\)"/\1/p' | head -n1)"
  [ -n "$ID" ] && export APPLE_SIGNING_IDENTITY="$ID" && echo "==> Signing as: $ID"
fi

if [[ "$RID" == win-* ]]; then
  # Cross-compile from macOS/Linux: cargo-xwin fetches the MSVC CRT + Windows
  # SDK; NSIS is the only installer bundler that works cross-platform.
  # makensis crashes with std::bad_alloc on Unicode installers unless the
  # locale is UTF-8 (https://sourceforge.net/p/nsis/bugs/1165/).
  export LC_ALL=en_US.UTF-8 LANG=en_US.UTF-8
  pnpm tauri build --runner cargo-xwin --target "$TRIPLE" --bundles nsis
else
  pnpm tauri build --bundles app
fi

# Tauri signs the app; re-sign the .NET daemon with the JIT entitlements it needs,
# then re-seal the bundle (inner first, bundle last — same order as eng/bash/macos).
if [[ "$RID" == osx-* && -n "${APPLE_SIGNING_IDENTITY:-}" ]]; then
  APP="$HERE/src-tauri/target/release/bundle/macos/OpenTabletDriver.app"
  ENTITLEMENTS="$HERE/src-tauri/entitlements.plist"
  echo "==> Re-signing daemon with .NET entitlements"
  codesign --force --options runtime --timestamp=none --entitlements "$ENTITLEMENTS" \
    --identifier net.opentabletdriver --sign "$APPLE_SIGNING_IDENTITY" \
    "$APP/Contents/MacOS/OpenTabletDriver.Daemon"
  codesign --force --options runtime --timestamp=none --entitlements "$ENTITLEMENTS" \
    --identifier net.opentabletdriver.ux --sign "$APPLE_SIGNING_IDENTITY" "$APP"
  codesign --verify --deep --strict "$APP" && echo "==> Signature verified"
fi

echo "==> Done. Bundle under src-tauri/target/release/bundle/"
