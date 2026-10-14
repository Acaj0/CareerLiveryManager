namespace CareerLiveryManager.Core.Models;

/// <summary>
/// Persisted user configuration: paths to the MSFS Official content folder
/// (e.g. "...\Official2024\Steam") and the Community folder.
/// </summary>
public sealed class AppConfig
{
    public string OfficialPath { get; set; } = string.Empty;
    public string CommunityPath { get; set; } = string.Empty;

    /// <summary>Whether the app checks GitHub Releases for a newer version on startup.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Set once the Community screen has been opened; the red badge on its header button disappears after that.</summary>
    public bool CommunityVisited { get; set; }
}
