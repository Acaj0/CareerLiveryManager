using System.Text.Json;
using CareerLiveryManager.Core.Services;
using CareerLiveryManager.Core.Tests.Support;

namespace CareerLiveryManager.Core.Tests.PackageBuilderTests;

public sealed class ManifestAndLayoutTests : IDisposable
{
    private readonly ApplyScenario _s = new("asobo_c172sp");

    public ManifestAndLayoutTests()
    {
        _s.AddOfficialFile("cargo_freelance_01", @"thumbnail\thumbnail.png", "t");
    }

    public void Dispose() => _s.Dispose();

    private JsonElement ReadManifest(string packageName = ApplyScenario.DefaultPackageName)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(_s.PackageFolder(packageName), "manifest.json")));
        return doc.RootElement.Clone();
    }

    [Fact]
    public void Manifest_IdentifiesThePackageAsOursAndAsALivery()
    {
        _s.AddSourceLivery("N123AB");

        _s.Apply("N123AB");

        var manifest = ReadManifest();
        Assert.Equal("CareerLiveryManager", manifest.GetProperty("creator").GetString());
        Assert.Equal("LIVERY", manifest.GetProperty("content_type").GetString());
        Assert.Equal("Community", manifest.GetProperty("export_type").GetString());
        Assert.Equal("Career Livery - Test Aircraft", manifest.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(manifest.GetProperty("package_version").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(manifest.GetProperty("minimum_game_version").GetString()));
    }

    [Fact]
    public void Manifest_ForASingleLiveryAircraft_HasNoActivityFields()
    {
        _s.AddSourceLivery("N123AB");

        _s.Apply("N123AB");

        var manifest = ReadManifest();
        Assert.Equal("asobo_c172sp", manifest.GetProperty("career_simobject").GetString());
        Assert.Equal(string.Empty, manifest.GetProperty("career_activity").GetString());
        Assert.Equal(string.Empty, manifest.GetProperty("career_activity_folder").GetString());
    }

    [Fact]
    public void Manifest_ForAnActivity_RecordsTheSlotItTargets()
    {
        _s.AddSourceLivery("N123AB");
        var activity = _s.Activity("cargo");

        _s.Apply("N123AB", activity);

        var manifest = ReadManifest();
        Assert.Equal("Career Livery - Test Aircraft - Cargo Transport", manifest.GetProperty("title").GetString());
        Assert.Equal("cargo", manifest.GetProperty("career_activity").GetString());
        Assert.Equal("Cargo Transport", manifest.GetProperty("career_activity_display").GetString());
        Assert.Equal("cargo_freelance_01", manifest.GetProperty("career_activity_folder").GetString());
    }

    [Fact]
    public void Layout_ListsEveryFileUnderSimobjects_WithItsRealSize()
    {
        _s.AddSourceLivery("N123AB");
        _s.AddSourceLivery("N123AB_DR");

        _s.Apply("N123AB", _s.Activity("cargo"));

        var package = _s.PackageFolder();
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(package, "layout.json")));
        var entries = doc.RootElement.GetProperty("content").EnumerateArray()
            .ToDictionary(e => e.GetProperty("path").GetString()!, e => e.GetProperty("size").GetInt64());

        var actual = Directory.EnumerateFiles(Path.Combine(package, "simobjects"), "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(package, f).Replace('\\', '/'), f => new FileInfo(f).Length);

        Assert.Equal(actual.OrderBy(kv => kv.Key), entries.OrderBy(kv => kv.Key));
        Assert.All(entries.Keys, p => Assert.StartsWith("simobjects/", p));
    }

    [Fact]
    public void Layout_DoesNotListTheManifestOrItself()
    {
        _s.AddSourceLivery("N123AB");

        _s.Apply("N123AB");

        var text = File.ReadAllText(Path.Combine(_s.PackageFolder(), "layout.json"));
        Assert.DoesNotContain("manifest.json", text);
        Assert.DoesNotContain("layout.json", text);
    }

    [Fact]
    public void Layout_EntriesCarryAWindowsFileTimeDate()
    {
        _s.AddSourceLivery("N123AB");

        _s.Apply("N123AB");

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(_s.PackageFolder(), "layout.json")));
        // 2000-01-01 as a Windows FILETIME is ~1.3e17; anything built from a Unix timestamp would be 1e9-ish.
        Assert.All(doc.RootElement.GetProperty("content").EnumerateArray(), e => Assert.True(e.GetProperty("date").GetInt64() > 125_000_000_000_000_000L));
    }

    // ---- Preview ------------------------------------------------------------------------

    private static string Rel(string folder, string file) => Path.GetRelativePath(folder, file).Replace('\\', '/');

    [Fact]
    public void Preview_WritesNothing()
    {
        _s.AddSourceLivery("N123AB");

        var preview = new PackageBuilder().Preview(_s.Request("N123AB"), _s.Community);

        Assert.Empty(Directory.EnumerateFileSystemEntries(_s.Community));
        Assert.NotEmpty(preview.Files);
        Assert.Contains("\"creator\": \"CareerLiveryManager\"", preview.ManifestJson);
    }

    [Fact]
    public void Preview_ListsTheFilesThatApplyThenWrites_ForPlainAndDrRecipes()
    {
        _s.AddSourceLivery("N123AB");
        _s.AddSourceLivery("N123AB_DR");
        var request = _s.Request("N123AB");

        var preview = new PackageBuilder().Preview(request, _s.Community);
        new PackageBuilder().Apply(request, _s.Community);

        var package = _s.PackageFolder();
        var written = Directory.EnumerateFiles(Path.Combine(package, "simobjects"), "*", SearchOption.AllDirectories)
            .Select(f => Rel(package, f))
            .OrderBy(p => p)
            .ToList();
        Assert.Equal(written, preview.Files.Select(f => f.RelativePath).OrderBy(p => p).ToList());
        Assert.Equal("!N123AB_DR", preview.WinningLiveryName);
    }

    [Fact]
    public void Preview_ListsTheFilesThatApplyThenWrites_ForAnActivityWithoutDr()
    {
        _s.AddSourceLivery("N123AB");
        var request = _s.Request("N123AB", _s.Activity("cargo"));

        var preview = new PackageBuilder().Preview(request, _s.Community);
        new PackageBuilder().Apply(request, _s.Community);

        var package = _s.PackageFolder();
        var written = Directory.EnumerateFiles(Path.Combine(package, "simobjects"), "*", SearchOption.AllDirectories)
            .Select(f => Rel(package, f))
            .OrderBy(p => p)
            .ToList();
        Assert.Equal(written, preview.Files.Select(f => f.RelativePath).OrderBy(p => p).ToList());
        Assert.Equal("cargo_freelance_01", preview.WinningLiveryName);
    }

    /// <summary>Regression: Preview used to list the base under '&lt;BaseName&gt;' while Apply nests it
    /// under '&lt;slot&gt;/_fallback_base', so the confirmation screen showed paths that were never written.</summary>
    [Fact]
    public void Preview_ListsTheFilesThatApplyThenWrites_ForAnActivityWithDr()
    {
        _s.AddSourceLivery("N123AB");
        _s.AddSourceLivery("N123AB_DR");
        var request = _s.Request("N123AB", _s.Activity("cargo"));

        var preview = new PackageBuilder().Preview(request, _s.Community);
        new PackageBuilder().Apply(request, _s.Community);

        var package = _s.PackageFolder();
        var written = Directory.EnumerateFiles(Path.Combine(package, "simobjects"), "*", SearchOption.AllDirectories)
            .Select(f => Rel(package, f))
            .OrderBy(p => p)
            .ToList();
        Assert.Equal(written, preview.Files.Select(f => f.RelativePath).OrderBy(p => p).ToList());
    }
}
