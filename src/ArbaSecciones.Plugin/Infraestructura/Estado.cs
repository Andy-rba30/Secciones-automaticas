using ArbaSecciones.Core.Acomodo;
using ArbaSecciones.Core.Comun;
using ArbaSecciones.Core.Configuracion;
using ArbaSecciones.Core.Escalas;
using Autodesk.AutoCAD.DatabaseServices;

namespace ArbaSecciones.Plugin.Infraestructura;

/// <summary>Configuración en uso y la última medición de vistas (por dibujo).</summary>
internal static class Estado
{
    private static ConfiguracionArba? _configuracion;

    public static string RutaConfiguracion { get; private set; } = AlmacenConfiguracion.RutaPorDefecto;

    public static ConfiguracionArba Configuracion
    {
        get
        {
            if (_configuracion == null)
            {
                _configuracion = AlmacenConfiguracion.CargarOPorDefecto(RutaConfiguracion, out string? error);
                if (error != null) Registro.Aviso(error);
            }
            return _configuracion;
        }
    }

    public static void Guardar()
    {
        try
        {
            AlmacenConfiguracion.Guardar(Configuracion, RutaConfiguracion);
        }
        catch (System.Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Registro.Error("No se pudo guardar la configuración en " + RutaConfiguracion, ex);
        }
    }

    public static void GuardarComo(string ruta)
    {
        AlmacenConfiguracion.Guardar(Configuracion, ruta);
        RutaConfiguracion = ruta;
    }

    public static void CargarDesde(string ruta)
    {
        _configuracion = AlmacenConfiguracion.Cargar(ruta);
        RutaConfiguracion = ruta;
    }

    /// <summary>Última medición de vistas, válida solo para el dibujo y la escala con que se hizo.</summary>
    public static Medicion? UltimaMedicion { get; set; }
}

internal sealed class Medicion
{
    public Medicion(string documento, ObjectId grupoId, ConversorEscala conversor, IReadOnlyList<VistaMedida> vistas)
    {
        Documento = documento;
        GrupoId = grupoId;
        Conversor = conversor;
        Vistas = vistas;
    }

    public string Documento { get; }
    public ObjectId GrupoId { get; }
    public ConversorEscala Conversor { get; }
    public IReadOnlyList<VistaMedida> Vistas { get; }
    public IReadOnlyList<MedidaSeccion> Medidas => Vistas.Select(v => v.Medida).ToList();
}

/// <summary>Una vista de sección del dibujo y su medida separada en gráfico y anotación.</summary>
internal sealed record VistaMedida(ObjectId VistaId, ObjectId LineaId, MedidaSeccion Medida);
