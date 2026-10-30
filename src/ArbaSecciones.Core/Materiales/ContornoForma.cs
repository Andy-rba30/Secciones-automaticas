namespace ArbaSecciones.Core.Materiales;

/// <summary>Arma el contorno de una forma (shape) a partir de sus vínculos, que pueden venir en cualquier orden y sentido.</summary>
public static class ContornoForma
{
    public static List<PuntoSeccion> Armar(IEnumerable<IReadOnlyList<PuntoSeccion>> lados, double tolerancia = 1e-4)
    {
        var pendientes = lados.Where(l => l.Count >= 2).Select(l => l.ToList()).ToList();
        if (pendientes.Count == 0) return new List<PuntoSeccion>();

        var contorno = new List<PuntoSeccion>(pendientes[0]);
        pendientes.RemoveAt(0);
        while (pendientes.Count > 0)
        {
            PuntoSeccion fin = contorno[^1];
            int i = pendientes.FindIndex(l => Cerca(l[0], fin, tolerancia) || Cerca(l[^1], fin, tolerancia));
            if (i < 0) break;
            List<PuntoSeccion> lado = pendientes[i];
            pendientes.RemoveAt(i);
            if (!Cerca(lado[0], fin, tolerancia)) lado.Reverse();
            contorno.AddRange(lado.Skip(1));
        }

        // Si los vínculos no se encadenan, se ordenan los puntos alrededor del centro (sirve para formas convexas).
        if (pendientes.Count > 0)
        {
            var todos = contorno.Concat(pendientes.SelectMany(l => l)).ToList();
            double cx = todos.Average(p => p.Desplazamiento), cz = todos.Average(p => p.Elevacion);
            contorno = todos.OrderBy(p => Math.Atan2(p.Elevacion - cz, p.Desplazamiento - cx)).ToList();
        }
        if (contorno.Count > 1 && Cerca(contorno[0], contorno[^1], tolerancia)) contorno.RemoveAt(contorno.Count - 1);
        return contorno;
    }

    private static bool Cerca(PuntoSeccion a, PuntoSeccion b, double t) =>
        Math.Abs(a.Desplazamiento - b.Desplazamiento) <= t && Math.Abs(a.Elevacion - b.Elevacion) <= t;
}
