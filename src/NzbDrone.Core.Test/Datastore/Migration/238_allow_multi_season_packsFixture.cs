using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration;

[TestFixture]
public class allow_multi_season_packsFixture : MigrationTest<allow_multi_season_packs>
{
    [TestCase(true)]
    [TestCase(false)]
    public void should_leave_existing_profiles_opted_out_and_preserve_upgrade_setting(bool upgradeAllowed)
    {
        var db = WithDapperMigrationTestDb(c =>
        {
            c.Insert.IntoTable("QualityProfiles").Row(new
            {
                Name = "Existing profile",
                Cutoff = 1,
                Items = "[]",
                UpgradeAllowed = upgradeAllowed,
                MinFormatScore = 0,
                CutoffFormatScore = 0,
                MinUpgradeFormatScore = 1,
                FormatItems = "[]"
            });
        });

        var profile = db.Query<QualityProfile>("SELECT \"AllowMultiSeasonPacks\", \"UpgradeAllowed\" FROM \"QualityProfiles\"").Single();
        profile.AllowMultiSeasonPacks.Should().BeFalse();
        profile.UpgradeAllowed.Should().Be(upgradeAllowed);
    }
}
