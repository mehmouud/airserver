using System.Windows;

namespace AirServer;

public partial class App : Application
{
    private Mutex? _single;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        bool demo = e.Args.Contains("--demo", StringComparer.OrdinalIgnoreCase);

        if (!demo)
        {
            _single = new Mutex(true, @"Local\AirServer.SingleInstance", out bool first);
            if (!first)
            {
                NativeMethods.ActivateExistingWindow("AirServer");
                Shutdown();
                return;
            }
        }

        var settings = Settings.Load();
        settings.Persist = !demo;
        var window = new MainWindow(settings, demo);
        MainWindow = window;
        // demo only: open a panel directly (used for UI screenshots) without stealing keyboard focus
        int i = Array.FindIndex(e.Args, a => a.Equals("--open", StringComparison.OrdinalIgnoreCase));
        if (demo && i >= 0) window.ShowActivated = false;
        window.Show();
        if (demo && i >= 0 && i + 1 < e.Args.Length) window.OpenPanel(e.Args[i + 1]);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _single?.Dispose();
        base.OnExit(e);
    }
}
