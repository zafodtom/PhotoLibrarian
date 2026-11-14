using PhotoLibrarian.Services;
using Xunit;

namespace PhotoLibrarian.Tests;

public sealed class DevelopmentUpdateChannelTests
{
    [Theory]
    [InlineData("v1.1.7-dev.12", "v1.1.6", true)]
    [InlineData("v1.1.7-dev.12", "v1.1.7-dev.11", true)]
    [InlineData("v1.1.7-dev.12", "v1.1.7-dev.12", false)]
    [InlineData("v1.1.7-dev.11", "v1.1.7-dev.12", false)]
    [InlineData("v1.1.7", "v1.1.7-dev.500", true)]
    [InlineData("v1.1.7-dev.500", "v1.1.7", false)]
    [InlineData("v1.1.6", "v1.1.7-dev.1", false)]
    [InlineData("v1.2.0", "v1.1.7-dev.900", true)]
    public void ReleaseOrderingRespectsStableAndDevelopmentPrecedence(
        string candidate, string installed, bool expected)
    {
        Assert.Equal(expected, GitHubUpdateService.IsNewerReleaseTag(candidate, installed));
    }

    [Theory]
    [InlineData("v1.1.7-beta.3")]
    [InlineData("v1.1.7-dev.foo")]
    [InlineData("v1.1")]
    public void RejectsUnknownOrMalformedReleaseTags(string invalid)
    {
        Assert.Throws<InvalidDataException>(() =>
            GitHubUpdateService.IsNewerReleaseTag(invalid, "v1.1.6"));
    }
}
