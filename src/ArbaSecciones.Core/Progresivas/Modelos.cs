namespace ArbaSecciones.Core.Progresivas;

/// <summary>Tipo de elemento geométrico del alineamiento.</summary>
public enum TipoTramo
{
    Tangente,
    Curva,
    Espiral,
}

/// <summary>Un elemento del alineamiento entre dos progresivas (sin ecuaciones de progresiva).</summary>
public readonly record struct TramoAlineamiento(double Inicio, double Fin, TipoTramo Tipo)
{
    public double Longitud => Fin - Inicio;
}

/// <summary>Origen de cada progresiva de muestreo.</summary>
public enum TipoProgresiva
{
    Inicio,
    Fin,
    PC,
    PT,
    TS,
    SC,
    CS,
    ST,
    /// <summary>Curva con curva (compuesta o inversa).</summary>
    PCC,
    /// <summary>Espiral con espiral.</summary>
    SS,
    /// <summary>Tangente con tangente sin curva (vértice).</summary>
    PI,
    Manual,
    Regular,
}

public enum ModoProgresivas
{
    /// <summary>0+010, 0+020… (múltiplos del intervalo desde 0+000).</summary>
    MultiplosRedondos,

    /// <summary>Se miden desde el inicio de cada elemento.</summary>
    DesdeInicioElemento,
}

public sealed record ProgresivaMuestreo(double Progresiva, TipoProgresiva Tipo, TipoTramo? Tramo)
{
    public bool EsPuntoNotable => Tipo is not (TipoProgresiva.Regular or TipoProgresiva.Manual);
}

public sealed class ResultadoProgresivas
{
    public ResultadoProgresivas(double inicio, double fin, IReadOnlyList<ProgresivaMuestreo> progresivas, IReadOnlyList<string> avisos)
    {
        Inicio = inicio;
        Fin = fin;
        Progresivas = progresivas;
        Avisos = avisos;
    }

    public double Inicio { get; }
    public double Fin { get; }
    public IReadOnlyList<ProgresivaMuestreo> Progresivas { get; }
    public IReadOnlyList<string> Avisos { get; }
}
