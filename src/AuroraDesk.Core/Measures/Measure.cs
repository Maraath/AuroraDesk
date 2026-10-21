namespace AuroraDesk.Core.Measures;

/// <summary>
/// Uma fonte de dados (CPU, RAM, relógio...). Cada instância é compartilhada por todos os
/// widgets que pedem a mesma medida com as mesmas opções (mesma <see cref="MeasureSpec.Key"/>).
/// </summary>
public abstract class Measure
{
    protected Measure(MeasureSpec spec) => Spec = spec;

    public MeasureSpec Spec { get; }
    public string Key => Spec.Key;

    /// <summary>Intervalo usado quando o skin não define "intervalo".</summary>
    public virtual TimeSpan DefaultInterval => TimeSpan.FromSeconds(1);

    /// <summary>Verdadeiro quando o valor natural é texto (relógio, estado da bateria).</summary>
    public virtual bool IsText => false;

    public double Value { get; protected set; }
    public double Max { get; protected set; } = 100;
    public string Text { get; protected set; } = "";

    /// <summary>Valor normalizado entre 0 e 1, usado por barras, arcos e gráficos.</summary>
    public double Fraction => Max <= 0 ? 0 : Math.Clamp(Value / Max, 0, 1);

    /// <summary>Incrementa sempre que valor ou texto mudam.</summary>
    public int Version { get; private set; }

    public string? Error { get; private set; }

    /// <summary>Lê a medida. Devolve true se algo mudou. Nunca lança exceção.</summary>
    internal bool Update(SampleContext context)
    {
        double oldValue = Value, oldMax = Max;
        string oldText = Text;
        try
        {
            Read(context);
            Error = null;
        }
        catch (Exception ex)
        {
            if (Error is null)
                Logging.Log.Warn($"Falha na medida '{Key}': {ex.Message}");
            Error = ex.Message;
            return false;
        }

        bool changed = Value != oldValue || Max != oldMax || !string.Equals(Text, oldText, StringComparison.Ordinal);
        if (changed) Version++;
        return changed;
    }

    protected abstract void Read(SampleContext context);

    /// <summary>Quando a medida deve ser lida de novo. O relógio sobrescreve para alinhar à virada do segundo/minuto.</summary>
    public virtual DateTimeOffset NextDue(DateTimeOffset now, TimeSpan interval) => now + interval;
}
