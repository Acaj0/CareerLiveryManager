using System.Text.Json;
using CareerLiveryManager.Core.Models;

namespace CareerLiveryManager.Core.Services;

/// <summary>
/// Checks the Official and Community folder choices for the mistakes that make the app "apply"
/// something that never shows up in Career, and explains them. Pure file-system reads, no UI, so
/// the Setup screen, the aircraft list and the diagnostic report can all share it. See ROADMAP.md
/// item 1 for the rule table (S01-S10); S11 and S12 are the empty/missing-path checks that used
/// to live ad hoc in the Setup screen.
///
/// Only manifest.json files directly inside the Official folder are read, never SimObjects.
/// </summary>
public sealed class SetupValidator
{
    private const string CreatorTag = "CareerLiveryManager";

    /// <summary>Below this many detected aircraft, an install with a StreamedPackages folder most
    /// likely just hasn't downloaded its aircraft yet (S10).</summary>
    public const int LowAircraftCount = 5;

    private static readonly string[] StorefrontFolderNames = ["Steam", "OneStore"];

    /// <summary>Tries to create and delete a file in a folder; returns the OS error text, or null
    /// when writable. A seam so tests can simulate a read-only Community folder.</summary>
    internal Func<string, string?> WriteProbe { get; init; } = TryWriteProbe;

    /// <summary>
    /// Folder-choice rules S01-S08, S11, S12. Errors first, then warnings, then info, each group in
    /// rule order. <paramref name="userCfg"/> is what <see cref="MsfsPathDetector.ReadUserCfg"/>
    /// found, or null when it wasn't found/readable (S06 is then skipped).
    /// </summary>
    public IReadOnlyList<SetupIssue> Validate(string? officialPath, string? communityPath, UserCfgInfo? userCfg = null)
    {
        var issues = new List<SetupIssue>();

        var official = Normalize(officialPath, out var officialInvalid);
        var community = Normalize(communityPath, out var communityInvalid);

        var officialExists = false;
        if (official is null)
        {
            issues.Add(new SetupIssue("S11", SetupIssueSeverity.Error, officialInvalid
                ? "The Official folder path isn't a valid Windows path."
                : "Please choose your Official content folder."));
        }
        else if (!Directory.Exists(official))
        {
            issues.Add(new SetupIssue("S11", SetupIssueSeverity.Error,
                $"The Official folder '{official}' doesn't exist. Choose the Official2024\\Steam or Official2024\\OneStore folder of your MSFS 2024 install."));
        }
        else
        {
            officialExists = true;
        }

        if (community is null)
        {
            issues.Add(new SetupIssue("S12", SetupIssueSeverity.Error, communityInvalid
                ? "The Community folder path isn't a valid Windows path."
                : "Please choose a Community folder."));
        }

        if (official is not null && community is not null)
        {
            if (SamePath(official, community))
            {
                issues.Add(new SetupIssue("S01", SetupIssueSeverity.Error,
                    "Official and Community point to the same folder. Official is where MSFS keeps its own aircraft; " +
                    "Community is where this app installs liveries. They must be two different folders."));
            }
            else if (IsInside(official, community) || IsInside(community, official))
            {
                issues.Add(new SetupIssue("S02", SetupIssueSeverity.Error,
                    "The Official and Community folders are nested (one is inside the other). They must be two separate folders."));
            }
        }

        if (officialExists)
        {
            AddOfficialContentIssues(official!, issues);
        }

        if (userCfg is not null && official is not null && community is not null)
        {
            AddUserCfgMismatchIssue(official, community, userCfg, issues);
        }

        if (community is not null && Directory.Exists(community))
        {
            var writeError = WriteProbe(community);
            if (writeError is not null)
            {
                issues.Add(new SetupIssue("S07", SetupIssueSeverity.Error,
                    $"The Community folder isn't writable, so the app can't install liveries there: {writeError}"));
            }
        }

        AddMsfs2020MixUpIssue(official, community, issues);

        return Order(issues);
    }

    /// <summary>
    /// Rules that need the scan result (S09, S10). Info only: they explain a result that looks
    /// like a bug but isn't.
    /// </summary>
    public IReadOnlyList<SetupIssue> ValidateScanResults(string officialPath, IReadOnlyList<AircraftInfo> aircraft)
    {
        var issues = new List<SetupIssue>();

        if (aircraft.Count > 0 && aircraft.All(a => !a.HasActivities))
        {
            issues.Add(new SetupIssue("S09", SetupIssueSeverity.Info,
                "None of your aircraft has Career activities. That's normal for some installs: only aircraft with activities " +
                "(Cargo, Flightseeing, ...) get a livery per job; every other aircraft gets one livery for the whole plane."));
        }

        var official = Normalize(officialPath, out _);
        if (official is not null && aircraft.Count < LowAircraftCount && HasStreamedPackagesNextToIt(official))
        {
            issues.Add(new SetupIssue("S10", SetupIssueSeverity.Info,
                $"Only {aircraft.Count} aircraft were found, and your install has a StreamedPackages folder. MSFS streams aircraft on demand: " +
                "download each aircraft fully in the in-game Content Manager, or it will look missing here."));
        }

        return Order(issues);
    }

    // ---- Official folder content (S03, S04, S05) --------------------------------------------

    private static void AddOfficialContentIssues(string official, List<SetupIssue> issues)
    {
        var content = InspectOfficial(official);
        if (content.HasAircraft)
        {
            return;
        }

        // Never suggest stepping into a MSFS 2020 folder: that "fix" would swap one mix-up (S08) for another.
        if (content.PackageCount == 0 && FindMsfs2020Marker(official) is null)
        {
            // The usual slip: choosing Official2024 itself instead of its Steam/OneStore subfolder.
            var child = StorefrontFolderNames
                .Select(name => Path.Combine(official, name))
                .FirstOrDefault(path => Directory.Exists(path) && InspectOfficial(path).HasAircraft);

            if (child is not null)
            {
                issues.Add(new SetupIssue("S03", SetupIssueSeverity.Error,
                    $"This is one level too high: MSFS's aircraft are inside its '{Path.GetFileName(child)}' subfolder.",
                    new SetupFix($"Use '{child}'", child, null)));
                return;
            }
        }

        if (content.PackageCount > 0 && content.AllCreatedByThisAppOrLiveries)
        {
            issues.Add(new SetupIssue("S05", SetupIssueSeverity.Error,
                "This folder only contains liveries (packages made by this app or other livery add-ons), not MSFS's own aircraft. " +
                "The Official folder is the one MSFS itself installs its content to (Official2024\\Steam or Official2024\\OneStore). " +
                "Never copy liveries into it."));
            return;
        }

        issues.Add(new SetupIssue("S04", SetupIssueSeverity.Error,
            "No official aircraft packages were found in this folder. Check that it's the Official2024\\Steam or Official2024\\OneStore folder, " +
            "and that the aircraft are downloaded in-game first (streamed aircraft that aren't downloaded won't appear). " +
            "On Game Pass / the Xbox app, try \"Detect automatically\" or see the setup guide."));
    }

    private readonly record struct OfficialContent(int PackageCount, bool HasAircraft, bool AllCreatedByThisAppOrLiveries);

    /// <summary>
    /// Reads manifest.json of each direct subfolder, stopping as soon as a real aircraft package is
    /// found (so a healthy install costs a handful of reads, not hundreds).
    /// </summary>
    private static OfficialContent InspectOfficial(string folder)
    {
        var packageCount = 0;
        var allLiveries = true;

        IEnumerable<string> packageFolders;
        try
        {
            packageFolders = Directory.EnumerateDirectories(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new OfficialContent(0, false, false);
        }

        foreach (var packageFolder in packageFolders)
        {
            var manifestPath = Path.Combine(packageFolder, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            packageCount++;

            string? contentType = null;
            string? creator = null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
                contentType = ReadString(doc.RootElement, "content_type");
                creator = ReadString(doc.RootElement, "creator");
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                allLiveries = false; // unreadable: can't call it a livery, so don't blame the user for it
                continue;
            }

            var isAircraft = string.Equals(contentType, "AIRCRAFT", StringComparison.OrdinalIgnoreCase) &&
                             !Path.GetFileName(packageFolder).Contains("passiveaircraft", StringComparison.OrdinalIgnoreCase);
            if (isAircraft)
            {
                return new OfficialContent(packageCount, true, false);
            }

            var isLivery = string.Equals(contentType, "LIVERY", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(creator, CreatorTag, StringComparison.Ordinal);
            if (!isLivery)
            {
                allLiveries = false;
            }
        }

        return new OfficialContent(packageCount, false, packageCount > 0 && allLiveries);
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    // ---- What MSFS itself uses (S06) ----------------------------------------------------------

    private static void AddUserCfgMismatchIssue(string official, string community, UserCfgInfo userCfg, List<SetupIssue> issues)
    {
        var root = Normalize(userCfg.InstalledPackagesPath, out _);
        if (root is null)
        {
            return;
        }

        var expectedCommunity = Path.Combine(root, "Community");
        var expectedOfficialRoot = Path.Combine(root, "Official2024");

        var communityDiffers = !SamePath(community, expectedCommunity);
        var officialDiffers = !(SamePath(official, expectedOfficialRoot) || IsInside(official, expectedOfficialRoot));
        if (!communityDiffers && !officialDiffers)
        {
            return;
        }

        string? suggestedOfficial = null;
        if (officialDiffers && Directory.Exists(expectedOfficialRoot))
        {
            suggestedOfficial = StorefrontFolderNames
                .Select(name => Path.Combine(expectedOfficialRoot, name))
                .FirstOrDefault(path => Directory.Exists(path) && InspectOfficial(path).HasAircraft);
        }

        var differs = communityDiffers && officialDiffers ? "Official and Community folders" : communityDiffers ? "Community folder" : "Official folder";
        var message = $"MSFS itself is set up to use '{root}' for its packages (read from its UserCfg.opt), but your {differs} " +
                      "point somewhere else. MSFS only loads liveries from its own Community folder, so a livery installed elsewhere won't appear in-game. " +
                      "This also happens after moving the game to another drive.";

        SetupFix? fix = null;
        var fixOfficial = suggestedOfficial;
        var fixCommunity = communityDiffers ? expectedCommunity : null;
        if (fixOfficial is not null || fixCommunity is not null)
        {
            fix = new SetupFix("Use the folders MSFS itself uses", fixOfficial, fixCommunity);
        }

        issues.Add(new SetupIssue("S06", SetupIssueSeverity.Warning, message, fix));
    }

    // ---- MSFS 2020 mix-up (S08) ---------------------------------------------------------------

    private static readonly string[] Msfs2020FolderNames =
    [
        "Official2020",
        "Official",                                  // MSFS 2020's own name for it (2024 uses Official2024)
        "Microsoft Flight Simulator",                // 2020's AppData folder (2024 adds " 2024")
        "Microsoft.FlightSimulator_8wekyb3d8bbwe",   // 2020's Microsoft Store package family
    ];

    /// <summary>The first path segment that only exists in an MSFS 2020 install, or null.</summary>
    private static string? FindMsfs2020Marker(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .FirstOrDefault(segment => Msfs2020FolderNames.Contains(segment, StringComparer.OrdinalIgnoreCase));

    private static void AddMsfs2020MixUpIssue(string? official, string? community, List<SetupIssue> issues)
    {
        foreach (var path in new[] { official, community })
        {
            if (path is null)
            {
                continue;
            }

            var marker = FindMsfs2020Marker(path);
            if (marker is not null)
            {
                issues.Add(new SetupIssue("S08", SetupIssueSeverity.Warning,
                    $"This path contains '{marker}', which belongs to Microsoft Flight Simulator 2020. This app is for MSFS 2024: " +
                    "use the Official2024 folder and the Community folder of your 2024 install."));
                return;
            }
        }
    }

    // ---- StreamedPackages (S10) ---------------------------------------------------------------

    private static bool HasStreamedPackagesNextToIt(string official)
    {
        // ...\Official2024\Steam: StreamedPackages sits next to Official2024, one level up.
        var parent = Directory.GetParent(official);
        var grandParent = parent?.Parent;
        return (parent is not null && Directory.Exists(Path.Combine(parent.FullName, "StreamedPackages"))) ||
               (grandParent is not null && Directory.Exists(Path.Combine(grandParent.FullName, "StreamedPackages")));
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static IReadOnlyList<SetupIssue> Order(List<SetupIssue> issues) => issues
        .OrderByDescending(i => i.Severity)
        .ThenBy(i => i.Id, StringComparer.Ordinal)
        .ToList();

    /// <summary>Full path without a trailing separator, with a directory link/junction at the leaf
    /// resolved to its target so two spellings of one folder compare equal. Null for empty input;
    /// <paramref name="invalid"/> is true when the text isn't a valid path at all.</summary>
    private static string? Normalize(string? path, out bool invalid)
    {
        invalid = false;
        var trimmed = path?.Trim().Trim('"').Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        // .NET's GetFullPath accepts most characters Windows can't actually use in a path, so
        // reject them here instead of letting the failure surface later as a confusing IO error.
        // The "\\?\" long-path prefix legitimately contains a '?'.
        var withoutLongPathPrefix = trimmed.StartsWith(@"\\?\", StringComparison.Ordinal) ? trimmed[4..] : trimmed;
        if (withoutLongPathPrefix.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || withoutLongPathPrefix.IndexOfAny(WindowsForbiddenPathChars) >= 0)
        {
            invalid = true;
            return null;
        }

        string full;
        try
        {
            full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmed));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            invalid = true;
            return null;
        }

        try
        {
            if (Directory.Exists(full) && new DirectoryInfo(full).ResolveLinkTarget(returnFinalTarget: true) is { } target)
            {
                full = Path.TrimEndingDirectorySeparator(target.FullName);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // keep the unresolved path
        }

        return full;
    }

    // Path.GetInvalidPathChars() on .NET 8 only lists '|' and control characters, not the rest
    // Windows forbids in a path.
    private static readonly char[] WindowsForbiddenPathChars = ['<', '>', '"', '|', '?', '*'];

    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool IsInside(string child, string parent) =>
        child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>Creates and deletes a tiny file. Returns the OS error text, or null when writable.</summary>
    private static string? TryWriteProbe(string folder)
    {
        var probe = Path.Combine(folder, $".clm-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return null;
        }
        catch (Exception ex)
        {
            try
            {
                File.Delete(probe);
            }
            catch
            {
                // nothing more to do
            }

            return ex.Message;
        }
    }
}
