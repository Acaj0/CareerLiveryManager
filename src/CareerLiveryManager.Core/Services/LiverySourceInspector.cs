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

        return results;
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
