using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.Qualities;
using Sonarr.Http.REST;

namespace Sonarr.Api.V3.Profiles.Quality
{
    public class QualityProfileResource : RestResource
    {
        public string Name { get; set; }
        public bool UpgradeAllowed { get; set; }
        public bool? AllowMultiSeasonPacks { get; set; }
        public int Cutoff { get; set; }
        public List<QualityProfileQualityItemResource> Items { get; set; }
        public int MinFormatScore { get; set; }
        public int CutoffFormatScore { get; set; }
        public int MinUpgradeFormatScore { get; set; }
        public List<ProfileFormatItemResource> FormatItems { get; set; }
    }

    public class QualityProfileQualityItemResource : RestResource
    {
        private double? _minSize;
        private double? _maxSize;
        private double? _preferredSize;

        public string Name { get; set; }
        public NzbDrone.Core.Qualities.Quality Quality { get; set; }
        public List<QualityProfileQualityItemResource> Items { get; set; }
        public bool Allowed { get; set; }
        public double? MinSize
        {
            get => _minSize;
            set
            {
                _minSize = value;
                MinSizeSpecified = true;
            }
        }

        public double? MaxSize
        {
            get => _maxSize;
            set
            {
                _maxSize = value;
                MaxSizeSpecified = true;
            }
        }

        public double? PreferredSize
        {
            get => _preferredSize;
            set
            {
                _preferredSize = value;
                PreferredSizeSpecified = true;
            }
        }

        [JsonIgnore]
        public bool MinSizeSpecified { get; private set; }

        [JsonIgnore]
        public bool MaxSizeSpecified { get; private set; }

        [JsonIgnore]
        public bool PreferredSizeSpecified { get; private set; }

        public QualityProfileQualityItemResource()
        {
            Items = new List<QualityProfileQualityItemResource>();
        }
    }

    public class ProfileFormatItemResource : RestResource
    {
        public int Format { get; set; }
        public string Name { get; set; }
        public int Score { get; set; }
    }

    public static class ProfileResourceMapper
    {
        public static QualityProfileResource ToResource(this QualityProfile model)
        {
            if (model == null)
            {
                return null;
            }

            return new QualityProfileResource
            {
                Id = model.Id,
                Name = model.Name,
                UpgradeAllowed = model.UpgradeAllowed && !model.AllowMultiSeasonPacks,
                AllowMultiSeasonPacks = model.AllowMultiSeasonPacks,
                Cutoff = model.Cutoff,
                Items = model.Items.ConvertAll(ToResource),
                MinFormatScore = model.MinFormatScore,
                CutoffFormatScore = model.CutoffFormatScore,
                MinUpgradeFormatScore = model.MinUpgradeFormatScore,
                FormatItems = model.FormatItems.ConvertAll(ToResource)
            };
        }

        public static QualityProfileQualityItemResource ToResource(this QualityProfileQualityItem model)
        {
            if (model == null)
            {
                return null;
            }

            return new QualityProfileQualityItemResource
            {
                Id = model.Id,
                Name = model.Name,
                Quality = model.Quality,
                Items = model.Items.ConvertAll(ToResource),
                Allowed = model.Allowed,
                MinSize = model.MinSize,
                MaxSize = model.MaxSize,
                PreferredSize = model.PreferredSize
            };
        }

        public static ProfileFormatItemResource ToResource(this ProfileFormatItem model)
        {
            return new ProfileFormatItemResource
            {
                Format = model.Format.Id,
                Name = model.Format.Name,
                Score = model.Score
            };
        }

        public static void PreserveUnspecifiedSizeLimits(this QualityProfileResource resource, QualityProfile existing)
        {
            var previous = existing.Items.SelectMany(FlattenItems).ToList();
            PreserveSizeLimits(resource.Items, previous);
        }

        public static QualityProfile ToModel(this QualityProfileResource resource)
        {
            if (resource == null)
            {
                return null;
            }

            return new QualityProfile
            {
                Id = resource.Id,
                Name = resource.Name,
                UpgradeAllowed = resource.UpgradeAllowed && resource.AllowMultiSeasonPacks != true,
                AllowMultiSeasonPacks = resource.AllowMultiSeasonPacks == true,
                Cutoff = resource.Cutoff,
                Items = resource.Items.ConvertAll(ToModel),
                MinFormatScore = resource.MinFormatScore,
                CutoffFormatScore = resource.CutoffFormatScore,
                MinUpgradeFormatScore = resource.MinUpgradeFormatScore,
                FormatItems = resource.FormatItems.ConvertAll(ToModel)
            };
        }

        public static QualityProfileQualityItem ToModel(this QualityProfileQualityItemResource resource)
        {
            if (resource == null)
            {
                return null;
            }

            return new QualityProfileQualityItem
            {
                Id = resource.Id,
                Name = resource.Name,
                Quality = resource.Quality != null ? (NzbDrone.Core.Qualities.Quality)resource.Quality.Id : null,
                Items = resource.Items.ConvertAll(ToModel),
                Allowed = resource.Allowed,
                MinSize = resource.MinSize,
                MaxSize = resource.MaxSize,
                PreferredSize = resource.PreferredSize
            };
        }

        public static ProfileFormatItem ToModel(this ProfileFormatItemResource resource)
        {
            return new ProfileFormatItem
            {
                Format = new CustomFormat { Id = resource.Format },
                Score = resource.Score
            };
        }

        public static List<QualityProfileResource> ToResource(this IEnumerable<QualityProfile> models)
        {
            return models.Select(ToResource).ToList();
        }

        private static IEnumerable<QualityProfileQualityItem> FlattenItems(QualityProfileQualityItem item)
        {
            yield return item;
            foreach (var child in item.Items.SelectMany(FlattenItems))
            {
                yield return child;
            }
        }

        private static void PreserveSizeLimits(
            IEnumerable<QualityProfileQualityItemResource> items,
            List<QualityProfileQualityItem> previous)
        {
            foreach (var item in items)
            {
                var existing = previous.FirstOrDefault(p => item.Quality != null
                    ? p.Quality?.Id == item.Quality.Id
                    : p.Quality == null && p.Id == item.Id);

                if (existing != null)
                {
                    if (!item.MinSizeSpecified)
                    {
                        item.MinSize = existing.MinSize;
                    }

                    if (!item.MaxSizeSpecified)
                    {
                        item.MaxSize = existing.MaxSize;
                    }

                    if (!item.PreferredSizeSpecified)
                    {
                        item.PreferredSize = existing.PreferredSize;
                    }
                }

                PreserveSizeLimits(item.Items, previous);
            }
        }
    }
}
