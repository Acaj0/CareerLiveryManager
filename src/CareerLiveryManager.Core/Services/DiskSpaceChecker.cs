using System.Globalization;

namespace CareerLiveryManager.Core.Services;

public enum DiskSpaceStatus
{
    Enough,
    Low,

    /// <summary>The free space couldn't be read (a network share, a missing drive...). Never blocks anything.</summary>
    Unknown,
}

public sealed record DiskSpaceCheck(DiskSpaceStatus Status, long FreeBytes, long RequiredBytes)
{
    /// <summary>Not even the files themselves fit (as opposed to <see cref="DiskSpaceStatus.Low"/>, which only
    /// means the safety margin doesn't). A copy would certainly stop halfway.</summary>
    public bool IsCertainlyShort => Status != DiskSpaceStatus.Unknown && FreeBytes < RequiredBytes;
}

/// <summary>
/// Looks at the free space of the drive a package is about to be copied to, so a nearly full disk is
/// noticed before a copy stops halfway and leaves a half-written package in Community.
/// </summary>
public static class DiskSpaceChecker
{
    /// <summary>Room left over after the copy (backfilled parts, a replaced package that is only removed afterwards...).</summary>
    public const long SafetyMarginBytes = 256L * 1024 * 1024;

    public static DiskSpaceCheck Evaluate(long freeBytes, long requiredBytes) =>
        new(freeBytes < requiredBytes + SafetyMarginBytes ? DiskSpaceStatus.Low : DiskSpaceStatus.Enough, freeBytes, requiredBytes);

    /// <summary>Free space on the drive that holds <paramref name="folder"/>, or <see cref="DiskSpaceStatus.Unknown"/>.</summary>
    public static DiskSpaceCheck Check(string folder, long requiredBytes)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(folder));
            if (string.IsNullOrEmpty(root))
            {
                return new DiskSpaceCheck(DiskSpaceStatus.Unknown, 0, requiredBytes);
            }

            return Evaluate(new DriveInfo(root).AvailableFreeSpace, requiredBytes);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or NotSupportedException)
        {
            return new DiskSpaceCheck(DiskSpaceStatus.Unknown, 0, requiredBytes);
        }
    }
}

/// <summary>"1.2 GB" style sizes for the preview and warnings.</summary>
public static class FileSizeFormatter
{
    public static string Format(long bytes, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        const double kb = 1024;
        const double mb = kb * 1024;
        const double gb = mb * 1024;

        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => string.Create(culture, $"{bytes / kb:0} KB"),
            < 100L * 1024 * 1024 => string.Create(culture, $"{bytes / mb:0.#} MB"),
            < 1024L * 1024 * 1024 => string.Create(culture, $"{bytes / mb:0} MB"),
            _ => string.Create(culture, $"{bytes / gb:0.0} GB"),
        };
    }
}
