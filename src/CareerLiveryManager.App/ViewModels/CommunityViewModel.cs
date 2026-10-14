using System.Diagnostics;
using CareerLiveryManager.Core.Models;
using CareerLiveryManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CareerLiveryManager.App.ViewModels;

/// <summary>One clickable tile on the Community screen.</summary>
public sealed class CommunityItem
{
    public CommunityItem(CommunityEntry entry)
    {
        Entry = entry;
    }

    public CommunityEntry Entry { get; }

    public string Name => Entry.Name;
    public string Url => Entry.Url;
    public string? Description => Entry.Description;
    public string ImageUri => $"pack://application:,,,/Resources/Community/{Entry.ImageFile}";
}

public sealed partial class CommunityViewModel : ObservableObject
{
    private readonly LogService _log;

    public event EventHandler? BackRequested;

    public IReadOnlyList<CommunityItem> Creators { get; } =
        CommunityCatalog.Creators.Select(e => new CommunityItem(e)).ToList();

    public IReadOnlyList<CommunityItem> RecommendedApps { get; } =
        CommunityCatalog.RecommendedApps.Select(e => new CommunityItem(e)).ToList();

    public CommunityViewModel(LogService log)
    {
        _log = log;
    }

    [RelayCommand]
    private void Back() => BackRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !CommunityCatalog.IsSafeUrl(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Error($"Could not open the Community link '{url}'.", ex);
        }
    }
}
