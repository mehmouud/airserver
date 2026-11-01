using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AirServer;

public sealed record ClientInfo(string Name, string Model, string DeviceId);

/// <summary>Geometry announced by the iPhone in the SPS/PPS packet of the mirror stream.</summary>
public sealed record StreamInfo(string Codec, int SourceW, int SourceH, int OffsetX, int OffsetY, int W, int H);

/// <summary>
/// Turns uxplay's stdout into structured events. Pure (no I/O), so it can be tested
/// by feeding it recorded log lines.
/// </summary>
public sealed partial class LogParser
{
    public event Action<ClientInfo>? ClientConnected;
    public event Action<StreamInfo>? StreamStarted;
    public event Action<IReadOnlyList<KeyValuePair<string, string>>>? StatsReceived;
    public event Action<string>? Error;

    [GeneratedRegex(@"connection request from (?<name>.*) \((?<model>[^()]*)\) with deviceID = (?<id>\S+)")]
    private static partial Regex ConnRx();

    [GeneratedRegex(@"codec is (?<codec>h26[45]): geometry is \(w,h\)=\((?<sw>\d+),(?<sh>\d+)\) \(source\); \(x,y\)=\((?<x>\d+),(?<y>\d+)\); \(w,h\)=\((?<w>\d+),(?<h>\d+)\)")]
    private static partial Regex GeomRx();

    private StringBuilder? _plist;

    public void Feed(string line)
    {
        // Multi-line XML plist printed by -FPSdata (one per second while mirroring).
        if (_plist is null && (line.Contains("<?xml", StringComparison.Ordinal) || line.Contains("<plist", StringComparison.Ordinal)))
            _plist = new StringBuilder();
        if (_plist is not null)
        {
            _plist.AppendLine(line);
            if (line.Contains("</plist>", StringComparison.Ordinal))
            {
                var xml = _plist.ToString();
                _plist = null;
                var stats = ParsePlist(xml);
                if (stats.Count > 0) StatsReceived?.Invoke(stats);
            }
            else if (_plist.Length > 256 * 1024) _plist = null;   // runaway guard
            return;
        }

        var m = ConnRx().Match(line);
        if (m.Success)
        {
            ClientConnected?.Invoke(new ClientInfo(m.Groups["name"].Value, m.Groups["model"].Value, m.Groups["id"].Value));
            return;
        }
        m = GeomRx().Match(line);
        if (m.Success)
        {
            int I(string g) => int.Parse(m.Groups[g].Value);
            StreamStarted?.Invoke(new StreamInfo(m.Groups["codec"].Value.ToUpperInvariant(),
                I("sw"), I("sh"), I("x"), I("y"), I("w"), I("h")));
            return;
        }
        if (line.Contains("*** ERROR", StringComparison.Ordinal))
            Error?.Invoke(line.Replace("*** ERROR:", "").Replace("***ERROR", "").Trim());
    }

    /// <summary>Flattens a plist dict into (dotted.key, value) pairs.</summary>
    public static List<KeyValuePair<string, string>> ParsePlist(string xml)
    {
        var result = new List<KeyValuePair<string, string>>();
        try
        {
            int start = xml.IndexOf('<');
            var doc = XDocument.Parse(xml[start..], LoadOptions.None);
            var root = doc.Root?.Elements().FirstOrDefault();
            if (root is not null) Flatten(root, "", result);
        }
        catch (System.Xml.XmlException) { /* partial/garbled block: ignore */ }
        return result;
    }

    private static void Flatten(XElement node, string prefix, List<KeyValuePair<string, string>> acc)
    {
        switch (node.Name.LocalName)
        {
            case "dict":
                string? key = null;
                foreach (var child in node.Elements())
                {
                    if (child.Name.LocalName == "key") { key = child.Value; continue; }
                    Flatten(child, prefix.Length == 0 ? key ?? "?" : $"{prefix}.{key}", acc);
                    key = null;
                }
                break;
            case "array":
                int i = 0;
                foreach (var child in node.Elements()) Flatten(child, $"{prefix}[{i++}]", acc);
                break;
            case "true": acc.Add(new(prefix, "true")); break;
            case "false": acc.Add(new(prefix, "false")); break;
            case "data": acc.Add(new(prefix, $"<{node.Value.Trim().Length * 3 / 4} octets>")); break;
            default: acc.Add(new(prefix, node.Value)); break;
        }
    }
}
