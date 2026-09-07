using CareerLiveryManager.Core.Models;

namespace CareerLiveryManager.Core.Services;

public sealed class LiverySourceInspector
{
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
