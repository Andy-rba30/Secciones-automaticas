using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ArbaSecciones.Core.Materiales;

namespace ArbaSecciones.Plugin.Interfaz;

/// <summary>
/// Lista de códigos de vínculo con una casilla para la Rasante y otra para la SubRasante.
/// Pasar el ratón por un código lo resalta en el visor; hacer clic en el nombre lo deja resaltado.
/// </summary>
internal sealed class ListaCodigos : Border
{
    private static readonly Brush Seleccion = Congelado(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0x6A, 0x00)));
    private static readonly Brush Transparente = Congelado(new SolidColorBrush(Colors.Transparent));

    private sealed class Fila
    {
        public required string Codigo { get; init; }
        public required Border Fondo { get; init; }
        public required CheckBox Rasante { get; init; }
        public required CheckBox SubRasante { get; init; }
    }

    private readonly Grid _tabla = new();
    private readonly List<Fila> _filas = new();
    private string? _fijado;
    private bool _cargando;

    /// <summary>Se marcó o desmarcó alguna casilla.</summary>
    public event Action? Cambio;

    /// <summary>Código a resaltar en el visor (null = ninguno).</summary>
    public event Action<string?>? Resaltar;

    public ListaCodigos()
    {
        Margin = new Thickness(0, 4, 0, 4);
        BorderBrush = Brushes.LightGray;
        BorderThickness = new Thickness(1);
        Padding = new Thickness(4);
        _tabla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _tabla.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _tabla.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Child = _tabla;
        Encabezado();
    }

    public bool TieneFilas => _filas.Count > 0;

    public IReadOnlyList<string> Codigos => _filas.Select(f => f.Codigo).ToList();

    /// <summary>Vuelve a llenar la lista con los códigos dados y marca los de cada superficie.</summary>
    public void Cargar(IEnumerable<string> codigos, IEnumerable<string> rasante, IEnumerable<string> subRasante)
    {
        _cargando = true;
        try
        {
            _tabla.Children.Clear();
            _tabla.RowDefinitions.Clear();
            _filas.Clear();
            Encabezado();
            var r = new HashSet<string>(rasante, StringComparer.OrdinalIgnoreCase);
            var s = new HashSet<string>(subRasante, StringComparer.OrdinalIgnoreCase);
            foreach (string c in codigos.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
                AgregarFila(c, r.Contains(c), s.Contains(c));
        }
        finally
        {
            _cargando = false;
        }
        Seleccionar(_fijado);
    }

    public List<string> Marcados(SuperficieMateriales superficie) => _filas
        .Where(f => (superficie == SuperficieMateriales.Rasante ? f.Rasante : f.SubRasante).IsChecked == true)
        .Select(f => f.Codigo)
        .ToList();

    /// <summary>Marca esos códigos (agrega las filas que falten). Si <paramref name="soloEstos"/>, desmarca los demás.</summary>
    public void Marcar(SuperficieMateriales superficie, IEnumerable<string> codigos, bool soloEstos)
    {
        var lista = new HashSet<string>(codigos, StringComparer.OrdinalIgnoreCase);
        _cargando = true;
        try
        {
            foreach (string c in lista.Where(c => _filas.All(f => !string.Equals(f.Codigo, c, StringComparison.OrdinalIgnoreCase))))
                AgregarFila(c, false, false);
            foreach (Fila f in _filas)
            {
                CheckBox casilla = superficie == SuperficieMateriales.Rasante ? f.Rasante : f.SubRasante;
                if (lista.Contains(f.Codigo)) casilla.IsChecked = true;
                else if (soloEstos) casilla.IsChecked = false;
            }
        }
        finally
        {
            _cargando = false;
        }
        Cambio?.Invoke();
    }

    /// <summary>Deja resaltada la fila del código (o ninguna) y avisa al visor.</summary>
    public void Seleccionar(string? codigo)
    {
        _fijado = codigo != null && _filas.Any(f => string.Equals(f.Codigo, codigo, StringComparison.OrdinalIgnoreCase)) ? codigo : null;
        foreach (Fila f in _filas)
            f.Fondo.Background = string.Equals(f.Codigo, _fijado, StringComparison.OrdinalIgnoreCase) ? Seleccion : Transparente;
        Resaltar?.Invoke(_fijado);
    }

    private void Encabezado()
    {
        _tabla.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Poner(new TextBlock { Text = "Código", FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 2, 8, 4) }, 0, 0);
        Poner(new TextBlock { Text = "Rasante", FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 2, 8, 4) }, 0, 1);
        Poner(new TextBlock { Text = "SubRasante", FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 2, 4, 4) }, 0, 2);
    }

    private void AgregarFila(string codigo, bool rasante, bool subRasante)
    {
        int n = _tabla.RowDefinitions.Count;
        _tabla.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var fondo = new Border { Background = Transparente, Cursor = Cursors.Hand };
        Grid.SetColumnSpan(fondo, 3);
        Poner(fondo, n, 0);

        var nombre = new TextBlock { Text = codigo, Margin = new Thickness(4, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
        Poner(nombre, n, 0);
        var r = new CheckBox { IsChecked = rasante, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(8, 2, 8, 2), ToolTip = "Rasante" };
        var s = new CheckBox { IsChecked = subRasante, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(8, 2, 4, 2), ToolTip = "SubRasante" };
        Poner(r, n, 1);
        Poner(s, n, 2);

        foreach (UIElement e in new UIElement[] { fondo, nombre, r, s })
        {
            e.MouseEnter += (_, _) => Resaltar?.Invoke(codigo);
            e.MouseLeave += (_, _) => Resaltar?.Invoke(_fijado);
        }
        MouseButtonEventHandler fijar = (_, _) => Seleccionar(string.Equals(_fijado, codigo, StringComparison.OrdinalIgnoreCase) ? null : codigo);
        fondo.MouseLeftButtonUp += fijar;
        nombre.MouseLeftButtonUp += fijar;
        foreach (CheckBox c in new[] { r, s })
        {
            c.Checked += (_, _) => AlCambiar();
            c.Unchecked += (_, _) => AlCambiar();
        }
        _filas.Add(new Fila { Codigo = codigo, Fondo = fondo, Rasante = r, SubRasante = s });
    }

    private void AlCambiar()
    {
        if (!_cargando) Cambio?.Invoke();
    }

    private void Poner(UIElement e, int fila, int columna)
    {
        Grid.SetRow(e, fila);
        Grid.SetColumn(e, columna);
        _tabla.Children.Add(e);
    }

    private static Brush Congelado(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }
}
