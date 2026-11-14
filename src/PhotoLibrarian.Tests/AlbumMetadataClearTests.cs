using PhotoLibrarian.Core.Models;
using PhotoLibrarian.Core.Services;
using PhotoLibrarian.Services;
using Xunit;

namespace PhotoLibrarian.Tests;

public sealed class AlbumMetadataClearTests
{
    [Fact]
    public async Task Reset_ClearsKeywordsAndFlag_WithoutRemovingCaptionOrSource()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "PL-reset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".album"));
        try
        {
            var photo = Path.Combine(root, "photo.jpg");
            await WicTestImage.CreateAsync(photo, 12, 9, jpeg: true);
            await EmbeddedMetadataWriter.WriteAsync(photo,
                keywords: ["Group/Category", "Another"],
                flagged: true, rating: 4, title: "Keep my caption");

            int cleared = 0;
            var result = await AlbumMetadataClearService.RunAsync(root,
                [new ImageEntry { Id = 42, FilePath = photo, FileName = "photo.jpg" }],
                _ => { cleared++; return Task.CompletedTask; });

            Assert.Equal(1, result.Cleaned);
            Assert.Equal(0, result.Failed);
            Assert.Equal(1, cleared);
            Assert.Empty(await EmbeddedMetadataWriter.ReadKeywordsAsync(photo));
            Assert.True(File.Exists(photo));
            Assert.True(File.Exists(Path.Combine(result.BackupDirectory, "photo.jpg")));
            var dimensions = await WicTestImage.ReadSizeAsync(photo);
            Assert.Equal((12u, 9u), dimensions);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Reset_RemovesSidecarTagsAndFlagButRetainsOtherXmp()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "PL-reset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".album"));
        try
        {
            var photo = Path.Combine(root, "image.jpg");
            await WicTestImage.CreateAsync(photo, 6, 6, jpeg: true);
            var sidecar = Path.ChangeExtension(photo, ".xmp");
            var xml = "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">" +
                "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">" +
                "<rdf:Description rdf:about=\"\" " +
                "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" " +
                "xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\" " +
                "xmlns:plib=\"http://ns.photolibrarian.app/1.0/\" " +
                "xmp:Rating=\"4\" plib:Flagged=\"True\">" +
                "<dc:subject><rdf:Bag><rdf:li>Fashion/Shoes</rdf:li>" +
                "</rdf:Bag></dc:subject></rdf:Description></rdf:RDF></x:xmpmeta>";
            await File.WriteAllTextAsync(sidecar, xml);
            var result = await AlbumMetadataClearService.RunAsync(root,
                [new ImageEntry { FilePath = photo, FileName = "image.jpg" }],
                _ => Task.CompletedTask);
            Assert.Equal(1, result.Cleaned);
            Assert.Equal(0, result.Failed);
            var resultXml = await File.ReadAllTextAsync(sidecar);
            var xmp = XmpCore.XmpMetaFactory.ParseFromString(resultXml);
            Assert.False(xmp.DoesPropertyExist(
                "http://purl.org/dc/elements/1.1/", "dc:subject"));
            Assert.False(xmp.DoesPropertyExist(
                "http://ns.photolibrarian.app/1.0/", "plib:Flagged"));
            Assert.Equal("4", xmp.GetPropertyString(
                "http://ns.adobe.com/xap/1.0/", "xmp:Rating"));
            Assert.True(File.Exists(Path.Combine(result.BackupDirectory, "image.xmp")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Reset_LeavesUnknownOrOutOfAlbumFilesUntouched()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "PL-reset-" + Guid.NewGuid().ToString("N"));
        var other = Path.Combine(Path.GetTempPath(),
            "PL-other-" + Guid.NewGuid().ToString("N") + ".jpg");
        Directory.CreateDirectory(Path.Combine(root, ".album"));
        try
        {
            await WicTestImage.CreateAsync(other, 4, 4, jpeg: true);
            int cleared = 0;
            var result = await AlbumMetadataClearService.RunAsync(root,
                [new ImageEntry { Id = 1, FilePath = other, FileName = "outside.jpg" }],
                _ => { cleared++; return Task.CompletedTask; });
            Assert.Equal(0, result.Cleaned);
            Assert.Equal(1, result.Failed);
            Assert.Equal(0, cleared);
            Assert.True(File.Exists(other));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            File.Delete(other);
        }
    }
}
