using CareerLiveryManager.Core.Services;
using CareerLiveryManager.Core.Tests.Support;

namespace CareerLiveryManager.Core.Tests.PackageBuilderTests;

/// <summary>
/// The Microsoft A320neo and A321 and the LVFR A330-900 get a "!000000000_" folder, a "!000 " display
/// name and the Career "Freelance" tags on the winning livery; every other aircraft is unchanged.
/// </summary>
public sealed class AirbusSortNameTests
{
    private static string Cfg(ApplyScenario s, string folder) => Path.Combine(s.DestVendor(), folder, "livery.cfg");

    private static string[] Folders(ApplyScenario s) =>
        Directory.GetDirectories(s.DestVendor()).Select(d => Path.GetFileName(d)!).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    [Theory]
    [InlineData("microsoft-a320neo")]
    [InlineData("microsoft-a321")]
    [InlineData("a330-900")]
    public void WithoutDr_FolderNameAndTagsAreSet(string simObject)
    {
        using var s = new ApplyScenario(simObject);
        s.AddSourceLivery("Delta_N547DE", extraLiveryCfg: "[Selection]\r\nrequired_tags = \"a20n_eng_leap\"\r\n");

        var result = s.Apply("Delta_N547DE", useDr: false);

        Assert.Equal(new[] { "!000000000_Delta_N547DE" }, Folders(s));
        var cfgPath = Cfg(s, "!000000000_Delta_N547DE");
        Assert.Equal("!000 Delta_N547DE", new LiveryCfgEditor().ReadLiveryName(cfgPath));
        var cfg = File.ReadAllText(cfgPath);
        Assert.Contains("dressing_codes = \"COF-PCC, PRC-PCC\"", cfg);
        Assert.Contains("tag.0 = \"Freelance\"", cfg);
        Assert.Contains("tag.1 = \"Licence_Airline\"", cfg);
        Assert.Contains("required_tags = \"a20n_eng_leap\"", cfg);
        Assert.Contains(result.Notes, n => n.StartsWith("Airbus:"));
    }

    [Fact]
    public void WithDr_OnlyTheWinnerIsRenamedAndTagged_AndItsFallbackStillFindsTheBase()
    {
        using var s = new ApplyScenario("microsoft-a321");
        s.AddSourceLivery("Delta");
        s.AddSourceLivery("Delta_DR", fallback1: @"..\..\Delta\texture");

        s.Apply("Delta");

        Assert.Equal(new[] { "!000000000_Delta_DR", "Delta" }, Folders(s));
        Assert.Contains("Freelance", File.ReadAllText(Cfg(s, "!000000000_Delta_DR")));
        Assert.DoesNotContain("Freelance", File.ReadAllText(Cfg(s, "Delta")));
        Assert.Equal(
            @"..\..\Delta\texture",
            new LiveryCfgEditor().ReadFallback1(Path.Combine(s.DestVendor(), "!000000000_Delta_DR", "texture", "texture.cfg")));
    }

    [Fact]
    public void ThePreviewUsesTheSameFolderNameAsApply()
    {
        using var s = new ApplyScenario("microsoft-a320neo");
        s.AddSourceLivery("Zorka");

        var preview = new PackageBuilder().Preview(s.Request("Zorka", useDr: false), s.Community);

        Assert.Equal("!000000000_Zorka", preview.WinningLiveryName);
        Assert.All(preview.Files, f => Assert.Contains("/!000000000_Zorka/", f.RelativePath));
    }

    [Theory]
    [InlineData("microsoft-a330")]
    [InlineData("asobo_longitude")]
    [InlineData("microsoft_pc24")]
    [InlineData("microsoft_sf50")]
    public void EveryOtherAircraft_IsUnchanged(string simObject)
    {
        using var s = new ApplyScenario(simObject);
        s.AddSourceLivery("Some_Livery");

        var result = s.Apply("Some_Livery", useDr: false);

        Assert.Equal(new[] { "!Some_Livery" }, Folders(s));
        Assert.Equal("!Some_Livery", new LiveryCfgEditor().ReadLiveryName(Cfg(s, "!Some_Livery")));
        Assert.DoesNotContain("Freelance", File.ReadAllText(Cfg(s, "!Some_Livery")));
        Assert.DoesNotContain(result.Notes, n => n.StartsWith("Airbus:"));
    }
}
