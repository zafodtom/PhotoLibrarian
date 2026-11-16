using Xunit;

namespace PhotoLibrarian.Tests;

/// <summary>Guard consistent album maintenance UI and bounded conversion overhead.</summary>
public sealed class AlbumMaintenancePresentationTests
{
    private static string Source(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", name));

    [Fact]
    public void AlbumDestructiveOperationsUseRealCinematicWarningAndConfirmationGate()
    {
        var settings = Source("SettingsPanel.xaml.cs");
        Assert.Contains("CinematicDialogShowcase.Create(", settings);
        Assert.Contains("CinematicDialogShowcase.Variant.Warning", settings);
        Assert.Contains("confirmLabel: \"DELETE ALBUM\"", settings);
        Assert.Contains("confirmLabel: \"CONVERT TO JPEG\"", settings);
        Assert.Contains("confirmLabel: \"CLEAR ALBUM TAGS + FLAGS\"", settings);
        Assert.Contains("if (await dialog.ShowAsync() != ContentDialogResult.Primary)", settings);
        Assert.Contains("DeleteCurrentAlbumAsync();", settings);
    }

    [Fact]
    public void ConversionUsesScaledValidationAndThrottlesUiProgress()
    {
        var conversion = Source("AlbumJpegConversionService.cs");
        Assert.Contains("ScaledWidth = 1, ScaledHeight = 1", conversion);
        Assert.Contains("overall.ElapsedMilliseconds - lastProgressMs >= 150", conversion);
        Assert.Contains("System.Xml.XmlReader.Create(", conversion);
        Assert.Contains("DtdProcessing = System.Xml.DtdProcessing.Prohibit", conversion);
        Assert.Contains("XmlResolver = null", conversion);
        Assert.Contains("File.ReadAllBytesAsync(sidecar, cancellationToken)", conversion);
        Assert.Contains("ContainsExactPacket(updated, xmpBytes)", conversion);
        Assert.DoesNotContain("await decoder.GetPixelDataAsync();", conversion);
    }
}
