namespace CareerLiveryManager.Core.Models;

/// <summary>
/// A livery folder pair found inside a user-supplied source folder: the base
/// (fixed registration) variant and, optionally, its "_DR" (Dynamic Registration) sibling.
/// </summary>
public sealed class LiverySourceInfo
{
    public required string BaseFolderPath { get; init; }
    public required string BaseFolderName { get; init; }
    public string? DrFolderPath { get; init; }
    public string? DrFolderName { get; init; }
    public bool HasDr => DrFolderPath is not null;
    public required string DetectedSimObjectName { get; init; }
    public string? ThumbnailPath { get; init; }

    /// <summary>
    /// Other aircraft versions of the same livery that must be installed together with this one
    /// (the Citation X's "winglets" folders, which borrow their paint from the standard ones).
    /// Empty for every other livery.
    /// </summary>
    public IReadOnlyList<LiverySourceVariant> Variants { get; init; } = Array.Empty<LiverySourceVariant>();
}

/// <summary>One extra base/DR folder pair of a livery that comes in several aircraft versions.</summary>
public sealed class LiverySourceVariant
{
    public required string BaseFolderPath { get; init; }
    public required string BaseFolderName { get; init; }
    public string? DrFolderPath { get; init; }
    public string? DrFolderName { get; init; }
    public bool HasDr => DrFolderPath is not null;
}
