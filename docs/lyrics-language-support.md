# Translation and romanization support

Implementation follow-up: authored variant display and controls are now implemented. See [the playback report](../tests/visual-playback.md) for verification. The investigation below records the state before that change.

Investigated 2026-10-05. Primary sources inspected: Better Lyrics commit `4c36f5b85d0de1da00bf258c8e8ad04e6e597bcd` and Braccato commit `46f7a76c8485c674eae71afbe7b2351350b5b0b4`. Better Lyrics currently pins the same `@braccato/core` version used by this plugin, **1.16.3**, plus `@braccato/parsers` 0.3.2. [Dependency manifest](https://github.com/better-lyrics/better-lyrics/blob/4c36f5b85d0de1da00bf258c8e8ad04e6e597bcd/package.json#L47-L51)

## Findings

**Both projects support displaying translations and romanizations. The missing display in this plugin is an integration gap, not an upstream capability limit.** Braccato supplies rendering primitives; Better Lyrics supplies selection, settings, provider metadata, and optional online enrichment.

| Capability | Upstream behavior |
| --- | --- |
| Translation display | `injectTranslation` appends a secondary text row, labels its language, and determines text direction. It displays plain text, without independent word timing. |
| Romanization display | `injectRomanization` inserts a row before a translation. A plain string displays as plain text; supplied `timedRomanization` parts use the lyric animation machinery when rich sync is enabled. |
| Automatic rendering from `Lyric` fields | The element/renderer does not automatically consume translation or romanization fields when building normal lyric lines. Consumers call the injection helpers on the built line, then schedule a layout update. |
| TTML variants | `@braccato/parsers` resolves translation/transliteration metadata to lyric lines by key, emits language-indexed translations, and preserves authored timed romanization parts. |
| Online generation | Better Lyrics, rather than Braccato core, requests missing translations/romanizations from external services. Generated romanizations are strings; this does not generate syllable timing. |

Rendering behavior is implemented in [Braccato injection helpers](https://github.com/better-lyrics/braccato/blob/46f7a76c8485c674eae71afbe7b2351350b5b0b4/packages/core/src/inject.ts#L585-L628). Its [integration documentation](https://github.com/better-lyrics/braccato/blob/46f7a76c8485c674eae71afbe7b2351350b5b0b4/packages/core/README.md#L378-L386) explicitly pairs injection with `renderer.scheduleLyricPositionUpdate`.

## What Better Lyrics adds

Better Lyrics has independent translation and romanization toggles, a target translation language, and per-language exclusions. Its processing pass prefers authored romanization (including timing), then cached text; for translation it selects a matching language from the authored `translations` map, accepts the legacy single translation shape, then checks cache. It injects rows only when the relevant toggle is enabled and the result differs from the original. [Processing and selection](https://github.com/better-lyrics/better-lyrics/blob/4c36f5b85d0de1da00bf258c8e8ad04e6e597bcd/src/modules/lyrics/injectLyrics.ts#L240-L341), [settings](https://github.com/better-lyrics/better-lyrics/blob/4c36f5b85d0de1da00bf258c8e8ad04e6e597bcd/src/modules/settings/settings.ts#L425-L445)

When variants are absent, it batches requests to Unison's `/translate` endpoint, with Google Translate endpoints as fallback. Translation requests ask for a selected target language; romanization requests ask for a Latin-script representation. Requests may return no usable result, so generation is best effort. The supported-language list and script classification guide romanization, and the generator skips Latin-only strings. These requests require network services; the display helpers do not. [Translation and romanization pipeline](https://github.com/better-lyrics/better-lyrics/blob/4c36f5b85d0de1da00bf258c8e8ad04e6e597bcd/src/modules/lyrics/translation.ts#L60-L423), [service URLs and language list](https://github.com/better-lyrics/better-lyrics/blob/4c36f5b85d0de1da00bf258c8e8ad04e6e597bcd/src/core/constants.ts#L103-L152)

Better Lyrics calls `injectRomanization` / `injectTranslation` rather than expecting `setLyrics` to render these fields. It records these additions separately so other views can replay them, and requests a layout update after additions. [Decoration replay](https://github.com/better-lyrics/better-lyrics/blob/4c36f5b85d0de1da00bf258c8e8ad04e6e597bcd/src/modules/lyrics/lyricDecorations.ts), [layout update](https://github.com/better-lyrics/better-lyrics/blob/4c36f5b85d0de1da00bf258c8e8ad04e6e597bcd/src/modules/ui/mainLyricsView.ts#L56-L60)

## TTML interoperability

The upstream parser handles Apple-style translation metadata with a language on `<translation>` and line reference on its `<text for="L1">`, and another dialect with language on `<translations>` and reference on `<translation for="L1">`. Transliteration metadata uses `<text for="L1">`; its timed spans produce `timedRomanization`. It does not simply display arbitrary metadata XML. [Parser mapping](https://github.com/better-lyrics/braccato/blob/46f7a76c8485c674eae71afbe7b2351350b5b0b4/packages/parsers/src/ttml.ts#L373-L423), [translation tests](https://github.com/better-lyrics/braccato/blob/46f7a76c8485c674eae71afbe7b2351350b5b0b4/packages/parsers/src/__tests__/ttml.test.ts#L497-L552)

The plugin's visual-test fixture used `am:key` on variant metadata text, rather than the upstream parser's expected `for` reference. Its successful API-preservation test therefore does **not** establish interoperability with this parser. That fixture needs correction before using it to validate upstream variant extraction.

## Implication for this plugin

Current `web/convert.mjs` maps original lines and word timing but discards `LyricDocument.Variants`; current `web/main.mjs` does not invoke the injection helpers. Keeping translation/romanization XML in the API is insufficient for visible rows. A complete integration needs to resolve variants to line keys, choose a translation language, retain authored romanization timing, inject the rows after Braccato builds its lines, and remeasure layout. This can support local TTML variants without adding an online translation service. Automatic generation would be a separate product decision.

This investigation inspected source and installed packages; it did not test Better Lyrics external translation services or claim live availability or output quality.
