using System.Windows;
using System.Windows.Controls;
using CareerLiveryManager.App.ViewModels;

namespace CareerLiveryManager.App.Views;

public partial class ApplyLiveryView : UserControl
{
    public ApplyLiveryView()
    {
        InitializeComponent();
    }

    private ApplyLiveryViewModel? ViewModel => DataContext as ApplyLiveryViewModel;

    private static bool HasFiles(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop);

    private void Root_DragEnter(object sender, DragEventArgs e) => UpdateDrag(e);

    private void Root_DragOver(object sender, DragEventArgs e) => UpdateDrag(e);

    private void UpdateDrag(DragEventArgs e)
    {
        var accept = HasFiles(e) && ViewModel is { IsBusy: false, ShowSuccessModal: false };
        e.Effects = accept ? DragDropEffects.Copy : DragDropEffects.None;
        if (ViewModel is { } vm)
        {
            vm.IsDragOver = accept;
        }

        e.Handled = true;
    }

    private void Root_DragLeave(object sender, DragEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            vm.IsDragOver = false;
        }
    }

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        vm.IsDragOver = false;
        e.Handled = true;

        if (vm is { IsBusy: true } or { ShowSuccessModal: true } || e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            return;
        }

        // A folder is inspected as is and a .zip is extracted first. Any other dropped file is passed on
        // too, so the screen can explain why it can't be used instead of silently ignoring the drop.
        await vm.LoadLiverySourceAsync(paths[0]);
    }
}
