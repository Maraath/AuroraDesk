using AuroraDesk.Interop;

namespace AuroraDesk.Tray;

/// <summary>
/// Menu de contexto nativo do Win32 (leve, com o visual do Windows 11 e modo escuro).
/// É criado ao abrir e destruído ao fechar: nada fica em memória entre um uso e outro.
/// </summary>
internal sealed class NativeMenu
{
    private readonly List<(string? Text, Action? Action, bool Checked, bool Enabled, NativeMenu? Submenu)> _items = [];

    public NativeMenu Item(string text, Action action, bool isChecked = false, bool enabled = true)
    {
        _items.Add((text, action, isChecked, enabled, null));
        return this;
    }

    public NativeMenu Submenu(string text, NativeMenu submenu)
    {
        _items.Add((text, null, false, true, submenu));
        return this;
    }

    public NativeMenu Separator()
    {
        _items.Add((null, null, false, true, null));
        return this;
    }

    public void Show(nint owner, bool dark)
    {
        try
        {
            Win32.SetPreferredAppMode(dark ? 2 : 3); // 2 = ForceDark, 3 = ForceLight
            Win32.FlushMenuThemes();
        }
        catch (EntryPointNotFoundException) { }

        var actions = new List<Action>();
        nint menu = Build(actions);
        try
        {
            Win32.GetCursorPos(out var pt);
            Win32.SetForegroundWindow(owner);
            int id = Win32.TrackPopupMenuEx(menu, Win32.TPM_RETURNCMD | Win32.TPM_RIGHTBUTTON | Win32.TPM_BOTTOMALIGN | Win32.TPM_RIGHTALIGN, pt.X, pt.Y, owner, 0);
            Win32.PostMessage(owner, Win32.WM_NULL, 0, 0);
            if (id > 0 && id <= actions.Count) actions[id - 1]();
        }
        finally
        {
            Win32.DestroyMenu(menu); // destrói os submenus junto
        }
    }

    private nint Build(List<Action> actions)
    {
        nint menu = Win32.CreatePopupMenu();
        foreach (var (text, action, isChecked, enabled, submenu) in _items)
        {
            if (text is null)
                Win32.AppendMenu(menu, Win32.MF_SEPARATOR, 0, null);
            else if (submenu is not null)
                Win32.AppendMenu(menu, Win32.MF_POPUP, submenu.Build(actions), text);
            else
            {
                actions.Add(action!);
                uint flags = Win32.MF_STRING | (isChecked ? Win32.MF_CHECKED : 0) | (enabled ? 0 : Win32.MF_GRAYED);
                Win32.AppendMenu(menu, flags, actions.Count, text);
            }
        }
        return menu;
    }
}
