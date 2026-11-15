using PhotoLibrarian.Core.Services;
using Xunit;

namespace PhotoLibrarian.Tests;

public sealed class TagSearchMatcherTests
{
    [Theory]
    [InlineData("Oblečení/Šaty", "saty")]
    [InlineData("Barva/Růžová", "ruzova")]
    [InlineData("Vzhled/Účes", "UCES")]
    [InlineData("Oblečení/Košilka", "KOSILKA")]
    [InlineData("Žluté šaty", "zlute")]
    [InlineData("Oblečení/Šaty", "obleceni/saty")]
    [InlineData("Ru\u030az\u030cova\u0301", "růžová")]
    public void SearchesTagPathsWithoutDiacritics(string value, string query)
        => Assert.True(TagSearchMatcher.Contains(value, query));

    [Fact]
    public void UnrelatedTagsDoNotMatch()
        => Assert.False(TagSearchMatcher.Contains("Oblečení/Šaty", "kalhoty"));

    [Fact]
    public void MatchingDoesNotChangeOriginalTag()
    {
        var original = "Oblečení/Šaty";
        Assert.True(TagSearchMatcher.Contains(original, "saty"));
        Assert.Equal("Oblečení/Šaty", original);
    }
}
