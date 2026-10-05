# Explicit lyric feature playback test

## Follow-up after implementation: 2026-10-05

The corrected fixture uses `for` references, English/French translations, timed romanization for L1, and plain romanization for L2. The Web view now joins these variants by line key with @braccato/parsers 0.3.2 and injects them using Braccato core 1.16.3. Original lyric timing still comes from TtmlLyricParser. Translation language/Off and Romanization controls remember preferences, and accessible lyric labels include the displayed variants. Repeated keys shift romanization cues with the resolved line start; out-of-range cues fall back to plain text.

The final Docker runtime, based on `jellyfin/jellyfin:latest`, reports Jellyfin 12.1.0. Image digest: `sha256:72c1d6e541f8cb8664387502755e8d76cbce3a5ae032b0aa48759cd0dca81de3`.

Actual playback ran from zero through the variant sections, pausing for screenshots. After the final build and full reload, the desktop sample at 3.436 seconds showed “A blue sky”, “Aoi” active, and “sora” upcoming. Continuing at 390×844 showed “Aoi” past and “sora” active at 4.745 seconds, then plain “Hikari ga hikaru” and “Light shines” at 8.455 seconds. Player and renderer clocks matched to floating-point precision. The narrow viewport had no horizontal overflow; it is a desktop browser viewport test, not a physical phone test. Audio is a synthetic tone, not sung vocals.

| Check | Result |
| --- | --- |
| English translation | Pass; both authored lines display. |
| Timed romanization | Pass; word states advance at authored cue boundaries. |
| Plain romanization | Pass; text row without fabricated word timings. |
| French selection | Pass; both translation rows change to French with correct language labels. |
| Both Off controls | Pass; rows disappear, all six originals remain. |
| Preference persistence | Pass; French and romanization Off survive full page reload. |
| Background regression | Pass; at 14.300 seconds “(echo” active, “answer)” upcoming. |
| Overlapping duet regression | Pass; at 20.415 seconds both singers active with left/right alignment. |

Evidence is in `artifacts/visual-tests/language-support/`: `desktop-timed.png`, `desktop-plain.png`, `french.png`, `mobile-timed.png`, `mobile-plain.png`, and sanitized `observations.json`. Build verification: 18 .NET tests and 12 Web tests pass; rich/legacy API smoke checks pass for Visual Features and existing fixtures. This displays authored local variants; it does not generate missing text through online services. Shared multi-agent lines retain the limitation documented below.

## Initial run before variant rendering was implemented

Executed 2026-10-04 America/Denver (2026-10-05 UTC) against the isolated Docker instance at localhost:8098, based on `jellyfin/jellyfin:latest`, reporting Jellyfin 12.1.0. The installed plugin/Web build was unchanged during this test.

The original fixture [Visual Features.ttml](fixtures/Visual%20Features.ttml) contains Japanese lyrics, keyed English translations, keyed Japanese romanization, nested background vocals, alternating singers `v1` and `v2`, overlapping singer lines, and one line assigned to both singers. `scripts/create-test-media.py` creates its 32-second synthetic tone WAV and sidecar. This checks visual synchronization against an actual media clock; the audio contains no real sung vocals.

## Method

Scanned the fixture into Jellyfin, fetched its authenticated rich lyric endpoint, started the track through Jellyfin's Play button, and opened the replacement lyric view. Played forward from zero without seeking between feature samples. Paused at each sample to save a stable screenshot and inspect renderer state. Repeated at 390×844 CSS pixels after the desktop run at 1280×800. The mobile run uses the same desktop browser with a narrow viewport, not a physical mobile device.

## Results

| Feature | Observed result | Verdict |
| --- | --- | --- |
| Translation | API retains `Translation`, language `en`, keys and both translated texts. At 3.448 seconds the original Japanese lyric highlights, but “A blue sky” is absent. Also absent in the narrow viewport. | Data preserved; visual display unsupported. |
| Romanization | API retains `Transliteration`, language `ja-Latn`, keys and both romanized texts. At 8.488 seconds the original lyric highlights, but “Hikari ga hikaru” is absent. Also absent in the narrow viewport. | Data preserved; visual display unsupported. |
| Background vocals | At 14.320 seconds “Lead voice” and “(echo” are independently active, while “answer)” remains upcoming until 15 seconds. Background vocals occupy a smaller separate row. Nested `x-bg` flags and their singer IDs survive parsing. Same rendering at narrow width. | Pass for independent background timing and visual distinction. The background singer does not receive separate singer alignment from its parent. |
| Separate singers / overlapping duet | At 20.416 seconds both overlapping lines are active, with independent word states. Singer `v1` aligns left/start and `v2` right, on desktop and narrow width. Narrow viewport has no horizontal overflow. | Pass for this two-singer alternating/overlapping fixture. |
| Shared line with two singers | API retains `[v1, v2]`. At 25.456 seconds the renderer receives only `v1` and uses left/start alignment. | Partial support: shared multi-agent identity is lost at Web conversion. No named singer controls are present. |

Screenshots and sanitized DOM samples are saved in the local ignored directory `artifacts/visual-tests/`: `translation.png`, `romanization.png`, `background.png`, `duet.png`, `unison.png`, four `mobile*.png` samples, and `observations.json`. A browser recording was started, but the recording-stop tool failed twice and returned no recording file; screenshots are the retained visual evidence.

The run confirms that all four features were explicitly exercised, not that all four have working display support. Translation/romanization UI, general singer identity mapping, and shared-singer presentation remain incomplete. These results do not establish arbitrary TTML dialect coverage or physical-device compatibility.

## Reproduce

1. Run `python3 scripts/create-test-media.py` with the isolated Compose instance running.
2. Run `python3 scripts/smoke-test.py` to refresh its library.
3. Play **Visual Features**, open Lyrics, and inspect 2–6 seconds (translation), 7–11 seconds (romanization), 12–17 seconds (background), 19–22 seconds (overlapping singers), and 24–28 seconds (shared singer line).
