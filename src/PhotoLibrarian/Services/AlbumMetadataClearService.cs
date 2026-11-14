using System.Text;
using PhotoLibrarian.Core.Models;
using PhotoLibrarian.Core.Services;
using XmpCore;

namespace PhotoLibrarian.Services;

public sealed record AlbumMetadataClearProgress(int Processed, int Total, int Failed, string FileName);
public sealed record AlbumMetadataClearResult(
    int Cleaned, int Failed, string BackupDirectory, IReadOnlyList<string> Errors);

/// <summary>
/// Removes image keywords and PhotoLibrarian flags, never ratings/captions/dates.
/// Full originals or sidecars are backed up before each write. The caller is
/// responsible for clearing corresponding database rows AFTER successful writes.
/// </summary>
public static class AlbumMetadataClearService
{
    private const string DcNs = "http://purl.org/dc/elements/1.1/";
    private const string PlibNs = EmbeddedMetadataWriter.PhotoLibrarianXmpNamespace;
    private const string FlagName = "plib:Flagged";

    public static async Task<AlbumMetadataClearResult> RunAsync(
        string albumRoot,
        IReadOnlyCollection<ImageEntry> images,
        Func<ImageEntry, Task> clearDatabaseEntry,
        IProgress<AlbumMetadataClearProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(albumRoot) || !Directory.Exists(
                Path.Combine(albumRoot, ".album")))
            throw new InvalidOperationException("No active PhotoLibrarian album.");
        string backupRoot = Path.Combine(albumRoot, ".album", "metadata-reset-backups",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
        int cleaned = 0, failed = 0, processed = 0;
        var errors = new List<string>();
        foreach (var entry in images.DistinctBy(e => e.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string source = entry.FilePath;
            try
            {
                if (!File.Exists(source))
                    throw new FileNotFoundException("Indexed photo no longer exists.", source);
                if (!PhotoLibrarian.Core.Services.FolderScannerService.IsPathOwnedByAlbumRoot(
                        albumRoot, source))
                    throw new InvalidOperationException("Photo is outside the active album.");

                var sidecar = Path.ChangeExtension(source, ".xmp");
                bool hasSidecar = File.Exists(sidecar);
                bool embedded = EmbeddedMetadataWriter.IsSupported(source);
                // Do not silently clear only the database for files that may
                // still have embedded tags in an unsupported format.
                if (!embedded && !hasSidecar && entry.Id > 0)
                    throw new NotSupportedException(
                        "Cannot safely remove embedded tags from this image format.");

                var relative = Path.GetRelativePath(albumRoot, source);
                var originalBackup = Path.Combine(backupRoot, relative);
                var sidecarBackup = Path.ChangeExtension(originalBackup, ".xmp");
                Directory.CreateDirectory(Path.GetDirectoryName(originalBackup)!);
                if (embedded)
                    File.Copy(source, originalBackup, overwrite: false);
                if (hasSidecar)
                    File.Copy(sidecar, sidecarBackup, overwrite: false);

                try
                {
                    if (embedded)
                    {
                        await EmbeddedMetadataWriter.WriteAsync(
                            source, keywords: Array.Empty<string>(), flagged: false);
                        // Verify that the file remains decodable and no
                        // keywords survive in its in-file property representation.
                        var remaining = await EmbeddedMetadataWriter.ReadKeywordsAsync(source);
                        if (remaining.Count > 0)
                            throw new InvalidDataException(
                                "Embedded keywords remained after clearing.");
                    }

                    if (hasSidecar)
                    {
                        var xml = await File.ReadAllTextAsync(sidecar, cancellationToken);
                        var packet = XmpMetaFactory.ParseFromString(xml);
                        if (packet.DoesPropertyExist(DcNs, "dc:subject"))
                            packet.DeleteProperty(DcNs, "dc:subject");
                        if (packet.DoesPropertyExist(PlibNs, FlagName))
                            packet.DeleteProperty(PlibNs, FlagName);
                        var updated = XmpMetaFactory.SerializeToString(packet,
                            new XmpCore.Options.SerializeOptions());
                        string temp = sidecar + ".reset-" + Guid.NewGuid().ToString("N") + ".tmp";
                        try
                        {
                            await File.WriteAllTextAsync(temp, updated, cancellationToken);
                            // Avoid losing unrelated XMP namespaces and properties.
                            var verification = XmpMetaFactory.ParseFromString(
                                await File.ReadAllTextAsync(temp, cancellationToken));
                            if (verification.DoesPropertyExist(DcNs, "dc:subject") ||
                                verification.DoesPropertyExist(PlibNs, FlagName))
                                throw new InvalidDataException("XMP reset verification failed.");
                            File.Move(temp, sidecar, overwrite: true);
                        }
                        finally
                        {
                            if (File.Exists(temp)) File.Delete(temp);
                        }
                    }
                    await clearDatabaseEntry(entry);
                    cleaned++;
                }
                catch
                {
                    // Restore image and sidecar if file metadata or database
                    // updates fail. Backups are also retained for manual recovery.
                    if (embedded && File.Exists(originalBackup))
                        File.Copy(originalBackup, source, overwrite: true);
                    if (hasSidecar && File.Exists(sidecarBackup))
                        File.Copy(sidecarBackup, sidecar, overwrite: true);
                    throw;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                errors.Add(Path.GetFileName(source) + ": " + ex.Message);
            }
            finally
            {
                processed++;
                progress?.Report(new AlbumMetadataClearProgress(
                    processed, images.Count, failed, Path.GetFileName(source)));
            }
        }

        return new AlbumMetadataClearResult(cleaned, failed, backupRoot, errors);
    }
}
