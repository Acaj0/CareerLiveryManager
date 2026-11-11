using System.Text.RegularExpressions;

namespace CareerLiveryManager.Core.Services;

public sealed partial class LiveryCfgEditor
{
    /// <summary>
    /// Rewrites the "Name=" value inside a livery.cfg's [GENERAL] section,
    /// preserving surrounding quotes if the original value had them.
    /// </summary>
    public void SetLiveryName(string liveryCfgPath, string newName)
    {
        var lines = File.ReadAllLines(liveryCfgPath);
        var regex = NameLineRegex();

        for (var i = 0; i < lines.Length; i++)
        {
            var match = regex.Match(lines[i]);
            if (!match.Success)
            {
                continue;
            }

            var hadQuotes = match.Groups["quote"].Value.Length > 0;
            lines[i] = hadQuotes ? $"Name=\"{newName}\"" : $"Name={newName}";
            break;
        }

        File.WriteAllLines(liveryCfgPath, lines);
    }

    /// <summary>Reads the current "Name=" value from a livery.cfg's [GENERAL] section (quotes
    /// stripped), or null if the file has no Name line.</summary>
    public string? ReadLiveryName(string liveryCfgPath)
    {
        var match = File.ReadLines(liveryCfgPath)
            .Select(l => NameValueRegex().Match(l))
            .FirstOrDefault(m => m.Success);

        return match?.Groups["value"].Value.Trim().Trim('"');
    }

    /// <summary>
    /// Rewrites fallback.1 inside a "_DR" variant's texture.cfg to the exact relative value
    /// given, e.g. <c>..\..\C700_N282N\texture</c> (sibling under the same vendor folder) or
    /// <c>..\_fallback_base\texture</c> (nested inside the winning folder itself - see
    /// PackageBuilder's activity-mode recipe, where the base can't be left as an untagged
    /// sibling in the shared vendor namespace).
    /// </summary>
    public void FixDrFallback(string textureCfgPath, string newFallbackValue)
    {
        var lines = File.ReadAllLines(textureCfgPath);
        var regex = Fallback1Regex();

        for (var i = 0; i < lines.Length; i++)
        {
            if (!regex.IsMatch(lines[i]))
            {
                continue;
            }

            lines[i] = $"fallback.1={newFallbackValue}";
            break;
        }

        File.WriteAllLines(textureCfgPath, lines);
    }

    /// <summary>
    /// Reads the value after "fallback.1=" in a texture.cfg, if present - e.g.
    /// "..\..\..\..\..\asobo_c172sp\liveries\kfs\N733EC\texture". Used to detect liveries
    /// (like the Cessna 172 G1000 variant) that borrow their paint from a sibling SimObject
    /// entirely, not just a sibling livery folder.
    /// </summary>
    public string? ReadFallback1(string textureCfgPath)
    {
        var match = File.ReadLines(textureCfgPath)
            .Select(l => Fallback1ValueRegex().Match(l))
            .FirstOrDefault(m => m.Success);

        return match?.Groups["value"].Value.Trim();
    }

    /// <summary>Every "fallback.N" value in a texture.cfg, in file order.</summary>
    public IReadOnlyList<string> ReadAllFallbacks(string textureCfgPath) =>
        File.ReadLines(textureCfgPath)
            .Select(l => AnyFallbackValueRegex().Match(l))
            .Where(m => m.Success)
            .Select(m => m.Groups["value"].Value.Trim())
            .ToList();

    /// <summary>The value of "required_tags" in a livery.cfg's [Selection] section, quotes removed
    /// (e.g. "winglets"), or null when there is none.</summary>
    public string? ReadRequiredTags(string liveryCfgPath)
    {
        var match = File.ReadLines(liveryCfgPath)
            .Select(l => RequiredTagsRegex().Match(l))
            .FirstOrDefault(m => m.Success);

        return match?.Groups["value"].Value.Trim().Trim('"');
    }

    /// <summary>
    /// Renames folder names inside every "fallback.N" path of a texture.cfg, e.g. turning
    /// "..\..\C750_N728QL_DR\texture" into "..\..\!C750_N728QL_DR\texture" after that folder was
    /// copied under a new name. Only whole path segments are replaced and the rest of each line
    /// (spacing, other fallbacks) is left exactly as it was.
    /// </summary>
    public void RenameFallbackFolders(string textureCfgPath, IReadOnlyDictionary<string, string> renames)
    {
        var lines = File.ReadAllLines(textureCfgPath);
        var changed = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var match = AnyFallbackValueRegex().Match(lines[i]);
            if (!match.Success)
            {
                continue;
            }

            var value = match.Groups["value"];
            var segments = value.Value.Split('\\');
            var segmentChanged = false;
            for (var s = 0; s < segments.Length; s++)
            {
                if (renames.TryGetValue(segments[s].Trim(), out var newName))
                {
                    segments[s] = newName;
                    segmentChanged = true;
                }
            }

            if (segmentChanged)
            {
                lines[i] = lines[i][..value.Index] + string.Join('\\', segments) + lines[i][(value.Index + value.Length)..];
                changed = true;
            }
        }

        if (changed)
        {
            File.WriteAllLines(textureCfgPath, lines);
        }
    }

    /// <summary>
    /// Writes (replacing any existing ones) the [Specialization]/[Tags] sections a Career
    /// "freelance" activity slot needs to win Freelancer-mode selection - see
    /// CAREER_LIVERY_RESEARCH.md section 19 for how this was discovered and validated.
    /// </summary>
    public void SetCareerActivityTags(string liveryCfgPath, string dressingCodes, string licenceTag)
    {
        var lines = File.ReadAllLines(liveryCfgPath).ToList();
        RemoveSection(lines, "Specialization");
        RemoveSection(lines, "Tags");

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        lines.Add(string.Empty);
        lines.Add("[Specialization]");
        lines.Add($"dressing_codes = \"{dressingCodes}\"");
        lines.Add(string.Empty);
        lines.Add("[Tags]");
        lines.Add("tag.0 = \"Freelance\"");
        lines.Add($"tag.1 = \"{licenceTag}\"");

        File.WriteAllLines(liveryCfgPath, lines);
    }

    private static void RemoveSection(List<string> lines, string sectionName)
    {
        var header = SectionHeaderRegex(sectionName);
        var start = lines.FindIndex(l => header.IsMatch(l));
        if (start < 0)
        {
            return;
        }

        var end = start + 1;
        while (end < lines.Count && !AnySectionHeaderRegex().IsMatch(lines[end]))
        {
            end++;
        }

        lines.RemoveRange(start, end - start);
    }

    private static Regex SectionHeaderRegex(string sectionName) =>
        new($@"^\s*\[{Regex.Escape(sectionName)}\]\s*$", RegexOptions.IgnoreCase);

    [GeneratedRegex(@"^\s*Name\s*=\s*(?<quote>""?)", RegexOptions.IgnoreCase)]
    private static partial Regex NameLineRegex();

    [GeneratedRegex(@"^\s*Name\s*=\s*(?<value>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex NameValueRegex();

    [GeneratedRegex(@"^\s*fallback\.1\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex Fallback1Regex();

    [GeneratedRegex(@"^\s*fallback\.1\s*=\s*(?<value>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex Fallback1ValueRegex();

    [GeneratedRegex(@"^\s*fallback\.\d+\s*=\s*(?<value>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex AnyFallbackValueRegex();

    [GeneratedRegex(@"^\s*required_tags\s*=\s*(?<value>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex RequiredTagsRegex();

    [GeneratedRegex(@"^\s*\[[^\]]+\]\s*$")]
    private static partial Regex AnySectionHeaderRegex();
}
