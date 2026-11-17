#!/usr/bin/env bash
# Regressions for deployment failures: missing Core dependency and accidentally shipped Autodesk DLLs.
# Run experiments/package.sh first. Uses copies of the real build output, not placeholder DLLs.
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p experiments/logs
scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT
cp -R artifacts/ACadHotKey.bundle "$scratch/"
bundle="$scratch/ACadHotKey.bundle"
python3 experiments/verify-package.py "$bundle" > experiments/logs/package-valid.log 2>&1
rm "$bundle/Contents/2020/ACadHotKey.Core.dll"
if python3 experiments/verify-package.py "$bundle" > experiments/logs/package-missing-core.log 2>&1; then
  echo "ERROR: package without Core dependency was accepted" >&2
  exit 1
fi
cp artifacts/ACadHotKey.bundle/Contents/2020/ACadHotKey.Core.dll "$bundle/Contents/2020/"
cp "$bundle/Contents/2020/ACadHotKey.dll" "$bundle/Contents/2020/AcMgd.dll"
if python3 experiments/verify-package.py "$bundle" > experiments/logs/package-unexpected-dll.log 2>&1; then
  echo "ERROR: package containing Autodesk-named reference DLL was accepted" >&2
  exit 1
fi
echo "Package regressions passed: missing Core and unexpected DLL rejected."
