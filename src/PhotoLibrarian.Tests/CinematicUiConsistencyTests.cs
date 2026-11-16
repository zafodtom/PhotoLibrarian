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
    public void CinematicWarningsStayCenteredAndAvoidDefaultAccentButtons()
    {
        var source = Read("CinematicDialogShowcase.cs");
        Assert.Contains("HorizontalAlignment = HorizontalAlignment.Center", source);
        Assert.Contains("VerticalAlignment = VerticalAlignment.Center", source);
        Assert.Contains("DefaultButton = ContentDialogButton.None", source);
        Assert.Contains("dialog.PrimaryButtonStyle = ActionStyle(", source);
        Assert.Contains("dialog.CloseButtonStyle = ActionStyle(", source);
    }

    [Fact]
    public void OrdinaryDialogsShareCenteredNeutralAndDestructiveButtonStyles()
    {
        var source = Read("CinematicDialogStyling.cs");
        var theme = Read("HiTechTheme.xaml");
        Assert.Contains("dialog.HorizontalAlignment = HorizontalAlignment.Center", source);
        Assert.Contains("dialog.VerticalAlignment = VerticalAlignment.Center", source);
        Assert.Contains("HiTechSectionActionStyle", source);
        Assert.Contains("HiTechDangerButtonStyle", source);
        Assert.Contains("x:Key=\"HiTechDangerButtonStyle\"", theme);
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
