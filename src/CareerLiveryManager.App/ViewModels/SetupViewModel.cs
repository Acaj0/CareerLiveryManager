using System.Collections.ObjectModel;
using CareerLiveryManager.Core.Models;
using CareerLiveryManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace CareerLiveryManager.App.ViewModels;

public sealed partial class SetupViewModel : ObservableObject
{
    private readonly ConfigService _configService;
    private readonly LogService _log;
    private readonly MsfsPathDetector _pathDetector = new();
    private readonly SetupValidator _validator = new();

    /// <summary>Identifies the exact folders + warnings the user already saw and chose to proceed
    /// with, so the second "Continue" click saves instead of showing the same warnings again.</summary>
    private string? _acknowledgedWarningsKey;

    public event EventHandler? SetupCompleted;

    [ObservableProperty]
    private string _officialPath;

    [ObservableProperty]
    private string _communityPath;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _statusMessageColor = "#D95C5C";

    /// <summary>What the validator found for the folders currently typed/picked.</summary>
    public ObservableCollection<SetupIssueItem> Issues { get; } = new();

    public SetupViewModel(ConfigService configService, LogService log)
    {
        _configService = configService;
        _log = log;
        var config = _configService.Load();
        _officialPath = config.OfficialPath;
        _communityPath = config.CommunityPath;
    }

    // Editing a path makes whatever was listed about the old one stale.
    partial void OnOfficialPathChanged(string value) => ClearIssues();

    partial void OnCommunityPathChanged(string value) => ClearIssues();

    private void ClearIssues()
    {
        Issues.Clear();
        _acknowledgedWarningsKey = null;
    }

    /// <summary>Validates the current folders and lists what was found. Called when the saved
    /// configuration went stale and the app sent the user back here.</summary>
    public void Revalidate() => RunValidation();

    private IReadOnlyList<SetupIssue> RunValidation()
    {
        var issues = _validator.Validate(OfficialPath, CommunityPath, _pathDetector.ReadUserCfg());

        Issues.Clear();
        foreach (var issue in issues)
        {
            Issues.Add(new SetupIssueItem(issue));
        }

        return issues;
    }

    [RelayCommand]
    private void DetectAutomatically()
    {
        var result = _pathDetector.TryDetect(_configService);
        if (result is null)
        {
            StatusMessageColor = "#D9A03C";
            StatusMessage = "Couldn't detect the folders automatically (this can happen with a custom install location). " +
                             "Please use \"Browse...\" below instead.";
            _log.Info("Detect automatically: no folders found.");
            return;
        }

        OfficialPath = result.OfficialPath;
        CommunityPath = result.CommunityPath;
        RunValidation();
        StatusMessageColor = "#3FBF8F";
        StatusMessage = "Detected automatically. Review the paths below, then continue.";
        _log.Info($"Detect automatically: found Official='{result.OfficialPath}', Community='{result.CommunityPath}'.");
    }

    [RelayCommand]
    private void BrowseOfficial()
    {
        var dialog = new OpenFolderDialog { Title = "Select your Official2024 content folder (Steam or OneStore)" };
        if (dialog.ShowDialog() == true)
        {
            OfficialPath = dialog.FolderName;
            RunValidation();
        }
    }

    [RelayCommand]
    private void BrowseCommunity()
    {
        var dialog = new OpenFolderDialog { Title = "Select the Community folder" };
        if (dialog.ShowDialog() == true)
        {
            CommunityPath = dialog.FolderName;
            RunValidation();
        }
    }

    [RelayCommand]
    private void ApplyFix(SetupIssueItem? item)
    {
        if (item?.Issue.SuggestedFix is not { } fix)
        {
            return;
        }

        if (fix.OfficialPath is not null)
        {
            OfficialPath = fix.OfficialPath;
        }

        if (fix.CommunityPath is not null)
        {
            CommunityPath = fix.CommunityPath;
        }

        _log.Info($"Setup fix applied for {item.Id}: Official='{OfficialPath}', Community='{CommunityPath}'.");
        var issues = RunValidation();
        StatusMessageColor = "#3FBF8F";
        StatusMessage = issues.Any(i => i.Severity == SetupIssueSeverity.Error)
            ? string.Empty
            : "Folders updated. Review them below, then continue.";
    }

    [RelayCommand]
    private void Continue()
    {
        var issues = RunValidation();

        if (issues.Any(i => i.Severity == SetupIssueSeverity.Error))
        {
            StatusMessageColor = "#D95C5C";
            StatusMessage = "These folders can't be used yet. Fix the problems listed below, then continue.";
            return;
        }

        var warnings = issues.Where(i => i.Severity == SetupIssueSeverity.Warning).ToList();
        if (warnings.Count > 0)
        {
            var key = $"{OfficialPath}|{CommunityPath}|{string.Join(",", warnings.Select(w => w.Id))}";
            if (_acknowledgedWarningsKey != key)
            {
                _acknowledgedWarningsKey = key;
                StatusMessageColor = "#D9A03C";
                StatusMessage = "Please review the warnings listed below. If everything is intentional, click Continue again.";
                return;
            }
        }

        if (!_configService.CommunityPathExists(CommunityPath))
        {
            try
            {
                _configService.EnsureCommunityPathExists(CommunityPath);
            }
            catch (Exception ex)
            {
                StatusMessageColor = "#D95C5C";
                StatusMessage = $"Couldn't create the Community folder at that path: {ex.Message}";
                _log.Error($"Failed to create Community folder '{CommunityPath}'.", ex);
                return;
            }

            // The folder only exists now, so this is the first chance to check it's writable.
            var created = _validator.Validate(OfficialPath, CommunityPath, _pathDetector.ReadUserCfg());
            if (created.Any(i => i.Severity == SetupIssueSeverity.Error))
            {
                RunValidation();
                StatusMessageColor = "#D95C5C";
                StatusMessage = "These folders can't be used yet. Fix the problems listed below, then continue.";
                return;
            }
        }

        _configService.Save(new AppConfig { OfficialPath = OfficialPath, CommunityPath = CommunityPath });
        _log.Info($"Setup saved: Official='{OfficialPath}', Community='{CommunityPath}'.");
        foreach (var issue in issues)
        {
            _log.Info($"Setup validation {issue.Id} ({issue.Severity}): {issue.Message}");
        }

        StatusMessage = string.Empty;
        SetupCompleted?.Invoke(this, EventArgs.Empty);
    }
}
