#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source_file="${repo_root}/tools/macos/WH851InputCapture.swift"
binary="${repo_root}/bin/wh851-input-capture"

mkdir -p "${repo_root}/bin" "${repo_root}/logs"

if [[ ! -x "${binary}" || "${source_file}" -nt "${binary}" ]]; then
  xcrun swiftc \
    "${source_file}" \
    -o "${binary}" \
    -framework Foundation \
    -framework IOKit \
    -framework CoreBluetooth \
    -framework CoreGraphics
fi

cd "${repo_root}"
trap '' TSTP
exec "${binary}" "$@"
