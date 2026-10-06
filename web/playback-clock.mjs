export function readPlaybackClock(manager) {
    const reportedTime = (manager.currentTime() || 0) / 1000;
    const fallback = { currentTime: reportedTime, playing: !manager.paused() };
    const player = manager.getCurrentPlayer();
    // Jellyfin 12.1/12.2's HTML players cache currentTime on timeupdate (~4 Hz).
    // Read their active media element each animation frame instead. This private
    // field is guarded so other player implementations retain the manager clock.
    const media = player?.isLocalPlayer === true ? player._mediaElement : null;
    if (!media?.isConnected || !['AUDIO', 'VIDEO'].includes(media.tagName) ||
        media.readyState < 1 || !Number.isFinite(media.currentTime) || typeof media.paused !== 'boolean') return fallback;
    const playerTime = typeof player.currentTime === 'function' ? player.currentTime() : undefined;
    if (!Number.isFinite(playerTime)) return fallback;
    // The manager adds the stream's transcoding offset to the player's time.
    // Keep that offset while replacing only the coarse underlying clock.
    const offset = reportedTime - playerTime / 1000;
    return { currentTime: media.currentTime + offset, playing: !media.paused && !media.ended };
}
