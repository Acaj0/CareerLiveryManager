using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CareerLiveryManager.App.ViewModels;

public sealed partial class HomeViewModel : ObservableObject
{
    public event EventHandler? GetStartedRequested;

    public string AuthorGitHubUrl => "https://github.com/Acaj0";

    [RelayCommand]
    private void GetStarted() => GetStartedRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenBuyMeACoffee() =>
        Process.Start(new ProcessStartInfo(MainViewModel.BuyMeACoffeeUrl) { UseShellExecute = true });
}
