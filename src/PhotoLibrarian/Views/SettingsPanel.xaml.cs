using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using PhotoLibrarian.Core.Services;
using PhotoLibrarian.ML.Services;
using PhotoLibrarian.ViewModels;

namespace PhotoLibrarian.Views;

public sealed partial class SettingsPanel : UserControl
{
    private SettingsViewModel? ViewModel =>
        App.ViewModel?.Settings;
    private bool _isUpdatingDisplay;
    private bool _isBenchmarkVisible;

    public SettingsPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        ModelProfileCombo.ItemsSource = ViewModel.Profiles;
        BenchmarkResultsList.ItemsSource =
            ViewModel.BenchmarkExamples;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateDisplay();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e) =>
        DispatcherQueue.TryEnqueue(UpdateDisplay);

    private void UpdateDisplay()
    {
        if (ViewModel is null)
        {
            return;
        }

        _isUpdatingDisplay = true;
        try
        {
            var profile = ViewModel.SelectedProfile;
            var isProfileEditable =
                !ViewModel.IsBenchmarkRunning &&
                !ViewModel.IsModelImportRunning;
            var calibratedThresholdsVisibility =
                profile.UsesCalibratedThresholds
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            var binaryTagMaskVisibility =
                profile.OutputKind == AutoTagOutputKind.BinaryTagMask
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            var isTinyClip =
                profile.Id == AutoTagModelCatalog.TinyClipProfileId;
            CacheSizeText.Text =
                $"Cache size: {ViewModel.ThumbnailCacheSizeMB} MB";
            CachePathText.Text = ViewModel.CacheLocation;
            AutoTaggingToggle.IsOn =
                ViewModel.IsAutoTaggingEnabled;
            AutoTaggingToggle.IsEnabled =
                ViewModel.IsSelectedProfileApproved;
            ModelProfileCombo.SelectedItem =
                ViewModel.Profiles.First(candidate =>
                    candidate.Id == ViewModel.SelectedProfileId);
            ModelProfileCombo.IsEnabled = isProfileEditable;
            ModelPurposeText.Text = ViewModel.ModelPurpose;
            ModelSizeText.Text =
                $"Asset size: {ViewModel.ModelDownloadSize}";
            ModelDownloadStatusText.Text =
                ViewModel.ModelDownloadStatus;
            QualityStatusText.Text = ViewModel.QualityStatus;
            MaximumTagsNumberBox.Maximum =
                profile.MaximumSupportedTags;
            MaximumTagsNumberBox.Value =
                ViewModel.MaximumTags;
            MaximumTagsNumberBox.IsEnabled = isProfileEditable;
            ConfidenceNumberBox.Value =
                ViewModel.ConfidencePercent;
            ConfidenceNumberBox.IsEnabled =
                isProfileEditable && !profile.UsesFixedThresholds;
            ConfidenceNumberBox.Visibility =
                profile.UsesFixedThresholds || isTinyClip
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            TinyClipOpennessPanel.Visibility =
                isTinyClip ? Visibility.Visible : Visibility.Collapsed;
            TinyClipOpennessSlider.Value = ViewModel.TinyClipOpenness;
            TinyClipOpennessSlider.IsEnabled = isProfileEditable;
            ConfidenceNumberBox.Header = isTinyClip
                ? "Minimum cosine similarity (%)"
                : "Minimum confidence (%)";
            AutomationProperties.SetName(
                ConfidenceNumberBox,
                isTinyClip
                    ? "Minimum TinyCLIP cosine similarity percentage"
                    : "Minimum confidence percentage");
            TinyClipLimitationsText.Visibility =
                isTinyClip
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            TaggingBehaviorHelpText.Text =
                profile.UsesFixedThresholds
                    ? "Changing the maximum tag count clears trust. Review and trust the profile again to enable automatic tagging."
                    : "Changing either value clears trust. Review and trust the profile again to enable automatic tagging.";
            CalibratedThresholdsText.Visibility =
                calibratedThresholdsVisibility;
            BenchmarkScoreHelpText.Visibility = calibratedThresholdsVisibility;
            BinaryDetectionThresholdsText.Visibility =
                binaryTagMaskVisibility;
            BinaryDetectionScoreHelpText.Visibility =
                binaryTagMaskVisibility;
            ModelDirectoryText.Text = ViewModel.ModelDirectory;
            AutoTaggingStatusText.Text =
                ViewModel.AutoTaggingStatus;
            AutoTaggingProgress.Visibility =
                ViewModel.IsAutoTaggingRunning
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            AutoTaggingProgress.IsIndeterminate =
                ViewModel.IsAutoTaggingRunning &&
                ViewModel.AutoTaggingTotal == 0;
            if (ViewModel.AutoTaggingTotal > 0)
            {
                AutoTaggingProgress.Value =
                    100d * ViewModel.AutoTaggingProcessed /
                    ViewModel.AutoTaggingTotal;
            }

            RunBenchmarkButton.IsEnabled =
                !ViewModel.IsBenchmarkRunning &&
                !ViewModel.IsModelImportRunning;
            BenchmarkProgressRing.IsActive =
                ViewModel.IsBenchmarkRunning;
            BenchmarkProgressRing.Visibility =
                ViewModel.IsBenchmarkRunning
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            BenchmarkFolderText.Text =
                ViewModel.BenchmarkFolder;
            BenchmarkSummaryText.Text =
                ViewModel.BenchmarkSummary;
            ApproveProfileButton.IsEnabled =
                !ViewModel.IsBenchmarkRunning &&
                !ViewModel.IsModelImportRunning &&
                (!profile.RequiresLocalImport ||
                 ViewModel.ModelDownloadStatus.StartsWith(
                     "Imported", StringComparison.Ordinal));
            var requiresImport = profile.RequiresLocalImport;
            ImportModelAssetsButton.Content =
                $"Import {profile.DisplayName} assets";
            ImportModelAssetsButton.Visibility =
                requiresImport
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            ImportModelAssetsButton.IsEnabled =
                requiresImport &&
                !ViewModel.IsModelImportRunning &&
                !ViewModel.IsBenchmarkRunning &&
                !ViewModel.IsAutoTaggingRunning;
            ModelImportProgressRing.IsActive =
                ViewModel.IsModelImportRunning;
            ModelImportProgressRing.Visibility =
                ViewModel.IsModelImportRunning
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            ModelImportInfoBar.Visibility =
                requiresImport
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            ModelImportInfoBar.IsOpen = requiresImport;
            ModelImportInfoBar.Title =
                $"Local assets — {profile.DisplayName}";
            ModelImportInfoBar.Message =
                ViewModel.ModelImportMessage;
            ModelImportInfoBar.Severity =
                ViewModel.ModelImportHasError
                    ? InfoBarSeverity.Error
                    : InfoBarSeverity.Informational;
            ModelSetupLink.NavigateUri =
                AutoTagModelCatalog.ModelSetupInstructionsUri;
            ModelSourceLink.NavigateUri =
                AutoTagModelCatalog.SourceUriFor(profile.Id);
            DeleteSelectedModelButton.IsEnabled =
                !ViewModel.IsAutoTaggingRunning &&
                !ViewModel.IsBenchmarkRunning &&
                !ViewModel.IsModelImportRunning;
            DeleteAllModelsButton.IsEnabled =
                !ViewModel.IsAutoTaggingRunning &&
                !ViewModel.IsBenchmarkRunning &&
                !ViewModel.IsModelImportRunning;
            RemoveAutoTagsButton.IsEnabled =
                !ViewModel.IsAutoTagCleanupRunning &&
                !ViewModel.IsBenchmarkRunning &&
                !ViewModel.IsModelImportRunning;
        }
        finally
        {
            _isUpdatingDisplay = false;
        }
    }

    private async void OnImportDigiKam(
        object sender,
        RoutedEventArgs e)
    {
        if (!App.HasActiveAlbum ||
            string.IsNullOrWhiteSpace(App.CurrentAlbumPath))
        {
            DigiKamImportStatusText.Text =
                "Open an album before importing from digiKam.";
            return;
        }

        var picker = new Windows.Storage.Pickers.FileOpenPicker
        {
            SuggestedStartLocation =
                Windows.Storage.Pickers.PickerLocationId.PicturesLibrary
        };
        picker.FileTypeFilter.Add(".db");
        picker.FileTypeFilter.Add(".sqlite");
        picker.FileTypeFilter.Add(".sqlite3");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(
            App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file is null)
            return;

        ImportDigiKamButton.IsEnabled = false;
        DigiKamImportProgressRing.IsActive = true;
        DigiKamImportProgressRing.Visibility = Visibility.Visible;
        DigiKamImportProgressBar.Visibility = Visibility.Visible;
        DigiKamImportProgressBar.Value = 0;
        DigiKamImportStatusText.Text =
            $"Reading {file.Name}…";

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

            if (result.UnmatchedImages > 0)
                details.Add($"{result.UnmatchedImages:N0} unmatched");
            if (result.AmbiguousImages > 0)
                details.Add($"{result.AmbiguousImages:N0} ambiguous");
            if (result.PersistentWriteFailures > 0)
            {
                details.Add(
                    $"{result.PersistentWriteFailures:N0} files could not persist metadata");
            }

            DigiKamImportProgressBar.Value = 100;
            DigiKamImportStatusText.Text = string.Join(" • ", details);

            if (result.UnmatchedExamples.Count > 0 ||
                result.AmbiguousExamples.Count > 0)
            {
                var exampleLines = new List<string>();
                if (result.UnmatchedExamples.Count > 0)
                {
                    exampleLines.Add(
                        "Unmatched examples: " +
                        string.Join(", ", result.UnmatchedExamples));
                }
                if (result.AmbiguousExamples.Count > 0)
                {
                    exampleLines.Add(
                        "Ambiguous examples: " +
                        string.Join(", ", result.AmbiguousExamples));
                }

                DigiKamImportStatusText.Text +=
                    Environment.NewLine +
                    string.Join(Environment.NewLine, exampleLines);
            }
        }
        catch (Exception exception)
            when (exception is IOException or
                UnauthorizedAccessException or
                InvalidDataException or
                Microsoft.Data.Sqlite.SqliteException or
                ArgumentException or
                InvalidOperationException)
        {
            DigiKamImportStatusText.Text =
                $"digiKam import failed: {exception.Message}";
        }
        finally
        {
            ImportDigiKamButton.IsEnabled = true;
            DigiKamImportProgressRing.IsActive = false;
            DigiKamImportProgressRing.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnRebuildCache(
        object sender,
        RoutedEventArgs e)
    {
        if (ViewModel?.RebuildCacheCommand.CanExecute(null) == true)
        {
            await ViewModel.RebuildCacheCommand.ExecuteAsync(null);
        }
    }

    private async void OnClose(object sender, RoutedEventArgs e)
    {
        await App.ViewModel.RefreshTagsTreeAsync();
        ViewModel?.CloseCommand.Execute(null);
    }

    private void OnAutoTaggingToggled(
        object sender,
        RoutedEventArgs e)
    {
        if (!_isUpdatingDisplay && ViewModel is not null)
        {
            ViewModel.IsAutoTaggingEnabled =
                AutoTaggingToggle.IsOn;
        }
    }

    private void OnModelProfileChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_isUpdatingDisplay &&
            ViewModel is not null &&
            ModelProfileCombo.SelectedItem is
                AutoTagModelDefinition profile)
        {
            ViewModel.SelectProfile(profile.Id);
        }
    }

    private void OnMaximumTagsChanged(
        NumberBox sender,
        NumberBoxValueChangedEventArgs args)
    {
        if (!_isUpdatingDisplay &&
            ViewModel is not null &&
            !ViewModel.IsBenchmarkRunning &&
            !ViewModel.IsModelImportRunning &&
            !double.IsNaN(args.NewValue))
        {
            ViewModel.MaximumTags = (int)args.NewValue;
        }
    }

    private void OnConfidenceChanged(
        NumberBox sender,
        NumberBoxValueChangedEventArgs args)
    {
        if (!_isUpdatingDisplay &&
            ViewModel is not null &&
            !ViewModel.IsBenchmarkRunning &&
            !ViewModel.IsModelImportRunning &&
            !ViewModel.SelectedProfile.UsesFixedThresholds &&
            !double.IsNaN(args.NewValue))
        {
            ViewModel.ConfidencePercent = args.NewValue;
        }
    }

    private void OnTinyClipOpennessChanged(
        object sender,
        RangeBaseValueChangedEventArgs args)
    {
        if (!_isUpdatingDisplay &&
            ViewModel is not null &&
            !ViewModel.IsBenchmarkRunning &&
            !ViewModel.IsModelImportRunning)
        {
            ViewModel.TinyClipOpenness = args.NewValue;
        }
    }

    private void OnToggleBenchmark(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        _isBenchmarkVisible = !_isBenchmarkVisible;
        BenchmarkSection.Visibility = _isBenchmarkVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        BenchmarkDivider.Visibility = BenchmarkSection.Visibility;
        args.Handled = true;
    }

    private async void OnChooseModelDirectory(
        object sender,
        RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (folder is not null)
        {
            ViewModel?.SetModelDirectory(folder.Path);
        }
    }

    private void OnResetModelDirectory(
        object sender,
        RoutedEventArgs e)
    {
        ViewModel?.ResetModelDirectory();
    }

    private async void OnImportModelAssets(
        object sender,
        RoutedEventArgs e)
    {
        var folder = await PickFolderAsync(
            Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary);
        if (folder is not null && ViewModel is not null)
        {
            await ViewModel.ImportSelectedModelAssetsAsync(folder.Path);
        }
    }

    private async void OnDeleteSelectedModel(
        object sender,
        RoutedEventArgs e)
    {
        if (ViewModel is null ||
            !await ConfirmAsync(
                "Delete selected model?",
                "The selected profile's managed model and vocabulary files will be deleted. Imported source files are not changed.",
                "Delete"))
        {
            return;
        }

        await ViewModel.DeleteSelectedModelAssetsAsync();
    }

    private async void OnRemoveAutoTags(
        object sender,
        RoutedEventArgs e)
    {
        if (ViewModel is null ||
            !await ConfirmAsync(
                "Remove all automatic tags?",
                "Automatic tagging will be turned off and every generated Auto tag will be removed from the library. Manual, imported, and metadata tags will not be changed.",
                "Remove automatic tags"))
        {
            return;
        }

        await App.ViewModel.RemoveAllAutomaticTagsAsync();
    }

    private async void OnDeleteAllModels(
        object sender,
        RoutedEventArgs e)
    {
        if (ViewModel is null ||
            !await ConfirmAsync(
                "Delete all model downloads?",
                "All automatic-tagging model assets in the current model location will be deleted. Imported source files, other files, and previous model locations are not changed.",
                "Delete all"))
        {
            return;
        }

        await ViewModel.DeleteAllModelAssetsAsync();
    }

    private async void OnRunBenchmark(
        object sender,
        RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (folder is not null && ViewModel is not null)
        {
            await ViewModel.RunBenchmarkAsync(folder.Path);
        }
    }

    private void OnApproveProfile(
        object sender,
        RoutedEventArgs e)
    {
        ViewModel?.ApproveSelectedProfile();
    }

    private static async Task<Windows.Storage.StorageFolder?> PickFolderAsync(
        Windows.Storage.Pickers.PickerLocationId startLocation =
            Windows.Storage.Pickers.PickerLocationId.PicturesLibrary)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker
        {
            SuggestedStartLocation = startLocation
        };
        picker.FileTypeFilter.Add("*");
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(
            App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        return await picker.PickSingleFolderAsync();
    }

    private async Task<bool> ConfirmAsync(
        string title,
        string message,
        string primaryButtonText)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = message,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() ==
            ContentDialogResult.Primary;
    }
}
