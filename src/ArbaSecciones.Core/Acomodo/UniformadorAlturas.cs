namespace ArbaSecciones.Core.Acomodo;

/// <summary>
/// Cálculo (sin AutoCAD) de los rangos de elevación uniformes de un conjunto de vistas de sección.
/// Todo en unidades de modelo. Cada rango requerido es [mínimo, máximo] de lo que hay que mostrar
/// (terreno, rasante, cunetas, taludes…): el rango fijado siempre lo contiene, así no se recorta nada.
/// </summary>
public static class UniformadorAlturas
{
    /// <summary>Múltiplo de la rejilla vertical que se usa si el usuario no indica otro.</summary>
    public const double IntervaloPorDefecto = 5.0;

    private const double Eps = 1e-9;

    /// <summary>Mayor múltiplo del intervalo que no pasa de <paramref name="valor"/>. Con intervalo 0 no redondea.</summary>
    public static double Piso(double valor, double intervalo) =>
        intervalo > 0 ? Math.Floor(valor / intervalo + Eps) * intervalo : valor;

    /// <summary>Menor múltiplo del intervalo que no es menor que <paramref name="valor"/>. Con intervalo 0 no redondea.</summary>
    public static double Techo(double valor, double intervalo) =>
        intervalo > 0 ? Math.Ceiling(valor / intervalo - Eps) * intervalo : valor;

    /// <summary>
    /// Altura mínima, múltiplo del intervalo, de una ventana alineada a la rejilla que contiene [min, max]
    /// (Techo(max) − Piso(min)). Puede superar al desnivel real hasta en un intervalo.
    /// </summary>
    public static double AlturaAlineada(double min, double max, double intervalo)
    {
        if (max < min) (min, max) = (max, min);
        return Techo(max, intervalo) - Piso(min, intervalo);
    }

    /// <summary>
    /// Altura envolvente: la mayor <see cref="AlturaAlineada"/> del conjunto. Con ella cualquier rango del conjunto
    /// cabe en una ventana alineada a la rejilla. 0 si no hay rangos.
    /// </summary>
    public static double AlturaUniforme(IEnumerable<(double Min, double Max)> rangos, double intervalo)
    {
        ArgumentNullException.ThrowIfNull(rangos);
        double h = 0;
        foreach (var (min, max) in rangos) h = Math.Max(h, AlturaAlineada(min, max, intervalo));
        return h;
    }

    /// <summary>
    /// Ventana de la altura indicada centrada en el rango requerido, con el mínimo en la rejilla y garantizando
    /// <c>Min ≤ min requerido</c> y <c>Max ≥ max requerido</c>.
    /// </summary>
    /// <param name="altura">Altura uniforme; debe ser al menos <see cref="AlturaAlineada"/> de este rango.</param>
    public static (double Min, double Max) RangoFijado(double min, double max, double altura, double intervalo)
    {
        if (max < min) (min, max) = (max, min);
        if (altura + 1e-6 < AlturaAlineada(min, max, intervalo))
            throw new ArgumentOutOfRangeException(nameof(altura), "La altura uniforme es menor que el rango que hay que mostrar.");

        double centro = (min + max) / 2;
        double inicio = Piso(centro - altura / 2, intervalo);

        // Ajuste para cubrir: la ventana no puede empezar por encima del mínimo requerido
        // ni terminar por debajo del máximo requerido.
        double inicioMasBajo = Techo(max, intervalo) - altura;
        double inicioMasAlto = Piso(min, intervalo);
        inicio = Math.Max(inicio, inicioMasBajo);
        inicio = Math.Min(inicio, inicioMasAlto);

        return (inicio, inicio + altura);
    }
}
