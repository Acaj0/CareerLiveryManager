using System.Collections.ObjectModel;
using CareerLiveryManager.Core.Models;
using CareerLiveryManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CareerLiveryManager.App.ViewModels;

/// <summary>Which aircraft the list shows, on top of whatever is typed in the search box.</summary>
public enum AircraftFilter
{
    All,
    Installed,
    WithActivities,
}

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

    /// <summary>Plain-language notes that explain a result that looks like a bug but isn't
    /// (no aircraft with Career activities, aircraft not downloaded yet...). Empty when there's nothing to explain.</summary>
    [ObservableProperty]
    private string _infoMessage = string.Empty;

    [ObservableProperty]
    private AircraftFilter _filter = AircraftFilter.All;

    /// <summary>Set when aircraft exist but the search/filter hides every one of them.</summary>
    [ObservableProperty]
    private string _noMatchMessage = string.Empty;

    public int AllCount => _allAircraft.Count;
    public int InstalledCount => _allAircraft.Count(a => a.IsInstalled);
    public int WithActivitiesCount => _allAircraft.Count(a => a.ActivityCount > 0);

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
            _log.InfoIfChanged("aircraft-scan", $"Scanned {_allAircraft.Count} aircraft in '{config.OfficialPath}'. " +
                      $"{withActivities.Count} have detected Career activities: " +
                      string.Join("; ", withActivities.Select(a => $"{a.Title}=[{string.Join(",", a.Activities.Select(x => x.ActivityKey))}]")));

            if (_allAircraft.Count == 0)
            {
                StatusMessage = "No aircraft found in that Official folder.";
            }

            var notes = new SetupValidator().ValidateScanResults(config.OfficialPath, _allAircraft);
            foreach (var note in notes)
            {
                _log.InfoIfChanged($"scan-note-{note.Id}", $"Scan note {note.Id}: {note.Message}");
            }

            InfoMessage = string.Join("\n\n", notes.Select(n => n.Message));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error scanning aircraft: {ex.Message}";
            _log.Error("Failed to scan aircraft.", ex);
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnFilterChanged(AircraftFilter value) => ApplyFilter();

    private void ApplyFilter()
    {
        Aircraft.Clear();

        IEnumerable<AircraftInfo> query = _allAircraft;

        query = Filter switch
        {
            AircraftFilter.Installed => query.Where(a => a.IsInstalled),
            AircraftFilter.WithActivities => query.Where(a => a.ActivityCount > 0),
            _ => query,
        };

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = query.Where(a => a.Title.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        foreach (var a in query)
        {
            Aircraft.Add(a);
        }

        NoMatchMessage = Aircraft.Count == 0 && _allAircraft.Count > 0 ? BuildNoMatchMessage() : string.Empty;
        OnPropertyChanged(nameof(HasActiveFilter));
    }

    /// <summary>True when a search or a chip is hiding part of the list.</summary>
    public bool HasActiveFilter => Filter != AircraftFilter.All || !string.IsNullOrWhiteSpace(SearchText);

    private string BuildNoMatchMessage()
    {
        var search = string.IsNullOrWhiteSpace(SearchText) ? null : $"\"{SearchText.Trim()}\"";
        return (Filter, search) switch
        {
            (AircraftFilter.Installed, null) => "You haven't applied a livery to any aircraft yet.",
            (AircraftFilter.Installed, _) => $"No aircraft with one of your liveries matches {search}.",
            (AircraftFilter.WithActivities, null) => "None of your aircraft has Career activities.",
            (AircraftFilter.WithActivities, _) => $"No aircraft with Career activities matches {search}.",
            (_, _) => $"No aircraft matches {search}.",
        };
    }

    [RelayCommand]
    private void SetFilter(AircraftFilter filter) => Filter = filter;

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        Filter = AircraftFilter.All;
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
