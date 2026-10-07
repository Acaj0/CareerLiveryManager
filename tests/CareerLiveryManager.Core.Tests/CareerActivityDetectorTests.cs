using CareerLiveryManager.Core.Services;
using CareerLiveryManager.Core.Tests.Support;

namespace CareerLiveryManager.Core.Tests;

public sealed class CareerActivityDetectorTests : IDisposable
{
    private readonly TestTree _tree = new();
    private readonly CareerActivityDetector _detector = new();

    public void Dispose() => _tree.Dispose();

    private string Vendor => _tree.At("liveries", "asobo");

    private void Slots(params string[] names)
    {
        foreach (var name in names)
        {
            Directory.CreateDirectory(Path.Combine(Vendor, name));
        }
    }

    [Fact]
    public void MissingFolder_ReturnsNothing()
    {
        Assert.Empty(_detector.DetectActivities(_tree.At("does-not-exist")));
    }

    [Fact]
    public void AircraftWithNoSlotNaming_HasNoActivities()
    {
        Slots("01", "02_special", "livery_blue");

        Assert.Empty(_detector.DetectActivities(Vendor));
    }

    [Fact]
    public void FreelanceSlots_BecomeOneActivityEach_WithTheDocumentedCodes()
    {
        Slots("cargo_freelance_01", "flightseeing_freelance_01");

        var result = _detector.DetectActivities(Vendor);

        var cargo = result.Single(a => a.ActivityKey == "cargo");
        Assert.Equal("Cargo Transport", cargo.DisplayName);
        Assert.Equal("cargo_freelance_01", cargo.OfficialFolderName);
        Assert.False(cargo.IsGenericSlot);
        Assert.Equal("CAR-PSO, CAR-PLC, CAR-PCC, CAR-PVO, CHT-ROH", cargo.DressingCodes);
        Assert.Equal("Licence_CargoTransport", cargo.LicenceTag);
        Assert.Equal("Licence_Tour", result.Single(a => a.ActivityKey == "flightseeing").LicenceTag);
    }

    [Fact]
    public void SeveralNumbersForOneActivity_UseTheLowest()
    {
        Slots("cargo_freelance_03", "cargo_freelance_01", "cargo_freelance_02");

        var cargo = Assert.Single(_detector.DetectActivities(Vendor));

        Assert.Equal("cargo_freelance_01", cargo.OfficialFolderName);
    }

    [Fact]
    public void SlotNames_AreMatchedCaseInsensitively()
    {
        Slots("Cargo_Freelance_01");

        var cargo = Assert.Single(_detector.DetectActivities(Vendor));

        Assert.Equal("cargo", cargo.ActivityKey, ignoreCase: true);
    }

    [Fact]
    public void EmployeeModeSlots_AreIgnored()
    {
        Slots("commercial_adaptivergnl_01", "commercial_adaptiveintl_01", "medevac_static_01");

        Assert.Empty(_detector.DetectActivities(Vendor));
    }

    [Fact]
    public void FreelanceSlotWithAnUndocumentedActivity_IsSkippedRatherThanGuessed()
    {
        Slots("mystery_freelance_01", "cargo_freelance_01");

        var result = _detector.DetectActivities(Vendor);

        Assert.Equal("cargo", Assert.Single(result).ActivityKey);
    }

    [Fact]
    public void GenericSlot_IsTheLowestNumberedOfficialStatic_AndComesLast()
    {
        Slots("official_static_02", "official_static_01", "cargo_freelance_01");

        var result = _detector.DetectActivities(Vendor);

        Assert.Equal(new[] { "cargo", "official" }, result.Select(a => a.ActivityKey).ToArray());
        var generic = result.Last();
        Assert.True(generic.IsGenericSlot);
        Assert.Equal("official_static_01", generic.OfficialFolderName);
        Assert.Equal("Default", generic.DisplayName);
        Assert.Equal(string.Empty, generic.DressingCodes);
        Assert.Equal(string.Empty, generic.LicenceTag);
    }

    [Fact]
    public void OfficialFreelanceSlot_IsNotTheGenericSlot()
    {
        Slots("official_freelance_01");

        Assert.Empty(_detector.DetectActivities(Vendor));
    }

    [Fact]
    public void Activities_AreOrderedByDisplayName()
    {
        Slots("skydive_freelance_01", "cargo_freelance_01", "agricultural_freelance_01");

        var names = _detector.DetectActivities(Vendor).Select(a => a.DisplayName).ToArray();

        Assert.Equal(new[] { "Agricultural Aviation", "Cargo Transport", "Skydive Aviation" }, names);
    }

    [Fact]
    public void DefaultThumbnail_IsTheSlotsOwnThumbnailImage()
    {
        Slots("cargo_freelance_01");
        var thumb = _tree.Write(Path.Combine(Vendor, "cargo_freelance_01", "thumbnail", "thumbnail.png"), "x");
        _tree.Write(Path.Combine(Vendor, "cargo_freelance_01", "texture", "body.png"), "not a thumbnail");

        var cargo = Assert.Single(_detector.DetectActivities(Vendor));

        Assert.Equal(thumb, cargo.DefaultThumbnailPath);
    }
}
