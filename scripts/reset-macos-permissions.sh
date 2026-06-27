#!/usr/bin/env bash
set -euo pipefail

APP_PATH="${1:-}"
DEFAULT_BUNDLE_ID="net.opentabletdriver"

bundle_id="$DEFAULT_BUNDLE_ID"
if [[ -n "$APP_PATH" ]]; then
  if [[ ! -d "$APP_PATH" ]]; then
    echo "App path not found: $APP_PATH" >&2
    exit 1
  fi

  plist="$APP_PATH/Contents/Info.plist"
  if [[ ! -f "$plist" ]]; then
    echo "Info.plist not found: $plist" >&2
    exit 1
  fi

  bundle_id="$(/usr/libexec/PlistBuddy -c 'Print CFBundleIdentifier' "$plist")"
fi

echo "Resetting OpenTabletDriver permissions for bundle id: $bundle_id"
echo

echo "Stopping OpenTabletDriver processes..."
osascript -e 'quit app "OpenTabletDriver"' >/dev/null 2>&1 || true
pkill -f 'OpenTabletDriver' >/dev/null 2>&1 || true
sleep 1

reset_service() {
  local service="$1"

  if tccutil reset "$service" "$bundle_id" >/dev/null 2>&1; then
    echo "reset: $service"
  else
    echo "skip:  $service (not present or unsupported on this macOS)"
  fi
}

reset_service "Accessibility"
reset_service "ListenEvent"
reset_service "BluetoothAlways"
reset_service "Bluetooth"

echo
echo "Done."
echo
echo "Next:"
echo "1. Open OpenTabletDriver again."
echo "2. Re-enable Accessibility when macOS asks."
echo "3. Re-enable Input Monitoring when macOS asks."
echo "4. Re-enable Bluetooth if macOS asks."
echo
echo "Settings pages:"
echo "  open 'x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility'"
echo "  open 'x-apple.systempreferences:com.apple.preference.security?Privacy_ListenEvent'"
