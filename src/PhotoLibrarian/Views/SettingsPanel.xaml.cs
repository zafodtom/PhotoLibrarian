using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoLibrarian.Services;
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
        InstalledVersionText.Text = GitHubUpdateService.InstalledTag.TrimStart('v');
        _loadingUpdateChannel = true;
        try { DevelopmentUpdatesToggle.IsOn = GitHubUpdateService.DevelopmentUpdatesEnabled; }
        finally { _loadingUpdateChannel = false; }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ResolveUpdateDecision(false);
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

    private bool _checkingUpdates;
    private TaskCompletionSource<bool>? _updateDecision;
    private bool _loadingUpdateChannel;

    private Task<bool> ConfirmUpdateAsync(
        string title, string description, string acceptLabel)
    {
        if (_updateDecision is not null)
            throw new InvalidOperationException("An update confirmation is already visible.");

        UpdateConfirmTitle.Text = title;
        UpdateConfirmDescription.Text = description;
        UpdateAcceptButton.Content = acceptLabel;
        UpdateConfirmOverlay.Visibility = Visibility.Visible;
        _updateDecision = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        UpdateAcceptButton.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
        return _updateDecision.Task;
    }

    private void ResolveUpdateDecision(bool accepted)
    {
        UpdateConfirmOverlay.Visibility = Visibility.Collapsed;
        var pending = _updateDecision;
        _updateDecision = null;
        pending?.TrySetResult(accepted);
    }

    private void OnUpdateAcceptClick(object sender, RoutedEventArgs e) =>
        ResolveUpdateDecision(true);

    private void OnUpdateCancelClick(object sender, RoutedEventArgs e) =>
        ResolveUpdateDecision(false);

    private void OnDevelopmentUpdatesToggled(object sender, RoutedEventArgs e)
    {
        if (_loadingUpdateChannel) return;
        try
        {
            GitHubUpdateService.DevelopmentUpdatesEnabled = DevelopmentUpdatesToggle.IsOn;
            UpdateStatusText.Text = DevelopmentUpdatesToggle.IsOn
                ? "Development update channel enabled (manual checks only)."
                : "Stable update channel enabled.";
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = "Could not save update preference: " + ex.Message;
        }
    }

    private async void OnCheckForUpdatesClick(object sender, RoutedEventArgs e)
    {
        if (_checkingUpdates) return;
        _checkingUpdates = true;
        CheckUpdatesButton.IsEnabled = false;
        UpdateDownloadProgress.Visibility = Visibility.Collapsed;
        var job = App.ViewModel?.BeginStatusJob("UPDATES // checking selected GitHub release channel");
        try
        {
            UpdateStatusText.Text = "Checking public PhotoLibrarian releases…";
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var release = await GitHubUpdateService.CheckAsync(
                cancellation.Token, DevelopmentUpdatesToggle.IsOn);
            if (release is null)
            {
                UpdateStatusText.Text = "No newer release is available in the selected channel.";
                return;
            }

            UpdateStatusText.Text = $"{release.Tag} {(release.IsDevelopment ? "(development)" : "(stable)")} available.";
            if (!await ConfirmUpdateAsync(
                $"UPDATE AVAILABLE // {release.Tag}",
                (release.IsDevelopment ? "This is a DEVELOPMENT BUILD and may be unstable. " : "") +
                "Download the official PhotoLibrarian installer from GitHub and verify SHA-256? " +
                "Your photographs, catalogs and album data will not be removed.",
                "DOWNLOAD UPDATE"))
                return;

            UpdateDownloadProgress.Value = 0;
            UpdateDownloadProgress.Visibility = Visibility.Visible;
            var progress = new Progress<int>(percent =>
            {
                UpdateDownloadProgress.Value = percent;
                UpdateStatusText.Text = $"Downloading verified update: {percent}%";
                if (job.HasValue)
                    App.ViewModel?.UpdateStatusJob(job.Value, $"UPDATES // download {percent}%");
            });
            using var downloadCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            var installerPath = await GitHubUpdateService.DownloadVerifiedAsync(
                release, progress, downloadCancellation.Token);
            UpdateStatusText.Text = "SHA-256 verified // Ready to install.";

            if (!await ConfirmUpdateAsync(
                "INSTALL VERIFIED UPDATE",
                "PhotoLibrarian will close. A separate Trimenor update window will install " +
                "the new version without the Inno Setup wizard and reopen this application. " +
                "Save any outstanding changes before continuing. Windows may ask for administrator approval.",
                "INSTALL & RESTART"))
                return;

            // Start the helper before closing the app. It waits for this process
            // to exit, independently verifies the installer and then runs Inno
            // silently with a custom progress window.
            GitHubUpdateService.LaunchUpdateHelper(
                release, installerPath, App.CurrentAlbumPath);
            App.Current.Exit();
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = "Update failed: " + ex.Message;
            System.Diagnostics.Debug.WriteLine("[UPDATES] " + ex);
        }
        finally
        {
            if (job.HasValue) App.ViewModel?.EndStatusJob(job.Value);
            _checkingUpdates = false;
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    // Album JPEG conversion is a deliberate one-shot action, never automatic.
    private CancellationTokenSource? _jpegConversionCancellation;
    private AlbumJpegConversionPreview? _jpegPreview;
    private string? _jpegPreviewAlbum;
    private bool _jpegConversionBusy;

    private async void OnPreviewJpegConversion(object sender, RoutedEventArgs e)
    {
        if (!EnsureAlbumOpen(JpegConversionStatusText) || _jpegConversionBusy || _clearAlbumBusy)
            return;

        var root = App.CurrentAlbumPath!;
        _jpegPreview = null;
        _jpegPreviewAlbum = null;
        PreviewJpegConversionButton.IsEnabled = false;
        RunJpegConversionButton.IsEnabled = false;
        JpegConversionStatusText.Text = "Scanning album for supported images…";
        try
        {
            var preview = await Task.Run(() => AlbumJpegConversionService.Preview(root));
            if (!string.Equals(root, App.CurrentAlbumPath, StringComparison.OrdinalIgnoreCase))
            {
                JpegConversionStatusText.Text = "Album changed; scan again.";
                return;
            }
            _jpegPreview = preview;
            _jpegPreviewAlbum = root;
            RunJpegConversionButton.IsEnabled = preview.PendingCount > 0;
            JpegConversionStatusText.Text =
                $"Ready: {preview.ConvertCount:N0} photos to JPEG, " +
                $"{preview.JpegMetadataCount:N0} existing JPEG XMP merges; " +
                $"{preview.TargetConflictCount:N0} destination conflicts (will be skipped). " +
                "Originals will be retained as backups.";
        }
        catch (Exception ex)
        {
            JpegConversionStatusText.Text = $"Scan failed: {ex.Message}";
        }
        finally
        {
            PreviewJpegConversionButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Album maintenance confirmations use the same dark FUI surfaces, accent
    /// typography and rectangular controls as the rest of PhotoLibrarian.
    /// Never show the default white/rounded WinUI ContentDialog for these jobs.
    /// </summary>
    private ContentDialog CreateAlbumMaintenanceDialog(
        string heading, string description, string confirmLabel, bool destructive)
    {
        var color = destructive
            ? Windows.UI.Color.FromArgb(255, 255, 110, 120)
            : Windows.UI.Color.FromArgb(255, 83, 215, 232);
        var accent = new Microsoft.UI.Xaml.Media.SolidColorBrush(color);
        var primary = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Windows.UI.Color.FromArgb(255, 230, 247, 250));
        var surface = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Windows.UI.Color.FromArgb(255, 12, 19, 27));
        var outerSurface = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Windows.UI.Color.FromArgb(255, 7, 11, 16));

        var body = new StackPanel { Spacing = 13, MinWidth = 380, MaxWidth = 525 };
        body.Children.Add(new TextBlock
        {
            Text = destructive ? "ALBUM MAINTENANCE // CAUTION" : "ALBUM MAINTENANCE // CONFIRM",
            Foreground = accent,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 11,
            CharacterSpacing = 140
        });
        body.Children.Add(new TextBlock
        {
            Text = heading,
            Foreground = primary,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 17,
            TextWrapping = TextWrapping.Wrap
        });
        body.Children.Add(new Border { Height = 2, Background = accent });
        body.Children.Add(new TextBlock
        {
            Text = description,
            Foreground = primary,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap
        });
        body.Children.Add(new TextBlock
        {
            Text = "BACKUP RECOMMENDED  //  REVIEW RESULTS BEFORE DELETING BACKUPS",
            Foreground = accent,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap
        });

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = null,
            Content = new Border
            {
                Padding = new Thickness(20),
                Background = surface,
                BorderBrush = accent,
                BorderThickness = new Thickness(1),
                Child = body
            },
            PrimaryButtonText = confirmLabel,
            CloseButtonText = "ABORT",
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = ElementTheme.Dark,
            Background = outerSurface,
            BorderBrush = accent,
            BorderThickness = new Thickness(1)
        };
        // Restyle actual dialog buttons (not just its content border).
        if (Application.Current.Resources.TryGetValue(
                "HiTechPrimaryButtonStyle", out var primaryStyle) &&
            primaryStyle is Style buttonStyle)
            dialog.PrimaryButtonStyle = buttonStyle;
        if (Application.Current.Resources.TryGetValue(
                "HiTechIconButtonStyle", out var secondaryStyle) &&
            secondaryStyle is Style closeStyle)
            dialog.CloseButtonStyle = closeStyle;
        return dialog;
    }

    private async void OnRunJpegConversion(object sender, RoutedEventArgs e)
    {
        if (_jpegConversionBusy || _clearAlbumBusy || _jpegPreview is null || _jpegPreview.PendingCount == 0 ||
            !EnsureAlbumOpen(JpegConversionStatusText))
            return;

        var root = App.CurrentAlbumPath!;
        if (!string.Equals(root, _jpegPreviewAlbum, StringComparison.OrdinalIgnoreCase))
        {
            JpegConversionStatusText.Text = "Album has changed. Scan it again first.";
            RunJpegConversionButton.IsEnabled = false;
            return;
        }

        var dialog = CreateAlbumMaintenanceDialog(
            "CONVERT TO JPEG",
            $"Convert {_jpegPreview.ConvertCount:N0} photo(s) and embed XMP " +
            $"into {_jpegPreview.JpegMetadataCount:N0} existing JPEG(s)?\n\n" +
            "JPEG conversion is lossy. Original photos and XMP sidecars " +
            "will be moved to a timestamped backup inside .album. " +
            "Existing JPEG destinations will NOT be overwritten. " +
            "Embedded EXIF and other metadata in non-JPEG sources may " +
            "not all survive transcoding; retain backups until inspected.\n\n" +
            "The operation cannot be automatically undone. " +
            "Keep a separate album backup before continuing.",
            "CONVERT ALBUM",
            destructive: false);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        _jpegConversionBusy = true;
        _jpegConversionCancellation = new CancellationTokenSource();
        PreviewJpegConversionButton.IsEnabled = false;
        RunJpegConversionButton.IsEnabled = false;
        CancelJpegConversionButton.IsEnabled = true;
        JpegConversionProgressBar.Value = 0;
        JpegConversionProgressBar.Visibility = Visibility.Visible;
        try
        {
            App.ViewModel?.PauseBackgroundIndexing();
            App.ViewModel?.PauseRecognitionProcessing();
            var progress = new Progress<AlbumJpegConversionProgress>(state =>
            {
                JpegConversionProgressBar.Value = state.Total == 0
                    ? 100 : 100.0 * state.Processed / state.Total;
                JpegConversionStatusText.Text =
                    $"JPEG // {state.Processed:N0}/{state.Total:N0}  " +
                    $"// {state.Failed:N0} failed  // {state.FileName}";
            });

            var result = await AlbumJpegConversionService.RunAsync(
                root, progress, _jpegConversionCancellation.Token,
                async (original, converted) =>
                {
                    if (App.ViewModel is { } main)
                        await main.RemapConvertedImagePathAsync(original, converted);
                });
            JpegConversionStatusText.Text =
                $"Complete: {result.Converted:N0} images converted; " +
                $"{result.EmbeddedXmp:N0} XMP packets embedded; " +
                $"{result.Failed:N0} failed, {result.Skipped:N0} skipped. " +
                $"Backup: {result.BackupDirectory}" +
                (result.Errors.Count > 0
                    ? "\nErrors: " + string.Join("; ", result.Errors.Take(3))
                    : "");
        }
        catch (OperationCanceledException)
        {
            JpegConversionStatusText.Text =
                "Conversion cancelled. Completed photos stay converted; " +
                "their originals remain in the backup folder.";
        }
        catch (Exception ex)
        {
            JpegConversionStatusText.Text = $"Conversion failed: {ex.Message}";
        }
        finally
        {
            _jpegConversionCancellation?.Dispose();
            _jpegConversionCancellation = null;
            _jpegConversionBusy = false;
            CancelJpegConversionButton.IsEnabled = false;
            PreviewJpegConversionButton.IsEnabled = true;
            _jpegPreview = null;
            _jpegPreviewAlbum = null;
            try
            {
                if (App.ViewModel is { } main &&
                    string.Equals(root, App.CurrentAlbumPath, StringComparison.OrdinalIgnoreCase))
                    await main.RefreshFilesystemUiAsync();
            }
            catch (Exception ex)
            {
                JpegConversionStatusText.Text +=
                    $"\nAlbum index refresh failed: {ex.Message}. Use the Refresh command.";
            }
        }
    }

    private void OnCancelJpegConversion(object sender, RoutedEventArgs e)
    {
        CancelJpegConversionButton.IsEnabled = false;
        _jpegConversionCancellation?.Cancel();
    }

    private CancellationTokenSource? _clearAlbumCancellation;
    private bool _clearAlbumBusy;

    private async void OnClearAlbumMetadata(object sender, RoutedEventArgs e)
    {
        if (_clearAlbumBusy || _jpegConversionBusy || App.ViewModel is null ||
            !EnsureAlbumOpen(ClearAlbumStatusText))
            return;
        var root = App.CurrentAlbumPath!;
        ClearAlbumMetadataButton.IsEnabled = false;
        int photoCount = 0;
        ClearAlbumStatusText.Text = "Scanning all album folders for image files…";
        try
        {
            using var scanner = new FolderScannerService();
            await foreach (var path in scanner.ScanFolderAsync(root, includeSubfolders: true))
            {
                if (!FolderScannerService.IsVideoFile(path))
                    photoCount++;
            }
        }
        catch (Exception ex)
        {
            ClearAlbumStatusText.Text = "Album scan failed: " + ex.Message;
            ClearAlbumMetadataButton.IsEnabled = true;
            return;
        }
        ClearAlbumMetadataButton.IsEnabled = true;
        if (!string.Equals(root, App.CurrentAlbumPath, StringComparison.OrdinalIgnoreCase))
        {
            ClearAlbumStatusText.Text = "Active album changed; scan again.";
            return;
        }
        // A separate dialog avoids accidental single-click mass deletion.
        var dialog = CreateAlbumMaintenanceDialog(
            "CLEAR ALBUM TAGS + FLAGS",
            $"Remove all assigned tags and user flags from {photoCount:N0} " +
            "photo(s), plus saved catalog tag names, in the ACTIVE album?\n\n" +
            "Ratings, dates, captions, face data and photos are preserved. " +
            "Automatic tagging will be switched OFF to prevent tags from returning. " +
            "The original image and XMP files will be backed up under " +
            ".album/metadata-reset-backups before each modification.\n\n" +
            "This is an album-wide operation and cannot be undone " +
            "automatically. Keep a separate backup before proceeding.",
            "CLEAR TAGS AND FLAGS",
            destructive: true);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;
        if (!string.Equals(root, App.CurrentAlbumPath, StringComparison.OrdinalIgnoreCase))
        {
            ClearAlbumStatusText.Text = "Album changed; reset cancelled.";
            return;
        }
        _clearAlbumBusy = true;
        _clearAlbumCancellation = new CancellationTokenSource();
        ClearAlbumMetadataButton.IsEnabled = false;
        CancelClearAlbumButton.IsEnabled = true;
        RunJpegConversionButton.IsEnabled = false;
        PreviewJpegConversionButton.IsEnabled = false;
        ClearAlbumProgressBar.Visibility = Visibility.Visible;
        ClearAlbumProgressBar.Value = 0;
        try
        {
            var progress = new Progress<AlbumMetadataClearProgress>(state =>
            {
                ClearAlbumProgressBar.Value = state.Total == 0
                    ? 100 : 100.0 * state.Processed / state.Total;
                ClearAlbumStatusText.Text =
                    $"RESET // {state.Processed:N0}/{state.Total:N0} " +
                    $"// {state.Failed:N0} failed  // {state.FileName}";
            });
            var result = await App.ViewModel.ClearActiveAlbumTagsAndFlagsAsync(
                progress, _clearAlbumCancellation.Token);
            ClearAlbumStatusText.Text =
                $"Finished: {result.Cleaned:N0} photos cleaned, {result.Failed:N0} failed. " +
                (result.Failed > 0
                    ? "PARTIAL RESET: tag catalog was preserved. " +
                      "Review reset-errors.txt in the backup directory. "
                    : "All processed photos passed; tag catalog cleared. ") +
                $"Backup: {result.BackupDirectory}" +
                (result.Errors.Count > 0
                    ? "\nFirst errors: " + string.Join("; ", result.Errors.Take(3))
                    : "");
        }
        catch (OperationCanceledException)
        {
            ClearAlbumStatusText.Text =
                "Reset cancelled; successfully cleaned photos stay cleaned. " +
                "Keep backups for recovery.";
        }
        catch (Exception ex)
        {
            ClearAlbumStatusText.Text = "Album reset failed: " + ex.Message;
        }
        finally
        {
            _clearAlbumCancellation?.Dispose();
            _clearAlbumCancellation = null;
            _clearAlbumBusy = false;
            CancelClearAlbumButton.IsEnabled = false;
            ClearAlbumMetadataButton.IsEnabled = true;
            PreviewJpegConversionButton.IsEnabled = true;
            RunJpegConversionButton.IsEnabled = false;
        }
    }

    private void OnCancelClearAlbum(object sender, RoutedEventArgs e)
    {
        CancelClearAlbumButton.IsEnabled = false;
        _clearAlbumCancellation?.Cancel();
    }

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
            Text = "CONFIRMATION REQUIRED  //  ORIGINAL PHOTOS ARE PRESERVED",
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 11,
            Foreground = red
        });
        var dialog = new ContentDialog
        {
            Title = null,
            Content = new Border
            {
                Padding = new Thickness(22),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 12, 17, 26)),
                BorderBrush = red,
                BorderThickness = new Thickness(2),
                Child = panel
            },
            PrimaryButtonText = "DELETE ALBUM",
            CloseButtonText = "ABORT",
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = ElementTheme.Dark,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 9, 16, 24)),
            BorderBrush = red,
            BorderThickness = new Thickness(1),
            XamlRoot = XamlRoot
        };
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
            var errorPanel = new StackPanel { Spacing = 12, MinWidth = 450 };
            errorPanel.Children.Add(new TextBlock
            {
                Text = "FATAL ERROR // ALBUM TERMINATION FAILED",
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                FontSize = 17,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = red
            });
            errorPanel.Children.Add(new Border { Height = 3, Background = red });
            errorPanel.Children.Add(new TextBlock
            {
                Text = ex.Message,
                Foreground = text,
                TextWrapping = TextWrapping.Wrap
            });
            errorPanel.Children.Add(new TextBlock
            {
                Text = "ALBUM DATA MAY BE PARTIALLY REMOVED. RESTART BEFORE RETRYING.",
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                Foreground = red,
                TextWrapping = TextWrapping.Wrap
            });
            var error = new ContentDialog
            {
                Title = null,
                Content = new Border
                {
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Windows.UI.Color.FromArgb(255, 12, 17, 26)),
                    BorderBrush = red,
                    BorderThickness = new Thickness(2),
                    Padding = new Thickness(20),
                    Child = errorPanel
                },
                CloseButtonText = "ACKNOWLEDGE",
                RequestedTheme = ElementTheme.Dark,
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 9, 16, 24)),
                BorderBrush = red,
                BorderThickness = new Thickness(1),
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
