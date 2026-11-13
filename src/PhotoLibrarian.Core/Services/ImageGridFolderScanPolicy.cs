using PhotoLibrarian.Core.Models;

namespace PhotoLibrarian.Core.Services;

/// <summary>
/// Controls when a disk-only photo can be shown as a temporary grid placeholder.
/// Tag exclusions cannot be evaluated without indexed metadata.
/// </summary>
public static class ImageGridFolderScanPolicy
{
    public static bool ShouldScanForMissingFiles(
        bool scanRequested,
        bool hasFolderFilter,
        bool hasInclusiveTagFilter,
        bool hasExcludedTagFilter) =>
        scanRequested && hasFolderFilter && !hasInclusiveTagFilter && !hasExcludedTagFilter;

    /// <summary>
    /// A filtered grid is not an index of the library. Use ALL database images
    /// to decide which disk files still need indexing.
    /// </summary>
    public static HashSet<string> GetIndexedPaths(IEnumerable<ImageEntry> indexedImages) =>
        indexedImages.Select(image => image.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
