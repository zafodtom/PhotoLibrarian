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
        TopRibbon.CropClicked += OnRibbonCropClicked;
        TopRibbon.MakeCopyClicked += OnRibbonMakeCopyClicked;
        TopRibbon.StraightenClicked += OnRibbonStraightenClicked;
        TopRibbon.CloseViewerClicked += (_, _) => ViewModel.ImageViewer.CloseCommand.Execute(null);
        TopRibbon.AdjustClicked += OnRibbonAdjustClicked;
        TopRibbon.RedEyeClicked += OnRibbonRedEyeClicked;
        TopRibbon.ApplyCropClicked += OnRibbonApplyCropClicked;
        TopRibbon.CancelCropClicked += OnRibbonCancelCropClicked;
        TopRibbon.CropAspectChanged += OnRibbonCropAspectChanged;
        TopRibbon.ApplyStraightenClicked += OnRibbonApplyStraightenClicked;
        TopRibbon.CancelStraightenClicked += OnRibbonCancelStraightenClicked;
        ViewerOverlay.CropApplyRequested += OnRibbonApplyCropClicked;
        ViewerOverlay.CropCancelRequested += OnRibbonCancelCropClicked;
        ViewerOverlay.StraightenApplyRequested += OnRibbonApplyStraightenClicked;
        ViewerOverlay.StraightenCancelRequested += OnRibbonCancelStraightenClicked;
        ViewerOverlay.RedEyeSelectionCompleted += OnRedEyeSelectionCompleted;
        ViewerOverlay.ManualFaceTaggingExited += (_, _) => ViewModel.OnManualFaceTaggingExited();

        // Cleanup on window close
        this.Closed += OnWindowClosed;
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
                var dialog = new ContentDialog
                {
                    Title = "FATAL ERROR // ALBUM OPEN",
                    Content = ex.Message,
                    CloseButtonText = "ACKNOWLEDGE",
                    XamlRoot = AppRoot.XamlRoot,
                    RequestedTheme = ElementTheme.Dark
                };
                await dialog.ShowAsync();
            }
        }
    }

    private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isClosing) return;

        if (e.PropertyName == nameof(ViewModel.StatusText))
            StatusBarText.Text = ViewModel.StatusText;
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
                if (!_isClosing) UpdateRibbonVisibility();
            });
        if (e.PropertyName == nameof(ImageViewerViewModel.Title))
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_isClosing)
                {
                    TopRibbon.SetContextLabel(ViewModel.ImageViewer.Title ?? "");
                    TopRibbon.SetMakeCopyEnabled(!ViewModel.ImageViewer.IsVideo);
                }
            });
        if (e.PropertyName == nameof(ImageViewerViewModel.IsVideo))
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_isClosing)
                    TopRibbon.SetMakeCopyEnabled(!ViewModel.ImageViewer.IsVideo);
            });
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_isClosing && e.PropertyName == nameof(ViewModel.Settings.IsOpen))
            UpdateSettingsVisibility();
    }

    private void UpdateBackgroundProgress()
    {
        IndexingProgress.IsActive =
            ViewModel.IsIndexing ||
            ViewModel.IsFaceDetectionRunning ||
            ViewModel.IsAutoTaggingRunning;
    }

    private void OnUserPointerActivity(
        object sender,
        PointerRoutedEventArgs e) =>
        ViewModel.NotifyUserActivity();

    private void OnUserKeyActivity(
        object sender,
        KeyRoutedEventArgs e) =>
        ViewModel.NotifyUserActivity();

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
        UpdateRibbonVisibility();
    }

    private void UpdateRibbonVisibility()
    {
        if (!ViewModel.ImageViewer.IsOpen && _isViewerFullscreen)
            ExitViewerFullscreen();

        TopRibbon.Visibility =
            ViewModel.ImageViewer.IsOpen && !_isViewerFullscreen
                ? Visibility.Visible
                : Visibility.Collapsed;
        if (!ViewModel.ImageViewer.IsOpen && ViewerOverlay.IsCropping)
        {
            ViewerOverlay.ExitCropMode();
            TopRibbon.ExitCropMode();
        }
        if (!ViewModel.ImageViewer.IsOpen && ViewerOverlay.IsStraightening)
        {
            ViewerOverlay.ExitStraightenMode();
            TopRibbon.ExitStraightenMode();
        }
        if (!ViewModel.ImageViewer.IsOpen && ViewerOverlay.IsRedEyeRemoving)
        {
            ViewerOverlay.ExitRedEyeMode();
            TopRibbon.ExitRedEyeMode();
        }
    }

    private void OnRibbonCropClicked(object? sender, EventArgs e)
    {
        if (!ViewModel.ImageViewer.IsOpen) return;
        if (ViewModel.ImageViewer.IsVideo) return;
        if (ViewerOverlay.IsStraightening)
        {
            ViewerOverlay.ExitStraightenMode();
            TopRibbon.ExitStraightenMode();
        }
        ViewerOverlay.EnterCropMode();
        ViewerOverlay.CropOverlay.AspectRatio = _pendingAspect;
        TopRibbon.EnterCropMode();
    }

    private async void OnRibbonMakeCopyClicked(object? sender, EventArgs e)
    {
        var entry = ViewModel.ImageViewer.CurrentEntry;
        if (entry is null || entry.MediaType != Core.Models.MediaType.Image) return;

        TopRibbon.IsEnabled = false;
        ViewModel.StatusText = "Creating a copy…";
        try
        {
            await ViewModel.MakeCopyForEditingAsync(entry);
        }
        catch (Exception ex)
        {
            ViewModel.StatusText = $"Make a copy failed: {ex.Message}";
        }
        finally
        {
            TopRibbon.IsEnabled = true;
        }
    }

    private void OnRibbonStraightenClicked(object? sender, EventArgs e)
    {
        var entry = ViewModel.ImageViewer.CurrentEntry;
        if (entry is null || ViewModel.ImageViewer.IsVideo) return;
        if (ViewerOverlay.CurrentImagePixelWidth == 0 || ViewerOverlay.CurrentImagePixelHeight == 0)
            return;
        if (!ImageEditRenderer.IsSupported(entry.FilePath))
        {
            ViewModel.StatusText =
                $"Editing not supported for {System.IO.Path.GetExtension(entry.FilePath)}";
            return;
        }

        if (ViewerOverlay.IsCropping)
        {
            ViewerOverlay.ExitCropMode();
            TopRibbon.ExitCropMode();
        }

        ViewerOverlay.EnterStraightenMode();
        TopRibbon.EnterStraightenMode();
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
        if (ViewerOverlay.IsStraightening)
        {
            ViewerOverlay.ExitStraightenMode();
            TopRibbon.ExitStraightenMode();
        }
        if (ViewerOverlay.IsRedEyeRemoving)
        {
            ViewerOverlay.ExitRedEyeMode();
            TopRibbon.ExitRedEyeMode();
        }

        await ViewModel.ImageEditor.OpenForEditAsync(entry);
    }

    private void OnRibbonRedEyeClicked(object? sender, EventArgs e)
    {
        var entry = ViewModel.ImageViewer.CurrentEntry;
        if (entry is null || ViewModel.ImageViewer.IsVideo) return;
        if (!ImageEditRenderer.IsSupported(entry.FilePath))
        {
            ViewModel.StatusText =
                $"Editing not supported for {System.IO.Path.GetExtension(entry.FilePath)}";
            return;
        }

        if (ViewerOverlay.IsCropping) OnRibbonCancelCropClicked(sender, e);
        if (ViewerOverlay.IsStraightening) OnRibbonCancelStraightenClicked(sender, e);
        ViewerOverlay.EnterRedEyeMode();
        TopRibbon.EnterRedEyeMode();
    }

    private async void OnRedEyeSelectionCompleted(object? sender, RedEyeBounds bounds)
    {
        var entry = ViewModel.ImageViewer.CurrentEntry;
        if (entry is null) return;

        TopRibbon.IsEnabled = false;
        try
        {
            await ViewModel.BackupService.BackupOriginalAsync(entry.FilePath);
            ViewModel.StatusText = "Removing red eye…";
            var changed = await ImageEditRenderer.RemoveRedEyeAsync(entry.FilePath, bounds);
            await ViewModel.RefreshAfterPixelEditAsync(
                entry.FilePath,
                ViewerOverlay.CurrentImagePixelWidth,
                ViewerOverlay.CurrentImagePixelHeight,
                changed == 0 ? "No red eye found in selection" : "Removed red eye from");
        }
        catch (Exception ex)
        {
            ViewModel.StatusText = $"Red-eye removal failed: {ex.Message}";
        }
        finally
        {
            ViewerOverlay.ExitRedEyeMode();
            TopRibbon.ExitRedEyeMode();
            TopRibbon.IsEnabled = true;
        }
    }

    private void OnRibbonCancelCropClicked(object? sender, EventArgs e)
    {
        ViewerOverlay.ExitCropMode();
        TopRibbon.ExitCropMode();
    }

    private void OnRibbonCancelStraightenClicked(object? sender, EventArgs e)
    {
        ViewerOverlay.ExitStraightenMode();
        TopRibbon.ExitStraightenMode();
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
        }
    }

    private async void OnRibbonApplyStraightenClicked(object? sender, EventArgs e)
    {
        if (!ViewerOverlay.IsStraightening) return;

        var entry = ViewModel.ImageViewer.CurrentEntry;
        if (entry is null)
        {
            OnRibbonCancelStraightenClicked(sender, e);
            return;
        }

        var angle = ViewerOverlay.StraightenAngle;
        if (Math.Abs(angle) < 0.05)
        {
            ViewModel.StatusText = "No straighten adjustment to apply";
            OnRibbonCancelStraightenClicked(sender, e);
            return;
        }

        TopRibbon.IsEnabled = false;
        ViewModel.StatusText = "Applying straighten…";
        try
        {
            await ViewModel.BackupService.BackupOriginalAsync(entry.FilePath);
            var (width, height) =
                await ImageEditRenderer.RenderStraightenedAsync(entry.FilePath, angle);
            await ViewModel.RefreshAfterStraightenAsync(entry.FilePath, width, height);
        }
        catch (Exception ex)
        {
            ViewModel.StatusText = $"Straighten failed: {ex.Message}";
        }
        finally
        {
            ViewerOverlay.ExitStraightenMode();
            TopRibbon.ExitStraightenMode();
            TopRibbon.IsEnabled = true;
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

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        ViewModel.Settings.OpenCommand.Execute(null);
    }


}
