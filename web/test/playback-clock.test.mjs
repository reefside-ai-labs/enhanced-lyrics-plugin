import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readPlaybackClock } from '../playback-clock.mjs';

function fixture(offset = 0) {
    const media = { tagName: 'AUDIO', isConnected: true, readyState: 4, currentTime: 2, paused: false, ended: false };
    const player = { isLocalPlayer: true, _mediaElement: media, cachedTime: 2000, currentTime() { return this.cachedTime; } };
    const manager = {
        getCurrentPlayer: () => player,
        currentTime: () => player.cachedTime + offset * 1000,
        paused: () => media.paused
    };
    return { media, player, manager };
}

test('word highlighting advances each frame while Jellyfin timeupdate remains at four Hz', () => {
    const { media, player, manager } = fixture();
    let previous = readPlaybackClock(manager).currentTime;
    for (let frame = 1; frame <= 60; frame++) {
        media.currentTime = 2 + frame / 60;
        if (frame % 15 === 0) player.cachedTime = media.currentTime * 1000;
        const clock = readPlaybackClock(manager);
        assert.ok(clock.currentTime > previous, `highlight froze at frame ${frame}`);
        assert.ok(Math.abs(clock.currentTime - media.currentTime) < 0.001);
        previous = clock.currentTime;
    }
});

test('native clock preserves the Jellyfin transcoding offset', () => {
    const { media, manager } = fixture(120);
    media.currentTime = 2.125;
    assert.equal(readPlaybackClock(manager).currentTime, 122.125);
});

test('pause, buffering, playback speed and seeks follow the media clock without extrapolation', () => {
    const { media, manager } = fixture();
    for (const [time, paused] of [[2.5, false], [2.5, false], [2.5, true], [19, true], [0.25, false], [0.275, false]]) {
        media.currentTime = time;
        media.paused = paused;
        assert.deepEqual(readPlaybackClock(manager), { currentTime: time, playing: !paused });
    }
});

test('remote players use their reported clock even when a local media element exists', () => {
    const { media, player, manager } = fixture();
    player.isLocalPlayer = false;
    media.currentTime = 10;
    assert.deepEqual(readPlaybackClock(manager), { currentTime: 2, playing: true });
});

test('unavailable, detached or uninitialized native players retain the manager clock', () => {
    for (const invalid of [null, { isConnected: false }, { readyState: 0 }, { currentTime: NaN }, { tagName: 'DIV' }]) {
        const { player, manager } = fixture();
        player._mediaElement = invalid === null ? null : { ...player._mediaElement, ...invalid };
        assert.deepEqual(readPlaybackClock(manager), { currentTime: 2, playing: true });
    }
});

test('unknown player clocks cannot corrupt timing', () => {
    const { player, manager } = fixture();
    player.currentTime = () => undefined;
    manager.currentTime = () => 1234;
    assert.equal(readPlaybackClock(manager).currentTime, 1.234);
});

test('local video and ended playback use the native clock and state', () => {
    const { media, manager } = fixture();
    media.tagName = 'VIDEO';
    media.currentTime = 32;
    media.ended = true;
    assert.deepEqual(readPlaybackClock(manager), { currentTime: 32, playing: false });
});
