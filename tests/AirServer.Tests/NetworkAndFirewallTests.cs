using AirServer;
using Xunit;

public class NetworkAndFirewallTests
{
    [Fact]
    public async Task Watcher_fires_once_when_address_set_changes_and_debounces_bursts()
    {
        string current = "192.168.1.56";
        using var w = new NetworkWatcher(debounceMs: 50, signature: () => current);
        int fired = 0;
        w.Changed += () => Interlocked.Increment(ref fired);

        current = "192.168.1.56,192.168.137.1";   // hotspot switched on
        for (int i = 0; i < 5; i++) w.Notify();   // Windows raises several events in a burst
        await Task.Delay(400);
        Assert.Equal(1, fired);
    }

    [Fact]
    public async Task Watcher_ignores_events_when_addresses_are_unchanged()
    {
        using var w = new NetworkWatcher(debounceMs: 50, signature: () => "192.168.1.56");
        int fired = 0;
        w.Changed += () => fired++;
        w.Notify();
        await Task.Delay(300);
        Assert.Equal(0, fired);
    }

    [Fact]
    public async Task Rebase_accepts_new_baseline()
    {
        string current = "a";
        using var w = new NetworkWatcher(debounceMs: 50, signature: () => current);
        int fired = 0;
        w.Changed += () => fired++;
        current = "b";
        w.Rebase();          // engine restarted with "b": no further restart needed
        w.Notify();
        await Task.Delay(300);
        Assert.Equal(0, fired);
    }

    [Fact]
    public void Firewall_rules_open_private_lan_and_only_the_hotspot_subnet_on_public()
    {
        string s = Firewall.RulesScript(@"C:\Users\o'neil\AppData\Local\Programs\AirServer\engine\uxplay.exe");
        Assert.Contains("-Profile Private,Domain", s);
        Assert.Contains("-Profile Public -RemoteAddress 192.168.137.0/24", s);
        Assert.DoesNotContain("-Profile Public |", s);           // never Public without subnet restriction
        Assert.Contains(@"'C:\Users\o''neil\AppData", s);       // quote escaped for PowerShell
        Assert.Equal(4, s.Split('\n').Count(l => l.StartsWith("New-NetFirewallRule")));
    }
}
