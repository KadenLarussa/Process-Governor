using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;
using ProcessGovernor.Core;

namespace ProcessGovernor.Services;

public sealed class NotificationService : INotificationService
{
    private readonly ISettingsService _settingsService;
    private readonly Lazy<Forms.NotifyIcon> _notifyIcon;
    private Window? _window;
    private bool _isDisposed;

    public NotificationService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _notifyIcon = new Lazy<Forms.NotifyIcon>(CreateNotifyIcon);
    }

    public void Attach(Window window)
    {
        _window = window;
        _notifyIcon.Value.Visible = true;
    }

    public Task ShowAsync(string title, string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_settingsService.Current.EnableNotifications)
        {
            return Task.CompletedTask;
        }

        var icon = _notifyIcon.Value;
        icon.BalloonTipTitle = title;
        icon.BalloonTipText = message;
        icon.ShowBalloonTip(3500);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        if (_notifyIcon.IsValueCreated)
        {
            _notifyIcon.Value.Visible = false;
            _notifyIcon.Value.Dispose();
        }

        _isDisposed = true;
    }

    private Forms.NotifyIcon CreateNotifyIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowWindow());
        menu.Items.Add("Exit", null, (_, _) =>
        {
            if (_window is MainWindow mainWindow)
            {
                mainWindow.AllowExit();
            }

            System.Windows.Application.Current.Dispatcher.Invoke(System.Windows.Application.Current.Shutdown);
        });

        var notifyIcon = new Forms.NotifyIcon
        {
            Text = AppConstants.AppName,
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = false
        };
        notifyIcon.DoubleClick += (_, _) => ShowWindow();
        return notifyIcon;
    }

    private void ShowWindow()
    {
        if (_window is null)
        {
            return;
        }

        _window.Dispatcher.Invoke(() =>
        {
            _window.Show();
            _window.WindowState = System.Windows.WindowState.Normal;
            _window.Activate();
        });
    }
}
