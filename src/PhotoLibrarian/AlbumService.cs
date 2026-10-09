using Microsoft.UI.Xaml;
using System.Text.Json;
using Windows.Storage.Pickers;

namespace PhotoLibrarian;

public static class AlbumService
{
    public const string AlbumFolderName = ".album";
    public const string AlbumManifestName = "album.json";
    public const string AlbumCacheName = "cache.db";
    public const string AlbumTagsName = "tags.json";

    public static string? GetAlbumPathFromCommandLine()
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.FindIndex(
            args,
            arg => string.Equals(arg, "--album", StringComparison.OrdinalIgnoreCase));

        if (index < 0 || index + 1 >= args.Length)
            return null;

        var candidate = args[index + 1].Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        return Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static string EnsureAlbum(string rootPath)
    {
        var root = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Album folder not found: {root}");

        var albumDir = Path.Combine(root, AlbumFolderName);
        Directory.CreateDirectory(albumDir);

        var manifestPath = Path.Combine(albumDir, AlbumManifestName);
        if (!File.Exists(manifestPath))
        {
            var manifest = new
            {
                version = 1,
                name = new DirectoryInfo(root).Name,
                createdUtc = DateTime.UtcNow
            };

            File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(
                    manifest,
                    new JsonSerializerOptions { WriteIndented = true }));
        }

        var tagsPath = Path.Combine(albumDir, AlbumTagsName);
        if (!File.Exists(tagsPath))
        {
            File.WriteAllText(
                tagsPath,
                JsonSerializer.Serialize(
                    new AlbumTagCatalog(),
                    new JsonSerializerOptions { WriteIndented = true }));
        }

        return root;
    }

    public static string GetAlbumCachePath(string albumRoot) =>
        Path.Combine(albumRoot, AlbumFolderName, AlbumCacheName);

    public static string GetAlbumTagsPath(string albumRoot) =>
        Path.Combine(albumRoot, AlbumFolderName, AlbumTagsName);

    public static AlbumTagCatalog LoadTagCatalog(string? albumRoot)
    {
        if (string.IsNullOrWhiteSpace(albumRoot))
            return new AlbumTagCatalog();

        var path = GetAlbumTagsPath(albumRoot);
        try
        {
            if (!File.Exists(path))
                return new AlbumTagCatalog();

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AlbumTagCatalog>(json)
                   ?? new AlbumTagCatalog();
        }
        catch
        {
            return new AlbumTagCatalog();
        }
    }

    public static void SaveTagCatalog(string albumRoot, AlbumTagCatalog catalog)
    {
        var path = GetAlbumTagsPath(albumRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var normalized = new AlbumTagCatalog
        {
            Version = Math.Max(1, catalog.Version),
            SelectedTags = catalog.SelectedTags
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim().Trim('/'))
                .Where(tag => tag.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Groups = catalog.Groups
                .Where(group => !string.IsNullOrWhiteSpace(group))
                .Select(group => group.Trim().Trim('/'))
                .Where(group => group.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                normalized,
                new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void AddGroup(string group)
    {
        if (!App.HasActiveAlbum || string.IsNullOrWhiteSpace(App.CurrentAlbumPath))
            return;

        var normalized = group.Trim().Trim('/');
        if (normalized.Length == 0) return;

        var catalog = LoadTagCatalog(App.CurrentAlbumPath);
        if (!catalog.Groups.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            catalog.Groups.Add(normalized);
            SaveTagCatalog(App.CurrentAlbumPath, catalog);
        }
    }

    public static IReadOnlyList<string> GetGroups() =>
        LoadTagCatalog(App.CurrentAlbumPath).Groups
            .OrderBy(group => group, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static void AddSelectedTag(string tag)
    {
        if (!App.HasActiveAlbum || string.IsNullOrWhiteSpace(App.CurrentAlbumPath))
            return;

        var trimmed = tag.Trim();
        var catalog = LoadTagCatalog(App.CurrentAlbumPath);
        if (catalog.SelectedTags.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            return;

        catalog.SelectedTags.Add(trimmed);
        SaveTagCatalog(App.CurrentAlbumPath, catalog);
    }

    public static void RemoveSelectedTag(string tag)
    {
        if (!App.HasActiveAlbum || string.IsNullOrWhiteSpace(App.CurrentAlbumPath))
            return;

        var catalog = LoadTagCatalog(App.CurrentAlbumPath);
        catalog.SelectedTags.RemoveAll(existing =>
            string.Equals(existing, tag, StringComparison.OrdinalIgnoreCase));
        SaveTagCatalog(App.CurrentAlbumPath, catalog);
    }

    public static string PrepareCleanSessionCache()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dataDir = Path.Combine(appData, "PhotoLibrarian");
        Directory.CreateDirectory(dataDir);

        var dbPath = Path.Combine(dataDir, "session-cache.db");
        foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // A stale session cache must never block startup.
            }
        }

        return dbPath;
    }

    public static async Task<string?> PickAlbumFolderAsync(Window owner)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary
        };
        picker.FileTypeFilter.Add("*");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(owner);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
            return null;

        return EnsureAlbum(folder.Path);
    }

    public static void RestartForAlbum(string albumRoot)
    {
        var escaped = albumRoot.Replace("\"", "\\\"");
        Microsoft.Windows.AppLifecycle.AppInstance.Restart($"--album \"{escaped}\"");
    }
}


public sealed class AlbumTagCatalog
{
    public int Version { get; set; } = 1;
    public List<string> SelectedTags { get; set; } = [];
    public List<string> Groups { get; set; } = [];
}
