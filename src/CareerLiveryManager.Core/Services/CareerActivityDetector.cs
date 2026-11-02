using System.Text.RegularExpressions;
using CareerLiveryManager.Core.Models;

namespace CareerLiveryManager.Core.Services;

/// <summary>
/// Detects which Career "freelance" activities (Cargo Transport, Flightseeing, etc.) a modular
/// Asobo aircraft supports, purely by reading its official livery folder names - no compiled
/// data is ever touched. See CAREER_LIVERY_RESEARCH.md sections 18-19 for how this convention
/// and the tag/dressing_codes mechanism it relies on were discovered and validated.
/// </summary>
public sealed partial class CareerActivityDetector
{
    private readonly bool _enablePc12Roles;

    /// <param name="enablePc12Roles">PC-12 NGX per-role slots. Off by default: in-game testing (v1.3.2)
    /// showed the game doesn't pick a livery per role for this aircraft, and the override broke the
    /// livery's paint, so the PC-12 is treated as an ordinary single-livery aircraft again. The
    /// detection code is kept for when it can be tested with a real PC-12.</param>
    public CareerActivityDetector(bool enablePc12Roles = false)
    {
        _enablePc12Roles = enablePc12Roles;
    }

    private const string GenericSlotPrefix = "official";
    private const string FreelanceMode = "freelance";

    [GeneratedRegex(@"^(?<activity>[a-z]+)_(?<mode>freelance|adaptivergnl|adaptiveintl|static)_(?<num>\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SlotNameRegex();

    /// <summary>
    /// Activity key -> (display name, dressing_codes value, Licence_* tag).
    /// Dressing codes list every weight/type variant for that activity (comma-separated, as the
    /// SDK allows) instead of trying to compute the exact one from the aircraft's payload mass,
    /// which we have no reliable way to read for official/compiled aircraft.
    /// </summary>
    private static readonly Dictionary<string, (string DisplayName, string DressingCodes, string LicenceTag)> ActivityDefinitions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cargo"] = ("Cargo Transport", "CAR-PSO, CAR-PLC, CAR-PCC, CAR-PVO, CHT-ROH", "Licence_CargoTransport"),
        ["flightseeing"] = ("Flightseeing", "TOR-PLN, TOR-ROT", "Licence_Tour"),
        ["skydive"] = ("Skydive Aviation", "SKP-PLN", "Licence_SkydiveSport"),
        ["aerialad"] = ("Aerial Advertising", "AAD-PLN", "Licence_AerialAdvertising"),
        ["medevac"] = ("Medevac", "MED-PLN", "Licence_Medevac"),
        ["rescue"] = ("Search & Rescue", "SAR-PLN, SAR-ROT, SAR-ROI", "Licence_SearchAndRescue"),
        ["agricultural"] = ("Agricultural Aviation", "AEA-PLN, AEA-ROT", "Licence_AgriculturalAviation"),
        ["firefighting"] = ("Aerial Firefighting", "FIR-INA, FIR-EXA", "Licence_Firefighting"),
        ["commercial"] = ("Commercial Flights", "COF-PCC, COF-ROT", "Licence_Airline"),
        ["private"] = ("Private Charter", "PRC-PSO, PRC-PLC, PRC-PCC", "Licence_PrivateCharter"),
        ["aerialconstruction"] = ("Aerial Construction", "CHT-AEC", "Licence_AerialConstruction"),
    };

    /// <summary>
    /// Scans the vendor's liveries folder (e.g. Official2024\...\liveries\asobo\) for freelance
    /// activity slots plus the generic "official_static" slot. Returns an empty list for aircraft
    /// that don't use this naming convention at all (the single-livery aircraft this app already
    /// supported before, e.g. Longitude, CJ4) - those keep using the existing recipe untouched.
    /// </summary>
    public IReadOnlyList<AircraftActivityInfo> DetectActivities(string vendorLiveriesPath, string? simObjectName = null)
    {
        var result = new List<AircraftActivityInfo>();

        if (!Directory.Exists(vendorLiveriesPath))
        {
            return result;
        }

        if (_enablePc12Roles && string.Equals(simObjectName, Pc12SimObjectName, StringComparison.OrdinalIgnoreCase))
        {
            return DetectPc12Activities(vendorLiveriesPath);
        }

        var slotsByActivity = new Dictionary<string, List<(string FolderName, int Number)>>(StringComparer.OrdinalIgnoreCase);
        string? genericSlotFolder = null;
        var genericSlotNumber = int.MaxValue;

        foreach (var dir in Directory.EnumerateDirectories(vendorLiveriesPath))
        {
            var name = Path.GetFileName(dir);
            var match = SlotNameRegex().Match(name);
            if (!match.Success)
            {
                continue;
            }

            var activity = match.Groups["activity"].Value;
            var mode = match.Groups["mode"].Value;
            var number = int.Parse(match.Groups["num"].Value);

            if (string.Equals(activity, GenericSlotPrefix, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(mode, "static", StringComparison.OrdinalIgnoreCase))
            {
                if (number < genericSlotNumber)
                {
                    genericSlotFolder = name;
                    genericSlotNumber = number;
                }

                continue;
            }

            if (!string.Equals(mode, FreelanceMode, StringComparison.OrdinalIgnoreCase))
            {
                // "_adaptivergnl_"/"_adaptiveintl_"/"_static_" (non-generic) slots are Employee-mode
                // company liveries, never used by the player's own owned aircraft - out of scope.
                continue;
            }

            if (!slotsByActivity.TryGetValue(activity, out var list))
            {
                list = new List<(string, int)>();
                slotsByActivity[activity] = list;
            }

            list.Add((name, number));
        }

        foreach (var (activityKey, slots) in slotsByActivity)
        {
            if (!ActivityDefinitions.TryGetValue(activityKey, out var definition))
            {
                // A freelance slot exists but we don't have documented dressing_codes/tags for it -
                // skip rather than guess and risk writing a livery.cfg that silently loses the
                // Freelance-mode match (see CAREER_LIVERY_RESEARCH.md 19.1).
                continue;
            }

            var folderName = slots.OrderBy(s => s.Number).First().FolderName;

            result.Add(new AircraftActivityInfo
            {
                ActivityKey = activityKey,
                DisplayName = definition.DisplayName,
                OfficialFolderName = folderName,
                IsGenericSlot = false,
                DressingCodes = definition.DressingCodes,
                LicenceTag = definition.LicenceTag,
                DefaultThumbnailPath = FindThumbnail(Path.Combine(vendorLiveriesPath, folderName)),
            });
        }

        if (genericSlotFolder is not null)
        {
            result.Add(new AircraftActivityInfo
            {
                ActivityKey = "official",
                DisplayName = "Default",
                OfficialFolderName = genericSlotFolder,
                IsGenericSlot = true,
                DefaultThumbnailPath = FindThumbnail(Path.Combine(vendorLiveriesPath, genericSlotFolder)),
            });
        }

        return result.OrderBy(a => a.IsGenericSlot).ThenBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private const string Pc12SimObjectName = "microsoft_pc12_ngx";

    // livery_03 (plain), livery_03_c, livery_03_m, livery_03_vip, livery_04_vip_4 ...
    [GeneratedRegex(@"^livery_(?<num>\d+)_(?<role>c|m|vip)(?:_(?<variant>\d+))?$", RegexOptions.IgnoreCase)]
    private static partial Regex Pc12RoleSlotRegex();

    private static readonly (string Suffix, string ActivityKey)[] Pc12Roles =
    {
        ("m", "medevac"),
        ("c", "cargo"),
        ("vip", "private"),
    };

    /// <summary>
    /// The PC-12 NGX doesn't use the "&lt;activity&gt;_freelance_NN" convention. Its official schemes
    /// come in role variants of the same paint: livery_03_m is the medevac one (its thumbnail is an
    /// air ambulance), _c is believed to be cargo and _vip VIP charter (inferred from the names, not
    /// verified in-game). Each role is mapped to its lowest-numbered folder, like every other
    /// aircraft's single freelance slot. The plain schemes (livery_NN) are not a role: a "Default"
    /// entry keeps the single-livery recipe available so nothing that worked before is lost.
    /// </summary>
    private static IReadOnlyList<AircraftActivityInfo> DetectPc12Activities(string vendorLiveriesPath)
    {
        var slots = new List<(string Folder, string Suffix, int Num, int Variant)>();
        string? firstPlainFolder = null;
        var firstPlainNumber = int.MaxValue;

        foreach (var dir in Directory.EnumerateDirectories(vendorLiveriesPath))
        {
            var name = Path.GetFileName(dir);
            var match = Pc12RoleSlotRegex().Match(name);
            if (match.Success)
            {
                var variant = match.Groups["variant"].Success ? int.Parse(match.Groups["variant"].Value) : 0;
                slots.Add((name, match.Groups["role"].Value.ToLowerInvariant(), int.Parse(match.Groups["num"].Value), variant));
                continue;
            }

            var plain = Regex.Match(name, @"^livery_(\d+)$", RegexOptions.IgnoreCase);
            if (plain.Success && int.Parse(plain.Groups[1].Value) < firstPlainNumber)
            {
                firstPlainFolder = name;
                firstPlainNumber = int.Parse(plain.Groups[1].Value);
            }
        }

        var result = new List<AircraftActivityInfo>();

        foreach (var (suffix, activityKey) in Pc12Roles)
        {
            var winner = slots
                .Where(s => s.Suffix == suffix)
                .OrderBy(s => s.Num).ThenBy(s => s.Variant)
                .Select(s => s.Folder)
                .FirstOrDefault();
            if (winner is null)
            {
                continue;
            }

            var definition = ActivityDefinitions[activityKey];
            result.Add(new AircraftActivityInfo
            {
                ActivityKey = activityKey,
                DisplayName = definition.DisplayName,
                OfficialFolderName = winner,
                IsGenericSlot = false,
                DressingCodes = definition.DressingCodes,
                LicenceTag = definition.LicenceTag,
                DefaultThumbnailPath = FindThumbnail(Path.Combine(vendorLiveriesPath, winner)),
            });
        }

        if (result.Count == 0)
        {
            return result;
        }

        result = result.OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        result.Add(new AircraftActivityInfo
        {
            ActivityKey = "default",
            DisplayName = "Default (all other PC-12 jobs)",
            OfficialFolderName = firstPlainFolder ?? string.Empty,
            IsGenericSlot = true,
            UsesStandardRecipe = true,
            DefaultThumbnailPath = firstPlainFolder is null ? null : FindThumbnail(Path.Combine(vendorLiveriesPath, firstPlainFolder)),
        });

        return result;
    }

    private static string? FindThumbnail(string slotFolderPath)
    {
        if (!Directory.Exists(slotFolderPath))
        {
            return null;
        }

        return Directory.EnumerateFiles(slotFolderPath, "*", SearchOption.AllDirectories)
            .FirstOrDefault(f =>
                (f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)) &&
                f.Contains("thumbnail", StringComparison.OrdinalIgnoreCase));
    }
}
