// Jellyfin 12.1 does not publish its playback manager as a window global.
// Observe the manager when its webpack module executes; never execute extra modules.
export function installPlaybackBridge(scope, onReady) {
    const chunks = scope.webpackChunk ||= [];
    const nativePush = Array.prototype.push;
    let delegate = chunks.push;
    let inside = false;
    let captured = false;
    const wrapped = new WeakSet();
    function prepare(chunk) {
        const modules = chunk?.[1];
        if (!modules || captured) return;
        for (const [id, factory] of Object.entries(modules)) {
            if (typeof factory !== 'function' || wrapped.has(factory)) continue;
            const source = Function.prototype.toString.call(factory);
            if (!source.includes('getCurrentPlayer') || !source.includes('playPause')) continue;
            const replacement = function (module, ...args) {
                const result = factory.call(this, module, ...args);
                for (const key of Object.keys(module.exports || {})) {
                    try {
                        const candidate = module.exports[key];
                        if (candidate && typeof candidate.getCurrentPlayer === 'function' &&
                            typeof candidate.getPlayerState === 'function' && typeof candidate.currentTime === 'function' &&
                            typeof candidate.seek === 'function' && typeof candidate.paused === 'function') {
                            if (!captured) { captured = true; onReady(candidate); }
                        }
                    } catch { /* Circular exports can still be uninitialized. */ }
                }
                return result;
            };
            wrapped.add(replacement);
            modules[id] = replacement;
        }
    }
    function push(...entries) {
        // Webpack's callback invokes the previously bound push after registering modules.
        if (inside) return nativePush.apply(chunks, entries);
        for (const entry of entries) prepare(entry);
        inside = true;
        try { return delegate.apply(this, entries); }
        finally { inside = false; }
    }
    for (const chunk of chunks) prepare(chunk);
    Object.defineProperty(chunks, 'push', {
        configurable: true,
        get: () => push,
        set: (next) => { if (next !== push) delegate = next; }
    });
}
