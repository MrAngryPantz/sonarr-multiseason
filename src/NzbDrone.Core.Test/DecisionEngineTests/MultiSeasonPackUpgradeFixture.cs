using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.DecisionEngine.Specifications.Search;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.DecisionEngineTests;

[TestFixture]
public class MultiSeasonPackUpgradeFixture : CoreTest<UpgradeAllowedSpecification>
{
    private QualityProfile _profile;
    private RemoteEpisode _remoteEpisode;

    [SetUp]
    public void Setup()
    {
        _profile = new QualityProfile
        {
            AllowMultiSeasonPacks = true,
            UpgradeAllowed = false,
            Items = Qualities.QualityFixture.GetDefaultQualities()
        };
        _remoteEpisode = new RemoteEpisode
        {
            Series = new Series { Monitored = true, QualityProfile = _profile },
            ParsedEpisodeInfo = new ParsedEpisodeInfo { FullSeason = true, SeasonNumbers = new List<int> { 1, 2 } },
            Episodes = new List<Episode>
            {
                new Episode { Id = 1, SeasonNumber = 1, EpisodeNumber = 1, Monitored = true, EpisodeFileId = 42, AirDateUtc = DateTime.UtcNow.AddDays(-5) },
                new Episode { Id = 2, SeasonNumber = 2, EpisodeNumber = 1, Monitored = true, AirDateUtc = DateTime.UtcNow.AddDays(-5) }
            }
        };
    }

    [TestCase(1)]
    [TestCase(2)]
    public void should_allow_pack_to_fill_gaps_without_upgrading_existing_files(int seasonCount)
    {
        if (seasonCount == 1)
        {
            _remoteEpisode.ParsedEpisodeInfo.SeasonNumbers = new List<int> { 1 };
            _remoteEpisode.Episodes[1].SeasonNumber = 1;
        }

        Subject.IsSatisfiedBy(_remoteEpisode, new()).Accepted.Should().BeTrue();
        Mocker.Resolve<UpgradeDiskSpecification>().IsSatisfiedBy(_remoteEpisode, new()).Accepted.Should().BeTrue();
    }

    [Test]
    public void should_reject_pack_when_every_episode_already_exists()
    {
        _remoteEpisode.Episodes[1].EpisodeFileId = 43;
        Mocker.Resolve<UpgradeDiskSpecification>().IsSatisfiedBy(_remoteEpisode, new()).Accepted.Should().BeFalse();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void should_block_quality_and_revision_upgrades_even_with_inconsistent_settings(bool upgradeAllowed)
    {
        _profile.UpgradeAllowed = upgradeAllowed;
        var specification = Mocker.Resolve<UpgradableSpecification>();
        var current = new QualityModel(Quality.HDTV720p, new Revision(version: 1));
        var proper = new QualityModel(Quality.HDTV720p, new Revision(version: 2));
        var better = new QualityModel(Quality.Bluray1080p);

        specification.IsUpgradeAllowed(_profile, current, new(), proper, new()).Should().BeFalse();
        specification.IsUpgradeAllowed(_profile, current, new(), better, new()).Should().BeFalse();
        specification.IsUpgradable(_profile, current, new(), proper, new()).Should().Be(UpgradeableRejectReason.UpgradesNotAllowed);
        specification.IsUpgradable(_profile, current, new(), better, new()).Should().Be(UpgradeableRejectReason.UpgradesNotAllowed);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void should_only_accept_pack_in_single_episode_search_when_enabled(bool enabled)
    {
        _profile.AllowMultiSeasonPacks = enabled;
        var search = new SingleEpisodeSearchCriteria
        {
            SeasonNumber = 2,
            EpisodeNumber = 1,
            Episodes = new List<Episode> { _remoteEpisode.Episodes[1] }
        };

        Mocker.Resolve<SingleEpisodeSearchMatchSpecification>()
            .IsSatisfiedBy(_remoteEpisode, new ReleaseDecisionInformation(false, search)).Accepted.Should().Be(enabled);
    }

    [Test]
    public void should_not_grab_pack_in_search_for_an_existing_episode()
    {
        var search = new SingleEpisodeSearchCriteria
        {
            SeasonNumber = 1,
            EpisodeNumber = 1,
            Episodes = new List<Episode> { _remoteEpisode.Episodes[0] }
        };

        Mocker.Resolve<SingleEpisodeSearchMatchSpecification>()
            .IsSatisfiedBy(_remoteEpisode, new ReleaseDecisionInformation(false, search)).Accepted.Should().BeFalse();
    }
}
