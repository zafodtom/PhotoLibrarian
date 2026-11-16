using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoLibrarian.ViewModels;
using PhotoLibrarian.Core.Models;
using PhotoLibrarian.Core.Data;
using System.Linq;
using System.IO;

namespace PhotoLibrarian.Views;

public sealed partial class ManageFoldersDialog : ContentDialog
{
    private FolderNavigationViewModel? ViewModel => App.ViewModel?.FolderNav;

    public ManageFoldersDialog()
    {
        this.InitializeComponent();
        this.Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshFolderList();
    }

    private void RefreshFolderList()
    {
        if (ViewModel is null) return;
        FoldersList.ItemsSource = ViewModel.RootFolders.ToList();
    }

    private async void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.AddFolderCommand.CanExecute(null) == true)
        {
            await ViewModel.AddFolderCommand.ExecuteAsync(null);
            RefreshFolderList();
        }
    }

    private async void OnRemoveFolderItemClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;

        // Get the folder from the button's DataContext
        if (sender is Button button && button.DataContext is FolderNode folder)
        {
            ViewModel.SelectedFolder = folder;
            
            if (ViewModel.RemoveFolderCommand.CanExecute(null))
            {
                await ViewModel.RemoveFolderCommand.ExecuteAsync(null);
                RefreshFolderList();
            }
        }
    }

    private async void OnClearDatabaseClick(object sender, RoutedEventArgs e)
    {
        // Database rebuild is an album-wide maintenance operation.
        var confirmDialog = CinematicDialogShowcase.Create(
            XamlRoot,
            CinematicDialogShowcase.Variant.Warning,
            subjectOverride: "CLEAR CACHE DATABASE?",
            detailOverride:
                "This removes all cached metadata and thumbnails. Original photos " +
                "are preserved, but the application must restart before the library " +
                "can be indexed again. This can take several minutes.",
            confirmLabel: "CLEAR AND RE-INDEX",
            preview: false);
        var result = await confirmDialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return;

        try
        {
            ClearDatabaseBtn.IsEnabled = false;
            if (App.ViewModel is not null)
            {
                await App.ViewModel.EnsureFaceMetadataPersistedAsync();
            }

            // Get database path
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dataDir = Path.Combine(appData, "PhotoLibrarian");
            var dbPath = Path.Combine(dataDir, "cache.db");

            // Close and delete database files
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
            if (File.Exists(dbPath + "-shm"))
            {
                File.Delete(dbPath + "-shm");
            }
            if (File.Exists(dbPath + "-wal"))
            {
                File.Delete(dbPath + "-wal");
            }

            var successDialog = CinematicDialogShowcase.Create(
                XamlRoot,
                CinematicDialogShowcase.Variant.Complete,
                subjectOverride: "CACHE DATABASE CLEARED",
                detailOverride:
                    "The cache database was removed. PhotoLibrarian will now restart " +
                    "and rebuild the local library index.",
                preview: false);
            await successDialog.ShowAsync();

            // Close this dialog
            Hide();

            // Request app restart
            Microsoft.Windows.AppLifecycle.AppInstance.Restart("Database cleared");
        }
        catch (Exception ex)
        {
            var errorDialog = CinematicDialogShowcase.Create(
                XamlRoot,
                CinematicDialogShowcase.Variant.Error,
                subjectOverride: "CACHE RESET FAILED",
                detailOverride:
                    $"Failed to clear database: {ex.Message}\n\n" +
                    "You may need to close the application and manually remove " +
                    "the cache database from %LOCALAPPDATA%\\PhotoLibrarian\\cache.db.",
                preview: false);
            await errorDialog.ShowAsync();
        }
        finally
        {
            ClearDatabaseBtn.IsEnabled = true;
        }
    }
}
