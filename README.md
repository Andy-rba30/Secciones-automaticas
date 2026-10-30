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
   creación de esas superficies en el corredor, lista de materiales (corte y relleno), tabla de volúmenes (estándar o dinámica nativa de
   Civil 3D, validada y ubicada en un área de la lámina) y CSV.
2. **Membrete**: presentación plantilla (del dibujo, importada de un DWG/DWT o creada con un bloque). Al crearla o importarla, la hoja
   de la presentación se ajusta sola al tamaño del membrete (A0, A1, A2, A3, A4… con su orientación, escala 1:1 y bordes alineados);
   también con el botón *Ajustar hoja al membrete*. Luego *Seleccionar área útil en pantalla*, unidades, franja del cuadro y atributos.
3. **Acomodo**: escala (arriba), estilos, orden y paginación. *Medir y simular* muestra el resumen sin mover nada; *Acomodar en el modelo* pide confirmación y mueve las vistas.
4. **Presentaciones**: una presentación por lámina, con viewport a escala y membrete lleno.
5. **Exportar**: PDF por presentación (en el papel de cada presentación; el campo *Papel* vacío = automático) y DWG independiente por lámina.

Cada pestaña ejecuta solo su etapa. Volver a ejecutar no duplica: pregunta si reemplazar líneas y presentaciones, y reacomoda las vistas existentes.

## Cambios en 1.5.0

- **Tabla dinámica nativa de volúmenes de Civil 3D** (pestaña Materiales, paso 6), opcional: la tabla estándar sigue siendo la de siempre y
  la opción por defecto.
  - Tipo *Volumen total* o *Volumen de material*, con los estilos de tabla del dibujo. Queda vinculada al grupo de líneas de muestreo y a la
    lista de materiales, así que se actualiza sola.
  - *Dividir tabla si excede altura*, *Máx. filas por columna* (0 = automático), *Espaciado entre columnas* y *Dirección*.
  - *Definir área para tabla en lámina…* (dos puntos en el modelo o en una presentación) o *Usar franja del cuadro…*; alineación
    (superior izquierda, centro…) y margen. La tabla se mide, se valida contra el área y se centra o alinea. Si no entra, se avisa con las
    medidas reales en papel y lo que falta, y se ofrece rehacerla con la división que sí entra o borrarla sin dejar el dibujo a medias.
  - *Validar y ubicar tabla existente…* para una tabla que ya esté en el dibujo.
  - La API .NET de Civil 3D no crea estas tablas: el plugin fija por API los ajustes de los comandos nativos `AddTotalVolumeTable` /
    `AddMaterialVolumeTable`, los ejecuta, devuelve los ajustes a como estaban y después mide y ubica (ver `docs/DISENO.md`, sección 10).
- Core: `Tablas/DimensionesTabla` y `Tablas/ValidadorCabidaTabla`; `ConfigMateriales.TablaVolumen` (los JSON anteriores se leen con la
  tabla estándar). Plugin: `ServicioTablasVolumen`. 85 pruebas (11 nuevas).

## Cambios en 1.4.0

- **Uniformizar altura de secciones por lámina** (pestaña Acomodo, activado por defecto): todas las vistas de una misma lámina quedan con
  el mismo alto y la misma cota de rejilla, alineadas en filas y columnas, **sin recortar terreno, rasante, taludes ni bandas**.
  - Demanda real de cada vista = su rango de elevación automático. Altura envolvente = la mayor demanda de la lámina, alineada a un
    múltiplo de la rejilla (*Múltiplo de altura*, 5 por defecto; 0 = sin redondeo). Cada vista queda centrada y con el piso en la rejilla;
    se comprueba que el rango fijado contiene al requerido y, si la API no lo admite, la vista vuelve a automático.
  - Opción *Usar la misma altura en todo el tramo*: una sola altura para todas las láminas.
  - Como al crecer las vistas pueden pasar secciones a la lámina siguiente, el plugin repite medir → uniformizar → paginar (máx. 4 vueltas) hasta que
    la composición de las láminas se estabiliza; si no, usa una sola altura para todo el tramo.
- Core: `UniformadorAlturas` (altura envolvente y rango fijado), `ParametrosAcomodo.UniformarAlturas/UniformarTodoElTramo`, simulación en
  `Paginador` (`Lamina.AlturasUniformes`, `Lamina.AltoMaximo`) y `ConfigAcomodo.UniformarAlturasPorLamina/UniformarTodoElTramo/IntervaloRejillaElevacion`.
- Plugin: `ServicioVistas.UniformizarRangosElevacion`, `RestaurarRangoAutomatico` y `PaginarUniformizado`. 74 pruebas (8 nuevas).

## Cambios en 1.3.0

- **Resiliencia en superficies de corredor y materiales**: purga automática de entradas `CorridorSurface` huérfanas cuando el usuario elimina
  y recrea la superficie TIN en Civil 3D, auto-regeneración de la superficie al calcular materiales si fue borrada, y protección en
  `PuntosSeccion` contra `InvalidOperationException: LeftOffset is not supported by current Section`.
- **Acomodo centrado en el área útil**: `Empaquetador` centra horizontal y verticalmente el bloque de columnas/filas dentro de la zona de
  secciones del área útil de cada lámina (evitando que las secciones queden pegadas a una esquina). 66 pruebas.
- **Regeneración limpia de presentaciones y actualización reactiva**: cambio de pestaña en la paleta refresca automáticamente las listas
  del dibujo descartando objetos borrados (`IsErased`), sincroniza el acomodo antes de regenerar presentaciones y evita bloqueos de
  regeneración de PaperSpace al clonar y configurar viewports.

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
