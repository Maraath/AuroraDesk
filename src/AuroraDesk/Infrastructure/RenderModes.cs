using System.Windows;
using System.Windows.Interop;

namespace AuroraDesk.Infrastructure;

/// <summary>
/// Renderização por janela. Por padrão, todas as janelas desenham por software: sem isso, o
/// WPF cria um dispositivo Direct3D e os drivers de vídeo (NVIDIA/Intel) ocupam ~85 MB no processo.
/// Só o dock com Mica/Acrílico usa GPU (o DWM precisa do canal alfa da superfície).
/// </summary>
internal static class RenderModes
{
    public static bool HardwareEverywhere { get; set; }

    public static void Apply(Window window, bool needsHardware = false)
    {
        if (PresentationSource.FromVisual(window) is HwndSource { CompositionTarget: { } target })
            target.RenderMode = HardwareEverywhere || needsHardware ? RenderMode.Default : RenderMode.SoftwareOnly;
    }
}
