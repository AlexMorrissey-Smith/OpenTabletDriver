#!/usr/bin/env bash

[ "${BUILD}" != "true" ] && exit_with_error "Must build to package MacOS"

pkg_script_root="$(readlink -f $(dirname "${BASH_SOURCE[0]}"))"

echo -e "\nPreparing package..."

PKG_FILE="${OTD_NAME}-${OTD_VERSION}_${NET_RUNTIME}.tar.gz"

pkg_root="${OUTPUT}/${OTD_NAME}.app"

# The UX is a net*-macos app (Microsoft.macOS backend); the SDK produces the .app bundle for us
# (Contents/MacOS launcher + Contents/MonoBundle managed assemblies + Info.plist). Publish it here
# rather than through the generic build() flat-publish.
#
# The .NET macOS SDK pins to a specific Xcode and needs full Xcode (not just Command Line Tools).
# Point it at an Xcode.app if the active developer dir is only the CLT, and don't hard-fail on an
# Xcode major-version mismatch (lets the app build across Xcode 26/27, e.g. on beta setups).
if [ -z "${DEVELOPER_DIR:-}" ]; then
  if ! xcode-select -p 2>/dev/null | grep -q "\.app/"; then
    xcode_app="$(ls -d /Applications/Xcode*.app 2>/dev/null | head -n 1)"
    [ -n "${xcode_app}" ] && export DEVELOPER_DIR="${xcode_app}/Contents/Developer"
  fi
fi

echo "Building macOS UX app bundle..."
dotnet publish "OpenTabletDriver.UX.MacOS/OpenTabletDriver.UX.MacOS.csproj" \
  --configuration "${CONFIG}" \
  --runtime "${NET_RUNTIME}" \
  --self-contained true \
  -p:LinkMode=None \
  -p:CreatePackage=false \
  -p:ValidateXcodeVersion=false \
  || exit_with_error "Failed to build the macOS UX app bundle"

ux_app="$(ls -d OpenTabletDriver.UX.MacOS/bin/${CONFIG}/net*-macos*/${NET_RUNTIME}/${OTD_NAME}.app 2>/dev/null | head -n 1)"
[ -d "${ux_app}/Contents/MacOS" ] || exit_with_error "Could not find published .app bundle at '${ux_app}'"

# Native helpers (optional; only built if the Swift sources + toolchain are present).
helper_source="${pkg_script_root}/../../../tools/macos/WH851BleBridge/Sources/WH851BleBridge/main.swift"
if [ -f "${helper_source}" ] && hash swiftc 2>/dev/null; then
  echo "Building CoreBluetooth bridge..."
  swiftc "${helper_source}" -o "${OUTPUT}/OpenTabletDriver.BleBridge" \
    -framework Foundation -framework CoreBluetooth
fi

overlay_source="${pkg_script_root}/../../../tools/macos/WheelModeOverlay/Sources/WheelModeOverlay/main.swift"
if [ -f "${overlay_source}" ] && hash swiftc 2>/dev/null; then
  echo "Building wheel mode overlay..."
  swiftc "${overlay_source}" -o "${OUTPUT}/OpenTabletDriver.WheelModeOverlay" \
    -framework AppKit -framework Foundation
fi

echo "Assembling app bundle..."
rm -rf "${pkg_root}"
cp -R "${ux_app}" "${pkg_root}"

# Move the daemon, console and native helpers (everything build()/swiftc left in OUTPUT) alongside
# the UX launcher in Contents/MacOS.
for entry in "${OUTPUT}"/*; do
  base="$(basename "${entry}")"
  [ "${base}" == "${OTD_NAME}.app" ] && continue
  mv "${entry}" "${pkg_root}/Contents/MacOS/"
done

echo "Copying MacOS assets..."
mkdir -p "${pkg_root}/Contents/Resources"
cp "${pkg_script_root}/Icon.icns" "${pkg_root}/Contents/Resources/"

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

    # Sign every inner Mach-O (Contents/MacOS executables + Contents/MonoBundle native libs) first,
    # then the daemon/console with their bundle identifiers, then the app bundle itself last.
    while IFS= read -r -d '' file; do
      if file "${file}" | grep -q 'Mach-O'; then
        codesign --force --options runtime "${timestamp_arg[@]}" "${entitlements_arg[@]}" --sign "${codesign_identity}" "${file}"
      fi
    done < <(find "${pkg_root}/Contents" -type f -print0)

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
