using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PhotoLibrarian.Services;

/// <summary>
/// Global display preference with independent per-album recent-tag history.
/// The history keeps more items than are currently displayed so decreasing the
/// visible count does not permanently discard the older choices.
/// </summary>
public static class RecentTagHistoryService
{
    public const int DefaultVisibleCount = 5;
    public const int MaximumVisibleCount = 20;
    private const int MaximumStoredCount = 40;
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PhotoLibrarian", "recent-tags");
    private static readonly string PreferencesPath = Path.Combine(DirectoryPath, "settings.json");

    public static event EventHandler? Changed;

    public static int VisibleCount
    {
        get
        {
            try
            {
                if (!File.Exists(PreferencesPath)) return DefaultVisibleCount;
                using var preferences = JsonDocument.Parse(File.ReadAllText(PreferencesPath));
                return preferences.RootElement.TryGetProperty("visibleCount", out var property) &&
                       property.TryGetInt32(out var count)
                    ? Math.Clamp(count, 0, MaximumVisibleCount)
                    : DefaultVisibleCount;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[RECENT TAGS] Preferences read failed: " + ex);
                return DefaultVisibleCount;
            }
        }
    }

    public static void SetVisibleCount(int count)
    {
        count = Math.Clamp(count, 0, MaximumVisibleCount);
        Directory.CreateDirectory(DirectoryPath);
        WriteAtomic(PreferencesPath, JsonSerializer.Serialize(new { visibleCount = count }));
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static string? GetHistoryPath(string? albumPath)
    {
        if (string.IsNullOrWhiteSpace(albumPath)) return null;
        var normalized = Path.GetFullPath(albumPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return Path.Combine(DirectoryPath, hash + ".json");
    }

    public static IReadOnlyList<string> Load(string? albumPath)
    {
        var path = GetHistoryPath(albumPath);
        if (path is null || !File.Exists(path)) return [];
        try
        {
            var tags = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? [];
            return tags.Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaximumStoredCount).ToArray();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[RECENT TAGS] History read failed: " + ex);
            return [];
        }
    }

    public static void Remember(string albumPath, string tag)
    {
        tag = tag.Trim();
        if (tag.Length == 0) return;
        var path = GetHistoryPath(albumPath);
        if (path is null) return;
        var tags = new[] { tag }.Concat(Load(albumPath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumStoredCount).ToArray();
        Directory.CreateDirectory(DirectoryPath);
        WriteAtomic(path, JsonSerializer.Serialize(tags));
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void Clear(string? albumPath)
    {
        var path = GetHistoryPath(albumPath);
        if (path is null) throw new InvalidOperationException("No active album.");
        if (File.Exists(path)) File.Delete(path);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static void WriteAtomic(string path, string content)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, content);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
