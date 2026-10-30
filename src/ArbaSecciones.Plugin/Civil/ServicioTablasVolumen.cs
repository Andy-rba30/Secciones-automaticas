using System.Windows;
using ArbaSecciones.Core.Comun;
using ArbaSecciones.Core.Configuracion;
using ArbaSecciones.Core.Escalas;
using ArbaSecciones.Core.Tablas;
using ArbaSecciones.Plugin.Infraestructura;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.Settings;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDb = Autodesk.AutoCAD.DatabaseServices;
using C3D = Autodesk.Civil.DatabaseServices;

namespace ArbaSecciones.Plugin.Civil;

/// <summary>
/// Tablas dinámicas nativas de volúmenes de Civil 3D (volumen total o de material), vinculadas al grupo de líneas de
/// muestreo y a su lista de materiales: se actualizan solas cuando cambian el corredor, las superficies o las líneas.
/// <para>
/// La API .NET de Civil 3D no expone un <c>Create</c> para estas tablas (solo para las que van pegadas a cada vista de
/// sección, ver <see cref="ServicioMateriales.TablasEnVistas"/>). Lo que sí expone son los ajustes de los comandos nativos
/// <c>AddTotalVolumeTable</c> y <c>AddMaterialVolumeTable</c> (<see cref="SettingsCmdAddTotalVolumeTable"/>,
/// <see cref="SettingsCmdAddMaterialVolumeTable"/>). Por eso este servicio:
/// </para>
/// <list type="number">
/// <item>fija por API el estilo y la división (Split table, máx. filas, espaciado, dirección) de ese comando;</item>
/// <item>ejecuta el comando nativo (el usuario elige alineamiento, grupo y lista en el cuadro de Civil 3D y coloca la tabla);</item>
/// <item>devuelve los ajustes del comando a como estaban;</item>
/// <item>mide la tabla creada (GeometricExtents), la valida contra el área designada y la centra o alinea en ella;</item>
/// <item>si no entra, lo explica con medidas en papel y ofrece rehacerla con otra división o borrarla, sin dejar el dibujo a medias.</item>
/// </list>
/// </summary>
internal static class ServicioTablasVolumen
{
    private const string ComandoTotal = "_.AddTotalVolumeTable";
    private const string ComandoMaterial = "_.AddMaterialVolumeTable";

    /// <summary>Intentos de crear la tabla (el primero y hasta dos rehechos con otra división).</summary>
    private const int MaxIntentos = 3;

    /// <summary>Máximo de partes por pila que se pasa al comando para que todas vayan en una fila (o columna).</summary>
    private const int TablasPorPilaSinLimite = 99;

    // ------------------------------------------------------------------ Estilos y lista

    /// <summary>Estilos de tabla de volumen total o de material del dibujo activo.</summary>
    public static List<ElementoDibujo> Estilos(Document doc, ClaseTablaVolumen clase)
    {
        CivilDocument civil = CivilDocument.GetCivilDocument(doc.Database);
        var raiz = civil.Styles.TableStyles;
        System.Collections.IEnumerable coleccion = clase == ClaseTablaVolumen.VolumenTotal
            ? raiz.QuantityTakeoffTotalVolumeTableStyles
            : raiz.QuantityTakeoffMaterialTableStyles;
        return LectorDibujo.Estilos(doc.Database, coleccion);
    }

    /// <summary>¿El grupo tiene una lista de materiales con ese nombre?</summary>
    public static bool ExisteLista(Document doc, ObjectId grupoId, string nombre)
    {
        if (grupoId.IsNull || grupoId.IsErased || string.IsNullOrWhiteSpace(nombre)) return false;
        using Transaction tr = doc.Database.TransactionManager.StartTransaction();
        var grupo = (C3D.SampleLineGroup)tr.GetObject(grupoId, OpenMode.ForRead);
        bool existe = false;
        foreach (C3D.QTOMaterialList l in grupo.MaterialLists)
            if (string.Equals(l.Name, nombre.Trim(), StringComparison.CurrentCultureIgnoreCase)) existe = true;
        tr.Commit();
        return existe;
    }

    // ------------------------------------------------------------------ Área

    /// <summary>Pide dos esquinas en el espacio actual (modelo, o papel de la presentación activa).</summary>
    public static AreaTabla PedirArea(Document doc)
    {
        Editor ed = doc.Editor;
        PromptPointResult r1 = ed.GetPoint(new PromptPointOptions("\nPrimera esquina del área para la tabla de volúmenes: "));
        if (r1.Status != PromptStatus.OK) throw new OperacionCancelada();
        PromptPointResult r2 = ed.GetCorner(new PromptCornerOptions("\nEsquina opuesta: ", r1.Value));
        if (r2.Status != PromptStatus.OK) throw new OperacionCancelada();

        Matrix3d ucs = ed.CurrentUserCoordinateSystem; // los puntos llegan en el SCP; el área se guarda en el SCU
        Point3d a = r1.Value.TransformBy(ucs);
        Point3d b = r2.Value.TransformBy(ucs);
        var (enModelo, presentacion) = EspacioActual(doc);
        AreaTabla area = AreaTabla.DesdeEsquinas(a.X, a.Y, b.X, b.Y, enModelo, presentacion);
        if (!area.Definida) throw new ErrorUsuario("El área elegida no tiene ancho o alto.");
        Registro.Info($"Área para la tabla: {area.Ancho:0.###} × {area.Alto:0.###} en {(enModelo ? "el modelo" : "la presentación " + presentacion)}.");
        return area;
    }

    /// <summary>Usa como área un rectángulo cerrado del dibujo (por ejemplo, la franja del cuadro que dibuja el acomodo).</summary>
    public static AreaTabla AreaDeRectangulo(Document doc)
    {
        var op = new PromptEntityOptions("\nSelecciona el rectángulo de la franja del cuadro (o cualquier polilínea cerrada): ");
        op.SetRejectMessage("\nDebe ser una polilínea.");
        op.AddAllowedClass(typeof(AcDb.Polyline), true);
        PromptEntityResult r = doc.Editor.GetEntity(op);
        if (r.Status != PromptStatus.OK) throw new OperacionCancelada();

        Database db = doc.Database;
        using Transaction tr = db.TransactionManager.StartTransaction();
        var pl = (AcDb.Polyline)tr.GetObject(r.ObjectId, OpenMode.ForRead);
        if (!pl.Closed) throw new ErrorUsuario("La polilínea debe estar cerrada.");
        Extents3d ext = pl.GeometricExtents;
        bool enModelo = pl.OwnerId == SymbolUtilityServices.GetBlockModelSpaceId(db);
        string? presentacion = enModelo ? null : NombrePresentacion(tr, pl.OwnerId);
        tr.Commit();

        AreaTabla area = AreaTabla.DesdeEsquinas(ext.MinPoint.X, ext.MinPoint.Y, ext.MaxPoint.X, ext.MaxPoint.Y, enModelo, presentacion);
        if (!area.Definida) throw new ErrorUsuario("El rectángulo no tiene ancho o alto.");
        return area;
    }

    private static (bool EnModelo, string? Presentacion) EspacioActual(Document doc)
    {
        Database db = doc.Database;
        if (db.TileMode) return (true, null);
        // En una presentación con una ventana gráfica activa (MSPACE) los puntos están en el modelo.
        if (doc.Editor.CurrentViewportObjectId != db.PaperSpaceVportId) return (true, null);
        return (false, LayoutManager.Current.CurrentLayout);
    }

    private static string? NombrePresentacion(Transaction tr, ObjectId espacioId)
    {
        if (tr.GetObject(espacioId, OpenMode.ForRead) is not BlockTableRecord btr || !btr.IsLayout) return null;
        return tr.GetObject(btr.LayoutId, OpenMode.ForRead) is Layout l ? l.LayoutName : null;
    }

    /// <summary>Activa el espacio del área para que el comando cree la tabla allí.</summary>
    private static void ActivarEspacio(Document doc, AreaTabla area)
    {
        try
        {
            if (area.EnModelo)
            {
                if (!doc.Database.TileMode) LayoutManager.Current.CurrentLayout = "Model";
                return;
            }
            LayoutManager.Current.CurrentLayout = area.Presentacion!;
            doc.Editor.SwitchToPaperSpace();
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            throw new ErrorUsuario($"No se pudo activar {(area.EnModelo ? "el espacio modelo" : "la presentación \"" + area.Presentacion + "\"")} ({ex.Message}). Vuelve a definir el área.");
        }
    }

    // ------------------------------------------------------------------ Crear

    /// <summary>
    /// Crea la tabla dinámica con el comando nativo y la ubica en el área designada. Devuelve el mensaje para la paleta.
    /// </summary>
    public static string CrearDinamica(Document doc, ConfiguracionArba cfg, ObjectId grupoId)
    {
        ConfigTablaVolumen t = cfg.Materiales.TablaVolumen;
        string lista = cfg.Materiales.NombreLista;
        if (!ExisteLista(doc, grupoId, lista))
            throw new ErrorUsuario($"El grupo de líneas de muestreo no tiene la lista de materiales \"{lista}\". " +
                                   "Usa «Calcular materiales» (paso 5) para crearla: la tabla nativa se vincula a esa lista.");

        ObjectId estiloId = ObjectId.Null;
        if (!string.IsNullOrWhiteSpace(t.Estilo))
        {
            estiloId = Estilos(doc, t.Clase).FirstOrDefault(e => string.Equals(e.Nombre, t.Estilo, StringComparison.CurrentCultureIgnoreCase))?.Id ?? ObjectId.Null;
            if (estiloId.IsNull) throw new ErrorUsuario($"No existe el estilo de tabla \"{t.Estilo}\" en este dibujo. Actualiza las listas y elige otro.");
        }

        int filasDatos = ServicioMateriales.ProgresivasGrupo(doc, grupoId).Count;
        AreaTabla? area = t.Area is { Definida: true } a ? a : null;
        if (area != null) ActivarEspacio(doc, area);

        // Con «automático» la primera tabla sale sin dividir: se mide y se calcula cuántas filas entran en el área.
        bool dividir = t.Dividir;
        int filas = t.Dividir ? t.MaxFilasPorColumna : 0;
        string comando = t.Clase == ClaseTablaVolumen.VolumenTotal ? "AddTotalVolumeTable" : "AddMaterialVolumeTable";
        doc.Editor.WriteMessage($"\nARBA: en el cuadro de Civil 3D elige el alineamiento, el grupo de muestreo y la lista \"{lista}\"; luego coloca la tabla.");

        for (int intento = 1; intento <= MaxIntentos; intento++)
        {
            OpcionesDivision usadas = t.Division(filas) with { Dividir = dividir && filas > 0 };
            List<ObjectId> ids = CrearConComando(doc, t.Clase, estiloId, usadas);
            Registro.Info($"Tabla nativa ({comando}) creada: {ids.Count} objeto(s), " +
                          (usadas.Dividir ? $"máx. {usadas.MaxFilasPorColumna} filas por columna." : "sin dividir."));

            if (area == null) return "Tabla dinámica de volúmenes creada. No hay área designada: queda donde la colocaste.";

            Ubicacion u = Ubicar(doc, cfg, area, ids, filasDatos, usadas);
            if (u.Ubicada) return "Tabla dinámica de volúmenes creada y ubicada. " + u.Mensaje;

            if (u.FilasSugeridas is int sugeridas && intento < MaxIntentos)
            {
                MessageBoxResult r = Dialogos.Preguntar(u.Mensaje + "\n\n" +
                    $"Sí: borrar esta tabla y volver a crearla con máx. {sugeridas} filas por columna.\n" +
                    "No: dejar la tabla donde la colocaste.\n" +
                    "Cancelar: borrar la tabla (el dibujo queda como estaba).");
                if (r == MessageBoxResult.Yes)
                {
                    Borrar(doc, ids);
                    dividir = true; // aunque la tabla original no se dividiera, la sugerida sí
                    filas = sugeridas;
                    continue;
                }
                if (r == MessageBoxResult.No) return "La tabla no entra en el área; se dejó donde la colocaste.";
                Borrar(doc, ids);
                throw new OperacionCancelada("Se borró la tabla creada; el dibujo queda como estaba.");
            }

            if (Dialogos.Confirmar(u.Mensaje + "\n\nAceptar: borrar la tabla creada (el dibujo queda como estaba).\nCancelar: dejarla donde la colocaste."))
            {
                Borrar(doc, ids);
                throw new OperacionCancelada("Se borró la tabla creada; el dibujo queda como estaba.");
            }
            return "La tabla no entra en el área; se dejó donde la colocaste.";
        }
        return "La tabla no entra en el área.";
    }

    /// <summary>Valida y ubica una tabla que ya está en el dibujo (nativa o estándar), con el área designada.</summary>
    public static string UbicarExistente(Document doc, ConfiguracionArba cfg, ObjectId grupoId)
    {
        ConfigTablaVolumen t = cfg.Materiales.TablaVolumen;
        AreaTabla area = t.Area is { Definida: true } a ? a : throw new ErrorUsuario("Define primero el área para la tabla (o usa la franja del cuadro).");
        ActivarEspacio(doc, area);

        var op = new PromptEntityOptions("\nSelecciona la tabla de volúmenes: ");
        PromptEntityResult r = doc.Editor.GetEntity(op);
        if (r.Status != PromptStatus.OK) throw new OperacionCancelada();

        int filasDatos = grupoId.IsNull || grupoId.IsErased ? 0 : ServicioMateriales.ProgresivasGrupo(doc, grupoId).Count;
        OpcionesDivision usadas = t.Dividir && t.MaxFilasPorColumna > 0 ? t.Division() : (t.Division(0) with { Dividir = false });
        Ubicacion u = Ubicar(doc, cfg, area, new List<ObjectId> { r.ObjectId }, filasDatos, usadas);
        if (u.Ubicada) return "Tabla ubicada. " + u.Mensaje;

        string sugerencia = u.FilasSugeridas is int f
            ? $"\n\nSugerencia: créala con «Dividir tabla si excede altura» y máx. {f} filas por columna."
            : string.Empty;
        Dialogos.Aviso(u.Mensaje + sugerencia + "\n\nLa tabla no se movió.");
        return "La tabla no entra en el área; no se movió.";
    }

    // ------------------------------------------------------------------ Comando nativo

    private sealed record AjustesCreacion(ObjectId Estilo, bool Dividir, int MaxFilas, int TablasPorPila, double Espaciado, TableTitleDirectionType Direccion);

    /// <summary>Fija los ajustes del comando, lo ejecuta, restaura los ajustes y devuelve los objetos que creó.</summary>
    private static List<ObjectId> CrearConComando(Document doc, ClaseTablaVolumen clase, ObjectId estiloId, OpcionesDivision o)
    {
        Database db = doc.Database;
        var nuevos = new AjustesCreacion(
            estiloId,
            o.Dividir,
            o.Dividir ? o.MaxFilasPorColumna : 0,
            TablasPorPilaSinLimite,
            o.Espaciado,
            o.Direccion == DireccionDivision.Horizontal ? TableTitleDirectionType.Across : TableTitleDirectionType.Down);

        HashSet<ObjectId> antes = Contenido(db);
        AjustesCreacion? anteriores = null;
        try
        {
            anteriores = Fijar(db, clase, nuevos);
            Lanzar(doc, clase == ClaseTablaVolumen.VolumenTotal ? ComandoTotal : ComandoMaterial);
        }
        finally
        {
            if (anteriores != null)
            {
                try
                {
                    Fijar(db, clase, anteriores);
                }
                catch (System.Exception ex)
                {
                    Registro.Error("No se pudieron restaurar los ajustes del comando de tabla de volúmenes.", ex);
                }
            }
        }

        List<ObjectId> creados = Contenido(db).Where(id => !antes.Contains(id) && !id.IsErased).ToList();
        if (creados.Count == 0)
            throw new OperacionCancelada("No se creó ninguna tabla (se canceló el comando o Civil 3D no lo reconoció). " +
                                         "Puedes crearla a mano con AddTotalVolumeTable / AddMaterialVolumeTable y luego usar «Validar y ubicar tabla existente…».");
        List<ObjectId> tablas = creados.Where(EsTabla).ToList();
        return tablas.Count > 0 ? tablas : creados;
    }

    private static bool EsTabla(ObjectId id) =>
        id.ObjectClass.Name.Contains("Table", StringComparison.OrdinalIgnoreCase) ||
        id.ObjectClass.DxfName.Contains("TABLE", StringComparison.OrdinalIgnoreCase);

    /// <summary>Objetos del espacio actual (para saber qué creó el comando).</summary>
    private static HashSet<ObjectId> Contenido(Database db)
    {
        var ids = new HashSet<ObjectId>();
        using Transaction tr = db.TransactionManager.StartTransaction();
        var espacio = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
        foreach (ObjectId id in espacio) ids.Add(id);
        tr.Commit();
        return ids;
    }

    /// <summary>Fija los ajustes de creación del comando y devuelve los que tenía.</summary>
    private static AjustesCreacion Fijar(Database db, ClaseTablaVolumen clase, AjustesCreacion v)
    {
        CivilDocument civil = CivilDocument.GetCivilDocument(db);
        using Transaction tr = db.TransactionManager.StartTransaction();
        AjustesCreacion anteriores;
        if (clase == ClaseTablaVolumen.VolumenTotal)
        {
            var t = civil.Settings.GetSettings<SettingsCmdAddTotalVolumeTable>().TableCreation;
            anteriores = Aplicar(t.TableStyleId, t.SplitTable, t.MaximumNumberOfRows, t.MaximumTablesPerStack, t.TableSpacing, t.TileDirection, v);
        }
        else
        {
            var t = civil.Settings.GetSettings<SettingsCmdAddMaterialVolumeTable>().TableCreation;
            anteriores = Aplicar(t.TableStyleId, t.SplitTable, t.MaximumNumberOfRows, t.MaximumTablesPerStack, t.TableSpacing, t.TileDirection, v);
        }
        tr.Commit();
        return anteriores;
    }

    private static AjustesCreacion Aplicar(PropertyObjectId estilo, PropertyBoolean dividir, PropertyInt filas, PropertyInt pila,
        PropertyDouble espaciado, PropertyEnum<TableTitleDirectionType> direccion, AjustesCreacion v)
    {
        var anteriores = new AjustesCreacion(estilo.Value, dividir.Value, filas.Value, pila.Value, espaciado.Value, direccion.Value);
        if (!v.Estilo.IsNull && !v.Estilo.IsErased) estilo.Value = v.Estilo;
        dividir.Value = v.Dividir;
        if (v.MaxFilas > 0) filas.Value = v.MaxFilas;
        if (v.TablasPorPila > 0) pila.Value = v.TablasPorPila;
        espaciado.Value = v.Espaciado;
        direccion.Value = v.Direccion;
        return anteriores;
    }

    /// <summary>
    /// Ejecuta el comando y espera a que termine: cada pausa deja que el usuario responda (cuadro de diálogo, punto de inserción).
    /// </summary>
    private static void Lanzar(Document doc, string comando)
    {
        Editor ed = doc.Editor;
        string nombre = comando.TrimStart('_', '.', '-').ToUpperInvariant();
        try
        {
            ed.Command(comando);
            int pausas = 0;
            while (ComandoActivo(nombre) && pausas++ < 100) ed.Command(Editor.PauseToken);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex) when (ex.ErrorStatus == Autodesk.AutoCAD.Runtime.ErrorStatus.UserBreak)
        {
            throw new OperacionCancelada("Se canceló el comando de Civil 3D; no se creó la tabla.");
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            throw new ErrorUsuario($"No se pudo ejecutar el comando {nombre} de Civil 3D ({ex.Message}). " +
                                   "Puedes crear la tabla a mano con ese comando y luego usar «Validar y ubicar tabla existente…».");
        }
    }

    private static bool ComandoActivo(string nombre) =>
        (AcApp.GetSystemVariable("CMDNAMES") as string ?? string.Empty).ToUpperInvariant().Contains(nombre);

    // ------------------------------------------------------------------ Medir, validar y ubicar

    private sealed record Ubicacion(bool Ubicada, string Mensaje, int? FilasSugeridas);

    /// <summary>
    /// Mide los objetos de la tabla, compara en unidades de papel con el área y, si entra, los mueve a la posición
    /// elegida. Si no entra no toca nada y explica por qué (con una división sugerida cuando la hay).
    /// </summary>
    private static Ubicacion Ubicar(Document doc, ConfiguracionArba cfg, AreaTabla area, List<ObjectId> ids, int filasDatos, OpcionesDivision usadas)
    {
        ConfigTablaVolumen t = cfg.Materiales.TablaVolumen;
        var (factor, unidad) = FactorPapel(doc, cfg, area);
        Database db = doc.Database;

        using Transaction tr = db.TransactionManager.StartTransaction();
        Extents3d ext = Extension(doc, tr, ids);
        ComprobarEspacio(tr, db, area, ids[0]);

        // Todo se compara en papel (mm): en el modelo, unidades de modelo × factor de la escala; en una presentación, 1:1.
        double anchoTabla = (ext.MaxPoint.X - ext.MinPoint.X) * factor;
        double altoTabla = (ext.MaxPoint.Y - ext.MinPoint.Y) * factor;
        double anchoArea = area.Ancho * factor;
        double altoArea = area.Alto * factor;

        CabidaTabla exacta = ValidadorCabidaTabla.Evaluar(anchoArea, altoArea,
            DimensionesTabla.DesdeMedida(anchoTabla, altoTabla, t.Margen), unidad);

        if (!exacta.Cabe)
        {
            tr.Commit(); // solo lectura
            EstructuraTabla estimada = ValidadorCabidaTabla.EstimarEstructura(anchoTabla, altoTabla, filasDatos, usadas);
            int? sugeridas = null;
            if (filasDatos > 1)
            {
                DimensionesTabla? d = ValidadorCabidaTabla.MejorDivision(estimada, usadas, t.Margen, anchoArea, altoArea);
                if (d != null && d.FilasPorColumna < filasDatos && d.FilasPorColumna != (usadas.Dividir ? usadas.MaxFilasPorColumna : filasDatos))
                    sugeridas = d.FilasPorColumna;
            }
            string mensaje = exacta.Mensaje;
            if (sugeridas is int f)
            {
                CabidaTabla conDivision = ValidadorCabidaTabla.Evaluar(anchoArea, altoArea, estimada, usadas, t.Margen, unidad);
                mensaje += "\n\n" + (conDivision.Estado == EstadoCabida.CabeDividiendo ? conDivision.Mensaje : $"Con máx. {f} filas por columna entraría.") +
                           " (Estimado a partir de la tabla medida.)";
            }
            Registro.Aviso("Tabla de volúmenes: " + exacta.Mensaje);
            return new Ubicacion(false, mensaje, sugeridas);
        }

        double margenDibujo = t.Margen / factor;
        double anchoDibujo = ext.MaxPoint.X - ext.MinPoint.X;
        double altoDibujo = ext.MaxPoint.Y - ext.MinPoint.Y;
        var (x, y) = ValidadorCabidaTabla.Posicionar(area.MinX, area.MinY, area.Ancho, area.Alto, anchoDibujo, altoDibujo, t.Alineacion, margenDibujo);
        var desplazamiento = new Vector3d(x - ext.MinPoint.X, y - ext.MinPoint.Y, 0);
        if (desplazamiento.Length > 1e-9)
        {
            Matrix3d mover = Matrix3d.Displacement(desplazamiento);
            foreach (ObjectId id in ids)
            {
                if (id.IsNull || id.IsErased) continue;
                var ent = (AcDb.Entity)tr.GetObject(id, OpenMode.ForWrite);
                ent.TransformBy(mover);
            }
        }
        tr.Commit();
        Registro.Info("Tabla de volúmenes ubicada: " + exacta.Mensaje);
        return new Ubicacion(true, exacta.Mensaje, null);
    }

    /// <summary>Factor de dibujo a papel y abreviatura de la unidad de papel para los mensajes.</summary>
    private static (double Factor, string Unidad) FactorPapel(Document doc, ConfiguracionArba cfg, AreaTabla area)
    {
        ConversorEscala? c = null;
        try
        {
            c = ServicioEscala.Conversor(doc.Database, cfg);
        }
        catch (ErrorUsuario)
        {
            // Sin escala configurada solo se puede trabajar en una presentación (papel 1:1).
        }
        string unidad = c?.UnidadPapel.Abreviatura() ?? "mm";
        if (!area.EnModelo) return (1.0, unidad);
        if (c == null) throw new ErrorUsuario("Elige la escala en la pestaña Acomodo: hace falta para pasar el área del modelo a medidas de papel.");
        return (c.FactorH, unidad);
    }

    /// <summary>Unión de las extensiones de los objetos de la tabla. Regenera una vez si todavía no tienen gráficos.</summary>
    private static Extents3d Extension(Document doc, Transaction tr, List<ObjectId> ids)
    {
        Extents3d? total = Unir(tr, ids);
        if (total == null)
        {
            doc.Editor.Regen();
            total = Unir(tr, ids);
        }
        return total ?? throw new ErrorUsuario("No se pudo medir la tabla (no tiene extensión geométrica).");
    }

    private static Extents3d? Unir(Transaction tr, List<ObjectId> ids)
    {
        Extents3d? total = null;
        foreach (ObjectId id in ids)
        {
            if (id.IsNull || id.IsErased || tr.GetObject(id, OpenMode.ForRead) is not AcDb.Entity ent) continue;
            try
            {
                Extents3d e = ent.GeometricExtents;
                if (total is Extents3d actual)
                {
                    actual.AddExtents(e);
                    total = actual;
                }
                else
                {
                    total = e;
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                // sin gráficos todavía
            }
        }
        return total;
    }

    /// <summary>La tabla y el área deben estar en el mismo espacio (y en la misma presentación).</summary>
    private static void ComprobarEspacio(Transaction tr, Database db, AreaTabla area, ObjectId tablaId)
    {
        var ent = (AcDb.Entity)tr.GetObject(tablaId, OpenMode.ForRead);
        bool enModelo = ent.OwnerId == SymbolUtilityServices.GetBlockModelSpaceId(db);
        string? presentacion = enModelo ? null : NombrePresentacion(tr, ent.OwnerId);
        bool mismo = enModelo == area.EnModelo &&
                     (enModelo || string.Equals(presentacion, area.Presentacion, StringComparison.CurrentCultureIgnoreCase));
        if (mismo) return;
        string Donde(bool m, string? p) => m ? "el espacio modelo" : $"la presentación \"{p}\"";
        throw new ErrorUsuario($"La tabla está en {Donde(enModelo, presentacion)} y el área en {Donde(area.EnModelo, area.Presentacion)}. " +
                               "Deben estar en el mismo espacio.");
    }

    private static void Borrar(Document doc, List<ObjectId> ids)
    {
        using Transaction tr = doc.Database.TransactionManager.StartTransaction();
        foreach (ObjectId id in ids)
        {
            if (id.IsNull || id.IsErased) continue;
            tr.GetObject(id, OpenMode.ForWrite).Erase();
        }
        tr.Commit();
        Registro.Info($"Tabla de volúmenes borrada ({ids.Count} objeto(s)).");
    }
}
