using System.Text.RegularExpressions;
using CareerLiveryManager.Core.Services;
using CareerLiveryManager.Core.Tests.Support;

namespace CareerLiveryManager.Core.Tests.PackageBuilderTests;

/// <summary>
/// The Career-activity recipe: the livery overwrites the exact official slot folder (no "!"
/// prefix) and freelance slots get [Specialization]/[Tags]. Uses a C172, which has no
/// aircraft-specific behavior of its own.
/// </summary>
public sealed class ActivityRecipeTests : IDisposable
{
    private const string SimObject = "asobo_c172sp";
    private readonly ApplyScenario _s = new(SimObject);
    private readonly LiveryCfgEditor _cfg = new();

    public ActivityRecipeTests()
    {
        _s.AddOfficialFile("cargo_freelance_01", @"thumbnail\thumbnail.png", "t");
        _s.AddOfficialFile("flightseeing_freelance_01", @"thumbnail\thumbnail.png", "t");
        _s.AddOfficialFile("official_static_01", @"thumbnail\thumbnail.png", "t");
    }

    public void Dispose() => _s.Dispose();

    [Fact]
    public void Slot_TheLiveryOverwritesTheExactOfficialFolderName_WithoutABangPrefix()
    {
        _s.AddSourceLivery("N733EC");

        _s.Apply("N733EC", _s.Activity("cargo"));

        var vendor = _s.DestVendor();
        Assert.Equal(new[] { "cargo_freelance_01" }, Directory.EnumerateDirectories(vendor).Select(Path.GetFileName).ToArray());
        Assert.True(File.Exists(Path.Combine(vendor, "cargo_freelance_01", "texture", "body_albd.png.ktx2")));
    }

    [Fact]
    public void Dr_NestsTheBaseInsideTheWinningFolder_SoNothingLooseSitsInTheVendorNamespace()
    {
        _s.AddSourceLivery("N733EC", displayName: "Base");
        _s.AddSourceLivery("N733EC_DR", displayName: "DR", fallback1: @"..\..\OLD\texture");

        _s.Apply("N733EC", _s.Activity("cargo"));

        var vendor = _s.DestVendor();
        Assert.Equal(new[] { "cargo_freelance_01" }, Directory.EnumerateDirectories(vendor).Select(Path.GetFileName).ToArray());
        Assert.True(File.Exists(Path.Combine(vendor, "cargo_freelance_01", "_fallback_base", "texture", "body_albd.png.ktx2")));
        Assert.Equal("DR", _cfg.ReadLiveryName(Path.Combine(vendor, "cargo_freelance_01", "livery.cfg")));
    }

    [Fact]
    public void Dr_Fallback1_PointsOneLevelUpToTheNestedBase_AndResolves()
    {
        _s.AddSourceLivery("N733EC", displayName: "Base");
        _s.AddSourceLivery("N733EC_DR", displayName: "DR", fallback1: @"..\..\OLD\texture");

        _s.Apply("N733EC", _s.Activity("cargo"));

        var winning = Path.Combine(_s.DestVendor(), "cargo_freelance_01");
        var textureCfg = Path.Combine(winning, "texture", "texture.cfg");
        var fallback = _cfg.ReadFallback1(textureCfg);

        Assert.Equal(@"..\_fallback_base\texture", fallback);
        var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(textureCfg)!, fallback!));
        Assert.Equal(Path.Combine(winning, "_fallback_base", "texture"), resolved);
        Assert.True(Directory.Exists(resolved));
    }

    [Fact]
    public void Dr_NestedBasesOwnTextureCfg_IsNotRewritten()
    {
        _s.AddSourceLivery("N733EC", displayName: "Base");
        _s.AddSourceLivery("N733EC_DR", displayName: "DR", fallback1: @"..\..\OLD\texture");

        _s.Apply("N733EC", _s.Activity("cargo"));

        var baseCfg = Path.Combine(_s.DestVendor(), "cargo_freelance_01", "_fallback_base", "texture", "texture.cfg");
        Assert.Equal(@"..\texture", _cfg.ReadFallback1(baseCfg));
    }

    [Fact]
    public void SourceHasDr_ButUseDrIsFalse_CopiesOnlyTheBase()
    {
        _s.AddSourceLivery("N733EC", displayName: "Base");
        _s.AddSourceLivery("N733EC_DR", displayName: "DR");

        _s.Apply("N733EC", _s.Activity("cargo"), useDr: false);

        var winning = Path.Combine(_s.DestVendor(), "cargo_freelance_01");
        Assert.False(Directory.Exists(Path.Combine(winning, "_fallback_base")));
        Assert.Equal("Base", _cfg.ReadLiveryName(Path.Combine(winning, "livery.cfg")));
    }

    [Fact]
    public void FreelanceSlot_GetsSpecializationAndTags()
    {
        _s.AddSourceLivery("N733EC");
        var activity = _s.Activity("cargo");

        _s.Apply("N733EC", activity);

        var text = File.ReadAllText(Path.Combine(_s.DestVendor(), "cargo_freelance_01", "livery.cfg"));
        Assert.Contains("[Specialization]", text);
        Assert.Contains($"dressing_codes = \"{activity.DressingCodes}\"", text);
        Assert.Contains("[Tags]", text);
        Assert.Contains("tag.0 = \"Freelance\"", text);
        Assert.Contains($"tag.1 = \"{activity.LicenceTag}\"", text);
    }

    [Fact]
    public void GenericSlot_GetsNoSpecializationOrTags()
    {
        _s.AddSourceLivery("N733EC");

        _s.Apply("N733EC", _s.Activity("official"));

        var text = File.ReadAllText(Path.Combine(_s.DestVendor(), "official_static_01", "livery.cfg"));
        Assert.DoesNotContain("[Specialization]", text);
        Assert.DoesNotContain("[Tags]", text);
    }

    [Fact]
    public void ExistingSpecializationAndTags_AreReplaced_NotDuplicated()
    {
        _s.AddSourceLivery(
            "N733EC",
            extraLiveryCfg: "\r\n[Specialization]\r\ndressing_codes = \"OLD-CODE\"\r\n\r\n[Tags]\r\ntag.0 = \"Old\"\r\ntag.1 = \"Other\"\r\n");
        var activity = _s.Activity("flightseeing");

        _s.Apply("N733EC", activity);

        var text = File.ReadAllText(Path.Combine(_s.DestVendor(), "flightseeing_freelance_01", "livery.cfg"));
        Assert.Single(Regex.Matches(text, @"^\[Specialization\]", RegexOptions.Multiline));
        Assert.Single(Regex.Matches(text, @"^\[Tags\]", RegexOptions.Multiline));
        Assert.DoesNotContain("OLD-CODE", text);
        Assert.DoesNotContain("\"Old\"", text);
        Assert.Contains($"dressing_codes = \"{activity.DressingCodes}\"", text);
    }

    [Fact]
    public void LiveryDisplayName_IsLeftAlone_OnAircraftWithoutADisplayNameFix()
    {
        _s.AddSourceLivery("N733EC", displayName: "Original Name");

        _s.Apply("N733EC", _s.Activity("cargo"));

        Assert.Equal("Original Name", _cfg.ReadLiveryName(Path.Combine(_s.DestVendor(), "cargo_freelance_01", "livery.cfg")));
    }

    [Fact]
    public void TwoActivitiesOfTheSameLivery_BecomeTwoIndependentPackages()
    {
        _s.AddSourceLivery("N733EC");

        _s.Apply("N733EC", _s.Activity("cargo"), packageName: "career-livery-c172-cargo-N733EC");
        var cargoBefore = TestTree.Snapshot(_s.PackageFolder("career-livery-c172-cargo-N733EC"));
        _s.Apply("N733EC", _s.Activity("flightseeing"), packageName: "career-livery-c172-flightseeing-N733EC");

        Assert.Equal(cargoBefore, TestTree.Snapshot(_s.PackageFolder("career-livery-c172-cargo-N733EC")));
        Assert.True(Directory.Exists(Path.Combine(_s.DestVendor("career-livery-c172-flightseeing-N733EC"), "flightseeing_freelance_01")));
        Assert.False(Directory.Exists(Path.Combine(_s.DestVendor("career-livery-c172-flightseeing-N733EC"), "cargo_freelance_01")));
    }
}
