using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace AirServer;

public enum ReceiverState { Stopped, Starting, Waiting, Connecting, Mirroring, Error }

/// <summary>A network the receiver is reachable on, in words a non-technical user understands.</summary>
public sealed record FriendlyNetwork(string Label, string Glyph, string Address);

/// <summary>
/// UI-agnostic receiver logic: owns the uxplay process, parses its output and exposes a simple
/// state machine. All events are raised on the SynchronizationContext it was created on (UI thread).
/// </summary>
public sealed class ReceiverController : IDisposable
{
    private readonly SynchronizationContext _ui;
    private readonly UxPlayHost _host = new();
    private readonly LogParser _parser = new();
    private readonly NetworkWatcher _network = new();
    private readonly System.Threading.Timer _heartbeat;
    private readonly List<DateTime> _crashes = [];
    private DateTime _lastStats = DateTime.MinValue;
    private bool _userStopped;
    private bool _restartPending;
    private CancellationTokenSource? _demo;

    public Settings Settings { get; }
    public bool DemoMode { get; }
    public ReceiverState State { get; private set; } = ReceiverState.Stopped;
    public ClientInfo? Client { get; private set; }
    public StreamInfo? Stream { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? CommandLine { get; private set; }
    public IReadOnlyDictionary<string, string> Stats => _stats;
    private readonly Dictionary<string, string> _stats = [];

    /// <summary>Raised whenever State / Client / Stream / ErrorMessage change.</summary>
    public event Action? Changed;
    public event Action? StatsChanged;
    public event Action<string>? LogLine;
    /// <summary>Short message for a transient in-app notification.</summary>
    public event Action<string>? Toast;

    public ReceiverController(Settings settings, bool demoMode = false)
    {
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        Settings = settings;
        DemoMode = demoMode;

        _host.LineReceived += line => Post(() => OnLine(line));
        _host.Exited += code => Post(() => OnEngineExited(code));
        _parser.ClientConnected += c => { Client = c; Stream = null; _stats.Clear(); SetState(ReceiverState.Connecting); };
        _parser.StreamStarted += s => { Stream = s; _lastStats = DateTime.UtcNow; SetState(ReceiverState.Mirroring); };
        _parser.StatsReceived += OnStats;
        _parser.Error += e => { if (e.Contains("h265", StringComparison.OrdinalIgnoreCase)) Toast?.Invoke("Cet iPhone envoie de l'HEVC : choisissez la qualité « Maximale »."); };
        _network.Changed += () => Post(OnNetworkChanged);
        _heartbeat = new System.Threading.Timer(_ => Post(Heartbeat), null, 1000, 1000);
    }

    private void Post(Action a) => _ui.Post(_ => a(), null);

    // ------------------------------------------------------------------ commands

    public void Start()
    {
        if (DemoMode) { StartDemo(); return; }
        if (_host.IsRunning) return;
        if (UxPlayHost.LocateEngine() is null)
        {
            Fail("Le composant de réception est introuvable. Réinstallez AirServer.");
            return;
        }
        _userStopped = false;
        ErrorMessage = null;
        Client = null; Stream = null; _stats.Clear(); StatsChanged?.Invoke();
        SetState(ReceiverState.Starting);
        try
        {
            var (_, _, hz) = NativeMethods.PrimaryDisplayMode();
            var args = Settings.BuildArguments(hz);
            _network.Rebase();
            _host.Start(args);
            CommandLine = "uxplay " + string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
            Log("[interfaces : " + NetworkWatcher.CurrentSignature() + "]");
            SetState(ReceiverState.Waiting);
        }
        catch (Exception ex)
        {
            Log("ERREUR : " + ex);
            Fail("Impossible de démarrer la réception.");
        }
    }

    public void Stop()
    {
        _userStopped = true;
        _demo?.Cancel();
        _host.Stop();
        Client = null; Stream = null;
        SetState(ReceiverState.Stopped);
    }

    /// <summary>Restarts so new settings take effect; deferred while a mirroring session is live.</summary>
    public void ApplySettings()
    {
        Settings.Save();
        if (State is ReceiverState.Stopped or ReceiverState.Error) return;
        if (State == ReceiverState.Mirroring) { _restartPending = true; Toast?.Invoke("Les réglages s'appliqueront à la prochaine recopie."); return; }
        Restart();
    }

    private void Restart()
    {
        _restartPending = false;
        if (DemoMode) { StartDemo(); return; }
        _host.Stop();
        Start();
    }

    // ------------------------------------------------------------------ engine events

    private void OnLine(string line)
    {
        Log(line);
        _parser.Feed(line);
    }

    private void OnStats(IReadOnlyList<KeyValuePair<string, string>> stats)
    {
        _lastStats = DateTime.UtcNow;
        foreach (var (k, v) in stats) _stats[k] = v;
        if (State != ReceiverState.Mirroring) SetState(ReceiverState.Mirroring);
        StatsChanged?.Invoke();
    }

    private void OnEngineExited(int code)
    {
        if (_host.IsRunning) return;   // a previous engine, already replaced by a restart
        Log($"[moteur arrêté, code {code}]");
        if (_userStopped) { SetState(ReceiverState.Stopped); return; }

        // unexpected exit: restart silently, but give up after 3 crashes in a minute
        _crashes.RemoveAll(t => DateTime.UtcNow - t > TimeSpan.FromMinutes(1));
        _crashes.Add(DateTime.UtcNow);
        if (_crashes.Count <= 3)
        {
            Toast?.Invoke("La réception a été relancée automatiquement.");
            Start();
        }
        else
        {
            Fail("La réception s'arrête de façon répétée.");
        }
    }

    private void Heartbeat()
    {
        if (State == ReceiverState.Mirroring && DateTime.UtcNow - _lastStats > TimeSpan.FromSeconds(3) && !DemoMode)
        {
            Stream = null;
            SetState(_host.IsRunning ? ReceiverState.Waiting : ReceiverState.Stopped);
        }
        if (_restartPending && State != ReceiverState.Mirroring) Restart();
    }

    private void OnNetworkChanged()
    {
        Changed?.Invoke();   // the "visible on" networks changed
        if (!_host.IsRunning) return;
        Log("[réseau modifié : " + NetworkWatcher.CurrentSignature() + "]");
        _restartPending = true;
        if (State != ReceiverState.Mirroring) Restart();
    }

    // ------------------------------------------------------------------ helpers

    private void Fail(string message)
    {
        ErrorMessage = message;
        SetState(ReceiverState.Error);
    }

    private void SetState(ReceiverState s)
    {
        State = s;
        Changed?.Invoke();
    }

    private void Log(string line) => LogLine?.Invoke(line);

    /// <summary>Networks an iPhone can use to reach this PC, with plain-language labels.</summary>
    public static List<FriendlyNetwork> FriendlyNetworks()
    {
        var list = new List<FriendlyNetwork>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            string d = ni.Description;
            bool vm = d.Contains("VirtualBox") || d.Contains("VMware") || d.Contains("Hyper-V") || d.Contains("vEthernet") || d.Contains("WSL");
            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var b = ua.Address.GetAddressBytes();
                if (b[0] == 169 && b[1] == 254) continue;
                string ip = ua.Address.ToString();
                if (b[0] == 192 && b[1] == 168 && b[2] == 137)
                    list.Add(new("Point d'accès mobile", "", ip));
                else if (vm)
                    continue;   // virtual-machine networks are noise for a phone user
                else if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    list.Add(new("Wi-Fi", "", ip));
                else
                    list.Add(new("Réseau filaire", "", ip));
            }
        }
        return list;
    }

    // ------------------------------------------------------------------ demo mode (UI testing without an iPhone)

    private async void StartDemo()
    {
        _demo?.Cancel();
        var cts = _demo = new CancellationTokenSource();
        try
        {
            _userStopped = false;
            Client = null; Stream = null; ErrorMessage = null; _stats.Clear(); StatsChanged?.Invoke();
            CommandLine = "uxplay (démo) " + string.Join(' ', Settings.BuildArguments(60));
            SetState(ReceiverState.Starting);
            await Task.Delay(800, cts.Token);
            SetState(ReceiverState.Waiting);
            int waitMs = int.TryParse(Environment.GetEnvironmentVariable("AIRSERVER_DEMO_WAIT"), out var w) ? w : 4000;
            await Task.Delay(waitMs, cts.Token);
            foreach (var line in new[]
            {
                "connection request from iPhone de Démo (iPhone17,2) with deviceID = 00:11:22:33:44:55",
                "*** codec is h265: geometry is (w,h)=(1320,2868) (source); (x,y)=(0,0); (w,h)=(1320,2868)",
            })
            {
                OnLine(line);
                await Task.Delay(1200, cts.Token);
            }
            var rnd = new Random(1);
            while (!cts.IsCancellationRequested)
            {
                OnStats([new("fps", (58 + rnd.NextDouble() * 2).ToString("0.0")), new("bitrate", (18 + rnd.Next(10)) + "000000"), new("droppedFrames", "0")]);
                await Task.Delay(1000, cts.Token);
            }
        }
        catch (TaskCanceledException) { }
    }

    public void Dispose()
    {
        _demo?.Cancel();
        _heartbeat.Dispose();
        _network.Dispose();
        _host.Dispose();
    }
}
