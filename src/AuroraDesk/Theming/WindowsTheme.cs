using AuroraDesk.Core.Theming;
using Microsoft.Win32;

namespace AuroraDesk.Theming;

/// <summary>Leitura (somente leitura!) do modo claro/escuro e da cor de destaque do Windows.</summary>
internal static class WindowsTheme
{
    public static bool AppsUseDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int light || light == 0;
        }
        catch { return true; }
    }

    /// <summary>Cor de destaque (DWM\AccentColor, gravada como ABGR).</summary>
    public static ArgbColor? Accent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int abgr)
            {
                uint v = unchecked((uint)abgr);
                return new ArgbColor(255, (byte)v, (byte)(v >> 8), (byte)(v >> 16));
            }
        }
        catch { }
        return null;
    }
}
