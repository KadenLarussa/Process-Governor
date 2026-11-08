using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProcessGovernor.ViewModels;

namespace ProcessGovernor.Views;

public partial class DashboardView : System.Windows.Controls.UserControl
{
    private bool _contextMenuOpen;

    public DashboardView()
    {
        InitializeComponent();
    }

    private void DataGridRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow row)
        {
            ProcessGrid.SelectedItems.Clear();
            ProcessGrid.SelectedItem = row.Item;
            row.Focus();
        }
    }

    private void ProcessGrid_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) => UpdateOrderHold();

    private void ProcessGrid_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => UpdateOrderHold();

    private void ProcessGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateOrderHold();

    private void ProcessGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        _contextMenuOpen = true;
        UpdateOrderHold();
    }

    private void ProcessGrid_ContextMenuClosing(object sender, ContextMenuEventArgs e)
    {
        _contextMenuOpen = false;
        UpdateOrderHold();
    }

    private void ProcessGrid_Unloaded(object sender, RoutedEventArgs e)
    {
        _contextMenuOpen = false;
        if (DataContext is DashboardViewModel viewModel)
        {
            viewModel.HoldProcessOrder = false;
        }
    }

    private void UpdateOrderHold()
    {
        if (DataContext is DashboardViewModel viewModel)
        {
            viewModel.HoldProcessOrder = _contextMenuOpen || ProcessGrid.IsMouseOver || ProcessGrid.SelectedItems.Count > 1;
        }
    }
}
