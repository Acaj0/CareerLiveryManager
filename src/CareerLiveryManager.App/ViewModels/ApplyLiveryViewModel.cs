using System.Collections.ObjectModel;
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

    public ApplyLiveryViewModel(
        LiverySourceInspector liveryInspector,
        PackageBuilder packageBuilder,
        SimProcessChecker simProcessChecker,
        LogService log,
        AppConfig config,
        AircraftInfo aircraft)
    {
        _liveryInspector = liveryInspector;
        _packageBuilder = packageBuilder;
        _simProcessChecker = simProcessChecker;
        _log = log;
        _config = config;
        Aircraft = aircraft;
        RefreshSimRunning();
    }

    partial void OnSelectedLiveryChanged(LiverySourceInfo? value) => Preview = null;
    partial void OnUseDynamicRegistrationChanged(bool value) => Preview = null;

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
    private void ApplyLivery()
    {
        if (SelectedLivery is null)
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

        var request = BuildRequest();
        try
        {
            var packageFolder = _packageBuilder.Apply(request, _config.CommunityPath);
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
