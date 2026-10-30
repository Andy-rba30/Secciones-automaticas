using ArbaSecciones.Core.Configuracion;
using ArbaSecciones.Core.Tablas;
using ArbaSecciones.Core.Tests.Marco;

namespace ArbaSecciones.Core.Tests;

/// <summary>
/// Tabla de ejemplo (en mm de papel): 100 mm de ancho, encabezado de 15 mm (3 filas de 5 mm) y filas de 5 mm.
/// Con 40 progresivas mide sin dividir 100 × 215 mm.
/// </summary>
public static class PruebasTablas
{
    private static EstructuraTabla Tabla(int filas = 40, double ancho = 100) => new(ancho, 15, 5, filas);

    private static OpcionesDivision Fija(int filas, DireccionDivision direccion = DireccionDivision.Horizontal, int porPila = 0) => new()
    {
        Dividir = true,
        MaxFilasPorColumna = filas,
        Espaciado = 10,
        Direccion = direccion,
        MaxTablasPorPila = porPila,
    };

    [Prueba]
    public static void Tabla_que_entra_en_el_area_con_holgura()
    {
        var c = ValidadorCabidaTabla.Evaluar(150, 250, Tabla(), OpcionesDivision.SinDividir, margen: 2);

        Afirmar.Igual(EstadoCabida.CabeConHolgura, c.Estado);
        Afirmar.Verdadero(c.Cabe);
        Afirmar.Igual(1, c.Dimensiones.Columnas);
        Afirmar.Cerca(104, c.Dimensiones.AnchoDemandado);
        Afirmar.Cerca(219, c.Dimensiones.AltoDemandado);
        Afirmar.Cerca(104 * 219, c.Dimensiones.AreaDemandada);
        Afirmar.Cerca(46, c.HolguraAncho);
        Afirmar.Cerca(31, c.HolguraAlto);
        Afirmar.Verdadero(c.Mensaje.Contains("entra en el área de 150.00 × 250.00 mm"), c.Mensaje);
    }

    [Prueba]
    public static void Tabla_justo_al_borde_entra_y_un_poco_menos_no()
    {
        var medida = DimensionesTabla.DesdeMedida(100, 215, margen: 2);
        Afirmar.Verdadero(ValidadorCabidaTabla.Evaluar(104, 219, medida).Cabe);
        Afirmar.Verdadero(!ValidadorCabidaTabla.Evaluar(103.9, 219, medida).Cabe);
        Afirmar.Verdadero(!ValidadorCabidaTabla.Evaluar(104, 218.9, medida).Cabe);
    }

    [Prueba]
    public static void Division_automatica_calcula_las_columnas_con_el_alto_disponible()
    {
        var auto = new OpcionesDivision { Dividir = true, MaxFilasPorColumna = 0, Espaciado = 10 };

        // Alto útil 150 − 15 de encabezado = 135 → 27 filas por columna → 2 columnas.
        Afirmar.Igual(27, ValidadorCabidaTabla.FilasQueEntran(Tabla(), 0, 150));
        var d = ValidadorCabidaTabla.Dimensiones(Tabla(), auto, 0, 150);
        Afirmar.Igual(27, d.FilasPorColumna);
        Afirmar.Igual(2, d.Columnas);
        Afirmar.Cerca(210, d.AnchoTotal); // 2 × 100 + 10 de espaciado
        Afirmar.Cerca(150, d.AltoTotal);  // 15 + 27 × 5

        var c = ValidadorCabidaTabla.Evaluar(330, 150, Tabla(), auto);
        Afirmar.Igual(EstadoCabida.CabeConHolgura, c.Estado);
        Afirmar.Verdadero(c.Mensaje.Contains("dividida en 2 columnas de hasta 27 filas"), c.Mensaje);
    }

    [Prueba]
    public static void Division_fija_horizontal_vertical_y_por_pilas()
    {
        var h = ValidadorCabidaTabla.Dimensiones(Tabla(), Fija(25));
        Afirmar.Igual(2, h.Columnas);
        Afirmar.Igual(25, h.FilasPorColumna);
        Afirmar.Cerca(210, h.AnchoTotal);
        Afirmar.Cerca(140, h.AltoTotal); // 15 + 25 × 5

        var v = ValidadorCabidaTabla.Dimensiones(Tabla(), Fija(25, DireccionDivision.Vertical));
        Afirmar.Cerca(100, v.AnchoTotal);
        Afirmar.Cerca(290, v.AltoTotal); // 2 × 140 + 10

        // 100 filas en partes de 25 = 4 partes, de a 2 por pila: 2 × 2.
        var p = ValidadorCabidaTabla.Dimensiones(Tabla(100), Fija(25, porPila: 2));
        Afirmar.Igual(4, p.Columnas);
        Afirmar.Cerca(210, p.AnchoTotal);
        Afirmar.Cerca(290, p.AltoTotal);

        // Sin dividir, el máximo no se aplica.
        var s = ValidadorCabidaTabla.Dimensiones(Tabla(), Fija(25) with { Dividir = false });
        Afirmar.Igual(1, s.Columnas);
        Afirmar.Cerca(215, s.AltoTotal);
    }

    [Prueba]
    public static void Sugiere_dividir_en_columnas_cuando_sin_dividir_no_entra()
    {
        var sinDividir = OpcionesDivision.SinDividir with { Espaciado = 10 };
        var c = ValidadorCabidaTabla.Evaluar(330, 150, Tabla(), sinDividir);

        Afirmar.Igual(EstadoCabida.CabeDividiendo, c.Estado);
        Afirmar.Verdadero(!c.Cabe);
        Afirmar.Cerca(215, c.Dimensiones.AltoTotal);
        Afirmar.Verdadero(c.Sugerida != null);
        Afirmar.Igual(27, c.Sugerida!.FilasPorColumna); // la de menos columnas que entra
        Afirmar.Igual(2, c.Sugerida.Columnas);
        Afirmar.Cerca(210, c.Sugerida.AnchoTotal);
        Afirmar.Cerca(150, c.Sugerida.AltoTotal);
        Afirmar.Verdadero(c.Mensaje.Contains("Dividida en 2 columnas de hasta 27 filas"), c.Mensaje);
    }

    [Prueba]
    public static void Reporte_de_area_insuficiente_con_las_medidas_que_faltan()
    {
        var c = ValidadorCabidaTabla.Evaluar(200, 140, DimensionesTabla.DesdeMedida(320, 195));

        Afirmar.Igual(EstadoCabida.Insuficiente, c.Estado);
        Afirmar.Cerca(120, c.FaltaAncho);
        Afirmar.Cerca(55, c.FaltaAlto);
        Afirmar.Verdadero(c.Mensaje.StartsWith("El área seleccionada de 200.00 × 140.00 mm es insuficiente para la tabla de 320.00 × 195.00 mm."), c.Mensaje);
        Afirmar.Verdadero(c.Mensaje.Contains("Se requiere ampliar al menos 120.00 mm en ancho y 55.00 mm en alto"), c.Mensaje);
    }

    [Prueba]
    public static void Insuficiente_aun_dividiendo_dice_que_falta()
    {
        // Una sola columna ya es más ancha que el área: ninguna división sirve.
        var ancha = ValidadorCabidaTabla.Evaluar(200, 100, Tabla(ancho: 300), Fija(10), unidad: "mm");
        Afirmar.Igual(EstadoCabida.Insuficiente, ancha.Estado);
        Afirmar.Verdadero(ancha.Sugerida == null);
        Afirmar.Verdadero(ancha.FaltaAncho > 0);
        Afirmar.Verdadero(ancha.Mensaje.Contains("mm en ancho"), ancha.Mensaje);

        // Solo falta alto y ninguna división entra en el ancho: el mensaje da el alto que falta y la alternativa de más columnas.
        var alta = ValidadorCabidaTabla.Evaluar(500, 20, Tabla(), Fija(40), margen: 0);
        Afirmar.Igual(EstadoCabida.Insuficiente, alta.Estado);
        Afirmar.Verdadero(alta.Mensaje.Contains("en alto") && alta.Mensaje.Contains("activar más columnas"), alta.Mensaje);

        // Con el margen el mensaje lo aclara.
        var conMargen = ValidadorCabidaTabla.Evaluar(100, 100, DimensionesTabla.DesdeMedida(100, 50, margen: 5));
        Afirmar.Verdadero(conMargen.Mensaje.Contains("110.00 × 60.00 mm (con un margen de 5.00 mm por lado)"), conMargen.Mensaje);
    }

    [Prueba]
    public static void Estima_la_estructura_desde_la_tabla_medida()
    {
        var sin = ValidadorCabidaTabla.Dimensiones(Tabla(), OpcionesDivision.SinDividir);
        var e1 = ValidadorCabidaTabla.EstimarEstructura(sin.AnchoTotal, sin.AltoTotal, 40, OpcionesDivision.SinDividir);
        Afirmar.Cerca(100, e1.AnchoTabla);
        Afirmar.Cerca(5, e1.AltoFila);
        Afirmar.Cerca(15, e1.AltoEncabezado);

        var dividida = ValidadorCabidaTabla.Dimensiones(Tabla(), Fija(25));
        var e2 = ValidadorCabidaTabla.EstimarEstructura(dividida.AnchoTotal, dividida.AltoTotal, 40, Fija(25));
        Afirmar.Cerca(100, e2.AnchoTabla);
        Afirmar.Cerca(5, e2.AltoFila);

        // Con la estructura estimada se puede volver a armar la tabla con otra división.
        var otra = ValidadorCabidaTabla.Dimensiones(e2, Fija(10));
        Afirmar.Igual(4, otra.Columnas);
        Afirmar.Cerca(65, otra.AltoTotal); // 15 + 10 × 5
    }

    [Prueba]
    public static void Posiciona_la_tabla_segun_la_alineacion()
    {
        // Área (10, 20) de 200 × 100, tabla de 50 × 30, margen 5.
        (double X, double Y) P(AlineacionTabla a) => ValidadorCabidaTabla.Posicionar(10, 20, 200, 100, 50, 30, a, 5);

        Afirmar.Cerca(15, P(AlineacionTabla.SuperiorIzquierda).X);
        Afirmar.Cerca(85, P(AlineacionTabla.SuperiorIzquierda).Y);
        Afirmar.Cerca(85, P(AlineacionTabla.Centro).X);
        Afirmar.Cerca(55, P(AlineacionTabla.Centro).Y);
        Afirmar.Cerca(155, P(AlineacionTabla.InferiorDerecha).X);
        Afirmar.Cerca(25, P(AlineacionTabla.InferiorDerecha).Y);
        Afirmar.Cerca(155, P(AlineacionTabla.CentroDerecha).X);
        Afirmar.Cerca(85, P(AlineacionTabla.InferiorCentro).X);
    }

    [Prueba]
    public static void Rechaza_datos_invalidos()
    {
        Afirmar.Lanza<ArgumentException>(() => ValidadorCabidaTabla.Dimensiones(new EstructuraTabla(-1, 15, 5, 10), OpcionesDivision.SinDividir));
        Afirmar.Lanza<ArgumentException>(() => ValidadorCabidaTabla.Dimensiones(Tabla(), Fija(-3)));
        Afirmar.Lanza<ArgumentException>(() => ValidadorCabidaTabla.Evaluar(0, 100, Tabla(), OpcionesDivision.SinDividir));
        Afirmar.Lanza<ArgumentException>(() => ValidadorCabidaTabla.Evaluar(100, 100, Tabla(), OpcionesDivision.SinDividir, margen: -1));
    }

    [Prueba]
    public static void Configuracion_de_la_tabla_por_defecto_y_en_json()
    {
        var cfg = new ConfiguracionArba();
        ConfigTablaVolumen t = cfg.Materiales.TablaVolumen;
        Afirmar.Igual(TipoTablaVolumen.Estandar, t.Tipo); // la tabla de siempre sigue siendo la opción por defecto
        Afirmar.Verdadero(t.Dividir);
        Afirmar.Igual(25, t.MaxFilasPorColumna);
        Afirmar.Verdadero(t.Area == null);

        t.Tipo = TipoTablaVolumen.DinamicaNativa;
        t.Clase = ClaseTablaVolumen.Material;
        t.Alineacion = AlineacionTabla.Centro;
        t.Area = AreaTabla.DesdeEsquinas(100, 50, 20, 10, enModelo: false, presentacion: "SEC-01");

        string json = AlmacenConfiguracion.Serializar(cfg);
        Afirmar.Verdadero(json.Contains("\"DinamicaNativa\""), "Los enumerados se guardan como texto.");
        var leida = AlmacenConfiguracion.Deserializar(json).Materiales.TablaVolumen;
        Afirmar.Igual(TipoTablaVolumen.DinamicaNativa, leida.Tipo);
        Afirmar.Igual(ClaseTablaVolumen.Material, leida.Clase);
        Afirmar.Igual(AlineacionTabla.Centro, leida.Alineacion);
        Afirmar.Verdadero(leida.Area != null && leida.Area.Definida);
        Afirmar.Cerca(20, leida.Area!.MinX);
        Afirmar.Cerca(80, leida.Area.Ancho);
        Afirmar.Cerca(40, leida.Area.Alto);
        Afirmar.Igual("SEC-01", leida.Area.Presentacion);

        // Un JSON de la versión 1.4 (sin la sección) se lee con la tabla estándar.
        var vieja = AlmacenConfiguracion.Deserializar("{ \"Materiales\": { \"NombreLista\": \"Movimiento\" } }");
        Afirmar.Igual(TipoTablaVolumen.Estandar, vieja.Materiales.TablaVolumen.Tipo);

        // Un área en una presentación sin nombre no sirve: se descarta al normalizar.
        cfg.Materiales.TablaVolumen.Area = AreaTabla.DesdeEsquinas(0, 0, 10, 10, enModelo: false, presentacion: null);
        Afirmar.Verdadero(AlmacenConfiguracion.Deserializar(AlmacenConfiguracion.Serializar(cfg)).Materiales.TablaVolumen.Area == null);
    }
}
