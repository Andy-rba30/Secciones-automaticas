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
    public static void Una_sola_seccion_queda_centrada_en_el_area_util()
    {
        var r = Paginador.Paginar(new List<MedidaSeccion> { Seccion(0) }, Escala(200), Area());
        var c = r.Laminas[0].Colocaciones[0];
        // Sección de 200 × 50 mm en área útil de 420 × 290 mm → MargenX = (420 − 200) / 2 = 110, MargenY = (290 − 50) / 2 = 120.
        Afirmar.Cerca(110, c.X);
        Afirmar.Cerca(120, c.Y);
    }

    [Prueba]
    public static void Columnas_van_de_abajo_hacia_arriba_y_de_izquierda_a_derecha()
    {
        var r = Paginador.Paginar(Serie(0, 20, 7), Escala(200), Area());
        var c = r.Laminas[0].Colocaciones;
        // 2 columnas de 200 mm + 10 mm separación = 410 mm → MargenX = (420 − 410) / 2 = 5.
        // Columna 1 (5 secciones, alto 290 mm): y = 0, 60, 120, 180, 240; x = 5.
        // Columna 2 (2 secciones, alto 110 mm): MargenY = (290 − 110) / 2 = 90 → y = 90, 150; x = 215.
        Afirmar.Cerca(5, c[0].X); Afirmar.Cerca(0, c[0].Y);
        Afirmar.Cerca(5, c[1].X); Afirmar.Cerca(60, c[1].Y);
        Afirmar.Cerca(240, c[4].Y);
        Afirmar.Cerca(215, c[5].X); Afirmar.Cerca(90, c[5].Y);
        Afirmar.Cerca(215, c[6].X); Afirmar.Cerca(150, c[6].Y);
    }

    [Prueba]
    public static void Secciones_angostas_se_centran_en_su_columna()
    {
        var secciones = new List<MedidaSeccion> { Seccion(0, ancho: 40), Seccion(20, ancho: 20) };
        var c = Paginador.Paginar(secciones, Escala(200), Area()).Laminas[0].Colocaciones;
        // 1 columna de 200 mm centrada en 420 mm → MargenX = (420 − 200) / 2 = 110.
        // Sección angosta de 100 mm centrada dentro de la columna de 200 mm → 110 + (200 − 100) / 2 = 160.
        Afirmar.Cerca(110, c[0].X);
        Afirmar.Cerca(160, c[1].X);
    }

    [Prueba]
    public static void Filas_van_de_izquierda_a_derecha_y_de_arriba_hacia_abajo()
    {
        var secciones = Enumerable.Range(0, 5).Select(i => Seccion(i * 20, ancho: 20)).ToList(); // 100 × 50 mm
        var c = Paginador.Paginar(secciones, Escala(200), Area(OrdenAcomodo.PorFilas)).Laminas[0].Colocaciones;
        // 2 filas de 50 mm + 10 mm = 110 mm → MargenY = (290 − 110) / 2 = 90.
        // Fila 1 (3 secciones, ancho 320 mm): MargenX = (420 − 320) / 2 = 50; y = 290 − 90 − 50 = 150.
        Afirmar.Cerca(50, c[0].X); Afirmar.Cerca(150, c[0].Y);
        Afirmar.Cerca(160, c[1].X); Afirmar.Cerca(150, c[1].Y);
        Afirmar.Cerca(270, c[2].X); Afirmar.Cerca(150, c[2].Y);
        // Fila 2 (2 secciones, ancho 210 mm): MargenX = (420 − 210) / 2 = 105; y = 290 − 90 − 60 − 50 = 90.
        Afirmar.Cerca(105, c[3].X); Afirmar.Cerca(90, c[3].Y);
        Afirmar.Cerca(215, c[4].X); Afirmar.Cerca(90, c[4].Y);
    }

    [Prueba]
    public static void Franja_del_cuadro_reserva_espacio_abajo()
    {
        var p = Area(franja: 40);
        var zona = p.ZonaSecciones;
        Afirmar.Cerca(50, zona.Y);
        Afirmar.Cerca(240, zona.Alto); // 290 − 40 de franja − 10 de separación
        var r = Paginador.Paginar(Serie(0, 20, 10), Escala(200), p);
        // 4 por columna = 230 mm en zona de 240 mm → MargenY = (240 − 230) / 2 = 5 → Y = 50 + 5 = 55.
        Afirmar.Cerca(55, r.Laminas[0].Colocaciones[0].Y);
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

    // ------------------------------------------------------------------ Uniformización de alturas

    private static ParametrosAcomodo AreaUniforme(bool todoElTramo = false) => new()
    {
        AnchoUtil = 420,
        AltoUtil = 290,
        SeparacionH = 10,
        SeparacionV = 10,
        UniformarAlturas = true,
        UniformarTodoElTramo = todoElTramo,
    };

    [Prueba]
    public static void Altura_uniforme_es_la_mayor_demanda_alineada_a_la_rejilla()
    {
        // A: 100–115 (15 m). B: 102–130 (28 m, alineada 100–130 = 30). C: 101.3–118.9 (alineada 100–120 = 20).
        var rangos = new[] { (100.0, 115.0), (102.0, 130.0), (101.3, 118.9) };
        Afirmar.Cerca(30, UniformadorAlturas.AlturaUniforme(rangos, 5));
        Afirmar.Cerca(20, UniformadorAlturas.AlturaAlineada(101.3, 118.9, 5));
        Afirmar.Cerca(28, UniformadorAlturas.AlturaUniforme(rangos, 0)); // sin redondeo: el desnivel mayor
        Afirmar.Cerca(0, UniformadorAlturas.AlturaUniforme(Array.Empty<(double, double)>(), 5));
    }

    [Prueba]
    public static void Rango_fijado_contiene_siempre_la_demanda_y_cae_en_la_rejilla()
    {
        var rangos = new List<(double, double)>();
        var azar = new Random(1234);
        for (int i = 0; i < 200; i++)
        {
            double min = 80 + azar.NextDouble() * 60;
            rangos.Add((min, min + azar.NextDouble() * 35));
        }

        foreach (double g in new[] { 5.0, 2.5, 1.0, 0.0 })
        {
            double h = UniformadorAlturas.AlturaUniforme(rangos, g);
            foreach (var (min, max) in rangos)
            {
                var (fMin, fMax) = UniformadorAlturas.RangoFijado(min, max, h, g);
                Afirmar.Verdadero(fMin <= min + 1e-9, $"piso {fMin} > mínimo {min} (g={g})");
                Afirmar.Verdadero(fMax >= max - 1e-9, $"techo {fMax} < máximo {max} (g={g})");
                Afirmar.Cerca(h, fMax - fMin, 1e-9);
                if (g > 0) Afirmar.Cerca(0, Math.IEEERemainder(fMin, g), 1e-9, $"el piso {fMin} no está en la rejilla de {g}");
            }
        }
    }

    [Prueba]
    public static void Rango_fijado_queda_centrado_cuando_la_rejilla_lo_permite()
    {
        // Demanda 100–115 con altura 30: centro 107.5 → piso 90, techo 120 (15 m de aire a cada lado… alineado a 5 m).
        var (min, max) = UniformadorAlturas.RangoFijado(100, 115, 30, 5);
        Afirmar.Cerca(90, min);
        Afirmar.Cerca(120, max);
        // La demanda más alta define la altura y queda exacta.
        var (m2, x2) = UniformadorAlturas.RangoFijado(102, 130, 30, 5);
        Afirmar.Cerca(100, m2);
        Afirmar.Cerca(130, x2);
        Afirmar.Lanza<ArgumentOutOfRangeException>(() => UniformadorAlturas.RangoFijado(100, 130, 20, 5));
    }

    [Prueba]
    public static void Paginador_uniformiza_alto_por_lamina_y_alinea_filas()
    {
        // Altos de 10, 14, 12, 10, 14, 12 m a 1:200 → 50, 70, 60, 50, 70, 60 mm.
        double[] altos = { 10, 14, 12, 10, 14, 12 };
        var secciones = altos.Select((a, i) => Seccion(i * 20, alto: a)).ToList();

        var sin = Paginador.Paginar(secciones, Escala(200), Area());
        Afirmar.Verdadero(!sin.Laminas[0].AlturasUniformes);

        var r = Paginador.Paginar(secciones, Escala(200), AreaUniforme());
        Afirmar.Verdadero(r.Valido);
        Afirmar.Igual(1, r.Laminas.Count);
        var l = r.Laminas[0];
        Afirmar.Igual(6, l.Cantidad);
        Afirmar.Verdadero(l.AlturasUniformes);
        foreach (var c in l.Colocaciones) Afirmar.Cerca(70, c.Alto);
        Afirmar.Cerca(70, l.AltoMaximo);

        // Dos columnas de 3: las filas coinciden (misma Y) y las cajas no se solapan.
        for (int i = 0; i < 3; i++)
            Afirmar.Cerca(l.Colocaciones[i].Y, l.Colocaciones[i + 3].Y);
        Afirmar.Cerca(l.Colocaciones[0].Y + 70 + 10, l.Colocaciones[1].Y);
    }

    [Prueba]
    public static void Paginador_sin_la_opcion_no_cambia_los_altos()
    {
        var secciones = new List<MedidaSeccion> { Seccion(0, alto: 10), Seccion(20, alto: 14) };
        var r = Paginador.Paginar(secciones, Escala(200), Area());
        Afirmar.Cerca(50, r.Laminas[0].Colocaciones[0].Alto);
        Afirmar.Cerca(70, r.Laminas[0].Colocaciones[1].Alto);
        Afirmar.Verdadero(!r.Laminas[0].AlturasUniformes);
    }

    [Prueba]
    public static void Uniformizar_pasa_a_la_lamina_siguiente_lo_que_ya_no_cabe()
    {
        // Ancho 45 m → 225 mm: no caben dos columnas. Altos 50, 75 y 100 mm.
        var secciones = new List<MedidaSeccion>
        {
            Seccion(0, ancho: 45, alto: 10),
            Seccion(20, ancho: 45, alto: 15),
            Seccion(40, ancho: 45, alto: 20),
        };

        var sin = Paginador.Paginar(secciones, Escala(200), Area());
        Afirmar.Igual(1, sin.Laminas.Count); // 50 + 10 + 75 + 10 + 100 = 245 ≤ 290

        var r = Paginador.Paginar(secciones, Escala(200), AreaUniforme());
        Afirmar.Verdadero(r.Valido);
        Afirmar.Igual(2, r.Laminas.Count);
        Afirmar.Igual(2, r.Laminas[0].Cantidad);
        Afirmar.Verdadero(r.Laminas[0].AlturasUniformes);
        Afirmar.Cerca(75, r.Laminas[0].AltoMaximo);   // cada lámina con su propia envolvente
        Afirmar.Igual(1, r.Laminas[1].Cantidad);
        Afirmar.Cerca(100, r.Laminas[1].AltoMaximo);
        Afirmar.Cerca(40, r.Laminas[1].ProgresivaInicial);
    }

    [Prueba]
    public static void Uniformizar_todo_el_tramo_usa_la_misma_altura_en_todas_las_laminas()
    {
        var secciones = new List<MedidaSeccion>
        {
            Seccion(0, ancho: 45, alto: 10),
            Seccion(20, ancho: 45, alto: 15),
            Seccion(40, ancho: 45, alto: 20),
        };

        var r = Paginador.Paginar(secciones, Escala(200), AreaUniforme(todoElTramo: true));
        Afirmar.Verdadero(r.Valido);
        Afirmar.Igual(2, r.Laminas.Count);
        foreach (var lamina in r.Laminas)
            foreach (var c in lamina.Colocaciones)
                Afirmar.Cerca(100, c.Alto);
    }

    [Prueba]
    public static void Configuracion_uniformiza_por_lamina_por_defecto_y_lo_pasa_al_acomodo()
    {
        var cfg = new ConfiguracionArba();
        Afirmar.Verdadero(cfg.Acomodo.UniformarAlturasPorLamina);
        Afirmar.Cerca(5, cfg.Acomodo.IntervaloRejillaElevacion);
        cfg.Membrete.AreaMinX = 0; cfg.Membrete.AreaMaxX = 420;
        cfg.Membrete.AreaMinY = 0; cfg.Membrete.AreaMaxY = 290;

        var p = cfg.ParametrosAcomodo();
        Afirmar.Verdadero(p.UniformarAlturas);
        Afirmar.Verdadero(!p.UniformarTodoElTramo);

        cfg.Acomodo.UniformarTodoElTramo = true;
        Afirmar.Verdadero(cfg.ParametrosAcomodo().UniformarTodoElTramo);

        cfg.Acomodo.UniformarAlturasPorLamina = false; // sin la opción principal no se uniformiza nada
        var sin = cfg.ParametrosAcomodo();
        Afirmar.Verdadero(!sin.UniformarAlturas);
        Afirmar.Verdadero(!sin.UniformarTodoElTramo);
    }
}
