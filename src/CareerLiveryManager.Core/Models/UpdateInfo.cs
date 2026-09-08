namespace CareerLiveryManager.Core.Models;

/// <summary>
/// Result of checking GitHub Releases for a newer version than the one currently running.
/// </summary>
public sealed class UpdateInfo
{
    public required Version CurrentVersion { get; init; }
    public required Version LatestVersion { get; init; }
    public required string ReleaseUrl { get; init; }
    public required string AssetName { get; init; }
    public required string AssetDownloadUrl { get; init; }
    public long AssetSizeBytes { get; init; }

    /// <summary>URL of the optional "&lt;asset&gt;.sha256" sibling asset, if the release published one.</summary>
    public string? ChecksumDownloadUrl { get; init; }

    public bool IsNewer => LatestVersion > CurrentVersion;
}
