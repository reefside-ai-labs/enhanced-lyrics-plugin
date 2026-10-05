#!/bin/sh
set -eu
mkdir -p /config/plugins/EnhancedLyrics_1.0.0.0
cp /opt/enhanced-lyrics/*.dll /config/plugins/EnhancedLyrics_1.0.0.0/
exec /jellyfin/jellyfin "$@"
