using System.Runtime.InteropServices;

namespace AuroraDesk.Interop;

// Monitores, estado da sessão, energia, tela cheia e eventos de janela.
internal static unsafe partial class Win32
{
    public const int GW_HWNDPREV = 3;

    [LibraryImport("user32.dll")]
    public static partial nint GetWindow(nint hwnd, int cmd);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    public static partial int GetClassName(nint hwnd, char* buffer, int max);

    public static string ClassName(nint hwnd)
    {
        char* buffer = stackalloc char[128];
        int n = GetClassName(hwnd, buffer, 128);
        return n > 0 ? new string(buffer, 0, n) : "";
    }

    // ---------- monitores ----------
    public const uint MONITOR_DEFAULTTONULL = 0;
    public const uint MONITOR_DEFAULTTOPRIMARY = 1;
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        public fixed char szDevice[32];
    }

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromRect(in RECT rect, uint flags);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromPoint(POINT pt, uint flags);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromWindow(nint hwnd, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfo(nint monitor, ref MONITORINFOEX info);

    public static MONITORINFOEX MonitorInfo(nint monitor)
    {
        var info = new MONITORINFOEX { cbSize = sizeof(MONITORINFOEX) };
        GetMonitorInfo(monitor, ref info);
        return info;
    }

    public static string DeviceName(in MONITORINFOEX info)
    {
        fixed (char* p = info.szDevice) return new string(p);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumDisplayMonitors(nint hdc, nint clip, delegate* unmanaged<nint, nint, RECT*, nint, int> callback, nint data);

    private static List<nint>? _monitorList;

    [UnmanagedCallersOnly]
    private static int CollectMonitor(nint monitor, nint hdc, RECT* rect, nint data)
    {
        _monitorList?.Add(monitor);
        return 1;
    }

    /// <summary>Todos os monitores (chamado só na thread de interface).</summary>
    public static List<nint> Monitors()
    {
        _monitorList = [];
        EnumDisplayMonitors(0, 0, &CollectMonitor, 0);
        var result = _monitorList;
        _monitorList = null;
        return result;
    }

    // ---------- sessão e energia ----------
    public const int NOTIFY_FOR_THIS_SESSION = 0;
    public const int WTS_SESSION_LOCK = 7;
    public const int WTS_SESSION_UNLOCK = 8;

    [LibraryImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WTSRegisterSessionNotification(nint hwnd, int flags);

    [LibraryImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WTSUnRegisterSessionNotification(nint hwnd);

    public const int PBT_APMPOWERSTATUSCHANGE = 0x000A;
    public const int PBT_POWERSETTINGCHANGE = 0x8013;
    public static readonly Guid GUID_CONSOLE_DISPLAY_STATE = new("6fe69556-704a-47a0-8f24-c28d936fda47");

    [StructLayout(LayoutKind.Sequential)]
    public struct POWERBROADCAST_SETTING
    {
        public Guid PowerSetting;
        public uint DataLength;
        public uint Data; // primeiro DWORD dos dados
    }

    [LibraryImport("user32.dll")]
    public static partial nint RegisterPowerSettingNotification(nint recipient, in Guid setting, int flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterPowerSettingNotification(nint handle);

    // QUERY_USER_NOTIFICATION_STATE: 2 = ocupado (tela cheia), 3 = D3D tela cheia, 4 = apresentação
    [LibraryImport("shell32.dll")]
    public static partial int SHQueryUserNotificationState(out int state);

    // ---------- eventos de janela ----------
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_OBJECT_DESTROY = 0x8001;
    public const uint EVENT_OBJECT_SHOW = 0x8002;
    public const uint EVENT_OBJECT_HIDE = 0x8003;
    public const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
    public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    [LibraryImport("user32.dll")]
    public static partial nint SetWinEventHook(uint eventMin, uint eventMax, nint module,
        delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> callback, uint processId, uint threadId, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWinEvent(nint hook);
}
