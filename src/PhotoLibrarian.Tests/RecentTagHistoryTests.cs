using PhotoLibrarian.Services;
using Xunit;

namespace PhotoLibrarian.Tests;

public sealed class RecentTagHistoryTests
{
    [Fact]
    public void AlbumsKeepIndependentHistories_AndClearOnlyActiveAlbum()
    {
        var first = Path.Combine(Path.GetTempPath(),
            "pl-recent-first-" + Guid.NewGuid().ToString("N"));
        var second = Path.Combine(Path.GetTempPath(),
            "pl-recent-second-" + Guid.NewGuid().ToString("N"));
        try
        {
            RecentTagHistoryService.Remember(first, "Style/Classic");
            RecentTagHistoryService.Remember(first, "Clothing/Dress");
            RecentTagHistoryService.Remember(first, "style/classic");
            RecentTagHistoryService.Remember(second, "Hair/Blonde");

            Assert.Equal(2, RecentTagHistoryService.Load(first).Count);
            Assert.Equal("style/classic", RecentTagHistoryService.Load(first)[0]);
            Assert.Equal(["Hair/Blonde"], RecentTagHistoryService.Load(second));

            RecentTagHistoryService.Clear(first);
            Assert.Empty(RecentTagHistoryService.Load(first));
            Assert.Equal(["Hair/Blonde"], RecentTagHistoryService.Load(second));
        }
        finally
        {
            RecentTagHistoryService.Clear(first);
            RecentTagHistoryService.Clear(second);
        }
    }

    [Fact]
    public void DisplayLimitIsIndependentOfStoredHistory()
    {
        var album = Path.Combine(Path.GetTempPath(),
            "pl-recent-limit-" + Guid.NewGuid().ToString("N"));
        try
        {
            for (var i = 0; i < 25; i++)
                RecentTagHistoryService.Remember(album, $"Tag/{i}");
            Assert.Equal(25, RecentTagHistoryService.Load(album).Count);
            Assert.Equal("Tag/24", RecentTagHistoryService.Load(album)[0]);
        }
        finally
        {
            RecentTagHistoryService.Clear(album);
        }
    }
}
