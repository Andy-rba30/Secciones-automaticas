namespace ArbaSecciones.Core.Tablas;

/// <summary>Cómo se reparten las partes de una tabla dividida (equivale a «Tile tables Across / Down» de Civil 3D).</summary>
public enum DireccionDivision
{
    /// <summary>Las partes van una al lado de otra, de izquierda a derecha (Across).</summary>
    Horizontal,

    /// <summary>Las partes van una debajo de otra, de arriba hacia abajo (Down).</summary>
    Vertical,
}

/// <summary>Dónde se coloca la tabla dentro del área útil designada.</summary>
public enum AlineacionTabla
{
    SuperiorIzquierda,
    SuperiorCentro,
    SuperiorDerecha,
    CentroIzquierda,
    Centro,
    CentroDerecha,
    InferiorIzquierda,
    InferiorCentro,
    InferiorDerecha,
}

/// <summary>Resultado de comparar la tabla con el área disponible.</summary>
public enum EstadoCabida
{
    /// <summary>Entra tal como está configurada.</summary>
    CabeConHolgura,

    /// <summary>No entra como está, pero sí dividiéndola en columnas (ver <see cref="CabidaTabla.Sugerida"/>).</summary>
    CabeDividiendo,

    /// <summary>No entra ni dividiéndola: hay que ampliar el área o reducir la tabla.</summary>
    Insuficiente,
}

/// <summary>
/// Forma de una tabla sin dividir, en una sola unidad (normalmente la del papel):
/// ancho de una tabla, alto del encabezado (título y nombres de columna, que se repiten en cada parte), alto de fila
/// y cantidad de filas de datos (una por progresiva).
/// </summary>
public sealed record EstructuraTabla(double AnchoTabla, double AltoEncabezado, double AltoFila, int FilasDatos)
{
    public double AltoSinDividir => AltoEncabezado + Math.Max(0, FilasDatos) * AltoFila;
}

/// <summary>Parámetros de división de la tabla (equivalen a «Table Split Data» de Civil 3D).</summary>
public sealed record OpcionesDivision
{
    /// <summary>Dividir la tabla cuando excede el máximo de filas por columna.</summary>
    public bool Dividir { get; init; }

    /// <summary>Máximo de filas de datos por columna. 0 = automático (las que entren en el alto disponible).</summary>
    public int MaxFilasPorColumna { get; init; }

    /// <summary>Separación entre las partes de la tabla, en la misma unidad que la estructura.</summary>
    public double Espaciado { get; init; }

    public DireccionDivision Direccion { get; init; } = DireccionDivision.Horizontal;

    /// <summary>Máximo de partes en cada fila (Horizontal) o columna (Vertical) antes de empezar otra. 0 = sin límite.</summary>
    public int MaxTablasPorPila { get; init; }

    public static OpcionesDivision SinDividir { get; } = new();
}

/// <summary>
/// Medidas de una tabla ya armada: ancho y alto totales (todas sus partes), número de columnas tras la división,
/// filas por columna y el margen que se deja alrededor dentro del área. El área envolvente demandada incluye el margen.
/// </summary>
public sealed record DimensionesTabla(double AnchoTotal, double AltoTotal, int Columnas, int FilasPorColumna, double Margen)
{
    public double AnchoDemandado => AnchoTotal + 2 * Margen;
    public double AltoDemandado => AltoTotal + 2 * Margen;
    public double AreaDemandada => AnchoDemandado * AltoDemandado;

    /// <summary>Medidas leídas del dibujo (extensión geométrica de la tabla), ya en la unidad de comparación.</summary>
    public static DimensionesTabla DesdeMedida(double ancho, double alto, double margen = 0, int columnas = 1, int filasPorColumna = 0) =>
        new(Math.Max(0, ancho), Math.Max(0, alto), Math.Max(1, columnas), Math.Max(0, filasPorColumna), Math.Max(0, margen));
}

/// <summary>Veredicto de cabida con el detalle de medidas y un mensaje listo para mostrar.</summary>
public sealed record CabidaTabla(
    EstadoCabida Estado,
    double AnchoDisponible,
    double AltoDisponible,
    DimensionesTabla Dimensiones,
    DimensionesTabla? Sugerida,
    string Mensaje)
{
    public bool Cabe => Estado == EstadoCabida.CabeConHolgura;
    public double FaltaAncho => Math.Max(0, Dimensiones.AnchoDemandado - AnchoDisponible);
    public double FaltaAlto => Math.Max(0, Dimensiones.AltoDemandado - AltoDisponible);
    public double HolguraAncho => Math.Max(0, AnchoDisponible - Dimensiones.AnchoDemandado);
    public double HolguraAlto => Math.Max(0, AltoDisponible - Dimensiones.AltoDemandado);
}
