using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.ThingiProvider.Events;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.IndexerSearchTests
{
    public class MultiSeasonSearchServiceFixture : CoreTest<MultiSeasonSearchService>
    {
        private Mock<IIndexer> _indexer;
        private SeasonSearchCriteria _criteria;
        private List<ReleaseInfo> _releases;

        [SetUp]
        public void SetUp()
        {
            Mocker.GetMock<IIndexerStatusService>().Setup(s => s.GetBlockedProviders()).Returns(new List<IndexerStatus>());
            _indexer = Mocker.GetMock<IIndexer>();
            _indexer.SetupGet(i => i.SupportsSearch).Returns(true);
            _indexer.SetupGet(i => i.Definition).Returns(new IndexerDefinition { Id = 1 });
            _criteria = new SeasonSearchCriteria
            {
                Series = new Series { Id = 1, TvdbId = 20 },
                SceneTitles = new List<string> { "Example Show" },
                SeasonNumber = 1,
                Episodes = new List<Episode> { new Episode { Id = 1 } }
            };
            _releases = new List<ReleaseInfo>
            {
                new ReleaseInfo { Guid = "pack", Title = "Example.Show.S01-S03.720p.WEB-DL" },
                new ReleaseInfo { Guid = "episode", Title = "Example.Show.S01E01.720p.WEB-DL" },
                new ReleaseInfo { Guid = "season", Title = "Example.Show.S01.720p.WEB-DL" }
            };
            _indexer.Setup(i => i.Fetch(It.IsAny<MultiSeasonSearchCriteria>())).ReturnsAsync(_releases);
        }

        [Test]
        public async Task should_only_add_multi_season_results_from_broad_search()
        {
            var releases = await Subject.Search(_indexer.Object, _criteria);

            releases.Select(r => r.Guid).Should().Equal("pack");
        }

        [Test]
        public async Task should_reuse_pack_query_across_seasons_and_episodes()
        {
            await Subject.Search(_indexer.Object, _criteria);
            _criteria.SeasonNumber = 2;
            await Subject.Search(_indexer.Object, _criteria);
            await Subject.Search(_indexer.Object, new SingleEpisodeSearchCriteria
            {
                Series = _criteria.Series,
                SceneTitles = _criteria.SceneTitles,
                Episodes = _criteria.Episodes,
                SeasonNumber = 3,
                EpisodeNumber = 1
            });

            _indexer.Verify(i => i.Fetch(It.IsAny<MultiSeasonSearchCriteria>()), Times.Once());
        }

        [Test]
        public async Task should_share_in_flight_pack_query()
        {
            var pending = new TaskCompletionSource<IList<ReleaseInfo>>();
            _indexer.Setup(i => i.Fetch(It.IsAny<MultiSeasonSearchCriteria>())).Returns(pending.Task);
            var first = Subject.Search(_indexer.Object, _criteria);
            var second = Subject.Search(_indexer.Object, _criteria);
            pending.SetResult(_releases);
            await Task.WhenAll(first, second);

            _indexer.Verify(i => i.Fetch(It.IsAny<MultiSeasonSearchCriteria>()), Times.Once());
        }

        [Test]
        public async Task should_retry_after_failed_query()
        {
            _indexer.SetupSequence(i => i.Fetch(It.IsAny<MultiSeasonSearchCriteria>()))
                .ThrowsAsync(new InvalidOperationException("Test failure"))
                .ReturnsAsync(_releases);

            Assert.ThrowsAsync<InvalidOperationException>(() => Subject.Search(_indexer.Object, _criteria));
            var releases = await Subject.Search(_indexer.Object, _criteria);

            releases.Should().ContainSingle();
            _indexer.Verify(i => i.Fetch(It.IsAny<MultiSeasonSearchCriteria>()), Times.Exactly(2));
        }

        [Test]
        public async Task should_refresh_after_indexer_settings_change()
        {
            await Subject.Search(_indexer.Object, _criteria);
            Subject.Handle(new ProviderUpdatedEvent<IIndexer>(_indexer.Object.Definition));
            await Subject.Search(_indexer.Object, _criteria);

            _indexer.Verify(i => i.Fetch(It.IsAny<MultiSeasonSearchCriteria>()), Times.Exactly(2));
        }

        [Test]
        public async Task should_keep_scene_alias_queries_separate()
        {
            await Subject.Search(_indexer.Object, _criteria);
            _criteria.SceneTitles = new List<string> { "Different Scene Title" };
            await Subject.Search(_indexer.Object, _criteria);

            _indexer.Verify(i => i.Fetch(It.IsAny<MultiSeasonSearchCriteria>()), Times.Exactly(2));
        }

        [Test]
        public async Task should_not_send_extra_queries_after_indexer_rate_limit()
        {
            Mocker.GetMock<IIndexerStatusService>().Setup(s => s.GetBlockedProviders())
                .Returns(new List<IndexerStatus> { new IndexerStatus { ProviderId = 1, DisabledTill = DateTime.UtcNow.AddHours(1) } });

            var releases = await Subject.Search(_indexer.Object, _criteria);

            releases.Should().BeEmpty();
            _indexer.Verify(i => i.Fetch(It.IsAny<MultiSeasonSearchCriteria>()), Times.Never());
        }

        [Test]
        public async Task should_not_query_rss_only_indexers()
        {
            _indexer.SetupGet(i => i.SupportsSearch).Returns(false);

            var releases = await Subject.Search(_indexer.Object, _criteria);

            releases.Should().BeEmpty();
            _indexer.Verify(i => i.Fetch(It.IsAny<MultiSeasonSearchCriteria>()), Times.Never());
        }
    }
}
