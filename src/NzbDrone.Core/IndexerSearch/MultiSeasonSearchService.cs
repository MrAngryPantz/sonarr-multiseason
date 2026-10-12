using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.IndexerSearch
{
    public interface IMultiSeasonSearchService
    {
        Task<IList<ReleaseInfo>> Search(IIndexer indexer, SearchCriteriaBase criteria);
    }

    public class MultiSeasonSearchService : IMultiSeasonSearchService,
        IHandle<ProviderUpdatedEvent<IIndexer>>, IHandle<ProviderDeletedEvent<IIndexer>>
    {
        private readonly ICached<Lazy<Task<IList<ReleaseInfo>>>> _cache;
        private readonly IIndexerStatusService _indexerStatusService;

        public MultiSeasonSearchService(ICacheManager cacheManager, IIndexerStatusService indexerStatusService)
        {
            _cache = cacheManager.GetCache<Lazy<Task<IList<ReleaseInfo>>>>(GetType());
            _indexerStatusService = indexerStatusService;
        }

        public async Task<IList<ReleaseInfo>> Search(IIndexer indexer, SearchCriteriaBase criteria)
        {
            if (!indexer.SupportsSearch || _indexerStatusService.GetBlockedProviders().Any(i => i.ProviderId == indexer.Definition.Id))
            {
                return Array.Empty<ReleaseInfo>();
            }

            var search = new MultiSeasonSearchCriteria
            {
                Series = criteria.Series,
                SceneTitles = criteria.SceneTitles.Distinct().OrderBy(t => t, StringComparer.Ordinal).ToList(),
                SearchMode = criteria.SearchMode,
                Episodes = criteria.Episodes,
                MonitoredEpisodesOnly = criteria.MonitoredEpisodesOnly,
                UserInvokedSearch = criteria.UserInvokedSearch,
                InteractiveSearch = criteria.InteractiveSearch
            };

            var key = new
            {
                IndexerId = indexer.Definition.Id,
                criteria.Series.Id,
                criteria.Series.TvdbId,
                criteria.Series.ImdbId,
                criteria.Series.TvRageId,
                criteria.Series.TvMazeId,
                criteria.Series.TmdbId,
                criteria.Series.SeriesType,
                search.SearchMode,
                search.SceneTitles
            }.ToJson();

            try
            {
                // Share an in-flight query as well as its results across episode/season searches.
                // Decisions are always made again against the current library and search criteria.
                return await _cache.Get(key,
                    () => new Lazy<Task<IList<ReleaseInfo>>>(() => FetchPacks(indexer, search)),
                    TimeSpan.FromMinutes(1)).Value;
            }
            catch
            {
                _cache.Remove(key);
                throw;
            }
        }

        private static async Task<IList<ReleaseInfo>> FetchPacks(IIndexer indexer, MultiSeasonSearchCriteria criteria)
        {
            var releases = await indexer.Fetch(criteria);
            return releases.Where(r => Parser.Parser.ParseTitle(r.Title)?.IsMultiSeason == true).ToList();
        }

        public void Handle(ProviderUpdatedEvent<IIndexer> message)
        {
            _cache.Clear();
        }

        public void Handle(ProviderDeletedEvent<IIndexer> message)
        {
            _cache.Clear();
        }
    }
}
