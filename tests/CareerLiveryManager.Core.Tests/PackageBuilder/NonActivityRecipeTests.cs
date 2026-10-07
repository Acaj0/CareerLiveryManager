using CareerLiveryManager.Core.Services;
using CareerLiveryManager.Core.Tests.Support;

namespace CareerLiveryManager.Core.Tests.PackageBuilderTests;

/// <summary>
/// The "!"-prefixed recipe used by aircraft with no Career activities (Longitude, CJ4, A321...).
/// </summary>
public sealed class NonActivityRecipeTests : IDisposable
{
    private const string SimObject = "asobo_longitude";
    private readonly ApplyScenario _s = new(SimObject);
    private readonly LiveryCfgEditor _cfg = new();

    public void Dispose() => _s.Dispose();

    private void AddDrPair()
    {
        _s.AddSourceLivery("C700_N282N", displayName: "Base paint");
        _s.AddSourceLivery("C700_N282N_DR", displayName: "DR paint", fallback1: @"..\..\SOME_OTHER_BASE\texture");
    }

    [Fact]
    public void Dr_KeepsBaseUnprefixed_AndPrefixesTheDrWinner()
    {
        AddDrPair();

        _s.Apply("C700_N282N");

        var vendor = _s.DestVendor();
        Assert.True(Directory.Exists(Path.Combine(vendor, "C700_N282N")));
        Assert.True(Directory.Exists(Path.Combine(vendor, "!C700_N282N_DR")));
        Assert.False(Directory.Exists(Path.Combine(vendor, "!C700_N282N")));
        Assert.False(Directory.Exists(Path.Combine(vendor, "C700_N282N_DR")));
    }

    /// <summary>
    /// Regression for v1.2.1 -> v1.2.3: FixDrFallback's contract changed to "write the value
    /// verbatim" and this branch kept passing the old argument; the first fix then used one ".."
    /// instead of two. The value must be two levels up (texture -> DR folder -> vendor folder) and
    /// must actually resolve to the unprefixed base texture folder.
    /// </summary>
    [Fact]
    public void Dr_Fallback1_GoesTwoLevelsUp_AndResolvesToTheBaseTextureFolder()
    {
        AddDrPair();

        _s.Apply("C700_N282N");

        var vendor = _s.DestVendor();
        var textureCfg = Path.Combine(vendor, "!C700_N282N_DR", "texture", "texture.cfg");
        var fallback = _cfg.ReadFallback1(textureCfg);

        Assert.Equal(@"..\..\C700_N282N\texture", fallback);

        var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(textureCfg)!, fallback!));
        Assert.Equal(Path.Combine(vendor, "C700_N282N", "texture"), resolved);
        Assert.True(Directory.Exists(resolved));
    }

    [Fact]
    public void Dr_RewritesTheWinnersDisplayName_WithTheBangPrefix()
    {
        AddDrPair();

        _s.Apply("C700_N282N");

        var drCfg = Path.Combine(_s.DestVendor(), "!C700_N282N_DR", "livery.cfg");
        Assert.Equal("!C700_N282N_DR", _cfg.ReadLiveryName(drCfg));
    }

    [Fact]
    public void Dr_LeavesTheBaseFoldersOwnFilesUntouched()
    {
        AddDrPair();

        _s.Apply("C700_N282N");

        var baseCfg = Path.Combine(_s.DestVendor(), "C700_N282N", "livery.cfg");
        Assert.Equal("Base paint", _cfg.ReadLiveryName(baseCfg));
        Assert.Equal(@"..\texture", _cfg.ReadFallback1(Path.Combine(_s.DestVendor(), "C700_N282N", "texture", "texture.cfg")));
    }

    [Fact]
    public void SourceHasDr_ButUseDrIsFalse_InstallsOnlyTheBaseAsTheWinner()
    {
        AddDrPair();

        _s.Apply("C700_N282N", useDr: false);

        var vendor = _s.DestVendor();
        Assert.Equal(new[] { "!C700_N282N" }, Directory.EnumerateDirectories(vendor).Select(Path.GetFileName).ToArray());
        Assert.Equal("!C700_N282N", _cfg.ReadLiveryName(Path.Combine(vendor, "!C700_N282N", "livery.cfg")));
    }

    [Fact]
    public void SourceWithoutDr_UseDrTrueIsIgnored_AndTheBaseBecomesTheWinner()
    {
        _s.AddSourceLivery("N123AB");

        _s.Apply("N123AB", useDr: true);

        var vendor = _s.DestVendor();
        Assert.Equal(new[] { "!N123AB" }, Directory.EnumerateDirectories(vendor).Select(Path.GetFileName).ToArray());
        Assert.Equal(@"..\texture", _cfg.ReadFallback1(Path.Combine(vendor, "!N123AB", "texture", "texture.cfg")));
    }

    [Fact]
    public void DisplayNameForAFolderAlreadyStartingWithBang_IsNotDoubled()
    {
        _s.AddSourceLivery("!Already");

        _s.Apply("!Already");

        var cfg = Path.Combine(_s.DestVendor(), "!!Already", "livery.cfg");
        Assert.Equal("!Already", _cfg.ReadLiveryName(cfg));
    }

    [Fact]
    public void LiveryWithoutThumbnail_StillApplies()
    {
        _s.AddSourceLivery("NoThumb", thumbnail: false);

        var result = _s.Apply("NoThumb");

        Assert.True(Directory.Exists(Path.Combine(_s.DestVendor(), "!NoThumb", "texture")));
        Assert.Empty(result.Notes);
    }

    [Fact]
    public void FolderNameWithSpacesBangAndNonAsciiCharacters_Applies()
    {
        const string name = "Café Série 5 !x";
        _s.AddSourceLivery(name, displayName: "Café Série 5");

        _s.Apply(name);

        var cfg = Path.Combine(_s.DestVendor(), "!" + name, "livery.cfg");
        Assert.True(File.Exists(cfg));
        Assert.Equal("!" + name, _cfg.ReadLiveryName(cfg));
    }

    [Fact]
    public void ApplyingTwiceWithTheSamePackageName_Succeeds_AndKeepsTheSameResult()
    {
        AddDrPair();

        _s.Apply("C700_N282N");
        var first = TestTree.Snapshot(_s.PackageFolder());
        _s.Apply("C700_N282N");
        var second = TestTree.Snapshot(_s.PackageFolder());

        Assert.Equal(first.Keys, second.Keys);
    }

    [Fact]
    public void PackageNameWithCharactersInvalidInFileNames_IsSanitised()
    {
        _s.AddSourceLivery("N123AB");

        var result = _s.Apply("N123AB", packageName: "career:livery?x");

        Assert.Equal(Path.Combine(_s.Community, "career-livery-x"), result.PackageFolder);
        Assert.True(Directory.Exists(result.PackageFolder));
    }
}
