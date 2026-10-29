using System.Globalization;
using ArbaSecciones.Core.Comun;
using ArbaSecciones.Core.Configuracion;
using ArbaSecciones.Core.Escalas;
using ArbaSecciones.Plugin.Infraestructura;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.ApplicationServices;
using C3D = Autodesk.Civil.DatabaseServices;
using C3DStyles = Autodesk.Civil.DatabaseServices.Styles;

namespace ArbaSecciones.Plugin.Civil;

/// <summary>Escala de anotación del dibujo y exageración vertical del estilo de vista.</summary>
internal static class ServicioEscala
{
    private const string ColeccionEscalas = "ACDB_ANNOTATIONSCALES";

    /// <summary>Unidad del modelo: la forzada en la configuración o INSUNITS; si no está definida, metros.</summary>
    public static UnidadLongitud UnidadModelo(Database db, ConfigEscala cfg)
    {
        if (cfg.UnidadModelo.HasValue) return cfg.UnidadModelo.Value;
        return UnidadesDibujo.DesdeInsunits(db.Insunits) ?? UnidadLongitud.Metros;
    }

    public static ConversorEscala Conversor(Database db, ConfiguracionArba cfg)
    {
        try
        {
            return new ConversorEscala(cfg.Escala.Crear(), UnidadModelo(db, cfg.Escala), cfg.Membrete.UnidadPapel);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new ErrorUsuario("Escala no válida: " + ex.Message);
        }
    }

    /// <summary>
    /// Fija CANNOSCALE en la escala equivalente al factor (1 unidad de papel = 1/factor unidades de dibujo).
    /// Si no existe en la lista de escalas del dibujo, la crea.
    /// </summary>
    public static AnnotationScale FijarEscalaAnotacion(Database db, ConversorEscala conversor)
    {
        AnnotationScale escala = ObtenerOCrear(db, conversor);
        if (db.Cannoscale == null || db.Cannoscale.Name != escala.Name)
        {
            db.Cannoscale = escala;
            Registro.Info("Escala de anotación fijada en " + escala.Name + ".");
        }
        return escala;
    }

    /// <summary>Busca en la base de datos una escala de anotación con la relación del conversor; si no hay, la crea.</summary>
    public static AnnotationScale ObtenerOCrear(Database db, ConversorEscala conversor) =>
        ObtenerOCrearPorRelacion(db, conversor.UnidadesDibujoPorUnidadPapel, EscalasNormalizadas.Formatear(conversor.Escala.DenominadorH));

    /// <summary>
    /// Escala de anotación con esa relación (unidades de dibujo por unidad de papel). <paramref name="nombreEscala"/>
    /// es el nombre con que se crea si no existe; acepta también el texto "H 1:200 / V 1:100".
    /// </summary>
    public static AnnotationScale ObtenerOCrearPorRelacion(Database db, double relacion, string nombreEscala)
    {
        ObjectContextCollection escalas = db.ObjectContextManager.GetContextCollection(ColeccionEscalas);

        foreach (ObjectContext contexto in escalas)
        {
            if (contexto is AnnotationScale a && a.PaperUnits > 0 &&
                Math.Abs(a.DrawingUnits / a.PaperUnits - relacion) <= 1e-9 * Math.Max(1.0, relacion))
                return a;
        }

        string baseNombre = NombreHorizontal(nombreEscala);
        string nombre = baseNombre;
        if (escalas.HasContext(nombre)) nombre = baseNombre + " (ARBA)"; // ya hay una "1:200" con otra relación de unidades
        int i = 2;
        while (escalas.HasContext(nombre)) nombre = baseNombre + " (ARBA " + i++ + ")";

        var nueva = new AnnotationScale
        {
            Name = nombre,
            PaperUnits = 1.0,
            DrawingUnits = relacion,
        };
        escalas.AddContext(nueva);
        Registro.Info($"Escala de anotación creada: {nombre} (1 = {relacion.ToString("0.######", CultureInfo.InvariantCulture)}).");
        return (AnnotationScale)escalas.GetContext(nombre);
    }

    /// <summary>"H 1:200 / V 1:100" → "1:200"; "1:200" → "1:200".</summary>
    internal static string NombreHorizontal(string texto)
    {
        string t = (texto ?? string.Empty).Trim();
        if (t.StartsWith("H ", StringComparison.OrdinalIgnoreCase))
        {
            int barra = t.IndexOf('/');
            t = (barra > 0 ? t[2..barra] : t[2..]).Trim();
        }
        return t.Length == 0 ? "ARBA" : t;
    }

    /// <summary>Exageración vertical del estilo de vista de sección.</summary>
    public static double ExageracionEstilo(Transaction tr, ObjectId estiloId)
    {
        var estilo = (C3DStyles.SectionViewStyle)tr.GetObject(estiloId, OpenMode.ForRead);
        return estilo.GraphStyle.VerticalExaggeration;
    }

    /// <summary>
    /// Copia el estilo de vista con otra exageración vertical (o reutiliza la copia si ya existe).
    /// Devuelve el ObjectId del estilo a usar.
    /// </summary>
    public static ObjectId EstiloConExageracion(Database db, Transaction tr, ObjectId estiloId, double exageracion)
    {
        var original = (C3DStyles.SectionViewStyle)tr.GetObject(estiloId, OpenMode.ForRead);
        string nombre = original.Name + " (V×" + exageracion.ToString("0.##", CultureInfo.InvariantCulture) + ")";

        CivilDocument civil = CivilDocument.GetCivilDocument(db);
        ObjectId copiaId = ObjectId.Null;
        foreach (ObjectId id in civil.Styles.SectionViewStyles)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is C3DStyles.SectionViewStyle s && s.Name == nombre)
            {
                copiaId = id;
                break;
            }
        }
        if (copiaId.IsNull) copiaId = original.CopyAsSibling(nombre);

        var copia = (C3DStyles.SectionViewStyle)tr.GetObject(copiaId, OpenMode.ForWrite);
        copia.GraphStyle.VerticalExaggeration = exageracion;
        Registro.Info($"Estilo de vista con exageración {exageracion}: {nombre}.");
        return copiaId;
    }

    /// <summary>Nombre del estilo (para la configuración).</summary>
    public static string NombreEstilo(Transaction tr, ObjectId estiloId) =>
        ((C3DStyles.StyleBase)tr.GetObject(estiloId, OpenMode.ForRead)).Name;

    /// <summary>Estilo actual de una vista.</summary>
    public static ObjectId EstiloDeVista(Transaction tr, ObjectId vistaId) =>
        ((C3D.SectionView)tr.GetObject(vistaId, OpenMode.ForRead)).StyleId;
}
