using CareerLiveryManager.Core.Models;
using CareerLiveryManager.Core.Services;
using CareerLiveryManager.Core.Tests.Support;

namespace CareerLiveryManager.Core.Tests;

/// <summary>
/// The PC-12 NGX names its official schemes livery_NN, livery_NN_c / _m / _vip instead of
/// "&lt;activity&gt;_freelance_NN". Roles are detected by that suffix, for this SimObject only.
/// </summary>
public sealed class Pc12ActivityTests : IDisposable
{
    private const string Pc12 = "microsoft_pc12_ngx";

    private readonly TestTree _tree = new();
    private readonly CareerActivityDetector _detector = new();

    public void Dispose() => _tree.Dispose();

    private string Vendor => _tree.At("liveries", "microsoft");

    private void Slots(params string[] names)
    {
        foreach (var name in names)
        {
            Directory.CreateDirectory(Path.Combine(Vendor, name));
        }
    }

    private void RealisticSlots() => Slots(
        "livery_01", "livery_02", "livery_02_c", "livery_03", "livery_03_c", "livery_03_m", "livery_03_vip",
        "livery_04_vip_4", "livery_04_vip_7", "livery_05_vip_5", "livery_05_vip_8", "livery_white");

    [Fact]
    public void RolesAreMappedBySuffix_ToTheLowestNumberedFolder()
    {
        RealisticSlots();

        var result = _detector.DetectActivities(Vendor, Pc12);

        Assert.Equal("livery_02_c", result.Single(a => a.ActivityKey == "cargo").OfficialFolderName);
        Assert.Equal("livery_03_m", result.Single(a => a.ActivityKey == "medevac").OfficialFolderName);
        Assert.Equal("livery_03_vip", result.Single(a => a.ActivityKey == "private").OfficialFolderName);
    }

    [Fact]
    public void VipVariants_AreOrderedByTheirNumbers_NotAlphabetically()
    {
        Slots("livery_05_vip_5", "livery_04_vip_7", "livery_04_vip_4", "livery_01");

        var vip = _detector.DetectActivities(Vendor, Pc12).Single(a => a.ActivityKey == "private");

        Assert.Equal("livery_04_vip_4", vip.OfficialFolderName);
    }

    [Fact]
    public void RoleActivities_CarryTheSameTagsAsEveryOtherAircrafts()
    {
        RealisticSlots();

        var medevac = _detector.DetectActivities(Vendor, Pc12).Single(a => a.ActivityKey == "medevac");

        Assert.Equal("MED-PLN", medevac.DressingCodes);
        Assert.Equal("Licence_Medevac", medevac.LicenceTag);
        Assert.False(medevac.IsGenericSlot);
        Assert.False(medevac.UsesStandardRecipe);
    }

    [Fact]
    public void ADefaultEntry_KeepsTheSingleLiveryRecipeAvailable_AndIsListedLast()
    {
        RealisticSlots();

        var result = _detector.DetectActivities(Vendor, Pc12);

        var last = result[^1];
        Assert.Equal("default", last.ActivityKey);
        Assert.True(last.UsesStandardRecipe);
        Assert.True(last.IsGenericSlot);
        Assert.Equal(3, result.Count(a => !a.IsGenericSlot));
    }

    [Fact]
    public void PlainSchemesAlone_AreNotARole_SoTheAircraftStaysAnOrdinaryOne()
    {
        Slots("livery_01", "livery_02", "livery_white");

        Assert.Empty(_detector.DetectActivities(Vendor, Pc12));
    }

    [Fact]
    public void TheSameFolderNames_OnAnyOtherAircraft_AreNotTreatedAsRoles()
    {
        RealisticSlots();

        Assert.Empty(_detector.DetectActivities(Vendor, "asobo_c208b"));
        Assert.Empty(_detector.DetectActivities(Vendor));
    }

    [Fact]
    public void OnlyTheRolesThatExist_AreListed()
    {
        Slots("livery_01", "livery_03_m");

        var result = _detector.DetectActivities(Vendor, Pc12);

        Assert.Equal(new[] { "medevac", "default" }, result.Select(a => a.ActivityKey));
    }
}
