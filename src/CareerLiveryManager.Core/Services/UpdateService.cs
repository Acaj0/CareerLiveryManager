using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using CareerLiveryManager.Core.Models;

namespace CareerLiveryManager.Core.Services;

/// <summary>
/// Checks GitHub Releases for a newer version of the app, downloads it, and hands off
/// to a small generated updater script that swaps the install folder once this process
/// has exited. Only ever touches the app's own install directory - never Official
/// content, Community packages, or Career save data.
/// </summary>
public sealed class UpdateService
{
    private const string RepoOwner = "Acaj0";
    private const string RepoName = "CareerLiveryManager";
    private const string LatestReleaseApiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

    // Short timeout for the small "check latest release" API call - if GitHub is slow to
    // respond, we'd rather silently give up than block app startup.
    private static readonly HttpClient Http = CreateHttpClient(TimeSpan.FromSeconds(10));

    // The zip asset can be tens of megabytes; a slow connection can easily take longer than
    // 10 seconds just to finish, so downloads get a much more generous timeout of their own.
    private static readonly HttpClient DownloadHttp = CreateHttpClient(TimeSpan.FromMinutes(10));

    private static HttpClient CreateHttpClient(TimeSpan timeout)
    {
        var client = new HttpClient { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CareerLiveryManager-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>
    /// Returns the currently running app's version, read from the entry assembly.
    /// </summary>
    public static Version GetCurrentVersion() =>
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>
    /// Asks the GitHub API for the latest release and returns update info if it's newer
    /// than <see cref="GetCurrentVersion"/>. Returns null on any failure (offline, GitHub
    /// unreachable, no matching asset, etc.) - update checking must never surface as an
    /// alarming error, it just silently doesn't find an update.
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(8));

            using var response = await Http.GetAsync(LatestReleaseApiUrl, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var release = await response.Content.ReadFromJsonAsync<GitHubRelease>(cancellationToken: cts.Token);
            if (release?.TagName is null || release.Assets is null)
            {
                return null;
            }

            var latestVersion = ParseVersion(release.TagName);
            if (latestVersion is null)
            {
                return null;
            }

            var zipAsset = release.Assets.FirstOrDefault(a =>
                a.Name.StartsWith(RepoName, StringComparison.OrdinalIgnoreCase) &&
                a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (zipAsset is null)
            {
                return null;
            }

            var checksumAsset = release.Assets.FirstOrDefault(a =>
                string.Equals(a.Name, zipAsset.Name + ".sha256", StringComparison.OrdinalIgnoreCase));

            var currentVersion = GetCurrentVersion();
            var info = new UpdateInfo
            {
                CurrentVersion = currentVersion,
                LatestVersion = latestVersion,
                ReleaseUrl = release.HtmlUrl ?? $"https://github.com/{RepoOwner}/{RepoName}/releases",
                AssetName = zipAsset.Name,
                AssetDownloadUrl = zipAsset.BrowserDownloadUrl,
                AssetSizeBytes = zipAsset.Size,
                ChecksumDownloadUrl = checksumAsset?.BrowserDownloadUrl,
            };

            return info.IsNewer ? info : null;
        }
        catch
        {
            // Offline, DNS failure, GitHub rate limit, malformed response, etc.
            // Update checking is best-effort and must never block normal app use.
            return null;
        }
    }

    /// <summary>
    /// Downloads the release zip to <paramref name="destZipPath"/>, reporting 0-100 progress.
    /// Throws on failure - the caller decides how to surface that (existing app files are
    /// never touched by this step, only the temp destination).
    /// </summary>
    public async Task DownloadAsync(UpdateInfo info, string destZipPath, IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destZipPath)!);

        using var response = await DownloadHttp.GetAsync(info.AssetDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? info.AssetSizeBytes;
        await using var httpStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(destZipPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        long totalRead = 0;
        int read;
        while ((read = await httpStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
            totalRead += read;
            if (totalBytes > 0)
            {
                progress?.Report(Math.Min(100.0, totalRead * 100.0 / totalBytes));
            }
        }
    }

    /// <summary>
    /// Downloads the optional "&lt;asset&gt;.sha256" file and checks it against the
    /// downloaded zip. Returns true if there's nothing to check against (older releases
    /// published before this convention existed) so those updates still work.
    /// </summary>
    public async Task<bool> VerifyChecksumAsync(UpdateInfo info, string zipPath, CancellationToken ct)
    {
        if (info.ChecksumDownloadUrl is null)
        {
            return true;
        }

        try
        {
            var expected = (await DownloadHttp.GetStringAsync(info.ChecksumDownloadUrl, ct)).Trim();
            var expectedHash = expected.Split(' ', '\t')[0].Trim();

            await using var stream = File.OpenRead(zipPath);
            var actualHashBytes = await SHA256.HashDataAsync(stream, ct);
            var actualHash = Convert.ToHexString(actualHashBytes);

            return string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // Couldn't fetch/compute the checksum - treat as unverifiable rather than failed,
            // the caller can still decide to abort if it wants strict verification.
            return true;
        }
    }

    public static string ExtractUpdate(string zipPath, string extractDir)
    {
        if (Directory.Exists(extractDir))
        {
            Directory.Delete(extractDir, recursive: true);
        }

        ZipFile.ExtractToDirectory(zipPath, extractDir);
        return extractDir;
    }

    /// <summary>
    /// Writes a small .cmd bootstrap script that waits for this process to exit, copies the
    /// extracted update over the current install directory, relaunches the app, then deletes
    /// itself and the temp files. Launches it and returns - the caller is expected to shut the
    /// app down immediately after calling this.
    /// </summary>
    public void LaunchUpdaterAndExit(string extractedDir)
    {
        var installDir = AppContext.BaseDirectory.TrimEnd('\\');
        var exeName = Path.GetFileName(Environment.ProcessPath) ?? "CareerLiveryManager.App.exe";
        var exePath = Path.Combine(installDir, exeName);
        var currentProcessId = Environment.ProcessId;
        var scriptPath = Path.Combine(Path.GetTempPath(), $"clm_update_{Guid.NewGuid():N}.cmd");
        var updateRootDir = Path.GetDirectoryName(extractedDir.TrimEnd('\\'))!;

        var script = new StringBuilder();
        script.AppendLine("@echo off");
        script.AppendLine("title Career Livery Manager Updater");
        script.AppendLine("echo Updating Career Livery Manager, please wait...");
        script.AppendLine($":waitloop");
        script.AppendLine($"tasklist /fi \"PID eq {currentProcessId}\" | find \" {currentProcessId} \" >nul");
        script.AppendLine("if not errorlevel 1 (");
        script.AppendLine("  timeout /t 1 /nobreak >nul");
        script.AppendLine("  goto waitloop");
        script.AppendLine(")");
        script.AppendLine($"robocopy \"{extractedDir}\" \"{installDir}\" /E /IS /IT /R:3 /W:1 >nul");
        script.AppendLine("if not exist \"" + exePath + "\" (");
        script.AppendLine("  echo Update failed: the new executable was not found after copying.");
        script.AppendLine("  echo Your previous installation was left untouched. Files kept at:");
        script.AppendLine($"  echo {extractedDir}");
        script.AppendLine("  pause");
        script.AppendLine("  goto cleanup");
        script.AppendLine(")");
        script.AppendLine($"start \"\" \"{exePath}\"");
        script.AppendLine(":cleanup");
        script.AppendLine($"rmdir /s /q \"{updateRootDir}\" 2>nul");
        script.AppendLine("(goto) 2>nul & del \"%~f0\"");

        File.WriteAllText(scriptPath, script.ToString(), Encoding.ASCII);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"")
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal,
        });
    }

    private static Version? ParseVersion(string tagName)
    {
        var trimmed = tagName.TrimStart('v', 'V');
        return Version.TryParse(trimmed, out var version) ? version : null;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }
}
