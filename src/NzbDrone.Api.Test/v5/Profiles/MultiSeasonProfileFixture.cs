using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Profiles.Qualities;
using V3Mapper = Sonarr.Api.V3.Profiles.Quality.ProfileResourceMapper;
using V5Mapper = Sonarr.Api.V5.Profiles.Quality.ProfileResourceMapper;

namespace NzbDrone.Api.Test.v5.Profiles;

[TestFixture]
public class MultiSeasonProfileFixture
{
    [TestCase(false)]
    [TestCase(true)]
    public void should_round_trip_option_and_disable_upgrades_in_both_api_versions(bool allowMultiSeasonPacks)
    {
        var profile = new QualityProfile
        {
            Name = "Test profile",
            AllowMultiSeasonPacks = allowMultiSeasonPacks,
            UpgradeAllowed = true,
            Items = []
        };

        var v3 = V3Mapper.ToModel(V3Mapper.ToResource(profile));
        var v5 = V5Mapper.ToModel(V5Mapper.ToResource(profile));

        v3.AllowMultiSeasonPacks.Should().Be(allowMultiSeasonPacks);
        v5.AllowMultiSeasonPacks.Should().Be(allowMultiSeasonPacks);
        v3.UpgradeAllowed.Should().Be(!allowMultiSeasonPacks);
        v5.UpgradeAllowed.Should().Be(!allowMultiSeasonPacks);
    }
}
