using Xunit;

namespace PhotoLibrarian.Tests;

/// <summary>
/// Source-wiring guard for tag edits made inside the open image viewer.
/// Full WinUI interaction and selection behavior must also be checked manually.
/// </summary>
public sealed class ViewerTagFilteringRegressionTests
{
    private static string Read(string file) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", file));

    [Fact]
    public void TagDependentFiltersIncludeUntaggedAndTreeIncludesExcludes()
    {
        var grid = Read("ImageGridViewModel.cs");
        Assert.Contains("public bool HasTagDependentFilter =>", grid);
        Assert.Contains("Refinement.RequiresTags", grid);
        Assert.Contains("_currentTagRootSelected", grid);
        Assert.Contains("_currentUntaggedSelected", grid);
        Assert.Contains("_currentTagFilters is { Count: > 0 }", grid);
        Assert.Contains("_currentExcludedTagFilters is { Count: > 0 }", grid);
    }

    [Fact]
    public void ViewerDefersFilterRebuildAndRestoresSelectionBeforeRefresh()
    {
        var main = Read("MainViewModel.cs");
        var viewer = Read("ImageViewerViewModel.cs");

        Assert.Contains("ImageViewer.ClosedAtEntry += ImageGrid.SelectLastViewedImage;", main);
        Assert.Contains("ClosedAtEntry?.Invoke(lastDisplayedEntry);", viewer);
        Assert.Contains("if (ImageViewer.IsOpen || _deferredViewerTagRefreshRunning)", main);
        Assert.Contains("_pendingViewerTagFilterRefresh = true;", main);
        Assert.Contains("while (!_isShuttingDown && !ImageViewer.IsOpen &&", main);
        Assert.Contains("await ImageGrid.LoadImagesAsync(scanDiskForMissingFiles: false);", main);

        var refreshMethod = main[
            main.IndexOf("public async Task RefreshTagsTreeAsync()", StringComparison.Ordinal)..];
        refreshMethod = refreshMethod[..refreshMethod.IndexOf("public async Task RefreshAfterDateChangeAsync()", StringComparison.Ordinal)];
        Assert.DoesNotContain("if (ImageGrid.Refinement.RequiresTags)", refreshMethod);
    }

    [Fact]
    public void ViewerNavigationSnapshotIsNotRebuiltByTagChanges()
    {
        var main = Read("MainViewModel.cs");
        var viewer = Read("ImageViewerViewModel.cs");
        var deferred = main[
            main.IndexOf("private async Task RefreshDeferredViewerTagFilterAsync()", StringComparison.Ordinal)..];
        deferred = deferred[..deferred.IndexOf("public async Task InitializeAsync()", StringComparison.Ordinal)];

        Assert.DoesNotContain("ImageViewer.UpdateLibraryImages", deferred);
        Assert.Contains("if (ImageViewer.IsOpen)", viewer);
        Assert.Contains("RaiseCurrentEntryChanged();", viewer);
    }
}
