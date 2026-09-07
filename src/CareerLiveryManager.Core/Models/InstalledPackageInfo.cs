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
}
