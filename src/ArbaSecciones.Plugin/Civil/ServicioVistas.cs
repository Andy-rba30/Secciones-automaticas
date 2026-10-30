using System.Globalization;
using System.Windows;
using ArbaSecciones.Core.Acomodo;
using ArbaSecciones.Core.Comun;
using ArbaSecciones.Core.Configuracion;
using ArbaSecciones.Core.Escalas;
using ArbaSecciones.Plugin.Infraestructura;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using C3D = Autodesk.Civil.DatabaseServices;

namespace ArbaSecciones.Plugin.Civil;

/// <summary>
/// Prepara las vistas de sección para el acomodo: fija la escala de anotación, revisa la exageración del estilo,
/// crea las vistas que faltan (sin duplicar las existentes) y mide la extensión real de cada una.
/// </summary>
internal static class ServicioVistas
{
    public static Medicion PrepararYMedir(Document doc, ConfiguracionArba cfg, ObjectId grupoId, Point3d zonaEspera)
    {
        if (grupoId.IsNull || grupoId.IsErased) throw new ErrorUsuario("Elige el alineamiento y el grupo de líneas de muestreo en la pestaña Muestreo.");
        Database db = doc.Database;
        ConversorEscala conversor = ServicioEscala.Conversor(db, cfg);

        // 1) Escala de anotación antes de crear o medir: los textos de rejilla y bandas cambian el tamaño de cada vista.
        ServicioEscala.FijarEscalaAnotacion(db, conversor);

        ObjectId estiloId = LectorDibujo.EstiloPorNombre(doc, cfg.Acomodo.EstiloVista, bandas: false);
        ObjectId bandasId = LectorDibujo.EstiloPorNombre(doc, cfg.Acomodo.ConjuntoBandas, bandas: true);
        if (!string.IsNullOrWhiteSpace(cfg.Acomodo.EstiloVista) && estiloId.IsNull)
            throw new ErrorUsuario($"No existe el estilo de vista \"{cfg.Acomodo.EstiloVista}\" en este dibujo.");
        if (!string.IsNullOrWhiteSpace(cfg.Acomodo.ConjuntoBandas) && bandasId.IsNull)
            throw new ErrorUsuario($"No existe el conjunto de bandas \"{cfg.Acomodo.ConjuntoBandas}\" en este dibujo.");

        // 2) Exageración vertical del estilo frente a la pedida.
        if (!estiloId.IsNull) estiloId = RevisarExageracion(db, cfg, estiloId, conversor.Escala.Exageracion);

        // 3) Vistas que faltan (en una zona de espera), limpieza de duplicadas y estilo en todas.
        int creadas = AsegurarVistas(db, grupoId, estiloId, bandasId, zonaEspera, PasoEspera(cfg));

        // 4) Regenerar para que las extensiones respondan a la escala de anotación vigente.
        doc.Editor.Regen();

        // 5) Medir.
        var vistas = Medir(db, grupoId, conversor);
        Registro.Info($"Vistas medidas: {vistas.Count} ({creadas} nuevas) a escala {conversor.Escala}.");
        return new Medicion(doc.Name, grupoId, conversor, vistas);
    }

    private static double PasoEspera(ConfiguracionArba cfg) =>
        (cfg.Muestreo.AnchoIzquierdo + cfg.Muestreo.AnchoDerecho) * 1.6 + 10;

    private static ObjectId RevisarExageracion(Database db, ConfiguracionArba cfg, ObjectId estiloId, double pedida)
    {
        using Transaction tr = db.TransactionManager.StartTransaction();
        double delEstilo;
        try
        {
            delEstilo = ServicioEscala.ExageracionEstilo(tr, estiloId);
        }
        catch (System.Exception ex)
        {
            Registro.Error("No se pudo leer la exageración del estilo de vista.", ex);
            tr.Commit();
            return estiloId;
        }

        if (Math.Abs(delEstilo - pedida) <= 1e-6)
        {
            tr.Commit();
            return estiloId;
        }

        string nombre = ServicioEscala.NombreEstilo(tr, estiloId);
        var inv = CultureInfo.InvariantCulture;
        MessageBoxResult r = Dialogos.Preguntar(
            $"El estilo \"{nombre}\" dibuja con exageración vertical {delEstilo.ToString("0.##", inv)}, " +
            $"pero la escala elegida ({cfg.Escala.Crear()}) necesita {pedida.ToString("0.##", inv)}.\n\n" +
            "Sí: crear una copia del estilo con esa exageración y usarla.\n" +
            "No: seguir con el estilo tal como está.\n" +
            "Cancelar: no hacer nada.");
        if (r == MessageBoxResult.Cancel) throw new OperacionCancelada();
        if (r == MessageBoxResult.No)
        {
            tr.Commit();
            Registro.Aviso($"Se mantiene el estilo {nombre} con exageración {delEstilo}.");
            return estiloId;
        }

        ObjectId copiaId = ServicioEscala.EstiloConExageracion(db, tr, estiloId, pedida);
        cfg.Acomodo.EstiloVista = ServicioEscala.NombreEstilo(tr, copiaId);
        tr.Commit();
        Estado.Guardar();
        return copiaId;
    }

    private static int AsegurarVistas(Database db, ObjectId grupoId, ObjectId estiloId, ObjectId bandasId, Point3d zonaEspera, double paso)
    {
        using Transaction tr = db.TransactionManager.StartTransaction();
        var grupo = (C3D.SampleLineGroup)tr.GetObject(grupoId, OpenMode.ForRead);
        var lineas = new List<ObjectId>();
        foreach (ObjectId id in grupo.GetSampleLineIds())
        {
            if (id.IsNull || id.IsErased) continue;
            if (tr.GetObject(id, OpenMode.ForRead) is C3D.SampleLine sl && !sl.IsErased) lineas.Add(id);
        }
        if (lineas.Count == 0) throw new ErrorUsuario($"El grupo \"{grupo.Name}\" no tiene líneas de muestreo. Créalas en la pestaña Muestreo.");

        int creadas = 0;
        int indice = 0;
        using (var progreso = new Progreso("Preparando vistas de sección…", lineas.Count))
        {
            foreach (ObjectId lineaId in lineas)
            {
                progreso.Avanzar();
                var linea = (C3D.SampleLine)tr.GetObject(lineaId, OpenMode.ForRead);
                List<ObjectId> vistas = VistasDe(tr, linea);

                if (vistas.Count == 0)
                {
                    var ubicacion = new Point3d(zonaEspera.X + indice * paso, zonaEspera.Y, 0);
                    ObjectId vistaId = C3D.SectionView.Create(linea.Name + " - " + grupo.Name, lineaId, ubicacion);
                    var vista = (C3D.SectionView)tr.GetObject(vistaId, OpenMode.ForWrite);
                    if (!estiloId.IsNull) vista.StyleId = estiloId;
                    if (!bandasId.IsNull) vista.Bands.ImportBandSetStyle(bandasId);
                    creadas++;
                }
                else
                {
                    // Si hay vistas duplicadas para la misma línea de muestreo, eliminar las sobrantes para evitar superposición
                    for (int k = 1; k < vistas.Count; k++)
                    {
                        if (tr.GetObject(vistas[k], OpenMode.ForWrite) is C3D.SectionView dup && !dup.IsErased)
                            dup.Erase(true);
                    }

                    var vista = (C3D.SectionView)tr.GetObject(vistas[0], OpenMode.ForRead);
                    if (!estiloId.IsNull && vista.StyleId != estiloId)
                    {
                        vista.UpgradeOpen();
                        vista.StyleId = estiloId;
                    }
                    if (!bandasId.IsNull)
                    {
                        if (!vista.IsWriteEnabled) vista.UpgradeOpen();
                        vista.Bands.ImportBandSetStyle(bandasId);
                    }
                }
                indice++;
            }
        }
        tr.Commit();
        return creadas;
    }

    public static List<VistaMedida> Medir(Database db, ObjectId grupoId, ConversorEscala conversor)
    {
        var resultado = new List<VistaMedida>();
        using Transaction tr = db.TransactionManager.StartTransaction();
        var grupo = (C3D.SampleLineGroup)tr.GetObject(grupoId, OpenMode.ForRead);
        foreach (ObjectId lineaId in grupo.GetSampleLineIds())
        {
            if (lineaId.IsNull || lineaId.IsErased) continue;
            if (tr.GetObject(lineaId, OpenMode.ForRead) is not C3D.SampleLine linea || linea.IsErased) continue;
            List<ObjectId> vistas = VistasDe(tr, linea);
            if (vistas.Count == 0) continue;

            var vista = (C3D.SectionView)tr.GetObject(vistas[0], OpenMode.ForRead);
            Extents3d ext;
            try
            {
                ext = vista.GeometricExtents;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                throw new ErrorUsuario($"No se pudo medir la vista de la progresiva {FormatoProgresiva.Formatear(linea.Station)} ({ex.Message}).");
            }

            double exageracion = 1.0;
            try
            {
                exageracion = ServicioEscala.ExageracionEstilo(tr, vista.StyleId);
            }
            catch (System.Exception ex)
            {
                Registro.Error("No se pudo leer la exageración del estilo; se asume 1.", ex);
            }

            var medida = MedidaSeccion.DesdeExtension(
                vistas[0].Handle.ToString(),
                linea.Station,
                ext.MaxPoint.X - ext.MinPoint.X,
                ext.MaxPoint.Y - ext.MinPoint.Y,
                Math.Abs(vista.OffsetRight - vista.OffsetLeft),
                vista.ElevationMax - vista.ElevationMin,
                exageracion,
                conversor);
            resultado.Add(new VistaMedida(vistas[0], lineaId, medida));
        }
        tr.Commit();
        return resultado.OrderBy(v => v.Medida.Progresiva).ToList();
    }

    private static List<ObjectId> VistasDe(Transaction tr, C3D.SampleLine linea)
    {
        var lista = new List<ObjectId>();
        foreach (ObjectId id in linea.GetSectionViewIds())
        {
            if (id.IsNull || id.IsErased) continue;
            if (tr.GetObject(id, OpenMode.ForRead) is C3D.SectionView sv && !sv.IsErased)
                lista.Add(id);
        }
        return lista;
    }
}
