using System.Globalization;
using System.Text;
using ArbaSecciones.Core.Comun;

namespace ArbaSecciones.Core.Materiales;

/// <summary>Áreas de corte y relleno de una sección.</summary>
public sealed record AreasSeccion(double Progresiva, double Corte, double Relleno);

/// <summary>Fila de la tabla de volúmenes (método de las áreas medias).</summary>
public sealed record FilaVolumen(
    double Progresiva,
    double AreaCorte,
    double AreaRelleno,
    double VolumenCorte,
    double VolumenRelleno,
    double AcumuladoCorte,
    double AcumuladoRelleno,
    double Masa)
{
    public string ProgresivaTexto => FormatoProgresiva.Formatear(Progresiva);
}

public static class CalculadorVolumenes
{
    /// <summary>
    /// Áreas entre el terreno y la superficie de comparación (subrasante), en el tramo donde existen las dos:
    /// corte = terreno por encima de la superficie; relleno = terreno por debajo. Las líneas se tratan como
    /// polilíneas en desplazamiento–elevación; se cortan donde se cruzan.
    /// </summary>
    public static AreasSeccion Areas(double progresiva, IReadOnlyList<PuntoSeccion> terreno, IReadOnlyList<PuntoSeccion> superficie)
    {
        var t = Ordenar(terreno);
        var s = Ordenar(superficie);
        if (t.Count < 2 || s.Count < 2) return new AreasSeccion(progresiva, 0, 0);

        double desde = Math.Max(t[0].Desplazamiento, s[0].Desplazamiento);
        double hasta = Math.Min(t[^1].Desplazamiento, s[^1].Desplazamiento);
        if (hasta <= desde) return new AreasSeccion(progresiva, 0, 0);

        var xs = t.Concat(s).Select(p => p.Desplazamiento)
            .Where(x => x > desde && x < hasta)
            .Append(desde).Append(hasta)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        double corte = 0, relleno = 0;
        for (int i = 0; i + 1 < xs.Count; i++)
        {
            double a = xs[i], b = xs[i + 1];
            if (b - a <= 1e-12) continue;
            double da = Elevacion(t, a, true) - Elevacion(s, a, true);
            double db = Elevacion(t, b, false) - Elevacion(s, b, false);
            if (da >= 0 && db >= 0) corte += (da + db) / 2 * (b - a);
            else if (da <= 0 && db <= 0) relleno += -(da + db) / 2 * (b - a);
            else
            {
                double x = a + (b - a) * da / (da - db); // donde se cruzan
                if (da > 0)
                {
                    corte += da / 2 * (x - a);
                    relleno += -db / 2 * (b - x);
                }
                else
                {
                    relleno += -da / 2 * (x - a);
                    corte += db / 2 * (b - x);
                }
            }
        }
        return new AreasSeccion(progresiva, corte, relleno);
    }

    /// <summary>
    /// Volúmenes por el método de las áreas medias: V = (A1 + A2) / 2 × distancia. Los acumulados llevan los factores
    /// (esponjamiento en el corte, compactación en el relleno) y la masa es acumulado de corte − acumulado de relleno.
    /// </summary>
    public static List<FilaVolumen> AreasMedias(IEnumerable<AreasSeccion> secciones, double factorCorte = 1.0, double factorRelleno = 1.0)
    {
        if (!(factorCorte > 0) || !(factorRelleno > 0)) throw new ArgumentException("Los factores deben ser mayores que cero.");
        var filas = new List<FilaVolumen>();
        AreasSeccion? anterior = null;
        double acumuladoCorte = 0, acumuladoRelleno = 0;
        foreach (AreasSeccion a in secciones.OrderBy(s => s.Progresiva))
        {
            double vc = 0, vr = 0;
            if (anterior != null)
            {
                double distancia = a.Progresiva - anterior.Progresiva;
                vc = (anterior.Corte + a.Corte) / 2 * distancia;
                vr = (anterior.Relleno + a.Relleno) / 2 * distancia;
            }
            acumuladoCorte += vc * factorCorte;
            acumuladoRelleno += vr * factorRelleno;
            filas.Add(new FilaVolumen(a.Progresiva, a.Corte, a.Relleno, vc, vr, acumuladoCorte, acumuladoRelleno, acumuladoCorte - acumuladoRelleno));
            anterior = a;
        }
        return filas;
    }

    public static readonly string[] Encabezados =
    {
        "Progresiva", "Área corte (m²)", "Área relleno (m²)", "Volumen corte (m³)", "Volumen relleno (m³)",
        "Acumulado corte (m³)", "Acumulado relleno (m³)", "Masa (m³)",
    };

    /// <summary>
    /// CSV con separador ";" y los números en la cultura indicada (la de Windows, para que Excel los lea como números).
    /// La codificación (UTF-8 con BOM) la pone quien escribe el archivo.
    /// </summary>
    public static string Csv(IEnumerable<FilaVolumen> filas, CultureInfo cultura, double factorCorte = 1.0, double factorRelleno = 1.0)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(";", Encabezados)).Append("\r\n");
        foreach (FilaVolumen f in filas)
        {
            sb.Append(f.ProgresivaTexto);
            foreach (double v in new[] { f.AreaCorte, f.AreaRelleno, f.VolumenCorte, f.VolumenRelleno, f.AcumuladoCorte, f.AcumuladoRelleno, f.Masa })
                sb.Append(';').Append(v.ToString("0.00", cultura));
            sb.Append("\r\n");
        }
        sb.Append($"Factor de esponjamiento (corte);{factorCorte.ToString("0.00", cultura)}\r\n");
        sb.Append($"Factor de compactación (relleno);{factorRelleno.ToString("0.00", cultura)}\r\n");
        return sb.ToString();
    }

    private static List<PuntoSeccion> Ordenar(IReadOnlyList<PuntoSeccion> puntos) =>
        puntos.Where(p => !double.IsNaN(p.Desplazamiento) && !double.IsNaN(p.Elevacion))
            .OrderBy(p => p.Desplazamiento)
            .ToList();

    /// <summary>
    /// Elevación de la polilínea en x. Si en x hay un tramo vertical, <paramref name="porDerecha"/> elige el valor
    /// de después del salto (al empezar un intervalo) o el de antes (al terminarlo).
    /// </summary>
    internal static double Elevacion(List<PuntoSeccion> p, double x, bool porDerecha)
    {
        if (x <= p[0].Desplazamiento) return p[0].Elevacion;
        if (x >= p[^1].Desplazamiento) return p[^1].Elevacion;
        if (porDerecha)
        {
            for (int i = p.Count - 1; i > 0; i--)
                if (x >= p[i - 1].Desplazamiento && x <= p[i].Desplazamiento) return Interpolar(p[i - 1], p[i], x);
        }
        else
        {
            for (int i = 1; i < p.Count; i++)
                if (x >= p[i - 1].Desplazamiento && x <= p[i].Desplazamiento) return Interpolar(p[i - 1], p[i], x);
        }
        return p[^1].Elevacion;
    }

    private static double Interpolar(PuntoSeccion a, PuntoSeccion b, double x)
    {
        double dx = b.Desplazamiento - a.Desplazamiento;
        return dx <= 1e-12 ? b.Elevacion : a.Elevacion + (b.Elevacion - a.Elevacion) * (x - a.Desplazamiento) / dx;
    }
}
