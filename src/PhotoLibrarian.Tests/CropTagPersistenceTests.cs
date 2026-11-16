using PhotoLibrarian.Core.Services;
using XmpCore;
using XmpCore.Options;
using Xunit;

namespace PhotoLibrarian.Tests;

/// <summary>Portable keyword regression tests that run without a photo decoder.</summary>
public sealed class CropTagPersistenceTests
{
    private const string DcNamespace = "http://purl.org/dc/elements/1.1/";

    [Fact]
    public void SidecarOnlyKeywordsCanBeReimportedAfterCropExport()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"photolibrarian-crop-tags-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var filePath = Path.Combine(directory, "new-crop.bmp");
            File.WriteAllBytes(filePath, [0, 1, 2, 3]);

            var xmp = XmpMetaFactory.Create();
            xmp.AppendArrayItem(DcNamespace, "dc:subject",
                new PropertyOptions { IsArray = true }, "Styl/svatební", null);
            xmp.AppendArrayItem(DcNamespace, "dc:subject",
                new PropertyOptions { IsArray = true }, "Oblečení/šaty", null);
            File.WriteAllText(Path.ChangeExtension(filePath, ".xmp"),
                XmpMetaFactory.SerializeToString(xmp, new SerializeOptions()));

            var reader = new MetadataReaderService();
            // The same RAW/BMP sidecar must be visible to album indexing,
            // but must NOT be mistaken for embedded photo metadata by reset.
            Assert.Empty(reader.ReadEmbeddedTags(filePath));
            var imported = reader.ReadTags(filePath);
            Assert.Contains("Styl/svatební", imported);
            Assert.Contains("Oblečení/šaty", imported);
            Assert.Equal(2, imported.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CropStagesMetadataBeforeReplacingImageAndRollsBackOnFailure()
    {
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "TestAssets", "CropService.cs"));
        Assert.Contains("TagWriterService.WriteTagsToSidecarAsync(temporaryImagePath, preservedTags)", source);
        Assert.Contains("TagWriterService.WriteTagsToSidecarAsync(temp, copiedTags)", source);
        Assert.Contains("CommitOutputs(destination, temp, sidecars)", source);
        Assert.Contains("CommitOutputs(filePath, temporaryImagePath, sidecars)", source);
        Assert.Contains("installed.AsEnumerable().Reverse()", source);
        Assert.Contains("File.Exists(destination + \".xmp\")", source);
    }
}
