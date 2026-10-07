using System.Text.RegularExpressions;

namespace CareerLiveryManager.Core.Services;

/// <summary>What MSFS 2024's own UserCfg.opt says: where it was found and its InstalledPackagesPath.</summary>
public sealed record UserCfgInfo(string UserCfgPath, string Storefront, string InstalledPackagesPath);

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
    private static IEnumerable<(string Path, string Storefront)> CandidateUserCfgPaths()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // Steam
        yield return (Path.Combine(appData, "Microsoft Flight Simulator 2024", "UserCfg.opt"), "Steam");

        // Microsoft Store / Xbox app (Game Pass) - MSFS 2024's package family name.
        yield return (Path.Combine(localAppData, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt"), "Microsoft Store / Xbox app");
    }

    public sealed class DetectionResult
    {
        public required string OfficialPath { get; init; }
        public required string CommunityPath { get; init; }
    }

    /// <summary>
    /// Reads the first UserCfg.opt that exists and has an InstalledPackagesPath, without checking
    /// that the folders it names exist. Null when none is found or readable.
    /// </summary>
    public UserCfgInfo? ReadUserCfg()
    {
        foreach (var (path, storefront) in CandidateUserCfgPaths())
        {
            var info = TryReadUserCfg(path, storefront);
            if (info is not null)
            {
                return info;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the detected Official2024/&lt;storefront&gt; and Community paths, or null if
    /// UserCfg.opt couldn't be found/parsed or the paths it points to don't actually exist.
    /// </summary>
    public DetectionResult? TryDetect(ConfigService configService)
    {
        foreach (var (userCfgPath, storefront) in CandidateUserCfgPaths())
        {
            var result = TryDetectFrom(userCfgPath, storefront, configService);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private static UserCfgInfo? TryReadUserCfg(string userCfgPath, string storefront)
    {
        if (!File.Exists(userCfgPath))
        {
            return null;
        }

        try
        {
            var match = InstalledPackagesPathRegex.Match(File.ReadAllText(userCfgPath));
            return match.Success ? new UserCfgInfo(userCfgPath, storefront, match.Groups[1].Value) : null;
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

    private static DetectionResult? TryDetectFrom(string userCfgPath, string storefront, ConfigService configService)
    {
        var info = TryReadUserCfg(userCfgPath, storefront);
        if (info is null)
        {
            return null;
        }

        try
        {
            var packagesRoot = info.InstalledPackagesPath;
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
