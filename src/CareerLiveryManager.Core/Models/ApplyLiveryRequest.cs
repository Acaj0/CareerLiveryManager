namespace CareerLiveryManager.Core.Models;

public sealed class ApplyLiveryRequest
{
    public required AircraftInfo Aircraft { get; init; }
    public required LiverySourceInfo Source { get; init; }

    /// <summary>
    /// When true and the source has a "_DR" variant, the DR variant becomes the
    /// winning ("!"-prefixed) livery and the base folder is kept unprefixed
    /// (only as a texture fallback source). When false (or no DR exists), the
    /// base folder itself becomes the winning livery.
    /// </summary>
    public bool UseDr { get; init; } = true;

    public required string PackageName { get; init; }
}

public sealed class PlannedFile
{
    public required string RelativePath { get; init; }
    public required long SizeBytes { get; init; }
}

public sealed class PackagePreview
{
    public required string PackageFolder { get; init; }
    public required IReadOnlyList<PlannedFile> Files { get; init; }
    public required string WinningLiveryName { get; init; }
    public required string ManifestJson { get; init; }
}
