using ArbaSecciones.Core.Escalas;
using Autodesk.AutoCAD.DatabaseServices;

namespace ArbaSecciones.Plugin.Infraestructura;

internal static class UnidadesDibujo
{
    /// <summary>Unidad del dibujo según INSUNITS. Null si no está definida o no es de longitud.</summary>
    public static UnidadLongitud? DesdeInsunits(UnitsValue valor) => valor switch
    {
        UnitsValue.Millimeters => UnidadLongitud.Milimetros,
        UnitsValue.Centimeters => UnidadLongitud.Centimetros,
        UnitsValue.Decimeters => UnidadLongitud.Decimetros,
        UnitsValue.Meters => UnidadLongitud.Metros,
        UnitsValue.Kilometers => UnidadLongitud.Kilometros,
        UnitsValue.Inches => UnidadLongitud.Pulgadas,
        UnitsValue.Feet => UnidadLongitud.Pies,
        _ => null,
    };

    /// <summary>Unidad del papel de una presentación según sus unidades de trazado.</summary>
    public static UnidadLongitud DesdePapel(PlotPaperUnit unidad) =>
        unidad == PlotPaperUnit.Inches ? UnidadLongitud.Pulgadas : UnidadLongitud.Milimetros;
}
