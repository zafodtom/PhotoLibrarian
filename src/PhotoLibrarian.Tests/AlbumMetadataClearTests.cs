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
