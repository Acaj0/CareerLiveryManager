using CareerLiveryManager.Core.Models;
using CareerLiveryManager.Core.Services;
using CareerLiveryManager.Core.Tests.Support;

namespace CareerLiveryManager.Core.Tests.PackageBuilderTests;

/// <summary>
/// The PC-12 NGX gets the Caravan's invisible part-backfill, scoped to its own SimObject, with the
/// medevac decal as the material to hide - and, unlike the Caravan, no loose-texture backfill.
/// </summary>
public sealed class Pc12BackfillTests : IDisposable
{
    private const string SimObject = "microsoft_pc12_ngx";
    private const string MedevacSlot = "livery_03_m";

    private readonly ApplyScenario _s = new(SimObject);

    public Pc12BackfillTests()
    {
        _s.AddOfficialSlotWithParts(MedevacSlot, "model.airframe", "model.nose", "model.wing_left_outer");
        _s.AddOfficialSlotWithParts("livery_01");
    }

    public void Dispose() => _s.Dispose();

    private string Winning => Path.Combine(_s.DestVendor(), MedevacSlot);

    private ApplyResult ApplyMedevac() => _s.Apply("N98GX", _s.Activity("medevac"));

    [Fact]
    public void MedevacApply_OverwritesTheOfficialMedevacSlotByItsExactName()
    {
        _s.AddSourceLivery("N98GX");

        ApplyMedevac();

        Assert.True(File.Exists(Path.Combine(Winning, "livery.cfg")));
        var cfg = File.ReadAllText(Path.Combine(Winning, "livery.cfg"));
        Assert.Contains("MED-PLN", cfg);
        Assert.Contains("Licence_Medevac", cfg);
    }

    [Fact]
    public void MissingPart_IsBackfilledInvisible()
    {
        _s.AddSourceLivery("N98GX");

        var result = ApplyMedevac();

        foreach (var material in Gltf.Parse(Path.Combine(Winning, "model.nose", "nose_lod01.gltf"))["materials"]!.AsArray())
        {
            Gltf.AssertInvisible(material!);
        }

        Assert.True(File.Exists(Path.Combine(Winning, "model.nose", "nose_lod00.gltf")));
        Assert.Contains(result.Notes, n => n.StartsWith("Backfilled 'model.nose' from Official (made invisible)"));
    }

    [Fact]
    public void TheOfficialSchemesLooseTextures_AreNotCopiedIn()
    {
        _s.AddSourceLivery("N98GX");

        var result = ApplyMedevac();

        Assert.False(File.Exists(Path.Combine(Winning, "texture", "official_albd.png.ktx2")));
        Assert.DoesNotContain(result.Notes, n => n.Contains("texture file"));
        Assert.DoesNotContain(result.Notes, n => n.StartsWith("Neutralized"));
    }

    [Fact]
    public void MedicalDecal_InTheLiverysOwnAirframe_IsHidden_AndEverythingElseIsUntouched()
    {
        var paint = Gltf.Material("CUSTOM_Color_00");
        var registration = Gltf.Material("Registration_TailLeft");
        _s.AddSourceLivery("N98GX");
        _s.AddSourceFile("N98GX", @"model.airframe\airframe_lod00.gltf", Gltf.Make(
            paint,
            registration,
            Gltf.Material("decal_medical", emissiveExtension: true)));

        ApplyMedevac();

        var airframe = Gltf.Parse(Path.Combine(Winning, "model.airframe", "airframe_lod00.gltf"));
        Gltf.AssertInvisible(Gltf.Material(airframe, "decal_medical"));
        Assert.Equal(paint.ToJsonString(), Gltf.Material(airframe, "CUSTOM_Color_00").ToJsonString());
        Assert.Equal(registration.ToJsonString(), Gltf.Material(airframe, "Registration_TailLeft").ToJsonString());
    }

    [Fact]
    public void DisplayName_IsNotRewritten()
    {
        _s.AddSourceLivery("N98GX", displayName: "Pilatus Original");

        ApplyMedevac();

        Assert.Equal("Pilatus Original", new LiveryCfgEditor().ReadLiveryName(Path.Combine(Winning, "livery.cfg")));
    }

    [Fact]
    public void TheDefaultEntry_AppliesTheOrdinarySingleLiveryRecipe()
    {
        _s.AddSourceLivery("N98GX");
        var defaultEntry = _s.Activity("default");
        Assert.True(defaultEntry.UsesStandardRecipe);

        // The app routes a standard-recipe entry to "no activity", exactly like an ordinary aircraft.
        _s.Apply("N98GX", activity: null, useDr: false);

        Assert.True(Directory.Exists(Path.Combine(_s.DestVendor(), "!N98GX")));
        Assert.False(Directory.Exists(Path.Combine(_s.DestVendor(), MedevacSlot)));
    }

    [Fact]
    public void OnAnotherAircraft_TheMedicalDecalIsNotHidden_AndNothingIsBackfilledLikeThePc12()
    {
        using var other = new ApplyScenario("asobo_c208b");
        other.AddOfficialSlotWithParts("cargo_freelance_01", "model.airframe");
        var decal = Gltf.Material("DECAL_MEDICAL", withPbr: false, emissiveExtension: true);
        var original = decal.ToJsonString();
        other.AddSourceLivery("N208AB");
        other.AddSourceFile("N208AB", @"model.airframe\airframe_lod00.gltf", Gltf.Make(decal));

        other.Apply("N208AB", other.Activity("cargo"));

        var airframe = Gltf.Parse(Path.Combine(other.DestVendor(), "cargo_freelance_01", "model.airframe", "airframe_lod00.gltf"));
        Assert.Equal(original, Gltf.Material(airframe, "DECAL_MEDICAL").ToJsonString());
    }
}
