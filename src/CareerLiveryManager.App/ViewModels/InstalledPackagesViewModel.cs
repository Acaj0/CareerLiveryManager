using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CareerLiveryManager.Core.Models;
using CareerLiveryManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CareerLiveryManager.App.ViewModels;

/// <summary>One row of the installed-packages list: the package plus whether its Remove button is
/// currently asking "are you sure?".</summary>
public sealed partial class InstalledPackageItem : ObservableObject
{
    public InstalledPackageItem(InstalledPackageInfo info)
    {
        Info = info;
    }

    public InstalledPackageInfo Info { get; }

    public string Title => Info.Title;
    public string AircraftSimObjectName => Info.AircraftSimObjectName;
    public string PackageFolder => Info.PackageFolder;
    public string? ThumbnailPath => Info.ThumbnailPath;
    public bool HasActivity => Info.HasActivity;
    public string ActivityDisplayName => Info.ActivityDisplayName;

    [ObservableProperty]
    private bool _isConfirmingRemove;
}

public sealed partial class InstalledPackagesViewModel : ObservableObject
{
    private readonly InstalledPackagesManager _manager;
    private readonly SimProcessChecker _simProcessChecker;
    private readonly LogService _log;
    private readonly AppConfig _config;

    public event EventHandler? BackRequested;

    public ObservableCollection<InstalledPackageItem> Packages { get; } = new();

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>"3 packages · 2 aircraft", shown under the title.</summary>
    [ObservableProperty]
    private string _summaryText = string.Empty;

    public bool IsEmpty => Packages.Count == 0;

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

        // Sorted for reading here only: the aircraft list relies on the manager's own order when it
        // picks which installed package's thumbnail to show, so that order is left alone.
        var ordered = _manager.List(_config.CommunityPath)
            .OrderBy(p => p.AircraftSimObjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.ActivityDisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var package in ordered)
        {
            Packages.Add(new InstalledPackageItem(package));
        }

        var aircraftCount = ordered.Select(p => p.AircraftSimObjectName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        SummaryText = ordered.Count == 0
            ? string.Empty
            : $"{ordered.Count} {(ordered.Count == 1 ? "package" : "packages")} · {aircraftCount} aircraft";
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private void AskRemove(InstalledPackageItem item)
    {
        foreach (var other in Packages)
        {
            other.IsConfirmingRemove = ReferenceEquals(other, item);
        }
    }

    [RelayCommand]
    private void CancelRemove(InstalledPackageItem item) => item.IsConfirmingRemove = false;

    [RelayCommand]
    private void ConfirmRemove(InstalledPackageItem item)
    {
        if (_simProcessChecker.IsSimRunning())
        {
            item.IsConfirmingRemove = false;
            StatusMessage = "Close MSFS completely before removing a package.";
            return;
        }

        try
        {
            _manager.Remove(item.PackageFolder);
            _log.Info($"Package removed: '{item.PackageFolder}'.");
            StatusMessage = string.Empty;
            Reload();
        }
        catch (Exception ex)
        {
            item.IsConfirmingRemove = false;
            StatusMessage = $"Error removing package: {ex.Message}";
            _log.Error($"Failed to remove package '{item.PackageFolder}'.", ex);
        }
    }

    [RelayCommand]
    private void OpenFolder(InstalledPackageItem item)
    {
        try
        {
            if (!Directory.Exists(item.PackageFolder))
            {
                StatusMessage = "That package folder no longer exists.";
                Reload();
                return;
            }

            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{item.PackageFolder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't open the folder: {ex.Message}";
            _log.Error($"Failed to open package folder '{item.PackageFolder}'.", ex);
        }
    }

    [RelayCommand]
    private void Back() => BackRequested?.Invoke(this, EventArgs.Empty);
}
