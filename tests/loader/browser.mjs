import { chromium } from 'playwright';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

const [accountPath, output] = process.argv.slice(2);
const account = JSON.parse(await readFile(accountPath, 'utf8'));
await mkdir(output, { recursive: true });
const report = { network: [], checks: [] };
const browser = await chromium.launch({ headless: true });
const context = await browser.newContext({ serviceWorkers: 'block', viewport: { width: 1200, height: 800 } });
const page = await context.newPage();
page.setDefaultTimeout(30000);
const cdp = await context.newCDPSession(page);
const assetResponses = [];
cdp.on('Network.responseReceived', event => {
    if (event.response.url.includes('/EnhancedLyrics/Assets/')) assetResponses.push(event);
});
const errors = [];
page.on('pageerror', error => errors.push(error.message));
try {
    assert.equal((await context.cookies()).length, 0);
    await cdp.send('Network.enable');
    await cdp.send('Network.clearBrowserCache');
    await cdp.send('Network.setCacheDisabled', { cacheDisabled: true });
    await cdp.send('Network.setBypassServiceWorker', { bypass: true });
    await cdp.send('Storage.clearDataForOrigin', { origin: new URL(account.baseUrl).origin, storageTypes: 'all' });
    report.checks.push('new nonpersistent context; cookies/storage/cache empty; cache and service workers bypassed');
    const documentUrl = account.baseUrl + '/web/index.html?cold=' + Date.now();
    await page.goto(documentUrl);
    await page.waitForFunction(() => window.EnhancedLyrics?.bridgeReady === true);
    assert.equal(await page.locator('script[src*="EnhancedLyrics/Assets/enhanced-lyrics.js"]').count(), 1);
    await page.locator('#txtManualName').fill(account.Username);
    await page.locator('#txtManualPassword').fill(account.Pw);
    await page.locator('button:has-text("Sign In")').click();
    await page.waitForFunction(() => !!window.ApiClient?.getCurrentUserId());
    // Login schedules a home redirect after authentication; let it finish before navigating.
    await page.waitForURL(url => url.hash.includes('/home'));
    await page.locator('.homePage:visible').waitFor();
    await page.goto(documentUrl + '#/details?id=' + account.itemId + '&serverId=' + account.serverId);
    await page.locator('.detailButton.btnPlay:visible').click();
    await page.waitForFunction(() => document.querySelector('audio')?.paused === false && document.querySelector('audio').currentTime > 0);
    await page.evaluate(() => { location.hash = '/lyrics'; });
    await page.waitForFunction(() => window.EnhancedLyrics?.active && !!document.querySelector('braccato-lyrics')?.renderer);
    await page.evaluate(() => { document.querySelector('audio').currentTime = 2.25; });
    await page.waitForFunction(() => document.querySelectorAll('braccato-lyrics [data-word-state="active"]').length > 0);
    const profile = await readFile(fileURLToPath(new URL('../../scripts/profile-highlighting.js', import.meta.url)), 'utf8');
    report.profile = await page.evaluate(profile.trim());
    assert.equal(report.profile.verdict, 'pass', 'Actual lyric clock did not advance on rendered frames');
    assert.equal(await page.locator('braccato-lyrics').count(), 1);
    const variants = await page.locator('.el-variant-controls select option').evaluateAll(options => options.map(o => o.value));
    assert.ok(variants.includes('en') && variants.includes('fr'));
    assert.equal(await page.locator('.el-variant-controls button').getAttribute('aria-pressed'), 'true');
    report.checks.push('normal navigation initializes bridge, renders synthetic TTML, highlights words, exposes translations/romanization');
    await page.locator('.el-toolbar button:has-text("Pause")').click();
    await page.waitForFunction(() => document.querySelector('audio').paused && !document.querySelector('braccato-lyrics').playing);
    assert.equal(await page.evaluate(() => document.querySelector('audio').error?.code || 0), 0);
    for (const filename of ['enhanced-lyrics.js', 'enhanced-lyrics.css']) {
        const event = assetResponses.find(e => new URL(e.response.url).pathname.endsWith('/' + filename));
        assert.ok(event, 'Asset must be requested by the real page: ' + filename);
        assert.equal(event.response.status, 200);
        assert.ok(!event.response.fromDiskCache && !event.response.fromServiceWorker, 'Asset came from cache/service worker');
        const body = await cdp.send('Network.getResponseBody', { requestId: event.requestId });
        const bytes = Buffer.from(body.body, body.base64Encoded ? 'base64' : 'utf8');
        assert.equal(createHash('sha256').update(bytes).digest('hex'), account.assets[filename]);
        report.network.push({ path: new URL(event.response.url).pathname, status: event.response.status,
            fromDiskCache: !!event.response.fromDiskCache, fromServiceWorker: !!event.response.fromServiceWorker,
            sha256: account.assets[filename] });
    }
    report.checks.push('loader JS/CSS received from server with expected SHA256; no browser cache/SW response');
    report.result = 'PASS';
    console.log('PASS: cache-free browser bridge, actual network assets, timed lyric playback');
} catch (error) {
    report.result = 'FAIL';
    report.failure = error.message;
    process.exitCode = 1;
    console.error(error.message);
} finally {
    await page.screenshot({ path: output + '/page.png', fullPage: true }).catch(() => {});
    report.errors = errors;
    const scrub = value => JSON.stringify(value, null, 2).replaceAll(account.Pw, '<REDACTED>').replaceAll(account.AccessToken, '<REDACTED>')
        .replace(/((?:ApiKey|api_key|Token)=)[^&\s\"]+/gi, '$1<REDACTED>');
    await writeFile(output + '/browser.json', scrub(report));
    await context.close();
    await browser.close();
}
