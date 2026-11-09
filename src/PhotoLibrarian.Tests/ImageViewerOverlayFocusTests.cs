using System.Xml.Linq;
using Xunit;

namespace PhotoLibrarian.Tests;

public sealed class ImageViewerOverlayFocusTests
{
    [Fact]
    public void OverlayCanReceiveFocusForKeyboardNavigation()
    {
        var xamlPath = Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "ImageViewerOverlay.xaml");
        var root = XDocument.Load(xamlPath).Root;

        Assert.NotNull(root);
        Assert.Equal("True", (string?)root.Attribute("IsTabStop"));
        // Keyboard routing is deliberately attached to the window content root
        // in code-behind: nested ScrollViewers and toolbar controls may own focus.
        var codePath = Path.Combine(
            AppContext.BaseDirectory, "TestAssets", "ImageViewerOverlay.xaml.cs");
        var code = File.ReadAllText(codePath);
        Assert.Contains("_windowKeyRoot?.AddHandler(", code);
        Assert.Contains("UIElement.KeyDownEvent", code);
        Assert.Contains("new KeyEventHandler(OnWindowKeyDown)", code);
    }
}
