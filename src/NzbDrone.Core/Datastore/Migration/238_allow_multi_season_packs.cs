using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(238)]
public class allow_multi_season_packs : NzbDroneMigrationBase
{
    protected override void MainDbUpgrade()
    {
        Alter.Table("QualityProfiles").AddColumn("AllowMultiSeasonPacks").AsBoolean().NotNullable().WithDefaultValue(false);
    }
}
