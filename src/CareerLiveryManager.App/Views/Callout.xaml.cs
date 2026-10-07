using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CareerLiveryManager.App.Views;

public enum CalloutKind
{
    Warning,
    Info,
}

/// <summary>An inline notice: <see cref="Kind"/> picks the colors and icon, <see cref="Text"/> the message,
/// and an optional <see cref="Title"/> a bold first line.</summary>
public partial class Callout : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(Callout), new PropertyMetadata(string.Empty, (d, _) => ((Callout)d).Refresh()));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(Callout), new PropertyMetadata(string.Empty, (d, _) => ((Callout)d).Refresh()));

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(CalloutKind), typeof(Callout), new PropertyMetadata(CalloutKind.Warning, (d, _) => ((Callout)d).Refresh()));

    public Callout()
    {
        InitializeComponent();
        Refresh();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public CalloutKind Kind
    {
        get => (CalloutKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    private void Refresh()
    {
        var warning = Kind == CalloutKind.Warning;

        Frame.Style = (Style)FindResource(warning ? "WarningCallout" : "InfoCallout");
        Glyph.Data = (Geometry)FindResource(warning ? "IconWarning" : "IconInfo");
        Glyph.Stroke = (Brush)FindResource(warning ? "WarningBrush" : "AccentBrush");

        var text = (Brush)FindResource(warning ? "WarningBrush" : "TextSecondaryBrush");
        TitleText.Foreground = text;
        BodyText.Foreground = text;
        BodyText.Opacity = warning && !string.IsNullOrEmpty(Title) ? 0.85 : 1;

        TitleText.Text = Title;
        TitleText.Visibility = string.IsNullOrEmpty(Title) ? Visibility.Collapsed : Visibility.Visible;
        BodyText.Text = Text;
        BodyText.FontSize = string.IsNullOrEmpty(Title) ? 13 : 12;
    }
}
