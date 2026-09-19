# AtlasEmbroidery — MD QUIRÚRGICO

## Correcciones de Foundation + Cierre de Fase 1 + Roadmap de Fases Futuras

**Repositorio:** `akamike17/AtlasEmbroidery`
**Rama actual:** `master`
**Framework:** `.NET 8`
**Lenguaje:** C#
**Motor:** AtlasBordado / AtlasEmbroidery
**Agente ejecutor:** Nemotron
**Objetivo inmediato:** corregir Foundation sin inventar alcance y dejar Fase 1 cerrada con evidencia reproducible.

---

# 0. REGLA PRINCIPAL

Este documento reemplaza cualquier interpretación ambigua del alcance inmediato.

Nemotron NO debe intentar implementar toda la visión de `ATLASBORDADO_MASTER.md`.

La visión maestra describe el producto completo.

Este MD define exclusivamente:

1. auditoría quirúrgica del estado actual;
2. correcciones reales de Foundation;
3. pruebas;
4. endurecimiento de serialización;
5. determinismo;
6. seguridad básica de parsers;
7. cierre formal de Fase 1;
8. definición de las fases posteriores sin implementarlas.

**No crear trabajo artificial sólo para aumentar el número de commits.**

Si una característica no es necesaria para cerrar Foundation, queda fuera.

---

# 1. IDENTIDAD DEL PROYECTO

AtlasEmbroidery es exclusivamente el proyecto de bordado.

Debe permanecer separado de:

* AtlasMI
* AtlasMail
* AtlasSEP
* AtlasNOC
* AtlasDocumentation
* AtlasRestaurantPOS
* cualquier otro proyecto Atlas.

## Evidencia actual

Los commits actuales de `master` son:

### `5efc11b`

Implementación/fixes del núcleo de bordado:

* MachineProfile
* GeometryUtils
* EmbroiderySimulator
* StitchEngine
* serialización
* integración de stores
* tests
* correcciones de generación de puntadas.

### `0bab1ac`

Correcciones específicamente relacionadas con:

* `SequenceIndex`
* `StitchPoint`
* `BinaryStitchSerializer`
* JSON serialization
* `StitchEngineTests`.

No se debe mover ningún commit actualmente.

Las búsquedas actuales no encontraron referencias a:

* AtlasMI
* AtlasMail
* SimulationRun
* OEE
* conceptos propios de AtlasMI
* conceptos propios de AtlasMail.

Por lo tanto:

**AtlasEmbroidery permanece en su repositorio.**

No hacer limpieza destructiva de Git si no existe contaminación demostrable.

---

# 2. ESTADO ACTUAL CONOCIDO

El repositorio contiene actualmente:

```text
Application/
Domain/
    Formats/
        Dst/
        Svg/
    Geometry/
    Image/
    Models/
    Serialization/
    Simulation/
    Stitching/
    Validation/
Infrastructure/
Models/
Tests/
ATLASBORDADO_MASTER.md
AtlasEmbroidery.csproj
AtlasEmbroidery.sln
```

El diseño actual ya contiene una separación razonable entre:

```text
Models
   ↓
Geometry
   ↓
Formats
   ↓
Stitching
   ↓
Serialization
   ↓
Validation
   ↓
Simulation
   ↓
Infrastructure
```

No destruir esta separación.

---

# 3. PROBLEMA CRÍTICO DE ALCANCE

`ATLASBORDADO_MASTER.md` describe desde digitalización hasta producción industrial, Machine Bridge, Doctor, Learning, backups y disaster recovery.

Eso NO significa que Foundation deba implementar todo.

Foundation debe demostrar que el núcleo puede:

```text
modelo
  ↓
geometría
  ↓
stitch generation
  ↓
stitch plan
  ↓
validación
  ↓
serialización
  ↓
deserialización
  ↓
round-trip
  ↓
resultado determinista
```

Si eso no está sólido, no avanzar a producción.

---

# 4. FASE 1 — FOUNDATION

## Objetivo

Dejar un núcleo C# confiable y comprobable para AtlasBordado.

Debe incluir:

* modelos;
* geometría;
* generación básica de puntadas;
* StitchPlan;
* secuencia global;
* serialización JSON;
* serialización binaria;
* hashing;
* DST;
* SVG;
* Validator básico;
* Simulation determinista;
* Infrastructure básica;
* tests automatizados.

---

# 5. CORRECCIÓN 1 — AUDITORÍA COMPLETA DEL CÓDIGO C#

Nemotron debe recorrer todos los `.cs`.

Proceso obligatorio:

```text
read
→ analizar
→ identificar defecto real
→ editar
→ read verify
→ dotnet build
→ dotnet test
→ revisar salida real
```

No declarar corregido un archivo únicamente porque compila.

Buscar especialmente:

* métodos TODO que sean invocados como si estuvieran completos;
* métodos que devuelvan resultados aproximados sin marcarlo;
* casts inseguros;
* overflow;
* listas con capacidad controlada únicamente por input;
* índices;
* división por cero;
* valores negativos;
* conversiones `int/ushort/byte`;
* GUIDs;
* DateTime;
* hash;
* secuencia;
* nullability;
* archivos vacíos;
* streams;
* paths;
* excepciones;
* loops potencialmente infinitos;
* recursión;
* determinismo.

---

# 6. CORRECCIÓN 2 — `SequenceIndex`

Actualmente `SequenceIndex` fue agregado al formato binario.

Debe quedar formalmente definido.

## Reglas

`SequenceIndex` representa posición dentro de la secuencia global.

Debe ser:

* determinista;
* consistente con `GlobalSequence`;
* reproducible después de serializar/deserializar;
* validado contra overflow.

## Problema a revisar

Actualmente se utiliza:

```csharp
(ushort)plan.GlobalSequence.Count
```

Eso puede truncar silenciosamente si la secuencia supera `65535`.

Nemotron debe determinar una de estas soluciones:

### Opción preferida

Cambiar `SequenceIndex` a un tipo suficientemente grande si el diseño lo permite.

### Alternativa

Mantener `ushort`, pero rechazar explícitamente una secuencia que exceda el máximo representable.

Nunca truncar silenciosamente.

Agregar prueba:

```text
sequence <= max → PASS
sequence > max → controlled failure
```

---

# 7. CORRECCIÓN 3 — VERSIONADO BINARIO

El formato actual utiliza:

```text
Magic = ATB1
Version = 1
```

Y actualmente serializa `SequenceIndex`.

Debe quedar documentado que:

```text
ATB1 + Version 1
```

es un formato binario versionado.

## Obligatorio

Agregar pruebas para:

1. magic correcto;
2. magic incorrecto;
3. versión soportada;
4. versión futura;
5. datos truncados;
6. datos corruptos;
7. cero puntadas;
8. una puntada;
9. múltiples puntadas;
10. valores negativos;
11. secuencia;
12. round-trip.

## Regla

Un archivo futuro no debe ser interpretado incorrectamente como archivo actual.

Un archivo truncado no debe provocar resultados parcialmente válidos.

---

# 8. CORRECCIÓN 4 — LÍMITES DE DESERIALIZACIÓN

Este es un punto importante de Foundation.

Actualmente el deserializador lee counts del archivo:

```text
paletteCount
mapCount
stitchCount
```

y crea estructuras utilizando esos valores.

Nemotron debe agregar límites defensivos.

Ejemplo conceptual:

```text
negative count → reject
absurdly large count → reject
truncated stream → reject
remaining bytes insuficientes → reject
```

No confiar en que el archivo es legítimo.

Esto es especialmente importante porque los parsers manejarán archivos externos.

## Objetivo

Un archivo corrupto nunca debe:

* consumir memoria absurda;
* generar excepciones no controladas;
* entrar en loops;
* producir un StitchPlan aparentemente válido.

---

# 9. CORRECCIÓN 5 — VARINT

Auditar:

```text
WriteVarInt
ReadVarInt
```

Debe comprobarse:

* negativos;
* `int.MinValue`;
* `int.MaxValue`;
* secuencias largas;
* terminación;
* overflow;
* EOF.

Un varint corrupto no debe quedarse leyendo indefinidamente.

Agregar pruebas de frontera.

---

# 10. CORRECCIÓN 6 — JSON ROUND-TRIP

Verificar round-trip:

```text
AtlasProject
→ JSON
→ AtlasProject
```

Debe preservar semánticamente:

* geometría;
* objetos;
* StitchParams;
* colores;
* IDs cuando correspondan;
* puntadas;
* SequenceIndex;
* configuración relevante.

No se requiere byte-for-byte equality del JSON.

Se requiere equivalencia semántica.

Agregar pruebas con:

* proyecto vacío;
* proyecto simple;
* múltiples objetos;
* colores;
* puntadas;
* caracteres Unicode;
* valores negativos;
* valores extremos válidos.

---

# 11. CORRECCIÓN 7 — HASH

`ComputeContentHash()` debe ser determinista.

Debe garantizar:

```text
mismo contenido lógico
→ mismo hash
```

Cambios irrelevantes de metadata temporal no deben modificarlo.

Pero cambios reales de contenido sí deben modificarlo.

Tests obligatorios:

```text
same project → same hash
change geometry → different hash
change stitch parameters → different hash
change volatile metadata only → same hash
```

Revisar cuidadosamente si todos los campos que representan contenido real están entrando al hash.

No asumir que el clone actual basta.

---

# 12. CORRECCIÓN 8 — STITCH ENGINE

Auditar especialmente:

```text
GenerateUnderlay
GenerateStitchesForObjectNoUnderlay
GenerateRunningStitches
GenerateSatin...
GenerateTatami...
GenerateZigzag...
GenerateContour...
GenerateTieIn
GenerateTieOff
CalculateMetrics
OptimizePlan
```

## Regla fundamental

La generación no debe producir puntadas físicamente absurdas sólo porque matemáticamente sean válidas.

Foundation no pretende resolver toda la física del bordado.

Pero sí debe evitar errores deterministas obvios.

---

# 13. CORRECCIÓN 9 — `OptimizePlan`

Actualmente existe un TODO para optimización multiobjetivo.

No fingir que está implementado.

Si `EnableOptimization` provoca llamada a un método vacío:

* corregir para que no se anuncie como optimización real;
* o implementar una versión Foundation explícitamente limitada.

La documentación debe indicar qué hace realmente.

No escribir:

```text
optimized
```

si únicamente se devuelve el mismo plan.

---

# 14. CORRECCIÓN 10 — DETERMINISMO

Para el mismo:

```text
AtlasProject
+ StitchParams
+ MachineProfile
+ WorkProfile
+ seed
```

la salida debe ser reproducible.

Test:

```text
compile(project)
compile(project)
```

comparar:

* cantidad de puntadas;
* coordenadas;
* tipos;
* colores;
* agujas;
* flags;
* SequenceIndex;
* métricas;
* bounds.

Si existe aleatoriedad, debe depender exclusivamente de `RandomSeed`.

---

# 15. CORRECCIÓN 11 — MÉTRICAS

Auditar:

```text
TotalStitches
TotalJumps
TotalTrims
TotalColorChanges
TotalStops
EstimatedTimeSeconds
EstimatedThreadMeters
DesignBounds
StitchesPerColor
ThreadMetersPerColor
```

No permitir:

* división por cero;
* velocidad negativa;
* métricas incoherentes;
* pérdida silenciosa de puntadas;
* diferencias entre `ObjectStitches` y `GlobalSequence`.

Agregar invariantes.

Ejemplo:

```text
GlobalSequence.Count
==
cantidad total de comandos representados
```

y las métricas de costura deben corresponder al subconjunto correcto.

---

# 16. CORRECCIÓN 12 — TIE-IN / TIE-OFF

Revisar:

```csharp
param.TieStitchCount
```

porque actualmente participa en una división.

Debe protegerse:

```text
TieStitchCount <= 0
```

No permitir:

```text
divide by zero
```

Además comprobar que la generación no crea desplazamientos absurdos cuando:

```text
TieInLength < TieStitchCount
TieOffLength < TieStitchCount
```

Agregar tests.

---

# 17. CORRECCIÓN 13 — SATIN / ZIGZAG

Auditar:

```text
ColumnWidth
SatinSpacing
path length
zero-length segments
```

No debe producir resultados erróneos cuando:

```text
pathPoints.Count < 2
len == 0
columnWidth <= 0
spacing <= 0
```

Los casos inválidos deben:

* rechazarse;
* normalizarse explícitamente;
* o producir una salida vacía documentada.

No elegir silenciosamente valores mágicos si cambian el resultado.

---

# 18. CORRECCIÓN 14 — TATAMI

Auditar:

```text
RowSpacing
Density
polygon intersections
alternate rows
offset
```

Especial atención a:

```text
Density <= 0
RowSpacing <= 0
polygon degenerado
intersections impares
```

Nunca asumir que las intersecciones siempre llegan en pares.

Si el polígono es degenerado, manejarlo de forma segura.

---

# 19. CORRECCIÓN 15 — UNDERLAY

La corrección contra recursión infinita ya existe:

```text
GenerateStitchesForObjectNoUnderlay
```

Debe conservarse.

Agregar prueba explícita que demuestre:

```text
underlay enabled
→ generation terminates
→ no infinite recursion
```

Además verificar que el underlay:

* conserva flags;
* no cambia accidentalmente el color real;
* no rompe SequenceIndex;
* no produce una secuencia inválida.

---

# 20. CORRECCIÓN 16 — DST

DST debe quedar en Foundation como primer formato externo.

Validar:

```text
read
write
round-trip
header
stitches
jumps
color changes
bounds
EOF
malformed input
```

No declarar compatibilidad universal.

La compatibilidad debe describirse como:

```text
DST V1 / comportamiento implementado y probado
```

hasta disponer de corpus real suficiente.

---

# 21. CORRECCIÓN 17 — SVG

SVG Reader debe auditarse como parser de entrada externa.

Buscar:

* XML malformado;
* entidades;
* archivos gigantes;
* profundidad excesiva;
* geometría degenerada;
* coordenadas extremas;
* paths vacíos;
* comandos desconocidos;
* loops;
* valores NaN/Infinity.

No introducir dependencia de red.

SVG debe continuar funcionando offline.

---

# 22. CORRECCIÓN 18 — FILE STORE

Auditar:

```text
ProjectStore
FileProjectStore
FileTemplateStore
```

Especialmente:

* path traversal;
* directorios inexistentes;
* archivos corruptos;
* overwrite;
* concurrencia;
* atomicidad;
* nombres inválidos;
* archivos parciales.

Nunca permitir que un nombre externo escape del directorio asignado.

---

# 23. CORRECCIÓN 19 — `.csproj`

Revisar las dependencias actuales:

```text
SkiaSharp 3.116.1
System.Text.Json 9.0.0
Microsoft.Extensions.Logging.Abstractions 9.0.0
Microsoft.Extensions.Options.ConfigurationExtensions 9.0.0
System.Drawing.Common 9.0.0
```

El proyecto es:

```text
net8.0
```

Nemotron debe determinar cuáles dependencias son realmente necesarias.

No cambiar versiones por gusto.

Pero sí documentar cualquier dependencia que:

* tenga target framework incompatible;
* sea innecesaria;
* introduzca dependencia innecesaria de plataforma;
* duplique funcionalidad del framework;
* complique soporte offline.

No actualizar paquetes sin una razón técnica y pruebas posteriores.

---

# 24. CORRECCIÓN 20 — TEST PROJECT

El proyecto de tests ya referencia:

```text
AtlasEmbroidery.csproj
```

y tiene:

```text
TreatWarningsAsErrors=true
```

Mantener.

La cobertura no debe utilizarse como único criterio de calidad.

La prioridad es:

```text
correctness
+
boundary tests
+
malformed input
+
determinism
+
round-trip
```

---

# 25. PRUEBAS MÍNIMAS PARA CIERRE

Nemotron debe ejecutar como mínimo:

```powershell
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release --no-restore
```

Si existe una prueba de cobertura válida:

```powershell
dotnet test --configuration Release --collect:"XPlat Code Coverage"
```

No declarar PASS basándose en un número escrito en un commit.

Usar la salida real del build/test.

---

# 26. MATRIZ DE CIERRE DE FOUNDATION

## Gate A — Compilación

```text
dotnet build Release
```

PASS solamente con:

```text
0 errors
0 warnings
```

si `TreatWarningsAsErrors=true`, cualquier warning bloqueante debe resolverse.

---

## Gate B — Tests

PASS:

```text
100% de tests existentes PASS
```

y nuevos tests de regresión PASS.

---

## Gate C — Serialización

PASS:

```text
JSON round-trip
Binary round-trip
DST round-trip donde aplique
hash determinista
malformed input controlado
```

---

## Gate D — Stitch Engine

PASS:

```text
running
satin
tatami
zigzag
underlay
tie-in
tie-off
sequence
metrics
```

con tests de límites.

---

## Gate E — Determinismo

PASS:

```text
same input
→ same logical output
```

---

## Gate F — Seguridad de entrada

PASS:

```text
truncated files
invalid counts
invalid version
invalid magic
malformed SVG
invalid parameters
```

no producen comportamientos peligrosos o resultados aparentemente válidos.

---

## Gate G — Repository Hygiene

PASS:

```text
AtlasEmbroidery contiene únicamente código/documentación correspondiente
a AtlasEmbroidery.
```

No mover commits actuales.

---

# 27. CRITERIO FINAL DE FASE 1

Fase 1 queda:

# CLOSED / VERIFIED

únicamente cuando:

```text
A PASS
B PASS
C PASS
D PASS
E PASS
F PASS
G PASS
```

No se requiere:

* Machine Bridge real;
* máquinas físicas;
* IA;
* producción;
* clientes;
* inventario;
* cotizaciones;
* disaster recovery;
* comunidad;
* nube.

Esos son trabajos posteriores.

---

# 28. FASE 2 — FORMATOS Y NORMALIZACIÓN

## Objetivo

Expandir el núcleo de formatos después de tener Foundation estable.

Prioridad:

```text
DST
→ PES/PEC
→ JEF
→ EXP
→ VP3
→ U01
→ XXX
→ TBF
```

Cada adapter debe tener:

```text
reader
writer
normalization
round-trip
golden corpus
malformed tests
semantic diff
```

No implementar todos simultáneamente.

---

# 29. FASE 3 — VALIDATOR V1

Convertir Validator en sistema formal de reglas.

Familias iniciales:

```text
Geometry
Stitches
Hoop
Machine
Density
Jumps
Trims
Micro-stitches
Bounds
Color/needle
```

Cada regla:

```text
RuleId
Severity
Evidence
Message
Recommendation
Confidence
```

Estados:

```text
PASS
WARNING
CRITICAL
UNKNOWN
```

---

# 30. FASE 4 — DIGITALIZACIÓN BÁSICA

Implementar progresivamente:

```text
SVG
Shapes
Running
Satin
Fill
Underlay
Pull Compensation
Sequence
```

Después:

```text
text
motifs
appliqué
knockdown
puff
sequins
```

No implementar todo en una sola iteración.

---

# 31. FASE 5 — MATERIAL LAB / AUTOSETUP

Crear perfiles versionados:

```text
Material
Thread
Needle
Stabilizer
Machine
Hoop
WorkProfile
```

Composición:

```text
Material
+
Work Type
+
Machine
+
Thread/Needle
+
Hoop
=
WorkProfile
```

Debe existir confianza:

```text
Validated
Verified
Experimental
Custom
```

---

# 32. FASE 6 — SIMULATION V2

Foundation puede tener simulación determinista.

La siguiente fase puede agregar:

```text
playback
layers
density maps
thread estimation
time estimation
machine limits
risk visualization
```

No afirmar física realista sin evidencia.

---

# 33. FASE 7 — MACHINE BRIDGE

Separar obligatoriamente:

```text
FormatAdapter
TransportAdapter
MachineAdapter
```

Niveles:

```text
0 Export
1 Media
2 Transfer
3 Status
4 Queue
5 Telemetry
6 Control
```

No implementar protocolos de máquina por inferencia.

Cada compatibilidad necesita:

```text
model
firmware
transport
test evidence
```

---

# 34. FASE 8 — ATLAS DOCTOR

Sistema determinista de diagnóstico.

Entrada:

```text
symptom
+
job
+
machine
+
material
+
history
```

Salida:

```text
probable causes
+
evidence
+
safe test
+
one-variable change
+
result logging
```

IA será opcional.

La lógica base no dependerá de IA.

---

# 35. FASE 9 — PRODUCTION

Dominios:

```text
Customers
Quotes
Orders
Inventory
Jobs
Machine Queue
Quality
Rework
History
```

El `Job Package` será versionado.

Debe contener:

```text
design/hash
material
machine
thread
needle
hoop
artifact
validator result
instructions
first article
```

No acoplar Production directamente al Stitch Engine.

---

# 36. FASE 10 — LEARNING / LAB

Feedback mínimo:

```text
👍 Bien
👎 Mal
comentario opcional
```

Registrar:

```text
design
version
parameters
material
machine
result
incident
```

El conocimiento privado nunca debe salir automáticamente.

---

# 37. FASE 11 — RELIABILITY / RECOVERY

Implementar:

```text
Audit Log
Backups
Restore
Checksums
Emergency Server
Health Checks
Disaster Recovery
```

Prueba real:

```text
job activo
→ falla servidor
→ levantar emergencia
→ identificar estado
→ recuperar hash/version
→ continuar
→ evitar duplicación
```

---

# 38. FASE 12 — AI ASSISTED EMBROIDERY

Sólo después de que el sistema determinista esté sólido.

IA podrá ayudar con:

```text
auto-digitize
optimization suggestions
troubleshooting
material recommendations
design analysis
```

Pero:

```text
IA propone
→ Validator verifica
→ usuario aprueba
→ sistema registra
```

Nunca:

```text
IA decide
→ máquina ejecuta
```

sin las capas de seguridad correspondientes.

---

# 39. REGLAS PARA NEMOTRON

## Procedimiento obligatorio

```text
READ
↓
PLAN CORTO
↓
EDIT/WRITE
↓
READ VERIFY
↓
BUILD
↓
TEST
↓
REVIEW REAL OUTPUT
↓
COMMIT
```

No modificar archivos sin leerlos primero.

No declarar éxito sin evidencia.

No detenerse únicamente en diagnóstico cuando existe una corrección segura y claramente definida.

No inventar:

* APIs;
* protocolos;
* capacidades de máquinas;
* compatibilidades;
* resultados de tests.

---

# 40. POWERSHELL

En PowerShell utilizar:

```powershell
comando1; comando2
```

No asumir sintaxis Bash:

```text
&&
```

si no es compatible con el entorno.

---

# 41. COMMITS

Commits pequeños y coherentes.

Ejemplos:

```text
fix: harden binary stitch deserialization
fix: make stitch sequence overflow explicit
fix: harden stitch parameter boundaries
test: add malformed binary regression coverage
test: add deterministic stitch plan coverage
fix: harden SVG input handling
chore: clean project dependency configuration
```

No mezclar:

```text
feature + refactor masivo + formatting + unrelated cleanup
```

en un solo commit.

---

# 42. NO HACER

No implementar todavía:

```text
Machine Bridge real
IA
producción
clientes
inventario
cloud
community
emergency server
disaster recovery
full digitizer UI
todos los formatos
```

No cambiar la arquitectura sólo porque existe una característica futura.

No crear 40 fases artificiales.

No declarar una fase incompleta únicamente porque la siguiente fase existe.

---

# 43. DEFINICIÓN DE TERMINADO

Foundation terminada significa:

```text
el núcleo compila
+
los tests pasan
+
los parsers soportan entradas corruptas
+
la serialización es reproducible
+
la secuencia es segura
+
el StitchEngine es determinista
+
las métricas son coherentes
+
DST/SVG tienen comportamiento probado
+
el repositorio permanece limpio
```

Eso es suficiente para cerrar Fase 1.

---

# 44. RESULTADO FINAL ESPERADO

Nemotron debe terminar entregando:

```text
1. lista de correcciones realizadas
2. lista de correcciones descartadas y motivo
3. archivos modificados
4. tests agregados
5. comando de build ejecutado
6. comando de test ejecutado
7. resultado real
8. commits realizados
9. SHA final
10. declaración:
   FOUNDATION = CLOSED / VERIFIED
   o
   FOUNDATION = NOT CLOSED
```

No usar:

```text
"casi"
"prácticamente"
"debería funcionar"
"todo bien"
```

La fase se cierra con evidencia o no se cierra.

---

# 45. PRINCIPIO DEL PROYECTO

AtlasBordado no debe convertirse en:

> "un programa que genera archivos DST".

Debe evolucionar hacia:

```text
Diseño
 ↓
Objetos
 ↓
Stitch Plan
 ↓
Validación
 ↓
Simulación
 ↓
Artifact
 ↓
Máquina
 ↓
Producción
 ↓
Evidencia
 ↓
Repetibilidad
```

Pero cada etapa debe ganarse con pruebas.

**Primero un núcleo pequeño que podamos defender técnicamente.
Después le ponemos las alas.**

