using System.Runtime.InteropServices;

namespace AuroraDesk.Core.Native;

/// <summary>
/// Volume do alto-falante padrão (Core Audio: IMMDeviceEnumerator → IAudioEndpointVolume),
/// chamado direto pela vtable. Cada chamada pega o dispositivo padrão do momento, então trocar
/// para o fone de ouvido é percebido na próxima leitura.
/// </summary>
public static unsafe partial class Audio
{
    private static readonly Guid CLSID_MMDeviceEnumerator = new("bcde0395-e52f-467c-8e3d-c4579291692e");
    private static readonly Guid IID_IMMDeviceEnumerator = new("a95664d2-9614-4f35-a746-de8db63617e6");
    private static readonly Guid IID_IAudioEndpointVolume = new("5cdf2c82-841e-4546-9722-0cf74078229a");

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid riid, out nint ppv);

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint coinit);

    private static void Release(nint unk) => ((delegate* unmanaged[Stdcall]<nint, uint>)(*(void***)unk)[2])(unk);

    /// <summary>Executa <paramref name="action"/> com o IAudioEndpointVolume do alto-falante padrão.</summary>
    private static bool WithEndpoint(Func<nint, bool> action)
    {
        CoInitializeEx(0, 0x0); // MTA nas threads do agendador; S_FALSE/RPC_E_CHANGED_MODE se já iniciado
        const uint CLSCTX_ALL = 0x17;
        if (CoCreateInstance(CLSID_MMDeviceEnumerator, 0, CLSCTX_ALL, IID_IMMDeviceEnumerator, out nint enumerator) != 0) return false;
        nint device = 0, volume = 0;
        try
        {
            // IMMDeviceEnumerator::GetDefaultAudioEndpoint (4): eRender = 0, eMultimedia = 1
            if (((delegate* unmanaged[Stdcall]<nint, int, int, nint*, int>)(*(void***)enumerator)[4])(enumerator, 0, 1, &device) != 0) return false;
            // IMMDevice::Activate (3)
            fixed (Guid* iid = &IID_IAudioEndpointVolume)
                if (((delegate* unmanaged[Stdcall]<nint, Guid*, uint, void*, nint*, int>)(*(void***)device)[3])(device, iid, CLSCTX_ALL, null, &volume) != 0) return false;
            return action(volume);
        }
        finally
        {
            if (volume != 0) Release(volume);
            if (device != 0) Release(device);
            Release(enumerator);
        }
    }

    // Ponteiro guardado para as leituras periódicas (thread MTA do agendador). Recriar
    // enumerador + dispositivo a cada leitura custava ~0,2% de CPU; assim são só duas chamadas.
    private static nint _cachedVolume;
    private static int _readsSinceRefresh;
    private static readonly object Gate = new();

    /// <summary>Volume (0..1) e mudo. Devolve false se não houver alto-falante.</summary>
    public static bool TryRead(out float level, out bool muted)
    {
        level = 0;
        muted = false;
        lock (Gate)
        {
            // A cada 10 leituras (~20 s) pega o dispositivo padrão de novo (fone conectado, etc.).
            if (_cachedVolume != 0 && ++_readsSinceRefresh >= 10) DropCache();
            if (_cachedVolume == 0 && !Acquire()) return false;

            float lv;
            int mv;
            nint v = _cachedVolume;
            // GetMasterVolumeLevelScalar (9), GetMute (15)
            if (((delegate* unmanaged[Stdcall]<nint, float*, int>)(*(void***)v)[9])(v, &lv) != 0 ||
                ((delegate* unmanaged[Stdcall]<nint, int*, int>)(*(void***)v)[15])(v, &mv) != 0)
            {
                DropCache(); // dispositivo removido: tenta de novo na próxima leitura
                return false;
            }
            level = lv;
            muted = mv != 0;
            return true;
        }
    }

    private static bool Acquire()
    {
        _readsSinceRefresh = 0;
        return WithEndpoint(v =>
        {
            // AddRef (1): o WithEndpoint libera a referência dele; esta fica guardada.
            ((delegate* unmanaged[Stdcall]<nint, uint>)(*(void***)v)[1])(v);
            _cachedVolume = v;
            return true;
        });
    }

    private static void DropCache()
    {
        if (_cachedVolume != 0) Release(_cachedVolume);
        _cachedVolume = 0;
    }

    /// <summary>Soma <paramref name="delta"/> ao volume (−1..1), limitado entre 0 e 1. Tira o mudo ao subir.</summary>
    public static bool Change(float delta)
    {
        return WithEndpoint(v =>
        {
            float l;
            if (((delegate* unmanaged[Stdcall]<nint, float*, int>)(*(void***)v)[9])(v, &l) != 0) return false;
            float next = Math.Clamp(l + delta, 0f, 1f);
            // SetMasterVolumeLevelScalar (7), SetMute (14)
            ((delegate* unmanaged[Stdcall]<nint, float, Guid*, int>)(*(void***)v)[7])(v, next, null);
            if (delta > 0) ((delegate* unmanaged[Stdcall]<nint, int, Guid*, int>)(*(void***)v)[14])(v, 0, null);
            return true;
        });
    }

    public static bool ToggleMute()
    {
        return WithEndpoint(v =>
        {
            int m;
            if (((delegate* unmanaged[Stdcall]<nint, int*, int>)(*(void***)v)[15])(v, &m) != 0) return false;
            return ((delegate* unmanaged[Stdcall]<nint, int, Guid*, int>)(*(void***)v)[14])(v, m == 0 ? 1 : 0, null) == 0;
        });
    }
}
