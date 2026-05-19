using System.Windows.Input;

namespace ProcessGovernor.Views;

public partial class DashboardView : System.Windows.Controls.UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private void DataGridRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.DataGridRow row)
        {
            row.IsSelected = true;
            row.Focus();
        }
    }
}
