using System.Globalization;

namespace ArbaSecciones.Core.Escalas;

/// <summary>
/// Escala de las secciones. La horizontal manda: con ella se fija el viewport y la escala de anotación.
/// La vertical solo define la exageración (denominador H / denominador V).
/// </summary>
public sealed record EscalaSecciones
{
    public double DenominadorH { get; }
    public double DenominadorV { get; }

    public EscalaSecciones(double denominadorH, double? denominadorV = null)
    {
        Validar(denominadorH, nameof(denominadorH));
        double v = denominadorV ?? denominadorH;
        Validar(v, nameof(denominadorV));
        DenominadorH = denominadorH;
        DenominadorV = v;
    }

    public static EscalaSecciones Uniforme(double denominador) => new(denominador);

    /// <summary>Exageración vertical. H 1:200 y V 1:100 → 2.</summary>
    public double Exageracion => DenominadorH / DenominadorV;

    public bool TieneExageracion => Math.Abs(Exageracion - 1.0) > 1e-9;

    /// <summary>Otra escala horizontal con la misma exageración vertical.</summary>
    public EscalaSecciones ConDenominadorH(double nuevoDenominadorH) => new(nuevoDenominadorH, nuevoDenominadorH / Exageracion);

    public override string ToString() => TieneExageracion
        ? "H " + EscalasNormalizadas.Formatear(DenominadorH) + " / V " + EscalasNormalizadas.Formatear(DenominadorV)
        : EscalasNormalizadas.Formatear(DenominadorH);

    private static void Validar(double denominador, string nombre)
    {
        if (double.IsNaN(denominador) || double.IsInfinity(denominador) || denominador <= 0)
            throw new ArgumentOutOfRangeException(nombre, denominador.ToString(CultureInfo.InvariantCulture), "El denominador de escala debe ser mayor que cero.");
    }
}
