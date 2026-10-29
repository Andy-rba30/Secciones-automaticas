using System.Globalization;
using System.Text.RegularExpressions;

namespace ArbaSecciones.Core.Comun;

/// <summary>
/// Reemplaza marcadores {clave} o {clave:formato} por valores. Ej.: "PL-SEC-{n:000}" con n = 7 → "PL-SEC-007".
/// Los marcadores desconocidos se dejan tal cual para que el error se vea en el plano.
/// </summary>
public static partial class PlantillaTexto
{
    [GeneratedRegex(@"\{(?<clave>[A-Za-z_][A-Za-z0-9_]*)(?::(?<formato>[^{}]+))?\}", RegexOptions.CultureInvariant)]
    private static partial Regex Marcador();

    public static string Aplicar(string? plantilla, IReadOnlyDictionary<string, object?> valores)
    {
        if (string.IsNullOrEmpty(plantilla)) return string.Empty;
        ArgumentNullException.ThrowIfNull(valores);

        return Marcador().Replace(plantilla, m =>
        {
            string clave = m.Groups["clave"].Value;
            if (!TryObtener(valores, clave, out object? valor)) return m.Value;
            if (valor is null) return string.Empty;

            string formato = m.Groups["formato"].Value;
            if (formato.Length > 0 && valor is IFormattable formateable)
            {
                try { return formateable.ToString(formato, CultureInfo.InvariantCulture); }
                catch (FormatException) { return m.Value; }
            }
            return Convert.ToString(valor, CultureInfo.InvariantCulture) ?? string.Empty;
        });
    }

    private static bool TryObtener(IReadOnlyDictionary<string, object?> valores, string clave, out object? valor)
    {
        if (valores.TryGetValue(clave, out valor)) return true;
        foreach (var par in valores)
        {
            if (string.Equals(par.Key, clave, StringComparison.OrdinalIgnoreCase))
            {
                valor = par.Value;
                return true;
            }
        }
        valor = null;
        return false;
    }
}
