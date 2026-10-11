using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoLibrarian.Core.Services;
using PhotoLibrarian.ViewModels;

namespace PhotoLibrarian.Views;

public sealed partial class SettingsPanel : UserControl
{
    private SettingsViewModel? ViewModel =>
        App.ViewModel?.Settings;

    public SettingsPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
            return;

        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateCacheDisplay();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingsViewModel.ThumbnailCacheSizeMB) or
            nameof(SettingsViewModel.CacheLocation))
        {
            DispatcherQueue.TryEnqueue(UpdateCacheDisplay);
        }
    }

    private void UpdateCacheDisplay()
    {
        if (ViewModel is null)
            return;

        CacheSizeText.Text =
            $"Cache size: {ViewModel.ThumbnailCacheSizeMB} MB";
        CachePathText.Text = ViewModel.CacheLocation;
    }

    private async void OnImportDigiKam(
        object sender,
        RoutedEventArgs e)
    {
        if (!EnsureAlbumOpen(DigiKamImportStatusText))
            return;

        var file = await PickDatabaseAsync(
            [".db", ".sqlite", ".sqlite3"]);
        if (file is null)
            return;

        SetDigiKamBusy(true);
        DigiKamImportProgressBar.Value = 0;
        DigiKamImportStatusText.Text = $"Reading {file.Name}…";

        try
        {
            var progress = new Progress<DigiKamImportProgress>(value =>
            {
                DigiKamImportProgressBar.Value =
                    value.Total > 0
                        ? 100d * value.Processed / value.Total
                        : 0;

                DigiKamImportStatusText.Text =
                    $"Importing {value.Processed:N0} / {value.Total:N0} — " +
                    $"{value.Matched:N0} matched, " +
                    $"{value.Unmatched:N0} unmatched, " +
                    $"{value.Ambiguous:N0} ambiguous";
            });

            var result = await App.ViewModel.ImportFromDigiKamAsync(
                file.Path,
                progress);

            var details = new List<string>
            {
                $"{result.MatchedImages:N0} of {result.SourceImages:N0} digiKam items matched",
                $"{result.TagAssignments:N0} tag assignments imported",
                $"{result.CaptionsImported:N0} captions imported"
            };

            AppendImportWarnings(
                details,
                result.UnmatchedImages,
                result.AmbiguousImages,
                result.PersistentWriteFailures);

            DigiKamImportProgressBar.Value = 100;
            DigiKamImportStatusText.Text =
                string.Join(" • ", details) +
                BuildExamples(
                    result.UnmatchedExamples,
                    result.AmbiguousExamples);
        }
        catch (Exception exception)
            when (IsImportException(exception))
        {
            DigiKamImportStatusText.Text =
                $"digiKam import failed: {exception.Message}";
        }
        finally
        {
            SetDigiKamBusy(false);
        }
    }

    private async void OnImportStash(
        object sender,
        RoutedEventArgs e)
    {
        if (!EnsureAlbumOpen(StashImportStatusText))
            return;

        var file = await PickDatabaseAsync(
            [".sqlite", ".db", ".sqlite3", ".zip"]);
        if (file is null)
            return;

        SetStashBusy(true);
        StashImportProgressBar.Value = 0;
        StashImportStatusText.Text = $"Reading {file.Name}…";

        try
        {
            var progress = new Progress<StashImportProgress>(value =>
            {
                StashImportProgressBar.Value =
                    value.Total > 0
                        ? 100d * value.Processed / value.Total
                        : 0;

                StashImportStatusText.Text =
                    $"Importing {value.Processed:N0} / {value.Total:N0} — " +
                    $"{value.Matched:N0} matched, " +
                    $"{value.Unmatched:N0} unmatched, " +
                    $"{value.Ambiguous:N0} ambiguous";
            });

            var result = await App.ViewModel.ImportFromStashAsync(
                file.Path,
                progress);

            var details = new List<string>
            {
                $"{result.MatchedImages:N0} of {result.SourceImages:N0} Stash images matched",
                $"{result.TagAssignments:N0} tag assignments imported"
            };

            AppendImportWarnings(
                details,
                result.UnmatchedImages,
                result.AmbiguousImages,
                result.PersistentWriteFailures);

            StashImportProgressBar.Value = 100;
            StashImportStatusText.Text =
                string.Join(" • ", details) +
                BuildExamples(
                    result.UnmatchedExamples,
                    result.AmbiguousExamples);
        }
        catch (Exception exception)
            when (IsImportException(exception))
        {
            StashImportStatusText.Text =
                $"Stash import failed: {exception.Message}";
        }
        finally
        {
            SetStashBusy(false);
        }
    }

    private bool EnsureAlbumOpen(TextBlock status)
    {
        if (App.HasActiveAlbum &&
            !string.IsNullOrWhiteSpace(App.CurrentAlbumPath))
        {
            return true;
        }

        status.Text =
            "Open an album before importing metadata.";
        return false;
    }

    private static async Task<Windows.Storage.StorageFile?> PickDatabaseAsync(
        IEnumerable<string> extensions)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker
        {
            SuggestedStartLocation =
                Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary
        };

        foreach (var extension in extensions)
            picker.FileTypeFilter.Add(extension);

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(
            App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        return await picker.PickSingleFileAsync();
    }

    private void SetDigiKamBusy(bool busy)
    {
        ImportDigiKamButton.IsEnabled = !busy;
        DigiKamImportProgressRing.IsActive = busy;
        DigiKamImportProgressRing.Visibility =
            busy ? Visibility.Visible : Visibility.Collapsed;
        DigiKamImportProgressBar.Visibility =
            busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetStashBusy(bool busy)
    {
        ImportStashButton.IsEnabled = !busy;
        StashImportProgressRing.IsActive = busy;
        StashImportProgressRing.Visibility =
            busy ? Visibility.Visible : Visibility.Collapsed;
        StashImportProgressBar.Visibility =
            busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void AppendImportWarnings(
        ICollection<string> details,
        int unmatched,
        int ambiguous,
        int persistentWriteFailures)
    {
        if (unmatched > 0)
            details.Add($"{unmatched:N0} unmatched");
        if (ambiguous > 0)
            details.Add($"{ambiguous:N0} ambiguous");
        if (persistentWriteFailures > 0)
        {
            details.Add(
                $"{persistentWriteFailures:N0} files could not persist metadata");
        }
    }

    private static string BuildExamples(
        IReadOnlyList<string> unmatched,
        IReadOnlyList<string> ambiguous)
    {
        var lines = new List<string>();

        if (unmatched.Count > 0)
        {
            lines.Add(
                "Unmatched examples: " +
                string.Join(", ", unmatched));
        }

        if (ambiguous.Count > 0)
        {
            lines.Add(
                "Ambiguous examples: " +
                string.Join(", ", ambiguous));
        }

        return lines.Count == 0
            ? string.Empty
            : Environment.NewLine +
              string.Join(Environment.NewLine, lines);
    }

    private static bool IsImportException(Exception exception) =>
        exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            Microsoft.Data.Sqlite.SqliteException or
            ArgumentException or
            InvalidOperationException;

    private async void OnRebuildCache(
        object sender,
        RoutedEventArgs e)
    {
        if (ViewModel?.RebuildCacheCommand.CanExecute(null) == true)
        {
            await ViewModel.RebuildCacheCommand.ExecuteAsync(null);
            UpdateCacheDisplay();
        }
    }

    private async void OnDeleteAlbum(object sender, RoutedEventArgs e)
    {
        await ConfirmDeleteAlbumAsync();
    }

    public async Task ConfirmDeleteAlbumAsync()
    {
        if (!App.HasActiveAlbum || string.IsNullOrWhiteSpace(App.CurrentAlbumPath))
            return;

        var root = App.CurrentAlbumPath;
        var xmpCount = AlbumService.FindAlbumXmpSidecars(root).Count;
        var red = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Windows.UI.Color.FromArgb(255, 255, 80, 94));
        var text = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Windows.UI.Color.FromArgb(255, 229, 240, 244));
        var panel = new StackPanel { Spacing = 14, MinWidth = 500 };
        panel.Children.Add(new TextBlock
        {
            Text = "WARNING // ARCHIVE TERMINATION",
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 19, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = red
        });
        panel.Children.Add(new Border
        {
            Height = 3, Background = red
        });
        panel.Children.Add(new TextBlock
        {
            Text = "CRITICAL OPERATION  //  IRREVERSIBLE",
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 13, Foreground = red
        });
        panel.Children.Add(new TextBlock
        {
            Text = "TARGET: " + root,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap, Foreground = text
        });
        panel.Children.Add(new TextBlock
        {
            Text = "PhotoLibrarian catalog, cache and matching album launchers will be deleted. " +
                   $"All {xmpCount} XMP sidecar file(s) within this album will also be deleted, " +
                   "including XMP originally created by other software. " +
                   "Original image/video files and folders remain. Embedded metadata in images " +
                   "is NOT reverted. Other standalone albums are excluded. The app will close.",
            TextWrapping = TextWrapping.Wrap, Foreground = text
        });
        panel.Children.Add(new TextBlock
        {
            Text = "TYPE DELETE TO AUTHORIZE TERMINATION",
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            Foreground = red
        });
        var phrase = new TextBox { PlaceholderText = "DELETE", MaxLength = 6 };
        panel.Children.Add(phrase);
        var dialog = new ContentDialog
        {
            Title = null,
            Content = new Border
            {
                Padding = new Thickness(22),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 22, 7, 13)),
                BorderBrush = red, BorderThickness = new Thickness(2),
                Child = panel
            },
            PrimaryButtonText = "CONFIRM DELETE",
            CloseButtonText = "ABORT",
            IsPrimaryButtonEnabled = false,
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = ElementTheme.Dark,
            XamlRoot = XamlRoot
        };
        phrase.TextChanged += (_, _) =>
            dialog.IsPrimaryButtonEnabled = string.Equals(
                phrase.Text, "DELETE", StringComparison.Ordinal);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        DeleteAlbumButton.IsEnabled = false;
        try
        {
            if (App.MainWindow is MainWindow window)
                await window.DeleteCurrentAlbumAsync();
        }
        catch (Exception ex)
        {
            DeleteAlbumButton.IsEnabled = true;
            var error = new ContentDialog
            {
                Title = "FATAL ERROR // ALBUM DELETE ABORTED",
                Content = ex.Message + "\nThe application must be restarted before retrying.",
                CloseButtonText = "ACKNOWLEDGE",
                RequestedTheme = ElementTheme.Dark,
                XamlRoot = XamlRoot
            };
            await error.ShowAsync();
        }
    }

    private async void OnClose(
        object sender,
        RoutedEventArgs e)
    {
        await App.ViewModel.RefreshTagsTreeAsync();
        ViewModel?.CloseCommand.Execute(null);
    }
}
