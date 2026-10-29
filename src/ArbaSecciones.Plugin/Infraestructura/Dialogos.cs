using System.Windows;
using ArbaSecciones.Core.Comun;

namespace ArbaSecciones.Plugin.Infraestructura;

internal static class Dialogos
{
    private const string Titulo = "ARBA · Secciones";

    public static void Info(string mensaje) =>
        MessageBox.Show(mensaje, Titulo, MessageBoxButton.OK, MessageBoxImage.Information);

    public static void Aviso(string mensaje) =>
        MessageBox.Show(mensaje, Titulo, MessageBoxButton.OK, MessageBoxImage.Warning);

    public static void Error(string operacion, System.Exception ex) =>
        MessageBox.Show(operacion + ": " + ex.Message + Environment.NewLine + Environment.NewLine +
                        "Detalle en el log:" + Environment.NewLine + Registro.RutaActual,
            Titulo, MessageBoxButton.OK, MessageBoxImage.Error);

    /// <summary>Aceptar / Cancelar.</summary>
    public static bool Confirmar(string mensaje) =>
        MessageBox.Show(mensaje, Titulo, MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    /// <summary>Sí / No / Cancelar.</summary>
    public static MessageBoxResult Preguntar(string mensaje) =>
        MessageBox.Show(mensaje, Titulo, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
}
