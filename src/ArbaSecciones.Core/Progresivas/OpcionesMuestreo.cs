namespace ArbaSecciones.Core.Progresivas;

/// <summary>Parámetros de la pestaña Muestreo. Se guardan en el JSON de configuración.</summary>
public sealed class OpcionesMuestreo
{
    /// <summary>Null = inicio del alineamiento.</summary>
    public double? ProgresivaInicial { get; set; }

    /// <summary>Null = fin del alineamiento.</summary>
    public double? ProgresivaFinal { get; set; }

    public double IntervaloTangente { get; set; } = 20;
    public double IntervaloCurva { get; set; } = 10;
    public ModoProgresivas Modo { get; set; } = ModoProgresivas.MultiplosRedondos;

    public bool IncluirInicio { get; set; } = true;
    public bool IncluirFin { get; set; } = true;
    public bool IncluirPC { get; set; } = true;
    public bool IncluirPT { get; set; } = true;
    public bool IncluirTS { get; set; } = true;
    public bool IncluirSC { get; set; } = true;
    public bool IncluirCS { get; set; } = true;
    public bool IncluirST { get; set; } = true;
    public bool IncluirPCC { get; set; } = true;
    public bool IncluirSS { get; set; } = true;
    public bool IncluirPI { get; set; } = true;

    public List<double> Adicionales { get; set; } = new();

    /// <summary>Dos progresivas a esta distancia o menos se consideran la misma.</summary>
    public double Tolerancia { get; set; } = 0.01;

    public double AnchoIzquierdo { get; set; } = 20;
    public double AnchoDerecho { get; set; } = 20;

    public string NombreGrupo { get; set; } = "ARBA-SEC";

    /// <summary>Último alineamiento usado (solo para volver a seleccionarlo en la paleta).</summary>
    public string? Alineamiento { get; set; }

    /// <summary>Nombres de las fuentes de muestreo marcadas (superficies, superficies de obra lineal, obras lineales).</summary>
    public List<string> Fuentes { get; set; } = new();

    public bool Incluye(TipoProgresiva tipo) => tipo switch
    {
        TipoProgresiva.Inicio => IncluirInicio,
        TipoProgresiva.Fin => IncluirFin,
        TipoProgresiva.PC => IncluirPC,
        TipoProgresiva.PT => IncluirPT,
        TipoProgresiva.TS => IncluirTS,
        TipoProgresiva.SC => IncluirSC,
        TipoProgresiva.CS => IncluirCS,
        TipoProgresiva.ST => IncluirST,
        TipoProgresiva.PCC => IncluirPCC,
        TipoProgresiva.SS => IncluirSS,
        TipoProgresiva.PI => IncluirPI,
        TipoProgresiva.Manual => true,
        TipoProgresiva.Regular => true,
        _ => false,
    };

    /// <summary>Lista de problemas que impiden generar. Vacía = válido.</summary>
    public List<string> Validar()
    {
        var errores = new List<string>();
        if (!(IntervaloTangente > 0)) errores.Add("El intervalo en tangentes debe ser mayor que cero.");
        if (!(IntervaloCurva > 0)) errores.Add("El intervalo en curvas debe ser mayor que cero.");
        if (Tolerancia < 0) errores.Add("La tolerancia no puede ser negativa.");
        double menor = Math.Min(IntervaloTangente, IntervaloCurva);
        if (menor > 0 && Tolerancia >= menor / 2) errores.Add("La tolerancia debe ser menor que la mitad del intervalo más chico.");
        if (ProgresivaInicial.HasValue && ProgresivaFinal.HasValue && ProgresivaFinal <= ProgresivaInicial)
            errores.Add("La progresiva final debe ser mayor que la inicial.");
        if (!(AnchoIzquierdo > 0) || !(AnchoDerecho > 0)) errores.Add("Los anchos de muestreo deben ser mayores que cero.");
        return errores;
    }
}
