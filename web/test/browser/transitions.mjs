import { BraccatoLyricsElement } from '@braccato/core/element';
import '@braccato/core/styles/variables.css';
import '@braccato/core/styles/lyrics.css';
import { createPlaybackDriver } from '../../playback-driver.mjs';

// Build with esbuild and run verifyPlaybackTransitions() in the T3 preview.
// original=true replays the old integration and must fail the same-line seek.
window.verifyPlaybackTransitions = (original = false) => {
    const lyrics = new BraccatoLyricsElement();
    lyrics.lyrics = [
        { words: 'Long word', startTimeMs: 1000, durationMs: 10000,
            parts: [{ words: 'Long word', startTimeMs: 1000, durationMs: 10000 }] },
        { words: 'Next', startTimeMs: 12000, durationMs: 5000 }
    ];
    document.body.replaceChildren(lyrics);
    const driver = createPlaybackDriver(lyrics);
    const snapshots = [];
    let offset;
    for (const [time, playing, now] of [[2, false, 0], [2.3, false, 16], [2.3, true, 32],
        [3, true, 48], [1.7, true, 64], [1.7, false, 80]]) {
        if (original) {
            if (lyrics.playing !== playing) lyrics.playing = playing;
            lyrics.currentTime = time;
        } else driver.update({ currentTime: time, playing }, now);
        // A running word's float animation exposes song time without the
        // highlight/fade clamping at the beginning or end of the effect.
        const animation = lyrics.renderer.lines[0].parts[0].animations.find(a => a.effect.getTiming().duration === 1750);
        if (!animation) throw new Error('Pinned Braccato fixture has changed: no word float animation');
        const actual = Number(animation.currentTime);
        offset ??= actual - (time - 1) * 1000;
        const expected = (time - 1) * 1000 + offset;
        snapshots.push({ time, playing, actual, expected, pass: Math.abs(actual - expected) < 1 });
    }
    driver.dispose();
    return { verdict: snapshots.every(s => s.pass) ? 'pass' : 'fail', snapshots };
};
