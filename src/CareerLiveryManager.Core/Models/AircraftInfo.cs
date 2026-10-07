namespace CareerLiveryManager.Core.Models;

/// <summary>
/// Represents an aircraft detected in the Official content folder.
/// </summary>
public sealed class AircraftInfo
{
    public required string Title { get; init; }
    public required string PackageFolder { get; init; }
    public required string ManifestPath { get; init; }
    public required string SimObjectName { get; init; }
    public required string VendorPath { get; init; }
    public required string VendorName { get; init; }
    public bool IsSupported { get; init; }
    public string? ThumbnailPath { get; init; }

    /// <summary>
    /// Freelance Career activities detected for this aircraft (Cargo Transport, Flightseeing, etc.),
    /// plus the generic "official_static" slot when present. Empty for single-livery aircraft
    /// (Longitude, CJ4, A321...), which keep using the original apply-livery flow untouched.
    /// </summary>
    public IReadOnlyList<AircraftActivityInfo> Activities { get; init; } = Array.Empty<AircraftActivityInfo>();

    public bool HasActivities => Activities.Count > 0;

    /// <summary>How many Career jobs (Cargo, Flightseeing...) can get their own livery. The generic
    /// "Default" slot isn't a job, so it isn't counted.</summary>
    public int ActivityCount => Activities.Count(a => !a.IsGenericSlot);

    /// <summary>"3 activities" / "1 activity", or empty when the aircraft has none.</summary>
    public string ActivityBadgeText => ActivityCount switch
    {
        0 => string.Empty,
        1 => "1 activity",
        var n => $"{n} activities",
    };

    /// <summary>Set by the ViewModel after construction when a Career Livery Manager
    /// package is already installed for this aircraft; overrides <see cref="ThumbnailPath"/> in the UI.</summary>
    public string? InstalledLiveryThumbnailPath { get; set; }

    public bool IsInstalled { get; set; }

    /// <summary>How many Career Livery Manager packages are installed for this aircraft
    /// (e.g. 3 if it has separate custom liveries for Cargo, Flightseeing, and VIP).</summary>
    public int InstalledCount { get; set; }

    public string? DisplayThumbnailPath => InstalledLiveryThumbnailPath ?? ThumbnailPath;
}
