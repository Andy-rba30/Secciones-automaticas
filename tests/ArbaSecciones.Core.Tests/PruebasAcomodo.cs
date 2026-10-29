using ArbaSecciones.Core.Acomodo;
using ArbaSecciones.Core.Configuracion;
using ArbaSecciones.Core.Escalas;
using ArbaSecciones.Core.Tests.Marco;

namespace ArbaSecciones.Core.Tests;

/// <summary>
/// Área útil 420 × 290 mm, separaciones de 10 mm, modelo en metros.
/// Una sección de 40 m de ancho y 10 m de alto (sin anotación) mide en papel:
///   a 1:200 → 200 × 50 mm: entran 5 por columna y 2 columnas = 10 por lámina;
///   a 1:100 → 400 × 100 mm: entran 2 por columna y 1 columna = 2 por lámina.
/// </summary>
public static class PruebasAcomodo
{
    private static ConversorEscala Escala(double denominador) =>
        new(EscalaSecciones.Uniforme(denominador), UnidadLongitud.Metros, UnidadLongitud.Milimetros);

    private static ParametrosAcomodo Area(OrdenAcomodo orden = OrdenAcomodo.PorColumnas, ModoPaginacion modo = ModoPaginacion.PorKilometro, double franja = 0) => new()
    {
        AnchoUtil = 420,
        AltoUtil = 290,
        SeparacionH = 10,
        SeparacionV = 10,
        AltoFranjaCuadro = franja,
        Orden = orden,
        Paginacion = modo,
    };

    private static MedidaSeccion Seccion(double progresiva, double ancho = 40, double alto = 10, double anotAncho = 0, double anotAlto = 0) =>
        new("SL-" + progresiva, progresiva, ancho, alto, anotAncho, anotAlto);

    private static List<MedidaSeccion> Serie(double desde, double paso, int cantidad) =>
        Enumerable.Range(0, cantidad).Select(i => Seccion(desde + i * paso)).ToList();

    [Prueba]
    public static void Km_que_no_entra_se_parte_en_dos_y_el_siguiente_entra_en_una()
    {
        var secciones = Serie(0, 80, 12);          // km 0: 0+000 … 0+880 (12 secciones)
        secciones.AddRange(Serie(1000, 100, 8));   // km 1: 1+000 … 1+700 (8 secciones)

        var r = Paginador.Paginar(secciones, Escala(200), Area());

        Afirmar.Verdadero(r.Valido);
        Afirmar.Igual(3, r.Laminas.Count);

        var l1 = r.Laminas[0];
        Afirmar.Igual(0, l1.Grupo); Afirmar.Igual(1, l1.Parte); Afirmar.Igual(2, l1.PartesDelGrupo); Afirmar.Igual(10, l1.Cantidad);
        Afirmar.Cerca(0, l1.ProgresivaInicial); Afirmar.Cerca(720, l1.ProgresivaFinal);

        var l2 = r.Laminas[1];
        Afirmar.Igual(0, l2.Grupo); Afirmar.Igual(2, l2.Parte); Afirmar.Igual(2, l2.PartesDelGrupo); Afirmar.Igual(2, l2.Cantidad);
        Afirmar.Cerca(800, l2.ProgresivaInicial); Afirmar.Cerca(880, l2.ProgresivaFinal);

        var l3 = r.Laminas[2];
        Afirmar.Igual(1, l3.Grupo); Afirmar.Igual(1, l3.Parte); Afirmar.Igual(1, l3.PartesDelGrupo); Afirmar.Igual(8, l3.Cantidad);
        Afirmar.Cerca(1000, l3.ProgresivaInicial); Afirmar.Cerca(1700, l3.ProgresivaFinal);

        Afirmar.Secuencia(new[] { 1, 2, 3 }, r.Laminas.Select(l => l.Numero));
    }

    [Prueba]
    public static void Mismo_km_necesita_dos_laminas_a_1a100_y_una_a_1a200()
    {
        var km0 = Serie(0, 250, 4); // 0+000, 0+250, 0+500, 0+750

        var a100 = Paginador.Paginar(km0, Escala(100), Area());
        Afirmar.Verdadero(a100.Valido);
        Afirmar.Igual(2, a100.Laminas.Count);
        Afirmar.Secuencia(new[] { 2, 2 }, a100.Laminas.Select(l => l.Cantidad));

        var a200 = Paginador.Paginar(km0, Escala(200), Area());
        Afirmar.Verdadero(a200.Valido);
        Afirmar.Igual(1, a200.Laminas.Count);
        Afirmar.Igual(4, a200.Laminas[0].Cantidad);
    }

    [Prueba]
    public static void Modo_continuo_llena_cada_lamina_sin_cortar_por_km()
    {
        var secciones = Serie(0, 80, 12);
        secciones.AddRange(Serie(1000, 100, 8));

        var r = Paginador.Paginar(secciones, Escala(200), Area(modo: ModoPaginacion.Continuo));
        Afirmar.Igual(2, r.Laminas.Count);
        Afirmar.Secuencia(new[] { 10, 10 }, r.Laminas.Select(l => l.Cantidad));
        Afirmar.Cerca(800, r.Laminas[1].ProgresivaInicial);
        Afirmar.Cerca(1700, r.Laminas[1].ProgresivaFinal);
        Afirmar.Igual(null, r.Laminas[1].Grupo);
    }

    [Prueba]
    public static void Columnas_van_de_abajo_hacia_arriba_y_de_izquierda_a_derecha()
    {
        var r = Paginador.Paginar(Serie(0, 20, 7), Escala(200), Area());
        var c = r.Laminas[0].Colocaciones;
        // Columna 1: y = 0, 60, 120, 180, 240; la columna 2 empieza en x = 210.
        Afirmar.Cerca(0, c[0].X); Afirmar.Cerca(0, c[0].Y);
        Afirmar.Cerca(0, c[1].X); Afirmar.Cerca(60, c[1].Y);
        Afirmar.Cerca(240, c[4].Y);
        Afirmar.Cerca(210, c[5].X); Afirmar.Cerca(0, c[5].Y);
        Afirmar.Cerca(210, c[6].X); Afirmar.Cerca(60, c[6].Y);
    }

    [Prueba]
    public static void Secciones_angostas_se_centran_en_su_columna()
    {
        var secciones = new List<MedidaSeccion> { Seccion(0, ancho: 40), Seccion(20, ancho: 20) };
        var c = Paginador.Paginar(secciones, Escala(200), Area()).Laminas[0].Colocaciones;
        Afirmar.Cerca(0, c[0].X);
        Afirmar.Cerca(50, c[1].X); // columna de 200 mm, sección de 100 mm → (200 − 100) / 2
    }

    [Prueba]
    public static void Filas_van_de_izquierda_a_derecha_y_de_arriba_hacia_abajo()
    {
        var secciones = Enumerable.Range(0, 5).Select(i => Seccion(i * 20, ancho: 20)).ToList(); // 100 × 50 mm
        var c = Paginador.Paginar(secciones, Escala(200), Area(OrdenAcomodo.PorFilas)).Laminas[0].Colocaciones;
        Afirmar.Cerca(0, c[0].X); Afirmar.Cerca(240, c[0].Y);
        Afirmar.Cerca(110, c[1].X); Afirmar.Cerca(240, c[1].Y);
        Afirmar.Cerca(220, c[2].X); Afirmar.Cerca(240, c[2].Y);
        // La cuarta ya no entra (220 + 100 + 10 + 100 = 430 > 420): baja a la segunda fila.
        Afirmar.Cerca(0, c[3].X); Afirmar.Cerca(180, c[3].Y); // 290 − 50 − 10 − 50
        Afirmar.Cerca(110, c[4].X); Afirmar.Cerca(180, c[4].Y);
    }

    [Prueba]
    public static void Franja_del_cuadro_reserva_espacio_abajo()
    {
        var p = Area(franja: 40);
        var zona = p.ZonaSecciones;
        Afirmar.Cerca(50, zona.Y);
        Afirmar.Cerca(240, zona.Alto); // 290 − 40 de franja − 10 de separación
        var r = Paginador.Paginar(Serie(0, 20, 10), Escala(200), p);
        Afirmar.Cerca(50, r.Laminas[0].Colocaciones[0].Y);
        Afirmar.Igual(8, r.Laminas[0].Cantidad); // 4 por columna × 2 columnas
        Afirmar.Igual(2, r.Laminas.Count);
    }

    [Prueba]
    public static void Seccion_que_no_entra_sola_no_crea_nada_y_sugiere_escala()
    {
        var secciones = new List<MedidaSeccion> { Seccion(0), Seccion(20, ancho: 50), Seccion(40) };
        var r = Paginador.Paginar(secciones, Escala(100), Area()); // 50 m a 1:100 = 500 mm > 420 mm
        Afirmar.Verdadero(!r.Valido);
        Afirmar.Igual(0, r.Laminas.Count);
        Afirmar.Igual(1, r.NoEntran.Count);
        Afirmar.Cerca(20, r.NoEntran[0].Progresiva);
        Afirmar.Igual(125.0, r.EscalaSugerida); // a 1:125 mide 400 mm
        Afirmar.Verdadero(r.Resumen().Contains("1:125"));
        Afirmar.Verdadero(r.Resumen().Contains("0+020.00"));
    }

    [Prueba]
    public static void Proponer_escala_para_que_cada_km_entre_en_una_lamina()
    {
        var secciones = Serie(0, 80, 12);
        secciones.AddRange(Serie(1000, 100, 8));
        // 1:200 → 10 por lámina (no alcanza para 12); 1:250 → 160 × 40 mm → 6 por columna × 2 = 12.
        Afirmar.Igual(250.0, SugeridorEscala.MenorEscalaUnaLaminaPorTramo(secciones, Escala(200), Area()));
    }

    [Prueba]
    public static void Medida_separa_grafico_y_anotacion()
    {
        // Medida a 1:200: extensión 44 × 13 m; gráfico 40 × 10 m → anotación 20 × 15 mm de papel.
        var m = MedidaSeccion.DesdeExtension("A", 0, 44, 13, 40, 10, 1, Escala(200));
        Afirmar.Cerca(20, m.AnchoAnotacion, 1e-9);
        Afirmar.Cerca(15, m.AltoAnotacion, 1e-9);

        var a200 = m.EnPapel(Escala(200));
        Afirmar.Cerca(220, a200.Ancho, 1e-9); // igual a 44 m × 5
        Afirmar.Cerca(65, a200.Alto, 1e-9);   // igual a 13 m × 5

        var a100 = m.EnPapel(Escala(100));     // el gráfico se duplica; la anotación no cambia
        Afirmar.Cerca(420, a100.Ancho, 1e-9);
        Afirmar.Cerca(115, a100.Alto, 1e-9);
    }

    [Prueba]
    public static void Medida_con_exageracion_vertical()
    {
        var hv = new ConversorEscala(new EscalaSecciones(200, 100), UnidadLongitud.Metros, UnidadLongitud.Milimetros);

        // Estilo con exageración 2: alto de extensión 23 m (10 m × 2 + 3 m de anotación).
        var m = MedidaSeccion.DesdeExtension("B", 0, 44, 23, 40, 10, 2, hv);
        Afirmar.Cerca(15, m.AltoAnotacion, 1e-9);
        Afirmar.Cerca(115, m.EnPapel(hv).Alto, 1e-9); // 23 m × 5

        // El estilo todavía dibuja con exageración 1: se respeta lo dibujado.
        var sinExagerar = MedidaSeccion.DesdeExtension("C", 0, 44, 13, 40, 10, 1, hv);
        Afirmar.Cerca(65, sinExagerar.EnPapel(hv).Alto, 1e-9); // 13 m × 5
    }

    [Prueba]
    public static void Grupo_de_km()
    {
        Afirmar.Igual(0, Paginador.Grupo(999.99, 1000));
        Afirmar.Igual(1, Paginador.Grupo(1000, 1000));
        Afirmar.Igual(1, Paginador.Grupo(999.9999999, 1000)); // ruido numérico de 1+000
        Afirmar.Igual(2, Paginador.Grupo(2500, 1000));
        Afirmar.Igual(5, Paginador.Grupo(2500, 500));
    }

    [Prueba]
    public static void Area_no_definida_se_rechaza()
    {
        Afirmar.Lanza<ArgumentException>(() => Paginador.Paginar(Serie(0, 20, 3), Escala(200), new ParametrosAcomodo()));
        Afirmar.Lanza<ArgumentException>(() => Paginador.Paginar(Serie(0, 20, 3), Escala(200), Area(franja: 300)));
    }

    [Prueba]
    public static void Datos_del_membrete_por_lamina()
    {
        var secciones = Serie(0, 80, 12);
        secciones.AddRange(Serie(1000, 100, 8));
        var r = Paginador.Paginar(secciones, Escala(200), Area());
        var cfg = new ConfigPresentaciones { Prefijo = "SEC-", Digitos = 2, CorrelativoInicial = 1, PatronCodigo = "PL-SEC-{n:000}" };

        var v = DatosLamina.Valores(r.Laminas[1], r.Laminas.Count, cfg, EscalaSecciones.Uniforme(200));
        Afirmar.Igual("PL-SEC-002", v["codigo"]);
        Afirmar.Igual("SEC-02", v["nombre"]);
        Afirmar.Igual("0+800.00", v["pi"]);
        Afirmar.Igual("0+880.00", v["pf"]);
        Afirmar.Igual("0+000 - 1+000", v["tramo"]);
        Afirmar.Igual("1:200", v["escala"]);
        Afirmar.Igual("2/3", Comun.PlantillaTexto.Aplicar("{n}/{total}", v));

        var tercera = DatosLamina.Valores(r.Laminas[2], r.Laminas.Count, cfg, EscalaSecciones.Uniforme(200));
        Afirmar.Igual("1+000 - 2+000", tercera["tramo"]);
        Afirmar.Igual("SEC_01", DatosLamina.LimpiarNombre("SEC/01"));

        // Los mismos datos leídos desde el marco guardado en el dibujo.
        var info = InfoLamina.Desde(r.Laminas[1]);
        var desdeMarco = DatosLamina.Valores(info, r.Laminas.Count, cfg, "1:200");
        Afirmar.Igual(v["codigo"], desdeMarco["codigo"]);
        Afirmar.Igual(v["tramo"], desdeMarco["tramo"]);
    }

    [Prueba]
    public static void Marcos_en_filas_de_diez_laminas()
    {
        var (x0, y0) = MarcosModelo.Origen(0, 156, 104, 20);
        Afirmar.Cerca(0, x0); Afirmar.Cerca(0, y0);
        var (x3, y3) = MarcosModelo.Origen(3, 156, 104, 20);
        Afirmar.Cerca(528, x3); Afirmar.Cerca(0, y3);
        var (x10, y10) = MarcosModelo.Origen(10, 156, 104, 20);
        Afirmar.Cerca(0, x10); Afirmar.Cerca(-124, y10);
    }
}
