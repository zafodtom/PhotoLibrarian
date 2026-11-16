using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PhotoLibrarian.Core.Services;
using PhotoLibrarian.Services;
using PhotoLibrarian.ViewModels;
using PhotoLibrarian.Views;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace PhotoLibrarian;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel => App.ViewModel;

    private CropAspectRatio _pendingAspect = CropAspectRatio.Free;
    private bool _isClosing;
    private bool _isViewerFullscreen;
    private GridLength _preFullscreenHeaderHeight;
    private GridLength _preFullscreenViewerCommandHeight;
    private GridLength _preFullscreenStatusHeight;
    private GridLength _preFullscreenRailWidth;
    private GridLength _preFullscreenLeftWidth;
    private GridLength _preFullscreenLeftSplitterWidth;
    private GridLength _preFullscreenRightSplitterWidth;
    private GridLength _preFullscreenRightWidth;
    private Thickness _preFullscreenViewportMargin;
    private Thickness _preFullscreenViewportBorderThickness;
    private double _preFullscreenLeftMinWidth;
    private double _preFullscreenRightMinWidth;
    private Microsoft.UI.Windowing.AppWindowPresenterKind _preFullscreenPresenterKind =
        Microsoft.UI.Windowing.AppWindowPresenterKind.Overlapped;

    public bool IsViewerFullscreen => _isViewerFullscreen;

    public void ToggleViewerFullscreen()
    {
        if (_isViewerFullscreen)
            ExitViewerFullscreen();
        else
            EnterViewerFullscreen();
    }

    public void EnterViewerFullscreen()
    {
        if (_isViewerFullscreen || !ViewModel.ImageViewer.IsOpen)
            return;

        _preFullscreenHeaderHeight = GlobalHeaderRow.Height;
        _preFullscreenViewerCommandHeight = ViewerCommandRow.Height;
        _preFullscreenStatusHeight = StatusRow.Height;
        _preFullscreenRailWidth = SystemRailColumn.Width;
        _preFullscreenLeftWidth = LeftPanelColumn.Width;
        _preFullscreenLeftSplitterWidth = LeftSplitterColumn.Width;
        _preFullscreenRightSplitterWidth = RightSplitterColumn.Width;
        _preFullscreenRightWidth = RightPanelColumn.Width;
        _preFullscreenViewportMargin = MainViewportFrame.Margin;
        _preFullscreenViewportBorderThickness = MainViewportBorder.BorderThickness;
        _preFullscreenLeftMinWidth = LeftPanelColumn.MinWidth;
        _preFullscreenRightMinWidth = RightPanelColumn.MinWidth;
        _preFullscreenPresenterKind = AppWindow.Presenter.Kind;

        GlobalHeaderRow.Height = new GridLength(0);
        ViewerCommandRow.Height = new GridLength(0);
        StatusRow.Height = new GridLength(0);

        LeftPanelColumn.MinWidth = 0;
        RightPanelColumn.MinWidth = 0;
        SystemRailColumn.Width = new GridLength(0);
        LeftPanelColumn.Width = new GridLength(0);
        LeftSplitterColumn.Width = new GridLength(0);
        RightSplitterColumn.Width = new GridLength(0);
        RightPanelColumn.Width = new GridLength(0);
        LeftPanelSplitter.Visibility = Visibility.Collapsed;
        RightPanelSplitter.Visibility = Visibility.Collapsed;
        GlobalHeader.Visibility = Visibility.Collapsed;
        SystemRail.Visibility = Visibility.Collapsed;
        FolderNavPanel.Visibility = Visibility.Collapsed;
        MetadataDetailPanel.Visibility = Visibility.Collapsed;
        StatusBar.Visibility = Visibility.Collapsed;
        TopRibbon.Visibility = Visibility.Collapsed;
        MainViewportDecorations.Visibility = Visibility.Collapsed;
        MainViewportFrame.Margin = new Thickness(0);
        MainViewportBorder.BorderThickness = new Thickness(0);

        AppWindow.SetPresenter(
            Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
        _isViewerFullscreen = true;
        FullscreenSideRail.Visibility = Visibility.Visible;
        UpdateRibbonVisibility();
    }

    public void ExitViewerFullscreen()
    {
        if (!_isViewerFullscreen)
            return;

        AppWindow.SetPresenter(_preFullscreenPresenterKind);

        GlobalHeaderRow.Height = _preFullscreenHeaderHeight;
        ViewerCommandRow.Height = _preFullscreenViewerCommandHeight;
        StatusRow.Height = _preFullscreenStatusHeight;

        LeftPanelColumn.MinWidth = _preFullscreenLeftMinWidth;
        RightPanelColumn.MinWidth = _preFullscreenRightMinWidth;
        SystemRailColumn.Width = _preFullscreenRailWidth;
        LeftPanelColumn.Width = _preFullscreenLeftWidth;
        LeftSplitterColumn.Width = _preFullscreenLeftSplitterWidth;
        RightSplitterColumn.Width = _preFullscreenRightSplitterWidth;
        RightPanelColumn.Width = _preFullscreenRightWidth;
        LeftPanelSplitter.Visibility = Visibility.Visible;
        RightPanelSplitter.Visibility = Visibility.Visible;
        GlobalHeader.Visibility = Visibility.Visible;
        SystemRail.Visibility = Visibility.Visible;
        FolderNavPanel.Visibility = Visibility.Visible;
        MetadataDetailPanel.Visibility = Visibility.Visible;
        StatusBar.Visibility = Visibility.Visible;
        MainViewportDecorations.Visibility = Visibility.Visible;
        MainViewportFrame.Margin = _preFullscreenViewportMargin;
        MainViewportBorder.BorderThickness = _preFullscreenViewportBorderThickness;

        _isViewerFullscreen = false;
        FullscreenSideRail.Visibility = Visibility.Collapsed;
        UpdateRibbonVisibility();
    }

    public async Task RefreshMetadataTreesAsync()
    {
        await FolderNavPanel.RefreshMetadataTreesAsync();
        await ImageGridPanel.RefreshPeopleAsync();
    }

    public async Task RefreshAllNavigationAsync()
    {
        await FolderNavPanel.RefreshAllTreesAsync();
        await ImageGridPanel.RefreshPeopleAsync();
    }

    public async Task RefreshPeopleFiltersAsync()
    {
        await FolderNavPanel.RefreshPeopleTreeAsync();
        await ImageGridPanel.RefreshPeopleAsync();
    }

    public Task RefreshTagsTreeAsync() =>
        FolderNavPanel.RefreshTagsTreeAsync();

    public string? GetPreferredFileOperationDirectory() =>
        FolderNavPanel.GetPreferredFileOperationDirectory();

    public void BeginManualFaceTagging() =>
        ViewerOverlay.EnterManualFaceTagging();

    public void CancelManualFaceTagging() =>
        ViewerOverlay.ExitManualFaceTagging();

    /// <summary>Forwards a hovered people-tag row to whichever surface is showing that image
    /// (the full viewer if it's open on that entry, and/or the grid thumbnail).</summary>
    public void SetHoveredFace(Core.Models.ImageEntry? entry, Core.Models.FaceRegion? region)
    {
        ViewerOverlay.SetFaceHighlight(
            entry != null && ViewModel.ImageViewer.CurrentEntry?.Id == entry.Id ? region : null);

        var thumbnail = entry is null
            ? null
            : ViewModel.ImageGrid.Images.FirstOrDefault(image => image.Entry.Id == entry.Id);
        ImageGridPanel.SetFaceHighlight(thumbnail, thumbnail is null ? null : region);
    }

    /// <summary>Repaints the left-panel "Flagged" node (count changes after flag edits).</summary>
    public void RefreshFlagTree()
    {
        FolderNavPanel.RefreshFlagTree();
    }

    private static void CenterOnCurrentDisplay(AppWindow window)
    {
        var area = DisplayArea.GetFromWindowId(window.Id, DisplayAreaFallback.Primary);
        var bounds = area.WorkArea;
        var size = window.Size;
        window.Move(new Windows.Graphics.PointInt32(
            bounds.X + Math.Max(0, (bounds.Width - size.Width) / 2),
            bounds.Y + Math.Max(0, (bounds.Height - size.Height) / 2)));
    }

    public MainWindow()
    {
        this.InitializeComponent();
        // Show the actual installed release tag, including -dev.N prereleases.
        InstalledVersionStatusText.Text = GitHubUpdateService.InstalledTag;

        AppRoot.AddHandler(
            UIElement.PointerMovedEvent,
            new PointerEventHandler(OnUserPointerActivity),
            true);
        AppRoot.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler(OnUserPointerActivity),
            true);
        AppRoot.AddHandler(
            UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(OnUserPointerActivity),
            true);
        AppRoot.AddHandler(
            UIElement.KeyDownEvent,
            new KeyEventHandler(OnUserKeyActivity),
            true);
        AppRoot.AddHandler(
            UIElement.KeyDownEvent,
            new KeyEventHandler(OnSelectAllImagesShortcut),
            true);

        var appWindow = this.AppWindow;
        appWindow.Resize(new Windows.Graphics.SizeInt32(1600, 900));
        CenterOnCurrentDisplay(appWindow);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(GlobalHeader);
        if (appWindow.TitleBar is { } titleBar)
        {
            titleBar.BackgroundColor = Windows.UI.Color.FromArgb(255, 10, 18, 26);
            titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(255, 10, 18, 26);
            titleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 83, 215, 232);
            titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(255, 26, 52, 65);
            titleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
            titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(255, 34, 80, 96);
            titleBar.ButtonPressedForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
            titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(255, 10, 18, 26);
            titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 95, 124, 136);
        }
        appWindow.Title = App.HasActiveAlbum
            ? $"PhotoLibrarian — {System.IO.Path.GetFileName(App.CurrentAlbumPath)}"
            : "PhotoLibrarian";

        MainLayout.Visibility = App.HasActiveAlbum ? Visibility.Visible : Visibility.Collapsed;
        AlbumStartOverlay.Visibility = App.HasActiveAlbum ? Visibility.Collapsed : Visibility.Visible;

        // Set the window icon (title bar + taskbar)
        try
        {
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "PhotoLibrarian.ico");
            if (System.IO.File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }
        }
        catch { /* Icon is cosmetic — never block startup */ }

        // Bind status bar to ViewModel
        ViewModel.PropertyChanged += OnMainViewModelPropertyChanged;
        ViewModel.ImageViewer.PropertyChanged += OnImageViewerPropertyChanged;
        ViewModel.Settings.PropertyChanged += OnSettingsPropertyChanged;

        // Top-ribbon events
        ViewerOverlay.ManualFaceTaggingStarted += (_, _) => UpdateRibbonVisibility();
        ViewerOverlay.ManualFaceTaggingExited += (_, _) => UpdateRibbonVisibility();
        TopRibbon.CropClicked += OnRibbonCropClicked;
        TopRibbon.CloseViewerClicked += (_, _) => ViewModel.ImageViewer.CloseCommand.Execute(null);
        TopRibbon.AdjustClicked += OnRibbonAdjustClicked;
        TopRibbon.ApplyCropClicked += OnRibbonApplyCropClicked;
        TopRibbon.CancelCropClicked += OnRibbonCancelCropClicked;
        TopRibbon.CropAspectChanged += OnRibbonCropAspectChanged;
        ViewerOverlay.CropApplyRequested += OnRibbonApplyCropClicked;
        ViewerOverlay.CropCancelRequested += OnRibbonCancelCropClicked;
        ViewerOverlay.ManualFaceTaggingExited += (_, _) => ViewModel.OnManualFaceTaggingExited();

        // Cleanup on window close
        this.Closed += OnWindowClosed;
    }

    private void OnSelectAllImagesShortcut(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || e.Key != Windows.System.VirtualKey.A ||
            SettingsOverlay.Visibility == Visibility.Visible ||
            ViewerOverlay.Visibility == Visibility.Visible ||
            e.OriginalSource is TextBox or PasswordBox or RichEditBox or
                NumberBox or ComboBox)
            return;

        var modifiers = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
        if ((modifiers & Windows.UI.Core.CoreVirtualKeyStates.Down) !=
            Windows.UI.Core.CoreVirtualKeyStates.Down)
            return;

        ImageGridPanel.SelectAllDisplayedImages();
        e.Handled = true;
    }

    public Task ConfirmDeleteAlbumAsync() =>
        SettingsPanel.ConfirmDeleteAlbumAsync();

    public async Task DeleteCurrentAlbumAsync()
    {
        if (!App.HasActiveAlbum || string.IsNullOrWhiteSpace(App.CurrentAlbumPath))
            throw new InvalidOperationException("No album is active.");

        var albumPath = App.CurrentAlbumPath;
        _isClosing = true;
        ViewModel.PropertyChanged -= OnMainViewModelPropertyChanged;
        ViewModel.ImageViewer.PropertyChanged -= OnImageViewerPropertyChanged;
        ViewModel.Settings.PropertyChanged -= OnSettingsPropertyChanged;
        ViewModel.ImageGrid.Cleanup();
        await ViewModel.CleanupAsync();

        // Database handles are now closed. Do not call asynchronous cleanup again.
        AlbumService.DeleteAlbumBookkeeping(albumPath);
        Closed -= OnWindowClosed;
        Close();
        App.Current.Exit();
    }

    private async void OnOpenAlbumClick(object sender, RoutedEventArgs e)
    {
        BootWindow? transition = null;
        System.Diagnostics.Process? newProcess = null;
        var transitionStopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var albumPath = await AlbumService.PickAlbumFolderAsync(this);
            if (albumPath is null)
                return;

            transition = new BootWindow();
            transition.Activate();

            transition.SetPhase(
                1,
                12,
                "ARCHIVE CONTEXT SWITCH",
                "External album root accepted. Preparing session handoff...",
                warning: true);
            await Task.Delay(700);

            transition.SetPhase(
                2,
                34,
                "VALIDATING TARGET",
                "Checking album manifest, portable metadata and local cache boundary...",
                warning: true);
            await Task.Delay(700);

            transition.SetPhase(
                3,
                58,
                "SESSION REBIND REQUIRED",
                "Current archive session will remain visible until the replacement process is online.",
                warning: true);
            await Task.Delay(700);

            transition.SetPhase(
                4,
                78,
                "PREPARING HANDOFF",
                "Arming replacement process and preserving visual continuity...",
                warning: true);
            await Task.Delay(700);

            transition.SetPhase(
                5,
                92,
                "LAUNCHING REPLACEMENT NODE",
                "Starting new PhotoLibrarian process with the selected archive root...",
                warning: true);

            newProcess = AlbumService.StartAlbumInstance(albumPath);

            // InputIdle is not a visual readiness signal. Wait until Windows
            // actually exposes a top-level window for the replacement process.
            // Its first visible window is the cyan BootWindow in App.OnLaunched.
            var replacementVisible = await Task.Run(async () =>
            {
                var timeout = System.Diagnostics.Stopwatch.StartNew();
                while (timeout.Elapsed < TimeSpan.FromSeconds(20))
                {
                    newProcess.Refresh();
                    if (newProcess.HasExited)
                        throw new InvalidOperationException(
                            "Replacement archive process exited during startup.");

                    if (newProcess.MainWindowHandle != IntPtr.Zero)
                        return true;

                    await Task.Delay(100);
                }
                return false;
            });

            if (!replacementVisible)
                throw new TimeoutException(
                    "Replacement archive boot window was not detected.");

            transition.SetPhase(
                6,
                100,
                "HANDOFF CONFIRMED",
                "Replacement node responded. Transferring visual control...",
                warning: true);

            const int minimumTransitionMs = 5000;
            var remainingTransitionMs =
                minimumTransitionMs - (int)transitionStopwatch.ElapsedMilliseconds;
            if (remainingTransitionMs > 0)
                await Task.Delay(remainingTransitionMs);

            await Task.Delay(350);

            // Finish the old session's async cleanup before leaving the
            // process, while the warning window still covers the handoff.
            // OnWindowClosed is not awaitable, so do not rely on its async-void
            // event handler to complete before Application.Exit().
            transition.SetPhase(
                7, 100, "RELEASING OLD SESSION",
                "Closing local indexing services and releasing database resources...",
                warning: true);
            ViewModel.PropertyChanged -= OnMainViewModelPropertyChanged;
            ViewModel.ImageViewer.PropertyChanged -= OnImageViewerPropertyChanged;
            ViewModel.Settings.PropertyChanged -= OnSettingsPropertyChanged;
            _isClosing = true;
            ViewModel.ImageGrid.Cleanup();
            await ViewModel.CleanupAsync();

            // Prevent a second asynchronous cleanup from the Closed event.
            this.Closed -= OnWindowClosed;
            this.Close();
            transition.Close();
            transition = null;
            App.Current.Exit();
        }
        catch (Exception ex)
        {
            try
            {
                if (newProcess is not null &&
                    !newProcess.HasExited &&
                    newProcess.MainWindowHandle == IntPtr.Zero)
                {
                    newProcess.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            if (transition is not null)
            {
                transition.SetPhase(
                    99,
                    100,
                    "ARCHIVE SWITCH FAILED",
                    ex.Message,
                    warning: false,
                    fatal: true);
                await Task.Delay(1800);
                transition.Close();
            }
            else
            {
                var dialog = PhotoLibrarian.Views.CinematicDialogShowcase.Create(
                    AppRoot.XamlRoot,
                    PhotoLibrarian.Views.CinematicDialogShowcase.Variant.Fatal,
                    subjectOverride: "ALBUM SWITCH FAILED",
                    detailOverride: ex.Message,
                    preview: false);
                await dialog.ShowAsync();
            }
        }
    }

    private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isClosing) return;

        if (e.PropertyName == nameof(ViewModel.StatusText) ||
            e.PropertyName == nameof(ViewModel.ActiveJobsText) ||
            e.PropertyName == nameof(ViewModel.RecentJobText))
            UpdateStatusRail();
        if (e.PropertyName == nameof(ViewModel.HasActiveJobs))
            UpdateBackgroundProgress();
        if (e.PropertyName == nameof(ViewModel.IsIndexing))
            UpdateBackgroundProgress();
        if (e.PropertyName == nameof(ViewModel.IsFaceDetectionRunning))
            UpdateBackgroundProgress();
        if (e.PropertyName == nameof(ViewModel.IsAutoTaggingRunning))
            UpdateBackgroundProgress();
        if (e.PropertyName is nameof(ViewModel.ImageViewer))
            UpdateViewerVisibility();
        if (e.PropertyName is nameof(ViewModel.Settings))
            UpdateSettingsVisibility();
    }

    private void OnImageViewerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isClosing) return;

        if (e.PropertyName == nameof(ImageViewerViewModel.IsOpen))
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_isClosing) return;
                UpdateViewerHeader();
                UpdateRibbonVisibility();
            });
        if (e.PropertyName == nameof(ImageViewerViewModel.Title) ||
            e.PropertyName == nameof(ImageViewerViewModel.ImageInfo))
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_isClosing) return;
                TopRibbon.SetContextLabel(ViewModel.ImageViewer.Title ?? "");
                UpdateViewerHeader();
            });
        if (e.PropertyName == nameof(ImageViewerViewModel.IsVideo))
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_isClosing) UpdateRibbonVisibility();
            });
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_isClosing && e.PropertyName == nameof(ViewModel.Settings.IsOpen))
            UpdateSettingsVisibility();
    }

    private void UpdateStatusRail()
    {
        StatusBarText.Text = ViewModel.HasActiveJobs
            ? ViewModel.ActiveJobsText
            : !string.IsNullOrEmpty(ViewModel.RecentJobText)
                ? ViewModel.RecentJobText
                : ViewModel.StatusText;
    }

    private void UpdateBackgroundProgress()
    {
        IndexingProgress.IsActive =
            ViewModel.IsIndexing ||
            ViewModel.IsFaceDetectionRunning ||
            ViewModel.IsAutoTaggingRunning ||
            ViewModel.IsRecognitionRunning ||
            ViewModel.HasActiveJobs;
        UpdateStatusRail();
    }

    private void OnUserPointerActivity(
        object sender,
        PointerRoutedEventArgs e) =>
        ViewModel.NotifyUserActivity();

    private void OnUserKeyActivity(
        object sender,
        KeyRoutedEventArgs e)
    {
        ViewModel.NotifyUserActivity();
        if (e.Handled || e.Key != Windows.System.VirtualKey.A ||
            SettingsOverlay.Visibility == Visibility.Visible ||
            ViewerOverlay.Visibility == Visibility.Visible ||
            e.OriginalSource is TextBox or RichEditBox or PasswordBox or
                NumberBox or ComboBox)
            return;

        var ctrl = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
        if ((ctrl & Windows.UI.Core.CoreVirtualKeyStates.Down) !=
            Windows.UI.Core.CoreVirtualKeyStates.Down)
            return;

        ImageGridPanel.SelectAllDisplayedImages();
        e.Handled = true;
    }

    private async void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _isClosing = true;
        ViewModel.PropertyChanged -= OnMainViewModelPropertyChanged;
        ViewModel.ImageViewer.PropertyChanged -= OnImageViewerPropertyChanged;
        ViewModel.Settings.PropertyChanged -= OnSettingsPropertyChanged;
        ViewModel.ImageGrid.Cleanup();
        await ViewModel.CleanupAsync();
    }

    private void UpdateViewerVisibility()
    {
        ViewerOverlay.Visibility = ViewModel.ImageViewer.IsOpen
            ? Visibility.Visible : Visibility.Collapsed;
        UpdateViewerHeader();
        UpdateRibbonVisibility();
    }

    private void UpdateViewerHeader()
    {
        var active = ViewModel.ImageViewer.IsOpen;
        WorkspaceTitleText.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        WorkspaceSubtitleText.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        ViewerHeaderAssetName.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        ViewerHeaderImageInfo.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        ViewerHeaderAssetName.Text = active ? (ViewModel.ImageViewer.Title ?? "") : "";
        ViewerHeaderImageInfo.Text = active ? (ViewModel.ImageViewer.ImageInfo ?? "") : "";
    }

    private void UpdateRibbonVisibility()
    {
        if (!ViewModel.ImageViewer.IsOpen && _isViewerFullscreen)
            ExitViewerFullscreen();

        // Normal viewing uses the existing narrow side rail. Only crop mode
        // needs a horizontal strip for aspect ratio and Apply/Cancel controls.
        var viewerOpen = ViewModel.ImageViewer.IsOpen && !_isViewerFullscreen;
        TopRibbon.Visibility = viewerOpen && ViewerOverlay.IsCropping
            ? Visibility.Visible : Visibility.Collapsed;
        ViewerSideCommands.Visibility = viewerOpen && !ViewerOverlay.IsCropping
            ? Visibility.Visible : Visibility.Collapsed;
        NavigationRailDecorations.Visibility = viewerOpen
            ? Visibility.Collapsed : Visibility.Visible;
        SideCropButton.IsEnabled = viewerOpen && !ViewModel.ImageViewer.IsVideo &&
            !ViewerOverlay.IsManualFaceTaggingActive;
        var zoomEnabled = viewerOpen && !ViewModel.ImageViewer.IsVideo &&
            !ViewerOverlay.IsManualFaceTaggingActive && !ViewerOverlay.IsStraightening;
        SideZoomOutButton.IsEnabled = zoomEnabled;
        SideZoomFitButton.IsEnabled = zoomEnabled;
        SideZoom100Button.IsEnabled = zoomEnabled;
        SideZoomInButton.IsEnabled = zoomEnabled;
        SideDeleteButton.IsEnabled = viewerOpen && !ViewerOverlay.IsManualFaceTaggingActive;
        SideCancelFaceButton.Visibility = viewerOpen && ViewerOverlay.IsManualFaceTaggingActive
            ? Visibility.Visible : Visibility.Collapsed;

        // The fullscreen rail is an overlay; the main workspace remains 100% width.
        // Both rails invoke the same viewer handlers and respect the same mode guards.
        var fullscreenViewer = _isViewerFullscreen && ViewModel.ImageViewer.IsOpen;
        FullscreenSideRail.Visibility = fullscreenViewer
            ? Visibility.Visible : Visibility.Collapsed;
        FullscreenCropButton.IsEnabled = fullscreenViewer && !ViewModel.ImageViewer.IsVideo &&
            !ViewerOverlay.IsManualFaceTaggingActive;
        var fullscreenZoomEnabled = fullscreenViewer && !ViewModel.ImageViewer.IsVideo &&
            !ViewerOverlay.IsManualFaceTaggingActive && !ViewerOverlay.IsStraightening;
        FullscreenZoomOutButton.IsEnabled = fullscreenZoomEnabled;
        FullscreenZoomFitButton.IsEnabled = fullscreenZoomEnabled;
        FullscreenZoom100Button.IsEnabled = fullscreenZoomEnabled;
        FullscreenZoomInButton.IsEnabled = fullscreenZoomEnabled;
        FullscreenDeleteButton.IsEnabled = fullscreenViewer && !ViewerOverlay.IsManualFaceTaggingActive;
        FullscreenCancelFaceButton.Visibility = fullscreenViewer && ViewerOverlay.IsManualFaceTaggingActive
            ? Visibility.Visible : Visibility.Collapsed;
        if (!ViewModel.ImageViewer.IsOpen && ViewerOverlay.IsCropping)
        {
            ViewerOverlay.ExitCropMode();
            TopRibbon.ExitCropMode();
        }
    }

    private void OnRibbonCropClicked(object? sender, EventArgs e)
    {
        if (!ViewModel.ImageViewer.IsOpen) return;
        if (ViewModel.ImageViewer.IsVideo) return;

        ViewerOverlay.EnterCropMode();
        ViewerOverlay.CropOverlay.AspectRatio = _pendingAspect;
        TopRibbon.EnterCropMode();
        UpdateRibbonVisibility();
    }

    private async void OnRibbonAdjustClicked(object? sender, EventArgs e)
    {
        var entry = ViewModel.ImageViewer.CurrentEntry;
        if (entry is null || ViewModel.ImageViewer.IsVideo) return;
        if (!ImageEditRenderer.IsSupported(entry.FilePath))
        {
            ViewModel.StatusText = $"Editing not supported for {System.IO.Path.GetExtension(entry.FilePath)}";
            return;
        }

        if (ViewerOverlay.IsCropping)
        {
            ViewerOverlay.ExitCropMode();
            TopRibbon.ExitCropMode();
        }



        await ViewModel.ImageEditor.OpenForEditAsync(entry);
    }

    private void OnRibbonCancelCropClicked(object? sender, EventArgs e)
    {
        ViewerOverlay.ExitCropMode();
        TopRibbon.ExitCropMode();
        UpdateRibbonVisibility();
    }

    private void OnRibbonCropAspectChanged(object? sender, CropAspectRatio ratio)
    {
        _pendingAspect = ratio;
        if (ViewerOverlay.IsCropping)
            ViewerOverlay.CropOverlay.AspectRatio = ratio;
    }

    private async void OnRibbonApplyCropClicked(object? sender, EventArgs e)
    {
        if (!ViewerOverlay.IsCropping) return;
        var bounds = ViewerOverlay.CropOverlay.GetCropBoundsInImagePixels();
        var entry = ViewModel.ImageViewer.CurrentEntry;
        if (bounds is null || entry is null) return;

        TopRibbon.IsEnabled = false;
        var resumeFaceDetection = false;
        try
        {
            resumeFaceDetection = await ViewModel.PauseFaceDetectionAsync();
            ViewModel.StatusText = "Applying crop…";

            // Back up the original first (no-op if a backup already exists).
            await App.ViewModel.BackupService.BackupOriginalAsync(entry.FilePath);

            var result = await CropService.CropImageAsync(entry.FilePath, bounds.Value);
            await ViewModel.RefreshAfterCropAsync(entry.FilePath, result);
        }
        catch (Exception ex)
        {
            ViewModel.StatusText = $"Crop failed: {ex.Message}";
        }
        finally
        {
            ViewModel.ResumeFaceDetection(resumeFaceDetection);
            ViewerOverlay.ExitCropMode();
            TopRibbon.ExitCropMode();
            TopRibbon.IsEnabled = true;
            UpdateRibbonVisibility();
        }
    }

    private void UpdateSettingsVisibility()
    {
        SettingsOverlay.Visibility = ViewModel.Settings.IsOpen
            ? Visibility.Visible : Visibility.Collapsed;
        
        if (ViewModel.Settings.IsOpen)
        {
            SettingsPanel.Loaded += (s, e) => ViewModel.Settings.OpenCommand.Execute(null);
        }
    }

    private void OnSideViewerCrop(object sender, RoutedEventArgs e)
    {
        // Crop mode uses the aspect / Apply / Cancel ribbon. Exit true fullscreen
        // before showing it so crop controls are never inaccessible.
        if (_isViewerFullscreen) ExitViewerFullscreen();
        OnRibbonCropClicked(sender, EventArgs.Empty);
    }

    private void OnSideViewerBack(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ImageViewer.IsOpen)
            ViewModel.ImageViewer.CloseCommand.Execute(null);
    }

    private void OnSideZoomOut(object sender, RoutedEventArgs e) => ViewerOverlay.ZoomOutFromSide();
    private void OnSideZoomFit(object sender, RoutedEventArgs e) => ViewerOverlay.ZoomFitFromSide();
    private void OnSideZoom100(object sender, RoutedEventArgs e) => ViewerOverlay.ZoomActualSizeFromSide();
    private void OnSideZoomIn(object sender, RoutedEventArgs e) => ViewerOverlay.ZoomInFromSide();
    private void OnSideFullscreen(object sender, RoutedEventArgs e) => ViewerOverlay.ToggleFullscreenFromSide();
    private async void OnSideDelete(object sender, RoutedEventArgs e) =>
        await ViewerOverlay.DeleteCurrentFromSideAsync();
    private void OnSideCancelFace(object sender, RoutedEventArgs e) =>
        ViewerOverlay.ExitManualFaceTagging();

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        ViewModel.Settings.OpenCommand.Execute(null);
    }


}
