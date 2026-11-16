using Xunit;

namespace PhotoLibrarian.Tests;

/// <summary>
/// Guards against reintroducing temporary dialog-preview tools in Settings,
/// without interfering with the production cinematic dialog factory.
/// </summary>
public sealed class DialogStylePreviewTests
{
    private static string Read(string name) => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "TestAssets", name));

    [Fact]
    public void SettingsHaveNoDialogPreviewOrUiShowcase()
    {
        var xaml = Read("SettingsPanel.xaml");
        var source = Read("SettingsPanel.xaml.cs");

        foreach (var obsolete in new[]
        {
            "PreviewAllDialogsButton", "OnPreviewAllDialogsClick",
            "UiShowcaseSectionHeader", "OnNavigateUiShowcase",
            "CinematicShowcaseSequenceButton", "OnShowcaseFatal",
            "OnShowcaseWarning", "OnShowcaseSequence",
            "DialogPreviews", "CreateStylePreviewDialog",
            "ShowCinematicSpecimensAsync"
        })
        {
            Assert.DoesNotContain(obsolete, xaml);
            Assert.DoesNotContain(obsolete, source);
        }
    }

    [Fact]
    public void ProductionCinematicWarningsRemainAvailableAfterRemovingPreviews()
    {
        var settings = Read("SettingsPanel.xaml.cs");
        var dialogFactory = Read("CinematicDialogShowcase.cs");

        Assert.Contains("CinematicDialogShowcase.Create(", settings);
        Assert.Contains("CinematicDialogShowcase.Variant.Warning", settings);
        Assert.Contains("confirmLabel: \"DELETE ALBUM\"", settings);
        Assert.Contains("if (await dialog.ShowAsync() != ContentDialogResult.Primary)", settings);
        Assert.Contains("internal static ContentDialog Create(", dialogFactory);
    }
}
