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
    public void RotationHasRecoveryPathIfFaceMetadataSaveFails()
    {
        var window = Read("MainWindow.xaml.cs");
        var viewModel = Read("MainViewModel.cs");
        Assert.Contains("GetBackupPathAsync(entry.FilePath)", window);
        Assert.Contains("pixelsWereRotated", window);
        Assert.Contains("System.IO.File.Copy(", window);
        Assert.Contains("WriteBatchAsync(", viewModel);
        Assert.Contains("RotateFaceRegionsAsync(entry.Id, !clockwise)", viewModel);
    }

    [Fact]
    public void CropSaveAsStartsInTheOpenedPhotosDirectory()
    {
        var window = Read("MainWindow.xaml.cs");
        Assert.Contains("new Microsoft.Windows.Storage.Pickers.FileSavePicker(AppWindow.Id)", window);
        Assert.Contains("SuggestedFolder = System.IO.Path.GetDirectoryName(entry.FilePath)", window);
        Assert.Contains("SuggestedFileName = System.IO.Path.GetFileNameWithoutExtension(entry.FilePath) + \"_crop\"", window);
        Assert.DoesNotContain("new Windows.Storage.Pickers.FileSavePicker", window);
    }

    [Fact]
    public void GalleryToolbarKeepsConsistentHeightAndControlStyles()
    {
        var document = XDocument.Parse(Read("ImageGridView.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement ByName(string name) => Assert.Single(document.Descendants()
            .Where(element => (string?)element.Attribute(xaml + "Name") == name));
        var sort = ByName("SortByCombo");
        Assert.Contains("HiTechComboStyle", (string?)sort.Attribute("Style") ?? "");
        foreach (var name in new[] { "SortByCombo", "SortOrderBtn", "SearchFileNameBox" })
            Assert.Equal("34", (string?)ByName(name).Attribute("Height"));
        foreach (var name in new[] { "GallerySearchApply", "GallerySearchClear" })
        {
            var button = Assert.Single(document.Descendants()
                .Where(element => (string?)element.Attribute("AutomationProperties.AutomationId") == name));
            Assert.Equal("34", (string?)button.Attribute("Height"));
            Assert.Contains("HiTechRailButtonStyle", (string?)button.Attribute("Style") ?? "");
        }
        foreach (var clickHandler in new[] { "OnDecreaseSize", "OnIncreaseSize" })
        {
            var button = Assert.Single(document.Descendants()
                .Where(element => (string?)element.Attribute("Click") == clickHandler));
            Assert.Equal("34", (string?)button.Attribute("Height"));
        }
    }

    [Fact]
    public void RotationIconsAreDirectionalAndDoNotUseUnrelatedFontGlyphs()
    {
        var document = XDocument.Parse(Read("MainWindow.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        string RotationGeometry(string buttonName)
        {
            var button = Assert.Single(document.Descendants()
                .Where(element => (string?)element.Attribute(xaml + "Name") == buttonName));
            var path = Assert.Single(button.Descendants()
                .Where(element => element.Name.LocalName == "Path"));
            return (string?)path.Attribute("Data") ?? "";
        }

        var left = RotationGeometry("SideRotateLeftButton");
        var right = RotationGeometry("SideRotateRightButton");
        Assert.NotEqual(left, right);
        Assert.Equal(left, RotationGeometry("FullscreenRotateLeftButton"));
        Assert.Equal(right, RotationGeometry("FullscreenRotateRightButton"));
        Assert.Contains("A 9,9", left);
        Assert.Contains("A 9,9", right);
    }

    [Fact]
    public void CropSaveAsOffersKeepTagsWithoutTagsAndCancel()
    {
        var window = Read("MainWindow.xaml.cs");
        var export = window.Split("private async void OnRibbonExportCropClicked(", 2)[1]
            .Split("private void UpdateSettingsVisibility(", 2)[0];

        Assert.Contains("GetPhotoTagsForCropAsync(entry)", export);
        Assert.Contains("subjectOverride: \"COPY ORIGINAL PHOTO TAGS?\"", export);
        Assert.Contains("confirmLabel: \"KEEP TAGS\"", export);
        Assert.Contains("SecondaryButtonText = \"WITHOUT TAGS\"", export);
        Assert.Contains("CloseButtonText = \"CANCEL\"", export);
        Assert.Contains("if (decision == ContentDialogResult.None)", export);
        Assert.True(export.IndexOf("tagChoice.ShowAsync()", StringComparison.Ordinal) <
                    export.IndexOf("picker.PickSaveFileAsync()", StringComparison.Ordinal));
        Assert.Contains("RegisterExportedCropAsync(selected.Path, copiedTags)", export);
    }

    [Fact]
    public void CropApplyPersistsOriginalTagAssignments()
    {
        var window = Read("MainWindow.xaml.cs");
        var method = window.Split("private async void OnRibbonApplyCropClicked(", 2)[1]
            .Split("private async void OnRibbonExportCropClicked(", 2)[0];
        Assert.Contains("GetPhotoTagsForCropAsync(entry)", method);
        Assert.Contains("CropImageAsync(", method);
        Assert.Contains("bounds.Value, assignedTags", method);

        var model = Read("MainViewModel.cs");
        Assert.Contains("GetTagsAsync(image.Id)", model);
        Assert.Contains("ClearImageTagsAsync(entry.Id)", model);
        Assert.Contains("AddTagAsync(new ImageTag", model);
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
