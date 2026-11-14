using PhotoLibrarian.Core.Models;
using PhotoLibrarian.Core.Services;
using XmpCore;

namespace PhotoLibrarian.Services;

public sealed record AlbumMetadataClearProgress(int Processed, int Total, int Failed, string FileName);
public sealed record AlbumMetadataClearResult(
    int Cleaned, int Failed, string BackupDirectory, IReadOnlyList<string> Errors);

/// <summary>
/// Removes tag assignments and PhotoLibrarian flags from each indexed photo.
/// Only writes an image when it actually has embedded keywords or flags.
/// Backups are retained and the caller updates SQLite after metadata succeeds.
/// </summary>
public static class AlbumMetadataClearService
{
    private const string DcNs = "http://purl.org/dc/elements/1.1/";
    private const string PlibNs = EmbeddedMetadataWriter.PhotoLibrarianXmpNamespace;
    private const string FlagName = "plib:Flagged";

    static AlbumMetadataClearService()
    {
        try { XmpMetaFactory.SchemaRegistry.RegisterNamespace(PlibNs, "plib"); }
        catch { /* Already registered by metadata services. */ }
    }

    /// <summary>
    /// Defensive filter for backups created by older versions which may have
    /// slipped into the SQLite index. This also protects reset runs on installs
    /// that do not yet include the common scanner fix.
    /// </summary>
    public static bool IsInternalAlbumPath(string albumRoot, string candidate)
    {
        var relative = Path.GetRelativePath(albumRoot, candidate);
        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(component => component.Equals(".album", StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<AlbumMetadataClearResult> RunAsync(
        string albumRoot,
        IReadOnlyCollection<ImageEntry> images,
        Func<ImageEntry, Task> clearDatabaseEntry,
        IProgress<AlbumMetadataClearProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(albumRoot) ||
            !Directory.Exists(Path.Combine(albumRoot, ".album")))
            throw new InvalidOperationException("No active PhotoLibrarian album.");

        string backupRoot = Path.Combine(albumRoot, ".album", "metadata-reset-backups",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);

        // Freeze the input count, including non-writable RAW files. The UI must
        // not report a successful album reset after processing only a subset.
        var candidates = images.DistinctBy(e => e.FilePath,
            StringComparer.OrdinalIgnoreCase).ToArray();
        int cleaned = 0, failed = 0, processed = 0;
        var errors = new List<string>();
        var metadataReader = new MetadataReaderService();

        foreach (var entry in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string source = entry.FilePath;
            string? imageBackup = null;
            string? sidecarBackup = null;
            bool imageModified = false, sidecarModified = false;
            try
            {
                if (!File.Exists(source))
                    throw new FileNotFoundException("Indexed photo no longer exists.", source);
                if (IsInternalAlbumPath(albumRoot, source))
                    throw new InvalidOperationException("Internal .album backup must not be modified.");
                if (!FolderScannerService.IsPathOwnedByAlbumRoot(albumRoot, source))
                    throw new InvalidOperationException("Photo is outside the active album.");

                var sidecar = Path.ChangeExtension(source, ".xmp");
                bool hasSidecar = File.Exists(sidecar);
                IXmpMeta? packet = null;
                bool updateSidecar = false;
                if (hasSidecar)
                {
                    packet = XmpMetaFactory.ParseFromString(
                        await File.ReadAllTextAsync(sidecar, cancellationToken));
                    updateSidecar = packet.DoesPropertyExist(DcNs, "dc:subject") ||
                                    packet.DoesPropertyExist(PlibNs, FlagName);
                }

                // Most records have DB-only tags or an XMP sidecar. Rewriting
                // every JPEG/PNG was unnecessarily slow and often failed due to
                // WIC file locks, leaving all later album tags untouched.
                bool canWriteEmbedded = EmbeddedMetadataWriter.IsSupported(source);
                var embeddedTags = metadataReader.ReadTags(source);
                if (canWriteEmbedded)
                {
                    var systemTags = await EmbeddedMetadataWriter.ReadKeywordsAsync(source);
                    embeddedTags.AddRange(systemTags);
                }
                var embeddedFlag = metadataReader.ReadMetadata(source).IsFlagged &&
                    MetadataWriterService.ReadFlagFromSidecar(source) != true;
                bool updateImage = embeddedTags.Count > 0 || embeddedFlag;

                if (updateImage && !canWriteEmbedded)
                    throw new NotSupportedException(
                        "Embedded tags or flags cannot safely be removed from this format.");

                var relative = Path.GetRelativePath(albumRoot, source);
                if (updateImage || updateSidecar)
                {
                    imageBackup = Path.Combine(backupRoot, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(imageBackup)!);
                }
                if (updateImage)
                    File.Copy(source, imageBackup!, overwrite: false);
                if (updateSidecar)
                {
                    sidecarBackup = Path.ChangeExtension(imageBackup!, ".xmp");
                    File.Copy(sidecar, sidecarBackup, overwrite: false);
                }

                try
                {
                    if (updateImage)
                    {
                        // This explicitly clears the System.Keywords fan-out
                        // (XMP dc:subject, EXIF XPKeywords, IPTC Keywords).
                        // A failed WIC transcode can partially touch its source.
                        // Mark this before writing so the backup is restored.
                        imageModified = true;
                        await EmbeddedMetadataWriter.WriteAsync(
                            source, keywords: Array.Empty<string>(),
                            flagged: embeddedFlag ? false : null);
                        var remaining = metadataReader.ReadTags(source);
                        var remainingWic = await EmbeddedMetadataWriter.ReadKeywordsAsync(source);
                        if (remaining.Count > 0 || remainingWic.Count > 0)
                            throw new InvalidDataException("Embedded keywords remained after reset.");
                    }

                    if (updateSidecar)
                    {
                        packet!.DeleteProperty(DcNs, "dc:subject");
                        packet.DeleteProperty(PlibNs, FlagName);
                        var updated = XmpMetaFactory.SerializeToString(packet,
                            new XmpCore.Options.SerializeOptions());
                        string temp = sidecar + ".reset-" + Guid.NewGuid().ToString("N") + ".tmp";
                        try
                        {
                            await File.WriteAllTextAsync(temp, updated, cancellationToken);
                            var check = XmpMetaFactory.ParseFromString(
                                await File.ReadAllTextAsync(temp, cancellationToken));
                            if (check.DoesPropertyExist(DcNs, "dc:subject") ||
                                check.DoesPropertyExist(PlibNs, FlagName))
                                throw new InvalidDataException("XMP reset verification failed.");
                            File.Move(temp, sidecar, overwrite: true);
                            sidecarModified = true;
                        }
                        finally
                        {
                            if (File.Exists(temp)) File.Delete(temp);
                        }
                    }

                    // A DB-only tag needs NO WIC write and NO XMP sidecar.
                    await clearDatabaseEntry(entry);
                    entry.IsFlagged = false;
                    cleaned++;
                }
                catch
                {
                    // Never restore a backup for a file we didn't modify; this
                    // also prevents a failed DB operation from overwriting a
                    // concurrent external edit to an unmodified photo.
                    if (imageModified && imageBackup is not null && File.Exists(imageBackup))
                        File.Copy(imageBackup, source, overwrite: true);
                    if (sidecarModified && sidecarBackup is not null && File.Exists(sidecarBackup))
                        File.Copy(sidecarBackup, sidecar, overwrite: true);
                    throw;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                errors.Add(Path.GetRelativePath(albumRoot, source) + ": " + ex.Message);
            }
            finally
            {
                processed++;
                progress?.Report(new AlbumMetadataClearProgress(
                    processed, candidates.Length, failed, Path.GetFileName(source)));
            }
        }

        if (errors.Count > 0)
        {
            Directory.CreateDirectory(backupRoot);
            await File.WriteAllLinesAsync(Path.Combine(backupRoot, "reset-errors.txt"),
                errors, CancellationToken.None);
        }
        return new AlbumMetadataClearResult(cleaned, failed, backupRoot, errors);
    }
}
