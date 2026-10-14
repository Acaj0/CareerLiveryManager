namespace CareerLiveryManager.Core.Models;

/// <summary>A creator or a recommended app shown on the Community screen. Clicking it opens <see cref="Url"/>.</summary>
public sealed record CommunityEntry(string Name, string Url, string ImageFile, string? Description = null);
