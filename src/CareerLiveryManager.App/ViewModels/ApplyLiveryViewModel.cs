using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using CareerLiveryManager.Core.Models;
using CareerLiveryManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace CareerLiveryManager.App.ViewModels;

public sealed partial class ApplyLiveryViewModel : ObservableObject, IDisposable
{
    private const string CloseSimMessage = "Close MSFS completely before applying the livery.";

    private readonly LiverySourceInspector _liveryInspector;
    private readonly PackageBuilder _packageBuilder;
    private readonly InstalledPackagesManager _installedPackagesManager;
    private readonly SimProcessChecker _simProcessChecker;
    private readonly LogService _log;
    private readonly AppConfig _config;
    private readonly ArchiveExtractor _archiveExtractor = new();

    /// <summary>The temporary folder a dropped .zip was extracted to; the chosen livery is read from here
    /// until another livery replaces it or the screen is left.</summary>
    private string? _extractedFolder;

    /// <summary>Re-checks whether MSFS is running while this screen is open, so the "close the game"
    /// banner goes away by itself once the user has done it.</summary>
    private readonly DispatcherTimer _simWatchTimer;

    public event EventHandler? BackRequested;

    public AircraftInfo Aircraft { get; }

    /// <summary>The Career activity being customized (Cargo Transport, VIP/Charter...), or null for
    /// single-livery aircraft that don't use the activity system at all.</summary>
    public AircraftActivityInfo? Activity { get; }

    public bool HasActivity => Activity is not null;

    public ObservableCollection<LiverySourceInfo> DetectedLiveries { get; } = new();

    /// <summary>True once a folder has been chosen and contained at least one livery.</summary>
    public bool HasDetectedLiveries => DetectedLiveries.Count > 0;

    [ObservableProperty]
    private LiverySourceInfo? _selectedLivery;

    [ObservableProperty]
    private bool _useDynamicRegistration = true;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _statusMessageColor = "#D9A03C";

    /// <summary>Why the chosen folder couldn't be used (compressed download, old format...). Shown as
    /// a callout under the drop zone, separate from the progress/result line.</summary>
    [ObservableProperty]
    private string _sourceProblemMessage = string.Empty;

    /// <summary>The livery looks like it belongs to another aircraft (or its aircraft can't be told).</summary>
    [ObservableProperty]
    private string _aircraftWarning = string.Empty;

    [ObservableProperty]
    private PackagePreview? _preview;

    /// <summary>"412 files · 1.2 GB", shown on the preview.</summary>
    [ObservableProperty]
    private string _previewSummary = string.Empty;

    /// <summary>Set when the Community drive is short on room for this copy. Empty otherwise.</summary>
    [ObservableProperty]
    private string _diskSpaceWarning = string.Empty;

    [ObservableProperty]
    private bool _isSimRunning;

    [ObservableProperty]
    private bool _showSuccessModal;

    [ObservableProperty]
    private string _successPackageFolder = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>True while a folder is being dragged over the window (drives the drop overlay).</summary>
    [ObservableProperty]
    private bool _isDragOver;

    public ApplyLiveryViewModel(
        LiverySourceInspector liveryInspector,
        PackageBuilder packageBuilder,
        InstalledPackagesManager installedPackagesManager,
        SimProcessChecker simProcessChecker,
        LogService log,
        AppConfig config,
        AircraftInfo aircraft,
        AircraftActivityInfo? activity = null)
    {
        _liveryInspector = liveryInspector;
        _packageBuilder = packageBuilder;
        _installedPackagesManager = installedPackagesManager;
        _simProcessChecker = simProcessChecker;
        _log = log;
        _config = config;
        Aircraft = aircraft;
        Activity = activity;
        RefreshSimRunning();

        _simWatchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _simWatchTimer.Tick += (_, _) => RefreshSimRunning();
        _simWatchTimer.Start();
    }

    public void Dispose()
    {
        _simWatchTimer.Stop();
        ReleaseExtractedFolder();
    }

    private void ReleaseExtractedFolder()
    {
        if (_extractedFolder is { } folder)
        {
            _extractedFolder = null;
            _archiveExtractor.Delete(folder);
        }
    }

    /// <summary>Enabled only once a preview has been generated and no apply is already running.</summary>
    public bool CanApply => Preview is not null && !IsBusy;

    /// <summary>A preview needs a livery to look at.</summary>
    public bool CanGeneratePreview => SelectedLivery is not null && !IsBusy;

    partial void OnSelectedLiveryChanged(LiverySourceInfo? value)
    {
        OnPropertyChanged(nameof(CanGeneratePreview));
        Preview = null;
        AircraftWarning = value is null
            ? string.Empty
            : LiverySourceInspector.DescribeAircraftMismatch(value.DetectedSimObjectName, Aircraft.SimObjectName) ?? string.Empty;
    }

    partial void OnUseDynamicRegistrationChanged(bool value) => Preview = null;

    partial void OnPreviewChanged(PackagePreview? value)
    {
        OnPropertyChanged(nameof(CanApply));
        if (value is null)
        {
            PreviewSummary = string.Empty;
            DiskSpaceWarning = string.Empty;
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanGeneratePreview));
    }

    partial void OnIsSimRunningChanged(bool value)
    {
        // The game was closed: drop the "close it first" message instead of leaving it contradicting the banner.
        if (!value && StatusMessage == CloseSimMessage)
        {
            StatusMessage = string.Empty;
        }
    }

    [RelayCommand]
    private async Task BrowseLiverySource()
    {
        var dialog = new OpenFolderDialog { Title = "Select the livery folder (the one containing SimObjects\\...)" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await LoadLiverySourceAsync(dialog.FolderName);
    }

    [RelayCommand]
    private async Task BrowseLiveryArchive()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select the livery archive you downloaded",
            Filter = $"Livery archives ({ArchiveExtractor.DialogFilterPattern})|{ArchiveExtractor.DialogFilterPattern}",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await LoadLiverySourceAsync(dialog.FileName);
    }

    /// <summary>
    /// Shows what a chosen or dropped path holds. A folder is inspected directly; a .zip is first
    /// extracted into a private temporary folder (the download itself is never touched), then
    /// inspected the same way.
    /// </summary>
    public async Task LoadLiverySourceAsync(string path)
    {
        if (IsBusy)
        {
            return;
        }

        SourceProblemMessage = string.Empty;
        StatusMessage = string.Empty;
        StatusMessageColor = "#D9A03C";

        var folder = path;
        string? extracted = null;

        if (ArchiveExtractor.CanExtract(path))
        {
            var archiveName = Path.GetFileName(path);
            IsBusy = true; // keeps Apply/preview off and shows the progress bar while it works
            StatusMessage = $"Extracting {archiveName}...";
            try
            {
                var progress = new Progress<double>(p => StatusMessage = $"Extracting {archiveName}... {p:P0}");
                extracted = await Task.Run(() => _archiveExtractor.Extract(path, progress));
                folder = extracted;
                _log.Info($"Extracted '{path}' to '{extracted}'.");
            }
            catch (ArchiveExtractionException ex)
            {
                _log.Info($"Couldn't extract '{path}' ({ex.Kind}): {ex.Message}");
                SourceProblemMessage = ex.Message;
                return;
            }
            catch (Exception ex)
            {
                _log.Error($"Unexpected error extracting '{path}'.", ex);
                SourceProblemMessage = $"Couldn't extract the zip: {ex.Message}";
                return;
            }
            finally
            {
                IsBusy = false;
                if (StatusMessage.StartsWith("Extracting", StringComparison.Ordinal))
                {
                    StatusMessage = string.Empty;
                }
            }
        }

        if (!TryLoadFolder(folder))
        {
            if (extracted is not null)
            {
                _archiveExtractor.Delete(extracted);
            }

            return;
        }

        // Only now that the new livery is in place is the previous extraction no longer needed.
        ReleaseExtractedFolder();
        _extractedFolder = extracted;
    }

    /// <summary>Inspects a folder and, if it holds liveries, makes them the current selection. Returns false (and says why) otherwise.</summary>
    private bool TryLoadFolder(string path)
    {
        IReadOnlyList<LiverySourceInfo> found = Array.Empty<LiverySourceInfo>();
        if (Directory.Exists(path))
        {
            try
            {
                found = _liveryInspector.Inspect(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Error($"Couldn't read the livery folder '{path}'.", ex);
                SourceProblemMessage = $"Couldn't read that folder: {ex.Message}";
                return false;
            }
        }

        if (found.Count == 0)
        {
            var problem = _liveryInspector.Diagnose(path);
            _log.Info($"Livery folder '{path}' not usable ({problem.Kind}).");
            SourceProblemMessage = problem.Message;
            return false;
        }

        DetectedLiveries.Clear();
        Preview = null;
        foreach (var livery in found)
        {
            DetectedLiveries.Add(livery);
        }

        OnPropertyChanged(nameof(HasDetectedLiveries));
        SelectedLivery = DetectedLiveries[0];
        return true;
    }

    [RelayCommand]
    private void GeneratePreview()
    {
        if (SelectedLivery is null)
        {
            return;
        }

        var request = BuildRequest();
        var preview = _packageBuilder.Preview(request, _config.CommunityPath);

        PreviewSummary = $"{preview.FileCount:N0} {(preview.FileCount == 1 ? "file" : "files")} · {FileSizeFormatter.Format(preview.TotalBytes)}";
        DiskSpaceWarning = DescribeDiskSpace(DiskSpaceChecker.Check(_config.CommunityPath, preview.TotalBytes));
        Preview = preview;
    }

    private static string DescribeDiskSpace(DiskSpaceCheck check)
    {
        if (check.Status != DiskSpaceStatus.Low)
        {
            return string.Empty;
        }

        var need = FileSizeFormatter.Format(check.RequiredBytes);
        var have = FileSizeFormatter.Format(check.FreeBytes);
        return check.IsCertainlyShort
            ? $"There isn't enough free space on the drive that holds your Community folder: this livery needs {need} and only {have} is free. Free up some space first."
            : $"Free space is getting tight on the drive that holds your Community folder: this livery needs {need} and {have} is free. Free up some space first, or the copy could stop halfway.";
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
            StatusMessage = CloseSimMessage;
            return;
        }

        // The free space may have changed since the preview was made.
        if (Preview is { } planned)
        {
            var space = DiskSpaceChecker.Check(_config.CommunityPath, planned.TotalBytes);
            if (space.IsCertainlyShort)
            {
                DiskSpaceWarning = DescribeDiskSpace(space);
                StatusMessageColor = "#D95C5C";
                StatusMessage = "Not enough free disk space to copy this livery.";
                return;
            }
        }

        IsBusy = true;
        try
        {
            // Find any package this app previously created for this exact aircraft *and this exact
            // activity* (a Cargo Transport livery must never remove a Flightseeing one on the same
            // plane - see CAREER_LIVERY_RESEARCH.md section 19), so it can be replaced instead of
            // left behind.
            var previousPackages = _installedPackagesManager.List(_config.CommunityPath)
                .Where(p => string.Equals(p.AircraftSimObjectName, Aircraft.SimObjectName, StringComparison.OrdinalIgnoreCase))
                .Where(p => Activity is null ? !p.HasActivity : string.Equals(p.ActivityKey, Activity.ActivityKey, StringComparison.OrdinalIgnoreCase))
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
            var applyResult = await Task.Run(() => _packageBuilder.ApplyWithNotes(request, _config.CommunityPath));
            var packageFolder = applyResult.PackageFolder;

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
            var activityNote = Activity is null ? string.Empty : $", activity='{Activity.ActivityKey}'";
            _log.Info($"Livery applied: aircraft='{Aircraft.Title}' ({Aircraft.SimObjectName}){activityNote}, " +
                      $"livery='{SelectedLivery.BaseFolderName}', useDr={UseDynamicRegistration}, package='{packageFolder}'.");
            foreach (var note in applyResult.Notes)
            {
                _log.Info($"  -> {note}");
            }
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

    [RelayCommand]
    private void OpenPackageFolder()
    {
        try
        {
            if (Directory.Exists(SuccessPackageFolder))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{SuccessPackageFolder}\"") { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Couldn't open the package folder '{SuccessPackageFolder}'.", ex);
        }
    }

    private void RefreshSimRunning() => IsSimRunning = _simProcessChecker.IsSimRunning();

    private ApplyLiveryRequest BuildRequest()
    {
        var activitySegment = Activity is null ? string.Empty : $"-{Activity.ActivityKey}";
        var packageName = $"career-livery-{Aircraft.SimObjectName}{activitySegment}-{SelectedLivery!.BaseFolderName}".Replace(' ', '-');

        return new ApplyLiveryRequest
        {
            Aircraft = Aircraft,
            Source = SelectedLivery,
            UseDr = UseDynamicRegistration,
            PackageName = packageName,
            Activity = Activity,
        };
    }
}
