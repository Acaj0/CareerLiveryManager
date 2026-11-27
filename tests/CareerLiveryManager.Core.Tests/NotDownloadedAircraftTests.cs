using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using CareerLiveryManager.Core.Models;
using CareerLiveryManager.Core.Services;
using CareerLiveryManager.Core.Tests.Support;

namespace CareerLiveryManager.Core.Tests;

/// <summary>BETA: listing aircraft that aren't in the Official folder.</summary>
public sealed class NotDownloadedAircraftTests : IDisposable
{
    private readonly TestTree _tree = new();

    private string Official => _tree.At("Official2024", "Steam");
    private string Community => _tree.At("Community");

    public NotDownloadedAircraftTests()
    {
        Directory.CreateDirectory(Official);
        Directory.CreateDirectory(Community);
    }

    public void Dispose() => _tree.Dispose();

    /// <summary>Writes a "minimal.fsarchive" in the layout the game uses.</summary>
    private void AddStreamed(string package, string scheme, params (string Path, string Content)[] files)
    {
        var contents = files.Select(f => Encoding.UTF8.GetBytes(f.Content)).ToList();
        long offset = 0;
        var list = files.Select((f, i) =>
        {
            var entry = new { path = f.Path, byteOffset = offset, byteSize = contents[i].Length };
            offset += contents[i].Length;
            return entry;
        }).ToList();

        var index = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { encryptionSetup = new { scheme }, fileInfoList = list }) + "\0");
        var header = new byte[32];
        "RASA"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), (uint)index.Length);

        _tree.WriteBytes(
            _tree.At("StreamedPackages", package, "content", "minimal.fsarchive"),
            header.Concat(index).Concat(contents.SelectMany(c => c)).ToArray());
    }

    private static (string, string)[] Aircraft(string sim, string vendor, string livery = "default", string? locpak = null)
    {
        var files = new List<(string, string)>
        {
            ($@"simobjects\airplanes\{sim}\common\config\ai.cfg", "x"),
            ($@"simobjects\airplanes\{sim}\liveries\{vendor}\{livery}\livery.cfg", "[GENERAL]\r\nName = A\r\n"),
        };
        if (locpak is not null)
        {
            files.Add(("en-us.locpak", locpak));
        }

        return files.ToArray();
    }

    private static string Locpak(string manufacturer, string model) => JsonSerializer.Serialize(new
    {
        LocalisationPackage = new
        {
            Strings = new Dictionary<string, string>
            {
                ["AIRCRAFT.UI_MANUFACTURER"] = manufacturer,
                ["AIRCRAFT.UI_MODEL"] = model,
            },
        },
    });

    private IReadOnlyList<AircraftInfo> List(IReadOnlyList<CatalogAircraft>? catalog = null, params AircraftInfo[] installed) =>
        new NotDownloadedAircraftProvider(catalog ?? Array.Empty<CatalogAircraft>()).List(Official, installed);

    private static AircraftInfo Installed(string sim) => new()
    {
        Title = sim,
        SimObjectName = sim,
        VendorName = "asobo",
        PackageFolder = "p",
        ManifestPath = "m",
        VendorPath = "v",
        IsSupported = true,
    };

    [Fact]
    public void AStreamedAircraft_IsListedWithItsVendorAndName()
    {
        AddStreamed("fs24-some-jet", "notEncrypted", Aircraft("some_jet", "ffx", locpak: Locpak("Maker", "Jet 1")));

        var aircraft = Assert.Single(List());

        Assert.Equal("some_jet", aircraft.SimObjectName);
        Assert.Equal("ffx", aircraft.VendorName);
        Assert.Equal("Maker Jet 1", aircraft.Title);
        Assert.True(aircraft.IsNotDownloaded);
        Assert.True(aircraft.IsSupported);
        Assert.False(aircraft.HasActivities);
    }

    [Fact]
    public void TheCatalogNameWins_AndTheGamesVendorWins()
    {
        AddStreamed("fs24-some-jet", "notEncrypted", Aircraft("some_jet", "ffx"));

        var aircraft = Assert.Single(List(new[] { new CatalogAircraft("some_jet", "old-vendor", "Nice Name") }));

        Assert.Equal("Nice Name", aircraft.Title);
        Assert.Equal("ffx", aircraft.VendorName);
    }

    [Fact]
    public void ACatalogAircraft_IsListedEvenWithoutStreamedPackages()
    {
        var aircraft = Assert.Single(List(new[] { new CatalogAircraft("some_jet", "ffx", "Some Jet") }));

        Assert.Equal("Some Jet", aircraft.Title);
    }

    [Fact]
    public void ADownloadedAircraft_IsNotListedTwice()
    {
        AddStreamed("fs24-some-jet", "notEncrypted", Aircraft("some_jet", "ffx"));

        Assert.Empty(List(new[] { new CatalogAircraft("some_jet", "ffx", "Some Jet") }, Installed("SOME_JET")));
    }

    [Theory]
    [InlineData("asobo_b737max")]
    [InlineData("asobo_c172sp_g1000")]
    [InlineData("asobo_c208b")]
    [InlineData("microsoft_pc12_ngx")]
    [InlineData("c750")]
    [InlineData("microsoft-a320neo")]
    [InlineData("microsoft-a321")]
    [InlineData("a330-900")]
    public void AircraftWithTheirOwnRules_AreNeverListed(string sim)
    {
        AddStreamed("fs24-" + sim, "notEncrypted", Aircraft(sim, "asobo"));

        Assert.Empty(List(new[] { new CatalogAircraft(sim, "asobo", sim) }));
    }

    [Fact]
    public void AnUnknownAircraftWithCareerSlots_IsNotListed()
    {
        AddStreamed("fs24-new", "notEncrypted", Aircraft("new_plane", "asobo", livery: "cargo_freelance_01"));

        Assert.Empty(List(new[] { new CatalogAircraft("new_plane", "asobo", "New") }));
    }

    [Fact]
    public void EncryptedBrokenPassiveAndLiveryOnlyPackages_AreIgnored()
    {
        AddStreamed("fs24-secret", "aes", Aircraft("secret_jet", "ffx"));
        AddStreamed("fs24-jet-passive", "notEncrypted", Aircraft("passive_jet", "ffx"));
        AddStreamed("fs24-jet-livery", "notEncrypted", (@"simobjects\airplanes\other_jet\liveries\ffx\red\livery.cfg", "x"));
        _tree.Write(_tree.At("StreamedPackages", "fs24-broken", "content", "minimal.fsarchive"), "RASA but nothing else that makes sense here");

        Assert.Empty(List());
    }

    [Fact]
    public void TheBuiltInList_HasOnlySimpleAircraftWithCompleteEntries()
    {
        var catalog = NotDownloadedAircraftProvider.LoadBuiltInCatalog();

        Assert.Contains(catalog, a => a.SimObjectName == "hjet" && a.VendorName == "ffx");
        Assert.Contains(catalog, a => a.SimObjectName == "asobo_longitude" && a.VendorName == "asobo");
        Assert.DoesNotContain(catalog, a => NotDownloadedAircraftProvider.IsExcluded(a.SimObjectName));
        Assert.All(catalog, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.SimObjectName));
            Assert.False(string.IsNullOrWhiteSpace(a.VendorName));
            Assert.False(string.IsNullOrWhiteSpace(a.Title));
        });
        Assert.Equal(catalog.Count, catalog.Select(a => (a.SimObjectName + "|" + a.VendorName).ToLowerInvariant()).Distinct().Count());
    }

    [Fact]
    public void ALiveryCanBeAppliedToANotDownloadedAircraft()
    {
        using var scenario = new ApplyScenario("hjet");
        scenario.AddSourceLivery("Blue");
        var aircraft = Assert.Single(List(new[] { new CatalogAircraft("hjet", "ffx", "HondaJet HA-420") }));

        new PackageBuilder().ApplyWithNotes(
            new ApplyLiveryRequest { Aircraft = aircraft, Source = scenario.Source("Blue"), UseDr = false, PackageName = "career-livery-hjet-Blue" },
            Community);

        var vendor = Path.Combine(Community, "career-livery-hjet-Blue", "simobjects", "airplanes", "hjet", "liveries", "ffx");
        Assert.True(File.Exists(Path.Combine(vendor, "!Blue", "livery.cfg")));
        Assert.True(File.Exists(Path.Combine(Community, "career-livery-hjet-Blue", "layout.json")));
    }

    [Fact]
    public void ThePictureSeenWhileDownloaded_IsReusedAfterwards()
    {
        var picture = _tree.Write(_tree.At("Official", "pkg", "photo.jpg"), "photo");
        var cache = new AircraftThumbnailCache(_tree.At("Cache"));
        var aircraft = Installed("some_jet");
        aircraft.ThumbnailPath = picture;

        cache.Remember(new[] { aircraft });
        File.Delete(picture);

        Assert.Equal("photo", File.ReadAllText(cache.Find("some_jet", Community)!));
    }

    [Fact]
    public void WithoutACachedPicture_ACommunityLiveryThumbnailIsUsed()
    {
        var thumbnail = _tree.Write(
            Path.Combine(Community, "pkg", "SimObjects", "Airplanes", "some_jet", "liveries", "ffx", "red", "thumbnail", "thumbnail.png"), "t");
        var cache = new AircraftThumbnailCache(_tree.At("Cache"));

        Assert.Equal(thumbnail, cache.Find("some_jet", Community));
        Assert.Null(cache.Find("another_jet", Community));
    }
}
