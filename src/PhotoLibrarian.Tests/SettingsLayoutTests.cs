using System.Xml.Linq;
using Xunit;

namespace PhotoLibrarian.Tests;

/// <summary>
/// Guards the configuration footer from being obscured by the application's
/// persistent SYS bar. This tests XAML structure without launching WinUI.
/// </summary>
public sealed class SettingsLayoutTests
{
    [Fact]
    public void SettingsFooterRemainsAboveGlobalStatusBar()
    {
        var window = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory, "TestAssets", "MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var settingsOverlay = window.Descendants(presentation + "Border")
            .Single(element => (string?)element.Attribute(x + "Name") == "SettingsOverlay");
        Assert.Equal("0", (string?)settingsOverlay.Attribute("Grid.Row"));
        Assert.Equal("3", (string?)settingsOverlay.Attribute("Grid.RowSpan"));

        var globalStatus = window.Descendants(presentation + "Grid")
            .Single(element => (string?)element.Attribute(x + "Name") == "StatusBar");
        Assert.Equal("3", (string?)globalStatus.Attribute("Grid.Row"));

        var settings = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory, "TestAssets", "SettingsPanel.xaml"));
        var close = settings.Descendants(presentation + "Button")
            .Single(element => (string?)element.Attribute(x + "Name") == "CloseSettingsButton");
        var footer = close.Parent;
        Assert.NotNull(footer);
        Assert.Equal("Grid", footer!.Name.LocalName);
        Assert.Equal("2", (string?)footer.Attribute("Grid.Row"));

        var content = settings.Descendants(presentation + "ScrollViewer").ToArray();
        Assert.Contains(content, element => (string?)element.Attribute("Grid.Column") == "1");
    }
}
