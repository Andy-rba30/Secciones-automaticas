namespace ArbaSecciones.Core.Acomodo;

/// <summary>
/// Datos de una lámina que se guardan en el dibujo (en el marco de la lámina) y se leen al crear las presentaciones.
/// </summary>
public sealed record InfoLamina(
    int Numero,
    int? Grupo,
    int Parte,
    int PartesDelGrupo,
    double ProgresivaInicial,
    double ProgresivaFinal,
    int Cantidad)
{
    public static InfoLamina Desde(Lamina lamina)
    {
        ArgumentNullException.ThrowIfNull(lamina);
        return new InfoLamina(lamina.Numero, lamina.Grupo, lamina.Parte, lamina.PartesDelGrupo,
            lamina.ProgresivaInicial, lamina.ProgresivaFinal, lamina.Cantidad);
    }
}

/// <summary>
/// Posición de los marcos de lámina en el modelo: filas de hasta <see cref="PorFila"/> láminas,
/// de izquierda a derecha, y las filas siguientes hacia abajo.
/// </summary>
public static class MarcosModelo
{
    public const int PorFila = 10;

    /// <summary>Esquina inferior izquierda del marco (relativa al punto base), en unidades de modelo.</summary>
    public static (double X, double Y) Origen(int indice, double anchoModelo, double altoModelo, double separacionModelo, int porFila = PorFila)
    {
        if (indice < 0) throw new ArgumentOutOfRangeException(nameof(indice));
        if (porFila < 1) throw new ArgumentOutOfRangeException(nameof(porFila));
        int columna = indice % porFila;
        int fila = indice / porFila;
        return (columna * (anchoModelo + separacionModelo), -fila * (altoModelo + separacionModelo));
    }
}
