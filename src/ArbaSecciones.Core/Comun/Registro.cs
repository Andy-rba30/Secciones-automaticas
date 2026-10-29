using System.Globalization;
using System.Text;

namespace ArbaSecciones.Core.Comun;

/// <summary>
/// Log del proceso en %LOCALAPPDATA%\ARBA\ArbaSecciones\logs (fuera de cualquier repositorio), en UTF-8.
/// Un fallo al escribir el log nunca detiene el trabajo.
/// </summary>
public static class Registro
{
    private static readonly object Candado = new();
    private static readonly Encoding Utf8SinBom = new UTF8Encoding(false);

    public static string CarpetaLogs { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ARBA", "ArbaSecciones", "logs");

    public static string RutaActual =>
        Path.Combine(CarpetaLogs, "arbasecciones-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");

    public static void Info(string mensaje) => Escribir("INFO ", mensaje);
    public static void Aviso(string mensaje) => Escribir("AVISO", mensaje);
    public static void Error(string mensaje, Exception? ex = null) => Escribir("ERROR", ex is null ? mensaje : mensaje + Environment.NewLine + ex);

    private static void Escribir(string nivel, string mensaje)
    {
        try
        {
            lock (Candado)
            {
                Directory.CreateDirectory(CarpetaLogs);
                string linea = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + nivel + " " + mensaje + Environment.NewLine;
                File.AppendAllText(RutaActual, linea, Utf8SinBom);
            }
        }
        catch
        {
            // El log es auxiliar: si el disco o los permisos fallan, el comando sigue.
        }
    }
}
