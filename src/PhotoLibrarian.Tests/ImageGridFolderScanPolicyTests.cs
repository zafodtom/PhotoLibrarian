using PhotoLibrarian.Core.Models;
using PhotoLibrarian.Core.Services;
using Xunit;

namespace PhotoLibrarian.Tests;

public sealed class ImageGridFolderScanPolicyTests
{
    [Theory]
    [InlineData(true, true, false, false, true)]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, true, true, false, false)]
    [InlineData(true, true, true, true, false)]
    [InlineData(true, false, false, false, false)]
    [InlineData(false, true, false, false, false)]
    public void ShouldScanForMissingFiles_AvoidsTagFilteredDiskPlaceholders(
        bool requested, bool folder, bool inclusive, bool excluded, bool expected)
    {
        Assert.Equal(expected, ImageGridFolderScanPolicy.ShouldScanForMissingFiles(
            requested, folder, inclusive, excluded));
    }

    [Fact]
    public void GetIndexedPaths_UsesAllDatabaseRecordsEvenIfExcludedFromVisibleGrid()
    {
        var indexed = new[]
        {
            new ImageEntry { Id = 11, FilePath = @"C:\Album\kept.jpg" },
            new ImageEntry { Id = 12, FilePath = @"C:\Album\excluded.jpg" }
        };

        var knownPaths = ImageGridFolderScanPolicy.GetIndexedPaths(indexed);

        Assert.Contains(@"c:\album\EXCLUDED.jpg", knownPaths);
        Assert.Equal(2, knownPaths.Count);
    }
}
