using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace ArbaSecciones.Plugin.Civil;

internal static class HerramientasViewport
{
    /// <summary>
    /// Viewports de un espacio papel en orden de enumeración. El primero es la cámara de la hoja y los demás
    /// son flotantes (lección 3: en una base de datos secundaria Viewport.Number vale 0 en todos).
    /// </summary>
    public static (ObjectId Camara, List<ObjectId> Flotantes) Viewports(Transaction tr, ObjectId espacioPapelId)
    {
        var espacio = (BlockTableRecord)tr.GetObject(espacioPapelId, OpenMode.ForRead);
        ObjectId camara = ObjectId.Null;
        var flotantes = new List<ObjectId>();
        foreach (ObjectId id in espacio)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not Viewport) continue;
            if (camara.IsNull) camara = id;
            else flotantes.Add(id);
        }
        return (camara, flotantes);
    }

    /// <summary>Primer viewport flotante cuyo centro cae dentro del rectángulo indicado (coordenadas de papel).</summary>
    public static ObjectId FlotanteDentroDe(Transaction tr, IEnumerable<ObjectId> flotantes, double xMin, double yMin, double xMax, double yMax)
    {
        foreach (ObjectId id in flotantes)
        {
            var vp = (Viewport)tr.GetObject(id, OpenMode.ForRead);
            Point3d c = vp.CenterPoint;
            if (c.X >= xMin && c.X <= xMax && c.Y >= yMin && c.Y <= yMax) return id;
        }
        return ObjectId.Null;
    }

    /// <summary>
    /// Configura el viewport en el orden que evita que salga en blanco (lección 2):
    /// desbloquear → dirección, giro y objetivo → centro, tamaño, vista y escala → sin rejilla y en capa no imprimible → bloquear.
    /// </summary>
    public static void Configurar(Viewport vp, double xMin, double yMin, double ancho, double alto, Point2d centroModelo,
        double factor, AnnotationScale? escalaAnotacion, string capa)
    {
        if (!(factor > 0)) throw new ArgumentOutOfRangeException(nameof(factor));

        vp.Locked = false;

        vp.ViewDirection = Vector3d.ZAxis;
        vp.TwistAngle = 0;
        vp.ViewTarget = Point3d.Origin;

        vp.CenterPoint = new Point3d(xMin + ancho / 2, yMin + alto / 2, 0);
        vp.Width = ancho;
        vp.Height = alto;
        vp.ViewCenter = centroModelo;
        vp.ViewHeight = alto / factor;
        vp.CustomScale = factor;
        if (escalaAnotacion != null) vp.AnnotationScale = escalaAnotacion;

        vp.GridOn = false;
        vp.Layer = capa;

        vp.Locked = true;
    }

    /// <summary>Crea la capa si no existe y la deja no imprimible.</summary>
    public static void AsegurarCapaNoImprimible(Database db, Transaction tr, string nombre)
    {
        var capas = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (capas.Has(nombre))
        {
            var existente = (LayerTableRecord)tr.GetObject(capas[nombre], OpenMode.ForRead);
            if (existente.IsPlottable && !string.Equals(nombre, "Defpoints", StringComparison.OrdinalIgnoreCase))
            {
                existente.UpgradeOpen();
                existente.IsPlottable = false;
            }
            return;
        }
        capas.UpgradeOpen();
        var capa = new LayerTableRecord { Name = nombre, IsPlottable = false };
        capas.Add(capa);
        tr.AddNewlyCreatedDBObject(capa, true);
    }
}
