using ArbaSecciones.Core.Comun;

namespace ArbaSecciones.Core.Progresivas;

/// <summary>
/// Calcula las progresivas de las líneas de muestreo: intervalos distintos en tangentes y curvas,
/// puntos notables, progresivas manuales y eliminación de duplicados con tolerancia.
/// </summary>
public static class GeneradorProgresivas
{
    /// <summary>Tope de seguridad para no colgar Civil 3D con un intervalo mal escrito (ej. 0.001).</summary>
    public const int MaximoProgresivas = 20_000;

    public static ResultadoProgresivas Generar(IReadOnlyList<TramoAlineamiento> tramos, OpcionesMuestreo opciones)
    {
        ArgumentNullException.ThrowIfNull(tramos);
        ArgumentNullException.ThrowIfNull(opciones);

        var errores = opciones.Validar();
        if (errores.Count > 0) throw new ArgumentException(string.Join(" ", errores), nameof(opciones));

        var ordenados = tramos
            .Where(t => t.Fin - t.Inicio > 1e-9)
            .OrderBy(t => t.Inicio)
            .ToList();
        if (ordenados.Count == 0) throw new ArgumentException("El alineamiento no tiene elementos con longitud.", nameof(tramos));

        double tol = opciones.Tolerancia;
        var avisos = new List<string>();

        double inicioAlineamiento = ordenados[0].Inicio;
        double finAlineamiento = ordenados[^1].Fin;

        double a = opciones.ProgresivaInicial ?? inicioAlineamiento;
        double b = opciones.ProgresivaFinal ?? finAlineamiento;
        if (a < inicioAlineamiento - tol)
        {
            avisos.Add($"La progresiva inicial {FormatoProgresiva.Formatear(a)} está antes del alineamiento; se usa {FormatoProgresiva.Formatear(inicioAlineamiento)}.");
            a = inicioAlineamiento;
        }
        if (b > finAlineamiento + tol)
        {
            avisos.Add($"La progresiva final {FormatoProgresiva.Formatear(b)} está después del alineamiento; se usa {FormatoProgresiva.Formatear(finAlineamiento)}.");
            b = finAlineamiento;
        }
        a = Math.Max(a, inicioAlineamiento);
        b = Math.Min(b, finAlineamiento);
        if (b - a <= tol) throw new ArgumentException("El rango de progresivas está vacío o fuera del alineamiento.", nameof(opciones));

        var candidatos = new List<ProgresivaMuestreo>();

        // 1) Progresivas regulares, con el intervalo de cada tipo de elemento.
        foreach (var tramo in ordenados)
        {
            double s0 = Math.Max(tramo.Inicio, a);
            double s1 = Math.Min(tramo.Fin, b);
            if (s1 < s0 - 1e-9) continue;

            double intervalo = tramo.Tipo == TipoTramo.Tangente ? opciones.IntervaloTangente : opciones.IntervaloCurva;
            double origen = opciones.Modo == ModoProgresivas.MultiplosRedondos ? 0.0 : tramo.Inicio;

            long kInicial = (long)Math.Ceiling((s0 - origen) / intervalo - 1e-9);
            long kFinal = (long)Math.Floor((s1 - origen) / intervalo + 1e-9);
            if (kFinal - kInicial > MaximoProgresivas)
                throw new ArgumentException($"El intervalo produce más de {MaximoProgresivas} progresivas. Revise los intervalos.", nameof(opciones));

            for (long k = kInicial; k <= kFinal; k++)
            {
                double s = Math.Round(origen + k * intervalo, 6);
                if (s < s0 - 1e-6 || s > s1 + 1e-6) continue;
                candidatos.Add(new ProgresivaMuestreo(s, TipoProgresiva.Regular, tramo.Tipo));
            }
        }

        // 2) Puntos notables en las uniones entre elementos.
        for (int i = 0; i < ordenados.Count - 1; i++)
        {
            var actual = ordenados[i];
            var siguiente = ordenados[i + 1];
            double hueco = siguiente.Inicio - actual.Fin;
            if (Math.Abs(hueco) > Math.Max(tol, 1e-3))
                avisos.Add($"Discontinuidad entre {FormatoProgresiva.Formatear(actual.Fin)} y {FormatoProgresiva.Formatear(siguiente.Inicio)}.");

            var tipo = Clasificar(actual.Tipo, siguiente.Tipo);
            double s = actual.Fin;
            if (opciones.Incluye(tipo) && s >= a - tol && s <= b + tol)
                candidatos.Add(new ProgresivaMuestreo(Math.Clamp(s, a, b), tipo, siguiente.Tipo));
        }

        // 3) Inicio y fin del rango.
        if (opciones.IncluirInicio) candidatos.Add(new ProgresivaMuestreo(a, TipoProgresiva.Inicio, null));
        if (opciones.IncluirFin) candidatos.Add(new ProgresivaMuestreo(b, TipoProgresiva.Fin, null));

        // 4) Progresivas manuales.
        foreach (double m in opciones.Adicionales ?? new List<double>())
        {
            if (m >= a - tol && m <= b + tol) candidatos.Add(new ProgresivaMuestreo(Math.Clamp(m, a, b), TipoProgresiva.Manual, null));
            else avisos.Add($"La progresiva manual {FormatoProgresiva.Formatear(m)} queda fuera del rango y no se usa.");
        }

        var unicas = EliminarDuplicados(candidatos, tol);
        var conTramo = unicas.Select(p => p with { Tramo = TramoEn(ordenados, p.Progresiva, tol) }).ToList();
        return new ResultadoProgresivas(a, b, conTramo, avisos);
    }

    /// <summary>Nombre del punto en la unión de dos elementos.</summary>
    public static TipoProgresiva Clasificar(TipoTramo anterior, TipoTramo siguiente) => (anterior, siguiente) switch
    {
        (TipoTramo.Tangente, TipoTramo.Curva) => TipoProgresiva.PC,
        (TipoTramo.Curva, TipoTramo.Tangente) => TipoProgresiva.PT,
        (TipoTramo.Tangente, TipoTramo.Espiral) => TipoProgresiva.TS,
        (TipoTramo.Espiral, TipoTramo.Curva) => TipoProgresiva.SC,
        (TipoTramo.Curva, TipoTramo.Espiral) => TipoProgresiva.CS,
        (TipoTramo.Espiral, TipoTramo.Tangente) => TipoProgresiva.ST,
        (TipoTramo.Curva, TipoTramo.Curva) => TipoProgresiva.PCC,
        (TipoTramo.Espiral, TipoTramo.Espiral) => TipoProgresiva.SS,
        _ => TipoProgresiva.PI,
    };

    /// <summary>Prioridad al fusionar duplicados: inicio/fin, luego puntos notables, manuales y regulares.</summary>
    internal static int Prioridad(TipoProgresiva tipo) => tipo switch
    {
        TipoProgresiva.Inicio or TipoProgresiva.Fin => 0,
        TipoProgresiva.Manual => 2,
        TipoProgresiva.Regular => 3,
        _ => 1,
    };

    internal static List<ProgresivaMuestreo> EliminarDuplicados(IEnumerable<ProgresivaMuestreo> candidatos, double tolerancia)
    {
        var ordenadas = candidatos
            .OrderBy(c => c.Progresiva)
            .ThenBy(c => Prioridad(c.Tipo))
            .ToList();

        var resultado = new List<ProgresivaMuestreo>();
        int i = 0;
        while (i < ordenadas.Count)
        {
            // Grupo anclado al primero: no se encadenan puntos que en total superen la tolerancia.
            double ancla = ordenadas[i].Progresiva;
            var mejor = ordenadas[i];
            int j = i + 1;
            while (j < ordenadas.Count && ordenadas[j].Progresiva - ancla <= tolerancia + 1e-9)
            {
                if (Prioridad(ordenadas[j].Tipo) < Prioridad(mejor.Tipo)) mejor = ordenadas[j];
                j++;
            }
            resultado.Add(mejor);
            i = j;
        }
        return resultado;
    }

    private static TipoTramo? TramoEn(List<TramoAlineamiento> ordenados, double s, double tol)
    {
        foreach (var t in ordenados)
            if (s >= t.Inicio - tol && s < t.Fin - tol) return t.Tipo;
        return ordenados[^1].Tipo;
    }
}
