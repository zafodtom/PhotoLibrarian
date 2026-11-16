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
    public void PrivacyCleanupResultIsNotMistakenForAConfirmation()
    {
        var dialog = Source("CinematicDialogShowcase.cs");
        var grid = Source("ImageGridView.xaml.cs");
        var settings = Source("SettingsPanel.xaml.cs");

        Assert.Contains("informationalWarning", dialog);
        Assert.Contains("SYSTEM WARNING // OPERATION RESULT", dialog);
        Assert.Contains("OPERATION RESULT  //  REVIEW THE DETAILS ABOVE", dialog);
        Assert.Contains("REVIEW THE OPERATION BEFORE CONFIRMING", dialog);
        Assert.Contains("string.IsNullOrWhiteSpace(confirmLabel)", dialog);
        Assert.Contains("No photos were changed; no backups were needed", grid);
        Assert.Contains("No files changed; no backups were needed", settings);
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
    public void LongRunningAlbumMaintenanceReportsCompletionOrErrors()
    {
        var source = Source("SettingsPanel.xaml.cs");
        Assert.Contains("JPEG CONVERSION COMPLETE", source);
        Assert.Contains("JPEG CONVERSION PARTIALLY FAILED", source);
        Assert.Contains("ALBUM RESET COMPLETE", source);
        Assert.Contains("ALBUM RESET PARTIALLY FAILED", source);
        Assert.Contains("JPEG CONVERSION FAILED", source);
        Assert.Contains("ALBUM RESET FAILED", source);
    }

    [Fact]
    public void ThumbnailCacheClearRequiresCinematicWarningBeforeExecution()
    {
        var source = Source("SettingsPanel.xaml.cs");
        var section = source.Split("private async void OnRebuildCache(", 2)[1]
            .Split("private async void OnDeleteAlbum(", 2)[0];

        Assert.Contains("CinematicDialogShowcase.Variant.Warning", section);
        Assert.Contains("subjectOverride: \"CLEAR THUMBNAIL CACHE?\"", section);
        Assert.Contains("confirmLabel: \"CLEAR THUMBNAILS\"", section);
        Assert.Contains("await dialog.ShowAsync() != ContentDialogResult.Primary", section);
        Assert.True(
            section.IndexOf("await dialog.ShowAsync()", StringComparison.Ordinal) <
            section.IndexOf("RebuildCacheCommand.ExecuteAsync(null)", StringComparison.Ordinal));
        Assert.Contains("Original photographs, assigned tags and the album catalog", section);
    }

    [Fact]
    public void UpdateUsesExistingCinematicModalInsteadOfLegacyOverlay()
    {
        var source = Source("SettingsPanel.xaml.cs");
        var xaml = Source("SettingsPanel.xaml");

        Assert.Contains("private async Task<bool> ConfirmUpdateAsync(", source);
        Assert.Contains("CinematicDialogShowcase.Create(", source);
        Assert.Contains("captionOverride: caption", source);
        Assert.Contains("operationLabel: \"UPDATE CONTROL\"", source);
        Assert.Contains("captionOverride: \"CACHE MAINTENANCE // CONFIRMATION\"", source);
        Assert.Contains("return await dialog.ShowAsync() == ContentDialogResult.Primary", source);
        Assert.Contains("CinematicDialogShowcase.Variant.Information", source);
        Assert.Contains("CinematicDialogShowcase.Variant.Warning", source);
        Assert.Contains("DOWNLOAD UPDATE", source);
        Assert.Contains("INSTALL & RESTART", source);
        Assert.Contains("_activeUpdateDialog?.Hide()", source);
        Assert.DoesNotContain("UpdateConfirmOverlay", xaml);
        Assert.DoesNotContain("ResolveUpdateDecision", source);
        Assert.DoesNotContain("TaskCompletionSource<bool>", source);
    }

    [Fact]
    public void SharedDialogAllowsOperationSpecificHeaderWithoutChangingExistingCallers()
    {
        var factory = Source("CinematicDialogShowcase.cs");
        Assert.Contains("string? captionOverride = null, string? operationLabel = null", factory);
        Assert.Contains("captionOverride ??", factory);
        Assert.Contains("informationalWarning ? \"SYSTEM WARNING // OPERATION RESULT\" : caption", factory);
        Assert.Contains(@"operationLabel ?? ""ALBUM OPERATION""", factory);
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
