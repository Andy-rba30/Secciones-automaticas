using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using ArbaSecciones.Core.Acomodo;
using ArbaSecciones.Core.Configuracion;
using ArbaSecciones.Core.Escalas;
using ArbaSecciones.Plugin.Civil;
using ArbaSecciones.Plugin.Infraestructura;
using Autodesk.AutoCAD.ApplicationServices;
using Microsoft.Win32;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ArbaSecciones.Plugin.Interfaz;

internal sealed partial class PanelPrincipal
{
    private readonly TextBox _txPlantilla = Ui.Caja();
    private readonly ComboBox _cbPresentaciones = Ui.Combo();
    private readonly TextBox _txArchivo = new() { IsReadOnly = true, Margin = Ui.Margen };
    private readonly ComboBox _cbPresArchivo = Ui.Combo();
    private readonly ComboBox _cbBloque = Ui.Combo(editable: true);
    private readonly TextBlock _lbOrigenBloque = Ui.Texto("Bloques del dibujo actual.", atenuado: true);
    private string? _rutaBloque;
    private readonly TextBlock _lbArea = Ui.Texto("");
    private readonly CheckBox _chkAjustarHoja = Ui.Casilla("Al crear o importar la plantilla, ajustar la hoja al tamaño del membrete");
    private readonly TextBlock _lbHoja = Ui.Texto("", atenuado: true);
    private readonly ComboBox _cbUnidadPapel = Ui.Combo();
    private readonly ComboBox _cbUnidadModelo = Ui.Combo();
    private readonly ComboBox _cbBloqueMembrete = Ui.Combo(editable: true);
    private readonly TextBox _txFranja = Ui.Caja();
    private readonly ComboBox _cbPosFranja = Ui.Combo();
    private readonly ObservableCollection<MapeoAtributo> _atributos = new();
    private readonly DataGrid _dgAtributos = new()
    {
        AutoGenerateColumns = true,
        CanUserAddRows = true,
        CanUserDeleteRows = true,
        Height = 180,
        Margin = Ui.Margen,
        HeadersVisibility = DataGridHeadersVisibility.Column,
    };

    private const string UnidadAutomatica = "Automática (INSUNITS)";

    private UIElement CrearMembrete()
    {
        foreach (UnidadLongitud u in Enum.GetValues<UnidadLongitud>())
        {
            _cbUnidadPapel.Items.Add(u);
        }
        _cbUnidadModelo.Items.Add(UnidadAutomatica);
        foreach (UnidadLongitud u in Enum.GetValues<UnidadLongitud>()) _cbUnidadModelo.Items.Add(u);
        _cbPosFranja.Items.Add(PosicionFranja.Abajo);
        _cbPosFranja.Items.Add(PosicionFranja.Arriba);
        _dgAtributos.ItemsSource = _atributos;

        _cbUnidadPapel.SelectionChanged += (_, _) => ActualizarInfoArea();
        _cbUnidadModelo.SelectionChanged += (_, _) => ActualizarInfoArea();

        var plantilla = Ui.Formulario();
        Ui.Fila(plantilla, "Presentación plantilla", _txPlantilla);

        var existente = Ui.Formulario();
        Ui.Fila(existente, "Del dibujo", _cbPresentaciones);

        var archivo = Ui.Formulario();
        Ui.Fila(archivo, "Archivo", _txArchivo);
        Ui.Fila(archivo, "Presentación", _cbPresArchivo);

        var bloque = Ui.Formulario();
        Ui.Fila(bloque, "Bloque", _cbBloque);

        var unidades = Ui.Formulario();
        Ui.Fila(unidades, "Unidad del papel", _cbUnidadPapel);
        Ui.Fila(unidades, "Unidad del modelo", _cbUnidadModelo);

        var franja = Ui.Formulario();
        Ui.Fila(franja, "Alto (unidades de papel)", _txFranja);
        Ui.Fila(franja, "Posición", _cbPosFranja);

        var atributos = Ui.Formulario();
        Ui.Fila(atributos, "Bloque del membrete", _cbBloqueMembrete);

        return Ui.Pila(
            plantilla,
            Ui.Texto("Todas las láminas se copian de esta presentación. Elige una de estas tres formas de tenerla:", atenuado: true),
            Ui.Grupo("1. Usar una presentación del dibujo", Ui.Pila(existente, Ui.Boton("Usar como plantilla", UsarPresentacionExistente))),
            Ui.Grupo("2. Importar de un DWG/DWT", Ui.Pila(
                archivo,
                Ui.Linea(Ui.Boton("Elegir archivo…", ElegirArchivoPlantilla), Ui.Boton("Importar presentación", ImportarPlantilla)))),
            Ui.Grupo("3. Crear con un bloque con atributos (se inserta en 0,0,0)", Ui.Pila(
                bloque,
                _lbOrigenBloque,
                Ui.Linea(Ui.Boton("Bloque de un DWG…", ElegirBloqueDeArchivo), Ui.Boton("Crear plantilla", CrearPlantillaConBloque)))),
            Ui.Grupo("Hoja (papel de la presentación)", Ui.Pila(
                Ui.Texto("Lee el tamaño del membrete y pone la hoja de ese formato (A0, A1, A2, A3, A4…) con su orientación, " +
                         "a escala 1:1 y con los bordes del membrete sobre los de la hoja. Usa el trazador de la pestaña Exportar.", atenuado: true),
                _chkAjustarHoja,
                Ui.Boton("Ajustar hoja al membrete", AjustarHojaPlantilla),
                _lbHoja)),
            Ui.Grupo("Área útil", Ui.Pila(
                Ui.Texto("Dibuja una polilínea rectangular dentro del membrete y selecciónala.", atenuado: true),
                Ui.Boton("Seleccionar área útil en pantalla", SeleccionarAreaUtil, principal: true),
                _lbArea)),
            Ui.Grupo("Unidades", unidades),
            Ui.Grupo("Franja para el cuadro de movimiento de tierras (0 = sin franja)", franja),
            Ui.Grupo("Atributos del membrete", Ui.Pila(
                atributos,
                _dgAtributos,
                Ui.Texto("Marcadores: {codigo} {n} {total} {pi} {pf} {tramo} {kmi} {kmf} {km} {parte} {partes} {escala} {nombre}. " +
                         "Formato: {n:000} → 007.", atenuado: true))));
    }

    private void CargarMembrete()
    {
        ConfigMembrete m = Cfg.Membrete;
        _txPlantilla.Text = m.LayoutPlantilla;
        _txArchivo.Text = m.RutaPlantilla ?? "";
        _cbUnidadPapel.SelectedItem = m.UnidadPapel;
        _cbUnidadModelo.SelectedItem = Cfg.Escala.UnidadModelo.HasValue ? Cfg.Escala.UnidadModelo.Value : UnidadAutomatica;
        _cbBloqueMembrete.Text = m.NombreBloque ?? "";
        _txFranja.Text = Ui.Num(m.AltoFranjaCuadro);
        _cbPosFranja.SelectedItem = m.PosicionFranja;
        _chkAjustarHoja.IsChecked = m.AjustarHoja;
        _lbHoja.Text = string.IsNullOrWhiteSpace(m.Hoja) ? "Hoja sin ajustar." : "Hoja: " + m.Hoja + ".";
        _atributos.Clear();
        foreach (var a in m.Atributos) _atributos.Add(new MapeoAtributo { Etiqueta = a.Etiqueta, Plantilla = a.Plantilla });
        ActualizarInfoArea();
    }

    private void LeerMembrete()
    {
        ConfigMembrete m = Cfg.Membrete;
        string plantilla = (_txPlantilla.Text ?? "").Trim();
        if (plantilla.Length == 0) throw new ErrorUsuario("Escribe el nombre de la presentación plantilla.");
        m.LayoutPlantilla = DatosLamina.LimpiarNombre(plantilla);
        if (_cbUnidadPapel.SelectedItem is UnidadLongitud up) m.UnidadPapel = up;
        Cfg.Escala.UnidadModelo = _cbUnidadModelo.SelectedItem is UnidadLongitud um ? um : null;
        m.NombreBloque = string.IsNullOrWhiteSpace(_cbBloqueMembrete.Text) ? null : _cbBloqueMembrete.Text.Trim();
        m.AltoFranjaCuadro = Ui.Numero(_txFranja, "Alto de la franja");
        if (m.AltoFranjaCuadro < 0) throw new ErrorUsuario("El alto de la franja no puede ser negativo.");
        m.PosicionFranja = _cbPosFranja.SelectedItem is PosicionFranja pf ? pf : PosicionFranja.Abajo;
        m.AjustarHoja = _chkAjustarHoja.IsChecked == true;

        _dgAtributos.CommitEdit(DataGridEditingUnit.Row, true);
        m.Atributos = _atributos
            .Where(a => !string.IsNullOrWhiteSpace(a.Etiqueta))
            .Select(a => new MapeoAtributo { Etiqueta = a.Etiqueta.Trim(), Plantilla = a.Plantilla ?? "" })
            .ToList();
    }

    private void RefrescarMembrete(Document doc)
    {
        object? previo = _cbPresentaciones.SelectedItem;
        _cbPresentaciones.Items.Clear();
        foreach (string n in LectorDibujo.Presentaciones(doc.Database)) _cbPresentaciones.Items.Add(n);
        Ui.Seleccionar(_cbPresentaciones, previo as string ?? Cfg.Membrete.LayoutPlantilla);

        if (_rutaBloque == null)
        {
            string texto = _cbBloque.Text;
            _cbBloque.Items.Clear();
            foreach (string b in LectorDibujo.BloquesConAtributos(doc.Database)) _cbBloque.Items.Add(b);
            _cbBloque.Text = texto;
        }
        ActualizarInfoArea();
    }

    private void UsarPresentacionExistente()
    {
        if (_cbPresentaciones.SelectedItem is not string nombre) throw new ErrorUsuario("Elige una presentación del dibujo.");
        _txPlantilla.Text = nombre;
        LeerMembrete();
        Estado.Guardar();
        Mensaje($"Plantilla: {nombre}. Ahora selecciona el área útil.");
    }

    private void ElegirArchivoPlantilla()
    {
        var dlg = new OpenFileDialog { Title = "DWG o DWT con el membrete", Filter = "Dibujos (*.dwg;*.dwt)|*.dwg;*.dwt" };
        if (dlg.ShowDialog() != true) return;
        string ruta = dlg.FileName;
        _txArchivo.Text = ruta;
        Ejecutor.Ejecutar("Leer presentaciones del archivo", _ =>
        {
            _cbPresArchivo.Items.Clear();
            foreach (string n in ServicioMembrete.PresentacionesDeArchivo(ruta)) _cbPresArchivo.Items.Add(n);
            if (_cbPresArchivo.Items.Count > 0) _cbPresArchivo.SelectedIndex = 0;
            Mensaje($"{_cbPresArchivo.Items.Count} presentaciones en {Path.GetFileName(ruta)}.");
        });
    }

    private void ImportarPlantilla()
    {
        LeerMembrete();
        string ruta = _txArchivo.Text;
        if (string.IsNullOrWhiteSpace(ruta)) throw new ErrorUsuario("Elige primero el archivo DWG/DWT.");
        if (_cbPresArchivo.SelectedItem is not string origen) throw new ErrorUsuario("Elige la presentación del archivo.");
        string destino = Cfg.Membrete.LayoutPlantilla;
        Ejecutor.Ejecutar("Importar presentación plantilla", doc =>
        {
            ServicioMembrete.ImportarPresentacion(doc, ruta, origen, destino);
            Cfg.Membrete.RutaPlantilla = ruta;
            Estado.Guardar();
            string hoja = AjustarHojaTrasInsertar(doc, destino);
            RefrescarMembrete(doc);
            Mensaje($"Presentación \"{origen}\" importada como \"{destino}\".{hoja} Ahora selecciona el área útil.");
        });
    }

    private void ElegirBloqueDeArchivo()
    {
        var dlg = new OpenFileDialog { Title = "DWG con el bloque del membrete", Filter = "Dibujos (*.dwg)|*.dwg" };
        if (dlg.ShowDialog() != true) return;
        string ruta = dlg.FileName;
        Ejecutor.Ejecutar("Leer bloques del archivo", _ =>
        {
            var bloques = ServicioMembrete.BloquesDeArchivo(ruta);
            _rutaBloque = ruta;
            _cbBloque.Items.Clear();
            foreach (string b in bloques) _cbBloque.Items.Add(b);
            _cbBloque.Text = bloques.Count > 0 ? bloques[0] : Path.GetFileNameWithoutExtension(ruta);
            _lbOrigenBloque.Text = bloques.Count > 0
                ? $"Bloques de {Path.GetFileName(ruta)}."
                : $"{Path.GetFileName(ruta)} no tiene bloques con atributos: se insertará el archivo completo como bloque.";
        });
    }

    private void CrearPlantillaConBloque()
    {
        LeerMembrete();
        string bloque = (_cbBloque.Text ?? "").Trim();
        if (bloque.Length == 0) throw new ErrorUsuario("Elige o escribe el nombre del bloque.");
        string destino = Cfg.Membrete.LayoutPlantilla;
        string? ruta = _rutaBloque;
        Ejecutor.Ejecutar("Crear plantilla con bloque", doc =>
        {
            ServicioMembrete.CrearConBloque(doc, ruta, bloque, destino);
            Cfg.Membrete.NombreBloque = bloque;
            Cfg.Membrete.RutaPlantilla = ruta;
            _cbBloqueMembrete.Text = bloque;
            Estado.Guardar();
            _rutaBloque = null;
            _lbOrigenBloque.Text = "Bloques del dibujo actual.";
            string hoja = AjustarHojaTrasInsertar(doc, destino);
            RefrescarMembrete(doc);
            Mensaje($"Presentación \"{destino}\" creada con el bloque {bloque}.{hoja} Ahora dibuja y selecciona el área útil.");
        });
    }

    private void SeleccionarAreaUtil()
    {
        LeerMembrete();
        string plantilla = Cfg.Membrete.LayoutPlantilla;
        Ejecutor.Ejecutar("Seleccionar área útil", doc =>
        {
            AreaUtil area = ServicioMembrete.SeleccionarArea(doc, plantilla);
            ConfigMembrete m = Cfg.Membrete;
            m.AreaMinX = area.MinX;
            m.AreaMinY = area.MinY;
            m.AreaMaxX = area.MaxX;
            m.AreaMaxY = area.MaxY;

            // La unidad de trazado (mm o pulgadas) manda, salvo que el membrete esté dibujado en cm o m (escala de trazado propia).
            UnidadLongitud? papel = ServicioMembrete.UnidadPapel(doc.Database, plantilla);
            if (papel.HasValue && m.UnidadPapel is UnidadLongitud.Milimetros or UnidadLongitud.Pulgadas) m.UnidadPapel = papel.Value;

            var bloques = ServicioMembrete.BloquesEnPresentacion(doc.Database, plantilla);
            BloqueMembrete? elegido = bloques.FirstOrDefault(b => string.Equals(b.Nombre, m.NombreBloque, StringComparison.OrdinalIgnoreCase))
                                      ?? bloques.FirstOrDefault();
            _cbBloqueMembrete.Items.Clear();
            foreach (var b in bloques) _cbBloqueMembrete.Items.Add(b.Nombre);
            if (elegido != null)
            {
                m.NombreBloque = elegido.Nombre;
                m.Atributos = MezclarAtributos(m.Atributos, elegido.Etiquetas);
            }

            Estado.UltimaMedicion = null;
            Estado.Guardar();
            CargarMembrete();
            Mensaje(elegido != null
                ? $"Área útil guardada. Membrete: {elegido.Nombre} ({elegido.Etiquetas.Count} atributos)."
                : "Área útil guardada. No se encontró un bloque con atributos en la plantilla.");
        });
    }

    private void AjustarHojaPlantilla()
    {
        LeerMembrete();
        LeerExportar();
        string plantilla = Cfg.Membrete.LayoutPlantilla;
        Ejecutor.Ejecutar("Ajustar hoja al membrete", doc =>
        {
            var lm = Autodesk.AutoCAD.DatabaseServices.LayoutManager.Current;
            if (!lm.LayoutExists(plantilla))
                throw new ErrorUsuario($"No existe la presentación \"{plantilla}\". Impórtala o créala primero.");
            lm.CurrentLayout = plantilla;
            ResultadoHoja r = AplicarAjusteHoja(doc, plantilla);
            string avisos = r.Avisos.Count > 0 ? "\n\n" + string.Join("\n", r.Avisos) : "";
            if (r.Avisos.Count > 0) Dialogos.Info($"Hoja de \"{plantilla}\": {r.Descripcion}.{avisos}");
            Mensaje($"Hoja de \"{plantilla}\": {r.Descripcion} ({r.Papel})." +
                    (r.SeMovio ? " Se movió el contenido de la presentación para alinear el membrete con la hoja." : "") +
                    " Si ya creaste las láminas, vuelve a crearlas para que tomen la hoja nueva.");
        });
    }

    /// <summary>Después de crear o importar la plantilla: ajusta la hoja si está marcado. Devuelve el texto para el mensaje.</summary>
    private string AjustarHojaTrasInsertar(Document doc, string plantilla)
    {
        if (!Cfg.Membrete.AjustarHoja) return "";
        try
        {
            ResultadoHoja r = AplicarAjusteHoja(doc, plantilla);
            return $" Hoja: {r.Descripcion}." + (r.Avisos.Count > 0 ? " " + string.Join(" ", r.Avisos) : "");
        }
        catch (ErrorUsuario ex)
        {
            ArbaSecciones.Core.Comun.Registro.Aviso("Ajustar hoja: " + ex.Message);
            return " No se ajustó la hoja: " + ex.Message;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            ArbaSecciones.Core.Comun.Registro.Error("Ajustar hoja", ex);
            return " No se ajustó la hoja: " + ex.Message;
        }
    }

    /// <summary>Ajusta la hoja de la plantilla y guarda lo que cambia en la configuración (unidad, área útil movida, hoja).</summary>
    private ResultadoHoja AplicarAjusteHoja(Document doc, string plantilla)
    {
        ConfigMembrete m = Cfg.Membrete;
        ResultadoHoja r = ServicioHoja.AjustarAlMembrete(doc, plantilla, m.NombreBloque, m.UnidadPapel, Cfg.Exportacion.Dispositivo);
        m.UnidadPapel = r.Unidad;
        m.Hoja = r.Descripcion;
        if (r.SeMovio && m.AreaDefinida && string.Equals(plantilla, m.LayoutPlantilla, StringComparison.OrdinalIgnoreCase))
        {
            // El área útil se dibujó en la plantilla y se movió con ella.
            m.AreaMinX += r.DesplazamientoX;
            m.AreaMaxX += r.DesplazamientoX;
            m.AreaMinY += r.DesplazamientoY;
            m.AreaMaxY += r.DesplazamientoY;
        }
        Estado.Guardar();
        CargarMembrete();
        return r;
    }

    /// <summary>Deja una fila por atributo del bloque; conserva lo ya configurado y propone el resto por el nombre.</summary>
    private static List<MapeoAtributo> MezclarAtributos(List<MapeoAtributo> actuales, IReadOnlyList<string> etiquetas)
    {
        var porEtiqueta = actuales
            .Where(a => !string.IsNullOrWhiteSpace(a.Etiqueta))
            .GroupBy(a => a.Etiqueta, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().Plantilla, StringComparer.OrdinalIgnoreCase);

        return etiquetas
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(t => new MapeoAtributo { Etiqueta = t, Plantilla = porEtiqueta.TryGetValue(t, out string? p) ? p : Proponer(t) })
            .ToList();
    }

    private static string Proponer(string etiqueta)
    {
        string t = etiqueta.ToUpperInvariant();
        if (t.Contains("COD") || t.Contains("PLANO")) return "{codigo}";
        if (t.Contains("INI")) return "{pi}";
        if (t.Contains("FIN")) return "{pf}";
        if (t.Contains("TRAMO")) return "{tramo}";
        if (t.Contains("LAM") || t.Contains("HOJA")) return "{n}/{total}";
        if (t.Contains("ESC")) return "{escala}";
        return "";
    }

    /// <summary>Muestra el área útil en papel y su equivalente en el modelo con la escala elegida.</summary>
    private void ActualizarInfoArea()
    {
        ConfigMembrete m = Cfg.Membrete;
        if (!m.AreaDefinida)
        {
            _lbArea.Text = "Área útil sin definir.";
            ActualizarInfoEscala();
            return;
        }

        UnidadLongitud papel = _cbUnidadPapel.SelectedItem is UnidadLongitud up ? up : m.UnidadPapel;
        string texto = $"{Ui.Num(m.AnchoUtil, "0.##")} × {Ui.Num(m.AltoUtil, "0.##")} {papel.Abreviatura()} " +
                       $"(desde {Ui.Num(m.AreaMinX, "0.##")}, {Ui.Num(m.AreaMinY, "0.##")}).";
        ConversorEscala? c = ConversorActual();
        if (c != null)
        {
            texto += $"\nA {c.Escala}: {Ui.Num(c.PapelAModelo(m.AnchoUtil), "0.##")} × {Ui.Num(c.PapelAModelo(m.AltoUtil), "0.##")} " +
                     $"{c.UnidadModelo.Abreviatura()} en el modelo (factor {Ui.Num(c.FactorH, "0.######")}).";
        }
        _lbArea.Text = texto;
        ActualizarInfoEscala();
    }

    /// <summary>Conversor con la escala y las unidades que muestran los controles. Null si algún dato no es válido.</summary>
    private ConversorEscala? ConversorActual()
    {
        try
        {
            EscalaSecciones? escala = EscalaDeControles();
            if (escala == null) return null;
            UnidadLongitud papel = _cbUnidadPapel.SelectedItem is UnidadLongitud up ? up : Cfg.Membrete.UnidadPapel;
            UnidadLongitud modelo;
            if (_cbUnidadModelo.SelectedItem is UnidadLongitud um) modelo = um;
            else
            {
                Document? doc = AcApp.DocumentManager.MdiActiveDocument;
                modelo = doc != null ? UnidadesDibujo.DesdeInsunits(doc.Database.Insunits) ?? UnidadLongitud.Metros : UnidadLongitud.Metros;
            }
            return new ConversorEscala(escala, modelo, papel);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
