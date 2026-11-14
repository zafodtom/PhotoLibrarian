using System.Diagnostics;
using System.Reflection;
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
    private const string ReleasesApiUrl = "https://api.github.com/repos/" + Repository + "/releases?per_page=50";
    private static readonly Regex ReleaseTagPattern = new(@"^v(?<core>[0-9]+\.[0-9]+\.[0-9]+)(?:-dev\.(?<build>[0-9]+))?$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private const long MaximumInstallerBytes = 700L * 1024 * 1024;
    private static readonly HttpClient Client = CreateClient();

    public sealed record Release(Version Version, string Tag, string AssetName,
        Uri DownloadUrl, long Size, string Sha256, bool IsDevelopment = false);

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

    private static readonly string PreferenceFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PhotoLibrarian", "update-channel.txt");

    public static bool DevelopmentUpdatesEnabled
    {
        get
        {
            try { return File.ReadAllText(PreferenceFile).Trim() == "development"; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
        set
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PreferenceFile)!);
            File.WriteAllText(PreferenceFile, value ? "development" : "stable");
        }
    }

    // In development builds the installer embeds its prerelease build number
    // into AssemblyInformationalVersion; a normal install cannot infer this
    // from numeric AssemblyVersion alone.
    public static string InstalledTag
    {
        get
        {
            var informational = typeof(GitHubUpdateService).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion?.Split('+')[0] ?? "";
            var tag = "v" + informational.TrimStart('v');
            return ReleaseTagPattern.IsMatch(tag)
                ? tag
                : "v" + InstalledVersion.ToString(3);
        }
    }

    /// <summary>Compare a candidate GitHub release to an installed release tag.</summary>
    public static bool IsNewerReleaseTag(string candidateTag, string installedTag) =>
        CompareTags(candidateTag, installedTag) > 0;

    private static int CompareTags(string a, string b)
    {
        var matchA = ReleaseTagPattern.Match(a);
        var matchB = ReleaseTagPattern.Match(b);
        if (!matchA.Success || !matchB.Success)
            throw new InvalidDataException("Invalid release version tag.");

        var aVersion = Version.Parse(matchA.Groups["core"].Value);
        var bVersion = Version.Parse(matchB.Groups["core"].Value);
        var versionComparison = aVersion.CompareTo(bVersion);
        if (versionComparison != 0) return versionComparison;
        var aDev = matchA.Groups["build"].Success;
        var bDev = matchB.Groups["build"].Success;
        // A final release ranks above every development build of the same version.
        if (aDev != bDev) return aDev ? -1 : 1;
        if (!aDev) return 0;
        return long.Parse(matchA.Groups["build"].Value,
            System.Globalization.CultureInfo.InvariantCulture).CompareTo(
            long.Parse(matchB.Groups["build"].Value,
                System.Globalization.CultureInfo.InvariantCulture));
    }

    public static async Task<Release?> CheckAsync(
        CancellationToken cancellationToken = default, bool includeDevelopment = false)
    {
        using var response = await Client.GetAsync(
            includeDevelopment ? ReleasesApiUrl : ApiUrl, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var root = document.RootElement;
        var releases = root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray().ToArray()
            : new[] { root };
        Release? best = null;
        foreach (var candidate in releases)
        {
            if (candidate.GetProperty("draft").GetBoolean())
                continue;
            var tag = candidate.GetProperty("tag_name").GetString() ?? "";
            var match = ReleaseTagPattern.Match(tag);
            if (!match.Success ||
                !Version.TryParse(match.Groups["core"].Value, out var version))
                continue;

            var development = match.Groups["build"].Success;
            if (development != candidate.GetProperty("prerelease").GetBoolean())
                continue;
            if (development && !includeDevelopment)
                continue;
            if (CompareTags(tag, InstalledTag) <= 0)
                continue;
            if (best is not null && CompareTags(tag, best.Tag) <= 0)
                continue;

            var expectedName = $"PhotoLibrarian-{version.Major}.{version.Minor}.{version.Build}-Setup-x64.exe";
            foreach (var asset in candidate.GetProperty("assets").EnumerateArray())
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

                best = new Release(version, tag, expectedName, url, size, digest[7..], development);
                break;
            }
        }
        return best;
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
    // the downloaded installer so Inno Setup can replace the app files.
    // Pass verification data as immutable process arguments rather than an
    // editable job file in a user-writable temporary directory.
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
        File.Copy(installedHelper, helperCopy, overwrite: true);

        var start = new ProcessStartInfo
        {
            FileName = helperCopy,
            WorkingDirectory = directory,
            UseShellExecute = true
        };
        start.ArgumentList.Add(path);
        start.ArgumentList.Add(release.Size.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.ArgumentList.Add(release.Sha256);
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.ArgumentList.Add(targetApp);
        start.ArgumentList.Add(albumPath ?? "");
        start.ArgumentList.Add(release.Version.ToString(3));
        if (Process.Start(start) is null)
            throw new InvalidOperationException("Windows did not start the update helper.");
    }
}
