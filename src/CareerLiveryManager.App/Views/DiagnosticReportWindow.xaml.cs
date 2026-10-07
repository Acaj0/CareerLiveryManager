using System.Windows;
using CareerLiveryManager.Core.Services;

namespace CareerLiveryManager.App.Views;

/// <summary>
/// Tells the user exactly what the diagnostic report contains before anything is written, and asks
/// whether to hide their Windows username. The lists come from <see cref="DiagnosticReportBuilder"/>
/// so the text can't drift from what it actually collects.
/// </summary>
public partial class DiagnosticReportWindow : Window
{
    public DiagnosticReportWindow()
    {
        InitializeComponent();
        IncludedList.ItemsSource = DiagnosticReportBuilder.IncludedItems.Select(i => "•  " + i).ToList();
        ExcludedList.ItemsSource = DiagnosticReportBuilder.NeverIncludedItems.Select(i => "•  " + i).ToList();
    }

    /// <summary>True when the "Hide my Windows username" box was ticked (it starts ticked).</summary>
    public bool HideUserName => HideUserNameCheckBox.IsChecked == true;

    private void Export_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
