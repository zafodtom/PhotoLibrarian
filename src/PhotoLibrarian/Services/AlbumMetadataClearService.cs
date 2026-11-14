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
    // Compact checkpoint used by the explicit RETRY FAILED action. It lists
    // only files that failed the last completed reset, never all indexed files.
    private const string PendingFileName = "metadata-reset-pending.json";

    private static string PendingPath(string albumRoot) =>
        Path.Combine(albumRoot, ".album", PendingFileName);

    public static IReadOnlyList<string> ReadPendingFailures(string albumRoot)
    {
        var manifest = PendingPath(albumRoot);
        // Older releases wrote reset-errors.txt but did not have a checkpoint.
        // Import the most recent legacy error report once, without rescanning
        // user photos. A subsequently completed reset always writes a
        // checkpoint (even an empty one), so stale reports cannot reappear.
        string[] paths;
        if (File.Exists(manifest))
        {
            // A damaged manifest is an error, not a successful reset.
            paths = System.Text.Json.JsonSerializer.Deserialize<string[]>(
                File.ReadAllText(manifest)) ?? [];
        }
        else
        {
            var reports = new[] { "metadata-reset-backups", "metadata-reset-reports" }
                .Select(folder => Path.Combine(albumRoot, ".album", folder))
                .Where(Directory.Exists)
                .SelectMany(folder => Directory.EnumerateDirectories(folder))
                .Select(folder => new
                {
                    Folder = folder,
                    Report = Path.Combine(folder, "reset-errors.txt")
                })
                .Where(item => File.Exists(item.Report))
                .OrderByDescending(item => Path.GetFileName(item.Folder), StringComparer.Ordinal)
                .ToArray();
            if (reports.Length == 0) return [];
            paths = File.ReadAllLines(reports[0].Report)
                .Select(line =>
                {
                    var separator = line.IndexOf(": [", StringComparison.Ordinal);
                    return separator > 0 ? line[..separator] : "";
                })
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToArray();
        }
        var root = Path.GetFullPath(albumRoot);
        return paths.Select(relative => Path.GetFullPath(Path.Combine(root, relative)))
            .Where(path => !IsInternalAlbumPath(root, path) &&
                FolderScannerService.IsPathOwnedByAlbumRoot(root, path))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static async Task SavePendingFailuresAsync(
        string albumRoot, IReadOnlyCollection<string> failedPaths)
    {
        var destination = PendingPath(albumRoot);
        var relative = failedPaths.Select(path => Path.GetRelativePath(albumRoot, path))
            .ToArray();
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary,
                System.Text.Json.JsonSerializer.Serialize(relative));
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    // Windows file-sharing and Shell metadata operations on NAS mounts can
    // briefly report ACCESS_DENIED while an image handler still has a handle.
    // Retry only at individual I/O boundaries: never repeat a committed album
    // database update or an entire metadata rewrite.
    private static bool IsRetryableAccess(Exception exception) =>
        exception is UnauthorizedAccessException ||
        exception is IOException ||
        exception is System.Runtime.InteropServices.COMException com &&
            (com.HResult == unchecked((int)0x80070005) ||
             com.HResult == unchecked((int)0x80070020) ||
             com.HResult == unchecked((int)0x80070021));

    private static async Task<T> WithAccessRetryAsync<T>(
        Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return await operation(); }
            catch (Exception ex) when (attempt < 5 && IsRetryableAccess(ex))
            {
                await Task.Delay(200 * (attempt + 1), cancellationToken);
            }
        }
    }

    private static Task WithAccessRetryAsync(
        Func<Task> operation, CancellationToken cancellationToken) =>
        WithAccessRetryAsync(async () =>
        {
            await operation();
            return true;
        }, cancellationToken);


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
    /// <summary>
    /// Clears only recognized standard XMP keywords / PhotoLibrarian flags in
    /// JPEG APP1. JPEG image data and unrelated EXIF/IPTC segments are retained.
    /// The caller checks for remaining non-XMP keywords and restores its backup
    /// if verification fails.
    /// </summary>
    private static async Task<bool> TryClearJpegXmpAsync(
        string source, CancellationToken cancellationToken, Action<string>? setStage = null)
    {
        if (!new[] { ".jpg", ".jpeg", ".jpe", ".jfif" }.Contains(
            Path.GetExtension(source), StringComparer.OrdinalIgnoreCase))
            return false;

        setStage?.Invoke("reading JPEG bytes for XMP");
        var original = await WithAccessRetryAsync(
            () => File.ReadAllBytesAsync(source, cancellationToken), cancellationToken);
        var packetBytes = JpegXmpPacket.ExtractStandardPacket(original);
        if (packetBytes is null)
            return false;

        // XmpCore's default serializer can emit an XMP packet with padding /
        // packet wrapping unsuitable for a round-trip through a JPEG APP1
        // payload. Edit the XML tree instead and validate the serialized result
        // before touching the original file.
        // Some JPEGs expose an APP1 packet with an invalid/non-XML payload.
        // The XMP fast-path is optional: leave such bytes untouched and let
        // the independent Windows keyword check decide whether tags remain.
        System.Xml.Linq.XDocument document;
        try
        {
            var xml = new System.Text.UTF8Encoding(false, true).GetString(packetBytes)
                .TrimStart('\uFEFF', '\0', ' ', '\r', '\n', '\t')
                .TrimEnd('\0');
            document = System.Xml.Linq.XDocument.Parse(xml,
                System.Xml.Linq.LoadOptions.PreserveWhitespace);
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
        catch (System.Text.DecoderFallbackException)
        {
            return false;
        }
        System.Xml.Linq.XName subject = System.Xml.Linq.XName.Get("subject", DcNs);
        System.Xml.Linq.XName flagged = System.Xml.Linq.XName.Get("Flagged", PlibNs);
        var targets = document.Descendants().Where(node =>
            node.Name == subject || node.Name == flagged).ToArray();
        var attributes = document.Descendants().SelectMany(node => node.Attributes()).Where(attribute =>
            attribute.Name == subject || attribute.Name == flagged).ToArray();
        if (targets.Length == 0 && attributes.Length == 0)
            return false;

        foreach (var node in targets)
            node.Remove();
        foreach (var attribute in attributes)
            attribute.Remove();

        var rewrittenXmp = System.Text.Encoding.UTF8.GetBytes(
            document.ToString(System.Xml.Linq.SaveOptions.DisableFormatting));
        var verifiedPacket = XmpMetaFactory.ParseFromString(
            System.Text.Encoding.UTF8.GetString(rewrittenXmp));
        if (verifiedPacket.DoesPropertyExist(DcNs, "dc:subject") ||
            verifiedPacket.DoesPropertyExist(PlibNs, FlagName))
            throw new InvalidDataException("Embedded XMP still contains tags or flags.");
        setStage?.Invoke("preparing modified JPEG XMP");
        var updated = JpegXmpPacket.Embed(original, rewrittenXmp);
        if (!JpegXmpPacket.ContainsExactPacket(updated, rewrittenXmp))
            throw new InvalidDataException("Updated embedded XMP packet could not be verified.");

        string temp = source + ".reset-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            setStage?.Invoke("writing temporary JPEG XMP");
            await File.WriteAllBytesAsync(temp, updated, cancellationToken);
            // Only retry the atomic replacement; the fully verified temporary
            // image is kept untouched between attempts.
            setStage?.Invoke("replacing JPEG after XMP update");
            await WithAccessRetryAsync(() =>
            {
                File.Move(temp, source, overwrite: true);
                return Task.CompletedTask;
            }, cancellationToken);
        }
        finally
        {
            if (File.Exists(temp))
                try { File.Delete(temp); }
                catch (IOException) { /* Keep recovery artifact on a sharing violation. */ }
                catch (UnauthorizedAccessException) { /* Keep recovery artifact. */ }
        }
        return true;
    }

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
        CancellationToken cancellationToken = default,
        bool keepBackups = true)
    {
        if (string.IsNullOrWhiteSpace(albumRoot) ||
            !Directory.Exists(Path.Combine(albumRoot, ".album")))
            throw new InvalidOperationException("No active PhotoLibrarian album.");

        string runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" +
            Guid.NewGuid().ToString("N")[..8];
        string backupRoot = Path.Combine(albumRoot, ".album",
            keepBackups ? "metadata-reset-backups" : "metadata-reset-reports", runId);
        // Even in no-backup mode a temporary rollback copy is mandatory while
        // changing bytes. Successful changes discard it immediately.
        string rollbackRoot = keepBackups ? backupRoot :
            Path.Combine(albumRoot, ".album", "metadata-reset-work", runId);

        // Freeze the input count, including non-writable RAW files. The UI must
        // not report a successful album reset after processing only a subset.
        var candidates = images.DistinctBy(e => e.FilePath,
            StringComparer.OrdinalIgnoreCase).ToArray();
        int cleaned = 0, failed = 0, processed = 0;
        var errors = new List<string>();
        var failedPaths = new List<string>();
        var metadataReader = new MetadataReaderService();

        foreach (var entry in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string source = entry.FilePath;
            string? imageBackup = null;
            string? sidecarBackup = null;
            bool imageModified = false, sidecarModified = false;
            string stage = "validating photo";
            try
            {
                if (!File.Exists(source))
                    throw new FileNotFoundException("Indexed photo no longer exists.", source);
                if (IsInternalAlbumPath(albumRoot, source))
                    throw new InvalidOperationException("Internal .album backup must not be modified.");
                if (!FolderScannerService.IsPathOwnedByAlbumRoot(albumRoot, source))
                    throw new InvalidOperationException("Photo is outside the active album.");

                stage = "reading sidecar XMP";
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
                stage = "reading embedded metadata";
                bool canWriteEmbedded = EmbeddedMetadataWriter.IsSupported(source);
                var embeddedTags = metadataReader.ReadTags(source);
                if (canWriteEmbedded)
                {
                    var systemTags = await WithAccessRetryAsync(
                        () => EmbeddedMetadataWriter.ReadKeywordsAsync(source, throwOnError: true),
                        cancellationToken);
                    embeddedTags.AddRange(systemTags);
                }
                var embeddedFlag = metadataReader.ReadMetadata(source).IsFlagged &&
                    MetadataWriterService.ReadFlagFromSidecar(source) != true;
                bool updateImage = embeddedTags.Count > 0 || embeddedFlag;

                if (updateImage && !canWriteEmbedded)
                    throw new NotSupportedException(
                        "Embedded tags or flags cannot safely be removed from this format.");

                // A source can be readable/indexable but still deny writes.
                // Fail before taking backups or changing anything, rather than
                // reporting an opaque 0x80070005 later in the pipeline.
                stage = "checking write access";
                if (updateImage && (File.GetAttributes(source) & FileAttributes.ReadOnly) != 0)
                    throw new UnauthorizedAccessException(
                        "Photo is read-only; remove the Read-only attribute before clearing embedded tags.");
                if (updateSidecar && (File.GetAttributes(sidecar) & FileAttributes.ReadOnly) != 0)
                    throw new UnauthorizedAccessException(
                        "XMP sidecar is read-only; remove the Read-only attribute before clearing tags.");

                stage = "preparing backups";
                var relative = Path.GetRelativePath(albumRoot, source);
                if (updateImage || updateSidecar)
                {
                    imageBackup = Path.Combine(rollbackRoot, relative);
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
                        stage = "writing embedded metadata (WIC)";
                        imageModified = true;
                        // A zero-length System.Keywords value is rejected by many
                        // WIC JPEG metadata handlers (E_INVALIDARG). Remove XMP
                        // dc:subject/our flag directly where possible, preserving
                        // encoded JPEG pixels, EXIF and unrelated APP segments.
                        // Other embedded keyword formats remain fail-closed.
                        stage = "inspecting and updating JPEG XMP";
                        await TryClearJpegXmpAsync(
                            source, cancellationToken, value => stage = value);

                        // System.Keywords is a merged view over XMP, IPTC and
                        // EXIF keyword fields. Cleaning only XMP is insufficient
                        // when the other containers still hold old tags. Use the
                        // Windows Shell deletion policy, not a zero-length WIC
                        // StringArray (which fails with E_INVALIDARG).
                        stage = "checking remaining Windows photo keywords";
                        var remainingBeforeShell = await WithAccessRetryAsync(
                        () => EmbeddedMetadataWriter.ReadKeywordsAsync(source, throwOnError: true),
                        cancellationToken);
                        if (remainingBeforeShell.Count > 0)
                        {
                        stage = "removing Windows photo keywords";
                        await WithAccessRetryAsync(
                            () => EmbeddedMetadataWriter.ClearKeywordsUsingFilePropertiesAsync(source),
                            cancellationToken);

                        }

                        if (embeddedFlag && metadataReader.ReadMetadata(source).IsFlagged &&
                            MetadataWriterService.ReadFlagFromSidecar(source) != true)
                        {
                            stage = "clearing embedded PhotoLibrarian flag";
                            await EmbeddedMetadataWriter.WriteAsync(source, flagged: false);
                        }
                        stage = "verifying embedded metadata";
                        var remaining = metadataReader.ReadTags(source);
                        var remainingWic = await WithAccessRetryAsync(
                        () => EmbeddedMetadataWriter.ReadKeywordsAsync(source, throwOnError: true),
                        cancellationToken);
                        if (remaining.Count > 0 || remainingWic.Count > 0)
                            throw new InvalidDataException("Embedded keywords remained after reset.");
                        if (embeddedFlag && metadataReader.ReadMetadata(source).IsFlagged &&
                            MetadataWriterService.ReadFlagFromSidecar(source) != true)
                            throw new InvalidDataException("Embedded PhotoLibrarian flag remained after reset.");
                    }

                    if (updateSidecar)
                    {
                        stage = "writing sidecar XMP";
                        if (packet!.DoesPropertyExist(DcNs, "dc:subject"))
                            packet.DeleteProperty(DcNs, "dc:subject");
                        if (packet.DoesPropertyExist(PlibNs, FlagName))
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
                    stage = "clearing SQLite tag assignments";
                    await clearDatabaseEntry(entry);
                    entry.IsFlagged = false;
                    cleaned++;
                    if (!keepBackups)
                    {
                        // Metadata and SQLite changes have committed. Backup
                        // cleanup must never trigger a rollback of committed
                        // database work; keep recovery data if deletion fails.
                        foreach (var retained in new[] { imageBackup, sidecarBackup })
                        {
                            if (retained is null || !File.Exists(retained)) continue;
                            try { File.Delete(retained); }
                            catch (Exception cleanupError) when (cleanupError is
                                IOException or UnauthorizedAccessException)
                            {
                                System.Diagnostics.Debug.WriteLine(
                                    "[RESET] Temporary rollback retained: " +
                                    retained + ": " + cleanupError.Message);
                            }
                        }
                    }
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
                    if (!keepBackups)
                    {
                        if (imageBackup is not null && File.Exists(imageBackup))
                            File.Delete(imageBackup);
                        if (sidecarBackup is not null && File.Exists(sidecarBackup))
                            File.Delete(sidecarBackup);
                    }
                    throw;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                failedPaths.Add(source);
                var hresult = $"0x{ex.HResult:X8}";
                errors.Add($"{Path.GetRelativePath(albumRoot, source)}: [{stage}] {ex.GetType().Name} ({hresult}): {ex.Message}");
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
        if (!keepBackups)
        {
            try
            {
                if (Directory.Exists(rollbackRoot) &&
                    !Directory.EnumerateFiles(rollbackRoot, "*", SearchOption.AllDirectories).Any())
                    Directory.Delete(rollbackRoot, recursive: true);
            }
            catch (IOException) { /* Preserve any pending recovery artifacts. */ }
            catch (UnauthorizedAccessException) { /* Never remove failed recovery data. */ }
        }
        // Only publish a checkpoint after the entire requested batch finishes.
        // A retry will replace it with the smaller set still failing.
        await SavePendingFailuresAsync(albumRoot, failedPaths);
        return new AlbumMetadataClearResult(cleaned, failed, backupRoot, errors);
    }
}
