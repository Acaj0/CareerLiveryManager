using System.Collections.ObjectModel;
using CareerLiveryManager.Core.Models;
using CareerLiveryManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CareerLiveryManager.App.ViewModels;

/// <summary>
/// Shown for aircraft that have detected Career activities (Cargo Transport, Flightseeing...):
/// a carousel of those activities, each showing whichever livery currently applies (the one this
/// app installed for it, or MSFS's own default) so the user can pick which one to customize next.
/// </summary>
public sealed partial class AircraftDetailViewModel : ObservableObject
{
    public event EventHandler<AircraftActivityInfo>? ActivityChosen;
    public event EventHandler? BackRequested;

    public AircraftInfo Aircraft { get; }

    public ObservableCollection<AircraftActivityInfo> Activities { get; } = new();

    public AircraftDetailViewModel(AircraftInfo aircraft, InstalledPackagesManager installedPackagesManager, AppConfig config)
    {
        Aircraft = aircraft;

        var installed = installedPackagesManager.List(config.CommunityPath)
            .Where(p => string.Equals(p.AircraftSimObjectName, aircraft.SimObjectName, StringComparison.OrdinalIgnoreCase) && p.HasActivity)
            .ToDictionary(p => p.ActivityKey, StringComparer.OrdinalIgnoreCase);

        foreach (var activity in aircraft.Activities)
        {
            if (installed.TryGetValue(activity.ActivityKey, out var package))
            {
                activity.IsInstalled = true;
                activity.InstalledThumbnailPath = package.ThumbnailPath;
            }

            Activities.Add(activity);
        }
    }

    [RelayCommand]
    private void Choose(AircraftActivityInfo activity) => ActivityChosen?.Invoke(this, activity);

    [RelayCommand]
    private void Back() => BackRequested?.Invoke(this, EventArgs.Empty);
}
