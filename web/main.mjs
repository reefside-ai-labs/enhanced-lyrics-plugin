import { BraccatoLyricsElement } from '@braccato/core/element';
import { injectTranslation, injectRomanization } from '@braccato/core';
import '@braccato/core/styles/variables.css';
import '@braccato/core/styles/lyrics.css';
import '@braccato/core/styles/instrumental.css';
import { Kawarp } from '@kawarp/core';
import { installPlaybackBridge } from './bridge.mjs';
import { toBraccato, chooseTranslationLanguage } from './convert.mjs';
import './style.css';

const scriptUrl = document.currentScript?.src;
const css = document.createElement('link');
css.rel = 'stylesheet';
css.href = scriptUrl?.replace(/\.js(?:\?.*)?$/, '.css') || '../EnhancedLyrics/Assets/enhanced-lyrics.css';
document.head.append(css);
let integrationBroken = false;
css.addEventListener('error', () => { integrationBroken = true; destroy(); log('Stylesheet failed to load; restored the built-in view.'); });
let playerManager;
let mounted;
let status = { enabled: false, animatedBackground: false };
let lastStatus = 0;
let checkingStatus = false;
let lastInspection = 0;
let requestNumber = 0;
let pending;
let wantedKey;
let failedKey;
const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)');
const api = () => window.ApiClient;
window.EnhancedLyrics = { bridgeReady: false, active: false };
installPlaybackBridge(window, manager => {
    playerManager = manager;
    window.EnhancedLyrics.bridgeReady = true;
});

function log(message, error) { console.warn('[Enhanced Lyrics]', message, error || ''); }
function destroy() {
    requestNumber++;
    pending?.abort();
    pending = null;
    wantedKey = null;
    if (!mounted) return;
    mounted.resize.disconnect();
    mounted.kawarp?.dispose();
    mounted.native.hidden = mounted.wasHidden;
    mounted.native.classList.remove('el-native-hidden');
    mounted.native.parentElement?.classList.remove('el-host');
    mounted.root.remove();
    mounted = null;
    window.EnhancedLyrics.active = false;
}
async function updateStatus(now) {
    if (checkingStatus || !api() || now - lastStatus < 5000) return;
    lastStatus = now;
    checkingStatus = true;
    try {
        const response = await fetch(api().getUrl('EnhancedLyrics/Status'), { cache: 'no-store' });
        if (!response.ok) throw new Error(`Plugin status: ${response.status}`);
        const next = await response.json();
        const backgroundChanged = status.animatedBackground !== next.animatedBackground;
        status = next;
        if (!status.enabled || backgroundChanged) { failedKey = null; destroy(); }
    } catch (error) {
        status = { enabled: false, animatedBackground: false };
        destroy();
        log('Plugin is unavailable; the built-in view remains available.', error);
    } finally { checkingStatus = false; }
}
function button(text, action) {
    const element = document.createElement('button');
    element.type = 'button';
    element.textContent = text;
    element.addEventListener('click', action);
    return element;
}
function preference(name, fallback) {
    try { return localStorage.getItem(`enhanced-lyrics:${name}`) ?? fallback; }
    catch { return fallback; }
}
function savePreference(name, value) {
    try { localStorage.setItem(`enhanced-lyrics:${name}`, value); } catch { /* Display works without storage. */ }
}
function mount(native, data, item, manager, key) {
    const converted = toBraccato(data, (item.RunTimeTicks || 0) / 10000);
    const languages = [...new Set(converted.flatMap(line => Object.keys(line.translations || {})))];
    const hasRomanization = converted.some(line => line.romanization);
    let romanizationEnabled = preference('romanization', 'true') === 'true';
    const preferredTranslation = preference('translation', navigator.language);
    let translationLanguage = preferredTranslation === 'off' ? 'off' : chooseTranslationLanguage(languages, preferredTranslation);
    const root = document.createElement('section');
    root.className = 'enhanced-lyrics';
    root.setAttribute('aria-label', 'Lyrics');
    const canvas = document.createElement('canvas');
    canvas.className = 'el-background';
    canvas.setAttribute('aria-hidden', 'true');
    const toolbar = document.createElement('div');
    toolbar.className = 'el-toolbar';
    const title = document.createElement('span');
    title.textContent = item.Name || 'Lyrics';
    title.className = 'el-title';
    const controls = document.createElement('div');
    const expand = button('Expand', () => {
        const expanded = root.classList.toggle('el-expanded');
        expand.textContent = expanded ? 'Collapse' : 'Expand';
        expand.setAttribute('aria-expanded', String(expanded));
        lyrics.renderer?.relayout();
    });
    expand.setAttribute('aria-expanded', 'false');
    const play = button(manager.paused() ? 'Play' : 'Pause', () => manager.playPause());
    const resume = button('Follow lyrics', () => lyrics.renderer?.resumeAutoscroll());
    resume.hidden = true;
    controls.append(resume, play, expand);
    toolbar.append(title, controls);
    const variantControls = document.createElement('div');
    variantControls.className = 'el-variant-controls';
    const rebuild = () => { lyrics.lyrics = converted; };
    if (languages.length) {
        const label = document.createElement('label');
        label.textContent = 'Translation ';
        const select = document.createElement('select');
        select.setAttribute('aria-label', 'Translation language');
        let names;
        try { names = new Intl.DisplayNames([navigator.language], { type: 'language' }); } catch { /* Use language tags. */ }
        for (const language of ['off', ...languages]) {
            const option = document.createElement('option');
            option.value = language;
            try { option.textContent = language === 'off' ? 'Off' : names?.of(language) || language; }
            catch { option.textContent = language; }
            select.append(option);
        }
        select.value = translationLanguage;
        select.addEventListener('change', () => {
            translationLanguage = select.value;
            savePreference('translation', translationLanguage);
            rebuild();
        });
        label.append(select);
        variantControls.append(label);
    }
    if (hasRomanization) {
        const romanize = button('Romanization', () => {
            romanizationEnabled = !romanizationEnabled;
            romanize.setAttribute('aria-pressed', String(romanizationEnabled));
            savePreference('romanization', String(romanizationEnabled));
            rebuild();
        });
        romanize.setAttribute('aria-pressed', String(romanizationEnabled));
        variantControls.append(romanize);
    }
    // Jellyfin patches document.createElement for its own legacy custom elements.
    const lyrics = new BraccatoLyricsElement();
    lyrics.className = 'el-words';
    lyrics.addEventListener('braccato:error', event => {
        failedKey = key;
        destroy();
        log('Renderer failed; restored the built-in view.', event.detail?.error);
    });
    lyrics.addEventListener('braccato:lyrics-loaded', () => {
        const renderer = lyrics.renderer;
        if (!renderer) return;
        for (const [index, line] of renderer.lines.entries()) {
            const source = converted[index];
            if (!source || source.isInstrumental) continue;
            if (romanizationEnabled && source.romanization) {
                injectRomanization(document, line.lyricElement, line, source.romanization, source.timedRomanization);
            }
            const translation = source.translations?.[translationLanguage];
            if (translation) injectTranslation(document, line.lyricElement, translation, translationLanguage);
        }
        renderer.relayout();
        if (!data.lines.some(line => line.start != null)) return;
        const rendered = [...lyrics.querySelectorAll('.blyrics--line')];
        for (const line of rendered) {
            if (line.classList.contains('blyrics--instrumental')) continue;
            line.tabIndex = 0;
            line.setAttribute('role', 'button');
            // The renderer has two text layers for highlighting; expose one accessible label.
            const source = converted[Number(line.dataset.lineNumber)];
            if (source) line.setAttribute('aria-label', [source.words,
                romanizationEnabled ? source.romanization : null,
                source.translations?.[translationLanguage]].filter(Boolean).join('. '));
            line.addEventListener('keydown', event => {
                if (event.key !== 'Enter' && event.key !== ' ') return;
                event.preventDefault();
                manager.seek(Math.round(Number(line.dataset.time) * 10000000));
            });
        }
    });
    lyrics.host = {
        seek: seconds => manager.seek(Math.round(seconds * 10000000)),
        isViewVisible: () => root.isConnected && !document.hidden,
        getScrollElement: () => lyrics,
        setResumeAffordanceVisible: visible => { resume.hidden = !visible; },
        log: (...args) => console.debug('[Enhanced Lyrics]', ...args)
    };
    lyrics.lyricsOptions = { language: data.language, loaderVisible: false, noLyrics: false };
    lyrics.lyrics = converted;
    root.append(canvas, toolbar);
    if (variantControls.childElementCount) root.append(variantControls);
    root.append(lyrics);
    native.after(root);
    const wasHidden = native.hidden;
    native.hidden = true;
    native.classList.add('el-native-hidden');
    native.parentElement.classList.add('el-host');
    let kawarp;
    const imageId = item.AlbumPrimaryImageTag ? item.AlbumId : item.ImageTags?.Primary ? item.Id : null;
    if (imageId) {
        const url = api().getImageUrl(imageId, { type: 'Primary', maxWidth: 512 });
        root.style.setProperty('--el-art', `url(${JSON.stringify(url)})`);
        if (status.animatedBackground && !reducedMotion.matches) {
            try {
                kawarp = new Kawarp(canvas, { blurPasses: 8, animationSpeed: 0.6 });
                kawarp.loadImage(url).then(() => { if (root.isConnected && !document.hidden) kawarp.start(); })
                    .catch(error => { log('Using a static background.', error); kawarp?.dispose(); kawarp = null; });
            } catch (error) { log('WebGL is unavailable; using a static background.', error); }
        }
    }
    const resize = new ResizeObserver(() => {
        lyrics.renderer?.relayout();
        // Kawarp reads canvas dimensions, rather than its CSS box. Bound the blurred
        // background's resolution so large/high-DPI screens do not allocate huge FBOs.
        const scale = Math.min(1, 1200 / Math.max(1, root.clientWidth, root.clientHeight));
        const width = Math.max(1, Math.round(root.clientWidth * scale));
        const height = Math.max(1, Math.round(root.clientHeight * scale));
        if (canvas.width !== width) canvas.width = width;
        if (canvas.height !== height) canvas.height = height;
        kawarp?.resize();
    });
    resize.observe(root);
    mounted = { root, native, wasHidden, lyrics, play, key, resize, get kawarp() { return kawarp; } };
    window.EnhancedLyrics.active = true;
}
async function load(native, item, manager, key) {
    destroy();
    wantedKey = key;
    const number = ++requestNumber;
    const controller = pending = new AbortController();
    try {
        const user = await api().getCurrentUser();
        const language = user?.Configuration?.AudioLanguagePreference || navigator.language;
        const response = await fetch(api().getUrl(`EnhancedLyrics/Audio/${item.Id}`, { language }), {
            headers: { Authorization: `MediaBrowser Token="${api().accessToken()}"` }, signal: controller.signal
        });
        if (!response.ok) throw new Error(`Lyrics: ${response.status}`);
        const data = await response.json();
        if (!data.lines?.length) throw new Error('No lyric lines');
        if (number !== requestNumber || !native.isConnected || !status.enabled) return;
        mount(native, data, item, manager, key);
        failedKey = null;
    } catch (error) {
        if (number !== requestNumber || controller.signal.aborted) return;
        failedKey = key;
        destroy();
        log('Could not enhance this track; using the built-in view.', error);
    }
}
function inspect(now) {
    updateStatus(now);
    if (integrationBroken || !status.enabled || !playerManager || !api()) return;
    const native = [...document.querySelectorAll('.lyricsContainer')].find(element => {
        const page = element.closest('[data-role="page"]') || element.parentElement;
        return page?.getClientRects().length && getComputedStyle(page).visibility !== 'hidden' &&
            (element === mounted?.native || element.getClientRects().length > 0);
    });
    const player = playerManager.getCurrentPlayer();
    if (!player) { destroy(); failedKey = null; return; }
    const item = playerManager.getPlayerState(player)?.NowPlayingItem;
    if (!native || !item?.Id) { destroy(); failedKey = null; return; }
    const key = `${api().serverId()}:${item.Id}`;
    if (mounted && (mounted.native !== native || mounted.key !== key)) destroy();
    if (!mounted && wantedKey !== key && failedKey !== key) load(native, item, playerManager, key);
}
function frame(now) {
    try {
        if (now - lastInspection > 250) { lastInspection = now; inspect(now); }
        if (mounted && playerManager) {
            mounted.lyrics.currentTime = (playerManager.currentTime() || 0) / 1000;
            mounted.lyrics.playing = !playerManager.paused();
            mounted.play.textContent = playerManager.paused() ? 'Play' : 'Pause';
        }
    } catch (error) { destroy(); log('Web integration failed; restored the built-in view.', error); }
    requestAnimationFrame(frame);
}
reducedMotion.addEventListener('change', () => { failedKey = null; destroy(); });
document.addEventListener('visibilitychange', () => {
    if (document.hidden) mounted?.kawarp?.stop();
    else if (!reducedMotion.matches && status.animatedBackground) mounted?.kawarp?.start();
});
setTimeout(() => { if (!playerManager) log('Playback bridge was not found. This loader must run before Jellyfin’s scripts; the built-in view remains available.'); }, 20000);
requestAnimationFrame(frame);
