#!/usr/bin/env bash
# Package Wretched Whispers as a self-contained desktop app: static-export the SPA into the API's
# wwwroot, then publish a single-file self-contained binary with the Photino native shell.
#
# Usage:  ./build-desktop.sh [rid]
#   rid defaults to the host RID; e.g. linux-x64, win-x64, osx-arm64, osx-x64
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
WEB="$ROOT/wretched-whispers-web"
API="$ROOT/WrtechedWhispers/WretchedWhispers.Api"
RID="${1:-$(dotnet --info | awk -F'[:] ' '/RID:/ {print $2; exit}' | tr -d ' ')}"

echo "==> [1/3] Building frontend (static export)"
cd "$WEB"
NEXT_EXPORT=1 NEXT_PUBLIC_DESKTOP=1 NEXT_PUBLIC_API_URL="" npm run build

echo "==> [2/3] Copying static UI into wwwroot"
rm -rf "$API/wwwroot"
mkdir -p "$API/wwwroot"
cp -r "$WEB/out/." "$API/wwwroot/"

echo "==> [3/3] Publishing self-contained desktop app ($RID)"
cd "$ROOT/WrtechedWhispers"
dotnet publish WretchedWhispers.Api/WretchedWhispers.Api.csproj \
  -c Release -r "$RID" --self-contained \
  -p:DesktopBuild=true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o "$ROOT/dist/$RID"

echo "==> Done → $ROOT/dist/$RID"
