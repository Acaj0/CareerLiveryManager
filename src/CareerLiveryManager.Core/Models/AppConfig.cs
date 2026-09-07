namespace CareerLiveryManager.Core.Models;

/// <summary>
/// Persisted user configuration: paths to the MSFS Official content folder
/// (e.g. "...\Official2024\Steam") and the Community folder.
/// </summary>
public sealed class AppConfig
{
    public string OfficialPath { get; set; } = string.Empty;
    public string CommunityPath { get; set; } = string.Empty;
}
