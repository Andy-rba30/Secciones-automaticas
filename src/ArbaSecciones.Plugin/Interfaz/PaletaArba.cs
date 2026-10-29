using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ArbaSecciones.Plugin.Interfaz;

/// <summary>Paleta acoplable con las cinco pestañas. Una sola instancia por sesión.</summary>
internal static class PaletaArba
{
    private static readonly Guid Id = new("6E3C2B7A-41D5-4F0B-9C1E-7A2D5B8F3C10");
    private static PaletteSet? _paleta;
    private static PanelPrincipal? _panel;

    public static void Mostrar()
    {
        if (_paleta == null)
        {
            _panel = new PanelPrincipal();
            _paleta = new PaletteSet("ARBA · Secciones", Id)
            {
                Style = PaletteSetStyles.ShowCloseButton | PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowPropertiesMenu,
                MinimumSize = new System.Drawing.Size(380, 520),
                DockEnabled = DockSides.Left | DockSides.Right,
            };
            _paleta.AddVisual("Secciones", _panel);
            _paleta.Size = new System.Drawing.Size(440, 820);

            AcApp.DocumentManager.DocumentActivated += AlActivarDocumento;
        }

        _paleta.Visible = true;

        // Se llama desde el comando ARBASEC: las listas se leen en el acto, dentro de ese comando.
        Document? doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc != null) _panel?.RefrescarListasEnComando(doc);
    }

    private static void AlActivarDocumento(object? sender, DocumentCollectionEventArgs e)
    {
        if (_paleta is { Visible: true }) _panel?.RefrescarListas();
    }
}
