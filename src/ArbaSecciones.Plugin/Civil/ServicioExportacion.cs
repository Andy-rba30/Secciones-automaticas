using ArbaSecciones.Core.Comun;
using ArbaSecciones.Core.Configuracion;
using ArbaSecciones.Plugin.Infraestructura;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.PlottingServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDb = Autodesk.AutoCAD.DatabaseServices;

namespace ArbaSecciones.Plugin.Civil;

internal sealed record ResultadoExportacion(int Pdf, int Dwg, List<string> Avisos);

/// <summary>
/// Fase 2: un PDF por presentación (A1, escala 1:1) y un DWG independiente por lámina con
/// el modelo (solo secciones y cuadro, sin rejilla) y la "Presentación 1" (membrete en 0,0,0 y viewport).
/// </summary>
internal static class ServicioExportacion
{
    public static ResultadoExportacion Exportar(Document doc, ConfiguracionArba cfg, string carpeta)
    {
        if (string.IsNullOrWhiteSpace(carpeta)) throw new ErrorUsuario("Elige la carpeta de salida.");
        if (!cfg.Exportacion.Pdf && !cfg.Exportacion.Dwg) throw new ErrorUsuario("Marca PDF, DWG o ambos.");
        Directory.CreateDirectory(carpeta);

        Database db = doc.Database;
        List<string> presentaciones = ServicioPresentaciones.Anteriores(db, cfg);
        if (presentaciones.Count == 0) throw new ErrorUsuario($"No hay presentaciones con el prefijo \"{cfg.Presentaciones.Prefijo}\". Créalas primero.");

        var avisos = new List<string>();
        int pdf = cfg.Exportacion.Pdf ? ExportarPdf(doc, presentaciones, carpeta, cfg.Exportacion) : 0;
        int dwg = cfg.Exportacion.Dwg ? ExportarDwg(doc, cfg, presentaciones, carpeta, avisos) : 0;

        LayoutManager.Current.CurrentLayout = presentaciones[0];
        Registro.Info($"Exportación en {carpeta}: {pdf} PDF, {dwg} DWG.");
        return new ResultadoExportacion(pdf, dwg, avisos);
    }

    // ───────────────────────────── PDF ─────────────────────────────

    private static int ExportarPdf(Document doc, List<string> presentaciones, string carpeta, ConfigExportacion cfg)
    {
        if (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting)
            throw new ErrorUsuario("Hay otro trazado en curso. Espera a que termine.");

        object fondo = AcApp.GetSystemVariable("BACKGROUNDPLOT");
        AcApp.SetSystemVariable("BACKGROUNDPLOT", (short)0);
        int n = 0;
        try
        {
            LayoutManager lm = LayoutManager.Current;
            using var progreso = new Progreso("Exportando PDF…", presentaciones.Count);
            foreach (string nombre in presentaciones)
            {
                progreso.Avanzar();
                lm.CurrentLayout = nombre;
                Trazar(doc, lm.GetLayoutId(nombre), Path.Combine(carpeta, NombreArchivo(nombre) + ".pdf"), cfg);
                n++;
            }
        }
        finally
        {
            AcApp.SetSystemVariable("BACKGROUNDPLOT", fondo);
        }
        return n;
    }

    private static void Trazar(Document doc, ObjectId layoutId, string ruta, ConfigExportacion cfg)
    {
        using Transaction tr = doc.Database.TransactionManager.StartTransaction();
        var layout = (Layout)tr.GetObject(layoutId, OpenMode.ForRead);
        var ajustes = new PlotSettings(layout.ModelType);
        ajustes.CopyFrom(layout);

        PlotSettingsValidator psv = PlotSettingsValidator.Current;
        try
        {
            psv.SetPlotConfigurationName(ajustes, cfg.Dispositivo, null);
            psv.RefreshLists(ajustes);
            psv.SetPlotConfigurationName(ajustes, cfg.Dispositivo, cfg.Papel);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            throw new ErrorUsuario($"El trazador \"{cfg.Dispositivo}\" no acepta el papel \"{cfg.Papel}\" ({ex.Message}).");
        }
        psv.SetPlotType(ajustes, AcDb.PlotType.Layout);
        psv.SetUseStandardScale(ajustes, true);
        psv.SetStdScaleType(ajustes, StdScaleType.StdScale1To1);
        psv.SetPlotPaperUnits(ajustes, PlotPaperUnit.Millimeters);

        var info = new PlotInfo { Layout = layoutId, OverrideSettings = ajustes };
        var validador = new PlotInfoValidator { MediaMatchingPolicy = MatchingPolicy.MatchEnabled };
        validador.Validate(info);

        using (PlotEngine motor = PlotFactory.CreatePublishEngine())
        {
            var pagina = new PlotPageInfo();
            motor.BeginPlot(null, null);
            motor.BeginDocument(info, doc.Name, null, 1, true, ruta);
            motor.BeginPage(pagina, info, true, null);
            motor.BeginGenerateGraphics(null);
            motor.EndGenerateGraphics(null);
            motor.EndPage(null);
            motor.EndDocument(null);
            motor.EndPlot(null);
        }
        tr.Commit();
    }

    // ───────────────────────────── DWG ─────────────────────────────

    private static int ExportarDwg(Document doc, ConfiguracionArba cfg, List<string> presentaciones, string carpeta, List<string> avisos)
    {
        var marcos = ServicioAcomodo.LeerMarcos(doc.Database)
            .ToDictionary(mk => DatosLamina.NombrePresentacion(cfg.Presentaciones, cfg.Presentaciones.CorrelativoInicial + mk.Info.Numero - 1),
                StringComparer.OrdinalIgnoreCase);

        int n = 0;
        using var progreso = new Progreso("Exportando DWG por lámina…", presentaciones.Count);
        foreach (string nombre in presentaciones)
        {
            progreso.Avanzar();
            if (!marcos.TryGetValue(nombre, out MarcoLamina? marco))
            {
                avisos.Add($"{nombre}: no tiene marco en el modelo; no se exportó.");
                continue;
            }
            ExportarLamina(doc, cfg, nombre, marco, Path.Combine(carpeta, NombreArchivo(nombre) + ".dwg"), avisos);
            n++;
        }
        return n;
    }

    private static void ExportarLamina(Document doc, ConfiguracionArba cfg, string presentacion, MarcoLamina marco, string ruta, List<string> avisos)
    {
        Database db = doc.Database;
        var originales = new List<ObjectId>();
        var civiles = new List<ObjectId>();

        // 1) Lo que está dentro del marco en el modelo: secciones y cuadro. Los marcos ARBA no van.
        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            var modelo = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in modelo)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not AcDb.Entity ent) continue;
                using (ResultBuffer? rb = ent.GetXDataForApplication(ServicioAcomodo.Aplicacion))
                {
                    if (rb != null) continue;
                }
                Extents3d ext;
                try { ext = ent.GeometricExtents; }
                catch (Autodesk.AutoCAD.Runtime.Exception) { continue; }
                if (!Dentro(ext, marco.Extension)) continue;
                (EsCivil(ent) ? civiles : originales).Add(id);
            }
            tr.Commit();
        }

        // 2) Los objetos de Civil 3D se desarman en entidades de AutoCAD, temporalmente en el modelo.
        var temporales = new List<ObjectId>();
        try
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var modelo = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                foreach (ObjectId id in civiles)
                {
                    var ent = (AcDb.Entity)tr.GetObject(id, OpenMode.ForRead);
                    List<AcDb.Entity> piezas = Desarmar(ent, 0);
                    if (piezas.Count == 0)
                    {
                        avisos.Add($"{presentacion}: un objeto de Civil 3D ({ent.GetType().Name}) no se pudo desarmar y se copia tal cual.");
                        originales.Add(id);
                        continue;
                    }
                    foreach (AcDb.Entity pieza in piezas)
                    {
                        modelo.AppendEntity(pieza);
                        tr.AddNewlyCreatedDBObject(pieza, true);
                        temporales.Add(pieza.ObjectId);
                    }
                }
                tr.Commit();
            }

            var ids = new ObjectIdCollection();
            foreach (ObjectId id in originales) ids.Add(id);
            foreach (ObjectId id in temporales) ids.Add(id);
            if (ids.Count == 0) avisos.Add($"{presentacion}: el marco no contiene objetos.");

            // 3) Nueva base de datos con solo esas entidades y lo que necesitan (capas, estilos, bloques).
            ObjectId layoutOrigenId = LayoutManager.Current.GetLayoutId(presentacion);
            using Database nueva = ids.Count > 0 ? db.Wblock(ids, Point3d.Origin) : new Database(true, true);
            ArmarPresentacion(db, nueva, layoutOrigenId, marco, cfg, avisos);
            nueva.SaveAs(ruta, DwgVersion.Current);
        }
        finally
        {
            if (temporales.Count > 0)
            {
                using Transaction tr = db.TransactionManager.StartTransaction();
                foreach (ObjectId id in temporales)
                    if (!id.IsErased) tr.GetObject(id, OpenMode.ForWrite).Erase();
                tr.Commit();
            }
        }
    }

    /// <summary>
    /// Deja en el DWG nuevo una sola presentación con el membrete en (0,0,0) y el viewport configurado.
    /// Todo con LayoutManager y la base de trabajo cambiada (lección 1), restaurada en finally.
    /// </summary>
    private static void ArmarPresentacion(Database origen, Database nueva, ObjectId layoutOrigenId, MarcoLamina marco, ConfiguracionArba cfg, List<string> avisos)
    {
        ConfigMembrete m = cfg.Membrete;
        var trazado = new PlotSettings(false);
        var papelSinCamara = new ObjectIdCollection();
        ObjectId camaraOrigen = ObjectId.Null;
        Point3d? posicionMembrete = null;

        using (Transaction tr = origen.TransactionManager.StartTransaction())
        {
            var layout = (Layout)tr.GetObject(layoutOrigenId, OpenMode.ForRead);
            trazado.CopyFrom(layout);
            var espacio = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);
            int mejor = -1;
            foreach (ObjectId id in espacio)
            {
                DBObject o = tr.GetObject(id, OpenMode.ForRead);
                if (o is Viewport && camaraOrigen.IsNull)
                {
                    camaraOrigen = id;
                    continue;
                }
                papelSinCamara.Add(id);
                if (o is BlockReference br && br.AttributeCollection.Count > 0)
                {
                    bool porNombre = !string.IsNullOrWhiteSpace(m.NombreBloque) &&
                                     string.Equals(LectorDibujo.NombreBloque(tr, br), m.NombreBloque, StringComparison.OrdinalIgnoreCase);
                    int puntaje = porNombre ? int.MaxValue : br.AttributeCollection.Count;
                    if (puntaje > mejor)
                    {
                        mejor = puntaje;
                        posicionMembrete = br.Position;
                    }
                }
            }
            tr.Commit();
        }

        Database anterior = HostApplicationServices.WorkingDatabase;
        try
        {
            HostApplicationServices.WorkingDatabase = nueva;
            LayoutManager lm = LayoutManager.Current;
            string destino = string.IsNullOrWhiteSpace(cfg.Exportacion.NombrePresentacionDwg) ? "Presentación 1" : cfg.Exportacion.NombrePresentacionDwg.Trim();

            // Una sola presentación, con el nombre pedido. Nunca se borran a mano registros de *Paper_Space.
            List<string> existentes = LectorDibujo.Presentaciones(nueva);
            if (existentes.Count == 0)
            {
                lm.CreateLayout(destino);
            }
            else
            {
                string conservar = existentes.FirstOrDefault(n => string.Equals(n, destino, StringComparison.OrdinalIgnoreCase)) ?? existentes[0];
                foreach (string n in existentes.Where(n => n != conservar)) lm.DeleteLayout(n);
                if (conservar != destino) lm.RenameLayout(conservar, destino);
            }

            ObjectId espacioId;
            bool tieneCamara;
            using (Transaction tr = nueva.TransactionManager.StartTransaction())
            {
                var layout = (Layout)tr.GetObject(lm.GetLayoutId(destino), OpenMode.ForWrite);
                layout.CopyFrom(trazado);
                espacioId = layout.BlockTableRecordId;
                tieneCamara = !HerramientasViewport.Viewports(tr, espacioId).Camara.IsNull;
                tr.Commit();
            }

            // Espacio papel de la lámina (membrete ya lleno y viewport). La cámara va primero si la hoja no tiene una.
            var aClonar = new ObjectIdCollection();
            if (!tieneCamara && !camaraOrigen.IsNull) aClonar.Add(camaraOrigen);
            foreach (ObjectId id in papelSinCamara) aClonar.Add(id);
            if (aClonar.Count > 0) origen.WblockCloneObjects(aClonar, espacioId, new IdMapping(), DuplicateRecordCloning.Ignore, false);

            using (Transaction tr = nueva.TransactionManager.StartTransaction())
            {
                Vector3d mover = posicionMembrete.HasValue ? Point3d.Origin - posicionMembrete.Value : new Vector3d(0, 0, 0);
                var espacio = (BlockTableRecord)tr.GetObject(espacioId, OpenMode.ForRead);
                if (mover.Length > 1e-9)
                {
                    foreach (ObjectId id in espacio)
                    {
                        var ent = (AcDb.Entity)tr.GetObject(id, OpenMode.ForRead);
                        if (ent is Viewport) continue;
                        ent.UpgradeOpen();
                        ent.TransformBy(Matrix3d.Displacement(mover));
                    }
                }

                var (camara, flotantes) = HerramientasViewport.Viewports(tr, espacioId);
                ObjectId vpId = HerramientasViewport.FlotanteDentroDe(tr, flotantes, m.AreaMinX, m.AreaMinY, m.AreaMaxX, m.AreaMaxY);
                if (vpId.IsNull && flotantes.Count > 0) vpId = flotantes[0];

                Viewport vp;
                bool nuevo = vpId.IsNull;
                if (nuevo)
                {
                    if (camara.IsNull)
                    {
                        var layout = (Layout)tr.GetObject(lm.GetLayoutId(destino), OpenMode.ForWrite);
                        layout.Initialize();
                    }
                    vp = new Viewport();
                    espacio.UpgradeOpen();
                    espacio.AppendEntity(vp);
                    tr.AddNewlyCreatedDBObject(vp, true);
                }
                else
                {
                    vp = (Viewport)tr.GetObject(vpId, OpenMode.ForWrite);
                }

                string capa = string.IsNullOrWhiteSpace(cfg.Presentaciones.CapaViewport) ? "Defpoints" : cfg.Presentaciones.CapaViewport.Trim();
                HerramientasViewport.AsegurarCapaNoImprimible(nueva, tr, capa);
                AnnotationScale escala = ServicioEscala.ObtenerOCrearPorRelacion(nueva, 1.0 / marco.Factor, marco.Escala);
                Extents3d ext = marco.Extension;
                HerramientasViewport.Configurar(vp,
                    m.AreaMinX + mover.X, m.AreaMinY + mover.Y, m.AnchoUtil, m.AltoUtil,
                    new Point2d((ext.MinPoint.X + ext.MaxPoint.X) / 2, (ext.MinPoint.Y + ext.MaxPoint.Y) / 2),
                    marco.Factor, escala, capa);
                if (nuevo)
                {
                    try { vp.On = true; }
                    catch (Autodesk.AutoCAD.Runtime.Exception ex) { avisos.Add($"{destino}: no se pudo encender el viewport nuevo ({ex.Message})."); }
                }

                // Lección 5: sin rejilla en el modelo ni en el papel.
                foreach (ObjectId id in espacio)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is Viewport v && v.GridOn)
                    {
                        v.UpgradeOpen();
                        v.GridOn = false;
                    }
                }
                ApagarRejillaModelo(nueva, tr);

                // Lección 4: fuentes SHX que no están instaladas.
                CorregirFuentes(nueva, tr, avisos);
                tr.Commit();
            }

            // Lección 6: abrir directamente en la presentación.
            nueva.TileMode = false;
        }
        finally
        {
            HostApplicationServices.WorkingDatabase = anterior;
        }
    }

    /// <summary>Desarma un objeto de Civil 3D hasta que solo queden entidades de AutoCAD (máximo 4 niveles).</summary>
    private static List<AcDb.Entity> Desarmar(AcDb.Entity ent, int nivel)
    {
        var resultado = new List<AcDb.Entity>();
        var piezas = new DBObjectCollection();
        try
        {
            ent.Explode(piezas);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            Registro.Aviso($"No se pudo desarmar {ent.GetType().Name}: {ex.Message}");
            return resultado;
        }

        foreach (DBObject o in piezas)
        {
            if (o is not AcDb.Entity pieza)
            {
                o.Dispose();
                continue;
            }
            if (EsCivil(pieza) && nivel < 4)
            {
                resultado.AddRange(Desarmar(pieza, nivel + 1));
                pieza.Dispose();
            }
            else
            {
                resultado.Add(pieza);
            }
        }
        return resultado;
    }

    private static bool EsCivil(AcDb.Entity ent)
    {
        string ns = ent.GetType().Namespace ?? string.Empty;
        return ns.StartsWith("Autodesk.Civil", StringComparison.Ordinal) || ns.StartsWith("Autodesk.Aec", StringComparison.Ordinal);
    }

    private static bool Dentro(Extents3d e, Extents3d marco)
    {
        double tol = Math.Max(marco.MaxPoint.X - marco.MinPoint.X, marco.MaxPoint.Y - marco.MinPoint.Y) * 1e-6;
        return e.MinPoint.X >= marco.MinPoint.X - tol && e.MinPoint.Y >= marco.MinPoint.Y - tol &&
               e.MaxPoint.X <= marco.MaxPoint.X + tol && e.MaxPoint.Y <= marco.MaxPoint.Y + tol;
    }

    private static void ApagarRejillaModelo(Database db, Transaction tr)
    {
        var tabla = (ViewportTable)tr.GetObject(db.ViewportTableId, OpenMode.ForRead);
        foreach (ObjectId id in tabla)
        {
            var registro = (ViewportTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (!registro.GridEnabled) continue;
            registro.UpgradeOpen();
            registro.GridEnabled = false;
        }
    }

    /// <summary>
    /// Reemplaza las fuentes SHX que no están instaladas: las de forma por ltypeshp.shx (lección 4),
    /// las de texto por simplex.shx, y quita las bigfont faltantes. Así el archivo abre sin el aviso de SHX.
    /// </summary>
    private static void CorregirFuentes(Database db, Transaction tr, List<string> avisos)
    {
        var tabla = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
        foreach (ObjectId id in tabla)
        {
            var estilo = (TextStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
            string archivo = estilo.FileName ?? string.Empty;
            string extension = Path.GetExtension(archivo);
            bool esShx = extension.Equals(".shx", StringComparison.OrdinalIgnoreCase) || (archivo.Length > 0 && extension.Length == 0);
            if (esShx)
            {
                string buscar = extension.Length == 0 ? archivo + ".shx" : archivo;
                if (!ExisteArchivo(buscar, db, estilo.IsShapeFile ? FindFileHint.CompiledShapeFile : FindFileHint.FontFile))
                {
                    string reemplazo = estilo.IsShapeFile ? "ltypeshp.shx" : "simplex.shx";
                    estilo.UpgradeOpen();
                    estilo.FileName = reemplazo;
                    avisos.Add($"Fuente {archivo} no instalada: se usa {reemplazo}.");
                }
            }

            string grande = estilo.BigFontFileName ?? string.Empty;
            if (grande.Length > 0 && !ExisteArchivo(grande, db, FindFileHint.FontFile))
            {
                estilo.UpgradeOpen();
                estilo.BigFontFileName = string.Empty;
                avisos.Add($"Fuente grande {grande} no instalada: se quitó.");
            }
        }
    }

    private static bool ExisteArchivo(string archivo, Database db, FindFileHint pista)
    {
        try
        {
            return !string.IsNullOrEmpty(HostApplicationServices.Current.FindFile(archivo, db, pista));
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return false;
        }
    }

    private static string NombreArchivo(string nombre)
    {
        var invalidos = Path.GetInvalidFileNameChars();
        return new string(nombre.Select(c => invalidos.Contains(c) ? '_' : c).ToArray());
    }
}
