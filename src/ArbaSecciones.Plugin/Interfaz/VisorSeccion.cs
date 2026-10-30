using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ArbaSecciones.Core.Materiales;

namespace ArbaSecciones.Plugin.Interfaz;

/// <summary>
/// Visor de la sección del corredor: vínculos, formas, vista previa de la Rasante y la SubRasante y huecos.
/// Rueda = zoom, arrastrar = desplazar, clic sobre una línea = ver su código. Se dibuja en coordenadas de pantalla
/// (el grosor de las líneas no cambia con el zoom).
/// </summary>
internal sealed class VisorSeccion : Border
{
    private static readonly Brush Fondo = Pincel(0xFF, 0xF7, 0xF7, 0xF5);
    private static readonly Brush Borde = Pincel(0xFF, 0x5A, 0x63, 0x70);
    private static readonly Brush Vinculo = Pincel(0xFF, 0x5A, 0x63, 0x70);
    private static readonly Brush FormaRelleno = Pincel(0x30, 0x7A, 0x8F, 0xA6);
    private static readonly Brush Resaltado = Pincel(0xFF, 0xFF, 0x6A, 0x00);
    private static readonly Brush Elegido = Pincel(0xFF, 0xC0, 0x1F, 0x8F);
    private static readonly Brush ColorRasante = Pincel(0x90, 0x1F, 0x6F, 0xD5);
    private static readonly Brush ColorSubRasante = Pincel(0x90, 0x1E, 0xA0, 0x4C);
    private static readonly Brush ColorHueco = Pincel(0x55, 0xE0, 0x20, 0x20);
    private static readonly Brush FondoRotulo = Pincel(0xEE, 0xFF, 0xFF, 0xFF);

    private readonly Canvas _lienzo = new() { ClipToBounds = true, Background = Fondo };
    private readonly TextBlock _rotulo = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly Border _cajaRotulo;

    private SeccionCorredor? _seccion;
    private double _exageracion = 1;
    private double _escala = 1, _dx, _dy;
    private bool _ajustado;
    private string? _codigoResaltado;
    private int? _vinculoElegido;
    private Envolvente? _rasante, _subRasante;
    private List<(double Desde, double Hasta)> _huecos = new();

    private bool _arrastrando, _movio;
    private Point _inicioArrastre, _ultimo;

    /// <summary>Se hizo clic sobre un vínculo.</summary>
    public event Action<VinculoSeccion>? VinculoElegido;

    public VisorSeccion()
    {
        Height = 320;
        Margin = new Thickness(0, 4, 0, 4);
        BorderBrush = Borde;
        BorderThickness = new Thickness(1);
        Background = Fondo;

        _cajaRotulo = new Border
        {
            Child = _rotulo,
            Background = FondoRotulo,
            BorderBrush = Borde,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(6),
            MaxWidth = 260,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };

        var raiz = new Grid();
        raiz.Children.Add(_lienzo);
        raiz.Children.Add(CrearLeyenda());
        raiz.Children.Add(_cajaRotulo);
        Child = raiz;

        _lienzo.MouseWheel += AlRueda;
        _lienzo.MouseLeftButtonDown += AlPresionar;
        _lienzo.MouseMove += AlMover;
        _lienzo.MouseLeftButtonUp += AlSoltar;
        _lienzo.SizeChanged += (_, _) =>
        {
            if (!_ajustado) Ajustar();
            else Dibujar();
        };
    }

    public SeccionCorredor? Seccion => _seccion;

    public void Mostrar(SeccionCorredor? seccion, bool mantenerVista = false)
    {
        _seccion = seccion;
        _vinculoElegido = null;
        _cajaRotulo.Visibility = Visibility.Collapsed;
        if (mantenerVista && _ajustado) Dibujar();
        else Ajustar();
    }

    /// <summary>Exageración vertical del dibujo (1, 2, 5…). No cambia los datos.</summary>
    public double Exageracion
    {
        get => _exageracion;
        set
        {
            _exageracion = value > 0 ? value : 1;
            Ajustar();
        }
    }

    public void Resaltar(string? codigo)
    {
        if (string.Equals(_codigoResaltado, codigo, StringComparison.OrdinalIgnoreCase)) return;
        _codigoResaltado = codigo;
        Dibujar();
    }

    public void VistaPrevia(Envolvente? rasante, Envolvente? subRasante, IEnumerable<(double Desde, double Hasta)> huecos)
    {
        _rasante = rasante;
        _subRasante = subRasante;
        _huecos = huecos.ToList();
        Dibujar();
    }

    /// <summary>Encuadra toda la sección.</summary>
    public void Ajustar()
    {
        double w = _lienzo.ActualWidth, h = _lienzo.ActualHeight;
        if (w < 10 || h < 10)
        {
            _ajustado = false;
            return;
        }
        _ajustado = true;
        if (_seccion == null || _seccion.Vinculos.Count == 0)
        {
            Dibujar();
            return;
        }
        double x0 = _seccion.DesdeX, x1 = _seccion.HastaX;
        double z0 = _seccion.ElevacionMinima * _exageracion, z1 = _seccion.ElevacionMaxima * _exageracion;
        double ancho = Math.Max(x1 - x0, 1e-3), alto = Math.Max(z1 - z0, 1e-3);
        _escala = Math.Max(1e-6, Math.Min((w - 40) / ancho, (h - 50) / alto));
        _dx = w / 2 - (x0 + x1) / 2 * _escala;
        _dy = h / 2 + (z0 + z1) / 2 * _escala + 8;
        Dibujar();
    }

    private Point Pantalla(double x, double z) => new(_dx + x * _escala, _dy - z * _exageracion * _escala);

    private PointCollection Puntos(IEnumerable<PuntoSeccion> puntos)
    {
        var pc = new PointCollection();
        foreach (PuntoSeccion p in puntos) pc.Add(Pantalla(p.Desplazamiento, p.Elevacion));
        return pc;
    }

    private void Dibujar()
    {
        _lienzo.Children.Clear();
        if (_seccion == null) return;
        double alto = Math.Max(_lienzo.ActualHeight, 1);

        foreach (var (desde, hasta) in _huecos)
        {
            double a = Pantalla(desde, 0).X, b = Pantalla(hasta, 0).X;
            var franja = new Rectangle { Width = Math.Max(b - a, 4), Height = alto, Fill = ColorHueco, IsHitTestVisible = false };
            Canvas.SetLeft(franja, Math.Min(a, b) - (b - a < 4 ? 2 : 0));
            Canvas.SetTop(franja, 0);
            _lienzo.Children.Add(franja);
        }

        foreach (FormaSeccion f in _seccion.Formas.Where(f => f.Contorno.Count >= 3))
            _lienzo.Children.Add(new Polygon { Points = Puntos(f.Contorno), Fill = FormaRelleno, IsHitTestVisible = false });

        // Primero los vínculos normales; encima los resaltados y el elegido.
        var resaltados = new List<VinculoSeccion>();
        foreach (VinculoSeccion v in _seccion.Vinculos)
        {
            bool resaltar = _codigoResaltado != null && v.Tiene(_codigoResaltado);
            if (resaltar || v.Indice == _vinculoElegido) resaltados.Add(v);
            else _lienzo.Children.Add(Linea(v.Puntos, Vinculo, 1.2));
        }

        foreach (Envolvente? e in new[] { _rasante, _subRasante })
        {
            if (e == null) continue;
            Brush color = e == _rasante ? ColorRasante : ColorSubRasante;
            foreach (IReadOnlyList<PuntoSeccion> parte in e.Polilineas())
                _lienzo.Children.Add(Linea(parte, color, 5));
        }

        foreach (VinculoSeccion v in resaltados)
            _lienzo.Children.Add(Linea(v.Puntos, v.Indice == _vinculoElegido ? Elegido : Resaltado, v.Indice == _vinculoElegido ? 3.5 : 3));
    }

    private Polyline Linea(IEnumerable<PuntoSeccion> puntos, Brush color, double grosor) => new()
    {
        Points = Puntos(puntos),
        Stroke = color,
        StrokeThickness = grosor,
        StrokeLineJoin = PenLineJoin.Round,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        IsHitTestVisible = false,
    };

    // ───────────────────────────── Ratón ─────────────────────────────

    private void AlRueda(object sender, MouseWheelEventArgs e)
    {
        Point p = e.GetPosition(_lienzo);
        double f = Math.Pow(1.2, e.Delta / 120.0);
        _dx = p.X - (p.X - _dx) * f;
        _dy = p.Y - (p.Y - _dy) * f;
        _escala *= f;
        Dibujar();
        e.Handled = true; // que no desplace la paleta
    }

    private void AlPresionar(object sender, MouseButtonEventArgs e)
    {
        _inicioArrastre = _ultimo = e.GetPosition(_lienzo);
        _arrastrando = true;
        _movio = false;
        _lienzo.CaptureMouse();
        e.Handled = true;
    }

    private void AlMover(object sender, MouseEventArgs e)
    {
        if (!_arrastrando) return;
        Point p = e.GetPosition(_lienzo);
        if (!_movio && (p - _inicioArrastre).Length > 3) _movio = true;
        if (!_movio) return;
        _dx += p.X - _ultimo.X;
        _dy += p.Y - _ultimo.Y;
        _ultimo = p;
        Dibujar();
    }

    private void AlSoltar(object sender, MouseButtonEventArgs e)
    {
        if (!_arrastrando) return;
        _arrastrando = false;
        _lienzo.ReleaseMouseCapture();
        if (!_movio) Elegir(e.GetPosition(_lienzo));
        e.Handled = true;
    }

    /// <summary>Elige el vínculo más cercano al clic (a menos de 6 píxeles).</summary>
    private void Elegir(Point p)
    {
        if (_seccion == null) return;
        VinculoSeccion? mejor = null;
        double distancia = 6;
        foreach (VinculoSeccion v in _seccion.Vinculos)
        {
            foreach (var (a, b) in v.Segmentos())
            {
                double d = DistanciaASegmento(p, Pantalla(a.Desplazamiento, a.Elevacion), Pantalla(b.Desplazamiento, b.Elevacion));
                if (d < distancia)
                {
                    distancia = d;
                    mejor = v;
                }
            }
        }

        if (mejor == null)
        {
            _vinculoElegido = null;
            _cajaRotulo.Visibility = Visibility.Collapsed;
            Dibujar();
            return;
        }
        _vinculoElegido = mejor.Indice;
        _rotulo.Text = Describir(mejor);
        _cajaRotulo.Visibility = Visibility.Visible;
        Dibujar();
        VinculoElegido?.Invoke(mejor);
    }

    internal static string Describir(VinculoSeccion v)
    {
        var inv = CultureInfo.InvariantCulture;
        var orden = v.Puntos.OrderBy(p => p.Desplazamiento).ToList();
        double? pendiente = v.Pendiente(1e-6);
        string textoPendiente = pendiente.HasValue ? (pendiente.Value * 100).ToString("0.00", inv) + " %" : "vertical";
        return $"Código: {v.CodigosTexto}\n" +
               $"Desplazamiento: {orden[0].Desplazamiento.ToString("0.00", inv)} → {orden[^1].Desplazamiento.ToString("0.00", inv)}\n" +
               $"Longitud: {v.Longitud.ToString("0.00", inv)} m · Pendiente: {textoPendiente}";
    }

    private static double DistanciaASegmento(Point p, Point a, Point b)
    {
        Vector ab = b - a, ap = p - a;
        double l2 = ab.LengthSquared;
        double t = l2 <= 1e-12 ? 0 : Math.Clamp((ap.X * ab.X + ap.Y * ab.Y) / l2, 0, 1);
        Point c = a + ab * t;
        return (p - c).Length;
    }

    private static UIElement CrearLeyenda()
    {
        var pila = new StackPanel
        {
            Margin = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = FondoRotulo,
            IsHitTestVisible = false,
        };
        pila.Children.Add(Entrada(Vinculo, 1.5, "Vínculos"));
        pila.Children.Add(Entrada(Resaltado, 3, "Código resaltado"));
        pila.Children.Add(Entrada(ColorRasante, 5, "Rasante (vista previa)"));
        pila.Children.Add(Entrada(ColorSubRasante, 5, "SubRasante (vista previa)"));
        pila.Children.Add(Entrada(ColorHueco, 8, "Hueco"));
        return pila;
    }

    private static UIElement Entrada(Brush color, double grosor, string texto)
    {
        var fila = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 1, 4, 1) };
        fila.Children.Add(new Rectangle { Width = 18, Height = grosor, Fill = color, VerticalAlignment = VerticalAlignment.Center });
        fila.Children.Add(new TextBlock { Text = texto, FontSize = 10, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return fila;
    }

    private static Brush Pincel(byte a, byte r, byte g, byte b)
    {
        var p = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        p.Freeze();
        return p;
    }
}
