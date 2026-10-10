using Microsoft.UI.Xaml;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.InteropServices;

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
            var catalog = JsonSerializer.Deserialize<AlbumTagCatalog>(json)
                          ?? new AlbumTagCatalog();

            // v1 stored organisational "Groups" separately. In v2 every node is
            // simply a catalog tag; hierarchy is expressed only by slash paths.
            if (catalog.LegacyGroups is { Count: > 0 })
            {
                catalog.SelectedTags = catalog.SelectedTags
                    .Concat(catalog.LegacyGroups)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim().Trim('/'))
                    .Where(value => value.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                catalog.LegacyGroups = null;
                catalog.Version = 2;
                SaveTagCatalog(albumRoot, catalog);
            }
            else if (catalog.Version < 2)
            {
                catalog.Version = 2;
                SaveTagCatalog(albumRoot, catalog);
            }

            return catalog;
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
            Version = Math.Max(2, catalog.Version),
            SelectedTags = catalog.SelectedTags
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim().Trim('/'))
                .Where(tag => tag.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            LegacyGroups = null
        };

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                normalized,
                new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void RenameCatalogPrefix(string oldPrefix, string newPrefix)
    {
        if (!App.HasActiveAlbum || string.IsNullOrWhiteSpace(App.CurrentAlbumPath))
            return;

        oldPrefix = oldPrefix.Trim().Trim('/');
        newPrefix = newPrefix.Trim().Trim('/');
        if (oldPrefix.Length == 0 || newPrefix.Length == 0) return;

        static string ReplacePrefix(string value, string oldPrefix, string newPrefix)
        {
            if (string.Equals(value, oldPrefix, StringComparison.OrdinalIgnoreCase))
                return newPrefix;
            return value.StartsWith(oldPrefix + "/", StringComparison.OrdinalIgnoreCase)
                ? newPrefix + value[oldPrefix.Length..]
                : value;
        }

        var catalog = LoadTagCatalog(App.CurrentAlbumPath);
        catalog.SelectedTags = catalog.SelectedTags
            .Select(value => ReplacePrefix(value, oldPrefix, newPrefix))
            .ToList();
        SaveTagCatalog(App.CurrentAlbumPath, catalog);
    }

    public static void RemoveCatalogPrefix(string prefix)
    {
        if (!App.HasActiveAlbum || string.IsNullOrWhiteSpace(App.CurrentAlbumPath))
            return;

        prefix = prefix.Trim().Trim('/');
        if (prefix.Length == 0) return;

        var catalog = LoadTagCatalog(App.CurrentAlbumPath);
        catalog.SelectedTags.RemoveAll(value =>
            string.Equals(value, prefix, StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase));
        SaveTagCatalog(App.CurrentAlbumPath, catalog);
    }

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

    public static Task<string?> PickAlbumFolderAsync(Window owner)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(owner);
        var dialogType = Type.GetTypeFromCLSID(
            new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7"),
            throwOnError: true)
            ?? throw new InvalidOperationException(
                "Windows folder picker COM type is unavailable.");
        var dialog = (IFileDialog)(
            Activator.CreateInstance(dialogType)
            ?? throw new InvalidOperationException(
                "Windows folder picker could not be created."));

        try
        {
            dialog.SetOptions(
                FosPickFolders |
                FosForceFileSystem |
                FosPathMustExist |
                FosNoChangeDir);
            dialog.SetTitle("Select album folder");
            dialog.SetOkButtonLabel("Select album");

            var initialPath = App.HasActiveAlbum &&
                              !string.IsNullOrWhiteSpace(App.CurrentAlbumPath)
                ? App.CurrentAlbumPath
                : Environment.GetFolderPath(
                    Environment.SpecialFolder.MyPictures);

            if (!string.IsNullOrWhiteSpace(initialPath) &&
                Directory.Exists(initialPath))
            {
                var iid = typeof(IShellItem).GUID;
                if (SHCreateItemFromParsingName(
                        initialPath,
                        IntPtr.Zero,
                        ref iid,
                        out var initialItem) == 0)
                {
                    try
                    {
                        dialog.SetFolder(initialItem);
                    }
                    finally
                    {
                        Marshal.FinalReleaseComObject(initialItem);
                    }
                }
            }

            var result = dialog.Show(hwnd);
            if (result == ErrorCancelled)
                return Task.FromResult<string?>(null);

            if (result < 0)
                Marshal.ThrowExceptionForHR(result);

            dialog.GetResult(out var shellItem);
            try
            {
                shellItem.GetDisplayName(SigdnFileSystemPath, out var pathPtr);
                try
                {
                    var path = Marshal.PtrToStringUni(pathPtr);
                    return Task.FromResult(
                        string.IsNullOrWhiteSpace(path)
                            ? null
                            : EnsureAlbum(path));
                }
                finally
                {
                    if (pathPtr != IntPtr.Zero)
                        Marshal.FreeCoTaskMem(pathPtr);
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(shellItem);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(dialog);
        }
    }

    private const uint FosNoChangeDir = 0x00000008;
    private const uint FosPickFolders = 0x00000020;
    private const uint FosForceFileSystem = 0x00000040;
    private const uint FosPathMustExist = 0x00000800;
    private const uint SigdnFileSystemPath = 0x80058000;
    private const int ErrorCancelled = unchecked((int)0x800704C7);

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
    private interface IFileDialog
    {
        [PreserveSig]
        int Show(IntPtr parent);

        void SetFileTypes(uint count, IntPtr filterSpec);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(IntPtr events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        void GetOptions(out uint options);
        void SetDefaultFolder(IShellItem shellItem);
        void SetFolder(IShellItem shellItem);
        void GetFolder(out IShellItem shellItem);
        void GetCurrentSelection(out IShellItem shellItem);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem shellItem);
        void AddPlace(IShellItem shellItem, uint alignment);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
        void Close(int hresult);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr filter);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    private interface IShellItem
    {
        void BindToHandler(
            IntPtr bindContext,
            ref Guid handler,
            ref Guid interfaceId,
            out IntPtr result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint displayNameType, out IntPtr name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }

    [DllImport(
        "shell32.dll",
        CharSet = CharSet.Unicode,
        PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string path,
        IntPtr bindContext,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem shellItem);

    public static void RestartForAlbum(string albumRoot)
    {
        var escaped = albumRoot.Replace("\"", "\\\"");
        Microsoft.Windows.AppLifecycle.AppInstance.Restart($"--album \"{escaped}\"");
    }
}


public sealed class AlbumTagCatalog
{
    public int Version { get; set; } = 2;
    public List<string> SelectedTags { get; set; } = [];

    // Read-only migration hook for v1 tags.json files. New files never write
    // a separate Groups collection.
    [JsonPropertyName("Groups")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? LegacyGroups { get; set; }
}
