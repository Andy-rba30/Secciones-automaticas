using System.Globalization;
using System.Text;
using ArbaSecciones.Core.Comun;
using ArbaSecciones.Core.Escalas;

namespace ArbaSecciones.Core.Acomodo;

public sealed class ResultadoAcomodo
{
    internal ResultadoAcomodo(ConversorEscala conversor, ParametrosAcomodo parametros, IReadOnlyList<Lamina> laminas,
        IReadOnlyList<MedidaSeccion> noEntran, double? escalaSugerida)
    {
        Conversor = conversor;
        Parametros = parametros;
        Laminas = laminas;
        NoEntran = noEntran;
        EscalaSugerida = escalaSugerida;
    }

    public ConversorEscala Conversor { get; }
    public ParametrosAcomodo Parametros { get; }
    public IReadOnlyList<Lamina> Laminas { get; }

    /// <summary>Secciones que, solas, no entran en el área útil con esta escala. Si hay alguna no se crea nada.</summary>
    public IReadOnlyList<MedidaSeccion> NoEntran { get; }

    /// <summary>Menor denominador de la lista normalizada con el que entran todas las de <see cref="NoEntran"/>.</summary>
    public double? EscalaSugerida { get; }

    public bool Valido => NoEntran.Count == 0;
    public int TotalSecciones => Laminas.Sum(l => l.Cantidad);

    public string Resumen()
    {
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        if (!Valido)
        {
            sb.AppendLine($"Escala {Conversor.Escala}: {NoEntran.Count} sección(es) no entran solas en el área útil. No se crea nada.");
            foreach (var m in NoEntran)
            {
                var t = m.EnPapel(Conversor);
                sb.AppendLine($"  {FormatoProgresiva.Formatear(m.Progresiva)}  ({t.Ancho.ToString("0.#", inv)} × {t.Alto.ToString("0.#", inv)} {Conversor.UnidadPapel.Abreviatura()})");
            }
            sb.AppendLine(EscalaSugerida.HasValue
                ? $"Escala de menor denominador con la que sí entran: {EscalasNormalizadas.Formatear(EscalaSugerida.Value)}."
                : "Ninguna escala de la lista las hace entrar: revise el área útil o el estilo de la vista (bandas, títulos).");
            return sb.ToString();
        }

        sb.AppendLine($"Escala {Conversor.Escala} · {Laminas.Count} lámina(s) · {TotalSecciones} sección(es) · área útil en modelo " +
                      $"{Conversor.PapelAModelo(Parametros.AnchoUtil).ToString("0.##", inv)} × {Conversor.PapelAModelo(Parametros.AltoUtil).ToString("0.##", inv)} {Conversor.UnidadModelo.Abreviatura()}");
        foreach (var l in Laminas)
        {
            string grupo = l.Grupo.HasValue
                ? $"km {l.Grupo.Value}" + (l.PartesDelGrupo > 1 ? $" ({l.Parte}/{l.PartesDelGrupo})" : string.Empty)
                : "continuo";
            sb.AppendLine($"  Lámina {l.Numero:00} · {grupo} · {FormatoProgresiva.Formatear(l.ProgresivaInicial)} – {FormatoProgresiva.Formatear(l.ProgresivaFinal)} · {l.Cantidad} secciones");
        }
        return sb.ToString();
    }
}

/// <summary>
/// Reparte las secciones en láminas. En modo por km, cada km empieza en lámina nueva, intenta entrar
/// completo y, si no entra, llena la lámina con el máximo posible y pasa el resto a continuaciones.
/// </summary>
public static class Paginador
{
    public static ResultadoAcomodo Paginar(IReadOnlyList<MedidaSeccion> secciones, ConversorEscala conversor, ParametrosAcomodo parametros,
        IReadOnlyList<double>? escalasCandidatas = null)
    {
        ArgumentNullException.ThrowIfNull(secciones);
        ArgumentNullException.ThrowIfNull(conversor);
        ArgumentNullException.ThrowIfNull(parametros);
        var errores = parametros.Validar();
        if (errores.Count > 0) throw new ArgumentException(string.Join(" ", errores), nameof(parametros));

        var items = secciones
            .OrderBy(s => s.Progresiva)
            .Select(s => (Medida: s, Tamano: s.EnPapel(conversor)))
            .ToList();

        var noEntran = items.Where(i => !Empaquetador.EntraSola(i.Tamano, parametros)).Select(i => i.Medida).ToList();
        if (noEntran.Count > 0)
        {
            double? sugerida = SugeridorEscala.MenorEscalaQueContiene(noEntran, conversor, parametros, escalasCandidatas);
            return new ResultadoAcomodo(conversor, parametros, Array.Empty<Lamina>(), noEntran, sugerida);
        }

        if (parametros.UniformarAlturas && parametros.UniformarTodoElTramo)
            items = UniformarTodoElTramo(items);

        var laminas = new List<Lamina>();
        foreach (var grupo in Agrupar(items, parametros))
        {
            int primeraDelGrupo = laminas.Count;
            int desde = 0;
            int parte = 1;
            while (desde < grupo.Items.Count)
            {
                var colocadas = Empaquetador.Llenar(grupo.Items, desde, parametros);
                if (parametros.UniformarAlturas && !parametros.UniformarTodoElTramo && colocadas.Count > 1)
                    colocadas = UniformarLamina(grupo.Items, desde, parametros, colocadas);
                if (colocadas.Count == 0)
                    throw new InvalidOperationException("No se pudo colocar ninguna sección en una lámina vacía."); // no debería pasar: ya se validó que entran solas
                laminas.Add(new Lamina(laminas.Count + 1, grupo.Clave, parte, colocadas));
                desde += colocadas.Count;
                parte++;
            }
            int partes = laminas.Count - primeraDelGrupo;
            for (int i = primeraDelGrupo; i < laminas.Count; i++) laminas[i].PartesDelGrupo = partes;
        }

        return new ResultadoAcomodo(conversor, parametros, laminas, Array.Empty<MedidaSeccion>(), null);
    }

    /// <summary>Todas las secciones con el alto de la más alta.</summary>
    private static List<(MedidaSeccion Medida, Tamano Tamano)> UniformarTodoElTramo(List<(MedidaSeccion Medida, Tamano Tamano)> items)
    {
        if (items.Count == 0) return items;
        double alto = items.Max(i => i.Tamano.Alto);
        return items.Select(i => (Medida: i.Medida, Tamano: new Tamano(i.Tamano.Ancho, alto))).ToList();
    }

    /// <summary>
    /// Iguala el alto de las secciones de una lámina al de la más alta (envolvente máxima). Como al crecer pueden
    /// dejar de caber todas, prueba con el mayor número de secciones consecutivas que sigue entrando ya uniformizado;
    /// las que sobran pasan a la lámina siguiente. Con una sola sección el resultado es ella misma.
    /// </summary>
    private static List<Colocacion> UniformarLamina(List<(MedidaSeccion Medida, Tamano Tamano)> items, int desde, ParametrosAcomodo p,
        List<Colocacion> iniciales)
    {
        for (int k = iniciales.Count; k >= 1; k--)
        {
            var tramo = items.Skip(desde).Take(k).ToList();
            double alto = tramo.Max(i => i.Tamano.Alto);
            var uniformes = tramo.Select(i => (Medida: i.Medida, Tamano: new Tamano(i.Tamano.Ancho, alto))).ToList();
            var colocadas = Empaquetador.Llenar(uniformes, 0, p);
            if (colocadas.Count == k) return colocadas;
        }
        return iniciales;
    }

    /// <summary>Índice de km (o del tramo configurado) de una progresiva. 1+000.00 ya pertenece al km 1.</summary>
    public static int Grupo(double progresiva, double longitudTramo) =>
        (int)Math.Floor(Math.Round(progresiva, 6) / longitudTramo + 1e-12);

    internal sealed record GrupoSecciones(int? Clave, List<(MedidaSeccion Medida, Tamano Tamano)> Items);

    internal static IEnumerable<GrupoSecciones> Agrupar(List<(MedidaSeccion Medida, Tamano Tamano)> items, ParametrosAcomodo p)
    {
        if (p.Paginacion == ModoPaginacion.Continuo)
        {
            if (items.Count > 0) yield return new GrupoSecciones(null, items);
            yield break;
        }

        foreach (var g in items.GroupBy(i => Grupo(i.Medida.Progresiva, p.LongitudTramo)).OrderBy(g => g.Key))
            yield return new GrupoSecciones(g.Key, g.ToList());
    }
}

public static class SugeridorEscala
{
    /// <summary>Menor denominador de la lista con el que cada una de las secciones entra sola en el área útil.</summary>
    public static double? MenorEscalaQueContiene(IEnumerable<MedidaSeccion> secciones, ConversorEscala baseConversor, ParametrosAcomodo p,
        IReadOnlyList<double>? candidatas = null)
    {
        var lista = secciones.ToList();
        foreach (double d in (candidatas ?? EscalasNormalizadas.Denominadores).OrderBy(x => x))
        {
            var c = baseConversor.ConEscala(baseConversor.Escala.ConDenominadorH(d));
            if (lista.All(s => Empaquetador.EntraSola(s.EnPapel(c), p))) return d;
        }
        return null;
    }

    /// <summary>
    /// Menor denominador de la lista con el que cada km entra completo en una sola lámina.
    /// Mantiene la exageración vertical. Null si ninguna escala lo logra.
    /// </summary>
    public static double? MenorEscalaUnaLaminaPorTramo(IReadOnlyList<MedidaSeccion> secciones, ConversorEscala baseConversor, ParametrosAcomodo p,
        IReadOnlyList<double>? candidatas = null)
    {
        if (secciones.Count == 0) return null;
        var porKm = new ParametrosAcomodo
        {
            AnchoUtil = p.AnchoUtil,
            AltoUtil = p.AltoUtil,
            SeparacionH = p.SeparacionH,
            SeparacionV = p.SeparacionV,
            AltoFranjaCuadro = p.AltoFranjaCuadro,
            PosicionFranja = p.PosicionFranja,
            Orden = p.Orden,
            Paginacion = ModoPaginacion.PorKilometro,
            LongitudTramo = p.LongitudTramo,
            UniformarAlturas = p.UniformarAlturas,
            UniformarTodoElTramo = p.UniformarTodoElTramo,
        };

        foreach (double d in (candidatas ?? EscalasNormalizadas.Denominadores).OrderBy(x => x))
        {
            var c = baseConversor.ConEscala(baseConversor.Escala.ConDenominadorH(d));
            var r = Paginador.Paginar(secciones, c, porKm, candidatas);
            if (r.Valido && r.Laminas.All(l => l.PartesDelGrupo == 1)) return d;
        }
        return null;
    }
}
