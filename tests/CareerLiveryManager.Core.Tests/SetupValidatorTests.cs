using CareerLiveryManager.Core.Models;
using CareerLiveryManager.Core.Services;
using CareerLiveryManager.Core.Tests.Support;

namespace CareerLiveryManager.Core.Tests;

/// <summary>One test (or a few) per rule id of ROADMAP item 1.</summary>
public sealed class SetupValidatorTests : IDisposable
{
    private readonly TestTree _tree = new();
    private readonly SetupValidator _validator = new();

    public void Dispose() => _tree.Dispose();

    // ---- fixtures ---------------------------------------------------------------------------

    private static string Manifest(string contentType, string? creator = null) =>
        creator is null
            ? $"{{\"content_type\":\"{contentType}\",\"title\":\"x\"}}"
            : $"{{\"content_type\":\"{contentType}\",\"creator\":\"{creator}\",\"title\":\"x\"}}";

    private string Package(string folder, string name, string contentType, string? creator = null)
    {
        var dir = Path.Combine(folder, name);
        _tree.Write(Path.Combine(dir, "manifest.json"), Manifest(contentType, creator));
        return dir;
    }

    /// <summary>A healthy install laid out the way MSFS does it; returns (root, official, community).</summary>
    private (string Root, string Official, string Community) Install(string storefront = "Steam", string name = "install")
    {
        var root = _tree.At(name);
        var official = Path.Combine(root, "Official2024", storefront);
        Package(official, "asobo-aircraft-c172", "AIRCRAFT");
        Package(official, "asobo-livery-x", "LIVERY");
        var community = Path.Combine(root, "Community");
        Directory.CreateDirectory(community);
        return (root, official, community);
    }

    private static string[] Ids(IEnumerable<SetupIssue> issues) => issues.Select(i => i.Id).ToArray();

    private static SetupIssue Single(IEnumerable<SetupIssue> issues, string id) => Assert.Single(issues, i => i.Id == id);

    // ---- healthy setups -------------------------------------------------------------------------

    [Theory]
    [InlineData("Steam")]
    [InlineData("OneStore")]
    public void CorrectInstall_ProducesNoIssuesAtAll(string storefront)
    {
        var (_, official, community) = Install(storefront);

        Assert.Empty(_validator.Validate(official, community));
    }

    [Fact]
    public void CorrectInstall_AgreesWithMsfsOwnUserCfg_SoNoS06()
    {
        var (root, official, community) = Install();
        var userCfg = new UserCfgInfo("UserCfg.opt", "Steam", root);

        Assert.Empty(_validator.Validate(official, community, userCfg));
    }

    [Fact]
    public void ValidationLeavesNoProbeFileBehindInCommunity()
    {
        var (_, official, community) = Install();

        _validator.Validate(official, community);

        Assert.Empty(Directory.EnumerateFileSystemEntries(community));
    }

    [Fact]
    public void SurroundingQuotesWhitespaceAndTrailingSeparators_AreTolerated()
    {
        var (_, official, community) = Install();

        var issues = _validator.Validate($"  \"{official}\\\"  ", community + "\\");

        Assert.Empty(issues);
    }

    // ---- S01 / S02 ------------------------------------------------------------------------------

    [Fact]
    public void S01_SamePathForBoth_IsAnError()
    {
        var (_, official, _) = Install();

        var issues = _validator.Validate(official, official);

        var s01 = Single(issues, "S01");
        Assert.Equal(SetupIssueSeverity.Error, s01.Severity);
    }

    [Theory]
    [InlineData("upper")]
    [InlineData("trailing")]
    [InlineData("quoted")]
    [InlineData("dots")]
    public void S01_IsFoundDespiteDifferentSpellingsOfTheSamePath(string variant)
    {
        var (_, official, _) = Install();
        var other = variant switch
        {
            "upper" => official.ToUpperInvariant(),
            "trailing" => official + Path.DirectorySeparatorChar,
            "quoted" => $"\"{official}\"",
            _ => Path.Combine(official, "..", Path.GetFileName(official)),
        };

        Assert.Contains("S01", Ids(_validator.Validate(official, other)));
    }

    [Fact]
    public void S01_AnnouncedAsFirst_ThenS05_ForTheSteamUsersExactMistake()
    {
        // Both folders pointed at the same "Community2024" folder; to get past the "no manifest"
        // error the user copied their own liveries in, so manifests existed - but only livery ones.
        var shared = _tree.At("Community2024");
        Package(shared, "my-livery-one", "LIVERY");
        Package(shared, "my-livery-two", "LIVERY", creator: "CareerLiveryManager");

        var issues = _validator.Validate(shared, shared);

        Assert.Equal(new[] { "S01", "S05" }, Ids(issues));
        Assert.All(issues, i => Assert.Equal(SetupIssueSeverity.Error, i.Severity));
    }

    [Fact]
    public void S02_CommunityInsideOfficial_IsAnError()
    {
        var (_, official, _) = Install();
        var nested = Path.Combine(official, "Community");
        Directory.CreateDirectory(nested);

        var issues = _validator.Validate(official, nested);

        Assert.Equal(SetupIssueSeverity.Error, Single(issues, "S02").Severity);
        Assert.DoesNotContain("S01", Ids(issues));
    }

    [Fact]
    public void S02_OfficialInsideCommunity_IsAnError()
    {
        var (_, official, community) = Install();
        var nestedOfficial = Path.Combine(community, "Official2024", "Steam");
        Package(nestedOfficial, "asobo-aircraft-c172", "AIRCRAFT");

        Assert.Contains("S02", Ids(_validator.Validate(nestedOfficial, community)));
    }

    [Fact]
    public void S02_SiblingFoldersWithACommonPrefix_AreNotMistakenForNesting()
    {
        var (_, official, _) = Install();
        var community = official + "-community"; // shares a name prefix, but is not inside it
        Directory.CreateDirectory(community);

        Assert.DoesNotContain("S02", Ids(_validator.Validate(official, community)));
    }

    // ---- S03 --------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Steam")]
    [InlineData("OneStore")]
    public void S03_ChoosingTheOfficial2024ParentFolder_OffersTheStorefrontChild(string storefront)
    {
        var (root, official, community) = Install(storefront);
        var parent = Path.Combine(root, "Official2024");

        var issues = _validator.Validate(parent, community);

        var s03 = Single(issues, "S03");
        Assert.Equal(SetupIssueSeverity.Error, s03.Severity);
        Assert.Equal(official, s03.SuggestedFix!.OfficialPath);
        Assert.Null(s03.SuggestedFix.CommunityPath);
        Assert.DoesNotContain("S04", Ids(issues));
    }

    [Fact]
    public void S03_NotOffered_WhenTheChildHasNoAircraft()
    {
        var root = _tree.At("empty-install");
        Directory.CreateDirectory(Path.Combine(root, "Official2024", "Steam"));
        var community = Path.Combine(root, "Community");
        Directory.CreateDirectory(community);

        var issues = _validator.Validate(Path.Combine(root, "Official2024"), community);

        Assert.DoesNotContain("S03", Ids(issues));
        Assert.Contains("S04", Ids(issues));
    }

    [Fact]
    public void S03_NeverOffersToStepIntoAMsfs2020Folder()
    {
        var root = _tree.At("both-sims");
        Package(Path.Combine(root, "Official2020", "Steam"), "asobo-aircraft-c172", "AIRCRAFT");
        var community = Path.Combine(root, "Community");
        Directory.CreateDirectory(community);

        var issues = _validator.Validate(Path.Combine(root, "Official2020"), community);

        Assert.DoesNotContain("S03", Ids(issues));
        Assert.Contains("S04", Ids(issues));
        Assert.Contains("S08", Ids(issues));
    }

    // ---- S04 --------------------------------------------------------------------------------------

    [Fact]
    public void S04_FolderWithNoPackagesAtAll_IsAnError()
    {
        var empty = _tree.At("empty");
        Directory.CreateDirectory(empty);
        var (_, _, community) = Install();

        var s04 = Single(_validator.Validate(empty, community), "S04");

        Assert.Equal(SetupIssueSeverity.Error, s04.Severity);
        Assert.Contains("downloaded in-game", s04.Message);
    }

    [Fact]
    public void S04_FolderWithOnlyNonAircraftPackages_IsAnError()
    {
        var folder = _tree.At("scenery-only");
        Package(folder, "some-airport", "SCENERY");
        Package(folder, "some-misc", "MISC");
        var (_, _, community) = Install();

        var issues = _validator.Validate(folder, community);

        Assert.Contains("S04", Ids(issues));
        Assert.DoesNotContain("S05", Ids(issues));
    }

    [Fact]
    public void S04_PassiveAircraftAreNotRealAircraft()
    {
        var folder = _tree.At("passive-only");
        Package(folder, "asobo-passiveaircraft-c172", "AIRCRAFT");
        var (_, _, community) = Install();

        Assert.Contains("S04", Ids(_validator.Validate(folder, community)));
    }

    [Fact]
    public void S04_UnreadableManifests_DoNotCrashTheValidator()
    {
        var folder = _tree.At("garbled");
        _tree.Write(Path.Combine(folder, "pkg", "manifest.json"), "{ not json");
        var (_, _, community) = Install();

        var issues = _validator.Validate(folder, community);

        Assert.Contains("S04", Ids(issues));
    }

    [Fact]
    public void S04_OneAircraftAmongManyOtherPackages_IsEnough()
    {
        var folder = _tree.At("mixed");
        Package(folder, "a-scenery", "SCENERY");
        Package(folder, "b-livery", "LIVERY");
        Package(folder, "c-aircraft", "AIRCRAFT");
        var (_, _, community) = Install();

        Assert.Empty(_validator.Validate(folder, community));
    }

    // ---- S05 --------------------------------------------------------------------------------------

    [Fact]
    public void S05_EveryPackageAMadeByThisAppOrALivery_IsAnError()
    {
        var folder = _tree.At("copied-liveries");
        Package(folder, "career-livery-x", "MISC", creator: "CareerLiveryManager");
        Package(folder, "third-party-livery", "LIVERY");
        var (_, _, community) = Install();

        var issues = _validator.Validate(folder, community);

        var s05 = Single(issues, "S05");
        Assert.Equal(SetupIssueSeverity.Error, s05.Severity);
        Assert.DoesNotContain("S04", Ids(issues));
    }

    [Fact]
    public void S05_AFolderWithARealAircraftToo_IsNotFlagged()
    {
        var (_, official, community) = Install();
        Package(official, "career-livery-x", "LIVERY", creator: "CareerLiveryManager");

        Assert.DoesNotContain("S05", Ids(_validator.Validate(official, community)));
    }

    // ---- S06 --------------------------------------------------------------------------------------

    [Fact]
    public void S06_CommunityDifferentFromTheOneMsfsUses_IsAWarningWithAFix()
    {
        var (root, official, _) = Install();
        var elsewhere = _tree.At("Community2024");
        Directory.CreateDirectory(elsewhere);
        var userCfg = new UserCfgInfo("UserCfg.opt", "Steam", root);

        var s06 = Single(_validator.Validate(official, elsewhere, userCfg), "S06");

        Assert.Equal(SetupIssueSeverity.Warning, s06.Severity);
        Assert.Equal(Path.Combine(root, "Community"), s06.SuggestedFix!.CommunityPath);
        Assert.Null(s06.SuggestedFix.OfficialPath); // the chosen Official was already right
        Assert.Contains("Community folder", s06.Message);
    }

    [Fact]
    public void S06_GameMovedToAnotherDrive_OfficialAndCommunityBothStale_FixesBoth()
    {
        var oldInstall = Install(name: "old-drive");
        var newInstall = Install(name: "new-drive");
        var userCfg = new UserCfgInfo("UserCfg.opt", "Steam", newInstall.Root);

        var s06 = Single(_validator.Validate(oldInstall.Official, oldInstall.Community, userCfg), "S06");

        Assert.Equal(newInstall.Official, s06.SuggestedFix!.OfficialPath);
        Assert.Equal(newInstall.Community, s06.SuggestedFix.CommunityPath);
        Assert.Contains("Official and Community folders", s06.Message);
    }

    [Fact]
    public void S06_NoUserCfg_MeansTheRuleIsSkipped()
    {
        var (_, official, _) = Install();
        var elsewhere = _tree.At("Community2024");
        Directory.CreateDirectory(elsewhere);

        Assert.DoesNotContain("S06", Ids(_validator.Validate(official, elsewhere, userCfg: null)));
    }

    [Fact]
    public void S06_UserCfgRootSpelledDifferently_StillMatches()
    {
        var (root, official, community) = Install();
        var userCfg = new UserCfgInfo("UserCfg.opt", "Steam", root.ToUpperInvariant() + Path.DirectorySeparatorChar);

        Assert.DoesNotContain("S06", Ids(_validator.Validate(official, community, userCfg)));
    }

    // ---- S07 --------------------------------------------------------------------------------------

    [Fact]
    public void S07_UnwritableCommunity_IsAnErrorShowingTheOsMessage()
    {
        var (_, official, community) = Install();
        var validator = new SetupValidator { WriteProbe = _ => "Access to the path is denied." };

        var s07 = Single(validator.Validate(official, community), "S07");

        Assert.Equal(SetupIssueSeverity.Error, s07.Severity);
        Assert.Contains("Access to the path is denied.", s07.Message);
    }

    [Fact]
    public void S07_CommunityThatDoesNotExistYet_IsNotProbed()
    {
        var (root, official, _) = Install();
        var validator = new SetupValidator { WriteProbe = _ => throw new InvalidOperationException("must not probe") };

        var issues = validator.Validate(official, Path.Combine(root, "NewCommunity"));

        Assert.DoesNotContain("S07", Ids(issues));
    }

    // ---- S08 --------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Official2020", "Steam")]
    [InlineData("Official", "Steam")]
    public void S08_Msfs2020OfficialFolders_AreAWarning(string officialFolderName, string storefront)
    {
        var root = _tree.At("msfs2020");
        var official = Path.Combine(root, officialFolderName, storefront);
        Package(official, "asobo-aircraft-c172", "AIRCRAFT");
        var community = Path.Combine(root, "Community");
        Directory.CreateDirectory(community);

        var s08 = Single(_validator.Validate(official, community), "S08");

        Assert.Equal(SetupIssueSeverity.Warning, s08.Severity);
        Assert.Contains(officialFolderName, s08.Message);
    }

    [Theory]
    [InlineData("Microsoft Flight Simulator")]
    [InlineData("Microsoft.FlightSimulator_8wekyb3d8bbwe")]
    public void S08_Msfs2020CommunityLocations_AreAWarning(string folder)
    {
        var (_, official, _) = Install();
        var community = _tree.At(folder, "Packages", "Community");
        Directory.CreateDirectory(community);

        Assert.Contains("S08", Ids(_validator.Validate(official, community)));
    }

    [Theory]
    [InlineData("Microsoft Flight Simulator 2024")]
    [InlineData("Microsoft.Limitless_8wekyb3d8bbwe")]
    public void S08_Msfs2024Locations_AreNotFlagged(string folder)
    {
        var (_, official, _) = Install();
        var community = _tree.At(folder, "Packages", "Community");
        Directory.CreateDirectory(community);

        Assert.DoesNotContain("S08", Ids(_validator.Validate(official, community)));
    }

    // ---- S09 / S10 --------------------------------------------------------------------------------

    private static AircraftInfo Aircraft(string name, bool withActivities)
    {
        var activities = withActivities
            ? new[] { new AircraftActivityInfo { ActivityKey = "cargo", DisplayName = "Cargo Transport", OfficialFolderName = "cargo_freelance_01" } }
            : Array.Empty<AircraftActivityInfo>();

        return new AircraftInfo
        {
            Title = name,
            PackageFolder = "p",
            ManifestPath = "m",
            SimObjectName = name,
            VendorPath = "v",
            VendorName = "asobo",
            IsSupported = true,
            Activities = activities,
        };
    }

    private static IReadOnlyList<AircraftInfo> Fleet(int count, bool withActivities) =>
        Enumerable.Range(0, count).Select(i => Aircraft($"plane{i}", withActivities)).ToList();

    [Fact]
    public void S09_AircraftFoundButNoneHasActivities_IsInfo()
    {
        var (_, official, _) = Install();

        var s09 = Single(_validator.ValidateScanResults(official, Fleet(10, withActivities: false)), "S09");

        Assert.Equal(SetupIssueSeverity.Info, s09.Severity);
    }

    [Fact]
    public void S09_NotRaised_WhenAtLeastOneAircraftHasActivities_OrNothingWasFound()
    {
        var (_, official, _) = Install();
        var mixed = Fleet(9, false).Append(Aircraft("with-activities", true)).ToList();

        Assert.DoesNotContain("S09", Ids(_validator.ValidateScanResults(official, mixed)));
        Assert.DoesNotContain("S09", Ids(_validator.ValidateScanResults(official, Array.Empty<AircraftInfo>())));
    }

    [Fact]
    public void S10_FewAircraftAndAStreamedPackagesFolder_IsInfo()
    {
        var (root, official, _) = Install();
        Directory.CreateDirectory(Path.Combine(root, "StreamedPackages"));

        var s10 = Single(_validator.ValidateScanResults(official, Fleet(2, withActivities: true)), "S10");

        Assert.Equal(SetupIssueSeverity.Info, s10.Severity);
        Assert.Contains("Content Manager", s10.Message);
    }

    [Fact]
    public void S10_NotRaised_WithoutStreamedPackages_OrWithPlentyOfAircraft()
    {
        var (root, official, _) = Install();

        Assert.DoesNotContain("S10", Ids(_validator.ValidateScanResults(official, Fleet(2, true))));

        Directory.CreateDirectory(Path.Combine(root, "StreamedPackages"));
        Assert.DoesNotContain("S10", Ids(_validator.ValidateScanResults(official, Fleet(SetupValidator.LowAircraftCount, true))));
    }

    // ---- S11 / S12 --------------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void S11_EmptyOfficial_IsAnError(string? official)
    {
        var (_, _, community) = Install();

        Assert.Contains("S11", Ids(_validator.Validate(official, community)));
    }

    [Fact]
    public void S11_OfficialFolderThatDoesNotExist_IsAnErrorNamingThePath()
    {
        var (_, _, community) = Install();
        var missing = _tree.At("nowhere", "Official2024", "Steam");

        var s11 = Single(_validator.Validate(missing, community), "S11");

        Assert.Contains(missing, s11.Message);
    }

    [Fact]
    public void S11_AndS12_InvalidPathCharacters_AreErrorsNotCrashes()
    {
        var issues = _validator.Validate("C:\\bad|path?", "C:\\also<bad>");

        Assert.Contains("S11", Ids(issues));
        Assert.Contains("S12", Ids(issues));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void S12_EmptyCommunity_IsAnError(string? community)
    {
        var (_, official, _) = Install();

        Assert.Contains("S12", Ids(_validator.Validate(official, community)));
    }

    // ---- ordering ---------------------------------------------------------------------------------

    [Fact]
    public void Issues_AreOrderedErrorsThenWarningsThenInfo_ThenById()
    {
        var oldInstall = Install(name: "old");
        var newInstall = Install(name: "new");
        var userCfg = new UserCfgInfo("UserCfg.opt", "Steam", newInstall.Root);
        var validator = new SetupValidator { WriteProbe = _ => "denied" };

        var issues = validator.Validate(oldInstall.Official, oldInstall.Community, userCfg);

        Assert.Equal(new[] { "S07", "S06" }, Ids(issues));
        Assert.Equal(new[] { SetupIssueSeverity.Error, SetupIssueSeverity.Warning }, issues.Select(i => i.Severity).ToArray());
    }
}
