using System;
using System.Drawing;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using Velopack;
using MessageBox = System.Windows.MessageBox;
using Velopack.Sources;
using System.Windows.Controls;

namespace Cake;

public partial class App : System.Windows.Application
{
    private NotifyIcon? _trayIcon;

    private String _version= "1.0.0";
    [STAThread]
    private static void Main(string[] args)
    {

        bool createdNew;
        using (var mutex = new System.Threading.Mutex(true, "IconizerAppMutex", out createdNew))
        {
            if (!createdNew)
            {
                MessageBox.Show("App already running.", "Unique instance", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                VelopackApp.Build().Run();

                var app = new App();
                app.InitializeComponent();
                app.Run();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fatal error on startup runtime: {ex.Message}", "Start failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public void ActualVersion()
    {
        var token = "";
        var source = new Velopack.Sources.GithubSource("https://github.com/NexusOnePlus/Cake", token, true);
        var updateManager = new UpdateManager(source);
        string version;
        if (updateManager.IsInstalled)
        {
            version = updateManager.CurrentVersion?.ToString() ?? "Unknown";
        }
        else
        {
            version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Debug";
        }
        _version = version;
    }



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
        ActualVersion();
        contextMenu.Items.Add("Version: " + _version);
        contextMenu.Items.Add("Update", null, (s, ev) => {
            Update();
            ActualVersion();
            }
        ); 
        contextMenu.Items.Add("Exit", null, (s, ev) => Shutdown());
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



    private async void Update()
    {
        var token = "";
        var source = new Velopack.Sources.GithubSource("https://github.com/NexusOnePlus/Cake", token, true);
        var updateManager = new UpdateManager(source);
        try
        {
            if (!updateManager.IsInstalled)
            {
                MessageBox.Show("Updates can only be checked in an installed version of the application.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var newVersion = await updateManager.CheckForUpdatesAsync();
                if (newVersion == null)
                {
                    MessageBox.Show("Your application is up to date.", "No Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                try
                {
                    await updateManager.DownloadUpdatesAsync(newVersion);
                }
                catch (Exception exDownload)
                {
                    MessageBox.Show($"Error during download:\n{exDownload}", "Download Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                try
                {
                    updateManager.ApplyUpdatesAndRestart(newVersion);
                }
                catch (Exception exApply)
                {
                    MessageBox.Show($"Error applying update:\n{exApply}", "Apply Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            catch (Exception exCheck)
            {
                MessageBox.Show($"Error checking for updates:\n{exCheck}", "Check Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"General error in update process:\n{ex}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }


}
