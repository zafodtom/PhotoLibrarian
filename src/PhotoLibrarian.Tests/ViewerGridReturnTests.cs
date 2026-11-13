using Xunit;

namespace PhotoLibrarian.Tests;

/// <summary>
/// Source-wiring regression checks for the WinUI viewer-to-grid handoff and
/// the initial sorting state; these do not require launching a window.
/// </summary>
public sealed class ViewerGridReturnTests
{
    private static string ReadSource(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", name));

    [Fact]
    public void DeletingInViewerKeepsCurrentPhotoMetadataAndFolderTagScope()
    {
        var main = ReadSource("MainViewModel.cs");
        var grid = ReadSource("ImageGridViewModel.cs");

        Assert.Contains("if (!_main.ImageViewer.IsOpen)", grid);
        Assert.Contains("if (ImageViewer.IsOpen && ImageViewer.CurrentEntry is { } shown)", main);
        Assert.Contains("MetadataPanel.ShowMetadata(shown);", main);
        Assert.Contains("await window.RefreshMetadataTreesAsync();", main);
        Assert.Contains("await TagNav.LoadTagsAsync();", main);
        // A blanket reload of the tag tree before the scoped window refresh
        // would show tags from other folders and race against the filtered tree.
        var deleteMethod = main[
            main.IndexOf("public async Task<IReadOnlyList<string>> DeleteEntriesAsync(", StringComparison.Ordinal)..];
        deleteMethod = deleteMethod[..deleteMethod.IndexOf("public async Task RefreshKnownLibraryStateAsync(", StringComparison.Ordinal)];
        Assert.DoesNotContain("await TagNav.LoadTagsAsync();\n        await DateNav.LoadDatesAsync();", deleteMethod);
        Assert.DoesNotContain("await MetadataPanel.ReloadTagsAsync();", deleteMethod);
    }

    [Fact]
    public void StartupSortsByFileNameAscending()
    {
        var source = ReadSource("ImageGridViewModel.cs");
        Assert.Contains("private string? _currentSortBy = \"file_name\";", source);
        Assert.Contains("private SortByOption _sortBy = SortByOption.FileName;", source);
        Assert.Contains("private bool _sortDescending = false;", source);
        Assert.Contains("SortField = \"Name\";", source);
    }

    [Fact]
    public void ViewerReturnsItsFinalEntryBeforeMetadataIsRestored()
    {
        var viewer = ReadSource("ImageViewerViewModel.cs");
        var grid = ReadSource("ImageGridViewModel.cs");
        var main = ReadSource("MainViewModel.cs");
        var view = ReadSource("ImageGridView.xaml.cs");
        var virtualization = ReadSource("VirtualizingPhotoGrid.xaml.cs");

        Assert.Contains("var lastDisplayedEntry = CurrentEntry;", viewer);
        Assert.Contains("ClosedAtEntry?.Invoke(lastDisplayedEntry);", viewer);
        Assert.Contains("ImageViewer.ClosedAtEntry += ImageGrid.SelectLastViewedImage;", main);
        Assert.Contains("UpdateSelection([match], match);", grid);
        Assert.Contains("ViewerReturnSelectionRequested?.Invoke(this, EventArgs.Empty);", grid);
        Assert.Contains("PhotoGrid.RestoreSelection(ViewModel.SelectedImages, ViewModel.SelectedImage);", view);
        Assert.Contains("PhotoGrid.ScrollPrimarySelectionIntoView();", view);
        Assert.Contains("public void ScrollPrimarySelectionIntoView()", virtualization);
    }
}
