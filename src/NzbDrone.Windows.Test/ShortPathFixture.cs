using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Test.Common;

namespace NzbDrone.Windows.Test
{
    [TestFixture]
    [Platform("Win")]
    public class ShortPathFixture : TestBase
    {
        private string _root;
        private string _seriesPath;
        private string _episodePath;

        [SetUp]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(), "Sonarr short path test " + Guid.NewGuid()).CleanFilePath();
            _seriesPath = Path.Combine(_root, "Series Title");
            _episodePath = Path.Combine(_seriesPath, "Season 01", "Series Title S01E01.mkv");
            Directory.CreateDirectory(Path.GetDirectoryName(_episodePath));
            File.WriteAllText(_episodePath, "episode");
        }

        [TearDown]
        public void Cleanup()
        {
            DeleteTempFolder(_root);
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void should_preserve_relative_episode_path_between_short_and_long_names(bool shortParent, bool shortChild)
        {
            var parent = shortParent ? ToShortPath(_seriesPath) : _seriesPath;
            var child = shortChild ? ToShortPath(_episodePath) : _episodePath;

            parent.IsParentPath(child).Should().BeTrue();
            parent.GetRelativePath(child).Should().Be(Path.Combine("Season 01", "Series Title S01E01.mkv"));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_not_treat_a_sibling_as_part_of_the_series(bool shortParent)
        {
            var sibling = Path.Combine(_root, "Series Title Extra");
            Directory.CreateDirectory(sibling);
            var parent = shortParent ? ToShortPath(_seriesPath) : _seriesPath;
            var child = Path.Combine(shortParent ? sibling : ToShortPath(sibling), "S01E01.mkv");

            parent.IsParentPath(child).Should().BeFalse();
            Action getRelativePath = () => parent.GetRelativePath(child);
            getRelativePath.Should().Throw<NotParentException>();
        }

        [Test]
        public void should_resolve_a_destination_before_the_file_exists()
        {
            var destination = Path.Combine(ToShortPath(_seriesPath), "Season 02", "Series Title S02E01.mkv");

            _seriesPath.IsParentPath(destination).Should().BeTrue();
            _seriesPath.GetRelativePath(destination).Should().Be(Path.Combine("Season 02", "Series Title S02E01.mkv"));
        }

        [Test]
        public void should_preserve_a_literal_tilde_in_a_filename()
        {
            var destination = Path.Combine(_seriesPath, "Season 01", "Series ~ S01E02.mkv");

            _seriesPath.IsParentPath(destination).Should().BeTrue();
            _seriesPath.GetRelativePath(destination).Should().Be(Path.Combine("Season 01", "Series ~ S01E02.mkv"));
        }

        [Test]
        public void should_preserve_unix_paths_containing_a_tilde()
        {
            const string parent = "/remote/Series ~ Title";
            const string child = "/remote/Series ~ Title/Season 01/S01E01.mkv";

            parent.IsParentPath(child).Should().BeTrue();
            parent.GetRelativePath(child).Should().Be("Season 01/S01E01.mkv");
        }

        private static string ToShortPath(string path)
        {
            var buffer = new char[32768];
            var length = GetShortPathName(path, buffer, (uint)buffer.Length);
            if (length == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            length.Should().BeLessThan((uint)buffer.Length);
            var shortPath = new string(buffer, 0, (int)length);
            if (string.Equals(shortPath, path, StringComparison.OrdinalIgnoreCase))
            {
                Assert.Ignore("This volume does not provide short file names.");
            }

            return shortPath;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetShortPathName(string path, [Out] char[] buffer, uint size);
    }
}
