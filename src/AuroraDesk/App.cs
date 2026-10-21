using System.Windows;
using System.Windows.Threading;
using AuroraDesk.Core.Logging;

namespace AuroraDesk;

/// <summary>Aplicação WPF sem janela principal: tudo é hospedado pelo <see cref="AppHost"/>.</summary>
internal sealed class App : Application
{
    private readonly bool _openSettings;
    private AppHost? _host;

    public App(bool openSettings)
    {
        _openSettings = openSettings;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Exceção não tratada", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Error("Tarefa com exceção não observada", e.Exception); e.SetObserved(); };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _host = new AppHost(this);
        _host.Start(_openSettings);
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
        _host?.ReportUiFailure(e.Exception);
    }
}
