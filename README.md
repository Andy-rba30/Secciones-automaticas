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

Abre Civil 3D 2027: pestaña **ARBA › Secciones** o el comando `ARBASEC`. La paleta tiene seis pestañas que se usan en orden:

1. **Muestreo**: alineamiento, grupo, intervalos en tangentes y curvas, puntos notables, fuentes. *Vista previa* y *Crear líneas de muestreo*.
   **Materiales**: visor de la sección del corredor, detección y validación de los códigos de la Rasante y la SubRasante,
   creación de esas superficies en el corredor, lista de materiales (corte y relleno), tabla de volúmenes y CSV.
2. **Membrete**: presentación plantilla (del dibujo, importada de un DWG/DWT o creada con un bloque). Al crearla o importarla, la hoja
   de la presentación se ajusta sola al tamaño del membrete (A0, A1, A2, A3, A4… con su orientación, escala 1:1 y bordes alineados);
   también con el botón *Ajustar hoja al membrete*. Luego *Seleccionar área útil en pantalla*, unidades, franja del cuadro y atributos.
3. **Acomodo**: escala (arriba), estilos, orden y paginación. *Medir y simular* muestra el resumen sin mover nada; *Acomodar en el modelo* pide confirmación y mueve las vistas.
4. **Presentaciones**: una presentación por lámina, con viewport a escala y membrete lleno.
5. **Exportar**: PDF por presentación (en el papel de cada presentación; el campo *Papel* vacío = automático) y DWG independiente por lámina.

Cada pestaña ejecuta solo su etapa. Volver a ejecutar no duplica: pregunta si reemplazar líneas y presentaciones, y reacomoda las vistas existentes.

## Cambios en 1.2.0

- Pestaña **Materiales** entre Muestreo y Membrete: visor interactivo de la sección del corredor (zoom, arrastre, clic en una línea
  para ver su código, vista previa de las superficies y huecos en rojo), detección automática de los códigos (por envolvente, sin
  depender de los nombres), validación de continuidad en varias progresivas, creación de las superficies del corredor, lista de
  materiales con factores, volúmenes por áreas medias con diagrama de masas, CSV y tabla en el modelo.
- Core: `Materiales/` (modelos, `DetectorEnvolvente`, `ValidadorSuperficie`, `PlanValidacion`, `CalculadorVolumenes`, `ContornoForma`)
  y la sección `Materiales` de la configuración (los JSON antiguos se leen con valores por defecto). 65 pruebas.

## Cambios en 1.1.0

- **Leer fuentes del grupo** ya no cierra Civil 3D: el grupo de líneas de muestreo se abre para escritura antes de pedir sus fuentes,
  como exige la API (`SampleLineGroup.GetSectionSources()` escribe en el grupo), y el grupo nuevo se crea en su propia transacción.
- **Hoja ajustada al membrete**: nuevo `ServicioHoja` (plugin) y `FormatosPapel` (Core, con pruebas). Mide el membrete, reconoce el
  formato, elige el papel del trazador (prefiere "full bleed"), gira la hoja si hace falta y alinea el membrete con los bordes.
- **PDF**: ya no fuerza A1 a todas las láminas; usa el papel de cada presentación. Las configuraciones guardadas con el A1 por defecto
  se migran solas a automático.

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
