using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Newznab;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerTests.NewznabTests
{
    public class NewznabRequestGeneratorFixture : CoreTest<NewznabRequestGenerator>
    {
        private SingleEpisodeSearchCriteria _singleEpisodeSearchCriteria;
        private SeasonSearchCriteria _seasonSearchCriteria;
        private AnimeEpisodeSearchCriteria _animeSearchCriteria;
        private AnimeSeasonSearchCriteria _animeSeasonSearchCriteria;
        private NewznabCapabilities _capabilities;

        [SetUp]
        public void SetUp()
        {
            Subject.Definition = new IndexerDefinition
            {
                Name = "Newznab"
            };

            Subject.Settings = new NewznabSettings()
            {
                BaseUrl = "http://127.0.0.1:1234/",
                Categories = new[] { 1, 2 },
                AnimeCategories = new[] { 3, 4 },
                ApiKey = "abcd",
            };

            _singleEpisodeSearchCriteria = new SingleEpisodeSearchCriteria
            {
                Series = new Tv.Series { TvRageId = 10, TvdbId = 20, TvMazeId = 30, ImdbId = "t40", TmdbId = 50 },
                SceneTitles = new List<string> { "Monkey Island" },
                SeasonNumber = 1,
                EpisodeNumber = 2
            };

            _seasonSearchCriteria = new SeasonSearchCriteria
            {
                Series = new Tv.Series { TvRageId = 10, TvdbId = 20, TvMazeId = 30, ImdbId = "t40", TmdbId = 50 },
                SceneTitles = new List<string> { "Monkey Island" },
                SeasonNumber = 1,
            };

            _animeSearchCriteria = new AnimeEpisodeSearchCriteria()
            {
                Series = new Tv.Series { TvRageId = 10, TvdbId = 20, TvMazeId = 30, ImdbId = "t40", TmdbId = 50 },
                SceneTitles = new List<string>() { "Monkey+Island" },
                AbsoluteEpisodeNumber = 100,
                SeasonNumber = 5,
                EpisodeNumber = 4
            };

            _animeSeasonSearchCriteria = new AnimeSeasonSearchCriteria()
            {
                Series = new Tv.Series { TvRageId = 10, TvdbId = 20, TvMazeId = 30, ImdbId = "t40", TmdbId = 50 },
                SceneTitles = new List<string> { "Monkey Island" },
                SeasonNumber = 3,
            };

            _capabilities = new NewznabCapabilities();

            Mocker.GetMock<INewznabCapabilitiesProvider>()
                .Setup(v => v.GetCapabilities(It.IsAny<NewznabSettings>()))
                .Returns(_capabilities);
        }

        private MultiSeasonSearchCriteria PackCriteria()
        {
            return new MultiSeasonSearchCriteria
            {
                Series = _seasonSearchCriteria.Series,
                SceneTitles = _seasonSearchCriteria.SceneTitles
            };
        }

        [TestCase("tvdbid", "20")]
        [TestCase("imdbid", "t40")]
        [TestCase("rid", "10")]
        [TestCase("tvmazeid", "30")]
        [TestCase("tmdbid", "50")]
        public void should_search_packs_by_supported_id_without_season_support(string parameter, string value)
        {
            _capabilities.SupportedTvSearchParameters = new[] { parameter };
            _capabilities.SupportedSearchParameters = null;

            var requests = Subject.GetSearchRequests(PackCriteria()).GetAllTiers().Select(p => p.First()).ToList();

            requests.Should().ContainSingle();
            requests[0].Url.Query.Should().Contain($"&{parameter}={value}").And.NotContain("season=").And.NotContain("ep=");
        }

        [TestCase("q")]
        [TestCase("title")]
        public void should_search_packs_by_supported_tv_text_parameter(string parameter)
        {
            _capabilities.SupportedTvSearchParameters = new[] { parameter };
            _capabilities.SupportedSearchParameters = null;

            var requests = Subject.GetSearchRequests(PackCriteria()).GetAllTiers().Select(p => p.First()).ToList();

            requests.Should().ContainSingle();
            requests[0].Url.Query.Should().Contain($"&{parameter}=Monkey%20Island").And.NotContain("season=").And.NotContain("ep=");
        }

        [Test]
        public void should_search_packs_with_only_generic_search_support()
        {
            _capabilities.SupportedTvSearchParameters = null;
            _capabilities.SupportedSearchParameters = new[] { "q" };

            var requests = Subject.GetSearchRequests(PackCriteria()).GetAllTiers().Select(p => p.First()).ToList();

            requests.Should().ContainSingle();
            requests[0].Url.Query.Should().Contain("t=search&").And.Contain("&q=Monkey%20Island");
        }

        [TestCase("q", false, false)]
        [TestCase("title", false, false)]
        [TestCase("title", true, false)]
        [TestCase("title", false, true)]
        [TestCase("title", true, true)]
        [TestCase("tvdbid", false, false)]
        public void should_only_send_advertised_episode_filters(string parameter, bool season, bool episode)
        {
            var supported = new List<string> { parameter };
            if (season)
            {
                supported.Add("season");
            }

            if (episode)
            {
                supported.Add("ep");
            }

            _capabilities.SupportedTvSearchParameters = supported.ToArray();
            _capabilities.SupportedSearchParameters = null;
            var requests = Subject.GetSearchRequests(_singleEpisodeSearchCriteria).GetAllTiers().Select(p => p.First()).ToList();
            if (!season || !episode)
            {
                requests.Should().BeEmpty();
                Subject.GetSearchRequests(PackCriteria()).GetAllTiers().Should().NotBeEmpty();
                return;
            }

            requests.Should().NotBeEmpty();
            foreach (var request in requests)
            {
                request.Url.Query.Should().Contain("&" + parameter + "=");
                request.Url.Query.Contains("&season=").Should().Be(season);
                request.Url.Query.Contains("&ep=").Should().Be(episode);
            }
        }

        [Test]
        public void should_search_text_for_unmapped_packs_even_when_id_search_returns_episodes()
        {
            _capabilities.SupportedTvSearchParameters = new[] { "tvdbid", "q", "season", "ep" };

            var requests = Subject.GetSearchRequests(PackCriteria());

            requests.Tiers.Should().Be(1);
            requests.GetTier(0).Should().HaveCount(3);
        }

        [Test]
        public void should_use_anime_categories_for_every_pack_query()
        {
            var criteria = PackCriteria();
            criteria.Series.SeriesType = Tv.SeriesTypes.Anime;

            var requests = Subject.GetSearchRequests(criteria).GetAllTiers().Select(p => p.First()).ToList();

            requests.Should().HaveCount(3);
            requests.Should().OnlyContain(r => r.Url.Query.Contains("&cat=3,4&"));
        }

        [TestCase(NzbDrone.Core.DataAugmentation.Scene.SearchMode.SearchID, 1)]
        [TestCase(NzbDrone.Core.DataAugmentation.Scene.SearchMode.SearchTitle, 2)]
        public void should_respect_pack_search_mode(NzbDrone.Core.DataAugmentation.Scene.SearchMode mode, int count)
        {
            var criteria = PackCriteria();
            criteria.SearchMode = mode;

            Subject.GetSearchRequests(criteria).GetAllTiers().Should().HaveCount(count);
        }

        [Test]
        public void should_not_send_pack_requests_without_search_capabilities()
        {
            _capabilities.SupportedTvSearchParameters = null;
            _capabilities.SupportedSearchParameters = null;

            Subject.GetSearchRequests(PackCriteria()).GetAllTiers().Should().BeEmpty();
        }

        [Test]
        public void should_use_all_categories_for_feed()
        {
            var results = Subject.GetRecentRequests();

            results.GetAllTiers().Should().HaveCount(1);

            var page = results.GetAllTiers().First().First();

            page.Url.Query.Should().Contain("&cat=1,2,3,4&");
        }

        [Test]
        public void should_include_tv_categories_for_generic_only_feed()
        {
            _capabilities.SupportedTvSearchParameters = null;

            var request = Subject.GetRecentRequests().GetAllTiers().Single().First();

            request.Url.Query.Should().Contain("t=search&").And.Contain("&cat=1,2,3,4&");
        }

        [Test]
        public void should_not_have_duplicate_categories()
        {
            Subject.Settings.Categories = new[] { 1, 2, 3 };

            var results = Subject.GetRecentRequests();

            results.GetAllTiers().Should().HaveCount(1);

            var page = results.GetAllTiers().First().First();

            page.Url.FullUri.Should().Contain("&cat=1,2,3,4&");
        }

        [Test]
        public void should_use_only_anime_categories_for_anime_search()
        {
            var results = Subject.GetSearchRequests(_animeSearchCriteria);

            results.GetAllTiers().Should().HaveCount(2);

            var pages = results.GetTier(0).Select(t => t.First()).ToList();

            pages[0].Url.FullUri.Should().Contain("&cat=3,4&");
            pages[1].Url.FullUri.Should().Contain("&cat=3,4&");
        }

        [Test]
        public void should_use_mode_search_for_anime()
        {
            var results = Subject.GetSearchRequests(_animeSearchCriteria);

            results.GetAllTiers().Should().HaveCount(2);

            results.GetAllTiers().First().First().Url.FullUri.Should().Contain("?t=tvsearch&");
            results.GetAllTiers().Last().First().Url.FullUri.Should().Contain("?t=search&");
        }

        [Test]
        public void should_return_subsequent_pages()
        {
            var results = Subject.GetSearchRequests(_animeSearchCriteria);

            results.GetAllTiers().Should().HaveCount(2);

            var pages = results.GetAllTiers().First().Take(3).ToList();

            pages[0].Url.FullUri.Should().Contain("&offset=0&");
            pages[1].Url.FullUri.Should().Contain("&offset=100&");
            pages[2].Url.FullUri.Should().Contain("&offset=200&");
        }

        [Test]
        public void should_not_get_unlimited_pages()
        {
            var results = Subject.GetSearchRequests(_animeSearchCriteria);

            results.GetAllTiers().Should().HaveCount(2);

            var pages = results.GetAllTiers().First().Take(500).ToList();

            pages.Count.Should().BeLessThan(500);
        }

        [Test]
        public void should_use_only_absolute_numbering_for_anime_search()
        {
            var results = Subject.GetSearchRequests(_animeSearchCriteria);

            results.GetAllTiers().Should().HaveCount(2);

            var pages = results.GetTier(0).Select(t => t.First()).ToList();

            pages[0].Url.FullUri.Should().Contain("rid=10&q=100");
            pages[1].Url.FullUri.Should().Contain("q=Monkey%20Island+100");
        }

        [Test]
        public void should_also_use_standard_numbering_for_anime_search()
        {
            Subject.Settings.AnimeStandardFormatSearch = true;
            var results = Subject.GetSearchRequests(_animeSearchCriteria);

            results.GetTier(0).Should().HaveCount(4);
            var pages = results.GetTier(0).Select(t => t.First()).ToList();

            pages[0].Url.FullUri.Should().Contain("rid=10&q=100");
            pages[1].Url.FullUri.Should().Contain("rid=10&season=5&ep=4");
            pages[2].Url.FullUri.Should().Contain("q=Monkey%20Island+100");
            pages[3].Url.FullUri.Should().Contain("q=Monkey%20Island&season=5&ep=4");
        }

        [Test]
        public void should_search_by_standard_season_number()
        {
            Subject.Settings.AnimeStandardFormatSearch = true;
            var results = Subject.GetSearchRequests(_animeSeasonSearchCriteria);

            results.GetTier(0).Should().HaveCount(2);
            var pages = results.GetTier(0).Select(t => t.First()).ToList();

            pages[0].Url.FullUri.Should().Contain("rid=10&season=3");
            pages[1].Url.FullUri.Should().Contain("q=Monkey%20Island&season=3");
        }

        [Test]
        public void should_search_season_title_alias_without_the_season_parameter()
        {
            Subject.Settings.AnimeStandardFormatSearch = true;
            _animeSeasonSearchCriteria.SeasonSceneTitles = new List<string> { "Monkey Island" };

            var results = Subject.GetSearchRequests(_animeSeasonSearchCriteria);

            var pages = results.GetAllTiers().Select(t => t.First().Url.FullUri).ToList();

            pages.Should().ContainSingle(p => p.Contains("q="));
            pages.Should().Contain(p => p.Contains("?t=search&") && p.Contains("q=Monkey%20Island") && !p.Contains("season="));
        }

        [Test]
        public void should_search_season_title_alias_when_standard_format_search_is_disabled()
        {
            Subject.Settings.AnimeStandardFormatSearch = false;
            _animeSeasonSearchCriteria.SeasonSceneTitles = new List<string> { "Monkey Island" };

            var results = Subject.GetSearchRequests(_animeSeasonSearchCriteria);

            var pages = results.GetAllTiers().Select(t => t.First().Url.FullUri).ToList();

            pages.Should().ContainSingle();
            pages.Should().Contain(p => p.Contains("q=Monkey%20Island") && !p.Contains("season="));
        }

        [Test]
        public void should_only_search_the_season_title_without_the_season_parameter()
        {
            Subject.Settings.AnimeStandardFormatSearch = true;
            _animeSeasonSearchCriteria.SceneTitles = new List<string> { "Monkey Island", "Monkey Island Revenge" };
            _animeSeasonSearchCriteria.SeasonSceneTitles = new List<string> { "Monkey Island Revenge" };

            var results = Subject.GetSearchRequests(_animeSeasonSearchCriteria);

            var pages = results.GetAllTiers().Select(t => t.First().Url.FullUri).ToList();

            pages.Should().Contain(p => p.Contains("q=Monkey%20Island&season=3"));
            pages.Should().Contain(p => p.Contains("q=Monkey%20Island%20Revenge") && !p.Contains("season="));
        }

        [Test]
        public void should_not_search_by_rid_if_not_supported()
        {
            _capabilities.SupportedTvSearchParameters = new[] { "q", "season", "ep" };

            var results = Subject.GetSearchRequests(_singleEpisodeSearchCriteria);

            results.GetAllTiers().Should().HaveCount(1);

            var page = results.GetAllTiers().First().First();

            page.Url.Query.Should().NotContain("rid=10");
            page.Url.Query.Should().Contain("q=Monkey");
        }

        [Test]
        public void should_search_by_rid_if_supported()
        {
            var results = Subject.GetSearchRequests(_singleEpisodeSearchCriteria);
            results.GetTier(0).Should().HaveCount(1);

            var page = results.GetAllTiers().First().First();

            page.Url.Query.Should().Contain("rid=10");
        }

        [Test]
        public void should_not_search_by_tvdbid_if_not_supported()
        {
            _capabilities.SupportedTvSearchParameters = new[] { "q", "season", "ep" };

            var results = Subject.GetSearchRequests(_singleEpisodeSearchCriteria);
            results.GetTier(0).Should().HaveCount(1);

            var page = results.GetAllTiers().First().First();

            page.Url.Query.Should().NotContain("rid=10");
            page.Url.Query.Should().Contain("q=Monkey");
        }

        [Test]
        public void should_search_by_tvdbid_if_supported()
        {
            _capabilities.SupportedTvSearchParameters = new[] { "q", "tvdbid", "season", "ep" };

            var results = Subject.GetSearchRequests(_singleEpisodeSearchCriteria);
            results.GetTier(0).Should().HaveCount(1);

            var page = results.GetAllTiers().First().First();

            page.Url.Query.Should().Contain("tvdbid=20");
        }

        [Test]
        public void should_search_by_tvmaze_if_supported()
        {
            _capabilities.SupportedTvSearchParameters = new[] { "q", "tvmazeid", "season", "ep" };

            var results = Subject.GetSearchRequests(_singleEpisodeSearchCriteria);
            results.GetTier(0).Should().HaveCount(1);

            var page = results.GetAllTiers().First().First();

            page.Url.Query.Should().Contain("tvmazeid=30");
        }

        [Test]
        public void should_search_by_imdbid_if_supported()
        {
            _capabilities.SupportedTvSearchParameters = new[] { "q", "imdbid", "season", "ep" };

            var results = Subject.GetSearchRequests(_singleEpisodeSearchCriteria);
            results.GetTier(0).Should().HaveCount(1);

            var page = results.GetAllTiers().First().First();

            page.Url.Query.Should().Contain("imdbid=t40");
        }

        [Test]
        public void should_search_by_tmdb_if_supported()
        {
            _capabilities.SupportedTvSearchParameters = new[] { "q", "tmdbid", "season", "ep" };

            var results = Subject.GetSearchRequests(_singleEpisodeSearchCriteria);
            results.GetTier(0).Should().HaveCount(1);

            var page = results.GetAllTiers().First().First();

            page.Url.Query.Should().Contain("tmdbid=50");
        }

        [Test]
        public void should_prefer_search_by_tvdbid_if_rid_supported()
        {
            _capabilities.SupportedTvSearchParameters = new[] { "q", "tvdbid", "rid", "season", "ep" };

            var results = Subject.GetSearchRequests(_singleEpisodeSearchCriteria);
            results.GetTier(0).Should().HaveCount(1);

            var page = results.GetAllTiers().First().First();

            page.Url.Query.Should().Contain("tvdbid=20");
            page.Url.Query.Should().NotContain("rid=10");
        }

        [Test]
        public void should_use_aggregrated_id_search_if_supported()
        {
            _capabilities.SupportedTvSearchParameters = new[] { "q", "tvdbid", "rid", "season", "ep" };
            _capabilities.SupportsAggregateIdSearch = true;

            var results = Subject.GetSearchRequests(_singleEpisodeSearchCriteria);
            results.GetTier(0).Should().HaveCount(1);

            var page = results.GetTier(0).First().First();

            page.Url.Query.Should().Contain("tvdbid=20");
            page.Url.Query.Should().Contain("rid=10");
        }

        [Test]
        public void should_not_use_aggregrated_id_search_if_no_ids_supported()
        {
            _capabilities.SupportedTvSearchParameters = new[] { "q", "season", "ep" };
            _capabilities.SupportsAggregateIdSearch = true; // Turns true if indexer supplies supportedParams.

            var results = Subject.GetSearchRequests(_singleEpisodeSearchCriteria);
            results.Tiers.Should().Be(1);
            results.GetTier(0).Should().HaveCount(1);

            var page = results.GetTier(0).First().First();

            page.Url.Query.Should().Contain("q=");
        }

        [Test]
        public void should_not_use_aggregrated_id_search_if_no_ids_are_known()
        {
            _capabilities.SupportedTvSearchParameters = new[] { "q", "rid", "season", "ep" };
            _capabilities.SupportsAggregateIdSearch = true; // Turns true if indexer supplies supportedParams.

            _singleEpisodeSearchCriteria.Series.TvRageId = 0;

            var results = Subject.GetSearchRequests(_singleEpisodeSearchCriteria);

            var page = results.GetTier(0).First().First();

            page.Url.Query.Should().Contain("q=");
        }

        [Test]
        public void should_fallback_to_title()
        {
            _capabilities.SupportedTvSearchParameters = new[] { "q", "title", "tvdbid", "rid", "season", "ep" };
            _capabilities.SupportsAggregateIdSearch = true;

            var results = Subject.GetSearchRequests(_singleEpisodeSearchCriteria);
            results.Tiers.Should().Be(2);

            var pageTier2 = results.GetTier(1).First().First();

            pageTier2.Url.Query.Should().NotContain("tvdbid=20");
            pageTier2.Url.Query.Should().NotContain("rid=10");
            pageTier2.Url.Query.Should().NotContain("q=");
            pageTier2.Url.Query.Should().Contain("title=Monkey%20Island");
        }

        [Test]
        public void should_url_encode_title()
        {
            _capabilities.SupportedTvSearchParameters = new[] { "q", "title", "tvdbid", "rid", "season", "ep" };
            _capabilities.SupportsAggregateIdSearch = true;

            _singleEpisodeSearchCriteria.SceneTitles[0] = "Elith & Little";

            var results = Subject.GetSearchRequests(_singleEpisodeSearchCriteria);
            results.Tiers.Should().Be(2);

            var pageTier2 = results.GetTier(1).First().First();

            pageTier2.Url.Query.Should().NotContain("tvdbid=20");
            pageTier2.Url.Query.Should().NotContain("rid=10");
            pageTier2.Url.Query.Should().NotContain("q=");
            pageTier2.Url.Query.Should().Contain("title=Elith%20%26%20Little");
        }

        [Test]
        public void should_fallback_to_q()
        {
            _capabilities.SupportedTvSearchParameters = new[] { "q", "tvdbid", "rid", "season", "ep" };
            _capabilities.SupportsAggregateIdSearch = true;

            var results = Subject.GetSearchRequests(_singleEpisodeSearchCriteria);
            results.Tiers.Should().Be(2);

            var pageTier2 = results.GetTier(1).First().First();

            pageTier2.Url.Query.Should().NotContain("tvdbid=20");
            pageTier2.Url.Query.Should().NotContain("rid=10");
            pageTier2.Url.Query.Should().Contain("q=");
        }

        [Test]
        public void should_encode_raw_title()
        {
            _capabilities.SupportedTvSearchParameters = new[] { "q", "season", "ep" };
            _capabilities.TvTextSearchEngine = "raw";
            _singleEpisodeSearchCriteria.SceneTitles[0] = "Edith & Little";

            var results = Subject.GetSearchRequests(_singleEpisodeSearchCriteria);
            results.Tiers.Should().Be(1);

            var pageTier = results.GetTier(0).First().First();

            pageTier.Url.Query.Should().Contain("q=Edith%20%26%20Little");
            pageTier.Url.Query.Should().NotContain(" & ");
            pageTier.Url.Query.Should().Contain("%26");
        }

        [Test]
        public void should_use_clean_title_and_encode()
        {
            _capabilities.SupportedTvSearchParameters = new[] { "q", "season", "ep" };
            _capabilities.TvTextSearchEngine = "sphinx";
            _singleEpisodeSearchCriteria.SceneTitles[0] = "Edith & Little";

            var results = Subject.GetSearchRequests(_singleEpisodeSearchCriteria);
            results.Tiers.Should().Be(1);

            var pageTier = results.GetTier(0).First().First();

            pageTier.Url.Query.Should().Contain("q=Edith%20and%20Little");
            pageTier.Url.Query.Should().Contain("and");
            pageTier.Url.Query.Should().NotContain(" & ");
            pageTier.Url.Query.Should().NotContain("%26");
        }

        [Test]
        public void should_allow_season_search_even_if_episode_search_is_not_allowed()
        {
            _capabilities.SupportedTvSearchParameters = new[] { "q", "tvdbid", "season" };

            var results = Subject.GetSearchRequests(_seasonSearchCriteria);
            results.GetTier(0).Should().HaveCount(1);

            var page = results.GetAllTiers().First().First();

            page.Url.Query.Should().Contain("tvdbid=20");
        }

        [Test]
        public void should_not_allow_season_search_if_season_param_is_not_allowed()
        {
            _capabilities.SupportedTvSearchParameters = new[] { "q", "tvdbid" };

            var results = Subject.GetSearchRequests(_seasonSearchCriteria);
            results.GetTier(0).Should().HaveCount(0);
        }
    }
}
