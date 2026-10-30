using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ArbaSecciones.Plugin.Arranque;

/// <summary>
/// Pestaña compartida "ARBA" de la cinta (Id: ARBA_PESTANA).
/// Se integra en la pestaña ARBA existente junto con los demás plugins (PlanimetriaCogo, VisibilidadParada, etc.).
/// Si la pestaña ya existe, agrega únicamente el panel "Secciones" sin duplicar la pestaña.
/// Se vuelve a crear al cambiar de espacio de trabajo (WSCURRENT).
/// </summary>
internal static class Cinta
{
    public const string IdPestana = "ARBA_PESTANA";
    public const string TituloPestana = "ARBA";
    private const string IdPanel = "ARBA_SECCIONES";
    private const string TituloPanel = "Secciones";
    private const string IdBotonPaleta = "ARBA_SECCIONES_PALETA";

    public static void Registrar()
    {
        if (ComponentManager.Ribbon != null)
        {
            Crear();
        }
        else
        {
            ComponentManager.ItemInitialized += AlInicializarCinta;
            AcApp.Idle += AlEstarInactivo;
        }

        AcApp.SystemVariableChanged += AlCambiarVariable;
    }

    private static void AlInicializarCinta(object? sender, RibbonItemEventArgs e)
    {
        if (ComponentManager.Ribbon == null) return;
        ComponentManager.ItemInitialized -= AlInicializarCinta;
        AcApp.Idle -= AlEstarInactivo;
        Crear();
    }

    private static void AlEstarInactivo(object? sender, EventArgs e)
    {
        if (ComponentManager.Ribbon == null) return;
        AcApp.Idle -= AlEstarInactivo;
        ComponentManager.ItemInitialized -= AlInicializarCinta;
        Crear();
    }

    private static void AlCambiarVariable(object? sender, Autodesk.AutoCAD.ApplicationServices.SystemVariableChangedEventArgs e)
    {
        if (string.Equals(e.Name, "WSCURRENT", StringComparison.OrdinalIgnoreCase))
        {
            Crear();
        }
    }

    /// <summary>
    /// Obtiene la pestaña ARBA compartida o la crea si ningún plugin la ha creado todavía.
    /// Si existe un duplicado antiguo creado con Id "ARBA", lo unifica y elimina para evitar pestañas dobles.
    /// </summary>
    public static RibbonTab? ObtenerPestana()
    {
        RibbonControl? cinta = ComponentManager.Ribbon;
        if (cinta == null) return null;

        RibbonTab? pestanaOficial = null;
        var pestañasArba = new List<RibbonTab>();

        foreach (var t in cinta.Tabs)
        {
            if (t.Id == IdPestana)
            {
                pestanaOficial = t;
            }
            if (t.Id == IdPestana ||
                string.Equals(t.Id, "ARBA", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Title, TituloPestana, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Name, TituloPestana, StringComparison.OrdinalIgnoreCase))
            {
                pestañasArba.Add(t);
            }
        }

        // Si ya existe la oficial, unificamos y removemos cualquier pestaña duplicada que haya quedado huérfana
        if (pestanaOficial != null)
        {
            foreach (var otra in pestañasArba.Where(t => !ReferenceEquals(t, pestanaOficial)).ToList())
            {
                // Mover paneles que no existan en la oficial
                foreach (var p in otra.Panels.ToList())
                {
                    otra.Panels.Remove(p);
                    if (!pestanaOficial.Panels.Any(op => op.Source != null && op.Source.Id == p.Source?.Id))
                    {
                        pestanaOficial.Panels.Add(p);
                    }
                }
                cinta.Tabs.Remove(otra);
            }
            return pestanaOficial;
        }

        // Si no existe con Id oficial pero sí por título/nombre, reutilizar la primera encontrada
        if (pestañasArba.Count > 0)
        {
            var principal = pestañasArba[0];
            return principal;
        }

        // No existe ninguna pestaña ARBA; crear la pestaña estándar compartida
        var nueva = new RibbonTab
        {
            Id = IdPestana,
            Title = TituloPestana,
            Name = TituloPestana,
            IsVisible = true
        };
        cinta.Tabs.Add(nueva);
        return nueva;
    }

    /// <summary>Obtiene el panel de Secciones dentro de la pestaña ARBA o lo crea si no existe.</summary>
    public static RibbonPanel? ObtenerPanel(string id, string titulo)
    {
        var pestana = ObtenerPestana();
        if (pestana == null) return null;

        foreach (var p in pestana.Panels)
        {
            if (p.Source != null && (p.Source.Id == id || string.Equals(p.Source.Title, titulo, StringComparison.OrdinalIgnoreCase)))
                return p;
        }

        var panel = new RibbonPanel
        {
            Source = new RibbonPanelSource
            {
                Id = id,
                Title = titulo,
                Name = titulo
            }
        };
        pestana.Panels.Add(panel);
        return panel;
    }

    private static void Crear()
    {
        try
        {
            var panel = ObtenerPanel(IdPanel, TituloPanel);
            if (panel == null) return;

            // Evitar duplicar el botón si ya está presente en el panel
            foreach (var item in panel.Source.Items)
            {
                if (item is RibbonButton existente && existente.Id == IdBotonPaleta)
                    return;
            }

            var boton = new RibbonButton
            {
                Id = IdBotonPaleta,
                Name = "Secciones transversales",
                Text = "Secciones\ntransversales",
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                LargeImage = Iconos.Crear(32),
                Image = Iconos.Crear(16),
                CommandParameter = "ARBASEC",
                CommandHandler = new ComandoCinta("ARBASEC"),
                ToolTip = new RibbonToolTip
                {
                    Title = "Secciones transversales",
                    Command = "ARBASEC",
                    Content = "Líneas de muestreo, vistas de sección y armado de láminas por km (paleta ARBA).",
                    IsHelpEnabled = false,
                }
            };

            panel.Source.Items.Add(boton);
        }
        catch (System.Exception ex)
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            doc?.Editor.WriteMessage("\nArbaSecciones: error al configurar la pestaña ARBA: " + ex.Message);
        }
    }

    /// <summary>Envía el comando a la línea de comandos de AutoCAD/Civil 3D.</summary>
    private sealed class ComandoCinta : ICommand
    {
        private readonly string _comando;
        public ComandoCinta(string comando) => _comando = comando;

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            string cmd = parameter as string ?? (parameter as RibbonCommandItem)?.CommandParameter as string ?? _comando;
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            doc.SendStringToExecute("_." + cmd.TrimStart('_', '.') + " ", true, false, true);
        }
    }

    /// <summary>Ícono vectorial dibujado en código (terreno y rasante) para no depender de archivos de imagen externos.</summary>
    private static class Iconos
    {
        public static ImageSource Crear(int lado)
        {
            double s = lado / 32.0;
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                // Fondo redondeado elegante en gris oscuro / acero suave
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x2D, 0x37, 0x48)), null, new Rect(0, 0, lado, lado), 4 * s, 4 * s);

                // Cuadro interno de la sección
                var marco = new Pen(new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)), 1.0 * s);
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1A, 0x20, 0x2C)), marco, new Rect(3 * s, 5 * s, 26 * s, 22 * s));

                // Terreno natural (marrón claro / ocre)
                var terreno = new Pen(new SolidColorBrush(Color.FromRgb(0xD6, 0x9E, 0x2E)), 2.0 * s);
                dc.DrawLine(terreno, new Point(4 * s, 21 * s), new Point(11 * s, 15 * s));
                dc.DrawLine(terreno, new Point(11 * s, 15 * s), new Point(19 * s, 18 * s));
                dc.DrawLine(terreno, new Point(19 * s, 18 * s), new Point(28 * s, 11 * s));

                // Rasante / sección vial (azul cian brillante)
                var rasante = new Pen(new SolidColorBrush(Color.FromRgb(0x31, 0x82, 0xCE)), 2.5 * s);
                dc.DrawLine(rasante, new Point(9 * s, 19 * s), new Point(13 * s, 16 * s));
                dc.DrawLine(rasante, new Point(13 * s, 16 * s), new Point(21 * s, 16 * s));
                dc.DrawLine(rasante, new Point(21 * s, 16 * s), new Point(25 * s, 13 * s));
            }
            var bmp = new RenderTargetBitmap(lado, lado, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            return bmp;
        }
    }
}
