namespace ArbaSecciones.Core.Escalas;

/// <summary>
/// Pasa medidas entre papel y modelo según la escala y las unidades reales del dibujo y del membrete.
/// <para>
/// Factor = unidades de papel por unidad de modelo. Con modelo en metros y papel en mm:
/// Factor = 1000 / denominador (1:200 → 5). Es el CustomScale del viewport y el divisor de
/// ViewHeight = Height / Factor.
/// </para>
/// </summary>
public sealed class ConversorEscala
{
    public EscalaSecciones Escala { get; }
    public UnidadLongitud UnidadModelo { get; }
    public UnidadLongitud UnidadPapel { get; }

    public ConversorEscala(EscalaSecciones escala, UnidadLongitud unidadModelo, UnidadLongitud unidadPapel)
    {
        Escala = escala ?? throw new ArgumentNullException(nameof(escala));
        UnidadModelo = unidadModelo;
        UnidadPapel = unidadPapel;
    }

    /// <summary>Milímetros de modelo por milímetro de papel, sin escala (1000 para m/mm).</summary>
    public double RelacionUnidades => UnidadModelo.EnMilimetros() / UnidadPapel.EnMilimetros();

    /// <summary>Unidades de papel por unidad de modelo en horizontal. Es el CustomScale del viewport.</summary>
    public double FactorH => RelacionUnidades / Escala.DenominadorH;

    /// <summary>Unidades de papel por unidad de cota (sin exagerar) en vertical.</summary>
    public double FactorV => RelacionUnidades / Escala.DenominadorV;

    public double PapelAModelo(double medidaPapel) => medidaPapel / FactorH;

    public double ModeloAPapel(double medidaModelo) => medidaModelo * FactorH;

    /// <summary>
    /// Unidades de dibujo por unidad de papel para la escala de anotación (AnnotationScale.DrawingUnits / PaperUnits).
    /// m/mm a 1:200 → 0.2 (1 mm de papel = 0.2 m de modelo).
    /// </summary>
    public double UnidadesDibujoPorUnidadPapel => 1.0 / FactorH;

    /// <summary>ViewHeight del viewport (unidades de modelo) para un viewport de esa altura en papel.</summary>
    public double ViewHeight(double altoViewportPapel) => altoViewportPapel / FactorH;

    public ConversorEscala ConEscala(EscalaSecciones nueva) => new(nueva, UnidadModelo, UnidadPapel);

    public override string ToString() =>
        Escala + " · factor " + FactorH.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture) +
        " " + UnidadPapel.Abreviatura() + "/" + UnidadModelo.Abreviatura();
}
