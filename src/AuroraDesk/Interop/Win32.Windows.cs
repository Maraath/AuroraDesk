using System.Runtime.InteropServices;

namespace AuroraDesk.Interop;

// Janelas de outros aplicativos (para o dock) e AppBar.
internal static unsafe partial class Win32
{
    public const int GW_OWNER = 4;
    public const int SW_RESTORE = 9;
    public const int SW_MINIMIZE = 6;
    public const int WM_CLOSE = 0x0010;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hwnd, int cmd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    public static partial int GetWindowText(nint hwnd, char* buffer, int max);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    public static partial int GetWindowTextLength(nint hwnd);

    public static string WindowTitle(nint hwnd)
    {
        int len = GetWindowTextLength(hwnd);
        if (len <= 0) return "";
        len = Math.Min(len, 512);
        char* buffer = stackalloc char[len + 1];
        int n = GetWindowText(hwnd, buffer, len + 1);
        return new string(buffer, 0, n);
    }

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumWindows(delegate* unmanaged<nint, nint, int> callback, nint data);

    private static List<nint>? _windowList;

    [UnmanagedCallersOnly]
    private static int CollectWindow(nint hwnd, nint data)
    {
        _windowList?.Add(hwnd);
        return 1;
    }

    /// <summary>Janelas de nível superior, de cima para baixo na ordem Z (thread de interface).</summary>
    public static List<nint> TopLevelWindows()
    {
        _windowList = new List<nint>(256);
        EnumWindows(&CollectWindow, 0);
        var result = _windowList;
        _windowList = null;
        return result;
    }

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmGetWindowAttribute(nint hwnd, int attribute, void* value, int size);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(nint hwnd, int attribute, void* value, int size);

    public const int DWMWA_CLOAKED = 14;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public static bool IsCloaked(nint hwnd)
    {
        int cloaked = 0;
        return DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, &cloaked, sizeof(int)) == 0 && cloaked != 0;
    }

    // Acrílico/desfoque por "accent policy": funciona em janelas transparentes desenhadas por
    // software, sem precisar de GPU no processo (ao contrário de DWMWA_SYSTEMBACKDROP_TYPE).
    [StructLayout(LayoutKind.Sequential)]
    private struct ACCENT_POLICY { public int AccentState, AccentFlags; public uint GradientColor; public int AnimationId; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWCOMPOSITIONATTRIBDATA { public int Attribute; public void* Data; public int SizeOfData; }

    [LibraryImport("user32.dll")]
    private static partial int SetWindowCompositionAttribute(nint hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);

    /// <summary>0 = desligado, 3 = desfoque, 4 = acrílico. Cor da camada em ARGB.</summary>
    public static bool SetAccent(nint hwnd, int state, uint argbTint)
    {
        const int WCA_ACCENT_POLICY = 19;
        // A API espera AABBGGRR.
        uint abgr = (argbTint & 0xFF00FF00) | ((argbTint & 0xFF) << 16) | ((argbTint >> 16) & 0xFF);
        var accent = new ACCENT_POLICY { AccentState = state, AccentFlags = 0, GradientColor = abgr };
        var data = new WINDOWCOMPOSITIONATTRIBDATA { Attribute = WCA_ACCENT_POLICY, Data = &accent, SizeOfData = sizeof(ACCENT_POLICY) };
        try { return SetWindowCompositionAttribute(hwnd, ref data) != 0; }
        catch (EntryPointNotFoundException) { return false; }
    }

    // ---------- processos ----------
    [LibraryImport("kernel32.dll")]
    public static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryFullProcessImageName(nint process, uint flags, char* buffer, ref int size);

    public static string? ProcessPath(uint pid)
    {
        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        nint h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == 0) return null;
        try
        {
            char* buffer = stackalloc char[1024];
            int size = 1024;
            return QueryFullProcessImageName(h, 0, buffer, ref size) ? new string(buffer, 0, size) : null;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    // ---------- teclado (atalhos do Windows: Win+A, Win+N) ----------
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT
    {
        [FieldOffset(0)] public int type;
        [FieldOffset(8)] public ushort wVk;
        [FieldOffset(10)] public ushort wScan;
        [FieldOffset(12)] public uint dwFlags;
        [FieldOffset(16)] public uint time;
        [FieldOffset(24)] public nint dwExtraInfo;
    }

    [LibraryImport("user32.dll")]
    private static partial uint SendInput(uint count, INPUT* inputs, int size);

    /// <summary>Pressiona Win + <paramref name="key"/> (ex.: 'A' configurações rápidas, 'N' notificações).</summary>
    public static void SendWinShortcut(char key)
    {
        const int INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_KEYUP = 2;
        const ushort VK_LWIN = 0x5B;
        var inputs = stackalloc INPUT[4];
        inputs[0] = new INPUT { type = INPUT_KEYBOARD, wVk = VK_LWIN };
        inputs[1] = new INPUT { type = INPUT_KEYBOARD, wVk = key };
        inputs[2] = new INPUT { type = INPUT_KEYBOARD, wVk = key, dwFlags = KEYEVENTF_KEYUP };
        inputs[3] = new INPUT { type = INPUT_KEYBOARD, wVk = VK_LWIN, dwFlags = KEYEVENTF_KEYUP };
        SendInput(4, inputs, sizeof(INPUT));
    }

    /// <summary>Digita um texto na janela em foco (caracteres Unicode, independente do layout do teclado).</summary>
    public static void SendText(string text)
    {
        const int INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_KEYUP = 2, KEYEVENTF_UNICODE = 4;
        if (text.Length == 0) return;
        var inputs = new INPUT[text.Length * 2];
        for (int i = 0; i < text.Length; i++)
        {
            inputs[2 * i] = new INPUT { type = INPUT_KEYBOARD, wScan = text[i], dwFlags = KEYEVENTF_UNICODE };
            inputs[2 * i + 1] = new INPUT { type = INPUT_KEYBOARD, wScan = text[i], dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP };
        }
        fixed (INPUT* p = inputs) SendInput((uint)inputs.Length, p, sizeof(INPUT));
    }

    /// <summary>Pressiona e solta uma tecla (código virtual).</summary>
    public static void SendKey(ushort vk)
    {
        const int INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_KEYUP = 2;
        var inputs = stackalloc INPUT[2];
        inputs[0] = new INPUT { type = INPUT_KEYBOARD, wVk = vk };
        inputs[1] = new INPUT { type = INPUT_KEYBOARD, wVk = vk, dwFlags = KEYEVENTF_KEYUP };
        SendInput(2, inputs, sizeof(INPUT));
    }

    [LibraryImport("shcore.dll")]
    public static partial int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    public static double MonitorScale(nint monitor) =>
        GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1.0;

    // ---------- AppBar (reserva de espaço na tela) ----------
    public const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3;
    public const uint ABE_LEFT = 0, ABE_TOP = 1, ABE_RIGHT = 2, ABE_BOTTOM = 3;
    public const int ABN_POSCHANGED = 1, ABN_FULLSCREENAPP = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA
    {
        public int cbSize;
        public nint hWnd;
        public int uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public nint lParam;
    }

    [LibraryImport("shell32.dll")]
    public static partial nuint SHAppBarMessage(uint message, ref APPBARDATA data);
}
