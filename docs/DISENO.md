# Diseño de ArbaSecciones

## 1. Estructura

| Proyecto | Qué contiene | Depende de Autodesk |
|---|---|---|
| `ArbaSecciones.Core` (net10.0) | Escala y unidades, progresivas, acomodo y paginación, datos de lámina, configuración JSON, log | No |
| `ArbaSecciones.Plugin` (net10.0-windows, WPF) | Comandos, cinta ARBA, paleta, servicios que usan la API de AutoCAD y Civil 3D | Sí |
| `ArbaSecciones.Core.Tests` (consola) | 41 pruebas de la lógica pura, con su propio ejecutor | No |

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
| `SugeridorEscala` | Menor denominador con el que entran las secciones, o con el que cada km cabe en una lámina |
| `DatosLamina`, `PlantillaTexto` | Valores de los atributos del membrete (`{codigo}`, `{pi}`, `{n:000}`…) |
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
| `ServicioPresentaciones` | Copia la plantilla por lámina, configura el viewport y llena el membrete |
| `ServicioExportacion` | PDF por presentación y DWG por lámina |
| `PanelPrincipal` | Paleta WPF con las cinco pestañas (sin XAML) |

## 3. Flujo de la interfaz

```
Muestreo ─► Membrete ─► Acomodo ─► Presentaciones ─► Exportar
   │            │           │             │               │
 líneas    plantilla +   vistas        layouts         PDF y DWG
           área útil   acomodadas
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
| 8. Transacciones por etapa y log | Una transacción por etapa; Esc la descarta. Si fallan las presentaciones a mitad se borran las de esa corrida. Log en `%LOCALAPPDATA%` |

## 8. Fuera del alcance de esta versión

- El cuadro de volúmenes por lámina no se genera: se reserva su franja y lo que se dibuje ahí viaja al DWG exportado.
- No se usó la biblioteca `ARBA-comun`: la pestaña ARBA se crea aquí y, si otra herramienta ARBA ya la creó, solo se agrega el panel.
- No se leyeron las notas de `C:\IA\memoria`; se aplicaron las lecciones escritas en el encargo.
