# Cold-install loader acceptance

This test proves that the plugin installs its loader through the server, without a patched image, cached browser script, or browser-side bootstrap injection.

## Run

Requires Docker, .NET 10, Node 24, Python 3, and the catalog-package builder. From the repository root:

```sh
npm --prefix web ci
npm --prefix web run build
python3 -m venv .test-data/jprm-venv
.test-data/jprm-venv/bin/pip install jprm==1.1.0
mkdir -p artifacts/loader-candidate
.test-data/jprm-venv/bin/jprm plugin build . --output artifacts/loader-candidate --dotnet-framework net10.0
npm --prefix tests/loader ci
PLAYWRIGHT_SKIP_BROWSER_GC=1 npx --prefix tests/loader playwright install chromium
./scripts/test-loader-install.sh --recreate --package artifacts/loader-candidate/enhanced-lyrics_0.2.1.0.zip
./scripts/test-loader-install.sh --recreate --image jellyfin/jellyfin:12.1 --base-url /jf --package artifacts/loader-candidate/enhanced-lyrics_0.2.1.0.zip
```

Use the filename emitted by the package builder if the version changes. Linux hosts may need `playwright install --with-deps chromium`. Docker's 12.1 tag is `12.1`, not `12.1.0` ([official image tags](https://hub.docker.com/r/jellyfin/jellyfin/tags)). `--fresh` omits the recreation phase. `--server-only` is a diagnostic subset and reports `PASS_SERVER_ONLY`, never full acceptance.

## Assertions

1. Pull the stock image and record its resolved image ID. Start with unique empty config/cache directories, a read-only root filesystem, and original synthetic audio/TTML.
2. Verify both the stock disk index and initial HTTP index contain no loader. Install the actual ZIP through Jellyfin's package/catalog API using an isolated HTTP catalog, then restart.
3. Fetch `/web/` and explicit index with cache-busting query parameters. Require exactly one loader before Jellyfin boot scripts, matching installed version, embedded assets, unchanged disk index, matching HEAD content length, and full transformed responses for conditional/range/compression requests.
4. Launch a new nonpersistent Chromium context. Assert no cookies, clear HTTP cache and all origin storage, disable HTTP cache, block and bypass service workers. Navigate normally; no route interception, `addInitScript`, or DOM bootstrap injection is used.
5. Require the bridge, normal login and UI playback, one Braccato renderer, active timed words, translation/romanization controls, advancing animation-frame lyric clock, and successful pause without media error. Require actual JS/CSS network responses from the server, without disk cache/service workers, and compare their SHA256 with independent HTTP downloads.
6. Destroy the Jellyfin container and recreate it from the stock image, retaining only configuration/cache mounts. Repeat HTTP and playback assertions with another fresh browser.
7. Disable the plugin and require no injected loader; uninstall and restart, then require byte-identical stock HTML.

The script cleans up only its own uniquely named containers and network. It does not prune images or touch another instance. Evidence remains under ignored `.test-data/loader-tests/el-loader-*/`: `report.json`, redacted `server.log`, served HTML, browser summaries, and screenshots. The private test account/config also stay in that ignored directory; CI uploads only the explicit evidence files, never account JSON or configuration.

Build CI runs against 12.1 and latest with a base URL. Publishing reruns both against the actual package before uploading release assets or notifying the catalog. A failed assertion exits nonzero and stops publication.

## Regression reproduced

The published v0.2.0 catalog package installed and reported active in a clean stock instance, then failed with `LOADER_MISSING_OR_DUPLICATED`. Fresh server HTML had no loader, proving that clearing browser cache alone could not fix the missing server integration. The production fix registers an ASP.NET startup filter and transforms the outgoing index response without modifying Web files.


## Verified results (2026-10-05)

- Released v0.2.0: expected **FAIL**, `LOADER_MISSING_OR_DUPLICATED`, after a successful real catalog install. Evidence: `.test-data/loader-tests/el-loader-34a7c4a181/report.json`.
- Fixed candidate, stock latest (server 12.2.0), root URL: **PASS**, fresh/recreated browser playback, transport checks, unchanged disk, disable/uninstall. Evidence: `.test-data/loader-tests/el-loader-33840d6f16/report.json`.
- Fixed candidate, stock 12.1 (server 12.1.0), `/jf`: **PASS**, same complete assertions. Evidence: `.test-data/loader-tests/el-loader-91bfc3242d/report.json`.
- Final candidate, stock latest (server 12.2.0), `/jf`: **PASS**, same complete assertions. Evidence: `.test-data/loader-tests/el-loader-ce14176740/report.json`.

The successful browser samples measured about 60 fps with zero lyric-clock stalls. Playback screenshots show both authored translation and romanization rows. All 29 .NET tests, 19 Web tests, and two legacy Python integration tests passed. These runs tested the implementation subsequently packaged for v0.2.1; they did not change the personal server.
