using System.Linq;
using NLog;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    public class MultiSeasonSpecification : IDownloadDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public MultiSeasonSpecification(Logger logger)
        {
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public virtual DownloadSpecDecision IsSatisfiedBy(RemoteEpisode subject, ReleaseDecisionInformation information)
        {
            if (subject.ParsedEpisodeInfo.IsMultiSeason)
            {
                var profile = subject.Series.QualityProfile?.Value;

                if (profile?.AllowMultiSeasonPacks != true || profile.UpgradeAllowed)
                {
                    _logger.Debug("Automatic multi-season packs are disabled for this quality profile");
                    return DownloadSpecDecision.Reject(DownloadRejectionReason.MultiSeason, "Enable multi-season packs in the quality profile with upgrades disabled");
                }

                if (!subject.Series.Monitored || subject.Episodes.Any(e => !e.Monitored))
                {
                    return DownloadSpecDecision.Reject(DownloadRejectionReason.EpisodeNotMonitored, "All episodes in a multi-season pack must be monitored");
                }

                if (subject.UnmappedSeasonNumbers.Any())
                {
                    return DownloadSpecDecision.Reject(DownloadRejectionReason.MultiSeason, "Some seasons in this pack could not be matched to episodes: {0}", string.Join(", ", subject.UnmappedSeasonNumbers));
                }

                if (!subject.Episodes.Any(e => e.EpisodeFileId == 0))
                {
                    return DownloadSpecDecision.Reject(DownloadRejectionReason.DiskUpgradesNotAllowed, "Multi-season packs only fill missing episodes; existing files are kept");
                }
            }

            return DownloadSpecDecision.Accept();
        }
    }
}
