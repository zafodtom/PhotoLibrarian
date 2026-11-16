using PhotoLibrarian.Core.Data;
using PhotoLibrarian.Core.Models;
using PhotoLibrarian.Services;
using Xunit;

namespace PhotoLibrarian.Tests;

public sealed class PhotoPrivacyWorkflowTests
{
    private static string ReadAsset(string filename) => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "TestAssets", filename));

    [Fact]
    public void PhotoAndAlbumControlsRequireExplicitConfirmation()
    {
        var grid = ReadAsset("ImageGridView.xaml.cs");
        var settings = ReadAsset("SettingsPanel.xaml");
        var settingsCode = ReadAsset("SettingsPanel.xaml.cs");

        Assert.Contains("Anonymize photo metadata…", grid);
        Assert.Contains("ANONYMIZE SELECTED PHOTOS?", grid);
        Assert.Contains("await warning.ShowAsync() != ContentDialogResult.Primary", grid);
        Assert.Contains("App.ViewModel.AnonymizePhotoMetadataAsync(", grid);
        Assert.Contains("AnonymizeAlbumButton", settings);
        Assert.Contains("KeepPrivacyBackupsCheckBox", settings);
        Assert.Contains("AnonymizeAlbumProgressBar", settings);
        Assert.Contains("OnCancelAnonymizeAlbum", settingsCode);
        Assert.Contains("ANONYMIZE ENTIRE ALBUM?", settingsCode);
        Assert.Contains("await dialog.ShowAsync() != ContentDialogResult.Primary", settingsCode);
        Assert.Contains("PhotoPrivacyProgress", settingsCode);
    }

    [Fact]
    public void PrivacyCompletionDialogsAreStatusReportsNotNewConfirmations()
    {
        var grid = ReadAsset("ImageGridView.xaml.cs");
        var settings = ReadAsset("SettingsPanel.xaml.cs");
        var factory = ReadAsset("CinematicDialogShowcase.cs");

        Assert.Contains("captionOverride: \"PRIVACY REPORT // PHOTO\"", grid);
        Assert.Contains("captionOverride: \"PRIVACY REPORT // ALBUM\"", settings);
        Assert.Contains("footerOverride: \"OPERATION FINISHED // REVIEW CLEANED AND SKIPPED FILES\"", grid);
        Assert.Contains("footerOverride: \"OPERATION FINISHED // REVIEW CLEANED AND SKIPPED FILES\"", settings);
        Assert.Contains("No photographs were anonymized.", grid);
        Assert.Contains("No photos were anonymized.", settings);
        Assert.Contains("footerOverride ??", factory);
    }

    [Fact]
    public async Task UnsupportedRawFilesAndTheirXmpRemainUntouched()
    {
        var album = Path.Combine(Path.GetTempPath(),
            "plib-privacy-unsupported-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(album, "raw");
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(Path.Combine(album, ".album"));
        var photoPath = Path.Combine(folder, "frame.cr3");
        var sidecarPath = Path.ChangeExtension(photoPath, ".xmp");
        await File.WriteAllBytesAsync(photoPath, new byte[] { 1, 2, 3, 4 });
        await File.WriteAllTextAsync(sidecarPath, "original tags and GPS");
        try
        {
            var called = false;
            var result = await PhotoPrivacyCleanupService.RunAsync(
                album,
                new[] { new ImageEntry
                {
                    FilePath = photoPath,
                    FileName = "frame.cr3",
                    MediaType = MediaType.Image
                } },
                (_, _, _, _, _) =>
                {
                    called = true;
                    return Task.CompletedTask;
                },
                keepBackups: false);

            Assert.Equal(0, result.Cleaned);
            Assert.Equal(1, result.Skipped);
            Assert.Equal(0, result.Failed);
            Assert.False(called);
            Assert.Equal(new byte[] { 1, 2, 3, 4 },
                await File.ReadAllBytesAsync(photoPath));
            Assert.Equal("original tags and GPS",
                await File.ReadAllTextAsync(sidecarPath));
        }
        finally
        {
            Directory.Delete(album, recursive: true);
        }
    }

    [Fact]
    public async Task PhotoDatabasePrivacyClearIsAtomicAndRemovesSensitiveRows()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "plib-privacy-db-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var db = new CacheDatabase(Path.Combine(root, "test.db"));
        try
        {
            await db.InitializeAsync();
            var images = new ImageRepository(db);
            var tags = new TagRepository(db);
            var id = await images.UpsertImageAsync(new ImageEntry
            {
                FilePath = Path.Combine(root, "portrait.jpg"),
                FileName = "portrait.jpg",
                FileSize = 999,
                Width = 100,
                Height = 150,
                DateModified = DateTime.UtcNow,
                DateTaken = new DateTime(2024, 7, 12),
                CameraMake = "Original Camera",
                CameraModel = "Model",
                GpsLatitude = 50.75,
                GpsLongitude = 15.05,
                Rating = 5,
                Orientation = 6,
                IsFlagged = true
            });
            await tags.AddTagAsync(new ImageTag
            {
                ImageId = id,
                Tag = "Personal",
                Source = TagSource.Manual,
                Confidence = 1
            });
            await images.ClearPhotoPrivacyAsync(id, 123, DateTime.UtcNow, 150, 100);
            var cleaned = await images.GetByPathAsync(Path.Combine(root, "portrait.jpg"));
            Assert.NotNull(cleaned);
            Assert.Null(cleaned.DateTaken);
            Assert.Null(cleaned.CameraMake);
            Assert.Null(cleaned.CameraModel);
            Assert.Null(cleaned.GpsLatitude);
            Assert.Null(cleaned.GpsLongitude);
            Assert.Null(cleaned.Rating);
            Assert.False(cleaned.IsFlagged);
            Assert.Equal(1, cleaned.Orientation);
            Assert.Equal(150, cleaned.Width);
            Assert.Equal(100, cleaned.Height);
            Assert.Empty(await tags.GetTagsAsync(id));
        }
        finally
        {
            db.Dispose();
            // SQLite shared cache handles are released with CacheDatabase.
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }
}
