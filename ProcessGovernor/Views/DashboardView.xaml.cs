using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace ProcessGovernor.Views;

public partial class DashboardView : System.Windows.Controls.UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private void DataGridRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow row)
        {
            row.IsSelected = true;
            row.Focus();
        }
    }

    private void ProcessGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        ProcessInspector.BeginAnimation(OpacityProperty, null);
        InspectorTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
        ProcessInspector.Opacity = 1;
        InspectorTranslate.Y = 0;

        if (!SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        ProcessInspector.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0.68, 1, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = easing
            });
        InspectorTranslate.BeginAnimation(
            System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(5, 0, TimeSpan.FromMilliseconds(170))
            {
                EasingFunction = easing
            });
    }
}
