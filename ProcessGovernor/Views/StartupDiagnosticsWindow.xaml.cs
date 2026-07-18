using System.Windows;
using System.Windows.Media.Animation;
using ProcessGovernor.Core;

namespace ProcessGovernor.Views;

public partial class StartupDiagnosticsWindow : Window
{
    public StartupDiagnosticsWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowAppearance.TryEnableDarkTitleBar(this);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            Opacity = 1;
            StartupTranslate.Y = 0;
            return;
        }

        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = ease
        });
        StartupTranslate.BeginAnimation(
            System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
    }
}
