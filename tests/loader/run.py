#!/usr/bin/env python3
"""Install a real plugin package into stock Docker; never patch server/client HTML."""
import argparse
import datetime
import hashlib
import json
import re
import secrets
import shutil
import socket
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GUID = '08c641eb-0fb9-47c7-9dc1-08615b8d220b'
LOADER = 'EnhancedLyrics/Assets/enhanced-lyrics.js'
AUTH = 'MediaBrowser Client="LoaderAcceptance", Device="Isolated test", DeviceId="loader-acceptance", Version="1.0"'


def command(*args):
    return subprocess.check_output(args, text=True, stderr=subprocess.STDOUT).strip()


def redact(text, credentials):
    for value in credentials.values():
        if isinstance(value, str) and len(value) > 10:
            text = text.replace(value, '<REDACTED>')
    return re.sub(r'(?i)((?:ApiKey|api_key|Token)=)[^&\s\"]+', r'\1<REDACTED>', text)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument('--fresh', action='store_true', help='Fresh catalog install, browser, transport, disable/uninstall')
    mode.add_argument('--recreate', action='store_true', help='Additionally destroy/recreate the stock container retaining config')
    parser.add_argument('--package', type=Path, required=True, help='Jellyfin-installable ZIP, DLLs/meta.json at root')
    parser.add_argument('--image', default='jellyfin/jellyfin:latest')
    parser.add_argument('--base-url', default='')
    parser.add_argument('--server-only', action='store_true', help='Diagnostic subset; does not claim browser acceptance')
    args = parser.parse_args()
    package = args.package.resolve()
    with zipfile.ZipFile(package) as archive:
        assert archive.testzip() is None, 'Corrupt ZIP'
        assert {'Jellyfin.Plugin.EnhancedLyrics.dll', 'TtmlLyricParser.dll', 'meta.json'} <= set(archive.namelist()), 'Use the catalog ZIP, not the manual bundle'
        meta = json.loads(archive.read('meta.json'))
    assert meta['guid'] == GUID
    prefix = '/' + args.base_url.strip('/') if args.base_url.strip('/') else ''
    run_id = uuid.uuid4().hex[:10]
    name = 'el-loader-' + run_id
    work = ROOT / '.test-data/loader-tests' / name
    work.mkdir(parents=True)
    catalog = work / 'catalog'
    catalog.mkdir()
    shutil.copy(package, catalog / 'plugin.zip')
    repository = 'http://catalog:8000/manifest.json'
    version = {**meta, 'sourceUrl': 'http://catalog:8000/plugin.zip', 'checksum': hashlib.md5(package.read_bytes()).hexdigest()}
    manifest = [{**meta, 'versions': [version]}]
    (catalog / 'manifest.json').write_text(json.dumps(manifest))
    for folder in ('config', 'cache', 'media'):
        (work / folder).mkdir()
    if prefix:
        config_dir = work / 'config/config'
        config_dir.mkdir()
        (config_dir / 'network.xml').write_text('<NetworkConfiguration><BaseUrl>' + prefix + '</BaseUrl></NetworkConfiguration>')
    # Original, deterministic media. Kept entirely inside this test's directory.
    import wave
    media = work / 'media'
    with wave.open(str(media / 'Loader Test.wav'), 'wb') as audio:
        audio.setparams((1, 2, 16000, 0, 'NONE', 'not compressed'))
        audio.writeframes(b'\0\0' * 16000 * 32)
    shutil.copy(ROOT / 'tests/fixtures/Visual Features.ttml', media / 'Loader Test.ttml')
    credentials = {'Username': 'loader-test', 'Pw': secrets.token_urlsafe(24)}
    report = {'image': args.image, 'packageVersion': meta['version'], 'baseUrl': prefix, 'checks': [], 'browserRequired': not args.server_only}
    resources = []
    token = None
    base = None
    failure = None

    def passed(label):
        report['checks'].append(label)
        print('PASS: ' + label, flush=True)

    def http(path, data=None, method=None, headers=None):
        request_headers = {'Authorization': AUTH + (', Token="' + token + '"' if token else '')}
        request_headers.update(headers or {})
        raw = json.dumps(data).encode() if data is not None else None
        if raw is not None:
            request_headers['Content-Type'] = 'application/json'
        request = urllib.request.Request(base + path, raw, request_headers, method=method or ('POST' if data is not None else 'GET'))
        with urllib.request.urlopen(request, timeout=30) as response:
            return response.status, dict(response.headers), response.read()

    def api(path, data=None, method=None):
        _, _, raw = http(path, data, method)
        return json.loads(raw) if raw else None

    def wait_for_server():
        for attempt in range(90):
            try:
                info = json.loads(http('/System/Info/Public', headers={'Authorization': AUTH})[2])
                report['serverVersion'] = info['Version']
                return info
            except (OSError, ValueError, KeyError):
                time.sleep(1)
        raise AssertionError('Stock Jellyfin did not become ready')

    def start(port=None):
        nonlocal base
        if port is None:
            with socket.socket() as reservation:
                reservation.bind(('127.0.0.1', 0))
                port = reservation.getsockname()[1]
        command('docker', 'run', '-d', '--name', name, '--label', 'enhanced-lyrics.loader-test=' + run_id,
                '--network', name, '--read-only', '--tmpfs', '/tmp', '--tmpfs', '/run',
                '-p', f'127.0.0.1:{port or 0}:8096',
                '-v', str(work / 'config') + ':/config', '-v', str(work / 'cache') + ':/cache',
                '-v', str(media) + ':/media:ro', args.image)
        resources.append(name)
        published = command('docker', 'port', name, '8096/tcp').split(':')[-1]
        base = 'http://127.0.0.1:' + published + prefix
        wait_for_server()
        return published

    def disk_index():
        return subprocess.check_output(['docker', 'exec', name, 'cat', '/jellyfin/jellyfin-web/index.html'])

    def server_assertions(label):
        status = api('/EnhancedLyrics/Status')
        assert status['enabled'] and status['version'] == meta['version'], 'Wrong/disabled installed plugin'
        passed(label + ': installed version active')
        for index_path in ('/web/', '/web/index.html?cold=' + run_id):
            code, headers, body = http(index_path, headers={'Cache-Control': 'no-cache'})
            (work / (label + '-index.html')).write_bytes(body)
            html = body.decode()
            assert code == 200 and html.count(LOADER) == 1, 'LOADER_MISSING_OR_DUPLICATED: fresh server HTML must contain exactly one loader'
            assert html.index(LOADER) < html.index('</script>'), 'Loader must precede Jellyfin boot scripts'
        passed(label + ': server serves exactly one early loader')
        assert disk_index() == original, 'Stock Web index was modified; a patched image cannot prove plugin integration'
        passed(label + ': read-only stock Web index unchanged')
        for filename in ('enhanced-lyrics.js', 'enhanced-lyrics.css'):
            code, _, body = http('/EnhancedLyrics/Assets/' + filename)
            assert code == 200 and len(body) > 1000, 'Embedded asset missing'
            report.setdefault('assets', {})[filename] = hashlib.sha256(body).hexdigest()
        # Transport semantics must not lose the injected loader to 304/ranges/compression.
        code, headers, body = http('/web/index.html', headers={'Accept-Encoding': 'gzip, br', 'If-None-Match': '"stale"', 'If-Modified-Since': 'Fri, 01 Jan 2100 00:00:00 GMT', 'Range': 'bytes=0-20'})
        assert code == 200 and LOADER.encode() in body and 'Content-Encoding' not in headers
        assert not any(h in headers for h in ('ETag', 'Last-Modified', 'Content-Range', 'Accept-Ranges'))
        _, head_headers, head_body = http('/web/index.html', method='HEAD')
        assert not head_body and int(head_headers['Content-Length']) == len(body), 'HEAD metadata differs from transformed GET'
        passed(label + ': HEAD, cache validators, range and compression requests')
        return status

    def browser_assertions(label):
        if args.server_only:
            return
        account_file = work / 'account.json'
        account_file.write_text(json.dumps({**credentials, 'baseUrl': base, 'serverId': info['Id'], 'itemId': item['Id'], 'assets': report['assets']}))
        account_file.chmod(0o600)
        result = subprocess.run(['node', str(ROOT / 'tests/loader/browser.mjs'), str(account_file), str(work / label)], text=True, capture_output=True, timeout=120)
        print(redact(result.stdout, credentials), end='', flush=True)
        if result.returncode:
            raise AssertionError('Cold browser failed: ' + redact(result.stderr, credentials))
        report.setdefault('browser', {})[label] = json.loads((work / label / 'browser.json').read_text())
        passed(label + ': empty browser context, cache disabled, network assets, bridge, playback')

    try:
        # Pull/resolve the stock image, without any plugin or Web integration build layers.
        command('docker', 'pull', args.image)
        report['imageId'] = command('docker', 'image', 'inspect', args.image, '--format', '{{.Id}}')
        command('docker', 'network', 'create', '--label', 'enhanced-lyrics.loader-test=' + run_id, name)
        resources.append('network')
        command('docker', 'run', '-d', '--name', name + '-catalog', '--label', 'enhanced-lyrics.loader-test=' + run_id,
                '--network', name, '--network-alias', 'catalog', '--read-only', '--tmpfs', '/tmp',
                '-v', str(catalog) + ':/catalog:ro', 'python:3.13-alpine', 'python', '-m', 'http.server', '8000', '--directory', '/catalog')
        resources.append(name + '-catalog')
        port = start()
        original = disk_index()
        assert LOADER.encode() not in original
        report['stockIndexSha256'] = hashlib.sha256(original).hexdigest()
        assert LOADER.encode() not in http('/web/index.html')[2]
        passed('stock baseline has no loader or patched files')
        api('/Startup/Configuration', {'ServerName': 'Loader acceptance', 'UICulture': 'en-US', 'MetadataCountryCode': 'US', 'PreferredMetadataLanguage': 'en'})
        api('/Startup/User')
        api('/Startup/User', {'Name': credentials['Username'], 'Password': credentials['Pw']})
        api('/Startup/Complete', method='POST')
        auth = api('/Users/AuthenticateByName', credentials)
        token = auth['AccessToken']
        credentials['AccessToken'] = token
        user_id = auth['User']['Id']
        info = api('/System/Info/Public')
        api('/Repositories', [{'Name': 'Isolated acceptance catalog', 'Url': repository, 'Enabled': True}])
        query = urllib.parse.urlencode({'assemblyGuid': GUID, 'version': meta['version'], 'repositoryUrl': repository})
        api('/Packages/Installed/Enhanced%20Lyrics?' + query, method='POST')
        passed('Jellyfin catalog installer accepted candidate package')
        command('docker', 'restart', name)
        wait_for_server()
        token = None
        token = api('/Users/AuthenticateByName', {k: credentials[k] for k in ('Username', 'Pw')})['AccessToken']
        credentials['AccessToken'] = token
        server_assertions('fresh')
        api('/Library/VirtualFolders?name=Loader%20Test&collectionType=music&refreshLibrary=true', {'LibraryOptions': {
            'PathInfos': [{'Path': '/media'}], 'EnableRealtimeMonitor': False, 'EnableInternetProviders': False,
            'TypeOptions': [{'Type': t, 'MetadataFetchers': [], 'ImageFetchers': []} for t in ('MusicArtist', 'MusicAlbum', 'Audio')]}})
        api('/Library/Refresh', method='POST')
        for attempt in range(60):
            items = api('/Items?' + urllib.parse.urlencode({'userId': user_id, 'recursive': 'true', 'includeItemTypes': 'Audio'}))['Items']
            if items:
                item = items[0]
                break
            time.sleep(1)
        else:
            raise AssertionError('Synthetic media was not scanned')
        browser_assertions('fresh')
        if args.recreate:
            command('docker', 'rm', '-f', name)
            resources.remove(name)
            start(port)
            token = None
            token = api('/Users/AuthenticateByName', {k: credentials[k] for k in ('Username', 'Pw')})['AccessToken']
            credentials['AccessToken'] = token
            server_assertions('recreated')
            browser_assertions('recreated')
        # Disable must stop injecting; uninstall/restart must restore the original index.
        config = api('/Plugins/' + GUID + '/Configuration')
        api('/Plugins/' + GUID + '/Configuration', {**config, 'Enabled': False})
        assert LOADER.encode() not in http('/web/index.html')[2]
        api('/Plugins/' + GUID + '/Configuration', config)
        passed('disable removes loader from subsequent server responses')
        api('/Plugins/' + GUID, method='DELETE')
        command('docker', 'restart', name)
        wait_for_server()
        assert http('/web/index.html')[2] == original
        passed('uninstall/restart restores stock HTML without file cleanup')
        report['result'] = 'PASS_SERVER_ONLY' if args.server_only else 'PASS'
    except (Exception, KeyboardInterrupt) as error:
        failure = redact(str(error), credentials)
        report.update(result='FAIL', failure=failure)
        print('FAIL: ' + failure, flush=True)
    finally:
        if name in resources:
            try:
                logs = command('docker', 'logs', name)
                (work / 'server.log').write_text(redact(logs, credentials))
            except subprocess.CalledProcessError:
                pass
        for resource in reversed(resources):
            try:
                if resource == 'network':
                    command('docker', 'network', 'rm', name)
                else:
                    command('docker', 'rm', '-f', resource)
            except subprocess.CalledProcessError:
                pass
        (work / 'report.json').write_text(json.dumps(report, indent=2))
        print('Evidence: ' + str(work / 'report.json'), flush=True)
    return 1 if failure else 0


if __name__ == '__main__':
    sys.exit(main())
