#!/usr/bin/env bash
# Build a complete autoload bundle, without copying Autodesk reference assemblies.
set -euo pipefail
cd "$(dirname "$0")/.."
bundle="artifacts/ACadHotKey.bundle"
mkdir -p "$bundle/Contents/2020" "$bundle/Contents/2021-2024" experiments/logs
cp bundle/ACadHotKey.bundle/PackageContents.xml "$bundle/PackageContents.xml"
for target in 2020 2021-2024; do
  version=23.1.0
  if [[ "$target" == 2021-2024 ]]; then version=24.0.0; fi
  output="$(mktemp -d)"
  # Build sequentially and turn off the persistent compiler server to bound memory usage.
  if ! dotnet build src/ACadHotKey.Plugin/ACadHotKey.Plugin.csproj -c Release -m:1 \
      -p:UseSharedCompilation=false -p:AutoCADNetVersion="$version" -o "$output" \
      > "experiments/logs/package-$target.log" 2>&1; then
    rm -rf "$output"
    echo "Build failed: experiments/logs/package-$target.log" >&2
    exit 1
  fi
  for filename in ACadHotKey.dll ACadHotKey.Core.dll ACadHotKey.config.xml; do
    cp "$output/$filename" "$bundle/Contents/$target/$filename"
  done
  rm -rf "$output"
done
python3 experiments/verify-package.py "$bundle"
echo "Bundle: $bundle (copy it to %APPDATA%/Autodesk/ApplicationPlugins/)"
