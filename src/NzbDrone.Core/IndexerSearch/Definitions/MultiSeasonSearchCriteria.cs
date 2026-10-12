namespace NzbDrone.Core.IndexerSearch.Definitions
{
    public class MultiSeasonSearchCriteria : SearchCriteriaBase
    {
        public override string ToString()
        {
            return $"[{Series.Title} : multi-season packs]";
        }
    }
}
