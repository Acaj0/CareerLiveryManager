using System.Security.Cryptography;
using System.Text.Json;
using CareerLiveryManager.Core.Models;

namespace CareerLiveryManager.Core.Services;

public sealed class AircraftScanner
{
    private readonly CareerActivityDetector _activityDetector = new();

    /// <summary>
    /// Scans every package folder under <paramref name="officialPath"/>, reads its
    /// manifest.json, and for every AIRCRAFT package tries to locate the
    /// "liveries/&lt;vendor&gt;" folder that Career draws its default livery from.
    /// </summary>
    public IReadOnlyList<AircraftInfo> ListAircraft(string officialPath)
    {
        var candidates = new List<Candidate>();

        foreach (var packageFolder in Directory.EnumerateDirectories(officialPath))
        {
            // "passiveaircraft" packages are simplified SimObjects used for AI traffic,
            // not the plane the player actually flies - skip them even though their
            // manifest also declares content_type "AIRCRAFT".
            if (Path.GetFileName(packageFolder).Contains("passiveaircraft", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var manifestPath = Path.Combine(packageFolder, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            string? title;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
                var root = doc.RootElement;

                if (!root.TryGetProperty("content_type", out var contentTypeEl) ||
                    !string.Equals(contentTypeEl.GetString(), "AIRCRAFT", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                title = root.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
            }
            catch (JsonException)
            {
                continue;
            }

            title ??= Path.GetFileName(packageFolder);

            var liveriesDir = FindLiveriesFolder(packageFolder);
            if (liveriesDir is null)
            {
                continue;
            }

            var simObjectName = Path.GetFileName(Path.GetDirectoryName(liveriesDir)!);

            foreach (var vendorDir in Directory.EnumerateDirectories(liveriesDir))
            {
                var vendorName = Path.GetFileName(vendorDir);
                var contentInfoThumbnail = FindContentInfoThumbnail(packageFolder);

                candidates.Add(new Candidate
                {
                    Title = title,
                    PackageFolder = packageFolder,
                    ManifestPath = manifestPath,
                    SimObjectName = simObjectName,
                    VendorDir = vendorDir,
                    VendorName = vendorName,
                    ContentInfoThumbnailPath = contentInfoThumbnail,
                    ContentInfoThumbnailHash = contentInfoThumbnail is null ? null : HashFile(contentInfoThumbnail),
                });
            }
        }

        // A real marketing photo is unique to its own aircraft. Several official packages instead
        // ship one of a handful of generic stand-in images (a gray "Placeholder" text card, the
        // MSFS anniversary logo, an untextured 3D render...) as their contentinfo thumbnail - and
        // because it's a stand-in, the exact same file turns up, byte-for-byte, on other aircraft
        // too. Any thumbnail whose hash repeats across more than one distinct SimObject is
        // therefore generic, not a real photo, however many different generic images MSFS ships -
        // no hardcoded list of "known placeholder" images needed.
        var genericHashes = candidates
            .Where(c => c.ContentInfoThumbnailHash is not null)
            .GroupBy(c => c.ContentInfoThumbnailHash, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(c => c.SimObjectName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(g => g.Key!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = new List<AircraftInfo>();

        foreach (var c in candidates)
        {
            var usableContentInfoThumbnail = c.ContentInfoThumbnailPath is not null && !genericHashes.Contains(c.ContentInfoThumbnailHash!)
                ? c.ContentInfoThumbnailPath
                : null;

            // NOTE: official liveries are always compiled (no loose livery.cfg), even for aircraft
            // that fully support the livery.cfg mechanism for third-party Community liveries.
            // Whether livery.cfg is actually supported can only be verified once the user picks a
            // livery source folder (see LiverySourceInspector) - not from the official package.
            var activities = _activityDetector.DetectActivities(c.VendorDir);

            result.Add(new AircraftInfo
            {
                Title = c.Title,
                PackageFolder = c.PackageFolder,
                ManifestPath = c.ManifestPath,
                SimObjectName = c.SimObjectName,
                VendorPath = c.VendorDir,
                VendorName = c.VendorName,
                IsSupported = true,
                ThumbnailPath = usableContentInfoThumbnail ?? FindLiveryFolderThumbnail(c.VendorDir),
                Activities = activities,
            });
        }

        return result;
    }

    private static string? FindLiveriesFolder(string packageFolder)
    {
        try
        {
            return Directory.EnumerateDirectories(packageFolder, "*", SearchOption.AllDirectories)
                .FirstOrDefault(dir => string.Equals(Path.GetFileName(dir), "liveries", StringComparison.OrdinalIgnoreCase));
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? FindContentInfoThumbnail(string packageFolder)
    {
        var contentInfoDir = Path.Combine(packageFolder, "contentinfo");
        if (!Directory.Exists(contentInfoDir))
        {
            return null;
        }

        return Directory.EnumerateFiles(contentInfoDir, "*.jpg", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(contentInfoDir, "*.png", SearchOption.AllDirectories))
            .FirstOrDefault();
    }

    /// <summary>
    /// Some aircraft (e.g. Cessna 172) ship no package-level marketing thumbnail under
    /// contentinfo/ at all, and others ship only a generic stand-in image shared with other
    /// aircraft - fall back to the thumbnail of the first official livery folder, so the
    /// aircraft list still shows a real picture either way.
    /// </summary>
    private static string? FindLiveryFolderThumbnail(string vendorDir)
    {
        return Directory.EnumerateDirectories(vendorDir)
            .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
            .Select(d => Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories)
                .FirstOrDefault(f =>
                    (f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)) &&
                    f.Contains("thumbnail", StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(t => t is not null);
    }

    private static string? HashFile(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (IOException)
        {
            return null;
        }
    }

    private sealed class Candidate
    {
        public required string Title { get; init; }
        public required string PackageFolder { get; init; }
        public required string ManifestPath { get; init; }
        public required string SimObjectName { get; init; }
        public required string VendorDir { get; init; }
        public required string VendorName { get; init; }
        public string? ContentInfoThumbnailPath { get; init; }
        public string? ContentInfoThumbnailHash { get; init; }
    }
}
