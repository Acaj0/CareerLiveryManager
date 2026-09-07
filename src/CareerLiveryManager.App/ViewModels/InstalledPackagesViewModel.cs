using System.Collections.ObjectModel;
using CareerLiveryManager.Core.Models;
using CareerLiveryManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CareerLiveryManager.App.ViewModels;

public sealed partial class InstalledPackagesViewModel : ObservableObject
{
    private readonly InstalledPackagesManager _manager;
    private readonly SimProcessChecker _simProcessChecker;
    private readonly LogService _log;
    private readonly AppConfig _config;

    public event EventHandler? BackRequested;

    public ObservableCollection<InstalledPackageInfo> Packages { get; } = new();

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public InstalledPackagesViewModel(InstalledPackagesManager manager, SimProcessChecker simProcessChecker, LogService log, AppConfig config)
    {
        _manager = manager;
        _simProcessChecker = simProcessChecker;
        _log = log;
        _config = config;
        Reload();
    }

    private void Reload()
    {
        Packages.Clear();
        foreach (var p in _manager.List(_config.CommunityPath))
        {
            Packages.Add(p);
        }
    }

    [RelayCommand]
    private void Remove(InstalledPackageInfo package)
    {
        if (_simProcessChecker.IsSimRunning())
        {
            StatusMessage = "Close MSFS completely before removing a package.";
            return;
        }

        try
        {
            _manager.Remove(package.PackageFolder);
            _log.Info($"Package removed: '{package.PackageFolder}'.");
            StatusMessage = string.Empty;
            Reload();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error removing package: {ex.Message}";
            _log.Error($"Failed to remove package '{package.PackageFolder}'.", ex);
        }
    }

    [RelayCommand]
    private void Back() => BackRequested?.Invoke(this, EventArgs.Empty);
}
