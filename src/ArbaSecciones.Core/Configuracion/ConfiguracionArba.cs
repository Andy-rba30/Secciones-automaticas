using ArbaSecciones.Core.Acomodo;
using ArbaSecciones.Core.Escalas;
using ArbaSecciones.Core.Progresivas;
using System.Text.Json.Serialization;

namespace ArbaSecciones.Core.Configuracion;

/// <summary>Toda la configuración reutilizable. Se guarda como JSON en UTF-8.</summary>
public sealed class ConfiguracionArba
{
    /// <summary>Versión del formato del archivo. 2: el papel del PDF sale de cada presentación.</summary>
    public int Version { get; set; } = VersionActual;

    public const int VersionActual = 2;
    public OpcionesMuestreo Muestreo { get; set; } = new();
    public ConfigEscala Escala { get; set; } = new();
    public ConfigMembrete Membrete { get; set; } = new();
    public ConfigAcomodo Acomodo { get; set; } = new();
    public ConfigPresentaciones Presentaciones { get; set; } = new();
    public ConfigExportacion Exportacion { get; set; } = new();

    /// <summary>Parámetros del acomodo a partir del área útil del membrete y de la pestaña Acomodo (todo en papel).</summary>
    public ParametrosAcomodo ParametrosAcomodo() => new()
    {
        AnchoUtil = Membrete.AnchoUtil,
        AltoUtil = Membrete.AltoUtil,
        SeparacionH = Acomodo.SeparacionH,
        SeparacionV = Acomodo.SeparacionV,
        AltoFranjaCuadro = Membrete.AltoFranjaCuadro,
        PosicionFranja = Membrete.PosicionFranja,
        Orden = Acomodo.Orden,
        Paginacion = Acomodo.Paginacion,
        LongitudTramo = Acomodo.LongitudTramo,
    };

    /// <summary>Repone secciones nulas que pudieran venir de un JSON incompleto o editado a mano.</summary>
    public ConfiguracionArba Normalizar()
    {
        Muestreo ??= new OpcionesMuestreo();
        Muestreo.Adicionales ??= new List<double>();
        Muestreo.Fuentes ??= new List<string>();
        Escala ??= new ConfigEscala();
        Membrete ??= new ConfigMembrete();
        Membrete.Atributos ??= ConfigMembrete.AtributosPorDefecto();
        Acomodo ??= new ConfigAcomodo();
        Presentaciones ??= new ConfigPresentaciones();
        Exportacion ??= new ConfigExportacion();
        Exportacion.Papel ??= string.Empty;

        // Versión 1 forzaba A1 en todos los PDF. Si quedó ese valor por defecto, ahora se usa el papel de cada presentación.
        if (Version < 2)
        {
            if (string.Equals(Exportacion.Papel, ConfigExportacion.PapelA1Version1, StringComparison.Ordinal))
                Exportacion.Papel = string.Empty;
            Version = VersionActual;
        }
        return this;
    }
}

public sealed class ConfigEscala
{
    public double DenominadorH { get; set; } = 200;

    /// <summary>Null = igual a la horizontal (sin exageración).</summary>
    public double? DenominadorV { get; set; }

    /// <summary>Unidad del modelo. Null = se lee de INSUNITS del dibujo (si no está definida, metros).</summary>
    public UnidadLongitud? UnidadModelo { get; set; }

    public EscalaSecciones Crear() => new(DenominadorH, DenominadorV);
}

public sealed class ConfigMembrete
{
    /// <summary>DWG o DWT de donde se importó la presentación plantilla (solo informativo).</summary>
    public string? RutaPlantilla { get; set; }

    /// <summary>Presentación del dibujo que sirve de plantilla para todas las láminas.</summary>
    public string LayoutPlantilla { get; set; } = "ARBA_PLANTILLA";

    /// <summary>Nombre del bloque del membrete (con atributos) dentro de la presentación plantilla.</summary>
    public string? NombreBloque { get; set; }

    /// <summary>Rectángulo del área útil en coordenadas del espacio papel de la plantilla.</summary>
    public double AreaMinX { get; set; }
    public double AreaMinY { get; set; }
    public double AreaMaxX { get; set; }
    public double AreaMaxY { get; set; }

    public UnidadLongitud UnidadPapel { get; set; } = UnidadLongitud.Milimetros;

    /// <summary>Alto de la franja del cuadro de movimiento de tierras, en unidades de papel. 0 = sin franja.</summary>
    public double AltoFranjaCuadro { get; set; }

    public PosicionFranja PosicionFranja { get; set; } = PosicionFranja.Abajo;

    /// <summary>Al crear o importar la plantilla, ajustar el papel de la presentación al tamaño del membrete (A0, A1…).</summary>
    public bool AjustarHoja { get; set; } = true;

    /// <summary>Última hoja ajustada, solo para mostrarla en la paleta. Ej.: "A1 horizontal (841 × 594 mm)".</summary>
    public string? Hoja { get; set; }

    public List<MapeoAtributo> Atributos { get; set; } = AtributosPorDefecto();

    [JsonIgnore]
    public bool AreaDefinida => AreaMaxX - AreaMinX > 1e-9 && AreaMaxY - AreaMinY > 1e-9;
    [JsonIgnore]
    public double AnchoUtil => AreaMaxX - AreaMinX;
    [JsonIgnore]
    public double AltoUtil => AreaMaxY - AreaMinY;

    public static List<MapeoAtributo> AtributosPorDefecto() => new()
    {
        new() { Etiqueta = "CODIGO", Plantilla = "{codigo}" },
        new() { Etiqueta = "PROG_INICIAL", Plantilla = "{pi}" },
        new() { Etiqueta = "PROG_FINAL", Plantilla = "{pf}" },
        new() { Etiqueta = "TRAMO", Plantilla = "{tramo}" },
        new() { Etiqueta = "LAMINA", Plantilla = "{n}/{total}" },
        new() { Etiqueta = "ESCALA", Plantilla = "{escala}" },
    };
}

/// <summary>Qué texto va en cada atributo del membrete. Ver <see cref="DatosLamina"/> para los marcadores.</summary>
public sealed class MapeoAtributo
{
    public string Etiqueta { get; set; } = string.Empty;
    public string Plantilla { get; set; } = string.Empty;
}

public sealed class ConfigAcomodo
{
    public string? EstiloVista { get; set; }
    public string? ConjuntoBandas { get; set; }
    public OrdenAcomodo Orden { get; set; } = OrdenAcomodo.PorColumnas;
    public ModoPaginacion Paginacion { get; set; } = ModoPaginacion.PorKilometro;

    /// <summary>Separaciones entre vistas, en unidades de papel.</summary>
    public double SeparacionH { get; set; } = 10;
    public double SeparacionV { get; set; } = 10;

    /// <summary>Separación entre los marcos de lámina en el modelo, en unidades de papel.</summary>
    public double SeparacionLaminas { get; set; } = 100;

    public double LongitudTramo { get; set; } = 1000;

    /// <summary>Punto base del acomodo en el modelo. Null = se pide en pantalla.</summary>
    public double? BaseX { get; set; }
    public double? BaseY { get; set; }
}

public sealed class ConfigPresentaciones
{
    public string Prefijo { get; set; } = "SEC-";
    public int Digitos { get; set; } = 2;
    public int CorrelativoInicial { get; set; } = 1;

    /// <summary>Código de plano. {n} es el correlativo. Ej.: "PL-SEC-{n:00}".</summary>
    public string PatronCodigo { get; set; } = "PL-SEC-{n:00}";

    /// <summary>Texto del tramo. Marcadores {kmi} y {kmf}.</summary>
    public string PatronTramo { get; set; } = "{kmi} - {kmf}";

    public string CapaViewport { get; set; } = "Defpoints";
}

public sealed class ConfigExportacion
{
    public string? Carpeta { get; set; }
    public bool Pdf { get; set; } = true;
    public bool Dwg { get; set; } = true;
    public string Dispositivo { get; set; } = "DWG To PDF.pc3";

    /// <summary>
    /// Papel del PDF (nombre canónico). Vacío = el de cada presentación, que se ajusta al membrete;
    /// si el trazador de la presentación es otro, se busca en <see cref="Dispositivo"/> el papel del mismo tamaño.
    /// </summary>
    public string Papel { get; set; } = string.Empty;

    /// <summary>Papel que la versión 1 usaba por defecto para todos los PDF.</summary>
    public const string PapelA1Version1 = "ISO_full_bleed_A1_(841.00_x_594.00_MM)";
    public string NombrePresentacionDwg { get; set; } = "Presentación 1";
}
