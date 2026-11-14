using PhotoLibrarian.Core.Services;
using Windows.Graphics.Imaging;
using Windows.Foundation;
using Windows.Storage;

namespace PhotoLibrarian.Services;

public sealed record AlbumJpegConversionPreview(
    int ConvertCount, int JpegMetadataCount, int UnsupportedCount,
    int TargetConflictCount, long TotalSourceBytes)
{
    public int PendingCount => ConvertCount + JpegMetadataCount;
}

public sealed record AlbumJpegConversionProgress(int Processed, int Total, int Failed, string FileName);

public sealed record AlbumJpegConversionResult(
    int Converted, int EmbeddedXmp, int Failed, int Skipped,
    string BackupDirectory, IReadOnlyList<string> Errors);

/// <summary>
/// Converts a complete album to JPEG. Files are staged and verified before the
/// originals/sidecars are moved into a retained, per-run rollback directory.
/// Never silently discard nonstandard XMP or overwrite an existing JPG.
/// </summary>
public static class AlbumJpegConversionService
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".bmp", ".tif", ".tiff",
        ".heic", ".heif", ".jxr", ".wdp", ".webp"
    };

    private static readonly HashSet<string> KnownUnsupported = new(StringComparer.OrdinalIgnoreCase)
    {
        ".gif", ".cr2", ".cr3", ".nef", ".arw", ".dng", ".orf", ".raf",
        ".rw2", ".pef", ".raw", ".avif"
    };

    private static IEnumerable<string> EnumerateImages(string albumRoot)
    {
        var pending = new Stack<string>();
        pending.Push(albumRoot);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            foreach (var sub in Directory.EnumerateDirectories(directory))
            {
                var info = new DirectoryInfo(sub);
                // A nested album is a separate library: never cross its marker.
                if (info.Name.Equals(".album", StringComparison.OrdinalIgnoreCase) ||
                    info.Attributes.HasFlag(System.IO.FileAttributes.ReparsePoint) ||
                    Directory.Exists(Path.Combine(sub, ".album")))
                    continue;
                pending.Push(sub);
            }
            foreach (var file in Directory.EnumerateFiles(directory))
                yield return file;
        }
    }

    public static AlbumJpegConversionPreview Preview(string albumRoot)
    {
        ValidateAlbum(albumRoot);
        int convert = 0, embed = 0, unsupported = 0, conflicts = 0;
        long bytes = 0;
        foreach (var file in EnumerateImages(albumRoot))
        {
            var extension = Path.GetExtension(file);
            if (extension.Equals(".xmp", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!Supported.Contains(extension))
            {
                // Videos and catalog files are ignored; count known photo formats
                // which require a future dedicated converter.
                if (KnownUnsupported.Contains(extension)) unsupported++;
                continue;
            }
            bool jpg = extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase);
            bool hasXmp = File.Exists(Path.ChangeExtension(file, ".xmp"));
            if (jpg && !hasXmp) continue;
            if (!jpg && File.Exists(Path.ChangeExtension(file, ".jpg")))
            {
                conflicts++;
                continue;
            }
            if (jpg) embed++; else convert++;
            bytes += new FileInfo(file).Length;
        }
        return new AlbumJpegConversionPreview(convert, embed, unsupported, conflicts, bytes);
    }

    public static async Task<AlbumJpegConversionResult> RunAsync(
        string albumRoot,
        IProgress<AlbumJpegConversionProgress>? progress,
        CancellationToken cancellationToken = default,
        Func<string, string, Task>? onConverted = null)
    {
        ValidateAlbum(albumRoot);
        var candidates = EnumerateImages(albumRoot)
            .Where(file => Supported.Contains(Path.GetExtension(file)))
            .Where(file => !Path.GetExtension(file).Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                           File.Exists(Path.ChangeExtension(file, ".xmp")))
            .ToList();

        string runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" +
                       Guid.NewGuid().ToString("N")[..8];
        var backupRoot = Path.Combine(albumRoot, ".album", "jpeg-conversion-backups", runId);
        var workRoot = Path.Combine(albumRoot, ".album", "jpeg-conversion-work", runId);
        Directory.CreateDirectory(workRoot);
        int converted = 0, embedded = 0, skipped = 0, failed = 0, processed = 0;
        var errors = new List<string>();
        var overall = System.Diagnostics.Stopwatch.StartNew();
        var lastProgressMs = -250L;
        try
        {
            foreach (var source in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string extension = Path.GetExtension(source);
                bool originalJpg = extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase);
                var destination = originalJpg ? source : Path.ChangeExtension(source, ".jpg");
                var sidecar = Path.ChangeExtension(source, ".xmp");
                var hasSidecar = File.Exists(sidecar);
                string? temporary = null;
                string? sourceBackup = null;
                string? sidecarBackup = null;
                try
                {
                    // A JPEG with the target basename already present is never overwritten.
                    if (!originalJpg && File.Exists(destination))
                    {
                        skipped++;
                        continue;
                    }

                    // Work files remain within the album volume, allowing atomic moves.
                    temporary = Path.Combine(workRoot, Guid.NewGuid().ToString("N") + ".jpg");
                    if (originalJpg || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                        extension.Equals(".jpe", StringComparison.OrdinalIgnoreCase) ||
                        extension.Equals(".jfif", StringComparison.OrdinalIgnoreCase))
                        await Task.Run(() => File.Copy(source, temporary), cancellationToken);
                    else
                        await EncodeJpegAsync(source, temporary);

                    byte[]? xmpBytes = null;
                    if (hasSidecar)
                    {
                        // Reject malformed XMP rather than inserting an unreadable
                        // packet and removing its only active sidecar.
                        xmpBytes = await File.ReadAllBytesAsync(sidecar, cancellationToken);
                        XmpCore.XmpMetaFactory.ParseFromString(
                            System.Text.Encoding.UTF8.GetString(xmpBytes));
                        var jpegBytes = await File.ReadAllBytesAsync(temporary, cancellationToken);
                        var updated = JpegXmpPacket.Embed(jpegBytes, xmpBytes);
                        if (!JpegXmpPacket.ContainsExactPacket(updated, xmpBytes))
                            throw new InvalidDataException("Embedded XMP verification failed.");
                        await File.WriteAllBytesAsync(temporary, updated, cancellationToken);
                    }

                    // Decode again to verify the resulting JPEG before moving anything.
                    var file = await StorageFile.GetFileFromPathAsync(temporary);
                    using (var stream = await file.OpenReadAsync())
                    {
                        var decoder = await BitmapDecoder.CreateAsync(stream);
                        if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0)
                            throw new InvalidDataException("JPEG has invalid dimensions.");
                        // The full-size pixel decode of every result used to duplicate
                        // the most expensive stage of conversion and allocate a second
                        // multi-megabyte bitmap. Verify decoding with a tiny downscaled
                        // read instead; originals are retained in the backup folder.
                        await decoder.GetPixelDataAsync(
                            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                            new BitmapTransform { ScaledWidth = 1, ScaledHeight = 1 },
                            ExifOrientationMode.IgnoreExifOrientation,
                            ColorManagementMode.DoNotColorManage);
                    }

                    var relative = Path.GetRelativePath(albumRoot, source);
                    sourceBackup = Path.Combine(backupRoot, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(sourceBackup)!);
                    if (File.Exists(sourceBackup))
                        throw new IOException("Backup filename already exists.");
                    if (hasSidecar)
                    {
                        sidecarBackup = Path.ChangeExtension(sourceBackup, ".xmp");
                        if (File.Exists(sidecarBackup))
                            throw new IOException("Sidecar backup filename already exists.");
                    }

                    // Commit only after image and embedded metadata verification.
                    File.Move(source, sourceBackup);
                    try
                    {
                        if (hasSidecar)
                            File.Move(sidecar, sidecarBackup!);
                        File.Move(temporary, destination);
                        temporary = null;
                    }
                    catch
                    {
                        if (hasSidecar && sidecarBackup is not null && File.Exists(sidecarBackup))
                            File.Move(sidecarBackup, sidecar);
                        if (File.Exists(sourceBackup) && !File.Exists(source))
                            File.Move(sourceBackup, source);
                        throw;
                    }
                    if (!originalJpg)
                    {
                        converted++;
                        if (onConverted is not null)
                        {
                            try
                            {
                                await onConverted(source, destination);
                            }
                            catch (Exception indexError)
                            {
                                // The JPEG is already committed and its original backed
                                // up; report the index warning rather than claiming the
                                // original conversion was rolled back.
                                errors.Add(Path.GetFileName(source) +
                                    ": image database remap failed: " + indexError.Message);
                            }
                        }
                    }
                    if (hasSidecar) embedded++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed++;
                    errors.Add(Path.GetFileName(source) + ": " + ex.Message);
                }
                finally
                {
                    if (temporary is not null)
                        try { File.Delete(temporary); } catch { }
                    processed++;
                    // Avoid scheduling thousands of small UI updates on the
                    // dispatcher when processing large albums.
                    if (overall.ElapsedMilliseconds - lastProgressMs >= 150 ||
                        processed == candidates.Count)
                    {
                        lastProgressMs = overall.ElapsedMilliseconds;
                        progress?.Report(new AlbumJpegConversionProgress(
                            processed, candidates.Count, failed, Path.GetFileName(source)));
                    }
                }
            }
        }
        finally
        {
            try { Directory.Delete(workRoot, recursive: true); } catch { }
        }
        System.Diagnostics.Debug.WriteLine(
            $"[JPEG] Album conversion: {processed} files in {overall.Elapsed.TotalSeconds:F2}s " +
            $"({converted} converted, {embedded} XMP merged, {failed} failed, {skipped} skipped).");
        return new AlbumJpegConversionResult(converted, embedded, failed, skipped, backupRoot, errors);
    }

    private static void ValidateAlbum(string albumRoot)
    {
        if (string.IsNullOrWhiteSpace(albumRoot) || !Directory.Exists(albumRoot))
            throw new DirectoryNotFoundException("Active album directory does not exist.");
        if (!Directory.Exists(Path.Combine(albumRoot, ".album")))
            throw new InvalidOperationException("The selected directory is not a PhotoLibrarian album.");
    }

    private static async Task EncodeJpegAsync(string source, string destination)
    {
        var input = await StorageFile.GetFileFromPathAsync(source);
        using var inputStream = await input.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(inputStream);
        if (decoder.FrameCount != 1)
            throw new NotSupportedException("Multi-frame images require an explicit frame choice.");

        var pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
            new BitmapTransform(), ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb);
        var bytes = pixels.DetachPixelData();
        // JPEG does not support transparency: composite against opaque white.
        for (int i = 0; i < bytes.Length; i += 4)
        {
            int alpha = bytes[i + 3];
            if (alpha < 255)
            {
                bytes[i] = (byte)((bytes[i] * alpha + 255 * (255 - alpha)) / 255);
                bytes[i + 1] = (byte)((bytes[i + 1] * alpha + 255 * (255 - alpha)) / 255);
                bytes[i + 2] = (byte)((bytes[i + 2] * alpha + 255 * (255 - alpha)) / 255);
                bytes[i + 3] = 255;
            }
        }
        using (File.Create(destination)) { }
        var output = await StorageFile.GetFileFromPathAsync(destination);
        using var outputStream = await output.OpenAsync(FileAccessMode.ReadWrite);
        var options = new BitmapPropertySet
        {
            { "ImageQuality", new BitmapTypedValue(0.95f, PropertyType.Single) }
        };
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId,
            outputStream, options);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            decoder.OrientedPixelWidth, decoder.OrientedPixelHeight,
            decoder.DpiX, decoder.DpiY, bytes);
        await encoder.FlushAsync();
    }
}
