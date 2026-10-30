using System.Globalization;
using ArbaSecciones.Core.Configuracion;
using ArbaSecciones.Core.Materiales;
using ArbaSecciones.Core.Tests.Marco;

namespace ArbaSecciones.Core.Tests;

/// <summary>Secciones sintéticas de corredor (desplazamiento, elevación), sin AutoCAD.</summary>
public static class PruebasMateriales
{
    private static int _indice;

    /// <summary>Vínculo con códigos separados por coma y puntos x1, z1, x2, z2…</summary>
    private static VinculoSeccion V(string codigos, params double[] xz)
    {
        var puntos = new List<PuntoSeccion>();
        for (int i = 0; i + 1 < xz.Length; i += 2) puntos.Add(new PuntoSeccion(xz[i], xz[i + 1]));
        var lista = codigos.Length == 0 ? new List<string>() : codigos.Split(',').Select(c => c.Trim()).ToList();
        return new VinculoSeccion(_indice++, lista, puntos);
    }

    private static SeccionCorredor S(params VinculoSeccion[] vinculos) => new(1000, vinculos, Array.Empty<FormaSeccion>());

    /// <summary>Carril de 3.5 m a cada lado con bombeo, paquete de 0.5 m y talud en corte (izq.) y relleno (der.).</summary>
    private static SeccionCorredor SeccionSimple() => S(
        V("Top,Pave", -3.5, 99.93, 0, 100),
        V("Top,Pave", 0, 100, 3.5, 99.93),
        V("Datum", -3.5, 99.43, 0, 99.5),
        V("Datum", 0, 99.5, 3.5, 99.43),
        V("Pave", -3.5, 99.93, -3.5, 99.43),
        V("Pave", 3.5, 99.93, 3.5, 99.43),
        V("Top,Datum,Daylight", -6.5, 101.43, -3.5, 99.43),
        V("Top,Datum,Daylight", 3.5, 99.43, 6.5, 97.43));

    [Prueba]
    public static void Seccion_simple_envolventes_superior_e_inferior()
    {
        SeccionCorredor s = SeccionSimple();
        Envolvente arriba = DetectorEnvolvente.Calcular(s.Vinculos, superior: true);
        Envolvente abajo = DetectorEnvolvente.Calcular(s.Vinculos, superior: false);

        Afirmar.Cerca(-6.5, arriba.Desde);
        Afirmar.Cerca(6.5, arriba.Hasta);
        Afirmar.Cerca(100, arriba.ElevacionEn(0)!.Value, 1e-9);
        Afirmar.Cerca(99.5, abajo.ElevacionEn(0)!.Value, 1e-9);
        Afirmar.Cerca(100.43, arriba.ElevacionEn(-5)!.Value, 1e-9);   // en el talud las dos coinciden
        Afirmar.Cerca(100.43, abajo.ElevacionEn(-5)!.Value, 1e-9);
        Afirmar.Igual(1, arriba.Cobertura().Count, "Sin huecos.");

        Afirmar.Secuencia(new[] { "Top" }, DetectorEnvolvente.Detectar(s, superior: true).NombresCodigos);
        Afirmar.Secuencia(new[] { "Datum" }, DetectorEnvolvente.Detectar(s, superior: false).NombresCodigos);
    }

    [Prueba]
    public static void Paquete_de_tres_capas_superior_Top_inferior_fondo_de_SubBase()
    {
        SeccionCorredor s = S(
            V("Top,Pave", -3.5, 100, 3.5, 100),
            V("Base", -3.5, 99.9, 3.5, 99.9),
            V("SubBase", -3.5, 99.7, 3.5, 99.7),
            V("SubBase,Datum", -3.5, 99.4, 3.5, 99.4),
            V("Pave", -3.5, 100, -3.5, 99.4),
            V("Pave", 3.5, 100, 3.5, 99.4),
            V("Top,Datum,Daylight", -6, 101, -3.5, 99.4),
            V("Top,Datum,Daylight", 3.5, 99.4, 6, 98));

        ResultadoDeteccion arriba = DetectorEnvolvente.Detectar(s, superior: true);
        ResultadoDeteccion abajo = DetectorEnvolvente.Detectar(s, superior: false);
        Afirmar.Secuencia(new[] { "Top" }, arriba.NombresCodigos);
        Afirmar.Secuencia(new[] { "Datum" }, abajo.NombresCodigos);
        Afirmar.Cerca(99.4, abajo.Envolvente.ElevacionEn(0)!.Value, 1e-9);
        Afirmar.Igual(0, abajo.Avisos.Count, string.Join(" | ", abajo.Avisos));

        var v = ValidadorSuperficie.Validar(s, abajo.NombresCodigos, CorreccionVoladizo.VinculosInferiores, SuperficieMateriales.SubRasante);
        Afirmar.Verdadero(v.SinProblemas, v.Resumen());
    }

    /// <summary>Carril de 3.6 m, cuneta "SAC" de 1.20 m solo a la izquierda y taludes.</summary>
    private static SeccionCorredor SeccionConCuneta() => S(
        V("Top,Pave", -3.6, 100, 3.6, 100),
        V("Datum", -3.6, 99.5, 3.6, 99.5),
        V("Pave", -3.6, 100, -3.6, 99.5),
        V("Pave", 3.6, 100, 3.6, 99.5),
        V("SAC", -3.6, 100, -4.2, 99.55),
        V("SAC", -4.2, 99.55, -4.8, 99.55),
        V("Top,Datum,Daylight", -7.8, 101.55, -4.8, 99.55),
        V("Top,Datum,Daylight", 3.6, 99.5, 6.6, 97.5));

    [Prueba]
    public static void Cuneta_con_codigo_no_estandar_entra_en_la_subrasante()
    {
        ResultadoDeteccion abajo = DetectorEnvolvente.Detectar(SeccionConCuneta(), superior: false);
        Afirmar.Verdadero(abajo.NombresCodigos.Contains("SAC"), string.Join(", ", abajo.NombresCodigos));
        Afirmar.Verdadero(abajo.NombresCodigos.Contains("Datum"));
        Afirmar.Igual("Datum", abajo.NombresCodigos[0], "El que más aporta va primero.");

        var v = ValidadorSuperficie.Validar(SeccionConCuneta(), abajo.NombresCodigos, CorreccionVoladizo.VinculosInferiores,
            SuperficieMateriales.SubRasante);
        Afirmar.Verdadero(v.Continua, v.Resumen());
    }

    [Prueba]
    public static void Sin_el_codigo_de_la_cuneta_hay_un_hueco_y_se_sugiere_SAC()
    {
        var v = ValidadorSuperficie.Validar(SeccionConCuneta(), new[] { "Datum" }, CorreccionVoladizo.VinculosInferiores,
            SuperficieMateriales.SubRasante);
        Afirmar.Igual(1, v.Huecos.Count, v.Resumen());
        Hueco h = v.Huecos[0];
        Afirmar.Cerca(-4.8, h.Desde, 1e-9);
        Afirmar.Cerca(-3.6, h.Hasta, 1e-9);
        Afirmar.Cerca(1.2, h.Ancho, 1e-9);
        Afirmar.Secuencia(new[] { "SAC" }, h.Sugeridos);
        Afirmar.Verdadero(h.CodigosPresentes.Contains("SAC"));
        Afirmar.Igual("Falta cubrir 1.20 m entre -4.80 y -3.60. Ahí están los vínculos con código SAC. ¿Agregar SAC?", h.Mensaje);
        Afirmar.Secuencia(new[] { "SAC" }, v.Sugerencias);
        Afirmar.Verdadero(!v.Continua);
    }

    [Prueba]
    public static void Codigo_usado_arriba_y_abajo_avisa_que_arrastra_vinculos()
    {
        SeccionCorredor s = S(
            V("Capa", -3, 100, 3, 100),
            V("Capa", -3, 99.5, 3, 99.5),
            V("", -3, 100, -3, 99.5),
            V("", 3, 100, 3, 99.5),
            V("Top,Datum", -5, 101, -3, 99.5),
            V("Top,Datum", 3, 99.5, 5, 98.5));
        ResultadoDeteccion arriba = DetectorEnvolvente.Detectar(s, superior: true);
        Afirmar.Verdadero(arriba.NombresCodigos.Contains("Capa"));
        CodigoSugerido capa = arriba.Codigos.First(c => c.Codigo == "Capa");
        Afirmar.Verdadero(capa.ArrastraFuera());
        Afirmar.Cerca(6, capa.LongitudFuera, 1e-9);
        Afirmar.Verdadero(arriba.Avisos.Any(a => a.Contains("Capa") && a.Contains("arrastraría")), string.Join(" | ", arriba.Avisos));
    }

    [Prueba]
    public static void Correccion_de_voladizo_Bottom_toma_la_mas_baja_y_Top_la_mas_alta()
    {
        SeccionCorredor s = S(
            V("Pave", -2, 100, 2, 100),
            V("Base", -2, 99.6, 2, 99.6),
            V("Base", -4, 99.6, -2, 99.6));
        var codigos = new[] { "Pave", "Base" };
        Envolvente abajo = ValidadorSuperficie.LineaSuperficie(s, codigos, CorreccionVoladizo.VinculosInferiores, SuperficieMateriales.SubRasante);
        Envolvente arriba = ValidadorSuperficie.LineaSuperficie(s, codigos, CorreccionVoladizo.VinculosSuperiores, SuperficieMateriales.SubRasante);
        Afirmar.Cerca(99.6, abajo.ElevacionEn(0)!.Value, 1e-9);
        Afirmar.Cerca(100, arriba.ElevacionEn(0)!.Value, 1e-9);
        Afirmar.Cerca(99.6, arriba.ElevacionEn(-3)!.Value, 1e-9);

        // Con capas superpuestas, la subrasante con "Top Links" pasaría por la cara superior: se informa como desvío.
        var v = ValidadorSuperficie.Validar(s, codigos, CorreccionVoladizo.VinculosSuperiores, SuperficieMateriales.SubRasante);
        Afirmar.Igual(1, v.Desvios.Count, v.Resumen());
        Afirmar.Cerca(0.4, v.Desvios[0].Maximo, 1e-9);
        Afirmar.Cerca(-2, v.Desvios[0].Desde, 1e-9);
        Afirmar.Cerca(2, v.Desvios[0].Hasta, 1e-9);
    }

    [Prueba]
    public static void Vinculo_vertical_no_rompe_la_deteccion_ni_crea_huecos()
    {
        SeccionCorredor s = S(
            V("Top", -3, 100, 0, 100),
            V("Top", 0, 100, 0, 100.15),           // cara del sardinel, vertical
            V("Top", 0, 100.15, 2, 100.17),
            V("Datum", -3, 99.5, 2, 99.5));
        ResultadoDeteccion arriba = DetectorEnvolvente.Detectar(s, superior: true);
        Afirmar.Secuencia(new[] { "Top" }, arriba.NombresCodigos);
        Afirmar.Igual(1, arriba.Envolvente.Cobertura().Count);
        var v = ValidadorSuperficie.Validar(s, new[] { "Top" }, CorreccionVoladizo.VinculosSuperiores, SuperficieMateriales.Rasante);
        Afirmar.Verdadero(v.SinProblemas, v.Resumen());
        Afirmar.Igual(0, arriba.Avisos.Count, string.Join(" | ", arriba.Avisos));
    }

    [Prueba]
    public static void Seccion_asimetrica_con_peralte()
    {
        SeccionCorredor s = S(
            V("Top,Pave", -3.5, 100.14, 3.5, 99.86),
            V("Datum", -3.5, 99.64, 3.5, 99.36),
            V("Pave", -3.5, 100.14, -3.5, 99.64),
            V("Pave", 3.5, 99.86, 3.5, 99.36),
            V("Top,Datum,Daylight_Cut", -7.5, 101.64, -3.5, 99.64),
            V("Top,Datum,Daylight_Fill", 3.5, 99.36, 9.5, 96.36));
        ResultadoDeteccion abajo = DetectorEnvolvente.Detectar(s, superior: false);
        Afirmar.Secuencia(new[] { "Datum" }, abajo.NombresCodigos);
        Afirmar.Cerca(-7.5, abajo.Envolvente.Desde);
        Afirmar.Cerca(9.5, abajo.Envolvente.Hasta);
        Afirmar.Cerca(99.5, abajo.Envolvente.ElevacionEn(0)!.Value, 1e-9);

        ResultadoDeteccion arriba = DetectorEnvolvente.Detectar(s, superior: true);
        Afirmar.Secuencia(new[] { "Top" }, arriba.NombresCodigos);
        Afirmar.Cerca(100, arriba.Envolvente.ElevacionEn(0)!.Value, 1e-9);

        var v = ValidadorSuperficie.Validar(s, abajo.NombresCodigos, CorreccionVoladizo.VinculosInferiores, SuperficieMateriales.SubRasante);
        Afirmar.Verdadero(v.SinProblemas, v.Resumen());
        Afirmar.Verdadero(!v.NoLlegaIzquierda && !v.NoLlegaDerecha);

        // Sin el daylight de relleno la superficie no llega al extremo derecho.
        SeccionCorredor sinRelleno = S(s.Vinculos.Select(x => x.Tiene("Daylight_Fill") ? x with { Codigos = new[] { "Daylight_Fill" } } : x).ToArray());
        var v2 = ValidadorSuperficie.Validar(sinRelleno, new[] { "Datum" }, CorreccionVoladizo.VinculosInferiores, SuperficieMateriales.SubRasante);
        Afirmar.Verdadero(v2.NoLlegaDerecha);
        Afirmar.Verdadero(!v2.NoLlegaIzquierda);
        Afirmar.Secuencia(new[] { "Daylight_Fill" }, v2.Sugerencias);
    }

    [Prueba]
    public static void Codigo_sobrante_y_ausente()
    {
        var v = ValidadorSuperficie.Validar(SeccionSimple(), new[] { "Datum", "Pave", "NoExiste" }, CorreccionVoladizo.VinculosInferiores,
            SuperficieMateriales.SubRasante);
        Afirmar.Secuencia(new[] { "Pave" }, v.Sobrantes);
        Afirmar.Secuencia(new[] { "NoExiste" }, v.Ausentes);
        Afirmar.Verdadero(v.Continua);
    }

    [Prueba]
    public static void Areas_de_corte_y_relleno_entre_terreno_y_subrasante()
    {
        var terreno = new[] { new PuntoSeccion(-10, 101), new PuntoSeccion(10, 101) };
        var sub = new[] { new PuntoSeccion(-5, 100), new PuntoSeccion(5, 100) };
        AreasSeccion a = CalculadorVolumenes.Areas(0, terreno, sub);
        Afirmar.Cerca(10, a.Corte, 1e-9);
        Afirmar.Cerca(0, a.Relleno, 1e-9);

        // El terreno cruza la subrasante en el eje: mitad corte, mitad relleno.
        var inclinado = new[] { new PuntoSeccion(-5, 101), new PuntoSeccion(5, 99) };
        AreasSeccion b = CalculadorVolumenes.Areas(0, inclinado, sub);
        Afirmar.Cerca(2.5, b.Corte, 1e-9);
        Afirmar.Cerca(2.5, b.Relleno, 1e-9);
    }

    [Prueba]
    public static void Areas_medias_volumenes_y_acumulados_calculados_a_mano()
    {
        var secciones = new[]
        {
            new AreasSeccion(0, 10, 0),
            new AreasSeccion(40, 6, 8),
            new AreasSeccion(20, 14, 2),
        };
        var f = CalculadorVolumenes.AreasMedias(secciones);
        Afirmar.Igual(3, f.Count);
        Afirmar.Cerca(0, f[0].VolumenCorte);
        Afirmar.Cerca(240, f[1].VolumenCorte, 1e-9);   // (10 + 14) / 2 × 20
        Afirmar.Cerca(20, f[1].VolumenRelleno, 1e-9);  // (0 + 2) / 2 × 20
        Afirmar.Cerca(200, f[2].VolumenCorte, 1e-9);
        Afirmar.Cerca(100, f[2].VolumenRelleno, 1e-9);
        Afirmar.Cerca(440, f[2].AcumuladoCorte, 1e-9);
        Afirmar.Cerca(120, f[2].AcumuladoRelleno, 1e-9);
        Afirmar.Cerca(320, f[2].Masa, 1e-9);

        var conFactores = CalculadorVolumenes.AreasMedias(secciones, 1.25, 0.9);
        Afirmar.Cerca(550, conFactores[2].AcumuladoCorte, 1e-9);
        Afirmar.Cerca(108, conFactores[2].AcumuladoRelleno, 1e-9);
        Afirmar.Cerca(442, conFactores[2].Masa, 1e-9);
        Afirmar.Cerca(200, conFactores[2].VolumenCorte, 1e-9, "El volumen de cada tramo va sin factor.");

        string csv = CalculadorVolumenes.Csv(f, CultureInfo.GetCultureInfo("es-ES"));
        Afirmar.Verdadero(csv.StartsWith("Progresiva;Área corte (m²);"), csv);
        Afirmar.Verdadero(csv.Contains("0+020.00;14,00;2,00;240,00;20,00;240,00;20,00;220,00"), csv);
    }

    [Prueba]
    public static void Plan_de_validacion_regiones_corte_relleno_y_grupo()
    {
        var plan = PlanValidacion.Elegir(
            new[] { (0.0, 100.0), (100.0, 300.0) },
            new[] { (20.0, -1.5), (150.0, 2.0), (250.0, -3.0) },
            new[] { 0.0, 50.0, 100.0 },
            incluirGrupo: true);
        Afirmar.Secuencia(new[] { 0.0, 50.0, 100.0, 150.0, 200.0, 250.0, 300.0 }, plan.Select(p => p.Progresiva));
        Afirmar.Igual("fin de la región 1, inicio de la región 2, grupo de muestreo", plan[2].Motivo);
        Afirmar.Igual("en corte", plan[5].Motivo);
        Afirmar.Igual("en relleno", plan[3].Motivo);

        var sinGrupo = PlanValidacion.Elegir(new[] { (0.0, 100.0) }, Array.Empty<(double, double)>(), new[] { 30.0 }, incluirGrupo: false);
        Afirmar.Igual(3, sinGrupo.Count);
    }

    [Prueba]
    public static void Contorno_de_forma_con_vinculos_desordenados()
    {
        // Rectángulo de 2 × 0.5 armado con lados en cualquier orden y sentido.
        var lados = new List<IReadOnlyList<PuntoSeccion>>
        {
            new[] { new PuntoSeccion(0, 0), new PuntoSeccion(2, 0) },
            new[] { new PuntoSeccion(0, 0.5), new PuntoSeccion(2, 0.5) },
            new[] { new PuntoSeccion(2, 0.5), new PuntoSeccion(2, 0) },
            new[] { new PuntoSeccion(0, 0), new PuntoSeccion(0, 0.5) },
        };
        List<PuntoSeccion> c = ContornoForma.Armar(lados);
        Afirmar.Igual(4, c.Count, string.Join(" ", c));
        double area = 0;
        for (int i = 0; i < c.Count; i++)
        {
            PuntoSeccion p = c[i], q = c[(i + 1) % c.Count];
            area += p.Desplazamiento * q.Elevacion - q.Desplazamiento * p.Elevacion;
        }
        Afirmar.Cerca(1.0, Math.Abs(area) / 2, 1e-9);
    }

    [Prueba]
    public static void Json_antiguo_sin_Materiales_usa_valores_por_defecto()
    {
        var cfg = AlmacenConfiguracion.Deserializar("{ \"Version\": 2, \"Escala\": { \"DenominadorH\": 250 } }");
        Afirmar.Verdadero(cfg.Materiales != null);
        Afirmar.Igual("SubRasante", cfg.Materiales!.NombreSubRasante);
        Afirmar.Igual("Rasante", cfg.Materiales.NombreRasante);
        Afirmar.Igual("Movimiento de tierras", cfg.Materiales.NombreLista);
        Afirmar.Cerca(1.0, cfg.Materiales.FactorEsponjamiento);
        Afirmar.Igual(CorreccionVoladizo.VinculosInferiores, cfg.Materiales.VoladizoSubRasante);
        Afirmar.Igual(CorreccionVoladizo.VinculosSuperiores, cfg.Materiales.VoladizoRasante);
        Afirmar.Igual(0, cfg.Materiales.CodigosSubRasante.Count);

        var nula = AlmacenConfiguracion.Deserializar("{ \"Materiales\": null }");
        Afirmar.Igual("SubRasante", nula.Materiales.NombreSubRasante);

        var vacia = AlmacenConfiguracion.Deserializar("{ \"Materiales\": { \"NombreSubRasante\": \"\", \"CodigosRasante\": null, \"FactorCompactacion\": 0 } }");
        Afirmar.Igual("SubRasante", vacia.Materiales.NombreSubRasante);
        Afirmar.Igual(0, vacia.Materiales.CodigosRasante.Count);
        Afirmar.Cerca(1.0, vacia.Materiales.FactorCompactacion);

        string json = AlmacenConfiguracion.Serializar(new ConfiguracionArba());
        Afirmar.Verdadero(json.Contains("\"VinculosInferiores\""), "Los enumerados de Materiales se guardan como texto.");
    }
}
