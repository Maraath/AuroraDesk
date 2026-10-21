using AuroraDesk.Core.Backup;

namespace AuroraDesk.Core.Tests;

public sealed class TaskbarBackupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aurora-barra-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void GuardaEApagaOEstadoOriginal()
    {
        var b = new TaskbarBackup(Path.Combine(_dir, "barra-de-tarefas.json"));
        Assert.False(b.Exists);
        b.Remember(currentAppBarState: 2, processId: 123);
        Assert.True(b.Exists);
        Assert.Equal(2, b.Load()!.OriginalAppBarState);
        Assert.Equal(123, b.Load()!.OwnerProcessId);
        b.Clear();
        Assert.False(b.Exists);
        Assert.Null(b.Load());
    }

    [Fact]
    public void DepoisDeUmaQuedaMantemOOriginalAntigo()
    {
        var b = new TaskbarBackup(Path.Combine(_dir, "barra-de-tarefas.json"));
        b.Remember(currentAppBarState: 0, processId: 1);      // original: sempre visível
        // O app caiu sem restaurar; ao abrir de novo, o estado atual já é "ocultar automaticamente".
        var state = b.Remember(currentAppBarState: 1, processId: 2);
        Assert.Equal(0, state.OriginalAppBarState);
        Assert.Equal(2, state.OwnerProcessId);
    }

    [Fact]
    public void ArquivoCorrompidoNaoQuebra()
    {
        Directory.CreateDirectory(_dir);
        string file = Path.Combine(_dir, "barra-de-tarefas.json");
        File.WriteAllText(file, "{ lixo");
        var b = new TaskbarBackup(file);
        Assert.Null(b.Load());
        Assert.Equal(3, b.Remember(3, 9).OriginalAppBarState);
    }
}
