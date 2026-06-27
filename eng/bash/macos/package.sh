#!/usr/bin/env bash

[ "${BUILD}" != "true" ] && exit_with_error "Must build to package MacOS"

pkg_script_root="$(readlink -f $(dirname "${BASH_SOURCE[0]}"))"

echo -e "\nPreparing package..."

PKG_FILE="${OTD_NAME}-${OTD_VERSION}_${NET_RUNTIME}.tar.gz"

pkg_root="${OUTPUT}/${OTD_NAME}.app"

helper_source="${pkg_script_root}/../../../tools/macos/WH851BleBridge/Sources/WH851BleBridge/main.swift"
if [ -f "${helper_source}" ] && hash swiftc 2>/dev/null; then
  echo "Building WH851 CoreBluetooth bridge..."
  swiftc \
    "${helper_source}" \
    -o "${OUTPUT}/OpenTabletDriver.WH851BleBridge" \
    -framework Foundation \
    -framework CoreBluetooth
fi

overlay_source="${pkg_script_root}/../../../tools/macos/WheelModeOverlay/Sources/WheelModeOverlay/main.swift"
if [ -f "${overlay_source}" ] && hash swiftc 2>/dev/null; then
  echo "Building wheel mode overlay..."
  swiftc \
    "${overlay_source}" \
    -o "${OUTPUT}/OpenTabletDriver.WheelModeOverlay" \
    -framework AppKit \
    -framework Foundation
fi

move_to_nested "${OUTPUT}" "${pkg_root}/Contents/MacOS"
rm -rf "${pkg_root}/Contents/MacOS/OpenTabletDriver.UX.MacOS.app"

echo "Copying MacOS assets..."
mkdir -p "${pkg_root}/Contents/Resources"
cp "${pkg_script_root}/Icon.icns" "${pkg_root}/Contents/Resources/"
cp "${pkg_script_root}/Info.plist" "${pkg_root}/Contents/"

if [ "${SIGNED}" == "true" ]; then
  echo "Signing app bundle..."
  if hash rcodesign 2>/dev/null; then
    rcodesign sign "${pkg_root}"
  elif hash codesign 2>/dev/null; then
    codesign_identity="${CODESIGN_IDENTITY:-}"
    if [ -z "${codesign_identity}" ]; then
      codesign_identity="$(security find-identity -v -p codesigning | sed -n 's/.*"\(Developer ID Application:.*\)"/\1/p' | head -n 1)"
    fi
    if [ -z "${codesign_identity}" ]; then
      codesign_identity="$(security find-identity -v -p codesigning | sed -n 's/.*"\(Apple Development:.*\)"/\1/p' | head -n 1)"
    fi
    if [ -z "${codesign_identity}" ]; then
      exit_with_error "No Developer ID Application or Apple Development signing identity found"
    fi

    timestamp_arg=(--timestamp=none)
    if [ "${codesign_identity}" == "-" ]; then
      timestamp_arg=()
    fi
    entitlements_arg=(--entitlements "${pkg_script_root}/entitlements.plist")

    while IFS= read -r -d '' file; do
      if file "${file}" | grep -q 'Mach-O'; then
        codesign --force --options runtime "${timestamp_arg[@]}" "${entitlements_arg[@]}" --sign "${codesign_identity}" "${file}"
      fi
    done < <(find "${pkg_root}/Contents/MacOS" -type f -print0)

    codesign --force --options runtime "${timestamp_arg[@]}" "${entitlements_arg[@]}" --identifier net.opentabletdriver --sign "${codesign_identity}" "${pkg_root}/Contents/MacOS/OpenTabletDriver.Daemon"
    codesign --force --options runtime "${timestamp_arg[@]}" "${entitlements_arg[@]}" --identifier net.opentabletdriver.console --sign "${codesign_identity}" "${pkg_root}/Contents/MacOS/OpenTabletDriver.Console"
    codesign --force --options runtime "${timestamp_arg[@]}" "${entitlements_arg[@]}" --identifier net.opentabletdriver --sign "${codesign_identity}" "${pkg_root}"
  else
    echo "Warning: neither rcodesign nor codesign found, skipping signing"
  fi
fi

echo "Creating tarball..."
create_binary_tarball "${pkg_root}" "${OUTPUT}/${PKG_FILE}"

echo -e "\nPackaging finished! Package created at '${OUTPUT}/${PKG_FILE}'"
