using System.Collections.ObjectModel;
using CareerLiveryManager.Core.Models;
using CareerLiveryManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CareerLiveryManager.App.ViewModels;

public sealed partial class AircraftListViewModel : ObservableObject
{
    public event EventHandler<AircraftInfo>? AircraftChosen;

    private readonly List<AircraftInfo> _allAircraft = new();
    private readonly LogService _log;

    public ObservableCollection<AircraftInfo> Aircraft { get; } = new();

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    public AircraftListViewModel(AircraftScanner scanner, InstalledPackagesManager installedPackagesManager, AppConfig config, LogService log)
    {
        _log = log;

        try
        {
            _allAircraft.AddRange(scanner.ListAircraft(config.OfficialPath).OrderBy(a => a.Title));

            var installedBySimObject = installedPackagesManager.List(config.CommunityPath)
                .GroupBy(p => p.AircraftSimObjectName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (var aircraft in _allAircraft)
            {
                if (installedBySimObject.TryGetValue(aircraft.SimObjectName, out var installed))
                {
                    aircraft.IsInstalled = true;
                    aircraft.InstalledCount = installed.Count;
                    aircraft.InstalledLiveryThumbnailPath = installed[0].ThumbnailPath;
                }
            }

            ApplyFilter();
            var withActivities = _allAircraft.Where(a => a.HasActivities).ToList();
            _log.Info($"Scanned {_allAircraft.Count} aircraft in '{config.OfficialPath}'. " +
                      $"{withActivities.Count} have detected Career activities: " +
                      string.Join("; ", withActivities.Select(a => $"{a.Title}=[{string.Join(",", a.Activities.Select(x => x.ActivityKey))}]")));

            if (_allAircraft.Count == 0)
            {
                StatusMessage = "No aircraft found in that Official folder.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error scanning aircraft: {ex.Message}";
            _log.Error("Failed to scan aircraft.", ex);
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        Aircraft.Clear();

        var query = string.IsNullOrWhiteSpace(SearchText)
            ? _allAircraft
            : _allAircraft.Where(a => a.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        foreach (var a in query)
        {
            Aircraft.Add(a);
        }
    }

    [RelayCommand]
    private void Choose(AircraftInfo aircraft)
    {
        if (!aircraft.IsSupported)
        {
            return;
        }

        AircraftChosen?.Invoke(this, aircraft);
    }
}
