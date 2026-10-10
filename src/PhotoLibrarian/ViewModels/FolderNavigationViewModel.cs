using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;
using PhotoLibrarian.Core.Data;
using PhotoLibrarian.Core.Services;
using PhotoLibrarian.Diagnostics;
using System.Collections.ObjectModel;

namespace PhotoLibrarian.ViewModels;

public partial class FolderNavigationViewModel : ObservableObject
{
    private readonly CacheDatabase _db;
    private readonly FolderScannerService _scanner;
    private readonly LibraryIndexingService _indexingService;
    private readonly MainViewModel _main;
    private CancellationTokenSource? _indexCts;

    public ObservableCollection<FolderNode> RootFolders { get; } = [];

    [ObservableProperty]
    public partial FolderNode? SelectedFolder { get; set; }

    public FolderNavigationViewModel(
        CacheDatabase db,
        FolderScannerService scanner,
        LibraryIndexingService indexingService,
        MainViewModel main)
    {
        _db = db;
        _scanner = scanner;
        _indexingService = indexingService;
        _main = main;
    }

    public async Task LoadWatchedFoldersAsync()
    {
        RootFolders.Clear();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, path, include_sub FROM watched_folders ORDER BY path";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var storedPath = reader.GetString(1);
            var path = AlbumPathStorage.ToAbsolutePath(storedPath);
            var node = new FolderNode
            {
                Id = reader.GetInt64(0),
                Path = path,
                Name = path, // Show full path for root folders
                IncludeSubfolders = reader.GetInt64(2) == 1,
                IsRootFolder = true
            };
            BuildChildNodes(node);
            RootFolders.Add(node);
        }
        _main.SyncWatchedFolders();
    }

    public static void BuildChildNodes(FolderNode parent)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(parent.Path))
            {
                if (string.Equals(
                    System.IO.Path.GetFileName(dir),
                    PhotoLibrarian.AlbumService.AlbumFolderName,
                    StringComparison.OrdinalIgnoreCase))
                    continue;

                var child = new FolderNode
                {
                    Path = dir,
                    Name = System.IO.Path.GetFileName(dir)
                };
                // Only one level deep initially; expand on demand
                if (Directory.GetDirectories(dir).Length > 0)
                    child.Children.Add(new FolderNode { Name = "Loading…", Path = "" }); // placeholder
                parent.Children.Add(child);
            }
        }
        catch { /* Access denied, etc. */ }
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        DebugLog.WriteLine("AddFolderAsync: Starting folder picker");
        
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
        picker.FileTypeFilter.Add("*");

        // Initialize picker with window handle
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            DebugLog.WriteLine("AddFolderAsync: User cancelled");
            return;
        }

        DebugLog.WriteLine($"AddFolderAsync: User selected '{folder.Path}'");
        await AddOrSelectFolderAsync(folder.Path);
    }

    /// <summary>
    /// Adds a folder without showing the picker and selects it as the active folder.
    /// This is used by the --album command-line mode and can later become the entry
    /// point for folder-contained album configuration.
    /// </summary>
    public async Task<FolderNode?> AddOrSelectFolderAsync(
        string folderPath,
        bool includeSubfolders = true)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return null;

        var normalizedPath = Path.GetFullPath(folderPath.Trim().Trim('"'))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!Directory.Exists(normalizedPath))
        {
            DebugLog.WriteLine($"AddOrSelectFolderAsync: Folder does not exist: '{normalizedPath}'");
            _main.StatusText = $"Album folder not found: {normalizedPath}";
            return null;
        }

        using var conn = _db.CreateConnection();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO watched_folders (path, include_sub)
            VALUES ($path, $includeSub)
            """;
        var storedRootPath = App.HasActiveAlbum
            ? AlbumPathStorage.AlbumRootStoragePath
            : normalizedPath;
        cmd.Parameters.AddWithValue("$path", storedRootPath);
        cmd.Parameters.AddWithValue("$includeSub", includeSubfolders ? 1 : 0);
        var rowsAffected = await cmd.ExecuteNonQueryAsync();

        var shouldIndex = rowsAffected > 0;
        if (App.HasActiveAlbum && !shouldIndex)
        {
            using var countImages = conn.CreateCommand();
            countImages.CommandText = "SELECT COUNT(*) FROM images";
            shouldIndex = Convert.ToInt32(await countImages.ExecuteScalarAsync()) == 0;
        }

        await LoadWatchedFoldersAsync();

        var node = RootFolders.FirstOrDefault(folder =>
            string.Equals(folder.Path, normalizedPath, StringComparison.OrdinalIgnoreCase));
        if (node is not null)
            SelectedFolder = node;

        if (shouldIndex && !App.HasActiveAlbum)
        {
            DebugLog.WriteLine($"AddOrSelectFolderAsync: Starting background indexing for '{normalizedPath}'");
            _indexCts?.Cancel();
            _indexCts = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                try
                {
                    await _indexingService.IndexFolderAsync(
                        normalizedPath,
                        includeSubfolders,
                        _indexCts.Token);
                    App.MainWindow?.DispatcherQueue.TryEnqueue(async () =>
                    {
                        await _main.RefreshAfterIndexAsync();
                        if (node is not null)
                            SelectedFolder = node;
                    });
                }
                catch (Exception ex)
                {
                    DebugLog.WriteLine(
                        $"AddOrSelectFolderAsync [Background]: ERROR - {ex.Message}\n{ex.StackTrace}");
                }
            });
        }

        return node;
    }

    [RelayCommand]
    private async Task RemoveFolderAsync()
    {
        if (SelectedFolder is null) return;

        // Find the root folder for the selected folder
        FolderNode? rootToRemove = null;
        if (SelectedFolder.Id > 0)
        {
            // This is a root folder
            rootToRemove = SelectedFolder;
        }
        else
        {
            // This is a subfolder - find its root
            foreach (var root in RootFolders)
            {
                if (SelectedFolder.Path.StartsWith(root.Path, StringComparison.OrdinalIgnoreCase))
                {
                    rootToRemove = root;
                    break;
                }
            }
        }

        if (rootToRemove is null || rootToRemove.Id <= 0) return;

        using var conn = _db.CreateConnection();

        // Album mode has exactly one root and stores image paths relative to it.
        using var delImages = conn.CreateCommand();
        if (App.HasActiveAlbum)
        {
            delImages.CommandText = "DELETE FROM images";
        }
        else
        {
            delImages.CommandText = "DELETE FROM images WHERE file_path LIKE $prefix || '%'";
            delImages.Parameters.AddWithValue("$prefix", rootToRemove.Path);
        }
        await delImages.ExecuteNonQueryAsync();

        // Remove watched folder
        using var delFolder = conn.CreateCommand();
        delFolder.CommandText = "DELETE FROM watched_folders WHERE id = $id";
        delFolder.Parameters.AddWithValue("$id", rootToRemove.Id);
        await delFolder.ExecuteNonQueryAsync();

        _scanner.StopWatching(rootToRemove.Path);
        await LoadWatchedFoldersAsync();
        await _main.RefreshAfterIndexAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        DebugLog.WriteLine("RefreshAsync: Starting refresh");
        _indexCts?.Cancel();
        _indexCts = new CancellationTokenSource();

        foreach (var folder in RootFolders)
        {
            DebugLog.WriteLine($"RefreshAsync: Starting background indexing for '{folder.Path}'");
            _ = Task.Run(async () =>
            {
                try
                {
                    DebugLog.WriteLine($"RefreshAsync [Background]: Calling IndexFolderAsync for '{folder.Path}'");
                    await _indexingService.IndexFolderAsync(folder.Path, folder.IncludeSubfolders, _indexCts.Token);
                    DebugLog.WriteLine($"RefreshAsync [Background]: IndexFolderAsync completed for '{folder.Path}'");
                    App.MainWindow?.DispatcherQueue.TryEnqueue(async () =>
                    {
                        await _main.RefreshAfterIndexAsync();
                    });
                }
                catch (Exception ex)
                {
                    DebugLog.WriteLine($"RefreshAsync [Background]: ERROR - {ex.Message}");
                }
            });
        }
        
        DebugLog.WriteLine("RefreshAsync: All background tasks started");
    }

    partial void OnSelectedFolderChanged(FolderNode? value)
    {
        DebugLog.WriteLine($"OnSelectedFolderChanged: value={value?.Path ?? "null"}");
        if (value is not null && value.Path.Length > 0)
        {
            // Expand lazy children
            if (value.Children.Count == 1 && value.Children[0].Path == "")
            {
                value.Children.Clear();
                BuildChildNodes(value);
            }

            // Filter image grid to this folder
            DebugLog.WriteLine($"OnSelectedFolderChanged: Calling FilterByFolderAsync with path='{value.Path}'");
            _ = _main.ImageGrid.FilterByFolderAsync(value.Path);
        }
    }
}

public partial class FolderNode : ObservableObject
{
    public long Id { get; set; }
    public required string Name { get; set; }
    public required string Path { get; set; }
    public bool IncludeSubfolders { get; set; }
    public bool IsRootFolder { get; set; }
    public ObservableCollection<FolderNode> Children { get; } = [];

    public string DisplayName => IsRootFolder ? Name : System.IO.Path.GetFileName(Path);
    
    // Use Windows Explorer folder icon
    public string IconGlyph => IsRootFolder ? "\uE8B7" : "\uE8D5"; // Library for root, folder for children
    
    public Windows.UI.Text.FontWeight FontWeight => IsRootFolder 
        ? new Windows.UI.Text.FontWeight { Weight = 600 } // SemiBold
        : new Windows.UI.Text.FontWeight { Weight = 400 }; // Normal
    
    public Microsoft.UI.Xaml.Visibility ShowPathVisibility => IsRootFolder 
        ? Microsoft.UI.Xaml.Visibility.Visible 
        : Microsoft.UI.Xaml.Visibility.Collapsed;

    public override string ToString() => DisplayName;
}
