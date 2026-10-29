using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArbaSecciones.Core.Configuracion;

/// <summary>Lee y guarda la configuración en JSON (UTF-8 sin BOM). Funciona con rutas con tildes.</summary>
public static class AlmacenConfiguracion
{
    private static readonly Encoding Utf8SinBom = new UTF8Encoding(false);

    public static readonly JsonSerializerOptions Opciones = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // tildes y ñ legibles en el archivo
        Converters = { new JsonStringEnumConverter() },
    };

    public static string RutaPorDefecto => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ARBA", "ArbaSecciones", "configuracion.json");

    public static string Serializar(ConfiguracionArba cfg) => JsonSerializer.Serialize(cfg, Opciones);

    public static ConfiguracionArba Deserializar(string json) =>
        (JsonSerializer.Deserialize<ConfiguracionArba>(json, Opciones) ?? new ConfiguracionArba()).Normalizar();

    /// <summary>Guarda primero en un archivo temporal y luego reemplaza, para no dejar un JSON a medias.</summary>
    public static void Guardar(ConfiguracionArba cfg, string ruta)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        ArgumentException.ThrowIfNullOrWhiteSpace(ruta);

        string? carpeta = Path.GetDirectoryName(Path.GetFullPath(ruta));
        if (!string.IsNullOrEmpty(carpeta)) Directory.CreateDirectory(carpeta);

        string temporal = ruta + ".tmp";
        File.WriteAllText(temporal, Serializar(cfg), Utf8SinBom);
        File.Move(temporal, ruta, overwrite: true);
    }

    public static ConfiguracionArba Cargar(string ruta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruta);
        if (!File.Exists(ruta)) return new ConfiguracionArba().Normalizar();
        return Deserializar(File.ReadAllText(ruta, Encoding.UTF8));
    }

    /// <summary>
    /// Como <see cref="Cargar"/>, pero si el archivo está dañado lo renombra a .dañado y devuelve la configuración por defecto.
    /// </summary>
    public static ConfiguracionArba CargarOPorDefecto(string ruta, out string? error)
    {
        error = null;
        try
        {
            return Cargar(ruta);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            error = $"No se pudo leer la configuración ({ex.Message}). Se usan los valores por defecto.";
            try
            {
                if (File.Exists(ruta)) File.Move(ruta, ruta + ".dañado", overwrite: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return new ConfiguracionArba().Normalizar();
        }
    }
}
