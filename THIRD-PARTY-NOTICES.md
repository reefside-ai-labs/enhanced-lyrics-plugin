# Third-party notices

Enhanced Lyrics is based on jellyfin/jellyfin-plugin-template and distributed under GPL-3.0 (see LICENSE).

The plugin bundles TtmlLyricParser 0.2.0 by Michael Teuscher, licensed under MIT. The Web bundle includes @braccato/core 1.16.3, @braccato/types 1.0.0 and @braccato/parsers 0.3.2 from better-lyrics/braccato, and @kawarp/core 1.3.1 from better-lyrics/kawarp, licensed under MIT. The TTML variant parser also includes fast-xml-parser and its dependencies (fast-xml-builder, strnum, anynum, is-unsafe, xml-naming, and path-expression-matcher). Their complete copyright and permission notices are included in the licenses directory; exact versions are pinned in web/package-lock.json.

Sources:
- https://github.com/mfteuscher/TtmlLyricParser
- https://github.com/better-lyrics/braccato
- https://github.com/better-lyrics/kawarp

Jellyfin assemblies are supplied by the host and excluded from the plugin distribution. esbuild and the npm/.NET test tools are build-time dependencies and are not bundled into the plugin.
