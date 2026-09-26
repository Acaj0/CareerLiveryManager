using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CareerLiveryManager.Core.Models;

namespace CareerLiveryManager.Core.Services;

public sealed class PackageBuilder
{
    private const string CreatorTag = "CareerLiveryManager";

    /// <summary>
    /// The 737 MAX splits its exterior into several separate modelled parts (airframe, wing_l,
    /// wing_r, wing_c, tail, landinggearl, landinggearr - see CAREER_LIVERY_RESEARCH.md section 21).
    /// Every official activity livery folder ships all of them, but a third-party repaint almost
    /// never covers wing_c (the wing-root/belly piece) - not one of the packs tested so far did,
    /// from two different creators. Whatever isn't covered falls back to the activity slot's own
    /// official demo scheme (e.g. "commercial_freelance_01" is the official "Air Infinity" livery),
    /// which carries its own visible branding/color - not a neutral default - even though the very
    /// same third-party livery looks fine in Free Flight under its own folder name, where nothing
    /// depends on that slot's content. Scoped to this aircraft only: other activity-supported
    /// aircraft (C172, Caravan...) are simpler single-body models where this gap doesn't occur.
    /// </summary>
    private const string B737MaxSimObjectName = "asobo_b737max";

    /// <summary>
    /// Official activity slots (like "commercial_freelance_01") carry their own official demo
    /// scheme's branded textures (e.g. an "Air Infinity" livery, with a distinctive color gradient
    /// right at the wing root). A gap a third-party livery leaves there falls back to that branding,
    /// not a neutral default - see <see cref="B737MaxSimObjectName"/>. This official_static slot is
    /// a plain, near-white scheme ("Boeing Business Jets") with nothing distinctive at the wing
    /// root, used both as the source for any part a livery doesn't cover at all (so the backfilled
    /// geometry's own self-contained material/texture is neutral instead of a demo scheme's) and as
    /// a substitute for any branded loose texture file the activity's own official folder would
    /// otherwise contribute. Not every official_static slot has every part (e.g. _04 is missing
    /// wing_c and the landing gear folders entirely) - _10 was checked to have all of them.
    /// </summary>
    private const string B737MaxNeutralStaticFolderName = "official_static_10";

    /// <summary>
    /// Fallback tint for <see cref="MakeMaterialsInvisible"/>'s rewritten materials. In practice the
    /// alpha is pushed low enough that the part renders effectively invisible regardless of this
    /// RGB, but it's set to an approximation of real "aircraft white" (sampled from the brightest,
    /// least-shadowed pixel of a real livery's own side-view thumbnail - Japan Airlines, a plain
    /// white scheme - which came out as RGB(249,247,243) rather than pure white) in case any
    /// material or LOD doesn't end up fully transparent in practice.
    /// </summary>
    private static readonly (double R, double G, double B) AircraftWhiteColor = (249.0 / 255, 247.0 / 255, 243.0 / 255);

    /// <summary>Name of the subfolder a DR livery's base (full paint) is nested under, in
    /// activity mode, so it never sits loose in the shared vendor namespace - see the DR
    /// branch in <see cref="Apply"/> for why.</summary>
    private const string FallbackBaseFolderName = "_fallback_base";

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
    public string Apply(ApplyLiveryRequest request, string communityPath) => ApplyWithNotes(request, communityPath).PackageFolder;

    /// <summary>Same as <see cref="Apply"/>, but also returns a plain-English list of any
    /// non-default recipe steps taken - see <see cref="ApplyResult"/>.</summary>
    public ApplyResult ApplyWithNotes(ApplyLiveryRequest request, string communityPath)
    {
        var notes = new List<string>();
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
                // The base (full paint) folder must NOT sit as a sibling in the shared
                // liveries/<vendor>/ namespace here: on aircraft with the activity/Tags system,
                // an untagged loose folder there can get picked up by other selection contexts
                // (e.g. the hangar/parking display) alongside our tagged winning folder, causing
                // the livery to flicker between default and custom depending on view angle. Nest
                // it inside the winning folder instead, where nothing else can ever find it.
                var baseDest = Path.Combine(winningDest, FallbackBaseFolderName);
                CopyDirectoryRecursive(request.Source.BaseFolderPath, baseDest);
                CopyDirectoryRecursive(request.Source.DrFolderPath!, winningDest);
                winningSourcePath = request.Source.DrFolderPath!;

                var textureCfgPath = Directory.EnumerateFiles(winningDest, "texture.cfg", SearchOption.AllDirectories)
                    .FirstOrDefault(f => !f.Contains(FallbackBaseFolderName, StringComparison.OrdinalIgnoreCase));
                if (textureCfgPath is not null)
                {
                    _cfgEditor.FixDrFallback(textureCfgPath, $@"..\{FallbackBaseFolderName}\texture");
                }
            }
            else
            {
                CopyDirectoryRecursive(request.Source.BaseFolderPath, winningDest);
                winningSourcePath = request.Source.BaseFolderPath;
            }

            // Cross-SimObject fallback siblings (e.g. the C172 G1000 variant borrowing paint from
            // its analog-gauge sibling) apply to any activity aircraft and predate this section -
            // left as-is. Everything else here (the "_common" shared-assets pattern, the missing-
            // part backfill, and the livery display name rewrite) was added specifically chasing 737
            // MAX bugs and is gated to that SimObject only: other activity aircraft (C172, Caravan,
            // AT-802, H125, XCub, CL-415, ES30) don't share the 737 MAX's split-into-many-parts
            // structure or its multi-registration "_common" pack convention, and their own DR/
            // activity handling already works - there's no reason for 737 MAX-specific fixes to run
            // for them at all.
            CopyCrossSimObjectFallbackSibling(winningDest, winningSourcePath, request.Aircraft.SimObjectName, packageFolder, notes);

            if (string.Equals(request.Aircraft.SimObjectName, B737MaxSimObjectName, StringComparison.OrdinalIgnoreCase))
            {
                CopySameVendorSiblingFolders(winningDest, winningSourcePath, vendorDestDir, notes);

                var officialActivityFolder = Path.Combine(request.Aircraft.VendorPath, activity.OfficialFolderName);
                var neutralFolder = Path.Combine(request.Aircraft.VendorPath, B737MaxNeutralStaticFolderName);
                FillMissingOfficialActivityFiles(winningDest, officialActivityFolder, neutralFolder, notes);
            }

            var activityCfgPath = Path.Combine(winningDest, "livery.cfg");
            if (File.Exists(activityCfgPath))
            {
                if (!activity.IsGenericSlot)
                {
                    _cfgEditor.SetCareerActivityTags(activityCfgPath, activity.DressingCodes, activity.LicenceTag);
                }

                // Every activity slot for the same third-party livery shares that livery's own
                // [GENERAL] Name (never touched before this) - e.g. installing the same "Japan
                // Airlines (JA351J)" livery for both the commercial and official 737 MAX slots
                // produced two entries with the exact same name in Free Flight, impossible to tell
                // apart. The "!" prefix is the same convention already used for non-activity
                // liveries (sorts to the top of the list); the activity name in brackets is new
                // here, so multiple slots of the same livery stay distinguishable too. Scoped to the
                // 737 MAX for the same reason as above - other activity aircraft weren't reported to
                // have this problem, so their existing display names are left exactly as they were.
                if (string.Equals(request.Aircraft.SimObjectName, B737MaxSimObjectName, StringComparison.OrdinalIgnoreCase))
                {
                    var currentName = _cfgEditor.ReadLiveryName(activityCfgPath) ?? request.PackageName;
                    _cfgEditor.SetLiveryName(activityCfgPath, $"! [{activity.DisplayName}] {currentName}");
                }
            }
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
                _cfgEditor.FixDrFallback(textureCfgPath, $@"..\..\{request.Source.BaseFolderName}\texture");
            }

            if (string.Equals(request.Aircraft.SimObjectName, B737MaxSimObjectName, StringComparison.OrdinalIgnoreCase))
            {
                CopySameVendorSiblingFolders(drDest, request.Source.DrFolderPath!, vendorDestDir, notes);
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

            if (string.Equals(request.Aircraft.SimObjectName, B737MaxSimObjectName, StringComparison.OrdinalIgnoreCase))
            {
                CopySameVendorSiblingFolders(dest, request.Source.BaseFolderPath, vendorDestDir, notes);
            }
        }

        File.WriteAllText(Path.Combine(packageFolder, "manifest.json"), BuildManifestJson(request.Aircraft.Title, request.Activity, request.Aircraft.SimObjectName));
        WriteLayoutJson(packageFolder);

        return new ApplyResult { PackageFolder = packageFolder, Notes = notes };
    }

    /// <summary>
    /// Some liveries (e.g. the Cessna 172 G1000 variant) ship only a partial texture set and rely
    /// on a texture.cfg fallback.1 pointing at a *different SimObject entirely* (the analog-gauge
    /// C172) for the rest of the paint job. Community packages don't automatically inherit that
    /// sibling from Official content, so without this the plane renders solid white in-game - see
    /// CAREER_LIVERY_RESEARCH.md section 17.6/18. This copies that sibling folder straight from
    /// the third-party source (never from Official content) if the fallback needs one.
    /// </summary>
    private void CopyCrossSimObjectFallbackSibling(string winningDestFolder, string winningSourceFolder, string currentSimObjectName, string packageFolder, List<string> notes)
    {
        // Ignore any nested "_fallback_base" (see the DR branch above) - we only care about the
        // winning folder's own texture.cfg here, not the base fallback's.
        var textureCfgPath = Directory.EnumerateFiles(winningDestFolder, "texture.cfg", SearchOption.AllDirectories)
            .FirstOrDefault(f => !f.Contains(FallbackBaseFolderName, StringComparison.OrdinalIgnoreCase));
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
        notes.Add($"Copied cross-SimObject fallback sibling '{string.Join('/', liveryRelativeSegments)}' (from '{targetSimObject}') - the livery's own texture.cfg needs it for the rest of its paint.");
    }

    /// <summary>
    /// Backfills, from the matching Official activity livery folder, whatever the third-party
    /// livery doesn't actually cover. See <see cref="B737MaxSimObjectName"/> for why this exists.
    /// livery.cfg and the thumbnail are always skipped: those must stay the third-party livery's
    /// own (activity tags, package name, and preview image), never Official's.
    ///
    /// Each model.&lt;part&gt; folder is handled as a whole, not file-by-file: if the livery doesn't
    /// have that folder at all, Official's is copied in outright. If it does have the folder but
    /// only as a livery.xml pointer (the "Kuro" TUI/JAL pack shape - see
    /// <see cref="CopySameVendorSiblingFolders"/>) whose ModelFile references don't actually resolve
    /// to a real file anywhere in the package, that's a gap in the third-party pack itself (not
    /// something we could complete by adding files alongside a pointer that's already there) - the
    /// whole folder is replaced with Official's instead of leaving a broken reference in place. A
    /// folder with real geometry of its own, or a resolving pointer, is left untouched either way.
    /// Loose files outside model.* (the texture set) still use the simpler fill-only-what's-missing
    /// rule, keyed by exact file name.
    /// </summary>
    private static void FillMissingOfficialActivityFiles(string winningDest, string officialActivityFolder, string neutralFolder, List<string> notes)
    {
        if (!Directory.Exists(officialActivityFolder))
        {
            notes.Add($"737 MAX backfill skipped: no matching Official activity folder found at '{officialActivityFolder}'.");
            return;
        }

        foreach (var officialPartDir in Directory.EnumerateDirectories(officialActivityFolder, "model.*"))
        {
            var partName = Path.GetFileName(officialPartDir);
            var destPartDir = Path.Combine(winningDest, partName);

            // The SimObject's own attachment geometry (tried in an earlier version) is NOT usable
            // here: it's a full, complex model (with animation channels, etc.) rather than the
            // simple decal shape a livery override is meant to be, and the merge system silently
            // rejects it - confirmed by real in-game testing, where it fell back to the activity's
            // own branded default exactly as if nothing had been provided at all. Any real official
            // livery folder's own geometry IS a valid livery override and does get merged -
            // confirmed working - so the activity's own official copy is always used as the shape
            // donor (it's guaranteed to have all 7 parts, unlike some official_static schemes which
            // are missing one or more), with every one of its materials made invisible in
            // <see cref="CopyModelPartDecompressingFsc"/> so every backfilled part is a consistent,
            // seamless gap - never that scheme's own real branding - at every LOD/distance.
            if (!Directory.Exists(destPartDir))
            {
                CopyModelPartDecompressingFsc(officialPartDir, destPartDir, winningDest, makeInvisible: true);
                notes.Add($"Backfilled '{partName}' from Official (made invisible) - the livery doesn't cover this part at all.");
                continue;
            }

            var liveryXmlPath = Path.Combine(destPartDir, "livery.xml");
            if (File.Exists(liveryXmlPath) && !ModelFileReferencesResolve(destPartDir, liveryXmlPath))
            {
                Directory.Delete(destPartDir, recursive: true);
                CopyModelPartDecompressingFsc(officialPartDir, destPartDir, winningDest, makeInvisible: true);
                notes.Add($"Backfilled '{partName}' from Official (made invisible) - the livery's own livery.xml pointed at a file that doesn't exist anywhere in the pack (a gap in the third-party livery itself).");
                continue;
            }

            // A livery that provides its own model.airframe directly as loose glTF (no livery.xml
            // pointer) could, in principle, reuse Official's own material naming for the employer
            // decal placeholders (CUSTOM_ADAPTIVE_IMAGE_2/3) if it was built from an Official
            // template - patch those two specifically by name, leaving every other material (the
            // livery's own real paint) untouched. Liveries using the "_common" sibling pattern
            // (TUI/JAL-style) only have a livery.xml pointer here, not a real .gltf, so this is a
            // no-op for them - not a concern, since neither has been seen to define these materials.
            if (string.Equals(partName, "model.airframe", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var gltfFile in Directory.EnumerateFiles(destPartDir, "*.gltf"))
                {
                    var patched = MakeNamedMaterialsInvisible(File.ReadAllBytes(gltfFile), B737MaxAirframeDecalMaterialsToHide);
                    File.WriteAllBytes(gltfFile, patched);
                }
            }
        }

        foreach (var officialFile in Directory.EnumerateFiles(officialActivityFolder, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(officialActivityFolder, officialFile);
            var topSegment = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            if (topSegment.StartsWith("model.", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(topSegment, "livery.cfg", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(topSegment, "thumbnail", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(relative), "texture.cfg", StringComparison.OrdinalIgnoreCase))
            {
                continue; // model.* folders are handled as a whole above
            }

            var destFile = Path.Combine(winningDest, relative);
            if (File.Exists(destFile))
            {
                continue; // the third-party livery already covers this exact file - keep it
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            var relativeDisplay = relative.Replace(Path.DirectorySeparatorChar, '/');
            var neutralSubstitute = FindNeutralAlbedoSubstitute(relative, neutralFolder);
            if (neutralSubstitute is not null)
            {
                File.Copy(neutralSubstitute, destFile);
                notes.Add($"Neutralized branded texture '{relativeDisplay}' - the activity's own official scheme has visible branding here (e.g. a demo airline's color scheme), so a plain/neutral scheme's texture was used instead.");
            }
            else
            {
                File.Copy(officialFile, destFile);
                notes.Add($"Backfilled texture file '{relativeDisplay}' from Official - the livery didn't provide it.");
            }
        }
    }

    /// <summary>
    /// Official geometry ships as ".gltf.fsc"/".bin.fsc" - a plain zlib-compressed glTF/bin, not
    /// some proprietary or encrypted format (confirmed by decompressing one with a standard
    /// <see cref="ZLibStream"/> and getting valid glTF JSON back out). The Community livery merge
    /// system expects real loose .gltf/.bin (or a livery.xml pointing at them) - copying the raw
    /// .fsc files verbatim (the previous approach) put valid-looking files in the right folder, but
    /// they're not something the merge step can actually read, so the part silently fell back to
    /// its attachment's own baked-in default look regardless of what we provided. This decompresses
    /// every .fsc file in the part folder into its real, working counterpart (same name, ".fsc"
    /// stripped).
    ///
    /// When <paramref name="makeInvisible"/> is true (the source is a real official livery's own
    /// geometry, borrowed only because its file shape is one the merge system actually accepts),
    /// every LOD's materials are rewritten to render effectively invisible instead of copying in
    /// that scheme's own real textures - a plain recolor (even an off-white one) still reads as a
    /// visible seam against the third-party livery's own real paint, but an invisible patch doesn't.
    /// See <see cref="MakeMaterialsInvisible"/>. When false, referenced textures are copied in as
    /// before instead - see <see cref="CopyReferencedTextures"/>.
    /// </summary>
    private static void CopyModelPartDecompressingFsc(string sourcePartDir, string destPartDir, string winningDest, bool makeInvisible)
    {
        Directory.CreateDirectory(destPartDir);

        var sourceTextureDir = Path.Combine(Directory.GetParent(sourcePartDir)!.FullName, "texture");
        var destTextureDir = Path.Combine(winningDest, "texture");

        foreach (var file in Directory.EnumerateFiles(sourcePartDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourcePartDir, file);

            if (!file.EndsWith(".fsc", StringComparison.OrdinalIgnoreCase))
            {
                var plainDestFile = Path.Combine(destPartDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(plainDestFile)!);
                File.Copy(file, plainDestFile, overwrite: true);
                continue;
            }

            var decompressed = DecompressFsc(file);
            var destFile = Path.Combine(destPartDir, relative[..^".fsc".Length]);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

            if (destFile.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase) && makeInvisible)
            {
                File.WriteAllBytes(destFile, MakeMaterialsInvisible(decompressed));
                continue;
            }

            File.WriteAllBytes(destFile, decompressed);

            if (destFile.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
            {
                CopyReferencedTextures(decompressed, sourceTextureDir, destTextureDir);
            }
        }

        SynthesizeClosestLod(destPartDir);
    }

    private static readonly Regex LodFileNamePattern = new(@"^(?<prefix>.*lod0*)(?<num>\d+)(?<suffix>\.gltf)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// None of the official LOD sets for a part start at 0 (they go lod01, lod02, lod03...), so
    /// there's never anything to merge in at the closest camera distances - that range always shows
    /// whatever the activity's own original mesh looked like (its real brand colors), regardless of
    /// what we provide for the other LODs. This adds a synthetic "lod00" by cloning the lowest LOD
    /// we do have and making every one of its materials invisible - a deliberately empty placeholder
    /// for the one distance range Official never gave us anything to work with, rather than trying
    /// to make it "accurate" to any particular scheme.
    /// </summary>
    private static void SynthesizeClosestLod(string destPartDir)
    {
        var lowest = Directory.EnumerateFiles(destPartDir, "*.gltf")
            .Select(f => (Path: f, Match: LodFileNamePattern.Match(Path.GetFileName(f))))
            .Where(x => x.Match.Success)
            .Select(x => (x.Path, Num: int.Parse(x.Match.Groups["num"].Value), x.Match))
            .OrderBy(x => x.Num)
            .FirstOrDefault();

        if (lowest.Path is null || lowest.Num == 0)
        {
            return; // no LOD files found, or a lod0 already exists - nothing to synthesize
        }

        var digitWidth = lowest.Match.Groups["num"].Value.Length;
        var zeroName = lowest.Match.Groups["prefix"].Value + "0".PadLeft(digitWidth, '0') + lowest.Match.Groups["suffix"].Value;
        var zeroPath = Path.Combine(destPartDir, zeroName);
        if (File.Exists(zeroPath))
        {
            return;
        }

        var invisibleGltf = MakeMaterialsInvisible(File.ReadAllBytes(lowest.Path));
        File.WriteAllBytes(zeroPath, invisibleGltf);
    }

    /// <summary>
    /// Some 737 MAX decal materials on the fuselage sides (the employer airline's adaptive name/
    /// logo placeholders on model.airframe) carry these exact names in every official activity
    /// folder. Unlike the plain-color decals (wing_c, landing gear) that already went invisible
    /// correctly, these still showed through after the near-zero-alpha treatment - confirmed by a
    /// real in-game report (a "fictional" texture-only livery, backfilled entirely from Official,
    /// still showed the employer's name on the tail). Both the tail's singular
    /// "CUSTOM_ADAPTIVE_IMAGE" and these two get the same emissive-extension fix in
    /// <see cref="WriteInvisibleMaterial"/> as part of a normal whole-part backfill; these two are
    /// additionally targeted by name wherever a livery provides its own model.airframe (see
    /// <see cref="MakeNamedMaterialsInvisible"/>), in case a third-party file happens to reuse
    /// Official's own material naming.
    /// </summary>
    private static readonly HashSet<string> B737MaxAirframeDecalMaterialsToHide = new(StringComparer.OrdinalIgnoreCase)
    {
        "CUSTOM_ADAPTIVE_IMAGE_2",
        "CUSTOM_ADAPTIVE_IMAGE_3",
    };

    /// <summary>Rewrites every material in a glTF JSON to render effectively invisible (a near-zero
    /// alpha, confirmed in-game to look better than any flat recolor - no visible seam against the
    /// third-party livery's own real paint) and drops any texture reference. The buffer/geometry
    /// data is untouched, so this still references the same .bin the source LOD does.</summary>
    private static byte[] MakeMaterialsInvisible(byte[] gltfBytes) => RewriteMaterialsInvisible(gltfBytes, targetNames: null);

    /// <summary>Same as <see cref="MakeMaterialsInvisible"/>, but only for materials whose "name"
    /// is in <paramref name="targetNames"/> - everything else in the glTF (the livery's own real
    /// paint) is left byte-for-byte untouched.</summary>
    private static byte[] MakeNamedMaterialsInvisible(byte[] gltfBytes, HashSet<string> targetNames) => RewriteMaterialsInvisible(gltfBytes, targetNames);

    private static byte[] RewriteMaterialsInvisible(byte[] gltfBytes, HashSet<string>? targetNames)
    {
        using var doc = JsonDocument.Parse(gltfBytes);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteWithInvisibleMaterials(doc.RootElement, writer, targetNames);
        }

        return stream.ToArray();
    }

    private static void WriteWithInvisibleMaterials(JsonElement root, Utf8JsonWriter writer, HashSet<string>? targetNames)
    {
        writer.WriteStartObject();
        foreach (var property in root.EnumerateObject())
        {
            if (property.NameEquals("materials"))
            {
                writer.WritePropertyName(property.Name);
                writer.WriteStartArray();
                foreach (var material in property.Value.EnumerateArray())
                {
                    var materialName = material.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                    var shouldHide = targetNames is null || (materialName is not null && targetNames.Contains(materialName));

                    if (shouldHide)
                    {
                        WriteInvisibleMaterial(material, writer);
                    }
                    else
                    {
                        material.WriteTo(writer);
                    }
                }

                writer.WriteEndArray();
            }
            else
            {
                property.WriteTo(writer);
            }
        }

        writer.WriteEndObject();
    }

    private static void WriteInvisibleMaterial(JsonElement material, Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        var wrotePbrMetallicRoughness = false;

        foreach (var materialProperty in material.EnumerateObject())
        {
            if (materialProperty.NameEquals("pbrMetallicRoughness"))
            {
                writer.WritePropertyName(materialProperty.Name);
                WriteInvisibleBaseColor(writer);
                wrotePbrMetallicRoughness = true;
            }
            else if (materialProperty.NameEquals("extensions"))
            {
                // ASOBO_material_emissive makes a decal self-illuminated, which - per a real
                // in-game report on the employer airline's name/logo decals - renders regardless of
                // alpha, defeating the near-zero-alpha trick above entirely. Every other extension
                // (draw order, decal blending, etc.) is harmless once the color/texture are gone, so
                // only this one is dropped.
                writer.WritePropertyName(materialProperty.Name);
                writer.WriteStartObject();
                foreach (var extension in materialProperty.Value.EnumerateObject())
                {
                    if (!extension.NameEquals("ASOBO_material_emissive"))
                    {
                        extension.WriteTo(writer);
                    }
                }

                writer.WriteEndObject();
            }
            else if (materialProperty.NameEquals("emissiveFactor") || materialProperty.NameEquals("emissiveTexture"))
            {
                // Dropped for the same reason, in case a material declares these directly instead
                // of via the ASOBO extension.
            }
            else
            {
                materialProperty.WriteTo(writer);
            }
        }

        // Some decal materials (e.g. the employer name's own "CUSTOM_COLOR_TEXT_LEFT/RIGHT") have
        // no pbrMetallicRoughness of their own at all - per the glTF spec, baseColorFactor then
        // defaults to fully opaque white, so skipping these left them rendering at full opacity
        // even after every other fix (confirmed by a real in-game report: the employer name still
        // showed, painted directly on the fuselage, despite the emissive-extension fix). One must
        // always be written, whether or not the source material had one.
        if (!wrotePbrMetallicRoughness)
        {
            writer.WritePropertyName("pbrMetallicRoughness");
            WriteInvisibleBaseColor(writer);
        }

        writer.WriteEndObject();
    }

    /// <summary>Writes a complete "pbrMetallicRoughness": { "baseColorFactor": [...] } object with
    /// the near-zero-alpha invisible color - shared by both branches of
    /// <see cref="WriteInvisibleMaterial"/> so a missing property is filled in identically to an
    /// overridden one.</summary>
    private static void WriteInvisibleBaseColor(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("baseColorFactor");
        writer.WriteStartArray();
        writer.WriteNumberValue(AircraftWhiteColor.R);
        writer.WriteNumberValue(AircraftWhiteColor.G);
        writer.WriteNumberValue(AircraftWhiteColor.B);
        // Near-zero alpha, not exactly 0: confirmed in-game to render invisibly, with no visible
        // seam against the third-party livery's own real paint.
        writer.WriteNumberValue(0.01);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static byte[] DecompressFsc(string path)
    {
        var compressed = File.ReadAllBytes(path);
        using var input = new MemoryStream(compressed);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>
    /// Every texture a decompressed part glTF's material references (its "images" array) needs to
    /// exist in the vendor's flat texture/ folder for the part to render correctly. The URI embedded
    /// in the glTF (e.g. "..\MODEL.WING_C\737_MAX_8_BBJ_LOGO_TAIL.PNG.KTX2") is just a label left
    /// over from Asobo's own export tooling, not a literal filesystem path to follow - the runtime
    /// resolves it by filename only, via the same texture-folder fallback chain documented for
    /// liveries generally, and the real file often differs in case from what's embedded in the URI.
    /// </summary>
    private static void CopyReferencedTextures(byte[] gltfBytes, string sourceTextureDir, string destTextureDir)
    {
        if (!Directory.Exists(sourceTextureDir))
        {
            return;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(gltfBytes);
        }
        catch (JsonException)
        {
            return;
        }

        if (!doc.RootElement.TryGetProperty("images", out var images))
        {
            return;
        }

        Directory.CreateDirectory(destTextureDir);

        foreach (var image in images.EnumerateArray())
        {
            if (!image.TryGetProperty("uri", out var uriProp))
            {
                continue;
            }

            var fileName = Path.GetFileName((uriProp.GetString() ?? string.Empty).Replace('\\', '/'));
            if (string.IsNullOrEmpty(fileName))
            {
                continue;
            }

            var sourceFile = Directory.EnumerateFiles(sourceTextureDir)
                .FirstOrDefault(f => string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase));
            if (sourceFile is null)
            {
                continue;
            }

            var destFile = Path.Combine(destTextureDir, Path.GetFileName(sourceFile));
            if (!File.Exists(destFile))
            {
                File.Copy(sourceFile, destFile);
            }

            var sourceJson = sourceFile + ".json";
            var destJson = destFile + ".json";
            if (File.Exists(sourceJson) && !File.Exists(destJson))
            {
                File.Copy(sourceJson, destJson);
            }
        }
    }

    /// <summary>
    /// If <paramref name="relativePath"/> is a base-paint albedo texture (its name contains "_albd.")
    /// that the activity's own official folder has but the neutral folder does NOT have under the
    /// same name, it's scheme-specific branding rather than a shared/generic asset (like paintflakes,
    /// which every scheme ships identically) - returns the neutral folder's own albedo file (or its
    /// .json sidecar, matching what's being asked for) as a substitute. Returns null for anything
    /// else, including decal-only textures (e.g. a demo airline's logo) that have no neutral
    /// equivalent to substitute - those are left as a known residual limitation for now.
    /// </summary>
    private static string? FindNeutralAlbedoSubstitute(string relativePath, string neutralFolder)
    {
        var fileName = Path.GetFileName(relativePath);
        if (!fileName.Contains("_albd.", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (File.Exists(Path.Combine(neutralFolder, relativePath)))
        {
            return null; // shared/generic file present under the same name in the neutral scheme too
        }

        var neutralTextureDir = Path.Combine(neutralFolder, "texture");
        if (!Directory.Exists(neutralTextureDir))
        {
            return null;
        }

        var wantsJsonSidecar = fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        return Directory.EnumerateFiles(neutralTextureDir, "*_albd.*.ktx2*")
            .FirstOrDefault(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) == wantsJsonSidecar);
    }

    /// <summary>True if every ModelFile a part's livery.xml references actually exists on disk,
    /// resolved relative to that part's own folder (matching how the game would resolve it).</summary>
    private static bool ModelFileReferencesResolve(string partDir, string liveryXmlPath)
    {
        var modelFiles = ExtractModelFileReferences(liveryXmlPath).ToList();
        if (modelFiles.Count == 0)
        {
            return true; // no references to check - nothing to contradict, leave the folder alone
        }

        return modelFiles.All(mf => File.Exists(Path.GetFullPath(Path.Combine(partDir, mf))));
    }

    /// <summary>
    /// Some multi-registration livery packs (e.g. the "Kuro" 737 MAX TUI/JAL packs) split each
    /// registration into a lightweight folder that has no geometry or paint of its own - both its
    /// texture.cfg fallback.1 and every model.&lt;part&gt;/livery.xml's ModelFile point at a shared
    /// "&lt;prefix&gt;_common" folder living as a SIBLING in the same vendor folder (not a different
    /// SimObject - that's <see cref="CopyCrossSimObjectFallbackSibling"/> - and not the DR base
    /// folder either, both already handled elsewhere). Without that sibling, the plane has nothing
    /// to render at all and shows up blank/white. This copies any such sibling straight from the
    /// third-party source, never from Official content.
    /// </summary>
    private static void CopySameVendorSiblingFolders(string winningDest, string winningSourceFolder, string vendorDestDir, List<string> notes)
    {
        var siblingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var textureCfgPath = Directory.EnumerateFiles(winningDest, "texture.cfg", SearchOption.AllDirectories)
            .FirstOrDefault(f => !f.Contains(FallbackBaseFolderName, StringComparison.OrdinalIgnoreCase));
        if (textureCfgPath is not null)
        {
            CollectSameVendorSibling(new LiveryCfgEditor().ReadFallback1(textureCfgPath), siblingNames);
        }

        foreach (var xmlPath in Directory.EnumerateFiles(winningDest, "livery.xml", SearchOption.AllDirectories))
        {
            foreach (var modelFile in ExtractModelFileReferences(xmlPath))
            {
                CollectSameVendorSibling(modelFile, siblingNames);
            }
        }

        if (siblingNames.Count == 0)
        {
            return;
        }

        var sourceVendorDir = Directory.GetParent(winningSourceFolder)?.FullName;
        if (sourceVendorDir is null)
        {
            return;
        }

        foreach (var siblingName in siblingNames)
        {
            var sourceSibling = Path.Combine(sourceVendorDir, siblingName);
            var destSibling = Path.Combine(vendorDestDir, siblingName);
            if (Directory.Exists(destSibling))
            {
                continue; // already copied (e.g. two activities from the same pack sharing it)
            }

            if (!Directory.Exists(sourceSibling))
            {
                notes.Add($"Livery references a same-vendor sibling folder '{siblingName}' that doesn't exist in the source pack - some parts may be missing geometry/paint.");
                continue;
            }

            CopyDirectoryRecursive(sourceSibling, destSibling);
            notes.Add($"Copied same-vendor shared-assets sibling '{siblingName}' - the livery's own model/texture files reference it (see CAREER_LIVERY_RESEARCH.md section 22).");
        }
    }

    /// <summary>
    /// A same-vendor sibling reference looks like "&lt;SiblingName&gt;\texture" or
    /// "&lt;SiblingName&gt;\model.part\file.gltf" (after stripping leading ".." segments) - as
    /// opposed to the cross-SimObject shape "&lt;SimObject&gt;\liveries\&lt;vendor&gt;\&lt;name&gt;\texture",
    /// which always has "liveries" as its second segment, and the DR base folder's own
    /// "<see cref="FallbackBaseFolderName"/>\texture", which is nested inside the winning folder
    /// itself rather than being a vendor-level sibling.
    /// </summary>
    private static void CollectSameVendorSibling(string? relativePath, HashSet<string> siblingNames)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        var segments = relativePath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        var relativeSegments = segments.SkipWhile(s => s == "..").ToArray();
        if (relativeSegments.Length < 2 ||
            string.Equals(relativeSegments[1], "liveries", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(relativeSegments[0], FallbackBaseFolderName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        siblingNames.Add(relativeSegments[0]);
    }

    /// <summary>Reads every LOD's ModelFile attribute out of a model.&lt;part&gt;/livery.xml file.</summary>
    private static IEnumerable<string> ExtractModelFileReferences(string livieryXmlPath)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Load(livieryXmlPath);
        }
        catch (Exception)
        {
            yield break; // malformed/unexpected file - never let this crash the whole apply
        }

        foreach (var lod in doc.Descendants("LOD"))
        {
            var modelFile = lod.Attribute("ModelFile")?.Value;
            if (!string.IsNullOrWhiteSpace(modelFile))
            {
                yield return modelFile;
            }
        }
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
