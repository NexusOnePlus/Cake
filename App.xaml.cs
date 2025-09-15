using System;
using System.Drawing;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;

namespace Cake;

public partial class App : System.Windows.Application
{
    private NotifyIcon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        KeyboardHook.Start();

   

        _trayIcon = new NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(
        System.Reflection.Assembly.GetExecutingAssembly().Location
    ),
            Visible = true,
            Text = "Cake Switcher"
        };

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Salir", null, (s, ev) => Shutdown());
        _trayIcon.ContextMenuStrip = contextMenu;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        KeyboardHook.Stop();

        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        base.OnExit(e);
    }
}
