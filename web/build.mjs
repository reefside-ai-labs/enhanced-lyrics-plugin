import { build } from 'esbuild';
await build({ entryPoints: ['main.mjs'], bundle: true, format: 'iife', target: ['es2022'],
    outfile: '../Jellyfin.Plugin.EnhancedLyrics/Web/dist/enhanced-lyrics.js', minify: true, legalComments: 'eof' });
