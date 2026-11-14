using Xunit;

namespace PhotoLibrarian.Tests;

/// <summary>Guard consistent album maintenance UI and bounded conversion overhead.</summary>
public sealed class AlbumMaintenancePresentationTests
{
    private static string Source(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", name));

    [Fact]
    public void BothDestructiveAlbumActionsUseTheSharedDarkPhotoLibrarianDialog()
    {
        var settings = Source("SettingsPanel.xaml.cs");
        Assert.Contains("CreateAlbumMaintenanceDialog(", settings);
        Assert.Contains("RequestedTheme = ElementTheme.Dark", settings);
        Assert.Contains("HiTechPrimaryButtonStyle", settings);
        Assert.Contains("HiTechIconButtonStyle", settings);
        Assert.Contains("\"CONVERT TO JPEG\"", settings);
        Assert.Contains("\"CLEAR ALBUM TAGS + FLAGS\"", settings);
        Assert.DoesNotContain("Title = \"ALBUM // CONVERT TO JPEG\"", settings);
        Assert.DoesNotContain("Title = \"WARNING // CLEAR ALBUM TAGS + FLAGS\"", settings);
    }

    [Fact]
    public void ConversionUsesScaledValidationAndThrottlesUiProgress()
    {
        var conversion = Source("AlbumJpegConversionService.cs");
        Assert.Contains("ScaledWidth = 1, ScaledHeight = 1", conversion);
        Assert.Contains("overall.ElapsedMilliseconds - lastProgressMs >= 150", conversion);
        Assert.Contains("XmpMetaFactory.ParseFromString(", conversion);
        Assert.Contains("File.ReadAllBytesAsync(sidecar, cancellationToken)", conversion);
        Assert.Contains("ContainsExactPacket(updated, xmpBytes)", conversion);
        Assert.DoesNotContain("await decoder.GetPixelDataAsync();", conversion);
    }
}
