using System.Text;
using ArbaSecciones.Core.Acomodo;
using ArbaSecciones.Core.Comun;
using ArbaSecciones.Core.Configuracion;
using ArbaSecciones.Core.Tests.Marco;

namespace ArbaSecciones.Core.Tests;

public static class PruebasConfiguracion
{
    private static string CarpetaConTilde()
    {
        string carpeta = Path.Combine(Path.GetTempPath(), "arba-pruebas-" + Guid.NewGuid().ToString("N"), "Andy Bayona Antón", "configuración");
        Directory.CreateDirectory(carpeta);
        return carpeta;
    }

    [Prueba]
    public static void Guarda_y_lee_en_ruta_con_tilde()
    {
        string carpeta = CarpetaConTilde();
        try
        {
            string ruta = Path.Combine(carpeta, "configuración.json");
            var cfg = new ConfiguracionArba();
            cfg.Escala.DenominadorH = 250;
            cfg.Escala.DenominadorV = 125;
            cfg.Muestreo.Adicionales.Add(1234.5);
            cfg.Muestreo.Fuentes.Add("Terreno natural – Señal");
            cfg.Acomodo.Orden = OrdenAcomodo.PorFilas;
            cfg.Membrete.AreaMinX = 10; cfg.Membrete.AreaMaxX = 790;
            cfg.Membrete.AreaMinY = 50; cfg.Membrete.AreaMaxY = 570;

            AlmacenConfiguracion.Guardar(cfg, ruta);

            byte[] bytes = File.ReadAllBytes(ruta);
            Afirmar.Verdadero(!(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF), "Sin BOM.");
            string texto = Encoding.UTF8.GetString(bytes);
            Afirmar.Verdadero(texto.Contains("Presentación 1"), "Las tildes quedan legibles en el JSON.");
            Afirmar.Verdadero(texto.Contains("\"PorFilas\""), "Los enumerados se guardan como texto.");
            Afirmar.Verdadero(!texto.Contains("AreaDefinida"), "Las propiedades calculadas no se guardan.");
            Afirmar.Verdadero(!File.Exists(ruta + ".tmp"));

            var leida = AlmacenConfiguracion.Cargar(ruta);
            Afirmar.Cerca(250, leida.Escala.DenominadorH);
            Afirmar.Igual(125.0, leida.Escala.DenominadorV);
            Afirmar.Cerca(2, leida.Escala.Crear().Exageracion);
            Afirmar.Secuencia(new[] { 1234.5 }, leida.Muestreo.Adicionales);
            Afirmar.Igual("Terreno natural – Señal", leida.Muestreo.Fuentes[0]);
            Afirmar.Igual(OrdenAcomodo.PorFilas, leida.Acomodo.Orden);
            Afirmar.Cerca(780, leida.Membrete.AnchoUtil);
            Afirmar.Verdadero(leida.Membrete.AreaDefinida);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(carpeta))!, recursive: true);
        }
    }

    [Prueba]
    public static void Archivo_inexistente_devuelve_valores_por_defecto()
    {
        var cfg = AlmacenConfiguracion.Cargar(Path.Combine(Path.GetTempPath(), "no-existe-" + Guid.NewGuid().ToString("N") + ".json"));
        Afirmar.Cerca(200, cfg.Escala.DenominadorH);
        Afirmar.Igual(6, cfg.Membrete.Atributos.Count);
    }

    [Prueba]
    public static void Archivo_danado_se_aparta_y_se_usan_valores_por_defecto()
    {
        string carpeta = CarpetaConTilde();
        try
        {
            string ruta = Path.Combine(carpeta, "configuración.json");
            File.WriteAllText(ruta, "{ esto no es json", new UTF8Encoding(false));
            var cfg = AlmacenConfiguracion.CargarOPorDefecto(ruta, out string? error);
            Afirmar.Verdadero(error != null);
            Afirmar.Cerca(200, cfg.Escala.DenominadorH);
            Afirmar.Verdadero(File.Exists(ruta + ".dañado"));
            Afirmar.Verdadero(!File.Exists(ruta));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(carpeta))!, recursive: true);
        }
    }

    [Prueba]
    public static void Json_incompleto_se_normaliza()
    {
        var cfg = AlmacenConfiguracion.Deserializar("{ \"Escala\": { \"DenominadorH\": 500 }, \"Membrete\": { \"Atributos\": null }, \"Muestreo\": null }");
        Afirmar.Cerca(500, cfg.Escala.DenominadorH);
        Afirmar.Verdadero(cfg.Muestreo != null);
        Afirmar.Igual(6, cfg.Membrete.Atributos.Count);
    }

    [Prueba]
    public static void Parametros_de_acomodo_salen_del_membrete_y_del_acomodo()
    {
        var cfg = new ConfiguracionArba();
        cfg.Membrete.AreaMinX = 20; cfg.Membrete.AreaMaxX = 800;
        cfg.Membrete.AreaMinY = 50; cfg.Membrete.AreaMaxY = 570;
        cfg.Membrete.AltoFranjaCuadro = 60;
        cfg.Acomodo.SeparacionH = 12;
        cfg.Acomodo.Orden = OrdenAcomodo.PorFilas;
        var p = cfg.ParametrosAcomodo();
        Afirmar.Cerca(780, p.AnchoUtil);
        Afirmar.Cerca(520, p.AltoUtil);
        Afirmar.Cerca(60, p.AltoFranjaCuadro);
        Afirmar.Cerca(12, p.SeparacionH);
        Afirmar.Igual(OrdenAcomodo.PorFilas, p.Orden);
        Afirmar.Igual(0, p.Validar().Count);
    }

    [Prueba]
    public static void Plantillas_de_texto()
    {
        var v = new Dictionary<string, object?> { ["n"] = 7, ["pi"] = "0+100.00" };
        Afirmar.Igual("PL-007", PlantillaTexto.Aplicar("PL-{n:000}", v));
        Afirmar.Igual("PL-7 0+100.00", PlantillaTexto.Aplicar("PL-{N} {pi}", v));
        Afirmar.Igual("{desconocido}", PlantillaTexto.Aplicar("{desconocido}", v));
        Afirmar.Igual(string.Empty, PlantillaTexto.Aplicar(null, v));
    }
}
