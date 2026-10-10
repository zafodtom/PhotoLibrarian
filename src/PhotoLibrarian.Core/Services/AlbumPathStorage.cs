namespace PhotoLibrarian.Core.Services;

/// <summary>
/// Converts between absolute runtime paths and portable paths stored in an album cache.
/// In album mode, paths inside the album root are stored relative to the root.
/// </summary>
public static class AlbumPathStorage
{
    private static readonly object Sync = new();
    private static string? _albumRoot;

    public static string? AlbumRoot
    {
        get
        {
            lock (Sync) return _albumRoot;
        }
    }

    public static bool IsAlbumMode => !string.IsNullOrWhiteSpace(AlbumRoot);

    public static void Configure(string? albumRoot)
    {
        lock (Sync)
        {
            _albumRoot = string.IsNullOrWhiteSpace(albumRoot)
                ? null
                : Path.GetFullPath(albumRoot)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    public static string ToStoragePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        var root = AlbumRoot;
        if (string.IsNullOrWhiteSpace(root))
            return path;

        var fullPath = Path.GetFullPath(path);
        if (!IsWithinRoot(fullPath, root))
            return fullPath;

        var relative = Path.GetRelativePath(root, fullPath);
        return NormalizeStoredRelativePath(relative);
    }

    public static string? ToStoragePathOrNull(string? path) =>
        string.IsNullOrWhiteSpace(path) ? path : ToStoragePath(path);

    public static string ToAbsolutePath(string storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
            return storedPath;

        if (Path.IsPathRooted(storedPath))
            return Path.GetFullPath(storedPath);

        var root = AlbumRoot;
        if (string.IsNullOrWhiteSpace(root))
            return storedPath;

        var normalized = storedPath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        return Path.GetFullPath(Path.Combine(root, normalized));
    }

    public static string? ToAbsolutePathOrNull(string? storedPath) =>
        string.IsNullOrWhiteSpace(storedPath) ? storedPath : ToAbsolutePath(storedPath);

    public static string AlbumRootStoragePath => ".";

    private static string NormalizeStoredRelativePath(string relative) =>
        relative.Replace('\\', '/');

    private static bool IsWithinRoot(string path, string root)
    {
        if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
            return true;

        var prefix = root + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
