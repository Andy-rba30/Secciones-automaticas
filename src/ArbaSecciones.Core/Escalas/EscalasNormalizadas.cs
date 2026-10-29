using System.Globalization;

namespace ArbaSecciones.Core.Escalas;

/// <summary>Escalas de la lista desplegable y lectura de escalas personalizadas.</summary>
public static class EscalasNormalizadas
{
    public static readonly IReadOnlyList<double> Denominadores = new double[]
    {
        20, 25, 50, 75, 100, 125, 150, 200, 250, 300, 400, 500, 750, 1000, 1250, 1500, 2000, 2500, 5000,
    };

    public static string Formatear(double denominador) =>
        "1:" + denominador.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Lee "1:200", "1/200", "1 : 200" o "200".</summary>
    public static bool TryParse(string? texto, out double denominador)
    {
        denominador = 0;
        if (string.IsNullOrWhiteSpace(texto)) return false;

        string limpio = texto.Trim().Replace(" ", string.Empty).Replace(',', '.');
        int separador = limpio.IndexOfAny(new[] { ':', '/' });

        double numerador = 1;
        string parteDenominador = limpio;
        if (separador >= 0)
        {
            if (!double.TryParse(limpio[..separador], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out numerador)) return false;
            parteDenominador = limpio[(separador + 1)..];
        }

        if (!double.TryParse(parteDenominador, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double d)) return false;
        if (numerador <= 0 || d <= 0) return false;

        denominador = d / numerador;
        return true;
    }
}
