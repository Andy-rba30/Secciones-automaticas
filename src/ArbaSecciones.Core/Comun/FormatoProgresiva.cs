using System.Globalization;

namespace ArbaSecciones.Core.Comun;

/// <summary>
/// Conversión entre progresivas numéricas (1234.56) y su forma de kilómetro (1+234.56).
/// </summary>
public static class FormatoProgresiva
{
    private const double MetrosPorKm = 1000.0;

    /// <summary>Devuelve la progresiva con formato "k+mmm.dd". Ej.: 1234.567 → "1+234.57".</summary>
    public static string Formatear(double progresiva, int decimales = 2)
    {
        if (double.IsNaN(progresiva) || double.IsInfinity(progresiva))
            throw new ArgumentOutOfRangeException(nameof(progresiva), "La progresiva no es un número válido.");
        if (decimales is < 0 or > 6)
            throw new ArgumentOutOfRangeException(nameof(decimales), "Use entre 0 y 6 decimales.");

        // Se redondea primero para que 1999.999 con 2 decimales salga 2+000.00 y no 1+1000.00.
        double redondeada = Math.Round(progresiva, decimales, MidpointRounding.AwayFromZero);
        string signo = redondeada < 0 ? "-" : string.Empty;
        double absoluta = Math.Abs(redondeada);

        long km = (long)Math.Floor(absoluta / MetrosPorKm + 1e-9);
        double resto = Math.Max(0, absoluta - km * MetrosPorKm);

        string formato = decimales == 0 ? "000" : "000." + new string('0', decimales);
        return signo + km.ToString(CultureInfo.InvariantCulture) + "+" + resto.ToString(formato, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Lee "1+234.56", "1234.56" o "1+234,56". Devuelve false si el texto no es una progresiva.
    /// </summary>
    public static bool TryParse(string? texto, out double progresiva)
    {
        progresiva = 0;
        if (string.IsNullOrWhiteSpace(texto)) return false;

        string limpio = texto.Trim().Replace(" ", string.Empty);
        if (!limpio.Contains('.') && limpio.Count(c => c == ',') == 1)
            limpio = limpio.Replace(',', '.');

        bool negativo = limpio.StartsWith('-');
        if (negativo) limpio = limpio[1..];
        if (limpio.Length == 0) return false;

        int mas = limpio.IndexOf('+');
        double valor;
        if (mas >= 0)
        {
            string parteKm = limpio[..mas];
            string parteM = limpio[(mas + 1)..];
            if (parteKm.Length == 0 || parteM.Length == 0) return false;
            if (!long.TryParse(parteKm, NumberStyles.None, CultureInfo.InvariantCulture, out long km)) return false;
            if (!double.TryParse(parteM, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double metros)) return false;
            valor = km * MetrosPorKm + metros;
        }
        else
        {
            if (!double.TryParse(limpio, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out valor)) return false;
        }

        progresiva = negativo ? -valor : valor;
        return true;
    }

    /// <summary>Lee una lista separada por punto y coma, saltos de línea o espacios. Devuelve los textos que no pudo leer.</summary>
    public static List<double> LeerLista(string? texto, out List<string> invalidos)
    {
        var valores = new List<double>();
        invalidos = new List<string>();
        if (string.IsNullOrWhiteSpace(texto)) return valores;

        foreach (string parte in texto.Split(new[] { ';', '\n', '\r', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryParse(parte, out double v)) valores.Add(v);
            else invalidos.Add(parte);
        }
        return valores;
    }
}
