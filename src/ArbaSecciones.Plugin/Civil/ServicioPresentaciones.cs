using System.Globalization;
using ArbaSecciones.Core.Comun;
using ArbaSecciones.Core.Configuracion;
using ArbaSecciones.Plugin.Infraestructura;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace ArbaSecciones.Plugin.Civil;

/// <summary>Una presentación por lámina, copiada de la plantilla, con su viewport y el membrete lleno.</summary>
internal static class ServicioPresentaciones
{
    public static List<string> Crear(Document doc, ConfiguracionArba cfg)
    {
        Database db = doc.Database;
        ConfigMembrete m = cfg.Membrete;
        LayoutManager lm = LayoutManager.Current;

        List<MarcoLamina> marcos = ServicioAcomodo.LeerMarcos(db);
        if (marcos.Count == 0) throw new ErrorUsuario("No hay láminas acomodadas en el modelo. Usa primero la pestaña Acomodo.");
        if (!m.AreaDefinida) throw new ErrorUsuario("Falta el área útil del membrete (pestaña Membrete).");
        if (!lm.LayoutExists(m.LayoutPlantilla)) throw new ErrorUsuario($"No existe la presentación plantilla \"{m.LayoutPlantilla}\".");
        ValidarMarcos(marcos, m);

        double factor = marcos[0].Factor;
        var nombres = marcos
            .Select(mk => DatosLamina.NombrePresentacion(cfg.Presentaciones, cfg.Presentaciones.CorrelativoInicial + mk.Info.Numero - 1))
            .ToList();
        if (nombres.Any(n => string.Equals(n, m.LayoutPlantilla, StringComparison.OrdinalIgnoreCase)))
            throw new ErrorUsuario("El prefijo de las láminas coincide con el nombre de la presentación plantilla. Cambia el prefijo.");

        BorrarAnteriores(db, cfg, nombres);

        AnnotationScale escalaAnotacion = ServicioEscala.ObtenerOCrearPorRelacion(db, 1.0 / factor, marcos[0].Escala);
        string capa = string.IsNullOrWhiteSpace(cfg.Presentaciones.CapaViewport) ? "Defpoints" : cfg.Presentaciones.CapaViewport.Trim();

        var creadas = new List<string>();
        try
        {
            int orden = SiguienteOrden(db);
            using var progreso = new Progreso("Creando presentaciones…", marcos.Count);
            for (int i = 0; i < marcos.Count; i++)
            {
                progreso.Avanzar();
                MarcoLamina marco = marcos[i];
                string nombre = nombres[i];

                lm.CloneLayout(m.LayoutPlantilla, nombre, orden++);
                creadas.Add(nombre);
                lm.CurrentLayout = nombre;

                using Transaction tr = db.TransactionManager.StartTransaction();
                var layout = (Layout)tr.GetObject(lm.GetLayoutId(nombre), OpenMode.ForRead);
                HerramientasViewport.AsegurarCapaNoImprimible(db, tr, capa);

                var (_, flotantes) = HerramientasViewport.Viewports(tr, layout.BlockTableRecordId);
                ObjectId vpId = HerramientasViewport.FlotanteDentroDe(tr, flotantes, m.AreaMinX, m.AreaMinY, m.AreaMaxX, m.AreaMaxY);
                bool nuevo = vpId.IsNull;
                Viewport vp;
                if (nuevo)
                {
                    vp = new Viewport();
                    var espacio = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForWrite);
                    espacio.AppendEntity(vp);
                    tr.AddNewlyCreatedDBObject(vp, true);
                }
                else
                {
                    vp = (Viewport)tr.GetObject(vpId, OpenMode.ForWrite);
                }

                Extents3d ext = marco.Extension;
                var centroModelo = new Point2d((ext.MinPoint.X + ext.MaxPoint.X) / 2, (ext.MinPoint.Y + ext.MaxPoint.Y) / 2);
                HerramientasViewport.Configurar(vp, m.AreaMinX, m.AreaMinY, m.AnchoUtil, m.AltoUtil, centroModelo, factor, escalaAnotacion, capa);
                if (nuevo) vp.On = true;

                var valores = DatosLamina.Valores(marco.Info, marco.Total, cfg.Presentaciones, marco.Escala, cfg.Acomodo.LongitudTramo);
                int llenos = LlenarAtributos(tr, layout.BlockTableRecordId, m, valores);
                if (llenos == 0 && i == 0)
                    Registro.Aviso("No se llenó ningún atributo: revisa el nombre del bloque del membrete y las etiquetas en la pestaña Membrete.");

                tr.Commit();
            }
        }
        catch
        {
            // Si algo falla a mitad, se quitan las presentaciones de esta corrida para no dejar el dibujo a medias.
            try
            {
                lm.CurrentLayout = m.LayoutPlantilla;
                foreach (string n in creadas)
                    if (lm.LayoutExists(n)) lm.DeleteLayout(n);
            }
            catch (System.Exception ex)
            {
                Registro.Error("No se pudieron quitar las presentaciones incompletas.", ex);
            }
            throw;
        }

        lm.CurrentLayout = creadas[0];
        doc.Editor.Regen();
        Registro.Info($"Presentaciones creadas: {creadas.Count} ({creadas[0]} … {creadas[^1]}).");
        return creadas;
    }

    /// <summary>Presentaciones de corridas anteriores con el mismo prefijo, para reemplazarlas sin duplicar.</summary>
    public static List<string> Anteriores(Database db, ConfiguracionArba cfg)
    {
        string prefijo = cfg.Presentaciones.Prefijo ?? string.Empty;
        return LectorDibujo.Presentaciones(db)
            .Where(n => !string.Equals(n, cfg.Membrete.LayoutPlantilla, StringComparison.OrdinalIgnoreCase))
            .Where(n => n.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase) && n.Length > prefijo.Length && n[prefijo.Length..].All(char.IsDigit))
            .ToList();
    }

    private static void BorrarAnteriores(Database db, ConfiguracionArba cfg, List<string> nuevas)
    {
        LayoutManager lm = LayoutManager.Current;
        var aBorrar = Anteriores(db, cfg)
            .Union(nuevas.Where(lm.LayoutExists), StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (aBorrar.Count == 0) return;

        string lista = string.Join(", ", aBorrar.Take(8)) + (aBorrar.Count > 8 ? ", …" : "");
        if (!Dialogos.Confirmar($"Ya existen {aBorrar.Count} presentaciones de una corrida anterior ({lista}).\n\n¿Reemplazarlas?"))
            throw new OperacionCancelada();

        lm.CurrentLayout = cfg.Membrete.LayoutPlantilla;
        foreach (string n in aBorrar) lm.DeleteLayout(n);
        Registro.Info($"Presentaciones anteriores borradas: {aBorrar.Count}.");
    }

    private static void ValidarMarcos(List<MarcoLamina> marcos, ConfigMembrete m)
    {
        double factor = marcos[0].Factor;
        if (marcos.Any(x => Math.Abs(x.Factor - factor) > 1e-9 * Math.Max(1, factor)))
            throw new ErrorUsuario("Los marcos del modelo tienen escalas distintas. Vuelve a acomodar.");

        double anchoEsperado = m.AnchoUtil / factor;
        double altoEsperado = m.AltoUtil / factor;
        Extents3d e = marcos[0].Extension;
        double ancho = e.MaxPoint.X - e.MinPoint.X;
        double alto = e.MaxPoint.Y - e.MinPoint.Y;
        if (Math.Abs(ancho - anchoEsperado) > anchoEsperado * 0.005 || Math.Abs(alto - altoEsperado) > altoEsperado * 0.005)
        {
            var inv = CultureInfo.InvariantCulture;
            throw new ErrorUsuario(
                $"El área útil cambió desde el último acomodo (marco de {ancho.ToString("0.##", inv)} × {alto.ToString("0.##", inv)}, " +
                $"se esperaba {anchoEsperado.ToString("0.##", inv)} × {altoEsperado.ToString("0.##", inv)}). Vuelve a acomodar.");
        }
    }

    private static int SiguienteOrden(Database db)
    {
        int maximo = 0;
        using Transaction tr = db.TransactionManager.StartTransaction();
        var dic = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
        foreach (DBDictionaryEntry entrada in dic)
            maximo = Math.Max(maximo, ((Layout)tr.GetObject(entrada.Value, OpenMode.ForRead)).TabOrder);
        tr.Commit();
        return maximo + 1;
    }

    /// <summary>Escribe los atributos del membrete según el mapeo de la configuración. Devuelve cuántos llenó.</summary>
    internal static int LlenarAtributos(Transaction tr, ObjectId espacioId, ConfigMembrete m, IReadOnlyDictionary<string, object?> valores)
    {
        var mapeo = m.Atributos
            .Where(a => !string.IsNullOrWhiteSpace(a.Etiqueta))
            .GroupBy(a => a.Etiqueta.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().Plantilla, StringComparer.OrdinalIgnoreCase);
        if (mapeo.Count == 0) return 0;

        int llenos = 0;
        var espacio = (BlockTableRecord)tr.GetObject(espacioId, OpenMode.ForRead);
        foreach (ObjectId id in espacio)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not BlockReference br || br.AttributeCollection.Count == 0) continue;
            if (!string.IsNullOrWhiteSpace(m.NombreBloque) &&
                !string.Equals(LectorDibujo.NombreBloque(tr, br), m.NombreBloque, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (ObjectId attId in br.AttributeCollection)
            {
                var att = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                if (!mapeo.TryGetValue(att.Tag, out string? plantilla)) continue;
                att.UpgradeOpen();
                att.TextString = PlantillaTexto.Aplicar(plantilla, valores);
                att.AdjustAlignment(br.Database);
                llenos++;
            }
        }
        return llenos;
    }
}
