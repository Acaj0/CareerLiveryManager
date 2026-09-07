using System.Text.Json;
using CareerLiveryManager.Core.Models;

namespace CareerLiveryManager.Core.Services;

public sealed class AircraftScanner
{
    /// <summary>
    /// Scans every package folder under <paramref name="officialPath"/>, reads its
    /// manifest.json, and for every AIRCRAFT package tries to locate the
    /// "liveries/&lt;vendor&gt;" folder that Career draws its default livery from.
    /// </summary>
    public IReadOnlyList<AircraftInfo> ListAircraft(string officialPath)
    {
        var result = new List<AircraftInfo>();

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

                // NOTE: official liveries are always compiled (no loose livery.cfg),
                // even for aircraft that fully support the livery.cfg mechanism for
                // third-party Community liveries. Whether livery.cfg is actually
                // supported can only be verified once the user picks a livery source
                // folder (see LiverySourceInspector) - not from the official package.
                var isSupported = true;

                result.Add(new AircraftInfo
                {
                    Title = title,
                    PackageFolder = packageFolder,
                    ManifestPath = manifestPath,
                    SimObjectName = simObjectName,
                    VendorPath = vendorDir,
                    VendorName = vendorName,
                    IsSupported = isSupported,
                    ThumbnailPath = FindThumbnail(packageFolder),
                });
            }
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

    private static string? FindThumbnail(string packageFolder)
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
}
