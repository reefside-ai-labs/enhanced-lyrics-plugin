import { parseTTMLContent } from '@braccato/parsers';

// The server resolves the original lyric timeline. Braccato reads the authored
// variant metadata; join by explicit key, never by translation text or row order.
function variantsByKey(document) {
    const variants = new Map();
    if (document.format !== 'ttml' || !document.sourceXml) return variants;
    try {
        for (const line of parseTTMLContent(document.sourceXml).lyrics) {
            if (!line.isInstrumental && line.key && (line.translation || line.romanization)) {
                if (!variants.has(line.key)) variants.set(line.key, line);
            }
        }
    } catch (error) {
        console.warn('[Enhanced Lyrics] Could not read variant metadata; displaying original lyrics.', error);
    }
    return variants;
}

export function chooseTranslationLanguage(languages, preferred) {
    const normalize = value => value?.replaceAll('_', '-').toLowerCase();
    const preference = normalize(preferred);
    return languages.find(lang => normalize(lang) === preference) ||
        languages.find(lang => normalize(lang)?.split('-')[0] === preference?.split('-')[0]) || languages[0];
}

export function toBraccato(document, durationMs) {
    const lines = document.lines;
    const result = [];
    let previousEnd = 0;
    const variants = variantsByKey(document);
    for (let index = 0; index < lines.length; index++) {
        const line = lines[index];
        const start = line.start == null ? 0 : Math.max(0, line.start);
        const next = lines.slice(index + 1).find(l => l.start != null && l.start > start)?.start;
        const end = line.end ?? next ?? (durationMs > start ? durationMs : start + 5000);
        if (line.start != null && start - previousEnd >= 5000) {
            result.push({ words: '', startTimeMs: previousEnd, durationMs: start - previousEnd, isInstrumental: true });
        }
        const words = line.words || [];
        const variant = line.key ? variants.get(line.key) : undefined;
        // A chorus may reuse its key. Authored variant times belong to the first
        // occurrence, so move them with the server's resolved line timeline.
        const timedRomanization = variant?.timedRomanization?.map(part => ({
            ...part, startTimeMs: part.startTimeMs + start - variant.startTimeMs
        }));
        const usableRomanizationTiming = timedRomanization?.length && timedRomanization.every(part =>
            Number.isFinite(part.startTimeMs) && Number.isFinite(part.durationMs) && part.durationMs >= 0 &&
            part.startTimeMs >= start && part.startTimeMs + part.durationMs <= end);
        result.push({
            words: line.text,
            startTimeMs: start,
            durationMs: Math.max(0, end - start),
            key: line.key,
            translations: variant?.translations,
            translation: variant?.translation,
            romanization: variant?.romanization,
            // Invalid/out-of-range variant cues still display as plain romanization.
            timedRomanization: usableRomanizationTiming ? timedRomanization : undefined,
            // Preserve agent metadata but defer duet-specific display controls.
            agent: line.singerIds?.[0],
            parts: words.length ? words.map(word => ({
                words: word.text,
                startTimeMs: Math.max(0, word.start),
                durationMs: Math.max(0, (word.end ?? end) - Math.max(0, word.start)),
                isBackground: word.background
            })) : undefined
        });
        previousEnd = Math.max(previousEnd, end);
    }
    if (lines.some(l => l.start != null) && durationMs - previousEnd >= 5000) {
        result.push({ words: '', startTimeMs: previousEnd, durationMs: durationMs - previousEnd, isInstrumental: true });
    }
    return result;
}
