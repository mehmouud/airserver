using System.Text.Json;

namespace AirServer;

public enum QualityPreset
{
    /// <summary>HEVC, 3840x2160 requested: the iPhone sends the highest resolution it accepts.</summary>
    Max4K,
    /// <summary>HEVC, 2560x1440 requested.</summary>
    Hevc1440,
    /// <summary>H.264 only, 1920x1080: for older iPhones or weak Wi-Fi.</summary>
    Compat1080,
    Custom,
}

public sealed class Settings
{
    public string ServerName { get; set; } = Environment.MachineName;
    public QualityPreset Quality { get; set; } = QualityPreset.Max4K;
    public int CustomWidth { get; set; } = 3840;
    public int CustomHeight { get; set; } = 2160;
    public bool CustomHevc { get; set; } = true;
    public int MaxFps { get; set; } = 60;
    public string VideoSink { get; set; } = "d3d12videosink";
    public bool Fullscreen { get; set; }
    public bool LowLatency { get; set; }
    public bool Audio { get; set; } = true;
    public bool AutoStart { get; set; } = true;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AirServer", "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), JsonOpts) ?? new Settings();
        }
        catch (Exception) { /* corrupt file: fall back to defaults */ }
        return new Settings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
    }

    /// <summary>(width, height, hevc) requested from the iPhone.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public (int W, int H, bool Hevc) Resolution => Quality switch
    {
        QualityPreset.Max4K => (3840, 2160, true),
        QualityPreset.Hevc1440 => (2560, 1440, true),
        QualityPreset.Compat1080 => (1920, 1080, false),
        _ => (Math.Clamp(CustomWidth, 320, 9999), Math.Clamp(CustomHeight, 240, 9999), CustomHevc),
    };

    /// <summary>Builds the uxplay command line. Each element is one argv entry (no manual quoting).</summary>
    public List<string> BuildArguments(int refreshRate)
    {
        var (w, h, hevc) = Resolution;
        var args = new List<string>
        {
            "-n", string.IsNullOrWhiteSpace(ServerName) ? "AirServer" : ServerName.Trim(),
            "-nh",
            "-p",                          // fixed legacy ports: TCP 7000/7001/7100, UDP 6000/6001/7011
            "-nohold",                     // a new iPhone can take over the screen
            "-FPSdata",                    // client sends 1 Hz streaming performance reports
            // AirPlay mirroring tops out at 60 fps; advertising a 144/280 Hz panel gains nothing.
            "-s", $"{w}x{h}@{Math.Clamp(refreshRate, 24, 60)}",
            "-fps", Math.Clamp(MaxFps, 1, 255).ToString(),
            "-vs", VideoSink,
        };
        if (hevc) args.Add("-h265");
        if (Fullscreen) args.Add("-fs");
        if (LowLatency) { args.Add("-vsync"); args.Add("no"); }
        if (!Audio) args.Add("-a");
        return args;
    }
}
