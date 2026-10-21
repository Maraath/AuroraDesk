using AuroraDesk.Core.Backup;

namespace AuroraDesk.Interop;

/// <summary>
/// Barra de tarefas do Windows (principal e as de outros monitores). Só Win32, sem WPF: o
/// processo vigia usa esta classe para restaurar a barra mesmo se o app cair.
/// </summary>
internal static unsafe class TaskbarControl
{
    private const uint ABM_GETSTATE = 4, ABM_SETSTATE = 10;
    public const int ABS_AUTOHIDE = 1, ABS_ALWAYSONTOP = 2;
    private const int SW_HIDE = 0, SW_SHOWNA = 8;

    public static readonly string[] Classes = ["Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    public static List<nint> Windows()
    {
        var list = new List<nint>();
        nint main = Win32.FindWindow("Shell_TrayWnd", null);
        if (main != 0) list.Add(main);
        for (nint h = Win32.FindWindowEx(0, 0, "Shell_SecondaryTrayWnd", null); h != 0; h = Win32.FindWindowEx(0, h, "Shell_SecondaryTrayWnd", null))
            list.Add(h);
        return list;
    }

    public static bool IsTaskbar(nint hwnd) => Win32.ClassName(hwnd) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";

    /// <summary>Estado da barra (ocultar automaticamente etc.). -1 se o Explorer não estiver rodando.</summary>
    public static int GetState()
    {
        nint tray = Win32.FindWindow("Shell_TrayWnd", null);
        if (tray == 0) return -1;
        var data = new Win32.APPBARDATA { cbSize = sizeof(Win32.APPBARDATA), hWnd = tray };
        return (int)Win32.SHAppBarMessage(ABM_GETSTATE, ref data);
    }

    public static void SetState(int state)
    {
        nint tray = Win32.FindWindow("Shell_TrayWnd", null);
        if (tray == 0 || state < 0) return;
        var data = new Win32.APPBARDATA { cbSize = sizeof(Win32.APPBARDATA), hWnd = tray, lParam = state };
        Win32.SHAppBarMessage(ABM_SETSTATE, ref data);
    }

    public static void HideAll()
    {
        foreach (var h in Windows()) Win32.ShowWindow(h, SW_HIDE);
    }

    public static void ShowAll()
    {
        foreach (var h in Windows()) Win32.ShowWindow(h, SW_SHOWNA);
    }

    /// <summary>Devolve a barra ao estado original guardado e apaga o backup. Seguro chamar sempre.</summary>
    public static bool Restore(TaskbarBackup backup)
    {
        var saved = backup.Load();
        if (saved is null && !backup.Exists) return false;
        if (saved is not null) SetState(saved.OriginalAppBarState);
        ShowAll();
        backup.Clear();
        return true;
    }
}
