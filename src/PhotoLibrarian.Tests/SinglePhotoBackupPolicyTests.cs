using PhotoLibrarian.Core.Models;
using PhotoLibrarian.Core.Services;
using Xunit;

namespace PhotoLibrarian.Tests;

public sealed class SinglePhotoBackupPolicyTests
{
    private static string Read(string name) => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "TestAssets", name));

    [Fact]
    public void SinglePhotoActionsNeverRequestPermanentOriginalBackups()
    {
        var gallery = Read("ImageGridView.xaml.cs");
        var main = Read("MainViewModel.cs");
        var editor = Read("ImageEditService.cs");
        var editorVm = Read("ImageEditorViewModel.cs");
        var photoOps = Read("PhotoOperationsService.cs");

        var clearContext = gallery.Split("var clearTags = new MenuFlyoutItem", 2)[1]
            .Split("menu.Items.Add(clearTags);", 2)[0];
        Assert.Contains("ClearSelectedImageTagsAsync(", clearContext);
        Assert.DoesNotContain("BackupOriginalAsync", clearContext);

        var clearTags = main.Split("public async Task ClearSelectedImageTagsAsync(", 2)[1]
            .Split("public async Task<PhotoPrivacyResult> AnonymizePhotoMetadataAsync(", 2)[0];
        Assert.Contains("TagWriterService.WriteTagsToSidecarAsync", clearTags);
        Assert.DoesNotContain("BackupOriginalAsync", clearTags);
        Assert.DoesNotContain("File.Copy(", clearTags);

        var rotate = photoOps.Split("public async Task<(uint Width, uint Height)> RotateAsync(", 2)[1]
            .Split("// -----------------------------------------------------------------", 2)[0];
        Assert.Contains("RenderRotatedAsync", rotate);
        Assert.DoesNotContain("BackupOriginalAsync", rotate);

        Assert.DoesNotContain("BackupOriginalAsync", editor);
        Assert.DoesNotContain("HasBackup = true;", editorVm);

        // Context privacy cleanup has no retained backup, but the full album
        // continues to honor the explicit user's backup preference.
        Assert.Contains("keepBackups: false", gallery);
        Assert.Contains("keepBackups: retain", Read("SettingsPanel.xaml.cs"));
    }

    [Fact]
    public async Task SavingEditorAdjustmentsDoesNotStoreOriginalPhotos()
    {
        var dir = Path.Combine(Path.GetTempPath(),
            "plib-single-editor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var photo = Path.Combine(dir, "test.jpg");
            var original = new byte[] { 0xff, 0xd8, 0xff, 0xd9 };
            await File.WriteAllBytesAsync(photo, original);
            var backupDir = Path.Combine(dir, "Originals");
            var backup = new OriginalBackupService(backupDir);
            var edits = new ImageEditService(backup);

            var rendered = false;
            var result = await edits.ApplyEditsAsync(
                photo,
                new EditParameters { Brightness = 0.2 },
                (path, _) =>
                {
                    rendered = true;
                    Assert.Equal(photo, path);
                    return Task.FromResult((2u, 2u));
                });

            Assert.True(rendered);
            Assert.Equal(2u, result.Width);
            Assert.Equal(2u, result.Height);
            Assert.Equal(original, await File.ReadAllBytesAsync(photo));
            Assert.Empty(Directory.EnumerateFiles(backupDir, "*",
                SearchOption.AllDirectories));
            Assert.False(await backup.HasBackupAsync(photo));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ClearingContextKeywordsDoesNotRetainPhotoBackups()
    {
        var dir = Path.Combine(Path.GetTempPath(),
            "plib-single-tags-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // RAW-like files use the XMP fallback; the context command only
            // changes the keyword list, not a copy of the source image.
            var photo = Path.Combine(dir, "picture.cr3");
            var bytes = new byte[] { 9, 8, 7, 6 };
            await File.WriteAllBytesAsync(photo, bytes);
            await TagWriterService.WriteTagsToSidecarAsync(photo,
                new[] { "Personal", "Vacation" });
            Assert.Equal(2, TagWriterService.ReadTagsFromSidecar(photo).Count);

            await TagWriterService.WriteTagsToSidecarAsync(photo,
                Array.Empty<string>());

            Assert.Empty(TagWriterService.ReadTagsFromSidecar(photo));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(photo));
            Assert.Empty(Directory.EnumerateFiles(dir, "*.tmp",
                SearchOption.AllDirectories));
            Assert.False(Directory.Exists(Path.Combine(dir, ".album")));
            Assert.False(Directory.Exists(Path.Combine(dir, "Originals")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MetadataTranscodeUsesStagedCommitRatherThanTruncatingSource()
    {
        var writer = Read("EmbeddedMetadataWriter.cs");
        Assert.Contains("File.Move(temporaryPath, filePath, overwrite: true)", writer);
        Assert.Contains("await output.FlushAsync()", writer);
        Assert.DoesNotContain("outStream.Size = 0", writer);
    }
}
