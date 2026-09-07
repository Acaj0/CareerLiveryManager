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

    /// <summary>Set by the ViewModel after construction when a Career Livery Manager
    /// package is already installed for this aircraft; overrides <see cref="ThumbnailPath"/> in the UI.</summary>
    public string? InstalledLiveryThumbnailPath { get; set; }

    public bool IsInstalled { get; set; }

    public string? DisplayThumbnailPath => InstalledLiveryThumbnailPath ?? ThumbnailPath;
}
