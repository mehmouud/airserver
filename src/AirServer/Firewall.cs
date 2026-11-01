namespace AirServer;

/// <summary>Inbound firewall rules for the engine. Same rule set as install.ps1.</summary>
public static class Firewall
{
    /// <summary>Windows Mobile Hotspot (Internet Connection Sharing) always uses this subnet.</summary>
    public const string HotspotSubnet = "192.168.137.0/24";

    /// <summary>
    /// PowerShell script (run elevated) that (re)creates the rules:
    /// - Private/Domain networks: allow (home/office LAN);
    /// - Public profile: allow ONLY from the PC's own Mobile Hotspot subnet. The hotspot interface has no
    ///   network profile, so Windows treats it as Public; restricting the remote subnet avoids opening the
    ///   receiver on café / hotel Wi-Fi.
    /// </summary>
    public static string RulesScript(string engineExe)
    {
        string exe = engineExe.Replace("'", "''");
        return string.Join("\n",
            "Get-NetFirewallRule -DisplayName 'AirServer*' -ErrorAction SilentlyContinue | Remove-NetFirewallRule",
            $"New-NetFirewallRule -DisplayName 'AirServer (TCP)' -Direction Inbound -Action Allow -Program '{exe}' -Protocol TCP -Profile Private,Domain | Out-Null",
            $"New-NetFirewallRule -DisplayName 'AirServer (UDP)' -Direction Inbound -Action Allow -Program '{exe}' -Protocol UDP -Profile Private,Domain | Out-Null",
            $"New-NetFirewallRule -DisplayName 'AirServer hotspot (TCP)' -Direction Inbound -Action Allow -Program '{exe}' -Protocol TCP -Profile Public -RemoteAddress {HotspotSubnet} | Out-Null",
            $"New-NetFirewallRule -DisplayName 'AirServer hotspot (UDP)' -Direction Inbound -Action Allow -Program '{exe}' -Protocol UDP -Profile Public -RemoteAddress {HotspotSubnet} | Out-Null");
    }

    public static string EncodedCommand(string script) =>
        Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
}
