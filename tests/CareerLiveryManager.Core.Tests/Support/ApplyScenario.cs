using CareerLiveryManager.Core.Models;
using CareerLiveryManager.Core.Services;

namespace CareerLiveryManager.Core.Tests.Support;

/// <summary>
/// One fake world for one aircraft: an "Official" aircraft package, a third-party livery download
/// and an empty Community folder, all inside a <see cref="TestTree"/>. Tests add the files a
/// scenario needs, call <see cref="Apply"/> and look at what landed in Community.
/// </summary>
internal sealed class ApplyScenario : IDisposable
{
    public const string VendorName = "asobo";
    public const string DefaultPackageName = "career-livery-test";

    public TestTree Tree { get; } = new();
    public string SimObject { get; }

    public ApplyScenario(string simObject)
    {
        SimObject = simObject;
        Directory.CreateDirectory(VendorPath);
        Directory.CreateDirectory(Community);
    }

    public string Community => Tree.At("Community");
    public string OfficialPackage => Tree.At("Official", "official-pkg");
    public string OfficialRoot => Tree.At("Official");
    public string VendorPath => Path.Combine(OfficialPackage, "simobjects", "airplanes", SimObject, "liveries", VendorName);
    public string DownloadRoot => Tree.At("Downloads", "Livery Pack ünï");
    public string SourceVendorDir => Path.Combine(DownloadRoot, "SimObjects", "Airplanes", SimObject, "liveries", "creator");

    public AircraftInfo Aircraft => new()
    {
        Title = "Test Aircraft",
        PackageFolder = OfficialPackage,
        ManifestPath = Path.Combine(OfficialPackage, "manifest.json"),
        SimObjectName = SimObject,
        VendorPath = VendorPath,
        VendorName = VendorName,
        IsSupported = true,
    };

    // ---- Official content ------------------------------------------------------------------

    public string OfficialPath(string slot, string relative) => Path.Combine(VendorPath, slot, relative);

    public void AddOfficialFile(string slot, string relative, string content) => Tree.Write(OfficialPath(slot, relative), content);

    /// <summary>An Official slot like "commercial_freelance_01" with a thumbnail, a branded base
    /// albedo, a shared paintflakes texture and a compressed model.&lt;part&gt; folder per part.</summary>
    public void AddOfficialSlotWithParts(string slot, params string[] parts)
    {
        AddOfficialFile(slot, "livery.cfg", "[GENERAL]\r\nName=\"Official demo scheme\"\r\n");
        AddOfficialFile(slot, @"thumbnail\thumbnail.png", "official-thumbnail");
        AddOfficialFile(slot, @"texture\texture.cfg", "[fltsim]\r\nfallback.1=..\\..\\official-fallback\\texture\r\n");
        AddOfficialFile(slot, @"texture\official_albd.png.ktx2", "official-branded-albedo");
        AddOfficialFile(slot, @"texture\official_albd.png.ktx2.json", "{\"official\":true}");
        AddOfficialFile(slot, @"texture\paintflakes_normal.ktx2", "shared-paintflakes");

        foreach (var part in parts)
        {
            AddOfficialPart(slot, part);
        }
    }

    /// <summary>One compressed model part. <paramref name="part"/> is the folder name, e.g.
    /// "model.wing_c". Two LODs are written (lod01/lod02) the way Official ships them: no lod00.</summary>
    public void AddOfficialPart(string slot, string part)
    {
        var shortName = part["model.".Length..];
        foreach (var lod in new[] { "01", "02" })
        {
            var gltf = Gltf.Make(
                Gltf.Material(shortName + "_branded_paint"),
                Gltf.Material(shortName + "_decal", withPbr: false, emissiveExtension: true));

            Tree.WriteFsc(OfficialPath(slot, $@"{part}\{shortName}_lod{lod}.gltf.fsc"), gltf);
            Tree.WriteFsc(OfficialPath(slot, $@"{part}\{shortName}_lod{lod}.bin.fsc"), new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        }
    }

    /// <summary>A plain neutral scheme (e.g. "official_static_10"): only a texture folder.</summary>
    public void AddNeutralSlot(string slot)
    {
        AddOfficialFile(slot, @"texture\neutral_albd.png.ktx2", "neutral-albedo");
        AddOfficialFile(slot, @"texture\neutral_albd.png.ktx2.json", "{\"neutral\":true}");
    }

    /// <summary>The activity as the app's own detector reports it for the folders created so far.</summary>
    public AircraftActivityInfo Activity(string activityKey) =>
        new CareerActivityDetector().DetectActivities(VendorPath).Single(a => a.ActivityKey == activityKey);

    // ---- Third-party livery download ---------------------------------------------------------

    /// <summary>Creates a livery folder (livery.cfg, texture/texture.cfg, a base albedo and a
    /// thumbnail) under the fake download and returns its path.</summary>
    public string AddSourceLivery(
        string folderName,
        string displayName = "My Livery",
        string? fallback1 = null,
        bool thumbnail = true,
        string extraLiveryCfg = "")
    {
        var dir = Path.Combine(SourceVendorDir, folderName);
        Tree.Write(Path.Combine(dir, "livery.cfg"), $"[GENERAL]\r\nName=\"{displayName}\"\r\n{extraLiveryCfg}");
        Tree.Write(Path.Combine(dir, "texture", "texture.cfg"), $"[fltsim]\r\nfallback.1={fallback1 ?? @"..\texture"}\r\n");
        Tree.Write(Path.Combine(dir, "texture", "body_albd.png.ktx2"), "paint-" + folderName);
        if (thumbnail)
        {
            Tree.Write(Path.Combine(dir, "thumbnail", "thumbnail.png"), "thumb-" + folderName);
        }

        return dir;
    }

    public string AddSourceFile(string folderName, string relative, string content) =>
        Tree.Write(Path.Combine(SourceVendorDir, folderName, relative), content);

    /// <summary>A same-vendor sibling folder with no livery.cfg of its own (the "_common" shape).</summary>
    public void AddSourceSiblingFolder(string name)
    {
        Tree.Write(Path.Combine(SourceVendorDir, name, "texture", "shared_albd.png.ktx2"), "shared-paint");
        Tree.Write(Path.Combine(SourceVendorDir, name, "model.airframe", "airframe_lod00.gltf"), Gltf.Make(Gltf.Material("shared_paint")));
    }

    public LiverySourceInfo Source(string baseFolderName) =>
        new LiverySourceInspector().Inspect(DownloadRoot).Single(s => s.BaseFolderName == baseFolderName);

    // ---- Apply -----------------------------------------------------------------------------

    public ApplyLiveryRequest Request(
        string baseFolderName,
        AircraftActivityInfo? activity = null,
        bool useDr = true,
        string packageName = DefaultPackageName) => new()
    {
        Aircraft = Aircraft,
        Source = Source(baseFolderName),
        UseDr = useDr,
        PackageName = packageName,
        Activity = activity,
    };

    public ApplyResult Apply(
        string baseFolderName,
        AircraftActivityInfo? activity = null,
        bool useDr = true,
        string packageName = DefaultPackageName) =>
        new PackageBuilder().ApplyWithNotes(Request(baseFolderName, activity, useDr, packageName), Community);

    public string PackageFolder(string packageName = DefaultPackageName) => Path.Combine(Community, packageName);

    /// <summary>The "liveries\asobo" folder inside the package the builder wrote.</summary>
    public string DestVendor(string packageName = DefaultPackageName) =>
        Path.Combine(PackageFolder(packageName), "simobjects", "airplanes", SimObject, "liveries", VendorName);

    public void Dispose() => Tree.Dispose();
}
