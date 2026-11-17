using PhotoLibrarian.Core.Models;
using PhotoLibrarian.Core.Services;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;

namespace PhotoLibrarian.Services;

public sealed record PhotoPrivacyProgress(
    int Processed, int Total, int Cleaned, int Skipped, int Failed, string FileName);

public sealed record PhotoPrivacyResult(
    int Cleaned, int Skipped, int Failed, bool Cancelled,
    string BackupDirectory, IReadOnlyList<string> Errors);

/// <summary>
/// One safe pipeline for single-selection and album-wide metadata anonymization.
/// The image and both XMP sidecar naming variants are backed up before the
/// first replacement. A database transaction commits only after the rewritten
/// file has been validated and its sidecars removed.
/// </summary>
public static class PhotoPrivacyCleanupService
{
    public static async Task<PhotoPrivacyResult> RunAsync(
        string albumRoot,
        IEnumerable<ImageEntry> entries,
        Func<ImageEntry, long, DateTime, int, int, Task> commitDatabase,
        IProgress<PhotoPrivacyProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool keepBackups = true)
    {
        var root = Path.GetFullPath(albumRoot);
        var items = entries
            .DistinctBy(e => e.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") +
            "-" + Guid.NewGuid().ToString("N")[..8];
        var reportRoot = Path.Combine(root, ".album", "privacy-reports", runId);
        var retained = Path.Combine(root, ".album", "privacy-backups", runId);
        var rollback = keepBackups ? retained :
            Path.Combine(root, ".album", "privacy-rollback", runId);
        var errors = new List<string>();
        var cleaned = 0;
        var failed = 0;
        var skipped = 0;
        var processed = 0;
        var cancelled = false;

        foreach (var entry in items)
        {
            var source = Path.GetFullPath(entry.FilePath);
            string? staged = null;
            var stage = "validating source";
            var modified = false;
            var recoveryCompleted = false;
            var backups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!FolderScannerService.IsPathOwnedByAlbumRoot(root, source) ||
                    FolderScannerService.ContainsAlbumMetadataComponent(source))
                    throw new InvalidOperationException("The image is outside the active album.");

                if (!PhotoMetadataAnonymizer.Supports(source) ||
                    entry.MediaType == MediaType.Video)
                {
                    skipped++;
                    errors.Add($"{Path.GetRelativePath(root, source)}: skipped (unsupported image format).");
                    continue;
                }
                if (!File.Exists(source))
                    throw new FileNotFoundException("Photo not found.", source);

                // a.xmp can belong to a RAW + JPEG pair or to photos sharing
                // a basename. Removing it for one image could corrupt another.
                var looseSidecar = Path.ChangeExtension(source, ".xmp");
                if (File.Exists(looseSidecar))
                {
                    var dirname = Path.GetDirectoryName(source)!;
                    var stem = Path.GetFileNameWithoutExtension(source);
                    var shared = Directory.EnumerateFiles(dirname)
                        .Where(other => !string.Equals(other, source,
                            StringComparison.OrdinalIgnoreCase))
                        .Any(other => FolderScannerService.IsSupportedFile(other) &&
                            string.Equals(Path.GetFileNameWithoutExtension(other), stem,
                                StringComparison.OrdinalIgnoreCase));
                    if (shared)
                    {
                        skipped++;
                        errors.Add($"{Path.GetRelativePath(root, source)}: skipped (XMP sidecar is shared by another image).");
                        continue;
                    }
                }

                stage = "preparing clean image";
                var bytes = await PhotoMetadataAnonymizer.PrepareCleanImageAsync(
                    source, cancellationToken);
                staged = source + ".privacy-" + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllBytesAsync(staged, bytes, cancellationToken);

                // Verify that the actual Windows image decoder accepts the
                // modified container before replacing the user's source.
                stage = "validating clean pixels";
                uint width, height;
                await using (var verifyFile = File.OpenRead(staged))
                {
                    using var random = verifyFile.AsRandomAccessStream();
                    var decoder = await BitmapDecoder.CreateAsync(random)
                        .AsTask(cancellationToken);
                    if (decoder.FrameCount != 1)
                        throw new InvalidDataException("Rewritten image has multiple frames.");
                    width = decoder.OrientedPixelWidth;
                    height = decoder.OrientedPixelHeight;
                }

                var sidecars = PhotoMetadataAnonymizer.SidecarPaths(source)
                    .Where(File.Exists)
                    .ToArray();
                stage = "backing up originals";
                foreach (var existing in new[] { source }.Concat(sidecars))
                {
                    var relative = Path.GetRelativePath(root, existing);
                    var backup = Path.Combine(rollback, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(existing, backup, overwrite: false);
                    backups.Add(existing, backup);
                }

                cancellationToken.ThrowIfCancellationRequested();
                stage = "replacing photo and XMP";
                File.Move(staged, source, overwrite: true);
                staged = null;
                modified = true;
                foreach (var sidecar in sidecars)
                    File.Delete(sidecar);

                stage = "clearing private catalog data";
                var info = new FileInfo(source);
                await commitDatabase(entry, info.Length, info.LastWriteTimeUtc,
                    checked((int)width), checked((int)height));

                cleaned++;
                recoveryCompleted = true; // Success: rollback copies may be discarded.
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (modified)
                {
                    try
                    {
                        RestoreBackups(backups);
                        recoveryCompleted = true;
                    }
                    catch (Exception restoreError)
                    {
                        failed++;
                        errors.Add($"{Path.GetRelativePath(root, source)}: CRITICAL: " +
                            $"cancelled operation could not be restored. Original recovery copies " +
                            $"remain at {rollback}. {restoreError.Message}");
                    }
                }
                cancelled = true;
                break;
            }
            catch (NotSupportedException ex)
            {
                if (modified)
                {
                    try
                    {
                        RestoreBackups(backups);
                        recoveryCompleted = true;
                    }
                    catch (Exception restoreError)
                    {
                        failed++;
                        errors.Add($"{Path.GetRelativePath(root, source)}: CRITICAL: " +
                            $"restore failed; originals remain at {rollback}. {restoreError.Message}");
                    }
                }
                skipped++;
                errors.Add($"{Path.GetRelativePath(root, source)}: skipped ({ex.Message})");
            }
            catch (Exception ex)
            {
                var detail = ex.Message;
                if (modified)
                {
                    try
                    {
                        RestoreBackups(backups);
                        recoveryCompleted = true;
                    }
                    catch (Exception recovery)
                    {
                        detail += " CRITICAL: restore failed; original backups remain at " +
                                  rollback + ". " + recovery.Message;
                    }
                }
                failed++;
                errors.Add($"{Path.GetRelativePath(root, source)}: [{stage}] {detail}");
            }
            finally
            {
                if (staged is not null)
                {
                    try { if (File.Exists(staged)) File.Delete(staged); }
                    catch (IOException) { /* Preserve failed temp on a sharing violation. */ }
                }
                // In no-backup mode, keep the temporary originals only if a
                // rollback itself failed. Otherwise delete them even after
                // errors or cancellation, so privacy cleanup leaves no retained
                // backup copies when recovery succeeded.
                if (!keepBackups && (!modified || recoveryCompleted))
                {
                    var left = TryDeleteBackups(backups.Values);
                    if (left.Count > 0)
                    {
                        failed++;
                        errors.Add($"{Path.GetRelativePath(root, source)}: " +
                            $"CRITICAL: temporary rollback copies could not be deleted: " +
                            string.Join("; ", left));
                    }
                }
                processed++;
                progress?.Report(new PhotoPrivacyProgress(
                    processed, items.Length, cleaned, skipped, failed,
                    Path.GetFileName(source)));
            }
        }

        if (!keepBackups)
        {
            TryRemoveEmptyDirectories(rollback);
            TryRemoveEmptyDirectories(Path.GetDirectoryName(rollback)!);
        }

        if (errors.Count > 0)
        {
            Directory.CreateDirectory(reportRoot);
            await File.WriteAllLinesAsync(
                Path.Combine(reportRoot, "privacy-errors.txt"), errors,
                CancellationToken.None);
        }

        return new PhotoPrivacyResult(
            cleaned, skipped, failed, cancelled,
            keepBackups ? retained : reportRoot, errors);
    }

    private static void RestoreBackups(IReadOnlyDictionary<string, string> backups)
    {
        // Restore image first, then all prior sidecars. If this fails, backups
        // stay on disk for manual recovery rather than being silently deleted.
        foreach (var (original, backup) in backups)
            File.Copy(backup, original, overwrite: true);
    }

    private static IReadOnlyList<string> TryDeleteBackups(IEnumerable<string> backupPaths)
    {
        var remaining = new List<string>();
        foreach (var path in backupPaths)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            if (File.Exists(path)) remaining.Add(path);
        }
        return remaining;
    }

    private static void TryRemoveEmptyDirectories(string root)
    {
        if (!Directory.Exists(root)) return;
        foreach (var path in Directory.GetDirectories(
                     root, "*", SearchOption.AllDirectories)
                 .OrderByDescending(path => path.Length))
        {
            try { Directory.Delete(path); }
            catch (IOException) { /* Another copy may need recovery. */ }
            catch (UnauthorizedAccessException) { }
        }
        try { Directory.Delete(root); }
        catch (IOException) { /* Do not remove a run containing backups. */ }
        catch (UnauthorizedAccessException) { }
    }
}
