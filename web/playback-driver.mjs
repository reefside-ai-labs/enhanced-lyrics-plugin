// Drive the renderer with one complete snapshot. The element's individual time
// and play-state setters each render immediately, including intermediate state.
export function createPlaybackDriver(lyrics) {
    let previous;
    let seekPending = false;
    let media;
    const onSeeking = () => { seekPending = true; };
    return {
        seek() { seekPending = true; },
        dispose() { media?.removeEventListener('seeking', onSeeking); },
        update(clock, now, playbackRate = 1, source = null) {
            if (source !== media) {
                media?.removeEventListener('seeking', onSeeking);
                media = source;
                media?.addEventListener('seeking', onSeeking);
                seekPending = true;
            }
            const renderer = lyrics.renderer;
            if (!renderer?.container) return;
            const elapsed = previous?.playing ? (now - previous.now) / 1000 * previous.playbackRate : 0;
            const jumped = previous && Math.abs(clock.currentTime - previous.currentTime - elapsed) > 0.075;
            const changed = previous && (clock.playing !== previous.playing || playbackRate !== previous.playbackRate);
            if (!previous || seekPending || jumped || changed) {
                // Braccato exposes writable render records. Cancel only the song
                // animations; preserve DOM, decorations, layout and user scrolling.
                for (const line of renderer.lines) {
                    for (const part of [line, ...line.parts]) {
                        for (const animation of part.animations) animation.cancel();
                        part.animations.length = 0;
                    }
                    line.isAnimating = false;
                    line.accumulatedOffsetMs = 0;
                }
            }
            renderer.tick(clock.currentTime, { ...lyrics.tickOptions, isPlaying: clock.playing, playbackRate });
            lyrics.dataset.currentTime = String(clock.currentTime);
            lyrics.dataset.playing = String(clock.playing);
            previous = { ...clock, now, playbackRate };
            seekPending = false;
        }
    };
}
