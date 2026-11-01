using System.Runtime.InteropServices;

namespace AirServer;

internal static partial class NativeMethods
{
    private const uint CTRL_C_EVENT = 0;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(uint dwProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FreeConsole();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleCtrlHandler(IntPtr handler, [MarshalAs(UnmanagedType.Bool)] bool add);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

    /// <summary>
    /// Sends Ctrl+C to a console child process so it can shut down cleanly
    /// (UxPlay then sends mDNS "goodbye" packets and the iPhone drops it from the list at once).
    /// </summary>
    public static bool SendCtrlC(int pid)
    {
        FreeConsole();
        if (!AttachConsole((uint)pid)) return false;
        SetConsoleCtrlHandler(IntPtr.Zero, true);   // ignore the event in our own process
        bool ok = GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0);
        Thread.Sleep(100);                          // let the event be delivered before detaching
        FreeConsole();
        SetConsoleCtrlHandler(IntPtr.Zero, false);
        return ok;
    }

    // ---- window chrome (Windows 11: dark frame + rounded corners; ignored on Windows 10) -------

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    public static void ApplyDarkRoundedFrame(IntPtr hwnd)
    {
        int on = 1, round = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
    }

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindWindow(string? cls, string title);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hwnd, int cmd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hwnd);

    /// <summary>Second launch: bring the already running window to the front.</summary>
    public static void ActivateExistingWindow(string title)
    {
        IntPtr h = FindWindow(null, title);
        if (h == IntPtr.Zero) return;
        ShowWindow(h, 9 /* SW_RESTORE */);
        SetForegroundWindow(h);
    }

    // ---- refresh rate of the primary display -------------------------------------------------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2;
        public int dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

    private const int ENUM_CURRENT_SETTINGS = -1;

    /// <summary>Returns (width, height, refresh Hz) of the primary display, or a 1080p60 fallback.</summary>
    public static (int W, int H, int Hz) PrimaryDisplayMode()
    {
        var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm) && dm.dmDisplayFrequency > 1)
            return (dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency);
        return (1920, 1080, 60);
    }
}
