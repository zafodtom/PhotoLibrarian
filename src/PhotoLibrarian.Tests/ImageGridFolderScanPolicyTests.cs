using PhotoLibrarian.Core.Models;
using PhotoLibrarian.Core.Services;
using Xunit;

namespace PhotoLibrarian.Tests;

public sealed class ImageGridFolderScanPolicyTests
{
    [Fact]
    public async Task AlbumScanner_ExcludesInternalRollbackAndNestedAlbumImages()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "PL-grid-scan-" + Guid.NewGuid().ToString("N"));
        var internalDir = Path.Combine(root, ".album", "jpeg-conversion-rollback", "old");
        var nested = Path.Combine(root, "nested");
        Directory.CreateDirectory(internalDir);
        Directory.CreateDirectory(Path.Combine(nested, ".album"));
        try
        {
            File.WriteAllBytes(Path.Combine(root, "visible.jpg"), [1, 2]);
            File.WriteAllBytes(Path.Combine(internalDir, "hidden.jpg"), [1, 2]);
            File.WriteAllBytes(Path.Combine(nested, "foreign.jpg"), [1, 2]);
            using var scanner = new FolderScannerService();
            var paths = new List<string>();
            await foreach (var path in scanner.ScanFolderAsync(root))
                paths.Add(path);
            Assert.Single(paths);
            Assert.Equal(Path.Combine(root, "visible.jpg"), paths[0]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }


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
            new ImageEntry { Id = 11, FilePath = @"C:\Album\kept.jpg", FileName = "kept.jpg" },
            new ImageEntry { Id = 12, FilePath = @"C:\Album\excluded.jpg", FileName = "excluded.jpg" }
        };

        var knownPaths = ImageGridFolderScanPolicy.GetIndexedPaths(indexed);

        Assert.Contains(@"c:\album\EXCLUDED.jpg", knownPaths);
        Assert.Equal(2, knownPaths.Count);
    }
}
