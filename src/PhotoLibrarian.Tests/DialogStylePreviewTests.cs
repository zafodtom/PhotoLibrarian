using Xunit;

namespace PhotoLibrarian.Tests;

/// <summary>
/// Keeps the non-destructive dialog style preview independent of the
/// retired gallery/tag-picker scroll workarounds.
/// </summary>
public sealed class DialogStylePreviewTests
{
    private static string Read(string name) => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "TestAssets", name));

    [Fact]
    public void SettingsExposeVisualOnlyDialogPreview()
    {
        var xaml = Read("SettingsPanel.xaml");
        var source = Read("SettingsPanel.xaml.cs");

        Assert.Contains("PreviewAllDialogsButton", xaml);
        Assert.Contains("OnPreviewAllDialogsClick", xaml);
        Assert.Contains("private static readonly DialogPreview[] DialogPreviews", source);
        Assert.Contains("CreateStylePreviewDialog", source);
        Assert.Contains("if (await dialog.ShowAsync() != ContentDialogResult.Primary)", source);
    }

    [Fact]
    public void PreviewHandlersNeverInvokeProductionOperations()
    {
        var source = Read("SettingsPanel.xaml.cs");
        var start = source.IndexOf("private async void OnPreviewAllDialogsClick(", StringComparison.Ordinal);
        var end = source.IndexOf("private async void OnClose(", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var preview = source[start..end];

        Assert.DoesNotContain("DeleteCurrentAlbumAsync(", preview);
        Assert.DoesNotContain("ConvertAlbumAsync(", preview);
        Assert.DoesNotContain("ClearAlbumMetadataAsync(", preview);
        Assert.DoesNotContain("ExecuteAsync(", preview);
        Assert.DoesNotContain("Process.Start(", preview);
    }
}
