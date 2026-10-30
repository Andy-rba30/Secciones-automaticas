using System.Collections.Specialized;
using System.Globalization;
using ArbaSecciones.Core.Comun;
using ArbaSecciones.Core.Escalas;
using ArbaSecciones.Core.Hojas;
using ArbaSecciones.Plugin.Infraestructura;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AcDb = Autodesk.AutoCAD.DatabaseServices;

namespace ArbaSecciones.Plugin.Civil;

/// <summary>Resultado de ajustar la hoja: el papel elegido y cuánto se movió el contenido del espacio papel.</summary>
internal sealed record ResultadoHoja(
    string Descripcion,
    string Papel,
    string Dispositivo,
    UnidadLongitud Unidad,
    double DesplazamientoX,
    double DesplazamientoY,
    List<string> Avisos)
{
    public bool SeMovio => Math.Abs(DesplazamientoX) > 1e-9 || Math.Abs(DesplazamientoY) > 1e-9;
}

/// <summary>Rectángulo del membrete en el espacio papel (unidades de dibujo).</summary>
internal sealed record ExtensionMembrete(double MinX, double MinY, double MaxX, double MaxY, string Origen)
{
    public double Ancho => MaxX - MinX;
    public double Alto => MaxY - MinY;
}

/// <summary>
/// Ajusta el papel de una presentación al membrete: lo mide, reconoce el formato (A0, A1, A2…), elige ese papel en el
/// trazador (con la orientación del membrete), deja la escala 1:1 y alinea el membrete con los bordes de la hoja.
/// </summary>
internal static class ServicioHoja
{
    /// <summary>
    /// Mide el membrete de la presentación y deja la hoja de su mismo tamaño. Si el membrete no está donde empieza
    /// la hoja, mueve todo el espacio papel (menos la cámara de la hoja) para que coincidan.
    /// </summary>
    public static ResultadoHoja AjustarAlMembrete(Document doc, string presentacion, string? nombreBloque,
        UnidadLongitud unidadPapel, string dispositivo)
    {
        Database db = doc.Database;
        LayoutManager lm = LayoutManager.Current;
        if (!lm.LayoutExists(presentacion))
            throw new ErrorUsuario($"No existe la presentación \"{presentacion}\". Impórtala o créala primero.");
        ObjectId layoutId = lm.GetLayoutId(presentacion);
        var avisos = new List<string>();

        string descripcion, papel, nombreDispositivo;
        UnidadLongitud unidad;
        Vector3d mover;
        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            var layout = (Layout)tr.GetObject(layoutId, OpenMode.ForRead);
            ExtensionMembrete membrete = MedirMembrete(tr, layout.BlockTableRecordId, nombreBloque)
                ?? throw new ErrorUsuario($"La presentación \"{presentacion}\" no tiene nada dibujado: inserta primero el membrete.");
            if (membrete.Origen.Length > 0) avisos.Add(membrete.Origen);

            unidad = DeducirUnidad(membrete, unidadPapel, avisos);
            double mm = unidad.EnMilimetros();
            double anchoMm = membrete.Ancho * mm;
            double altoMm = membrete.Alto * mm;

            var ajustes = new PlotSettings(layout.ModelType);
            ajustes.CopyFrom(layout);
            PlotSettingsValidator psv = PlotSettingsValidator.Current;
            nombreDispositivo = UsarDispositivo(psv, ajustes, dispositivo);
            List<MedioPapel> medios = Medios(psv, ajustes);

            psv.SetPlotType(ajustes, AcDb.PlotType.Layout);
            OpcionPapel opcion = ElegirPapel(psv, ajustes, anchoMm, altoMm, medios)
                ?? throw new ErrorUsuario(
                    $"El membrete mide {FormatosPapel.Describir(anchoMm, altoMm)} y no entra en ningún papel de \"{nombreDispositivo}\".");
            // Después del papel: al cambiarlo AutoCAD puede poner las unidades del papel nuevo.
            AplicarEscala(psv, ajustes, unidad);
            psv.SetPlotOrigin(ajustes, new Point2d(0, 0));
            try { psv.SetPlotCentered(ajustes, false); }
            catch (Autodesk.AutoCAD.Runtime.Exception) { /* con trazado de presentación no se centra */ }

            var (anchoHoja, altoHoja) = HojaVisible(ajustes);
            var (margenIzq, margenInf) = MargenesVisibles(ajustes);
            if (margenIzq > 0.01 || margenInf > 0.01)
                avisos.Add($"El papel {opcion.Medio.Nombre} tiene márgenes no imprimibles: lo que quede en el borde de la hoja no saldrá en el PDF.");

            // Esquina donde debe quedar el membrete para que sus bordes coincidan con la hoja (o quede centrado si es menor).
            var (xMm, yMm) = FormatosPapel.PosicionMembrete(anchoMm, altoMm, anchoHoja, altoHoja, margenIzq, margenInf);
            mover = new Vector3d(xMm / mm - membrete.MinX, yMm / mm - membrete.MinY, 0);
            double tolerancia = Math.Max(membrete.Ancho, membrete.Alto) * 1e-6;
            if (Math.Abs(mover.X) <= tolerancia && Math.Abs(mover.Y) <= tolerancia) mover = new Vector3d(0, 0, 0);
            else MoverEspacioPapel(tr, layout.BlockTableRecordId, mover, avisos);

            layout.UpgradeOpen();
            layout.CopyFrom(ajustes);
            tr.Commit();

            papel = opcion.Medio.Nombre;
            descripcion = FormatosPapel.Describir(anchoHoja, altoHoja);
            if (!opcion.Exacto)
                descripcion += $"; el membrete ({FormatosPapel.Describir(anchoMm, altoMm)}) queda centrado";
        }

        if (string.Equals(lm.CurrentLayout, presentacion, StringComparison.OrdinalIgnoreCase)) doc.Editor.Regen();
        var inv = CultureInfo.InvariantCulture;
        Registro.Info($"Hoja de \"{presentacion}\": {descripcion} · {papel} en {nombreDispositivo}" +
                      (mover.Length > 0 ? $" · espacio papel movido {mover.X.ToString("0.###", inv)}, {mover.Y.ToString("0.###", inv)}" : "") + ".");
        return new ResultadoHoja(descripcion, papel, nombreDispositivo, unidad, mover.X, mover.Y, avisos);
    }

    // ───────────────────────────── Medición del membrete ─────────────────────────────

    /// <summary>
    /// Rectángulo del membrete: el bloque con el nombre indicado; si no, el bloque con atributos más grande; si no hay,
    /// el bloque más grande; y si tampoco, todo lo dibujado en la presentación (sin viewports).
    /// Los atributos no cuentan, para que un texto fuera del recuadro no agrande la hoja.
    /// </summary>
    internal static ExtensionMembrete? MedirMembrete(Transaction tr, ObjectId espacioId, string? nombreBloque)
    {
        var espacio = (BlockTableRecord)tr.GetObject(espacioId, OpenMode.ForRead);
        Extents3d? porNombre = null, conAtributos = null, cualquiera = null, todo = null;
        double areaAtributos = -1, areaCualquiera = -1;
        string nombreConAtributos = "", nombreCualquiera = "";

        foreach (ObjectId id in espacio)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not AcDb.Entity ent || ent is Viewport) continue;
            if (ent is BlockReference br)
            {
                Extents3d? e = ExtensionBloque(tr, br);
                if (e == null) continue;
                Unir(ref todo, e.Value);
                string nombre = LectorDibujo.NombreBloque(tr, br);
                double area = Area(e.Value);
                if (!string.IsNullOrWhiteSpace(nombreBloque) && string.Equals(nombre, nombreBloque, StringComparison.OrdinalIgnoreCase))
                {
                    if (porNombre == null || area > Area(porNombre.Value)) porNombre = e;
                }
                if (br.AttributeCollection.Count > 0 && area > areaAtributos)
                {
                    conAtributos = e; areaAtributos = area; nombreConAtributos = nombre;
                }
                if (area > areaCualquiera)
                {
                    cualquiera = e; areaCualquiera = area; nombreCualquiera = nombre;
                }
                continue;
            }
            if (Extension(ent) is Extents3d x) Unir(ref todo, x);
        }

        if (porNombre != null) return Crear(porNombre.Value, "");
        if (conAtributos != null)
        {
            string aviso = string.IsNullOrWhiteSpace(nombreBloque) ? "" : $"No está el bloque \"{nombreBloque}\"; se midió el membrete \"{nombreConAtributos}\".";
            return Crear(conAtributos.Value, aviso);
        }
        if (cualquiera != null) return Crear(cualquiera.Value, $"Se midió el bloque \"{nombreCualquiera}\" (no tiene atributos).");
        if (todo != null) return Crear(todo.Value, "El membrete no es un bloque: se midió todo lo dibujado en la presentación.");
        return null;
    }

    private static ExtensionMembrete? Crear(Extents3d e, string origen)
    {
        double ancho = e.MaxPoint.X - e.MinPoint.X, alto = e.MaxPoint.Y - e.MinPoint.Y;
        if (!(ancho > 1e-9) || !(alto > 1e-9)) return null;
        return new ExtensionMembrete(e.MinPoint.X, e.MinPoint.Y, e.MaxPoint.X, e.MaxPoint.Y, origen);
    }

    /// <summary>Extensión de la geometría del bloque (sin atributos), en coordenadas del espacio papel.</summary>
    private static Extents3d? ExtensionBloque(Transaction tr, BlockReference br)
    {
        Extents3d? total = null;
        try
        {
            var definicion = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
            Matrix3d transformacion = br.BlockTransform;
            foreach (ObjectId id in definicion)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not AcDb.Entity ent || ent is AttributeDefinition || !ent.Visible) continue;
                if (Extension(ent) is not Extents3d e) continue;
                e.TransformBy(transformacion);
                Unir(ref total, e);
            }
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            total = null;
        }
        return total ?? Extension(br);
    }

    private static Extents3d? Extension(AcDb.Entity ent)
    {
        try
        {
            return ent.GeometricExtents;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            return null; // entidades sin extensión (textos vacíos, rayos…)
        }
    }

    private static void Unir(ref Extents3d? total, Extents3d e)
    {
        if (total == null)
        {
            total = e;
            return;
        }
        Extents3d t = total.Value;
        t.AddExtents(e);
        total = t;
    }

    private static double Area(Extents3d e) => (e.MaxPoint.X - e.MinPoint.X) * (e.MaxPoint.Y - e.MinPoint.Y);

    /// <summary>
    /// Unidad en que está dibujado el membrete. Se usa la de la configuración, salvo que el tamaño solo tenga sentido
    /// en otra (ej. 0.841 × 0.594 es un A1 dibujado en metros).
    /// </summary>
    private static UnidadLongitud DeducirUnidad(ExtensionMembrete m, UnidadLongitud preferida, List<string> avisos)
    {
        if (FormatosPapel.Reconocer(m.Ancho * preferida.EnMilimetros(), m.Alto * preferida.EnMilimetros()) != null) return preferida;
        foreach (UnidadLongitud u in new[] { UnidadLongitud.Milimetros, UnidadLongitud.Centimetros, UnidadLongitud.Metros, UnidadLongitud.Pulgadas })
        {
            if (u == preferida) continue;
            FormatoPapel? f = FormatosPapel.Reconocer(m.Ancho * u.EnMilimetros(), m.Alto * u.EnMilimetros());
            if (f == null) continue;
            var inv = CultureInfo.InvariantCulture;
            avisos.Add($"El membrete mide {m.Ancho.ToString("0.###", inv)} × {m.Alto.ToString("0.###", inv)}: es un {f.Nombre} " +
                       $"si está dibujado en {u.Abreviatura()}. Se usa {u.Abreviatura()} como unidad del papel.");
            return u;
        }
        return preferida;
    }

    // ───────────────────────────── Configuración de trazado ─────────────────────────────

    /// <summary>Pone el trazador (con el nombre exacto de la lista de AutoCAD) y actualiza sus listas de papeles.</summary>
    internal static string UsarDispositivo(PlotSettingsValidator psv, PlotSettings ajustes, string dispositivo)
    {
        string buscado = string.IsNullOrWhiteSpace(dispositivo) ? "DWG To PDF.pc3" : dispositivo.Trim();
        string? exacto = null;
        StringCollection lista = psv.GetPlotDeviceList();
        foreach (string? d in lista)
        {
            if (d != null && string.Equals(d, buscado, StringComparison.OrdinalIgnoreCase))
            {
                exacto = d;
                break;
            }
        }
        if (exacto == null) throw new ErrorUsuario($"No se encontró el trazador \"{buscado}\" en Civil 3D.");
        try
        {
            if (!string.Equals(ajustes.PlotConfigurationName, exacto, StringComparison.OrdinalIgnoreCase))
                psv.SetPlotConfigurationName(ajustes, exacto, null);
            psv.RefreshLists(ajustes);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            throw new ErrorUsuario($"No se pudo usar el trazador \"{exacto}\" ({ex.Message}).");
        }
        return exacto;
    }

    /// <summary>Papeles del trazador con su tamaño en mm (leído del nombre o, si no lo trae, del propio papel).</summary>
    internal static List<MedioPapel> Medios(PlotSettingsValidator psv, PlotSettings ajustes)
    {
        var nombres = new List<string>();
        foreach (string? n in psv.GetCanonicalMediaNameList(ajustes))
            if (!string.IsNullOrEmpty(n)) nombres.Add(n);

        var medios = new List<MedioPapel>();
        foreach (string n in nombres)
            if (FormatosPapel.TryLeerTamano(n, out double a, out double b)) medios.Add(new MedioPapel(n, a, b));
        if (medios.Count > 0) return medios;

        // Trazadores cuyos nombres no traen el tamaño: se elige cada papel y se lee su tamaño.
        string anterior = ajustes.CanonicalMediaName;
        foreach (string n in nombres)
        {
            try
            {
                psv.SetCanonicalMediaName(ajustes, n);
                Point2d t = ajustes.PlotPaperSize;
                if (t.X > 0 && t.Y > 0) medios.Add(new MedioPapel(n, t.X, t.Y));
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                // papel que el trazador no acepta en esta configuración
            }
        }
        try
        {
            if (!string.IsNullOrEmpty(anterior)) psv.SetCanonicalMediaName(ajustes, anterior);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception) { }
        return medios;
    }

    /// <summary>Escala del trazado: 1:1 si el membrete está en mm o pulgadas; si está en cm o m, 1 unidad = tantos mm.</summary>
    internal static void AplicarEscala(PlotSettingsValidator psv, PlotSettings ajustes, UnidadLongitud unidad)
    {
        if (unidad == UnidadLongitud.Pulgadas)
        {
            psv.SetPlotPaperUnits(ajustes, PlotPaperUnit.Inches);
            psv.SetUseStandardScale(ajustes, true);
            psv.SetStdScaleType(ajustes, StdScaleType.StdScale1To1);
            return;
        }
        psv.SetPlotPaperUnits(ajustes, PlotPaperUnit.Millimeters);
        if (unidad == UnidadLongitud.Milimetros)
        {
            psv.SetUseStandardScale(ajustes, true);
            psv.SetStdScaleType(ajustes, StdScaleType.StdScale1To1);
        }
        else
        {
            psv.SetUseStandardScale(ajustes, false);
            psv.SetCustomPrintScale(ajustes, new CustomScale(unidad.EnMilimetros(), 1.0));
        }
    }

    /// <summary>
    /// Prueba los papeles candidatos en orden y deja puesto el primero que, ya elegido, mide lo que se espera.
    /// La orientación se comprueba con el tamaño real del papel, no solo con su nombre.
    /// </summary>
    internal static OpcionPapel? ElegirPapel(PlotSettingsValidator psv, PlotSettings ajustes, double anchoMm, double altoMm, List<MedioPapel> medios)
    {
        double tol = FormatosPapel.Tolerancia(anchoMm, altoMm);
        foreach (OpcionPapel opcion in FormatosPapel.Candidatos(anchoMm, altoMm, medios).Take(12))
        {
            try
            {
                psv.SetCanonicalMediaName(ajustes, opcion.Medio.Nombre);
                psv.SetPlotRotation(ajustes, opcion.Girado ? PlotRotation.Degrees090 : PlotRotation.Degrees000);
                var (w, h) = HojaVisible(ajustes);
                bool hojaHorizontal = w > h + tol, hojaVertical = h > w + tol;
                bool membreteHorizontal = anchoMm > altoMm + tol, membreteVertical = altoMm > anchoMm + tol;
                if ((hojaHorizontal && membreteVertical) || (hojaVertical && membreteHorizontal))
                {
                    psv.SetPlotRotation(ajustes, opcion.Girado ? PlotRotation.Degrees000 : PlotRotation.Degrees090);
                    (w, h) = HojaVisible(ajustes);
                }
                if (w + tol >= anchoMm && h + tol >= altoMm) return opcion;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                Registro.Aviso($"El papel {opcion.Medio.Nombre} no se pudo usar: {ex.Message}");
            }
        }
        return null;
    }

    /// <summary>Ancho y alto de la hoja tal como se ve en la presentación (mm): el papel girado si la rotación es 90° o 270°.</summary>
    internal static (double Ancho, double Alto) HojaVisible(PlotSettings ajustes)
    {
        Point2d t = ajustes.PlotPaperSize;
        return Girada(ajustes) ? (t.Y, t.X) : (t.X, t.Y);
    }

    /// <summary>Márgenes no imprimibles izquierdo e inferior de la hoja tal como se ve (mm).</summary>
    internal static (double Izquierdo, double Inferior) MargenesVisibles(PlotSettings ajustes)
    {
        Extents2d m = ajustes.PlotPaperMargins;
        return Girada(ajustes) ? (m.MinPoint.Y, m.MinPoint.X) : (m.MinPoint.X, m.MinPoint.Y);
    }

    private static bool Girada(PlotSettings ajustes) =>
        ajustes.PlotRotation == PlotRotation.Degrees090 || ajustes.PlotRotation == PlotRotation.Degrees270;

    /// <summary>Mueve todo el espacio papel menos la cámara de la hoja (primer viewport), también en capas bloqueadas.</summary>
    private static void MoverEspacioPapel(Transaction tr, ObjectId espacioId, Vector3d mover, List<string> avisos)
    {
        ObjectId camara = HerramientasViewport.Viewports(tr, espacioId).Camara;
        Matrix3d desplazamiento = Matrix3d.Displacement(mover);
        var espacio = (BlockTableRecord)tr.GetObject(espacioId, OpenMode.ForRead);
        int fallidos = 0;
        foreach (ObjectId id in espacio)
        {
            if (id == camara) continue;
            try
            {
                if (tr.GetObject(id, OpenMode.ForWrite, false, true) is AcDb.Entity ent) ent.TransformBy(desplazamiento);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                fallidos++;
                Registro.Aviso("No se pudo mover un objeto del espacio papel: " + ex.Message);
            }
        }
        if (fallidos > 0) avisos.Add($"{fallidos} objetos del espacio papel no se pudieron mover con el membrete.");
    }
}
