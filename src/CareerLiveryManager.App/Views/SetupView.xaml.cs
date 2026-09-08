using System.Windows.Controls;
using System.Windows.Navigation;

namespace CareerLiveryManager.App.Views;

public partial class SetupView : UserControl
{
    public SetupView()
    {
        InitializeComponent();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
