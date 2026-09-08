using System.Collections.ObjectModel;
using System.IO;
using CareerLiveryManager.Core.Models;
using CareerLiveryManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace CareerLiveryManager.App.ViewModels;

public sealed partial class ApplyLiveryViewModel : ObservableObject
{
    private readonly LiverySourceInspector _liveryInspector;
    private readonly PackageBuilder _packageBuilder;
    private readonly InstalledPackagesManager _installedPackagesManager;
    private readonly SimProcessChecker _simProcessChecker;
    private readonly LogService _log;
    private readonly AppConfig _config;

    public event EventHandler? BackRequested;

    public AircraftInfo Aircraft { get; }

    public ObservableCollection<LiverySourceInfo> DetectedLiveries { get; } = new();

    [ObservableProperty]
    private LiverySourceInfo? _selectedLivery;

    [ObservableProperty]
    private bool _useDynamicRegistration = true;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _statusMessageColor = "#D9A03C";

    [ObservableProperty]
    private PackagePreview? _preview;

    [ObservableProperty]
    private bool _isSimRunning;

    [ObservableProperty]
    private bool _showSuccessModal;

    [ObservableProperty]
    private string _successPackageFolder = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public ApplyLiveryViewModel(
        LiverySourceInspector liveryInspector,
        PackageBuilder packageBuilder,
        InstalledPackagesManager installedPackagesManager,
        SimProcessChecker simProcessChecker,
        LogService log,
        AppConfig config,
        AircraftInfo aircraft)
    {
        _liveryInspector = liveryInspector;
        _packageBuilder = packageBuilder;
        _installedPackagesManager = installedPackagesManager;
        _simProcessChecker = simProcessChecker;
        _log = log;
        _config = config;
        Aircraft = aircraft;
        RefreshSimRunning();
    }

    /// <summary>Enabled only once a preview has been generated and no apply is already running.</summary>
    public bool CanApply => Preview is not null && !IsBusy;

    partial void OnSelectedLiveryChanged(LiverySourceInfo? value) => Preview = null;
    partial void OnUseDynamicRegistrationChanged(bool value) => Preview = null;
    partial void OnPreviewChanged(PackagePreview? value) => OnPropertyChanged(nameof(CanApply));
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanApply));

    [RelayCommand]
    private void BrowseLiverySource()
    {
        var dialog = new OpenFolderDialog { Title = "Select the livery folder (the one containing SimObjects\\...)" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        DetectedLiveries.Clear();
        Preview = null;
        StatusMessageColor = "#D9A03C";

        var found = _liveryInspector.Inspect(dialog.FolderName);
        if (found.Count == 0)
        {
            StatusMessage = "No livery.cfg was found in that folder. This livery isn't supported by this tool.";
            return;
        }

        foreach (var livery in found)
        {
            DetectedLiveries.Add(livery);
        }

        SelectedLivery = DetectedLiveries[0];
        StatusMessage = string.Empty;

        if (!string.Equals(SelectedLivery.DetectedSimObjectName, Aircraft.SimObjectName, StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = $"Warning: this livery looks like it belongs to a different aircraft ({SelectedLivery.DetectedSimObjectName}), " +
                             $"not {Aircraft.SimObjectName}. It may not work correctly.";
        }
    }

    [RelayCommand]
    private void GeneratePreview()
    {
        if (SelectedLivery is null)
        {
            return;
        }

        var request = BuildRequest();
        Preview = _packageBuilder.Preview(request, _config.CommunityPath);
    }

    [RelayCommand]
    private async Task ApplyLivery()
    {
        if (SelectedLivery is null || IsBusy)
        {
            return;
        }

        RefreshSimRunning();
        if (IsSimRunning)
        {
            StatusMessageColor = "#D9A03C";
            StatusMessage = "Close MSFS completely before applying the livery.";
            return;
        }

        IsBusy = true;
        try
        {
            // Find any package this app previously created for this exact aircraft, so it
            // can be replaced instead of left behind (a stale package would otherwise keep
            // "winning" the ordering trick over the new one, or just clutter Community).
            var previousPackages = _installedPackagesManager.List(_config.CommunityPath)
                .Where(p => string.Equals(p.AircraftSimObjectName, Aircraft.SimObjectName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (previousPackages.Count > 0)
            {
                StatusMessageColor = "#D9A03C";
                StatusMessage = "Replacing existing livery...";
                await Task.Delay(1);
            }
            else
            {
                StatusMessageColor = "#D9A03C";
                StatusMessage = "Installing new livery...";
                await Task.Delay(1);
            }

            var request = BuildRequest();
            var packageFolder = await Task.Run(() => _packageBuilder.Apply(request, _config.CommunityPath));

            // Only now that the new package exists do we remove the old one(s) - if Apply
            // above had thrown, the user would still be left with a working livery.
            foreach (var previous in previousPackages)
            {
                if (!string.Equals(Path.GetFullPath(previous.PackageFolder), Path.GetFullPath(packageFolder), StringComparison.OrdinalIgnoreCase))
                {
                    _installedPackagesManager.Remove(previous.PackageFolder);
                    _log.Info($"Removed previous Career Livery Manager package for '{Aircraft.SimObjectName}': '{previous.PackageFolder}'.");
                }
            }

            StatusMessageColor = "#3FBF8F";
            StatusMessage = $"Package created at: {packageFolder}\nOpen MSFS again so it rescans the Community folder.";
            _log.Info($"Livery applied: aircraft='{Aircraft.Title}' ({Aircraft.SimObjectName}), " +
                      $"livery='{SelectedLivery.BaseFolderName}', useDr={UseDynamicRegistration}, package='{packageFolder}'.");
            Preview = null;
            SuccessPackageFolder = packageFolder;
            ShowSuccessModal = true;
        }
        catch (Exception ex)
        {
            StatusMessageColor = "#D95C5C";
            StatusMessage = $"Error applying the livery: {ex.Message}";
            _log.Error($"Failed to apply livery '{SelectedLivery.BaseFolderName}' to '{Aircraft.Title}'.", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Back() => BackRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void CloseSuccessModal()
    {
        ShowSuccessModal = false;
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshSimRunning() => IsSimRunning = _simProcessChecker.IsSimRunning();

    private ApplyLiveryRequest BuildRequest()
    {
        var packageName = $"career-livery-{Aircraft.SimObjectName}-{SelectedLivery!.BaseFolderName}".Replace(' ', '-');

        return new ApplyLiveryRequest
        {
            Aircraft = Aircraft,
            Source = SelectedLivery,
            UseDr = UseDynamicRegistration,
            PackageName = packageName,
        };
    }
}
