using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.BroadcastheNet;
using NzbDrone.Core.Indexers.Fanzub;
using NzbDrone.Core.Indexers.FileList;
using NzbDrone.Core.Indexers.HDBits;
using NzbDrone.Core.Indexers.IPTorrents;
using NzbDrone.Core.Indexers.Nyaa;
using NzbDrone.Core.Indexers.Torrentleech;
using NzbDrone.Core.Indexers.TorrentRss;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.IndexerTests
{
    public class MultiSeasonRequestGeneratorFixture : CoreTest
    {
        private MultiSeasonSearchCriteria _criteria;

        [SetUp]
        public void SetUp()
        {
            _criteria = new MultiSeasonSearchCriteria
            {
                Series = new Series { Id = 1, TvdbId = 20, ImdbId = "tt1234567" },
                SceneTitles = new List<string> { "Example Show" },
                Episodes = new List<Episode> { new Episode { SeasonNumber = 2, EpisodeNumber = 1 } }
            };
        }

        [Test]
        public void should_search_btn_without_episode_or_season_category()
        {
            var generator = new BroadcastheNetRequestGenerator { Settings = new BroadcastheNetSettings { ApiKey = "test" } };
            var request = generator.GetSearchRequests(_criteria).GetAllTiers().Single().First();
            var parameters = JObject.Parse(Encoding.UTF8.GetString(request.HttpRequest.ContentData))["params"][1];

            parameters["tvdb"].Value<string>().Should().Be("20");
            parameters["category"].Should().BeNull();
            parameters["name"].Should().BeNull();
        }

        [Test]
        public void should_search_hdbits_without_season_or_episode_filters()
        {
            var generator = new HDBitsRequestGenerator { Settings = new HDBitsSettings { Username = "test", ApiKey = "test" } };
            var request = generator.GetSearchRequests(_criteria).GetAllTiers().Single().First();
            var parameters = JObject.Parse(Encoding.UTF8.GetString(request.HttpRequest.ContentData));

            parameters["tvdb"]["id"].Value<int>().Should().Be(20);
            parameters["tvdb"]["season"]?.Value<int?>().Should().BeNull();
            parameters["tvdb"]["episode"]?.Value<int?>().Should().BeNull();
        }

        [TestCase(SeriesTypes.Standard, "1,2")]
        [TestCase(SeriesTypes.Anime, "3")]
        public void should_search_filelist_by_id_and_title_with_correct_categories(SeriesTypes type, string categories)
        {
            _criteria.Series.SeriesType = type;
            var generator = new FileListRequestGenerator
            {
                Settings = new FileListSettings { Username = "test", Passkey = "test", Categories = new[] { 1, 2 }, AnimeCategories = new[] { 3 } }
            };
            var queries = generator.GetSearchRequests(_criteria).GetTier(0).Select(p => p.First().Url.Query).ToList();

            queries.Should().HaveCount(2);
            queries[0].Should().Contain("&type=imdb&query=tt1234567");
            queries[1].Should().Contain("&type=name&query=Example%20Show");
            queries.Should().OnlyContain(q => q.Contains($"category={categories}&") && !q.Contains("season=") && !q.Contains("episode="));
        }

        [Test]
        public void should_escape_nyaa_title_without_episode_suffix()
        {
            _criteria.SceneTitles = new List<string> { "Example & Friends" };
            var generator = new NyaaRequestGenerator { Settings = new NyaaSettings { BaseUrl = "https://example.test" } };
            var request = generator.GetSearchRequests(_criteria).GetAllTiers().Single().First();

            request.Url.Query.Should().Contain("&term=Example%20%26%20Friends");
        }

        [Test]
        public void should_search_fanzub_without_absolute_episode_or_season_suffix()
        {
            var generator = new FanzubRequestGenerator { Settings = new FanzubSettings { BaseUrl = "https://example.test" } };
            var request = generator.GetSearchRequests(_criteria).GetAllTiers().Single().First();

            Uri.UnescapeDataString(request.Url.Query).Should().EndWith("&q=Example+Show");
        }

        [Test]
        public void should_leave_feed_only_generators_on_rss()
        {
            IIndexerRequestGenerator[] generators =
            {
                new RssIndexerRequestGenerator("https://example.test"),
                new TorrentRssIndexerRequestGenerator(),
                new IPTorrentsRequestGenerator(),
                new TorrentleechRequestGenerator()
            };

            foreach (var generator in generators)
            {
                generator.GetSearchRequests(_criteria).GetAllTiers().Should().BeEmpty();
            }
        }
    }
}
