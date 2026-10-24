using System.Windows;
using System.Windows.Threading;
using AuroraDesk.Core.Logging;

namespace AuroraDesk;

/// <summary>Aplicação WPF sem janela principal: tudo é hospedado pelo <see cref="AppHost"/>.</summary>
internal sealed class App : Application
{
    private readonly bool _openSettings;
    private readonly bool _logon;
    private AppHost? _host;

    public App(bool openSettings, bool logon)
    {
        _openSettings = openSettings;
        _logon = logon;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Exceção não tratada", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Error("Tarefa com exceção não observada", e.Exception); e.SetObserved(); };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _host = new AppHost(this);
        if (_logon && !ShellReady()) WaitForShell(_host);
        else _host.Start(_openSettings);
    }

    /// <summary>A barra do Explorer já existe? Sem ela não há bandeja, AppBar nem barra para ocultar.</summary>
    private static bool ShellReady() => Interop.Win32.FindWindow("Shell_TrayWnd", null) != 0;

    /// <summary>
    /// Aberto pela tarefa do logon, o app pode chegar antes do Explorer. A configuração, os temas e o
    /// WPF já carregaram no construtor do host (em paralelo com o Explorer); aqui só espera a barra
    /// existir, verificando a cada 100 ms, por até 60 s.
    /// </summary>
    private void WaitForShell(AppHost host)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            if (!ShellReady() && watch.Elapsed < TimeSpan.FromSeconds(60)) return;
            timer.Stop();
            Log.Info($"Esperou o Explorer por {watch.ElapsedMilliseconds} ms");
            host.Start(_openSettings);
        };
        timer.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        Log.Info("Encerrado");
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Um widget com defeito não pode derrubar o app: registra e segue.
        Log.Error("Erro na interface", e.Exception);
        e.Handled = true;
    }
}
