using System.IO.Compression;
using System.Text;
using CareerLiveryManager.Core.Models;
using CareerLiveryManager.Core.Services;
using CareerLiveryManager.Core.Tests.Support;

namespace CareerLiveryManager.Core.Tests;

public sealed class DiagnosticReportBuilderTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0);

    private readonly TestTree _tree = new();
    private readonly DiagnosticReportBuilder _builder = new();

    public void Dispose() => _tree.Dispose();

    // ---- a small but realistic world -------------------------------------------------------------

    private string Official => _tree.At("msfs", "Official2024", "Steam");
    private string Community => _tree.At("msfs", "Community");
    private string LogDir => _tree.At("logs");
    private string ZipPath => _tree.At("out", "report.zip");

    private DiagnosticReportRequest Request(bool redact = true, string? userProfile = null, UserCfgInfo? userCfg = null) => new()
    {
        AppVersion = "v9.9.9",
        Config = new AppConfig { OfficialPath = Official, CommunityPath = Community },
        UserCfg = userCfg ?? new UserCfgInfo(@"C:\Users\Jane Doe\AppData\Roaming\Microsoft Flight Simulator 2024\UserCfg.opt", "Steam", _tree.At("msfs")),
        LogDirectory = LogDir,
        RedactUserName = redact,
        UserProfilePath = userProfile ?? @"C:\Users\Jane Doe",
        Now = Now,
    };

    private void BuildWorld(bool withInstalledPackage = true)
    {
        // One official aircraft with a Cargo activity slot.
        var pkg = Path.Combine(Official, "asobo-aircraft-c172");
        _tree.Write(Path.Combine(pkg, "manifest.json"), "{\"content_type\":\"AIRCRAFT\",\"title\":\"Cessna 172\"}");
        _tree.Write(Path.Combine(pkg, "simobjects", "airplanes", "asobo_c172sp", "liveries", "asobo", "cargo_freelance_01", "thumbnail", "thumbnail.png"), "t");
        Directory.CreateDirectory(Community);

        if (withInstalledPackage)
        {
            var package = Path.Combine(Community, "career-livery-c172-cargo");
            _tree.Write(Path.Combine(package, "manifest.json"),
                "{\"creator\":\"CareerLiveryManager\",\"title\":\"Career Livery - C172\",\"career_simobject\":\"asobo_c172sp\",\"career_activity\":\"cargo\",\"career_activity_folder\":\"cargo_freelance_01\"}");
            var winning = Path.Combine(package, "simobjects", "airplanes", "asobo_c172sp", "liveries", "asobo", "cargo_freelance_01");
            _tree.Write(Path.Combine(winning, "livery.cfg"), "[GENERAL]\r\nName=\"My C172\"\r\n[Tags]\r\ntag.0 = \"Freelance\"\r\n");
            _tree.Write(Path.Combine(winning, "texture", "texture.cfg"), "[fltsim]\r\nfallback.1=..\\_fallback_base\\texture\r\n");
            _tree.WriteBytes(Path.Combine(winning, "texture", "body_albd.png.ktx2"), new byte[] { 1, 2, 3, 4, 5 });
            _tree.Write(Path.Combine(winning, "thumbnail", "thumbnail.png"), "thumb");
        }
    }

    private Dictionary<string, string> BuildAndRead(DiagnosticReportRequest request)
    {
        _builder.Build(request, ZipPath);
        using var zip = ZipFile.OpenRead(ZipPath);
        return zip.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var reader = new StreamReader(e.Open(), Encoding.UTF8);
            return reader.ReadToEnd();
        });
    }

    // ---- what goes in --------------------------------------------------------------------------------

    [Fact]
    public void Zip_ContainsTheDocumentedFiles()
    {
        BuildWorld();
        _tree.Write(Path.Combine(LogDir, "app.log"), "[2026-10-07 11:00:00] [INFO] hello\r\n");

        var files = BuildAndRead(Request());

        Assert.Equal(
            new[]
            {
                "aircraft.txt", "app.log", "config.json", "installed-packages/career-livery-c172-cargo/layout-summary.txt",
                "installed-packages/career-livery-c172-cargo/manifest.json", "setup-validation.txt", "summary.txt", "usercfg-packages-path.txt",
            },
            files.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Summary_HasVersionEnvironmentAndTheSetupFacts()
    {
        BuildWorld();

        var summary = BuildAndRead(Request(redact: false))["summary.txt"];

        Assert.Contains("App version: v9.9.9", summary);
        Assert.Contains("Windows:", summary);
        Assert.Contains(".NET runtime:", summary);
        Assert.Contains("System language:", summary);
        Assert.Contains("MSFS storefront (from where UserCfg.opt was found): Steam", summary);
        Assert.Contains($"Official folder: {Official}", summary);
        Assert.Contains("Aircraft detected: 1 (1 with Career activities)", summary);
        Assert.Contains("Packages installed by this app: 1", summary);
        Assert.Contains("Setup check: 0 problem(s)", summary);
    }

    [Fact]
    public void Aircraft_ListsSimObjectVendorAndActivities()
    {
        BuildWorld();

        var aircraft = BuildAndRead(Request())["aircraft.txt"];

        Assert.Contains("simobject=asobo_c172sp", aircraft);
        Assert.Contains("vendor=asobo", aircraft);
        Assert.Contains("cargo=cargo_freelance_01", aircraft);
    }

    [Fact]
    public void SetupValidation_ShowsWhatTheValidatorFound_ForTheBrokenSteamSetup()
    {
        // Both folders the same, with only liveries inside: S01 then S05 must be visible without any follow-up questions.
        var shared = _tree.At("Community2024");
        _tree.Write(Path.Combine(shared, "my-livery", "manifest.json"), "{\"content_type\":\"LIVERY\"}");
        var request = new DiagnosticReportRequest
        {
            AppVersion = "v1",
            Config = new AppConfig { OfficialPath = shared, CommunityPath = shared },
            LogDirectory = LogDir,
            UserProfilePath = @"C:\Users\Jane Doe",
            Now = Now,
        };

        var files = BuildAndRead(request);

        Assert.Contains("S01 [Error]", files["setup-validation.txt"]);
        Assert.Contains("S05 [Error]", files["setup-validation.txt"]);
        Assert.Contains("[S01, S05]", files["summary.txt"]);
        Assert.Contains("UserCfg.opt not found", files["summary.txt"]);
    }

    [Fact]
    public void UserCfgFile_OnlyTheInstalledPackagesPathLineIsIncluded()
    {
        BuildWorld();
        var userCfgPath = _tree.Write(_tree.At("UserCfg.opt"), "{Secrets\r\nSomeToken \"abc123\"\r\nInstalledPackagesPath \"D:\\msfs24\"\r\nAnotherSetting 7\r\n}");
        var request = Request(userCfg: new UserCfgInfo(userCfgPath, "Steam", @"D:\msfs24"));

        var files = BuildAndRead(request);

        Assert.Contains("InstalledPackagesPath \"D:\\msfs24\"", files["usercfg-packages-path.txt"]);
        Assert.DoesNotContain("abc123", string.Concat(files.Values));
        Assert.DoesNotContain("AnotherSetting", string.Concat(files.Values));
    }

    // ---- installed packages ------------------------------------------------------------------------------

    [Fact]
    public void LayoutSummary_ListsFilesWithSizes_AndTheTextOfLiveryAndTextureCfg()
    {
        BuildWorld();

        var layout = BuildAndRead(Request())["installed-packages/career-livery-c172-cargo/layout-summary.txt"];

        Assert.Contains("simobjects/airplanes/asobo_c172sp/liveries/asobo/cargo_freelance_01/texture/body_albd.png.ktx2  (5 bytes)", layout);
        Assert.Contains("----- simobjects/airplanes/asobo_c172sp/liveries/asobo/cargo_freelance_01/livery.cfg -----", layout);
        Assert.Contains("tag.0 = \"Freelance\"", layout);
        Assert.Contains("fallback.1=..\\_fallback_base\\texture", layout);
    }

    [Fact]
    public void Binaries_AreListedButTheirBytesNeverIncluded()
    {
        BuildWorld();
        _builder.Build(Request(), ZipPath);

        using var zip = ZipFile.OpenRead(ZipPath);
        Assert.DoesNotContain(zip.Entries, e => e.FullName.EndsWith(".ktx2") || e.FullName.EndsWith(".png") || e.FullName.EndsWith(".gltf"));
    }

    [Fact]
    public void PackagesNotCreatedByThisApp_AreNotIncluded()
    {
        BuildWorld(withInstalledPackage: false);
        _tree.Write(Path.Combine(Community, "someone-elses-addon", "manifest.json"), "{\"creator\":\"Someone Else\"}");

        var files = BuildAndRead(Request());

        Assert.DoesNotContain(files.Keys, k => k.StartsWith("installed-packages/"));
        Assert.DoesNotContain("someone-elses-addon", string.Concat(files.Values));
    }

    [Fact]
    public void HugeFileLists_AreCappedAndTheCapIsStated()
    {
        BuildWorld(withInstalledPackage: false);
        var package = Path.Combine(Community, "career-livery-big");
        _tree.Write(Path.Combine(package, "manifest.json"), "{\"creator\":\"CareerLiveryManager\",\"title\":\"Big\",\"career_simobject\":\"asobo_c172sp\"}");
        for (var i = 0; i < DiagnosticReportBuilder.MaxTreeLines + 25; i++)
        {
            File.WriteAllText(Path.Combine(package, $"f{i:D5}.txt"), "x");
        }

        var layout = BuildAndRead(Request())["installed-packages/career-livery-big/layout-summary.txt"];

        Assert.Contains($"{DiagnosticReportBuilder.MaxTreeLines + 26} file(s)", layout); // + manifest.json
        Assert.Contains("more file(s) not listed", layout);
        Assert.DoesNotContain($"f{DiagnosticReportBuilder.MaxTreeLines + 24:D5}.txt", layout);
    }

    // ---- redaction ---------------------------------------------------------------------------------------

    [Fact]
    public void WindowsUserName_IsRedactedInEveryFile_ByDefault()
    {
        BuildWorld();
        _tree.Write(Path.Combine(LogDir, "app.log"),
            "[2026-10-07 11:00:00] [INFO] Setup saved: Official='C:\\Users\\Jane Doe\\AppData\\x', Community='c:/users/jane doe/y'\r\n" +
            "[2026-10-07 11:00:01] [ERROR] boom\r\n   at X in C:\\Users\\Jane Doe\\source\\repos\\a.cs:line 5\r\n");
        _tree.Write(Path.Combine(Community, "career-livery-c172-cargo", "simobjects", "airplanes", "asobo_c172sp", "liveries", "asobo", "cargo_freelance_01", "livery.cfg"),
            "[GENERAL]\r\nName=\"x\"\r\nNote=D:\\Users\\Jane Doe\\Downloads\r\n");

        var files = BuildAndRead(Request());

        var everything = string.Concat(files.Values);
        Assert.DoesNotContain("Jane Doe", everything, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"C:\Users\<redacted>\AppData\x", files["app.log"]);
        Assert.Contains(@"c:/users/<redacted>/y", files["app.log"]);
        Assert.Contains(@"D:\Users\<redacted>\Downloads", files["installed-packages/career-livery-c172-cargo/layout-summary.txt"]);
        Assert.Contains(@"C:\Users\<redacted>\AppData\Roaming", files["usercfg-packages-path.txt"]);
    }

    [Fact]
    public void RedactionAlsoCoversEscapedBackslashesInJson()
    {
        var request = new DiagnosticReportRequest
        {
            AppVersion = "v1",
            Config = new AppConfig { OfficialPath = @"C:\Users\Jane Doe\Games\Official2024\Steam", CommunityPath = @"C:\Users\Jane Doe\Games\Community" },
            LogDirectory = LogDir,
            UserProfilePath = @"C:\Users\Jane Doe",
            Now = Now,
        };

        var config = BuildAndRead(request)["config.json"];

        Assert.DoesNotContain("Jane Doe", config);
        Assert.Contains(@"C:\\Users\\<redacted>\\Games", config);
    }

    [Fact]
    public void ProfileFolderOutsideUsers_IsReplacedByAPlaceholder()
    {
        var request = new DiagnosticReportRequest
        {
            AppVersion = "v1",
            Config = new AppConfig { OfficialPath = @"D:\Profiles\jdoe\Games\Official2024\Steam", CommunityPath = @"D:\Profiles\jdoe\Games\Community" },
            LogDirectory = LogDir,
            UserProfilePath = @"D:\Profiles\jdoe",
            Now = Now,
        };

        var summary = BuildAndRead(request)["summary.txt"];

        Assert.DoesNotContain("jdoe", summary);
        Assert.Contains(@"<UserProfile>\Games\Community", summary);
    }

    [Fact]
    public void RedactionOff_LeavesPathsAsTheyAre()
    {
        BuildWorld();

        var files = BuildAndRead(Request(redact: false));

        Assert.Contains(@"C:\Users\Jane Doe\AppData", files["usercfg-packages-path.txt"]);
    }

    // ---- the log -----------------------------------------------------------------------------------------

    [Fact]
    public void Log_OnlyTheLastSevenDaysAreIncluded()
    {
        BuildWorld();
        _tree.Write(Path.Combine(LogDir, "app.log"),
            "[2026-09-01 10:00:00] [INFO] ancient entry\r\n" +
            "[2026-09-29 10:00:00] [INFO] eight days ago\r\n" +
            "[2026-10-01 10:00:00] [INFO] six days ago\r\n" +
            "[2026-10-07 11:59:00] [INFO] a minute ago\r\n");

        var log = BuildAndRead(Request())["app.log"];

        Assert.DoesNotContain("ancient entry", log);
        Assert.DoesNotContain("eight days ago", log);
        Assert.Contains("six days ago", log);
        Assert.Contains("a minute ago", log);
        Assert.Contains("log trimmed", log);
    }

    [Fact]
    public void Log_MultiLineEntriesStayInOnePiece()
    {
        BuildWorld();
        _tree.Write(Path.Combine(LogDir, "app.log"),
            "[2026-09-01 10:00:00] [ERROR] old failure\r\n   at OldFrame()\r\n" +
            "[2026-10-07 11:00:00] [ERROR] new failure\r\nSystem.Exception: x\r\n   at NewFrame()\r\n");

        var log = BuildAndRead(Request())["app.log"];

        Assert.DoesNotContain("OldFrame", log);
        Assert.Contains("new failure", log);
        Assert.Contains("   at NewFrame()", log);
    }

    [Fact]
    public void Log_IsCappedAtTwoMegabytes_KeepingTheNewestEntries()
    {
        BuildWorld();
        var sb = new StringBuilder();
        var padding = new string('x', 1000);
        for (var i = 0; i < 3000; i++)
        {
            sb.Append($"[2026-10-07 11:{i / 60 % 60:D2}:{i % 60:D2}] [INFO] entry-{i:D4} {padding}\r\n");
        }

        _tree.Write(Path.Combine(LogDir, "app.log"), sb.ToString());

        var log = BuildAndRead(Request())["app.log"];

        Assert.True(Encoding.UTF8.GetByteCount(log) <= DiagnosticReportBuilder.LogMaxBytes + 200);
        Assert.Contains("entry-2999", log);
        Assert.DoesNotContain("entry-0000", log);
    }

    [Fact]
    public void Log_RolledFilesAreReadInOrder()
    {
        BuildWorld();
        _tree.Write(Path.Combine(LogDir, "app.2.log"), "[2026-10-06 10:00:00] [INFO] from-oldest\r\n");
        _tree.Write(Path.Combine(LogDir, "app.1.log"), "[2026-10-06 11:00:00] [INFO] from-middle\r\n");
        _tree.Write(Path.Combine(LogDir, "app.log"), "[2026-10-07 10:00:00] [INFO] from-current\r\n");

        var log = BuildAndRead(Request())["app.log"];

        Assert.True(log.IndexOf("from-oldest", StringComparison.Ordinal) < log.IndexOf("from-middle", StringComparison.Ordinal));
        Assert.True(log.IndexOf("from-middle", StringComparison.Ordinal) < log.IndexOf("from-current", StringComparison.Ordinal));
    }

    [Fact]
    public void Log_ReadableWhileAnotherProcessHasItOpenForWriting()
    {
        BuildWorld();
        var logPath = _tree.Write(Path.Combine(LogDir, "app.log"), "[2026-10-07 11:00:00] [INFO] locked but readable\r\n");

        using var writer = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read | FileShare.Write);
        var files = BuildAndRead(Request());

        Assert.Contains("locked but readable", files["app.log"]);
    }

    [Fact]
    public void Log_MissingLog_IsReportedNotFatal()
    {
        BuildWorld();

        var files = BuildAndRead(Request());

        Assert.Contains("empty or was not found", files["app.log"]);
    }

    // ---- robustness ---------------------------------------------------------------------------------------

    [Fact]
    public void ExistingReportAtTheSamePath_IsReplaced_AndNoTempFileIsLeftBehind()
    {
        BuildWorld();
        _tree.Write(ZipPath, "old junk");

        _builder.Build(Request(), ZipPath);

        using var zip = ZipFile.OpenRead(ZipPath);
        Assert.NotEmpty(zip.Entries);
        Assert.False(File.Exists(ZipPath + ".tmp"));
    }

    [Fact]
    public void MissingOfficialAndCommunityFolders_StillProduceAReport()
    {
        var request = new DiagnosticReportRequest
        {
            AppVersion = "v1",
            Config = new AppConfig { OfficialPath = _tree.At("nowhere", "Official2024"), CommunityPath = _tree.At("nowhere", "Community") },
            LogDirectory = LogDir,
            UserProfilePath = @"C:\Users\Jane Doe",
            Now = Now,
        };

        var files = BuildAndRead(request);

        Assert.Contains("S11", files["setup-validation.txt"]);
        Assert.Contains("skipped", files["aircraft.txt"]);
    }

    [Fact]
    public void Summary_ForTheClipboard_IsTheSameFactsRedacted()
    {
        BuildWorld();

        var summary = _builder.BuildSummary(Request());

        Assert.Contains("App version: v9.9.9", summary);
        Assert.Contains("Packages installed by this app: 1", summary);
        // The test folders live under the real user's temp folder, so their path must come out redacted.
        Assert.DoesNotContain($@"\Users\{Environment.UserName}\", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"\Users\<redacted>\", summary);
    }
}
