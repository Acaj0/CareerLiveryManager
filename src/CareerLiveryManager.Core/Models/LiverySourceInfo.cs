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
}
