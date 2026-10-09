using Microsoft.UI.Xaml;
using System.Text.Json;
using Windows.Storage.Pickers;

namespace PhotoLibrarian;

public static class AlbumService
{
    public const string AlbumFolderName = ".album";
    public const string AlbumManifestName = "album.json";
    public const string AlbumCacheName = "cache.db";

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

        return root;
    }

    public static string GetAlbumCachePath(string albumRoot) =>
        Path.Combine(albumRoot, AlbumFolderName, AlbumCacheName);

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
        var escaped = albumRoot.Replace(""", "\"");
        Microsoft.Windows.AppLifecycle.AppInstance.Restart($"--album \"{escaped}\"");
    }
}
