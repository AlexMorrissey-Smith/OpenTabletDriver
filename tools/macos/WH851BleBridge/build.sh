#!/usr/bin/env bash
set -euo pipefail

script_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
output="${1:-${script_root}/.build/release/OpenTabletDriver.BleBridge}"

mkdir -p "$(dirname "${output}")"
swiftc \
  "${script_root}/Sources/WH851BleBridge/main.swift" \
  -o "${output}" \
  -framework Foundation \
  -framework CoreBluetooth

echo "${output}"
