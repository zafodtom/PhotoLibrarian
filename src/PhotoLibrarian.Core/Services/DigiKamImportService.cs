using Microsoft.Data.Sqlite;
using PhotoLibrarian.Core.Data;
using PhotoLibrarian.Core.Models;

namespace PhotoLibrarian.Core.Services;

/// <summary>
/// Imports portable library metadata from a digiKam SQLite core database.
/// The source database is opened read-only and is never modified.
/// </summary>
public sealed class DigiKamImportService
{
    private readonly ImageRepository _images;
    private readonly TagRepository _tags;

    public DigiKamImportService(
        ImageRepository images,
        TagRepository tags)
    {
        _images = images;
        _tags = tags;
    }

    public async Task<DigiKamImportResult> ImportAsync(
        string databasePath,
        string targetAlbumRoot,
        IProgress<DigiKamImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("A digiKam database path is required.", nameof(databasePath));
        if (!File.Exists(databasePath))
            throw new FileNotFoundException("The digiKam database was not found.", databasePath);
        if (string.IsNullOrWhiteSpace(targetAlbumRoot) || !Directory.Exists(targetAlbumRoot))
            throw new DirectoryNotFoundException($"PhotoLibrarian album folder not found: {targetAlbumRoot}");

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private
        };

        await using var source = new SqliteConnection(builder.ToString());
        await source.OpenAsync(cancellationToken);

        await ValidateSchemaAsync(source, cancellationToken);

        var sourceTags = await ReadTagHierarchyAsync(source, cancellationToken);
        var sourceImages = await ReadImagesAsync(source, sourceTags, cancellationToken);
        var targetImages = await _images.GetAllAsync();

        var targetRoot = Path.GetFullPath(targetAlbumRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var byRelativePath = new Dictionary<string, ImageEntry>(
            StringComparer.OrdinalIgnoreCase);
        var byFileName = new Dictionary<string, List<ImageEntry>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var image in targetImages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relative = NormalizeRelativePath(
                Path.GetRelativePath(targetRoot, image.FilePath));
            if (!relative.StartsWith("../", StringComparison.Ordinal) &&
                relative != "..")
            {
                byRelativePath.TryAdd(relative, image);
            }

            if (!byFileName.TryGetValue(image.FileName, out var list))
            {
                list = [];
                byFileName.Add(image.FileName, list);
            }
            list.Add(image);
        }

        var matched = 0;
        var unmatched = 0;
        var ambiguous = 0;
        var tagAssignments = 0;
        var captions = 0;
        var persistentWrites = 0;
        var persistentWriteFailures = 0;
        var matchedTargetIds = new HashSet<long>();
        var unmatchedExamples = new List<string>();
        var ambiguousExamples = new List<string>();

        for (var index = 0; index < sourceImages.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceImage = sourceImages[index];

            var resolution = ResolveTarget(
                sourceImage,
                byRelativePath,
                byFileName,
                targetRoot);

            if (resolution.Kind == MatchKind.None)
            {
                unmatched++;
                AddExample(unmatchedExamples, sourceImage.RelativePath);
                progress?.Report(new DigiKamImportProgress(
                    index + 1, sourceImages.Count, matched, unmatched, ambiguous));
                continue;
            }

            if (resolution.Kind == MatchKind.Ambiguous || resolution.Image is null)
            {
                ambiguous++;
                AddExample(ambiguousExamples, sourceImage.RelativePath);
                progress?.Report(new DigiKamImportProgress(
                    index + 1, sourceImages.Count, matched, unmatched, ambiguous));
                continue;
            }

            var target = resolution.Image;
            matched++;
            matchedTargetIds.Add(target.Id);

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

            // Persist migrated metadata so cache.db remains disposable.
            try
            {
                if (sourceImage.Tags.Count > 0)
                {
                    var currentTags = await _tags.GetTagsAsync(target.Id);
                    await TagWriterService.WriteTagsToSidecarAsync(
                        target.FilePath,
                        currentTags
                            .Select(item => item.Tag)
                            .Distinct(StringComparer.OrdinalIgnoreCase));
                }

                if (!string.IsNullOrWhiteSpace(sourceImage.Caption))
                {
                    await MetadataWriterService.WriteCaptionAsync(
                        target.FilePath,
                        sourceImage.Caption);
                    captions++;
                }

                if (sourceImage.Tags.Count > 0 ||
                    !string.IsNullOrWhiteSpace(sourceImage.Caption))
                {
                    persistentWrites++;
                }
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException
                    or InvalidDataException or NotSupportedException)
            {
                persistentWriteFailures++;
            }

            progress?.Report(new DigiKamImportProgress(
                index + 1, sourceImages.Count, matched, unmatched, ambiguous));
        }

        var catalogTags = sourceTags.Values
            .Where(tag => !tag.IsInternal && !string.IsNullOrWhiteSpace(tag.Path))
            .Select(tag => tag.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new DigiKamImportResult(
            sourceImages.Count,
            matched,
            unmatched,
            ambiguous,
            tagAssignments,
            captions,
            persistentWrites,
            persistentWriteFailures,
            catalogTags,
            unmatchedExamples,
            ambiguousExamples);
    }

    private static async Task ValidateSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var required = new[]
        {
            "Images",
            "Albums",
            "Tags",
            "ImageTags"
        };

        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT name FROM sqlite_master WHERE type = 'table'";
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                tables.Add(reader.GetString(0));
        }

        var missing = required.Where(table => !tables.Contains(table)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidDataException(
                "This does not look like a supported digiKam core database. " +
                "Missing tables: " + string.Join(", ", missing));
        }

        await RequireColumnsAsync(
            connection,
            "Images",
            ["id", "album", "name"],
            cancellationToken);
        await RequireColumnsAsync(
            connection,
            "Albums",
            ["id", "relativePath"],
            cancellationToken);
        await RequireColumnsAsync(
            connection,
            "Tags",
            ["id", "pid", "name"],
            cancellationToken);
        await RequireColumnsAsync(
            connection,
            "ImageTags",
            ["imageid", "tagid"],
            cancellationToken);
    }

    private static async Task RequireColumnsAsync(
        SqliteConnection connection,
        string table,
        IReadOnlyCollection<string> required,
        CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info([{table.Replace("]", "]]")}])";
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            columns.Add(reader.GetString(1));

        var missing = required.Where(column => !columns.Contains(column)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidDataException(
                $"Unsupported digiKam schema: table '{table}' is missing " +
                string.Join(", ", missing));
        }
    }

    private static async Task<Dictionary<long, DigiKamTag>> ReadTagHierarchyAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var raw = new Dictionary<long, (long ParentId, string Name)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, pid, name FROM Tags";
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetInt64(0);
                var parentId = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
                var name = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                raw[id] = (parentId, name);
            }
        }

        var resolved = new Dictionary<long, DigiKamTag>();
        foreach (var id in raw.Keys)
        {
            ResolveTag(id, raw, resolved, new HashSet<long>());
        }
        return resolved;
    }

    private static DigiKamTag ResolveTag(
        long id,
        IReadOnlyDictionary<long, (long ParentId, string Name)> raw,
        IDictionary<long, DigiKamTag> resolved,
        HashSet<long> visiting)
    {
        if (resolved.TryGetValue(id, out var known))
            return known;

        if (!raw.TryGetValue(id, out var row))
            return new DigiKamTag(id, string.Empty, true);

        if (!visiting.Add(id))
        {
            var cycle = new DigiKamTag(id, CleanTagPart(row.Name), true);
            resolved[id] = cycle;
            return cycle;
        }

        var own = CleanTagPart(row.Name);
        var internalTag = IsInternalTagName(own);
        string path;

        if (row.ParentId <= 0 || row.ParentId == id || !raw.ContainsKey(row.ParentId))
        {
            path = own;
        }
        else
        {
            var parent = ResolveTag(row.ParentId, raw, resolved, visiting);
            internalTag |= parent.IsInternal;
            path = string.IsNullOrWhiteSpace(parent.Path)
                ? own
                : string.IsNullOrWhiteSpace(own)
                    ? parent.Path
                    : $"{parent.Path}/{own}";
        }

        visiting.Remove(id);
        var tag = new DigiKamTag(id, path.Trim('/'), internalTag);
        resolved[id] = tag;
        return tag;
    }

    private static async Task<List<DigiKamImage>> ReadImagesAsync(
        SqliteConnection connection,
        IReadOnlyDictionary<long, DigiKamTag> tags,
        CancellationToken cancellationToken)
    {
        var tagMap = new Dictionary<long, List<string>>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT imageid, tagid FROM ImageTags";
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var imageId = reader.GetInt64(0);
                var tagId = reader.GetInt64(1);
                if (!tags.TryGetValue(tagId, out var tag) ||
                    tag.IsInternal ||
                    string.IsNullOrWhiteSpace(tag.Path))
                {
                    continue;
                }

                if (!tagMap.TryGetValue(imageId, out var list))
                {
                    list = [];
                    tagMap.Add(imageId, list);
                }

                list.Add(tag.Path);
            }
        }

        var captions = await ReadCaptionsAsync(connection, cancellationToken);
        var results = new List<DigiKamImage>();

        await using var imageCommand = connection.CreateCommand();
        imageCommand.CommandText = """
            SELECT i.id, i.name, a.relativePath
            FROM Images i
            INNER JOIN Albums a ON a.id = i.album
            """;

        await using var imageReader =
            await imageCommand.ExecuteReaderAsync(cancellationToken);
        while (await imageReader.ReadAsync(cancellationToken))
        {
            var id = imageReader.GetInt64(0);
            var name = imageReader.IsDBNull(1)
                ? string.Empty
                : imageReader.GetString(1);
            var albumPath = imageReader.IsDBNull(2)
                ? string.Empty
                : imageReader.GetString(2);

            if (string.IsNullOrWhiteSpace(name))
                continue;

            var relative = CombineRelative(albumPath, name);
            var imageTags = tagMap.TryGetValue(id, out var assigned)
                ? assigned
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : [];

            captions.TryGetValue(id, out var caption);
            results.Add(new DigiKamImage(
                id,
                relative,
                name,
                imageTags,
                caption));
        }

        return results;
    }

    private static async Task<Dictionary<long, string>> ReadCaptionsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<long, string>();

        if (!await TableHasColumnsAsync(
                connection,
                "ImageComments",
                ["imageid", "type", "language", "comment"],
                cancellationToken))
        {
            return result;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT imageid, comment
            FROM ImageComments
            WHERE type = 1
              AND TRIM(COALESCE(comment, '')) <> ''
            ORDER BY
                CASE WHEN language = 'x-default' THEN 0 ELSE 1 END,
                id
            """;

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1))
                continue;

            var imageId = reader.GetInt64(0);
            var value = reader.GetString(1).Trim();
            if (string.IsNullOrWhiteSpace(value) ||
                IsTechnicalCodecComment(value) ||
                result.ContainsKey(imageId))
            {
                continue;
            }

            result[imageId] = value;
        }

        return result;
    }

    private static bool IsTechnicalCodecComment(string value)
    {
        var trimmed = value.Trim();
        return trimmed.StartsWith("Lavc", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("Lavf", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("libavcodec", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("libavformat", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> TableHasColumnsAsync(
        SqliteConnection connection,
        string table,
        IReadOnlyCollection<string> required,
        CancellationToken cancellationToken)
    {
        var exists = false;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name LIMIT 1";
            command.Parameters.AddWithValue("$name", table);
            exists = await command.ExecuteScalarAsync(cancellationToken) is not null;
        }

        if (!exists)
            return false;

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = $"PRAGMA table_info([{table.Replace("]", "]]")}])";
        await using var reader =
            await pragma.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            columns.Add(reader.GetString(1));

        return required.All(columns.Contains);
    }

    private static TargetResolution ResolveTarget(
        DigiKamImage source,
        IReadOnlyDictionary<string, ImageEntry> byRelativePath,
        IReadOnlyDictionary<string, List<ImageEntry>> byFileName,
        string targetRoot)
    {
        var normalizedSource = NormalizeRelativePath(source.RelativePath);
        if (byRelativePath.TryGetValue(normalizedSource, out var exact))
            return new TargetResolution(MatchKind.Exact, exact);

        if (!byFileName.TryGetValue(source.FileName, out var sameName) ||
            sameName.Count == 0)
        {
            return new TargetResolution(MatchKind.None, null);
        }

        if (sameName.Count == 1)
            return new TargetResolution(MatchKind.FileNameUnique, sameName[0]);

        var suffixMatches = sameName
            .Where(candidate =>
            {
                var targetRelative = NormalizeRelativePath(
                    Path.GetRelativePath(targetRoot, candidate.FilePath));
                return normalizedSource.EndsWith(
                           "/" + targetRelative,
                           StringComparison.OrdinalIgnoreCase) ||
                       targetRelative.EndsWith(
                           "/" + normalizedSource,
                           StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        return suffixMatches.Count switch
        {
            1 => new TargetResolution(MatchKind.Suffix, suffixMatches[0]),
            > 1 => new TargetResolution(MatchKind.Ambiguous, null),
            _ => new TargetResolution(MatchKind.Ambiguous, null)
        };
    }

    private static string CombineRelative(string albumPath, string fileName)
    {
        var album = NormalizeRelativePath(albumPath);
        var file = NormalizeRelativePath(fileName);
        if (album is "." or "")
            return file;
        return $"{album}/{file}".TrimStart('/');
    }

    private static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var normalized = path
            .Replace('\\', '/')
            .Trim()
            .TrimStart('/');

        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];

        return normalized == "." ? string.Empty : normalized;
    }

    private static string CleanTagPart(string value) =>
        value.Trim().Replace("/", "／");

    private static bool IsInternalTagName(string name) =>
        name.StartsWith("_Digikam_Internal_Tags_", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("internal", StringComparison.OrdinalIgnoreCase) &&
        name.StartsWith("_", StringComparison.Ordinal);

    private static void AddExample(List<string> examples, string value)
    {
        const int maxExamples = 10;
        if (examples.Count < maxExamples)
            examples.Add(value);
    }

    private sealed record DigiKamTag(long Id, string Path, bool IsInternal);

    private sealed record DigiKamImage(
        long Id,
        string RelativePath,
        string FileName,
        IReadOnlyList<string> Tags,
        string? Caption);

    private sealed record TargetResolution(MatchKind Kind, ImageEntry? Image);

    private enum MatchKind
    {
        None,
        Exact,
        FileNameUnique,
        Suffix,
        Ambiguous
    }
}

public sealed record DigiKamImportProgress(
    int Processed,
    int Total,
    int Matched,
    int Unmatched,
    int Ambiguous);

public sealed record DigiKamImportResult(
    int SourceImages,
    int MatchedImages,
    int UnmatchedImages,
    int AmbiguousImages,
    int TagAssignments,
    int CaptionsImported,
    int PersistentWrites,
    int PersistentWriteFailures,
    IReadOnlyList<string> CatalogTags,
    IReadOnlyList<string> UnmatchedExamples,
    IReadOnlyList<string> AmbiguousExamples);
