using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AuroraDesk.Interop;

/// <summary>
/// COM do shell chamado direto pela vtable (sem wrappers gerados): ícones no tamanho exato
/// (IShellItemImageFactory), alvo e AppUserModelID de atalhos .lnk (IShellLinkW/IPropertyStore)
/// e AppUserModelID de janelas (SHGetPropertyStoreForWindow).
/// </summary>
internal static unsafe partial class ShellInterop
{
    private static readonly Guid IID_IShellItemImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");
    private static readonly Guid IID_IPropertyStore = new("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99");
    private static readonly Guid CLSID_ShellLink = new("00021401-0000-0000-c000-000000000046");
    private static readonly Guid IID_IShellLinkW = new("000214f9-0000-0000-c000-000000000046");
    private static readonly Guid IID_IPersistFile = new("0000010b-0000-0000-c000-000000000046");

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY { public Guid fmtid; public uint pid; }

    // PKEY_AppUserModel_ID
    private static readonly PROPERTYKEY PKEY_AppUserModel_ID = new() { fmtid = new Guid("9f4c2855-9f79-4b39-a8d0-e1d42de1d5f3"), pid = 5 };

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPVARIANT
    {
        public ushort vt;
        public ushort r1, r2, r3;
        public nint p;
        public nint p2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public short bmPlanes, bmBitsPixel;
        public nint bmBits;
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateItemFromParsingName(string path, nint bindCtx, in Guid riid, out nint ppv);

    [LibraryImport("shell32.dll")]
    private static partial int SHGetPropertyStoreForWindow(nint hwnd, in Guid riid, out nint ppv);

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid riid, out nint ppv);

    [LibraryImport("ole32.dll")]
    private static partial int PropVariantClear(ref PROPVARIANT pv);

    [LibraryImport("gdi32.dll")]
    private static partial int GetObjectW(nint h, int size, void* buffer);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDIBits(nint hdc, nint hbm, uint start, uint lines, void* bits, BITMAPINFOHEADER* info, uint usage);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint h);

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint hwnd, nint hdc);

    private static void Release(nint unk) => ((delegate* unmanaged[Stdcall]<nint, uint>)(*(void***)unk)[2])(unk);

    private static int QueryInterface(nint unk, in Guid iid, out nint ppv)
    {
        fixed (Guid* p = &iid)
        fixed (nint* pp = &ppv)
            return ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(void***)unk)[0])(unk, p, pp);
    }

    /// <summary>
    /// Ícone de um caminho do shell (arquivo, pasta, exe, .lnk ou "shell:AppsFolder\AUMID") em
    /// <paramref name="size"/> pixels. Devolve null se o shell não souber desenhar.
    /// </summary>
    public static BitmapSource? GetIcon(string parsingName, int size)
    {
        if (SHCreateItemFromParsingName(parsingName, 0, IID_IShellItemImageFactory, out nint factory) != 0 || factory == 0)
            return null;
        nint hbm = 0;
        try
        {
            const int SIIGBF_ICONONLY = 0x4, SIIGBF_BIGGERSIZEOK = 0x1;
            var sz = new SIZE { cx = size, cy = size };
            int hr = ((delegate* unmanaged[Stdcall]<nint, SIZE, int, nint*, int>)(*(void***)factory)[3])(factory, sz, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, &hbm);
            return hr == 0 && hbm != 0 ? ToBitmapSource(hbm) : null;
        }
        finally
        {
            if (hbm != 0) DeleteObject(hbm);
            Release(factory);
        }
    }

    /// <summary>Converte um HBITMAP de 32 bits (alfa pré-multiplicado) em BitmapSource congelado.</summary>
    private static BitmapSource? ToBitmapSource(nint hbm)
    {
        BITMAP bm;
        if (GetObjectW(hbm, sizeof(BITMAP), &bm) == 0 || bm.bmWidth <= 0 || bm.bmHeight == 0) return null;
        int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
        var pixels = new byte[w * h * 4];
        var header = new BITMAPINFOHEADER { biSize = sizeof(BITMAPINFOHEADER), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        nint dc = GetDC(0);
        try
        {
            fixed (byte* p = pixels)
                if (GetDIBits(dc, hbm, 0, (uint)h, p, &header, 0) == 0) return null;
        }
        finally
        {
            ReleaseDC(0, dc);
        }

        // Ícones antigos vêm sem canal alfa (tudo 0): trata como opacos.
        bool anyAlpha = false;
        for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] != 0) { anyAlpha = true; break; }
        if (!anyAlpha) for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;

        var source = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, w * 4);
        source.Freeze();
        return source;
    }

    /// <summary>Alvo e AppUserModelID de um atalho .lnk (qualquer um pode faltar).</summary>
    public static (string? Target, string? AppId, string? Arguments) ResolveShortcut(string lnkPath)
    {
        const uint CLSCTX_INPROC_SERVER = 1;
        if (CoCreateInstance(CLSID_ShellLink, 0, CLSCTX_INPROC_SERVER, IID_IShellLinkW, out nint link) != 0) return (null, null, null);
        try
        {
            if (QueryInterface(link, IID_IPersistFile, out nint persist) != 0) return (null, null, null);
            try
            {
                fixed (char* path = lnkPath)
                {
                    int hr = ((delegate* unmanaged[Stdcall]<nint, char*, int, int>)(*(void***)persist)[5])(persist, path, 0);
                    if (hr != 0) return (null, null, null);
                }
            }
            finally
            {
                Release(persist);
            }

            char* buffer = stackalloc char[1024];
            string? target = null, args = null;
            // IShellLinkW::GetPath (3) e GetArguments (10)
            if (((delegate* unmanaged[Stdcall]<nint, char*, int, void*, uint, int>)(*(void***)link)[3])(link, buffer, 1024, null, 0) == 0)
                target = new string(buffer) is { Length: > 0 } t ? t : null;
            if (((delegate* unmanaged[Stdcall]<nint, char*, int, int>)(*(void***)link)[10])(link, buffer, 1024) == 0)
                args = new string(buffer) is { Length: > 0 } a ? a : null;

            string? appId = null;
            if (QueryInterface(link, IID_IPropertyStore, out nint store) == 0)
            {
                appId = ReadAppId(store);
                Release(store);
            }
            return (target, appId, args);
        }
        finally
        {
            Release(link);
        }
    }

    /// <summary>AppUserModelID explícito de uma janela (UWP e apps que o definem).</summary>
    public static string? WindowAppId(nint hwnd)
    {
        if (SHGetPropertyStoreForWindow(hwnd, IID_IPropertyStore, out nint store) != 0 || store == 0) return null;
        try { return ReadAppId(store); }
        finally { Release(store); }
    }

    private static string? ReadAppId(nint store)
    {
        var key = PKEY_AppUserModel_ID;
        var pv = new PROPVARIANT();
        // IPropertyStore::GetValue (5)
        int hr = ((delegate* unmanaged[Stdcall]<nint, PROPERTYKEY*, PROPVARIANT*, int>)(*(void***)store)[5])(store, &key, &pv);
        try
        {
            const ushort VT_LPWSTR = 31;
            return hr == 0 && pv.vt == VT_LPWSTR && pv.p != 0 ? Marshal.PtrToStringUni(pv.p) : null;
        }
        finally
        {
            PropVariantClear(ref pv);
        }
    }
}
