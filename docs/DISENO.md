# Diseño de ArbaSecciones

## 1. Estructura

| Proyecto | Qué contiene | Depende de Autodesk |
|---|---|---|
| `ArbaSecciones.Core` (net10.0) | Escala y unidades, progresivas, acomodo y paginación, datos de lámina, configuración JSON, log | No |
| `ArbaSecciones.Plugin` (net10.0-windows, WPF) | Comandos, cinta ARBA, paleta, servicios que usan la API de AutoCAD y Civil 3D | Sí |
| `ArbaSecciones.Core.Tests` (consola) | 65 pruebas de la lógica pura, con su propio ejecutor | No |

La lógica que decide **qué** hacer (cuántas láminas, dónde va cada sección, qué progresivas) está en Core y se prueba sin AutoCAD.
El plugin solo lee el dibujo, llama a Core y aplica el resultado.

## 2. Clases principales

### Core

| Clase | Función |
|---|---|
| `EscalaSecciones` | Denominador H y V; exageración = H / V |
| `ConversorEscala` | Factor = (mm por unidad de modelo / mm por unidad de papel) / denominador H. Papel ↔ modelo, `ViewHeight`, relación de la escala de anotación |
| `GeneradorProgresivas` | Intervalo por tipo de elemento, puntos notables (PC, PT, TS, SC, CS, ST, PCC, SS, PI), manuales, duplicados con tolerancia |
| `MedidaSeccion` | Tamaño de una vista separado en **gráfico** (modelo) y **anotación** (papel) |
| `Paginador` + `Empaquetador` | Acomodo por columnas o filas y paginación por km o continua |
| `UniformadorAlturas` | Altura envolvente de un conjunto de vistas y rango de elevación fijado (centrado, en la rejilla, sin recortar) |
| `SugeridorEscala` | Menor denominador con el que entran las secciones, o con el que cada km cabe en una lámina |
| `DatosLamina`, `PlantillaTexto` | Valores de los atributos del membrete (`{codigo}`, `{pi}`, `{n:000}`…) |
| `DetectorEnvolvente` | Envolvente superior (rasante) e inferior (subrasante) de una sección y los códigos que la forman |
| `ValidadorSuperficie`, `PlanValidacion` | Huecos, desvíos y códigos sobrantes de una superficie; qué progresivas revisar |
| `CalculadorVolumenes` | Áreas de corte y relleno entre dos líneas, áreas medias, acumulados, masa y CSV |
| `FormatosPapel` | Reconoce A0…A4 (y alargados ISO 5457) por el tamaño, lee el tamaño del nombre del papel y ordena los papeles candidatos |
| `AlmacenConfiguracion` | JSON UTF-8 sin BOM, escritura atómica, tolera archivos dañados |

### Plugin

| Clase | Función |
|---|---|
| `Ejecutor` | Corre cada acción en contexto de comando, solo si Civil 3D está libre (espera al Idle), una a la vez |
| `ServicioMuestreo` | Lee tangentes, curvas y espirales del alineamiento; crea el grupo y las líneas de muestreo |
| `ServicioEscala` | Escala de anotación (la busca o la crea) y exageración del estilo de vista |
| `ServicioVistas` | Crea las vistas que faltan, aplica el estilo y mide la extensión real de cada una |
| `ServicioAcomodo` | Mueve las vistas y dibuja un marco por lámina con sus datos en XData |
| `ServicioMembrete` | Presentación plantilla (importar, crear con bloque) y área útil |
| `ServicioCorredor` | Lee corredores, líneas base, regiones y la sección (vínculos y formas); crea las superficies del corredor |
| `ServicioMateriales` | Muestreo de fuentes, lista de materiales (QTO), áreas desde las secciones, tablas de volumen |
| `ServicioHoja` | Mide el membrete y deja la hoja de la presentación de su tamaño, orientación y posición |
| `ServicioPresentaciones` | Copia la plantilla por lámina, configura el viewport y llena el membrete |
| `ServicioExportacion` | PDF por presentación y DWG por lámina |
| `PanelPrincipal` | Paleta WPF con las cinco pestañas (sin XAML) |

## 3. Flujo de la interfaz

```
Muestreo ─► Materiales ─► Membrete ─► Acomodo ─► Presentaciones ─► Exportar
   │            │             │           │             │               │
 líneas    superficies,   plantilla +   vistas        layouts         PDF y DWG
           volúmenes      área útil   acomodadas
```

Cada pestaña guarda la configuración y ejecuta solo su etapa. Las etapas se comunican por el dibujo
(grupo de líneas, marcos con XData, presentaciones), así se pueden hacer en sesiones distintas.

## 4. Escala

1. El usuario elige la escala H (y, si quiere, una V distinta) en la pestaña Acomodo.
2. Factor = 1000 / denominador para modelo en metros y papel en mm (1:200 → 5). Las unidades se leen de INSUNITS
   y de la configuración de trazado de la plantilla; se pueden forzar en la pestaña Membrete.
3. Área útil en el modelo = área útil en papel / factor (780 × 520 mm a 1:200 → 156 × 104 m).
4. Separaciones y franja del cuadro se escriben en unidades de papel; en la lámina miden igual con cualquier escala.
5. Antes de crear o medir vistas se fija CANNOSCALE en la escala equivalente (si no existe, se crea).
   Se regenera y recién entonces se mide cada vista.
6. Viewport: `CustomScale` = factor; `ViewHeight` = alto / factor; misma escala de anotación. Siempre con el factor H:
   la exageración ya está dibujada en la vista.
7. Si el estilo de vista tiene otra exageración que la pedida, se pregunta si crear una copia del estilo con la exageración pedida.

**Cambio de escala.** Cada vista se guarda como gráfico (no cambia en el modelo) + anotación (no cambia en el papel).
Con eso la paleta recalcula al instante el resumen y la vista previa con cualquier escala (marcado "estimado").
Al acomodar se fija la escala nueva, se vuelve a medir y se reacomodan las mismas vistas: no se duplican.

## 5. Acomodo y paginación

- **Por columnas**: de abajo hacia arriba; la siguiente columna va a la derecha. Cada sección se centra en su columna.
- **Por filas**: de izquierda a derecha; la siguiente fila va debajo. Cada sección se centra en su fila.
- **Por km**: se agrupa por km (1+000.00 ya es del km 1). Cada km empieza en lámina nueva, intenta entrar completo y,
  si no entra, se coloca el máximo de secciones consecutivas y el resto pasa a láminas de continuación.
- **Continuo**: se llena cada lámina sin cortar por km.
- **Altura uniforme** (`UniformarAlturas`): todas las secciones de una lámina toman el alto de la más alta; si ya no caben, las que sobran
  pasan a la lámina siguiente. `UniformarTodoElTramo` usa una sola altura para todo. En el dibujo, `ServicioVistas.PaginarUniformizado`
  fija el rango de elevación de cada vista (`UniformadorAlturas`: altura envolvente alineada a la rejilla, ventana centrada que siempre
  contiene la demanda), regenera, vuelve a medir y repite hasta que las láminas no cambian.
- Si una sección no entra sola, no se mueve nada: se listan esas progresivas y la menor escala con la que entrarían.
- En el modelo, los marcos de lámina van en filas de 10 desde el punto base, en la capa `ARBA_SEC_MARCOS` (no imprimible).

## 6. Las vistas se crean para poder medirlas

El tamaño real de una vista (bandas, títulos, textos) solo se conoce cuando existe. Por eso *Medir y simular* crea las vistas
que faltan en una zona de espera sobre el punto base y muestra el resumen **sin mover nada**. Las láminas, el movimiento
de vistas y las presentaciones se hacen recién después de confirmar.

## 7. Lecciones aplicadas

| Lección | Dónde |
|---|---|
| 1. Archivo dañado (ErrorStatus 53) | `ServicioExportacion.ArmarPresentacion`: solo `LayoutManager`, con `WorkingDatabase` cambiada y restaurada en `finally` |
| 2. Viewport en blanco | `HerramientasViewport.Configurar`: el orden exacto pedido |
| 3. Viewports en base secundaria | `HerramientasViewport.Viewports`: orden de enumeración, nunca `Number` |
| 4. Aviso de SHX | `ServicioExportacion.CorregirFuentes`: de forma → `ltypeshp.shx`; de texto → `simplex.shx`; bigfont faltante se quita |
| 5. Cuadrícula | `ViewportTableRecord.GridEnabled = false` y `Viewport.GridOn = false` |
| 6. Abrir en la presentación | `TileMode = false` al guardar |
| 7. Hilo principal e Idle | `Ejecutor`: contexto de comando, espera al Idle, rechaza si hay un comando activo |
| 9. `GetSectionSources()` escribe en el grupo | `ServicioMuestreo.LeerFuentes`: el grupo se abre para escritura (abierto para lectura Civil 3D se cerraba) |
| 8. Transacciones por etapa y log | Una transacción por etapa; Esc la descarta. Si fallan las presentaciones a mitad se borran las de esa corrida. Log en `%LOCALAPPDATA%` |

## 8. Hoja ajustada al membrete (1.1.0)

1. Se mide el membrete en el espacio papel: el bloque elegido, si no el bloque con atributos más grande (sin contar atributos),
   si no el bloque más grande, y si no hay bloques todo lo dibujado.
2. El tamaño se pasa a mm con la unidad del papel; si no es un formato pero sí lo sería en cm, m o pulgadas, se usa esa unidad.
3. Se listan los papeles del trazador de la pestaña Exportar (DWG To PDF.pc3 por defecto) y se ordenan: primero el de igual tamaño,
   luego el más chico donde entra; a igual tamaño, "full bleed" y sin girar.
4. Se pone el papel y la rotación; la orientación se comprueba con `PlotPaperSize` (girado 90° o 270° se invierten los lados).
5. Escala 1:1 (o 1 unidad = k mm), trazado de presentación, origen 0,0. Si el membrete no está donde empieza la hoja, se mueve todo
   el espacio papel menos la cámara (y el área útil guardada), así el membrete queda sobre los bordes del papel o centrado si es menor.
6. Las láminas se clonan de la plantilla y heredan la hoja; el PDF usa el papel de cada presentación. En el DWG por lámina, al llevar
   el membrete a 0,0,0 el origen de trazado se corre lo mismo.

## 9. Materiales (1.2.0)

### 9.1 Miembros reales de la API de Civil 3D 2027

Comprobados en la referencia de AeccDbMgd **13.9.0.960** (Civil 3D 2027). Varias pistas del encargo no existen con ese nombre:
`AppliedAssembly.GetLinks()/GetShapes()` son las **propiedades** `Links` y `Shapes`; los factores de la lista no son `CutFactor/FillFactor`
sino `QTOMaterial.SetFactor(MaterialFactorType, double)`.

| Uso | Miembro |
|---|---|
| Corredores del dibujo | `CivilDocument.CorridorCollection` (`IEnumerable<ObjectId>`) |
| Líneas base y regiones | `Corridor.Baselines` → `Baseline.Name`, `AlignmentId`, `BaselineRegions` → `BaselineRegion.Name`, `StartStation`, `EndStation` |
| Ensamblaje en una progresiva | `BaselineRegion.AppliedAssemblies.Stations()` (`double[]`) y `.GetItemAt(double)` (tolerancia 1e-4). No se usa `Baseline.SortedStations()` porque no funciona en corredores DREF |
| Vínculos | `AppliedAssembly.Links` → `CalculatedLink.CorridorCodes` (`CorridorCodeCollection : IEnumerable<string>`), `CalculatedPoints` → `CalculatedPoint.StationOffsetElevationToBaseline` (`Point3d`: Y = desplazamiento) y `XYZ` (Z = elevación) |
| Formas | `AppliedAssembly.Shapes` → `CalculatedShape.CorridorCodes`, `CalculatedLinks` |
| Códigos del corredor | `Corridor.GetLinkCodes()`, `GetShapeCodes()` (`string[]`) |
| Superficies del corredor | `Corridor.CorridorSurfaces` → `SurfaceNames()`, `this[string]`, `Add(string)`, `Add(string, ObjectId estilo)` |
| Configurarlas | `CorridorSurface.AddLinkCode(string, bool comoLineaDeRotura)`, `RemoveLinkCode(string)`, `LinkCodes()`, `OverhangCorrection` (`OverhangCorrectionType.None/TopLinks/BottomLinks`), `SurfaceStyleId`, `SurfaceId`, `Boundaries.AddCorridorExtentsBoundary(string)`, `Boundaries.BoundaryNames()`, `Boundaries.Remove(string)` |
| Reconstruir | `Corridor.Rebuild()` |
| Terreno en el eje | `Alignment.PointLocation`, `Surface.FindElevationAtXY(x, y)` |
| Muestreo | `SampleLineGroup.GetSectionSources()` (grupo abierto para escritura), `SectionSource.SourceId`, `IsSampled` |
| Lista de materiales | `SampleLineGroup.MaterialLists` (`QTOMaterialListCollection`) → `Add(string)`, `Remove(QTOMaterialList)`; `QTOMaterialList.Add(string)`, `Guid`; `QTOMaterial.QuantityType` (`MaterialQuantityType.Cut/Fill/Structure`), `SetFactor(MaterialFactorType.Cut/Fill, double)`, `Add(ObjectId superficie)`, `Add(ObjectId corredor, string forma)`; `QTOMaterialItem.Condition` (`MaterialConditionType.Above/Below`) |
| Secciones | `SampleLine.GetSectionId(ObjectId fuente)`, `Section.SectionPoints` → `SectionPoint.Location` (X = desplazamiento, Y = elevación), `Section.LeftOffset/RightOffset` |
| Tabla de volumen por vista | `SectionView.VolumeTables.CreateVolumeTable(VolumeTableType.Material, Guid lista)` (lanza `ArgumentException` si ya existe) |
| Tabla en el modelo | `Table` de AutoCAD (`SetSize`, `Cells[f, c]`, `Columns[c].Width`, `GenerateLayout`). La API no crea la tabla de volumen total de Civil 3D en el modelo: ese es el respaldo |

### 9.2 Detección por envolvente (Core)

1. Se juntan los desplazamientos de todos los vértices de los vínculos con avance horizontal y los puntos donde dos vínculos se cruzan.
2. En cada intervalo se evalúa, en su punto medio, la elevación de cada vínculo que lo cubre y se elige la mayor (rasante) o la menor
   (subrasante). Los vínculos verticales (avance ≤ tolerancia) se dibujan pero no se eligen, así no crean huecos falsos.
3. Los vínculos que coinciden con la envolvente (± 1 mm) cuentan todos para ese tramo.
4. Códigos: se cubre la envolvente con el menor grupo de códigos eligiendo, en cada paso, el que cubre más ancho sin cubrir menos su ancho
   fuera de la envolvente. Se informa si un código elegido también está en vínculos fuera de ella (al agregarlo los arrastraría).

### 9.3 Validación

La línea de los códigos elegidos se calcula con su corrección de voladizo (Top Links = la más alta, Bottom Links = la más baja) y se compara
con la envolvente: huecos (con los códigos presentes y los sugeridos), desvíos, códigos sobrantes, extremos sin daylight. «Validar todo el
corredor» revisa inicio, medio y fin de cada región, la progresiva de más corte y la de más relleno (rasante − terreno en el eje, si hay terreno
elegido) y, si se marca, todas las del grupo de muestreo. «Calcular materiales» repite la revisión con los códigos y el voladizo **reales** de la
SubRasante del corredor y no calcula si hay huecos, salvo «Calcular igual».

### 9.4 Volúmenes

Áreas entre el terreno y la SubRasante con los puntos de sus secciones en cada línea de muestreo (corte = terreno encima, relleno = terreno
debajo, cortando donde se cruzan), y volúmenes por áreas medias, el método por defecto de Civil 3D. Los acumulados llevan los factores
(esponjamiento en el corte, compactación en el relleno) y la masa es acumulado de corte − acumulado de relleno. La misma lista se crea como
lista de materiales de Civil 3D, para comparar con su informe de volúmenes.

Etapas y transacciones: muestrear las fuentes, crear la lista y leer las áreas son tres transacciones seguidas (Civil 3D crea las secciones
al cerrar el grupo, por eso no se anidan). Cada una queda completa o no se aplica; si se cancela con Esc mientras se leen las áreas, el
muestreo y la lista ya están completos y solo falta la tabla, que se rehace con «Calcular materiales».

## 10. Fuera del alcance de esta versión

- El cuadro de volúmenes por lámina no se genera: se reserva su franja y lo que se dibuje ahí viaja al DWG exportado.
- No se usó la biblioteca `ARBA-comun`: la pestaña ARBA se crea aquí y, si otra herramienta ARBA ya la creó, solo se agrega el panel.
- No se leyeron las notas de `C:\IA\memoria`; se aplicaron las lecciones escritas en el encargo.
