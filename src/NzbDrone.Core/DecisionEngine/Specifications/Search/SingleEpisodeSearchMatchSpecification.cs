using System.Linq;
using NLog;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications.Search
{
    public class SingleEpisodeSearchMatchSpecification : IDownloadDecisionEngineSpecification
    {
        private readonly Logger _logger;
        private readonly ISceneMappingService _sceneMappingService;

        public SingleEpisodeSearchMatchSpecification(ISceneMappingService sceneMappingService, Logger logger)
        {
            _logger = logger;
            _sceneMappingService = sceneMappingService;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public DownloadSpecDecision IsSatisfiedBy(RemoteEpisode remoteEpisode, ReleaseDecisionInformation information)
        {
            var searchCriteria = information.SearchCriteria;

            if (searchCriteria == null)
            {
                return DownloadSpecDecision.Accept();
            }

            if (searchCriteria is SingleEpisodeSearchCriteria singleEpisodeSpec)
            {
                return IsSatisfiedBy(remoteEpisode, singleEpisodeSpec);
            }

            if (searchCriteria is AnimeEpisodeSearchCriteria animeEpisodeSpec)
            {
                return IsSatisfiedBy(remoteEpisode, animeEpisodeSpec);
            }

            return DownloadSpecDecision.Accept();
        }

        private DownloadSpecDecision IsSatisfiedBy(RemoteEpisode remoteEpisode, SingleEpisodeSearchCriteria singleEpisodeSpec)
        {
            var seasonNumbers = remoteEpisode.ParsedEpisodeInfo.SeasonNumbers;

            var seasonMatches = seasonNumbers.Count > 0
                ? seasonNumbers.Contains(singleEpisodeSpec.SeasonNumber)
                : singleEpisodeSpec.SeasonNumber == remoteEpisode.MappedSeasonNumber;

            if (!seasonMatches)
            {
                _logger.Debug("Season number does not match searched season number, skipping.");
                return DownloadSpecDecision.Reject(DownloadRejectionReason.WrongSeason, "Wrong season");
            }

            if (!remoteEpisode.ParsedEpisodeInfo.EpisodeNumbers.Any())
            {
                if (CanFillMissingSearchedEpisode(remoteEpisode, singleEpisodeSpec))
                {
                    return DownloadSpecDecision.Accept();
                }

                _logger.Debug("Full season result during single episode search, skipping.");
                return DownloadSpecDecision.Reject(DownloadRejectionReason.FullSeason, "Full season pack");
            }

            if (!remoteEpisode.ParsedEpisodeInfo.EpisodeNumbers.Contains(singleEpisodeSpec.EpisodeNumber))
            {
                _logger.Debug("Episode number does not match searched episode number, skipping.");
                return DownloadSpecDecision.Reject(DownloadRejectionReason.WrongEpisode, "Wrong episode");
            }

            return DownloadSpecDecision.Accept();
        }

        private DownloadSpecDecision IsSatisfiedBy(RemoteEpisode remoteEpisode, AnimeEpisodeSearchCriteria animeEpisodeSpec)
        {
            if (remoteEpisode.ParsedEpisodeInfo.FullSeason && !animeEpisodeSpec.IsSeasonSearch)
            {
                if (CanFillMissingSearchedEpisode(remoteEpisode, animeEpisodeSpec))
                {
                    return DownloadSpecDecision.Accept();
                }

                _logger.Debug("Full season result during single episode search, skipping.");
                return DownloadSpecDecision.Reject(DownloadRejectionReason.FullSeason, "Full season pack");
            }

            return DownloadSpecDecision.Accept();
        }

        private static bool CanFillMissingSearchedEpisode(RemoteEpisode remoteEpisode, SearchCriteriaBase criteria)
        {
            var profile = remoteEpisode.Series?.QualityProfile?.Value;
            return remoteEpisode.ParsedEpisodeInfo.IsMultiSeason &&
                   profile?.AllowMultiSeasonPacks == true && !profile.UpgradeAllowed &&
                   remoteEpisode.Episodes.Any(e => e.Monitored && e.EpisodeFileId == 0 && criteria.Episodes.Any(s => s.Id == e.Id));
        }
    }
}
