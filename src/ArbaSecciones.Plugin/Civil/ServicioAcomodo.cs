using ArbaSecciones.Core.Acomodo;
using ArbaSecciones.Core.Comun;
using ArbaSecciones.Core.Escalas;
using ArbaSecciones.Plugin.Infraestructura;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcDb = Autodesk.AutoCAD.DatabaseServices;

namespace ArbaSecciones.Plugin.Civil;

/// <summary>Marco de una lámina guardado en el modelo, con sus datos en XData.</summary>
internal sealed record MarcoLamina(ObjectId Id, InfoLamina Info, int Total, string Escala, double Factor, Extents3d Extension);

/// <summary>
/// Mueve las vistas a su lugar en el modelo y dibuja un marco no imprimible por lámina.
/// Los marcos guardan los datos de la lámina, así las presentaciones se pueden crear en otra sesión.
/// </summary>
internal static class ServicioAcomodo
{
    public const string CapaMarcos = "ARBA_SEC_MARCOS";
    public const string Aplicacion = "ARBA_SECCIONES";
    private const string TipoMarco = "MARCO";

    /// <summary>Altura de las etiquetas de los marcos, en unidades de papel.</summary>
    private const double AltoTextoPapel = 5.0;

    public static void Aplicar(Document doc, ResultadoAcomodo resultado, Medicion medicion, Point3d puntoBase, double separacionLaminasPapel)
    {
        if (!resultado.Valido) throw new ErrorUsuario(resultado.Resumen());

        Database db = doc.Database;
        ConversorEscala c = resultado.Conversor;
        ParametrosAcomodo p = resultado.Parametros;
        double anchoModelo = c.PapelAModelo(p.AnchoUtil);
        double altoModelo = c.PapelAModelo(p.AltoUtil);
        double separacionModelo = c.PapelAModelo(separacionLaminasPapel);
        var vistas = medicion.Vistas.ToDictionary(v => v.Medida.Id);

        using Transaction tr = db.TransactionManager.StartTransaction();
        AsegurarCapa(db, tr);
        AsegurarAplicacion(db, tr);
        int borrados = BorrarMarcos(db, tr);

        var modelo = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        int total = resultado.Laminas.Count;
        int movidas = 0;

        using (var progreso = new Progreso("Acomodando vistas en láminas…", resultado.TotalSecciones))
        {
            for (int k = 0; k < total; k++)
            {
                Lamina lamina = resultado.Laminas[k];
                var (ox, oy) = MarcosModelo.Origen(k, anchoModelo, altoModelo, separacionModelo);
                var origen = new Point3d(puntoBase.X + ox, puntoBase.Y + oy, 0);
                var info = InfoLamina.Desde(lamina);

                Agregar(modelo, tr, Rectangulo(origen.X, origen.Y, anchoModelo, altoModelo,
                    DatosMarco(info, total, c.Escala.ToString(), c.FactorH)));

                var franja = p.ZonaCuadro;
                if (franja.HasValue)
                {
                    Agregar(modelo, tr, Rectangulo(origen.X + c.PapelAModelo(franja.Value.X), origen.Y + c.PapelAModelo(franja.Value.Y),
                        c.PapelAModelo(franja.Value.Ancho), c.PapelAModelo(franja.Value.Alto), Etiqueta("FRANJA")));
                }

                string grupo = lamina.Grupo.HasValue ? $" · km {lamina.Grupo}" + (lamina.PartesDelGrupo > 1 ? $" ({lamina.Parte}/{lamina.PartesDelGrupo})" : "") : "";
                var texto = new DBText
                {
                    Position = new Point3d(origen.X, origen.Y + altoModelo + c.PapelAModelo(2), 0),
                    Height = c.PapelAModelo(AltoTextoPapel),
                    TextString = $"LÁMINA {lamina.Numero:00}{grupo} · {FormatoProgresiva.Formatear(lamina.ProgresivaInicial)} – {FormatoProgresiva.Formatear(lamina.ProgresivaFinal)} · {c.Escala}",
                    Layer = CapaMarcos,
                    XData = Etiqueta("ETIQUETA"),
                };
                Agregar(modelo, tr, texto);

                foreach (Colocacion col in lamina.Colocaciones)
                {
                    progreso.Avanzar();
                    if (!vistas.TryGetValue(col.Seccion.Id, out VistaMedida? vm))
                        throw new InvalidOperationException("No se encontró la vista de la progresiva " + FormatoProgresiva.Formatear(col.Seccion.Progresiva) + ".");

                    var vista = (AcDb.Entity)tr.GetObject(vm.VistaId, OpenMode.ForWrite);
                    Extents3d ext = vista.GeometricExtents;
                    double dx = origen.X + c.PapelAModelo(col.X) - ext.MinPoint.X;
                    double dy = origen.Y + c.PapelAModelo(col.Y) - ext.MinPoint.Y;
                    if (Math.Abs(dx) > 1e-9 || Math.Abs(dy) > 1e-9)
                        vista.TransformBy(Matrix3d.Displacement(new Vector3d(dx, dy, 0)));
                    movidas++;
                }
            }
        }

        tr.Commit();
        Registro.Info($"Acomodo: {movidas} vistas en {total} láminas a {c.Escala}; {borrados} marcos anteriores reemplazados.");
    }

    /// <summary>Marcos de lámina del modelo, ordenados por número.</summary>
    public static List<MarcoLamina> LeerMarcos(Database db)
    {
        var lista = new List<MarcoLamina>();
        using Transaction tr = db.TransactionManager.StartTransaction();
        var modelo = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        foreach (ObjectId id in modelo)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not AcDb.Polyline pl) continue;
            ResultBuffer? rb = pl.GetXDataForApplication(Aplicacion);
            if (rb == null) continue;
            TypedValue[] v = rb.AsArray();
            rb.Dispose();
            if (v.Length < 12 || v[1].Value as string != TipoMarco) continue;

            int grupo = Convert.ToInt32(v[3].Value);
            var info = new InfoLamina(
                Convert.ToInt32(v[2].Value),
                grupo == int.MinValue ? null : grupo,
                Convert.ToInt32(v[4].Value),
                Convert.ToInt32(v[5].Value),
                Convert.ToDouble(v[6].Value),
                Convert.ToDouble(v[7].Value),
                Convert.ToInt32(v[8].Value));
            lista.Add(new MarcoLamina(id, info, Convert.ToInt32(v[9].Value), v[10].Value as string ?? "", Convert.ToDouble(v[11].Value), pl.GeometricExtents));
        }
        tr.Commit();
        return lista.OrderBy(m => m.Info.Numero).ToList();
    }

    /// <summary>Pide el punto base del acomodo (esquina inferior izquierda de la primera lámina).</summary>
    public static Point3d? PedirPuntoBase(Document doc)
    {
        var op = new PromptPointOptions("\nPunto base del acomodo (esquina inferior izquierda de la primera lámina): ")
        {
            AllowNone = false,
        };
        PromptPointResult r = doc.Editor.GetPoint(op);
        return r.Status == PromptStatus.OK ? r.Value : null;
    }

    private static int BorrarMarcos(Database db, Transaction tr)
    {
        var modelo = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        var nuestros = new List<AcDb.Entity>();
        foreach (ObjectId id in modelo)
        {
            var ent = (AcDb.Entity)tr.GetObject(id, OpenMode.ForRead);
            ResultBuffer? rb = ent.GetXDataForApplication(Aplicacion);
            if (rb == null) continue;
            rb.Dispose();
            nuestros.Add(ent);
        }

        int marcos = 0;
        foreach (AcDb.Entity ent in nuestros)
        {
            ent.UpgradeOpen();
            ent.Erase();
            if (ent is AcDb.Polyline) marcos++;
        }
        return marcos;
    }

    private static AcDb.Polyline Rectangulo(double x, double y, double ancho, double alto, ResultBuffer xdata)
    {
        var pl = new AcDb.Polyline();
        pl.AddVertexAt(0, new Point2d(x, y), 0, 0, 0);
        pl.AddVertexAt(1, new Point2d(x + ancho, y), 0, 0, 0);
        pl.AddVertexAt(2, new Point2d(x + ancho, y + alto), 0, 0, 0);
        pl.AddVertexAt(3, new Point2d(x, y + alto), 0, 0, 0);
        pl.Closed = true;
        pl.Layer = CapaMarcos;
        pl.XData = xdata;
        return pl;
    }

    private static void Agregar(BlockTableRecord espacio, Transaction tr, AcDb.Entity ent)
    {
        espacio.AppendEntity(ent);
        tr.AddNewlyCreatedDBObject(ent, true);
    }

    private static ResultBuffer Etiqueta(string tipo) => new(
        new TypedValue((int)DxfCode.ExtendedDataRegAppName, Aplicacion),
        new TypedValue((int)DxfCode.ExtendedDataAsciiString, tipo));

    private static ResultBuffer DatosMarco(InfoLamina info, int total, string escala, double factor) => new(
        new TypedValue((int)DxfCode.ExtendedDataRegAppName, Aplicacion),
        new TypedValue((int)DxfCode.ExtendedDataAsciiString, TipoMarco),
        new TypedValue((int)DxfCode.ExtendedDataInteger32, info.Numero),
        new TypedValue((int)DxfCode.ExtendedDataInteger32, info.Grupo ?? int.MinValue),
        new TypedValue((int)DxfCode.ExtendedDataInteger32, info.Parte),
        new TypedValue((int)DxfCode.ExtendedDataInteger32, info.PartesDelGrupo),
        new TypedValue((int)DxfCode.ExtendedDataReal, info.ProgresivaInicial),
        new TypedValue((int)DxfCode.ExtendedDataReal, info.ProgresivaFinal),
        new TypedValue((int)DxfCode.ExtendedDataInteger32, info.Cantidad),
        new TypedValue((int)DxfCode.ExtendedDataInteger32, total),
        new TypedValue((int)DxfCode.ExtendedDataAsciiString, escala),
        new TypedValue((int)DxfCode.ExtendedDataReal, factor));

    public static void AsegurarCapa(Database db, Transaction tr)
    {
        var capas = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (capas.Has(CapaMarcos)) return;
        capas.UpgradeOpen();
        var capa = new LayerTableRecord
        {
            Name = CapaMarcos,
            Color = Color.FromColorIndex(ColorMethod.ByAci, 8),
            IsPlottable = false,
        };
        capas.Add(capa);
        tr.AddNewlyCreatedDBObject(capa, true);
    }

    private static void AsegurarAplicacion(Database db, Transaction tr)
    {
        var apps = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
        if (apps.Has(Aplicacion)) return;
        apps.UpgradeOpen();
        var app = new RegAppTableRecord { Name = Aplicacion };
        apps.Add(app);
        tr.AddNewlyCreatedDBObject(app, true);
    }
}
