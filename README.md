# Sonarr Multi-Season

This fork lets Sonarr automatically download packs that contain several seasons. It is meant for filling gaps in a library while keeping the episodes you already have.

In **Settings → Profiles**, edit a quality profile and enable **Allow Multi-Season Packs**. This turns **Upgrades Allowed** off and prevents it from being turned back on while the option is enabled. Turning multi-season packs off leaves upgrades off.

A pack can be selected from RSS or automatic search when all its matched episodes are monitored and at least one is missing. The usual quality, custom format, size, seeder and other release checks still apply. Existing episodes are kept, including when a pack contains a better quality, proper or repack. A finished download is only marked complete once every expected episode has been imported or already exists in the library.

The option is off by default, so existing profiles keep their current behaviour until you enable it. It does not change your indexer's minimum seeder setting.

## Status and limits

This is experimental work based on Sonarr's v5 development branch and [IonBazan's multi-season import work](https://github.com/Sonarr/Sonarr/pull/9075). It is not an official Sonarr release. Use a separate test instance and a separate application data directory; do not point it at a working installation's database.

The indexer must return a pack whose season range Sonarr can parse. Packs containing unmonitored episodes are rejected. If one video contains both an existing episode and a missing episode, it is left for manual review because importing it would replace an existing file. Packs download in full; this feature does not change torrent file priorities.

The first version supports filling missing episodes. Upgrading multi-season packs is deliberately disabled.

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

The tests cover opting in, forcing upgrades off, filling gaps, preserving files, API settings and incomplete downloads. Test a fresh database before trying a copy of an existing library.

## Credits and licence

Sonarr is developed by [the Sonarr project](https://github.com/Sonarr/Sonarr). The season parsing, display and import groundwork here comes from IonBazan's upstream contributions. This fork adds automatic selection and the rule that existing episodes are kept.

The source remains available under the [GNU General Public License v3](LICENSE.md). Upstream history and copyright notices are retained.
