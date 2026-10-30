using System.Globalization;

namespace ArbaSecciones.Core.Materiales;

/// <summary>
/// Tramo de una envolvente: entre <see cref="Desde"/> y <see cref="Hasta"/> la línea es recta y la forman los vínculos
/// <see cref="Vinculos"/> (índices de <see cref="VinculoSeccion.Indice"/>; varios si coinciden).
/// </summary>
public sealed record TramoEnvolvente(double Desde, double Hasta, double ElevacionDesde, double ElevacionHasta, IReadOnlyList<int> Vinculos)
{
    public double Ancho => Hasta - Desde;

    public double ElevacionEn(double x) =>
        Ancho <= 0 ? ElevacionDesde : ElevacionDesde + (ElevacionHasta - ElevacionDesde) * (x - Desde) / Ancho;
}

/// <summary>Línea superior o inferior de un conjunto de vínculos, de izquierda a derecha. Puede tener huecos.</summary>
public sealed class Envolvente
{
    public Envolvente(bool superior, IReadOnlyList<TramoEnvolvente> tramos)
    {
        Superior = superior;
        Tramos = tramos;
    }

    public bool Superior { get; }
    public IReadOnlyList<TramoEnvolvente> Tramos { get; }
    public bool Vacia => Tramos.Count == 0;
    public double Desde => Vacia ? 0 : Tramos[0].Desde;
    public double Hasta => Vacia ? 0 : Tramos[^1].Hasta;

    /// <summary>Elevación de la envolvente en x, o null si x cae en un hueco o fuera de ella.</summary>
    public double? ElevacionEn(double x, double tolerancia = 1e-9)
    {
        foreach (TramoEnvolvente t in Tramos)
            if (x >= t.Desde - tolerancia && x <= t.Hasta + tolerancia) return t.ElevacionEn(Math.Clamp(x, t.Desde, t.Hasta));
        return null;
    }

    /// <summary>Intervalos cubiertos, ya unidos.</summary>
    public IReadOnlyList<(double Desde, double Hasta)> Cobertura(double tolerancia = 1e-6) =>
        Intervalos.Unir(Tramos.Select(t => (t.Desde, t.Hasta)), tolerancia);

    /// <summary>Polilíneas para dibujar (una por cada parte continua).</summary>
    public IReadOnlyList<IReadOnlyList<PuntoSeccion>> Polilineas(double tolerancia = 1e-6)
    {
        var resultado = new List<IReadOnlyList<PuntoSeccion>>();
        List<PuntoSeccion>? actual = null;
        TramoEnvolvente? anterior = null;
        foreach (TramoEnvolvente t in Tramos)
        {
            bool continua = anterior != null && Math.Abs(anterior.Hasta - t.Desde) <= tolerancia &&
                            Math.Abs(anterior.ElevacionHasta - t.ElevacionDesde) <= 1e-3;
            if (!continua)
            {
                actual = new List<PuntoSeccion> { new(t.Desde, t.ElevacionDesde) };
                resultado.Add(actual);
            }
            actual!.Add(new PuntoSeccion(t.Hasta, t.ElevacionHasta));
            anterior = t;
        }
        return resultado;
    }

    /// <summary>Ancho horizontal que aporta cada vínculo.</summary>
    public Dictionary<int, double> AnchoPorVinculo()
    {
        var d = new Dictionary<int, double>();
        foreach (TramoEnvolvente t in Tramos)
            foreach (int v in t.Vinculos)
                d[v] = d.GetValueOrDefault(v) + t.Ancho;
        return d;
    }
}

/// <summary>Código propuesto para una superficie y cuánto aporta.</summary>
public sealed record CodigoSugerido(string Codigo, double LongitudEnvolvente, double LongitudFuera)
{
    /// <summary>El código también está en vínculos que no forman la envolvente: al agregarlo, los arrastra a la superficie.</summary>
    public bool ArrastraFuera(double tolerancia = 0.01) => LongitudFuera > tolerancia;
}

public sealed record ResultadoDeteccion(Envolvente Envolvente, IReadOnlyList<CodigoSugerido> Codigos, IReadOnlyList<string> Avisos)
{
    public IReadOnlyList<string> NombresCodigos => Codigos.Select(c => c.Codigo).ToList();
}

/// <summary>Tolerancias de la detección y la validación (en unidades del dibujo, normalmente metros).</summary>
public sealed class OpcionesEnvolvente
{
    /// <summary>Un tramo que avanza menos que esto en horizontal se considera vertical: se dibuja pero no se elige.</summary>
    public double ToleranciaVertical { get; set; } = 0.001;

    /// <summary>Dos vínculos a menos de esta distancia vertical se consideran la misma línea.</summary>
    public double ToleranciaElevacion { get; set; } = 0.001;

    /// <summary>Huecos más angostos que esto no se informan.</summary>
    public double ToleranciaHueco { get; set; } = 0.01;

    /// <summary>Diferencia de elevación con la envolvente esperada a partir de la cual se informa un desvío.</summary>
    public double ToleranciaDesvio { get; set; } = 0.01;
}

/// <summary>
/// Envolventes superior (rasante) e inferior (subrasante) de una sección y los códigos que las forman,
/// sin depender de los nombres de los códigos.
/// </summary>
public static class DetectorEnvolvente
{
    private const double Eps = 1e-9;

    private readonly record struct Segmento(int Vinculo, double X1, double Z1, double X2, double Z2)
    {
        public double Z(double x) => Z1 + (Z2 - Z1) * (x - X1) / (X2 - X1);
        public bool Cubre(double x) => x > X1 + Eps && x < X2 - Eps;
    }

    /// <summary>
    /// Envolvente de los vínculos: en cada intervalo de desplazamiento, el vínculo más alto (superior) o el más bajo.
    /// Los vínculos verticales se ignoran para elegir. Se cortan los intervalos también donde dos vínculos se cruzan,
    /// para que dentro de cada intervalo el orden de los vínculos no cambie.
    /// </summary>
    public static Envolvente Calcular(IEnumerable<VinculoSeccion> vinculos, bool superior, OpcionesEnvolvente? opciones = null)
    {
        opciones ??= new OpcionesEnvolvente();
        var segmentos = new List<Segmento>();
        foreach (VinculoSeccion v in vinculos)
        {
            foreach (var (a, b) in v.Segmentos())
            {
                if (Math.Abs(b.Desplazamiento - a.Desplazamiento) <= opciones.ToleranciaVertical) continue;
                segmentos.Add(a.Desplazamiento < b.Desplazamiento
                    ? new Segmento(v.Indice, a.Desplazamiento, a.Elevacion, b.Desplazamiento, b.Elevacion)
                    : new Segmento(v.Indice, b.Desplazamiento, b.Elevacion, a.Desplazamiento, a.Elevacion));
            }
        }
        if (segmentos.Count == 0) return new Envolvente(superior, Array.Empty<TramoEnvolvente>());

        var cortes = new List<double>();
        foreach (Segmento s in segmentos)
        {
            cortes.Add(s.X1);
            cortes.Add(s.X2);
        }
        // Cruces entre vínculos: ahí puede cambiar cuál es el más alto o el más bajo.
        for (int i = 0; i < segmentos.Count; i++)
        {
            for (int j = i + 1; j < segmentos.Count; j++)
            {
                Segmento p = segmentos[i], q = segmentos[j];
                double a = Math.Max(p.X1, q.X1), b = Math.Min(p.X2, q.X2);
                if (b - a <= Eps) continue;
                double da = p.Z(a) - q.Z(a), db = p.Z(b) - q.Z(b);
                if (da * db < 0)
                {
                    double x = a + (b - a) * da / (da - db);
                    if (x > a + Eps && x < b - Eps) cortes.Add(x);
                }
            }
        }
        cortes.Sort();
        var xs = new List<double>();
        foreach (double c in cortes)
            if (xs.Count == 0 || c - xs[^1] > 1e-7) xs.Add(c);

        var tramos = new List<TramoEnvolvente>();
        for (int i = 0; i + 1 < xs.Count; i++)
        {
            double a = xs[i], b = xs[i + 1], m = (a + b) / 2;
            Segmento? mejor = null;
            double zMejor = 0;
            foreach (Segmento s in segmentos)
            {
                if (!s.Cubre(m)) continue;
                double z = s.Z(m);
                if (mejor == null || (superior ? z > zMejor : z < zMejor))
                {
                    mejor = s;
                    zMejor = z;
                }
            }
            if (mejor == null) continue; // hueco: ningún vínculo pasa por aquí

            Segmento elegido = mejor.Value;
            double za = elegido.Z(a), zb = elegido.Z(b);
            var coincidentes = segmentos
                .Where(s => s.Cubre(m) &&
                            Math.Abs(s.Z(a) - za) <= opciones.ToleranciaElevacion &&
                            Math.Abs(s.Z(b) - zb) <= opciones.ToleranciaElevacion)
                .Select(s => s.Vinculo)
                .Distinct()
                .OrderBy(v => v)
                .ToList();
            tramos.Add(new TramoEnvolvente(a, b, za, zb, coincidentes));
        }
        return new Envolvente(superior, Unir(tramos));
    }

    /// <summary>Une tramos seguidos que forman la misma recta con los mismos vínculos.</summary>
    private static List<TramoEnvolvente> Unir(List<TramoEnvolvente> tramos)
    {
        var r = new List<TramoEnvolvente>();
        foreach (TramoEnvolvente t in tramos)
        {
            if (r.Count > 0)
            {
                TramoEnvolvente p = r[^1];
                bool seguido = Math.Abs(p.Hasta - t.Desde) <= 1e-7 && Math.Abs(p.ElevacionHasta - t.ElevacionDesde) <= 1e-7;
                bool mismos = p.Vinculos.SequenceEqual(t.Vinculos);
                double pendienteP = (p.ElevacionHasta - p.ElevacionDesde) / p.Ancho;
                double pendienteT = (t.ElevacionHasta - t.ElevacionDesde) / t.Ancho;
                if (seguido && mismos && Math.Abs(pendienteP - pendienteT) <= 1e-7)
                {
                    r[^1] = p with { Hasta = t.Hasta, ElevacionHasta = t.ElevacionHasta };
                    continue;
                }
            }
            r.Add(t);
        }
        return r;
    }

    /// <summary>
    /// Códigos que forman la envolvente superior (rasante) o inferior (subrasante), ordenados por la longitud
    /// horizontal que aportan. Se elige el menor grupo de códigos que la cubre entera, prefiriendo el código que cubre
    /// más envolvente y menos vínculos fuera de ella.
    /// </summary>
    public static ResultadoDeteccion Detectar(SeccionCorredor seccion, bool superior, OpcionesEnvolvente? opciones = null)
    {
        opciones ??= new OpcionesEnvolvente();
        Envolvente envolvente = Calcular(seccion.Vinculos, superior, opciones);
        var avisos = new List<string>();
        var inv = CultureInfo.InvariantCulture;
        string nombre = superior ? "rasante" : "subrasante";

        if (envolvente.Vacia)
        {
            avisos.Add("La sección no tiene vínculos con avance horizontal.");
            return new ResultadoDeteccion(envolvente, Array.Empty<CodigoSugerido>(), avisos);
        }

        Dictionary<string, double> fuera = LongitudFueraPorCodigo(seccion, envolvente, opciones);
        List<string> elegidos = CubrirConCodigos(seccion, envolvente.Tramos, fuera);

        var porVinculo = seccion.Vinculos.ToDictionary(v => v.Indice);
        var sugeridos = elegidos
            .Select(c => new CodigoSugerido(c, LongitudEnEnvolvente(c, envolvente.Tramos, porVinculo), fuera.GetValueOrDefault(c)))
            .OrderByDescending(c => c.LongitudEnvolvente)
            .ThenBy(c => c.Codigo, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (CodigoSugerido c in sugeridos.Where(c => c.ArrastraFuera(opciones.ToleranciaHueco)))
            avisos.Add($"El código {c.Codigo} también está en vínculos que no forman la {nombre} ({c.LongitudFuera.ToString("0.00", inv)} m): " +
                       "al agregarlo a la superficie arrastraría esos vínculos.");

        var cobertura = envolvente.Cobertura();
        for (int i = 1; i < cobertura.Count; i++)
        {
            double desde = cobertura[i - 1].Hasta, hasta = cobertura[i].Desde;
            if (hasta - desde > opciones.ToleranciaHueco)
                avisos.Add($"La propia sección no tiene vínculos entre {desde.ToString("0.00", inv)} y {hasta.ToString("0.00", inv)}.");
        }
        return new ResultadoDeteccion(envolvente, sugeridos, avisos);
    }

    internal static double LongitudEnEnvolvente(string codigo, IEnumerable<TramoEnvolvente> tramos, IReadOnlyDictionary<int, VinculoSeccion> vinculos) =>
        tramos.Where(t => t.Vinculos.Any(v => vinculos.TryGetValue(v, out var vs) && vs.Tiene(codigo))).Sum(t => t.Ancho);

    /// <summary>Por código: ancho horizontal de sus vínculos que NO está sobre la envolvente.</summary>
    internal static Dictionary<string, double> LongitudFueraPorCodigo(SeccionCorredor seccion, Envolvente envolvente, OpcionesEnvolvente opciones)
    {
        Dictionary<int, double> sobre = envolvente.AnchoPorVinculo();
        var fuera = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (VinculoSeccion v in seccion.Vinculos)
        {
            double resto = Math.Max(0, v.AvanceHorizontal - sobre.GetValueOrDefault(v.Indice));
            if (v.AvanceHorizontal <= opciones.ToleranciaVertical) resto = 0;
            foreach (string c in v.Codigos.Distinct(StringComparer.OrdinalIgnoreCase))
                fuera[c] = fuera.GetValueOrDefault(c) + resto;
        }
        return fuera;
    }

    /// <summary>
    /// Elige códigos hasta cubrir todos los tramos: en cada paso, el que cubre más ancho todavía sin cubrir menos su
    /// ancho fuera de la envolvente; a igualdad, el que cubre más envolvente en total.
    /// </summary>
    internal static List<string> CubrirConCodigos(SeccionCorredor seccion, IReadOnlyList<TramoEnvolvente> tramos, IReadOnlyDictionary<string, double> fuera)
    {
        var porVinculo = seccion.Vinculos.ToDictionary(v => v.Indice);
        var codigosTramo = tramos
            .Select(t => t.Vinculos
                .Where(porVinculo.ContainsKey)
                .SelectMany(v => porVinculo[v].Codigos)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList())
            .ToList();
        var total = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < tramos.Count; i++)
            foreach (string c in codigosTramo[i])
                total[c] = total.GetValueOrDefault(c) + tramos[i].Ancho;

        var sinCubrir = new HashSet<int>(Enumerable.Range(0, tramos.Count).Where(i => codigosTramo[i].Count > 0));
        var elegidos = new List<string>();
        while (sinCubrir.Count > 0)
        {
            var ganancia = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (int i in sinCubrir)
                foreach (string c in codigosTramo[i])
                    ganancia[c] = ganancia.GetValueOrDefault(c) + tramos[i].Ancho;
            if (ganancia.Count == 0) break;

            string mejor = ganancia
                .OrderByDescending(g => g.Value - fuera.GetValueOrDefault(g.Key))
                .ThenByDescending(g => total.GetValueOrDefault(g.Key))
                .ThenBy(g => fuera.GetValueOrDefault(g.Key))
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .First().Key;
            elegidos.Add(mejor);
            sinCubrir.RemoveWhere(i => codigosTramo[i].Contains(mejor, StringComparer.OrdinalIgnoreCase));
        }
        return elegidos;
    }
}

/// <summary>Operaciones con intervalos [desde, hasta].</summary>
public static class Intervalos
{
    public static List<(double Desde, double Hasta)> Unir(IEnumerable<(double Desde, double Hasta)> intervalos, double tolerancia = 1e-6)
    {
        var r = new List<(double Desde, double Hasta)>();
        foreach (var (d, h) in intervalos.Where(i => i.Hasta > i.Desde).OrderBy(i => i.Desde))
        {
            if (r.Count > 0 && d <= r[^1].Hasta + tolerancia) r[^1] = (r[^1].Desde, Math.Max(r[^1].Hasta, h));
            else r.Add((d, h));
        }
        return r;
    }

    /// <summary>Partes de <paramref name="a"/> que no cubre <paramref name="b"/>.</summary>
    public static List<(double Desde, double Hasta)> Restar(IEnumerable<(double Desde, double Hasta)> a, IEnumerable<(double Desde, double Hasta)> b, double tolerancia = 1e-6)
    {
        var quita = Unir(b, tolerancia);
        var r = new List<(double Desde, double Hasta)>();
        foreach (var (d0, h0) in Unir(a, tolerancia))
        {
            double d = d0;
            foreach (var (qd, qh) in quita)
            {
                if (qh <= d || qd >= h0) continue;
                if (qd > d) r.Add((d, qd));
                d = Math.Max(d, qh);
                if (d >= h0) break;
            }
            if (d < h0) r.Add((d, h0));
        }
        return r.Where(i => i.Hasta - i.Desde > tolerancia).ToList();
    }

    public static double Solape(double d1, double h1, double d2, double h2) => Math.Max(0, Math.Min(h1, h2) - Math.Max(d1, d2));
}
