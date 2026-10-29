using ArbaSecciones.Core.Comun;
using ArbaSecciones.Core.Escalas;
using ArbaSecciones.Plugin.Infraestructura;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcDb = Autodesk.AutoCAD.DatabaseServices;

namespace ArbaSecciones.Plugin.Civil;

internal sealed record AreaUtil(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Ancho => MaxX - MinX;
    public double Alto => MaxY - MinY;
}

internal sealed record BloqueMembrete(string Nombre, IReadOnlyList<string> Etiquetas, Point3d Posicion);

/// <summary>Presentación plantilla: importarla de un DWG/DWT o armarla con un bloque, y leer su área útil.</summary>
internal static class ServicioMembrete
{
    /// <summary>Presentaciones de un DWG/DWT sin abrirlo en pantalla.</summary>
    public static List<string> PresentacionesDeArchivo(string ruta)
    {
        using Database origen = LeerArchivo(ruta);
        return LectorDibujo.Presentaciones(origen);
    }

    /// <summary>Bloques con atributos de un DWG.</summary>
    public static List<string> BloquesDeArchivo(string ruta)
    {
        using Database origen = LeerArchivo(ruta);
        return LectorDibujo.BloquesConAtributos(origen);
    }

    /// <summary>
    /// Copia una presentación de otro archivo al dibujo actual con el nombre indicado: configuración de trazado
    /// y todo su espacio papel (membrete, textos, viewports flotantes). La cámara de la hoja no se copia.
    /// </summary>
    public static void ImportarPresentacion(Document doc, string ruta, string nombreOrigen, string nombreDestino)
    {
        Database db = doc.Database;
        using Database origen = LeerArchivo(ruta);
        PrepararDestino(nombreDestino);

        LayoutManager lm = LayoutManager.Current;
        ObjectId nuevoId = lm.CreateLayout(nombreDestino);
        lm.CurrentLayout = nombreDestino; // al activarla se crea su propia cámara de hoja

        var ids = new ObjectIdCollection();
        ObjectId espacioDestino;
        using (Transaction trO = origen.TransactionManager.StartTransaction())
        {
            var dic = (DBDictionary)trO.GetObject(origen.LayoutDictionaryId, OpenMode.ForRead);
            if (!dic.Contains(nombreOrigen)) throw new ErrorUsuario($"El archivo no tiene la presentación \"{nombreOrigen}\".");
            var layoutOrigen = (Layout)trO.GetObject(dic.GetAt(nombreOrigen), OpenMode.ForRead);

            bool camaraSaltada = false;
            var espacioOrigen = (BlockTableRecord)trO.GetObject(layoutOrigen.BlockTableRecordId, OpenMode.ForRead);
            foreach (ObjectId id in espacioOrigen)
            {
                if (!camaraSaltada && trO.GetObject(id, OpenMode.ForRead) is Viewport)
                {
                    camaraSaltada = true;
                    continue;
                }
                ids.Add(id);
            }

            using (Transaction trD = db.TransactionManager.StartTransaction())
            {
                var nuevo = (Layout)trD.GetObject(nuevoId, OpenMode.ForWrite);
                var trazado = new PlotSettings(false);
                trazado.CopyFrom(layoutOrigen);
                nuevo.CopyFrom(trazado);
                espacioDestino = nuevo.BlockTableRecordId;
                trD.Commit();
            }
            trO.Commit();
        }

        if (ids.Count > 0)
        {
            var mapa = new IdMapping();
            origen.WblockCloneObjects(ids, espacioDestino, mapa, DuplicateRecordCloning.Ignore, false);
        }
        doc.Editor.Regen();
        Registro.Info($"Presentación \"{nombreOrigen}\" importada de {ruta} como \"{nombreDestino}\" ({ids.Count} objetos).");
    }

    /// <summary>
    /// Crea la presentación plantilla con un bloque de membrete insertado en (0,0,0).
    /// Si <paramref name="rutaDwg"/> no es nulo, el bloque se toma de ese archivo.
    /// </summary>
    public static void CrearConBloque(Document doc, string? rutaDwg, string nombreBloque, string nombreDestino)
    {
        Database db = doc.Database;
        ObjectId bloqueId;
        if (!string.IsNullOrWhiteSpace(rutaDwg))
        {
            using Database origen = LeerArchivo(rutaDwg);
            bloqueId = TraerBloque(db, origen, nombreBloque, rutaDwg);
        }
        else
        {
            using Transaction tr = db.TransactionManager.StartTransaction();
            var tabla = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (!tabla.Has(nombreBloque)) throw new ErrorUsuario($"No existe el bloque \"{nombreBloque}\" en este dibujo.");
            bloqueId = tabla[nombreBloque];
            tr.Commit();
        }

        PrepararDestino(nombreDestino);
        LayoutManager lm = LayoutManager.Current;
        ObjectId layoutId = lm.CreateLayout(nombreDestino);
        lm.CurrentLayout = nombreDestino;

        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            var layout = (Layout)tr.GetObject(layoutId, OpenMode.ForRead);
            var espacio = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForWrite);
            var definicion = (BlockTableRecord)tr.GetObject(bloqueId, OpenMode.ForRead);

            var referencia = new BlockReference(Point3d.Origin, bloqueId);
            espacio.AppendEntity(referencia);
            tr.AddNewlyCreatedDBObject(referencia, true);

            if (definicion.HasAttributeDefinitions)
            {
                foreach (ObjectId id in definicion)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not AttributeDefinition ad || ad.Constant) continue;
                    var ar = new AttributeReference();
                    ar.SetAttributeFromBlock(ad, referencia.BlockTransform);
                    ar.TextString = ad.TextString;
                    referencia.AttributeCollection.AppendAttribute(ar);
                    tr.AddNewlyCreatedDBObject(ar, true);
                }
            }
            tr.Commit();
        }
        doc.Editor.Regen();
        Registro.Info($"Presentación \"{nombreDestino}\" creada con el bloque {nombreBloque}.");
    }

    /// <summary>Pide en pantalla la polilínea rectangular del área útil dentro de la presentación plantilla.</summary>
    public static AreaUtil SeleccionarArea(Document doc, string presentacion)
    {
        LayoutManager lm = LayoutManager.Current;
        if (!lm.LayoutExists(presentacion)) throw new ErrorUsuario($"No existe la presentación \"{presentacion}\". Impórtala o créala primero.");
        lm.CurrentLayout = presentacion;
        Editor ed = doc.Editor;
        ed.SwitchToPaperSpace();

        var op = new PromptEntityOptions("\nSelecciona la polilínea rectangular del área útil: ");
        op.SetRejectMessage("\nDebe ser una polilínea.");
        op.AddAllowedClass(typeof(AcDb.Polyline), true);
        PromptEntityResult r = ed.GetEntity(op);
        if (r.Status != PromptStatus.OK) throw new OperacionCancelada("No se seleccionó el área útil.");

        using Transaction tr = doc.Database.TransactionManager.StartTransaction();
        var pl = (AcDb.Polyline)tr.GetObject(r.ObjectId, OpenMode.ForRead);
        var layout = (Layout)tr.GetObject(lm.GetLayoutId(presentacion), OpenMode.ForRead);
        if (pl.OwnerId != layout.BlockTableRecordId)
            throw new ErrorUsuario("La polilínea debe estar en el espacio papel de la presentación plantilla (no dentro de un viewport).");

        AreaUtil area = ValidarRectangulo(pl);
        tr.Commit();
        Registro.Info($"Área útil: {area.Ancho:0.###} × {area.Alto:0.###} desde ({area.MinX:0.###}, {area.MinY:0.###}).");
        return area;
    }

    /// <summary>Bloques con atributos dentro de la presentación (candidatos a membrete).</summary>
    public static List<BloqueMembrete> BloquesEnPresentacion(Database db, string presentacion)
    {
        var lista = new List<BloqueMembrete>();
        LayoutManager lm = LayoutManager.Current;
        if (!lm.LayoutExists(presentacion)) return lista;

        using Transaction tr = db.TransactionManager.StartTransaction();
        var layout = (Layout)tr.GetObject(lm.GetLayoutId(presentacion), OpenMode.ForRead);
        var espacio = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);
        foreach (ObjectId id in espacio)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not BlockReference br || br.AttributeCollection.Count == 0) continue;
            var etiquetas = new List<string>();
            foreach (ObjectId attId in br.AttributeCollection)
                etiquetas.Add(((AttributeReference)tr.GetObject(attId, OpenMode.ForRead)).Tag);
            lista.Add(new BloqueMembrete(LectorDibujo.NombreBloque(tr, br), etiquetas, br.Position));
        }
        tr.Commit();
        return lista.OrderByDescending(b => b.Etiquetas.Count).ToList();
    }

    /// <summary>Unidad del papel según la configuración de trazado de la presentación.</summary>
    public static UnidadLongitud? UnidadPapel(Database db, string presentacion)
    {
        LayoutManager lm = LayoutManager.Current;
        if (!lm.LayoutExists(presentacion)) return null;
        using Transaction tr = db.TransactionManager.StartTransaction();
        var layout = (Layout)tr.GetObject(lm.GetLayoutId(presentacion), OpenMode.ForRead);
        var unidad = UnidadesDibujo.DesdePapel(layout.PlotPaperUnits);
        tr.Commit();
        return unidad;
    }

    internal static AreaUtil ValidarRectangulo(AcDb.Polyline pl)
    {
        int n = pl.NumberOfVertices;
        var puntos = new List<Point2d>();
        for (int i = 0; i < n; i++)
        {
            if (Math.Abs(pl.GetBulgeAt(i)) > 1e-9) throw new ErrorUsuario("El área útil no puede tener arcos.");
            puntos.Add(pl.GetPoint2dAt(i));
        }
        if (n == 5 && puntos[0].GetDistanceTo(puntos[4]) < 1e-6) puntos.RemoveAt(4);
        if (puntos.Count != 4) throw new ErrorUsuario("El área útil debe ser un rectángulo de 4 vértices.");
        if (!pl.Closed && n != 5) throw new ErrorUsuario("La polilínea del área útil debe estar cerrada.");

        double minX = puntos.Min(p => p.X), maxX = puntos.Max(p => p.X);
        double minY = puntos.Min(p => p.Y), maxY = puntos.Max(p => p.Y);
        double tol = Math.Max(maxX - minX, maxY - minY) * 1e-6;
        var esquinas = new[] { new Point2d(minX, minY), new Point2d(maxX, minY), new Point2d(maxX, maxY), new Point2d(minX, maxY) };
        bool rectangulo = esquinas.All(e => puntos.Any(p => p.GetDistanceTo(e) <= tol)) && maxX - minX > tol && maxY - minY > tol;
        if (!rectangulo) throw new ErrorUsuario("El área útil debe ser un rectángulo alineado con los ejes (sin rotar).");
        return new AreaUtil(minX, minY, maxX, maxY);
    }

    private static Database LeerArchivo(string ruta)
    {
        if (!File.Exists(ruta)) throw new ErrorUsuario("No se encuentra el archivo: " + ruta);
        var db = new Database(false, true);
        try
        {
            db.ReadDwgFile(ruta, FileOpenMode.OpenForReadAndAllShare, true, "");
            db.CloseInput(true);
            return db;
        }
        catch
        {
            db.Dispose();
            throw;
        }
    }

    private static ObjectId TraerBloque(Database destino, Database origen, string nombreBloque, string ruta)
    {
        // Si el archivo tiene una definición con ese nombre, se copia esa; si no, el archivo entero se inserta como bloque.
        ObjectId origenId = ObjectId.Null;
        using (Transaction tr = origen.TransactionManager.StartTransaction())
        {
            var tabla = (BlockTable)tr.GetObject(origen.BlockTableId, OpenMode.ForRead);
            if (tabla.Has(nombreBloque)) origenId = tabla[nombreBloque];
            tr.Commit();
        }

        if (!origenId.IsNull)
        {
            var ids = new ObjectIdCollection();
            ids.Add(origenId);
            var mapa = new IdMapping();
            origen.WblockCloneObjects(ids, destino.BlockTableId, mapa, DuplicateRecordCloning.Replace, false);
            return mapa[origenId].Value;
        }

        string nombre = string.IsNullOrWhiteSpace(nombreBloque) ? Path.GetFileNameWithoutExtension(ruta) : nombreBloque;
        return destino.Insert(nombre, origen, true);
    }

    private static void PrepararDestino(string nombreDestino)
    {
        LayoutManager lm = LayoutManager.Current;
        if (!lm.LayoutExists(nombreDestino)) return;
        if (!Dialogos.Confirmar($"Ya existe la presentación \"{nombreDestino}\". ¿Reemplazarla?"))
            throw new OperacionCancelada();
        if (string.Equals(lm.CurrentLayout, nombreDestino, StringComparison.OrdinalIgnoreCase))
            lm.CurrentLayout = "Model";
        lm.DeleteLayout(nombreDestino);
    }
}
