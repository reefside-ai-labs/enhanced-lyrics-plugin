import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createPlaybackDriver } from '../playback-driver.mjs';

function fixture() {
    const ticks = [];
    let cancellations = 0;
    const line = { parts: [{ animations: [] }], animations: [], isAnimating: false, accumulatedOffsetMs: 0 };
    const renderer = {
        container: {}, lines: [line],
        tick(time, options) {
            ticks.push({ time, ...options });
            if (!line.isAnimating) {
                for (const part of [line, ...line.parts]) part.animations.push({ cancel() { cancellations++; } });
                line.isAnimating = true;
            }
        }
    };
    const lyrics = { renderer, tickOptions: { lyricOffset: 0.25 }, dataset: {} };
    const driver = createPlaybackDriver(lyrics);
    return { driver, ticks, lyrics, cancelled: () => cancellations };
}

test('pause and resume render exactly once with the new time and play state', () => {
    const { driver, ticks, cancelled } = fixture();
    driver.update({ currentTime: 2, playing: true }, 0);
    driver.update({ currentTime: 2.016, playing: false }, 16);
    driver.update({ currentTime: 2.016, playing: true }, 1000);
    assert.equal(ticks.length, 3);
    assert.deepEqual(ticks.map(t => [t.time, t.isPlaying]), [[2, true], [2.016, false], [2.016, true]]);
    assert.equal(cancelled(), 4);
    assert.ok(ticks.every(t => t.lyricOffset === 0.25));
});

test('forward and backward seeks reset animations even within the same paused line', () => {
    const { driver, cancelled } = fixture();
    driver.update({ currentTime: 2, playing: false }, 0);
    driver.update({ currentTime: 2.3, playing: false }, 16);
    driver.update({ currentTime: 1.7, playing: false }, 32);
    assert.equal(cancelled(), 4);
});

test('steady playback keeps native animations, including at 1.5x', () => {
    const { driver, cancelled, ticks } = fixture();
    for (let frame = 0; frame <= 60; frame++) {
        driver.update({ currentTime: 2 + frame / 40, playing: true }, frame * 1000 / 60, 1.5);
    }
    assert.equal(cancelled(), 0);
    assert.ok(ticks.every(t => t.playbackRate === 1.5));
});

test('native seeking detects tiny scrubs and releases listeners when the source changes or unmounts', () => {
    const { driver, cancelled } = fixture();
    const source = new EventTarget();
    const next = new EventTarget();
    driver.update({ currentTime: 2, playing: false }, 0, 1, source);
    source.dispatchEvent(new Event('seeking'));
    driver.update({ currentTime: 2.01, playing: false }, 16, 1, source);
    assert.equal(cancelled(), 2);
    driver.update({ currentTime: 2.01, playing: false }, 32, 1, next);
    assert.equal(cancelled(), 4);
    source.dispatchEvent(new Event('seeking'));
    driver.update({ currentTime: 2.01, playing: false }, 48, 1, next);
    assert.equal(cancelled(), 4);
    driver.dispose();
    next.dispatchEvent(new Event('seeking'));
    driver.update({ currentTime: 2.01, playing: false }, 64, 1, next);
    assert.equal(cancelled(), 4);
});

test('explicit lyric seeks and playback-rate changes synchronize immediately', () => {
    const { driver, cancelled } = fixture();
    driver.update({ currentTime: 2, playing: true }, 0);
    driver.seek();
    driver.update({ currentTime: 2.01, playing: true }, 16);
    driver.update({ currentTime: 2.034, playing: true }, 32, 1.5);
    assert.equal(cancelled(), 4);
});
