using Xunit;

namespace PhotoLibrarian.Tests;

/// <summary>
/// Prevents the dialog taxonomy from reverting to ad-hoc WinUI dialogs.
/// These checks inspect call sites; live modal placement needs Windows testing.
/// </summary>
public sealed class CinematicDialogRoutingTests
{
    private static string Source(string file) => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "TestAssets", file));

    [Fact]
    public void GalleryUsesWarningForBulkTagClearingAndErrorForFailedRename()
    {
        var source = Source("ImageGridView.xaml.cs");
        Assert.Contains("subjectOverride: \"CLEAR SELECTED PHOTO TAGS?\"", source);
        Assert.Contains("subjectOverride: \"PHOTO RENAME FAILED\"", source);
        Assert.Contains("CinematicDialogStyling.Apply(dialog);", source);
        Assert.DoesNotContain("var hudRed =", source);
    }

    [Fact]
    public void DatabaseMaintenanceHasWarningCompletionAndError()
    {
        var source = Source("ManageFoldersDialog.xaml.cs");
        Assert.Contains("Variant.Warning", source);
        Assert.Contains("Variant.Complete", source);
        Assert.Contains("Variant.Error", source);
        Assert.Contains("confirmLabel: \"CLEAR AND RE-INDEX\"", source);
    }

    [Fact]
    public void PeopleAndAlbumFailuresUseCorrectSeverities()
    {
        var people = Source("PeopleReviewView.xaml.cs");
        var album = Source("MainWindow.xaml.cs");
        var folders = Source("FolderNavigationPanel.xaml.cs");
        Assert.Contains("Variant.Warning", people);
        Assert.Contains("Variant.Error", people);
        Assert.Contains("Variant.Fatal", album);
        Assert.Contains("Variant.Error", folders);
    }

    [Fact]
    public void InformationalAlertsHaveAcknowledgementNotCancel()
    {
        var factory = Source("CinematicDialogShowcase.cs");
        Assert.Contains("string.IsNullOrWhiteSpace(confirmLabel)", factory);
        Assert.Contains("ACKNOWLEDGE", factory);
        Assert.Contains("PrimaryButtonText = preview ? \"NEXT PREVIEW\" : confirmLabel", factory);
    }
}
