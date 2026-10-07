using CareerLiveryManager.Core.Services;
using CareerLiveryManager.Core.Tests.Support;

namespace CareerLiveryManager.Core.Tests;

public sealed class InstalledPackagesManagerTests : IDisposable
{
    private readonly TestTree _tree = new();
    private readonly InstalledPackagesManager _manager = new();

    public void Dispose() => _tree.Dispose();

    private string Community => _tree.At("Community");

    private string Package(string name, string manifestJson)
    {
        var folder = Path.Combine(Community, name);
        _tree.Write(Path.Combine(folder, "manifest.json"), manifestJson);
        return folder;
    }

    [Fact]
    public void MissingCommunityFolder_ListsNothing()
    {
        Assert.Empty(_manager.List(_tree.At("nope")));
    }

    [Fact]
    public void OnlyPackagesWithOurCreatorTag_AreListed()
    {
        Package("ours", "{\"creator\":\"CareerLiveryManager\",\"title\":\"Ours\",\"career_simobject\":\"asobo_c172sp\"}");
        Package("theirs", "{\"creator\":\"Someone Else\",\"title\":\"Theirs\"}");
        Package("no-creator", "{\"title\":\"Nobody\"}");
        Directory.CreateDirectory(Path.Combine(Community, "no-manifest"));

        var result = _manager.List(Community);

        Assert.Equal("Ours", Assert.Single(result).Title);
    }

    [Fact]
    public void CreatorTag_IsMatchedExactly_IncludingCase()
    {
        Package("lookalike", "{\"creator\":\"careerlivierymanager\",\"title\":\"x\"}");

        Assert.Empty(_manager.List(Community));
    }

    [Fact]
    public void MalformedManifest_IsSkipped_NotFatal()
    {
        Package("broken", "{ this is not json");
        Package("ours", "{\"creator\":\"CareerLiveryManager\",\"title\":\"Ours\",\"career_simobject\":\"asobo_c172sp\"}");

        Assert.Equal("Ours", Assert.Single(_manager.List(Community)).Title);
    }

    [Fact]
    public void ActivityFields_AreReadBackFromTheManifest()
    {
        Package(
            "cargo-pkg",
            "{\"creator\":\"CareerLiveryManager\",\"title\":\"T\",\"career_simobject\":\"asobo_c208b\",\"career_activity\":\"cargo\",\"career_activity_display\":\"Cargo Transport\",\"career_activity_folder\":\"cargo_freelance_01\"}");

        var package = Assert.Single(_manager.List(Community));

        Assert.Equal("asobo_c208b", package.AircraftSimObjectName);
        Assert.Equal("cargo", package.ActivityKey);
        Assert.Equal("Cargo Transport", package.ActivityDisplayName);
        Assert.True(package.HasActivity);
    }

    [Fact]
    public void ManifestWithoutCareerFields_HasNoActivity_AndFallsBackToTheFolderForTheSimObject()
    {
        var folder = Package("old-pkg", "{\"creator\":\"CareerLiveryManager\",\"title\":\"Old\"}");
        Directory.CreateDirectory(Path.Combine(folder, "simobjects", "airplanes", "asobo_longitude", "liveries"));

        var package = Assert.Single(_manager.List(Community));

        Assert.Equal("asobo_longitude", package.AircraftSimObjectName);
        Assert.False(package.HasActivity);
    }

    [Fact]
    public void Thumbnail_IsLookedUpInsideTheWinningSlotOnly_NotInACrossSimObjectSibling()
    {
        var folder = Package(
            "c172-g1000",
            "{\"creator\":\"CareerLiveryManager\",\"title\":\"T\",\"career_simobject\":\"asobo_c172sp_as1000\",\"career_activity\":\"cargo\",\"career_activity_folder\":\"cargo_freelance_01\"}");
        var winning = _tree.Write(
            Path.Combine(folder, "simobjects", "airplanes", "asobo_c172sp_as1000", "liveries", "asobo", "cargo_freelance_01", "thumbnail", "thumbnail_winning_long_name.png"), "w");
        _tree.Write(Path.Combine(folder, "simobjects", "airplanes", "asobo_c172sp", "liveries", "kfs", "N733EC", "thumbnail", "thumbnail.png"), "sibling");

        var package = Assert.Single(_manager.List(Community));

        Assert.Equal(winning, package.ThumbnailPath);
    }

    [Fact]
    public void Thumbnail_ForTheBangPrefixedRecipe_PrefersTheWinningFoldersOverTheUnprefixedBase()
    {
        var folder = Package("plain", "{\"creator\":\"CareerLiveryManager\",\"title\":\"T\",\"career_simobject\":\"asobo_longitude\"}");
        var vendor = Path.Combine(folder, "simobjects", "airplanes", "asobo_longitude", "liveries", "asobo");
        _tree.Write(Path.Combine(vendor, "C700_N282N", "thumbnail", "thumbnail.png"), "base");
        var winning = _tree.Write(Path.Combine(vendor, "!C700_N282N_DR", "thumbnail", "thumbnail_long_name.png"), "dr");

        var package = Assert.Single(_manager.List(Community));

        Assert.Equal(winning, package.ThumbnailPath);
    }

    [Fact]
    public void Remove_DeletesOnlyThatPackageFolder()
    {
        var ours = Package("ours", "{\"creator\":\"CareerLiveryManager\"}");
        var other = Package("other", "{\"creator\":\"Someone Else\"}");

        _manager.Remove(ours);

        Assert.False(Directory.Exists(ours));
        Assert.True(Directory.Exists(other));
    }

    [Fact]
    public void Remove_OfAFolderThatIsAlreadyGone_DoesNothing()
    {
        _manager.Remove(Path.Combine(Community, "gone"));
    }
}
