using ArbaSecciones.Core.Comun;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ArbaSecciones.Plugin.Infraestructura;

/// <summary>El usuario canceló (Esc o respondió Cancelar). No es un error.</summary>
internal sealed class OperacionCancelada : System.Exception
{
    public OperacionCancelada(string mensaje = "Operación cancelada.") : base(mensaje) { }
}

/// <summary>Dato faltante o incorrecto: se muestra como aviso, sin detalle técnico.</summary>
internal sealed class ErrorUsuario : System.Exception
{
    public ErrorUsuario(string mensaje) : base(mensaje) { }
}

/// <summary>
/// Ejecuta las acciones de la paleta en el contexto de comando del dibujo activo (hilo principal),
/// solo cuando Civil 3D está libre: si hay un comando activo espera al Idle y vuelve a comprobar.
/// Nunca corre dos acciones a la vez.
/// </summary>
internal static class Ejecutor
{
    private static bool _ocupado;

    public static bool Ocupado => _ocupado;

    public static void Ejecutar(string nombre, Action<Document> accion, Action? alTerminar = null, bool silencioso = false)
    {
        Document? doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc == null)
        {
            if (!silencioso) Dialogos.Aviso("Abre un dibujo de Civil 3D.");
            return;
        }
        if (_ocupado)
        {
            if (!silencioso) Dialogos.Aviso("Espera a que termine la operación en curso.");
            return;
        }

        if (EstaLibre(doc))
        {
            Lanzar(nombre, doc, accion, alTerminar);
            return;
        }
        if (silencioso) return;

        // Civil 3D está ocupado: se reintenta una sola vez cuando quede en reposo.
        EventHandler? enReposo = null;
        enReposo = (_, _) =>
        {
            AcApp.Idle -= enReposo;
            Document? actual = AcApp.DocumentManager.MdiActiveDocument;
            if (actual != null && EstaLibre(actual) && !_ocupado) Lanzar(nombre, actual, accion, alTerminar);
            else Dialogos.Aviso("Hay un comando activo. Termínalo o cancélalo con Esc y vuelve a intentar.");
        };
        AcApp.Idle += enReposo;
    }

    private static bool EstaLibre(Document doc) => AcApp.IsQuiescent && string.IsNullOrEmpty(doc.CommandInProgress);

    /// <summary>
    /// Para código que ya corre dentro de un comando (ej. ARBASEC al abrir la paleta): ejecuta en el acto,
    /// marcando la operación en curso para que los eventos de la paleta no intenten lanzar otra.
    /// </summary>
    public static void EnComandoActual(string nombre, Document doc, Action<Document> accion)
    {
        bool yaOcupado = _ocupado;
        _ocupado = true;
        try
        {
            accion(doc);
        }
        catch (System.Exception ex)
        {
            Registro.Error(nombre, ex);
        }
        finally
        {
            _ocupado = yaOcupado;
        }
    }

    private static void Lanzar(string nombre, Document doc, Action<Document> accion, Action? alTerminar)
    {
        _ocupado = true;
        Registro.Info("Inicio: " + nombre + " · " + doc.Name);
        try
        {
            AcApp.DocumentManager.ExecuteInCommandContextAsync(_ =>
            {
                try
                {
                    using (doc.LockDocument())
                    {
                        accion(doc);
                    }
                    Registro.Info("Fin: " + nombre);
                }
                catch (OperacionCancelada ex)
                {
                    Registro.Aviso(nombre + ": " + ex.Message);
                    Dialogos.Aviso(ex.Message);
                }
                catch (ErrorUsuario ex)
                {
                    Registro.Aviso(nombre + ": " + ex.Message);
                    Dialogos.Aviso(ex.Message);
                }
                catch (System.Exception ex)
                {
                    Registro.Error(nombre, ex);
                    Dialogos.Error(nombre, ex);
                }
                finally
                {
                    _ocupado = false;
                    try { alTerminar?.Invoke(); }
                    catch (System.Exception ex) { Registro.Error(nombre + " (actualizar paleta)", ex); }
                }
                return Task.CompletedTask;
            }, null);
        }
        catch (System.Exception ex)
        {
            _ocupado = false;
            Registro.Error(nombre, ex);
            Dialogos.Error(nombre, ex);
        }
    }
}

/// <summary>Barra de progreso de AutoCAD. Esc cancela la operación (la transacción en curso se descarta).</summary>
internal sealed class Progreso : IDisposable
{
    private readonly ProgressMeter _medidor = new();

    public Progreso(string texto, int total)
    {
        _medidor.SetLimit(Math.Max(1, total));
        _medidor.Start(texto);
    }

    public void Avanzar()
    {
        _medidor.MeterProgress();
        if (HostApplicationServices.Current.UserBreak())
            throw new OperacionCancelada("Cancelado con Esc. No se guardaron los cambios de esta etapa.");
    }

    public void Dispose()
    {
        _medidor.Stop();
        _medidor.Dispose();
    }
}
