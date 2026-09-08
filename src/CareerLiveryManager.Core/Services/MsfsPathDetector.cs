using System.Text.RegularExpressions;

namespace CareerLiveryManager.Core.Services;

/// <summary>
/// Tries to locate the Official2024 and Community folders automatically by reading
/// MSFS 2024's own UserCfg.opt, instead of asking the user to browse for them by hand.
/// This is the same file the game itself reads its package paths from, so it's storefront-agnostic -
/// it works the same whether MSFS was installed via Steam, the Microsoft Store, or the Xbox app
/// (Game Pass), all of which use different, sometimes hidden, install locations.
/// </summary>
public sealed class MsfsPathDetector
{
    private static readonly Regex InstalledPackagesPathRegex =
        new(@"InstalledPackagesPath\s+""([^""]+)""", RegexOptions.Compiled);

    /// <summary>Known locations for MSFS 2024's UserCfg.opt, one per storefront.</summary>
    private static IEnumerable<string> CandidateUserCfgPaths()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // Steam
        yield return Path.Combine(appData, "Microsoft Flight Simulator 2024", "UserCfg.opt");

        // Microsoft Store / Xbox app (Game Pass) - MSFS 2024's package family name.
        yield return Path.Combine(localAppData, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt");
    }

    public sealed class DetectionResult
    {
        public required string OfficialPath { get; init; }
        public required string CommunityPath { get; init; }
    }

    /// <summary>
    /// Returns the detected Official2024/&lt;storefront&gt; and Community paths, or null if
    /// UserCfg.opt couldn't be found/parsed or the paths it points to don't actually exist.
    /// </summary>
    public DetectionResult? TryDetect(ConfigService configService)
    {
        foreach (var userCfgPath in CandidateUserCfgPaths())
        {
            var result = TryDetectFrom(userCfgPath, configService);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private static DetectionResult? TryDetectFrom(string userCfgPath, ConfigService configService)
    {
        if (!File.Exists(userCfgPath))
        {
            return null;
        }

        try
        {
            var content = File.ReadAllText(userCfgPath);
            var match = InstalledPackagesPathRegex.Match(content);
            if (!match.Success)
            {
                return null;
            }

            var packagesRoot = match.Groups[1].Value;
            var officialRoot = Path.Combine(packagesRoot, "Official2024");
            var communityPath = Path.Combine(packagesRoot, "Community");

            if (!Directory.Exists(officialRoot))
            {
                return null;
            }

            // The Official2024 folder itself just holds one "Steam" or "OneStore" subfolder
            // depending on storefront - find whichever one actually has packages in it.
            var officialPath = Directory.EnumerateDirectories(officialRoot)
                .FirstOrDefault(configService.IsValidOfficialPath);

            if (officialPath is null)
            {
                return null;
            }

            return new DetectionResult
            {
                OfficialPath = officialPath,
                CommunityPath = communityPath,
            };
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
