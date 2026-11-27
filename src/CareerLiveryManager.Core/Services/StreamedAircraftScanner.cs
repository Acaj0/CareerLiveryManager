using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CareerLiveryManager.Core.Services;

/// <summary>An aircraft the game knows about but streams instead of keeping on disk.</summary>
/// <param name="Title">Manufacturer and model from the game's English strings, or null when it ships none.</param>
public sealed record StreamedAircraft(string PackageName, string SimObjectName, string VendorName, string? Title, bool HasActivitySlots);

/// <summary>
/// BETA. Reads what MSFS 2024 keeps in "StreamedPackages" for aircraft that aren't downloaded: each
/// package has a "minimal.fsarchive" with a plain JSON index followed by the files themselves. Only
/// archives that declare themselves "notEncrypted" are read - the others are left alone - and nothing
/// is ever written there.
///
/// Layout: "RASA", a little-endian uint32 at offset 12 with the length of the index, the index at
/// offset 32, and the file contents right after it.
/// </summary>
public sealed partial class StreamedAircraftScanner
{
    private const string FolderName = "StreamedPackages";
    private const int IndexOffset = 32;
    private const long MaxArchiveBytes = 64 * 1024 * 1024;

    [GeneratedRegex(@"^simobjects/airplanes/(?<sim>[^/]+)/(?<rest>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SimObjectPathRegex();

    [GeneratedRegex(@"^liveries/(?<vendor>[^/]+)/(?<livery>[^/]+)/livery\.cfg$", RegexOptions.IgnoreCase)]
    private static partial Regex LiveryCfgPathRegex();

    // Folder names of Career job slots (see CareerActivityDetector).
    [GeneratedRegex(@"^(?!official_)[a-z]+_freelance_\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex ActivitySlotRegex();

    /// <summary>StreamedPackages sits next to Official2024, one or two levels above the Official folder
    /// the user selects (Official2024\Steam or Official2024\OneStore).</summary>
    public static string? FindStreamedPackagesFolder(string officialPath)
    {
        if (string.IsNullOrWhiteSpace(officialPath))
        {
            return null;
        }

        var current = new DirectoryInfo(Path.TrimEndingDirectorySeparator(officialPath));
        for (var level = 0; level < 3 && current is not null; level++, current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, FolderName);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public IReadOnlyList<StreamedAircraft> Scan(string officialPath)
    {
        var root = FindStreamedPackagesFolder(officialPath);
        if (root is null)
        {
            return Array.Empty<StreamedAircraft>();
        }

        var result = new List<StreamedAircraft>();
        foreach (var package in Directory.EnumerateDirectories(root).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(package);
            // "passiveaircraft" packages are AI traffic, as in AircraftScanner.
            if (name.Contains("passive", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var archive = Path.Combine(package, "content", "minimal.fsarchive");
            try
            {
                if (File.Exists(archive) && new FileInfo(archive).Length <= MaxArchiveBytes)
                {
                    result.AddRange(ReadPackage(name, File.ReadAllBytes(archive)));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                           or InvalidDataException or InvalidOperationException or KeyNotFoundException)
            {
                // A half-downloaded or unfamiliar archive only means this one aircraft isn't listed.
            }
        }

        return result;
    }

    private static IEnumerable<StreamedAircraft> ReadPackage(string packageName, byte[] data)
    {
        if (data.Length <= IndexOffset || !data.AsSpan(0, 4).SequenceEqual("RASA"u8))
        {
            yield break;
        }

        var indexLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12, 4));
        if (indexLength <= 0 || indexLength > data.Length - IndexOffset)
        {
            yield break;
        }

        var index = Encoding.UTF8.GetString(data, IndexOffset, indexLength).TrimEnd('\0');
        using var doc = JsonDocument.Parse(index);

        if (doc.RootElement.GetProperty("encryptionSetup").GetProperty("scheme").GetString() != "notEncrypted")
        {
            yield break;
        }

        var files = doc.RootElement.GetProperty("fileInfoList").EnumerateArray()
            .Select(f => (Path: (f.GetProperty("path").GetString() ?? string.Empty).Replace('\\', '/'), Entry: f))
            .ToList();

        var bySimObject = files
            .Select(f => SimObjectPathRegex().Match(f.Path))
            .Where(m => m.Success)
            .GroupBy(m => m.Groups["sim"].Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string? title = null;
        var locpak = files.FirstOrDefault(f => f.Path.EndsWith("en-us.locpak", StringComparison.OrdinalIgnoreCase));
        if (locpak.Path is not null)
        {
            title = ReadTitle(data, IndexOffset + indexLength, locpak.Entry);
        }

        foreach (var group in bySimObject)
        {
            var rest = group.Select(m => m.Groups["rest"].Value).ToList();

            // A livery add-on has liveries but none of the aircraft's own configuration.
            var isAircraft = rest.Any(r => r.StartsWith("presets/", StringComparison.OrdinalIgnoreCase) ||
                                           r.StartsWith("common/", StringComparison.OrdinalIgnoreCase));
            if (!isAircraft)
            {
                continue;
            }

            var liveries = rest.Select(r => LiveryCfgPathRegex().Match(r)).Where(m => m.Success).ToList();
            foreach (var vendor in liveries.GroupBy(m => m.Groups["vendor"].Value, StringComparer.OrdinalIgnoreCase))
            {
                yield return new StreamedAircraft(
                    packageName,
                    group.Key,
                    vendor.Key,
                    title,
                    vendor.Any(m => ActivitySlotRegex().IsMatch(m.Groups["livery"].Value)));
            }
        }
    }

    private static string? ReadTitle(byte[] data, int contentStart, JsonElement entry)
    {
        var offset = entry.GetProperty("byteOffset").GetInt64() + contentStart;
        var size = entry.GetProperty("byteSize").GetInt64();
        if (offset < contentStart || size <= 0 || offset + size > data.Length)
        {
            return null;
        }

        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(data, (int)offset, (int)size).TrimStart('﻿').TrimEnd('\0'));
        if (!doc.RootElement.TryGetProperty("LocalisationPackage", out var package) ||
            !package.TryGetProperty("Strings", out var strings))
        {
            return null;
        }

        string? Find(string key) => strings.EnumerateObject()
            .Where(p => p.Name.EndsWith(key, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Name.Length)
            .Select(p => p.Value.GetString())
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        var model = Find("AIRCRAFT.UI_MODEL");
        if (model is null)
        {
            return null;
        }

        var manufacturer = Find("AIRCRAFT.UI_MANUFACTURER");
        return manufacturer is null || model.Contains(manufacturer, StringComparison.OrdinalIgnoreCase)
            ? model
            : $"{manufacturer} {model}";
    }
}
