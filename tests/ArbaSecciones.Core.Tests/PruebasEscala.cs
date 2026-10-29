using ArbaSecciones.Core.Escalas;
using ArbaSecciones.Core.Tests.Marco;

namespace ArbaSecciones.Core.Tests;

public static class PruebasEscala
{
    private static ConversorEscala Metros(double h, double? v = null) =>
        new(new EscalaSecciones(h, v), UnidadLongitud.Metros, UnidadLongitud.Milimetros);

    [Prueba]
    public static void Factor_1a200_metros_milimetros_es_5()
    {
        var c = Metros(200);
        Afirmar.Cerca(5, c.FactorH);
        Afirmar.Cerca(0.2, c.UnidadesDibujoPorUnidadPapel);
    }

    [Prueba]
    public static void Area_util_780x520_a_1a200_es_156x104_m()
    {
        var c = Metros(200);
        Afirmar.Cerca(156, c.PapelAModelo(780));
        Afirmar.Cerca(104, c.PapelAModelo(520));
        Afirmar.Cerca(104, c.ViewHeight(520));
    }

    [Prueba]
    public static void Diez_metros_de_modelo_miden_50_mm_a_1a200()
    {
        Afirmar.Cerca(50, Metros(200).ModeloAPapel(10));
        Afirmar.Cerca(100, Metros(100).ModeloAPapel(10));
    }

    [Prueba]
    public static void Dibujo_en_milimetros_no_supone_metros()
    {
        var c = new ConversorEscala(EscalaSecciones.Uniforme(200), UnidadLongitud.Milimetros, UnidadLongitud.Milimetros);
        Afirmar.Cerca(0.005, c.FactorH);
        Afirmar.Cerca(200, c.UnidadesDibujoPorUnidadPapel);

        var pulgadas = new ConversorEscala(EscalaSecciones.Uniforme(1), UnidadLongitud.Metros, UnidadLongitud.Pulgadas);
        Afirmar.Cerca(1000 / 25.4, pulgadas.FactorH, 1e-9);
    }

    [Prueba]
    public static void Exageracion_vertical_H200_V100_es_2()
    {
        var c = Metros(200, 100);
        Afirmar.Cerca(2, c.Escala.Exageracion);
        Afirmar.Verdadero(c.Escala.TieneExageracion);
        Afirmar.Cerca(5, c.FactorH);   // el viewport siempre usa la horizontal
        Afirmar.Cerca(10, c.FactorV);
        Afirmar.Igual("H 1:200 / V 1:100", c.Escala.ToString());
        Afirmar.Igual("1:200", EscalaSecciones.Uniforme(200).ToString());
    }

    [Prueba]
    public static void Cambiar_escala_horizontal_mantiene_la_exageracion()
    {
        var e = new EscalaSecciones(200, 100).ConDenominadorH(500);
        Afirmar.Cerca(500, e.DenominadorH);
        Afirmar.Cerca(250, e.DenominadorV);
    }

    [Prueba]
    public static void Denominadores_invalidos_se_rechazan()
    {
        Afirmar.Lanza<ArgumentOutOfRangeException>(() => _ = new EscalaSecciones(0));
        Afirmar.Lanza<ArgumentOutOfRangeException>(() => _ = new EscalaSecciones(200, -1));
        Afirmar.Lanza<ArgumentOutOfRangeException>(() => _ = new EscalaSecciones(double.NaN));
    }

    [Prueba]
    public static void Lectura_de_escalas_escritas_por_el_usuario()
    {
        Afirmar.Verdadero(EscalasNormalizadas.TryParse("1:200", out double a)); Afirmar.Cerca(200, a);
        Afirmar.Verdadero(EscalasNormalizadas.TryParse("1/250", out double b)); Afirmar.Cerca(250, b);
        Afirmar.Verdadero(EscalasNormalizadas.TryParse(" 1 : 100 ", out double c)); Afirmar.Cerca(100, c);
        Afirmar.Verdadero(EscalasNormalizadas.TryParse("500", out double d)); Afirmar.Cerca(500, d);
        Afirmar.Verdadero(EscalasNormalizadas.TryParse("2:1000", out double e)); Afirmar.Cerca(500, e);
        Afirmar.Verdadero(!EscalasNormalizadas.TryParse("abc", out _));
        Afirmar.Verdadero(!EscalasNormalizadas.TryParse("1:0", out _));
        Afirmar.Verdadero(!EscalasNormalizadas.TryParse("", out _));
    }
}
