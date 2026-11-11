using CareerLiveryManager.Core.Models;

namespace CareerLiveryManager.Core.Services;

public enum LiverySourceProblemKind
{
    FolderMissing,
    NotAFolder,
    Archive,
    LegacyAircraftCfg,
    SimObjectsWithoutLiveryCfg,
    NoLiveryFound,
}

/// <summary>Why a chosen folder has no usable livery, in words a person can act on.</summary>
public sealed record LiverySourceProblem(LiverySourceProblemKind Kind, string Message);

public sealed class LiverySourceInspector
{
    private static readonly string[] ArchiveExtensions = [".zip", ".rar", ".7z"];

    /// <summary>
    /// Looks inside a user-supplied livery source folder for livery.cfg files and
    /// pairs up a base variant with its "_DR" (Dynamic Registration) sibling, if any.
    /// </summary>
    public IReadOnlyList<LiverySourceInfo> Inspect(string folderPath)
    {
        var cfgFiles = Directory.EnumerateFiles(folderPath, "livery.cfg", SearchOption.AllDirectories).ToList();
        var liveryFolders = cfgFiles.Select(f => Path.GetDirectoryName(f)!).Distinct().ToList();

        var drFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<LiverySourceInfo>();

        foreach (var folder in liveryFolders)
        {
            var name = Path.GetFileName(folder)!;
            if (name.EndsWith("_DR", StringComparison.OrdinalIgnoreCase))
            {
                drFolders.Add(folder);
            }
        }

        foreach (var folder in liveryFolders)
        {
            if (drFolders.Contains(folder))
            {
                continue; // handled as a pair below, alongside its base folder
            }

            var name = Path.GetFileName(folder)!;
            var parent = Path.GetDirectoryName(folder)!;
            var drCandidate = Path.Combine(parent, name + "_DR");
            var drFolder = liveryFolders.FirstOrDefault(f => string.Equals(f, drCandidate, StringComparison.OrdinalIgnoreCase));

            results.Add(new LiverySourceInfo
            {
                BaseFolderPath = folder,
                BaseFolderName = name,
                DrFolderPath = drFolder,
                DrFolderName = drFolder is null ? null : Path.GetFileName(drFolder),
                DetectedSimObjectName = DetectSimObjectName(folder),
                ThumbnailPath = FindThumbnail(folder),
            });
        }

        return GroupCitationXVersions(results);
    }

    private const string CitationXSimObjectName = "c750";

    /// <summary>
    /// The Citation X comes in two aircraft versions (standard and winglets), and a livery for it ships
    /// one folder pair per version, told apart by "required_tags" in livery.cfg. The winglets folders
    /// hold no paint of their own: their texture.cfg borrows it from the standard folders, so the two
    /// pairs only work together. Each winglets pair is attached to the standard pair it borrows from
    /// (found by the folder name in its texture.cfg fallbacks, or by the usual "C750w_" / "C750_"
    /// naming) and listed as one livery. Anything that doesn't fit this pattern - and every other
    /// aircraft - is returned untouched.
    /// </summary>
    private static IReadOnlyList<LiverySourceInfo> GroupCitationXVersions(List<LiverySourceInfo> entries)
    {
        if (!entries.Any(e => string.Equals(e.DetectedSimObjectName, CitationXSimObjectName, StringComparison.OrdinalIgnoreCase)))
        {
            return entries;
        }

        var cfg = new LiveryCfgEditor();
        bool IsCitationX(LiverySourceInfo e) => string.Equals(e.DetectedSimObjectName, CitationXSimObjectName, StringComparison.OrdinalIgnoreCase);
        bool HasTag(LiverySourceInfo e, string tag)
        {
            var path = Path.Combine(e.BaseFolderPath, "livery.cfg");
            var tags = File.Exists(path) ? cfg.ReadRequiredTags(path) : null;
            return tags is not null && tags.Contains(tag, StringComparison.OrdinalIgnoreCase);
        }

        var winglets = entries.Where(e => IsCitationX(e) && HasTag(e, "winglets") && !HasTag(e, "standard")).ToList();
        var standards = entries.Where(e => IsCitationX(e) && HasTag(e, "standard") && !HasTag(e, "winglets")).ToList();
        if (winglets.Count == 0 || standards.Count == 0)
        {
            return entries;
        }

        var attached = new Dictionary<LiverySourceInfo, List<LiverySourceVariant>>();
        var consumed = new HashSet<LiverySourceInfo>();

        foreach (var w in winglets)
        {
            var owner = standards.FirstOrDefault(s => BorrowsFrom(w, s, cfg)) ?? standards.FirstOrDefault(s => SameLiveryByName(w, s));
            if (owner is null)
            {
                continue;
            }

            if (!attached.TryGetValue(owner, out var list))
            {
                attached[owner] = list = new List<LiverySourceVariant>();
            }

            list.Add(new LiverySourceVariant
            {
                BaseFolderPath = w.BaseFolderPath,
                BaseFolderName = w.BaseFolderName,
                DrFolderPath = w.DrFolderPath,
                DrFolderName = w.DrFolderName,
            });
            consumed.Add(w);
        }

        var result = new List<LiverySourceInfo>();
        foreach (var entry in entries)
        {
            if (consumed.Contains(entry))
            {
                continue;
            }

            result.Add(attached.TryGetValue(entry, out var variants)
                ? new LiverySourceInfo
                {
                    BaseFolderPath = entry.BaseFolderPath,
                    BaseFolderName = entry.BaseFolderName,
                    DrFolderPath = entry.DrFolderPath,
                    DrFolderName = entry.DrFolderName,
                    DetectedSimObjectName = entry.DetectedSimObjectName,
                    ThumbnailPath = entry.ThumbnailPath,
                    Variants = variants,
                }
                : entry);
        }

        return result;
    }

    /// <summary>True when any texture.cfg of the winglets pair names one of the standard pair's folders in a fallback.</summary>
    private static bool BorrowsFrom(LiverySourceInfo winglets, LiverySourceInfo standard, LiveryCfgEditor cfg)
    {
        var wanted = new[] { standard.BaseFolderName, standard.DrFolderName }.Where(n => n is not null).ToList();
        var folders = new[] { winglets.BaseFolderPath, winglets.DrFolderPath }.Where(p => p is not null);

        foreach (var folder in folders)
        {
            foreach (var textureCfg in Directory.EnumerateFiles(folder!, "texture.cfg", SearchOption.AllDirectories))
            {
                foreach (var fallback in cfg.ReadAllFallbacks(textureCfg))
                {
                    var segments = fallback.Split('\\', '/');
                    if (wanted.Any(n => segments.Contains(n, StringComparer.OrdinalIgnoreCase)))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>"C750w_N728QL" belongs with "C750_N728QL": the same names with a "w" after the prefix.</summary>
    private static bool SameLiveryByName(LiverySourceInfo winglets, LiverySourceInfo standard)
    {
        var match = System.Text.RegularExpressions.Regex.Match(winglets.BaseFolderName, @"^(?<prefix>[^_]*?)w_(?<rest>.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success &&
               string.Equals(standard.BaseFolderName, match.Groups["prefix"].Value + "_" + match.Groups["rest"].Value, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Explains, for a path where <see cref="Inspect"/> found nothing (or that can't be inspected at
    /// all), the most likely reason: a compressed download that wasn't extracted, an old-format
    /// livery, the wrong folder level... Call it only after Inspect came back empty.
    /// </summary>
    public LiverySourceProblem Diagnose(string path)
    {
        if (File.Exists(path))
        {
            return IsArchive(path)
                ? FileArchiveProblem(path)
                : new LiverySourceProblem(LiverySourceProblemKind.NotAFolder,
                    "That's a file, not a folder. Choose the folder you got after extracting the livery download.");
        }

        if (!Directory.Exists(path))
        {
            return new LiverySourceProblem(LiverySourceProblemKind.FolderMissing, "That folder doesn't exist (anymore). Choose it again.");
        }

        var everything = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };

        if (Directory.EnumerateFiles(path, "aircraft.cfg", everything).Any())
        {
            return new LiverySourceProblem(LiverySourceProblemKind.LegacyAircraftCfg,
                "This livery uses the old FSX/P3D format (an aircraft.cfg with no livery.cfg), which this tool can't use yet. " +
                "Look for a version made for MSFS 2020/2024: it has a livery.cfg inside SimObjects\\Airplanes\\...");
        }

        var archives = Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly).Where(IsArchive).Select(f => Path.GetFileName(f)!).Take(3).ToList();
        if (archives.Count > 0)
        {
            return ArchiveProblem(archives);
        }

        if (Directory.EnumerateDirectories(path, "SimObjects", everything).Any())
        {
            return new LiverySourceProblem(LiverySourceProblemKind.SimObjectsWithoutLiveryCfg,
                "This folder has a SimObjects folder, but no livery.cfg inside it, so it isn't a livery this tool supports.");
        }

        return new LiverySourceProblem(LiverySourceProblemKind.NoLiveryFound,
            "No livery.cfg was found in that folder. Choose the folder that contains SimObjects (the one you get after extracting the download).");
    }

    /// <summary>
    /// A warning when the livery's own aircraft doesn't match the aircraft being customised, or null
    /// when it matches. A livery outside a "SimObjects\Airplanes\&lt;aircraft&gt;" path has no detectable aircraft.
    /// </summary>
    public static string? DescribeAircraftMismatch(string detectedSimObjectName, string expectedSimObjectName)
    {
        if (string.Equals(detectedSimObjectName, expectedSimObjectName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(detectedSimObjectName)
            ? $"Couldn't tell which aircraft this livery is for (it isn't inside a SimObjects\\Airplanes\\<aircraft> folder), so it can't be checked against {expectedSimObjectName}. Apply it only if it was made for this aircraft."
            : $"Warning: this livery looks like it belongs to a different aircraft ({detectedSimObjectName}), not {expectedSimObjectName}. It may not work correctly.";
    }

    /// <summary>A single archive file was chosen but couldn't be handled (any .rar/.7z, or a zip the app can't open).</summary>
    private static LiverySourceProblem FileArchiveProblem(string path) =>
        new(LiverySourceProblemKind.Archive,
            $"{Path.GetFileName(path)} couldn't be opened by the app. Extract it with 7-Zip or WinRAR, then choose (or drop) the extracted folder.");

    /// <summary>The chosen folder holds archives instead of a livery.</summary>
    private static LiverySourceProblem ArchiveProblem(IReadOnlyList<string> names)
    {
        var list = string.Join(", ", names);
        return new LiverySourceProblem(LiverySourceProblemKind.Archive,
            $"This folder has compressed files ({list}), not an extracted livery. Drop the archive itself onto this window and the app extracts it for you.");
    }

    private static bool IsArchive(string path) =>
        ArchiveExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static string? FindThumbnail(string liveryFolderPath)
    {
        return Directory.EnumerateFiles(liveryFolderPath, "thumbnail*.png", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(liveryFolderPath, "thumbnail*.jpg", SearchOption.AllDirectories))
            .OrderBy(f => f.Length) // prefer the shortest match, usually "thumbnail.png" over "thumbnail_small.png"
            .FirstOrDefault();
    }

    private static string DetectSimObjectName(string liveryFolderPath)
    {
        // Expected shape: .../SimObjects/Airplanes/<simobject>/liveries/<creator>/<liveryName>
        var parts = liveryFolderPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var airplanesIndex = Array.FindIndex(parts,
            p => string.Equals(p, "airplanes", StringComparison.OrdinalIgnoreCase));

        return airplanesIndex >= 0 && airplanesIndex + 1 < parts.Length
            ? parts[airplanesIndex + 1]
            : string.Empty;
    }
}
