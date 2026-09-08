using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using CareerLiveryManager.Core.Models;
using CareerLiveryManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CareerLiveryManager.App.ViewModels;

/// <summary>
/// The application shell. Owns the shared services, the persistent header actions
/// (logs / settings / support link), and swaps the currently displayed screen
/// (view-model) as the user navigates.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly ConfigService _configService = new();
    private readonly AircraftScanner _aircraftScanner = new();
    private readonly LiverySourceInspector _liveryInspector = new();
    private readonly PackageBuilder _packageBuilder = new();
    private readonly InstalledPackagesManager _installedPackagesManager = new();
    private readonly SimProcessChecker _simProcessChecker = new();
    private readonly UpdateService _updateService = new();
    private readonly LogService _log = new();

    private UpdateInfo? _pendingUpdate;

    public const string BuyMeACoffeeUrl = "https://buymeacoffee.com/acaj0";

    /// <summary>Shown in the footer so users can tell you which build they're running (e.g. when reporting an issue).</summary>
    public string AppVersionText => "v" + (
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? UpdateService.GetCurrentVersion().ToString());

    [ObservableProperty]
    private ObservableObject _currentViewModel;

    /// <summary>Only the Home screen hides the persistent header (it has its own hero branding).</summary>
    [ObservableProperty]
    private bool _showHeader;

    [ObservableProperty]
    private bool _updateBannerVisible;

    [ObservableProperty]
    private string _updateBannerText = string.Empty;

    [ObservableProperty]
    private bool _isDownloadingUpdate;

    [ObservableProperty]
    private double _updateDownloadProgress;

    [ObservableProperty]
    private bool _checkForUpdatesEnabled = true;

    public MainViewModel()
    {
        _currentViewModel = null!;
        CheckForUpdatesEnabled = _configService.Load().CheckForUpdates;
        GoToHome();
        _ = CheckForUpdatesOnStartupAsync();
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        if (!CheckForUpdatesEnabled)
        {
            return;
        }

        var update = await _updateService.CheckForUpdateAsync();
        if (update is null)
        {
            return;
        }

        _pendingUpdate = update;
        UpdateBannerText = $"Career Livery Manager {update.LatestVersion} is available. You have {update.CurrentVersion}.";
        UpdateBannerVisible = true;
    }

    [RelayCommand]
    private void DismissUpdateBanner() => UpdateBannerVisible = false;

    [RelayCommand]
    private async Task UpdateNow()
    {
        if (_pendingUpdate is null || IsDownloadingUpdate)
        {
            return;
        }

        IsDownloadingUpdate = true;
        UpdateDownloadProgress = 0;

        var updateRoot = Path.Combine(Path.GetTempPath(), $"clm_update_{Guid.NewGuid():N}");
        var zipPath = Path.Combine(updateRoot, _pendingUpdate.AssetName);
        var extractDir = Path.Combine(updateRoot, "extracted");

        try
        {
            var progress = new Progress<double>(p => UpdateDownloadProgress = p);
            await _updateService.DownloadAsync(_pendingUpdate, zipPath, progress, CancellationToken.None);

            var verified = await _updateService.VerifyChecksumAsync(_pendingUpdate, zipPath, CancellationToken.None);
            if (!verified)
            {
                UpdateBannerText = "Downloaded update failed integrity verification. Update aborted, your current installation was not touched.";
                _log.Error($"Update checksum mismatch for {_pendingUpdate.AssetName}.");
                IsDownloadingUpdate = false;
                return;
            }

            UpdateService.ExtractUpdate(zipPath, extractDir);
            _log.Info($"Update downloaded and verified: {_pendingUpdate.CurrentVersion} -> {_pendingUpdate.LatestVersion}. Launching updater.");

            _updateService.LaunchUpdaterAndExit(extractDir);
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateBannerText = "Couldn't download the update. Your current installation was not touched. Try again later.";
            _log.Error("Failed to download/prepare update.", ex);
            IsDownloadingUpdate = false;
        }
    }

    [RelayCommand]
    private void ToggleCheckForUpdates()
    {
        CheckForUpdatesEnabled = !CheckForUpdatesEnabled;
        var config = _configService.Load();
        config.CheckForUpdates = CheckForUpdatesEnabled;
        _configService.Save(config);
    }

    public void GoToHome()
    {
        var vm = new HomeViewModel();
        vm.GetStartedRequested += (_, _) => ContinuePastHome();
        CurrentViewModel = vm;
        ShowHeader = false;
    }

    private void ContinuePastHome()
    {
        var config = _configService.Load();
        var alreadyConfigured =
            !string.IsNullOrWhiteSpace(config.OfficialPath) && _configService.IsValidOfficialPath(config.OfficialPath) &&
            !string.IsNullOrWhiteSpace(config.CommunityPath) && _configService.CommunityPathExists(config.CommunityPath);

        if (alreadyConfigured)
        {
            GoToAircraftList();
        }
        else
        {
            GoToSetup();
        }
    }

    public void GoToSetup()
    {
        var vm = new SetupViewModel(_configService);
        vm.SetupCompleted += (_, _) => GoToAircraftList();
        CurrentViewModel = vm;
        ShowHeader = true;
    }

    public void GoToAircraftList()
    {
        var config = _configService.Load();
        var vm = new AircraftListViewModel(_aircraftScanner, _installedPackagesManager, config, _log);
        vm.AircraftChosen += (_, aircraft) => GoToApplyLivery(aircraft);
        CurrentViewModel = vm;
        ShowHeader = true;
    }

    private void GoToApplyLivery(Core.Models.AircraftInfo aircraft)
    {
        var config = _configService.Load();
        var vm = new ApplyLiveryViewModel(_liveryInspector, _packageBuilder, _installedPackagesManager, _simProcessChecker, _log, config, aircraft);
        vm.BackRequested += (_, _) => GoToAircraftList();
        CurrentViewModel = vm;
        ShowHeader = true;
    }

    [RelayCommand]
    private void GoToInstalledPackages()
    {
        var config = _configService.Load();
        var vm = new InstalledPackagesViewModel(_installedPackagesManager, _simProcessChecker, _log, config);
        vm.BackRequested += (_, _) => GoToAircraftList();
        CurrentViewModel = vm;
        ShowHeader = true;
    }

    [RelayCommand]
    private void OpenLogs()
    {
        try
        {
            Process.Start(new ProcessStartInfo(LogService.LogFilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Error("Could not open the log file from the header menu.", ex);
        }
    }

    [RelayCommand]
    private void ChangeFolders() => GoToSetup();

    [RelayCommand]
    private void OpenBuyMeACoffee() =>
        Process.Start(new ProcessStartInfo(BuyMeACoffeeUrl) { UseShellExecute = true });
}
