using System.Globalization;
using CareerLiveryManager.Core.Models;
using CareerLiveryManager.Core.Services;
using CareerLiveryManager.Core.Tests.Support;

namespace CareerLiveryManager.Core.Tests;

/// <summary>What the Apply screen tells the user when the folder they chose has no usable livery.</summary>
public sealed class LiverySourceDiagnosisTests : IDisposable
{
    private readonly TestTree _tree = new();
    private readonly LiverySourceInspector _inspector = new();

    public void Dispose() => _tree.Dispose();

    private string Folder(string name = "download")
    {
        var path = _tree.At(name);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void ArchiveFile_PickedDirectly_TellsTheUserToExtractIt()
    {
        var zip = _tree.Write(_tree.At("Boeing_Livery.zip"), "PK");

        var problem = _inspector.Diagnose(zip);

        Assert.Equal(LiverySourceProblemKind.Archive, problem.Kind);
        Assert.Contains("Boeing_Livery.zip", problem.Message);
        Assert.Contains("Extract it with 7-Zip or WinRAR", problem.Message);
    }

    [Theory]
    [InlineData("a.zip")]
    [InlineData("a.RAR")]
    [InlineData("a.7z")]
    public void FolderHoldingOnlyAnArchive_TellsTheUserToExtractIt(string archive)
    {
        var folder = Folder();
        _tree.Write(Path.Combine(folder, archive), "x");

        var problem = _inspector.Diagnose(folder);

        Assert.Equal(LiverySourceProblemKind.Archive, problem.Kind);
        Assert.Contains(archive, problem.Message);
        Assert.Contains("Drop the archive itself", problem.Message);
    }

    [Fact]
    public void ManyArchives_AreListedButCapped()
    {
        var folder = Folder();
        foreach (var n in Enumerable.Range(1, 8))
        {
            _tree.Write(Path.Combine(folder, $"pack{n}.zip"), "x");
        }

        var problem = _inspector.Diagnose(folder);

        Assert.Equal(LiverySourceProblemKind.Archive, problem.Kind);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(problem.Message, @"pack\d\.zip").Count);
        Assert.Contains("Drop the archive itself", problem.Message);
    }

    [Fact]
    public void OldFsxStyleLivery_IsRecognisedByItsAircraftCfg()
    {
        var folder = Folder();
        _tree.Write(Path.Combine(folder, "Baron", "aircraft.cfg"), "[fltsim.0]\r\ntitle=Test\r\n");
        _tree.Write(Path.Combine(folder, "Baron", "texture.red", "body.dds"), "x");

        var problem = _inspector.Diagnose(folder);

        Assert.Equal(LiverySourceProblemKind.LegacyAircraftCfg, problem.Kind);
        Assert.Contains("aircraft.cfg", problem.Message);
        Assert.Contains("MSFS 2020/2024", problem.Message);
    }

    [Fact]
    public void LegacyAircraftCfg_WinsOverAnArchiveNextToIt()
    {
        var folder = Folder();
        _tree.Write(Path.Combine(folder, "aircraft.cfg"), "[fltsim.0]");
        _tree.Write(Path.Combine(folder, "backup.zip"), "x");

        Assert.Equal(LiverySourceProblemKind.LegacyAircraftCfg, _inspector.Diagnose(folder).Kind);
    }

    [Fact]
    public void SimObjectsWithoutAnyLiveryCfg_SaysSo()
    {
        var folder = Folder();
        _tree.Write(Path.Combine(folder, "SimObjects", "Airplanes", "asobo_c172sp", "model", "x.gltf"), "x");

        var problem = _inspector.Diagnose(folder);

        Assert.Equal(LiverySourceProblemKind.SimObjectsWithoutLiveryCfg, problem.Kind);
        Assert.Contains("livery.cfg", problem.Message);
    }

    [Fact]
    public void AnythingElse_AsksForTheFolderThatContainsSimObjects()
    {
        var folder = Folder();
        _tree.Write(Path.Combine(folder, "readme.txt"), "x");

        var problem = _inspector.Diagnose(folder);

        Assert.Equal(LiverySourceProblemKind.NoLiveryFound, problem.Kind);
        Assert.Contains("SimObjects", problem.Message);
    }

    [Fact]
    public void EmptyFolder_IsNoLiveryFound()
    {
        Assert.Equal(LiverySourceProblemKind.NoLiveryFound, _inspector.Diagnose(Folder()).Kind);
    }

    [Fact]
    public void FolderThatNoLongerExists_IsReported()
    {
        Assert.Equal(LiverySourceProblemKind.FolderMissing, _inspector.Diagnose(_tree.At("gone")).Kind);
    }

    [Fact]
    public void ARegularFile_IsNotAFolder()
    {
        var file = _tree.Write(_tree.At("notes.txt"), "x");

        Assert.Equal(LiverySourceProblemKind.NotAFolder, _inspector.Diagnose(file).Kind);
    }

    // ---- aircraft mismatch ----------------------------------------------------------------------------

    [Fact]
    public void Mismatch_SameAircraft_IsNoWarning_IgnoringCase()
    {
        Assert.Null(LiverySourceInspector.DescribeAircraftMismatch("ASOBO_C172SP", "asobo_c172sp"));
    }

    [Fact]
    public void Mismatch_DifferentAircraft_NamesBoth()
    {
        var message = LiverySourceInspector.DescribeAircraftMismatch("asobo_c208b", "asobo_c172sp");

        Assert.Contains("(asobo_c208b)", message);
        Assert.Contains("asobo_c172sp", message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Mismatch_UnknownAircraft_NeverPrintsEmptyParentheses(string detected)
    {
        var message = LiverySourceInspector.DescribeAircraftMismatch(detected, "asobo_c172sp");

        Assert.DoesNotContain("()", message);
        Assert.Contains("Couldn't tell which aircraft", message);
        Assert.Contains("asobo_c172sp", message);
    }
}

public sealed class DiskSpaceAndSizeTests
{
    private const long Mb = 1024 * 1024;

    [Fact]
    public void Evaluate_PlentyOfRoom_IsEnough()
    {
        var check = DiskSpaceChecker.Evaluate(freeBytes: 10_000 * Mb, requiredBytes: 500 * Mb);

        Assert.Equal(DiskSpaceStatus.Enough, check.Status);
        Assert.Equal(500 * Mb, check.RequiredBytes);
    }

    [Fact]
    public void Evaluate_NeedsTheSafetyMarginToo()
    {
        var justTooLittle = 500 * Mb + DiskSpaceChecker.SafetyMarginBytes - 1;
        var justEnough = 500 * Mb + DiskSpaceChecker.SafetyMarginBytes;

        Assert.Equal(DiskSpaceStatus.Low, DiskSpaceChecker.Evaluate(justTooLittle, 500 * Mb).Status);
        Assert.Equal(DiskSpaceStatus.Enough, DiskSpaceChecker.Evaluate(justEnough, 500 * Mb).Status);
    }

    [Fact]
    public void Check_OnARealFolder_ReadsTheDriveFreeSpace()
    {
        var check = DiskSpaceChecker.Check(Path.GetTempPath(), requiredBytes: 1);

        Assert.NotEqual(DiskSpaceStatus.Unknown, check.Status);
        Assert.True(check.FreeBytes > 0);
    }

    [Fact]
    public void Check_OnAnImpossiblePath_IsUnknown_NotAnException()
    {
        var check = DiskSpaceChecker.Check("::not a path::\0", requiredBytes: 1);

        Assert.Equal(DiskSpaceStatus.Unknown, check.Status);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(2048, "2 KB")]
    [InlineData(5 * 1024 * 1024 + 300 * 1024, "5.3 MB")]
    [InlineData(250L * 1024 * 1024, "250 MB")]
    [InlineData(1_288_490_189, "1.2 GB")]
    public void Format_UsesTheRightUnit(long bytes, string expected)
    {
        Assert.Equal(expected, FileSizeFormatter.Format(bytes, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Format_FollowsTheCultureDecimalSeparator()
    {
        Assert.Equal("1,2 GB", FileSizeFormatter.Format(1_288_490_189, new CultureInfo("pt-BR")));
    }

    [Fact]
    public void Preview_TotalsItsFiles()
    {
        var preview = new PackagePreview
        {
            PackageFolder = "p",
            WinningLiveryName = "w",
            ManifestJson = "{}",
            Files = new[]
            {
                new PlannedFile { RelativePath = "a", SizeBytes = 100 },
                new PlannedFile { RelativePath = "b", SizeBytes = 250 },
            },
        };

        Assert.Equal(2, preview.FileCount);
        Assert.Equal(350, preview.TotalBytes);
    }

    [Theory]
    [InlineData(0, 0, "")]
    [InlineData(1, 0, "1 activity")]
    [InlineData(3, 0, "3 activities")]
    [InlineData(2, 1, "2 activities")]
    [InlineData(0, 1, "")]
    public void AircraftBadge_CountsJobsButNotTheDefaultSlot(int freelance, int generic, string expected)
    {
        var activities = Enumerable.Range(0, freelance)
            .Select(i => new AircraftActivityInfo { ActivityKey = $"k{i}", DisplayName = $"D{i}", OfficialFolderName = $"f{i}" })
            .Concat(Enumerable.Range(0, generic)
                .Select(i => new AircraftActivityInfo { ActivityKey = "official", DisplayName = "Default", OfficialFolderName = "official_static_01", IsGenericSlot = true }))
            .ToList();
        var aircraft = new AircraftInfo
        {
            Title = "t", PackageFolder = "p", ManifestPath = "m", SimObjectName = "s", VendorPath = "v", VendorName = "asobo", Activities = activities,
        };

        Assert.Equal(expected, aircraft.ActivityBadgeText);
        Assert.Equal(freelance, aircraft.ActivityCount);
    }
}
