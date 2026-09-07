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
                var simObjectName = FindSimObjectName(packageFolder);

                result.Add(new InstalledPackageInfo
                {
                    PackageFolder = packageFolder,
                    Title = title ?? Path.GetFileName(packageFolder),
                    AircraftSimObjectName = simObjectName,
                    ThumbnailPath = FindThumbnail(packageFolder),
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

    private static string? FindThumbnail(string packageFolder)
    {
        // Prefer a thumbnail that belongs to the "!"-prefixed winning livery folder,
        // since a DR setup also keeps an un-prefixed base folder around.
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
