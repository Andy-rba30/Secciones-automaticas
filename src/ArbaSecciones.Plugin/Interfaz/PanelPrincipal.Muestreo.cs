using System.Windows;
using System.Windows.Controls;
using ArbaSecciones.Core.Comun;
using ArbaSecciones.Core.Progresivas;
using ArbaSecciones.Plugin.Civil;
using ArbaSecciones.Plugin.Infraestructura;
using Autodesk.AutoCAD.ApplicationServices;

namespace ArbaSecciones.Plugin.Interfaz;

internal sealed partial class PanelPrincipal
{
    private readonly ComboBox _cbAlineamiento = Ui.Combo();
    private readonly ComboBox _cbGrupo = Ui.Combo(editable: true);
    private readonly TextBox _txInicio = Ui.Caja();
    private readonly TextBox _txFin = Ui.Caja();
    private readonly TextBox _txIntervaloTangente = Ui.Caja();
    private readonly TextBox _txIntervaloCurva = Ui.Caja();
    private readonly ComboBox _cbModo = Ui.Combo();
    private readonly Dictionary<TipoProgresiva, CheckBox> _puntos = new();
    private readonly TextBox _txAdicionales = Ui.Caja(multilinea: true);
    private readonly TextBox _txAnchoIzquierdo = Ui.Caja();
    private readonly TextBox _txAnchoDerecho = Ui.Caja();
    private readonly TextBox _txTolerancia = Ui.Caja();
    private readonly StackPanel _panelFuentes = new();
    private readonly DataGrid _dgProgresivas = new()
    {
        AutoGenerateColumns = true,
        IsReadOnly = true,
        Height = 220,
        Margin = Ui.Margen,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        CanUserAddRows = false,
    };
    private readonly TextBlock _lbProgresivas = Ui.Texto("", atenuado: true);

    /// <summary>Fila de la vista previa de progresivas.</summary>
    public sealed record FilaProgresiva(string Progresiva, string Tipo, string Elemento);

    private UIElement CrearMuestreo()
    {
        _cbModo.Items.Add("Múltiplos redondos (0+010, 0+020…)");
        _cbModo.Items.Add("Desde el inicio de cada elemento");
        _cbAlineamiento.SelectionChanged += (_, _) => AlCambiarAlineamiento();

        var datos = Ui.Formulario();
        Ui.Fila(datos, "Alineamiento", _cbAlineamiento);
        Ui.Fila(datos, "Grupo de líneas", _cbGrupo);
        Ui.Fila(datos, "Progresiva inicial", _txInicio);
        Ui.Fila(datos, "Progresiva final", _txFin);
        Ui.Fila(datos, "Intervalo en tangentes", _txIntervaloTangente);
        Ui.Fila(datos, "Intervalo en curvas y espirales", _txIntervaloCurva);
        Ui.Fila(datos, "Progresivas", _cbModo);
        Ui.Fila(datos, "Ancho izquierdo", _txAnchoIzquierdo);
        Ui.Fila(datos, "Ancho derecho", _txAnchoDerecho);
        Ui.Fila(datos, "Tolerancia de duplicados", _txTolerancia);

        var puntos = new WrapPanel();
        foreach (var t in new[]
                 {
                     TipoProgresiva.Inicio, TipoProgresiva.Fin, TipoProgresiva.PC, TipoProgresiva.PT, TipoProgresiva.TS,
                     TipoProgresiva.SC, TipoProgresiva.CS, TipoProgresiva.ST, TipoProgresiva.PCC, TipoProgresiva.SS, TipoProgresiva.PI,
                 })
        {
            var c = Ui.Casilla(t.ToString());
            _puntos[t] = c;
            puntos.Children.Add(c);
        }

        return Ui.Pila(
            Ui.Linea(Ui.Boton("Actualizar listas", RefrescarListas)),
            datos,
            Ui.Texto("Vacías = todo el alineamiento. Acepta 1+250.00 o 1250.", atenuado: true),
            Ui.Grupo("Puntos notables", puntos),
            Ui.Grupo("Progresivas adicionales (separadas por ; o salto de línea)", _txAdicionales),
            Ui.Grupo("Fuentes de muestreo", Ui.Pila(
                Ui.Boton("Leer fuentes del grupo", LeerFuentes),
                Ui.Texto("Si el grupo no existe se crea vacío para poder listar sus fuentes.", atenuado: true),
                _panelFuentes)),
            Ui.Linea(
                Ui.Boton("Vista previa", VistaPreviaMuestreo),
                Ui.Boton("Crear líneas de muestreo", CrearLineasMuestreo, principal: true)),
            _lbProgresivas,
            _dgProgresivas);
    }

    private void CargarMuestreo()
    {
        OpcionesMuestreo m = Cfg.Muestreo;
        _txInicio.Text = m.ProgresivaInicial.HasValue ? FormatoProgresiva.Formatear(m.ProgresivaInicial.Value) : "";
        _txFin.Text = m.ProgresivaFinal.HasValue ? FormatoProgresiva.Formatear(m.ProgresivaFinal.Value) : "";
        _txIntervaloTangente.Text = Ui.Num(m.IntervaloTangente);
        _txIntervaloCurva.Text = Ui.Num(m.IntervaloCurva);
        _cbModo.SelectedIndex = m.Modo == ModoProgresivas.MultiplosRedondos ? 0 : 1;
        foreach (var (tipo, casilla) in _puntos) casilla.IsChecked = m.Incluye(tipo);
        _txAdicionales.Text = string.Join("; ", m.Adicionales.Select(a => FormatoProgresiva.Formatear(a)));
        _txAnchoIzquierdo.Text = Ui.Num(m.AnchoIzquierdo);
        _txAnchoDerecho.Text = Ui.Num(m.AnchoDerecho);
        _txTolerancia.Text = Ui.Num(m.Tolerancia, "0.####");
        _cbGrupo.Text = m.NombreGrupo;
    }

    private void LeerMuestreo()
    {
        OpcionesMuestreo m = Cfg.Muestreo;
        m.ProgresivaInicial = LeerProgresivaOpcional(_txInicio, "Progresiva inicial");
        m.ProgresivaFinal = LeerProgresivaOpcional(_txFin, "Progresiva final");
        m.IntervaloTangente = Ui.Numero(_txIntervaloTangente, "Intervalo en tangentes");
        m.IntervaloCurva = Ui.Numero(_txIntervaloCurva, "Intervalo en curvas");
        m.Modo = _cbModo.SelectedIndex == 1 ? ModoProgresivas.DesdeInicioElemento : ModoProgresivas.MultiplosRedondos;
        m.IncluirInicio = _puntos[TipoProgresiva.Inicio].IsChecked == true;
        m.IncluirFin = _puntos[TipoProgresiva.Fin].IsChecked == true;
        m.IncluirPC = _puntos[TipoProgresiva.PC].IsChecked == true;
        m.IncluirPT = _puntos[TipoProgresiva.PT].IsChecked == true;
        m.IncluirTS = _puntos[TipoProgresiva.TS].IsChecked == true;
        m.IncluirSC = _puntos[TipoProgresiva.SC].IsChecked == true;
        m.IncluirCS = _puntos[TipoProgresiva.CS].IsChecked == true;
        m.IncluirST = _puntos[TipoProgresiva.ST].IsChecked == true;
        m.IncluirPCC = _puntos[TipoProgresiva.PCC].IsChecked == true;
        m.IncluirSS = _puntos[TipoProgresiva.SS].IsChecked == true;
        m.IncluirPI = _puntos[TipoProgresiva.PI].IsChecked == true;

        m.Adicionales = FormatoProgresiva.LeerLista(_txAdicionales.Text, out var invalidos);
        if (invalidos.Count > 0) throw new ErrorUsuario("Progresivas adicionales no válidas: " + string.Join(", ", invalidos));

        m.AnchoIzquierdo = Ui.Numero(_txAnchoIzquierdo, "Ancho izquierdo");
        m.AnchoDerecho = Ui.Numero(_txAnchoDerecho, "Ancho derecho");
        m.Tolerancia = Ui.Numero(_txTolerancia, "Tolerancia");
        m.NombreGrupo = (_cbGrupo.Text ?? string.Empty).Trim();
        if (AlineamientoElegido != null) m.Alineamiento = AlineamientoElegido.Nombre;

        // Fuentes: solo si se listaron en esta sesión; si no, se mantiene lo guardado.
        if (_panelFuentes.Children.Count > 0)
        {
            m.Fuentes = _panelFuentes.Children.OfType<CheckBox>()
                .Where(c => c.IsChecked == true)
                .Select(c => c.Tag as string ?? string.Empty)
                .Where(n => n.Length > 0)
                .ToList();
        }

        var errores = m.Validar();
        if (errores.Count > 0) throw new ErrorUsuario(string.Join("\n", errores));
    }

    private static double? LeerProgresivaOpcional(TextBox caja, string campo)
    {
        if (string.IsNullOrWhiteSpace(caja.Text)) return null;
        if (FormatoProgresiva.TryParse(caja.Text, out double v)) return v;
        throw new ErrorUsuario($"\"{campo}\" no es una progresiva válida (ej. 1+250.00).");
    }

    private void RefrescarMuestreo(Document doc)
    {
        string? previo = AlineamientoElegido?.Nombre ?? Cfg.Muestreo.Alineamiento;
        _cbAlineamiento.Items.Clear();
        foreach (var a in LectorDibujo.Alineamientos(doc)) _cbAlineamiento.Items.Add(a);
        Ui.Seleccionar(_cbAlineamiento, previo);
        if (_cbAlineamiento.SelectedIndex < 0 && _cbAlineamiento.Items.Count > 0) _cbAlineamiento.SelectedIndex = 0;
    }

    private void AlCambiarAlineamiento()
    {
        _panelFuentes.Children.Clear();
        if (AlineamientoElegido == null) return;

        // Si el cambio ocurre dentro de una acción en curso (contexto de comando), se lee directo.
        if (Ejecutor.Ocupado)
        {
            Document? doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc != null) RellenarGrupos(doc);
            return;
        }
        Ejecutor.Ejecutar("Leer grupos", RellenarGrupos, silencioso: true);
    }

    private void RellenarGrupos(Document doc)
    {
        string texto = _cbGrupo.Text;
        _cbGrupo.Items.Clear();
        ElementoDibujo? alin = AlineamientoElegido;
        if (alin == null) return;
        foreach (var g in LectorDibujo.GruposMuestreo(doc, alin.Id)) _cbGrupo.Items.Add(g.Nombre);
        _cbGrupo.Text = string.IsNullOrWhiteSpace(texto) ? Cfg.Muestreo.NombreGrupo : texto;
    }

    private void LeerFuentes()
    {
        LeerMuestreo();
        ElementoDibujo alin = AlineamientoElegido ?? throw new ErrorUsuario("Elige un alineamiento.");
        string grupo = Cfg.Muestreo.NombreGrupo;
        if (grupo.Length == 0) throw new ErrorUsuario("Escribe el nombre del grupo.");

        Ejecutor.Ejecutar("Leer fuentes", doc =>
        {
            var fuentes = ServicioMuestreo.LeerFuentes(doc, alin.Id, grupo, crearSiFalta: true);
            _panelFuentes.Children.Clear();
            var marcadas = new HashSet<string>(Cfg.Muestreo.Fuentes, StringComparer.CurrentCultureIgnoreCase);
            foreach (var f in fuentes)
            {
                var c = Ui.Casilla($"{f.Nombre}  [{f.Tipo}]", f.Muestreada || marcadas.Contains(f.Nombre));
                c.Tag = f.Nombre;
                _panelFuentes.Children.Add(c);
            }
            if (fuentes.Count == 0) _panelFuentes.Children.Add(Ui.Texto("El dibujo no tiene superficies ni obras lineales.", atenuado: true));
            if (!_cbGrupo.Items.Contains(grupo)) _cbGrupo.Items.Add(grupo);
            Mensaje($"{fuentes.Count} fuentes en el grupo \"{grupo}\".");
        });
    }

    private void VistaPreviaMuestreo()
    {
        LeerMuestreo();
        Estado.Guardar();
        ElementoDibujo alin = AlineamientoElegido ?? throw new ErrorUsuario("Elige un alineamiento.");
        Ejecutor.Ejecutar("Vista previa de progresivas", doc =>
        {
            ResultadoProgresivas r = ServicioMuestreo.Calcular(doc, alin.Id, Cfg.Muestreo);
            MostrarProgresivas(r);
        });
    }

    private void CrearLineasMuestreo()
    {
        LeerMuestreo();
        Estado.Guardar();
        ElementoDibujo alin = AlineamientoElegido ?? throw new ErrorUsuario("Elige un alineamiento.");
        Ejecutor.Ejecutar("Crear líneas de muestreo", doc =>
        {
            ResultadoProgresivas r = ServicioMuestreo.Calcular(doc, alin.Id, Cfg.Muestreo);
            MostrarProgresivas(r);
            ResultadoMuestreo hecho = ServicioMuestreo.CrearLineas(doc, alin.Id, Cfg.Muestreo, r);
            Estado.UltimaMedicion = null;
            RellenarGrupos(doc);
            Mensaje($"Líneas de muestreo: {hecho.Creadas} creadas, {hecho.Omitidas} ya existían, {hecho.Borradas} reemplazadas.");
        });
    }

    private void MostrarProgresivas(ResultadoProgresivas r)
    {
        _dgProgresivas.ItemsSource = r.Progresivas
            .Select(p => new FilaProgresiva(FormatoProgresiva.Formatear(p.Progresiva), p.Tipo.ToString(), p.Tramo?.ToString() ?? ""))
            .ToList();
        string avisos = r.Avisos.Count > 0 ? "\n" + string.Join("\n", r.Avisos) : "";
        _lbProgresivas.Text = $"{r.Progresivas.Count} progresivas entre {FormatoProgresiva.Formatear(r.Inicio)} y {FormatoProgresiva.Formatear(r.Fin)}." + avisos;
    }

    /// <summary>Grupo elegido (lo usa el acomodo). Null si no existe en el dibujo.</summary>
    private Autodesk.AutoCAD.DatabaseServices.ObjectId BuscarGrupoElegido(Document doc)
    {
        ElementoDibujo alin = AlineamientoElegido ?? throw new ErrorUsuario("Elige el alineamiento en la pestaña Muestreo.");
        string nombre = Cfg.Muestreo.NombreGrupo;
        using var tr = doc.Database.TransactionManager.StartTransaction();
        var id = LectorDibujo.BuscarGrupo(tr, alin.Id, nombre);
        tr.Commit();
        if (id.IsNull) throw new ErrorUsuario($"El alineamiento {alin.Nombre} no tiene el grupo \"{nombre}\". Crea las líneas de muestreo primero.");
        return id;
    }
}
