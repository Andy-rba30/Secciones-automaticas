namespace ArbaSecciones.Core.Acomodo;

/// <summary>
/// Llena una lámina con una secuencia de secciones en orden de progresiva y devuelve cuántas entraron.
/// Coloca siempre el mayor número posible de secciones consecutivas (orden obligatorio) y centra el conjunto
/// dentro de la zona de secciones del área útil.
/// </summary>
internal static class Empaquetador
{
    private const double Eps = 1e-6;

    public static List<Colocacion> Llenar(IReadOnlyList<(MedidaSeccion Medida, Tamano Tamano)> items, int desde, ParametrosAcomodo p)
    {
        return p.Orden == OrdenAcomodo.PorColumnas ? PorColumnas(items, desde, p) : PorFilas(items, desde, p);
    }

    public static bool EntraSola(Tamano t, ParametrosAcomodo p)
    {
        var zona = p.ZonaSecciones;
        return t.Ancho <= zona.Ancho + Eps && t.Alto <= zona.Alto + Eps;
    }

    private sealed class Grupo
    {
        public double Posicion;          // X de la columna o distancia desde arriba de la fila
        public double Ancho;             // ancho de la columna o largo usado de la fila
        public double Alto;              // alto usado de la columna o alto de la fila
        public readonly List<(MedidaSeccion Medida, Tamano Tamano)> Items = new();
    }

    // Columnas: de abajo hacia arriba; la siguiente columna va a la derecha.
    private static List<Colocacion> PorColumnas(IReadOnlyList<(MedidaSeccion Medida, Tamano Tamano)> items, int desde, ParametrosAcomodo p)
    {
        var zona = p.ZonaSecciones;
        var columnas = new List<Grupo>();
        var actual = new Grupo { Posicion = 0 };

        for (int i = desde; i < items.Count; i++)
        {
            var t = items[i].Tamano;
            if (!EntraSola(t, p)) break;

            double altoNuevo = actual.Items.Count == 0 ? t.Alto : actual.Alto + p.SeparacionV + t.Alto;
            double anchoNuevo = Math.Max(actual.Ancho, t.Ancho);

            if (altoNuevo <= zona.Alto + Eps && actual.Posicion + anchoNuevo <= zona.Ancho + Eps)
            {
                actual.Items.Add(items[i]);
                actual.Alto = altoNuevo;
                actual.Ancho = anchoNuevo;
                continue;
            }

            if (actual.Items.Count == 0) break;

            double siguienteX = actual.Posicion + actual.Ancho + p.SeparacionH;
            if (siguienteX + t.Ancho > zona.Ancho + Eps) break; // la lámina está llena

            columnas.Add(actual);
            actual = new Grupo { Posicion = siguienteX, Ancho = t.Ancho, Alto = t.Alto };
            actual.Items.Add(items[i]);
        }
        if (actual.Items.Count > 0) columnas.Add(actual);

        var resultado = new List<Colocacion>();
        if (columnas.Count == 0) return resultado;

        double anchoTotal = columnas[^1].Posicion + columnas[^1].Ancho;
        double margenX = Math.Max(0, (zona.Ancho - anchoTotal) / 2);

        foreach (var col in columnas)
        {
            double margenY = Math.Max(0, (zona.Alto - col.Alto) / 2);
            double y = zona.Y + margenY;
            foreach (var (medida, tamano) in col.Items)
            {
                double x = zona.X + margenX + col.Posicion + (col.Ancho - tamano.Ancho) / 2; // centrada en la columna y en el área útil
                resultado.Add(new Colocacion(medida, x, y, tamano.Ancho, tamano.Alto));
                y += tamano.Alto + p.SeparacionV;
            }
        }
        return resultado;
    }

    // Filas: de izquierda a derecha; la siguiente fila va debajo.
    private static List<Colocacion> PorFilas(IReadOnlyList<(MedidaSeccion Medida, Tamano Tamano)> items, int desde, ParametrosAcomodo p)
    {
        var zona = p.ZonaSecciones;
        var filas = new List<Grupo>();
        var actual = new Grupo { Posicion = 0 };

        for (int i = desde; i < items.Count; i++)
        {
            var t = items[i].Tamano;
            if (!EntraSola(t, p)) break;

            double anchoNuevo = actual.Items.Count == 0 ? t.Ancho : actual.Ancho + p.SeparacionH + t.Ancho;
            double altoNuevo = Math.Max(actual.Alto, t.Alto);

            if (anchoNuevo <= zona.Ancho + Eps && actual.Posicion + altoNuevo <= zona.Alto + Eps)
            {
                actual.Items.Add(items[i]);
                actual.Ancho = anchoNuevo;
                actual.Alto = altoNuevo;
                continue;
            }

            if (actual.Items.Count == 0) break;

            double siguienteArriba = actual.Posicion + actual.Alto + p.SeparacionV;
            if (siguienteArriba + t.Alto > zona.Alto + Eps) break; // la lámina está llena

            filas.Add(actual);
            actual = new Grupo { Posicion = siguienteArriba, Ancho = t.Ancho, Alto = t.Alto };
            actual.Items.Add(items[i]);
        }
        if (actual.Items.Count > 0) filas.Add(actual);

        var resultado = new List<Colocacion>();
        if (filas.Count == 0) return resultado;

        double altoTotal = filas[^1].Posicion + filas[^1].Alto;
        double margenY = Math.Max(0, (zona.Alto - altoTotal) / 2);

        foreach (var fila in filas)
        {
            double margenX = Math.Max(0, (zona.Ancho - fila.Ancho) / 2);
            double x = zona.X + margenX;
            double yInferiorFila = zona.Y + zona.Alto - margenY - fila.Posicion - fila.Alto;
            foreach (var (medida, tamano) in fila.Items)
            {
                double y = yInferiorFila + (fila.Alto - tamano.Alto) / 2; // centrada en la fila y en el área útil
                resultado.Add(new Colocacion(medida, x, y, tamano.Ancho, tamano.Alto));
                x += tamano.Ancho + p.SeparacionH;
            }
        }
        return resultado;
    }
}
