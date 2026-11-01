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
