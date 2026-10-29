using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ArbaSecciones.Core.Acomodo;
using ArbaSecciones.Core.Comun;

namespace ArbaSecciones.Plugin.Interfaz;

/// <summary>Dibujo esquemático de una lámina: área útil, franja del cuadro y cada sección con su progresiva.</summary>
internal sealed class VistaPreviaLaminas : Border
{
    private readonly Canvas _lienzo = new();
    private static readonly Brush Fondo = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF5));
    private static readonly Brush Linea = new SolidColorBrush(Color.FromRgb(0x5A, 0x63, 0x70));
    private static readonly Brush Seccion = new SolidColorBrush(Color.FromArgb(0x40, 0x1F, 0x6F, 0xB5));
    private static readonly Brush Franja = new SolidColorBrush(Color.FromArgb(0x30, 0xB5, 0x7A, 0x1F));

    public VistaPreviaLaminas()
    {
        Height = 220;
        Margin = new Thickness(0, 4, 0, 4);
        BorderBrush = Linea;
        BorderThickness = new Thickness(1);
        Background = Fondo;
        Child = new Viewbox { Stretch = Stretch.Uniform, Child = _lienzo, Margin = new Thickness(6) };
    }

    public void Limpiar() => _lienzo.Children.Clear();

    public void Mostrar(ResultadoAcomodo? resultado, int indice)
    {
        _lienzo.Children.Clear();
        if (resultado == null || !resultado.Valido || indice < 0 || indice >= resultado.Laminas.Count) return;

        ParametrosAcomodo p = resultado.Parametros;
        _lienzo.Width = p.AnchoUtil;
        _lienzo.Height = p.AltoUtil;
        double trazo = Math.Max(p.AnchoUtil, p.AltoUtil) / 300.0;

        Agregar(new Rectangle { Width = p.AnchoUtil, Height = p.AltoUtil, Stroke = Linea, StrokeThickness = trazo * 2 }, 0, 0);

        var franja = p.ZonaCuadro;
        if (franja.HasValue)
        {
            var f = franja.Value;
            Agregar(new Rectangle { Width = f.Ancho, Height = f.Alto, Fill = Franja, Stroke = Linea, StrokeThickness = trazo },
                f.X, p.AltoUtil - f.Y - f.Alto);
        }

        double alturaTexto = Math.Max(p.AltoUtil / 45.0, 1);
        foreach (Colocacion c in resultado.Laminas[indice].Colocaciones)
        {
            double arriba = p.AltoUtil - c.Y - c.Alto; // WPF mide desde arriba; el papel, desde abajo
            Agregar(new Rectangle { Width = c.Ancho, Height = c.Alto, Fill = Seccion, Stroke = Linea, StrokeThickness = trazo }, c.X, arriba);
            Agregar(new TextBlock
            {
                Text = FormatoProgresiva.Formatear(c.Seccion.Progresiva),
                FontSize = alturaTexto,
                Foreground = Linea,
            }, c.X + trazo * 3, arriba + trazo * 2);
        }
    }

    private void Agregar(UIElement e, double izquierda, double arriba)
    {
        Canvas.SetLeft(e, izquierda);
        Canvas.SetTop(e, arriba);
        _lienzo.Children.Add(e);
    }
}
