using CareerLiveryManager.Core.Models;

namespace CareerLiveryManager.Core.Services;

/// <summary>
/// BETA. A not-downloaded aircraft has no picture on disk, and the app doesn't ship the game's images.
/// So the picture of every downloaded aircraft is copied to the user's own app data while it is there,
/// and reused once the aircraft is gone. Failing that, a thumbnail of a Community livery for the same
/// aircraft is shown.
/// </summary>
public sealed class AircraftThumbnailCache
{
    private static readonly string[] Extensions = { ".png", ".jpg" };

    private readonly string _folder;

    public AircraftThumbnailCache()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CareerLiveryManager", "thumbnails"))
    {
    }

    public AircraftThumbnailCache(string folder) => _folder = folder;

    public void Remember(IEnumerable<AircraftInfo> installed)
    {
        foreach (var aircraft in installed)
        {
            var source = aircraft.ThumbnailPath;
            if (source is null)
            {
                continue;
            }

            try
            {
                var target = Path.Combine(_folder, aircraft.SimObjectName + Path.GetExtension(source).ToLowerInvariant());
                var from = new FileInfo(source);
                var to = new FileInfo(target);
                if (!from.Exists || (to.Exists && to.Length == from.Length))
                {
                    continue;
                }

                Directory.CreateDirectory(_folder);
                File.Copy(source, target, overwrite: true);
                File.SetAttributes(target, FileAttributes.Normal);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A missing picture is not worth interrupting the scan.
            }
        }
    }

    public string? Find(string simObjectName, string communityPath)
    {
        var cached = Extensions.Select(e => Path.Combine(_folder, simObjectName + e)).FirstOrDefault(File.Exists);
        return cached ?? FindCommunityThumbnail(simObjectName, communityPath);
    }

    private static string? FindCommunityThumbnail(string simObjectName, string communityPath)
    {
        if (!Directory.Exists(communityPath))
        {
            return null;
        }

        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var package in Directory.EnumerateDirectories(communityPath).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var liveries = Path.Combine(package, "SimObjects", "Airplanes", simObjectName, "liveries");
                if (!Directory.Exists(liveries))
                {
                    continue;
                }

                var thumbnail = Directory.EnumerateFiles(liveries, "thumbnail*", options)
                    .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                    .OrderBy(f => f.Contains("small", StringComparison.OrdinalIgnoreCase) || f.Contains("button", StringComparison.OrdinalIgnoreCase))
                    .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (thumbnail is not null)
                {
                    return thumbnail;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Try the next package.
            }
        }

        return null;
    }
}
