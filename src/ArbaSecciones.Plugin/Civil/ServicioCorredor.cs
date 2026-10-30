using ArbaSecciones.Core.Comun;
using ArbaSecciones.Core.Configuracion;
using ArbaSecciones.Core.Materiales;
using ArbaSecciones.Plugin.Infraestructura;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.ApplicationServices;
using C3D = Autodesk.Civil.DatabaseServices;

namespace ArbaSecciones.Plugin.Civil;

/// <summary>Región de una línea base del corredor.</summary>
internal sealed record RegionCorredor(string Nombre, double Inicio, double Fin);

/// <summary>Sección leída del corredor: la progresiva pedida y la del ensamblaje aplicado más cercano.</summary>
internal sealed record LecturaSeccion(SeccionCorredor Seccion, double ProgresivaPedida, string Region)
{
    public double ProgresivaReal => Seccion.Progresiva;
}

/// <summary>Configuración real de una superficie del corredor.</summary>
internal sealed record SuperficieCorredor(string Nombre, ObjectId SurfaceId, IReadOnlyList<string> Codigos, CorreccionVoladizo Voladizo);

/// <summary>
/// Lectura de corredores (líneas base, regiones, ensamblajes aplicados, vínculos y formas) y creación de sus superficies.
/// Miembros de la API comprobados en la referencia de Civil 3D 2027 (AeccDbMgd 13.9): ver docs/DISENO.md.
/// </summary>
internal static class ServicioCorredor
{
    public const string NombreContorno = "Extensión del corredor";

    public static List<ElementoDibujo> Corredores(Document doc)
    {
        var lista = new List<ElementoDibujo>();
        CivilDocument civil = CivilDocument.GetCivilDocument(doc.Database);
        using Transaction tr = doc.Database.TransactionManager.StartTransaction();
        foreach (ObjectId id in civil.CorridorCollection)
        {
            if (id.IsNull || id.IsErased) continue;
            if (tr.GetObject(id, OpenMode.ForRead) is C3D.Corridor c && !c.IsErased)
                lista.Add(new ElementoDibujo(c.Name, id));
        }
        tr.Commit();
        return lista.OrderBy(e => e.Nombre, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static List<string> LineasBase(Document doc, ObjectId corredorId)
    {
        var lista = new List<string>();
        if (corredorId.IsNull || corredorId.IsErased) return lista;
        using Transaction tr = doc.Database.TransactionManager.StartTransaction();
        if (tr.GetObject(corredorId, OpenMode.ForRead) is C3D.Corridor corredor && !corredor.IsErased)
        {
            foreach (C3D.Baseline b in corredor.Baselines) lista.Add(b.Name);
        }
        tr.Commit();
        return lista;
    }

    /// <summary>Superficies del dibujo (TIN, rejilla, de corredor…) para elegir el terreno o la comparación.</summary>
    public static List<ElementoDibujo> Superficies(Document doc)
    {
        var lista = new List<ElementoDibujo>();
        CivilDocument civil = CivilDocument.GetCivilDocument(doc.Database);
        using Transaction tr = doc.Database.TransactionManager.StartTransaction();
        foreach (ObjectId id in civil.GetSurfaceIds())
        {
            if (id.IsNull || id.IsErased) continue;
            if (tr.GetObject(id, OpenMode.ForRead) is C3D.Surface s && !s.IsErased)
                lista.Add(new ElementoDibujo(s.Name, id));
        }
        tr.Commit();
        return lista.OrderBy(e => e.Nombre, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static List<ElementoDibujo> EstilosSuperficie(Document doc)
    {
        CivilDocument civil = CivilDocument.GetCivilDocument(doc.Database);
        var lista = new List<ElementoDibujo>();
        using Transaction tr = doc.Database.TransactionManager.StartTransaction();
        foreach (ObjectId id in civil.Styles.SurfaceStyles)
        {
            if (id.IsNull || id.IsErased) continue;
            if (tr.GetObject(id, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Styles.StyleBase e && !e.IsErased)
                lista.Add(new ElementoDibujo(e.Name, id));
        }
        tr.Commit();
        return lista.OrderBy(e => e.Nombre, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static C3D.Baseline BuscarLineaBase(C3D.Corridor corredor, string? nombre)
    {
        C3D.Baseline? primera = null;
        foreach (C3D.Baseline b in corredor.Baselines)
        {
            primera ??= b;
            if (!string.IsNullOrWhiteSpace(nombre) && string.Equals(b.Name, nombre, StringComparison.CurrentCultureIgnoreCase)) return b;
        }
        return primera ?? throw new ErrorUsuario($"El corredor {corredor.Name} no tiene líneas base.");
    }

    public static List<RegionCorredor> Regiones(Document doc, ObjectId corredorId, string? lineaBase)
    {
        var lista = new List<RegionCorredor>();
        if (corredorId.IsNull || corredorId.IsErased) return lista;
        using Transaction tr = doc.Database.TransactionManager.StartTransaction();
        var corredor = (C3D.Corridor)tr.GetObject(corredorId, OpenMode.ForRead);
        C3D.Baseline b = BuscarLineaBase(corredor, lineaBase);
        foreach (C3D.BaselineRegion r in b.BaselineRegions) lista.Add(new RegionCorredor(r.Name, r.StartStation, r.EndStation));
        tr.Commit();
        return lista.OrderBy(r => r.Inicio).ToList();
    }

    /// <summary>Progresivas donde el corredor tiene un ensamblaje aplicado (las de su frecuencia), ordenadas.</summary>
    public static List<double> ProgresivasAplicadas(Document doc, ObjectId corredorId, string? lineaBase)
    {
        var lista = new List<double>();
        if (corredorId.IsNull || corredorId.IsErased) return lista;
        using Transaction tr = doc.Database.TransactionManager.StartTransaction();
        var corredor = (C3D.Corridor)tr.GetObject(corredorId, OpenMode.ForRead);
        C3D.Baseline b = BuscarLineaBase(corredor, lineaBase);
        // SortedStations() no funciona en corredores por referencia (DREF): se usan las de cada región.
        foreach (C3D.BaselineRegion r in b.BaselineRegions) lista.AddRange(r.AppliedAssemblies.Stations());
        tr.Commit();
        return lista.Distinct().OrderBy(p => p).ToList();
    }

    /// <summary>
    /// Vínculos y formas del ensamblaje aplicado más cercano a la progresiva pedida. Desplazamiento respecto a la línea
    /// base (StationOffsetElevationToBaseline.Y) y elevación absoluta (XYZ.Z).
    /// </summary>
    public static LecturaSeccion LeerSeccion(Document doc, ObjectId corredorId, string? lineaBase, double progresiva)
    {
        if (corredorId.IsNull || corredorId.IsErased) throw new ErrorUsuario("Elige un corredor válido.");
        using Transaction tr = doc.Database.TransactionManager.StartTransaction();
        var corredor = (C3D.Corridor)tr.GetObject(corredorId, OpenMode.ForRead);
        C3D.Baseline b = BuscarLineaBase(corredor, lineaBase);

        C3D.BaselineRegion? region = null;
        double mejorDistancia = double.MaxValue, real = progresiva;
        foreach (C3D.BaselineRegion r in b.BaselineRegions)
        {
            foreach (double p in r.AppliedAssemblies.Stations())
            {
                double d = Math.Abs(p - progresiva);
                if (d < mejorDistancia)
                {
                    mejorDistancia = d;
                    real = p;
                    region = r;
                }
            }
        }
        if (region == null) throw new ErrorUsuario($"El corredor {corredor.Name} no tiene ensamblajes aplicados. Reconstrúyelo.");

        C3D.AppliedAssembly aplicado = region.AppliedAssemblies.GetItemAt(real);
        var vinculos = new List<VinculoSeccion>();
        int indice = 0;
        foreach (C3D.CalculatedLink l in aplicado.Links)
        {
            var codigos = l.CorridorCodes.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
            vinculos.Add(new VinculoSeccion(indice++, codigos, Puntos(l)));
        }

        var formas = new List<FormaSeccion>();
        foreach (C3D.CalculatedShape s in aplicado.Shapes)
        {
            var lados = new List<IReadOnlyList<PuntoSeccion>>();
            foreach (C3D.CalculatedLink l in s.CalculatedLinks) lados.Add(Puntos(l));
            formas.Add(new FormaSeccion(s.CorridorCodes.Where(c => !string.IsNullOrWhiteSpace(c)).ToList(), ContornoForma.Armar(lados)));
        }
        tr.Commit();
        return new LecturaSeccion(new SeccionCorredor(real, vinculos, formas), progresiva, region.Name);
    }

    private static List<PuntoSeccion> Puntos(C3D.CalculatedLink l)
    {
        var puntos = new List<PuntoSeccion>();
        foreach (C3D.CalculatedPoint p in l.CalculatedPoints)
            puntos.Add(new PuntoSeccion(p.StationOffsetElevationToBaseline.Y, p.XYZ.Z));
        return puntos;
    }

    public static List<string> CodigosForma(Document doc, ObjectId corredorId)
    {
        if (corredorId.IsNull || corredorId.IsErased) return new List<string>();
        using Transaction tr = doc.Database.TransactionManager.StartTransaction();
        var corredor = (C3D.Corridor)tr.GetObject(corredorId, OpenMode.ForRead);
        var lista = corredor.GetShapeCodes().Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
        tr.Commit();
        return lista;
    }

    /// <summary>
    /// Rasante − terreno en el eje (desplazamiento 0) en unas progresivas repartidas a lo largo de la línea base,
    /// para encontrar una en corte (negativa) y otra en relleno (positiva).
    /// </summary>
    public static List<(double Progresiva, double Diferencia)> DiferenciasEnEje(Document doc, ObjectId corredorId, string? lineaBase,
        ObjectId terrenoId, int muestras = 60)
    {
        var resultado = new List<(double, double)>();
        if (terrenoId.IsNull || terrenoId.IsErased || corredorId.IsNull || corredorId.IsErased) return resultado;
        List<double> todas = ProgresivasAplicadas(doc, corredorId, lineaBase);
        if (todas.Count == 0) return resultado;
        int paso = Math.Max(1, todas.Count / Math.Max(1, muestras));

        ObjectId alineamientoId;
        using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
        {
            var corredor = (C3D.Corridor)tr.GetObject(corredorId, OpenMode.ForRead);
            alineamientoId = BuscarLineaBase(corredor, lineaBase).AlignmentId;
            tr.Commit();
        }

        for (int i = 0; i < todas.Count; i += paso)
        {
            SeccionCorredor s = LeerSeccion(doc, corredorId, lineaBase, todas[i]).Seccion;
            double? rasante = DetectorEnvolvente.Calcular(s.Vinculos, superior: true).ElevacionEn(0);
            if (!rasante.HasValue) continue;
            using Transaction tr = doc.Database.TransactionManager.StartTransaction();
            try
            {
                var alin = (C3D.Alignment)tr.GetObject(alineamientoId, OpenMode.ForRead);
                var terreno = (C3D.Surface)tr.GetObject(terrenoId, OpenMode.ForRead);
                double x = 0, y = 0;
                alin.PointLocation(s.Progresiva, 0, ref x, ref y);
                resultado.Add((s.Progresiva, rasante.Value - terreno.FindElevationAtXY(x, y)));
            }
            catch (System.Exception ex) when (ex is not OperacionCancelada)
            {
                // Fuera del terreno o del alineamiento (Civil 3D lanza su propia excepción): se salta esa progresiva.
            }
            tr.Commit();
        }
        return resultado;
    }

    /// <summary>
    /// Códigos y voladizo reales de una superficie del corredor. Null si no existe o si su TinSurface subyacente fue borrada.
    /// </summary>
    public static SuperficieCorredor? LeerSuperficie(Document doc, ObjectId corredorId, string nombre)
    {
        if (corredorId.IsNull || corredorId.IsErased) return null;
        using Transaction tr = doc.Database.TransactionManager.StartTransaction();
        var corredor = (C3D.Corridor)tr.GetObject(corredorId, OpenMode.ForRead);
        C3D.CorridorSurfaceCollection superficies = corredor.CorridorSurfaces;
        string? real = superficies.SurfaceNames().FirstOrDefault(n => string.Equals(n, nombre, StringComparison.CurrentCultureIgnoreCase));
        SuperficieCorredor? r = null;
        if (real != null)
        {
            C3D.CorridorSurface cs = superficies[real];
            if (SuperficieTinValida(tr, cs.SurfaceId))
            {
                r = new SuperficieCorredor(cs.Name, cs.SurfaceId, cs.LinkCodes().ToList(), DesdeCivil(cs.OverhangCorrection));
            }
        }
        tr.Commit();
        return r;
    }

    private static bool SuperficieTinValida(Transaction tr, ObjectId surfaceId)
    {
        if (surfaceId.IsNull || surfaceId.IsErased) return false;
        try
        {
            DBObject? obj = tr.GetObject(surfaceId, OpenMode.ForRead, openErased: true);
            return obj is C3D.Surface s && !s.IsErased;
        }
        catch
        {
            return false;
        }
    }

    private static void PurgarSuperficieHuerfanaPorNombre(Database db, Transaction tr, string nombreSuperficie, HashSet<ObjectId> idsVinculadosAlCorredor)
    {
        CivilDocument civilDoc = CivilDocument.GetCivilDocument(db);
        foreach (ObjectId surfId in civilDoc.GetSurfaceIds())
        {
            if (surfId.IsNull || surfId.IsErased || idsVinculadosAlCorredor.Contains(surfId)) continue;
            if (tr.GetObject(surfId, OpenMode.ForRead) is C3D.Surface surf &&
                !surf.IsErased &&
                string.Equals(surf.Name, nombreSuperficie, StringComparison.OrdinalIgnoreCase))
            {
                surf.UpgradeOpen();
                surf.Erase(true);
                Registro.Info($"Superficie huérfana '{nombreSuperficie}' eliminada para recrear limpia.");
                break;
            }
        }
    }

    /// <summary>
    /// Crea o reemplaza de forma resiliente la configuración de las superficies del corredor: limpia entradas huérfanas
    /// cuya TinSurface haya sido borrada en Civil 3D, sincroniza códigos como líneas de rotura, corrección de voladizo
    /// y contorno exterior, y reconstruye el corredor.
    /// </summary>
    public static List<string> CrearSuperficies(Document doc, ObjectId corredorId, ConfigMateriales m, ObjectId estiloId)
    {
        if (corredorId.IsNull || corredorId.IsErased) throw new ErrorUsuario("Elige un corredor.");
        if (m.CodigosSubRasante.Count == 0) throw new ErrorUsuario("Marca los códigos de la SubRasante (o usa «Detectar automáticamente»).");

        var definiciones = new List<(string Nombre, List<string> Codigos, CorreccionVoladizo Voladizo)>
        {
            (m.NombreSubRasante.Trim(), m.CodigosSubRasante, m.VoladizoSubRasante),
        };
        if (m.CrearRasante)
        {
            if (m.CodigosRasante.Count == 0) throw new ErrorUsuario("Marca los códigos de la Rasante o desmarca «Crear también la Rasante».");
            definiciones.Insert(0, (m.NombreRasante.Trim(), m.CodigosRasante, m.VoladizoRasante));
        }
        if (definiciones.Select(d => d.Nombre).Distinct(StringComparer.CurrentCultureIgnoreCase).Count() != definiciones.Count)
            throw new ErrorUsuario("La Rasante y la SubRasante deben tener nombres distintos.");

        var avisos = new List<string>();
        using Transaction tr = doc.Database.TransactionManager.StartTransaction();
        var corredor = (C3D.Corridor)tr.GetObject(corredorId, OpenMode.ForWrite);
        C3D.CorridorSurfaceCollection superficies = corredor.CorridorSurfaces;

        // 1. Limpiar cualquier definición de CorridorSurface cuya TinSurface haya sido borrada en el dibujo (referencia zombi)
        foreach (string nombreExistente in superficies.SurfaceNames().ToList())
        {
            C3D.CorridorSurface csExistente = superficies[nombreExistente];
            if (!SuperficieTinValida(tr, csExistente.SurfaceId))
            {
                try
                {
                    superficies.Remove(nombreExistente);
                    Registro.Info($"Referencia huérfana de superficie de corredor '{nombreExistente}' purgada.");
                }
                catch (System.Exception ex)
                {
                    Registro.Aviso($"No se pudo quitar referencia previa '{nombreExistente}': {ex.Message}");
                }
            }
        }

        var existentes = new HashSet<string>(superficies.SurfaceNames(), StringComparer.CurrentCultureIgnoreCase);
        var idsVinculados = new HashSet<ObjectId>();
        foreach (string n in superficies.SurfaceNames())
        {
            ObjectId sid = superficies[n].SurfaceId;
            if (!sid.IsNull && !sid.IsErased) idsVinculados.Add(sid);
        }

        var codigosCorredor = new HashSet<string>(corredor.GetLinkCodes(), StringComparer.OrdinalIgnoreCase);

        var repetidas = definiciones.Where(d => existentes.Contains(d.Nombre)).Select(d => d.Nombre).ToList();
        if (repetidas.Count > 0 && !Dialogos.Confirmar(
                $"El corredor {corredor.Name} ya tiene {string.Join(" y ", repetidas)}.\n\n" +
                "¿Reemplazar su configuración (códigos, voladizo y contorno)?"))
            throw new OperacionCancelada();

        foreach (var (nombre, codigos, voladizo) in definiciones)
        {
            C3D.CorridorSurface cs;
            if (existentes.Contains(nombre))
            {
                string real = superficies.SurfaceNames().First(n => string.Equals(n, nombre, StringComparison.CurrentCultureIgnoreCase));
                cs = superficies[real];
                foreach (string c in cs.LinkCodes().ToList()) cs.RemoveLinkCode(c);
                foreach (string b in cs.Boundaries.BoundaryNames().ToList()) cs.Boundaries.Remove(b);
                if (!estiloId.IsNull && !estiloId.IsErased) cs.SurfaceStyleId = estiloId;
            }
            else
            {
                PurgarSuperficieHuerfanaPorNombre(doc.Database, tr, nombre, idsVinculados);
                cs = (estiloId.IsNull || estiloId.IsErased) ? superficies.Add(nombre) : superficies.Add(nombre, estiloId);
            }

            cs.IsBuild = true;
            foreach (string codigo in codigos)
            {
                if (!codigosCorredor.Contains(codigo))
                {
                    avisos.Add($"{nombre}: el código {codigo} no es un código de vínculo del corredor; no se agregó.");
                    continue;
                }
                try
                {
                    cs.AddLinkCode(codigo, true);
                }
                catch (ArgumentException ex)
                {
                    avisos.Add($"{nombre}: no se pudo agregar {codigo} ({ex.Message}).");
                }
            }
            cs.OverhangCorrection = ACivil(voladizo);
            try
            {
                cs.Boundaries.AddCorridorExtentsBoundary(NombreContorno);
            }
            catch (System.Exception ex)
            {
                avisos.Add($"{nombre}: no se pudo aplicar el contorno automático ({ex.Message}).");
            }
            Registro.Info($"Superficie de corredor {nombre}: {string.Join(", ", codigos)} · voladizo {voladizo}.");
        }

        corredor.Rebuild();
        tr.Commit();
        return avisos;
    }

    public static C3D.OverhangCorrectionType ACivil(CorreccionVoladizo v) => v switch
    {
        CorreccionVoladizo.VinculosSuperiores => C3D.OverhangCorrectionType.TopLinks,
        CorreccionVoladizo.VinculosInferiores => C3D.OverhangCorrectionType.BottomLinks,
        _ => C3D.OverhangCorrectionType.None,
    };

    public static CorreccionVoladizo DesdeCivil(C3D.OverhangCorrectionType v) => v switch
    {
        C3D.OverhangCorrectionType.TopLinks => CorreccionVoladizo.VinculosSuperiores,
        C3D.OverhangCorrectionType.BottomLinks => CorreccionVoladizo.VinculosInferiores,
        _ => CorreccionVoladizo.Ninguna,
    };
}
