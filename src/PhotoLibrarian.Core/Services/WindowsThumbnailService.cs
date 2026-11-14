using Windows.Storage;
using Windows.Storage.FileProperties;
using System.Runtime.InteropServices.WindowsRuntime;

namespace PhotoLibrarian.Core.Services;

/// <summary>
/// Uses Windows.Storage thumbnail provider (same as File Explorer) for fast thumbnail generation.
/// This is what Windows Explorer uses internally and is highly optimized.
/// </summary>
public sealed record AlbumThumbnailProgress(
    int Processed, int Total, int Generated, int AlreadyCached, int Failed, string FileName);
public sealed record AlbumThumbnailResult(int Total, int Generated, int AlreadyCached, int Failed);

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
            catch (IOException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[THUMBNAILS] Cache initialization failed: {ex}");
                _albumCacheDirectory = null;
            }
            catch (UnauthorizedAccessException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[THUMBNAILS] Cache access denied: {ex}");
                _albumCacheDirectory = null;
            }
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

    /// <summary>
    /// Scans the active album with the shared album-boundary rules and fills
    /// only missing portable grid thumbnails. No source image is modified.
    /// </summary>
    public static async Task<AlbumThumbnailResult> GenerateMissingAlbumThumbnailsAsync(
        string albumRoot, IProgress<AlbumThumbnailProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(albumRoot);
        var expectedCache = Path.Combine(root, ".album", "thumbnails");
        if (!Directory.Exists(Path.Combine(root, ".album")))
            throw new InvalidOperationException("No active PhotoLibrarian album.");
        if (_albumCacheDirectory is null ||
            !string.Equals(Path.GetFullPath(_albumCacheDirectory),
                Path.GetFullPath(expectedCache), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The thumbnail cache is not configured for this album.");

        const int size = 256; // Same size as ImageGridViewModel.
        var missing = new List<string>();
        int cached = 0;
        using (var scanner = new FolderScannerService())
        {
            await foreach (var file in scanner.ScanFolderAsync(
                root, includeSubfolders: true, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (FolderScannerService.IsVideoFile(file)) continue;
                var cache = GetCachePath(file, size);
                if (cache is null) continue;
                if (File.Exists(cache) && new FileInfo(cache).Length > 0)
                    cached++;
                else
                    missing.Add(file);
            }
        }

        var generated = 0;
        var failed = 0;
        var processed = 0;
        progress?.Report(new AlbumThumbnailProgress(0, missing.Count,
            0, cached, 0, ""));
        foreach (var file in missing)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var bytes = await GetThumbnailStreamAsync(file, size);
                var path = GetCachePath(file, size);
                if (bytes is { Length: > 0 } && path is not null &&
                    File.Exists(path) && new FileInfo(path).Length > 0)
                    generated++;
                else
                    failed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                System.Diagnostics.Debug.WriteLine(
                    $"[THUMBNAILS] Backfill failed for {file}: {ex}");
            }
            processed++;
            progress?.Report(new AlbumThumbnailProgress(processed, missing.Count,
                generated, cached, failed, Path.GetFileName(file)));
        }
        return new AlbumThumbnailResult(missing.Count + cached,
            generated, cached, failed);
    }

    // Keep both active grid sizes: 256px in the virtualized grid and 180px
    // in the legacy/secondary paths. Only paths generated by this cache key
    // function are eligible for deletion. Capture BEFORE moving to Recycle Bin.
    private static readonly int[] KnownGridSizes = [180, 256];

    public static string[] CaptureThumbnailPathsForDeletion(string filePath)
    {
        var root = _albumCacheDirectory;
        if (root is null || !File.Exists(filePath)) return [];
        return KnownGridSizes
            .Select(size => GetCachePath(filePath, size))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Best-effort cleanup after successful file recycling. Never touches an
    /// original photo, an XMP sidecar or the Windows system thumbnail cache.
    /// </summary>
    public static Task DeleteCapturedThumbnailsAsync(IEnumerable<string> capturedPaths) =>
        Task.Run(() =>
        {
            var root = _albumCacheDirectory;
            if (string.IsNullOrWhiteSpace(root)) return;
            var fullRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            foreach (var path in capturedPaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var fullPath = Path.GetFullPath(path);
                    if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(Path.GetExtension(fullPath), ".bin",
                            StringComparison.OrdinalIgnoreCase)) continue;
                    File.Delete(fullPath);
                }
                catch (IOException ex)
                {
                    System.Diagnostics.Debug.WriteLine("[THUMBNAILS] Cache cleanup failed: " + ex.Message);
                }
                catch (UnauthorizedAccessException ex)
                {
                    System.Diagnostics.Debug.WriteLine("[THUMBNAILS] Cache cleanup denied: " + ex.Message);
                }
            }
        });

    public static bool IsOrphanCleanupDue()
    {
        var root = _albumCacheDirectory;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return false;
        try
        {
            if ((File.GetAttributes(root) & System.IO.FileAttributes.ReparsePoint) != 0)
                return false;
            var stamp = Path.Combine(root, ".last-orphan-sweep");
            return !File.Exists(stamp) ||
                DateTime.UtcNow - File.GetLastWriteTimeUtc(stamp) >= TimeSpan.FromDays(1);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// Maintenance only: clean opaque, legacy SHA-256 cache names whose
    /// source photo is no longer present (or whose metadata changed).
    /// Run after startup indexing has had time to settle, at most once/day.
    /// Does not touch the Windows thumbnail cache or photo/metadata files.
    /// </summary>
    public static Task PruneOrphanedThumbnailsAsync(
        IReadOnlyCollection<PhotoLibrarian.Core.Models.ImageEntry> indexedImages) =>
        Task.Run(() =>
        {
            var root = _albumCacheDirectory;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return;
            try
            {
                if ((File.GetAttributes(root) & System.IO.FileAttributes.ReparsePoint) != 0)
                    return;
                var stamp = Path.Combine(root, ".last-orphan-sweep");
                if (File.Exists(stamp) &&
                    DateTime.UtcNow - File.GetLastWriteTimeUtc(stamp) < TimeSpan.FromDays(1))
                    return;

                // Calculate only live, known key variants from the existing
                // library catalog. All image and filesystem reads occur on the
                // worker thread; no work is placed on the WinUI dispatcher.
                var valid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var image in indexedImages)
                    foreach (var key in CaptureThumbnailPathsForDeletion(image.FilePath))
                        valid.Add(key);

                int removed = 0;
                var buckets = new List<string> { root };
                buckets.AddRange(Directory.EnumerateDirectories(root, "*",
                    SearchOption.TopDirectoryOnly));
                foreach (var bucket in buckets)
                {
                    if ((File.GetAttributes(bucket) & System.IO.FileAttributes.ReparsePoint) != 0)
                        continue;
                    foreach (var path in Directory.EnumerateFiles(bucket, "*.bin",
                        SearchOption.TopDirectoryOnly))
                    {
                        if (valid.Contains(path)) continue;
                        try
                        {
                            if ((File.GetAttributes(path) & System.IO.FileAttributes.ReparsePoint) != 0)
                                continue;
                            File.Delete(path);
                            removed++;
                        }
                        catch (IOException) { /* still in use */ }
                        catch (UnauthorizedAccessException) { }
                    }
                }
                File.WriteAllText(stamp, DateTime.UtcNow.ToString("O",
                    System.Globalization.CultureInfo.InvariantCulture));
                System.Diagnostics.Debug.WriteLine(
                    $"[THUMBNAILS] Orphan sweep removed {removed} unused cache entries.");
            }
            catch (IOException ex)
            {
                System.Diagnostics.Debug.WriteLine("[THUMBNAILS] Orphan sweep failed: " + ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                System.Diagnostics.Debug.WriteLine("[THUMBNAILS] Orphan sweep denied: " + ex.Message);
            }
        });

    public static string CacheDirectory => _albumCacheDirectory ?? "";

    public static Task<long> GetCacheSizeAsync() => Task.Run(() =>
    {
        var root = _albumCacheDirectory;
        if (root is null || !Directory.Exists(root)) return 0L;
        long size = 0;
        try
        {
            foreach (var path in Directory.EnumerateFiles(root, "*.bin",
                         SearchOption.AllDirectories))
            {
                try { size += new FileInfo(path).Length; }
                catch (IOException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return size;
    });

    public static Task ClearCacheAsync() => Task.Run(() =>
    {
        var root = _albumCacheDirectory;
        if (root is null || !Directory.Exists(root)) return;
        foreach (var path in Directory.EnumerateFiles(root, "*.bin",
                     SearchOption.AllDirectories))
        {
            try { File.Delete(path); }
            catch (IOException) { /* active thumbnail load */ }
            catch (UnauthorizedAccessException) { }
        }
    });

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
        // Use only Windows' thumbnail handler inside the main application
        // process. The optional libheif decoder invokes unmanaged code which
        // can terminate the entire app with 0xC0000005; .NET try/catch and
        // CrashLog cannot recover from that. A native fallback must run in an
        // isolated helper process before it may be enabled again.
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(filePath);
            using var thumb = await file.GetThumbnailAsync(
                ThumbnailMode.SingleItem,
                (uint)size,
                ThumbnailOptions.UseCurrentScale);
            if (thumb == null || thumb.Size == 0)
                return null;

            var bytes = new byte[thumb.Size];
            await thumb.AsStreamForRead().ReadAsync(bytes, 0, bytes.Length);
            return bytes;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"WindowsThumbnailService.GetThumbnailStreamAsync failed for {Path.GetFileName(filePath)}: {ex.Message}");
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
