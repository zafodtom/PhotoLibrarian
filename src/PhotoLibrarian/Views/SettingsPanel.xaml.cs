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

    private bool _loadingRecentTagSettings;

    public SettingsPanel()
    {
        // NumberBox can raise ValueChanged while XAML is still constructing.
        // Bind only after InitializeComponent, when all named controls exist.
        _loadingRecentTagSettings = true;
        InitializeComponent();
        RecentTagCountNumberBox.Value = RecentTagHistoryService.VisibleCount;
        RecentTagCountNumberBox.ValueChanged += OnRecentTagCountChanged;
        _loadingRecentTagSettings = false;
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
        _loadingRecentTagSettings = true;
        try { RecentTagCountNumberBox.Value = RecentTagHistoryService.VisibleCount; }
        finally { _loadingRecentTagSettings = false; }
        RecentTagsSettingsStatusText.Text = App.HasActiveAlbum
            ? "Recent tag history is specific to the active album."
            : "Open an album to clear its recent tag history.";
        ClearRecentTagsButton.IsEnabled = App.HasActiveAlbum;
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
        _thumbnailGenerationCancellation?.Cancel();
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

    private void OnRecentTagCountChanged(
        NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loadingRecentTagSettings || double.IsNaN(args.NewValue)) return;
        var count = Math.Clamp((int)Math.Round(args.NewValue), 0,
            RecentTagHistoryService.MaximumVisibleCount);
        try
        {
            RecentTagHistoryService.SetVisibleCount(count);
            if (sender.Value != count)
            {
                _loadingRecentTagSettings = true;
                try { sender.Value = count; }
                finally { _loadingRecentTagSettings = false; }
            }
            RecentTagsSettingsStatusText.Text =
                $"Showing up to {count} recent tag(s) in the metadata panel.";
        }
        catch (Exception ex)
        {
            RecentTagsSettingsStatusText.Text =
                "Unable to save recent tag count: " + ex.Message;
        }
    }

    private void OnClearRecentTagHistory(object sender, RoutedEventArgs e)
    {
        if (!App.HasActiveAlbum || string.IsNullOrWhiteSpace(App.CurrentAlbumPath))
        {
            RecentTagsSettingsStatusText.Text = "Open an album first.";
            return;
        }

        try
        {
            RecentTagHistoryService.Clear(App.CurrentAlbumPath);
            RecentTagsSettingsStatusText.Text =
                "Recent tag history cleared for the active album. Assigned photo tags were not changed.";
        }
        catch (Exception ex)
        {
            RecentTagsSettingsStatusText.Text = "Unable to clear recent tags: " + ex.Message;
        }
    }

    private void OnNavigateImport(object sender, RoutedEventArgs e) =>
        NavigateToSettingsSection(ImportSectionHeader);

    private void OnNavigateCache(object sender, RoutedEventArgs e) =>
        NavigateToSettingsSection(CacheSectionHeader);

    private void OnNavigateAlbum(object sender, RoutedEventArgs e) =>
        NavigateToSettingsSection(AlbumSectionHeader);

    private void OnNavigateAbout(object sender, RoutedEventArgs e) =>
        NavigateToSettingsSection(AboutSectionHeader);

    private void OnNavigateUiShowcase(object sender, RoutedEventArgs e) =>
        NavigateToSettingsSection(UiShowcaseSectionHeader);

    private void NavigateToSettingsSection(FrameworkElement sectionHeader)
    {
        // StartBringIntoView only scrolls far enough to make an element visible,
        // which can leave its header at the bottom of the viewport.
        // Reserve one viewport of extra space so even ABOUT can reach the top.
        var viewer = SettingsContentScrollViewer;
        SettingsNavigationEndSpacer.Height = Math.Max(0, viewer.ViewportHeight);

        // The content stack is the coordinate space of the scrollable document.
        // ChangeView uses an absolute scroll offset rather than minimal scrolling.
        var point = sectionHeader.TransformToVisual(SettingsSectionsStack)
            .TransformPoint(new Windows.Foundation.Point(0, 0));
        viewer.ChangeView(null, Math.Max(0, point.Y), null, disableAnimation: true);
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
        if (!EnsureAlbumOpen(JpegConversionStatusText) || _jpegConversionBusy || _clearAlbumBusy ||
            _thumbnailGenerationBusy)
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
                $"{preview.TargetConflictCount:N0} existing JPEG filenames (numbered suffix will be used). " +
                (KeepJpegConversionBackupsCheckBox.IsChecked != false
                    ? "Originals will be retained as backups."
                    : "NO RETAINED BACKUPS: originals are deleted after verified conversion.");
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
    /// Shared cinematic confirmation; preserves the original Primary/Cancel
    /// results. No operation runs unless the caller receives Primary.
    /// </summary>
    private ContentDialog CreateAlbumMaintenanceDialog(
        string heading, string description, string confirmLabel, bool destructive)
    {
        return CinematicDialogShowcase.Create(
            XamlRoot, CinematicDialogShowcase.Variant.Warning,
            subjectOverride: heading,
            detailOverride: description,
            confirmLabel: confirmLabel,
            preview: false);
    }

    private async void OnRunJpegConversion(object sender, RoutedEventArgs e)
    {
        if (_jpegConversionBusy || _clearAlbumBusy || _thumbnailGenerationBusy || _jpegPreview is null || _jpegPreview.PendingCount == 0 ||
            !EnsureAlbumOpen(JpegConversionStatusText))
            return;

        var root = App.CurrentAlbumPath!;
        if (!string.Equals(root, _jpegPreviewAlbum, StringComparison.OrdinalIgnoreCase))
        {
            JpegConversionStatusText.Text = "Album has changed. Scan it again first.";
            RunJpegConversionButton.IsEnabled = false;
            return;
        }

        bool keepJpegBackups = KeepJpegConversionBackupsCheckBox.IsChecked != false;
        var dialog = CreateAlbumMaintenanceDialog(
            "CONVERT TO JPEG",
            $"Convert {_jpegPreview.ConvertCount:N0} photo(s) and embed XMP " +
            $"into {_jpegPreview.JpegMetadataCount:N0} existing JPEG(s)?\n\n" +
            (keepJpegBackups
                ? "JPEG conversion is lossy. Original photos and XMP sidecars will be retained in a timestamped .album backup. "
                : "WARNING: NO BACKUPS. JPEG conversion is lossy and your original images and XMP sidecars will be permanently discarded after verified conversion. ") +
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
                }, keepBackups: keepJpegBackups);
            JpegConversionStatusText.Text =
                $"Complete: {result.Converted:N0} images converted; " +
                $"{result.EmbeddedXmp:N0} XMP packets embedded; " +
                $"{result.Failed:N0} failed, {result.Skipped:N0} skipped. " +
                (keepJpegBackups
                    ? $"Backup: {result.BackupDirectory}"
                    : $"No retained backups. Error reports: {result.BackupDirectory}") +
                (result.Errors.Count > 0
                    ? "\nErrors: " + string.Join("; ", result.Errors.Take(3))
                    : "");

            var report = CinematicDialogShowcase.Create(
                XamlRoot,
                result.Failed > 0
                    ? CinematicDialogShowcase.Variant.Error
                    : CinematicDialogShowcase.Variant.Complete,
                subjectOverride: result.Failed > 0
                    ? "JPEG CONVERSION PARTIALLY FAILED"
                    : "JPEG CONVERSION COMPLETE",
                detailOverride: JpegConversionStatusText.Text,
                preview: false);
            await report.ShowAsync();
        }
        catch (OperationCanceledException)
        {
            JpegConversionStatusText.Text =
                "Conversion cancelled. Completed photos stay converted; " +
                (keepJpegBackups
                    ? "their originals remain in the backup folder."
                    : "completed originals were permanently removed (no-backup mode).");
        }
        catch (Exception ex)
        {
            JpegConversionStatusText.Text = $"Conversion failed: {ex.Message}";
            var error = CinematicDialogShowcase.Create(
                XamlRoot,
                CinematicDialogShowcase.Variant.Error,
                subjectOverride: "JPEG CONVERSION FAILED",
                detailOverride: ex.Message,
                preview: false);
            await error.ShowAsync();
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

    private async void OnClearAlbumMetadata(object sender, RoutedEventArgs e) =>
        await ClearAlbumMetadataAsync(retryFailedOnly: false);

    private async void OnRetryFailedAlbumMetadata(object sender, RoutedEventArgs e) =>
        await ClearAlbumMetadataAsync(retryFailedOnly: true);

    private async Task ClearAlbumMetadataAsync(bool retryFailedOnly)
    {
        if (_clearAlbumBusy || _jpegConversionBusy || _thumbnailGenerationBusy ||
            App.ViewModel is null ||
            !EnsureAlbumOpen(ClearAlbumStatusText))
            return;
        var root = App.CurrentAlbumPath!;
        ClearAlbumMetadataButton.IsEnabled = false;
        RetryFailedAlbumMetadataButton.IsEnabled = false;
        int photoCount = 0;
        ClearAlbumStatusText.Text = retryFailedOnly
            ? "Reading previous failed-photo list…"
            : "Scanning all album folders for image files…";
        try
        {
            if (retryFailedOnly)
            {
                photoCount = AlbumMetadataClearService.ReadPendingFailures(root).Count;
                if (photoCount == 0)
                {
                    ClearAlbumStatusText.Text =
                        "No saved failed photos. Run the full album reset first.";
                    return;
                }
            }
            else
            {
                using var scanner = new FolderScannerService();
                await foreach (var path in scanner.ScanFolderAsync(root, includeSubfolders: true))
                    if (!FolderScannerService.IsVideoFile(path))
                        photoCount++;
            }
        }
        catch (Exception ex)
        {
            ClearAlbumStatusText.Text = "Album scan / retry list failed: " + ex.Message;
            return;
        }
        finally
        {
            ClearAlbumMetadataButton.IsEnabled = true;
            RetryFailedAlbumMetadataButton.IsEnabled = true;
        }
        if (!string.Equals(root, App.CurrentAlbumPath, StringComparison.OrdinalIgnoreCase))
        {
            ClearAlbumStatusText.Text = "Active album changed; scan again.";
            return;
        }
        // A separate dialog avoids accidental single-click mass deletion.
        bool keepResetBackups = KeepAlbumResetBackupsCheckBox.IsChecked != false;
        var dialog = CreateAlbumMaintenanceDialog(
            retryFailedOnly ? "RETRY FAILED PHOTO RESET" : "CLEAR ALBUM TAGS + FLAGS",
            (retryFailedOnly
                ? $"Retry metadata reset on {photoCount:N0} previously failed photo(s) only?\n\n"
                : $"Remove all assigned tags and user flags from {photoCount:N0} photo(s), plus saved catalog tag names, in the ACTIVE album?\n\n") +
            "Ratings, dates, captions, face data and photos are preserved. " +
            "Automatic tagging will be switched OFF to prevent tags from returning. " +
            (keepResetBackups
                ? "Original image and XMP files will be backed up under .album/metadata-reset-backups before each modification.\n\n"
                : "WARNING: NO RETAINED BACKUPS. Changes to embedded tags and flags cannot be reversed; only temporary rollback files are used during each write.\n\n") +
            (retryFailedOnly
                ? "Only the saved failures will be checked. The rest of the album is not rescanned. "
                : "This is an album-wide operation and cannot be undone automatically. ") +
            "Keep a separate backup before proceeding.",
            retryFailedOnly ? "RETRY FAILED PHOTOS" : "CLEAR TAGS AND FLAGS",
            destructive: true);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;
        if (!string.Equals(root, App.CurrentAlbumPath, StringComparison.OrdinalIgnoreCase))
        {
            ClearAlbumStatusText.Text = "Album changed; reset cancelled.";
            return;
        }
        _clearAlbumBusy = true;
        RetryFailedAlbumMetadataButton.IsEnabled = false;
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
                progress, _clearAlbumCancellation.Token, keepBackups: keepResetBackups,
                retryFailedOnly: retryFailedOnly);
            ClearAlbumStatusText.Text =
                $"Finished: {result.Cleaned:N0} photos cleaned, {result.Failed:N0} failed. " +
                (result.Failed > 0
                    ? "PARTIAL RESET: tag catalog was preserved. " +
                      "Review reset-errors.txt in the backup directory. "
                    : "All processed photos passed; tag catalog cleared. ") +
                (keepResetBackups
                    ? $"Backup: {result.BackupDirectory}"
                    : $"No retained backups. Error reports: {result.BackupDirectory}") +
                (result.Errors.Count > 0
                    ? "\nFirst errors: " + string.Join("; ", result.Errors.Take(3))
                    : "");

            var report = CinematicDialogShowcase.Create(
                XamlRoot,
                result.Failed > 0
                    ? CinematicDialogShowcase.Variant.Error
                    : CinematicDialogShowcase.Variant.Complete,
                subjectOverride: result.Failed > 0
                    ? "ALBUM RESET PARTIALLY FAILED"
                    : "ALBUM RESET COMPLETE",
                detailOverride: ClearAlbumStatusText.Text,
                preview: false);
            await report.ShowAsync();
        }
        catch (OperationCanceledException)
        {
            ClearAlbumStatusText.Text =
                "Reset cancelled; successfully cleaned photos stay cleaned. " +
                (keepResetBackups
                    ? "Keep backups for recovery."
                    : "No retained backups; completed changes cannot be undone.");
        }
        catch (Exception ex)
        {
            ClearAlbumStatusText.Text = "Album reset failed: " + ex.Message;
            var error = CinematicDialogShowcase.Create(
                XamlRoot,
                CinematicDialogShowcase.Variant.Error,
                subjectOverride: "ALBUM RESET FAILED",
                detailOverride: ex.Message,
                preview: false);
            await error.ShowAsync();
        }
        finally
        {
            _clearAlbumCancellation?.Dispose();
            _clearAlbumCancellation = null;
            _clearAlbumBusy = false;
            CancelClearAlbumButton.IsEnabled = false;
            ClearAlbumMetadataButton.IsEnabled = true;
            RetryFailedAlbumMetadataButton.IsEnabled = true;
            PreviewJpegConversionButton.IsEnabled = true;
            RunJpegConversionButton.IsEnabled = false;
        }
    }

    private void OnCancelClearAlbum(object sender, RoutedEventArgs e)
    {
        CancelClearAlbumButton.IsEnabled = false;
        _clearAlbumCancellation?.Cancel();
    }

    private CancellationTokenSource? _thumbnailGenerationCancellation;
    private bool _thumbnailGenerationBusy;

    private async void OnGenerateMissingThumbnails(object sender, RoutedEventArgs e)
    {
        if (_thumbnailGenerationBusy || _jpegConversionBusy || _clearAlbumBusy ||
            !EnsureAlbumOpen(ThumbnailGenerationStatusText))
            return;

        var albumRoot = App.CurrentAlbumPath!;
        _thumbnailGenerationBusy = true;
        _thumbnailGenerationCancellation = new CancellationTokenSource();
        GenerateMissingThumbnailsButton.IsEnabled = false;
        CancelThumbnailGenerationButton.IsEnabled = true;
        ThumbnailGenerationProgressBar.Value = 0;
        ThumbnailGenerationProgressBar.Visibility = Visibility.Visible;
        ThumbnailGenerationStatusText.Text = "Scanning album for missing 256px thumbnails…";
        try
        {
            var progress = new Progress<AlbumThumbnailProgress>(value =>
            {
                ThumbnailGenerationProgressBar.Value = value.Total == 0
                    ? 100 : 100d * value.Processed / value.Total;
                ThumbnailGenerationStatusText.Text =
                    $"THUMBNAILS // {value.Processed:N0}/{value.Total:N0} missing " +
                    $"// {value.Generated:N0} generated // {value.Failed:N0} failed " +
                    $"// {value.AlreadyCached:N0} already cached // {value.FileName}";
            });
            var result = await WindowsThumbnailService.GenerateMissingAlbumThumbnailsAsync(
                albumRoot, progress, _thumbnailGenerationCancellation.Token);
            ThumbnailGenerationStatusText.Text =
                $"Finished: {result.Generated:N0} generated, " +
                $"{result.AlreadyCached:N0} already cached, {result.Failed:N0} failed. " +
                "Only missing thumbnails were generated. Cache has a 1 GB size limit.";
            UpdateCacheDisplay();
        }
        catch (OperationCanceledException)
        {
            ThumbnailGenerationStatusText.Text =
                "Thumbnail generation canceled. Already created thumbnails are retained.";
        }
        catch (Exception ex)
        {
            ThumbnailGenerationStatusText.Text = "Thumbnail generation failed: " + ex.Message;
        }
        finally
        {
            _thumbnailGenerationCancellation?.Dispose();
            _thumbnailGenerationCancellation = null;
            _thumbnailGenerationBusy = false;
            GenerateMissingThumbnailsButton.IsEnabled = true;
            CancelThumbnailGenerationButton.IsEnabled = false;
        }
    }

    private void OnCancelThumbnailGeneration(object sender, RoutedEventArgs e)
    {
        CancelThumbnailGenerationButton.IsEnabled = false;
        _thumbnailGenerationCancellation?.Cancel();
    }

    private async void OnRebuildCache(
        object sender,
        RoutedEventArgs e)
    {
        if (_thumbnailGenerationBusy) return;
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
        // Preserve the complete deletion warning and its explicit Primary/Cancel gate.
        var dialog = CinematicDialogShowcase.Create(
            XamlRoot,
            CinematicDialogShowcase.Variant.Warning,
            subjectOverride: "DELETE ALBUM AND METADATA?",
            detailOverride:
                "PhotoLibrarian catalog, cache and matching album launchers will be deleted. " +
                $"All {xmpCount} XMP sidecar file(s) within this album will also be deleted, " +
                "including XMP originally created by other software. " +
                "Original image/video files and folders remain. Embedded metadata in images " +
                "is NOT reverted. Other standalone albums are excluded. The app will close. " +
                "This operation is irreversible. Review your backups before proceeding.",
            confirmLabel: "DELETE ALBUM",
            preview: false,
            context: root);
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
            var error = CinematicDialogShowcase.Create(
                XamlRoot,
                CinematicDialogShowcase.Variant.Fatal,
                subjectOverride: "ALBUM TERMINATION FAILED",
                detailOverride: ex.Message +
                    "\n\nALBUM DATA MAY BE PARTIALLY REMOVED. RESTART BEFORE RETRYING.",
                preview: false);
            await error.ShowAsync();
        }
    }

    // Visual-only dialog catalog. This is deliberately separate from production
    // handlers: previews never invoke conversions, tag edits, deletion or updates.
    private sealed record DialogPreview(
        string Category, string Heading, string Message,
        string Action, bool Warning = false, bool Maintenance = false,
        bool EditFields = false);

    private static readonly DialogPreview[] DialogPreviews =
    [
        new("Import", "IMPORT // DIGIKAM",
            "Import an existing digiKam album and synchronize its local catalog.", "IMPORT", EditFields: true),
        new("Import", "IMPORT // STASH",
            "Connect a Stash export to the current album.", "IMPORT", EditFields: true),
        new("Library", "ADD // FOLDER",
            "Choose a photo folder and add it to the library.", "ADD FOLDER", EditFields: true),
        new("Library", "REMOVE // FOLDER",
            "Remove the selected folder from the library index.", "REMOVE", Warning: true),
        new("Catalog", "TAG // CREATE",
            "Create a new hierarchical tag in the album catalog.", "CREATE TAG", EditFields: true),
        new("Catalog", "TAG // MODIFY",
            "Rename or move the selected catalog tag.", "SAVE", EditFields: true),
        new("Catalog", "TAG // REMOVE",
            "Remove catalog entries while preserving existing photo assignments.", "REMOVE", Warning: true),
        new("Catalog", "TAG // CATALOG CONTROL",
            "Review and manage the hierarchical tag catalog.", "APPLY", EditFields: true),
        new("Gallery", "REMOVE // IMAGE",
            "Move selected photos to the Windows Recycle Bin.", "MOVE TO RECYCLE BIN", Warning: true),
        new("Gallery", "RENAME // IMAGE",
            "Rename the selected photo without altering its image content.", "RENAME", EditFields: true),
        new("Gallery", "CLEAR // PHOTO TAGS",
            "Remove manually assigned tags from selected images.", "CLEAR TAGS", Warning: true),
        new("Album", "JPG // CONVERSION",
            "Convert eligible images to JPEG with metadata migration and rollback support.", "CONVERT TO JPG", Maintenance: true),
        new("Album", "METADATA // RESET",
            "Clear album tag assignments after a backup is prepared.", "CLEAR METADATA", Warning: true, Maintenance: true),
        new("Album", "METADATA // RETRY FAILED",
            "Retry entries from a previous album maintenance operation.", "RETRY", Maintenance: true),
        new("Album", "WARNING // ARCHIVE TERMINATION",
            "The album index, sidecars and local cache will be removed; original photos stay intact.", "DELETE ALBUM", Warning: true),
        new("System", "CACHE // REBUILD",
            "Rebuild cached thumbnails and photo previews.", "REBUILD CACHE"),
        new("System", "UPDATE // CONFIRMATION",
            "A new verified PhotoLibrarian build is available. The installer will replace the current build.", "INSTALL UPDATE"),
        new("System", "UPDATE // FAILED",
            "The update could not be downloaded or validated.", "ACKNOWLEDGE", Warning: true),
        new("System", "ERROR // OPERATION FAILED",
            "An image or metadata operation failed. The underlying error is reported here.", "ACKNOWLEDGE", Warning: true),
        new("System", "FATAL ERROR // ALBUM TERMINATION FAILED",
            "Album data may be partially removed. Restart the application before retrying.", "ACKNOWLEDGE", Warning: true)
    ];

    private async void OnPreviewAllDialogsClick(object sender, RoutedEventArgs e)
    {
        PreviewAllDialogsButton.IsEnabled = false;
        try
        {
            for (var index = 0; index < DialogPreviews.Length; index++)
            {
                var specimen = DialogPreviews[index];
                var dialog = CreateStylePreviewDialog(specimen, index + 1, DialogPreviews.Length);
                // NEXT is a navigation command only: no original operation runs.
                if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                    break;
            }
        }
        finally
        {
            PreviewAllDialogsButton.IsEnabled = true;
        }
    }

    private ContentDialog CreateStylePreviewDialog(DialogPreview preview, int index, int total)
    {
        if (preview.Maintenance)
        {
            return CinematicDialogShowcase.Create(
                XamlRoot, CinematicDialogShowcase.Variant.Warning,
                subjectOverride: preview.Heading,
                detailOverride: preview.Message,
                preview: true);
        }

        var accent = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            preview.Warning
                ? Windows.UI.Color.FromArgb(255, 255, 90, 108)
                : Windows.UI.Color.FromArgb(255, 83, 215, 232));
        var primary = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Windows.UI.Color.FromArgb(255, 230, 247, 250));
        var surface = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Windows.UI.Color.FromArgb(255, 12, 19, 27));
        var body = new StackPanel { Spacing = 12, MinWidth = 390, MaxWidth = 535 };
        body.Children.Add(new TextBlock
        {
            Text = $"PREVIEW {index:D2}/{total:D2}  //  {preview.Category.ToUpperInvariant()}  //  SAFE MODE",
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 11, Foreground = accent
        });
        body.Children.Add(new TextBlock
        {
            Text = preview.Heading,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = primary,
            TextWrapping = TextWrapping.Wrap
        });
        body.Children.Add(new Border { Height = 2, Background = accent });
        body.Children.Add(new TextBlock
        {
            Text = preview.Message,
            Foreground = primary, TextWrapping = TextWrapping.Wrap
        });
        if (preview.EditFields)
        {
            var input = new TextBox
            {
                Header = "Preview input (not saved)",
                Text = "Example value",
                IsReadOnly = true
            };
            body.Children.Add(input);
        }
        body.Children.Add(new TextBlock
        {
            Text = "VISUAL CHECK ONLY  //  PRIMARY BUTTON SHOWS NEXT PREVIEW",
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 10, Foreground = accent, TextWrapping = TextWrapping.Wrap
        });
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ElementTheme.Dark,
            Title = null,
            Content = new Border
            {
                Padding = new Thickness(20),
                Background = surface,
                BorderBrush = accent,
                BorderThickness = new Thickness(preview.Warning ? 2 : 1),
                Child = body
            },
            PrimaryButtonText = preview.Action + " (PREVIEW)",
            CloseButtonText = "END PREVIEW",
            DefaultButton = ContentDialogButton.Close,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(255, 7, 11, 16)),
            BorderBrush = accent,
            BorderThickness = new Thickness(1)
        };
        if (Application.Current.Resources.TryGetValue(
                "HiTechPrimaryButtonStyle", out var primaryStyle) && primaryStyle is Style primaryButton)
            dialog.PrimaryButtonStyle = primaryButton;
        if (Application.Current.Resources.TryGetValue(
                "HiTechIconButtonStyle", out var closeStyle) && closeStyle is Style closeButton)
            dialog.CloseButtonStyle = closeButton;
        CinematicDialogStyling.Apply(dialog);
        return dialog;
    }

    // Isolated Cinematic UI 2.0 lab. Intentionally never calls production operations.
    private async void OnShowcaseFatal(object sender, RoutedEventArgs e) =>
        await ShowCinematicSpecimensAsync(CinematicDialogShowcase.Variant.Fatal);

    private async void OnShowcaseWarning(object sender, RoutedEventArgs e) =>
        await ShowCinematicSpecimensAsync(CinematicDialogShowcase.Variant.Warning);

    private async void OnShowcaseComplete(object sender, RoutedEventArgs e) =>
        await ShowCinematicSpecimensAsync(CinematicDialogShowcase.Variant.Complete);

    private async void OnShowcaseError(object sender, RoutedEventArgs e) =>
        await ShowCinematicSpecimensAsync(CinematicDialogShowcase.Variant.Error);

    private async void OnShowcaseInformation(object sender, RoutedEventArgs e) =>
        await ShowCinematicSpecimensAsync(CinematicDialogShowcase.Variant.Information);

    private async void OnShowcaseProgress(object sender, RoutedEventArgs e) =>
        await ShowCinematicSpecimensAsync(CinematicDialogShowcase.Variant.Progress);

    private async void OnShowcaseSequence(object sender, RoutedEventArgs e) =>
        await ShowCinematicSpecimensAsync(
            CinematicDialogShowcase.Variant.Fatal,
            CinematicDialogShowcase.Variant.Error,
            CinematicDialogShowcase.Variant.Warning,
            CinematicDialogShowcase.Variant.Complete,
            CinematicDialogShowcase.Variant.Information,
            CinematicDialogShowcase.Variant.Progress);

    private bool _cinematicShowcaseOpen;

    private async System.Threading.Tasks.Task ShowCinematicSpecimensAsync(
        params CinematicDialogShowcase.Variant[] variants)
    {
        if (_cinematicShowcaseOpen) return;
        _cinematicShowcaseOpen = true;
        CinematicShowcaseSequenceButton.IsEnabled = false;
        try
        {
            foreach (var variant in variants)
            {
                var dialog = CinematicDialogShowcase.Create(XamlRoot, variant);
                // Primary only navigates to the next specimen. No actual operation executes.
                if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                    break;
            }
        }
        finally
        {
            CinematicShowcaseSequenceButton.IsEnabled = true;
            _cinematicShowcaseOpen = false;
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
