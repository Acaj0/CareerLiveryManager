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
        var winningName = request.UseDr && request.Source.HasDr
            ? request.Source.DrFolderName!
            : request.Source.BaseFolderName;

        var files = new List<PlannedFile>();
        CollectPlannedFiles(request, files);

        var manifest = BuildManifestJson(request.Aircraft.Title);

        return new PackagePreview
        {
            PackageFolder = packageFolder,
            Files = files,
            WinningLiveryName = "!" + winningName,
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

        if (useDr)
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

        File.WriteAllText(Path.Combine(packageFolder, "manifest.json"), BuildManifestJson(request.Aircraft.Title));
        WriteLayoutJson(packageFolder);

        return packageFolder;
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

        if (useDr)
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

    private static string BuildManifestJson(string aircraftTitle)
    {
        var manifest = new
        {
            dependencies = Array.Empty<object>(),
            content_type = "LIVERY",
            title = $"Career Livery - {aircraftTitle}",
            manufacturer = "",
            creator = CreatorTag,
            package_version = "1.0.0",
            minimum_game_version = "1.0.67",
            minimum_compatibility_version = "6.0.0.113",
            export_type = "Community",
            builder = "Microsoft Flight Simulator 2024",
            package_order_hint = "SIMOBJECTS_PATCH",
            release_notes = new { neutral = new { LastUpdate = "", OlderHistory = "" } },
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
