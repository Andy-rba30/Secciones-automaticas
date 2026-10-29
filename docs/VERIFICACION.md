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

Si alguno no existe con ese nombre o esa firma, el error de compilación indica archivo y línea.

## B. En Civil 3D (cada punto debe comprobarse)

- [ ] **Cinta**: aparece la pestaña ARBA con el botón *Secciones transversales*; `ARBASEC` abre la paleta.
- [ ] **Progresivas en tangentes y curvas**: con intervalos 20/10, la vista previa muestra múltiplos de 20 en tangentes, de 10 en curvas y espirales, y los PC/PT/TS/SC/CS/ST. Comparar con las etiquetas del alineamiento.
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
- [ ] **PDF**: A1 sin márgenes, escala 1:1, uno por presentación.
- [ ] **DWG por lámina**: abre en *Presentación 1*, sin el diálogo de recuperación ni el aviso de SHX; el modelo tiene solo las secciones y el cuadro de esa lámina, sin rejilla; el membrete está en 0,0,0.
- [ ] **Rutas con tilde**: configuración y logs se guardan en `C:\Users\Andy Bayona Antón\…` sin errores.
