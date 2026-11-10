using PhotoLibrarian.Core.Models;
using PhotoLibrarian.Core.Services;
using Xunit;

namespace PhotoLibrarian.Tests;

public sealed class ThumbnailCacheCleanupTests
{
    [Fact]
    public async Task PhotoDeletionRemovesOnlyCapturedCacheFiles()
    {
        var album = Path.Combine(Path.GetTempPath(), "pl-cache-delete-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(album);
        try
        {
            WindowsThumbnailService.ConfigureAlbumCache(album);
            var image = Path.Combine(album, "test.jpg");
            File.WriteAllBytes(image, [1, 2, 3, 4]);
            var keys = WindowsThumbnailService.CaptureThumbnailPathsForDeletion(image);
            Assert.Equal(2, keys.Length);
            foreach (var key in keys)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(key)!);
                File.WriteAllBytes(key, [5, 6, 7]);
            }

            // Simulate a successful move to the Windows Recycle Bin.
            File.Delete(image);
            var outside = Path.Combine(album, "do-not-delete.bin");
            File.WriteAllText(outside, "retain");
            await WindowsThumbnailService.DeleteCapturedThumbnailsAsync(
                keys.Append(outside));
            Assert.All(keys, key => Assert.False(File.Exists(key)));
            Assert.True(File.Exists(outside));
        }
        finally
        {
            WindowsThumbnailService.ConfigureAlbumCache(null);
            Directory.Delete(album, recursive: true);
        }
    }

    [Fact]
    public async Task OrphanSweepKeepsLiveCacheAndDeletesUnknownHashes()
    {
        var album = Path.Combine(Path.GetTempPath(), "pl-cache-sweep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(album);
        try
        {
            WindowsThumbnailService.ConfigureAlbumCache(album);
            var image = Path.Combine(album, "live.jpg");
            File.WriteAllBytes(image, [1, 2, 3]);
            var liveKeys = WindowsThumbnailService.CaptureThumbnailPathsForDeletion(image);
            Assert.Equal(2, liveKeys.Length);
            var liveKey = liveKeys[0];
            Directory.CreateDirectory(Path.GetDirectoryName(liveKey)!);
            File.WriteAllBytes(liveKey, [4, 5]);

            var orphanKey = Path.Combine(WindowsThumbnailService.CacheDirectory,
                "EE", new string('E', 64) + ".bin");
            Directory.CreateDirectory(Path.GetDirectoryName(orphanKey)!);
            File.WriteAllBytes(orphanKey, [9, 9]);

            await WindowsThumbnailService.PruneOrphanedThumbnailsAsync([
                new ImageEntry
                {
                    FilePath = image,
                    FileName = "live.jpg",
                    DateModified = File.GetLastWriteTimeUtc(image),
                    FileSize = new FileInfo(image).Length
                }
            ]);

            Assert.True(File.Exists(liveKey));
            Assert.False(File.Exists(orphanKey));
            Assert.True(File.Exists(image));
        }
        finally
        {
            WindowsThumbnailService.ConfigureAlbumCache(null);
            Directory.Delete(album, recursive: true);
        }
    }
}
