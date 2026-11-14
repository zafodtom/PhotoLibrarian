using Xunit;

namespace PhotoLibrarian.Tests;

/// <summary>
/// Source-level checks for the intentional tag-first PhotoLibrarian UI.
/// These assertions do not require booting the WinUI shell.
/// </summary>
public sealed class TagFirstCleanupTests
{
    private static string Source(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", name));

    [Fact]
    public void DefaultSortStartsAtFilenameAndIgnoresEarlySelectionEvents()
    {
        var view = Source("ImageGridView.xaml");
        var code = Source("ImageGridView.xaml.cs");
        var grid = Source("ImageGridViewModel.cs");
        var combo = view[view.IndexOf("<ComboBox x:Name=\"SortByCombo\"", StringComparison.Ordinal)..];
        Assert.Contains("SelectedIndex=\"0\"", combo[..combo.IndexOf('>')]);
        Assert.Contains("SortByOption.FileName => 0", code);
        Assert.Contains("_sortBy = SortByOption.FileName;", grid);
        Assert.Contains("_sortDescending = false;", grid);
        Assert.Contains("if (!_isInitialized || ViewModel is null", code);
    }

    [Fact]
    public void OldHiddenFiltersAndNavigationAreRemoved()
    {
        var gallery = Source("ImageGridView.xaml");
        var navigation = Source("FolderNavigationPanel.xaml");
        var ribbon = Source("TopRibbon.xaml");
        foreach (var retired in new[]
        {
            "RatingCombo", "RatingModeCombo", "PersonCombo",
            "FlagCombo", "DatePresetCombo", "DateFromPicker",
            "DateToPicker", "MissingMetadataCombo"
        })
            Assert.DoesNotContain(retired, gallery);
        Assert.DoesNotContain("PeopleTree", navigation);
        Assert.DoesNotContain("DateTree", navigation);
        Assert.DoesNotContain("FlagsTree", navigation);
        foreach (var retired in new[]
        {
            "MakeCopyButton", "StraightenButton", "RedEyeButton"
        })
            Assert.DoesNotContain(retired, ribbon);
        Assert.Contains("IncludeTagsBox", gallery);
        Assert.Contains("ExcludeTagsBox", gallery);
        Assert.Contains("TagsTree", navigation);
        Assert.Contains("AdjustButton", ribbon);
    }

    [Fact]
    public void RecognitionPipelineKeepsAutoTagsButNeverRequestsFaces()
    {
        var main = Source("MainViewModel.cs");
        Assert.Contains("new RecognitionRequest(\n                false,", main);
        Assert.Contains("_autoTaggingEnabled ? Settings.CurrentAutoTaggingSettings : null", main);
        Assert.Contains("public void StartBackgroundFaceDetection() { }", main);
    }
}
