using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace AirServer;

/// <summary>
/// Raises <see cref="Changed"/> (debounced) when the set of usable IPv4 addresses changes,
/// e.g. when the Windows Mobile Hotspot is switched on. The UxPlay mDNS responder only binds
/// the interfaces present at start-up, so the receiver must be restarted to appear on a new one.
/// </summary>
public sealed class NetworkWatcher : IDisposable
{
    private readonly System.Threading.Timer _debounce;
    private readonly Func<string> _probe;
    private readonly int _debounceMs;
    private string _signature;

    public event Action? Changed;

    public NetworkWatcher(int debounceMs = 4000, Func<string>? signature = null)
    {
        _probe = signature ?? CurrentSignature;
        _signature = _probe();
        _debounceMs = debounceMs;
        _debounce = new System.Threading.Timer(_ =>
        {
            string now = _probe();
            if (now == _signature) return;   // e.g. DHCP renew with the same address: no restart
            _signature = now;
            Changed?.Invoke();
        });
        NetworkChange.NetworkAddressChanged += OnChange;
        NetworkChange.NetworkAvailabilityChanged += OnAvailability;
    }

    private void OnChange(object? s, EventArgs e) => Notify();
    private void OnAvailability(object? s, NetworkAvailabilityEventArgs e) => Notify();

    /// <summary>Schedules a (debounced) re-check of the address set.</summary>
    public void Notify() => _debounce.Change(_debounceMs, Timeout.Infinite);

    /// <summary>Accepts the current address set as the baseline (call when the engine starts).</summary>
    public void Rebase() => _signature = _probe();

    /// <summary>Sorted list of IPv4 addresses on interfaces that are up (no loopback, no APIPA).</summary>
    public static string CurrentSignature() => string.Join(",", UsableIPv4().Select(a => a.ToString()).Order());

    public static IEnumerable<IPAddress> UsableIPv4()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;
            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                var a = ua.Address;
                if (a.AddressFamily != AddressFamily.InterNetwork) continue;
                var b = a.GetAddressBytes();
                if (b[0] == 169 && b[1] == 254) continue;
                yield return a;
            }
        }
    }

    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged -= OnChange;
        NetworkChange.NetworkAvailabilityChanged -= OnAvailability;
        _debounce.Dispose();
    }
}
