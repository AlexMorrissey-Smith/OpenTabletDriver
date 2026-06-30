#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
app_path="${1:-"${repo_root}/dist/OpenTabletDriver.app"}"
bundle_id="net.opentabletdriver"
reset_btm="no"

if [[ "${1:-}" == "--reset-btm" ]]; then
  app_path="${repo_root}/dist/OpenTabletDriver.app"
  reset_btm="yes"
elif [[ "${2:-}" == "--reset-btm" ]]; then
  reset_btm="yes"
fi

if [[ -d "${app_path}" && -f "${app_path}/Contents/Info.plist" ]]; then
  bundle_id="$(/usr/libexec/PlistBuddy -c 'Print CFBundleIdentifier' "${app_path}/Contents/Info.plist")"
fi

echo "Uninstalling OpenTabletDriver state for ${bundle_id}"
echo "Keeping app: ${app_path}"

uid="$(id -u)"
while read -r label; do
  [[ -z "${label}" ]] && continue
  launchctl bootout "gui/${uid}/${label}" >/dev/null 2>&1 || true
done < <(launchctl list | awk -v id="${bundle_id}" '$3 ~ id { print $3 }')

osascript -e 'quit app "OpenTabletDriver"' >/dev/null 2>&1 || true
pkill -f "${app_path}/Contents/MacOS/OpenTabletDriver" >/dev/null 2>&1 || true

tccutil reset All "${bundle_id}" >/dev/null 2>&1 || true

rm -rf "${HOME}/Library/Application Support/OpenTabletDriver"
rm -f "${HOME}/Library/Preferences/${bundle_id}.plist"
rm -f "${HOME}/Library/Application Support/CrashReporter/OpenTabletDriver."*.plist 2>/dev/null || true

for path in \
  "${HOME}/Library/LaunchAgents" \
  "/Library/LaunchAgents" \
  "/Library/LaunchDaemons"
do
  [[ -d "${path}" ]] || continue
  find "${path}" -maxdepth 1 -type f \( \
    -iname '*opentabletdriver*' -o \
    -iname '*tabletdriver*' \
  \) -print -delete 2>/dev/null || true
done

if [[ "${reset_btm}" == "yes" ]]; then
  sfltool resetbtm >/dev/null 2>&1 || true
fi

echo
echo "Remaining OpenTabletDriver processes:"
ps aux | grep -Ei 'opentablet|tabletdriver' | grep -Ev 'grep|language_server|Biome' || true

echo
echo "Remaining Background Items:"
sfltool dumpbtm 2>/dev/null | grep -i "${bundle_id}" || true

echo
echo "Done."
