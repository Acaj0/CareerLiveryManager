namespace CareerLiveryManager.Core.Models;

/// <summary>
/// A Community package previously created by this app.
/// </summary>
public sealed class InstalledPackageInfo
{
    public required string PackageFolder { get; init; }
    public required string Title { get; init; }
    public required string AircraftSimObjectName { get; init; }
    public string? ThumbnailPath { get; init; }

    /// <summary>Activity key (e.g. "cargo"), empty for single-livery/pre-activity-system packages.</summary>
    public string ActivityKey { get; init; } = string.Empty;

    /// <summary>Human-readable activity name for the UI, e.g. "Cargo Transport". Empty when <see cref="ActivityKey"/> is.</summary>
    public string ActivityDisplayName { get; init; } = string.Empty;

    public bool HasActivity => !string.IsNullOrEmpty(ActivityKey);
}
