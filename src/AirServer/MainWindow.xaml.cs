using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AirServer;

public partial class MainWindow : Window
{
    private readonly ReceiverController _ctl;
    private readonly Settings _settings;
    private readonly StringBuilder _log = new();
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private ReceiverState? _shownState;
    private bool _syncing;          // true while code (not the user) changes a control
    private bool _ripplesOn;

    public MainWindow(Settings settings, bool demo)
    {
        InitializeComponent();
        _settings = settings;
        _ctl = new ReceiverController(settings, demo);
        _ctl.Changed += UpdateUi;
        _ctl.StatsChanged += () => { UpdateStreamChips(); if (DiagLayer.IsVisible) RefreshDiagnostics(); };
        _ctl.LogLine += AppendLog;
        _ctl.Toast += ShowToast;
        _toastTimer.Tick += (_, _) => HideToast();

        DemoBadge.Visibility = demo ? Visibility.Visible : Visibility.Collapsed;
        VersionText.Text = $"AirServer {GetType().Assembly.GetName().Version?.ToString(3)} · moteur UxPlay 1.74";
        DiagButton.Visibility = _settings.ShowDiagnostics ? Visibility.Visible : Visibility.Collapsed;

        SourceInitialized += (_, _) => NativeMethods.ApplyDarkRoundedFrame(new WindowInteropHelper(this).Handle);
        StateChanged += (_, _) =>
        {
            // WindowChrome draws past the screen edge when maximized: compensate
            Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            MaxGlyph.Text = WindowState == WindowState.Maximized ? "" : "";
        };
        Loaded += (_, _) =>
        {
            ((Storyboard)Resources["AuroraStory"]).Begin(this, true);
            ((Storyboard)Resources["RingStory"]).Begin(this, true);
            if (_settings.AutoStart || demo) _ctl.Start();
            UpdateUi();
        };
        Closing += (_, _) => _ctl.Dispose();
        PreviewKeyDown += OnKey;
        UpdateUi();
    }

    // ================================================================== state → visuals

    private void UpdateUi()
    {
        var s = _ctl.State;
        string pcName = string.IsNullOrWhiteSpace(_settings.ServerName) ? "AirServer" : _settings.ServerName.Trim();
        StepName.Text = pcName;
        StepName.ToolTip = pcName;
        RefreshNetworks();

        _syncing = true;
        PowerSwitch.IsChecked = s is not (ReceiverState.Stopped or ReceiverState.Error);
        PowerLabel.Text = PowerSwitch.IsChecked == true ? "Réception activée" : "Réception désactivée";
        _syncing = false;

        if (s == _shownState && s != ReceiverState.Mirroring) return;
        bool stateChanged = s != _shownState;
        _shownState = s;

        string client = _ctl.Client?.Name is { Length: > 0 } n ? n : "votre iPhone";
        (string glyph, Color a, Color b, string title, string subtitle) = s switch
        {
            ReceiverState.Starting => ("", C("#6366F1"), C("#8B5CF6"), "Démarrage…", "Préparation de la réception."),
            ReceiverState.Waiting => ("", C("#8B5CF6"), C("#22D3EE"), "Prêt à recevoir",
                "Votre iPhone doit être sur le même Wi-Fi que ce PC, ou connecté à son point d'accès mobile."),
            ReceiverState.Connecting => ("", C("#22D3EE"), C("#F472B6"), "Connexion…", $"{client} se connecte à ce PC."),
            ReceiverState.Mirroring => ("", C("#34D399"), C("#22D3EE"), client,
                "La recopie s'affiche dans la fenêtre vidéo. Alt + Entrée pour le plein écran."),
            ReceiverState.Error => ("", C("#F87171"), C("#FBBF24"), "Un problème est survenu",
                (_ctl.ErrorMessage ?? "La réception n'a pas pu démarrer.") + (_settings.ShowDiagnostics ? "" : " Réessayez dans un instant.")),
            _ => ("", C("#4B5375"), C("#2A3049"), "Réception en pause",
                "Activez la réception pour que votre iPhone puisse trouver ce PC."),
        };

        if (stateChanged)
        {
            AnimateColor(StopA, a); AnimateColor(StopB, b);
            AnimateColor(RingA, a); AnimateColor(RingB, b);
            AnimateColor(GlowStop, Color.FromArgb(0x55, a.R, a.G, a.B));
            OrbGlyph.Text = glyph;
            Pop(GlyphScale);
            CrossFade();
        }

        TitleText.Text = title;
        SubtitleText.Text = subtitle;
        StepsPanel.Visibility = Vis(s == ReceiverState.Waiting);
        StreamPanel.Visibility = Vis(s == ReceiverState.Mirroring);
        ActionPanel.Visibility = Vis(s is ReceiverState.Stopped or ReceiverState.Error);
        ActionText.Text = s == ReceiverState.Error ? "Réessayer" : "Activer la réception";
        ActionGlyph.Text = s == ReceiverState.Error ? "" : "";

        bool live = s == ReceiverState.Mirroring;
        if (live && LiveBadge.Visibility != Visibility.Visible) ((Storyboard)Resources["LivePulse"]).Begin(this, true);
        LiveBadge.Visibility = Vis(live);

        SetRipples(s is ReceiverState.Waiting or ReceiverState.Connecting);
        UpdateStreamChips();
        if (DiagLayer.IsVisible) RefreshDiagnostics();
    }

    private void UpdateStreamChips()
    {
        Chips.Children.Clear();
        var st = _ctl.Stream;
        if (st is null) { QualityLine.Text = ""; return; }

        bool hevc = st.Codec == "H265";
        AddChip("", hevc ? (Math.Max(st.W, st.H) > 2000 ? "HEVC · Ultra HD" : "HEVC") : "H.264", C("#8B5CF6"));
        AddChip("", $"{st.W} × {st.H}", C("#22D3EE"));
        if (FindStat("fps") is double fps) AddChip("", $"{fps:0} images/s", C("#F472B6"));
        if (FindStat("bitrate") is double bps && bps > 0) AddChip("", $"{bps / 1_000_000:0.0} Mbit/s", C("#FBBF24"));

        bool full = st.W * st.H >= st.SourceW * st.SourceH;
        QualityLine.Text = full ? "✓  Pleine résolution de l'iPhone"
                                : $"Image réduite à {100.0 * st.W * st.H / Math.Max(1, st.SourceW * st.SourceH):0} % des pixels de l'iPhone";
        QualityLine.Foreground = full ? (Brush)FindResource("B.Green") : (Brush)FindResource("B.Amber");
    }

    private double? FindStat(string key)
    {
        foreach (var (k, v) in _ctl.Stats)
            if (k.Contains(key, StringComparison.OrdinalIgnoreCase) &&
                double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                return d;
        return null;
    }

    private void AddChip(string glyph, string text, Color color)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock { Text = glyph, Style = (Style)FindResource("T.Icon"), FontSize = 14, Foreground = new SolidColorBrush(color) });
        sp.Children.Add(new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        Chips.Children.Add(new Border { Style = (Style)Resources["Chip"], Child = sp });
    }

    private void RefreshNetworks()
    {
        var nets = ReceiverController.FriendlyNetworks();
        NetworkChips.ItemsSource = nets;
        NoNetwork.Visibility = Vis(nets.Count == 0);
    }

    // ================================================================== animations

    private static void AnimateColor(GradientStop stop, Color to) =>
        stop.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(to, TimeSpan.FromMilliseconds(600)) { EasingFunction = new CubicEase() });

    private static void Pop(ScaleTransform t)
    {
        var anim = new DoubleAnimation(0.6, 1, TimeSpan.FromMilliseconds(450)) { EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut } };
        t.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        t.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    private void CrossFade()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        HeroText.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });
        HeroShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
    }

    private void SetRipples(bool on)
    {
        if (on == _ripplesOn) return;
        _ripplesOn = on;
        var sb = (Storyboard)Resources["RippleStory"];
        if (on) { Ripples.Visibility = Visibility.Visible; sb.Begin(this, true); }
        else { sb.Stop(this); Ripples.Visibility = Visibility.Collapsed; }
    }

    private void ShowToast(string text)
    {
        ToastText.Text = text;
        Toast.Visibility = Visibility.Visible;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(250)));
        ToastShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void HideToast()
    {
        _toastTimer.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300));
        fade.Completed += (_, _) => { if (Toast.Opacity == 0) Toast.Visibility = Visibility.Collapsed; };
        Toast.BeginAnimation(OpacityProperty, fade);
    }

    private static void Slide(TranslateTransform t, DependencyProperty p, double from, double to, Action? done = null)
    {
        var a = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        if (done is not null) a.Completed += (_, _) => done();
        t.BeginAnimation(p, a);
    }

    // ================================================================== settings drawer

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        LoadSettingsIntoUi();
        SettingsLayer.Visibility = Visibility.Visible;
        SettingsBackdrop.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
        Slide(SettingsShift, TranslateTransform.XProperty, SettingsPanel.Width, 0);
    }

    private void OnCloseSettings(object sender, RoutedEventArgs e) => CloseSettings();

    private void CloseSettings()
    {
        if (SettingsLayer.Visibility != Visibility.Visible) return;
        bool changed = ReadSettingsFromUi();
        SettingsBackdrop.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(250)));
        Slide(SettingsShift, TranslateTransform.XProperty, 0, SettingsPanel.Width, () => SettingsLayer.Visibility = Visibility.Collapsed);
        if (changed) { _ctl.ApplySettings(); UpdateUi(); }
        else _settings.Save();
    }

    private void LoadSettingsIntoUi()
    {
        _syncing = true;
        NameBox.Text = _settings.ServerName;
        QMax.IsChecked = _settings.Quality == QualityPreset.Max4K;
        QBalanced.IsChecked = _settings.Quality == QualityPreset.Hevc1440;
        QCompat.IsChecked = _settings.Quality == QualityPreset.Compat1080;
        QCustom.IsChecked = _settings.Quality == QualityPreset.Custom;
        QCustom.Visibility = Vis(_settings.Quality == QualityPreset.Custom);
        Fps60.IsChecked = _settings.MaxFps >= 60;
        Fps30.IsChecked = _settings.MaxFps < 60;
        AudioSwitch.IsChecked = _settings.Audio;
        FullscreenSwitch.IsChecked = _settings.Fullscreen;
        LatencySwitch.IsChecked = _settings.LowLatency;
        AutoStartSwitch.IsChecked = _settings.AutoStart;
        DiagSwitch.IsChecked = _settings.ShowDiagnostics;
        CustomSwitch.IsChecked = _settings.Quality == QualityPreset.Custom;
        CustomW.Text = _settings.CustomWidth.ToString();
        CustomH.Text = _settings.CustomHeight.ToString();
        CustomHevc.IsChecked = _settings.CustomHevc;
        CustomPanel.IsEnabled = _settings.Quality == QualityPreset.Custom;
        SinkD3D11.IsChecked = _settings.VideoSink == "d3d11videosink";
        SinkD3D12.IsChecked = !SinkD3D11.IsChecked;
        _syncing = false;
    }

    /// <summary>Copies the drawer into Settings. Returns true if something the engine uses changed.</summary>
    private bool ReadSettingsFromUi()
    {
        string before = string.Join("|", _settings.BuildArguments(60));
        _settings.ServerName = string.IsNullOrWhiteSpace(NameBox.Text) ? Environment.MachineName : NameBox.Text.Trim();
        _settings.Quality = CustomSwitch.IsChecked == true ? QualityPreset.Custom
                          : QBalanced.IsChecked == true ? QualityPreset.Hevc1440
                          : QCompat.IsChecked == true ? QualityPreset.Compat1080
                          : QualityPreset.Max4K;
        _settings.MaxFps = Fps30.IsChecked == true ? 30 : 60;
        _settings.Audio = AudioSwitch.IsChecked == true;
        _settings.Fullscreen = FullscreenSwitch.IsChecked == true;
        _settings.LowLatency = LatencySwitch.IsChecked == true;
        _settings.AutoStart = AutoStartSwitch.IsChecked == true;
        _settings.ShowDiagnostics = DiagSwitch.IsChecked == true;
        if (int.TryParse(CustomW.Text, out int w)) _settings.CustomWidth = Math.Clamp(w, 320, 9999);
        if (int.TryParse(CustomH.Text, out int h)) _settings.CustomHeight = Math.Clamp(h, 240, 9999);
        _settings.CustomHevc = CustomHevc.IsChecked == true;
        _settings.VideoSink = SinkD3D11.IsChecked == true ? "d3d11videosink" : "d3d12videosink";
        return before != string.Join("|", _settings.BuildArguments(60));
    }

    private void OnAdvancedToggled(object sender, RoutedEventArgs e)
    {
        bool open = AdvancedToggle.IsChecked == true;
        AdvancedPanel.Visibility = Vis(open);
        if (open) AdvancedPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
    }

    private void OnDiagSwitch(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _settings.ShowDiagnostics = DiagSwitch.IsChecked == true;
        DiagButton.Visibility = Vis(_settings.ShowDiagnostics);
    }

    private void OnCustomSwitch(object sender, RoutedEventArgs e)
    {
        bool on = CustomSwitch.IsChecked == true;
        CustomPanel.IsEnabled = on;
        if (_syncing) return;
        QCustom.Visibility = Vis(on);
        if (on) QCustom.IsChecked = true;
        else if (QCustom.IsChecked == true) QMax.IsChecked = true;
    }

    private void OnRepairFirewall(object sender, RoutedEventArgs e)
    {
        var engine = UxPlayHost.LocateEngine();
        if (engine is null) { ShowToast("Composant de réception introuvable."); return; }
        string enc = Firewall.EncodedCommand(Firewall.RulesScript(engine.Value.Exe));
        try
        {
            using var p = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -EncodedCommand {enc}")
                { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            p?.WaitForExit(30000);
            bool ok = p?.ExitCode == 0;
            AppendLog(ok ? $"[pare-feu] règles recréées pour {engine.Value.Exe}" : "[pare-feu] échec, code " + p?.ExitCode);
            ShowToast(ok ? "Accès réseau réparé." : "La réparation a échoué.");
        }
        catch (System.ComponentModel.Win32Exception) { ShowToast("Réparation annulée."); }
    }

    // ================================================================== technical page

    private void OnOpenDiagnostics(object sender, RoutedEventArgs e) => OpenDiagnostics();

    private void OpenDiagnostics()
    {
        RefreshDiagnostics();
        LogBox.Text = _log.ToString();
        LogBox.ScrollToVerticalOffset(double.MaxValue);
        DiagLayer.Visibility = Visibility.Visible;
        DiagLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
        Slide(DiagShift, TranslateTransform.YProperty, 30, 0);
    }

    private void OnCloseDiagnostics(object sender, RoutedEventArgs e) => CloseDiagnostics();

    private void CloseDiagnostics()
    {
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) => DiagLayer.Visibility = Visibility.Collapsed;
        DiagLayer.BeginAnimation(OpacityProperty, fade);
    }

    private void RefreshDiagnostics()
    {
        var c = _ctl.Client;
        var st = _ctl.Stream;
        DiagSession.Text =
            $"état         {_ctl.State}\n" +
            $"appareil     {c?.Name ?? "—"}\n" +
            $"modèle       {c?.Model ?? "—"}\n" +
            $"deviceID     {c?.DeviceId ?? "—"}\n" +
            (st is null ? "flux         —" :
            $"codec        {st.Codec}\n" +
            $"source       {st.SourceW}×{st.SourceH}\n" +
            $"reçu         {st.W}×{st.H} (offset {st.OffsetX},{st.OffsetY})");
        DiagStats.Text = _ctl.Stats.Count == 0 ? "aucune (pas de recopie en cours)"
            : string.Join("\n", _ctl.Stats.OrderBy(k => k.Key).Select(k => $"{k.Key,-22} {k.Value}"));
        var (dw, dh, hz) = NativeMethods.PrimaryDisplayMode();
        DiagEngine.Text =
            $"interfaces   {NetworkWatcher.CurrentSignature()}\n" +
            $"écran PC     {dw}×{dh} @ {hz} Hz\n" +
            $"moteur       {UxPlayHost.LocateEngine()?.Exe ?? "introuvable"}\n\n" +
            $"{_ctl.CommandLine ?? "(non démarré)"}";
    }

    private void AppendLog(string line)
    {
        if (_log.Length > 400_000) _log.Remove(0, 200_000);
        _log.AppendLine(line);
        if (DiagLayer.IsVisible)
        {
            LogBox.AppendText(line + Environment.NewLine);
            LogBox.ScrollToVerticalOffset(double.MaxValue);
        }
    }

    private void OnCopyLog(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(_log.ToString()); ShowToast("Journal copié dans le presse-papiers."); }
        catch (Exception) { ShowToast("Impossible d'accéder au presse-papiers."); }
    }

    // ================================================================== window / input

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.D && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            _settings.ShowDiagnostics = true;
            _settings.Save();
            DiagButton.Visibility = Visibility.Visible;
            if (DiagLayer.IsVisible) CloseDiagnostics(); else OpenDiagnostics();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (DiagLayer.IsVisible) CloseDiagnostics();
            else CloseSettings();
            e.Handled = true;
        }
    }

    private void OnPowerChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        if (PowerSwitch.IsChecked == true) _ctl.Start(); else _ctl.Stop();
    }

    /// <summary>Demo/screenshot hook: "settings", "advanced" or "diag".</summary>
    public void OpenPanel(string which)
    {
        if (which is "settings" or "advanced")
        {
            OnOpenSettings(this, new RoutedEventArgs());
            if (which == "advanced")
            {
                AdvancedToggle.IsChecked = true;
                Dispatcher.BeginInvoke(() => VersionText.BringIntoView(), DispatcherPriority.Loaded);
            }
        }
        else if (which == "diag") OpenDiagnostics();
    }

    private void OnAction(object sender, RoutedEventArgs e) => _ctl.Start();
    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private static Visibility Vis(bool v) => v ? Visibility.Visible : Visibility.Collapsed;
    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
