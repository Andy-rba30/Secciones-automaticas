using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.ApplicationServices;
using C3D = Autodesk.Civil.DatabaseServices;
using C3DStyles = Autodesk.Civil.DatabaseServices.Styles;

namespace ArbaSecciones.Plugin.Civil;

/// <summary>Elemento de una lista desplegable: nombre visible e ObjectId.</summary>
internal sealed record ElementoDibujo(string Nombre, ObjectId Id)
{
    public override string ToString() => Nombre;
}

/// <summary>Listas que llenan la paleta. Solo lectura, descartando objetos borrados (IsErased).</summary>
internal static class LectorDibujo
{
    public static List<ElementoDibujo> Alineamientos(Document doc)
    {
        var lista = new List<ElementoDibujo>();
        CivilDocument civil = CivilDocument.GetCivilDocument(doc.Database);
        using Transaction tr = doc.Database.TransactionManager.StartTransaction();
        foreach (ObjectId id in civil.GetAlignmentIds())
        {
            if (id.IsNull || id.IsErased) continue;
            if (tr.GetObject(id, OpenMode.ForRead) is C3D.Alignment a && !a.IsErased)
                lista.Add(new ElementoDibujo(a.Name, id));
        }
        tr.Commit();
        return lista.OrderBy(e => e.Nombre, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static List<ElementoDibujo> GruposMuestreo(Document doc, ObjectId alineamientoId)
    {
        var lista = new List<ElementoDibujo>();
        if (alineamientoId.IsNull || alineamientoId.IsErased) return lista;
        using Transaction tr = doc.Database.TransactionManager.StartTransaction();
        if (tr.GetObject(alineamientoId, OpenMode.ForRead) is C3D.Alignment alin && !alin.IsErased)
        {
            foreach (ObjectId id in alin.GetSampleLineGroupIds())
            {
                if (id.IsNull || id.IsErased) continue;
                if (tr.GetObject(id, OpenMode.ForRead) is C3D.SampleLineGroup g && !g.IsErased)
                    lista.Add(new ElementoDibujo(g.Name, id));
            }
        }
        tr.Commit();
        return lista;
    }

    public static ObjectId BuscarGrupo(Transaction tr, ObjectId alineamientoId, string nombre)
    {
        if (alineamientoId.IsNull || alineamientoId.IsErased) return ObjectId.Null;
        if (tr.GetObject(alineamientoId, OpenMode.ForRead) is not C3D.Alignment alin || alin.IsErased) return ObjectId.Null;
        foreach (ObjectId id in alin.GetSampleLineGroupIds())
        {
            if (id.IsNull || id.IsErased) continue;
            if (tr.GetObject(id, OpenMode.ForRead) is C3D.SampleLineGroup g &&
                !g.IsErased &&
                string.Equals(g.Name, nombre, StringComparison.CurrentCultureIgnoreCase))
                return id;
        }
        return ObjectId.Null;
    }

    public static List<ElementoDibujo> EstilosVista(Document doc)
    {
        CivilDocument civil = CivilDocument.GetCivilDocument(doc.Database);
        return Estilos(doc.Database, civil.Styles.SectionViewStyles);
    }

    public static List<ElementoDibujo> ConjuntosBandas(Document doc)
    {
        CivilDocument civil = CivilDocument.GetCivilDocument(doc.Database);
        return Estilos(doc.Database, civil.Styles.SectionViewBandSetStyles);
    }

    public static List<ElementoDibujo> Estilos(Database db, System.Collections.IEnumerable coleccion)
    {
        var lista = new List<ElementoDibujo>();
        using Transaction tr = db.TransactionManager.StartTransaction();
        foreach (object o in coleccion)
        {
            if (o is ObjectId id && !id.IsNull && !id.IsErased &&
                tr.GetObject(id, OpenMode.ForRead) is C3DStyles.StyleBase estilo && !estilo.IsErased)
                lista.Add(new ElementoDibujo(estilo.Name, id));
        }
        tr.Commit();
        return lista.OrderBy(e => e.Nombre, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static ObjectId EstiloPorNombre(Document doc, string? nombre, bool bandas)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return ObjectId.Null;
        var lista = bandas ? ConjuntosBandas(doc) : EstilosVista(doc);
        return lista.FirstOrDefault(e => string.Equals(e.Nombre, nombre, StringComparison.CurrentCultureIgnoreCase))?.Id ?? ObjectId.Null;
    }

    /// <summary>Presentaciones del dibujo (sin Modelo), en el orden de las pestañas.</summary>
    public static List<string> Presentaciones(Database db)
    {
        var lista = new List<(string Nombre, int Orden)>();
        using Transaction tr = db.TransactionManager.StartTransaction();
        var dic = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
        foreach (DBDictionaryEntry entrada in dic)
        {
            if (entrada.Value.IsNull || entrada.Value.IsErased) continue;
            if (tr.GetObject(entrada.Value, OpenMode.ForRead) is Layout layout && !layout.IsErased && !layout.ModelType)
                lista.Add((layout.LayoutName, layout.TabOrder));
        }
        tr.Commit();
        return lista.OrderBy(l => l.Orden).Select(l => l.Nombre).ToList();
    }

    /// <summary>Definiciones de bloque con atributos (sin xrefs, anónimos ni espacios).</summary>
    public static List<string> BloquesConAtributos(Database db)
    {
        var lista = new List<string>();
        using Transaction tr = db.TransactionManager.StartTransaction();
        var tabla = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId id in tabla)
        {
            if (id.IsNull || id.IsErased) continue;
            var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (btr.IsErased || btr.IsLayout || btr.IsAnonymous || btr.IsFromExternalReference || btr.IsDependent) continue;
            if (btr.HasAttributeDefinitions) lista.Add(btr.Name);
        }
        tr.Commit();
        return lista.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Nombre real del bloque (para bloques dinámicos devuelve el de la definición, no el anónimo *U).</summary>
    public static string NombreBloque(Transaction tr, BlockReference br)
    {
        ObjectId id = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
        return ((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name;
    }
}
