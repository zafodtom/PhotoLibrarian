using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace PhotoLibrarian.Tests;

/// <summary>
/// Offline XAML smoke checks for the Cinematic UI. These do not replace
/// interactive WinUI checks of pointer, focus or fullscreen behavior.
/// </summary>
public sealed class CinematicUiConsistencyTests
{
    private static readonly string[] Screens =
    [
        "MainWindow.xaml",
        "BootWindow.xaml",
        "FolderNavigationPanel.xaml",
        "ImageGridView.xaml",
        "ImageViewerOverlay.xaml",
        "ImageEditorView.xaml",
        "ManageFoldersDialog.xaml",
        "PeopleReviewView.xaml",
        "MetadataPanel.xaml",
        "SettingsPanel.xaml",
        "TopRibbon.xaml",
        "CropOverlay.xaml"
    ];

    private static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", name));

    [Fact]
    public void AllCinematicScreensRemainWellFormedXaml()
    {
        _ = XDocument.Parse(Read("HiTechTheme.xaml"));
        foreach (var screen in Screens)
        {
            var document = XDocument.Parse(Read(screen));
            Assert.NotNull(document.Root);
        }
    }

    [Fact]
    public void EveryUsedHiTechStaticResourceExistsInSharedTheme()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var theme = XDocument.Parse(Read("HiTechTheme.xaml"));
        var keys = theme.Descendants()
            .Select(node => (string?)node.Attribute(x + "Key"))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var screen in Screens)
        {
            var references = Regex.Matches(Read(screen), @"\{StaticResource (HiTech\w+)\}");
            foreach (Match match in references)
            {
                var key = match.Groups[1].Value;
                Assert.True(keys.Contains(key),
                    $"Missing {key} referenced by {screen}.");
            }
        }
    }

    [Fact]
    public void CinematicWarningsUseNativeCenteredPlacementAndAvoidDefaultAccentButtons()
    {
        var source = Read("CinematicDialogShowcase.cs");
        // Keep fixed dimensions on the INNER CONTENT, not on the dialog:
        // WinUI's popup presenter otherwise places the dialog at the edge.
        var dialogSection = source.Split("var dialog = new ContentDialog", 2)[1]
            .Split("return dialog;", 2)[0];
        Assert.DoesNotContain("Width = dialogWidth,", dialogSection);
        Assert.DoesNotContain("MaxWidth = dialogWidth", dialogSection);
        Assert.DoesNotContain("HorizontalAlignment = HorizontalAlignment.Center,", dialogSection);
        Assert.DoesNotContain("VerticalAlignment = VerticalAlignment.Center,", dialogSection);
        Assert.Contains("ContentDialogMaxWidth", dialogSection);
        Assert.Contains("DefaultButton = ContentDialogButton.None", dialogSection);
        Assert.Contains("dialog.PrimaryButtonStyle = ActionStyle(", dialogSection);
        Assert.Contains("dialog.CloseButtonStyle = ActionStyle(", dialogSection);
    }

    [Fact]
    public void OrdinaryDialogsLeavePopupPlacementToWinUi()
    {
        var source = Read("CinematicDialogStyling.cs");
        var theme = Read("HiTechTheme.xaml");
        Assert.Contains("dialog.ClearValue(FrameworkElement.HorizontalAlignmentProperty)", source);
        Assert.Contains("dialog.ClearValue(FrameworkElement.VerticalAlignmentProperty)", source);
        Assert.DoesNotContain("dialog.HorizontalAlignment = HorizontalAlignment.Center", source);
        Assert.Contains("HiTechSecondaryButtonStyle", source);
        Assert.Contains("HiTechDangerButtonStyle", source);
        Assert.Contains("x:Key=\"HiTechDangerButtonStyle\"", theme);

        var folders = XDocument.Parse(Read("ManageFoldersDialog.xaml"));
        Assert.Null(folders.Root?.Attribute("MinWidth"));
        Assert.Null(folders.Root?.Attribute("HorizontalAlignment"));
        Assert.Null(folders.Root?.Attribute("VerticalAlignment"));
    }

    [Fact]
    public void SecondaryActionsAndDisabledButtonsUseSharedCinematicPalette()
    {
        var theme = Read("HiTechTheme.xaml");
        var settings = Read("SettingsPanel.xaml");
        var metadata = Read("MetadataPanel.xaml.cs");
        var navigation = Read("FolderNavigationPanel.xaml.cs");

        Assert.Contains("x:Key=\"HiTechSecondaryButtonStyle\"", theme);
        Assert.Contains("x:Key=\"ButtonBackgroundDisabled\"", theme);
        Assert.Contains("x:Key=\"ButtonForegroundDisabled\"", theme);
        Assert.Contains("x:Key=\"ButtonBorderBrushDisabled\"", theme);
        Assert.Contains("HiTechSecondaryButtonStyle", settings);
        Assert.Contains("HiTechSecondaryButtonStyle", metadata);
        Assert.Contains("HiTechSecondaryButtonStyle", navigation);
    }

    [Fact]
    public void ManageCatalogCancelUsesTheSharedNeutralActionStyle()
    {
        var source = Read("MetadataPanel.xaml.cs");
        var actions = source.Split("var saveButton = new Button { Content = \"SAVE CHANGES\" };", 2)[1]
            .Split("actionRail.Children.Add(saveButton)", 2)[0];

        Assert.Contains("HiTechSecondaryButtonStyle", actions);
        Assert.DoesNotContain("cancelButton.Style = (Style)Application.Current.Resources[\"HiTechIconButtonStyle\"]", actions);
        Assert.Contains("MinWidth = 100", actions);
    }

    [Fact]
    public void AllProgressIndicatorsReuseTheBootScreenStrip()
    {
        var shared = Read("CinematicProgressBar.xaml");
        var logic = Read("CinematicProgressBar.xaml.cs");
        Assert.Contains("Height=\"12\"", shared);
        Assert.Contains("BorderBrush=\"#2C5061\"", shared);
        Assert.Contains("Background=\"#0B151F\"", shared);
        Assert.Contains("Height=\"8\"", shared);
        Assert.Contains("Margin=\"2\"", shared);
        Assert.Contains("Foreground=\"#65D5E5\"", shared);
        Assert.Contains("nameof(IsIndeterminate)", logic);
        Assert.Contains("nameof(IsActive)", logic);
        Assert.Contains("InnerProgress.IsIndeterminate = IsIndeterminate;", logic);

        foreach (var name in new[]
        {
            "BootWindow.xaml", "MainWindow.xaml", "SettingsPanel.xaml",
            "ImageGridView.xaml", "PeopleReviewView.xaml"
        })
        {
            var screen = Read(name);
            Assert.Contains("CinematicProgressBar", screen);
            Assert.DoesNotContain("<ProgressRing", screen);
            Assert.DoesNotContain("<ProgressBar ", screen);
        }

        var tiles = Read("VirtualizingPhotoGrid.xaml.cs");
        Assert.Contains("new CinematicProgressBar", tiles);
        Assert.Contains("CinematicProgressBar.IsActiveProperty", tiles);
        Assert.DoesNotContain("new ProgressRing", tiles);
    }

    [Fact]
    public void ViewerControlsKeepActualSizeSeparateFromActualZoomReadout()
    {
        var window = Read("MainWindow.xaml");
        var viewer = Read("ImageViewerOverlay.xaml");

        Assert.Contains("SideZoom100Button", window);
        Assert.Contains("FullscreenZoom100Button", window);
        Assert.Contains("ZoomPercentBadge", viewer);
        Assert.Contains("ZoomPercentText", viewer);
    }
}
