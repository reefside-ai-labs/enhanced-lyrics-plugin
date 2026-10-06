FROM jellyfin/jellyfin:latest AS jellyfin
FROM node:24-alpine AS web-build
WORKDIR /src/web
COPY web/package*.json ./
RUN npm ci
COPY web/ ./
RUN npm run build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS plugin-build
WORKDIR /src
COPY Directory.Build.props .editorconfig ./
COPY Jellyfin.Plugin.EnhancedLyrics/ Jellyfin.Plugin.EnhancedLyrics/
COPY --from=web-build /src/Jellyfin.Plugin.EnhancedLyrics/Web/dist/ Jellyfin.Plugin.EnhancedLyrics/Web/dist/
RUN dotnet build Jellyfin.Plugin.EnhancedLyrics/Jellyfin.Plugin.EnhancedLyrics.csproj -c Release
FROM jellyfin AS runtime
COPY --from=plugin-build /src/Jellyfin.Plugin.EnhancedLyrics/bin/Release/net10.0/Jellyfin.Plugin.EnhancedLyrics.dll /opt/enhanced-lyrics/
COPY --from=plugin-build /src/Jellyfin.Plugin.EnhancedLyrics/bin/Release/net10.0/TtmlLyricParser.dll /opt/enhanced-lyrics/
COPY scripts/docker-entrypoint.sh /opt/enhanced-lyrics/entrypoint.sh
ENTRYPOINT ["/bin/sh", "/opt/enhanced-lyrics/entrypoint.sh"]
