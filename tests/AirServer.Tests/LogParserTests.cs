using AirServer;
using Xunit;

public class LogParserTests
{
    [Fact]
    public void Parses_connection_request()
    {
        var p = new LogParser();
        ClientInfo? got = null;
        p.ClientConnected += c => got = c;
        p.Feed("connection request from iPhone de Mehmoud (iPhone17,2) with deviceID = 6A:1B:2C:3D:4E:5F");
        Assert.Equal(new ClientInfo("iPhone de Mehmoud", "iPhone17,2", "6A:1B:2C:3D:4E:5F"), got);
    }

    [Fact]
    public void Parses_stream_geometry_line_as_printed_by_video_renderer()
    {
        var p = new LogParser();
        StreamInfo? got = null;
        p.StreamStarted += s => got = s;
        // exact format string from renderers/video_renderer.c
        p.Feed("*** codec is h265: geometry is (w,h)=(1320,2868) (source); (x,y)=(0,0); (w,h)=(994,2160)");
        Assert.Equal(new StreamInfo("H265", 1320, 2868, 0, 0, 994, 2160), got);
    }

    [Fact]
    public void Parses_multiline_fps_plist_and_flattens_nested_keys()
    {
        var p = new LogParser();
        IReadOnlyList<KeyValuePair<string, string>>? got = null;
        p.StatsReceived += s => got = s;
        string[] lines =
        [
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>",
            "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">",
            "<plist version=\"1.0\">",
            "<dict>",
            "  <key>fps</key><real>59.8</real>",
            "  <key>net</key><dict><key>bitrate</key><integer>24000000</integer></dict>",
            "  <key>ok</key><true/>",
            "</dict>",
            "</plist>",
        ];
        foreach (var l in lines) p.Feed(l);
        Assert.NotNull(got);
        Assert.Contains(new KeyValuePair<string, string>("fps", "59.8"), got!);
        Assert.Contains(new KeyValuePair<string, string>("net.bitrate", "24000000"), got!);
        Assert.Contains(new KeyValuePair<string, string>("ok", "true"), got!);
    }

    [Fact]
    public void Plist_lines_are_not_mistaken_for_other_events()
    {
        var p = new LogParser();
        int events = 0;
        p.ClientConnected += _ => events++;
        p.Error += _ => events++;
        p.Feed("<plist version=\"1.0\"><dict><key>x</key><string>*** ERROR inside</string></dict>");
        p.Feed("</plist>");
        Assert.Equal(0, events);
    }

    [Fact]
    public void Reports_errors()
    {
        var p = new LogParser();
        string? err = null;
        p.Error += e => err = e;
        p.Feed("*** ERROR: video is h265 but the -h265 option was not used");
        Assert.Equal("video is h265 but the -h265 option was not used", err);
    }

    [Fact]
    public void Builds_max_quality_arguments()
    {
        var s = new Settings { ServerName = "Salon PC", Quality = QualityPreset.Max4K, MaxFps = 60, Audio = true };
        var a = s.BuildArguments(280);   // high-refresh gaming monitor is capped to 60
        Assert.Equal(["-n", "Salon PC", "-nh", "-p", "-nohold", "-FPSdata", "-s", "3840x2160@60", "-fps", "60", "-vs", "d3d12videosink", "-h265"], a);
    }

    [Fact]
    public void Compat_preset_is_h264_1080p_without_h265_flag()
    {
        var a = new Settings { Quality = QualityPreset.Compat1080, Audio = false, LowLatency = true }.BuildArguments(60);
        Assert.DoesNotContain("-h265", a);
        Assert.Contains("1920x1080@60", a);
        Assert.Contains("-a", a);
        Assert.Equal("no", a[a.IndexOf("-vsync") + 1]);
    }
}
