using System.Text.Json;
using CareerLiveryManager.Core.Models;

namespace CareerLiveryManager.Core.Services;

/// <summary>One aircraft of the built-in beta list: names only, no game content.</summary>
public sealed record CatalogAircraft(string SimObjectName, string VendorName, string Title);

/// <summary>
/// BETA. Lists the simple (one livery for the whole plane) aircraft that aren't in the Official folder,
/// so a livery can be applied to them anyway: the game streams the aircraft when it's opened. Names come
/// from a built-in list plus whatever the game keeps in StreamedPackages.
///
/// Applying a livery to a simple aircraft only needs its SimObject and vendor folder names. Aircraft with
/// Career activities or their own install rules need the Official files and are never listed here.
/// </summary>
public sealed class NotDownloadedAircraftProvider
{
    private const string CatalogResourceName = "CareerLiveryManager.Core.aircraft-catalog.json";

    private static readonly HashSet<string> ExcludedSimObjects = new(StringComparer.OrdinalIgnoreCase)
    {
        // Career activities
        "asobo_b737max", "asobo_c172sp_g1000", "asobo_c208b", "asobo_at802", "asobo_cl415",
        "asobo_h125", "asobo_xcub", "asobo_es30", "asobo_dg1001", "asobo_fa18", "asobo_flydoo",
        "asobo_hotairballoon", "asobo_skyship600", "microsoft_pc12_ngx",
        // Own install rules
        "c750", "microsoft-a320neo", "microsoft-a321", "a330-900",
    };

    private readonly StreamedAircraftScanner _streamedScanner = new();
    private readonly IReadOnlyList<CatalogAircraft> _catalog;

    public NotDownloadedAircraftProvider() : this(LoadBuiltInCatalog())
    {
    }

    internal NotDownloadedAircraftProvider(IReadOnlyList<CatalogAircraft> catalog) => _catalog = catalog;

    public static bool IsExcluded(string simObjectName) => ExcludedSimObjects.Contains(simObjectName);

    public static IReadOnlyList<CatalogAircraft> LoadBuiltInCatalog()
    {
        using var stream = typeof(NotDownloadedAircraftProvider).Assembly.GetManifestResourceStream(CatalogResourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource '{CatalogResourceName}'.");
        return JsonSerializer.Deserialize<List<CatalogAircraft>>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? new List<CatalogAircraft>();
    }

    /// <param name="installed">What the Official folder scan found; none of these SimObjects is listed again.</param>
    public IReadOnlyList<AircraftInfo> List(string officialPath, IReadOnlyList<AircraftInfo> installed)
    {
        var onDisk = installed.Select(a => a.SimObjectName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var streamed = _streamedScanner.Scan(officialPath);
        var withActivities = streamed.Where(s => s.HasActivitySlots).Select(s => s.SimObjectName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        bool Eligible(string sim) => !onDisk.Contains(sim) && !IsExcluded(sim) && !withActivities.Contains(sim);

        var result = new Dictionary<string, AircraftInfo>(StringComparer.OrdinalIgnoreCase);

        // What the game itself says comes first: it has the vendor folder this install actually uses.
        foreach (var s in streamed.Where(s => Eligible(s.SimObjectName)))
        {
            var known = _catalog.FirstOrDefault(c => string.Equals(c.SimObjectName, s.SimObjectName, StringComparison.OrdinalIgnoreCase));
            var title = known?.Title ?? s.Title ?? s.SimObjectName;
            result.TryAdd($"{s.SimObjectName}|{s.VendorName}", Create(s.SimObjectName, s.VendorName, title));
        }

        var fromGame = result.Values.Select(a => a.SimObjectName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var c in _catalog.Where(c => Eligible(c.SimObjectName) && !fromGame.Contains(c.SimObjectName)))
        {
            result.TryAdd($"{c.SimObjectName}|{c.VendorName}", Create(c.SimObjectName, c.VendorName, c.Title));
        }

        return result.Values.ToList();
    }

    private static AircraftInfo Create(string simObjectName, string vendorName, string title) => new()
    {
        Title = title,
        SimObjectName = simObjectName,
        VendorName = vendorName,
        PackageFolder = string.Empty,
        ManifestPath = string.Empty,
        VendorPath = string.Empty,
        IsSupported = true,
        IsNotDownloaded = true,
    };
}
