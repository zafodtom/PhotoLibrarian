using Xunit;

namespace PhotoLibrarian.Tests;

/// <summary>
/// Guards against rebuilding the complete tag picker when assigning tags.
/// In WinUI, replacing ListView.ItemsSource can reset its scroll and focus.
/// </summary>
public sealed class TagPickerScrollRegressionTests
{
    private static string Source() => File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "TestAssets", "MetadataPanel.xaml.cs"));

    [Fact]
    public void CompletePickerTagAssignmentDoesNotRebindListView()
    {
        var source = Source();
        var handler = source[
            source.IndexOf("private async void OnTagSuggestionItemClick(", StringComparison.Ordinal)..];
        handler = handler[..handler.IndexOf("// Shared dark HUD", StringComparison.Ordinal)];

        Assert.Contains("var keepOpen = _showingCompleteTagList;", handler);
        Assert.Contains("await AssignAndRememberTagAsync(chosen.Tag);", handler);
        Assert.Contains("if (!keepOpen)", handler);
        Assert.Contains("HideTagSuggestions();", handler);
        Assert.DoesNotContain("ShowTagSuggestions(", handler);
        Assert.DoesNotContain("TagSuggestionsList.ItemsSource =", handler);
    }

    [Fact]
    public void CompletePickerPinToggleDoesNotRebindListView()
    {
        var source = Source();
        var handler = source[
            source.IndexOf("private async void OnToggleAlbumTagSelectionClick(", StringComparison.Ordinal)..];
        handler = handler[..handler.IndexOf("private void OnNewTagKeyDown(", StringComparison.Ordinal)];

        Assert.Contains("await model.ToggleAlbumTagSelectionAsync(item);", handler);
        Assert.DoesNotContain("ShowTagSuggestions(", handler);
    }

    [Fact]
    public void SearchAndEscapeStillDismissPickerAndSearchIgnoresDiacritics()
    {
        var source = Source();
        Assert.Contains("TagSearchMatcher.Contains(item.Tag, query)", source);
        Assert.Contains("if (e.Key == Windows.System.VirtualKey.Escape)", source);
        Assert.Contains("NewTagBox.Text = \"\";", source);
        Assert.Contains("TagSuggestionsPanel.Visibility = Visibility.Collapsed;", source);
    }
}
