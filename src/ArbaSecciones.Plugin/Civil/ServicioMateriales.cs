using ArbaSecciones.Core.Comun;
using ArbaSecciones.Core.Configuracion;
using ArbaSecciones.Core.Materiales;
using ArbaSecciones.Plugin.Infraestructura;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using C3D = Autodesk.Civil.DatabaseServices;

namespace ArbaSecciones.Plugin.Civil;

internal sealed record ResultadoMateriales(List<FilaVolumen> Filas, string Lista, int TablasEnVistas, List<string> Avisos);

/// <summary>
/// Lista de materiales (corte y relleno entre el terreno y la subrasante) en el grupo de líneas de muestreo, volúmenes
/// por áreas medias y tablas. Las áreas se calculan con los puntos de las secciones del terreno y de la subrasante
/// (Section.SectionPoints), el mismo método de áreas medias que usa Civil 3D.
/// </summary>
internal static class ServicioMateriales
{
    public const string MaterialCorte = "Corte";
    public const string MaterialRelleno = "Relleno";

    /// <summary>Progresivas de las líneas de muestreo del grupo, ordenadas.</summary>
    public static List<double> ProgresivasGrupo(Document doc, ObjectId grupoId)
    {
        var lista = new List<double>();
        if (grupoId.IsNull) return lista;
        using Transaction tr = doc.Database.TransactionManager.StartTransaction();
        var grupo = (C3D.SampleLineGroup)tr.GetObject(grupoId, OpenMode.ForRead);
        foreach (ObjectId id in grupo.GetSampleLineIds())
        {
            if (id.IsErased) continue;
            if (tr.GetObject(id, OpenMode.ForRead) is C3D.SampleLine sl) lista.Add(sl.Station);
        }
        tr.Commit();
        return lista.OrderBy(p => p).ToList();
    }

    /// <summary>
    /// Deja muestreadas en el grupo las superficies indicadas (y el corredor, si hay materiales de estructura).
    /// Devuelve, por cada id pedido, el id de la fuente con que el grupo la identifica.
    /// </summary>
    public static Dictionary<ObjectId, ObjectId> AsegurarMuestreo(Document doc, ObjectId grupoId, IEnumerable<ObjectId> ids, IReadOnlyDictionary<ObjectId, string> nombres)
    {
        var buscados = ids.Where(i => !i.IsNull).Distinct().ToList();
        var resultado = new Dictionary<ObjectId, ObjectId>();
        using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
        {
            // GetSectionSources() escribe en el grupo: se abre para escritura (lección 9).
            var grupo = (C3D.SampleLineGroup)tr.GetObject(grupoId, OpenMode.ForWrite);
            var fuentes = new List<C3D.SectionSource>();
            foreach (C3D.SectionSource f in grupo.GetSectionSources()) fuentes.Add(f);

            foreach (ObjectId id in buscados)
            {
                C3D.SectionSource? fuente = fuentes.FirstOrDefault(f => f.SourceId == id);
                string? nombre = nombres.GetValueOrDefault(id);
                if (fuente == null && !string.IsNullOrEmpty(nombre))
                {
                    // Las superficies de corredor pueden aparecer con su nombre, o con el del corredor delante.
                    var porNombre = fuentes.Select(f => (Fuente: f, Nombre: ServicioMuestreo.NombreFuente(tr, f.SourceId))).ToList();
                    fuente = porNombre.FirstOrDefault(f => string.Equals(f.Nombre, nombre, StringComparison.CurrentCultureIgnoreCase)).Fuente
                             ?? porNombre.FirstOrDefault(f => f.Nombre.EndsWith(nombre, StringComparison.CurrentCultureIgnoreCase)).Fuente;
                }
                if (fuente == null)
                {
                    string n = nombre ?? $"{id.Handle}";
                    throw new ErrorUsuario($"\"{n}\" no aparece entre las fuentes del grupo de líneas de muestreo. " +
                                           "Si es una superficie de corredor, reconstruye el corredor y vuelve a intentar.");
                }
                if (!fuente.IsSampled) fuente.IsSampled = true;
                resultado[id] = fuente.SourceId;
            }
            tr.Commit();
        }
        return resultado;
    }

    /// <summary>
    /// Crea (o reemplaza, preguntando) la lista de materiales: Corte = debajo del terreno y encima de la subrasante;
    /// Relleno = encima del terreno y debajo de la subrasante; con sus factores. Y un material de estructura por cada
    /// código de forma marcado.
    /// </summary>
    public static Guid CrearLista(Document doc, ObjectId grupoId, ObjectId terrenoFuente, ObjectId comparacionFuente,
        ObjectId corredorFuente, ConfigMateriales m, List<string> avisos)
    {
        string nombre = string.IsNullOrWhiteSpace(m.NombreLista) ? "Movimiento de tierras" : m.NombreLista.Trim();
        using Transaction tr = doc.Database.TransactionManager.StartTransaction();
        var grupo = (C3D.SampleLineGroup)tr.GetObject(grupoId, OpenMode.ForWrite);
        C3D.QTOMaterialListCollection listas = grupo.MaterialLists;

        C3D.QTOMaterialList? existente = null;
        foreach (C3D.QTOMaterialList l in listas)
            if (string.Equals(l.Name, nombre, StringComparison.CurrentCultureIgnoreCase)) existente = l;
        if (existente != null)
        {
            if (!Dialogos.Confirmar($"El grupo ya tiene la lista de materiales \"{nombre}\".\n\n¿Reemplazarla?"))
                throw new OperacionCancelada();
            listas.Remove(existente);
        }

        C3D.QTOMaterialList lista = listas.Add(nombre);

        C3D.QTOMaterial corte = lista.Add(MaterialCorte);
        corte.QuantityType = C3D.MaterialQuantityType.Cut;
        Factor(corte, C3D.MaterialFactorType.Cut, m.FactorEsponjamiento, avisos);
        corte.Add(terrenoFuente).Condition = C3D.MaterialConditionType.Below;
        corte.Add(comparacionFuente).Condition = C3D.MaterialConditionType.Above;

        C3D.QTOMaterial relleno = lista.Add(MaterialRelleno);
        relleno.QuantityType = C3D.MaterialQuantityType.Fill;
        Factor(relleno, C3D.MaterialFactorType.Fill, m.FactorCompactacion, avisos);
        relleno.Add(terrenoFuente).Condition = C3D.MaterialConditionType.Above;
        relleno.Add(comparacionFuente).Condition = C3D.MaterialConditionType.Below;

        foreach (string codigo in m.CodigosForma)
        {
            if (corredorFuente.IsNull) break;
            try
            {
                C3D.QTOMaterial estructura = lista.Add(codigo);
                estructura.QuantityType = C3D.MaterialQuantityType.Structure;
                estructura.Add(corredorFuente, codigo);
            }
            catch (System.Exception ex) when (ex is ArgumentException or Autodesk.AutoCAD.Runtime.Exception)
            {
                avisos.Add($"No se pudo agregar la estructura {codigo}: {ex.Message}");
            }
        }

        Guid guid = lista.Guid;
        tr.Commit();
        Registro.Info($"Lista de materiales \"{nombre}\" creada (corte ×{m.FactorEsponjamiento}, relleno ×{m.FactorCompactacion}, {m.CodigosForma.Count} estructuras).");
        return guid;
    }

    private static void Factor(C3D.QTOMaterial material, C3D.MaterialFactorType tipo, double valor, List<string> avisos)
    {
        try
        {
            material.SetFactor(tipo, valor);
        }
        catch (System.Exception ex) when (ex is ArgumentException or InvalidOperationException or Autodesk.AutoCAD.Runtime.Exception)
        {
            avisos.Add($"No se pudo fijar el factor {tipo} del material {material.Name}: {ex.Message}");
        }
    }

    /// <summary>Áreas de corte y relleno en cada línea de muestreo, con los puntos de las secciones de las dos superficies.</summary>
    public static List<AreasSeccion> Areas(Document doc, ObjectId grupoId, ObjectId terrenoFuente, ObjectId comparacionFuente, List<string> avisos)
    {
        var areas = new List<AreasSeccion>();
        using Transaction tr = doc.Database.TransactionManager.StartTransaction();
        var grupo = (C3D.SampleLineGroup)tr.GetObject(grupoId, OpenMode.ForRead);
        var lineas = new List<C3D.SampleLine>();
        foreach (ObjectId id in grupo.GetSampleLineIds())
            if (!id.IsErased && tr.GetObject(id, OpenMode.ForRead) is C3D.SampleLine sl) lineas.Add(sl);

        using (var progreso = new Progreso("Calculando áreas de corte y relleno…", lineas.Count))
        {
            foreach (C3D.SampleLine sl in lineas.OrderBy(l => l.Station))
            {
                progreso.Avanzar();
                List<PuntoSeccion>? terreno = PuntosSeccion(tr, sl, terrenoFuente);
                List<PuntoSeccion>? superficie = PuntosSeccion(tr, sl, comparacionFuente);
                if (terreno == null || superficie == null)
                {
                    avisos.Add($"{FormatoProgresiva.Formatear(sl.Station)}: falta la sección del {(terreno == null ? "terreno" : "corredor")}; no se calculó.");
                    continue;
                }
                areas.Add(CalculadorVolumenes.Areas(sl.Station, terreno, superficie));
            }
        }
        tr.Commit();
        return areas;
    }

    /// <summary>Puntos de la sección de una fuente en la línea de muestreo, sin los que quedan fuera de sus desplazamientos.</summary>
    private static List<PuntoSeccion>? PuntosSeccion(Transaction tr, C3D.SampleLine sl, ObjectId fuente)
    {
        ObjectId id;
        try
        {
            id = sl.GetSectionId(fuente);
        }
        catch (System.Exception ex) when (ex is ArgumentException or InvalidOperationException or Autodesk.AutoCAD.Runtime.Exception)
        {
            return null;
        }
        if (id.IsNull || id.IsErased || tr.GetObject(id, OpenMode.ForRead) is not C3D.Section seccion) return null;

        double izquierda = seccion.LeftOffset, derecha = seccion.RightOffset;
        var puntos = new List<PuntoSeccion>();
        foreach (C3D.SectionPoint p in seccion.SectionPoints)
        {
            Point3d l = p.Location; // X = desplazamiento, Y = elevación
            if (l.X < izquierda - 1e-6 || l.X > derecha + 1e-6) continue;
            puntos.Add(new PuntoSeccion(l.X, l.Y));
        }
        return puntos.Count >= 2 ? puntos.OrderBy(p => p.Desplazamiento).ToList() : null;
    }

    /// <summary>Agrega a cada vista de sección del grupo la tabla de volumen de la lista (las repetidas se saltan).</summary>
    public static int TablasEnVistas(Document doc, ObjectId grupoId, Guid lista, List<string> avisos)
    {
        int creadas = 0, repetidas = 0;
        using Transaction tr = doc.Database.TransactionManager.StartTransaction();
        var grupo = (C3D.SampleLineGroup)tr.GetObject(grupoId, OpenMode.ForRead);
        foreach (ObjectId lineaId in grupo.GetSampleLineIds())
        {
            if (lineaId.IsErased || tr.GetObject(lineaId, OpenMode.ForRead) is not C3D.SampleLine sl) continue;
            foreach (ObjectId vistaId in sl.GetSectionViewIds())
            {
                if (vistaId.IsErased || tr.GetObject(vistaId, OpenMode.ForWrite) is not C3D.SectionView vista) continue;
                try
                {
                    vista.VolumeTables.CreateVolumeTable(C3D.VolumeTableType.Material, lista);
                    creadas++;
                }
                catch (ArgumentException)
                {
                    repetidas++; // ya tenía esa tabla
                }
            }
        }
        tr.Commit();
        if (repetidas > 0) avisos.Add($"{repetidas} vistas ya tenían la tabla de volumen de esta lista.");
        return creadas;
    }

    /// <summary>Tabla de AutoCAD con los volúmenes, en el punto que se pida en pantalla (modelo).</summary>
    public static void InsertarTabla(Document doc, IReadOnlyList<FilaVolumen> filas, double altoTexto, string titulo)
    {
        if (filas.Count == 0) throw new ErrorUsuario("Calcula primero los materiales.");
        Editor ed = doc.Editor;
        PromptPointResult r = ed.GetPoint(new PromptPointOptions("\nPunto de inserción de la tabla de volúmenes (esquina superior izquierda): "));
        if (r.Status != PromptStatus.OK) throw new OperacionCancelada();

        Database db = doc.Database;
        using Transaction tr = db.TransactionManager.StartTransaction();
        var modelo = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        string[] encabezados = CalculadorVolumenes.Encabezados;
        var tabla = new Table
        {
            TableStyle = db.Tablestyle,
            Position = r.Value,
        };
        tabla.SetSize(filas.Count + 2, encabezados.Length);
        double alto = altoTexto > 0 ? altoTexto : 2.5;
        for (int c = 0; c < encabezados.Length; c++) tabla.Columns[c].Width = alto * (c == 0 ? 10 : 16);
        for (int f = 0; f < filas.Count + 2; f++) tabla.Rows[f].Height = alto * 2;

        Celda(tabla, 0, 0, titulo, alto);
        for (int c = 0; c < encabezados.Length; c++) Celda(tabla, 1, c, encabezados[c], alto);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        for (int i = 0; i < filas.Count; i++)
        {
            FilaVolumen f = filas[i];
            var valores = new[]
            {
                f.ProgresivaTexto,
                f.AreaCorte.ToString("0.00", inv), f.AreaRelleno.ToString("0.00", inv),
                f.VolumenCorte.ToString("0.00", inv), f.VolumenRelleno.ToString("0.00", inv),
                f.AcumuladoCorte.ToString("0.00", inv), f.AcumuladoRelleno.ToString("0.00", inv),
                f.Masa.ToString("0.00", inv),
            };
            for (int c = 0; c < valores.Length; c++) Celda(tabla, i + 2, c, valores[c], alto);
        }
        tabla.GenerateLayout();
        modelo.AppendEntity(tabla);
        tr.AddNewlyCreatedDBObject(tabla, true);
        tr.Commit();
        Registro.Info($"Tabla de volúmenes insertada ({filas.Count} filas).");
    }

    private static void Celda(Table t, int fila, int columna, string texto, double alto)
    {
        Cell celda = t.Cells[fila, columna];
        celda.TextString = texto;
        celda.TextHeight = alto;
        celda.Alignment = CellAlignment.MiddleCenter;
    }
}
