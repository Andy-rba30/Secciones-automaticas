using ArbaSecciones.Core.Comun;
using ArbaSecciones.Core.Progresivas;
using ArbaSecciones.Core.Tests.Marco;

namespace ArbaSecciones.Core.Tests;

public static class PruebasProgresivas
{
    // Tangente 0–105, curva circular 105–163.5, tangente 163.5–300.
    private static readonly TramoAlineamiento[] Simple =
    {
        new(0, 105, TipoTramo.Tangente),
        new(105, 163.5, TipoTramo.Curva),
        new(163.5, 300, TipoTramo.Tangente),
    };

    // Tangente 0–50, espiral 50–90, curva 90–130, espiral 130–170, tangente 170–250.
    private static readonly TramoAlineamiento[] ConEspirales =
    {
        new(0, 50, TipoTramo.Tangente),
        new(50, 90, TipoTramo.Espiral),
        new(90, 130, TipoTramo.Curva),
        new(130, 170, TipoTramo.Espiral),
        new(170, 250, TipoTramo.Tangente),
    };

    private static List<double> Valores(ResultadoProgresivas r) => r.Progresivas.Select(p => p.Progresiva).ToList();

    private static TipoProgresiva TipoEn(ResultadoProgresivas r, double s) =>
        r.Progresivas.Single(p => Math.Abs(p.Progresiva - s) < 1e-6).Tipo;

    [Prueba]
    public static void Tangentes_cada_20_y_curvas_cada_10_en_multiplos_redondos()
    {
        var r = GeneradorProgresivas.Generar(Simple, new OpcionesMuestreo { IntervaloTangente = 20, IntervaloCurva = 10 });
        Afirmar.Secuencia(new double[]
        {
            0, 20, 40, 60, 80, 100, 105, 110, 120, 130, 140, 150, 160, 163.5,
            180, 200, 220, 240, 260, 280, 300,
        }, Valores(r));
        Afirmar.Igual(TipoProgresiva.Inicio, TipoEn(r, 0));
        Afirmar.Igual(TipoProgresiva.PC, TipoEn(r, 105));
        Afirmar.Igual(TipoProgresiva.PT, TipoEn(r, 163.5));
        Afirmar.Igual(TipoProgresiva.Fin, TipoEn(r, 300));
        Afirmar.Igual(TipoTramo.Curva, r.Progresivas.Single(p => p.Progresiva == 130).Tramo);
        Afirmar.Igual(TipoTramo.Tangente, r.Progresivas.Single(p => p.Progresiva == 200).Tramo);
    }

    [Prueba]
    public static void Espirales_usan_el_intervalo_de_curva_y_marcan_TS_SC_CS_ST()
    {
        var r = GeneradorProgresivas.Generar(ConEspirales, new OpcionesMuestreo { IntervaloTangente = 20, IntervaloCurva = 10 });
        Afirmar.Secuencia(new double[]
        {
            0, 20, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150, 160, 170, 180, 200, 220, 240, 250,
        }, Valores(r));
        Afirmar.Igual(TipoProgresiva.TS, TipoEn(r, 50));
        Afirmar.Igual(TipoProgresiva.SC, TipoEn(r, 90));
        Afirmar.Igual(TipoProgresiva.CS, TipoEn(r, 130));
        Afirmar.Igual(TipoProgresiva.ST, TipoEn(r, 170));
    }

    [Prueba]
    public static void Punto_notable_desactivado_no_se_agrega()
    {
        var op = new OpcionesMuestreo { IntervaloTangente = 20, IntervaloCurva = 10, IncluirSC = false, IncluirPC = false };
        var r = GeneradorProgresivas.Generar(ConEspirales, op);
        Afirmar.Igual(TipoProgresiva.Regular, TipoEn(r, 90)); // 90 es múltiplo de 10: queda como regular

        var r2 = GeneradorProgresivas.Generar(Simple, op);
        Afirmar.Verdadero(!Valores(r2).Contains(105), "105 no es múltiplo y el PC está desactivado.");
        Afirmar.Verdadero(Valores(r2).Contains(163.5), "El PT sigue activo.");
    }

    [Prueba]
    public static void Modo_desde_inicio_de_cada_elemento()
    {
        var op = new OpcionesMuestreo { IntervaloTangente = 20, IntervaloCurva = 10, Modo = ModoProgresivas.DesdeInicioElemento };
        var v = Valores(GeneradorProgresivas.Generar(Simple, op));
        foreach (double s in new[] { 105, 115, 155, 163.5, 183.5, 283.5 })
            Afirmar.Verdadero(v.Contains(s), $"Falta {s}.");
        Afirmar.Verdadero(!v.Contains(110), "110 es un múltiplo redondo, no medido desde el inicio de la curva.");
        Afirmar.Verdadero(!v.Contains(180), "180 es un múltiplo redondo, no medido desde el PT.");
    }

    [Prueba]
    public static void Rango_parcial_respeta_inicio_y_fin()
    {
        var op = new OpcionesMuestreo { IntervaloTangente = 20, IntervaloCurva = 10, ProgresivaInicial = 50, ProgresivaFinal = 150 };
        var r = GeneradorProgresivas.Generar(Simple, op);
        Afirmar.Cerca(50, r.Progresivas[0].Progresiva);
        Afirmar.Igual(TipoProgresiva.Inicio, r.Progresivas[0].Tipo);
        Afirmar.Cerca(150, r.Progresivas[^1].Progresiva);
        Afirmar.Igual(TipoProgresiva.Fin, r.Progresivas[^1].Tipo);
        Afirmar.Verdadero(!Valores(r).Contains(163.5), "El PT queda fuera del rango.");
        Afirmar.Secuencia(new double[] { 50, 60, 80, 100, 105, 110, 120, 130, 140, 150 }, Valores(r));
    }

    [Prueba]
    public static void Rango_fuera_del_alineamiento_se_recorta_con_aviso()
    {
        var r = GeneradorProgresivas.Generar(Simple, new OpcionesMuestreo { ProgresivaInicial = -50, ProgresivaFinal = 400 });
        Afirmar.Cerca(0, r.Inicio);
        Afirmar.Cerca(300, r.Fin);
        Afirmar.Igual(2, r.Avisos.Count);
    }

    [Prueba]
    public static void Duplicados_dentro_de_la_tolerancia_se_fusionan()
    {
        var op = new OpcionesMuestreo { Tolerancia = 0.01, Adicionales = { 100.005, 100.05, 400 } };
        var r = GeneradorProgresivas.Generar(Simple, op);
        var cerca100 = r.Progresivas.Where(p => p.Progresiva > 99 && p.Progresiva < 101).ToList();
        Afirmar.Igual(2, cerca100.Count, "100.005 se fusiona con 100; 100.05 queda aparte.");
        Afirmar.Igual(TipoProgresiva.Manual, cerca100[0].Tipo, "La manual tiene prioridad sobre la regular.");
        Afirmar.Cerca(100.005, cerca100[0].Progresiva);
        Afirmar.Cerca(100.05, cerca100[1].Progresiva);
        Afirmar.Verdadero(r.Avisos.Any(a => a.Contains("0+400.00")), "La manual fuera del rango genera aviso.");
    }

    [Prueba]
    public static void La_tolerancia_no_encadena_puntos_separados()
    {
        var lista = new[]
        {
            new ProgresivaMuestreo(10.000, TipoProgresiva.Regular, null),
            new ProgresivaMuestreo(10.008, TipoProgresiva.Regular, null),
            new ProgresivaMuestreo(10.016, TipoProgresiva.Regular, null),
        };
        var r = GeneradorProgresivas.EliminarDuplicados(lista, 0.01);
        Afirmar.Secuencia(new[] { 10.000, 10.016 }, r.Select(p => p.Progresiva));
    }

    [Prueba]
    public static void Clasificacion_de_uniones()
    {
        Afirmar.Igual(TipoProgresiva.PC, GeneradorProgresivas.Clasificar(TipoTramo.Tangente, TipoTramo.Curva));
        Afirmar.Igual(TipoProgresiva.PT, GeneradorProgresivas.Clasificar(TipoTramo.Curva, TipoTramo.Tangente));
        Afirmar.Igual(TipoProgresiva.TS, GeneradorProgresivas.Clasificar(TipoTramo.Tangente, TipoTramo.Espiral));
        Afirmar.Igual(TipoProgresiva.SC, GeneradorProgresivas.Clasificar(TipoTramo.Espiral, TipoTramo.Curva));
        Afirmar.Igual(TipoProgresiva.CS, GeneradorProgresivas.Clasificar(TipoTramo.Curva, TipoTramo.Espiral));
        Afirmar.Igual(TipoProgresiva.ST, GeneradorProgresivas.Clasificar(TipoTramo.Espiral, TipoTramo.Tangente));
        Afirmar.Igual(TipoProgresiva.PCC, GeneradorProgresivas.Clasificar(TipoTramo.Curva, TipoTramo.Curva));
        Afirmar.Igual(TipoProgresiva.SS, GeneradorProgresivas.Clasificar(TipoTramo.Espiral, TipoTramo.Espiral));
        Afirmar.Igual(TipoProgresiva.PI, GeneradorProgresivas.Clasificar(TipoTramo.Tangente, TipoTramo.Tangente));
    }

    [Prueba]
    public static void Parametros_invalidos_se_rechazan()
    {
        Afirmar.Lanza<ArgumentException>(() => GeneradorProgresivas.Generar(Simple, new OpcionesMuestreo { IntervaloTangente = 0 }));
        Afirmar.Lanza<ArgumentException>(() => GeneradorProgresivas.Generar(Simple, new OpcionesMuestreo { ProgresivaInicial = 200, ProgresivaFinal = 100 }));
        Afirmar.Lanza<ArgumentException>(() => GeneradorProgresivas.Generar(Array.Empty<TramoAlineamiento>(), new OpcionesMuestreo()));
        Afirmar.Lanza<ArgumentException>(() => GeneradorProgresivas.Generar(Simple, new OpcionesMuestreo { IntervaloTangente = 0.001, IntervaloCurva = 0.001, Tolerancia = 0 }));
    }

    [Prueba]
    public static void Formato_de_progresivas()
    {
        Afirmar.Igual("1+234.57", FormatoProgresiva.Formatear(1234.567));
        Afirmar.Igual("0+000.00", FormatoProgresiva.Formatear(0));
        Afirmar.Igual("2+000.00", FormatoProgresiva.Formatear(1999.999));
        Afirmar.Igual("0+013", FormatoProgresiva.Formatear(12.5, 0));
        Afirmar.Igual("-0+010.00", FormatoProgresiva.Formatear(-10));
        Afirmar.Igual("12+005.500", FormatoProgresiva.Formatear(12005.5, 3));
    }

    [Prueba]
    public static void Lectura_de_progresivas()
    {
        Afirmar.Verdadero(FormatoProgresiva.TryParse("1+234.56", out double a)); Afirmar.Cerca(1234.56, a, 1e-9);
        Afirmar.Verdadero(FormatoProgresiva.TryParse("1234.56", out double b)); Afirmar.Cerca(1234.56, b, 1e-9);
        Afirmar.Verdadero(FormatoProgresiva.TryParse("1+234,56", out double c)); Afirmar.Cerca(1234.56, c, 1e-9);
        Afirmar.Verdadero(FormatoProgresiva.TryParse("-0+010", out double d)); Afirmar.Cerca(-10, d);
        Afirmar.Verdadero(!FormatoProgresiva.TryParse("1+", out _));
        Afirmar.Verdadero(!FormatoProgresiva.TryParse("abc", out _));

        var lista = FormatoProgresiva.LeerLista("0+050; 0+075\n120 xx", out var invalidos);
        Afirmar.Secuencia(new double[] { 50, 75, 120 }, lista);
        Afirmar.Secuencia(new[] { "xx" }, invalidos);
    }
}
