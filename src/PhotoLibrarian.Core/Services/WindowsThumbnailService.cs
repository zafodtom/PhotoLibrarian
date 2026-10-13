using Windows.Storage;
using Windows.Storage.FileProperties;
using System.Runtime.InteropServices.WindowsRuntime;

namespace PhotoLibrarian.Core.Services;

/// <summary>
/// Uses Windows.Storage thumbnail provider (same as File Explorer) for fast thumbnail generation.
/// This is what Windows Explorer uses internally and is highly optimized.
/// </summary>
public static class WindowsThumbnailService
{
    // Portable, regenerable album cache. The key includes path, size, file length
    // and modification timestamp so stale thumbnails are never reused after edits.
    private static string? _albumCacheDirectory;
    private const long MaxCacheBytes = 1024L * 1024 * 1024;
    private static long _cacheHits;
    private static long _cacheMisses;
    private static long _cacheWrites;
    public static (long Hits, long Misses, long Writes) CacheStatistics =>
        (Interlocked.Read(ref _cacheHits),
         Interlocked.Read(ref _cacheMisses),
         Interlocked.Read(ref _cacheWrites));


    public static void ConfigureAlbumCache(string? albumRoot)
    {
        _albumCacheDirectory = string.IsNullOrWhiteSpace(albumRoot)
            ? null
            : Path.Combine(albumRoot, ".album", "thumbnails");
        if (_albumCacheDirectory is not null)
        {
            try
            {
                Directory.CreateDirectory(_albumCacheDirectory);
                _ = Task.Run(TrimCache);
            }
            catch (IOException) { _albumCacheDirectory = null; }
            catch (UnauthorizedAccessException) { _albumCacheDirectory = null; }
        }
    }

    private static string? GetCachePath(string filePath, int size)
    {
        if (_albumCacheDirectory is null) return null;
        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists) return null;
            var key = $"{Path.GetFullPath(filePath).ToUpperInvariant()}|{size}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(key)));
            return Path.Combine(_albumCacheDirectory, hash[..2], hash + ".bin");
        }
        catch { return null; }
    }

    private static void TrimCache()
    {
        var root = _albumCacheDirectory;
        if (root is null || !Directory.Exists(root)) return;
        try
        {
            var files = Directory.EnumerateFiles(root, "*.bin", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToList();
            long bytes = 0;
            foreach (var file in files)
            {
                bytes += file.Length;
                if (bytes <= MaxCacheBytes) continue;
                try { file.Delete(); } catch (IOException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Gets thumbnail as encoded bytes (PNG/BMP from cache). Much faster than pixel decode.
    /// Use this for BitmapImage.SetSourceAsync() which can decode the stream directly.
    /// </summary>
    public static async Task<byte[]?> GetThumbnailStreamAsync(string filePath, int size)
    {
        var cachePath = GetCachePath(filePath, size);
        if (cachePath is not null)
        {
            try
            {
                if (File.Exists(cachePath))
                {
                    var cached = await File.ReadAllBytesAsync(cachePath);
                    if (cached.Length > 0)
                    {
                        Interlocked.Increment(ref _cacheHits);
                        return cached;
                    }
                }
            }
            catch (IOException) { /* regenerate */ }
        }

        Interlocked.Increment(ref _cacheMisses);
        byte[]? created = await GenerateThumbnailBytesAsync(filePath, size);
        if (created is { Length: > 0 } && cachePath is not null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                var temporary = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllBytesAsync(temporary, created);
                    File.Move(temporary, cachePath, overwrite: true);
                    if (Interlocked.Increment(ref _cacheWrites) % 200 == 0)
                        _ = Task.Run(TrimCache);
                }
                finally
                {
                    try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
                }
            }
            catch (IOException) { /* disk cache is optional */ }
            catch (UnauthorizedAccessException) { }
        }
        return created;
    }

    private static async Task<byte[]?> GenerateThumbnailBytesAsync(string filePath, int size)
    {
        if (HeifFallbackDecoder.IsHeifFamily(filePath))
        {
            try
            {
                return await HeifFallbackDecoder.DecodeToJpegAsync(filePath, size);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HEIF thumbnail decode failed for {System.IO.Path.GetFileName(filePath)}: {ex.Message}");
                return null;
            }
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(filePath);
            
            // Request cached thumbnail - UseCurrentScale avoids DPI scaling overhead
            using var thumb = await file.GetThumbnailAsync(
                ThumbnailMode.SingleItem,
                (uint)size,
                ThumbnailOptions.UseCurrentScale);
            
            if (thumb == null || thumb.Size == 0)
            {
                if (HeifFallbackDecoder.IsHeifFamily(filePath))
                    return await HeifFallbackDecoder.DecodeToJpegAsync(filePath, size);

                return null;
            }
            
            // Return the encoded stream bytes (PNG/BMP from cache)
            var bytes = new byte[thumb.Size];
            await thumb.AsStreamForRead().ReadAsync(bytes, 0, bytes.Length);
            return bytes;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WindowsThumbnailService.GetThumbnailStreamAsync failed for {System.IO.Path.GetFileName(filePath)}: {ex.Message}");

            if (HeifFallbackDecoder.IsHeifFamily(filePath))
            {
                try
                {
                    return await HeifFallbackDecoder.DecodeToJpegAsync(filePath, size);
                }
                catch (Exception fallbackEx)
                {
                    System.Diagnostics.Debug.WriteLine($"HEIF thumbnail fallback failed for {System.IO.Path.GetFileName(filePath)}: {fallbackEx.Message}");
                }
            }

            return null;
        }
    }
    
    /// <summary>
    /// Gets thumbnail as decoded BGRA8 pixels. Use this for WriteableBitmap.
    /// </summary>
    public static async Task<(byte[] pixels, int width, int height)?> GenerateThumbnailPixelsAsync(string filePath, int size)
    {
        try
        {
            // Use WinRT API which leverages Windows thumbnail cache (what Explorer uses)
            var file = await StorageFile.GetFileFromPathAsync(filePath);
            
            // Request thumbnail - use cache if available, generate if needed
            // UseCurrentScale = don't apply DPI scaling
            // ReturnOnlyIfCached would fail if no cache exists, so we omit it
            using var thumb = await file.GetThumbnailAsync(
                ThumbnailMode.SingleItem,
                (uint)size,
                ThumbnailOptions.UseCurrentScale);
            
            if (thumb == null || thumb.Size == 0)
                return null;
            
            // Read the stream directly - thumbnail cache already gives us decoded bitmap data
            var bytes = new byte[thumb.Size];
            await thumb.AsStreamForRead().ReadAsync(bytes, 0, bytes.Length);
            
            // Decode to BGRA8 pixels
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);
            
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
            var pixelData = await decoder.GetPixelDataAsync(
                Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                new Windows.Graphics.Imaging.BitmapTransform(),
                Windows.Graphics.Imaging.ExifOrientationMode.RespectExifOrientation,
                Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);
            
            var pixels = pixelData.DetachPixelData();
            return (pixels, (int)decoder.PixelWidth, (int)decoder.PixelHeight);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WindowsThumbnailService failed for {System.IO.Path.GetFileName(filePath)}: {ex.Message}");
            return null;
        }
    }
}
