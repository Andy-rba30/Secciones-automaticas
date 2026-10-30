using System.Windows;
using ArbaSecciones.Core.Comun;
using ArbaSecciones.Core.Progresivas;
using ArbaSecciones.Plugin.Infraestructura;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using C3D = Autodesk.Civil.DatabaseServices;

namespace ArbaSecciones.Plugin.Civil;

/// <summary>Fuente de muestreo de un grupo: superficie, superficie de obra lineal u obra lineal.</summary>
internal sealed record FuenteMuestreo(string Nombre, string Tipo, bool Muestreada);

internal sealed record ResultadoMuestreo(ObjectId GrupoId, int Creadas, int Omitidas, int Borradas);

/// <summary>Lectura de la geometría del alineamiento y creación de las líneas de muestreo.</summary>
internal static class ServicioMuestreo
{
    /// <summary>Elementos del alineamiento (tangentes, curvas, espirales) con sus progresivas.</summary>
    public static List<TramoAlineamiento> LeerTramos(Transaction tr, ObjectId alineamientoId)
    {
        var alin = (C3D.Alignment)tr.GetObject(alineamientoId, OpenMode.ForRead);
        var tramos = new List<TramoAlineamiento>();
        foreach (C3D.AlignmentEntity entidad in alin.Entities)
        {
            for (int i = 0; i < entidad.SubEntityCount; i++)
            {
                var sub = entidad[i];
                TipoTramo tipo = sub.SubEntityType.ToString() switch
                {
                    "Arc" => TipoTramo.Curva,
                    "Spiral" => TipoTramo.Espiral,
                    _ => TipoTramo.Tangente,
                };
                tramos.Add(new TramoAlineamiento(sub.StartStation, sub.EndStation, tipo));
            }
        }
        if (tramos.Count == 0)
            tramos.Add(new TramoAlineamiento(alin.StartingStation, alin.EndingStation, TipoTramo.Tangente));
        return tramos;
    }

    public static ResultadoProgresivas Calcular(Document doc, ObjectId alineamientoId, OpcionesMuestreo opciones)
    {
        if (alineamientoId.IsNull || alineamientoId.IsErased) throw new ErrorUsuario("Elige un alineamiento.");
        List<TramoAlineamiento> tramos;
        using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
        {
            tramos = LeerTramos(tr, alineamientoId);
            tr.Commit();
        }
        try
        {
            return GeneradorProgresivas.Generar(tramos, opciones);
        }
        catch (ArgumentException ex)
        {
            throw new ErrorUsuario(ex.Message);
        }
    }

    /// <summary>Fuentes disponibles del grupo. Si el grupo no existe y <paramref name="crearSiFalta"/>, lo crea.</summary>
    /// <remarks>
    /// GetSectionSources() actualiza la colección de fuentes y la escribe en el grupo antes de devolverla
    /// (así lo indica la referencia de la API de Civil 3D). Con el grupo abierto solo para lectura Civil 3D
    /// se cerraba con un error fatal: por eso el grupo se abre para escritura. Además, el grupo nuevo se crea
    /// y se confirma en su propia transacción antes de leer sus fuentes.
    /// </remarks>
    public static List<FuenteMuestreo> LeerFuentes(Document doc, ObjectId alineamientoId, string nombreGrupo, bool crearSiFalta)
    {
        var lista = new List<FuenteMuestreo>();
        if (alineamientoId.IsNull || alineamientoId.IsErased || string.IsNullOrWhiteSpace(nombreGrupo)) return lista;
        Database db = doc.Database;

        ObjectId grupoId;
        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            grupoId = LectorDibujo.BuscarGrupo(tr, alineamientoId, nombreGrupo);
            if (grupoId.IsNull && crearSiFalta)
            {
                grupoId = C3D.SampleLineGroup.Create(nombreGrupo.Trim(), alineamientoId);
                Registro.Info("Grupo de líneas de muestreo creado: " + nombreGrupo);
            }
            tr.Commit();
        }
        if (grupoId.IsNull) return lista;

        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            var grupo = (C3D.SampleLineGroup)tr.GetObject(grupoId, OpenMode.ForWrite);

            // Primero se copian los datos de la colección; los nombres se resuelven después,
            // para no abrir otros objetos mientras se recorre la colección nativa.
            var datos = new List<(ObjectId Id, string Tipo, bool Muestreada)>();
            try
            {
                foreach (C3D.SectionSource fuente in grupo.GetSectionSources())
                {
                    if (fuente.SourceId.IsNull || fuente.SourceId.IsErased) continue;
                    datos.Add((fuente.SourceId, fuente.SourceType.ToString(), fuente.IsSampled));
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                throw new ErrorUsuario($"Civil 3D no pudo leer las fuentes del grupo \"{nombreGrupo}\" ({ex.Message}).");
            }

            foreach (var (id, tipo, muestreada) in datos)
            {
                if (tr.GetObject(id, OpenMode.ForRead, openErased: true) is DBObject obj && obj.IsErased) continue;
                lista.Add(new FuenteMuestreo(NombreFuente(tr, id), tipo, muestreada));
            }
            tr.Commit();
        }
        return lista;
    }

    /// <summary>
    /// Crea las líneas de muestreo en una sola transacción: si algo falla, no queda ninguna a medias.
    /// Si el grupo ya tiene líneas, pregunta si reemplazarlas o agregar solo las que faltan.
    /// </summary>
    public static ResultadoMuestreo CrearLineas(Document doc, ObjectId alineamientoId, OpcionesMuestreo op, ResultadoProgresivas progresivas)
    {
        if (alineamientoId.IsNull || alineamientoId.IsErased) throw new ErrorUsuario("Elige un alineamiento.");
        if (string.IsNullOrWhiteSpace(op.NombreGrupo)) throw new ErrorUsuario("Escribe el nombre del grupo de líneas de muestreo.");
        if (progresivas.Progresivas.Count == 0) throw new ErrorUsuario("No hay progresivas para crear.");

        Database db = doc.Database;
        using Transaction tr = db.TransactionManager.StartTransaction();
        var alin = (C3D.Alignment)tr.GetObject(alineamientoId, OpenMode.ForRead);

        ObjectId grupoId = LectorDibujo.BuscarGrupo(tr, alineamientoId, op.NombreGrupo);
        if (grupoId.IsNull) grupoId = C3D.SampleLineGroup.Create(op.NombreGrupo.Trim(), alineamientoId);
        var grupo = (C3D.SampleLineGroup)tr.GetObject(grupoId, OpenMode.ForWrite);

        // Fuentes marcadas en la paleta. Si la lista está vacía se respeta lo que ya tiene el grupo.
        if (op.Fuentes.Count > 0)
        {
            var marcadas = new HashSet<string>(op.Fuentes, StringComparer.CurrentCultureIgnoreCase);
            foreach (C3D.SectionSource fuente in grupo.GetSectionSources())
            {
                if (fuente.SourceId.IsNull || fuente.SourceId.IsErased) continue;
                fuente.IsSampled = marcadas.Contains(NombreFuente(tr, fuente.SourceId));
            }
        }

        // Líneas existentes: reemplazar o agregar solo las que faltan.
        var existentes = new List<(ObjectId Id, double Progresiva)>();
        foreach (ObjectId id in grupo.GetSampleLineIds())
        {
            if (id.IsNull || id.IsErased) continue;
            if (tr.GetObject(id, OpenMode.ForRead) is C3D.SampleLine sl && !sl.IsErased) existentes.Add((id, sl.Station));
        }

        int borradas = 0;
        if (existentes.Count > 0)
        {
            MessageBoxResult r = Dialogos.Preguntar(
                $"El grupo \"{grupo.Name}\" ya tiene {existentes.Count} líneas de muestreo.\n\n" +
                "Sí: reemplazarlas todas (se borran también sus vistas de sección).\n" +
                "No: agregar solo las progresivas que faltan.\n" +
                "Cancelar: no hacer nada.");
            if (r == MessageBoxResult.Cancel) throw new OperacionCancelada();
            if (r == MessageBoxResult.Yes)
            {
                foreach (var (id, _) in existentes)
                {
                    tr.GetObject(id, OpenMode.ForWrite).Erase();
                    borradas++;
                }
                existentes.Clear();
            }
        }

        var nombres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ObjectId id in grupo.GetSampleLineIds())
        {
            // Las líneas recién borradas en esta misma transacción no se pueden abrir (eWasErased).
            if (id.IsNull || id.IsErased) continue;
            if (tr.GetObject(id, OpenMode.ForRead) is C3D.SampleLine sl && !sl.IsErased) nombres.Add(sl.Name);
        }

        int creadas = 0;
        int omitidas = 0;
        double inicio = alin.StartingStation;
        double fin = alin.EndingStation;
        using (var progreso = new Progreso("Creando líneas de muestreo…", progresivas.Progresivas.Count))
        {
            foreach (ProgresivaMuestreo p in progresivas.Progresivas)
            {
                progreso.Avanzar();
                if (existentes.Any(e => Math.Abs(e.Progresiva - p.Progresiva) <= op.Tolerancia))
                {
                    omitidas++;
                    continue;
                }

                double s = Math.Clamp(p.Progresiva, inicio, fin);
                double xi = 0, yi = 0, xd = 0, yd = 0;
                alin.PointLocation(s, -op.AnchoIzquierdo, ref xi, ref yi);
                alin.PointLocation(s, op.AnchoDerecho, ref xd, ref yd);
                var puntos = new Point2dCollection();
                puntos.Add(new Point2d(xi, yi));
                puntos.Add(new Point2d(xd, yd));

                string nombre = NombreUnico(nombres, FormatoProgresiva.Formatear(p.Progresiva));
                C3D.SampleLine.Create(nombre, grupoId, puntos);
                nombres.Add(nombre);
                creadas++;
            }
        }

        tr.Commit();
        Registro.Info($"Líneas de muestreo: {creadas} creadas, {omitidas} ya existían, {borradas} borradas. Grupo {grupo.Name}.");
        return new ResultadoMuestreo(grupoId, creadas, omitidas, borradas);
    }

    public static string NombreFuente(Transaction tr, ObjectId id)
    {
        try
        {
            if (!id.IsNull && !id.IsErased && tr.GetObject(id, OpenMode.ForRead) is C3D.Entity e && !e.IsErased) return e.Name;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            // Fuente que no se puede abrir: se identifica por su handle.
        }
        return id.IsNull ? "(sin nombre)" : id.Handle.ToString();
    }

    private static string NombreUnico(HashSet<string> usados, string baseNombre)
    {
        if (!usados.Contains(baseNombre)) return baseNombre;
        for (int i = 2; ; i++)
        {
            string candidato = baseNombre + " (" + i + ")";
            if (!usados.Contains(candidato)) return candidato;
        }
    }
}
