#!/usr/bin/env bash
# Compiles the plugin against the AutoCAD .NET API of every supported AutoCAD version
# (NuGet package AutoCAD.NET: 23.1 = 2020, 24.0 = 2021, 24.1 = 2022, 24.2 = 2023, 24.3 = 2024).
# A successful build proves that every API member used by the plugin exists in that version.
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p experiments/logs
for v in 23.1.0 24.0.0 24.1.51000 24.2.0 24.3.0; do
  out="$(mktemp -d)"
  log="experiments/logs/api-$v.log"
  if dotnet build src/ACadHotKey.Plugin/ACadHotKey.Plugin.csproj -c Release -m:1 \
      -p:UseSharedCompilation=false -p:AutoCADNetVersion="$v" -o "$out" > "$log" 2>&1; then
    echo "AutoCAD.NET $v: OK"
    rm -rf "$out"
  else
    rm -rf "$out"
    echo "AutoCAD.NET $v: FAILED (log: $log)"
    exit 1
  fi
done
