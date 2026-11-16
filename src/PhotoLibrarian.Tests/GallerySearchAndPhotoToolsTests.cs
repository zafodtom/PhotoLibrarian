using System.Xml.Linq;
using Xunit;

namespace PhotoLibrarian.Tests;

/// <summary>Guards viewer action wiring and the simplified gallery toolbar.</summary>
public sealed class GallerySearchAndPhotoToolsTests
{
    private static string Read(string file) => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "TestAssets", file));

    [Fact]
    public void GalleryHasSearchAndSortButNoGroupOrFacetFilterControls()
    {
        var grid = XDocument.Parse(Read("ImageGridView.xaml"));
        var all = grid.ToString();
        Assert.Contains("GalleryFileSearch", all);
        Assert.Contains("GallerySearchApply", all);
        Assert.Contains("GallerySearchClear", all);
        Assert.Contains("SortByCombo", all);
        Assert.DoesNotContain("GroupByCombo", all);
        Assert.DoesNotContain("GridFilterButton", all);
        Assert.DoesNotContain("FilterIncludeTags", all);
        Assert.DoesNotContain("FilterExcludeTags", all);
    }

    [Fact]
    public void EmptyGalleryFooterStaysInBottomRow()
    {
        var document = XDocument.Parse(Read("ImageGridView.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var footerText = Assert.Single(document.Descendants()
            .Where(element => (string?)element.Attribute(xaml + "Name") == "EmptyStateCode"));
        var footerRow = Assert.IsType<XElement>(footerText.Parent);
        Assert.Equal("Grid", footerRow.Name.LocalName);
        Assert.Equal("2", (string?)footerRow.Attribute("Grid.Row"));
    }

    [Fact]
    public void CropRibbonExposesSeparateSaveAsAndExistingApply()
    {
        var ribbon = Read("TopRibbon.xaml");
        Assert.Contains("RibbonCropExport", ribbon);
        Assert.Contains("OnExportCropClick", ribbon);
        Assert.Contains("RibbonCropApply", ribbon);
        Assert.Contains("RibbonCropCancel", ribbon);
    }

    [Fact]
    public void BothViewerRailsOfferTwoQuarterTurns()
    {
        var main = Read("MainWindow.xaml");
        Assert.Contains("SideRotateLeftButton", main);
        Assert.Contains("SideRotateRightButton", main);
        Assert.Contains("FullscreenRotateLeftButton", main);
        Assert.Contains("FullscreenRotateRightButton", main);
    }
}
