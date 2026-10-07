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
    private readonly SetupValidator _setupValidator = new();
    private readonly MsfsPathDetector _pathDetector = new();
    private readonly UpdateService _updateService = new();
    private readonly LogService _log = new();

    private UpdateInfo? _pendingUpdate;

    public const string BuyMeACoffeeUrl = "https://buymeacoffee.com/acaj0";
    public const string FeedbackUrl = "https://clm.antoniodeabreu.dev/feedback";
    public const string ChangelogUrl = "https://clm.antoniodeabreu.dev/changelog";
    public const string RoadmapUrl = "https://clm.antoniodeabreu.dev/roadmap";

    /// <summary>Shown in the footer so users can tell you which build they're running (e.g. when reporting an issue).</summary>
    public string AppVersionText => "v" + (
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? UpdateService.GetCurrentVersion().ToString());

    [ObservableProperty]
    private ObservableObject _currentViewModel;

    /// <summary>A screen that holds a timer or similar is told it's being left, whichever way the user left it.</summary>
    partial void OnCurrentViewModelChanging(ObservableObject? oldValue, ObservableObject newValue)
    {
        if (oldValue is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

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
        // Folders a dropped .zip was extracted to are normally deleted when the screen is left; this removes
        // any a crash or forced close left behind. Best effort, off the UI thread.
        _ = Task.Run(() => new ArchiveExtractor().CleanupStale(TimeSpan.FromHours(12)));
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

        // Never configured: the plain Setup screen. A saved configuration is validated (and, if it
        // went stale, explained) by GoToAircraftList.
        if (string.IsNullOrWhiteSpace(config.OfficialPath) || string.IsNullOrWhiteSpace(config.CommunityPath))
        {
            GoToSetup();
        }
        else
        {
            GoToAircraftList();
        }
    }

    public void GoToSetup(string? statusMessage = null)
    {
        var vm = new SetupViewModel(_configService, _log);
        if (!string.IsNullOrEmpty(statusMessage))
        {
            vm.StatusMessage = statusMessage;
            vm.StatusMessageColor = "#D9A03C";
            vm.Revalidate();
        }

        vm.SetupCompleted += (_, _) => GoToAircraftList();
        CurrentViewModel = vm;
        ShowHeader = true;
    }

    public void GoToAircraftList()
    {
        var config = _configService.Load();

        // The saved config can go stale between screens (folder deleted/renamed, moved drive, etc.) -
        // re-validate here instead of letting AircraftScanner throw a raw exception on a bad/empty path.
        // Only errors send the user back; warnings were already confirmed when the setup was saved.
        var issues = _setupValidator.Validate(config.OfficialPath, config.CommunityPath, _pathDetector.ReadUserCfg());
        if (issues.Any(i => i.Severity == SetupIssueSeverity.Error))
        {
            foreach (var issue in issues.Where(i => i.Severity == SetupIssueSeverity.Error))
            {
                _log.Info($"Saved setup failed validation {issue.Id}: {issue.Message}");
            }

            GoToSetup("Your saved folders need attention. Fix the problems listed below.");
            return;
        }

        if (!_configService.CommunityPathExists(config.CommunityPath))
        {
            GoToSetup("Your Community folder couldn't be found. Please check it below.");
            return;
        }

        var vm = new AircraftListViewModel(_aircraftScanner, _installedPackagesManager, config, _log);
        vm.AircraftChosen += (_, aircraft) =>
        {
            if (aircraft.HasActivities)
            {
                GoToAircraftDetail(aircraft);
            }
            else
            {
                GoToApplyLivery(aircraft, activity: null);
            }
        };
        CurrentViewModel = vm;
        ShowHeader = true;
    }

    private void GoToAircraftDetail(Core.Models.AircraftInfo aircraft)
    {
        var config = _configService.Load();
        var vm = new AircraftDetailViewModel(aircraft, _installedPackagesManager, config);
        vm.ActivityChosen += (_, activity) => GoToApplyLivery(aircraft, activity);
        vm.BackRequested += (_, _) => GoToAircraftList();
        CurrentViewModel = vm;
        ShowHeader = true;
    }

    private void GoToApplyLivery(Core.Models.AircraftInfo aircraft, Core.Models.AircraftActivityInfo? activity)
    {
        var config = _configService.Load();
        var vm = new ApplyLiveryViewModel(_liveryInspector, _packageBuilder, _installedPackagesManager, _simProcessChecker, _log, config, aircraft, activity);
        vm.BackRequested += (_, _) =>
        {
            if (aircraft.HasActivities)
            {
                GoToAircraftDetail(aircraft);
            }
            else
            {
                GoToAircraftList();
            }
        };
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
            // Typically no program is set to open ".log" files. The folder is always openable, and
            // the log (plus its rolled-over copies) is right there.
            _log.Error("Could not open the log file from the header menu; opening its folder instead.", ex);
            try
            {
                Directory.CreateDirectory(LogService.LogDirectory);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{LogService.LogDirectory}\"") { UseShellExecute = true });
            }
            catch (Exception folderEx)
            {
                _log.Error("Could not open the log folder either.", folderEx);
            }
        }
    }

    [RelayCommand]
    private async Task ExportDiagnosticReport()
    {
        var dialog = new Views.DiagnosticReportWindow { Owner = System.Windows.Application.Current.MainWindow };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var save = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save the diagnostic report",
            Filter = "Zip file (*.zip)|*.zip",
            FileName = $"CLM-diagnostic-{DateTime.Now:yyyyMMdd-HHmm}.zip",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            DefaultExt = ".zip",
            AddExtension = true,
        };
        if (save.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var request = CreateDiagnosticRequest(redactUserName: dialog.HideUserName);
            var builder = new DiagnosticReportBuilder();
            await Task.Run(() => builder.Build(request, save.FileName));

            _log.Info($"Diagnostic report exported to '{save.FileName}' (username hidden: {request.RedactUserName}).");
            var openFolder = System.Windows.MessageBox.Show(
                $"The report was saved to:\n{save.FileName}\n\nOpen the folder?",
                "Diagnostic report saved",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Information);
            if (openFolder == System.Windows.MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{save.FileName}\"") { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            _log.Error("Failed to export the diagnostic report.", ex);
            System.Windows.MessageBox.Show(
                $"Couldn't create the report: {ex.Message}",
                "Diagnostic report",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private async Task CopyDiagnosticSummary()
    {
        try
        {
            var request = CreateDiagnosticRequest(redactUserName: true);
            var summary = await Task.Run(() => new DiagnosticReportBuilder().BuildSummary(request));
            System.Windows.Clipboard.SetText(summary);

            _log.Info("Diagnostic summary copied to the clipboard.");
            System.Windows.MessageBox.Show(
                "A short summary was copied to the clipboard (your Windows username is hidden). Paste it into your message.",
                "Diagnostic summary copied",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _log.Error("Failed to copy the diagnostic summary.", ex);
            System.Windows.MessageBox.Show(
                $"Couldn't create the summary: {ex.Message}",
                "Diagnostic summary",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    private DiagnosticReportRequest CreateDiagnosticRequest(bool redactUserName) => new()
    {
        AppVersion = AppVersionText,
        Config = _configService.Load(),
        UserCfg = _pathDetector.ReadUserCfg(),
        LogDirectory = LogService.LogDirectory,
        RedactUserName = redactUserName,
    };

    [RelayCommand]
    private void ChangeFolders() => GoToSetup();

    [RelayCommand]
    private void OpenBuyMeACoffee() =>
        Process.Start(new ProcessStartInfo(BuyMeACoffeeUrl) { UseShellExecute = true });

    [RelayCommand]
    private void OpenFeedback() =>
        Process.Start(new ProcessStartInfo(FeedbackUrl) { UseShellExecute = true });

    [RelayCommand]
    private void OpenChangelog() =>
        Process.Start(new ProcessStartInfo(ChangelogUrl) { UseShellExecute = true });

    [RelayCommand]
    private void OpenRoadmap() =>
        Process.Start(new ProcessStartInfo(RoadmapUrl) { UseShellExecute = true });
}
