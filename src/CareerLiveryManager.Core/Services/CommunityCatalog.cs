using CareerLiveryManager.Core.Models;

namespace CareerLiveryManager.Core.Services;

/// <summary>
/// The fixed list behind the Community screen. It lives in code on purpose: the screen works offline,
/// and changing a link is a normal release. Images are in the app's Resources/Community folder.
/// </summary>
public static class CommunityCatalog
{
    public static IReadOnlyList<CommunityEntry> Creators { get; } = new[]
    {
        new CommunityEntry("MR Liveries", "https://discord.gg/FjncRa7TMg", "mr-liveries.png"),
        new CommunityEntry("Kiwi Flight Sim", "https://kiwiflightsim.com/requests", "kiwi-flight-sim.png"),
    };

    public static IReadOnlyList<CommunityEntry> RecommendedApps { get; } = new[]
    {
        new CommunityEntry(
            "MSFS Livery Toolkit",
            "https://flightsim.to/addon/112147/msfs-livery-toolkit",
            "livery-toolkit.png",
            "Create your own liveries for MSFS aircraft."),
    };

    public static bool IsSafeUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
