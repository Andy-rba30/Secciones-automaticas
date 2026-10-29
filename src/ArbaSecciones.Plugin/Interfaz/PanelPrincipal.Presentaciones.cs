using System.Windows;
using System.Windows.Controls;
using ArbaSecciones.Core.Configuracion;
using ArbaSecciones.Plugin.Civil;
using ArbaSecciones.Plugin.Infraestructura;
using Microsoft.Win32;

namespace ArbaSecciones.Plugin.Interfaz;

internal sealed partial class PanelPrincipal
{
    private readonly TextBox _txPrefijo = Ui.Caja();
    private readonly TextBox _txDigitos = Ui.Caja();
    private readonly TextBox _txCorrelativo = Ui.Caja();
    private readonly TextBox _txPatronCodigo = Ui.Caja();
    private readonly TextBox _txPatronTramo = Ui.Caja();
    private readonly TextBox _txCapaViewport = Ui.Caja();
    private readonly TextBox _txPresentaciones = Ui.Informe(140);

    private readonly TextBox _txCarpeta = Ui.Caja();
    private readonly CheckBox _chkPdf = Ui.Casilla("PDF de cada presentación");
    private readonly CheckBox _chkDwg = Ui.Casilla("DWG independiente por lámina");
    private readonly TextBox _txDispositivo = Ui.Caja();
    private readonly TextBox _txPapel = Ui.Caja();
    private readonly TextBox _txNombreDwg = Ui.Caja();
    private readonly TextBox _txExportacion = Ui.Informe(140);

    private UIElement CrearPresentaciones()
    {
        var f = Ui.Formulario();
        Ui.Fila(f, "Prefijo del nombre", _txPrefijo);
        Ui.Fila(f, "Dígitos del correlativo", _txDigitos);
        Ui.Fila(f, "Correlativo inicial", _txCorrelativo);
        Ui.Fila(f, "Código de plano", _txPatronCodigo);
        Ui.Fila(f, "Texto del tramo", _txPatronTramo);
        Ui.Fila(f, "Capa del viewport", _txCapaViewport);

        return Ui.Pila(
            f,
            Ui.Texto("Código: {n} es el correlativo, {n:00} con ceros. Tramo: {kmi} y {kmf}. " +
                     "Las presentaciones se copian de la plantilla; el viewport se ajusta al área útil con la escala del acomodo.", atenuado: true),
            Ui.Boton("Crear presentaciones", CrearPresentacionesDibujo, principal: true),
            _txPresentaciones);
    }

    private void CargarPresentaciones()
    {
        ConfigPresentaciones p = Cfg.Presentaciones;
        _txPrefijo.Text = p.Prefijo;
        _txDigitos.Text = p.Digitos.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _txCorrelativo.Text = p.CorrelativoInicial.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _txPatronCodigo.Text = p.PatronCodigo;
        _txPatronTramo.Text = p.PatronTramo;
        _txCapaViewport.Text = p.CapaViewport;
    }

    private void LeerPresentaciones()
    {
        ConfigPresentaciones p = Cfg.Presentaciones;
        p.Prefijo = _txPrefijo.Text ?? "";
        p.Digitos = Ui.Entero(_txDigitos, "Dígitos del correlativo");
        p.CorrelativoInicial = Ui.Entero(_txCorrelativo, "Correlativo inicial");
        p.PatronCodigo = _txPatronCodigo.Text ?? "";
        p.PatronTramo = _txPatronTramo.Text ?? "";
        p.CapaViewport = string.IsNullOrWhiteSpace(_txCapaViewport.Text) ? "Defpoints" : _txCapaViewport.Text.Trim();
        if (p.Digitos is < 1 or > 6) throw new ErrorUsuario("Los dígitos del correlativo deben estar entre 1 y 6.");
        if (p.CorrelativoInicial < 0) throw new ErrorUsuario("El correlativo inicial no puede ser negativo.");
    }

    private void CrearPresentacionesDibujo()
    {
        LeerFormulario();
        Ejecutor.Ejecutar("Crear presentaciones", doc =>
        {
            List<string> creadas = ServicioPresentaciones.Crear(doc, Cfg);
            _txPresentaciones.Text = $"{creadas.Count} presentaciones creadas:\n" + string.Join("\n", creadas);
            RefrescarMembrete(doc);
            Mensaje($"Presentaciones creadas: {creadas.Count}.");
        });
    }

    private UIElement CrearExportar()
    {
        var f = Ui.Formulario();
        Ui.Fila(f, "Carpeta de salida", _txCarpeta);
        Ui.Fila(f, "Trazador", _txDispositivo);
        Ui.Fila(f, "Papel", _txPapel);
        Ui.Fila(f, "Presentación en el DWG", _txNombreDwg);

        return Ui.Pila(
            f,
            Ui.Boton("Elegir carpeta…", ElegirCarpeta),
            Ui.Linea(_chkPdf, _chkDwg),
            Ui.Texto("PDF a escala 1:1. Cada DWG lleva en el modelo solo las secciones y el cuadro de su lámina (sin rejilla) " +
                     "y una presentación con el membrete en 0,0,0; abre directamente en la presentación.", atenuado: true),
            Ui.Boton("Exportar", ExportarLaminas, principal: true),
            _txExportacion);
    }

    private void CargarExportar()
    {
        ConfigExportacion e = Cfg.Exportacion;
        _txCarpeta.Text = e.Carpeta ?? "";
        _chkPdf.IsChecked = e.Pdf;
        _chkDwg.IsChecked = e.Dwg;
        _txDispositivo.Text = e.Dispositivo;
        _txPapel.Text = e.Papel;
        _txNombreDwg.Text = e.NombrePresentacionDwg;
    }

    private void LeerExportar()
    {
        ConfigExportacion e = Cfg.Exportacion;
        e.Carpeta = string.IsNullOrWhiteSpace(_txCarpeta.Text) ? null : _txCarpeta.Text.Trim();
        e.Pdf = _chkPdf.IsChecked == true;
        e.Dwg = _chkDwg.IsChecked == true;
        e.Dispositivo = string.IsNullOrWhiteSpace(_txDispositivo.Text) ? "DWG To PDF.pc3" : _txDispositivo.Text.Trim();
        e.Papel = string.IsNullOrWhiteSpace(_txPapel.Text) ? "ISO_full_bleed_A1_(841.00_x_594.00_MM)" : _txPapel.Text.Trim();
        e.NombrePresentacionDwg = string.IsNullOrWhiteSpace(_txNombreDwg.Text) ? "Presentación 1" : _txNombreDwg.Text.Trim();
    }

    private void ElegirCarpeta()
    {
        var dlg = new OpenFolderDialog { Title = "Carpeta de salida de PDF y DWG" };
        if (!string.IsNullOrWhiteSpace(_txCarpeta.Text) && Directory.Exists(_txCarpeta.Text)) dlg.InitialDirectory = _txCarpeta.Text;
        if (dlg.ShowDialog() == true) _txCarpeta.Text = dlg.FolderName;
    }

    private void ExportarLaminas()
    {
        LeerFormulario();
        string carpeta = Cfg.Exportacion.Carpeta ?? throw new ErrorUsuario("Elige la carpeta de salida.");
        Ejecutor.Ejecutar("Exportar láminas", doc =>
        {
            ResultadoExportacion r = ServicioExportacion.Exportar(doc, Cfg, carpeta);
            _txExportacion.Text = $"{r.Pdf} PDF y {r.Dwg} DWG en:\n{carpeta}" +
                                  (r.Avisos.Count > 0 ? "\n\nAvisos:\n" + string.Join("\n", r.Avisos) : "");
            Mensaje($"Exportación terminada: {r.Pdf} PDF, {r.Dwg} DWG.");
        });
    }
}
