namespace PhotoLibrarian.Core.Models;

/// <summary>Shared semantics for the tag navigator. A path matches itself and its descendants.</summary>
public static class TagFacetFilter
{
    public static bool MatchesPath(IReadOnlyCollection<string>? assigned, string path)
    {
        assigned ??= Array.Empty<string>();
        return path switch
        {
            "" => assigned.Count > 0,
            "__untagged__" => assigned.Count == 0,
            _ => assigned.Any(tag => string.Equals(tag, path, StringComparison.OrdinalIgnoreCase) ||
                                     tag.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase))
        };
    }

    public static bool Matches(
        IReadOnlyCollection<string>? assigned,
        IReadOnlyCollection<string>? included,
        IReadOnlyCollection<string>? excluded,
        bool requireAll)
    {
        if (excluded?.Any(path => MatchesPath(assigned, path)) == true)
            return false;
        if (included is null || included.Count == 0)
            return true;
        return requireAll
            ? included.All(path => MatchesPath(assigned, path))
            : included.Any(path => MatchesPath(assigned, path));
    }

    /// <summary>Count current matching images for each assigned tag and all ancestors once per image.</summary>
    public static Dictionary<string, int> CountFacets(
        IEnumerable<IReadOnlyCollection<string>> imageTagSets)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var tags in imageTagSets)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (tags.Count == 0) paths.Add("__untagged__");
            else paths.Add("");
            foreach (var tag in tags)
            {
                if (string.IsNullOrWhiteSpace(tag)) continue;
                paths.Add(tag);
                for (int slash = tag.LastIndexOf('/'); slash > 0;
                     slash = tag.LastIndexOf('/', slash - 1))
                    paths.Add(tag[..slash]);
            }
            foreach (var path in paths)
                counts[path] = counts.GetValueOrDefault(path) + 1;
        }
        return counts;
    }
}
