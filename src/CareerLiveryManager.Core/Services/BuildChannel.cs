namespace CareerLiveryManager.Core.Services;

/// <summary>Which line of the app this is. True on the "beta" branch only.</summary>
public static class BuildChannel
{
    public static bool IsBeta => true;
}
