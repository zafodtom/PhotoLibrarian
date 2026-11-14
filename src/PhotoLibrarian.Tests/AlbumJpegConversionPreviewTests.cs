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
            Directory.CreateDirectory(Path.Combine(root, "different-album", ".album"));
            File.WriteAllBytes(Path.Combine(root, "different-album", "foreign.png"), [1]);

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
}
