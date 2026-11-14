using PhotoLibrarian.Core.Models;
using Xunit;

namespace PhotoLibrarian.Tests;

public sealed class TagFacetFilterTests
{
    [Fact]
    public void AndMode_NarrowsToPhotosContainingEveryIncludedPath()
    {
        string[] tags = ["People/klara", "Fashion/podpatky"];
        Assert.True(TagFacetFilter.Matches(tags, ["People", "Fashion"], null, true));
        Assert.False(TagFacetFilter.Matches(tags, ["People", "Vacation"], null, true));
        Assert.True(TagFacetFilter.Matches(tags, ["People", "Vacation"], null, false));
    }

    [Fact]
    public void ExclusionsOverrideBothIncludeModesAndApplyToDescendants()
    {
        string[] tags = ["People/klara", "Fake/AI"];
        Assert.False(TagFacetFilter.Matches(tags, ["People"], ["Fake"], true));
        Assert.False(TagFacetFilter.Matches(tags, ["People"], ["Fake"], false));
        Assert.True(TagFacetFilter.Matches(["People/klara"], ["People"], ["Fake"], true));
        Assert.False(TagFacetFilter.Matches(["People"], null, ["People"], true));
    }

    [Fact]
    public void UntaggedAndRootTagsMatchOnlyTheirRespectiveImages()
    {
        Assert.True(TagFacetFilter.Matches([], ["__untagged__"], null, true));
        Assert.False(TagFacetFilter.Matches(["People"], ["__untagged__"], null, true));
        Assert.True(TagFacetFilter.Matches(["People"], [""], null, true));
        Assert.False(TagFacetFilter.Matches([], [""], null, true));
    }

    [Fact]
    public void CountsIncludeAncestorsButNeverDoubleCountAnImage()
    {
        IReadOnlyCollection<string>[] photos =
        [
            ["People/klara", "People/solo", "Fashion/podpatky"],
            ["People/klara"],
            []
        ];
        var counts = TagFacetFilter.CountFacets(photos);
        Assert.Equal(2, counts["People"]);
        Assert.Equal(2, counts["People/klara"]);
        Assert.Equal(1, counts["People/solo"]);
        Assert.Equal(1, counts["Fashion"]);
        Assert.Equal(1, counts["Fashion/podpatky"]);
        Assert.Equal(1, counts["__untagged__"]);
        Assert.Equal(2, counts[""]);
        Assert.False(counts.ContainsKey("DoesNotExist"));
    }
}
