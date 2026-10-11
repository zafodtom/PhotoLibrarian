using Microsoft.UI.Xaml;
using PhotoLibrarian.Diagnostics;
using PhotoLibrarian.Core.Data;
using PhotoLibrarian.Core.Services;
using PhotoLibrarian.ML.Services;
using PhotoLibrarian.ViewModels;
using System;
using System.Threading.Tasks;

namespace PhotoLibrarian;

public partial class App : Application
{
    private Window? _window;

    public static MainViewModel ViewModel { get; private set; } = null!;
    public static string? CurrentAlbumPath { get; private set; }
    public static bool HasActiveAlbum => !string.IsNullOrWhiteSpace(CurrentAlbumPath);

    /// <summary>
    /// Global logging control. Set to false to disable debug logging.
    /// </summary>
    public static bool EnableDebugLogging
    {
        get => Diagnostics.DebugLog.EnableLogging;
        set
        {
            Diagnostics.DebugLog.EnableLogging = value;
            Core.Diagnostics.DebugLog.EnableLogging = value;
        }
    }

    public App()
    {
        this.InitializeComponent();

        // Enable debug logging by default (set to false to disable)
        EnableDebugLogging = true;

        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnUnhandledException(
        object sender,
        Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        CrashLog.Write("WinUI UnhandledException", e.Exception, CurrentAlbumPath);
    }

    private void OnAppDomainUnhandledException(
        object sender,
        System.UnhandledExceptionEventArgs e)
    {
        CrashLog.Write("AppDomain.UnhandledException", e.ExceptionObject, CurrentAlbumPath);
    }

    private void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        CrashLog.Write("TaskScheduler.UnobservedTaskException", e.Exception, CurrentAlbumPath);
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        BootWindow? bootWindow = null;
        var bootStopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            CurrentAlbumPath = AlbumService.GetAlbumPathFromCommandLine();

            bootWindow = new BootWindow();
            bootWindow.Activate();
            bootWindow.SetPhase(
                1,
                5,
                "RESOLVING ARCHIVE",
                CurrentAlbumPath is null
                    ? "Preparing clean local session..."
                    : "Resolving portable album root...");
            // Give WinUI a real render opportunity before startup work continues.
            await Task.Delay(90);

        string dbPath;
        if (CurrentAlbumPath is not null)
        {
            CurrentAlbumPath = AlbumService.EnsureAlbum(CurrentAlbumPath);
            dbPath = AlbumService.GetAlbumCachePath(CurrentAlbumPath);
        }
        else
        {
            dbPath = AlbumService.PrepareCleanSessionCache();
        }

        AlbumPathStorage.Configure(CurrentAlbumPath);

        bootWindow.SetPhase(
            2,
            18,
            "OPENING CACHE",
            "Connecting local cache database...");

        // Create services
        var db = new CacheDatabase(dbPath);
        await db.InitializeAsync();

        bootWindow.SetPhase(
            3,
            34,
            "VALIDATING DATABASE",
            "Checking portable paths and album schema...");
        if (CurrentAlbumPath is not null)
            await db.MigrateAlbumPathsToRelativeAsync(CurrentAlbumPath);

        bootWindow.SetPhase(
            4,
            48,
            "LINKING SERVICES",
            "Connecting metadata, index and thumbnail subsystems...");

        var imageRepo = new ImageRepository(db);
        var tagRepo = new TagRepository(db);
        var faceRepo = new FaceRepository(db);
        var scanner = new FolderScannerService();
        var metadataReader = new MetadataReaderService();
        var faceMetadataStore = new FaceMetadataStore();
        var indexingService = new LibraryIndexingService(
            db,
            imageRepo,
            tagRepo,
            faceRepo,
            scanner,
            metadataReader,
            faceMetadataStore);
        var backupService = new OriginalBackupService();
        var sessionManager = new OnnxSessionManager();
        var faceDetectionService = new FaceDetectionService(sessionManager);
        var faceEmbeddingService = new FaceEmbeddingService(sessionManager);
        var faceReviewService = new FaceReviewService(
            faceRepo,
            imageRepo,
            new FaceClusteringService(),
            new FaceRecognitionService(),
            faceMetadataStore,
            new RecentPeopleStore());
        var activityGate = new UserActivityGate();
        var autoTagModelManager = new AutoTagModelManager(sessionManager);
        var autoTaggingService = new AutoTaggingService(
            sessionManager,
            autoTagModelManager);
        var recognitionPipeline = new RecognitionPipeline(
            faceRepo,
            tagRepo,
            new FaceModelProvider(sessionManager),
            faceDetectionService,
            faceEmbeddingService,
            autoTagModelManager,
            autoTaggingService,
            activityGate,
            new WindowsImageDecoder());
        var autoTagBenchmarkProcessor = new AutoTagBenchmarkProcessor(
            autoTagModelManager,
            autoTaggingService);
        var autoTaggingSettingsStore = new AutoTaggingSettingsStore();

        // Note: ThumbnailRepository removed - we use Windows thumbnail cache instead
        bootWindow.SetPhase(
            5,
            62,
            "INITIALIZING WORKSPACE",
            "Constructing local archive control matrix...");

        ViewModel = new MainViewModel(
            db,
            imageRepo,
            tagRepo,
            faceRepo,
            scanner,
            metadataReader,
            indexingService,
            backupService,
            recognitionPipeline,
            faceReviewService,
            sessionManager,
            autoTaggingSettingsStore,
            autoTagModelManager,
            autoTagBenchmarkProcessor,
            activityGate);

        _window = new MainWindow();

        await ViewModel.InitializeAsync();

        bootWindow.SetPhase(
            6,
            76,
            "MOUNTING ARCHIVE",
            CurrentAlbumPath is null
                ? "Preparing album selection interface..."
                : "Loading album navigation and cached records...");

        if (CurrentAlbumPath is not null)
        {
            await ViewModel.FolderNav.AddOrSelectFolderAsync(CurrentAlbumPath);

            // RootFolders is populated only after AddOrSelectFolderAsync. Starting
            // the startup scan before this point silently did nothing for existing
            // albums because StartBackgroundIndexing saw an empty folder list.
            bootWindow.SetPhase(
                7,
                91,
                "STARTING VISUAL FEED",
                "Attaching folder tree and background index feed...");

            ViewModel.StartBackgroundIndexing();
            ViewModel.StatusText = $"Album: {CurrentAlbumPath}";
        }
        else
        {
            ViewModel.StatusText = "Open or create an album";
        }

        bootWindow.SetPhase(
            8,
            100,
            "VISUAL FEED READY",
            CurrentAlbumPath is null
                ? "Archive console ready."
                : "Album mounted. Rendering workspace...");

        const int minimumBootMs = 5000;
        var remainingBootMs =
            minimumBootMs - (int)bootStopwatch.ElapsedMilliseconds;
        if (remainingBootMs > 0)
            await Task.Delay(remainingBootMs);

        _window.Activate();
        await Task.Delay(180);
        bootWindow.Close();
        bootWindow = null;
        }
        catch (Exception ex)
        {
            try
            {
                if (bootWindow is not null)
                {
                    bootWindow.SetPhase(
                        99,
                        100,
                        "INITIALIZATION FAILED",
                        ex.Message,
                        warning: false,
                        fatal: true);
                    await Task.Delay(1200);
                    bootWindow.Close();
                }
            }
            catch
            {
            }

            CrashLog.Write("App.OnLaunched", ex, CurrentAlbumPath);
            throw;
        }
    }

    public static new App Current => (App)Application.Current;
    public static Window? MainWindow => ((App)Application.Current)._window;
}
