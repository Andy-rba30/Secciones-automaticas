using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ArbaSecciones.Core.Tests.Marco;

[AttributeUsage(AttributeTargets.Method)]
public sealed class PruebaAttribute : Attribute
{
}

public sealed class FalloPrueba : Exception
{
    public FalloPrueba(string mensaje) : base(mensaje) { }
}

public static class Afirmar
{
    public static void Igual<T>(T esperado, T real, string? detalle = null, [CallerArgumentExpression(nameof(real))] string? expresion = null)
    {
        if (!EqualityComparer<T>.Default.Equals(esperado, real))
            throw new FalloPrueba($"{expresion}: se esperaba <{esperado}> y se obtuvo <{real}>. {detalle}");
    }

    public static void Cerca(double esperado, double real, double tolerancia = 1e-9, string? detalle = null, [CallerArgumentExpression(nameof(real))] string? expresion = null)
    {
        if (double.IsNaN(real) || Math.Abs(esperado - real) > tolerancia)
            throw new FalloPrueba($"{expresion}: se esperaba {esperado} ± {tolerancia} y se obtuvo {real}. {detalle}");
    }

    public static void Verdadero(bool condicion, string? detalle = null, [CallerArgumentExpression(nameof(condicion))] string? expresion = null)
    {
        if (!condicion) throw new FalloPrueba($"No se cumple: {expresion}. {detalle}");
    }

    public static void Secuencia<T>(IEnumerable<T> esperado, IEnumerable<T> real, [CallerArgumentExpression(nameof(real))] string? expresion = null)
    {
        var e = esperado.ToList();
        var r = real.ToList();
        if (!e.SequenceEqual(r))
            throw new FalloPrueba($"{expresion}: se esperaba [{string.Join(", ", e)}] y se obtuvo [{string.Join(", ", r)}].");
    }

    public static TEx Lanza<TEx>(Action accion, [CallerArgumentExpression(nameof(accion))] string? expresion = null) where TEx : Exception
    {
        try
        {
            accion();
        }
        catch (TEx ex)
        {
            return ex;
        }
        catch (Exception ex)
        {
            throw new FalloPrueba($"{expresion}: se esperaba {typeof(TEx).Name} y se lanzó {ex.GetType().Name}: {ex.Message}");
        }
        throw new FalloPrueba($"{expresion}: se esperaba {typeof(TEx).Name} y no se lanzó nada.");
    }
}

public static class Ejecutor
{
    public static int Main(string[] args)
    {
        string? filtro = args.Length > 0 ? args[0] : null;
        var pruebas = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t.IsClass)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<PruebaAttribute>() != null)
                .Select(m => (Tipo: t, Metodo: m)))
            .Where(p => filtro is null || (p.Tipo.Name + "." + p.Metodo.Name).Contains(filtro, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Tipo.Name).ThenBy(p => p.Metodo.Name)
            .ToList();

        int ok = 0;
        var fallos = new List<string>();
        var reloj = Stopwatch.StartNew();
        foreach (var (tipo, metodo) in pruebas)
        {
            string nombre = tipo.Name + "." + metodo.Name;
            try
            {
                object? instancia = metodo.IsStatic ? null : Activator.CreateInstance(tipo);
                metodo.Invoke(instancia, null);
                ok++;
                Console.WriteLine("  OK    " + nombre);
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                fallos.Add(nombre + " → " + tie.InnerException.Message);
                Console.WriteLine("  FALLA " + nombre + Environment.NewLine + "        " + tie.InnerException.Message);
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{ok} correctas, {fallos.Count} fallidas, {pruebas.Count} en total ({reloj.ElapsedMilliseconds} ms).");
        return fallos.Count == 0 && pruebas.Count > 0 ? 0 : 1;
    }
}
