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

    /// <summary>
    /// The Career activity being customized (Cargo Transport, VIP/Charter, etc.), or null for
    /// single-livery aircraft (Longitude, CJ4, A321...) using the original "!"-prefixed recipe.
    /// When set, PackageBuilder overwrites the exact official slot folder instead - see
    /// CAREER_LIVERY_RESEARCH.md section 19.
    /// </summary>
    public AircraftActivityInfo? Activity { get; init; }
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

    public int FileCount => Files.Count;

    /// <summary>Sum of the planned files' sizes: what the copy will add to the Community folder.</summary>
    public long TotalBytes => Files.Sum(f => f.SizeBytes);
}

/// <summary>
/// Result of <see cref="Services.PackageBuilder.Apply"/>. <see cref="Notes"/> records any
/// non-default recipe steps PackageBuilder took (a 737 MAX part backfilled from Official, a
/// same-vendor "_common" sibling copied, a cross-SimObject fallback sibling copied...) so a bug
/// report's log line can show exactly what happened, without needing to re-extract the livery zip
/// to find out - see CAREER_LIVERY_RESEARCH.md section 21/22 for the cases this covers.
/// </summary>
public sealed class ApplyResult
{
    public required string PackageFolder { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
}
