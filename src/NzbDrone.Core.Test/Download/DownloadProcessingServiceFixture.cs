using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class DownloadProcessingServiceFixture : CoreTest<DownloadProcessingService>
    {
        [TestCase(true)]
        [TestCase(false)]
        public void should_check_completed_pack_before_publishing_removal(bool safeToRemove)
        {
            var download = new TrackedDownload
            {
                State = TrackedDownloadState.Imported,
                IsTrackable = false,
                DownloadItem = new DownloadClientItem { CanBeRemoved = true }
            };
            Mocker.GetMock<ITrackedDownloadService>().Setup(s => s.GetTrackedDownloads()).Returns([download]);
            Mocker.GetMock<ICompletedDownloadService>().Setup(s => s.CanRemove(download)).Returns(safeToRemove);

            Subject.Execute(new ProcessMonitoredDownloadsCommand());

            Mocker.GetMock<IEventAggregator>().Verify(s => s.PublishEvent(It.IsAny<DownloadCanBeRemovedEvent>()), safeToRemove ? Times.Once() : Times.Never());
            download.State.Should().Be(TrackedDownloadState.Imported);
        }
    }
}
