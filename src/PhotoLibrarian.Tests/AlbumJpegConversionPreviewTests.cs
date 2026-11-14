using PhotoLibrarian.Core.Services;
using PhotoLibrarian.Services;
using Xunit;

namespace PhotoLibrarian.Tests;

public sealed class AlbumJpegConversionPreviewTests
{
    [Fact]
    public async Task Convert_PngWithExistingJpg_CreatesNumberedJpegAndPreservesOriginal()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "PL-jpeg-collision-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".album"));
        try
        {
            var png = Path.Combine(root, "photo.png");
            var jpeg = Path.Combine(root, "photo.jpg");
            var suffixed = Path.Combine(root, "photo_001.jpg");
            await WicTestImage.CreateAsync(png, 8, 6);
            await WicTestImage.CreateAsync(jpeg, 4, 4, jpeg: true);
            var originalJpeg = await File.ReadAllBytesAsync(jpeg);
            var originalPng = await File.ReadAllBytesAsync(png);
            var preview = AlbumJpegConversionService.Preview(root);
            Assert.Equal(1, preview.ConvertCount);
            Assert.Equal(1, preview.TargetConflictCount);
            Assert.Equal(1, preview.PendingCount);

            var result = await AlbumJpegConversionService.RunAsync(root, null);
            Assert.Equal(1, result.Converted);
            Assert.Equal(0, result.Skipped);
            Assert.Equal(0, result.Failed);
            Assert.True(File.Exists(suffixed));
            Assert.Equal((8u, 6u), await WicTestImage.ReadSizeAsync(suffixed));
            Assert.Equal(originalJpeg, await File.ReadAllBytesAsync(jpeg));
            Assert.False(File.Exists(png));
            Assert.Equal(originalPng, await File.ReadAllBytesAsync(
                Path.Combine(result.BackupDirectory, "photo.png")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Convert_ChoosesNextFreeSuffixForHeicAndDoesNotTouchOriginal()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "PL-heic-suffix-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var heic = Path.Combine(root, "photo.heic");
            File.WriteAllBytes(heic, [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(root, "photo.jpg"), [4]);
            File.WriteAllBytes(Path.Combine(root, "photo_001.jpg"), [5]);
            Assert.Equal(Path.Combine(root, "photo_002.jpg"),
                AlbumJpegConversionService.ChooseJpegDestination(heic));
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(heic));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Preview_DoesNotOverwriteJpegsOrTraverseAlbumBackups()
    {
        var root = Path.Combine(Path.GetTempPath(), "PhotoLibrarian-jpeg-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".album", "jpeg-conversion-backups", "old"));
        try
        {
            File.WriteAllBytes(Path.Combine(root, "a.png"), [1]);
            File.WriteAllBytes(Path.Combine(root, "a.jpg"), [1]);
            File.WriteAllBytes(Path.Combine(root, "b.webp"), [1]);
            File.WriteAllBytes(Path.Combine(root, "c.jpg"), [1]);
            File.WriteAllText(Path.Combine(root, "c.xmp"), "<xmp />");
            File.WriteAllBytes(Path.Combine(root, "raw.cr3"), [1]);
            File.WriteAllBytes(Path.Combine(root, ".album", "jpeg-conversion-backups", "old", "hidden.png"), [1]);
            Directory.CreateDirectory(Path.Combine(root, "different-album", ".album"));
            File.WriteAllBytes(Path.Combine(root, "different-album", "foreign.png"), [1]);

            var preview = AlbumJpegConversionService.Preview(root);
            Assert.Equal(2, preview.ConvertCount);
            Assert.Equal(1, preview.JpegMetadataCount);
            Assert.Equal(1, preview.TargetConflictCount);
            Assert.Equal(1, preview.UnsupportedCount);
            Assert.Equal(3, preview.PendingCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Convert_EmbedsEntireSidecarAndKeepsRestorableOriginal()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "PhotoLibrarian-jpeg-convert-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".album"));
        try
        {
            var source = Path.Combine(root, "photo.png");
            var sidecar = Path.ChangeExtension(source, ".xmp");
            await WicTestImage.CreateAsync(source, 7, 5);
            var xmp = System.Text.Encoding.UTF8.GetBytes(
                "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">" +
                "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">" +
                "<rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" " +
                "dc:format=\"image/png\" /></rdf:RDF></x:xmpmeta>");
            await File.WriteAllBytesAsync(sidecar, xmp);

            var result = await AlbumJpegConversionService.RunAsync(root, null);
            Assert.Equal(1, result.Converted);
            Assert.Equal(1, result.EmbeddedXmp);
            Assert.Equal(0, result.Failed);
            var jpeg = Path.ChangeExtension(source, ".jpg");
            Assert.True(File.Exists(jpeg));
            Assert.False(File.Exists(source));
            Assert.False(File.Exists(sidecar));
            Assert.True(JpegXmpPacket.ContainsExactPacket(
                await File.ReadAllBytesAsync(jpeg), xmp));
            var dimensions = await WicTestImage.ReadSizeAsync(jpeg);
            Assert.Equal((7u, 5u), dimensions);
            Assert.True(File.Exists(Path.Combine(result.BackupDirectory, "photo.png")));
            Assert.True(File.Exists(Path.Combine(result.BackupDirectory, "photo.xmp")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Preview_RequiresAnExistingPhotoLibrarianAlbum()
    {
        var root = Path.Combine(Path.GetTempPath(), "PhotoLibrarian-not-an-album-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Throws<InvalidOperationException>(() => AlbumJpegConversionService.Preview(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Convert_WithoutRetainedBackups_RemovesOriginalButLeavesVerifiedJpeg()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "PhotoLibrarian-jpeg-no-backups-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".album"));
        try
        {
            var original = Path.Combine(root, "photo.png");
            await WicTestImage.CreateAsync(original, 8, 6);
            var result = await AlbumJpegConversionService.RunAsync(
                root, null, keepBackups: false);

            Assert.Equal(1, result.Converted);
            Assert.Equal(0, result.Failed);
            Assert.False(File.Exists(original));
            Assert.True(File.Exists(Path.Combine(root, "photo.jpg")));
            Assert.False(File.Exists(Path.Combine(result.BackupDirectory, "photo.png")));
            var rollback = Path.Combine(root, ".album", "jpeg-conversion-rollback");
            Assert.False(Directory.Exists(rollback) &&
                Directory.EnumerateFiles(rollback, "*", SearchOption.AllDirectories).Any());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
    [Fact]
    public async Task Convert_ReadOnlyPhoto_DoesNotMoveOriginalIntoNoBackupRollback()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "PL-jpeg-readonly-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".album"));
        var source = Path.Combine(root, "readonly.png");
        try
        {
            await WicTestImage.CreateAsync(source, 8, 6);
            var original = await File.ReadAllBytesAsync(source);
            File.SetAttributes(source, File.GetAttributes(source) | FileAttributes.ReadOnly);

            var result = await AlbumJpegConversionService.RunAsync(
                root, null, keepBackups: false);

            Assert.Equal(1, result.Failed);
            Assert.Equal(0, result.Converted);
            Assert.Contains("read-only", result.Errors.Single(),
                StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(source));
            Assert.False(File.Exists(Path.ChangeExtension(source, ".jpg")));
            Assert.Equal(original, await File.ReadAllBytesAsync(source));
        }
        finally
        {
            if (File.Exists(source))
                File.SetAttributes(source, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
    }

}
