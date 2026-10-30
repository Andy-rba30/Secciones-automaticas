using System.Globalization;
using System.Text.RegularExpressions;

namespace ArbaSecciones.Core.Hojas;

/// <summary>Formato normalizado de hoja. Medidas en mm: lado menor × lado mayor.</summary>
public sealed record FormatoPapel(string Nombre, double LadoMenor, double LadoMayor);

/// <summary>Papel que ofrece un trazador: nombre canónico y tamaño en mm tal como lo define el trazador.</summary>
public sealed record MedioPapel(string Nombre, double Ancho, double Alto)
{
    /// <summary>Papel sin márgenes no imprimibles ("full bleed" en DWG To PDF).</summary>
    public bool SinMargenes =>
        Nombre.Contains("full_bleed", StringComparison.OrdinalIgnoreCase) ||
        Nombre.Contains("full bleed", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Papel propuesto para un membrete: el medio, si se gira 90° y el tamaño de la hoja tal como se verá.</summary>
public sealed record OpcionPapel(MedioPapel Medio, bool Girado, double AnchoHoja, double AltoHoja, bool Exacto);

/// <summary>
/// Reconoce el formato de un membrete (A0, A1, A2…) por su tamaño y elige, entre los papeles de un trazador,
/// el que mejor lo contiene. Todo en milímetros.
/// </summary>
public static class FormatosPapel
{
    /// <summary>Serie A de ISO 216 y formatos alargados de ISO 5457 (A1×3, A3×4…).</summary>
    public static readonly IReadOnlyList<FormatoPapel> Normalizados = new[]
    {
        new FormatoPapel("A4", 210, 297),
        new FormatoPapel("A3", 297, 420),
        new FormatoPapel("A2", 420, 594),
        new FormatoPapel("A1", 594, 841),
        new FormatoPapel("A0", 841, 1189),
        new FormatoPapel("2A0", 1189, 1682),
        new FormatoPapel("4A0", 1682, 2378),
        new FormatoPapel("A4×3", 297, 630),
        new FormatoPapel("A4×4", 297, 841),
        new FormatoPapel("A4×5", 297, 1051),
        new FormatoPapel("A3×3", 420, 891),
        new FormatoPapel("A3×4", 420, 1189),
        new FormatoPapel("A2×3", 594, 1261),
        new FormatoPapel("A2×4", 594, 1682),
        new FormatoPapel("A1×3", 841, 1783),
        new FormatoPapel("A1×4", 841, 2378),
        new FormatoPapel("A0×3", 1189, 2523),
    };

    /// <summary>Tolerancia para comparar tamaños: 0,5 % del lado mayor, como mínimo 2 mm.</summary>
    public static double Tolerancia(double ancho, double alto) => Math.Max(2.0, 0.005 * Math.Max(Math.Abs(ancho), Math.Abs(alto)));

    /// <summary>Formato normalizado que coincide con el tamaño (en cualquier orientación), o null.</summary>
    public static FormatoPapel? Reconocer(double ancho, double alto)
    {
        double menor = Math.Min(ancho, alto), mayor = Math.Max(ancho, alto);
        double tol = Tolerancia(ancho, alto);
        return Normalizados
            .Where(f => Math.Abs(f.LadoMenor - menor) <= tol && Math.Abs(f.LadoMayor - mayor) <= tol)
            .OrderBy(f => Math.Abs(f.LadoMenor - menor) + Math.Abs(f.LadoMayor - mayor))
            .FirstOrDefault();
    }

    /// <summary>"A1 horizontal (841 × 594 mm)" o "900 × 600 mm" si no es un formato normalizado.</summary>
    public static string Describir(double ancho, double alto)
    {
        var inv = CultureInfo.InvariantCulture;
        string medidas = $"{ancho.ToString("0.#", inv)} × {alto.ToString("0.#", inv)} mm";
        FormatoPapel? f = Reconocer(ancho, alto);
        if (f == null) return medidas;
        string orientacion = Math.Abs(ancho - alto) <= Tolerancia(ancho, alto) ? "" : ancho > alto ? " horizontal" : " vertical";
        return $"{f.Nombre}{orientacion} ({medidas})";
    }

    private static readonly Regex TamanoEnNombre = new(
        @"\(\s*(?<a>\d+(?:\.\d+)?)[_\s]*x[_\s]*(?<b>\d+(?:\.\d+)?)[_\s]*(?<u>MM|Millimeters|Inches|in)\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Lee el tamaño del nombre del papel, ej. "ISO_full_bleed_A1_(841.00_x_594.00_MM)" → 841 × 594 mm,
    /// "ANSI_full_bleed_D_(34.00_x_22.00_Inches)" → 863,6 × 558,8 mm.
    /// </summary>
    public static bool TryLeerTamano(string nombre, out double ancho, out double alto)
    {
        ancho = alto = 0;
        if (string.IsNullOrEmpty(nombre)) return false;
        Match m = TamanoEnNombre.Match(nombre);
        if (!m.Success) return false;
        double factor = m.Groups["u"].Value.StartsWith("M", StringComparison.OrdinalIgnoreCase) ? 1.0 : 25.4;
        ancho = double.Parse(m.Groups["a"].Value, CultureInfo.InvariantCulture) * factor;
        alto = double.Parse(m.Groups["b"].Value, CultureInfo.InvariantCulture) * factor;
        return ancho > 0 && alto > 0;
    }

    /// <summary>
    /// Papeles donde entra el membrete, del mejor al peor: primero los de su mismo tamaño, luego los más chicos;
    /// a igual tamaño, sin márgenes y sin girar. Lista vacía si el membrete no entra en ninguno.
    /// </summary>
    public static List<OpcionPapel> Candidatos(double anchoMembrete, double altoMembrete, IEnumerable<MedioPapel> medios)
    {
        if (!(anchoMembrete > 0) || !(altoMembrete > 0))
            throw new ArgumentException("El tamaño del membrete debe ser mayor que cero.");

        double tol = Tolerancia(anchoMembrete, altoMembrete);
        var opciones = new List<OpcionPapel>();
        foreach (MedioPapel medio in medios)
        {
            if (!(medio.Ancho > 0) || !(medio.Alto > 0)) continue;
            foreach (bool girado in new[] { false, true })
            {
                double w = girado ? medio.Alto : medio.Ancho;
                double h = girado ? medio.Ancho : medio.Alto;
                if (w + tol < anchoMembrete || h + tol < altoMembrete) continue;
                bool exacto = Math.Abs(w - anchoMembrete) <= tol && Math.Abs(h - altoMembrete) <= tol;
                opciones.Add(new OpcionPapel(medio, girado, w, h, exacto));
            }
        }

        return opciones
            .OrderByDescending(o => o.Exacto)
            .ThenBy(o => Math.Round(o.AnchoHoja * o.AltoHoja / 100.0))
            .ThenByDescending(o => o.Medio.SinMargenes)
            .ThenBy(o => o.Girado)
            .ThenBy(o => o.Medio.Nombre, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Dónde debe quedar la esquina inferior izquierda del membrete en el espacio papel (mm), con el origen de trazado
    /// en 0,0, para que quede centrado en la hoja; si mide lo mismo que la hoja, sus bordes coinciden con los del papel.
    /// El 0,0 del espacio papel es la esquina del área imprimible, por eso se restan los márgenes no imprimibles
    /// (cero en los papeles "full bleed").
    /// </summary>
    public static (double X, double Y) PosicionMembrete(double anchoMembrete, double altoMembrete,
        double anchoHoja, double altoHoja, double margenIzquierdo = 0, double margenInferior = 0) =>
        ((anchoHoja - anchoMembrete) / 2 - margenIzquierdo,
         (altoHoja - altoMembrete) / 2 - margenInferior);
}
