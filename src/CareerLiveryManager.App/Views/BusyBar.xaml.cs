using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace CareerLiveryManager.App.Views;

/// <summary>An indeterminate progress bar that only animates while it's visible.</summary>
public partial class BusyBar : UserControl
{
    public BusyBar()
    {
        InitializeComponent();
    }

    private void BusyBar_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            var travel = Math.Max(ActualWidth, 300);
            var slide = new DoubleAnimation(-Segment.Width, travel, TimeSpan.FromSeconds(1.2))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Slide.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slide);
        }
        else
        {
            Slide.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
        }
    }
}
