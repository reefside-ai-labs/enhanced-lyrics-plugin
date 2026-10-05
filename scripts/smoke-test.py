#!/usr/bin/env python3
"""Initialize and verify ONLY the isolated compose test server on localhost:8098."""
import json
import base64
import secrets
import time
import urllib.request
import urllib.error
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
BASE = 'http://127.0.0.1:8098'
state_path = ROOT / '.test-data/test-account.json'
token = None
AUTH = 'MediaBrowser Client="EnhancedLyricsTests", Device="Smoke tests", DeviceId="enhanced-lyrics-smoke", Version="1.0"'
def request(path, data=None, method=None, authenticated=True):
    headers = {'Authorization': AUTH}
    if authenticated and token:
        headers['Authorization'] = AUTH + ', Token="' + token + '"'
    if data is not None:
        headers['Content-Type'] = 'application/json'
    req = urllib.request.Request(BASE + path, json.dumps(data).encode() if data is not None else None,
        headers, method=method or ('POST' if data is not None else 'GET'))
    with urllib.request.urlopen(req, timeout=15) as response:
        raw = response.read()
        return json.loads(raw) if raw else None

for attempt in range(60):
    try:
        info = request('/System/Info/Public')
        if 'Version' in info:
            break
        time.sleep(1)
    except (OSError, ValueError):
        time.sleep(1)
else:
    raise RuntimeError('Test server did not start')
assert info['Version'].startswith('12.1.'), info['Version']
if not info['StartupWizardCompleted']:
    account = {'Username': 'lyrics-test', 'Pw': secrets.token_urlsafe(18)}
    state_path.write_text(json.dumps(account))
    state_path.chmod(0o600)
    request('/Startup/Configuration', {'ServerName': 'Enhanced Lyrics Test', 'UICulture': 'en-US', 'MetadataCountryCode': 'US', 'PreferredMetadataLanguage': 'en'})
    request('/Startup/User')
    request('/Startup/User', {'Name': account['Username'], 'Password': account['Pw']})
    request('/Startup/Complete', method='POST')
account = json.loads(state_path.read_text())
auth = request('/Users/AuthenticateByName', account)
token = auth['AccessToken']
user_id = auth['User']['Id']
folders = request('/Library/VirtualFolders')
if not any(f['Name'] == 'Enhanced Lyrics Test' for f in folders):
    request('/Library/VirtualFolders?name=Enhanced%20Lyrics%20Test&collectionType=music&refreshLibrary=true', {'LibraryOptions': {
        'PathInfos': [{'Path': '/media'}], 'EnableRealtimeMonitor': False, 'EnableInternetProviders': False,
        'TypeOptions': [{'Type': t, 'MetadataFetchers': [], 'ImageFetchers': []} for t in ('MusicArtist', 'MusicAlbum', 'Audio')]
    }})
request('/Library/Refresh', method='POST')
for attempt in range(45):
    items = request(f'/Items?userId={user_id}&recursive=true&includeItemTypes=Audio&fields=Path')['Items']
    items = [item for item in items if '/Synthetic Album/' in item.get('Path', '')]
    if len(items) >= 5:
        break
    time.sleep(1)
assert len(items) >= 5, 'Synthetic test media was not scanned'
# Give the synthetic track original album art for browser/WebGL verification.
art = ROOT / '.test-data/media/Test Artist/Synthetic Album/cover.png'
if art.exists():
    for item in items:
        if item['Name'] == 'Synthetic':
            req = urllib.request.Request(BASE + '/Items/' + item['Id'] + '/Images/Primary',
                base64.b64encode(art.read_bytes()), {'Authorization': AUTH + ', Token="' + token + '"', 'Content-Type': 'image/png'}, method='POST')
            with urllib.request.urlopen(req, timeout=15) as response:
                assert response.status == 204
assert request('/EnhancedLyrics/Status')['enabled'] is True
for item in items:
    if item['Name'] == 'No lyrics':
        continue
    rich = request('/EnhancedLyrics/Audio/' + item['Id'])
    legacy = request('/Audio/' + item['Id'] + '/Lyrics')
    assert rich['lines'] and legacy['Lyrics'], item['Name']
    if item['Name'] == 'Synthetic':
        assert rich['format'] == 'ttml'
        assert rich['lines'][0]['words'][0]['start'] == 2000
        assert legacy['Lyrics'][0]['Cues'][1]['Start'] == 30000000
    elif item['Name'] in ('Enhanced LRC', 'Fallback'):
        assert rich['format'] == 'elrc'
    elif item['Name'] == 'Visual Features':
        assert rich['format'] == 'ttml'
        variants = {(v['kind'], v['language']): v['xml'] for v in rich['variants']}
        assert 'A blue sky' in variants[('Translation', 'en')]
        assert 'Un ciel bleu' in variants[('Translation', 'fr')]
        assert 'Aoi ' in variants[('Transliteration', 'ja-Latn')]
        assert 'for="L1"' in variants[('Transliteration', 'ja-Latn')]
        assert rich['lines'][0]['start'] == 2000
    print(item['Name'] + ': rich and legacy lyric APIs passed')
try:
    request('/EnhancedLyrics/Audio/' + items[0]['Id'], authenticated=False)
    raise AssertionError('Unauthenticated lyrics should be rejected')
except urllib.error.HTTPError as error:
    assert error.code == 401, error.code
# Check enable/disable behavior, restoring configuration even if assertions fail.
plugin_id = '08c641eb-0fb9-47c7-9dc1-08615b8d220b'
config = request('/Plugins/' + plugin_id + '/Configuration')
try:
    request('/Plugins/' + plugin_id + '/Configuration', {**config, 'Enabled': False})
    assert request('/EnhancedLyrics/Status')['enabled'] is False
    try:
        request('/EnhancedLyrics/Audio/' + items[0]['Id'])
        raise AssertionError('Disabled plugin should return 404')
    except urllib.error.HTTPError as error:
        assert error.code == 404
finally:
    request('/Plugins/' + plugin_id + '/Configuration', config)
(ROOT / '.test-data/items.json').write_text(json.dumps(items))
print('Jellyfin ' + info['Version'] + ': authentication, fallback and disable checks passed')
