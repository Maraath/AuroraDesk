using AuroraDesk.Interop;

namespace AuroraDesk.Tray;

/// <summary>Ícone da bandeja via Shell_NotifyIcon (sem WinForms, que custaria vários MB).</summary>
internal sealed unsafe class TrayIcon : IDisposable
{
    private const int CallbackMessage = Win32.WM_APP + 1;
    private const int IconResourceId = 32512; // id do ícone do app gerado pelo SDK (ApplicationIcon)

    private readonly ControlWindow _window;
    private readonly string _tooltip;
    private nint _icon;
    private bool _added;

    public TrayIcon(ControlWindow window, string tooltip)
    {
        _window = window;
        _tooltip = tooltip;
        _window.Message += OnMessage;
    }

    /// <summary>Clique esquerdo.</summary>
    public event Action? Activated;

    /// <summary>Clique direito: quem assina monta e mostra o menu.</summary>
    public event Action? ContextMenuRequested;

    public void Show()
    {
        LoadIcon();
        var data = CreateData();
        _added = Shell32.Shell_NotifyIcon(Shell32.NIM_ADD, ref data);
        data.uVersion = Shell32.NOTIFYICON_VERSION_4;
        Shell32.Shell_NotifyIcon(Shell32.NIM_SETVERSION, ref data);
        if (!_added) Core.Logging.Log.Warn("Não foi possível adicionar o ícone da bandeja");
    }

    private void LoadIcon()
    {
        if (_icon != 0) Win32.DestroyIcon(_icon);
        uint dpi = Win32.GetDpiForWindow(_window.Handle);
        int size = Win32.GetSystemMetricsForDpi(Win32.SM_CXSMICON, dpi == 0 ? 96 : dpi);
        int hr = Win32.LoadIconWithScaleDown(Win32.GetModuleHandle(null), IconResourceId, size, size, out _icon);
        if (hr != 0) Core.Logging.Log.Warn($"Ícone da bandeja não carregado (0x{hr:X8})");
    }

    private Shell32.NOTIFYICONDATAW CreateData()
    {
        var data = new Shell32.NOTIFYICONDATAW
        {
            cbSize = sizeof(Shell32.NOTIFYICONDATAW),
            hWnd = _window.Handle,
            uID = 1,
            uFlags = Shell32.NIF_MESSAGE | Shell32.NIF_ICON | Shell32.NIF_TIP | Shell32.NIF_SHOWTIP,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
        };
        var tip = _tooltip.AsSpan(0, Math.Min(_tooltip.Length, 127));
        tip.CopyTo(new Span<char>(data.szTip, 128));
        return data;
    }

    private bool OnMessage(int msg, nint wParam, nint lParam)
    {
        if (msg == ControlWindow.TaskbarCreatedMessage)
        {
            // Explorer reiniciou: o ícone precisa ser recriado.
            Show();
            return false; // outros também querem saber
        }
        if (msg == Win32.WM_DPICHANGED && _added)
        {
            LoadIcon();
            var data = CreateData();
            Shell32.Shell_NotifyIcon(Shell32.NIM_MODIFY, ref data);
            return false;
        }
        if (msg != CallbackMessage) return false;

        int ev = (int)(lParam & 0xFFFF);
        switch (ev)
        {
            case Shell32.NIN_SELECT:
            case Shell32.NIN_KEYSELECT:
                Activated?.Invoke();
                break;
            case Win32.WM_CONTEXTMENU:
                ContextMenuRequested?.Invoke();
                break;
        }
        return true;
    }

    public void Dispose()
    {
        _window.Message -= OnMessage;
        if (_added)
        {
            var data = CreateData();
            Shell32.Shell_NotifyIcon(Shell32.NIM_DELETE, ref data);
            _added = false;
        }
        if (_icon != 0) { Win32.DestroyIcon(_icon); _icon = 0; }
    }
}
