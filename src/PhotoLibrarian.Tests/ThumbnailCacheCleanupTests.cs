using PhotoLibrarian.Core.Models;
using PhotoLibrarian.Core.Services;
using Xunit;

namespace PhotoLibrarian.Tests;

public sealed class ThumbnailCacheCleanupTests
{
    [Fact]
    public async Task Backfill_SkipsAlreadyCachedImagesAndInternalAlbumBackups()
    {
        var album = Path.Combine(Path.GetTempPath(),
            "pl-backfill-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(album, ".album"));
        try
        {
            var photo = Path.Combine(album, "cached.jpg");
            await WicTestImage.CreateAsync(photo, 10, 8, jpeg: true);
            var rollbackDir = Path.Combine(album, ".album", "backup");
            Directory.CreateDirectory(rollbackDir);
            await WicTestImage.CreateAsync(
                Path.Combine(rollbackDir, "rollback.jpg"), 10, 8, jpeg: true);
            WindowsThumbnailService.ConfigureAlbumCache(album);

            var key = WindowsThumbnailService.CaptureThumbnailPathsForDeletion(photo)[1];
            Directory.CreateDirectory(Path.GetDirectoryName(key)!);
            await File.WriteAllBytesAsync(key, [1, 2, 3]);

            var result = await WindowsThumbnailService.GenerateMissingAlbumThumbnailsAsync(album);
            Assert.Equal(1, result.Total);
            Assert.Equal(1, result.AlreadyCached);
            Assert.Equal(0, result.Generated);
            Assert.Equal(0, result.Failed);
        }
        finally
        {
            WindowsThumbnailService.ConfigureAlbumCache(null);
            Directory.Delete(album, recursive: true);
        }
    }

    [Fact]
    public async Task Backfill_RejectsCacheConfiguredForDifferentAlbum()
    {
        var first = Path.Combine(Path.GetTempPath(),
            "pl-backfill-a-" + Guid.NewGuid().ToString("N"));
        var second = Path.Combine(Path.GetTempPath(),
            "pl-backfill-b-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(first, ".album"));
        Directory.CreateDirectory(Path.Combine(second, ".album"));
        try
        {
            WindowsThumbnailService.ConfigureAlbumCache(first);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                WindowsThumbnailService.GenerateMissingAlbumThumbnailsAsync(second));
        }
        finally
        {
            WindowsThumbnailService.ConfigureAlbumCache(null);
            Directory.Delete(first, recursive: true);
            Directory.Delete(second, recursive: true);
        }
    }


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
