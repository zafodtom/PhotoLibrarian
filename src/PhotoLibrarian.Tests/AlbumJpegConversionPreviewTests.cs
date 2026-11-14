using PhotoLibrarian.Services;
using Xunit;

namespace PhotoLibrarian.Tests;

public sealed class AlbumJpegConversionPreviewTests
{
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

            var preview = AlbumJpegConversionService.Preview(root);
            Assert.Equal(1, preview.ConvertCount);
            Assert.Equal(1, preview.JpegMetadataCount);
            Assert.Equal(1, preview.TargetConflictCount);
            Assert.Equal(1, preview.UnsupportedCount);
            Assert.Equal(2, preview.PendingCount);
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
}
