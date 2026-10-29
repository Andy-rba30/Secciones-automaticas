using System.Diagnostics;
using ArbaSecciones.Core.Comun;
using ArbaSecciones.Plugin.Arranque;
using ArbaSecciones.Plugin.Interfaz;
using Autodesk.AutoCAD.Runtime;

[assembly: ExtensionApplication(typeof(Aplicacion))]
[assembly: CommandClass(typeof(Comandos))]

namespace ArbaSecciones.Plugin.Arranque;

/// <summary>Se ejecuta al cargar el bundle: registra la pestaña ARBA de la cinta.</summary>
public sealed class Aplicacion : IExtensionApplication
{
    public void Initialize()
    {
        Registro.Info("ArbaSecciones " + typeof(Aplicacion).Assembly.GetName().Version + " cargado.");
        try
        {
            Cinta.Registrar();
        }
        catch (System.Exception ex)
        {
            Registro.Error("No se pudo crear la pestaña ARBA de la cinta.", ex);
        }
    }

    public void Terminate()
    {
    }
}

public sealed class Comandos
{
    /// <summary>Abre la paleta de secciones.</summary>
    [CommandMethod("ARBA", "ARBASEC", CommandFlags.Modal)]
    public void AbrirPaleta() => PaletaArba.Mostrar();

    /// <summary>Abre la carpeta de logs.</summary>
    [CommandMethod("ARBA", "ARBASECLOG", CommandFlags.Modal)]
    public void AbrirLogs()
    {
        Directory.CreateDirectory(Registro.CarpetaLogs);
        Process.Start(new ProcessStartInfo { FileName = Registro.CarpetaLogs, UseShellExecute = true });
    }
}
