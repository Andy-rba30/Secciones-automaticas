using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using ArbaSecciones.Core.Comun;
using ArbaSecciones.Core.Configuracion;
using ArbaSecciones.Plugin.Civil;
using ArbaSecciones.Plugin.Infraestructura;
using Microsoft.Win32;

namespace ArbaSecciones.Plugin.Interfaz;

/// <summary>
/// Contenido de la paleta: Muestreo | Membrete | Acomodo | Presentaciones | Exportar.
/// Cada pestaña tiene su vista previa y un botón que ejecuta solo esa etapa.
/// </summary>
internal sealed partial class PanelPrincipal : UserControl
{
    private readonly TextBlock _estado = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(8, 4, 8, 6),
        Opacity = 0.85,
    };

    private static ConfiguracionArba Cfg => Estado.Configuracion;

    public PanelPrincipal()
    {
        var pestanas = new TabControl { Margin = new Thickness(4) };
        pestanas.Items.Add(new TabItem { Header = "Muestreo", Content = Ui.Desplazable(CrearMuestreo()) });
        pestanas.Items.Add(new TabItem { Header = "Membrete", Content = Ui.Desplazable(CrearMembrete()) });
        pestanas.Items.Add(new TabItem { Header = "Acomodo", Content = Ui.Desplazable(CrearAcomodo()) });
        pestanas.Items.Add(new TabItem { Header = "Presentaciones", Content = Ui.Desplazable(CrearPresentaciones()) });
        pestanas.Items.Add(new TabItem { Header = "Exportar", Content = Ui.Desplazable(CrearExportar()) });

        var barra = Ui.Linea(
            Ui.Boton("Guardar configuración…", GuardarConfiguracionComo),
            Ui.Boton("Cargar configuración…", CargarConfiguracionDesde),
            Ui.Boton("Logs", AbrirLogs));
        barra.Margin = new Thickness(8, 6, 8, 0);

        var raiz = new DockPanel();
        DockPanel.SetDock(barra, Dock.Top);
        DockPanel.SetDock(_estado, Dock.Bottom);
        raiz.Children.Add(barra);
        raiz.Children.Add(_estado);
        raiz.Children.Add(pestanas);
        Content = raiz;

        CargarFormulario();
        Mensaje("Configuración: " + Estado.RutaConfiguracion);
    }

    /// <summary>Vuelve a leer del dibujo activo las listas (alineamientos, estilos, presentaciones, bloques).</summary>
    public void RefrescarListas() => Ejecutor.Ejecutar("Leer listas del dibujo", CargarListas, silencioso: true);

    /// <summary>Lo mismo, cuando ya se está dentro de un comando (al abrir la paleta con ARBASEC).</summary>
    public void RefrescarListasEnComando(Autodesk.AutoCAD.ApplicationServices.Document doc) =>
        Ejecutor.EnComandoActual("Leer listas del dibujo", doc, CargarListas);

    private void CargarListas(Autodesk.AutoCAD.ApplicationServices.Document doc)
    {
        RefrescarMuestreo(doc);
        RefrescarMembrete(doc);
        RefrescarAcomodo(doc);
    }

    private void Mensaje(string texto) => _estado.Text = texto;

    private void CargarFormulario()
    {
        CargarMuestreo();
        CargarMembrete();
        CargarAcomodo();
        CargarPresentaciones();
        CargarExportar();
    }

    /// <summary>Pasa todos los controles a la configuración y la guarda.</summary>
    private void LeerFormulario()
    {
        LeerMuestreo();
        LeerMembrete();
        LeerAcomodo();
        LeerPresentaciones();
        LeerExportar();
        Estado.Guardar();
    }

    private void GuardarConfiguracionComo()
    {
        LeerFormulario();
        var dlg = new SaveFileDialog
        {
            Title = "Guardar configuración de ArbaSecciones",
            Filter = "Configuración ARBA (*.json)|*.json",
            FileName = Path.GetFileName(Estado.RutaConfiguracion),
            InitialDirectory = Path.GetDirectoryName(Estado.RutaConfiguracion),
        };
        if (dlg.ShowDialog() != true) return;
        Estado.GuardarComo(dlg.FileName);
        Mensaje("Configuración guardada en " + dlg.FileName);
    }

    private void CargarConfiguracionDesde()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Cargar configuración de ArbaSecciones",
            Filter = "Configuración ARBA (*.json)|*.json",
            InitialDirectory = Path.GetDirectoryName(Estado.RutaConfiguracion),
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            Estado.CargarDesde(dlg.FileName);
        }
        catch (System.Exception ex) when (ex is System.Text.Json.JsonException or IOException)
        {
            throw new ErrorUsuario("No se pudo leer la configuración: " + ex.Message);
        }
        Estado.UltimaMedicion = null;
        CargarFormulario();
        RefrescarListas();
        Mensaje("Configuración cargada de " + dlg.FileName);
    }

    private static void AbrirLogs()
    {
        Directory.CreateDirectory(Registro.CarpetaLogs);
        Process.Start(new ProcessStartInfo { FileName = Registro.CarpetaLogs, UseShellExecute = true });
    }

    /// <summary>Alineamiento elegido en la pestaña Muestreo (lo usan también Acomodo y Presentaciones).</summary>
    private ElementoDibujo? AlineamientoElegido => _cbAlineamiento.SelectedItem as ElementoDibujo;
}
