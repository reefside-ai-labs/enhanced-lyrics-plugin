# Lyric highlighting stutter

Diagnosis and local fix: 2026-10-05. Production NAS and published packages were unchanged.

## Cause and fix

Jellyfin's HTML player caches its playback position when the media `timeupdate` event fires. The playback manager uses that cached value. Passing it to Braccato every animation frame therefore repeats one time value for approximately 15 frames, then jumps about 265 ms. A 60 fps page can still display stuttering word highlighting. [Jellyfin HTML audio clock](https://github.com/jellyfin/jellyfin-web/blob/dd6d1d66ac1a020635818aaaa712458a72a60d05/src/plugins/htmlAudioPlayer/plugin.js#L427), [manager stream offset](https://github.com/jellyfin/jellyfin-web/blob/dd6d1d66ac1a020635818aaaa712458a72a60d05/src/components/playback/playbackmanager.js#L2272).

`web/playback-clock.mjs` reads the active local player's native media clock each frame. It preserves the manager's transcoding offset by subtracting the cached player time from the manager time. This follows actual pause, buffering, seeks and playback rate rather than predicting time. Native media access is guarded; remote players and unavailable native implementations keep the manager clock. The `_mediaElement` field is a version-specific Jellyfin Web seam and must be rechecked when upgrading supported Web versions.

The frame loop also avoids writing unchanged play state and button text. Braccato's time and play-state setters both tick its renderer, so continuously writing an unchanged play state performs redundant work. [Braccato element API/source](https://github.com/better-lyrics/braccato/tree/main/packages/core).

## Evidence

Local stock-image prototype: Jellyfin 12.2.0 at loopback port 8099, with the previous automatic-integration prototype and the rebuilt Web bundle. No server Web files were modified.

| Measurement | Original | Fixed |
| --- | ---: | ---: |
| Median frame interval | 16.7 ms | 16.7 ms |
| P95 frame interval | 17.7 ms | 17.6–17.7 ms |
| Frames sampled | 211 | 180–181 |
| Lyric clock stalled while audio advanced | 93.8% | 0% |
| Largest lyric-clock advance | 269 ms | 20–27 ms |
| Frames over 33 ms | 0 | 0 |

A minimized boundary probe recorded 60 clock writes in one second, with 55 repeated input values and a 267.6 ms jump. Replacing only the clock input with native audio time eliminated stalls, without changing the background or renderer. The rebuilt plugin passed three separate frame samples with zero clock stalls.

Pause had zero clock drift. Direct forward/backward seeks to 3.25, 19 and 1.5 seconds matched exactly after a rendered frame. A lyric-row click routed through Jellyfin's seek handler and left both audio and lyric clocks at 12 seconds. At 1.5x speed, playback advanced 1.5 seconds in one second and the sampled lyric clock differed from audio by 13.9 ms (less than one frame). Playback was left paused, with native looping disabled and speed restored to 1x.

T3's inactive preview sometimes delivered zero animation frames despite continuing audio. Such samples are inconclusive, not evidence of a plugin regression. Recording the visible preview restored continuous frames; repeated fixed-build samples then passed. Recordings are local evidence, not committed media: `/Users/michaelteuscher/.t3/userdata/attachments/37c56611-7270-4993-bdc5-336acc013613-dc45b605-605f-45e4-b365-3af00f248e4f-mp4.mp4`.

## Reproduce and verify

During visible local audio playback on the enhanced Lyrics page, evaluate the expression in `scripts/profile-highlighting.js`. It reports frame timing separately from lyric-clock stalls and marks insufficient frame delivery inconclusive. The original clock fails; the fixed clock passes.

`node --test web/test/playback-clock.test.mjs` deterministically simulates 60 animation frames with four cached updates per second. It failed on the original clock at frame 1, then passed with the fix. The tests also cover transcoding offsets, paused/buffering clocks, seeks, remote-player fallback, invalid media, video, and ended playback. Full Web tests and bundle build passed.

Native local playback was tested live on 12.2. The preserved transcoding offset and remote fallback were tested with fixtures; an actual transcoding session or remote client was not exercised. This work does not promise 60 fps on every GPU or device, and does not interpolate coarse remote-client clocks.

## Playback transition correction

A follow-up found a separate transition bug after the native-clock fix. Braccato's
individual `playing` and `currentTime` setters each render immediately. Changing
play state first therefore renders once using the previous time, then again with
the new time. Existing selected word animations can also survive a seek. While
paused they never receive drift correction: a seek from 2 to 2.3 seconds left the
word float animation at 1650 ms instead of 1950 ms. Running playback can eventually
correct old animations, which explains the temporary disturbance followed by
smooth movement.

`web/playback-driver.mjs` sends one complete snapshot directly to the element's
renderer. On pause/resume, seeks and rate changes it cancels song animations and
marks their render records for immediate setup at the new time. Steady frames
reuse the animations. It preserves the DOM, translations, layout and scroll state.
Native `seeking` events also catch small scrubs; lyric clicks explicitly mark a
seek, and a timeline discontinuity detects coarse clock/remote jumps. Media
listeners are removed on replacement and unmount. Playback rate is passed through.

The driver uses Braccato 1.16.3's exposed writable render records; this seam needs
rechecking on upgrades. The element's separate clock setters are no longer used:
`data-current-time` and `data-playing` expose the last snapshot for profiling and
acceptance checks. Ordinary animation frames do not reset animation state.

Validation: 24 Web tests passed and the production bundle built. A real browser
fixture using the installed Braccato renderer and native Web Animations compared
the previous integration against the driver. The original failed the same-line
paused seek by 300 ms; the driver passed all six snapshots (initial position,
paused seek, resume, forward seek, backward seek and pause) within 1 ms. This is an
isolated renderer reproduction, not a full authenticated Jellyfin playback test
or a measurement of perceived smoothness on every device. No production server
or published package was changed.

To repeat the browser check from the repository root:

```sh
mkdir -p .test-data/transitions
web/node_modules/.bin/esbuild web/test/browser/transitions.mjs --bundle --outfile=.test-data/transitions/bundle.js
printf '%s' '<link rel="stylesheet" href="bundle.css"><style>braccato-lyrics{display:block;height:600px}</style><script src="bundle.js"></script>' > .test-data/transitions/index.html
python3 -m http.server 8101 --directory .test-data/transitions
```

Open `http://localhost:8101` in the T3 preview and evaluate
`({original: verifyPlaybackTransitions(true), fixed: verifyPlaybackTransitions()})`.
Expected verdicts are `fail` and `pass`. The pinned word float animation is checked
at the requested song time rather than just asserting that a clock property
changed. `node --test web/test/playback-driver.test.mjs` separately verifies single
snapshot updates, seeks, stable playback at 1.5x and media-listener cleanup.
