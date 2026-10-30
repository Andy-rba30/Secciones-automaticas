using System.Globalization;

namespace ArbaSecciones.Core.Tablas;

/// <summary>
/// Cabida geométrica de una tabla de volúmenes en un área rectangular (sin AutoCAD). Todas las medidas en una sola
/// unidad, normalmente la del papel (mm), para que los mensajes digan lo que el usuario ve en la lámina.
/// <list type="bullet">
/// <item><see cref="Dimensiones"/>: arma la tabla (con o sin división en columnas) a partir de su estructura.</item>
/// <item><see cref="Evaluar(double, double, EstructuraTabla, OpcionesDivision, double, string)"/>: decide si cabe,
/// si cabe dividiéndola o si el área es insuficiente, y explica cuánto falta.</item>
/// <item><see cref="EstimarEstructura"/>: recupera la estructura a partir de la medida real de una tabla ya dibujada.</item>
/// <item><see cref="Posicionar"/>: esquina inferior izquierda de la tabla dentro del área según la alineación.</item>
/// </list>
/// </summary>
public static class ValidadorCabidaTabla
{
    /// <summary>Filas que ocupa el encabezado de una tabla de volúmenes de Civil 3D (título y dos de nombres de columna).</summary>
    public const int FilasEncabezadoPorDefecto = 3;

    private const double Eps = 1e-6;

    // ------------------------------------------------------------------ Armado de la tabla

    /// <summary>
    /// Medidas de la tabla con la división indicada. Con <see cref="OpcionesDivision.MaxFilasPorColumna"/> = 0 (automático)
    /// se usan las filas que entran en <paramref name="altoDisponible"/>; sin alto disponible, no se divide.
    /// </summary>
    public static DimensionesTabla Dimensiones(EstructuraTabla e, OpcionesDivision o, double margen = 0, double? altoDisponible = null)
    {
        Validar(e, o, margen);
        int n = Math.Max(0, e.FilasDatos);
        int filasPorColumna = n;
        if (o.Dividir && n > 0)
        {
            int maximo = o.MaxFilasPorColumna > 0 ? o.MaxFilasPorColumna : FilasQueEntran(e, margen, altoDisponible);
            filasPorColumna = Math.Clamp(maximo, 1, n);
        }
        return Componer(e, o, margen, filasPorColumna);
    }

    /// <summary>Filas de datos que entran en una columna de la altura disponible (al menos 1).</summary>
    public static int FilasQueEntran(EstructuraTabla e, double margen, double? altoDisponible)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (altoDisponible is null || !(e.AltoFila > 0)) return Math.Max(1, e.FilasDatos);
        double util = altoDisponible.Value - 2 * Math.Max(0, margen) - e.AltoEncabezado;
        int filas = (int)Math.Floor(util / e.AltoFila + Eps);
        return Math.Max(1, filas);
    }

    private static DimensionesTabla Componer(EstructuraTabla e, OpcionesDivision o, double margen, int filasPorColumna)
    {
        int n = Math.Max(0, e.FilasDatos);
        int filas = n == 0 ? 0 : Math.Clamp(filasPorColumna, 1, n);
        int partes = n == 0 ? 1 : (n + filas - 1) / filas;
        var (columnasGrilla, filasGrilla) = Grilla(partes, o);

        double altoParte = e.AltoEncabezado + filas * e.AltoFila;
        double espaciado = partes > 1 ? Math.Max(0, o.Espaciado) : 0;
        double ancho = columnasGrilla * e.AnchoTabla + (columnasGrilla - 1) * espaciado;
        double alto = filasGrilla * altoParte + (filasGrilla - 1) * espaciado;
        return new DimensionesTabla(ancho, alto, partes, filas, Math.Max(0, margen));
    }

    /// <summary>Cuántas partes quedan a lo ancho y a lo alto según la dirección y el máximo por pila.</summary>
    private static (int Columnas, int Filas) Grilla(int partes, OpcionesDivision o)
    {
        int porPila = o.MaxTablasPorPila > 0 ? o.MaxTablasPorPila : partes;
        int enPila = Math.Min(partes, porPila);
        int pilas = (partes + porPila - 1) / porPila;
        return o.Direccion == DireccionDivision.Horizontal ? (enPila, pilas) : (pilas, enPila);
    }

    // ------------------------------------------------------------------ Veredicto

    /// <summary>¿Entran las dimensiones demandadas (con margen) en el área disponible?</summary>
    public static bool Entra(DimensionesTabla d, double anchoDisponible, double altoDisponible)
    {
        ArgumentNullException.ThrowIfNull(d);
        return d.AnchoDemandado <= anchoDisponible + Eps && d.AltoDemandado <= altoDisponible + Eps;
    }

    /// <summary>
    /// Compara una tabla ya medida con el área. Solo dice si cabe o no (sin probar otras divisiones).
    /// </summary>
    public static CabidaTabla Evaluar(double anchoDisponible, double altoDisponible, DimensionesTabla d, string unidad = "mm")
    {
        ArgumentNullException.ThrowIfNull(d);
        ValidarArea(anchoDisponible, altoDisponible);
        bool cabe = Entra(d, anchoDisponible, altoDisponible);
        string mensaje = cabe
            ? MensajeCabe(anchoDisponible, altoDisponible, d, unidad)
            : MensajeInsuficiente(anchoDisponible, altoDisponible, d, unidad, puedeDividir: false);
        return new CabidaTabla(cabe ? EstadoCabida.CabeConHolgura : EstadoCabida.Insuficiente, anchoDisponible, altoDisponible, d, null, mensaje);
    }

    /// <summary>
    /// Decide si la tabla cabe como está configurada; si no, busca la división en columnas con más filas por columna
    /// (o sea, la de menos columnas) que sí entra; si ninguna entra, el área es insuficiente y el mensaje dice cuánto falta.
    /// </summary>
    public static CabidaTabla Evaluar(double anchoDisponible, double altoDisponible, EstructuraTabla e, OpcionesDivision o,
        double margen = 0, string unidad = "mm")
    {
        ValidarArea(anchoDisponible, altoDisponible);
        DimensionesTabla actual = Dimensiones(e, o, margen, altoDisponible);
        if (Entra(actual, anchoDisponible, altoDisponible))
            return new CabidaTabla(EstadoCabida.CabeConHolgura, anchoDisponible, altoDisponible, actual, null,
                MensajeCabe(anchoDisponible, altoDisponible, actual, unidad));

        DimensionesTabla? sugerida = MejorDivision(e, o, margen, anchoDisponible, altoDisponible);
        if (sugerida != null)
            return new CabidaTabla(EstadoCabida.CabeDividiendo, anchoDisponible, altoDisponible, actual, sugerida,
                MensajeDividiendo(anchoDisponible, altoDisponible, actual, sugerida, unidad));

        return new CabidaTabla(EstadoCabida.Insuficiente, anchoDisponible, altoDisponible, actual, null,
            MensajeInsuficiente(anchoDisponible, altoDisponible, actual, unidad, puedeDividir: e.FilasDatos > 1));
    }

    /// <summary>
    /// División que entra en el área con el mayor número de filas por columna (la de menos columnas).
    /// Mantiene la dirección, el espaciado y el máximo por pila. Null si ninguna entra.
    /// </summary>
    public static DimensionesTabla? MejorDivision(EstructuraTabla e, OpcionesDivision o, double margen, double anchoDisponible, double altoDisponible)
    {
        Validar(e, o, margen);
        var dividida = o with { Dividir = true };
        for (int filas = e.FilasDatos; filas >= 1; filas--)
        {
            DimensionesTabla d = Componer(e, dividida, margen, filas);
            if (Entra(d, anchoDisponible, altoDisponible)) return d;
        }
        return null;
    }

    // ------------------------------------------------------------------ Estimación desde la medida real

    /// <summary>
    /// Estructura aproximada de una tabla ya dibujada a partir de su extensión, las filas de datos que tiene y la
    /// división con que se creó. Supone que el encabezado ocupa <paramref name="filasEncabezado"/> filas del mismo alto.
    /// </summary>
    public static EstructuraTabla EstimarEstructura(double anchoMedido, double altoMedido, int filasDatos, OpcionesDivision usadas,
        int filasEncabezado = FilasEncabezadoPorDefecto)
    {
        ArgumentNullException.ThrowIfNull(usadas);
        int n = Math.Max(0, filasDatos);
        if (n == 0) return new EstructuraTabla(Math.Max(0, anchoMedido), Math.Max(0, altoMedido), 0, 0);

        int filas = usadas.Dividir && usadas.MaxFilasPorColumna > 0 ? Math.Min(usadas.MaxFilasPorColumna, n) : n;
        int partes = (n + filas - 1) / filas;
        var (columnasGrilla, filasGrilla) = Grilla(partes, usadas);
        double espaciado = partes > 1 ? Math.Max(0, usadas.Espaciado) : 0;

        double anchoParte = (anchoMedido - (columnasGrilla - 1) * espaciado) / columnasGrilla;
        double altoParte = (altoMedido - (filasGrilla - 1) * espaciado) / filasGrilla;
        double altoFila = altoParte / (filas + Math.Max(0, filasEncabezado));
        return new EstructuraTabla(Math.Max(0, anchoParte), Math.Max(0, filasEncabezado) * altoFila, Math.Max(0, altoFila), n);
    }

    // ------------------------------------------------------------------ Posición

    /// <summary>
    /// Esquina inferior izquierda de la tabla dentro del área (misma unidad para todo), respetando el margen.
    /// Si la tabla es más grande que el área, queda alineada igual (el llamador ya debió validar la cabida).
    /// </summary>
    public static (double X, double Y) Posicionar(double areaX, double areaY, double areaAncho, double areaAlto,
        double anchoTabla, double altoTabla, AlineacionTabla alineacion, double margen = 0)
    {
        double m = Math.Max(0, margen);
        double izquierda = areaX + m;
        double derecha = areaX + areaAncho - m - anchoTabla;
        double centroX = areaX + (areaAncho - anchoTabla) / 2;
        double abajo = areaY + m;
        double arriba = areaY + areaAlto - m - altoTabla;
        double centroY = areaY + (areaAlto - altoTabla) / 2;

        return alineacion switch
        {
            AlineacionTabla.SuperiorIzquierda => (izquierda, arriba),
            AlineacionTabla.SuperiorCentro => (centroX, arriba),
            AlineacionTabla.SuperiorDerecha => (derecha, arriba),
            AlineacionTabla.CentroIzquierda => (izquierda, centroY),
            AlineacionTabla.Centro => (centroX, centroY),
            AlineacionTabla.CentroDerecha => (derecha, centroY),
            AlineacionTabla.InferiorIzquierda => (izquierda, abajo),
            AlineacionTabla.InferiorCentro => (centroX, abajo),
            AlineacionTabla.InferiorDerecha => (derecha, abajo),
            _ => (izquierda, arriba),
        };
    }

    // ------------------------------------------------------------------ Mensajes

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static string N(double v) => v.ToString("0.00", Inv);

    private static string Medida(double ancho, double alto, string unidad) => $"{N(ancho)} × {N(alto)} {unidad}";

    private static string ConMargen(DimensionesTabla d, string unidad) =>
        d.Margen > 0 ? $" (con un margen de {N(d.Margen)} {unidad} por lado)" : string.Empty;

    private static string Division(DimensionesTabla d) =>
        d.Columnas > 1 ? $"dividida en {d.Columnas} columnas de hasta {d.FilasPorColumna} filas" : "sin dividir";

    private static string MensajeCabe(double ancho, double alto, DimensionesTabla d, string unidad) =>
        $"La tabla de {Medida(d.AnchoDemandado, d.AltoDemandado, unidad)}{ConMargen(d, unidad)} entra en el área de " +
        $"{Medida(ancho, alto, unidad)} ({Division(d)}). Holgura: {N(Math.Max(0, ancho - d.AnchoDemandado))} {unidad} en ancho y " +
        $"{N(Math.Max(0, alto - d.AltoDemandado))} {unidad} en alto.";

    private static string MensajeDividiendo(double ancho, double alto, DimensionesTabla actual, DimensionesTabla sugerida, string unidad) =>
        $"Tal como está configurada ({Division(actual)}), la tabla mide {Medida(actual.AnchoDemandado, actual.AltoDemandado, unidad)}" +
        $"{ConMargen(actual, unidad)} y no entra en el área de {Medida(ancho, alto, unidad)}. " +
        $"Dividida en {sugerida.Columnas} columnas de hasta {sugerida.FilasPorColumna} filas mediría " +
        $"{Medida(sugerida.AnchoDemandado, sugerida.AltoDemandado, unidad)} y sí entra.";

    private static string MensajeInsuficiente(double ancho, double alto, DimensionesTabla d, string unidad, bool puedeDividir)
    {
        double faltaAncho = Math.Max(0, d.AnchoDemandado - ancho);
        double faltaAlto = Math.Max(0, d.AltoDemandado - alto);
        var partes = new List<string>();
        if (faltaAncho > Eps) partes.Add($"{N(faltaAncho)} {unidad} en ancho");
        if (faltaAlto > Eps) partes.Add($"{N(faltaAlto)} {unidad} en alto");

        string alternativa;
        if (faltaAlto > Eps && faltaAncho <= Eps)
            alternativa = puedeDividir ? " o activar más columnas (menos filas por columna)" : " o reducir el alto de la tabla";
        else if (faltaAncho > Eps && faltaAlto <= Eps)
            alternativa = " o usar menos columnas (más filas por columna)";
        else
            alternativa = " o usar un estilo de tabla más compacto";

        return $"El área seleccionada de {Medida(ancho, alto, unidad)} es insuficiente para la tabla de " +
               $"{Medida(d.AnchoDemandado, d.AltoDemandado, unidad)}{ConMargen(d, unidad)}. " +
               $"Se requiere ampliar al menos {string.Join(" y ", partes)}{alternativa}.";
    }

    // ------------------------------------------------------------------ Validación de datos

    private static void Validar(EstructuraTabla e, OpcionesDivision o, double margen)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(o);
        if (e.AnchoTabla < 0 || e.AltoEncabezado < 0 || e.AltoFila < 0)
            throw new ArgumentException("Las medidas de la tabla no pueden ser negativas.", nameof(e));
        if (e.FilasDatos < 0) throw new ArgumentException("La cantidad de filas no puede ser negativa.", nameof(e));
        if (o.MaxFilasPorColumna < 0) throw new ArgumentException("El máximo de filas por columna no puede ser negativo.", nameof(o));
        if (o.Espaciado < 0) throw new ArgumentException("El espaciado entre columnas no puede ser negativo.", nameof(o));
        if (margen < 0) throw new ArgumentException("El margen no puede ser negativo.", nameof(margen));
    }

    private static void ValidarArea(double ancho, double alto)
    {
        if (!(ancho > 0) || !(alto > 0)) throw new ArgumentException("El área disponible debe tener ancho y alto mayores que cero.");
    }
}
