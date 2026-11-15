using Xunit;

namespace PhotoLibrarian.Tests;

/// <summary>
/// Source-wiring checks for tag edits that would remove the selected photo
/// from an untagged or other tag-filtered gallery.
/// </summary>
public sealed class TagPickerFilterDeferralTests
{
    private static string Read(string name) => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "TestAssets", name));

    [Fact]
    public void CompletePickerNotifiesGridOfItsLifetime()
    {
        var metadata = Read("MetadataPanel.xaml.cs");
        Assert.Contains("SetCompleteTagPickerOpen(true);", metadata);
        Assert.Contains("SetCompleteTagPickerOpen(false);", metadata);
        Assert.Contains("private void HideTagSuggestions()", metadata);
    }

    [Fact]
    public void FilterResultsCannotRefreshWhileCompletePickerIsOpen()
    {
        var main = Read("MainViewModel.cs");
        Assert.Contains("public void SetCompleteTagPickerOpen(bool open)", main);
        Assert.Contains("_isCompleteTagPickerOpen = open;", main);
        Assert.Contains("ImageViewer.IsOpen || _isCompleteTagPickerOpen ||", main);
        Assert.Contains("!_isCompleteTagPickerOpen && _pendingViewerTagFilterRefresh", main);
        Assert.Contains("await ImageGrid.LoadImagesAsync(scanDiskForMissingFiles: false);", main);
    }

    [Fact]
    public void DismissalProcessesPendingRefreshOnlyOutsidePhotoViewer()
    {
        var main = Read("MainViewModel.cs");
        Assert.Contains("if (!open && !ImageViewer.IsOpen &&", main);
        Assert.Contains("_pendingViewerTagFilterRefresh && !_deferredViewerTagRefreshRunning)", main);
        Assert.Contains("_ = RefreshDeferredViewerTagFilterAsync();", main);
    }

    [Fact]
    public void SettingsOffersPreviewOnlyDialogSequence()
    {
        var xaml = Read("SettingsPanel.xaml");
        var code = Read("SettingsPanel.xaml.cs");
        Assert.Contains("PreviewAllDialogsButton", xaml);
        Assert.Contains("OnPreviewAllDialogsClick", xaml);
        Assert.Contains("private static readonly DialogPreview[] DialogPreviews", code);
        Assert.Contains("if (await dialog.ShowAsync() != ContentDialogResult.Primary)", code);
        Assert.DoesNotContain("DeleteCurrentAlbumAsync", code[
            code.IndexOf("private async void OnPreviewAllDialogsClick(", StringComparison.Ordinal)..
            code.IndexOf("private async void OnClose(", StringComparison.Ordinal)]);
    }
}
