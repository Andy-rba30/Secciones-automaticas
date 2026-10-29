# ArbaSecciones · Civil 3D 2027

Plugin (C# / .NET 10) que crea las líneas de muestreo, las vistas de sección y las láminas de secciones transversales,
acomodadas por km y a la escala que se elija.

## Requisitos (PC1)

- Civil 3D 2027 instalado.
- SDK de .NET 10 x64 (`dotnet --list-sdks` debe mostrar una versión 10.x).

## Compilar e instalar

Civil 3D debe estar cerrado. Descomprime el zip en `D:\Proyectos C#\ArbaSecciones` y ejecuta:

**PC1** (doble clic o desde una consola):

```bat
D:\Proyectos C#\ArbaSecciones\compilar.cmd
```

El script:

1. busca Civil 3D 2027 en el registro (carpeta y versión interna);
2. corre las pruebas de la lógica pura (no necesitan AutoCAD);
3. compila el plugin contra las DLL de tu instalación;
4. arma `artefactos\ArbaSecciones.bundle` y `artefactos\ArbaSecciones.bundle.zip`;
5. lo copia a `%APPDATA%\Autodesk\ApplicationPlugins\ArbaSecciones.bundle`.

Opciones (**PC1**, en PowerShell):

```powershell
.\compilar.ps1 -SinInstalar                                            # solo compila y arma el zip
.\compilar.ps1 -AcadDir "C:\Program Files\Autodesk\AutoCAD 2027"      # si no detecta la carpeta
.\compilar.ps1 -Serie R26.0                                            # si no detecta la versión interna
```

Solo las pruebas (**PC1**):

```bat
dotnet run --project tests\ArbaSecciones.Core.Tests -c Release
```

## Uso

Abre Civil 3D 2027: pestaña **ARBA › Secciones** o el comando `ARBASEC`. La paleta tiene cinco pestañas que se usan en orden:

1. **Muestreo**: alineamiento, grupo, intervalos en tangentes y curvas, puntos notables, fuentes. *Vista previa* y *Crear líneas de muestreo*.
2. **Membrete**: presentación plantilla (del dibujo, importada de un DWG/DWT o creada con un bloque), *Seleccionar área útil en pantalla*, unidades, franja del cuadro y atributos.
3. **Acomodo**: escala (arriba), estilos, orden y paginación. *Medir y simular* muestra el resumen sin mover nada; *Acomodar en el modelo* pide confirmación y mueve las vistas.
4. **Presentaciones**: una presentación por lámina, con viewport a escala y membrete lleno.
5. **Exportar**: PDF por presentación y DWG independiente por lámina.

Cada pestaña ejecuta solo su etapa. Volver a ejecutar no duplica: pregunta si reemplazar líneas y presentaciones, y reacomoda las vistas existentes.

## Archivos fuera del proyecto

| Qué | Dónde |
|---|---|
| Configuración | `%APPDATA%\ARBA\ArbaSecciones\configuracion.json` (se puede guardar otra con *Guardar configuración…*) |
| Logs | `%LOCALAPPDATA%\ARBA\ArbaSecciones\logs` (comando `ARBASECLOG`) |

## Desinstalar

Cierra Civil 3D y borra `%APPDATA%\Autodesk\ApplicationPlugins\ArbaSecciones.bundle`.

## Documentos

- `docs\DISENO.md`: arquitectura, clases, manejo de la escala, algoritmo de acomodo y cómo se aplican las lecciones.
- `docs\VERIFICACION.md`: lista de verificación manual en Civil 3D y miembros de la API que deben confirmarse al compilar.
