namespace PhotoLibrarian.Core.Services;

/// <summary>
/// Scans watched folders for supported image and video files.
/// Uses FileSystemWatcher for live change detection.
/// </summary>
public sealed class FolderScannerService : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];

    // Supported extensions
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".tiff", ".tif", ".bmp", ".gif", ".webp",
        ".heic", ".heif", ".avif", ".cr2", ".cr3", ".nef", ".arw", ".dng", ".orf", ".rw2"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".m4v", ".webm"
    };

    public event EventHandler<FileChangedEventArgs>? FileChanged;
    public event EventHandler<FileChangedEventArgs>? DirectoryChanged;
    public event EventHandler<PathRenamedEventArgs>? FileRenamed;
    public event EventHandler<PathRenamedEventArgs>? DirectoryRenamed;
    public event EventHandler<ScanProgressEventArgs>? ScanProgress;

    /// <summary>
    /// Scans a folder recursively, yielding discovered media files.
    /// </summary>
    public async IAsyncEnumerable<string> ScanFolderAsync(
        string folderPath,
        bool includeSubfolders = true,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var root = Path.GetFullPath(folderPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        int count = 0;
        await Task.Yield();

        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = pending.Pop();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(
                    current,
                    "*.*",
                    SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                if (!IsSupportedFile(file))
                    continue;

                count++;
                if (count % 100 == 0)
                    ScanProgress?.Invoke(
                        this,
                        new ScanProgressEventArgs(count, root));

                yield return file;
            }

            if (!includeSubfolders)
                continue;

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(
                    current,
                    "*",
                    SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var directory in directories)
            {
                ct.ThrowIfCancellationRequested();

                if (IsAlbumMetadataDirectory(directory))
                    continue;

                // The current scan root belongs to this album, but any descendant
                // directory containing its own .album marker is a nested album and
                // therefore an indexing boundary.
                if (HasAlbumMarker(directory))
                    continue;

                pending.Push(directory);
            }
        }

        ScanProgress?.Invoke(
            this,
            new ScanProgressEventArgs(count, root, isComplete: true));
    }

    public static IReadOnlyList<string> FindNestedAlbumRoots(string folderPath)
    {
        var root = Path.GetFullPath(folderPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var results = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(
                    current,
                    "*",
                    SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var directory in directories)
            {
                if (IsAlbumMetadataDirectory(directory))
                    continue;

                if (HasAlbumMarker(directory))
                {
                    results.Add(Path.GetFullPath(directory));
                    continue;
                }

                pending.Push(directory);
            }
        }

        return results;
    }

    public static bool HasAlbumMarker(string directoryPath) =>
        Directory.Exists(directoryPath) &&
        Directory.Exists(Path.Combine(directoryPath, ".album"));

    public static bool IsAlbumMetadataDirectory(string directoryPath) =>
        string.Equals(
            Path.GetFileName(
                directoryPath.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)),
            ".album",
            StringComparison.OrdinalIgnoreCase);

    public static bool IsPathOwnedByAlbumRoot(
        string albumRoot,
        string path)
    {
        var root = Path.GetFullPath(albumRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path);

        var candidateDirectory = Directory.Exists(fullPath)
            ? fullPath
            : Path.GetDirectoryName(fullPath);

        if (candidateDirectory is null)
            return false;

        var current = Path.GetFullPath(candidateDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        while (!string.Equals(
                   current,
                   root,
                   StringComparison.OrdinalIgnoreCase))
        {
            if (!current.StartsWith(
                    root + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (HasAlbumMarker(current))
                return false;

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent) ||
                string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            current = parent.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }

        return true;
    }

    private static bool IsAlbumMarkerPath(string path) =>
        string.Equals(
            Path.GetFileName(
                path.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)),
            ".album",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Starts watching a folder for file changes.
    /// </summary>
    public void StartWatching(string folderPath, bool includeSubfolders = true)
    {
        var watcher = new FileSystemWatcher(folderPath)
        {
            IncludeSubdirectories = includeSubfolders,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            InternalBufferSize = 32 * 1024
        };

        watcher.Created += OnFileCreated;
        watcher.Changed += OnFileModified;
        watcher.Deleted += OnFileDeleted;
        watcher.Renamed += OnFileRenamed;
        watcher.Error += (_, e) => WatcherError?.Invoke(this, e.GetException());
        try
        {
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
        catch
        {
            watcher.Dispose();
            throw;
        }
    }

    public event EventHandler<Exception>? WatcherError;

    public void SyncWatchedFolders(IEnumerable<(string Path, bool IncludeSubfolders)> folders)
    {
        var wanted = folders.ToDictionary(
            folder => folder.Path,
            folder => folder.IncludeSubfolders,
            StringComparer.OrdinalIgnoreCase);
        var errors = new List<Exception>();

        foreach (var watcher in _watchers.ToArray())
        {
            if (!wanted.TryGetValue(watcher.Path, out var includeSubfolders) ||
                watcher.IncludeSubdirectories != includeSubfolders ||
                !Directory.Exists(watcher.Path))
                StopWatching(watcher.Path);
        }

        foreach (var folder in wanted)
        {
            if (Directory.Exists(folder.Key) &&
                !_watchers.Any(w => w.Path.Equals(folder.Key, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    StartWatching(folder.Key, folder.Value);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors.Add(new IOException($"Could not monitor '{folder.Key}'.", ex));
                }
            }
        }
        if (errors.Count > 0)
            throw new AggregateException(errors);
    }

    public void StopWatching(string folderPath)
    {
        var watcher = _watchers.FirstOrDefault(w => w.Path.Equals(folderPath, StringComparison.OrdinalIgnoreCase));
        if (watcher is not null)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
            _watchers.Remove(watcher);
        }
    }

    public static bool IsSupportedFile(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return ImageExtensions.Contains(ext) || VideoExtensions.Contains(ext);
    }

    public static bool IsVideoFile(string filePath)
    {
        return VideoExtensions.Contains(Path.GetExtension(filePath));
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        if (sender is not FileSystemWatcher watcher)
            return;

        if (IsAlbumMarkerPath(e.FullPath))
        {
            DirectoryChanged?.Invoke(
                this,
                new FileChangedEventArgs(
                    watcher.Path,
                    FileChangeType.Modified));
            return;
        }

        if (!IsPathOwnedByAlbumRoot(watcher.Path, e.FullPath))
            return;

        if (Directory.Exists(e.FullPath))
        {
            DirectoryChanged?.Invoke(
                this,
                new FileChangedEventArgs(
                    e.FullPath,
                    FileChangeType.Created));
            return;
        }

        if (IsSupportedFile(e.FullPath) || IsSidecar(e.FullPath))
            FileChanged?.Invoke(
                this,
                new FileChangedEventArgs(
                    e.FullPath,
                    FileChangeType.Created));
    }

    private void OnFileModified(object sender, FileSystemEventArgs e)
    {
        if (sender is not FileSystemWatcher watcher)
            return;

        if (IsAlbumMarkerPath(e.FullPath))
        {
            DirectoryChanged?.Invoke(
                this,
                new FileChangedEventArgs(
                    watcher.Path,
                    FileChangeType.Modified));
            return;
        }

        if (!IsPathOwnedByAlbumRoot(watcher.Path, e.FullPath))
            return;

        if (IsSupportedFile(e.FullPath) || IsSidecar(e.FullPath))
            FileChanged?.Invoke(
                this,
                new FileChangedEventArgs(
                    e.FullPath,
                    FileChangeType.Modified));
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        if (sender is not FileSystemWatcher watcher)
            return;

        if (IsAlbumMarkerPath(e.FullPath))
        {
            DirectoryChanged?.Invoke(
                this,
                new FileChangedEventArgs(
                    watcher.Path,
                    FileChangeType.Modified));
            return;
        }

        if (!IsPathOwnedByAlbumRoot(watcher.Path, e.FullPath))
            return;

        if (IsSupportedFile(e.FullPath) || IsSidecar(e.FullPath))
        {
            FileChanged?.Invoke(
                this,
                new FileChangedEventArgs(
                    e.FullPath,
                    FileChangeType.Deleted));
        }
        else
        {
            DirectoryChanged?.Invoke(
                this,
                new FileChangedEventArgs(
                    e.FullPath,
                    FileChangeType.Deleted));
        }
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (sender is not FileSystemWatcher watcher)
            return;

        if (IsAlbumMarkerPath(e.OldFullPath) ||
            IsAlbumMarkerPath(e.FullPath))
        {
            DirectoryChanged?.Invoke(
                this,
                new FileChangedEventArgs(
                    watcher.Path,
                    FileChangeType.Modified));
            return;
        }

        var oldOwned = IsPathOwnedByAlbumRoot(
            watcher.Path,
            e.OldFullPath);
        var newOwned = IsPathOwnedByAlbumRoot(
            watcher.Path,
            e.FullPath);

        if (!oldOwned && !newOwned)
            return;

        if (Directory.Exists(e.FullPath))
        {
            if (oldOwned && newOwned)
            {
                DirectoryRenamed?.Invoke(
                    this,
                    new PathRenamedEventArgs(
                        e.OldFullPath,
                        e.FullPath));
            }
            else if (oldOwned)
            {
                DirectoryChanged?.Invoke(
                    this,
                    new FileChangedEventArgs(
                        e.OldFullPath,
                        FileChangeType.Deleted));
            }
            else
            {
                DirectoryChanged?.Invoke(
                    this,
                    new FileChangedEventArgs(
                        e.FullPath,
                        FileChangeType.Created));
            }

            return;
        }

        var oldIsMedia = IsSupportedFile(e.OldFullPath);
        var newIsMedia = IsSupportedFile(e.FullPath);

        if (oldOwned && newOwned && oldIsMedia && newIsMedia)
        {
            FileRenamed?.Invoke(
                this,
                new PathRenamedEventArgs(
                    e.OldFullPath,
                    e.FullPath));
            return;
        }

        if (oldOwned &&
            (oldIsMedia || IsSidecar(e.OldFullPath)))
        {
            FileChanged?.Invoke(
                this,
                new FileChangedEventArgs(
                    e.OldFullPath,
                    FileChangeType.Deleted));
        }

        if (newOwned &&
            (newIsMedia || IsSidecar(e.FullPath)))
        {
            FileChanged?.Invoke(
                this,
                new FileChangedEventArgs(
                    e.FullPath,
                    FileChangeType.Created));
        }
    }

    private static bool IsSidecar(string path) =>
        Path.GetExtension(path).Equals(".xmp", StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();
    }
}

public sealed class FileDiscoveredEventArgs(string filePath) : EventArgs
{
    public string FilePath { get; } = filePath;
}

public sealed class PathRenamedEventArgs(
    string oldPath,
    string newPath) : EventArgs
{
    public string OldPath { get; } = oldPath;
    public string NewPath { get; } = newPath;
}

public sealed class FileChangedEventArgs(string filePath, FileChangeType changeType) : EventArgs
{
    public string FilePath { get; } = filePath;
    public FileChangeType ChangeType { get; } = changeType;
}

public enum FileChangeType
{
    Created,
    Modified,
    Deleted
}

public sealed class ScanProgressEventArgs(int filesFound, string folder, bool isComplete = false) : EventArgs
{
    public int FilesFound { get; } = filesFound;
    public string Folder { get; } = folder;
    public bool IsComplete { get; } = isComplete;
}
