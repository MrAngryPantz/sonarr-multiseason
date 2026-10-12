# Sonarr Multi-Season

This fork lets Sonarr automatically download packs that contain several seasons. It is meant for filling gaps in a library while keeping the episodes you already have.

In **Settings → Profiles**, edit a quality profile and enable **Allow Multi-Season Packs**. This turns **Upgrades Allowed** off and prevents it from being turned back on while the option is enabled. Turning multi-season packs off leaves upgrades off.

A pack can be selected from RSS or automatic search when all its matched episodes are monitored and at least one is missing. The usual quality, custom format, size, seeder and other release checks still apply. Existing episodes are kept, including when a pack contains a better quality, proper or repack. A finished download is only marked complete once every expected episode has been imported or already exists in the library.

The option is off by default, so existing profiles keep their current behaviour until you enable it. It does not change your indexer's minimum seeder setting.

## Status and limits

This is experimental work based on Sonarr's v5 development branch and [IonBazan's multi-season import work](https://github.com/Sonarr/Sonarr/pull/9075). It is not an official Sonarr release. Use a separate test instance and a separate application data directory; do not point it at a working installation's database.

The indexer must return a pack whose season range Sonarr can parse. Packs containing unmonitored episodes are rejected. If one video contains both an existing episode and a missing episode, it is left for manual review because importing it would replace an existing file. Packs download in full; this feature does not change torrent file priorities.

A pack can contain different qualities in different seasons. Its title is used for release selection; it does not prove the quality of every file. This fork keeps existing files and disables quality, custom format, proper and repack upgrades while the option is on. It does not try to make a mixed pack uniform.

A season with no matching episode metadata blocks automatic selection. If a download was marked imported by an older version but still has missing episodes, it stays in the queue with a warning. Review and import the remaining files manually. A download client that deletes its own files independently of Sonarr still needs to be configured to keep them until import finishes.

## Checks against the upstream concerns

The [design discussion](https://github.com/Sonarr/Sonarr/issues/8938) and [incomplete-import reports](https://github.com/Sonarr/Sonarr/issues/8133) describe the main problems this fork needs to avoid:

- Mixed qualities must not cause repeated downloads, including when another indexer offers the same pack. Completed packs are rejected, and queued episodes cannot be upgraded.
- Importing one season must not allow the whole download to be deleted. Completion checks cover every expected episode. Removal checks current metadata and actual library files, including after a restart and when older completion history exists.
- Season mapping and queue counts must cover all seasons. Each season uses its own mapping, missing metadata is reported, and queue counts refresh after a partial import. Interactive search shows the same rejection reasons used for automatic selection.
- Existing files must survive a pack import. They are checked both when making import decisions and immediately before importing, including manually approved imports. Normal library scans still work.

The automated tests cover these cases, ordinary single-season behaviour, profile migrations, both API versions and failure retries. The isolated trial also runs a real application process with a fresh database, generated video files and simulated indexers and download client. It exercises RSS selection, duplicate offers, mixed qualities, partial imports, queue counts, restarts and cleanup. It does not download real torrents or establish compatibility with every indexer or download client.

## Development

Use the .NET SDK version in `global.json`, Node.js 24 and Yarn 1.22.22.

```sh
yarn install --frozen-lockfile
yarn typecheck
yarn build
dotnet build src/Sonarr.sln
```

Run the focused backend tests from the repository root:

```sh
dotnet test src/NzbDrone.Core.Test/Sonarr.Core.Test.csproj \
  -p:SolutionDir="$PWD/src/" \
  --filter 'FullyQualifiedName~MultiSeason|FullyQualifiedName~allow_multi_season_packsFixture|FullyQualifiedName~QualityProfileServiceFixture|FullyQualifiedName~UpgradeSpecificationFixture|FullyQualifiedName~CompletedDownloadServiceTests|FullyQualifiedName~UpgradeAllowedSpecificationFixture|FullyQualifiedName~SingleEpisodeSearchMatchSpecification'
dotnet test src/NzbDrone.Api.Test/Sonarr.Api.Test.csproj \
  -p:SolutionDir="$PWD/src/" \
  --filter 'FullyQualifiedName~MultiSeasonProfileFixture'
```

Run the full unit suite without tests that require external services or manual setup:

```sh
dotnet test src/Sonarr.sln --no-build --no-restore \
  --filter 'Category!=ManualTest&Category!=IntegrationTest&Category!=AutomationTest&Category!=WINDOWS'
```

That filter is for Linux. Windows tests should be run separately on Windows.

The isolated application trial needs Python 3, FFmpeg and ffprobe. Put ffprobe beside the built application, as it is in a normal Sonarr installation, then run:

```sh
python3 scripts/test-multiseason.py --app _output/net10.0/Sonarr.dll
```

The script creates its own temporary application data and media folders, listens only on loopback and leaves its logs there for inspection. It never uses an existing Sonarr database or a real download client. Test a fresh database before trying a copy of an existing library.

## Credits and licence

Sonarr is developed by [the Sonarr project](https://github.com/Sonarr/Sonarr). The season parsing, display and import groundwork here comes from IonBazan's upstream contributions. This fork adds automatic selection and the rule that existing episodes are kept.

The source remains available under the [GNU General Public License v3](LICENSE.md). Upstream history and copyright notices are retained.
