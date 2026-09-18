using System.Text.Json;
using CareerLiveryManager.Core.Models;

namespace CareerLiveryManager.Core.Services;

public sealed class InstalledPackagesManager
{
    private const string CreatorTag = "CareerLiveryManager";

    /// <summary>
    /// Lists only the Community packages this app created (identified by
    /// manifest.json's "creator" field) - never touches third-party packages.
    /// </summary>
    public IReadOnlyList<InstalledPackageInfo> List(string communityPath)
    {
        var result = new List<InstalledPackageInfo>();

        if (!Directory.Exists(communityPath))
        {
            return result;
        }

        foreach (var packageFolder in Directory.EnumerateDirectories(communityPath))
        {
            var manifestPath = Path.Combine(packageFolder, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
                var root = doc.RootElement;

                var creator = root.TryGetProperty("creator", out var creatorEl) ? creatorEl.GetString() : null;
                if (!string.Equals(creator, CreatorTag, StringComparison.Ordinal))
                {
                    continue;
                }

                var title = root.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : Path.GetFileName(packageFolder);
                var activityKey = GetString(root, "career_activity");
                var activityDisplay = GetString(root, "career_activity_display");
                var activityFolder = GetString(root, "career_activity_folder");

                // Older packages (pre-activity-system) don't have "career_simobject" saved -
                // fall back to the old folder-enumeration guess for those.
                var simObjectName = GetString(root, "career_simobject");
                if (string.IsNullOrEmpty(simObjectName))
                {
                    simObjectName = FindSimObjectName(packageFolder);
                }

                result.Add(new InstalledPackageInfo
                {
                    PackageFolder = packageFolder,
                    Title = title ?? Path.GetFileName(packageFolder),
                    AircraftSimObjectName = simObjectName,
                    ThumbnailPath = FindThumbnail(packageFolder, simObjectName, activityFolder),
                    ActivityKey = activityKey,
                    ActivityDisplayName = activityDisplay,
                });
            }
            catch (JsonException)
            {
                continue;
            }
        }

        return result;
    }

    public void Remove(string packageFolder)
    {
        if (Directory.Exists(packageFolder))
        {
            Directory.Delete(packageFolder, recursive: true);
        }
    }

    private static string GetString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var el) ? el.GetString() ?? string.Empty : string.Empty;

    private static string? FindThumbnail(string packageFolder, string simObjectName, string activityFolder)
    {
        // If we know exactly which SimObject/activity-slot folder is the "winning" one, look
        // there first - avoids picking up a cross-SimObject fallback sibling's own thumbnail
        // by mistake (see PackageBuilder.CopyCrossSimObjectFallbackSibling).
        if (!string.IsNullOrEmpty(simObjectName) && !string.IsNullOrEmpty(activityFolder))
        {
            var scopedDir = Path.Combine(packageFolder, "simobjects", "airplanes", simObjectName, "liveries");
            if (Directory.Exists(scopedDir))
            {
                var scoped = Directory.EnumerateFiles(scopedDir, "*", SearchOption.AllDirectories)
                    .Where(f => f.Contains($"{Path.DirectorySeparatorChar}{activityFolder}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                    .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                    .Where(f => Path.GetFileName(f).Contains("thumbnail", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f.Length)
                    .FirstOrDefault();

                if (scoped is not null)
                {
                    return scoped;
                }
            }
        }

        // Fallback (older packages, or the "!"-prefixed single-livery recipe): prefer a thumbnail
        // that belongs to the "!"-prefixed winning livery folder, since a DR setup also keeps an
        // un-prefixed base folder around.
        var all = Directory.EnumerateFiles(packageFolder, "thumbnail*.png", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(packageFolder, "thumbnail*.jpg", SearchOption.AllDirectories))
            .ToList();

        return all.FirstOrDefault(f => f.Contains($"{Path.DirectorySeparatorChar}!", StringComparison.Ordinal))
            ?? all.OrderBy(f => f.Length).FirstOrDefault();
    }

    private static string FindSimObjectName(string packageFolder)
    {
        var airplanesDir = Path.Combine(packageFolder, "simobjects", "airplanes");
        if (!Directory.Exists(airplanesDir))
        {
            return string.Empty;
        }

        return Directory.EnumerateDirectories(airplanesDir).Select(Path.GetFileName).FirstOrDefault() ?? string.Empty;
    }
}
