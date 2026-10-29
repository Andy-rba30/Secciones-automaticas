namespace ArbaSecciones.Core.Escalas;

/// <summary>Unidades de longitud que pueden tener el dibujo (modelo) o el membrete (papel).</summary>
public enum UnidadLongitud
{
    Milimetros,
    Centimetros,
    Decimetros,
    Metros,
    Kilometros,
    Pulgadas,
    Pies,
}

public static class Unidades
{
    /// <summary>Cuántos milímetros mide una unidad.</summary>
    public static double EnMilimetros(this UnidadLongitud unidad) => unidad switch
    {
        UnidadLongitud.Milimetros => 1.0,
        UnidadLongitud.Centimetros => 10.0,
        UnidadLongitud.Decimetros => 100.0,
        UnidadLongitud.Metros => 1000.0,
        UnidadLongitud.Kilometros => 1_000_000.0,
        UnidadLongitud.Pulgadas => 25.4,
        UnidadLongitud.Pies => 304.8,
        _ => throw new ArgumentOutOfRangeException(nameof(unidad), unidad, "Unidad no soportada."),
    };

    public static string Abreviatura(this UnidadLongitud unidad) => unidad switch
    {
        UnidadLongitud.Milimetros => "mm",
        UnidadLongitud.Centimetros => "cm",
        UnidadLongitud.Decimetros => "dm",
        UnidadLongitud.Metros => "m",
        UnidadLongitud.Kilometros => "km",
        UnidadLongitud.Pulgadas => "in",
        UnidadLongitud.Pies => "ft",
        _ => unidad.ToString(),
    };
}
