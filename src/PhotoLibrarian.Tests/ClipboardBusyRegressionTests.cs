using Xunit;

namespace PhotoLibrarian.Tests;

public sealed class ClipboardBusyRegressionTests
{
    private static string Source(string name) => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "TestAssets", name));

    [Fact]
    public void ClipboardFlushRetriesTemporaryComLockAndCallerHandlesErrors()
    {
        var operations = Source("PhotoOperationsService.cs");
        var gallery = Source("ImageGridView.xaml.cs");
        Assert.Contains("Clipboard.Flush();", operations);
        Assert.Contains("0x800401D0", operations);
        Assert.Contains("await Task.Delay(80 * (attempt + 1))", operations);
        Assert.Contains("Could not copy photos:", gallery);
        Assert.Contains("Could not cut photos:", gallery);
    }
}
