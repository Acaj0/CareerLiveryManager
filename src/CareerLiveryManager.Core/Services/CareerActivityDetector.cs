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
    public IReadOnlyList<AircraftActivityInfo> DetectActivities(string vendorLiveriesPath)
    {
        var result = new List<AircraftActivityInfo>();

        if (!Directory.Exists(vendorLiveriesPath))
        {
            return result;
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
