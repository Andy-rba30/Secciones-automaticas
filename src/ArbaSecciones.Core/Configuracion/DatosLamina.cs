using System.Globalization;
using ArbaSecciones.Core.Acomodo;
using ArbaSecciones.Core.Comun;
using ArbaSecciones.Core.Escalas;

namespace ArbaSecciones.Core.Configuracion;

/// <summary>
/// Valores que se escriben en el membrete de cada lámina.
/// Marcadores disponibles en las plantillas de atributos:
/// {codigo} {n} {total} {pi} {pf} {tramo} {kmi} {kmf} {km} {parte} {partes} {escala} {nombre}.
/// </summary>
public static class DatosLamina
{
    public static IReadOnlyDictionary<string, object?> Valores(Lamina lamina, int totalLaminas, ConfigPresentaciones cfg, EscalaSecciones escala, double longitudTramo = 1000)
    {
        ArgumentNullException.ThrowIfNull(lamina);
        ArgumentNullException.ThrowIfNull(escala);
        return Valores(InfoLamina.Desde(lamina), totalLaminas, cfg, escala.ToString(), longitudTramo);
    }

    public static IReadOnlyDictionary<string, object?> Valores(InfoLamina lamina, int totalLaminas, ConfigPresentaciones cfg, string textoEscala, double longitudTramo = 1000)
    {
        ArgumentNullException.ThrowIfNull(lamina);
        ArgumentNullException.ThrowIfNull(cfg);
        if (!(longitudTramo > 0)) throw new ArgumentOutOfRangeException(nameof(longitudTramo));

        int n = cfg.CorrelativoInicial + lamina.Numero - 1;
        int total = cfg.CorrelativoInicial + totalLaminas - 1;
        var basicos = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["n"] = n };

        double kmi;
        double kmf;
        if (lamina.Grupo.HasValue)
        {
            kmi = lamina.Grupo.Value * longitudTramo;
            kmf = kmi + longitudTramo;
        }
        else
        {
            kmi = Math.Floor(lamina.ProgresivaInicial / longitudTramo) * longitudTramo;
            kmf = Math.Ceiling(lamina.ProgresivaFinal / longitudTramo) * longitudTramo;
            if (kmf <= kmi) kmf = kmi + longitudTramo;
        }

        var tramoValores = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["kmi"] = FormatoProgresiva.Formatear(kmi, 0),
            ["kmf"] = FormatoProgresiva.Formatear(kmf, 0),
        };

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["n"] = n,
            ["total"] = total,
            ["codigo"] = PlantillaTexto.Aplicar(cfg.PatronCodigo, basicos),
            ["nombre"] = NombrePresentacion(cfg, n),
            ["pi"] = FormatoProgresiva.Formatear(lamina.ProgresivaInicial),
            ["pf"] = FormatoProgresiva.Formatear(lamina.ProgresivaFinal),
            ["kmi"] = tramoValores["kmi"],
            ["kmf"] = tramoValores["kmf"],
            ["tramo"] = PlantillaTexto.Aplicar(cfg.PatronTramo, tramoValores),
            ["km"] = lamina.Grupo,
            ["parte"] = lamina.Parte,
            ["partes"] = lamina.PartesDelGrupo,
            ["escala"] = textoEscala ?? string.Empty,
        };
    }

    /// <summary>Nombre de la presentación: prefijo + correlativo con ceros. Quita caracteres no válidos en AutoCAD.</summary>
    public static string NombrePresentacion(ConfigPresentaciones cfg, int correlativo)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        string numero = correlativo.ToString(new string('0', Math.Clamp(cfg.Digitos, 1, 6)), CultureInfo.InvariantCulture);
        return LimpiarNombre((cfg.Prefijo ?? string.Empty) + numero);
    }

    /// <summary>AutoCAD no admite &lt; &gt; / \ " : ; ? * | , = ` en nombres de presentación.</summary>
    public static string LimpiarNombre(string nombre)
    {
        const string prohibidos = "<>/\\\":;?*|,=`";
        var limpio = new string(nombre.Select(c => prohibidos.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim();
        if (limpio.Length == 0) limpio = "SEC";
        return limpio.Length > 255 ? limpio[..255] : limpio;
    }
}
