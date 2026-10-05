#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
npm --prefix web ci
npm --prefix web run build
dotnet test Jellyfin.Plugin.EnhancedLyrics.slnx -c Release
npm --prefix web test
mkdir -p artifacts/EnhancedLyrics
cp Jellyfin.Plugin.EnhancedLyrics/bin/Release/net10.0/Jellyfin.Plugin.EnhancedLyrics.dll artifacts/EnhancedLyrics/
cp Jellyfin.Plugin.EnhancedLyrics/bin/Release/net10.0/TtmlLyricParser.dll artifacts/EnhancedLyrics/
cp scripts/integrate-web.py artifacts/
cp README.md LICENSE THIRD-PARTY-NOTICES.md artifacts/
cp -R licenses artifacts/
(cd artifacts && zip -qr enhanced-lyrics-1.0.0.zip EnhancedLyrics integrate-web.py README.md LICENSE THIRD-PARTY-NOTICES.md licenses)
