using CareerLiveryManager.Core.Services;
using CareerLiveryManager.Core.Tests.Support;

namespace CareerLiveryManager.Core.Tests.PackageBuilderTests;

/// <summary>
/// Enforces ROADMAP principle 4: an aircraft-specific fix runs for its own SimObject and for no
/// other. Every fix gets the same input applied to a different SimObject name, and the test asserts
/// that none of the fix's effects show up.
/// </summary>
public sealed class AircraftGatingTests
{
    /// <summary>Real activity-supported aircraft without a fix of their own, plus a non-activity one.</summary>
    public static TheoryData<string> AircraftWithoutAFix => new()
    {
        "asobo_c172sp",
        "asobo_c172sp_as1000",
        "asobo_at802",
        "asobo_h125",
        "asobo_xcub",
        "asobo_cl415",
        "asobo_es30",
        "asobo_longitude",
    };

    private static string TuiSiblingCfg => @"..\..\TUI_common\texture";

    private const string TuiPointer =
        "<Models><LODS><LOD minSize=\"0\" ModelFile=\"..\\..\\TUI_common\\model.airframe\\airframe_lod00.gltf\"/></LODS></Models>";

    /// <summary>A DR pair whose DR folder reaches the shared "_common" folder through its model
    /// livery.xml (its texture.cfg fallback is rewritten to the base folder by the DR recipe).</summary>
    private static void AddDrCommonSiblingLivery(ApplyScenario s)
    {
        s.AddSourceLivery("TUI_G-TAWA");
        s.AddSourceLivery("TUI_G-TAWA_DR", fallback1: TuiSiblingCfg);
        s.AddSourceFile("TUI_G-TAWA_DR", @"model.airframe\livery.xml", TuiPointer);
        s.AddSourceSiblingFolder("TUI_common");
    }

    private static void AddCommonSiblingLivery(ApplyScenario s)
    {
        s.AddSourceLivery("TUI_G-TAWA", fallback1: TuiSiblingCfg);
        s.AddSourceFile(
            "TUI_G-TAWA",
            @"model.airframe\livery.xml",
            "<Models><LODS><LOD minSize=\"0\" ModelFile=\"..\\..\\TUI_common\\model.airframe\\airframe_lod00.gltf\"/></LODS></Models>");
        s.AddSourceSiblingFolder("TUI_common");
    }

    // ---- the missing-part backfill is 737 MAX / Caravan only ---------------------------------

    [Theory]
    [MemberData(nameof(AircraftWithoutAFix))]
    public void ActivityApply_DoesNotBackfillParts_OrTextures_OnOtherAircraft(string simObject)
    {
        using var s = new ApplyScenario(simObject);
        s.AddOfficialSlotWithParts("cargo_freelance_01", "model.airframe", "model.wing_c", "model.tail");
        s.AddNeutralSlot("cargo_static_01");
        s.AddNeutralSlot("official_static_10");
        s.AddSourceLivery("N123AB");

        var result = s.Apply("N123AB", s.Activity("cargo"));

        var winning = Path.Combine(s.DestVendor(), "cargo_freelance_01");
        Assert.Empty(Directory.EnumerateDirectories(winning, "model.*"));
        Assert.False(File.Exists(Path.Combine(winning, "texture", "official_albd.png.ktx2")));
        Assert.False(File.Exists(Path.Combine(winning, "texture", "paintflakes_normal.ktx2")));
        Assert.Empty(result.Notes);
    }

    [Theory]
    [MemberData(nameof(AircraftWithoutAFix))]
    public void ActivityApply_DoesNotHideDecalMaterials_OnOtherAircraft(string simObject)
    {
        using var s = new ApplyScenario(simObject);
        s.AddOfficialSlotWithParts("cargo_freelance_01", "model.airframe");
        s.AddSourceLivery("N123AB");
        var decals = new[]
        {
            Gltf.Material("CUSTOM_ADAPTIVE_IMAGE_2", withPbr: false, emissiveExtension: true),
            Gltf.Material("CUSTOM_Image_00", emissiveExtension: true),
        };
        var original = Gltf.Make(decals);
        s.AddSourceFile("N123AB", @"model.airframe\airframe_lod00.gltf", original);

        s.Apply("N123AB", s.Activity("cargo"));

        Assert.Equal(original, File.ReadAllText(Path.Combine(s.DestVendor(), "cargo_freelance_01", "model.airframe", "airframe_lod00.gltf")));
    }

    // ---- the display-name rewrite is 737 MAX only --------------------------------------------

    [Theory]
    [MemberData(nameof(AircraftWithoutAFix))]
    public void ActivityApply_DoesNotRewriteTheDisplayName_OnOtherAircraft(string simObject)
    {
        using var s = new ApplyScenario(simObject);
        s.AddOfficialFile("cargo_freelance_01", @"thumbnail\thumbnail.png", "t");
        s.AddSourceLivery("N123AB", displayName: "Untouched Name");

        s.Apply("N123AB", s.Activity("cargo"));

        var name = new LiveryCfgEditor().ReadLiveryName(Path.Combine(s.DestVendor(), "cargo_freelance_01", "livery.cfg"));
        Assert.Equal("Untouched Name", name);
    }

    // ---- the same-vendor "_common" sibling copy is 737 MAX only -------------------------------

    [Theory]
    [MemberData(nameof(AircraftWithoutAFix))]
    public void ActivityApply_DoesNotCopyCommonSiblings_OnOtherAircraft(string simObject)
    {
        using var s = new ApplyScenario(simObject);
        s.AddOfficialFile("cargo_freelance_01", @"thumbnail\thumbnail.png", "t");
        AddCommonSiblingLivery(s);

        var result = s.Apply("TUI_G-TAWA", s.Activity("cargo"));

        Assert.False(Directory.Exists(Path.Combine(s.DestVendor(), "TUI_common")));
        Assert.Empty(result.Notes);
    }

    [Theory]
    [MemberData(nameof(AircraftWithoutAFix))]
    public void NonActivityApply_DoesNotCopyCommonSiblings_OnOtherAircraft(string simObject)
    {
        using var s = new ApplyScenario(simObject);
        AddCommonSiblingLivery(s);

        var result = s.Apply("TUI_G-TAWA");

        Assert.False(Directory.Exists(Path.Combine(s.DestVendor(), "TUI_common")));
        Assert.Empty(result.Notes);
    }

    [Theory]
    [MemberData(nameof(AircraftWithoutAFix))]
    public void DrApply_DoesNotCopyCommonSiblings_OnOtherAircraft(string simObject)
    {
        using var s = new ApplyScenario(simObject);
        AddDrCommonSiblingLivery(s);

        var result = s.Apply("TUI_G-TAWA");

        Assert.False(Directory.Exists(Path.Combine(s.DestVendor(), "TUI_common")));
        Assert.Empty(result.Notes);
    }

    // ---- the fixes of one aircraft don't leak into the other fixed aircraft ------------------

    [Fact]
    public void Caravan_DoesNotCopyCommonSiblings_NorRewriteTheName()
    {
        using var s = new ApplyScenario("asobo_c208b");
        s.AddOfficialSlotWithParts("cargo_freelance_01");
        AddCommonSiblingLivery(s);

        var result = s.Apply("TUI_G-TAWA", s.Activity("cargo"));

        Assert.False(Directory.Exists(Path.Combine(s.DestVendor(), "TUI_common")));
        Assert.DoesNotContain(result.Notes, n => n.Contains("sibling"));
        var name = new LiveryCfgEditor().ReadLiveryName(Path.Combine(s.DestVendor(), "cargo_freelance_01", "livery.cfg"));
        Assert.Equal("My Livery", name);
    }

    [Fact]
    public void B737Max_DoesNotUseTheCaravansNeutralScheme()
    {
        using var s = new ApplyScenario("asobo_b737max");
        s.AddOfficialSlotWithParts("commercial_freelance_01");
        s.AddNeutralSlot("commercial_static_01");
        s.AddSourceLivery("JA351J");

        s.Apply("JA351J", s.Activity("commercial"));

        // The 737's neutral donor is official_static_10 only. It is absent here, so the branded
        // official albedo is copied as-is instead of a "<category>_static_01" substitute.
        var albedo = Path.Combine(s.DestVendor(), "commercial_freelance_01", "texture", "official_albd.png.ktx2");
        Assert.Equal("official-branded-albedo", File.ReadAllText(albedo));
    }

    [Fact]
    public void B737Max_DoesNotHideTheCaravansDecalMaterials()
    {
        using var s = new ApplyScenario("asobo_b737max");
        s.AddOfficialSlotWithParts("commercial_freelance_01", "model.airframe");
        s.AddSourceLivery("JA351J");
        var original = Gltf.Make(Gltf.Material("CUSTOM_Image_00", emissiveExtension: true));
        s.AddSourceFile("JA351J", @"model.airframe\airframe_lod00.gltf", original);

        s.Apply("JA351J", s.Activity("commercial"));

        Assert.Equal(original, File.ReadAllText(Path.Combine(s.DestVendor(), "commercial_freelance_01", "model.airframe", "airframe_lod00.gltf")));
    }

    // ---- positive controls: the same input DOES trigger the fix on its own aircraft ------------

    [Fact]
    public void PositiveControl_B737Max_CopiesCommonSiblings_InAllThreeRecipes()
    {
        foreach (var scenario in new[] { "activity", "plain", "dr" })
        {
            using var s = new ApplyScenario("asobo_b737max");
            s.AddOfficialSlotWithParts("commercial_freelance_01");
            s.AddNeutralSlot("official_static_10");

            if (scenario == "dr")
            {
                AddDrCommonSiblingLivery(s);
            }
            else
            {
                AddCommonSiblingLivery(s);
            }

            var result = s.Apply("TUI_G-TAWA", scenario == "activity" ? s.Activity("commercial") : null);

            Assert.True(Directory.Exists(Path.Combine(s.DestVendor(), "TUI_common")), $"{scenario}: sibling not copied");
            Assert.Contains(result.Notes, n => n.StartsWith("Copied same-vendor shared-assets sibling 'TUI_common'"));
        }
    }
}
