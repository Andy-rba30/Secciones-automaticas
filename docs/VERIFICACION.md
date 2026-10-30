# Verificación en Civil 3D 2027

## A. Al compilar

El código se compiló contra referencias con las firmas de la API escritas a mano (no hay SDK de Autodesk disponible donde se programó).
La primera compilación real en PC1 confirma cada llamada. Estos miembros son los que hay que mirar si aparece un error:

| Miembro | Archivo |
|---|---|
| `AlignmentEntity.SubEntityCount`, `AlignmentEntity[i]`, `StartStation`, `EndStation`, `SubEntityType` | `Civil/ServicioMuestreo.cs` |
| `SampleLineGroup.Create(nombre, alineamientoId)` | `Civil/ServicioMuestreo.cs` |
| `SampleLineGroup.GetSectionSources()`, `SectionSource.IsSampled` (escritura), `SourceId`, `SourceType` | `Civil/ServicioMuestreo.cs` |
| `SampleLine.Create(nombre, grupoId, Point2dCollection)`, `SampleLine.GetSectionViewIds()` | `Civil/ServicioMuestreo.cs`, `Civil/ServicioVistas.cs` |
| `SectionView.Create(nombre, lineaId, Point3d)`, `SectionView.Bands.ImportBandSetStyle(id)` | `Civil/ServicioVistas.cs` |
| `SectionView.OffsetMin/OffsetMax/ElevationMin/ElevationMax` | `Civil/ServicioVistas.cs` |
| `SectionViewStyle.GraphStyle.VerticalExaggeration` (lectura y escritura), `StyleBase.CopyAsSibling(nombre)` | `Civil/ServicioEscala.cs` |
| `CivilDocument.Styles.SectionViewStyles`, `SectionViewBandSetStyles` | `Civil/LectorDibujo.cs` |
| `LayoutManager.CloneLayout(origen, nuevo, orden)` | `Civil/ServicioPresentaciones.cs` |
| `Layout.Initialize()` (solo si la hoja exportada no tiene cámara) | `Civil/ServicioExportacion.cs` |
| `DocumentCollection.ExecuteInCommandContextAsync` | `Infraestructura/Ejecutor.cs` |
| `PlotSettingsValidator.GetPlotDeviceList`, `GetCanonicalMediaNameList`, `SetCanonicalMediaName`, `SetPlotRotation`, `SetPlotOrigin`, `SetPlotCentered`, `SetCustomPrintScale` | `Civil/ServicioHoja.cs` |
| `PlotSettings.PlotConfigurationName`, `CanonicalMediaName`, `PlotPaperSize`, `PlotPaperMargins`, `PlotRotation`, `PlotOrigin`; `CustomScale(num, den)` | `Civil/ServicioHoja.cs`, `Civil/ServicioExportacion.cs` |
| `Extents3d.TransformBy`, `Extents3d.AddExtents`, `Entity.Visible`, `Transaction.GetObject(id, modo, false, true)` | `Civil/ServicioHoja.cs` |

| Miembros de la pestaña Materiales (corredor, superficies de corredor, QTO, secciones, tablas de volumen) | `Civil/ServicioCorredor.cs`, `Civil/ServicioMateriales.cs` (lista completa en `docs/DISENO.md`, 9.1) |

En la versión 1.1.0 los servicios y la paleta se compilaron contra firmas extraídas del `ArbaSecciones.Plugin.dll` que ya
compiló en PC1 (0 errores); solo los miembros de las tres últimas filas se escribieron a mano.

Si alguno no existe con ese nombre o esa firma, el error de compilación indica archivo y línea.

## B. En Civil 3D (cada punto debe comprobarse)

- [ ] **Cinta**: aparece la pestaña ARBA con el botón *Secciones transversales*; `ARBASEC` abre la paleta.
- [ ] **Progresivas en tangentes y curvas**: con intervalos 20/10, la vista previa muestra múltiplos de 20 en tangentes, de 10 en curvas y espirales, y los PC/PT/TS/SC/CS/ST. Comparar con las etiquetas del alineamiento.
- [ ] **Leer fuentes del grupo**: con un grupo existente y con un nombre de grupo nuevo, lista las superficies y obras lineales sin cerrar Civil 3D.
- [ ] **Hoja al membrete**: crear la plantilla con un membrete A1 en 0,0,0 → la hoja queda A1 horizontal y sus bordes coinciden con el membrete.
  Repetir con A0 (debe girar el A0 vertical de DWG To PDF), A3 vertical y un membrete movido (se mueve todo el espacio papel y el área útil).
- [ ] **Materiales · pestaña**: aparece entre Muestreo y Membrete; lista los corredores, las líneas base y las progresivas del grupo.
- [ ] **Materiales · visor**: «Leer sección» dibuja vínculos y formas; la rueda hace zoom, arrastrar mueve, «Ajustar» encuadra; la exageración 5× estira en vertical.
- [ ] **Materiales · códigos**: pasar el ratón por un código lo pinta en naranja; clic en una línea del visor muestra su código, desplazamientos, longitud y pendiente, y lo marca en la lista.
- [ ] **Materiales · detección**: «Detectar automáticamente» marca códigos con los que la SubRasante va de daylight a daylight (línea verde continua, sin franjas rojas).
- [ ] **Materiales · hueco**: desmarcar a propósito el código de la cuneta pinta una franja roja y «Validar esta sección» explica «Falta cubrir … Ahí están los vínculos con código …»; «Aplicar sugerencias» lo vuelve a marcar.
- [ ] **Materiales · todo el corredor**: «Validar todo el corredor» revisa inicio, medio y fin de cada región (y corte y relleno con terreno elegido); doble clic en una línea del informe lleva el visor a esa progresiva.
- [ ] **Materiales · superficies**: «Crear / actualizar superficies» deja Rasante (Top Links) y SubRasante (Bottom Links) con los códigos como líneas de rotura y el contorno de extensión del corredor; repetir pregunta si reemplazar.
- [ ] **Materiales · cálculo**: con la SubRasante con huecos no calcula (salvo «Calcular igual»); sin huecos crea la lista «Movimiento de tierras» y llena la tabla.
- [ ] **Materiales · comparación**: los volúmenes totales difieren menos de 0,5 % del informe de volúmenes de Civil 3D (*Analizar › Informe de volumen*) con la misma lista.
- [ ] **Materiales · salidas**: el CSV abre en Excel con tildes y números; «Insertar tabla de volúmenes» pide un punto y crea la tabla; con la casilla marcada, cada vista de sección muestra su tabla de volumen.
- [ ] **Materiales · Esc**: cancelar con Esc a mitad de «Validar todo el corredor» o del cálculo no deja nada a medias.
- [ ] **Líneas de muestreo**: se crean a esas progresivas con los anchos pedidos; volver a crear pregunta si reemplazar o agregar solo las que faltan.
- [ ] **Escala en papel**: a 1:200, medir en la presentación 10 m del modelo (por ejemplo una línea de rejilla de la vista): deben ser 50 mm.
- [ ] **Textos**: los textos de rejilla y bandas miden en papel la altura definida en los estilos.
- [ ] **Exageración**: con H 1:200 / V 1:100, pregunta si crear la copia del estilo con exageración 2 y las vistas salen estiradas al doble.
- [ ] **Cambio de escala**: al cambiar de 1:200 a 1:100 el resumen se recalcula al instante (estimado); al acomodar, el número de láminas cambia y no aparecen vistas duplicadas.
- [ ] **Paginación por km**: ningún km comparte lámina con otro; un km que no entra sigue en láminas de continuación.
- [ ] **Sección que no entra**: con un área útil chica, se listan las progresivas y la escala sugerida, y no se mueve nada.
- [ ] **Viewport**: no sale en blanco; está en Defpoints, bloqueado, sin rejilla y con la escala de anotación correcta.
- [ ] **Membrete**: los atributos muestran código, progresivas inicial y final, tramo y número de lámina.
- [ ] **Repetir**: crear presentaciones otra vez pregunta si reemplazar y no deja presentaciones duplicadas.
- [ ] **PDF**: del tamaño de cada presentación (A0, A1…), sin márgenes, escala 1:1, uno por presentación.
- [ ] **DWG por lámina**: abre en *Presentación 1*, sin el diálogo de recuperación ni el aviso de SHX; el modelo tiene solo las secciones y el cuadro de esa lámina, sin rejilla; el membrete está en 0,0,0.
- [ ] **Rutas con tilde**: configuración y logs se guardan en `C:\Users\Andy Bayona Antón\…` sin errores.
