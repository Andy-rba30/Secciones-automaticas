using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ArbaSecciones.Plugin.Arranque;

/// <summary>
/// Pestaña "ARBA" de la cinta. Si otra herramienta ARBA ya creó la pestaña, solo se agrega el panel.
/// Se vuelve a crear al cambiar de espacio de trabajo (WSCURRENT).
/// </summary>
internal static class Cinta
{
    private const string IdPestana = "ARBA";
    private const string IdPanel = "ARBA_SECCIONES";

    public static void Registrar()
    {
        if (ComponentManager.Ribbon != null) Crear();
        else ComponentManager.ItemInitialized += AlInicializarCinta;

        AcApp.SystemVariableChanged += AlCambiarVariable;
    }

    private static void AlInicializarCinta(object? sender, RibbonItemEventArgs e)
    {
        if (ComponentManager.Ribbon == null) return;
        ComponentManager.ItemInitialized -= AlInicializarCinta;
        Crear();
    }

    private static void AlCambiarVariable(object? sender, Autodesk.AutoCAD.ApplicationServices.SystemVariableChangedEventArgs e)
    {
        if (string.Equals(e.Name, "WSCURRENT", StringComparison.OrdinalIgnoreCase)) Crear();
    }

    private static void Crear()
    {
        RibbonControl? cinta = ComponentManager.Ribbon;
        if (cinta == null) return;

        RibbonTab? pestana = cinta.Tabs.FirstOrDefault(t => t.Id == IdPestana);
        if (pestana == null)
        {
            pestana = new RibbonTab { Id = IdPestana, Title = "ARBA" };
            cinta.Tabs.Add(pestana);
        }
        if (pestana.Panels.Any(p => p.Source != null && p.Source.Id == IdPanel)) return;

        var fuente = new RibbonPanelSource { Id = IdPanel, Title = "Secciones" };
        fuente.Items.Add(new RibbonButton
        {
            Id = "ARBA_SECCIONES_PALETA",
            Text = "Secciones\ntransversales",
            ShowText = true,
            ShowImage = true,
            Size = RibbonItemSize.Large,
            Orientation = System.Windows.Controls.Orientation.Vertical,
            LargeImage = Iconos.Crear(32),
            Image = Iconos.Crear(16),
            CommandParameter = "_ARBASEC ",
            CommandHandler = new ManejadorComando(),
        });
        pestana.Panels.Add(new RibbonPanel { Source = fuente });
    }

    /// <summary>Envía el comando del botón a la línea de comandos del dibujo activo.</summary>
    private sealed class ManejadorComando : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            if (parameter is not RibbonCommandItem boton || boton.CommandParameter is not string comando) return;
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            doc?.SendStringToExecute(comando, true, false, false);
        }
    }

    /// <summary>Ícono dibujado en código (terreno y rasante) para no depender de archivos de imagen.</summary>
    private static class Iconos
    {
        public static ImageSource Crear(int lado)
        {
            double s = lado / 32.0;
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                var marco = new Pen(new SolidColorBrush(Color.FromRgb(0x5A, 0x63, 0x70)), 1.5 * s);
                var terreno = new Pen(new SolidColorBrush(Color.FromRgb(0x8B, 0x5E, 0x34)), 2 * s);
                var rasante = new Pen(new SolidColorBrush(Color.FromRgb(0x1F, 0x6F, 0xB5)), 2.5 * s);
                dc.DrawRectangle(Brushes.White, marco, new Rect(2 * s, 4 * s, 28 * s, 24 * s));
                dc.DrawLine(terreno, new Point(3 * s, 20 * s), new Point(10 * s, 14 * s));
                dc.DrawLine(terreno, new Point(10 * s, 14 * s), new Point(18 * s, 17 * s));
                dc.DrawLine(terreno, new Point(18 * s, 17 * s), new Point(29 * s, 10 * s));
                dc.DrawLine(rasante, new Point(8 * s, 18 * s), new Point(12 * s, 15 * s));
                dc.DrawLine(rasante, new Point(12 * s, 15 * s), new Point(20 * s, 15 * s));
                dc.DrawLine(rasante, new Point(20 * s, 15 * s), new Point(24 * s, 12 * s));
            }
            var bmp = new RenderTargetBitmap(lado, lado, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            return bmp;
        }
    }
}
