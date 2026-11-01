using System.Diagnostics;
using System.Text;

namespace AirServer;

/// <summary>Starts/stops uxplay.exe and streams its output lines.</summary>
public sealed class UxPlayHost : IDisposable
{
    private Process? _proc;

    public event Action<string>? LineReceived;
    public event Action<int>? Exited;

    public bool IsRunning => _proc is { HasExited: false };

    /// <summary>
    /// Finds the engine: first the bundled copy next to the app (dist layout),
    /// then a developer MSYS2 install.
    /// </summary>
    public static (string Exe, string? BundleDir)? LocateEngine()
    {
        string bundled = Path.Combine(AppContext.BaseDirectory, "engine", "uxplay.exe");
        if (File.Exists(bundled)) return (bundled, Path.GetDirectoryName(bundled));
        foreach (var dev in new[] { @"C:\msys64\ucrt64\bin\uxplay.exe" })
            if (File.Exists(dev)) return (dev, null);
        return null;
    }

    public void Start(IReadOnlyList<string> args)
    {
        if (IsRunning) throw new InvalidOperationException("déjà démarré");
        var engine = LocateEngine() ?? throw new FileNotFoundException(
            "Moteur introuvable : engine\\uxplay.exe (lancer scripts\\build.ps1).");

        var psi = new ProcessStartInfo(engine.Exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(engine.Exe)!,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        string engineDir = Path.GetDirectoryName(engine.Exe)!;
        psi.Environment["PATH"] = engineDir + ";" + Environment.GetEnvironmentVariable("PATH");
        if (engine.BundleDir is { } b)
        {
            // Self-contained GStreamer: never pick up plugins from another install.
            psi.Environment["GST_PLUGIN_SYSTEM_PATH_1_0"] = Path.Combine(b, "lib", "gstreamer-1.0");
            psi.Environment["GST_PLUGIN_PATH_1_0"] = "";
            psi.Environment["GST_PLUGIN_SCANNER_1_0"] = Path.Combine(b, "libexec", "gst-plugin-scanner.exe");
            string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AirServer");
            Directory.CreateDirectory(cache);
            psi.Environment["GST_REGISTRY_1_0"] = Path.Combine(cache, "gst-registry.bin");
        }

        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) LineReceived?.Invoke(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) LineReceived?.Invoke(e.Data); };
        p.Exited += (_, _) => Exited?.Invoke(SafeExitCode(p));
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        _proc = p;
        LineReceived?.Invoke("> " + engine.Exe + " " + string.Join(' ', args.Select(Quote)));
    }

    /// <summary>Graceful stop (Ctrl+C → mDNS goodbye), then kill after a timeout.</summary>
    public void Stop(int graceMs = 3000)
    {
        var p = _proc;
        if (p is null || p.HasExited) return;
        try
        {
            if (NativeMethods.SendCtrlC(p.Id) && p.WaitForExit(graceMs)) return;
        }
        catch (Exception) { /* fall through to kill */ }
        try { p.Kill(entireProcessTree: true); p.WaitForExit(2000); } catch (Exception) { }
    }

    private static int SafeExitCode(Process p) { try { return p.ExitCode; } catch { return -1; } }

    private static string Quote(string s) => s.Contains(' ') || s.Length == 0 ? $"\"{s}\"" : s;

    public void Dispose()
    {
        Stop(1500);
        _proc?.Dispose();
    }
}
