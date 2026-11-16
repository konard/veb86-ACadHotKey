#!/usr/bin/env bash
# Compiles the plugin against the AutoCAD .NET API of every supported AutoCAD version
# (NuGet package AutoCAD.NET: 23.1 = 2020, 24.0 = 2021, 24.1 = 2022, 24.2 = 2023, 24.3 = 2024).
# A successful build proves that every API member used by the plugin exists in that version.
set -euo pipefail
cd "$(dirname "$0")/.."
for v in 23.1.0 24.0.0 24.1.51000 24.2.0 24.3.0; do
  out="$(mktemp -d)"
  if dotnet build src/ACadHotKey.Plugin/ACadHotKey.Plugin.csproj -p:AutoCADNetVersion="$v" -o "$out" > "$out/build.log" 2>&1; then
    echo "AutoCAD.NET $v: OK"
  else
    echo "AutoCAD.NET $v: FAILED (log: $out/build.log)"; grep -E "error" "$out/build.log" | sort -u | head -20; exit 1
  fi
done
