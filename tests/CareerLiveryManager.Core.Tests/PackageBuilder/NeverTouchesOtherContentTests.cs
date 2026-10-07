using CareerLiveryManager.Core.Tests.Support;

namespace CareerLiveryManager.Core.Tests.PackageBuilderTests;

/// <summary>
/// ROADMAP principles 1-3: Official content, the user's livery download and packages the app
/// didn't create are never modified, whichever recipe runs.
/// </summary>
public sealed class NeverTouchesOtherContentTests
{
    /// <summary>Builds the fake world for one recipe and returns the action that applies it.</summary>
    private static (ApplyScenario Scenario, Action Apply) Prepare(string recipe)
    {
        switch (recipe)
        {
            case "plain":
            {
                var s = new ApplyScenario("asobo_longitude");
                s.AddOfficialSlotWithParts("official_static_01", "model.airframe");
                s.AddSourceLivery("N123AB");
                return (s, () => s.Apply("N123AB"));
            }
            case "dr":
            {
                var s = new ApplyScenario("asobo_longitude");
                s.AddSourceLivery("N123AB");
                s.AddSourceLivery("N123AB_DR");
                return (s, () => s.Apply("N123AB"));
            }
            case "activity-dr":
            {
                var s = new ApplyScenario("asobo_c172sp");
                s.AddOfficialSlotWithParts("cargo_freelance_01", "model.airframe");
                s.AddSourceLivery("N123AB");
                s.AddSourceLivery("N123AB_DR");
                return (s, () => s.Apply("N123AB", s.Activity("cargo")));
            }
            case "737-backfill":
            {
                var s = new ApplyScenario("asobo_b737max");
                s.AddOfficialSlotWithParts("commercial_freelance_01", "model.airframe", "model.wing_c", "model.tail");
                s.AddNeutralSlot("official_static_10");
                s.AddSourceLivery("JA351J");
                return (s, () => s.Apply("JA351J", s.Activity("commercial")));
            }
            case "caravan-backfill":
            {
                var s = new ApplyScenario("asobo_c208b");
                s.AddOfficialSlotWithParts("cargo_freelance_01", "model.airframe", "model.tail");
                s.AddNeutralSlot("cargo_static_01");
                s.AddSourceLivery("N208AB");
                return (s, () => s.Apply("N208AB", s.Activity("cargo")));
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(recipe));
        }
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("dr")]
    [InlineData("activity-dr")]
    [InlineData("737-backfill")]
    [InlineData("caravan-backfill")]
    public void OfficialContent_AndTheUsersDownload_AreByteForByteUnchanged(string recipe)
    {
        var (scenario, apply) = Prepare(recipe);
        using var _ = scenario;
        var officialBefore = TestTree.Snapshot(scenario.OfficialRoot);
        var downloadBefore = TestTree.Snapshot(scenario.DownloadRoot);

        apply();

        Assert.Equal(officialBefore, TestTree.Snapshot(scenario.OfficialRoot));
        Assert.Equal(downloadBefore, TestTree.Snapshot(scenario.DownloadRoot));
        Assert.NotEmpty(Directory.EnumerateFileSystemEntries(scenario.Community));
    }

    [Fact]
    public void PackagesTheAppDidNotCreate_AreLeftExactlyAsTheyWere()
    {
        using var s = new ApplyScenario("asobo_longitude");
        var other = Path.Combine(s.Community, "some-other-addon");
        s.Tree.Write(Path.Combine(other, "manifest.json"), "{\"creator\":\"Someone Else\"}");
        s.Tree.Write(
            Path.Combine(other, "simobjects", "airplanes", "asobo_longitude", "liveries", "asobo", "!N123AB", "livery.cfg"),
            "[GENERAL]\r\nName=\"theirs\"\r\n");
        var before = TestTree.Snapshot(other);
        s.AddSourceLivery("N123AB");

        s.Apply("N123AB");

        Assert.Equal(before, TestTree.Snapshot(other));
    }
}
