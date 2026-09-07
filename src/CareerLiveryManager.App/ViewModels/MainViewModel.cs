using System.Diagnostics;
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
    private readonly LogService _log = new();

    public const string BuyMeACoffeeUrl = "https://buymeacoffee.com/acaj0";

    [ObservableProperty]
    private ObservableObject _currentViewModel;

    /// <summary>Only the Home screen hides the persistent header (it has its own hero branding).</summary>
    [ObservableProperty]
    private bool _showHeader;

    public MainViewModel()
    {
        _currentViewModel = null!;
        GoToHome();
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
        var vm = new ApplyLiveryViewModel(_liveryInspector, _packageBuilder, _simProcessChecker, _log, config, aircraft);
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
