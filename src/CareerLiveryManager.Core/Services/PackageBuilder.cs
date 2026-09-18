using System.Text.Json;
using CareerLiveryManager.Core.Models;

namespace CareerLiveryManager.Core.Services;

public sealed class PackageBuilder
{
    private const string CreatorTag = "CareerLiveryManager";
    private readonly LiveryCfgEditor _cfgEditor = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Builds the exact same package a call to <see cref="Apply"/> would produce,
    /// but only in memory - nothing is written to disk. Used for the confirmation screen.
    /// </summary>
    public PackagePreview Preview(ApplyLiveryRequest request, string communityPath)
    {
        var packageFolder = Path.Combine(communityPath, SanitizePackageName(request.PackageName));
        var winningName = WinningFolderName(request);

        var files = new List<PlannedFile>();
        CollectPlannedFiles(request, files);

        var manifest = BuildManifestJson(request.Aircraft.Title, request.Activity, request.Aircraft.SimObjectName);

        return new PackagePreview
        {
            PackageFolder = packageFolder,
            Files = files,
            WinningLiveryName = winningName,
            ManifestJson = manifest,
        };
    }

    /// <summary>
    /// Creates the Community package on disk. Never touches Official content or the
    /// original third-party livery folder - everything is copied into a brand new folder.
    /// </summary>
    public string Apply(ApplyLiveryRequest request, string communityPath)
    {
        var packageFolder = Path.Combine(communityPath, SanitizePackageName(request.PackageName));
        var vendorDestDir = Path.Combine(
            packageFolder, "simobjects", "airplanes", request.Aircraft.SimObjectName, "liveries", request.Aircraft.VendorName);

        Directory.CreateDirectory(vendorDestDir);

        var useDr = request.UseDr && request.Source.HasDr;

        if (request.Activity is { } activity)
        {
            // Newer recipe (see CAREER_LIVERY_RESEARCH.md section 19): the official folder name
            // itself is what Career keys off - either by exact path (the generic "official_static"
            // slot) or by [Tags]/[Specialization] inside livery.cfg (freelance activity slots).
            // No "!" prefix trick needed or wanted here.
            var winningDest = Path.Combine(vendorDestDir, activity.OfficialFolderName);
            string winningSourcePath;

            if (useDr)
            {
                var baseDest = Path.Combine(vendorDestDir, request.Source.BaseFolderName);
                CopyDirectoryRecursive(request.Source.BaseFolderPath, baseDest);
                CopyDirectoryRecursive(request.Source.DrFolderPath!, winningDest);
                winningSourcePath = request.Source.DrFolderPath!;

                var textureCfgPath = Directory.EnumerateFiles(winningDest, "texture.cfg", SearchOption.AllDirectories).FirstOrDefault();
                if (textureCfgPath is not null)
                {
                    _cfgEditor.FixDrFallback(textureCfgPath, request.Source.BaseFolderName);
                }
            }
            else
            {
                CopyDirectoryRecursive(request.Source.BaseFolderPath, winningDest);
                winningSourcePath = request.Source.BaseFolderPath;
            }

            if (!activity.IsGenericSlot)
            {
                var cfgPath = Path.Combine(winningDest, "livery.cfg");
                if (File.Exists(cfgPath))
                {
                    _cfgEditor.SetCareerActivityTags(cfgPath, activity.DressingCodes, activity.LicenceTag);
                }
            }

            CopyCrossSimObjectFallbackSibling(winningDest, winningSourcePath, request.Aircraft.SimObjectName, packageFolder);
        }
        else if (useDr)
        {
            // Base folder is kept WITHOUT "!" - it only exists so the DR variant's
            // texture.cfg fallback.1 can still find the real paint job.
            var baseDest = Path.Combine(vendorDestDir, request.Source.BaseFolderName);
            CopyDirectoryRecursive(request.Source.BaseFolderPath, baseDest);

            var drDestName = "!" + request.Source.DrFolderName;
            var drDest = Path.Combine(vendorDestDir, drDestName);
            CopyDirectoryRecursive(request.Source.DrFolderPath!, drDest);

            var drCfgPath = Path.Combine(drDest, "livery.cfg");
            if (File.Exists(drCfgPath))
            {
                _cfgEditor.SetLiveryName(drCfgPath, "!" + StripLeadingBang(request.Source.DrFolderName!));
            }

            var textureCfgPath = Directory.EnumerateFiles(drDest, "texture.cfg", SearchOption.AllDirectories).FirstOrDefault();
            if (textureCfgPath is not null)
            {
                _cfgEditor.FixDrFallback(textureCfgPath, request.Source.BaseFolderName);
            }
        }
        else
        {
            var destName = "!" + request.Source.BaseFolderName;
            var dest = Path.Combine(vendorDestDir, destName);
            CopyDirectoryRecursive(request.Source.BaseFolderPath, dest);

            var cfgPath = Path.Combine(dest, "livery.cfg");
            if (File.Exists(cfgPath))
            {
                _cfgEditor.SetLiveryName(cfgPath, "!" + StripLeadingBang(request.Source.BaseFolderName));
            }
        }

        File.WriteAllText(Path.Combine(packageFolder, "manifest.json"), BuildManifestJson(request.Aircraft.Title, request.Activity, request.Aircraft.SimObjectName));
        WriteLayoutJson(packageFolder);

        return packageFolder;
    }

    /// <summary>
    /// Some liveries (e.g. the Cessna 172 G1000 variant) ship only a partial texture set and rely
    /// on a texture.cfg fallback.1 pointing at a *different SimObject entirely* (the analog-gauge
    /// C172) for the rest of the paint job. Community packages don't automatically inherit that
    /// sibling from Official content, so without this the plane renders solid white in-game - see
    /// CAREER_LIVERY_RESEARCH.md section 17.6/18. This copies that sibling folder straight from
    /// the third-party source (never from Official content) if the fallback needs one.
    /// </summary>
    private void CopyCrossSimObjectFallbackSibling(string winningDestFolder, string winningSourceFolder, string currentSimObjectName, string packageFolder)
    {
        var textureCfgPath = Directory.EnumerateFiles(winningDestFolder, "texture.cfg", SearchOption.AllDirectories).FirstOrDefault();
        if (textureCfgPath is null)
        {
            return;
        }

        var fallback = _cfgEditor.ReadFallback1(textureCfgPath);
        if (string.IsNullOrWhiteSpace(fallback))
        {
            return;
        }

        var segments = fallback.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        var relativeSegments = segments.SkipWhile(s => s == "..").ToArray();
        if (relativeSegments.Length < 4)
        {
            return; // not the "<simobject>\liveries\<vendor>\<name>\texture" shape we expect
        }

        var targetSimObject = relativeSegments[0];
        if (string.Equals(targetSimObject, currentSimObjectName, StringComparison.OrdinalIgnoreCase))
        {
            return; // same-SimObject fallback (e.g. a DR sibling) - already handled elsewhere
        }

        // Drop the trailing "texture" segment to get the sibling livery folder itself.
        var liveryRelativeSegments = relativeSegments[..^1];

        var airplanesRoot = FindSimObjectsAirplanesRoot(winningSourceFolder);
        if (airplanesRoot is null)
        {
            return;
        }

        var sourceSiblingPath = Path.Combine(airplanesRoot, Path.Combine(liveryRelativeSegments));
        if (!Directory.Exists(sourceSiblingPath))
        {
            return;
        }

        var destSiblingPath = Path.Combine(new[] { packageFolder, "simobjects", "airplanes" }.Concat(liveryRelativeSegments).ToArray());
        if (Directory.Exists(destSiblingPath))
        {
            return; // already copied (e.g. two activities sharing the same sibling in one package)
        }

        CopyDirectoryRecursive(sourceSiblingPath, destSiblingPath);
    }

    /// <summary>Walks up from a livery folder to find the "SimObjects/Airplanes" ancestor in the source package.</summary>
    private static string? FindSimObjectsAirplanesRoot(string liveryFolderPath)
    {
        var dir = new DirectoryInfo(liveryFolderPath);
        while (dir is not null)
        {
            if (string.Equals(dir.Name, "Airplanes", StringComparison.OrdinalIgnoreCase) &&
                dir.Parent is not null && string.Equals(dir.Parent.Name, "SimObjects", StringComparison.OrdinalIgnoreCase))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static string WinningFolderName(ApplyLiveryRequest request)
    {
        if (request.Activity is { } activity)
        {
            return activity.OfficialFolderName;
        }

        var useDr = request.UseDr && request.Source.HasDr;
        var name = useDr ? request.Source.DrFolderName! : request.Source.BaseFolderName;
        return "!" + name;
    }

    private static string StripLeadingBang(string name) => name.TrimStart('!');

    private void CollectPlannedFiles(ApplyLiveryRequest request, List<PlannedFile> files)
    {
        var useDr = request.UseDr && request.Source.HasDr;

        void AddFolder(string sourceFolder, string destFolderName)
        {
            foreach (var file in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
            {
                var relativeToSource = Path.GetRelativePath(sourceFolder, file);
                var relativePath = Path.Combine(
                    "simobjects", "airplanes", request.Aircraft.SimObjectName, "liveries",
                    request.Aircraft.VendorName, destFolderName, relativeToSource);
                files.Add(new PlannedFile
                {
                    RelativePath = relativePath.Replace(Path.DirectorySeparatorChar, '/'),
                    SizeBytes = new FileInfo(file).Length,
                });
            }
        }

        if (request.Activity is not null)
        {
            var destName = request.Activity.OfficialFolderName;
            if (useDr)
            {
                AddFolder(request.Source.BaseFolderPath, request.Source.BaseFolderName);
                AddFolder(request.Source.DrFolderPath!, destName);
            }
            else
            {
                AddFolder(request.Source.BaseFolderPath, destName);
            }
        }
        else if (useDr)
        {
            AddFolder(request.Source.BaseFolderPath, request.Source.BaseFolderName);
            AddFolder(request.Source.DrFolderPath!, "!" + request.Source.DrFolderName);
        }
        else
        {
            AddFolder(request.Source.BaseFolderPath, "!" + request.Source.BaseFolderName);
        }
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var dir in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, dir);
            Directory.CreateDirectory(Path.Combine(destDir, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var destFile = Path.Combine(destDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            File.Copy(file, destFile, overwrite: true);
        }
    }

    private static string BuildManifestJson(string aircraftTitle, AircraftActivityInfo? activity, string? simObjectName)
    {
        var titleSuffix = activity is null ? string.Empty : $" - {activity.DisplayName}";
        var manifest = new
        {
            dependencies = Array.Empty<object>(),
            content_type = "LIVERY",
            title = $"Career Livery - {aircraftTitle}{titleSuffix}",
            manufacturer = "",
            creator = CreatorTag,
            package_version = "1.0.0",
            minimum_game_version = "1.0.67",
            minimum_compatibility_version = "6.0.0.113",
            export_type = "Community",
            builder = "Microsoft Flight Simulator 2024",
            package_order_hint = "SIMOBJECTS_PATCH",
            release_notes = new { neutral = new { LastUpdate = "", OlderHistory = "" } },
            // Extra fields InstalledPackagesManager reads back to know exactly which SimObject/
            // activity slot this package targets, since folder-name enumeration alone becomes
            // ambiguous once a cross-SimObject fallback sibling (see CopyCrossSimObjectFallbackSibling)
            // is present alongside the winning livery folder.
            career_simobject = simObjectName ?? "",
            career_activity = activity?.ActivityKey ?? "",
            career_activity_display = activity?.DisplayName ?? "",
            career_activity_folder = activity?.OfficialFolderName ?? "",
        };

        return JsonSerializer.Serialize(manifest, JsonOptions);
    }

    private static void WriteLayoutJson(string packageFolder)
    {
        var simobjectsDir = Path.Combine(packageFolder, "simobjects");
        var content = new List<object>();

        foreach (var file in Directory.EnumerateFiles(simobjectsDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(packageFolder, file).Replace(Path.DirectorySeparatorChar, '/');
            var info = new FileInfo(file);

            content.Add(new
            {
                path = relative,
                size = info.Length,
                date = ToWindowsFileTime(info.LastWriteTimeUtc),
            });
        }

        var layout = new { content };
        File.WriteAllText(Path.Combine(packageFolder, "layout.json"), JsonSerializer.Serialize(layout, JsonOptions));
    }

    private static long ToWindowsFileTime(DateTime utcTime)
    {
        var unixSeconds = new DateTimeOffset(utcTime).ToUnixTimeSeconds();
        return (unixSeconds + 11644473600L) * 10000000L;
    }

    private static string SanitizePackageName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '-' : c).ToArray();
        return new string(chars);
    }
}
