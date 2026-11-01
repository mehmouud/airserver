using System.Diagnostics;

namespace AirServer;

public sealed class MainForm : Form
{
    private readonly Settings _settings = Settings.Load();
    private readonly UxPlayHost _host = new();
    private readonly LogParser _parser = new();
    private readonly System.Windows.Forms.Timer _heartbeat = new() { Interval = 1000 };
    private DateTime _lastStats = DateTime.MinValue;
    private bool _mirroring;
    private bool _userStopped;
    private bool _exiting;

    // settings controls
    private readonly TextBox _name = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _quality = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _cw = new() { Minimum = 320, Maximum = 9999, Width = 70 };
    private readonly NumericUpDown _ch = new() { Minimum = 240, Maximum = 9999, Width = 70 };
    private readonly CheckBox _chevc = new() { Text = "HEVC", AutoSize = true };
    private readonly ComboBox _fps = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _sink = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _fullscreen = new() { Text = "Plein écran au démarrage (Alt+Entrée pour basculer)", AutoSize = true };
    private readonly CheckBox _lowLatency = new() { Text = "Faible latence (désactive la synchro audio/vidéo)", AutoSize = true };
    private readonly CheckBox _audio = new() { Text = "Son", AutoSize = true };
    private readonly CheckBox _autoStart = new() { Text = "Démarrer le récepteur à l'ouverture", AutoSize = true };
    private readonly Button _startStop = new() { Text = "Démarrer", AutoSize = true, Padding = new Padding(10, 4, 10, 4) };
    private readonly Button _firewall = new() { Text = "Autoriser dans le pare-feu…", AutoSize = true, Padding = new Padding(6, 4, 6, 4) };
    private readonly Panel _customPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };

    // status controls
    private readonly Label _state = new() { AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 11f, FontStyle.Bold) };
    private readonly Label _client = new() { AutoSize = true };
    private readonly Label _stream = new() { AutoSize = true };
    private readonly Label _display = new() { AutoSize = true };
    private readonly ListView _stats = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable };
    private readonly TextBox _log = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font(FontFamily.GenericMonospace, 8.5f) };
    private readonly NotifyIcon _tray = new() { Text = "AirServer", Visible = true };

    public MainForm()
    {
        Text = "AirServer – Recopie d'écran iPhone";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        _tray.Icon = Icon;
        MinimumSize = new Size(900, 620);
        Size = new Size(1040, 720);
        StartPosition = FormStartPosition.CenterScreen;
        Font = SystemFonts.MessageBoxFont;

        BuildLayout();
        LoadSettingsIntoUi();
        Wire();

        var (dw, dh, hz) = NativeMethods.PrimaryDisplayMode();
        _display.Text = $"Écran du PC : {dw}×{dh} @ {hz} Hz";
    }

    // ------------------------------------------------------------------ layout

    private void BuildLayout()
    {
        var settings = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Padding = new Padding(8) };
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(string label, Control c)
        {
            settings.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 6, 3) });
            settings.Controls.Add(c);
        }
        void Full(Control c) { settings.Controls.Add(c); settings.SetColumnSpan(c, 2); }

        _quality.Items.AddRange([
            "Maximum – HEVC, jusqu'à 4K (recommandé)",
            "HEVC 1440p",
            "Compatibilité – H.264 1080p (anciens iPhone / Wi-Fi faible)",
            "Personnalisé…",
        ]);
        _fps.Items.AddRange(["30", "60"]);
        _sink.Items.AddRange(["d3d12videosink", "d3d11videosink"]);
        _customPanel.Controls.AddRange([_cw, new Label { Text = "×", AutoSize = true, Margin = new Padding(2, 7, 2, 0) }, _ch, _chevc]);

        Row("Nom AirPlay", _name);
        Row("Qualité demandée", _quality);
        Row("Personnalisé", _customPanel);
        Row("Images/s max", _fps);
        Row("Rendu GPU", _sink);
        Full(_fullscreen);
        Full(_lowLatency);
        Full(_audio);
        Full(_autoStart);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
        buttons.Controls.AddRange([_startStop, _firewall]);
        Full(buttons);
        Full(new Label
        {
            AutoSize = true, MaximumSize = new Size(380, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 10, 3, 3),
            Text = "Le débit est choisi par l'iPhone : son encodeur s'adapte en continu au réseau. " +
                   "L'application lui demande la résolution maximale et affiche ce qu'il envoie réellement. " +
                   "Pour la meilleure qualité : PC en Ethernet, iPhone en Wi-Fi 5/6 GHz sur le même réseau.",
        });

        var settingsBox = new GroupBox { Text = "Réglages", Dock = DockStyle.Fill, AutoSize = true };
        settingsBox.Controls.Add(settings);

        var statusTop = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(8) };
        foreach (var l in new[] { _state, _client, _stream, _display })
        {
            l.Margin = new Padding(3, 3, 3, 5);
            statusTop.Controls.Add(l);
        }
        _stats.Columns.Add("Statistique (envoyée par l'iPhone chaque seconde)", 300);
        _stats.Columns.Add("Valeur", 200);
        var statusBox = new GroupBox { Text = "État", Dock = DockStyle.Fill };
        statusBox.Controls.Add(_stats);
        statusBox.Controls.Add(statusTop);

        var top = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
        top.Panel1.Controls.Add(settingsBox);
        top.Panel2.Controls.Add(statusBox);

        var logBox = new GroupBox { Text = "Journal du moteur", Dock = DockStyle.Fill };
        logBox.Controls.Add(_log);

        var main = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        main.Panel1.Controls.Add(top);
        main.Panel2.Controls.Add(logBox);
        Controls.Add(main);

        Load += (_, _) =>
        {
            top.SplitterDistance = 420;
            main.SplitterDistance = (int)(ClientSize.Height * 0.66);
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Afficher", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Quitter", null, (_, _) => { _exiting = true; Close(); });
        _tray.ContextMenuStrip = menu;
    }

    private void LoadSettingsIntoUi()
    {
        _name.Text = _settings.ServerName;
        _quality.SelectedIndex = (int)_settings.Quality;
        _cw.Value = Math.Clamp(_settings.CustomWidth, 320, 9999);
        _ch.Value = Math.Clamp(_settings.CustomHeight, 240, 9999);
        _chevc.Checked = _settings.CustomHevc;
        _fps.SelectedItem = _settings.MaxFps >= 60 ? "60" : "30";
        _sink.SelectedItem = _sink.Items.Contains(_settings.VideoSink) ? _settings.VideoSink : "d3d12videosink";
        _fullscreen.Checked = _settings.Fullscreen;
        _lowLatency.Checked = _settings.LowLatency;
        _audio.Checked = _settings.Audio;
        _autoStart.Checked = _settings.AutoStart;
        _customPanel.Enabled = _settings.Quality == QualityPreset.Custom;
        SetState("Arrêté", Color.Gray);
        _client.Text = "Client : —";
        _stream.Text = "Flux : —";
    }

    private void ReadUiIntoSettings()
    {
        _settings.ServerName = _name.Text.Trim();
        _settings.Quality = (QualityPreset)_quality.SelectedIndex;
        _settings.CustomWidth = (int)_cw.Value;
        _settings.CustomHeight = (int)_ch.Value;
        _settings.CustomHevc = _chevc.Checked;
        _settings.MaxFps = int.Parse((string)_fps.SelectedItem!);
        _settings.VideoSink = (string)_sink.SelectedItem!;
        _settings.Fullscreen = _fullscreen.Checked;
        _settings.LowLatency = _lowLatency.Checked;
        _settings.Audio = _audio.Checked;
        _settings.AutoStart = _autoStart.Checked;
        _settings.Save();
    }

    // ------------------------------------------------------------------ behaviour

    private void Wire()
    {
        _quality.SelectedIndexChanged += (_, _) => _customPanel.Enabled = _quality.SelectedIndex == (int)QualityPreset.Custom;
        _startStop.Click += (_, _) => { if (_host.IsRunning) StopEngine(); else StartEngine(); };
        _firewall.Click += (_, _) => AddFirewallRule();

        _host.LineReceived += line => BeginInvokeSafe(() => { AppendLog(line); _parser.Feed(line); });
        _host.Exited += code => BeginInvokeSafe(() => OnEngineExited(code));

        _parser.ClientConnected += c => { _client.Text = $"Client : {c.Name} ({c.Model})"; SetState("Connexion…", Color.DarkOrange); };
        _parser.StreamStarted += OnStream;
        _parser.StatsReceived += OnStats;
        _parser.Error += e => _tray.ShowBalloonTip(3000, "AirServer", e, ToolTipIcon.Warning);

        _heartbeat.Tick += (_, _) =>
        {
            if (_mirroring && DateTime.UtcNow - _lastStats > TimeSpan.FromSeconds(3))
            {
                _mirroring = false;
                SetState(_host.IsRunning ? "En attente d'un iPhone" : "Arrêté", _host.IsRunning ? Color.SteelBlue : Color.Gray);
            }
        };
        _heartbeat.Start();

        Shown += (_, _) => { if (_settings.AutoStart) StartEngine(); };
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };
        _tray.DoubleClick += (_, _) => RestoreFromTray();
        FormClosing += (_, _) => { _heartbeat.Stop(); _host.Dispose(); _tray.Visible = false; };
    }

    private void StartEngine()
    {
        ReadUiIntoSettings();
        if (UxPlayHost.LocateEngine() is null)
        {
            MessageBox.Show(this, "Le moteur (engine\\uxplay.exe) est introuvable.\nLancez scripts\\build.ps1 pour le compiler.",
                "AirServer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        var (_, _, hz) = NativeMethods.PrimaryDisplayMode();
        _log.Clear();
        _stats.Items.Clear();
        _stream.Text = "Flux : —";
        _client.Text = "Client : —";
        _userStopped = false;
        try
        {
            _host.Start(_settings.BuildArguments(hz));
            SetState("En attente d'un iPhone", Color.SteelBlue);
            _startStop.Text = "Arrêter";
            SetSettingsEnabled(false);
        }
        catch (Exception ex)
        {
            AppendLog("ERREUR : " + ex.Message);
            SetState("Erreur de démarrage", Color.Firebrick);
        }
    }

    private void StopEngine()
    {
        _userStopped = true;
        _startStop.Enabled = false;
        UseWaitCursor = true;
        _host.Stop();
        UseWaitCursor = false;
        _startStop.Enabled = true;
    }

    private void OnEngineExited(int code)
    {
        _mirroring = false;
        _startStop.Text = "Démarrer";
        _startStop.Enabled = true;
        SetSettingsEnabled(true);
        AppendLog($"[moteur arrêté, code {code}]");
        if (_userStopped || _exiting) SetState("Arrêté", Color.Gray);
        else SetState($"Moteur arrêté de façon inattendue (code {code}) – voir le journal", Color.Firebrick);
    }

    private void OnStream(StreamInfo s)
    {
        bool full = s.W >= s.SourceW && s.H >= s.SourceH;
        _stream.Text = $"Flux : {s.Codec}  •  reçu {s.W}×{s.H}  •  source iPhone {s.SourceW}×{s.SourceH}" +
                       (full ? "  ✔ pleine résolution source" : $"  (réduit à {100.0 * s.W * s.H / Math.Max(1, s.SourceW * s.SourceH):0}% des pixels)");
        _lastStats = DateTime.UtcNow;
        _mirroring = true;
        SetState("Recopie en cours", Color.ForestGreen);
    }

    private void OnStats(IReadOnlyList<KeyValuePair<string, string>> stats)
    {
        _lastStats = DateTime.UtcNow;
        if (!_mirroring) { _mirroring = true; SetState("Recopie en cours", Color.ForestGreen); }
        _stats.BeginUpdate();
        foreach (var (k, v) in stats)
        {
            var item = _stats.Items[k];
            if (item is null) _stats.Items.Add(new ListViewItem([k, v]) { Name = k });
            else item.SubItems[1].Text = v;
        }
        _stats.EndUpdate();
    }

    private void AddFirewallRule()
    {
        var engine = UxPlayHost.LocateEngine();
        if (engine is null) { MessageBox.Show(this, "Moteur introuvable.", "AirServer"); return; }
        const string rule = "AirServer (UxPlay)";
        string args = $"/c netsh advfirewall firewall delete rule name=\"{rule}\" >nul 2>&1 & " +
                      $"netsh advfirewall firewall add rule name=\"{rule}\" dir=in action=allow program=\"{engine.Value.Exe}\" enable=yes profile=private,domain";
        try
        {
            using var p = Process.Start(new ProcessStartInfo("cmd.exe", args) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            p?.WaitForExit(15000);
            AppendLog(p?.ExitCode == 0 ? "[pare-feu] règle entrante ajoutée pour " + engine.Value.Exe : "[pare-feu] échec, code " + p?.ExitCode);
        }
        catch (System.ComponentModel.Win32Exception) { AppendLog("[pare-feu] annulé par l'utilisateur"); }
    }

    // ------------------------------------------------------------------ helpers

    private void SetState(string text, Color color) { _state.Text = "● " + text; _state.ForeColor = color; _tray.Text = "AirServer – " + text[..Math.Min(text.Length, 50)]; }

    private void SetSettingsEnabled(bool on)
    {
        foreach (Control c in new Control[] { _name, _quality, _fps, _sink, _fullscreen, _lowLatency, _audio })
            c.Enabled = on;
        _customPanel.Enabled = on && _quality.SelectedIndex == (int)QualityPreset.Custom;
    }

    private void AppendLog(string line)
    {
        if (_log.TextLength > 400_000) _log.Text = _log.Text[^200_000..];
        _log.AppendText(line + Environment.NewLine);
    }

    private void RestoreFromTray() { Show(); WindowState = FormWindowState.Normal; Activate(); }

    private void BeginInvokeSafe(Action a)
    {
        if (IsDisposed || Disposing) return;
        try { BeginInvoke(a); } catch (InvalidOperationException) { /* closing */ }
    }
}
