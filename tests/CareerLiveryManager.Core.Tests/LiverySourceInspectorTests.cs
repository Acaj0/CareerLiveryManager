using CareerLiveryManager.Core.Services;
using CareerLiveryManager.Core.Tests.Support;

namespace CareerLiveryManager.Core.Tests;

public sealed class LiverySourceInspectorTests : IDisposable
{
    private readonly TestTree _tree = new();
    private readonly LiverySourceInspector _inspector = new();

    public void Dispose() => _tree.Dispose();

    private string Livery(string name, string simObject = "asobo_longitude", string creator = "creator")
    {
        var dir = _tree.At("Pack", "SimObjects", "Airplanes", simObject, "liveries", creator, name);
        _tree.Write(Path.Combine(dir, "livery.cfg"), "[GENERAL]\r\nName=\"x\"\r\n");
        return dir;
    }

    [Fact]
    public void BaseAndItsDrSibling_AreReportedAsOneEntryWithBothPaths()
    {
        var baseDir = Livery("C700_N282N");
        var drDir = Livery("C700_N282N_DR");

        var result = _inspector.Inspect(_tree.At("Pack"));

        var entry = Assert.Single(result);
        Assert.Equal(baseDir, entry.BaseFolderPath);
        Assert.Equal("C700_N282N", entry.BaseFolderName);
        Assert.Equal(drDir, entry.DrFolderPath);
        Assert.Equal("C700_N282N_DR", entry.DrFolderName);
        Assert.True(entry.HasDr);
    }

    [Fact]
    public void LiveryWithoutADrSibling_HasNoDr()
    {
        Livery("N123AB");

        var entry = Assert.Single(_inspector.Inspect(_tree.At("Pack")));

        Assert.False(entry.HasDr);
        Assert.Null(entry.DrFolderPath);
        Assert.Null(entry.DrFolderName);
    }

    [Fact]
    public void DrDetection_IsCaseInsensitive()
    {
        Livery("N123AB");
        Livery("N123AB_dr");

        var entry = Assert.Single(_inspector.Inspect(_tree.At("Pack")));

        Assert.True(entry.HasDr);
    }

    [Fact]
    public void SeveralLiveriesInOnePack_AreReportedSeparately()
    {
        Livery("N111AA");
        Livery("N222BB");
        Livery("N222BB_DR");

        var result = _inspector.Inspect(_tree.At("Pack"));

        Assert.Equal(new[] { "N111AA", "N222BB" }, result.Select(r => r.BaseFolderName).OrderBy(n => n).ToArray());
        Assert.False(result.Single(r => r.BaseFolderName == "N111AA").HasDr);
        Assert.True(result.Single(r => r.BaseFolderName == "N222BB").HasDr);
    }

    [Fact]
    public void FoldersWithoutALiveryCfg_AreNotLiveries()
    {
        Livery("N123AB");
        _tree.Write(_tree.At("Pack", "SimObjects", "Airplanes", "asobo_longitude", "liveries", "creator", "TUI_common", "texture", "x.ktx2"), "x");

        var result = _inspector.Inspect(_tree.At("Pack"));

        Assert.Equal("N123AB", Assert.Single(result).BaseFolderName);
    }

    [Fact]
    public void FolderWithNoLiveryCfgAnywhere_ReturnsNothing()
    {
        _tree.Write(_tree.At("Pack", "readme.txt"), "x");

        Assert.Empty(_inspector.Inspect(_tree.At("Pack")));
    }

    [Theory]
    [InlineData("asobo_b737max")]
    [InlineData("asobo_c172sp_as1000")]
    public void SimObjectName_IsReadFromThePathAfterAirplanes(string simObject)
    {
        Livery("N123AB", simObject);

        var entry = Assert.Single(_inspector.Inspect(_tree.At("Pack")));

        Assert.Equal(simObject, entry.DetectedSimObjectName);
    }

    [Fact]
    public void SimObjectName_IsEmpty_WhenThePathHasNoAirplanesFolder()
    {
        _tree.Write(_tree.At("loose", "MyLivery", "livery.cfg"), "[GENERAL]");

        var entry = Assert.Single(_inspector.Inspect(_tree.At("loose")));

        Assert.Equal(string.Empty, entry.DetectedSimObjectName);
    }

    [Fact]
    public void Thumbnail_PrefersTheShortestName_OverASmallVariant()
    {
        var dir = Livery("N123AB");
        _tree.Write(Path.Combine(dir, "thumbnail", "thumbnail_small.png"), "s");
        _tree.Write(Path.Combine(dir, "thumbnail", "thumbnail.png"), "t");

        var entry = Assert.Single(_inspector.Inspect(_tree.At("Pack")));

        Assert.Equal(Path.Combine(dir, "thumbnail", "thumbnail.png"), entry.ThumbnailPath);
    }

    [Fact]
    public void Thumbnail_AcceptsAJpg_AndIsNullWhenAbsent()
    {
        var withJpg = Livery("WithJpg");
        _tree.Write(Path.Combine(withJpg, "thumbnail", "thumbnail.jpg"), "j");
        Livery("NoThumb");

        var result = _inspector.Inspect(_tree.At("Pack"));

        Assert.Equal(Path.Combine(withJpg, "thumbnail", "thumbnail.jpg"), result.Single(r => r.BaseFolderName == "WithJpg").ThumbnailPath);
        Assert.Null(result.Single(r => r.BaseFolderName == "NoThumb").ThumbnailPath);
    }
}
