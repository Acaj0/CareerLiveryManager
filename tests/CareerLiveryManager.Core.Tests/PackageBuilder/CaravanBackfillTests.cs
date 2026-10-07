using CareerLiveryManager.Core.Models;
using CareerLiveryManager.Core.Services;
using CareerLiveryManager.Core.Tests.Support;

namespace CareerLiveryManager.Core.Tests.PackageBuilderTests;

/// <summary>
/// The Cessna 208B Caravan has the same multi-part structure as the 737 MAX and reuses the same
/// invisible-backfill machinery, with its own decal material names and a per-category neutral
/// scheme ("cargo_static_01") instead of one shared one.
/// </summary>
public sealed class CaravanBackfillTests : IDisposable
{
    private const string SimObject = "asobo_c208b";
    private const string OfficialSlot = "cargo_freelance_01";

    private readonly ApplyScenario _s = new(SimObject);
    private readonly LiveryCfgEditor _cfg = new();

    public CaravanBackfillTests()
    {
        _s.AddOfficialSlotWithParts(OfficialSlot, "model.airframe", "model.tail", "model.wing_left");
    }

    public void Dispose() => _s.Dispose();

    private string Winning => Path.Combine(_s.DestVendor(), OfficialSlot);

    private ApplyResult ApplyCargo() => _s.Apply("N208AB", _s.Activity("cargo"));

    [Fact]
    public void MissingPart_IsBackfilledInvisible()
    {
        _s.AddSourceLivery("N208AB");

        var result = ApplyCargo();

        foreach (var material in Gltf.Parse(Path.Combine(Winning, "model.wing_left", "wing_left_lod01.gltf"))["materials"]!.AsArray())
        {
            Gltf.AssertInvisible(material!);
        }

        Assert.True(File.Exists(Path.Combine(Winning, "model.wing_left", "wing_left_lod00.gltf")));
        Assert.Contains(result.Notes, n => n.StartsWith("Backfilled 'model.wing_left' from Official (made invisible)"));
    }

    [Fact]
    public void CompanyDecalMaterials_InTheLiverysOwnAirframe_AreHidden_CaseInsensitively()
    {
        var painting = Gltf.Material("CustomCOLOR_Painting_00");
        var registration = Gltf.Material("RegistrationNumber");
        _s.AddSourceLivery("N208AB");
        _s.AddSourceFile("N208AB", @"model.airframe\airframe_lod00.gltf", Gltf.Make(
            painting,
            registration,
            Gltf.Material("CUSTOM_Image_00", emissiveExtension: true),
            Gltf.Material("CUSTOM_Image_01"),
            Gltf.Material("custom_text_00"),
            Gltf.Material("CUSTOM_TEXT_00_LEFT"),
            Gltf.Material("CUSTOM_TEXT_00_RIGHT"),
            Gltf.Material("CUSTOM_Text_01")));

        ApplyCargo();

        var airframe = Gltf.Parse(Path.Combine(Winning, "model.airframe", "airframe_lod00.gltf"));
        foreach (var name in new[] { "CUSTOM_Image_00", "CUSTOM_Image_01", "custom_text_00", "CUSTOM_TEXT_00_LEFT", "CUSTOM_TEXT_00_RIGHT", "CUSTOM_Text_01" })
        {
            Gltf.AssertInvisible(Gltf.Material(airframe, name));
        }

        Assert.Equal(painting.ToJsonString(), Gltf.Material(airframe, "CustomCOLOR_Painting_00").ToJsonString());
        Assert.Equal(registration.ToJsonString(), Gltf.Material(airframe, "RegistrationNumber").ToJsonString());
    }

    [Fact]
    public void The737sDecalMaterialNames_AreNotHidden_OnTheCaravan()
    {
        var decal737 = Gltf.Material("CUSTOM_ADAPTIVE_IMAGE_2", withPbr: false, emissiveExtension: true);
        var original = decal737.ToJsonString();
        _s.AddSourceLivery("N208AB");
        _s.AddSourceFile("N208AB", @"model.airframe\airframe_lod00.gltf", Gltf.Make(decal737));

        ApplyCargo();

        var airframe = Gltf.Parse(Path.Combine(Winning, "model.airframe", "airframe_lod00.gltf"));
        Assert.Equal(original, Gltf.Material(airframe, "CUSTOM_ADAPTIVE_IMAGE_2").ToJsonString());
    }

    [Fact]
    public void WhenTheCategorysStaticSchemeExists_ItsAlbedoReplacesTheBrandedOne()
    {
        _s.AddNeutralSlot("cargo_static_01");
        _s.AddSourceLivery("N208AB");

        var result = ApplyCargo();

        Assert.Equal("neutral-albedo", File.ReadAllText(Path.Combine(Winning, "texture", "official_albd.png.ktx2")));
        Assert.Contains(result.Notes, n => n.StartsWith("Neutralized branded texture"));
    }

    [Fact]
    public void WhenTheCategoryHasNoStaticScheme_TheOfficialAlbedoIsCopied_ButPartsAreStillBackfilled()
    {
        _s.AddSourceLivery("N208AB");

        var result = ApplyCargo();

        Assert.Equal("official-branded-albedo", File.ReadAllText(Path.Combine(Winning, "texture", "official_albd.png.ktx2")));
        Assert.Contains(result.Notes, n => n.StartsWith("Backfilled texture file 'texture/official_albd.png.ktx2'"));
        Assert.True(File.Exists(Path.Combine(Winning, "model.tail", "tail_lod01.gltf")));
    }

    [Fact]
    public void ActivityNeutralScheme_IsChosenPerCategory_NotTheFirstStaticOne()
    {
        _s.AddNeutralSlot("flightseeing_static_01");
        _s.AddOfficialFile("flightseeing_static_01", @"texture\neutral_albd.png.ktx2", "flightseeing-neutral");
        _s.AddSourceLivery("N208AB");

        ApplyCargo();

        // The cargo slot must not borrow the flightseeing category's neutral scheme.
        Assert.Equal("official-branded-albedo", File.ReadAllText(Path.Combine(Winning, "texture", "official_albd.png.ktx2")));
    }

    [Fact]
    public void DisplayName_IsNotRewritten_OnTheCaravan()
    {
        _s.AddSourceLivery("N208AB", displayName: "Caravan Original");

        ApplyCargo();

        Assert.Equal("Caravan Original", _cfg.ReadLiveryName(Path.Combine(Winning, "livery.cfg")));
    }
}
