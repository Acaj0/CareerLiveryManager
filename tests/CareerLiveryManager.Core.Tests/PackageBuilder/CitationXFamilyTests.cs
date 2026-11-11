using CareerLiveryManager.Core.Models;
using CareerLiveryManager.Core.Services;
using CareerLiveryManager.Core.Tests.Support;

namespace CareerLiveryManager.Core.Tests.PackageBuilderTests;

/// <summary>
/// The Citation X (c750) comes in a standard and a winglets aircraft version. A livery ships one
/// folder pair per version (told apart by required_tags), and the winglets folders borrow their paint
/// from the standard ones - so all four folders have to be installed together. The folder layout
/// below is the real one of a published livery.
/// </summary>
public sealed class CitationXFamilyTests : IDisposable
{
    private const string SimObject = "c750";
    private const string Standard = "C750_N728QL";
    private const string StandardDr = "C750_N728QL_DR";
    private const string Winglets = "C750w_N728QL";
    private const string WingletsDr = "C750w_N728QL_DR";

    private readonly ApplyScenario _s = new(SimObject);
    private readonly LiveryCfgEditor _cfg = new();

    public void Dispose() => _s.Dispose();

    private static string LiveryCfg(string name, string tag) =>
        $"[Version]\r\nmajor = 1\r\nminor = 0\r\n\r\n[GENERAL]\r\nName = {name}\r\n\r\n[FLTSIM]\r\natc_id = N728QL\r\n\r\n[Selection]\r\nrequired_tags = \"{tag}\"\r\n";

    private void AddFamily(bool withWinglets = true)
    {
        _s.AddSourceFile(Standard, "livery.cfg", LiveryCfg("N728QL", "standard"));
        _s.AddSourceFile(Standard, @"texture\body_albd.png.ktx2", "standard-paint");
        _s.AddSourceFile(Standard, @"thumbnail\thumbnail.png", "thumb");

        _s.AddSourceFile(StandardDr, "livery.cfg", LiveryCfg("N728QL (Dynamic Registration)", "standard"));
        _s.AddSourceFile(StandardDr, @"texture\texture.CFG", "[fltsim]\r\nfallback.1 =..\\..\\C750_N728QL\\texture\r\n");
        _s.AddSourceFile(StandardDr, @"texture\tail_logo.png.ktx2", "tail");

        if (!withWinglets)
        {
            return;
        }

        _s.AddSourceFile(Winglets, "livery.cfg", LiveryCfg("N728QL", "winglets"));
        _s.AddSourceFile(Winglets, @"texture\texture.CFG", "[fltsim]\r\nfallback.1 =..\\..\\C750_N728QL\\texture\r\n");

        _s.AddSourceFile(WingletsDr, "livery.cfg", LiveryCfg("N728QL (Dynamic Registration)", "winglets"));
        _s.AddSourceFile(WingletsDr, @"texture\texture.CFG", "[fltsim]\r\nfallback.1 =..\\..\\C750_N728QL_DR\\texture\r\nfallback.2 =..\\..\\C750_N728QL\\texture\r\n");
    }

    private string Vendor => _s.DestVendor();

    private string Fallbacks(string folder) =>
        string.Join("|", _cfg.ReadAllFallbacks(Path.Combine(Vendor, folder, "texture", "texture.CFG")));

    // ---- grouping ---------------------------------------------------------------------------

    [Fact]
    public void TheFourFolders_AreListedAsOneLivery_WithTheWingletsPairAsAVariant()
    {
        AddFamily();

        var entry = new LiverySourceInspector().Inspect(_s.DownloadRoot).Single();

        Assert.Equal(Standard, entry.BaseFolderName);
        Assert.Equal(StandardDr, entry.DrFolderName);
        var variant = Assert.Single(entry.Variants);
        Assert.Equal(Winglets, variant.BaseFolderName);
        Assert.Equal(WingletsDr, variant.DrFolderName);
    }

    [Fact]
    public void AStandardOnlyLivery_IsLeftAlone()
    {
        AddFamily(withWinglets: false);

        var entry = new LiverySourceInspector().Inspect(_s.DownloadRoot).Single();

        Assert.Empty(entry.Variants);
    }

    [Fact]
    public void AWingletsPairThatBorrowsFromNothingInThePack_StaysItsOwnEntry()
    {
        _s.AddSourceFile("Other_ONE", "livery.cfg", LiveryCfg("Other", "standard"));
        _s.AddSourceFile(Winglets, "livery.cfg", LiveryCfg("N728QL", "winglets"));
        _s.AddSourceFile(Winglets, @"texture\texture.CFG", "[fltsim]\r\nfallback.1 =..\\..\\SomewhereElse\\texture\r\n");
        _s.AddSourceFile("Unrelated_name", "livery.cfg", LiveryCfg("x", "winglets"));

        var entries = new LiverySourceInspector().Inspect(_s.DownloadRoot);

        Assert.Equal(3, entries.Count);
        Assert.All(entries, e => Assert.Empty(e.Variants));
    }

    [Fact]
    public void TheSameLayoutOnAnotherAircraft_IsNotGrouped()
    {
        using var other = new ApplyScenario("asobo_cj4");
        foreach (var (folder, tag) in new[] { (Standard, "standard"), (Winglets, "winglets") })
        {
            other.AddSourceFile(folder, "livery.cfg", LiveryCfg("N728QL", tag));
            other.AddSourceFile(folder, @"texture\texture.CFG", "[fltsim]\r\nfallback.1 =..\\..\\C750_N728QL\\texture\r\n");
        }

        var entries = new LiverySourceInspector().Inspect(other.DownloadRoot);

        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Empty(e.Variants));
    }

    // ---- applying ---------------------------------------------------------------------------

    [Fact]
    public void WithDr_AllFourFoldersAreInstalled_TheDrOnesPrefixedAndTheBasesLoose()
    {
        AddFamily();

        var result = _s.Apply(Standard);

        Assert.Equal(
            new[] { "!C750_N728QL_DR", "!C750w_N728QL_DR", "C750_N728QL", "C750w_N728QL" }.OrderBy(x => x),
            Directory.GetDirectories(Vendor).Select(Path.GetFileName).OrderBy(x => x)!);
        Assert.True(File.Exists(Path.Combine(Vendor, Standard, "texture", "body_albd.png.ktx2")));
        Assert.Contains(result.Notes, n => n.StartsWith("Citation X: installed all 2 aircraft versions"));
    }

    [Fact]
    public void WithDr_FallbacksKeepTheCreatorsChain_WithRenamedFoldersUpdated()
    {
        AddFamily();

        _s.Apply(Standard);

        Assert.Equal(@"..\..\C750_N728QL\texture", Fallbacks("!C750_N728QL_DR"));
        Assert.Equal(@"..\..\!C750_N728QL_DR\texture|..\..\C750_N728QL\texture", Fallbacks("!C750w_N728QL_DR"));
        Assert.Equal(@"..\..\C750_N728QL\texture", Fallbacks("C750w_N728QL"));
    }

    [Fact]
    public void WithDr_BothWinnersGetTheBangName_AndKeepTheirRequiredTags()
    {
        AddFamily();

        _s.Apply(Standard);

        Assert.Equal("!C750_N728QL_DR", _cfg.ReadLiveryName(Path.Combine(Vendor, "!C750_N728QL_DR", "livery.cfg")));
        Assert.Equal("!C750w_N728QL_DR", _cfg.ReadLiveryName(Path.Combine(Vendor, "!C750w_N728QL_DR", "livery.cfg")));
        Assert.Equal("standard", _cfg.ReadRequiredTags(Path.Combine(Vendor, "!C750_N728QL_DR", "livery.cfg")));
        Assert.Equal("winglets", _cfg.ReadRequiredTags(Path.Combine(Vendor, "!C750w_N728QL_DR", "livery.cfg")));
    }

    [Fact]
    public void WithoutDr_TheTwoBasesBecomeTheWinners_AndTheDrFoldersAreNotCopied()
    {
        AddFamily();

        _s.Apply(Standard, useDr: false);

        Assert.Equal(
            new[] { "!C750_N728QL", "!C750w_N728QL" },
            Directory.GetDirectories(Vendor).Select(Path.GetFileName).OrderBy(x => x)!);
        Assert.Equal(@"..\..\!C750_N728QL\texture", Fallbacks("!C750w_N728QL"));
        Assert.True(File.Exists(Path.Combine(Vendor, "!C750_N728QL", "texture", "body_albd.png.ktx2")));
    }

    [Fact]
    public void ThePreviewListsTheFilesOfAllFourFolders()
    {
        AddFamily();

        var preview = new PackageBuilder().Preview(_s.Request(Standard), _s.Community);

        Assert.Equal("!C750_N728QL_DR", preview.WinningLiveryName);
        foreach (var folder in new[] { "!C750_N728QL_DR/", "!C750w_N728QL_DR/", "/C750_N728QL/", "/C750w_N728QL/" })
        {
            Assert.Contains(preview.Files, f => f.RelativePath.Contains(folder));
        }
    }

    [Fact]
    public void OnAnotherAircraft_OnlyTheChosenPairIsCopied_AsBefore()
    {
        AddFamily();
        var request = _s.Request(Standard);
        var otherAircraft = new AircraftInfo
        {
            Title = "Other",
            PackageFolder = _s.OfficialPackage,
            ManifestPath = Path.Combine(_s.OfficialPackage, "manifest.json"),
            SimObjectName = "asobo_cj4",
            VendorPath = _s.VendorPath,
            VendorName = "asobo",
            IsSupported = true,
        };

        var result = new PackageBuilder().ApplyWithNotes(
            new ApplyLiveryRequest { Aircraft = otherAircraft, Source = request.Source, UseDr = true, PackageName = "other-pkg" },
            _s.Community);

        var vendor = Path.Combine(result.PackageFolder, "simobjects", "airplanes", "asobo_cj4", "liveries", "asobo");
        Assert.Equal(
            new[] { "!C750_N728QL_DR", "C750_N728QL" }.OrderBy(x => x),
            Directory.GetDirectories(vendor).Select(Path.GetFileName).OrderBy(x => x)!);
        Assert.DoesNotContain(result.Notes, n => n.StartsWith("Citation X"));
    }

    // ---- the fallback rename helper ---------------------------------------------------------

    [Fact]
    public void RenamingFallbackFolders_ReplacesWholeSegmentsOnly_AndKeepsTheRestOfTheFile()
    {
        var path = _s.Tree.Write(
            Path.Combine(_s.Tree.At("cfg"), "texture.cfg"),
            "[fltsim]\r\nfallback.1 =..\\..\\A_DR\\texture\r\nfallback.2=..\\..\\A_DR_OTHER\\texture\r\n; comment A_DR\r\n");

        _cfg.RenameFallbackFolders(path, new Dictionary<string, string> { ["A_DR"] = "!A_DR" });

        Assert.Equal(
            new[] { "[fltsim]", @"fallback.1 =..\..\!A_DR\texture", @"fallback.2=..\..\A_DR_OTHER\texture", "; comment A_DR" },
            File.ReadAllLines(path));
    }
}
