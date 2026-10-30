namespace ArbaSecciones.Core.Materiales;

/// <summary>Punto de la sección del corredor: desplazamiento respecto a la línea base y elevación.</summary>
public sealed record PuntoSeccion(double Desplazamiento, double Elevacion);

/// <summary>Vínculo (link) de la sección con sus códigos y sus puntos.</summary>
public sealed record VinculoSeccion(int Indice, IReadOnlyList<string> Codigos, IReadOnlyList<PuntoSeccion> Puntos)
{
    public double DesdeX => Puntos.Count == 0 ? 0 : Puntos.Min(p => p.Desplazamiento);
    public double HastaX => Puntos.Count == 0 ? 0 : Puntos.Max(p => p.Desplazamiento);

    /// <summary>Lo que avanza en horizontal (cero en un vínculo vertical).</summary>
    public double AvanceHorizontal => HastaX - DesdeX;

    public double Longitud
    {
        get
        {
            double l = 0;
            for (int i = 1; i < Puntos.Count; i++)
                l += Math.Sqrt(Math.Pow(Puntos[i].Desplazamiento - Puntos[i - 1].Desplazamiento, 2) +
                               Math.Pow(Puntos[i].Elevacion - Puntos[i - 1].Elevacion, 2));
            return l;
        }
    }

    /// <summary>Pendiente media (Δelevación / Δdesplazamiento) entre el primer y el último punto. Null si es vertical.</summary>
    public double? Pendiente(double toleranciaVertical = 1e-6)
    {
        if (Puntos.Count < 2) return null;
        double dx = Puntos[^1].Desplazamiento - Puntos[0].Desplazamiento;
        if (Math.Abs(dx) <= toleranciaVertical) return null;
        return (Puntos[^1].Elevacion - Puntos[0].Elevacion) / dx;
    }

    public bool Tiene(string codigo) => Codigos.Any(c => string.Equals(c, codigo, StringComparison.OrdinalIgnoreCase));

    public bool TieneAlguno(IEnumerable<string> codigos) => codigos.Any(Tiene);

    /// <summary>Tramos rectos del vínculo, en el orden de sus puntos.</summary>
    public IEnumerable<(PuntoSeccion A, PuntoSeccion B)> Segmentos()
    {
        for (int i = 1; i < Puntos.Count; i++) yield return (Puntos[i - 1], Puntos[i]);
    }

    public string CodigosTexto => Codigos.Count == 0 ? "(sin código)" : string.Join(", ", Codigos);
}

/// <summary>Forma (shape) de la sección: códigos y contorno.</summary>
public sealed record FormaSeccion(IReadOnlyList<string> Codigos, IReadOnlyList<PuntoSeccion> Contorno);

/// <summary>Sección del corredor en una progresiva, sin tipos de Autodesk.</summary>
public sealed record SeccionCorredor(double Progresiva, IReadOnlyList<VinculoSeccion> Vinculos, IReadOnlyList<FormaSeccion> Formas)
{
    public double DesdeX => Vinculos.Where(v => v.Puntos.Count > 0).Select(v => v.DesdeX).DefaultIfEmpty(0).Min();
    public double HastaX => Vinculos.Where(v => v.Puntos.Count > 0).Select(v => v.HastaX).DefaultIfEmpty(0).Max();
    public double ElevacionMinima => Vinculos.SelectMany(v => v.Puntos).Select(p => p.Elevacion).DefaultIfEmpty(0).Min();
    public double ElevacionMaxima => Vinculos.SelectMany(v => v.Puntos).Select(p => p.Elevacion).DefaultIfEmpty(0).Max();

    /// <summary>Códigos de vínculo de la sección, sin repetir, en orden alfabético.</summary>
    public IReadOnlyList<string> CodigosVinculo => Vinculos
        .SelectMany(v => v.Codigos)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public IReadOnlyList<string> CodigosForma => Formas
        .SelectMany(f => f.Codigos)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
        .ToList();
}

/// <summary>Corrección de voladizo de la superficie del corredor (OverhangCorrectionType de Civil 3D).</summary>
public enum CorreccionVoladizo
{
    Ninguna,
    VinculosSuperiores,
    VinculosInferiores,
}

/// <summary>Las dos superficies del corredor que arma la pestaña Materiales.</summary>
public enum SuperficieMateriales
{
    Rasante,
    SubRasante,
}
