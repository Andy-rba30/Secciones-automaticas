using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ArbaSecciones.Core.Acomodo;
using ArbaSecciones.Core.Configuracion;
using ArbaSecciones.Core.Escalas;
using ArbaSecciones.Plugin.Civil;
using ArbaSecciones.Plugin.Infraestructura;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ArbaSecciones.Plugin.Interfaz;

internal sealed partial class PanelPrincipal
{
    private const string SinCambiar = "(sin cambiar)";

    private readonly ComboBox _cbEscalaH = Ui.Combo(editable: true);
    private readonly CheckBox _chkVertical = Ui.Casilla("Escala vertical distinta (exageración)");
    private readonly ComboBox _cbEscalaV = Ui.Combo(editable: true);
    private readonly TextBlock _lbEscala = Ui.Texto("");
    private readonly ComboBox _cbEstilo = Ui.Combo();
    private readonly ComboBox _cbBandas = Ui.Combo();
    private readonly ComboBox _cbOrden = Ui.Combo();
    private readonly ComboBox _cbPaginacion = Ui.Combo();
    private readonly TextBox _txLongitudTramo = Ui.Caja();
    private readonly TextBox _txSeparacionH = Ui.Caja();
    private readonly TextBox _txSeparacionV = Ui.Caja();
    private readonly TextBox _txSeparacionLaminas = Ui.Caja();
    private readonly CheckBox _chkUniformar = Ui.Casilla("Uniformizar altura de secciones por lámina", valor: true);
    private readonly CheckBox _chkUniformarTramo = Ui.Casilla("Usar la misma altura en todo el tramo (no solo por lámina)");
    private readonly TextBox _txIntervaloRejilla = Ui.Caja();
    private readonly TextBlock _lbBase = Ui.Texto("", atenuado: true);
    private readonly TextBox _txResumen = Ui.Informe(170);
    private readonly ComboBox _cbLaminaPrevia = Ui.Combo();
    private readonly VistaPreviaLaminas _vistaPrevia = new();
    private ResultadoAcomodo? _ultimoResultado;
    private bool _cargando;

    private UIElement CrearAcomodo()
    {
        foreach (double d in EscalasNormalizadas.Denominadores)
        {
            _cbEscalaH.Items.Add(EscalasNormalizadas.Formatear(d));
            _cbEscalaV.Items.Add(EscalasNormalizadas.Formatear(d));
        }
        _cbOrden.Items.Add("Por columnas (de abajo hacia arriba, de izquierda a derecha)");
        _cbOrden.Items.Add("Por filas (de izquierda a derecha, de arriba hacia abajo)");
        _cbPaginacion.Items.Add("Por km (cada km empieza en lámina nueva)");
        _cbPaginacion.Items.Add("Continuo (llena cada lámina)");

        _cbEscalaH.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => AlCambiarEscala()));
        _cbEscalaV.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => AlCambiarEscala()));
        _chkVertical.Checked += (_, _) => AlCambiarEscala();
        _chkVertical.Unchecked += (_, _) => AlCambiarEscala();
        _cbOrden.SelectionChanged += (_, _) => AlCambiarEscala();
        _cbPaginacion.SelectionChanged += (_, _) => AlCambiarEscala();
        _chkUniformar.Checked += (_, _) => AlCambiarEscala();
        _chkUniformar.Unchecked += (_, _) => AlCambiarEscala();
        _chkUniformarTramo.Checked += (_, _) => AlCambiarEscala();
        _chkUniformarTramo.Unchecked += (_, _) => AlCambiarEscala();
        _cbLaminaPrevia.SelectionChanged += (_, _) => _vistaPrevia.Mostrar(_ultimoResultado, _cbLaminaPrevia.SelectedIndex);

        var escala = Ui.Formulario();
        Ui.Fila(escala, "Escala (horizontal)", _cbEscalaH);
        Ui.Fila(escala, "", _chkVertical);
        Ui.Fila(escala, "Escala vertical", _cbEscalaV);

        var estilos = Ui.Formulario();
        Ui.Fila(estilos, "Estilo de vista", _cbEstilo);
        Ui.Fila(estilos, "Conjunto de bandas", _cbBandas);

        var acomodo = Ui.Formulario();
        Ui.Fila(acomodo, "Orden", _cbOrden);
        Ui.Fila(acomodo, "Paginación", _cbPaginacion);
        Ui.Fila(acomodo, "Longitud de cada tramo", _txLongitudTramo);
        Ui.Fila(acomodo, "Separación horizontal (papel)", _txSeparacionH);
        Ui.Fila(acomodo, "Separación vertical (papel)", _txSeparacionV);
        Ui.Fila(acomodo, "Separación entre láminas (papel)", _txSeparacionLaminas);
        Ui.Fila(acomodo, "", _chkUniformar);
        Ui.Fila(acomodo, "", _chkUniformarTramo);
        Ui.Fila(acomodo, "Múltiplo de altura (modelo, 0 = sin redondeo)", _txIntervaloRejilla);

        return Ui.Pila(
            Ui.Grupo("Escala de las secciones", Ui.Pila(
                escala,
                _lbEscala,
                Ui.Boton("Proponer escala (cada km en una lámina)", ProponerEscala))),
            Ui.Grupo("Estilos (las vistas nuevas usan estos)", estilos),
            Ui.Grupo("Acomodo", acomodo),
            Ui.Grupo("Punto base en el modelo", Ui.Pila(
                _lbBase,
                Ui.Linea(Ui.Boton("Elegir punto base…", ElegirPuntoBase), Ui.Boton("Pedirlo al acomodar", OlvidarPuntoBase)))),
            Ui.Linea(
                Ui.Boton("Medir y simular", () => EjecutarAcomodo(mover: false)),
                Ui.Boton("Acomodar en el modelo", () => EjecutarAcomodo(mover: true), principal: true)),
            Ui.Texto("Las vistas que faltan se crean en una zona de espera sobre el punto base; las existentes se reacomodan sin duplicarse.", atenuado: true),
            _txResumen,
            Ui.Linea(Ui.Texto("Vista previa: "), _cbLaminaPrevia),
            _vistaPrevia);
    }

    private void CargarAcomodo()
    {
        _cargando = true;
        try
        {
            ConfigEscala e = Cfg.Escala;
            ConfigAcomodo a = Cfg.Acomodo;
            _cbEscalaH.Text = EscalasNormalizadas.Formatear(e.DenominadorH);
            _chkVertical.IsChecked = e.DenominadorV.HasValue && Math.Abs(e.DenominadorV.Value - e.DenominadorH) > 1e-9;
            _cbEscalaV.Text = EscalasNormalizadas.Formatear(e.DenominadorV ?? e.DenominadorH);
            Ui.Seleccionar(_cbEstilo, a.EstiloVista ?? SinCambiar);
            Ui.Seleccionar(_cbBandas, a.ConjuntoBandas ?? SinCambiar);
            _cbOrden.SelectedIndex = a.Orden == OrdenAcomodo.PorColumnas ? 0 : 1;
            _cbPaginacion.SelectedIndex = a.Paginacion == ModoPaginacion.PorKilometro ? 0 : 1;
            _txLongitudTramo.Text = Ui.Num(a.LongitudTramo);
            _txSeparacionH.Text = Ui.Num(a.SeparacionH);
            _txSeparacionV.Text = Ui.Num(a.SeparacionV);
            _txSeparacionLaminas.Text = Ui.Num(a.SeparacionLaminas);
            _chkUniformar.IsChecked = a.UniformarAlturasPorLamina;
            _chkUniformarTramo.IsChecked = a.UniformarTodoElTramo;
            _txIntervaloRejilla.Text = Ui.Num(a.IntervaloRejillaElevacion);
            _lbBase.Text = a.BaseX.HasValue && a.BaseY.HasValue
                ? $"({Ui.Num(a.BaseX.Value, "0.###")}, {Ui.Num(a.BaseY.Value, "0.###")})"
                : "Se pedirá en pantalla al acomodar.";
        }
        finally
        {
            _cargando = false;
        }
        ActualizarInfoEscala();
    }

    private void LeerAcomodo()
    {
        EscalaSecciones escala = EscalaDeControles() ?? throw new ErrorUsuario("Escala no válida. Escribe por ejemplo 1:200 o 200.");
        Cfg.Escala.DenominadorH = escala.DenominadorH;
        Cfg.Escala.DenominadorV = _chkVertical.IsChecked == true ? escala.DenominadorV : null;

        ConfigAcomodo a = Cfg.Acomodo;
        if (_cbEstilo.Items.Count > 1) a.EstiloVista = TextoElegido(_cbEstilo);
        if (_cbBandas.Items.Count > 1) a.ConjuntoBandas = TextoElegido(_cbBandas);
        a.Orden = _cbOrden.SelectedIndex == 1 ? OrdenAcomodo.PorFilas : OrdenAcomodo.PorColumnas;
        a.Paginacion = _cbPaginacion.SelectedIndex == 1 ? ModoPaginacion.Continuo : ModoPaginacion.PorKilometro;
        a.LongitudTramo = Ui.Numero(_txLongitudTramo, "Longitud de cada tramo");
        a.SeparacionH = Ui.Numero(_txSeparacionH, "Separación horizontal");
        a.SeparacionV = Ui.Numero(_txSeparacionV, "Separación vertical");
        a.SeparacionLaminas = Ui.Numero(_txSeparacionLaminas, "Separación entre láminas");
        a.UniformarAlturasPorLamina = _chkUniformar.IsChecked == true;
        a.UniformarTodoElTramo = _chkUniformarTramo.IsChecked == true;
        a.IntervaloRejillaElevacion = Ui.Numero(_txIntervaloRejilla, "Múltiplo de altura");
        if (a.IntervaloRejillaElevacion < 0) throw new ErrorUsuario("El múltiplo de altura no puede ser negativo.");
        if (a.LongitudTramo <= 0) throw new ErrorUsuario("La longitud de cada tramo debe ser mayor que cero.");
        if (a.SeparacionH < 0 || a.SeparacionV < 0 || a.SeparacionLaminas < 0) throw new ErrorUsuario("Las separaciones no pueden ser negativas.");
    }

    private static string? TextoElegido(ComboBox combo)
    {
        string? t = combo.SelectedItem as string;
        return string.IsNullOrWhiteSpace(t) || t == SinCambiar ? null : t;
    }

    private void RefrescarAcomodo(Document doc)
    {
        _cargando = true;
        try
        {
            _cbEstilo.Items.Clear();
            _cbEstilo.Items.Add(SinCambiar);
            foreach (var e in LectorDibujo.EstilosVista(doc)) _cbEstilo.Items.Add(e.Nombre);
            _cbBandas.Items.Clear();
            _cbBandas.Items.Add(SinCambiar);
            foreach (var e in LectorDibujo.ConjuntosBandas(doc)) _cbBandas.Items.Add(e.Nombre);
            Ui.Seleccionar(_cbEstilo, Cfg.Acomodo.EstiloVista ?? SinCambiar);
            Ui.Seleccionar(_cbBandas, Cfg.Acomodo.ConjuntoBandas ?? SinCambiar);
            if (_cbEstilo.SelectedIndex < 0) _cbEstilo.SelectedIndex = 0;
            if (_cbBandas.SelectedIndex < 0) _cbBandas.SelectedIndex = 0;
        }
        finally
        {
            _cargando = false;
        }

        if (Estado.UltimaMedicion != null)
        {
            bool invalida = Estado.UltimaMedicion.Documento != doc.Name ||
                            Estado.UltimaMedicion.GrupoId.IsNull ||
                            Estado.UltimaMedicion.GrupoId.IsErased;
            if (!invalida)
            {
                int actuales = ServicioMateriales.ProgresivasGrupo(doc, Estado.UltimaMedicion.GrupoId).Count;
                if (actuales != Estado.UltimaMedicion.Vistas.Count)
                    invalida = true;
            }
            if (invalida)
            {
                Estado.UltimaMedicion = null;
                MostrarResultado(null, estimado: false);
            }
        }
        ActualizarInfoArea();
    }

    /// <summary>Escala que muestran los controles. Null si el texto no es una escala válida.</summary>
    private EscalaSecciones? EscalaDeControles()
    {
        if (!EscalasNormalizadas.TryParse(_cbEscalaH.Text, out double h)) return null;
        double? v = null;
        if (_chkVertical.IsChecked == true)
        {
            if (!EscalasNormalizadas.TryParse(_cbEscalaV.Text, out double dv)) return null;
            v = dv;
        }
        return new EscalaSecciones(h, v);
    }

    private void AlCambiarEscala()
    {
        if (_cargando) return;
        _cbEscalaV.IsEnabled = _chkVertical.IsChecked == true;
        _chkUniformarTramo.IsEnabled = _chkUniformar.IsChecked == true;
        _txIntervaloRejilla.IsEnabled = _chkUniformar.IsChecked == true;
        ActualizarInfoArea();
        ResimularEstimado();
    }

    private void ActualizarInfoEscala()
    {
        _cbEscalaV.IsEnabled = _chkVertical.IsChecked == true;
        _chkUniformarTramo.IsEnabled = _chkUniformar.IsChecked == true;
        _txIntervaloRejilla.IsEnabled = _chkUniformar.IsChecked == true;
        ConversorEscala? c = ConversorActual();
        if (c == null)
        {
            _lbEscala.Text = "Escala no válida.";
            return;
        }
        string texto = $"Factor {Ui.Num(c.FactorH, "0.######")} {c.UnidadPapel.Abreviatura()}/{c.UnidadModelo.Abreviatura()} " +
                       $"(CustomScale del viewport). Escala de anotación: 1 {c.UnidadPapel.Abreviatura()} = {Ui.Num(c.UnidadesDibujoPorUnidadPapel, "0.######")} {c.UnidadModelo.Abreviatura()}.";
        if (c.Escala.TieneExageracion) texto += $"\nExageración vertical {Ui.Num(c.Escala.Exageracion, "0.##")}.";
        ConfigMembrete m = Cfg.Membrete;
        if (m.AreaDefinida)
            texto += $"\nÁrea útil en el modelo: {Ui.Num(c.PapelAModelo(m.AnchoUtil), "0.##")} × {Ui.Num(c.PapelAModelo(m.AltoUtil), "0.##")} {c.UnidadModelo.Abreviatura()}.";
        _lbEscala.Text = texto;
    }

    /// <summary>
    /// Con la última medición, recalcula el acomodo con la escala de los controles sin tocar el dibujo.
    /// Al acomodar se vuelve a fijar la escala de anotación y a medir, así que el resultado final es exacto.
    /// </summary>
    private void ResimularEstimado()
    {
        Medicion? medicion = Estado.UltimaMedicion;
        ConversorEscala? c = ConversorActual();
        if (medicion == null || c == null || !Cfg.Membrete.AreaDefinida) return;
        try
        {
            LeerAcomodo();
            ParametrosAcomodo p = Cfg.ParametrosAcomodo();
            if (p.Validar().Count > 0) return;
            ResultadoAcomodo r = Paginador.Paginar(medicion.Medidas, c, p);
            bool estimado = c.Escala != medicion.Conversor.Escala || c.UnidadModelo != medicion.Conversor.UnidadModelo || c.UnidadPapel != medicion.Conversor.UnidadPapel;
            MostrarResultado(r, estimado);
        }
        catch (ErrorUsuario)
        {
            // Mientras el usuario escribe puede haber valores incompletos: no se interrumpe.
        }
    }

    private void MostrarResultado(ResultadoAcomodo? r, bool estimado)
    {
        _ultimoResultado = r;
        _cbLaminaPrevia.Items.Clear();
        if (r == null)
        {
            _txResumen.Text = "";
            _vistaPrevia.Limpiar();
            return;
        }

        _txResumen.Text = (estimado ? "ESTIMADO con la escala nueva (las vistas se miden de nuevo al acomodar).\n" : "") + r.Resumen();
        foreach (Lamina l in r.Laminas)
        {
            string grupo = l.Grupo.HasValue ? $"km {l.Grupo}" + (l.PartesDelGrupo > 1 ? $" {l.Parte}/{l.PartesDelGrupo}" : "") : "continuo";
            _cbLaminaPrevia.Items.Add($"Lámina {l.Numero:00} · {grupo} · {l.Cantidad} secciones");
        }
        if (_cbLaminaPrevia.Items.Count > 0) _cbLaminaPrevia.SelectedIndex = 0;
        else _vistaPrevia.Limpiar();
    }

    private void EjecutarAcomodo(bool mover)
    {
        LeerFormulario();
        if (!Cfg.Membrete.AreaDefinida) throw new ErrorUsuario("Falta el área útil del membrete (pestaña Membrete).");
        var errores = Cfg.ParametrosAcomodo().Validar();
        if (errores.Count > 0) throw new ErrorUsuario(string.Join("\n", errores));

        Ejecutor.Ejecutar(mover ? "Acomodar vistas" : "Medir y simular", doc =>
        {
            ObjectId grupoId = BuscarGrupoElegido(doc);
            Point3d puntoBase = PuntoBase(doc);
            ConversorEscala c = ServicioEscala.Conversor(doc.Database, Cfg);
            double subir = c.PapelAModelo(Cfg.Membrete.AltoUtil) + c.PapelAModelo(Math.Max(Cfg.Acomodo.SeparacionLaminas, 10)) * 2;
            var zonaEspera = new Point3d(puntoBase.X, puntoBase.Y + subir, 0);

            Medicion medicion = ServicioVistas.PrepararYMedir(doc, Cfg, grupoId, zonaEspera);
            CargarAcomodo(); // el estilo puede haber cambiado (copia con exageración)

            // Con la uniformización activa fija los rangos de elevación, vuelve a medir y pagina con las alturas finales.
            (medicion, ResultadoAcomodo r) = ServicioVistas.PaginarUniformizado(doc, Cfg, medicion);
            Estado.UltimaMedicion = medicion;
            MostrarResultado(r, estimado: false);

            if (!r.Valido)
            {
                Dialogos.Aviso(r.Resumen());
                Mensaje("Hay secciones que no entran solas con esta escala. No se movió nada.");
                return;
            }
            if (!mover)
            {
                Mensaje($"Simulación a {r.Conversor.Escala}: {r.Laminas.Count} láminas, {r.TotalSecciones} secciones. Nada se movió.");
                return;
            }
            if (!Dialogos.Confirmar(Recortar(r.Resumen(), 30) + "\n¿Acomodar las vistas así?"))
                throw new OperacionCancelada();

            ServicioAcomodo.Aplicar(doc, r, medicion, puntoBase, Cfg.Acomodo.SeparacionLaminas);
            Mensaje($"Vistas acomodadas: {r.TotalSecciones} en {r.Laminas.Count} láminas a {r.Conversor.Escala}.");
        });
    }

    /// <summary>
    /// Re-sincroniza automáticamente las vistas y marcos de lámina en el Espacio Modelo antes de generar o regenerar
    /// las presentaciones si el usuario agregó o eliminó líneas de muestreo o cambió parámetros.
    /// </summary>
    private void SincronizarAcomodoParaPresentaciones(Document doc)
    {
        if (!Cfg.Membrete.AreaDefinida || Cfg.ParametrosAcomodo().Validar().Count > 0) return;

        ObjectId grupoId = ObjectId.Null;
        try
        {
            grupoId = BuscarGrupoElegido(doc);
        }
        catch (ErrorUsuario)
        {
            return;
        }
        if (grupoId.IsNull || grupoId.IsErased) return;

        Point3d? puntoBase = null;
        if (Cfg.Acomodo.BaseX.HasValue && Cfg.Acomodo.BaseY.HasValue)
        {
            puntoBase = new Point3d(Cfg.Acomodo.BaseX.Value, Cfg.Acomodo.BaseY.Value, 0);
        }
        else
        {
            List<MarcoLamina> marcosPrevios = ServicioAcomodo.LeerMarcos(doc.Database);
            if (marcosPrevios.Count > 0)
            {
                Extents3d ext0 = marcosPrevios[0].Extension;
                puntoBase = new Point3d(ext0.MinPoint.X, ext0.MinPoint.Y, 0);
                Cfg.Acomodo.BaseX = puntoBase.Value.X;
                Cfg.Acomodo.BaseY = puntoBase.Value.Y;
                Estado.Guardar();
            }
        }
        if (!puntoBase.HasValue) return;

        ConversorEscala c = ServicioEscala.Conversor(doc.Database, Cfg);
        double subir = c.PapelAModelo(Cfg.Membrete.AltoUtil) + c.PapelAModelo(Math.Max(Cfg.Acomodo.SeparacionLaminas, 10)) * 2;
        var zonaEspera = new Point3d(puntoBase.Value.X, puntoBase.Value.Y + subir, 0);

        Medicion medicion = ServicioVistas.PrepararYMedir(doc, Cfg, grupoId, zonaEspera);
        CargarAcomodo();

        (medicion, ResultadoAcomodo r) = ServicioVistas.PaginarUniformizado(doc, Cfg, medicion);
        Estado.UltimaMedicion = medicion;
        MostrarResultado(r, estimado: false);
        if (r.Valido)
        {
            ServicioAcomodo.Aplicar(doc, r, medicion, puntoBase.Value, Cfg.Acomodo.SeparacionLaminas);
        }
    }

    private Point3d PuntoBase(Document doc)
    {
        ConfigAcomodo a = Cfg.Acomodo;
        if (a.BaseX.HasValue && a.BaseY.HasValue) return new Point3d(a.BaseX.Value, a.BaseY.Value, 0);
        Point3d p = ServicioAcomodo.PedirPuntoBase(doc) ?? throw new OperacionCancelada("No se eligió el punto base.");
        a.BaseX = p.X;
        a.BaseY = p.Y;
        Estado.Guardar();
        CargarAcomodo();
        return p;
    }

    private void ElegirPuntoBase()
    {
        Ejecutor.Ejecutar("Elegir punto base", doc =>
        {
            Point3d p = ServicioAcomodo.PedirPuntoBase(doc) ?? throw new OperacionCancelada("No se eligió el punto base.");
            Cfg.Acomodo.BaseX = p.X;
            Cfg.Acomodo.BaseY = p.Y;
            Estado.Guardar();
            CargarAcomodo();
        });
    }

    private void OlvidarPuntoBase()
    {
        Cfg.Acomodo.BaseX = null;
        Cfg.Acomodo.BaseY = null;
        Estado.Guardar();
        CargarAcomodo();
    }

    private void ProponerEscala()
    {
        Medicion medicion = Estado.UltimaMedicion ?? throw new ErrorUsuario("Primero usa \"Medir y simular\" para conocer el tamaño real de las vistas.");
        Document? doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc == null || medicion.Documento != doc.Name) throw new ErrorUsuario("La última medición es de otro dibujo. Vuelve a medir.");
        LeerFormulario();
        ConversorEscala c = ConversorActual() ?? throw new ErrorUsuario("Escala no válida.");

        double? d = SugeridorEscala.MenorEscalaUnaLaminaPorTramo(medicion.Medidas, c, Cfg.ParametrosAcomodo());
        if (d == null)
        {
            Dialogos.Aviso("Ninguna escala de la lista logra que cada km entre en una sola lámina. Revisa el área útil o el estilo de las vistas.");
            return;
        }
        EscalaSecciones nueva = c.Escala.ConDenominadorH(d.Value);
        _cbEscalaH.Text = EscalasNormalizadas.Formatear(nueva.DenominadorH);
        if (_chkVertical.IsChecked == true) _cbEscalaV.Text = EscalasNormalizadas.Formatear(nueva.DenominadorV);
        Mensaje($"Escala propuesta: {nueva}. Revisa el resumen y usa \"Acomodar en el modelo\".");
    }

    private static string Recortar(string texto, int lineas)
    {
        var partes = texto.Split('\n');
        return partes.Length <= lineas ? texto : string.Join("\n", partes.Take(lineas)) + "\n  …";
    }
}
