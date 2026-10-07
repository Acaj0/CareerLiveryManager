using CareerLiveryManager.Core.Services;
using CareerLiveryManager.Core.Tests.Support;

namespace CareerLiveryManager.Core.Tests;

public sealed class LiveryCfgEditorTests : IDisposable
{
    private readonly TestTree _tree = new();
    private readonly LiveryCfgEditor _editor = new();

    public void Dispose() => _tree.Dispose();

    private string Cfg(string content, string fileName = "livery.cfg") => _tree.Write(_tree.At(fileName), content);

    [Fact]
    public void SetLiveryName_KeepsQuotes_WhenTheOriginalHadThem()
    {
        var path = Cfg("[GENERAL]\r\nName=\"Old\"\r\nOther=1\r\n");

        _editor.SetLiveryName(path, "!New");

        Assert.Equal(new[] { "[GENERAL]", "Name=\"!New\"", "Other=1" }, File.ReadAllLines(path));
    }

    [Fact]
    public void SetLiveryName_StaysUnquoted_WhenTheOriginalWasUnquoted()
    {
        var path = Cfg("[GENERAL]\r\nName=Old\r\n");

        _editor.SetLiveryName(path, "!New");

        Assert.Equal(new[] { "[GENERAL]", "Name=!New" }, File.ReadAllLines(path));
    }

    [Fact]
    public void SetLiveryName_ToleratesSpacesAndLowercaseKey()
    {
        var path = Cfg("[GENERAL]\r\n  name = \"Old\"\r\n");

        _editor.SetLiveryName(path, "New");

        Assert.Equal("New", _editor.ReadLiveryName(path));
    }

    [Fact]
    public void SetLiveryName_OnlyReplacesTheFirstNameLine()
    {
        var path = Cfg("[GENERAL]\r\nName=\"First\"\r\n[Other]\r\nName=\"Second\"\r\n");

        _editor.SetLiveryName(path, "Changed");

        Assert.Equal(new[] { "[GENERAL]", "Name=\"Changed\"", "[Other]", "Name=\"Second\"" }, File.ReadAllLines(path));
    }

    [Fact]
    public void ReadLiveryName_StripsQuotes_AndIsNullWhenAbsent()
    {
        Assert.Equal("My Livery", _editor.ReadLiveryName(Cfg("[GENERAL]\r\nName=\"My Livery\"\r\n")));
        Assert.Null(_editor.ReadLiveryName(Cfg("[GENERAL]\r\nOther=1\r\n", "empty.cfg")));
    }

    [Fact]
    public void FixDrFallback_WritesTheGivenValueVerbatim_AndLeavesOtherLinesAlone()
    {
        var path = Cfg("[fltsim]\r\nfallback.1=..\\old\\texture\r\nfallback.2=..\\..\\second\\texture\r\n", "texture.cfg");

        _editor.FixDrFallback(path, @"..\..\C700_N282N\texture");

        Assert.Equal(
            new[] { "[fltsim]", @"fallback.1=..\..\C700_N282N\texture", @"fallback.2=..\..\second\texture" },
            File.ReadAllLines(path));
    }

    [Fact]
    public void FixDrFallback_OnAFileWithoutFallback1_ChangesNothing()
    {
        var path = Cfg("[fltsim]\r\nfallback.2=x\r\n", "texture.cfg");

        _editor.FixDrFallback(path, "anything");

        Assert.Equal(new[] { "[fltsim]", "fallback.2=x" }, File.ReadAllLines(path));
    }

    [Fact]
    public void ReadFallback1_ReturnsTheTrimmedValue_OrNull()
    {
        Assert.Equal(@"..\..\a\texture", _editor.ReadFallback1(Cfg("[fltsim]\r\nFallback.1 =  ..\\..\\a\\texture  \r\n", "a.cfg")));
        Assert.Null(_editor.ReadFallback1(Cfg("[fltsim]\r\nfallback.2=x\r\n", "b.cfg")));
    }

    [Fact]
    public void SetCareerActivityTags_AppendsBothSections()
    {
        var path = Cfg("[GENERAL]\r\nName=\"X\"\r\n");

        _editor.SetCareerActivityTags(path, "CAR-PSO, CAR-PLC", "Licence_CargoTransport");

        Assert.Equal(
            new[]
            {
                "[GENERAL]", "Name=\"X\"", "",
                "[Specialization]", "dressing_codes = \"CAR-PSO, CAR-PLC\"", "",
                "[Tags]", "tag.0 = \"Freelance\"", "tag.1 = \"Licence_CargoTransport\"",
            },
            File.ReadAllLines(path));
    }

    [Fact]
    public void SetCareerActivityTags_ReplacesOldSections_AndKeepsTheOthersIntact()
    {
        var path = Cfg(
            "[GENERAL]\r\nName=\"X\"\r\n\r\n[Specialization]\r\ndressing_codes = \"OLD\"\r\n\r\n[Tags]\r\ntag.0 = \"Old\"\r\n\r\n[Other]\r\nKeep=1\r\n");

        _editor.SetCareerActivityTags(path, "NEW", "Licence_Tour");

        var lines = File.ReadAllLines(path);
        Assert.Equal(1, lines.Count(l => l == "[Specialization]"));
        Assert.Equal(1, lines.Count(l => l == "[Tags]"));
        Assert.DoesNotContain("OLD", string.Join("\n", lines));
        Assert.Contains("[Other]", lines);
        Assert.Contains("Keep=1", lines);
        Assert.Contains("tag.1 = \"Licence_Tour\"", lines);
    }
}
