using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.CompletedDownloadServiceTests
{
    [TestFixture]
    public class PackRemovalFixture : CoreTest<CompletedDownloadService>
    {
        private TrackedDownload _download;
        private RemoteEpisode _current;
        private List<EpisodeFile> _files;

        [SetUp]
        public void Setup()
        {
            var series = new Series
            {
                Id = 1,
                Path = @"C:\Library\Example".AsOsAgnostic(),
                QualityProfile = new QualityProfile { AllowMultiSeasonPacks = true }
            };
            _current = new RemoteEpisode
            {
                Series = series,
                ParsedEpisodeInfo = Parser.Parser.ParseTitle("Example.S01-S03.1080p.BluRay"),
                Episodes = Enumerable.Range(1, 3).Select(s => new Episode { Id = s, SeasonNumber = s, EpisodeNumber = 1, EpisodeFileId = s }).ToList()
            };
            _files = _current.Episodes.Select(e => new EpisodeFile { Id = e.EpisodeFileId, RelativePath = $"Example.S{e.SeasonNumber:00}E01.mkv" }).ToList();
            _download = new TrackedDownload
            {
                State = TrackedDownloadState.Imported,
                DownloadItem = new DownloadClientItem { Title = "Example.S01-S03.1080p.BluRay", CanBeRemoved = true },
                RemoteEpisode = _current
            };
            Mocker.GetMock<ISeriesService>().Setup(s => s.GetSeries(series.Id)).Returns(series);
            Mocker.GetMock<IParsingService>().Setup(s => s.Map(It.IsAny<ParsedEpisodeInfo>(), series)).Returns(() => _current);
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFiles(It.IsAny<IEnumerable<int>>())).Returns(() => _files);
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(It.IsAny<string>())).Returns(true);
        }

        [Test]
        public void should_allow_removal_when_every_episode_has_a_real_library_file()
        {
            Subject.CanRemove(_download).Should().BeTrue();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_retain_legacy_imported_pack_with_missing_seasons(bool enabled)
        {
            _current.Series.QualityProfile.Value.AllowMultiSeasonPacks = enabled;
            _current.Episodes[2].EpisodeFileId = 0;

            Subject.CanRemove(_download).Should().BeFalse();
            _download.Status.Should().Be(TrackedDownloadStatus.Warning);
        }

        [Test]
        public void should_retain_pack_if_a_file_has_disappeared_from_disk()
        {
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(Path.Combine(_current.Series.Path, _files.Last().RelativePath))).Returns(false);

            Subject.CanRemove(_download).Should().BeFalse();
        }

        [Test]
        public void should_retain_pack_if_a_season_has_no_metadata()
        {
            _current.UnmappedSeasonNumbers = [4];

            Subject.CanRemove(_download).Should().BeFalse();
        }

        [Test]
        public void should_retain_pack_if_mapping_returns_no_episodes()
        {
            _current.Episodes.Clear();

            Subject.CanRemove(_download).Should().BeFalse();
        }

        [Test]
        public void should_retain_pack_if_series_was_removed()
        {
            Mocker.GetMock<ISeriesService>().Setup(s => s.GetSeries(1)).Returns((Series)null);

            Subject.CanRemove(_download).Should().BeFalse();
        }

        [Test]
        public void should_retain_pack_if_disk_check_fails()
        {
            Mocker.GetMock<IDiskProvider>().Setup(s => s.FileExists(It.IsAny<string>())).Throws(new IOException("Disk unavailable"));

            Subject.CanRemove(_download).Should().BeFalse();
        }

        [Test]
        public void should_use_current_mapping_instead_of_cached_completion()
        {
            _current = new RemoteEpisode
            {
                Series = _download.RemoteEpisode.Series,
                Episodes = [new Episode { Id = 4, SeasonNumber = 3, EpisodeNumber = 2 }]
            };

            Subject.CanRemove(_download).Should().BeFalse();
        }

        [Test]
        public void should_check_single_season_packs_when_missing_episode_mode_is_enabled()
        {
            _current.ParsedEpisodeInfo.SeasonNumbers = [1];
            _current.Episodes[0].EpisodeFileId = 0;

            Subject.CanRemove(_download).Should().BeFalse();
        }

        [Test]
        public void should_leave_ordinary_single_season_cleanup_unchanged()
        {
            _current.ParsedEpisodeInfo.SeasonNumbers = [1];
            _current.Series.QualityProfile.Value.AllowMultiSeasonPacks = false;
            _current.Episodes[0].EpisodeFileId = 0;

            Subject.CanRemove(_download).Should().BeTrue();
            Mocker.GetMock<IParsingService>().Verify(s => s.Map(It.IsAny<ParsedEpisodeInfo>(), It.IsAny<Series>()), Times.Never());
        }
    }
}
