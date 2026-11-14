using Xunit;

namespace PhotoLibrarian.Tests;

/// <summary>
/// Regression coverage for the two WinUI drag paths and nonblocking transfers.
/// These checks intentionally do not require a live XAML window.
/// </summary>
public sealed class PhotoDragDropStabilityTests
{
    private static string Source(string name) => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "TestAssets", name));

    [Fact]
    public void BothGalleryLayoutsSupplyDragDataAndAlwaysCompleteDeferral()
    {
        var view = Source("ImageGridView.xaml.cs");
        var grid = Source("VirtualizingPhotoGrid.xaml.cs");
        Assert.Contains("PhotoGrid.FlatDragStarting += OnFlatPhotoDragStarting;", view);
        Assert.Contains("await Services.PhotoOperationsService.PopulateDragDataAsync(drag.Data, paths);", view);
        Assert.Contains("deferral.Complete();", view);
        Assert.Contains("drag.Cancel = true;", view);
        Assert.Contains("grid.DragStarting += OnPhotoDragStarting;", grid);
        Assert.Contains("await PhotoLibrarian.Services.PhotoOperationsService.PopulateDragDataAsync(", grid);
        Assert.Contains("args.Cancel = true;", grid);
        Assert.Contains("deferral.Complete();", grid);
    }

    [Fact]
    public void FileTransfersAreSerializedAndRunAwayFromUiThread()
    {
        var operations = Source("PhotoOperationsService.cs");
        Assert.Contains("_transferGate.WaitAsync(0)", operations);
        Assert.Contains("Task.Run(() => MoveDirectory(sourcePath, destination))", operations);
        Assert.Contains("Task.Run(() => CopyDirectory(sourcePath, destination))", operations);
        Assert.Contains("Task.Run(() => MoveFile(sourcePath, destination))", operations);
        Assert.Contains("Task.Run(() =>", operations);
        Assert.Contains("if (move && allSucceeded && sourcePaths.Length > 0", operations);
        Assert.Contains("_transferGate.Release();", operations);
    }
}
