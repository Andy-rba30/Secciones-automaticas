using ArbaSecciones.Core.Escalas;

namespace ArbaSecciones.Core.Acomodo;

public readonly record struct Tamano(double Ancho, double Alto);

public enum OrdenAcomodo
{
    /// <summary>Columnas de abajo hacia arriba, avanzando de izquierda a derecha.</summary>
    PorColumnas,

    /// <summary>Filas de izquierda a derecha, avanzando de arriba hacia abajo.</summary>
    PorFilas,
}

public enum ModoPaginacion
{
    /// <summary>Cada km empieza en lámina nueva.</summary>
    PorKilometro,

    /// <summary>Se llena cada lámina sin respetar los cortes de km.</summary>
    Continuo,
}

public enum PosicionFranja
{
    Abajo,
    Arriba,
}

/// <summary>
/// Medida de una vista de sección separada en dos partes:
/// <list type="bullet">
/// <item>el gráfico, que está en unidades de modelo y se achica o agranda con la escala;</item>
/// <item>la anotación (textos de rejilla, bandas, título), que está en unidades de papel y no cambia con la escala.</item>
/// </list>
/// Así se puede saber el tamaño de la vista en papel con cualquier escala.
/// </summary>
public sealed record MedidaSeccion(
    string Id,
    double Progresiva,
    double RangoDesplazamiento,
    double RangoElevacion,
    double AnchoAnotacion,
    double AltoAnotacion)
{
    /// <summary>Tamaño en papel de la vista con la escala del conversor.</summary>
    public Tamano EnPapel(ConversorEscala conversor) => new(
        RangoDesplazamiento * conversor.FactorH + AnchoAnotacion,
        RangoElevacion * conversor.FactorV + AltoAnotacion);

    /// <summary>
    /// Arma la medida a partir de la extensión real de la vista en el modelo.
    /// </summary>
    /// <param name="anchoTotalModelo">Ancho de la extensión (GeometricExtents) en unidades de modelo.</param>
    /// <param name="altoTotalModelo">Alto de la extensión en unidades de modelo.</param>
    /// <param name="rangoDesplazamiento">Desplazamiento máximo menos mínimo de la vista (modelo).</param>
    /// <param name="rangoElevacion">Elevación máxima menos mínima de la vista, sin exagerar (modelo).</param>
    /// <param name="exageracionDibujada">Exageración vertical con la que está dibujada la vista (la del estilo).</param>
    /// <param name="conversorMedicion">Escala con la que se midió (la escala de anotación vigente).</param>
    public static MedidaSeccion DesdeExtension(
        string id,
        double progresiva,
        double anchoTotalModelo,
        double altoTotalModelo,
        double rangoDesplazamiento,
        double rangoElevacion,
        double exageracionDibujada,
        ConversorEscala conversorMedicion)
    {
        ArgumentNullException.ThrowIfNull(conversorMedicion);
        double f = conversorMedicion.FactorH;
        double rangoD = Math.Max(0, rangoDesplazamiento);
        double rangoE = Math.Max(0, rangoElevacion);
        double altoGrafico = rangoE * exageracionDibujada;

        // EnPapel usa la escala vertical pedida. Si el estilo dibuja con otra exageración, se guarda un
        // rango equivalente para que el tamaño calculado sea el que realmente ocupa la vista dibujada.
        double rangoEEquivalente = rangoE * exageracionDibujada / conversorMedicion.Escala.Exageracion;

        return new MedidaSeccion(
            id,
            progresiva,
            rangoD,
            rangoEEquivalente,
            Math.Max(0, (anchoTotalModelo - rangoD) * f),
            Math.Max(0, (altoTotalModelo - altoGrafico) * f));
    }
}

/// <summary>Ubicación de una sección dentro del área útil, en unidades de papel, medida desde la esquina inferior izquierda.</summary>
public sealed record Colocacion(MedidaSeccion Seccion, double X, double Y, double Ancho, double Alto)
{
    public double CentroX => X + Ancho / 2;
    public double CentroY => Y + Alto / 2;
}

public sealed class Lamina
{
    internal Lamina(int numero, int? grupo, int parte, IReadOnlyList<Colocacion> colocaciones)
    {
        Numero = numero;
        Grupo = grupo;
        Parte = parte;
        Colocaciones = colocaciones;
    }

    /// <summary>Número correlativo de la lámina, desde 1.</summary>
    public int Numero { get; }

    /// <summary>Índice del km (0 = 0+000 a 1+000). Null en modo continuo.</summary>
    public int? Grupo { get; }

    /// <summary>Número de parte dentro del km (1 = lámina principal, 2 = continuación…).</summary>
    public int Parte { get; }

    /// <summary>Cuántas láminas ocupa el km de esta lámina.</summary>
    public int PartesDelGrupo { get; internal set; } = 1;

    public IReadOnlyList<Colocacion> Colocaciones { get; }

    public int Cantidad => Colocaciones.Count;

    /// <summary>Alto (papel) de la sección más alta de la lámina.</summary>
    public double AltoMaximo => Colocaciones.Max(c => c.Alto);

    /// <summary>True si todas las secciones de la lámina tienen el mismo alto.</summary>
    public bool AlturasUniformes => Colocaciones.All(c => Math.Abs(c.Alto - Colocaciones[0].Alto) <= 1e-6);
    public double ProgresivaInicial => Colocaciones.Min(c => c.Seccion.Progresiva);
    public double ProgresivaFinal => Colocaciones.Max(c => c.Seccion.Progresiva);
}
