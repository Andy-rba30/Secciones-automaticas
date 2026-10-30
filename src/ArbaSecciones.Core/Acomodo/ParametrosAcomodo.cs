namespace ArbaSecciones.Core.Acomodo;

/// <summary>Todo en unidades de papel, salvo LongitudTramo (unidades de progresiva).</summary>
public sealed class ParametrosAcomodo
{
    public double AnchoUtil { get; init; }
    public double AltoUtil { get; init; }
    public double SeparacionH { get; init; } = 10;
    public double SeparacionV { get; init; } = 10;

    /// <summary>Alto de la franja reservada para el cuadro de movimiento de tierras. 0 = sin franja.</summary>
    public double AltoFranjaCuadro { get; init; }

    public PosicionFranja PosicionFranja { get; init; } = PosicionFranja.Abajo;
    public OrdenAcomodo Orden { get; init; } = OrdenAcomodo.PorColumnas;
    public ModoPaginacion Paginacion { get; init; } = ModoPaginacion.PorKilometro;

    /// <summary>Longitud de cada grupo de paginación. 1000 = un km.</summary>
    public double LongitudTramo { get; init; } = 1000;

    /// <summary>
    /// Todas las secciones de una misma lámina tienen la misma altura (la mayor de la lámina), para que
    /// queden alineadas en filas y columnas. El rango de elevación de las vistas se ajusta en el dibujo.
    /// </summary>
    public bool UniformarAlturas { get; init; }

    /// <summary>Con <see cref="UniformarAlturas"/>: una sola altura (la mayor) para todas las secciones del tramo.</summary>
    public bool UniformarTodoElTramo { get; init; }

    /// <summary>Esquina inferior izquierda (relativa al área útil) y tamaño de la zona donde van las secciones.</summary>
    public (double X, double Y, double Ancho, double Alto) ZonaSecciones
    {
        get
        {
            if (AltoFranjaCuadro <= 0) return (0, 0, AnchoUtil, AltoUtil);
            double alto = AltoUtil - AltoFranjaCuadro - SeparacionV;
            double y = PosicionFranja == PosicionFranja.Abajo ? AltoFranjaCuadro + SeparacionV : 0;
            return (0, y, AnchoUtil, alto);
        }
    }

    /// <summary>Esquina inferior izquierda y tamaño de la franja del cuadro (relativa al área útil).</summary>
    public (double X, double Y, double Ancho, double Alto)? ZonaCuadro
    {
        get
        {
            if (AltoFranjaCuadro <= 0) return null;
            double y = PosicionFranja == PosicionFranja.Abajo ? 0 : AltoUtil - AltoFranjaCuadro;
            return (0, y, AnchoUtil, AltoFranjaCuadro);
        }
    }

    public List<string> Validar()
    {
        var errores = new List<string>();
        if (!(AnchoUtil > 0) || !(AltoUtil > 0)) errores.Add("El área útil no está definida (ancho y alto deben ser mayores que cero).");
        if (SeparacionH < 0 || SeparacionV < 0) errores.Add("Las separaciones no pueden ser negativas.");
        if (AltoFranjaCuadro < 0) errores.Add("El alto de la franja del cuadro no puede ser negativo.");
        if (AltoUtil > 0 && AltoFranjaCuadro > 0 && ZonaSecciones.Alto <= 0) errores.Add("La franja del cuadro ocupa toda el área útil.");
        if (!(LongitudTramo > 0)) errores.Add("La longitud de cada tramo de paginación debe ser mayor que cero.");
        return errores;
    }
}
