using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using CareerLiveryManager.Core.Models;

namespace CareerLiveryManager.Core.Services;

/// <summary>Everything <see cref="DiagnosticReportBuilder"/> needs to know about this run of the app.</summary>
public sealed class DiagnosticReportRequest
{
    public required string AppVersion { get; init; }
    public required AppConfig Config { get; init; }

    /// <summary>What MSFS's UserCfg.opt said (see <see cref="MsfsPathDetector.ReadUserCfg"/>), or null.</summary>
    public UserCfgInfo? UserCfg { get; init; }

    public required string LogDirectory { get; init; }

    /// <summary>Rewrites the Windows user name in every text file before it goes into the zip.</summary>
    public bool RedactUserName { get; init; } = true;

    /// <summary>The user's profile folder; defaults to the current user's. A seam for tests.</summary>
    public string? UserProfilePath { get; init; }

    /// <summary>"Now" for the report header and the log window. A seam for tests.</summary>
    public DateTime Now { get; init; } = DateTime.Now;
}

/// <summary>
/// Builds the single file a user attaches to a bug report: one zip with the setup, what the app
/// detected, what it installed and the recent log, so support never has to ask for a screenshot of
/// a 42,000-character log. See ROADMAP.md item 2.
///
/// It only reads. It never uploads anything, never includes textures, models or other binaries,
/// never includes the whole UserCfg.opt, and (by default) replaces the Windows user name with
/// "&lt;redacted&gt;" in every file. Safe to run while MSFS is running.
/// </summary>
public sealed class DiagnosticReportBuilder
{
    /// <summary>Only log entries newer than this go into the report...</summary>
    public const int LogDays = 7;

    /// <summary>...and at most this many bytes of them (whichever limit is smaller).</summary>
    public const int LogMaxBytes = 2 * 1024 * 1024;

    /// <summary>A package with thousands of files must not make the report huge.</summary>
    public const int MaxTreeLines = 3000;

    private const int MaxConfigFileBytes = 20 * 1024;

    private readonly SetupValidator _validator = new();
    private readonly AircraftScanner _scanner = new();
    private readonly InstalledPackagesManager _installedPackages = new();

    /// <summary>The file names a user is told about before exporting - kept next to the code that writes them.</summary>
    public static readonly IReadOnlyList<string> IncludedItems =
    [
        "App version, Windows version, .NET runtime and system language",
        "Your Official and Community folder paths and the setup check results",
        "The InstalledPackagesPath line from MSFS's UserCfg.opt (never the rest of that file)",
        "The aircraft the app detected and their Career activities",
        "For each package this app created: its manifest.json, file list with sizes, and the text of every livery.cfg and texture.cfg",
        "The last 7 days (at most 2 MB) of the app's log",
    ];

    public static readonly IReadOnlyList<string> NeverIncludedItems =
    [
        "Textures, models or any other binary file",
        "Your livery downloads or any package this app didn't create",
        "Nothing is uploaded anywhere - you choose where the file goes",
    ];

    /// <summary>Writes the report zip to <paramref name="outputZipPath"/> (replacing any file there).</summary>
    public void Build(DiagnosticReportRequest request, string outputZipPath)
    {
        var data = Collect(request);
        var redactor = new Redactor(request);

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputZipPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Written next to the destination and moved into place, so a failure never leaves a half-written zip.
        var temp = outputZipPath + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                AddText(zip, "summary.txt", BuildSummaryText(request, data), redactor);
                AddText(zip, "config.json", BuildConfigJson(request.Config), redactor);
                AddText(zip, "setup-validation.txt", BuildValidationText(data), redactor);
                AddText(zip, "usercfg-packages-path.txt", BuildUserCfgText(request.UserCfg), redactor);
                AddText(zip, "aircraft.txt", BuildAircraftText(data), redactor);
                AddInstalledPackages(zip, data, redactor);
                AddText(zip, "app.log", ReadLog(request.LogDirectory, request.Now), redactor);
            }

            File.Move(temp, outputZipPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    /// <summary>A short plain-text version of summary.txt for pasting into a chat where files can't be attached.</summary>
    public string BuildSummary(DiagnosticReportRequest request) =>
        new Redactor(request).Apply(BuildSummaryText(request, Collect(request)));

    // ---- collected facts ----------------------------------------------------------------------

    private sealed class ReportData
    {
        public IReadOnlyList<SetupIssue> Issues { get; init; } = Array.Empty<SetupIssue>();
        public IReadOnlyList<AircraftInfo>? Aircraft { get; init; }
        public string? AircraftError { get; init; }
        public IReadOnlyList<InstalledPackageInfo> Packages { get; init; } = Array.Empty<InstalledPackageInfo>();
    }

    private ReportData Collect(DiagnosticReportRequest request)
    {
        var issues = _validator.Validate(request.Config.OfficialPath, request.Config.CommunityPath, request.UserCfg);

        IReadOnlyList<AircraftInfo>? aircraft = null;
        string? aircraftError = null;
        if (Directory.Exists(request.Config.OfficialPath))
        {
            try
            {
                aircraft = _scanner.ListAircraft(request.Config.OfficialPath);
                issues = issues.Concat(_validator.ValidateScanResults(request.Config.OfficialPath, aircraft)).ToList();
            }
            catch (Exception ex)
            {
                aircraftError = $"The aircraft scan failed: {ex.Message}";
            }
        }
        else
        {
            aircraftError = "The aircraft scan was skipped because the Official folder doesn't exist (see setup-validation.txt).";
        }

        IReadOnlyList<InstalledPackageInfo> packages = Array.Empty<InstalledPackageInfo>();
        try
        {
            packages = _installedPackages.List(request.Config.CommunityPath);
        }
        catch (Exception)
        {
            // An unreadable Community folder is already reported by the setup validation.
        }

        return new ReportData { Issues = issues, Aircraft = aircraft, AircraftError = aircraftError, Packages = packages };
    }

    // ---- file contents ------------------------------------------------------------------------

    private static string BuildSummaryText(DiagnosticReportRequest request, ReportData data)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Career Livery Manager - diagnostic report");
        sb.AppendLine($"Generated: {request.Now:yyyy-MM-dd HH:mm:ss} (local time)");
        sb.AppendLine($"App version: {request.AppVersion}");
        sb.AppendLine($"Windows: {RuntimeInformation.OSDescription} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")} OS, {(Environment.Is64BitProcess ? "64-bit" : "32-bit")} process)");
        sb.AppendLine($".NET runtime: {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"System language: {CultureInfo.CurrentCulture.Name} (UI: {CultureInfo.CurrentUICulture.Name})");
        sb.AppendLine($"MSFS storefront (from where UserCfg.opt was found): {request.UserCfg?.Storefront ?? "UserCfg.opt not found"}");
        sb.AppendLine($"Official folder: {request.Config.OfficialPath}");
        sb.AppendLine($"Community folder: {request.Config.CommunityPath}");
        sb.AppendLine($"Check for updates: {request.Config.CheckForUpdates}");

        var errors = data.Issues.Count(i => i.Severity == SetupIssueSeverity.Error);
        var warnings = data.Issues.Count(i => i.Severity == SetupIssueSeverity.Warning);
        var notes = data.Issues.Count(i => i.Severity == SetupIssueSeverity.Info);
        var ids = data.Issues.Count == 0 ? "none" : string.Join(", ", data.Issues.Select(i => i.Id));
        sb.AppendLine($"Setup check: {errors} problem(s), {warnings} warning(s), {notes} note(s) [{ids}]");

        if (data.Aircraft is { } aircraft)
        {
            sb.AppendLine($"Aircraft detected: {aircraft.Count} ({aircraft.Count(a => a.HasActivities)} with Career activities)");
        }
        else
        {
            sb.AppendLine($"Aircraft detected: unknown ({data.AircraftError})");
        }

        sb.AppendLine($"Packages installed by this app: {data.Packages.Count}");
        return sb.ToString();
    }

    private static string BuildConfigJson(AppConfig config) =>
        System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    private static string BuildValidationText(ReportData data)
    {
        if (data.Issues.Count == 0)
        {
            return "No problems, warnings or notes.\n";
        }

        var sb = new StringBuilder();
        foreach (var issue in data.Issues)
        {
            sb.AppendLine($"{issue.Id} [{issue.Severity}] {issue.Message}");
            if (issue.SuggestedFix is { } fix)
            {
                sb.AppendLine($"    suggested fix: {fix.Description} (Official={fix.OfficialPath ?? "-"}, Community={fix.CommunityPath ?? "-"})");
            }
        }

        return sb.ToString();
    }

    private static string BuildUserCfgText(UserCfgInfo? userCfg) =>
        userCfg is null
            ? "UserCfg.opt was not found or has no InstalledPackagesPath.\n"
            : $"InstalledPackagesPath \"{userCfg.InstalledPackagesPath}\"\n(read from: {userCfg.UserCfgPath} - {userCfg.Storefront})\n";

    private static string BuildAircraftText(ReportData data)
    {
        if (data.Aircraft is null)
        {
            return (data.AircraftError ?? "No aircraft scan was made.") + "\n";
        }

        var sb = new StringBuilder();
        foreach (var aircraft in data.Aircraft)
        {
            var activities = aircraft.Activities.Count == 0
                ? "none"
                : string.Join(", ", aircraft.Activities.Select(a => $"{a.ActivityKey}={a.OfficialFolderName}"));
            sb.AppendLine($"{aircraft.Title} | simobject={aircraft.SimObjectName} | vendor={aircraft.VendorName} | activities: {activities}");
        }

        if (data.Aircraft.Count == 0)
        {
            sb.AppendLine("No aircraft were detected.");
        }

        return sb.ToString();
    }

    private static void AddInstalledPackages(ZipArchive zip, ReportData data, Redactor redactor)
    {
        foreach (var package in data.Packages)
        {
            var name = Path.GetFileName(package.PackageFolder);
            var manifestPath = Path.Combine(package.PackageFolder, "manifest.json");
            if (File.Exists(manifestPath))
            {
                AddText(zip, $"installed-packages/{name}/manifest.json", ReadShared(manifestPath), redactor);
            }

            AddText(zip, $"installed-packages/{name}/layout-summary.txt", BuildLayoutSummary(package.PackageFolder), redactor);
        }
    }

    /// <summary>The file tree with sizes (capped) followed by the text of every livery.cfg and texture.cfg:
    /// small files that are exactly what diagnoses fallback and tag bugs.</summary>
    internal static string BuildLayoutSummary(string packageFolder)
    {
        var files = Directory.EnumerateFiles(packageFolder, "*", SearchOption.AllDirectories)
            .Select(f => (Path: f, Relative: Path.GetRelativePath(packageFolder, f).Replace('\\', '/')))
            .OrderBy(f => f.Relative, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"{files.Count} file(s), {files.Sum(f => new FileInfo(f.Path).Length):N0} bytes in total");
        sb.AppendLine();

        foreach (var file in files.Take(MaxTreeLines))
        {
            sb.AppendLine($"{file.Relative}  ({new FileInfo(file.Path).Length:N0} bytes)");
        }

        if (files.Count > MaxTreeLines)
        {
            sb.AppendLine($"... and {files.Count - MaxTreeLines} more file(s) not listed (the list is capped at {MaxTreeLines} lines).");
        }

        foreach (var file in files.Where(f => IsConfigFileOfInterest(f.Path)))
        {
            sb.AppendLine();
            sb.AppendLine($"----- {file.Relative} -----");
            var text = ReadShared(file.Path);
            if (text.Length > MaxConfigFileBytes)
            {
                text = text[..MaxConfigFileBytes] + "\n... (cut: file is larger than 20 KB)";
            }

            sb.AppendLine(text.TrimEnd());
        }

        return sb.ToString();
    }

    private static bool IsConfigFileOfInterest(string path)
    {
        var name = Path.GetFileName(path);
        return string.Equals(name, "livery.cfg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "texture.cfg", StringComparison.OrdinalIgnoreCase);
    }

    // ---- the log ------------------------------------------------------------------------------

    private static readonly Regex LogEntryStart = new(@"^\[(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\]", RegexOptions.Compiled);

    /// <summary>The newest part of the log that fits both limits: entries from the last
    /// <see cref="LogDays"/> days, then at most <see cref="LogMaxBytes"/> bytes of those.</summary>
    internal static string ReadLog(string logDirectory, DateTime now)
    {
        var parts = new List<string>();
        for (var i = LogService.RolledFilesToKeep; i >= 1; i--)
        {
            parts.Add(Path.Combine(logDirectory, LogService.RolledFileName(i)));
        }

        parts.Add(Path.Combine(logDirectory, "app.log"));

        var text = string.Concat(parts.Where(File.Exists).Select(p => EnsureTrailingNewline(ReadShared(p))));
        if (text.Length == 0)
        {
            return "The log is empty or was not found.\n";
        }

        // An entry is its timestamped line plus any lines after it (a stack trace) up to the next timestamp.
        var entries = new List<(DateTime Time, string Text)>();
        StringBuilder? current = null;
        var currentTime = DateTime.MinValue;
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            var match = LogEntryStart.Match(trimmed);
            if (match.Success)
            {
                if (current is not null)
                {
                    entries.Add((currentTime, current.ToString()));
                }

                currentTime = DateTime.TryParseExact(match.Groups["ts"].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                    ? parsed
                    : DateTime.MinValue;
                current = new StringBuilder().AppendLine(trimmed);
            }
            else if (current is not null && trimmed.Length > 0)
            {
                current.AppendLine(trimmed);
            }
        }

        if (current is not null)
        {
            entries.Add((currentTime, current.ToString()));
        }

        var cutoff = now.AddDays(-LogDays);
        var recent = entries.Where(e => e.Time >= cutoff).ToList();
        var droppedByAge = entries.Count - recent.Count;

        var kept = new List<string>();
        long bytes = 0;
        for (var i = recent.Count - 1; i >= 0; i--)
        {
            var size = Encoding.UTF8.GetByteCount(recent[i].Text);
            if (bytes + size > LogMaxBytes)
            {
                break;
            }

            kept.Add(recent[i].Text);
            bytes += size;
        }

        kept.Reverse();
        var droppedBySize = recent.Count - kept.Count;

        var sb = new StringBuilder();
        if (droppedByAge > 0 || droppedBySize > 0)
        {
            sb.AppendLine($"[log trimmed: {droppedByAge} older than {LogDays} days and {droppedBySize} over the {LogMaxBytes / 1024 / 1024} MB limit were left out]");
        }

        foreach (var entry in kept)
        {
            sb.Append(entry);
        }

        return sb.Length == 0 ? $"No log entries from the last {LogDays} days.\n" : sb.ToString();
    }

    private static string EnsureTrailingNewline(string text) =>
        text.Length == 0 || text.EndsWith('\n') ? text : text + "\n";

    // ---- plumbing -----------------------------------------------------------------------------

    /// <summary>Reads a text file even while another program (or this app's own logger) has it open.</summary>
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static void AddText(ZipArchive zip, string entryName, string content, Redactor redactor)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(redactor.Apply(content));
        stream.Write(bytes);
    }

    /// <summary>Rewrites the Windows user name in paths: "C:\Users\Jane" becomes "C:\Users\&lt;redacted&gt;",
    /// on any drive and with either slash (or the doubled backslashes inside JSON). A profile folder
    /// kept somewhere other than "...\Users\" is replaced by "&lt;UserProfile&gt;".</summary>
    private sealed class Redactor
    {
        private static readonly Regex UsersPath = new(@"(?<prefix>[A-Za-z]:[\\/]+Users[\\/]+)(?<name>[^\\/\r\n""*?<>|:]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly bool _enabled;
        private readonly string? _profile;

        public Redactor(DiagnosticReportRequest request)
        {
            _enabled = request.RedactUserName;
            var profile = request.UserProfilePath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _profile = string.IsNullOrWhiteSpace(profile) ? null : profile.TrimEnd('\\', '/');
        }

        public string Apply(string text)
        {
            if (!_enabled)
            {
                return text;
            }

            var result = UsersPath.Replace(text, m => m.Groups["prefix"].Value + "<redacted>");

            if (_profile is not null)
            {
                result = result.Replace(_profile, "<UserProfile>", StringComparison.OrdinalIgnoreCase);
                result = result.Replace(_profile.Replace("\\", "\\\\"), "<UserProfile>", StringComparison.OrdinalIgnoreCase);
            }

            return result;
        }
    }
}
