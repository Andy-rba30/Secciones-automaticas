using ArbaSecciones.Core.Configuracion;
using ArbaSecciones.Core.Hojas;
using ArbaSecciones.Core.Tests.Marco;

namespace ArbaSecciones.Core.Tests;

public static class PruebasHojas
{
    // Papeles de DWG To PDF.pc3 con sus nombres canónicos (una muestra representativa).
    private static readonly string[] NombresDwgToPdf =
    {
        "ISO_full_bleed_A4_(210.00_x_297.00_MM)",
        "ISO_full_bleed_A4_(297.00_x_210.00_MM)",
        "ISO_full_bleed_A3_(297.00_x_420.00_MM)",
        "ISO_full_bleed_A3_(420.00_x_297.00_MM)",
        "ISO_full_bleed_A2_(420.00_x_594.00_MM)",
        "ISO_full_bleed_A2_(594.00_x_420.00_MM)",
        "ISO_full_bleed_A1_(594.00_x_841.00_MM)",
        "ISO_full_bleed_A1_(841.00_x_594.00_MM)",
        "ISO_full_bleed_A0_(841.00_x_1189.00_MM)",
        "ISO_expand_A1_(841.00_x_594.00_MM)",
        "ISO_A1_(841.00_x_594.00_MM)",
        "ISO_A0_(841.00_x_1189.00_MM)",
        "ISO_full_bleed_2A0_(1189.00_x_1682.00_MM)",
        "ANSI_full_bleed_D_(34.00_x_22.00_Inches)",
        "ARCH_full_bleed_E_(36.00_x_48.00_Inches)",
    };

    private static List<MedioPapel> Medios(params string[] nombres) =>
        nombres.Select(n => FormatosPapel.TryLeerTamano(n, out double a, out double b) ? new MedioPapel(n, a, b) : null)
            .Where(m => m != null)
            .Select(m => m!)
            .ToList();

    private static List<MedioPapel> DwgToPdf => Medios(NombresDwgToPdf);

    [Prueba]
    public static void Lee_el_tamano_del_nombre_del_papel()
    {
        Afirmar.Verdadero(FormatosPapel.TryLeerTamano("ISO_full_bleed_A1_(841.00_x_594.00_MM)", out double a, out double b));
        Afirmar.Cerca(841, a);
        Afirmar.Cerca(594, b);

        Afirmar.Verdadero(FormatosPapel.TryLeerTamano("ANSI_full_bleed_D_(34.00_x_22.00_Inches)", out a, out b));
        Afirmar.Cerca(863.6, a, 1e-9);
        Afirmar.Cerca(558.8, b, 1e-9);

        Afirmar.Verdadero(FormatosPapel.TryLeerTamano("ISO full bleed A3 (420.00 x 297.00 MM)", out a, out b), "Nombre local con espacios.");
        Afirmar.Cerca(420, a);
        Afirmar.Verdadero(!FormatosPapel.TryLeerTamano("Carta", out _, out _));
        Afirmar.Verdadero(!FormatosPapel.TryLeerTamano("Sun_Hi-Res_(1600.00_x_1280.00_Pixels)", out _, out _));
    }

    [Prueba]
    public static void Reconoce_A0_A1_A2_A3_A4_en_cualquier_orientacion()
    {
        Afirmar.Igual("A0", FormatosPapel.Reconocer(1189, 841)?.Nombre);
        Afirmar.Igual("A1", FormatosPapel.Reconocer(841, 594)?.Nombre);
        Afirmar.Igual("A1", FormatosPapel.Reconocer(594, 841)?.Nombre);
        Afirmar.Igual("A2", FormatosPapel.Reconocer(594, 420)?.Nombre);
        Afirmar.Igual("A3", FormatosPapel.Reconocer(420, 297)?.Nombre);
        Afirmar.Igual("A4", FormatosPapel.Reconocer(210, 297)?.Nombre);
        Afirmar.Igual("A1", FormatosPapel.Reconocer(842.5, 593.2)?.Nombre, "Tolera diferencias de dibujo.");
        Afirmar.Igual("A1×3", FormatosPapel.Reconocer(1783, 841)?.Nombre);
        Afirmar.Igual<FormatoPapel?>(null, FormatosPapel.Reconocer(811, 574), "Marco interior: no es un formato.");
        Afirmar.Igual("A1 horizontal (841 × 594 mm)", FormatosPapel.Describir(841, 594));
        Afirmar.Igual("A3 vertical (297 × 420 mm)", FormatosPapel.Describir(297, 420));
        Afirmar.Igual("811 × 574 mm", FormatosPapel.Describir(811, 574));
    }

    [Prueba]
    public static void Membrete_A1_horizontal_usa_A1_full_bleed_sin_girar()
    {
        var c = FormatosPapel.Candidatos(841, 594, DwgToPdf);
        Afirmar.Igual("ISO_full_bleed_A1_(841.00_x_594.00_MM)", c[0].Medio.Nombre);
        Afirmar.Verdadero(!c[0].Girado);
        Afirmar.Verdadero(c[0].Exacto);
        Afirmar.Cerca(841, c[0].AnchoHoja);
        Afirmar.Cerca(594, c[0].AltoHoja);
    }

    [Prueba]
    public static void Membrete_A0_horizontal_gira_el_A0_vertical()
    {
        var c = FormatosPapel.Candidatos(1189, 841, DwgToPdf);
        Afirmar.Igual("ISO_full_bleed_A0_(841.00_x_1189.00_MM)", c[0].Medio.Nombre);
        Afirmar.Verdadero(c[0].Girado);
        Afirmar.Cerca(1189, c[0].AnchoHoja);
        Afirmar.Cerca(841, c[0].AltoHoja);
    }

    [Prueba]
    public static void Membrete_vertical_A3_y_A4()
    {
        var a3 = FormatosPapel.Candidatos(297, 420, DwgToPdf)[0];
        Afirmar.Igual("ISO_full_bleed_A3_(297.00_x_420.00_MM)", a3.Medio.Nombre);
        Afirmar.Verdadero(!a3.Girado);

        var a4 = FormatosPapel.Candidatos(297, 210, DwgToPdf)[0];
        Afirmar.Igual("ISO_full_bleed_A4_(297.00_x_210.00_MM)", a4.Medio.Nombre);
    }

    [Prueba]
    public static void Marco_interior_va_en_la_hoja_mas_chica_que_lo_contiene()
    {
        // Solo el recuadro interior de un A1 (811 × 574): no es exacto, pero A1 es la hoja más chica donde entra.
        var c = FormatosPapel.Candidatos(811, 574, DwgToPdf);
        Afirmar.Igual("ISO_full_bleed_A1_(841.00_x_594.00_MM)", c[0].Medio.Nombre);
        Afirmar.Verdadero(!c[0].Exacto);
    }

    [Prueba]
    public static void Membrete_mas_grande_que_todo_no_tiene_candidatos()
    {
        Afirmar.Igual(0, FormatosPapel.Candidatos(3000, 1200, DwgToPdf).Count);
        Afirmar.Lanza<ArgumentException>(() => FormatosPapel.Candidatos(0, 594, DwgToPdf));
    }

    [Prueba]
    public static void A_igual_tamano_prefiere_sin_margenes()
    {
        var c = FormatosPapel.Candidatos(841, 594, Medios("ISO_A1_(841.00_x_594.00_MM)", "ISO_full_bleed_A1_(841.00_x_594.00_MM)"));
        Afirmar.Verdadero(c[0].Medio.SinMargenes);
        Afirmar.Verdadero(!c[1].Medio.SinMargenes);
    }

    [Prueba]
    public static void Posicion_del_membrete_en_la_hoja()
    {
        // Membrete del tamaño de la hoja en un papel sin márgenes: su esquina va en 0,0.
        var (x, y) = FormatosPapel.PosicionMembrete(841, 594, 841, 594);
        Afirmar.Cerca(0, x);
        Afirmar.Cerca(0, y);

        // Recuadro de 811 × 574 sobre un A1: queda centrado (15 y 10 mm de borde).
        (x, y) = FormatosPapel.PosicionMembrete(811, 574, 841, 594);
        Afirmar.Cerca(15, x);
        Afirmar.Cerca(10, y);

        // Papel con 5 mm no imprimibles: el 0,0 es la esquina del área imprimible, la hoja empieza en -5,-5.
        (x, y) = FormatosPapel.PosicionMembrete(841, 594, 841, 594, 5, 5);
        Afirmar.Cerca(-5, x);
        Afirmar.Cerca(-5, y);
    }

    [Prueba]
    public static void Configuracion_version_1_deja_de_forzar_A1()
    {
        var vieja = AlmacenConfiguracion.Deserializar(
            "{ \"Version\": 1, \"Exportacion\": { \"Papel\": \"ISO_full_bleed_A1_(841.00_x_594.00_MM)\" } }");
        Afirmar.Igual("", vieja.Exportacion.Papel);
        Afirmar.Igual(ConfiguracionArba.VersionActual, vieja.Version);

        var elegida = AlmacenConfiguracion.Deserializar(
            "{ \"Version\": 1, \"Exportacion\": { \"Papel\": \"ISO_full_bleed_A0_(841.00_x_1189.00_MM)\" } }");
        Afirmar.Igual("ISO_full_bleed_A0_(841.00_x_1189.00_MM)", elegida.Exportacion.Papel, "Un papel elegido a mano se respeta.");

        var nueva = new ConfiguracionArba().Normalizar();
        Afirmar.Igual("", nueva.Exportacion.Papel);
        Afirmar.Verdadero(nueva.Membrete.AjustarHoja);
    }
}
