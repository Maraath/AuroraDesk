using System.Windows.Interop;

namespace AuroraDesk.Interop;

/// <summary>
/// Janela oculta (popup de tamanho zero) que recebe mensagens do sistema: bandeja, comandos de
/// outra instância, TaskbarCreated (Explorer reiniciado), mudança de tema e de monitores.
/// Não é "message-only" porque o menu da bandeja precisa de uma janela que possa ficar em primeiro plano.
/// </summary>
internal sealed class ControlWindow : IDisposable
{
    public const string Title = "AuroraDesk.Controle";

    /// <summary>Comandos enviados por uma segunda instância (wParam).</summary>
    public const int CommandOpenSettings = 1;
    public const int CommandExit = 2;

    public static readonly int CommandMessage = Win32.RegisterWindowMessage("AuroraDesk.Comando");
    public static readonly int TaskbarCreatedMessage = Win32.RegisterWindowMessage("TaskbarCreated");

    private readonly HwndSource _source;

    public ControlWindow()
    {
        var p = new HwndSourceParameters(Title)
        {
            Width = 0,
            Height = 0,
            WindowStyle = Win32.WS_POPUP,
            ExtendedWindowStyle = Win32.WS_EX_TOOLWINDOW,
        };
        _source = new HwndSource(p);
        _source.AddHook(Hook);
    }

    public nint Handle => _source.Handle;

    /// <summary>Hook de mensagens. Devolva true para marcar como tratada.</summary>
    public event Func<int, nint, nint, bool>? Message;

    private nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (Message is { } handler)
        {
            foreach (Func<int, nint, nint, bool> h in handler.GetInvocationList())
            {
                try
                {
                    if (h(msg, wParam, lParam)) { handled = true; break; }
                }
                catch (Exception ex)
                {
                    Core.Logging.Log.Error($"Erro ao tratar a mensagem 0x{msg:X}", ex);
                }
            }
        }
        return 0;
    }

    /// <summary>Envia um comando para a instância já aberta. Devolve false se não houver.</summary>
    public static bool SendToRunningInstance(int command)
    {
        nint hwnd = Win32.FindWindow(null, Title);
        if (hwnd == 0) return false;
        Win32.PostMessage(hwnd, CommandMessage, command, 0);
        return true;
    }

    public void Dispose() => _source.Dispose();
}
