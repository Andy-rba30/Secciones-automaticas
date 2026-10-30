using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ArbaSecciones.Core.Comun;
using ArbaSecciones.Core.Configuracion;
using ArbaSecciones.Core.Materiales;
using ArbaSecciones.Core.Tablas;
using ArbaSecciones.Plugin.Civil;
using ArbaSecciones.Plugin.Infraestructura;
using Autodesk.AutoCAD.ApplicationServices;
using Microsoft.Win32;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;

namespace ArbaSecciones.Plugin.Interfaz;

/// <summary>
/// Pestaña Materiales: corredor → visor y códigos → validación → superficies → materiales → tablas / CSV.
/// La detección y la validación son de Core (sin AutoCAD); aquí solo se lee el dibujo y se muestra.
/// </summary>
internal sealed partial class PanelPrincipal
{
    private const string ComparacionCorredor = "(SubRasante del corredor)";
    private const string EstiloPorDefecto = "(estilo por defecto)";

    private readonly ComboBox _cbCorredor = Ui.Combo();
    private readonly ComboBox _cbLineaBase = Ui.Combo();
    private readonly ComboBox _cbProgresivaMat = Ui.Combo();
    private readonly TextBox _txProgresivaMat = Ui.Caja();
    private readonly TextBlock _lbSeccion = Ui.Texto("Elige el corredor y una progresiva, y presiona «Leer sección».", atenuado: true);
    private readonly VisorSeccion _visor = new();
    private readonly ComboBox _cbExageracion = Ui.Combo();
    private readonly TextBlock _lbPrevia = Ui.Texto("");
    private readonly ListaCodigos _listaCodigos = new();
    private readonly TextBlock _lbDeteccion = Ui.Texto("", atenuado: true);
    private readonly CheckBox _chkValidarGrupo = Ui.Casilla("Revisar también todas las progresivas del grupo de muestreo");
    private readonly TextBlock _lbEstadoValidacion = Ui.Texto("");
    private readonly TextBox _txValidacion = Ui.Informe(170);
    private readonly TextBox _txNombreRasante = Ui.Caja();
    private readonly TextBox _txNombreSubRasante = Ui.Caja();
    private readonly CheckBox _chkCrearRasante = Ui.Casilla("Crear también la Rasante");
    private readonly ComboBox _cbEstiloSuperficie = Ui.Combo();
    private readonly ComboBox _cbVoladizoRasante = Ui.Combo();
    private readonly ComboBox _cbVoladizoSubRasante = Ui.Combo();
    private readonly ComboBox _cbTerreno = Ui.Combo();
    private readonly ComboBox _cbComparacion = Ui.Combo();
    private readonly TextBox _txNombreLista = Ui.Caja();
    private readonly TextBox _txEsponjamiento = Ui.Caja();
    private readonly TextBox _txCompactacion = Ui.Caja();
    private readonly WrapPanel _panelFormas = new();
    private readonly CheckBox _chkTablaVistas = Ui.Casilla("Agregar la tabla de volumen de la lista a cada vista de sección");
    private readonly CheckBox _chkCalcularIgual = Ui.Casilla("Calcular igual aunque la SubRasante tenga huecos (bajo mi responsabilidad)");
    private readonly TextBlock _lbVolumenes = Ui.Texto("", atenuado: true);

    // Tabla de volúmenes: estándar (AutoCAD) o dinámica nativa de Civil 3D.
    private const string EstiloDelComando = "(el que tenga el comando)";
    private readonly RadioButton _rbTablaEstandar = Ui.Radio("Tabla estándar (existente)", "TipoTablaVolumen", valor: true);
    private readonly RadioButton _rbTablaDinamica = Ui.Radio("Tabla dinámica nativa de Civil 3D (se actualiza automáticamente)", "TipoTablaVolumen");
    private readonly ComboBox _cbClaseTabla = Ui.Combo();
    private readonly ComboBox _cbEstiloTabla = Ui.Combo();
    private readonly CheckBox _chkDividirTabla = Ui.Casilla("Dividir tabla si excede altura", valor: true);
    private readonly TextBox _txMaxFilasTabla = Ui.Caja();
    private readonly TextBox _txEspaciadoTabla = Ui.Caja();
    private readonly ComboBox _cbDireccionTabla = Ui.Combo();
    private readonly ComboBox _cbAlineacionTabla = Ui.Combo();
    private readonly TextBox _txMargenTabla = Ui.Caja();
    private readonly TextBlock _lbAreaTabla = Ui.Texto("", atenuado: true);
    private readonly StackPanel _panelTablaDinamica = new();
    private bool _cargandoTabla;
    private readonly DataGrid _dgVolumenes = new()
    {
        AutoGenerateColumns = true,
        IsReadOnly = true,
        Height = 220,
        Margin = Ui.Margen,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        CanUserAddRows = false,
    };

    private SeccionCorredor? _seccionMat;
    private List<double> _progresivasGrupoMat = new();
    private List<double> _progresivasAplicadas = new();
    private List<FilaVolumen> _filasVolumen = new();
    private readonly HashSet<string> _sugeridosRasante = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _sugeridosSubRasante = new(StringComparer.OrdinalIgnoreCase);
    private bool _cargandoMat;

    /// <summary>Fila de la tabla de volúmenes de la paleta.</summary>
    public sealed record FilaTablaVolumen(string Progresiva, string AreaCorte, string AreaRelleno, string VolumenCorte,
        string VolumenRelleno, string AcumuladoCorte, string AcumuladoRelleno, string Masa);

    private UIElement CrearMateriales()
    {
        foreach (string e in new[] { "1×", "2×", "5×" }) _cbExageracion.Items.Add(e);
        _cbExageracion.SelectedIndex = 0;
        _cbExageracion.SelectionChanged += (_, _) => _visor.Exageracion = _cbExageracion.SelectedIndex switch { 1 => 2, 2 => 5, _ => 1 };
        foreach (ComboBox c in new[] { _cbVoladizoRasante, _cbVoladizoSubRasante })
        {
            c.Items.Add("Ninguna");
            c.Items.Add("Top Links (vínculos superiores)");
            c.Items.Add("Bottom Links (vínculos inferiores)");
            c.SelectionChanged += (_, _) => Ui.Proteger(ActualizarVistaPrevia);
        }

        _cbCorredor.SelectionChanged += (_, _) => AlCambiarCorredor();
        _cbProgresivaMat.DropDownClosed += (_, _) => _txProgresivaMat.Text = ""; // elegir del grupo anula la progresiva escrita
        _listaCodigos.Cambio += () => Ui.Proteger(ActualizarVistaPrevia);
        _listaCodigos.Resaltar += c => _visor.Resaltar(c);
        _visor.VinculoElegido += v =>
        {
            string? codigo = v.Codigos.FirstOrDefault(c => _listaCodigos.Marcados(SuperficieMateriales.SubRasante).Contains(c, StringComparer.OrdinalIgnoreCase))
                             ?? v.Codigos.FirstOrDefault();
            _listaCodigos.Seleccionar(codigo);
            Mensaje(VisorSeccion.Describir(v).Replace("\n", " · "));
        };
        _txValidacion.MouseDoubleClick += (_, _) => Ui.Proteger(IrAProgresivaDelInforme);
        _dgVolumenes.AutoGeneratingColumn += (_, e) =>
        {
            int i = Array.IndexOf(new[] { "Progresiva", "AreaCorte", "AreaRelleno", "VolumenCorte", "VolumenRelleno", "AcumuladoCorte", "AcumuladoRelleno", "Masa" }, e.PropertyName);
            if (i >= 0) e.Column.Header = CalculadorVolumenes.Encabezados[i];
        };

        _cbClaseTabla.Items.Add("Volumen total (AddTotalVolumeTable)");
        _cbClaseTabla.Items.Add("Volumen de material (AddMaterialVolumeTable)");
        _cbDireccionTabla.Items.Add("Horizontal (columnas hacia la derecha)");
        _cbDireccionTabla.Items.Add("Vertical (partes hacia abajo)");
        foreach (string a in new[] { "Superior izquierda", "Superior centro", "Superior derecha", "Centro izquierda", "Centro",
                                     "Centro derecha", "Inferior izquierda", "Inferior centro", "Inferior derecha" })
            _cbAlineacionTabla.Items.Add(a);
        _rbTablaEstandar.Checked += (_, _) => ActualizarTipoTabla();
        _rbTablaDinamica.Checked += (_, _) => ActualizarTipoTabla();
        _chkDividirTabla.Checked += (_, _) => ActualizarTipoTabla();
        _chkDividirTabla.Unchecked += (_, _) => ActualizarTipoTabla();
        _cbClaseTabla.SelectionChanged += (_, _) =>
        {
            if (_cargandoTabla) return;
            Ejecutor.Ejecutar("Leer estilos de tabla", LlenarEstilosTabla, silencioso: true);
        };

        var tablaDinamica = Ui.Formulario();
        Ui.Fila(tablaDinamica, "Tipo de tabla nativa", _cbClaseTabla);
        Ui.Fila(tablaDinamica, "Estilo de tabla de volúmenes", _cbEstiloTabla);
        Ui.Fila(tablaDinamica, "", _chkDividirTabla);
        Ui.Fila(tablaDinamica, "Máx. filas por columna (0 = auto)", _txMaxFilasTabla);
        Ui.Fila(tablaDinamica, "Espaciado entre columnas (papel)", _txEspaciadoTabla);
        Ui.Fila(tablaDinamica, "Dirección de la división", _cbDireccionTabla);
        _panelTablaDinamica.Children.Add(tablaDinamica);
        _panelTablaDinamica.Children.Add(Ui.Texto(
            "Se abre el cuadro de Civil 3D: elige el alineamiento, el grupo de muestreo y la lista de materiales, y coloca la tabla. " +
            "Luego se mide, se valida contra el área y se alinea en ella. Si no entra, se avisa con las medidas que faltan.", atenuado: true));

        var ubicacionTabla = Ui.Formulario();
        Ui.Fila(ubicacionTabla, "Alineación en el área", _cbAlineacionTabla);
        Ui.Fila(ubicacionTabla, "Margen dentro del área (papel)", _txMargenTabla);
        var areaTabla = Ui.Pila(
            Ui.Texto("Área en la lámina (para validar y ubicar la tabla):"),
            ubicacionTabla,
            Ui.Linea(
                Ui.Boton("Definir área para tabla en lámina…", DefinirAreaTabla),
                Ui.Boton("Usar franja del cuadro…", UsarFranjaTabla),
                Ui.Boton("Quitar área", QuitarAreaTabla)),
            _lbAreaTabla);

        var corredor = Ui.Formulario();
        Ui.Fila(corredor, "Corredor", _cbCorredor);
        Ui.Fila(corredor, "Línea base", _cbLineaBase);
        Ui.Fila(corredor, "Progresiva (grupo de muestreo)", _cbProgresivaMat);
        Ui.Fila(corredor, "o escribe una progresiva", _txProgresivaMat);

        var superficies = Ui.Formulario();
        Ui.Fila(superficies, "Nombre de la Rasante", _txNombreRasante);
        Ui.Fila(superficies, "Nombre de la SubRasante", _txNombreSubRasante);
        Ui.Fila(superficies, "Estilo de superficie", _cbEstiloSuperficie);
        Ui.Fila(superficies, "Voladizo de la Rasante", _cbVoladizoRasante);
        Ui.Fila(superficies, "Voladizo de la SubRasante", _cbVoladizoSubRasante);

        var materiales = Ui.Formulario();
        Ui.Fila(materiales, "Terreno natural", _cbTerreno);
        Ui.Fila(materiales, "Comparar con", _cbComparacion);
        Ui.Fila(materiales, "Lista de materiales", _txNombreLista);
        Ui.Fila(materiales, "Esponjamiento (corte)", _txEsponjamiento);
        Ui.Fila(materiales, "Compactación (relleno)", _txCompactacion);

        return Ui.Pila(
            Ui.Linea(Ui.Boton("Actualizar listas", RefrescarListas)),
            Ui.Grupo("1. Corredor y progresiva", Ui.Pila(
                corredor,
                Ui.Linea(Ui.Boton("◀", () => MoverProgresiva(-1)), Ui.Boton("Leer sección", LeerSeccionMat, principal: true), Ui.Boton("▶", () => MoverProgresiva(1))),
                _lbSeccion)),
            Ui.Grupo("2. Sección y códigos", Ui.Pila(
                Ui.Linea(Ui.Texto("Exageración vertical"), _cbExageracion, Ui.Boton("Ajustar", () => _visor.Ajustar())),
                _visor,
                Ui.Texto("Rueda: zoom · arrastrar: mover · clic en una línea: ver su código. Pasa el ratón por un código de la lista para verlo en la sección.", atenuado: true),
                _lbPrevia,
                Ui.Boton("Detectar automáticamente", DetectarCodigos, principal: true),
                _lbDeteccion,
                _listaCodigos)),
            Ui.Grupo("3. Validación", Ui.Pila(
                _chkValidarGrupo,
                Ui.Linea(
                    Ui.Boton("Validar esta sección", ValidarSeccionActual),
                    Ui.Boton("Validar todo el corredor", ValidarCorredor, principal: true),
                    Ui.Boton("Aplicar sugerencias", AplicarSugerencias)),
                _lbEstadoValidacion,
                _txValidacion,
                Ui.Texto("Doble clic en una línea del informe para ver esa progresiva en el visor.", atenuado: true))),
            Ui.Grupo("4. Superficies del corredor", Ui.Pila(
                superficies,
                _chkCrearRasante,
                Ui.Boton("Crear / actualizar superficies", CrearSuperficiesCorredor, principal: true),
                Ui.Texto("Los códigos se agregan como líneas de rotura, con la corrección de voladizo elegida y el contorno " +
                         "«Extensión del corredor como contorno exterior». Al final se reconstruye el corredor.", atenuado: true))),
            Ui.Grupo("5. Materiales", Ui.Pila(
                materiales,
                Ui.Texto("Estructuras (formas del corredor, tipo de cantidad Estructuras):", atenuado: true),
                _panelFormas,
                _chkTablaVistas,
                _chkCalcularIgual,
                Ui.Boton("Calcular materiales", CalcularMateriales, principal: true),
                Ui.Texto("Usa el grupo de líneas de muestreo de la pestaña Muestreo. Corte: debajo del terreno y encima de la SubRasante; " +
                         "relleno: encima del terreno y debajo de la SubRasante.", atenuado: true))),
            Ui.Grupo("6. Resultados", Ui.Pila(
                _lbVolumenes,
                _dgVolumenes,
                Ui.Titulo("Tabla de volúmenes"),
                _rbTablaEstandar,
                _rbTablaDinamica,
                _panelTablaDinamica,
                areaTabla,
                Ui.Linea(
                    Ui.Boton("Exportar CSV…", ExportarCsv),
                    Ui.Boton("Insertar tabla de volúmenes", InsertarTablaVolumenes),
                    Ui.Boton("Validar y ubicar tabla existente…", UbicarTablaExistente)))));
    }

    private ElementoDibujo? CorredorElegido => _cbCorredor.SelectedItem as ElementoDibujo;

    private static CorreccionVoladizo VoladizoDe(ComboBox c) => c.SelectedIndex switch
    {
        1 => CorreccionVoladizo.VinculosSuperiores,
        2 => CorreccionVoladizo.VinculosInferiores,
        _ => CorreccionVoladizo.Ninguna,
    };

    private void CargarMateriales()
    {
        ConfigMateriales m = Cfg.Materiales;
        _txNombreRasante.Text = m.NombreRasante;
        _txNombreSubRasante.Text = m.NombreSubRasante;
        _chkCrearRasante.IsChecked = m.CrearRasante;
        _cbVoladizoRasante.SelectedIndex = (int)m.VoladizoRasante;
        _cbVoladizoSubRasante.SelectedIndex = (int)m.VoladizoSubRasante;
        _txNombreLista.Text = m.NombreLista;
        _txEsponjamiento.Text = Ui.Num(m.FactorEsponjamiento, "0.00");
        _txCompactacion.Text = Ui.Num(m.FactorCompactacion, "0.00");
        _chkValidarGrupo.IsChecked = m.ValidarGrupoMuestreo;
        _chkTablaVistas.IsChecked = m.TablaEnVistas;
        CargarTablaVolumen();
    }

    private void CargarTablaVolumen()
    {
        ConfigTablaVolumen t = Cfg.Materiales.TablaVolumen;
        _cargandoTabla = true;
        try
        {
            _rbTablaDinamica.IsChecked = t.Tipo == TipoTablaVolumen.DinamicaNativa;
            _rbTablaEstandar.IsChecked = t.Tipo != TipoTablaVolumen.DinamicaNativa;
            _cbClaseTabla.SelectedIndex = t.Clase == ClaseTablaVolumen.Material ? 1 : 0;
            _chkDividirTabla.IsChecked = t.Dividir;
            _txMaxFilasTabla.Text = t.MaxFilasPorColumna.ToString(CultureInfo.InvariantCulture);
            _txEspaciadoTabla.Text = Ui.Num(t.EspaciadoColumnas);
            _cbDireccionTabla.SelectedIndex = t.Direccion == DireccionDivision.Vertical ? 1 : 0;
            _cbAlineacionTabla.SelectedIndex = (int)t.Alineacion;
            _txMargenTabla.Text = Ui.Num(t.Margen);
        }
        finally
        {
            _cargandoTabla = false;
        }
        MostrarAreaTabla();
        ActualizarTipoTabla();
    }

    private void LeerMateriales()
    {
        ConfigMateriales m = Cfg.Materiales;
        if (CorredorElegido is ElementoDibujo c) m.Corredor = c.Nombre;
        if (_cbLineaBase.SelectedItem is string lb) m.LineaBase = lb;
        if (_listaCodigos.TieneFilas)
        {
            m.CodigosRasante = _listaCodigos.Marcados(SuperficieMateriales.Rasante);
            m.CodigosSubRasante = _listaCodigos.Marcados(SuperficieMateriales.SubRasante);
        }
        m.NombreRasante = string.IsNullOrWhiteSpace(_txNombreRasante.Text) ? "Rasante" : _txNombreRasante.Text.Trim();
        m.NombreSubRasante = string.IsNullOrWhiteSpace(_txNombreSubRasante.Text) ? "SubRasante" : _txNombreSubRasante.Text.Trim();
        m.CrearRasante = _chkCrearRasante.IsChecked == true;
        if (_cbEstiloSuperficie.Items.Count > 0) m.EstiloSuperficie = _cbEstiloSuperficie.SelectedItem is ElementoDibujo e ? e.Nombre : null;
        m.VoladizoRasante = VoladizoDe(_cbVoladizoRasante);
        m.VoladizoSubRasante = VoladizoDe(_cbVoladizoSubRasante);
        if (_cbTerreno.SelectedItem is ElementoDibujo t) m.TerrenoNatural = t.Nombre;
        if (_cbComparacion.Items.Count > 0) m.SuperficieComparacion = _cbComparacion.SelectedItem is ElementoDibujo s ? s.Nombre : null;
        m.NombreLista = string.IsNullOrWhiteSpace(_txNombreLista.Text) ? "Movimiento de tierras" : _txNombreLista.Text.Trim();
        m.FactorEsponjamiento = Ui.Numero(_txEsponjamiento, "Esponjamiento");
        m.FactorCompactacion = Ui.Numero(_txCompactacion, "Compactación");
        if (!(m.FactorEsponjamiento > 0) || !(m.FactorCompactacion > 0)) throw new ErrorUsuario("Los factores deben ser mayores que cero.");
        if (_panelFormas.Children.Count > 0)
            m.CodigosForma = _panelFormas.Children.OfType<CheckBox>().Where(x => x.IsChecked == true).Select(x => x.Tag as string ?? "").Where(x => x.Length > 0).ToList();
        m.ValidarGrupoMuestreo = _chkValidarGrupo.IsChecked == true;
        m.TablaEnVistas = _chkTablaVistas.IsChecked == true;
        LeerTablaVolumen();
    }

    private void LeerTablaVolumen()
    {
        ConfigTablaVolumen t = Cfg.Materiales.TablaVolumen;
        t.Tipo = _rbTablaDinamica.IsChecked == true ? TipoTablaVolumen.DinamicaNativa : TipoTablaVolumen.Estandar;
        t.Clase = _cbClaseTabla.SelectedIndex == 1 ? ClaseTablaVolumen.Material : ClaseTablaVolumen.VolumenTotal;
        if (_cbEstiloTabla.Items.Count > 0) t.Estilo = _cbEstiloTabla.SelectedItem is ElementoDibujo e ? e.Nombre : null;
        t.Dividir = _chkDividirTabla.IsChecked == true;
        string filas = (_txMaxFilasTabla.Text ?? string.Empty).Trim();
        t.MaxFilasPorColumna = filas.Length == 0 || filas.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? 0
            : Ui.Entero(_txMaxFilasTabla, "Máx. filas por columna");
        t.EspaciadoColumnas = Ui.Numero(_txEspaciadoTabla, "Espaciado entre columnas");
        t.Direccion = _cbDireccionTabla.SelectedIndex == 1 ? DireccionDivision.Vertical : DireccionDivision.Horizontal;
        t.Alineacion = _cbAlineacionTabla.SelectedIndex >= 0 ? (AlineacionTabla)_cbAlineacionTabla.SelectedIndex : AlineacionTabla.SuperiorIzquierda;
        t.Margen = Ui.Numero(_txMargenTabla, "Margen dentro del área");
        if (t.MaxFilasPorColumna < 0) throw new ErrorUsuario("El máximo de filas por columna no puede ser negativo (0 = automático).");
        if (t.EspaciadoColumnas < 0 || t.Margen < 0) throw new ErrorUsuario("El espaciado y el margen de la tabla no pueden ser negativos.");
    }

    private void ActualizarTipoTabla()
    {
        bool dinamica = _rbTablaDinamica.IsChecked == true;
        _panelTablaDinamica.IsEnabled = dinamica;
        bool dividir = _chkDividirTabla.IsChecked == true;
        _txMaxFilasTabla.IsEnabled = dividir;
        _txEspaciadoTabla.IsEnabled = dividir;
        _cbDireccionTabla.IsEnabled = dividir;
    }

    private void MostrarAreaTabla()
    {
        AreaTabla? a = Cfg.Materiales.TablaVolumen.Area;
        _lbAreaTabla.Text = a is { Definida: true }
            ? $"Área: {Ui.Num(a.Ancho, "0.##")} × {Ui.Num(a.Alto, "0.##")} " +
              (a.EnModelo ? "unidades de modelo (espacio modelo)." : $"unidades de papel (presentación «{a.Presentacion}»).")
            : "Sin área: la tabla queda donde la coloques y no se valida.";
    }

    /// <summary>Estilos de tabla de volumen total o de material del dibujo, según el tipo elegido.</summary>
    private void LlenarEstilosTabla(Document doc)
    {
        ClaseTablaVolumen clase = _cbClaseTabla.SelectedIndex == 1 ? ClaseTablaVolumen.Material : ClaseTablaVolumen.VolumenTotal;
        string? previo = (_cbEstiloTabla.SelectedItem as ElementoDibujo)?.Nombre ?? Cfg.Materiales.TablaVolumen.Estilo;
        _cargandoTabla = true;
        try
        {
            _cbEstiloTabla.Items.Clear();
            _cbEstiloTabla.Items.Add(EstiloDelComando);
            foreach (ElementoDibujo e in ServicioTablasVolumen.Estilos(doc, clase)) _cbEstiloTabla.Items.Add(e);
            Ui.Seleccionar(_cbEstiloTabla, previo);
            if (_cbEstiloTabla.SelectedIndex < 0) _cbEstiloTabla.SelectedIndex = 0;
        }
        finally
        {
            _cargandoTabla = false;
        }
    }

    private void DefinirAreaTabla()
    {
        Ejecutor.Ejecutar("Definir área para la tabla", doc =>
        {
            Cfg.Materiales.TablaVolumen.Area = ServicioTablasVolumen.PedirArea(doc);
            Estado.Guardar();
            MostrarAreaTabla();
            Mensaje("Área para la tabla de volúmenes guardada.");
        });
    }

    private void UsarFranjaTabla()
    {
        Ejecutor.Ejecutar("Usar franja del cuadro", doc =>
        {
            Cfg.Materiales.TablaVolumen.Area = ServicioTablasVolumen.AreaDeRectangulo(doc);
            Estado.Guardar();
            MostrarAreaTabla();
            Mensaje("La franja elegida se usará como área para la tabla de volúmenes.");
        });
    }

    private void QuitarAreaTabla()
    {
        Cfg.Materiales.TablaVolumen.Area = null;
        Estado.Guardar();
        MostrarAreaTabla();
    }

    private void InsertarTablaDinamica()
    {
        Ejecutor.Ejecutar("Insertar tabla dinámica de volúmenes", doc =>
        {
            ObjectId grupoId = GrupoMuestreo(doc);
            Mensaje(ServicioTablasVolumen.CrearDinamica(doc, Cfg, grupoId));
        });
    }

    private void UbicarTablaExistente()
    {
        LeerTablaVolumen();
        Estado.Guardar();
        Ejecutor.Ejecutar("Validar y ubicar tabla de volúmenes", doc =>
        {
            ObjectId grupoId = ObjectId.Null;
            try
            {
                grupoId = GrupoMuestreo(doc);
            }
            catch (ErrorUsuario)
            {
                // sin grupo no se puede sugerir una división, pero sí validar y mover
            }
            Mensaje(ServicioTablasVolumen.UbicarExistente(doc, Cfg, grupoId));
        });
    }

    /// <summary>Corredores, superficies y estilos del dibujo (al abrir la paleta y con «Actualizar listas»).</summary>
    private void RefrescarMateriales(Document doc)
    {
        ConfigMateriales m = Cfg.Materiales;
        _cargandoMat = true;
        try
        {
            string? corredorPrevio = CorredorElegido?.Nombre ?? m.Corredor;
            _cbCorredor.Items.Clear();
            foreach (ElementoDibujo c in ServicioCorredor.Corredores(doc)) _cbCorredor.Items.Add(c);
            Ui.Seleccionar(_cbCorredor, corredorPrevio);
            if (_cbCorredor.SelectedIndex < 0 && _cbCorredor.Items.Count > 0) _cbCorredor.SelectedIndex = 0;

            List<ElementoDibujo> superficies = ServicioCorredor.Superficies(doc);
            string? terrenoPrevio = (_cbTerreno.SelectedItem as ElementoDibujo)?.Nombre ?? m.TerrenoNatural;
            _cbTerreno.Items.Clear();
            foreach (ElementoDibujo s in superficies) _cbTerreno.Items.Add(s);
            Ui.Seleccionar(_cbTerreno, terrenoPrevio);

            string? comparacionPrevia = _cbComparacion.SelectedItem?.ToString() ?? m.SuperficieComparacion;
            _cbComparacion.Items.Clear();
            _cbComparacion.Items.Add(ComparacionCorredor);
            foreach (ElementoDibujo s in superficies) _cbComparacion.Items.Add(s);
            Ui.Seleccionar(_cbComparacion, comparacionPrevia);
            if (_cbComparacion.SelectedIndex < 0) _cbComparacion.SelectedIndex = 0;

            string? estiloPrevio = (_cbEstiloSuperficie.SelectedItem as ElementoDibujo)?.Nombre ?? m.EstiloSuperficie;
            _cbEstiloSuperficie.Items.Clear();
            _cbEstiloSuperficie.Items.Add(EstiloPorDefecto);
            foreach (ElementoDibujo e in ServicioCorredor.EstilosSuperficie(doc)) _cbEstiloSuperficie.Items.Add(e);
            Ui.Seleccionar(_cbEstiloSuperficie, estiloPrevio);
            if (_cbEstiloSuperficie.SelectedIndex < 0) _cbEstiloSuperficie.SelectedIndex = 0;
        }
        finally
        {
            _cargandoMat = false;
        }
        LlenarEstilosTabla(doc);
        LlenarDatosCorredor(doc);
    }

    private void AlCambiarCorredor()
    {
        if (_cargandoMat || CorredorElegido == null) return;
        if (Ejecutor.Ocupado)
        {
            Document? doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc != null) LlenarDatosCorredor(doc);
            return;
        }
        Ejecutor.Ejecutar("Leer corredor", LlenarDatosCorredor, silencioso: true);
    }

    /// <summary>Líneas base, códigos de forma y progresivas (del grupo de muestreo y del corredor).</summary>
    private void LlenarDatosCorredor(Document doc)
    {
        ConfigMateriales m = Cfg.Materiales;
        _cbLineaBase.Items.Clear();
        _panelFormas.Children.Clear();
        _progresivasAplicadas = new List<double>();
        ElementoDibujo? c = CorredorElegido;
        if (c != null)
        {
            foreach (string b in ServicioCorredor.LineasBase(doc, c.Id)) _cbLineaBase.Items.Add(b);
            Ui.Seleccionar(_cbLineaBase, m.LineaBase);
            if (_cbLineaBase.SelectedIndex < 0 && _cbLineaBase.Items.Count > 0) _cbLineaBase.SelectedIndex = 0;

            var marcadas = new HashSet<string>(m.CodigosForma, StringComparer.OrdinalIgnoreCase);
            foreach (string f in ServicioCorredor.CodigosForma(doc, c.Id))
            {
                CheckBox casilla = Ui.Casilla(f, marcadas.Contains(f));
                casilla.Tag = f;
                _panelFormas.Children.Add(casilla);
            }
            try
            {
                _progresivasAplicadas = ServicioCorredor.ProgresivasAplicadas(doc, c.Id, _cbLineaBase.SelectedItem as string);
            }
            catch (ErrorUsuario)
            {
                _progresivasAplicadas = new List<double>();
            }
        }
        if (_panelFormas.Children.Count == 0) _panelFormas.Children.Add(Ui.Texto("(el corredor no tiene formas)", atenuado: true));

        string anterior = _cbProgresivaMat.SelectedItem as string ?? "";
        _cbProgresivaMat.Items.Clear();
        _progresivasGrupoMat = new List<double>();
        try
        {
            _progresivasGrupoMat = ServicioMateriales.ProgresivasGrupo(doc, GrupoMuestreo(doc));
        }
        catch (ErrorUsuario)
        {
            // Sin grupo de líneas de muestreo todavía: se usan las progresivas del corredor.
        }
        foreach (double p in _progresivasGrupoMat) _cbProgresivaMat.Items.Add(FormatoProgresiva.Formatear(p));
        Ui.Seleccionar(_cbProgresivaMat, anterior);
        if (_cbProgresivaMat.SelectedIndex < 0 && _cbProgresivaMat.Items.Count > 0) _cbProgresivaMat.SelectedIndex = 0;
    }

    /// <summary>Grupo de líneas de muestreo elegido en la pestaña Muestreo (con el nombre que está escrito ahora).</summary>
    private ObjectId GrupoMuestreo(Document doc)
    {
        string escrito = (_cbGrupo.Text ?? "").Trim();
        if (escrito.Length > 0) Cfg.Muestreo.NombreGrupo = escrito;
        return BuscarGrupoElegido(doc);
    }

    private double ProgresivaElegidaMat()
    {
        if (!string.IsNullOrWhiteSpace(_txProgresivaMat.Text))
        {
            if (FormatoProgresiva.TryParse(_txProgresivaMat.Text, out double p)) return p;
            throw new ErrorUsuario("La progresiva escrita no es válida (ej. 1+250.00).");
        }
        int i = _cbProgresivaMat.SelectedIndex;
        if (i >= 0 && i < _progresivasGrupoMat.Count) return _progresivasGrupoMat[i];
        if (_seccionMat != null) return _seccionMat.Progresiva;
        if (_progresivasAplicadas.Count > 0) return _progresivasAplicadas[0];
        throw new ErrorUsuario("Elige o escribe una progresiva.");
    }

    /// <summary>◀ ▶: la progresiva anterior o siguiente del grupo de muestreo (o del corredor si no hay grupo).</summary>
    private void MoverProgresiva(int sentido)
    {
        List<double> lista = _progresivasGrupoMat.Count > 0 ? _progresivasGrupoMat : _progresivasAplicadas;
        if (lista.Count == 0) throw new ErrorUsuario("No hay progresivas: elige el corredor (y el grupo de muestreo en la pestaña Muestreo).");
        double actual = _seccionMat?.Progresiva ?? ProgresivaElegidaMat();
        double? destino = sentido > 0
            ? lista.Where(p => p > actual + 1e-3).Select(p => (double?)p).FirstOrDefault()
            : lista.Where(p => p < actual - 1e-3).Select(p => (double?)p).LastOrDefault();
        if (!destino.HasValue) throw new ErrorUsuario(sentido > 0 ? "Es la última progresiva." : "Es la primera progresiva.");

        int i = _progresivasGrupoMat.IndexOf(destino.Value);
        if (i >= 0)
        {
            _txProgresivaMat.Text = "";
            _cbProgresivaMat.SelectedIndex = i;
        }
        else
        {
            _txProgresivaMat.Text = FormatoProgresiva.Formatear(destino.Value);
        }
        LeerSeccionMat();
    }

    private void LeerSeccionMat()
    {
        LeerMateriales();
        ElementoDibujo c = CorredorElegido ?? throw new ErrorUsuario("Elige un corredor.");
        double p = ProgresivaElegidaMat();
        string? lineaBase = Cfg.Materiales.LineaBase;
        Ejecutor.Ejecutar("Leer sección del corredor", doc => MostrarSeccion(ServicioCorredor.LeerSeccion(doc, c.Id, lineaBase, p)));
    }

    private void MostrarSeccion(LecturaSeccion lectura)
    {
        ConfigMateriales m = Cfg.Materiales;
        SeccionCorredor s = lectura.Seccion;
        _seccionMat = s;
        var rasante = _listaCodigos.TieneFilas ? _listaCodigos.Marcados(SuperficieMateriales.Rasante) : m.CodigosRasante;
        var subRasante = _listaCodigos.TieneFilas ? _listaCodigos.Marcados(SuperficieMateriales.SubRasante) : m.CodigosSubRasante;
        _listaCodigos.Cargar(s.CodigosVinculo.Concat(rasante).Concat(subRasante), rasante, subRasante);
        _visor.Mostrar(s);

        string texto = $"Progresiva {FormatoProgresiva.Formatear(s.Progresiva)} (región {lectura.Region}): " +
                       $"{s.Vinculos.Count} vínculos, {s.Formas.Count} formas.";
        if (Math.Abs(lectura.ProgresivaReal - lectura.ProgresivaPedida) > 1e-3)
            texto += $" Pediste {FormatoProgresiva.Formatear(lectura.ProgresivaPedida)}: se muestra el ensamblaje aplicado más cercano.";
        _lbSeccion.Text = texto;
        ActualizarVistaPrevia();
        Mensaje($"Sección {FormatoProgresiva.Formatear(s.Progresiva)} leída.");
    }

    /// <summary>Recalcula la vista previa de las dos superficies y los huecos de esta sección (al marcar cualquier código).</summary>
    private void ActualizarVistaPrevia()
    {
        if (_seccionMat == null) return;
        OpcionesEnvolvente op = Cfg.Materiales.Opciones();
        var rasante = _listaCodigos.Marcados(SuperficieMateriales.Rasante);
        var subRasante = _listaCodigos.Marcados(SuperficieMateriales.SubRasante);
        ResultadoValidacion? vr = rasante.Count > 0
            ? ValidadorSuperficie.Validar(_seccionMat, rasante, VoladizoDe(_cbVoladizoRasante), SuperficieMateriales.Rasante, op) : null;
        ResultadoValidacion? vs = subRasante.Count > 0
            ? ValidadorSuperficie.Validar(_seccionMat, subRasante, VoladizoDe(_cbVoladizoSubRasante), SuperficieMateriales.SubRasante, op) : null;

        var huecos = (vs?.Huecos ?? (IReadOnlyList<Hueco>)Array.Empty<Hueco>())
            .Concat(vr?.Huecos ?? (IReadOnlyList<Hueco>)Array.Empty<Hueco>())
            .Select(h => (h.Desde, h.Hasta));
        _visor.VistaPrevia(vr?.Resultante, vs?.Resultante, huecos);
        _lbPrevia.Text = $"En esta sección: Rasante {EstadoTexto(vr)} · SubRasante {EstadoTexto(vs)}";

        _sugeridosRasante.Clear();
        _sugeridosSubRasante.Clear();
        if (vr != null) _sugeridosRasante.UnionWith(vr.Sugerencias);
        if (vs != null) _sugeridosSubRasante.UnionWith(vs.Sugerencias);
    }

    private static string EstadoTexto(ResultadoValidacion? v) =>
        v == null ? "sin códigos" :
        !v.Continua ? $"✖ {v.Huecos.Count} hueco(s)" :
        v.Desvios.Count > 0 ? $"✔ continua, {v.Desvios.Count} desvío(s)" : "✔ continua";

    private void DetectarCodigos()
    {
        SeccionCorredor s = _seccionMat ?? throw new ErrorUsuario("Lee primero una sección del corredor.");
        OpcionesEnvolvente op = Cfg.Materiales.Opciones();
        ResultadoDeteccion arriba = DetectorEnvolvente.Detectar(s, superior: true, op);
        ResultadoDeteccion abajo = DetectorEnvolvente.Detectar(s, superior: false, op);
        _listaCodigos.Marcar(SuperficieMateriales.Rasante, arriba.NombresCodigos, soloEstos: true);
        _listaCodigos.Marcar(SuperficieMateriales.SubRasante, abajo.NombresCodigos, soloEstos: true);

        var inv = CultureInfo.InvariantCulture;
        string Lista(ResultadoDeteccion r) => r.Codigos.Count == 0 ? "(ninguno)" :
            string.Join(", ", r.Codigos.Select(c => $"{c.Codigo} ({c.LongitudEnvolvente.ToString("0.0", inv)} m)"));
        var avisos = arriba.Avisos.Select(a => "Rasante: " + a).Concat(abajo.Avisos.Select(a => "SubRasante: " + a)).ToList();
        _lbDeteccion.Text = $"Rasante: {Lista(arriba)}\nSubRasante: {Lista(abajo)}" + (avisos.Count > 0 ? "\n⚠ " + string.Join("\n⚠ ", avisos) : "");
        LeerMateriales();
        Estado.Guardar();
        Mensaje("Códigos detectados en esta sección. Valida todo el corredor antes de crear las superficies.");
    }

    private void ValidarSeccionActual()
    {
        SeccionCorredor s = _seccionMat ?? throw new ErrorUsuario("Lee primero una sección del corredor.");
        LeerMateriales();
        ConfigMateriales m = Cfg.Materiales;
        var lineas = new List<string>();
        var r = ValidarSeccion(s, m, lineas);
        _txValidacion.Text = string.Join(Environment.NewLine, lineas);
        _lbEstadoValidacion.Text = r.Sub == null ? "Marca los códigos de la SubRasante." :
            r.Sub.Continua ? "✔ SubRasante continua en esta sección." : $"✖ La SubRasante tiene {r.Sub.Huecos.Count} hueco(s) en esta sección.";
    }

    /// <summary>Valida una sección con los códigos marcados; agrega las líneas del informe y acumula las sugerencias.</summary>
    private (ResultadoValidacion? Sub, ResultadoValidacion? Ras) ValidarSeccion(SeccionCorredor s, ConfigMateriales m, List<string> lineas, string motivo = "")
    {
        OpcionesEnvolvente op = m.Opciones();
        ResultadoValidacion? sub = m.CodigosSubRasante.Count > 0
            ? ValidadorSuperficie.Validar(s, m.CodigosSubRasante, m.VoladizoSubRasante, SuperficieMateriales.SubRasante, op) : null;
        ResultadoValidacion? ras = m.CrearRasante && m.CodigosRasante.Count > 0
            ? ValidadorSuperficie.Validar(s, m.CodigosRasante, m.VoladizoRasante, SuperficieMateriales.Rasante, op) : null;
        string sufijo = motivo.Length > 0 ? $"   [{motivo}]" : "";
        if (sub != null)
        {
            lineas.Add(sub.Resumen() + sufijo);
            _sugeridosSubRasante.UnionWith(sub.Sugerencias);
        }
        else
        {
            lineas.Add(FormatoProgresiva.Formatear(s.Progresiva) + "  SubRasante: sin códigos marcados" + sufijo);
        }
        if (ras != null)
        {
            lineas.Add(ras.Resumen() + sufijo);
            _sugeridosRasante.UnionWith(ras.Sugerencias);
        }
        return (sub, ras);
    }

    /// <summary>
    /// Revisa inicio, medio y fin de cada región, una progresiva en corte y otra en relleno (si hay terreno elegido)
    /// y, si se pide, todas las del grupo de muestreo.
    /// </summary>
    private void ValidarCorredor()
    {
        LeerMateriales();
        Estado.Guardar();
        ElementoDibujo c = CorredorElegido ?? throw new ErrorUsuario("Elige un corredor.");
        ConfigMateriales m = Cfg.Materiales;
        if (m.CodigosSubRasante.Count == 0) throw new ErrorUsuario("Marca los códigos de la SubRasante (o usa «Detectar automáticamente»).");
        ObjectId terrenoId = (_cbTerreno.SelectedItem as ElementoDibujo)?.Id ?? ObjectId.Null;
        bool incluirGrupo = _chkValidarGrupo.IsChecked == true;
        List<double> grupo = _progresivasGrupoMat;

        Ejecutor.Ejecutar("Validar el corredor", doc =>
        {
            List<ProgresivaRevision> plan = PlanCorredor(doc, c.Id, m.LineaBase, terrenoId, grupo, incluirGrupo);
            _sugeridosRasante.Clear();
            _sugeridosSubRasante.Clear();
            var lineas = new List<string>();
            int conHuecos = 0, revisadas = 0;
            using (var progreso = new Progreso("Validando las superficies del corredor…", plan.Count))
            {
                foreach (ProgresivaRevision p in plan)
                {
                    progreso.Avanzar();
                    SeccionCorredor s = ServicioCorredor.LeerSeccion(doc, c.Id, m.LineaBase, p.Progresiva).Seccion;
                    var r = ValidarSeccion(s, m, lineas, p.Motivo);
                    revisadas++;
                    if (r.Sub != null && !r.Sub.Continua) conHuecos++;
                }
            }
            if (terrenoId.IsNull) lineas.Insert(0, "Elige el terreno natural (paso 5) para revisar también una progresiva en corte y otra en relleno.");
            _txValidacion.Text = string.Join(Environment.NewLine, lineas);
            _lbEstadoValidacion.Text = conHuecos == 0
                ? $"✔ SubRasante continua en las {revisadas} progresivas revisadas."
                : $"✖ La SubRasante tiene huecos en {conHuecos} de {revisadas} progresivas. Revisa el informe o usa «Aplicar sugerencias».";
            Mensaje(_lbEstadoValidacion.Text);
        });
    }

    private static List<ProgresivaRevision> PlanCorredor(Document doc, ObjectId corredorId, string? lineaBase, ObjectId terrenoId,
        List<double> grupo, bool incluirGrupo)
    {
        List<RegionCorredor> regiones = ServicioCorredor.Regiones(doc, corredorId, lineaBase);
        var diferencias = ServicioCorredor.DiferenciasEnEje(doc, corredorId, lineaBase, terrenoId);
        return PlanValidacion.Elegir(regiones.Select(r => (r.Inicio, r.Fin)), diferencias, grupo, incluirGrupo);
    }

    private void AplicarSugerencias()
    {
        if (_sugeridosRasante.Count == 0 && _sugeridosSubRasante.Count == 0)
            throw new ErrorUsuario("No hay sugerencias: valida primero (esta sección o todo el corredor).");
        if (_sugeridosSubRasante.Count > 0) _listaCodigos.Marcar(SuperficieMateriales.SubRasante, _sugeridosSubRasante, soloEstos: false);
        if (_sugeridosRasante.Count > 0) _listaCodigos.Marcar(SuperficieMateriales.Rasante, _sugeridosRasante, soloEstos: false);
        string texto = $"Agregados — SubRasante: {Texto(_sugeridosSubRasante)} · Rasante: {Texto(_sugeridosRasante)}. Vuelve a validar.";
        LeerMateriales();
        Estado.Guardar();
        _lbEstadoValidacion.Text = texto;
        Mensaje(texto);

        static string Texto(IEnumerable<string> c) => c.Any() ? string.Join(", ", c) : "nada";
    }

    private void IrAProgresivaDelInforme()
    {
        int linea = _txValidacion.GetLineIndexFromCharacterIndex(_txValidacion.CaretIndex);
        for (int i = linea; i >= 0; i--)
        {
            string texto = _txValidacion.GetLineText(i) ?? "";
            if (texto.Length == 0 || char.IsWhiteSpace(texto[0])) continue;
            string primera = texto.Split(' ', 2)[0];
            if (!FormatoProgresiva.TryParse(primera, out double p)) continue;
            _txProgresivaMat.Text = FormatoProgresiva.Formatear(p);
            LeerSeccionMat();
            return;
        }
    }

    private void CrearSuperficiesCorredor()
    {
        LeerMateriales();
        Estado.Guardar();
        ElementoDibujo c = CorredorElegido ?? throw new ErrorUsuario("Elige un corredor.");
        ObjectId estiloId = (_cbEstiloSuperficie.SelectedItem as ElementoDibujo)?.Id ?? ObjectId.Null;
        ConfigMateriales m = Cfg.Materiales;
        Ejecutor.Ejecutar("Crear superficies del corredor", doc =>
        {
            List<string> avisos = ServicioCorredor.CrearSuperficies(doc, c.Id, m, estiloId);
            RefrescarMateriales(doc);
            string nombres = m.CrearRasante ? $"{m.NombreRasante} y {m.NombreSubRasante}" : m.NombreSubRasante;
            Mensaje($"Superficies {nombres} listas en {c.Nombre}; corredor reconstruido.");
            if (avisos.Count > 0) Dialogos.Aviso(string.Join("\n", avisos));
        });
    }

    private void CalcularMateriales()
    {
        LeerMateriales();
        Estado.Guardar();
        ConfigMateriales m = Cfg.Materiales;
        ElementoDibujo terreno = _cbTerreno.SelectedItem as ElementoDibujo ?? throw new ErrorUsuario("Elige el terreno natural.");
        ElementoDibujo? corredor = CorredorElegido;
        ElementoDibujo? comparacion = _cbComparacion.SelectedItem as ElementoDibujo;
        ObjectId estiloSuperficieId = (_cbEstiloSuperficie.SelectedItem as ElementoDibujo)?.Id ?? ObjectId.Null;
        bool calcularIgual = _chkCalcularIgual.IsChecked == true;
        bool incluirGrupo = _chkValidarGrupo.IsChecked == true;

        Ejecutor.Ejecutar("Calcular materiales", doc =>
        {
            ObjectId grupoId = GrupoMuestreo(doc);
            List<double> grupo = ServicioMateriales.ProgresivasGrupo(doc, grupoId);
            _progresivasGrupoMat = grupo;

            var avisos = new List<string>();
            ObjectId comparacionId;
            string comparacionNombre;
            if (comparacion != null && !comparacion.Id.IsNull && !comparacion.Id.IsErased)
            {
                comparacionId = comparacion.Id;
                comparacionNombre = comparacion.Nombre;
            }
            else
            {
                if (corredor == null) throw new ErrorUsuario("Elige el corredor (paso 1) o la superficie de comparación.");
                SuperficieCorredor? sup = ServicioCorredor.LeerSuperficie(doc, corredor.Id, m.NombreSubRasante);
                if (sup == null && m.CodigosSubRasante.Count > 0)
                {
                    // Si el usuario borró la superficie en Civil 3D o aún no la creó pero ya tiene códigos marcados, se recrea automáticamente
                    avisos.AddRange(ServicioCorredor.CrearSuperficies(doc, corredor.Id, m, estiloSuperficieId));
                    sup = ServicioCorredor.LeerSuperficie(doc, corredor.Id, m.NombreSubRasante);
                }
                if (sup == null)
                    throw new ErrorUsuario($"El corredor {corredor.Nombre} no tiene la superficie {m.NombreSubRasante}. Créala en el paso 4.");

                comparacionId = sup.SurfaceId;
                comparacionNombre = sup.Nombre;
                ComprobarContinuidad(doc, corredor, sup, m, grupo, incluirGrupo, calcularIgual, avisos);
            }

            var nombres = new Dictionary<ObjectId, string> { [terreno.Id] = terreno.Nombre, [comparacionId] = comparacionNombre };
            var ids = new List<ObjectId> { terreno.Id, comparacionId };
            bool estructuras = m.CodigosForma.Count > 0 && corredor != null;
            if (estructuras)
            {
                ids.Add(corredor!.Id);
                nombres[corredor.Id] = corredor.Nombre;
            }

            Dictionary<ObjectId, ObjectId> fuentes = ServicioMateriales.AsegurarMuestreo(doc, grupoId, ids, nombres);
            Guid lista = ServicioMateriales.CrearLista(doc, grupoId, fuentes[terreno.Id], fuentes[comparacionId],
                estructuras ? fuentes[corredor!.Id] : ObjectId.Null, m, avisos);
            List<AreasSeccion> areas = ServicioMateriales.Areas(doc, grupoId, fuentes[terreno.Id], fuentes[comparacionId], avisos);
            if (areas.Count == 0) throw new ErrorUsuario("No se pudo leer ninguna sección de las dos superficies en el grupo de muestreo.");
            _filasVolumen = CalculadorVolumenes.AreasMedias(areas, m.FactorEsponjamiento, m.FactorCompactacion);
            int tablas = m.TablaEnVistas ? ServicioMateriales.TablasEnVistas(doc, grupoId, lista, avisos) : 0;

            RefrescarMateriales(doc);
            MostrarVolumenes(avisos);
            Mensaje($"Lista \"{m.NombreLista}\" creada; {_filasVolumen.Count} secciones calculadas" + (tablas > 0 ? $", {tablas} tablas en las vistas." : "."));
            if (avisos.Count > 0) Registro.Aviso("Materiales: " + string.Join(" | ", avisos));
        });
    }

    /// <summary>
    /// Antes de calcular: la SubRasante del corredor (con sus códigos y voladizo reales) no debe tener huecos en las
    /// progresivas de revisión. Si los tiene, no se calcula salvo que se marque «Calcular igual».
    /// </summary>
    private static void ComprobarContinuidad(Document doc, ElementoDibujo corredor, SuperficieCorredor sup, ConfigMateriales m,
        List<double> grupo, bool incluirGrupo, bool calcularIgual, List<string> avisos)
    {
        if (sup.Codigos.Count == 0) throw new ErrorUsuario($"La superficie {sup.Nombre} no tiene códigos de vínculo.");
        ObjectId terrenoId = ObjectId.Null;
        List<ProgresivaRevision> plan = PlanCorredor(doc, corredor.Id, m.LineaBase, terrenoId, grupo, incluirGrupo);
        var problemas = new List<string>();
        foreach (ProgresivaRevision p in plan)
        {
            SeccionCorredor s = ServicioCorredor.LeerSeccion(doc, corredor.Id, m.LineaBase, p.Progresiva).Seccion;
            ResultadoValidacion v = ValidadorSuperficie.Validar(s, sup.Codigos, sup.Voladizo, SuperficieMateriales.SubRasante, m.Opciones());
            if (!v.Continua) problemas.Add(v.Resumen());
        }
        if (problemas.Count == 0) return;
        string detalle = string.Join("\n", problemas.Take(6)) + (problemas.Count > 6 ? $"\n… y {problemas.Count - 6} más." : "");
        if (!calcularIgual)
            throw new ErrorUsuario($"La SubRasante {sup.Nombre} tiene huecos en {problemas.Count} de {plan.Count} progresivas revisadas; " +
                                   "los volúmenes saldrían mal.\n\n" + detalle +
                                   "\n\nCorrige los códigos (pasos 2 a 4) o marca «Calcular igual».");
        avisos.Add($"Se calculó con huecos en la SubRasante ({problemas.Count} progresivas).");
    }

    private void MostrarVolumenes(List<string> avisos)
    {
        var inv = CultureInfo.InvariantCulture;
        string N(double v) => v.ToString("0.00", inv);
        _dgVolumenes.ItemsSource = _filasVolumen
            .Select(f => new FilaTablaVolumen(f.ProgresivaTexto, N(f.AreaCorte), N(f.AreaRelleno), N(f.VolumenCorte), N(f.VolumenRelleno),
                N(f.AcumuladoCorte), N(f.AcumuladoRelleno), N(f.Masa)))
            .ToList();
        FilaVolumen? ultima = _filasVolumen.LastOrDefault();
        var sb = new StringBuilder();
        if (ultima != null)
        {
            sb.Append($"{_filasVolumen.Count} secciones. Total corte {N(_filasVolumen.Sum(f => f.VolumenCorte))} m³, " +
                      $"relleno {N(_filasVolumen.Sum(f => f.VolumenRelleno))} m³; masa final {N(ultima.Masa)} m³ " +
                      $"(acumulados con esponjamiento {N(Cfg.Materiales.FactorEsponjamiento)} y compactación {N(Cfg.Materiales.FactorCompactacion)}).");
        }
        foreach (string a in avisos) sb.Append("\n⚠ ").Append(a);
        _lbVolumenes.Text = sb.ToString();
    }

    private void ExportarCsv()
    {
        if (_filasVolumen.Count == 0) throw new ErrorUsuario("Calcula primero los materiales.");
        var dlg = new SaveFileDialog
        {
            Title = "Exportar volúmenes",
            Filter = "CSV para Excel (*.csv)|*.csv",
            FileName = DatosLamina.LimpiarNombre(Cfg.Materiales.NombreLista) + ".csv",
        };
        if (dlg.ShowDialog() != true) return;
        string csv = CalculadorVolumenes.Csv(_filasVolumen, CultureInfo.CurrentCulture, Cfg.Materiales.FactorEsponjamiento, Cfg.Materiales.FactorCompactacion);
        File.WriteAllText(dlg.FileName, csv, new UTF8Encoding(true)); // con BOM: Excel muestra bien las tildes
        Mensaje("Volúmenes exportados a " + dlg.FileName);
    }

    private void InsertarTablaVolumenes()
    {
        LeerTablaVolumen();
        Estado.Guardar();
        if (Cfg.Materiales.TablaVolumen.Tipo == TipoTablaVolumen.DinamicaNativa)
        {
            InsertarTablaDinamica();
            return;
        }

        // Tabla estándar (la de siempre): tabla de AutoCAD con los valores calculados.
        if (_filasVolumen.Count == 0) throw new ErrorUsuario("Calcula primero los materiales.");
        string titulo = "VOLÚMENES — " + Cfg.Materiales.NombreLista.ToUpperInvariant();
        List<FilaVolumen> filas = _filasVolumen;
        Ejecutor.Ejecutar("Insertar tabla de volúmenes", doc =>
        {
            double alto = 2.5;
            try
            {
                alto = ServicioEscala.Conversor(doc.Database, Cfg).PapelAModelo(2.5); // 2.5 mm en papel a la escala de las láminas
            }
            catch (ErrorUsuario)
            {
            }
            ServicioMateriales.InsertarTabla(doc, filas, alto, titulo);
            Mensaje("Tabla de volúmenes insertada en el modelo.");
        });
    }
}
