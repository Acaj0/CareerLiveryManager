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

    public event EventHandler? SetupCompleted;

    [ObservableProperty]
    private string _officialPath;

    [ObservableProperty]
    private string _communityPath;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _statusMessageColor = "#D95C5C";

    public SetupViewModel(ConfigService configService, LogService log)
    {
        _configService = configService;
        _log = log;
        var config = _configService.Load();
        _officialPath = config.OfficialPath;
        _communityPath = config.CommunityPath;
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
        }
    }

    [RelayCommand]
    private void BrowseCommunity()
    {
        var dialog = new OpenFolderDialog { Title = "Select the Community folder" };
        if (dialog.ShowDialog() == true)
        {
            CommunityPath = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void Continue()
    {
        if (!_configService.IsValidOfficialPath(OfficialPath))
        {
            StatusMessageColor = "#D95C5C";
            StatusMessage = "This doesn't look like a valid Official2024 folder (no package with a manifest.json was found). " +
                             "If you installed via the Xbox app/Game Pass, try \"Detect automatically\" above, or see the setup guide link.";
            return;
        }

        if (string.IsNullOrWhiteSpace(CommunityPath))
        {
            StatusMessageColor = "#D95C5C";
            StatusMessage = "Please choose a Community folder.";
            return;
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
        }

        _configService.Save(new AppConfig { OfficialPath = OfficialPath, CommunityPath = CommunityPath });
        _log.Info($"Setup saved: Official='{OfficialPath}', Community='{CommunityPath}'.");
        StatusMessage = string.Empty;
        SetupCompleted?.Invoke(this, EventArgs.Empty);
    }
}
