using Microsoft.Data.Sqlite;
using PhotoLibrarian.Core.Data;
using PhotoLibrarian.Core.Models;
using System.IO.Compression;

namespace PhotoLibrarian.Core.Services;

public sealed class StashImportService
{
    private readonly ImageRepository _images;
    private readonly TagRepository _tags;

    public StashImportService(
        ImageRepository images,
        TagRepository tags)
    {
        _images = images;
        _tags = tags;
    }

    public async Task<StashImportResult> ImportAsync(
        string sourcePath,
        string targetAlbumRoot,
        IProgress<StashImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("A Stash database or backup is required.", nameof(sourcePath));
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The Stash source file was not found.", sourcePath);
        if (string.IsNullOrWhiteSpace(targetAlbumRoot) || !Directory.Exists(targetAlbumRoot))
            throw new DirectoryNotFoundException($"PhotoLibrarian album folder not found: {targetAlbumRoot}");

        string? temporaryDirectory = null;
        try
        {
            var databasePath = sourcePath;
            if (string.Equals(Path.GetExtension(sourcePath), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                temporaryDirectory = Path.Combine(
                    Path.GetTempPath(),
                    "PhotoLibrarian",
                    "StashImport",
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temporaryDirectory);

                using var archive = ZipFile.OpenRead(sourcePath);
                var databaseEntry = archive.Entries.FirstOrDefault(entry =>
                    string.Equals(
                        Path.GetFileName(entry.FullName),
                        "stash-go.sqlite",
                        StringComparison.OrdinalIgnoreCase));

                if (databaseEntry is null)
                    throw new InvalidDataException(
                        "The backup does not contain stash-go.sqlite.");

                databasePath = Path.Combine(temporaryDirectory, "stash-go.sqlite");
                databaseEntry.ExtractToFile(databasePath, overwrite: true);
            }

            return await ImportDatabaseAsync(
                databasePath,
                targetAlbumRoot,
                progress,
                cancellationToken);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(temporaryDirectory))
            {
                try
                {
                    Directory.Delete(temporaryDirectory, recursive: true);
                }
                catch
                {
                    // Temp cleanup is best-effort only.
                }
            }
        }
    }

    private async Task<StashImportResult> ImportDatabaseAsync(
        string databasePath,
        string targetAlbumRoot,
        IProgress<StashImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private
        };

        await using var source = new SqliteConnection(builder.ToString());
        await source.OpenAsync(cancellationToken);
        await ValidateSchemaAsync(source, cancellationToken);

        var tagCatalog = await ReadTagCatalogAsync(source, cancellationToken);
        var sourceImages = await ReadImagesAsync(
            source,
            tagCatalog,
            cancellationToken);
        var targetImages = await _images.GetAllAsync();

        var targetRoot = Path.GetFullPath(targetAlbumRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var byFileName = new Dictionary<string, List<ImageEntry>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var image in targetImages)
        {
            if (!byFileName.TryGetValue(image.FileName, out var list))
            {
                list = [];
                byFileName[image.FileName] = list;
            }
            list.Add(image);
        }

        var matched = 0;
        var unmatched = 0;
        var ambiguous = 0;
        var tagAssignments = 0;
        var persistentWrites = 0;
        var persistentWriteFailures = 0;
        var unmatchedExamples = new List<string>();
        var ambiguousExamples = new List<string>();

        for (var index = 0; index < sourceImages.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceImage = sourceImages[index];

            var resolution = ResolveTarget(
                sourceImage,
                byFileName,
                targetRoot);

            if (resolution.Kind == MatchKind.None)
            {
                unmatched++;
                AddExample(unmatchedExamples, sourceImage.SourcePath);
                progress?.Report(new StashImportProgress(
                    index + 1,
                    sourceImages.Count,
                    matched,
                    unmatched,
                    ambiguous));
                continue;
            }

            if (resolution.Kind == MatchKind.Ambiguous ||
                resolution.Image is null)
            {
                ambiguous++;
                AddExample(ambiguousExamples, sourceImage.SourcePath);
                progress?.Report(new StashImportProgress(
                    index + 1,
                    sourceImages.Count,
                    matched,
                    unmatched,
                    ambiguous));
                continue;
            }

            var target = resolution.Image;
            matched++;

            foreach (var tag in sourceImage.Tags)
            {
                await _tags.AddTagAsync(new ImageTag
                {
                    ImageId = target.Id,
                    Tag = tag,
                    Source = TagSource.Imported,
                    Confidence = 1.0f
                });
                tagAssignments++;
            }

            if (sourceImage.Tags.Count > 0)
            {
                try
                {
                    var currentTags = await _tags.GetTagsAsync(target.Id);
                    await TagWriterService.WriteTagsToSidecarAsync(
                        target.FilePath,
                        currentTags
                            .Select(item => item.Tag)
                            .Distinct(StringComparer.OrdinalIgnoreCase));
                    persistentWrites++;
                }
                catch (Exception exception)
                    when (exception is IOException or
                        UnauthorizedAccessException or
                        InvalidDataException or
                        NotSupportedException)
                {
                    persistentWriteFailures++;
                }
            }

            progress?.Report(new StashImportProgress(
                index + 1,
                sourceImages.Count,
                matched,
                unmatched,
                ambiguous));
        }

        return new StashImportResult(
            sourceImages.Count,
            matched,
            unmatched,
            ambiguous,
            tagAssignments,
            persistentWrites,
            persistentWriteFailures,
            tagCatalog.Values
                .Where(tag => !string.IsNullOrWhiteSpace(tag.Path))
                .Select(tag => tag.Path)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            unmatchedExamples,
            ambiguousExamples);
    }

    private static async Task ValidateSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var required = new[]
        {
            "images",
            "images_files",
            "files",
            "folders",
            "images_tags",
            "tags"
        };

        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master WHERE type='table'";

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            tables.Add(reader.GetString(0));

        var missing = required
            .Where(table => !tables.Contains(table))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidDataException(
                "This does not look like a supported Stash database. Missing tables: " +
                string.Join(", ", missing));
        }
    }

    private static async Task<Dictionary<long, StashTag>> ReadTagCatalogAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var names = new Dictionary<long, string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, name FROM tags";
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.IsDBNull(0))
                    continue;

                var id = reader.GetInt64(0);
                var name = reader.IsDBNull(1)
                    ? string.Empty
                    : CleanTagPart(reader.GetString(1));
                names[id] = name;
            }
        }

        var parentByChild = new Dictionary<long, long>();
        if (await TableExistsAsync(
                connection,
                "tags_relations",
                cancellationToken))
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT parent_id, child_id FROM tags_relations";
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.IsDBNull(0) || reader.IsDBNull(1))
                    continue;

                var parent = reader.GetInt64(0);
                var child = reader.GetInt64(1);
                parentByChild.TryAdd(child, parent);
            }
        }

        var result = new Dictionary<long, StashTag>();
        foreach (var id in names.Keys)
        {
            var parts = new List<string>();
            var current = id;
            var visited = new HashSet<long>();

            while (visited.Add(current) &&
                   names.TryGetValue(current, out var name))
            {
                if (!string.IsNullOrWhiteSpace(name))
                    parts.Add(name);

                if (!parentByChild.TryGetValue(current, out var parent))
                    break;

                current = parent;
            }

            parts.Reverse();
            result[id] = new StashTag(
                id,
                string.Join("/", parts));
        }

        return result;
    }

    private static async Task<List<StashImage>> ReadImagesAsync(
        SqliteConnection connection,
        IReadOnlyDictionary<long, StashTag> tags,
        CancellationToken cancellationToken)
    {
        var tagMap = new Dictionary<long, List<string>>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT image_id, tag_id FROM images_tags";
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.IsDBNull(0) || reader.IsDBNull(1))
                    continue;

                var imageId = reader.GetInt64(0);
                var tagId = reader.GetInt64(1);

                if (!tags.TryGetValue(tagId, out var tag) ||
                    string.IsNullOrWhiteSpace(tag.Path))
                {
                    continue;
                }

                if (!tagMap.TryGetValue(imageId, out var list))
                {
                    list = [];
                    tagMap[imageId] = list;
                }

                list.Add(tag.Path);
            }
        }

        var results = new List<StashImage>();
        await using var imageCommand = connection.CreateCommand();
        imageCommand.CommandText = """
            SELECT
                i.id,
                fo.path,
                f.basename,
                imf.[primary]
            FROM images i
            INNER JOIN images_files imf
                ON imf.image_id = i.id
            INNER JOIN files f
                ON f.id = imf.file_id
            INNER JOIN folders fo
                ON fo.id = f.parent_folder_id
            ORDER BY
                i.id,
                imf.[primary] DESC,
                f.id
            """;

        var seenImages = new HashSet<long>();
        await using var imageReader =
            await imageCommand.ExecuteReaderAsync(cancellationToken);
        while (await imageReader.ReadAsync(cancellationToken))
        {
            var imageId = imageReader.GetInt64(0);
            if (!seenImages.Add(imageId))
                continue;

            var folder = imageReader.IsDBNull(1)
                ? string.Empty
                : imageReader.GetString(1);
            var fileName = imageReader.IsDBNull(2)
                ? string.Empty
                : imageReader.GetString(2);

            if (string.IsNullOrWhiteSpace(fileName))
                continue;

            var sourcePath = NormalizePath(
                string.IsNullOrWhiteSpace(folder)
                    ? fileName
                    : folder.TrimEnd('/', '\\') + "/" + fileName);

            var imageTags = tagMap.TryGetValue(imageId, out var assigned)
                ? assigned
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : [];

            results.Add(new StashImage(
                imageId,
                sourcePath,
                fileName,
                imageTags));
        }

        return results;
    }

    private static TargetResolution ResolveTarget(
        StashImage source,
        IReadOnlyDictionary<string, List<ImageEntry>> byFileName,
        string targetRoot)
    {
        if (!byFileName.TryGetValue(source.FileName, out var candidates) ||
            candidates.Count == 0)
        {
            return new TargetResolution(MatchKind.None, null);
        }

        var sourcePath = NormalizePath(source.SourcePath);

        var suffixMatches = candidates
            .Where(candidate =>
            {
                var relative = NormalizePath(
                    Path.GetRelativePath(targetRoot, candidate.FilePath));

                return string.Equals(
                           sourcePath,
                           relative,
                           StringComparison.OrdinalIgnoreCase) ||
                       sourcePath.EndsWith(
                           "/" + relative,
                           StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        if (suffixMatches.Count == 1)
            return new TargetResolution(MatchKind.PathSuffix, suffixMatches[0]);
        if (suffixMatches.Count > 1)
            return new TargetResolution(MatchKind.Ambiguous, null);

        if (candidates.Count == 1)
            return new TargetResolution(MatchKind.UniqueFileName, candidates[0]);

        return new TargetResolution(MatchKind.Ambiguous, null);
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name LIMIT 1";
        command.Parameters.AddWithValue("$name", table);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/')
            .Trim()
            .TrimStart('/')
            .Replace("//", "/");

    private static string CleanTagPart(string value) =>
        value.Trim().Replace("/", "／");

    private static void AddExample(
        List<string> examples,
        string value)
    {
        if (examples.Count < 10)
            examples.Add(value);
    }

    private sealed record StashTag(long Id, string Path);

    private sealed record StashImage(
        long Id,
        string SourcePath,
        string FileName,
        IReadOnlyList<string> Tags);

    private sealed record TargetResolution(
        MatchKind Kind,
        ImageEntry? Image);

    private enum MatchKind
    {
        None,
        PathSuffix,
        UniqueFileName,
        Ambiguous
    }
}

public sealed record StashImportProgress(
    int Processed,
    int Total,
    int Matched,
    int Unmatched,
    int Ambiguous);

public sealed record StashImportResult(
    int SourceImages,
    int MatchedImages,
    int UnmatchedImages,
    int AmbiguousImages,
    int TagAssignments,
    int PersistentWrites,
    int PersistentWriteFailures,
    IReadOnlyList<string> CatalogTags,
    IReadOnlyList<string> UnmatchedExamples,
    IReadOnlyList<string> AmbiguousExamples);
