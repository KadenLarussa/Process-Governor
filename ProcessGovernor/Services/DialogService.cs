using System.Windows;

namespace ProcessGovernor.Services;

public sealed class DialogService : IDialogService
{
    public string? PickExecutable()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose an application",
            Filter = "Windows applications (*.exe)|*.exe",
            CheckFileExists = true,
            DereferenceLinks = true
        };
        return dialog.ShowDialog() == true ? Path.GetFileName(dialog.FileName) : null;
    }

    public bool Confirm(string title, string message)
    {
        return System.Windows.MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    public void ShowInformation(string title, string message)
    {
        System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void ShowWarning(string title, string message)
    {
        System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public void ShowError(string title, string message)
    {
        System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
