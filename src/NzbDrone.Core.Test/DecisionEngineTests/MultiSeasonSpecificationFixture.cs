using System;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class MultiSeasonSpecificationFixture : CoreTest<MultiSeasonSpecification>
    {
        private RemoteEpisode _remoteEpisode;

        [SetUp]
        public void Setup()
        {
            var series = new Series { Id = 1234, Monitored = true, QualityProfile = new QualityProfile() };
            _remoteEpisode = new RemoteEpisode
            {
                ParsedEpisodeInfo = new ParsedEpisodeInfo
                {
                    FullSeason = true,
                    SeasonNumbers = [1, 2, 3, 4, 5]
                },
                Episodes = Builder<Episode>.CreateListOfSize(3)
                                           .All()
                                           .With(s => s.SeriesId = series.Id)
                                           .With(s => s.Monitored = true)
                                           .With(s => s.EpisodeFileId = 0)
                                           .BuildList(),
                Series = series,
                Release = new ReleaseInfo
                {
                    Title = "Series.Title.S01-05.720p.BluRay.X264-RlsGrp"
                }
            };
        }

        [Test]
        public void should_reject_pack_with_an_unmapped_season_in_rss_and_interactive_search()
        {
            _remoteEpisode.Series.QualityProfile.Value.AllowMultiSeasonPacks = true;
            _remoteEpisode.UnmappedSeasonNumbers = [5];

            Subject.IsSatisfiedBy(_remoteEpisode, new()).Accepted.Should().BeFalse();
            Subject.IsSatisfiedBy(_remoteEpisode, new(false, new SeasonSearchCriteria { InteractiveSearch = true })).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_return_true_if_is_not_a_multi_season_release()
        {
            _remoteEpisode.ParsedEpisodeInfo.SeasonNumbers = [1];
            _remoteEpisode.Episodes.Last().AirDateUtc = DateTime.UtcNow.AddDays(+2);
            Subject.IsSatisfiedBy(_remoteEpisode, new()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_return_false_if_is_a_multi_season_release()
        {
            var decision = Subject.IsSatisfiedBy(_remoteEpisode, new());

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(DownloadRejectionReason.MultiSeason);
        }

        [Test]
        public void should_return_false_if_is_a_multi_season_release_in_interactive_search()
        {
            var information = new ReleaseDecisionInformation(false, new SeasonSearchCriteria { SeasonNumber = 3, InteractiveSearch = true });

            Subject.IsSatisfiedBy(_remoteEpisode, information).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_accept_enabled_pack_with_missing_episodes()
        {
            _remoteEpisode.Series.QualityProfile.Value.AllowMultiSeasonPacks = true;
            _remoteEpisode.Episodes[0].EpisodeFileId = 42;

            Subject.IsSatisfiedBy(_remoteEpisode, new()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_completed_pack_even_if_better_quality_is_available()
        {
            _remoteEpisode.Series.QualityProfile.Value.AllowMultiSeasonPacks = true;
            _remoteEpisode.Episodes.ForEach(e => e.EpisodeFileId = e.Id);

            Subject.IsSatisfiedBy(_remoteEpisode, new()).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_reject_inconsistent_upgrade_setting()
        {
            _remoteEpisode.Series.QualityProfile.Value.AllowMultiSeasonPacks = true;
            _remoteEpisode.Series.QualityProfile.Value.UpgradeAllowed = true;

            Subject.IsSatisfiedBy(_remoteEpisode, new()).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_reject_unmonitored_episode_even_during_search()
        {
            _remoteEpisode.Series.QualityProfile.Value.AllowMultiSeasonPacks = true;
            _remoteEpisode.Episodes[0].Monitored = false;
            var information = new ReleaseDecisionInformation(false, new SeasonSearchCriteria { MonitoredEpisodesOnly = false });

            Subject.IsSatisfiedBy(_remoteEpisode, information).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_reject_unmonitored_series()
        {
            _remoteEpisode.Series.QualityProfile.Value.AllowMultiSeasonPacks = true;
            _remoteEpisode.Series.Monitored = false;

            Subject.IsSatisfiedBy(_remoteEpisode, new()).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_reject_pack_without_mapped_episodes()
        {
            _remoteEpisode.Series.QualityProfile.Value.AllowMultiSeasonPacks = true;
            _remoteEpisode.Episodes.Clear();

            Subject.IsSatisfiedBy(_remoteEpisode, new()).Accepted.Should().BeFalse();
        }
    }
}
