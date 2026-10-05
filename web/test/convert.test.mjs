import { test } from 'node:test';
import assert from 'node:assert/strict';
import { toBraccato, chooseTranslationLanguage } from '../convert.mjs';
import { readFileSync } from 'node:fs';
test('converts explicit words and includes instrumental gaps', () => {
    const source = { lines: [
        { text: 'Hello world', start: 6000, end: 8000, words: [{ text: 'Hello ', start: 6000, end: 7000 }, { text: 'world', start: 7000, end: 8000 }] },
        { text: 'Again', start: 15000, end: 18000, words: [] }
    ] };
    const before = JSON.stringify(source);
    const output = toBraccato(source, 25000);
    assert.equal(output[0].isInstrumental, true);
    assert.equal(output[1].parts[0].durationMs, 1000);
    assert.equal(output[2].isInstrumental, true);
    assert.equal(output.at(-1).isInstrumental, true);
    assert.equal(JSON.stringify(source), before);
});
test('line-synced files do not receive fabricated word timings', () => {
    const lines = toBraccato({ lines: [{ text: 'A', start: 1000, words: [] }, { text: 'B', start: 3000, words: [] }] }, 5000);
    assert.equal(lines[0].durationMs, 2000);
    assert.equal(lines[0].parts, undefined);
});
test('plain text remains entirely unsynced', () => {
    const lines = toBraccato({ lines: [{ text: 'A', start: null }, { text: 'B', start: null }] }, 20000);
    assert.equal(lines.length, 2);
    assert.ok(lines.every(line => line.startTimeMs === 0 && !line.parts && !line.isInstrumental));
});
test('overlapping vocals do not become negative instrumental gaps', () => {
    const lines = toBraccato({ lines: [{ text: 'A', start: 0, end: 8000 }, { text: 'B', start: 3000, end: 5000 }] }, 10000);
    assert.equal(lines.length, 2);
});
const sourceXml = readFileSync(new URL('../../tests/fixtures/Visual Features.ttml', import.meta.url), 'utf8');
test('joins authored translations and timed/plain romanization to their explicit line keys', () => {
    const source = { format: 'ttml', sourceXml, lines: [
        { key: 'L2', text: '光が 光る', start: 7000, end: 11000 },
        { key: 'L1', text: '青い 空', start: 2000, end: 6000 },
        { key: 'unknown', text: 'Unmatched', start: 12000, end: 13000 }
    ] };
    const before = JSON.stringify(source);
    const output = toBraccato(source, 13000).filter(line => !line.isInstrumental);
    assert.equal(output[0].romanization, 'Hikari ga hikaru');
    assert.equal(output[0].timedRomanization, undefined);
    assert.deepEqual(output[1].translations, { en: 'A blue sky', fr: 'Un ciel bleu' });
    assert.equal(output[1].romanization, 'Aoi sora');
    assert.deepEqual(output[1].timedRomanization.map(p => [p.words, p.startTimeMs, p.durationMs]), [['Aoi ', 2000, 2000], ['sora', 4000, 2000]]);
    assert.equal(output[1].startTimeMs, 2000);
    assert.equal(output[2].translations, undefined);
    assert.equal(JSON.stringify(source), before);
});
test('a repeated key receives its translation at every occurrence', () => {
    const lines = toBraccato({ format: 'ttml', sourceXml, lines: [
        { key: 'L1', text: '青い 空', start: 2000, end: 6000 },
        { key: 'L1', text: '青い 空', start: 7000, end: 11000 }
    ] }, 11000);
    assert.ok(lines.every(line => line.translations.en === 'A blue sky'));
    assert.equal(lines[1].timedRomanization[0].startTimeMs, 7000);
    assert.equal(lines[1].timedRomanization[1].startTimeMs, 9000);
});
test('out-of-range romanization cues fall back to readable plain text', () => {
    const output = toBraccato({ format: 'ttml', sourceXml, lines: [
        { key: 'L1', text: '青い 空', start: 2000, end: 3000 }
    ] }, 3000);
    assert.equal(output[0].romanization, 'Aoi sora');
    assert.equal(output[0].timedRomanization, undefined);
});
test('unavailable or malformed variant metadata leaves original lines usable', () => {
    for (const xml of [undefined, '<broken>']) {
        const output = toBraccato({ format: 'ttml', sourceXml: xml, lines: [{ key: 'L1', text: 'Original', start: 2000, end: 6000 }] }, 6000);
        assert.equal(output[0].words, 'Original');
        assert.equal(output[0].translation, undefined);
    }
});
test('translation preference matches exact locale then base language with a stable fallback', () => {
    assert.equal(chooseTranslationLanguage(['en', 'fr-FR', 'fr-CA'], 'fr_CA'), 'fr-CA');
    assert.equal(chooseTranslationLanguage(['en', 'fr'], 'fr-CA'), 'fr');
    assert.equal(chooseTranslationLanguage(['en', 'fr'], 'ja'), 'en');
    assert.equal(chooseTranslationLanguage([], 'en'), undefined);
});
