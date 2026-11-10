using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PhotoLibrarian.Services;

/// <summary>
/// Explicit, user-initiated updates from stable public GitHub Releases.
/// No background network requests or account credentials; installation runs
/// silently only after explicit confirmation and verified download.
/// </summary>
public static class GitHubUpdateService
{
    private const string Repository = "zafodtom/PhotoLibrarian";
    private const string ApiUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";
    private const long MaximumInstallerBytes = 700L * 1024 * 1024;
    private static readonly HttpClient Client = CreateClient();

    public sealed record Release(Version Version, string Tag, string AssetName,
        Uri DownloadUrl, long Size, string Sha256);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PhotoLibrarian-Update/1.1");
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static Version InstalledVersion =>
        typeof(GitHubUpdateService).Assembly.GetName().Version ?? new Version(1, 1, 0, 0);

    public static async Task<Release?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetAsync(ApiUrl, cancellationToken);
        // A new project can have Actions artifacts but no published stable release yet.
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() ||
            root.GetProperty("prerelease").GetBoolean())
            return null;

        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v[0-9]+\.[0-9]+\.[0-9]+$",
                RegexOptions.CultureInvariant) ||
            !Version.TryParse(tag[1..], out var latest) ||
            latest <= InstalledVersion)
            return null;

        var expectedName = $"PhotoLibrarian-{latest.Major}.{latest.Minor}.{latest.Build}-Setup-x64.exe";
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (!string.Equals(asset.GetProperty("name").GetString(),
                    expectedName, StringComparison.Ordinal))
                continue;

            var size = asset.GetProperty("size").GetInt64();
            if (size <= 0 || size > MaximumInstallerBytes)
                throw new InvalidDataException("Release installer size is invalid.");

            if (!asset.TryGetProperty("digest", out var digestElement))
                throw new InvalidDataException("Release lacks SHA-256 digest.");
            var digest = digestElement.GetString() ?? "";
            if (!Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$",
                    RegexOptions.CultureInvariant))
                throw new InvalidDataException("Release has no valid SHA-256 digest.");

            var urlText = asset.GetProperty("browser_download_url").GetString() ?? "";
            if (!Uri.TryCreate(urlText, UriKind.Absolute, out var url) ||
                url.Scheme != Uri.UriSchemeHttps ||
                url.Host != "github.com" ||
                url.AbsolutePath !=
                    $"/{Repository}/releases/download/{tag}/{expectedName}")
                throw new InvalidDataException("Installer URL does not match the official release.");

            return new Release(latest, tag, expectedName, url, size, digest[7..]);
        }
        throw new InvalidDataException("The release has no matching x64 installer.");
    }

    public static async Task<string> DownloadVerifiedAsync(Release release,
        IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var targetFolder = Path.Combine(Path.GetTempPath(), "PhotoLibrarian-Updates",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetFolder);
        var destination = Path.Combine(targetFolder, release.AssetName);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, release.DownloadUrl);
            using var response = await Client.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length &&
                length != release.Size)
                throw new InvalidDataException("Release size changed during download.");

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var destinationStream = new FileStream(destination, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[128 * 1024];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += read;
                    if (total > release.Size || total > MaximumInstallerBytes)
                        throw new InvalidDataException("Installer exceeded expected size.");
                    hash.AppendData(buffer, 0, read);
                    await destinationStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    progress?.Report((int)(total * 100 / release.Size));
                }
                await destinationStream.FlushAsync(cancellationToken);
                if (total != release.Size ||
                    !string.Equals(Convert.ToHexString(hash.GetHashAndReset()),
                        release.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Installer size or SHA-256 verification failed.");
            }
            return destination;
        }
        catch
        {
            try { Directory.Delete(targetFolder, recursive: true); } catch { }
            throw;
        }
    }

    // The helper is published alongside the application, then copied beside
    // the downloaded installer so Inno Setup may replace the running app files.
    private sealed record UpdateJob(
        string InstallerPath,
        long InstallerSize,
        string Sha256,
        int ProcessId,
        string AppExecutable,
        string? AlbumPath,
        string Version);

    public static void LaunchUpdateHelper(
        Release release, string verifiedInstallerPath, string? albumPath)
    {
        var path = Path.GetFullPath(verifiedInstallerPath);
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidDataException("Invalid update directory.");
        if (!File.Exists(path) ||
            new FileInfo(path).Length != release.Size ||
            !string.Equals(Path.GetFileName(path), release.AssetName,
                StringComparison.Ordinal))
            throw new InvalidDataException("Downloaded installer is missing or changed.");

        var installedHelper = Path.Combine(
            AppContext.BaseDirectory, "PhotoLibrarian.UpdateHelper.exe");
        if (!File.Exists(installedHelper))
            throw new FileNotFoundException(
                "The update helper is not installed. Reinstall PhotoLibrarian from the official release.",
                installedHelper);

        var targetApp = Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, "PhotoLibrarian.exe");
        targetApp = Path.GetFullPath(targetApp);
        if (!string.Equals(Path.GetFileName(targetApp), "PhotoLibrarian.exe",
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected PhotoLibrarian executable.");

        var helperCopy = Path.Combine(directory, "PhotoLibrarian.UpdateHelper.exe");
        var jobPath = Path.Combine(directory, "update-job.json");
        File.Copy(installedHelper, helperCopy, overwrite: true);
        var job = new UpdateJob(
            path, release.Size, release.Sha256,
            Environment.ProcessId, targetApp, albumPath,
            release.Version.ToString(3));
        File.WriteAllText(jobPath, JsonSerializer.Serialize(job));

        var start = new ProcessStartInfo
        {
            FileName = helperCopy,
            WorkingDirectory = directory,
            UseShellExecute = true
        };
        start.ArgumentList.Add(jobPath);
        Process.Start(start)
            ?? throw new InvalidOperationException("Windows did not start the update helper.");
    }
}
