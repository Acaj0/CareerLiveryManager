namespace CareerLiveryManager.Core.Models;

/// <summary>
/// One Career "freelance" activity slot detected on a modular Asobo aircraft (e.g. Cargo Transport
/// on a Cessna 172), or the generic "official_static" slot that Career uses outside the
/// Freelance/Employee tag system (VIP/charter-style, no tag matching required).
/// </summary>
public sealed class AircraftActivityInfo
{
    /// <summary>Short key used to look up the activity, e.g. "cargo". Matches the folder name prefix.</summary>
    public required string ActivityKey { get; init; }

    /// <summary>Human-readable name for the UI, e.g. "Cargo Transport".</summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// The exact official livery folder name this activity resolves to, e.g. "cargo_freelance_01"
    /// or "official_static_01". A Community override must reuse this exact name (no "!" prefix)
    /// for freelance-tagged activities; the generic "official_static" slot is the one exception
    /// that still uses the older "!"-prefixed new-folder trick, since it isn't tag-matched at all.
    /// </summary>
    public required string OfficialFolderName { get; init; }

    /// <summary>True for the generic "official_static" slot (no [Tags]/dressing_codes needed to win it).</summary>
    public bool IsGenericSlot { get; init; }

    /// <summary>
    /// Value to write as [Specialization] dressing_codes in the override's livery.cfg.
    /// Empty for the generic slot, which doesn't check tags at all.
    /// </summary>
    public string DressingCodes { get; init; } = string.Empty;

    /// <summary>
    /// Value to write as the second [Tags] entry (after "Freelance") in the override's livery.cfg.
    /// Empty for the generic slot.
    /// </summary>
    public string LicenceTag { get; init; } = string.Empty;

    /// <summary>Path to the official (stock) thumbnail for this specific slot, if one was found.</summary>
    public string? DefaultThumbnailPath { get; init; }

    /// <summary>Set by the ViewModel when a Career Livery Manager package already targets this activity.</summary>
    public string? InstalledThumbnailPath { get; set; }

    public bool IsInstalled { get; set; }

    public string? DisplayThumbnailPath => InstalledThumbnailPath ?? DefaultThumbnailPath;
}
