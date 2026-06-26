#!/usr/bin/env bash
set -euo pipefail

script_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
output="${script_root}/.build/release/BleProbe"

mkdir -p "$(dirname "${output}")"
swiftc \
  "${script_root}/Sources/BleProbe/main.swift" \
  -o "${output}" \
  -framework Foundation \
  -framework CoreBluetooth

echo "${output}"
