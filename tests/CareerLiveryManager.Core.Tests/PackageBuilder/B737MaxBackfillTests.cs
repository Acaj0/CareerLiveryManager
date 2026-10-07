using CareerLiveryManager.Core.Models;
using CareerLiveryManager.Core.Services;
using CareerLiveryManager.Core.Tests.Support;

namespace CareerLiveryManager.Core.Tests.PackageBuilderTests;

/// <summary>
/// The 737 MAX is built from several separate model parts and a third-party repaint almost never
/// covers all of them; whatever it misses is backfilled from the Official activity slot as an
/// invisible overlay. See CAREER_LIVERY_RESEARCH.md section 21.
/// </summary>
public sealed class B737MaxBackfillTests : IDisposable
{
    private const string SimObject = "asobo_b737max";
    private const string OfficialSlot = "commercial_freelance_01";
    private const string NeutralSlot = "official_static_10";

    private readonly ApplyScenario _s = new(SimObject);
    private readonly LiveryCfgEditor _cfg = new();

    public B737MaxBackfillTests()
    {
        _s.AddOfficialSlotWithParts(OfficialSlot, "model.airframe", "model.wing_c", "model.wing_l", "model.tail");
        _s.AddNeutralSlot(NeutralSlot);
    }

    public void Dispose() => _s.Dispose();

    private string Winning => Path.Combine(_s.DestVendor(), OfficialSlot);

    private ApplyResult ApplyCommercial(string liveryFolder = "JA351J") => _s.Apply(liveryFolder, _s.Activity("commercial"));

    [Fact]
    public void MissingPart_IsBackfilledFromOfficial_WithEveryMaterialInvisible()
    {
        _s.AddSourceLivery("JA351J");

        var result = ApplyCommercial();

        foreach (var lod in new[] { "lod01", "lod02" })
        {
            var gltf = Gltf.Parse(Path.Combine(Winning, "model.wing_c", $"wing_c_{lod}.gltf"));
            Assert.Equal(2, gltf["materials"]!.AsArray().Count);
            foreach (var material in gltf["materials"]!.AsArray())
            {
                Gltf.AssertInvisible(material!);
            }
        }

        Assert.Contains(result.Notes, n => n.StartsWith("Backfilled 'model.wing_c' from Official (made invisible)"));
    }

    [Fact]
    public void WholeFuselageOfATextureOnlyLivery_IsBackfilledToo()
    {
        _s.AddSourceLivery("JA351J");

        var result = ApplyCommercial();

        Assert.True(File.Exists(Path.Combine(Winning, "model.airframe", "airframe_lod01.gltf")));
        Assert.True(File.Exists(Path.Combine(Winning, "model.tail", "tail_lod01.gltf")));
        Assert.Contains(result.Notes, n => n.Contains("'model.airframe'"));
        Assert.Contains(result.Notes, n => n.Contains("'model.tail'"));
    }

    [Fact]
    public void BackfilledPart_IsDecompressed_NoFscFilesRemain()
    {
        _s.AddSourceLivery("JA351J");

        ApplyCommercial();

        var partDir = Path.Combine(Winning, "model.wing_c");
        Assert.Empty(Directory.EnumerateFiles(partDir, "*.fsc"));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, File.ReadAllBytes(Path.Combine(partDir, "wing_c_lod01.bin")));
    }

    [Fact]
    public void BackfilledPart_GetsASyntheticInvisibleLod00_BecauseOfficialNeverShipsOne()
    {
        _s.AddSourceLivery("JA351J");

        ApplyCommercial();

        var lod00 = Path.Combine(Winning, "model.wing_c", "wing_c_lod00.gltf");
        Assert.True(File.Exists(lod00));
        foreach (var material in Gltf.Parse(lod00)["materials"]!.AsArray())
        {
            Gltf.AssertInvisible(material!);
        }
    }

    [Fact]
    public void PartTheLiveryProvides_IsLeftUntouched()
    {
        _s.AddSourceLivery("JA351J");
        var original = Gltf.Make(Gltf.Material("livery_own_paint"));
        _s.AddSourceFile("JA351J", @"model.wing_c\wing_c_lod00.gltf", original);

        var result = ApplyCommercial();

        var partDir = Path.Combine(Winning, "model.wing_c");
        Assert.Equal(new[] { "wing_c_lod00.gltf" }, Directory.EnumerateFiles(partDir).Select(Path.GetFileName).ToArray());
        Assert.Equal(original, File.ReadAllText(Path.Combine(partDir, "wing_c_lod00.gltf")));
        Assert.DoesNotContain(result.Notes, n => n.Contains("'model.wing_c'"));
    }

    [Fact]
    public void LiveryXmlPointer_ThatDoesNotResolve_IsReplacedByOfficialsGeometry()
    {
        _s.AddSourceLivery("JA351J");
        _s.AddSourceFile("JA351J", @"model.tail\livery.xml",
            "<Models><LODS><LOD minSize=\"0\" ModelFile=\"..\\..\\MISSING_common\\model.tail\\tail_lod00.gltf\"/></LODS></Models>");

        var result = ApplyCommercial();

        var partDir = Path.Combine(Winning, "model.tail");
        Assert.False(File.Exists(Path.Combine(partDir, "livery.xml")));
        Assert.True(File.Exists(Path.Combine(partDir, "tail_lod01.gltf")));
        Assert.Contains(result.Notes, n => n.Contains("'model.tail'") && n.Contains("livery.xml pointed at a file that doesn't exist"));
    }

    [Fact]
    public void LiveryXmlPointer_ThatResolves_IsKept()
    {
        _s.AddSourceLivery("JA351J");
        _s.AddSourceFile("JA351J", @"model.wing_l\livery.xml",
            "<Models><LODS><LOD minSize=\"0\" ModelFile=\"wing_l_lod00.gltf\"/></LODS></Models>");
        _s.AddSourceFile("JA351J", @"model.wing_l\wing_l_lod00.gltf", Gltf.Make(Gltf.Material("own")));

        var result = ApplyCommercial();

        var partDir = Path.Combine(Winning, "model.wing_l");
        Assert.True(File.Exists(Path.Combine(partDir, "livery.xml")));
        Assert.False(File.Exists(Path.Combine(partDir, "wing_l_lod01.gltf")));
        Assert.DoesNotContain(result.Notes, n => n.Contains("'model.wing_l'"));
    }

    [Fact]
    public void EmployerDecalMaterials_InTheLiverysOwnAirframe_AreHiddenByName_AndNothingElse()
    {
        var paint = Gltf.Material("Paint_Body");
        var paintJson = paint.ToJsonString();
        _s.AddSourceLivery("JA351J");
        _s.AddSourceFile("JA351J", @"model.airframe\airframe_lod00.gltf", Gltf.Make(
            paint,
            Gltf.Material("CUSTOM_ADAPTIVE_IMAGE_2", withPbr: false, emissiveExtension: true),
            Gltf.Material("custom_adaptive_image_3", emissiveExtension: true)));

        ApplyCommercial();

        var airframe = Gltf.Parse(Path.Combine(Winning, "model.airframe", "airframe_lod00.gltf"));
        Gltf.AssertInvisible(Gltf.Material(airframe, "CUSTOM_ADAPTIVE_IMAGE_2"));
        Gltf.AssertInvisible(Gltf.Material(airframe, "custom_adaptive_image_3"));
        Assert.Equal(paintJson, Gltf.Material(airframe, "Paint_Body").ToJsonString());
        Assert.False(File.Exists(Path.Combine(Winning, "model.airframe", "airframe_lod01.gltf")));
    }

    [Fact]
    public void InvisibleMaterial_LosesTheEmissiveExtension_ButKeepsOtherExtensions()
    {
        _s.AddSourceLivery("JA351J");

        ApplyCommercial();

        var decal = Gltf.Material(Gltf.Parse(Path.Combine(Winning, "model.wing_c", "wing_c_lod01.gltf")), "wing_c_decal");
        Assert.Null(decal["extensions"]!["ASOBO_material_emissive"]);
        Assert.NotNull(decal["extensions"]!["ASOBO_material_draw_order"]);
    }

    [Fact]
    public void InvisibleMaterial_GetsAPbrBlock_EvenWhenTheSourceMaterialHadNone()
    {
        _s.AddSourceLivery("JA351J");

        ApplyCommercial();

        // "wing_c_decal" is written by the fixture without any pbrMetallicRoughness - per the glTF
        // spec that defaults to opaque white, which is what made employer names show through.
        var decal = Gltf.Material(Gltf.Parse(Path.Combine(Winning, "model.wing_c", "wing_c_lod01.gltf")), "wing_c_decal");
        Assert.NotNull(decal["pbrMetallicRoughness"]);
        Gltf.AssertInvisible(decal);
    }

    [Fact]
    public void BrandedAlbedo_IsSubstitutedByTheNeutralSchemesAlbedo_AndItsJsonSidecar()
    {
        _s.AddSourceLivery("JA351J");

        var result = ApplyCommercial();

        var texture = Path.Combine(Winning, "texture");
        Assert.Equal("neutral-albedo", File.ReadAllText(Path.Combine(texture, "official_albd.png.ktx2")));
        Assert.Equal("{\"neutral\":true}", File.ReadAllText(Path.Combine(texture, "official_albd.png.ktx2.json")));
        Assert.Contains(result.Notes, n => n.StartsWith("Neutralized branded texture 'texture/official_albd.png.ktx2'"));
    }

    [Fact]
    public void SharedTexture_TheLiveryLacks_IsBackfilledFromOfficial_AndExistingOnesAreKept()
    {
        _s.AddSourceLivery("JA351J");
        _s.AddSourceFile("JA351J", @"texture\other_normal.ktx2", "livery-own");
        _s.AddOfficialFile(OfficialSlot, @"texture\other_normal.ktx2", "official-version");

        var result = ApplyCommercial();

        var texture = Path.Combine(Winning, "texture");
        Assert.Equal("shared-paintflakes", File.ReadAllText(Path.Combine(texture, "paintflakes_normal.ktx2")));
        Assert.Equal("livery-own", File.ReadAllText(Path.Combine(texture, "other_normal.ktx2")));
        Assert.Contains(result.Notes, n => n.StartsWith("Backfilled texture file 'texture/paintflakes_normal.ktx2'"));
        Assert.Equal("paint-JA351J", File.ReadAllText(Path.Combine(texture, "body_albd.png.ktx2")));
    }

    [Fact]
    public void WithoutAnyNeutralAlbedo_TheOfficialAlbedoIsCopiedAsIs()
    {
        Directory.Delete(Path.Combine(_s.VendorPath, NeutralSlot), recursive: true);
        _s.AddSourceLivery("JA351J");

        ApplyCommercial();

        Assert.Equal("official-branded-albedo", File.ReadAllText(Path.Combine(Winning, "texture", "official_albd.png.ktx2")));
    }

    [Fact]
    public void LiveryCfg_Thumbnail_AndTextureCfg_AreNeverTakenFromOfficial()
    {
        _s.AddSourceLivery("JA351J", displayName: "Japan Airlines");

        ApplyCommercial();

        Assert.Equal("thumb-JA351J", File.ReadAllText(Path.Combine(Winning, "thumbnail", "thumbnail.png")));
        Assert.Equal(@"..\texture", _cfg.ReadFallback1(Path.Combine(Winning, "texture", "texture.cfg")));
        Assert.DoesNotContain("Official demo scheme", File.ReadAllText(Path.Combine(Winning, "livery.cfg")));
    }

    [Fact]
    public void DisplayName_GetsABangAndTheActivityLabel()
    {
        _s.AddSourceLivery("JA351J", displayName: "Japan Airlines (JA351J)");

        ApplyCommercial();

        Assert.Equal("! [Commercial Flights] Japan Airlines (JA351J)", _cfg.ReadLiveryName(Path.Combine(Winning, "livery.cfg")));
    }

    [Fact]
    public void ActivityFolderMissingFromOfficial_SkipsTheBackfill_AndSaysSoInTheNotes()
    {
        _s.AddSourceLivery("JA351J");
        var ghost = new AircraftActivityInfo
        {
            ActivityKey = "commercial",
            DisplayName = "Commercial Flights",
            OfficialFolderName = "ghost_freelance_01",
            DressingCodes = "COF-PCC",
            LicenceTag = "Licence_Airline",
        };

        var result = _s.Apply("JA351J", ghost);

        Assert.Contains(result.Notes, n => n.StartsWith("Missing-part backfill skipped"));
        Assert.False(Directory.Exists(Path.Combine(_s.DestVendor(), "ghost_freelance_01", "model.wing_c")));
    }
}
