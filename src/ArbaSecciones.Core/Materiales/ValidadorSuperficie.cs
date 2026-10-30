using System.Globalization;
using System.Text;
using ArbaSecciones.Core.Comun;

namespace ArbaSecciones.Core.Materiales;

/// <summary>Zona entre los extremos de la sección que ningún código elegido cubre.</summary>
public sealed record Hueco(double Desde, double Hasta, IReadOnlyList<string> CodigosPresentes, IReadOnlyList<string> Sugeridos)
{
    public double Ancho => Hasta - Desde;

    public string Mensaje
    {
        get
        {
            var inv = CultureInfo.InvariantCulture;
            string texto = $"Falta cubrir {Ancho.ToString("0.00", inv)} m entre {Desde.ToString("0.00", inv)} y {Hasta.ToString("0.00", inv)}.";
            if (CodigosPresentes.Count > 0)
                texto += $" Ahí están los vínculos con código {string.Join(", ", CodigosPresentes)}.";
            if (Sugeridos.Count > 0)
                texto += $" ¿Agregar {string.Join(", ", Sugeridos)}?";
            return texto;
        }
    }
}

/// <summary>Zona donde la superficie resultante se aparta de la envolvente esperada.</summary>
public sealed record Desvio(double Desde, double Hasta, double Maximo)
{
    public string Mensaje
    {
        get
        {
            var inv = CultureInfo.InvariantCulture;
            string lado = Maximo > 0 ? "por encima" : "por debajo";
            return $"Entre {Desde.ToString("0.00", inv)} y {Hasta.ToString("0.00", inv)} la superficie pasa hasta " +
                   $"{Math.Abs(Maximo).ToString("0.000", inv)} m {lado} de donde debería.";
        }
    }
}

public sealed record ResultadoValidacion(
    double Progresiva,
    SuperficieMateriales Superficie,
    Envolvente Esperada,
    Envolvente Resultante,
    IReadOnlyList<Hueco> Huecos,
    IReadOnlyList<Desvio> Desvios,
    IReadOnlyList<string> Sobrantes,
    IReadOnlyList<string> Ausentes,
    bool NoLlegaIzquierda,
    bool NoLlegaDerecha)
{
    /// <summary>Sin huecos: la superficie va de un extremo al otro de la sección.</summary>
    public bool Continua => Huecos.Count == 0;

    public bool SinProblemas => Continua && Desvios.Count == 0;

    /// <summary>Códigos que conviene agregar para tapar los huecos.</summary>
    public IReadOnlyList<string> Sugerencias => Huecos
        .SelectMany(h => h.Sugeridos)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    public IEnumerable<string> Problemas()
    {
        var inv = CultureInfo.InvariantCulture;
        if (NoLlegaIzquierda)
            yield return $"No llega al extremo izquierdo ({Esperada.Desde.ToString("0.00", inv)}): falta el daylight de ese lado.";
        if (NoLlegaDerecha)
            yield return $"No llega al extremo derecho ({Esperada.Hasta.ToString("0.00", inv)}): falta el daylight de ese lado.";
        foreach (Hueco h in Huecos) yield return h.Mensaje;
        foreach (Desvio d in Desvios) yield return d.Mensaje;
        foreach (string c in Sobrantes) yield return $"El código {c} no aporta nada a la {Nombre(Superficie)} en esta progresiva.";
    }

    public string Resumen()
    {
        var sb = new StringBuilder();
        string nombre = Superficie == SuperficieMateriales.Rasante ? "Rasante" : "SubRasante";
        sb.Append(FormatoProgresiva.Formatear(Progresiva)).Append("  ").Append(nombre).Append(": ");
        sb.Append(Continua ? (SinProblemas ? "✔ continua" : "✔ continua, con desvíos") : $"✖ {Huecos.Count} hueco(s)");
        foreach (string p in Problemas()) sb.AppendLine().Append("    · ").Append(p);
        if (Ausentes.Count > 0)
            sb.AppendLine().Append("    · No aparecen en esta progresiva: ").Append(string.Join(", ", Ausentes)).Append(" (pueden estar en otras regiones).");
        return sb.ToString();
    }

    private static string Nombre(SuperficieMateriales s) => s == SuperficieMateriales.Rasante ? "rasante" : "subrasante";
}

/// <summary>
/// Compara la línea que formarían los códigos elegidos (con su corrección de voladizo) con la envolvente esperada
/// y reporta huecos, desvíos, códigos sobrantes y extremos sin cubrir.
/// </summary>
public static class ValidadorSuperficie
{
    /// <summary>Rasante = envolvente superior; SubRasante = envolvente inferior.</summary>
    public static bool EsSuperior(SuperficieMateriales s) => s == SuperficieMateriales.Rasante;

    /// <summary>
    /// Línea que tendría la superficie del corredor con esos códigos: donde se superponen vínculos,
    /// "Top Links" toma el más alto y "Bottom Links" el más bajo. Sin corrección se toma el de la superficie esperada.
    /// </summary>
    public static Envolvente LineaSuperficie(SeccionCorredor seccion, IEnumerable<string> codigos, CorreccionVoladizo correccion,
        SuperficieMateriales superficie, OpcionesEnvolvente? opciones = null)
    {
        var lista = codigos.ToList();
        bool superior = correccion switch
        {
            CorreccionVoladizo.VinculosSuperiores => true,
            CorreccionVoladizo.VinculosInferiores => false,
            _ => EsSuperior(superficie),
        };
        return DetectorEnvolvente.Calcular(seccion.Vinculos.Where(v => v.TieneAlguno(lista)), superior, opciones);
    }

    public static ResultadoValidacion Validar(SeccionCorredor seccion, IEnumerable<string> codigos, CorreccionVoladizo correccion,
        SuperficieMateriales superficie, OpcionesEnvolvente? opciones = null)
    {
        opciones ??= new OpcionesEnvolvente();
        var elegidos = codigos.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Envolvente esperada = DetectorEnvolvente.Calcular(seccion.Vinculos, EsSuperior(superficie), opciones);
        Envolvente resultante = LineaSuperficie(seccion, elegidos, correccion, superficie, opciones);

        var porVinculo = seccion.Vinculos.ToDictionary(v => v.Indice);
        var coberturaEsperada = esperada.Cobertura();
        var coberturaResultante = resultante.Cobertura();

        // Huecos: lo esperado que la superficie no cubre.
        var huecos = new List<Hueco>();
        Dictionary<string, double> fuera = DetectorEnvolvente.LongitudFueraPorCodigo(seccion, esperada, opciones);
        foreach (var (desde, hasta) in Intervalos.Restar(coberturaEsperada, coberturaResultante))
        {
            if (hasta - desde <= opciones.ToleranciaHueco) continue;
            var presentes = seccion.Vinculos
                .Where(v => v.AvanceHorizontal > opciones.ToleranciaVertical &&
                            Intervalos.Solape(v.DesdeX, v.HastaX, desde, hasta) > opciones.ToleranciaHueco / 2)
                .SelectMany(v => v.Codigos)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var tramosHueco = esperada.Tramos
                .Where(t => Intervalos.Solape(t.Desde, t.Hasta, desde, hasta) > 1e-9)
                .Select(t => t with
                {
                    Desde = Math.Max(t.Desde, desde),
                    Hasta = Math.Min(t.Hasta, hasta),
                    ElevacionDesde = t.ElevacionEn(Math.Max(t.Desde, desde)),
                    ElevacionHasta = t.ElevacionEn(Math.Min(t.Hasta, hasta)),
                })
                .ToList();
            var sugeridos = DetectorEnvolvente.CubrirConCodigos(seccion, tramosHueco, fuera)
                .Where(c => !elegidos.Contains(c, StringComparer.OrdinalIgnoreCase))
                .ToList();
            huecos.Add(new Hueco(desde, hasta, presentes, sugeridos));
        }

        // Desvíos: donde hay las dos líneas y se separan más que la tolerancia.
        var desvios = new List<Desvio>();
        var cortes = esperada.Tramos.SelectMany(t => new[] { t.Desde, t.Hasta })
            .Concat(resultante.Tramos.SelectMany(t => new[] { t.Desde, t.Hasta }))
            .OrderBy(x => x)
            .ToList();
        (double Desde, double Hasta, double Maximo)? abierto = null;
        for (int i = 0; i + 1 < cortes.Count; i++)
        {
            double a = cortes[i], b = cortes[i + 1];
            if (b - a <= 1e-7) continue;
            double m = (a + b) / 2;
            double? em = esperada.ElevacionEn(m), rm = resultante.ElevacionEn(m);
            double? diferencia = null;
            if (em.HasValue && rm.HasValue)
            {
                double da = (resultante.ElevacionEn(a + 1e-9) ?? rm.Value) - (esperada.ElevacionEn(a + 1e-9) ?? em.Value);
                double db = (resultante.ElevacionEn(b - 1e-9) ?? rm.Value) - (esperada.ElevacionEn(b - 1e-9) ?? em.Value);
                double peor = Math.Abs(da) >= Math.Abs(db) ? da : db;
                if (Math.Abs(peor) > opciones.ToleranciaDesvio) diferencia = peor;
            }
            if (diferencia.HasValue)
            {
                if (abierto.HasValue && Math.Abs(abierto.Value.Hasta - a) <= 1e-6)
                {
                    var p = abierto.Value;
                    abierto = (p.Desde, b, Math.Abs(diferencia.Value) > Math.Abs(p.Maximo) ? diferencia.Value : p.Maximo);
                }
                else
                {
                    if (abierto.HasValue) desvios.Add(new Desvio(abierto.Value.Desde, abierto.Value.Hasta, abierto.Value.Maximo));
                    abierto = (a, b, diferencia.Value);
                }
            }
        }
        if (abierto.HasValue) desvios.Add(new Desvio(abierto.Value.Desde, abierto.Value.Hasta, abierto.Value.Maximo));
        desvios.RemoveAll(d => d.Hasta - d.Desde <= opciones.ToleranciaHueco);

        // Códigos elegidos que no aportan a la línea, o que no están en esta sección.
        var enSeccion = new HashSet<string>(seccion.CodigosVinculo, StringComparer.OrdinalIgnoreCase);
        var ausentes = elegidos.Where(c => !enSeccion.Contains(c)).ToList();
        var sobrantes = elegidos
            .Where(enSeccion.Contains)
            .Where(c => DetectorEnvolvente.LongitudEnEnvolvente(c, resultante.Tramos, porVinculo) <= opciones.ToleranciaHueco)
            .ToList();

        bool noLlegaIzquierda = !esperada.Vacia && (resultante.Vacia || resultante.Desde > esperada.Desde + opciones.ToleranciaHueco);
        bool noLlegaDerecha = !esperada.Vacia && (resultante.Vacia || resultante.Hasta < esperada.Hasta - opciones.ToleranciaHueco);

        return new ResultadoValidacion(seccion.Progresiva, superficie, esperada, resultante, huecos, desvios, sobrantes, ausentes,
            noLlegaIzquierda, noLlegaDerecha);
    }
}

/// <summary>Progresiva elegida para revisar y por qué.</summary>
public sealed record ProgresivaRevision(double Progresiva, string Motivo);

/// <summary>Qué progresivas revisar al validar todo el corredor.</summary>
public static class PlanValidacion
{
    /// <summary>
    /// Inicio, medio y fin de cada región; la progresiva con más corte y la de más relleno (diferencia = rasante − terreno
    /// en el eje: negativa en corte, positiva en relleno); y, si se pide, todas las del grupo de muestreo.
    /// Las que caen a menos de <paramref name="tolerancia"/> se juntan.
    /// </summary>
    public static List<ProgresivaRevision> Elegir(
        IEnumerable<(double Inicio, double Fin)> regiones,
        IEnumerable<(double Progresiva, double Diferencia)> diferencias,
        IEnumerable<double> grupoMuestreo,
        bool incluirGrupo,
        double tolerancia = 0.01)
    {
        var lista = new List<ProgresivaRevision>();
        int n = 0;
        foreach (var (inicio, fin) in regiones)
        {
            n++;
            lista.Add(new ProgresivaRevision(inicio, $"inicio de la región {n}"));
            lista.Add(new ProgresivaRevision((inicio + fin) / 2, $"medio de la región {n}"));
            lista.Add(new ProgresivaRevision(fin, $"fin de la región {n}"));
        }

        var d = diferencias.ToList();
        var corte = d.Where(x => x.Diferencia < 0).OrderBy(x => x.Diferencia).FirstOrDefault();
        if (d.Any(x => x.Diferencia < 0)) lista.Add(new ProgresivaRevision(corte.Progresiva, "en corte"));
        var relleno = d.Where(x => x.Diferencia > 0).OrderByDescending(x => x.Diferencia).FirstOrDefault();
        if (d.Any(x => x.Diferencia > 0)) lista.Add(new ProgresivaRevision(relleno.Progresiva, "en relleno"));

        if (incluirGrupo)
            lista.AddRange(grupoMuestreo.Select(p => new ProgresivaRevision(p, "grupo de muestreo")));

        var resultado = new List<ProgresivaRevision>();
        foreach (ProgresivaRevision r in lista.OrderBy(r => r.Progresiva))
        {
            if (resultado.Count > 0 && r.Progresiva - resultado[^1].Progresiva <= tolerancia)
            {
                ProgresivaRevision p = resultado[^1];
                if (!p.Motivo.Split(", ").Contains(r.Motivo)) resultado[^1] = p with { Motivo = p.Motivo + ", " + r.Motivo };
                continue;
            }
            resultado.Add(r);
        }
        return resultado;
    }
}
