using PhotoLibrarian.Core.Services;
using Xunit;

namespace PhotoLibrarian.Tests;

public sealed class AlbumInternalExclusionTests
{
    [Fact]
    public async Task BackupsAreNeverDiscoveredEvenWhenScannedDirectly()
    {
        var album = Path.Combine(Path.GetTempPath(), "PL-private-" + Guid.NewGuid().ToString("N"));
        var normal = Path.Combine(album, "photos");
        var backup = Path.Combine(album, ".album", "jpeg-conversion-backups", "run-001");
        var reset = Path.Combine(album, ".album", "metadata-reset-backups", "run-002");
        Directory.CreateDirectory(normal);
        Directory.CreateDirectory(backup);
        Directory.CreateDirectory(reset);
        try
        {
            var original = Path.Combine(normal, "portrait.jpg");
            var older = Path.Combine(backup, "portrait.jpg");
            var copy = Path.Combine(reset, "portrait.jpg");
            await File.WriteAllBytesAsync(original, [1, 2]);
            await File.WriteAllBytesAsync(older, [1, 2]);
            await File.WriteAllBytesAsync(copy, [1, 2]);

            using var scanner = new FolderScannerService();
            var files = new List<string>();
            await foreach (var file in scanner.ScanFolderAsync(album))
                files.Add(file);
            Assert.Equal([original], files);
            await foreach (var file in scanner.ScanFolderAsync(Path.Combine(album, ".album")))
                Assert.Fail("Internal metadata scans must not yield any files");

            Assert.True(FolderScannerService.IsPathOwnedByAlbumRoot(album, original));
            Assert.False(FolderScannerService.IsPathOwnedByAlbumRoot(album, older));
            Assert.False(FolderScannerService.IsPathOwnedByAlbumRoot(album, copy));
            Assert.False(FolderScannerService.IsPathOwnedByAlbumRoot(album, backup));
            Assert.True(FolderScannerService.ContainsAlbumMetadataComponent(older));
            Assert.True(FolderScannerService.ContainsAlbumMetadataComponent(copy));
        }
        finally
        {
            Directory.Delete(album, recursive: true);
        }
    }

    [Fact]
    public void ExclusionIsCaseInsensitiveButNotANamePrefixMatch()
    {
        var root = Path.Combine(Path.GetTempPath(), "PL-private-" + Guid.NewGuid().ToString("N"));
        var backup = Path.Combine(root, ".ALBUM", "jpeg-conversion-work", "photo.png");
        var similar = Path.Combine(root, ".album-old", "photo.png");
        Assert.True(FolderScannerService.ContainsAlbumMetadataComponent(backup));
        Assert.False(FolderScannerService.IsPathOwnedByAlbumRoot(root, backup));
        Assert.False(FolderScannerService.ContainsAlbumMetadataComponent(similar));
        Assert.True(FolderScannerService.IsPathOwnedByAlbumRoot(root, similar));
    }
}
