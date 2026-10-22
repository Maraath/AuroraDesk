using AuroraDesk.Core.Backup;

namespace AuroraDesk.Core.Theming;

/// <summary>O que sincronizar entre o tema do Aurora e as cores do Windows.</summary>
public sealed record WindowsColorOptions(bool DarkMode, bool Accent, bool AccentOnTitleBars);

/// <summary>Um valor do registro do usuário (HKCU) a gravar, já codificado (<see cref="RegistryValueCodec"/>).</summary>
public sealed record RegistryWrite(string Key, string Name, string Value);

/// <summary>
/// Traduz um tema para as configurações oficiais de personalização do Windows: modo escuro ou
/// claro, cor de destaque (com a paleta de 8 tons que o Windows usa) e "mostrar a cor de
/// destaque nas barras de título e bordas". Só valores do usuário atual, todos reversíveis.
/// </summary>
public static class WindowsColorPlan
{
    public const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    public const string DwmKey = @"Software\Microsoft\Windows\DWM";
    public const string AccentKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
    public const string DesktopKey = @"Control Panel\Desktop";

    /// <summary>Chaves que este plano mexe (para restaurar só elas ao fechar o app).</summary>
    public static readonly string[] Keys = [PersonalizeKey, DwmKey, AccentKey, DesktopKey];

    public static IReadOnlyList<RegistryWrite> Build(Theme theme, WindowsColorOptions options)
    {
        var writes = new List<RegistryWrite>();
        if (options.DarkMode)
        {
            uint light = theme.Dark ? 0u : 1u;
            writes.Add(new(PersonalizeKey, "AppsUseLightTheme", RegistryValueCodec.Dword(light)));
            writes.Add(new(PersonalizeKey, "SystemUsesLightTheme", RegistryValueCodec.Dword(light)));
        }
        if (options.Accent)
        {
            var accent = theme.Accent with { A = 255 };
            var palette = Palette(accent);
            uint abgr = Abgr(accent);
            writes.Add(new(DesktopKey, "AutoColorization", RegistryValueCodec.Dword(0))); // não trocar pela cor do papel de parede
            writes.Add(new(AccentKey, "AccentPalette", RegistryValueCodec.Binary(PaletteBytes(palette))));
            writes.Add(new(AccentKey, "AccentColorMenu", RegistryValueCodec.Dword(abgr)));
            writes.Add(new(AccentKey, "StartColorMenu", RegistryValueCodec.Dword(Abgr(palette[4]))));
            writes.Add(new(DwmKey, "AccentColor", RegistryValueCodec.Dword(abgr)));
            writes.Add(new(DwmKey, "ColorizationColor", RegistryValueCodec.Dword(0xC4000000 | (accent.Value & 0x00FFFFFF))));
            writes.Add(new(DwmKey, "ColorizationAfterglow", RegistryValueCodec.Dword(0xC4000000 | (accent.Value & 0x00FFFFFF))));
        }
        if (options.AccentOnTitleBars)
            writes.Add(new(DwmKey, "ColorPrevalence", RegistryValueCodec.Dword(1)));
        return writes;
    }

    /// <summary>Formato do registro: 0xAABBGGRR.</summary>
    public static uint Abgr(ArgbColor c) => (uint)(0xFF << 24 | c.B << 16 | c.G << 8 | c.R);

    /// <summary>
    /// Paleta de 8 tons, do mais claro ao mais escuro, como a que o Windows grava em
    /// AccentPalette: três tons claros, a cor base no índice 3 e três escuros, mais um cinza.
    /// </summary>
    public static ArgbColor[] Palette(ArgbColor accent) =>
    [
        Mix(accent, ArgbColor.White, 0.60),
        Mix(accent, ArgbColor.White, 0.40),
        Mix(accent, ArgbColor.White, 0.20),
        accent,
        Mix(accent, ArgbColor.Black, 0.20),
        Mix(accent, ArgbColor.Black, 0.40),
        Mix(accent, ArgbColor.Black, 0.60),
        new ArgbColor(255, 0x69, 0x79, 0x7E),
    ];

    /// <summary>32 bytes: R, G, B, A (A = 0) por tom.</summary>
    public static byte[] PaletteBytes(ArgbColor[] palette)
    {
        var bytes = new byte[32];
        for (int i = 0; i < 8; i++)
        {
            bytes[i * 4] = palette[i].R;
            bytes[i * 4 + 1] = palette[i].G;
            bytes[i * 4 + 2] = palette[i].B;
            bytes[i * 4 + 3] = 0;
        }
        return bytes;
    }

    private static ArgbColor Mix(ArgbColor a, ArgbColor b, double t) => new(255,
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));
}
