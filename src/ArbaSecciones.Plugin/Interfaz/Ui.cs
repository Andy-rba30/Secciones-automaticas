using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ArbaSecciones.Plugin.Infraestructura;

namespace ArbaSecciones.Plugin.Interfaz;

/// <summary>Constructores de controles WPF para armar la paleta en código, sin XAML.</summary>
internal static class Ui
{
    public static readonly Thickness Margen = new(0, 2, 0, 2);
    public static readonly FontFamily Monoespaciada = new("Consolas");

    public static TextBlock Texto(string texto, bool atenuado = false) => new()
    {
        Text = texto,
        TextWrapping = TextWrapping.Wrap,
        Margin = Margen,
        Opacity = atenuado ? 0.7 : 1.0,
    };

    public static TextBlock Titulo(string texto) => new()
    {
        Text = texto,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 8, 0, 4),
    };

    public static TextBox Caja(string valor = "", bool multilinea = false, double alto = double.NaN)
    {
        var t = new TextBox { Text = valor, Margin = Margen, VerticalContentAlignment = VerticalAlignment.Center };
        if (multilinea)
        {
            t.AcceptsReturn = true;
            t.TextWrapping = TextWrapping.Wrap;
            t.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            t.Height = double.IsNaN(alto) ? 60 : alto;
            t.VerticalContentAlignment = VerticalAlignment.Top;
        }
        return t;
    }

    public static TextBox Informe(double alto = 160) => new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = Monoespaciada,
        FontSize = 11,
        Height = alto,
        Margin = Margen,
    };

    public static Button Boton(string texto, Action alClic, bool principal = false)
    {
        var b = new Button
        {
            Content = texto,
            Margin = new Thickness(0, 3, 6, 3),
            Padding = new Thickness(10, 3, 10, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            FontWeight = principal ? FontWeights.SemiBold : FontWeights.Normal,
        };
        b.Click += (_, _) => Proteger(alClic);
        return b;
    }

    public static CheckBox Casilla(string texto, bool valor = false) => new()
    {
        Content = texto,
        IsChecked = valor,
        Margin = new Thickness(0, 2, 12, 2),
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static ComboBox Combo(bool editable = false) => new()
    {
        IsEditable = editable,
        Margin = Margen,
        MinWidth = 120,
    };

    /// <summary>Formulario de dos columnas: etiqueta y control.</summary>
    public static Grid Formulario()
    {
        var g = new Grid { Margin = Margen };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return g;
    }

    public static void Fila(Grid g, string etiqueta, UIElement control)
    {
        int fila = g.RowDefinitions.Count;
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var e = new TextBlock { Text = etiqueta, Margin = new Thickness(0, 4, 8, 4), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(e, fila);
        Grid.SetColumn(e, 0);
        Grid.SetRow(control, fila);
        Grid.SetColumn(control, 1);
        g.Children.Add(e);
        g.Children.Add(control);
    }

    public static StackPanel Pila(params UIElement[] hijos)
    {
        var p = new StackPanel { Orientation = Orientation.Vertical };
        foreach (var h in hijos) p.Children.Add(h);
        return p;
    }

    public static WrapPanel Linea(params UIElement[] hijos)
    {
        var p = new WrapPanel { Orientation = Orientation.Horizontal, Margin = Margen };
        foreach (var h in hijos) p.Children.Add(h);
        return p;
    }

    public static GroupBox Grupo(string titulo, UIElement contenido) => new()
    {
        Header = titulo,
        Content = contenido,
        Margin = new Thickness(0, 4, 0, 6),
        Padding = new Thickness(6),
    };

    public static ScrollViewer Desplazable(UIElement contenido) => new()
    {
        Content = contenido,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Padding = new Thickness(8),
    };

    /// <summary>Lee un número del cuadro de texto (acepta coma o punto decimal).</summary>
    public static double Numero(TextBox caja, string campo)
    {
        string t = (caja.Text ?? string.Empty).Trim().Replace(',', '.');
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && !double.IsNaN(v) && !double.IsInfinity(v))
            return v;
        throw new ErrorUsuario($"\"{campo}\" debe ser un número.");
    }

    public static int Entero(TextBox caja, string campo)
    {
        if (int.TryParse((caja.Text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) return v;
        throw new ErrorUsuario($"\"{campo}\" debe ser un número entero.");
    }

    public static string Num(double v, string formato = "0.###") => v.ToString(formato, CultureInfo.InvariantCulture);

    /// <summary>Los errores de datos de la paleta se muestran como aviso y no cierran nada.</summary>
    public static void Proteger(Action accion)
    {
        try
        {
            accion();
        }
        catch (ErrorUsuario ex)
        {
            Dialogos.Aviso(ex.Message);
        }
        catch (OperacionCancelada)
        {
        }
        catch (System.Exception ex)
        {
            ArbaSecciones.Core.Comun.Registro.Error("Paleta", ex);
            Dialogos.Error("Paleta", ex);
        }
    }

    /// <summary>Selecciona en el combo el elemento cuyo texto coincide; si es editable y no está, escribe el texto.</summary>
    public static void Seleccionar(ComboBox combo, string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return;
        foreach (object item in combo.Items)
        {
            if (string.Equals(item?.ToString(), texto, StringComparison.CurrentCultureIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
        if (combo.IsEditable) combo.Text = texto;
    }
}
