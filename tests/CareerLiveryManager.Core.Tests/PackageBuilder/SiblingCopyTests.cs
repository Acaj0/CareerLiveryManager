using CareerLiveryManager.Core.Services;
using CareerLiveryManager.Core.Tests.Support;

namespace CareerLiveryManager.Core.Tests.PackageBuilderTests;

/// <summary>
/// Liveries that borrow paint from a folder outside their own: a same-vendor "_common" sibling (737
/// MAX multi-registration packs) and a different SimObject (the C172 G1000 variant).
/// </summary>
public sealed class SiblingCopyTests
{
    private const string TuiTexture = @"..\..\TUI_common\texture";
    private const string TuiPointer =
        "<Models><LODS><LOD minSize=\"0\" ModelFile=\"..\\..\\TUI_common\\model.airframe\\airframe_lod00.gltf\"/></LODS></Models>";

    private static void AddTuiPack(ApplyScenario s)
    {
        s.AddSourceLivery("TUI_G-TAWA", fallback1: TuiTexture);
        s.AddSourceFile("TUI_G-TAWA", @"model.airframe\livery.xml", TuiPointer);
        s.AddSourceSiblingFolder("TUI_common");
    }

    [Fact]
    public void CommonSibling_IsCopiedNextToTheWinningFolder_WithANote()
    {
        using var s = new ApplyScenario("asobo_b737max");
        s.AddOfficialSlotWithParts("commercial_freelance_01", "model.airframe");
        AddTuiPack(s);

        var result = s.Apply("TUI_G-TAWA", s.Activity("commercial"));

        var sibling = Path.Combine(s.DestVendor(), "TUI_common");
        Assert.True(File.Exists(Path.Combine(sibling, "texture", "shared_albd.png.ktx2")));
        Assert.True(File.Exists(Path.Combine(sibling, "model.airframe", "airframe_lod00.gltf")));
        Assert.Contains(result.Notes, n => n.StartsWith("Copied same-vendor shared-assets sibling 'TUI_common'"));
    }

    [Fact]
    public void LiveryXmlPointingAtACopiedSibling_IsNotReplacedByAnInvisibleBackfill()
    {
        // The v1.2.2 interplay: the pointer only resolves once the sibling has been copied, so the
        // copy must run before the backfill decides whether the pointer dangles.
        using var s = new ApplyScenario("asobo_b737max");
        s.AddOfficialSlotWithParts("commercial_freelance_01", "model.airframe");
        AddTuiPack(s);

        var result = s.Apply("TUI_G-TAWA", s.Activity("commercial"));

        var airframe = Path.Combine(s.DestVendor(), "commercial_freelance_01", "model.airframe");
        Assert.True(File.Exists(Path.Combine(airframe, "livery.xml")));
        Assert.False(File.Exists(Path.Combine(airframe, "airframe_lod01.gltf")));
        Assert.DoesNotContain(result.Notes, n => n.Contains("Backfilled 'model.airframe'"));
    }

    [Fact]
    public void SiblingReferencedButAbsentFromTheSourcePack_IsReportedInTheNotes()
    {
        using var s = new ApplyScenario("asobo_b737max");
        s.AddOfficialSlotWithParts("commercial_freelance_01");
        s.AddSourceLivery("TUI_G-TAWA", fallback1: TuiTexture);

        var result = s.Apply("TUI_G-TAWA", s.Activity("commercial"));

        Assert.Contains(result.Notes, n => n.Contains("'TUI_common'") && n.Contains("doesn't exist in the source pack"));
        Assert.False(Directory.Exists(Path.Combine(s.DestVendor(), "TUI_common")));
    }

    [Fact]
    public void TwoActivitiesInOnePackage_ShareOneSiblingCopy_AndNoteItOnce()
    {
        using var s = new ApplyScenario("asobo_b737max");
        s.AddOfficialSlotWithParts("commercial_freelance_01");
        s.AddOfficialSlotWithParts("private_freelance_01");
        AddTuiPack(s);

        var first = s.Apply("TUI_G-TAWA", s.Activity("commercial"));
        var second = s.Apply("TUI_G-TAWA", s.Activity("private"));

        Assert.Contains(first.Notes, n => n.StartsWith("Copied same-vendor shared-assets sibling"));
        Assert.DoesNotContain(second.Notes, n => n.StartsWith("Copied same-vendor shared-assets sibling"));
        Assert.True(Directory.Exists(Path.Combine(s.DestVendor(), "commercial_freelance_01")));
        Assert.True(Directory.Exists(Path.Combine(s.DestVendor(), "private_freelance_01")));
        Assert.True(Directory.Exists(Path.Combine(s.DestVendor(), "TUI_common")));
    }

    [Fact]
    public void SelfContainedLivery_CopiesNothingExtra_AndProducesNoSiblingNotes()
    {
        // The Longitude-style pack: texture.cfg's own "..\texture" fallback, no pointers.
        using var s = new ApplyScenario("asobo_b737max");
        s.AddOfficialSlotWithParts("commercial_freelance_01");
        s.AddSourceLivery("SelfContained");

        var result = s.Apply("SelfContained", s.Activity("commercial"));

        Assert.DoesNotContain(result.Notes, n => n.Contains("sibling"));
        Assert.Equal(new[] { "commercial_freelance_01" }, Directory.EnumerateDirectories(s.DestVendor()).Select(Path.GetFileName).ToArray());
    }

    // ---- different-SimObject fallback (applies to every activity aircraft) ------------------------

    private static void AddC172G1000Pack(ApplyScenario s)
    {
        // The G1000 livery has only a partial texture set and borrows the rest from the analog C172.
        s.AddSourceLivery("N733EC", fallback1: @"..\..\..\..\..\asobo_c172sp\liveries\kfs\N733EC\texture");
        s.Tree.Write(
            Path.Combine(s.DownloadRoot, "SimObjects", "Airplanes", "asobo_c172sp", "liveries", "kfs", "N733EC", "texture", "analog_albd.png.ktx2"),
            "analog-paint");
    }

    [Fact]
    public void CrossSimObjectFallback_IsCopiedFromTheDownload_IntoTheSameRelativeLocation()
    {
        using var s = new ApplyScenario("asobo_c172sp_as1000");
        s.AddOfficialFile("cargo_freelance_01", @"thumbnail\thumbnail.png", "t");
        AddC172G1000Pack(s);

        var result = s.Apply("N733EC", s.Activity("cargo"));

        var copied = Path.Combine(s.PackageFolder(), "simobjects", "airplanes", "asobo_c172sp", "liveries", "kfs", "N733EC", "texture", "analog_albd.png.ktx2");
        Assert.Equal("analog-paint", File.ReadAllText(copied));
        Assert.Contains(result.Notes, n => n.StartsWith("Copied cross-SimObject fallback sibling 'asobo_c172sp/liveries/kfs/N733EC'"));
    }

    [Fact]
    public void CrossSimObjectFallback_ResolvesFromTheWinningFoldersTextureFolder()
    {
        using var s = new ApplyScenario("asobo_c172sp_as1000");
        s.AddOfficialFile("cargo_freelance_01", @"thumbnail\thumbnail.png", "t");
        AddC172G1000Pack(s);

        s.Apply("N733EC", s.Activity("cargo"));

        var textureCfg = Path.Combine(s.DestVendor(), "cargo_freelance_01", "texture", "texture.cfg");
        var fallback = new LiveryCfgEditor().ReadFallback1(textureCfg)!;
        var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(textureCfg)!, fallback));
        Assert.True(Directory.Exists(resolved), $"fallback.1 resolves to a missing folder: {resolved}");
    }

    [Fact]
    public void CrossSimObjectFallback_ToTheSameSimObject_CopiesNothing()
    {
        using var s = new ApplyScenario("asobo_c172sp");
        s.AddOfficialFile("cargo_freelance_01", @"thumbnail\thumbnail.png", "t");
        s.AddSourceLivery("N733EC", fallback1: @"..\..\..\..\..\asobo_c172sp\liveries\kfs\OTHER\texture");

        var result = s.Apply("N733EC", s.Activity("cargo"));

        Assert.Empty(result.Notes);
    }

    [Fact]
    public void CrossSimObjectFallback_WhoseFolderIsNotInTheDownload_CopiesNothing()
    {
        using var s = new ApplyScenario("asobo_c172sp_as1000");
        s.AddOfficialFile("cargo_freelance_01", @"thumbnail\thumbnail.png", "t");
        s.AddSourceLivery("N733EC", fallback1: @"..\..\..\..\..\asobo_c172sp\liveries\kfs\N733EC\texture");

        var result = s.Apply("N733EC", s.Activity("cargo"));

        Assert.Empty(result.Notes);
        Assert.False(Directory.Exists(Path.Combine(s.PackageFolder(), "simobjects", "airplanes", "asobo_c172sp")));
    }
}
