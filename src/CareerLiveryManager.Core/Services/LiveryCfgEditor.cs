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

    /// <summary>
    /// Rewrites fallback.1 inside a "_DR" variant's texture.cfg so it points at the
    /// (possibly renamed) sibling base-livery folder, e.g.:
    /// fallback.1=..\..\C700_N282N\texture
    /// </summary>
    public void FixDrFallback(string textureCfgPath, string newBaseFolderName)
    {
        var lines = File.ReadAllLines(textureCfgPath);
        var regex = Fallback1Regex();

        for (var i = 0; i < lines.Length; i++)
        {
            if (!regex.IsMatch(lines[i]))
            {
                continue;
            }

            lines[i] = $@"fallback.1=..\..\{newBaseFolderName}\texture";
            break;
        }

        File.WriteAllLines(textureCfgPath, lines);
    }

    [GeneratedRegex(@"^\s*Name\s*=\s*(?<quote>""?)", RegexOptions.IgnoreCase)]
    private static partial Regex NameLineRegex();

    [GeneratedRegex(@"^\s*fallback\.1\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex Fallback1Regex();
}
