using PhotoLibrarian.Core.Data;
using PhotoLibrarian.Core.Diagnostics;
using PhotoLibrarian.Core.Models;
using System.Security.Cryptography;

namespace PhotoLibrarian.Core.Services;

/// <summary>
/// Orchestrates folder scanning and metadata reading into a unified indexing pipeline.
/// Note: Thumbnail generation removed - we now use Windows thumbnail cache on-demand for better performance.
/// </summary>
public sealed class LibraryIndexingService
{
    private readonly CacheDatabase _db;
    private readonly ImageRepository _imageRepo;
    private readonly TagRepository _tagRepo;
    private readonly FaceRepository _faceRepo;
    private readonly FolderScannerService _scanner;
    private readonly MetadataReaderService _metadataReader;
    private readonly IFaceMetadataStore _faceMetadataStore;
    private readonly SemaphoreSlim _databaseWriteGate = new(1, 1);

    private static readonly int IndexWorkerCount =
        Math.Clamp(Environment.ProcessorCount / 2, 2, 6);

    public event EventHandler<IndexingProgressEventArgs>? Progress;

    public LibraryIndexingService(
        CacheDatabase db,
        ImageRepository imageRepo,
        TagRepository tagRepo,
        FaceRepository faceRepo,
        FolderScannerService scanner,
        MetadataReaderService metadataReader,
        IFaceMetadataStore faceMetadataStore)
    {
        _db = db;
        _imageRepo = imageRepo;
        _tagRepo = tagRepo;
        _faceRepo = faceRepo;
        _scanner = scanner;
        _metadataReader = metadataReader;
        _faceMetadataStore = faceMetadataStore;
    }

    public async Task ExportPendingFaceMetadataAsync(
        CancellationToken cancellationToken = default)
    {
        var pending = await _faceRepo
            .GetImagesRequiringFaceMetadataExportAsync(cancellationToken);
        foreach (var image in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(image.FilePath))
            {
                throw new FileNotFoundException(
                    "A photo with cache-only people metadata could not be found.",
                    image.FilePath);
            }

            var metadata = await _faceRepo.GetPhotoFaceMetadataAsync(
                image.Id,
                image.Width,
                image.Height,
                cancellationToken);
            await _faceMetadataStore.WriteAsync(
                image.FilePath,
                metadata,
                cancellationToken);
            await _faceRepo.SetFaceMetadataExportRequiredAsync(
                image.Id,
                false,
                cancellationToken);
        }
    }

    /// <summary>
    /// Indexes a folder: scans files, reads metadata, generates thumbnails.
    /// </summary>
    public async Task IndexFolderAsync(
        string folderPath,
        bool includeSubfolders = true,
        CancellationToken ct = default)
    {
        var processed = 0;
        var skipped = 0;
        var errors = 0;
        var files = new List<string>();

        DebugLog.WriteLine(
            $"IndexFolderAsync: Starting scan of '{folderPath}' " +
            $"(includeSubfolders={includeSubfolders}, workers={IndexWorkerCount})");

        await foreach (var filePath in _scanner.ScanFolderAsync(
                           folderPath,
                           includeSubfolders,
                           ct))
        {
            ct.ThrowIfCancellationRequested();
            files.Add(filePath);
        }

        await Parallel.ForEachAsync(
            files,
            new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = IndexWorkerCount
            },
            async (filePath, token) =>
            {
                try
                {
                    if (!await IndexFileAsync(filePath, token))
                    {
                        Interlocked.Increment(ref skipped);
                        return;
                    }

                    var done = Interlocked.Increment(ref processed);
                    if (done % 25 == 0)
                    {
                        Progress?.Invoke(
                            this,
                            new IndexingProgressEventArgs(
                                done,
                                Volatile.Read(ref skipped),
                                folderPath));
                    }
                }
                catch (OperationCanceledException)
                    when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    var errorCount = Interlocked.Increment(ref errors);
                    if (errorCount <= 5)
                    {
                        DebugLog.WriteLine(
                            $"IndexFolderAsync: '{filePath}' failed: {ex.Message}");
                    }
                }
            });

        var nestedAlbumRoots =
            FolderScannerService.FindNestedAlbumRoots(folderPath);
        var removedNested = await _imageRepo.DeleteUnderDirectoryRootsAsync(
            nestedAlbumRoots);
        if (removedNested > 0)
        {
            DebugLog.WriteLine(
                $"IndexFolderAsync: Removed {removedNested} cached record(s) owned by nested album(s)");
        }

        var removedMissing =
            await _imageRepo.DeleteMissingInDirectoryAsync(folderPath);
        if (removedMissing > 0)
        {
            DebugLog.WriteLine(
                $"IndexFolderAsync: Removed {removedMissing} missing cache record(s) under '{folderPath}'");
        }

        var removed = removedNested + removedMissing;
        DebugLog.WriteLine(
            $"IndexFolderAsync: Complete - processed={processed}, skipped={skipped}, " +
            $"errors={errors}, removed={removed}, workers={IndexWorkerCount}");
        Progress?.Invoke(
            this,
            new IndexingProgressEventArgs(
                processed,
                skipped,
                folderPath,
                isComplete: true));
    }

    /// <summary>Indexes a single changed file without rescanning its containing folder.</summary>
    public async Task<bool> IndexFileAsync(
        string filePath,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException(
                "The photo to index could not be found.",
                filePath);
        }

        var existing = await _imageRepo.GetByPathAsync(filePath);

        string? currentHash = existing?.FileHash;
        var contentChanged = existing is not null &&
            (existing.FileSize != fileInfo.Length ||
             existing.DateModified != fileInfo.LastWriteTimeUtc);
        var hashMustBeComputed =
            existing is null ||
            string.IsNullOrWhiteSpace(currentHash) ||
            contentChanged;

        if (hashMustBeComputed)
            currentHash = await ComputeSha256Async(filePath, ct);

        // Matching a newly observed path to an old missing row and changing its
        // path must be atomic relative to other index workers.
        if (existing is null && currentHash is not null)
        {
            await _databaseWriteGate.WaitAsync(ct);
            try
            {
                var moved = await _imageRepo.FindUniqueMissingByHashAsync(
                    currentHash,
                    fileInfo.Length);
                if (moved is not null)
                {
                    var oldMovedPath = moved.FilePath;
                    MoveSidecarIfNeeded(oldMovedPath, filePath);
                    await _imageRepo.UpdatePathAsync(moved.Id, filePath);
                    existing = moved;
                    existing.FilePath = filePath;
                    existing.FileName = Path.GetFileName(filePath);
                    DebugLog.WriteLine(
                        $"IndexFileAsync: Matched moved/renamed photo " +
                        $"'{oldMovedPath}' -> '{filePath}' by SHA-256");
                }
            }
            finally
            {
                _databaseWriteGate.Release();
            }
        }
        else if (existing is not null &&
                 hashMustBeComputed &&
                 !contentChanged &&
                 currentHash is not null)
        {
            await _databaseWriteGate.WaitAsync(ct);
            try
            {
                await _imageRepo.UpdateHashAsync(existing.Id, currentHash);
                existing.FileHash = currentHash;
            }
            finally
            {
                _databaseWriteGate.Release();
            }
        }

        if (existing?.FaceMetadataExportRequired == true)
        {
            var cachedMetadata =
                await _faceRepo.GetPhotoFaceMetadataAsync(
                    existing.Id,
                    existing.Width,
                    existing.Height,
                    ct);
            await _faceMetadataStore.WriteAsync(
                filePath,
                cachedMetadata,
                ct);

            await _databaseWriteGate.WaitAsync(ct);
            try
            {
                await _faceRepo.SetFaceMetadataExportRequiredAsync(
                    existing.Id,
                    false,
                    ct);
            }
            finally
            {
                _databaseWriteGate.Release();
            }

            fileInfo.Refresh();
        }

        var sidecarPath =
            FaceMetadataStore.GetSidecarPathForImage(filePath);
        var sidecar =
            File.Exists(sidecarPath)
                ? new FileInfo(sidecarPath)
                : null;
        var sidecarChanged = existing is not null &&
            (!string.Equals(
                 existing.FaceSidecarPath,
                 sidecar?.FullName,
                 StringComparison.OrdinalIgnoreCase) ||
             existing.FaceSidecarSize != sidecar?.Length ||
             existing.FaceSidecarModified != sidecar?.LastWriteTimeUtc);

        if (existing is not null &&
            existing.FileSize == fileInfo.Length &&
            existing.DateModified >= fileInfo.LastWriteTimeUtc &&
            existing.FaceMetadataImported &&
            !sidecarChanged)
        {
            return false;
        }

        // CPU/file work is deliberately outside the SQLite write gate so several
        // workers can hash and parse metadata concurrently.
        var entry = _metadataReader.ReadMetadata(filePath);
        entry.FileHash =
            currentHash ?? await ComputeSha256Async(filePath, ct);
        var faceMetadata = _faceMetadataStore.Read(filePath);
        var tags = _metadataReader.ReadTags(filePath);

        await _databaseWriteGate.WaitAsync(ct);
        try
        {
            var imageId = await _imageRepo.UpsertImageAsync(entry);
            await _faceRepo.SetFaceMetadataImportedAsync(
                imageId,
                false,
                cancellationToken: ct);
            await _faceRepo.ImportFaceMetadataAsync(
                imageId,
                faceMetadata,
                ct);
            await _faceRepo.SetFaceMetadataImportedAsync(
                imageId,
                true,
                sidecar?.FullName,
                sidecar?.Length,
                sidecar?.LastWriteTimeUtc,
                ct);
            await _faceRepo.SetFaceMetadataExportRequiredAsync(
                imageId,
                false,
                ct);

            foreach (var tag in tags)
            {
                await _tagRepo.AddTagAsync(new ImageTag
                {
                    ImageId = imageId,
                    Tag = tag,
                    Source = TagSource.Metadata,
                    Confidence = 1.0f
                });
            }
        }
        finally
        {
            _databaseWriteGate.Release();
        }

        return true;
    }

    private static async Task<string> ComputeSha256Async(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static void MoveSidecarIfNeeded(
        string oldImagePath,
        string newImagePath)
    {
        try
        {
            var oldSidecar = FaceMetadataStore.GetSidecarPathForImage(oldImagePath);
            var newSidecar = FaceMetadataStore.GetSidecarPathForImage(newImagePath);

            if (string.Equals(
                    oldSidecar,
                    newSidecar,
                    StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(oldSidecar) ||
                File.Exists(newSidecar))
            {
                return;
            }

            var destinationDirectory = Path.GetDirectoryName(newSidecar);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);

            File.Move(oldSidecar, newSidecar);
            DebugLog.WriteLine(
                $"Moved sidecar '{oldSidecar}' -> '{newSidecar}' with its photo.");
        }
        catch (Exception ex)
        {
            // The photo identity is still preserved in the DB; a sidecar problem must
            // not turn a successful move into a lost library record.
            DebugLog.WriteLine($"Could not move photo sidecar: {ex.Message}");
        }
    }

}

public sealed class IndexingProgressEventArgs(int processed, int skipped, string folder, bool isComplete = false) : EventArgs
{
    public int Processed { get; } = processed;
    public int Skipped { get; } = skipped;
    public string Folder { get; } = folder;
    public bool IsComplete { get; } = isComplete;
}
