# AtlasEmbroidery

## MD Quirúrgico Definitivo — Cierre Real de Foundation + Roadmap Maestro

**Repositorio:** `akamike17/AtlasEmbroidery`
**Commit auditado:** `ce9abd7bb07f5bda044dda5a85e1b28841df0a60`
**Estado declarado por el agente:** Foundation CLOSED / VERIFIED
**Estado de esta auditoría:** Foundation requiere una última corrección quirúrgica antes del cierre definitivo.

---

# 1. OBJETIVO DE ESTE DOCUMENTO

Este documento tiene dos objetivos:

1. Corregir definitivamente los puntos restantes encontrados después de la auditoría del commit `ce9abd7`.
2. Definir desde ahora el roadmap completo de AtlasEmbroidery, evitando la creación de fases artificiales durante el desarrollo.

No se permitirá volver a ampliar Foundation indefinidamente.

La intención es:

```text
ce9abd7
    ↓
último hardening real
    ↓
regresión
    ↓
auditoría
    ↓
FOUNDATION CLOSED / VERIFIED
    ↓
Phase 2
    ↓
Phase 3
    ↓
...
    ↓
producto completo
```

---

# 2. ESTADO ACTUAL

El commit `ce9abd7` incorporó:

* versionado binario explícito;
* lectura segura de strings;
* límites de VarInt;
* budget inicial de stitches;
* rechazo de trailing data;
* prueba real de `SequenceIndex`;
* validación de velocidad;
* controles del `StitchEngine`;
* optimización determinista básica;
* validaciones de Satin;
* validaciones de Tatami;
* protección de tie-in/tie-off;
* protección de underlay;
* round-trip JSON;
* hash determinista;
* pruebas de FileStore;
* pruebas de SVG;
* auditoría de dependencias;
* tests adicionales.

Estado reportado:

```text
210 tests
58.9% line coverage
43.1% branch coverage
Gates A-G PASS
```

Estos resultados son evidencia importante.

Sin embargo:

```text
PASS tests
≠
contrato completamente cerrado
```

La siguiente iteración debe corregir los puntos descritos en este documento.

---

# 3. REGLA DE CIERRE

No se debe volver a declarar:

```text
FOUNDATION CLOSED / VERIFIED
```

hasta que se cumpla:

```text
Código corregido
+
tests corregidos
+
tests nuevos
+
build Release
+
tests Release
+
auditoría de diff
+
revisión de determinismo
+
revisión de input no confiable
```

Después de eso:

```text
FOUNDATION = CLOSED
```

Y cualquier mejora posterior pasa a la fase correspondiente.

No reabrir Foundation por mejoras de producto que pertenezcan a fases posteriores.

---

# 4. CORRECCIÓN 1 — ELIMINAR CATCH GENÉRICO DEL DESERIALIZADOR

Archivo:

```text
Domain/Serialization/AtlasSerializer.cs
```

Actualmente existe:

```csharp
catch (EndOfStreamException)
{
    return null;
}
catch (InvalidDataException)
{
    return null;
}
catch (Exception)
{
    return null;
}
```

El tercer catch debe eliminarse o sustituirse por una frontera mucho más estricta.

## Objetivo

Diferenciar:

```text
archivo corrupto
```

de:

```text
bug interno
```

### Archivo corrupto

Debe producir resultado controlado:

```text
null
```

o el mecanismo de error establecido por el proyecto.

### Bug interno

Debe propagarse.

Ejemplo:

```text
NullReferenceException
InvalidOperationException
ArgumentException
Overflow inesperado
```

no deben convertirse silenciosamente en:

```text
archivo corrupto
```

## Regla

No esconder bugs del motor bajo el contrato de deserialización.

---

# 5. CORRECCIÓN 2 — UTF-8 ESTRICTO

`ReadStringSafe()` debe utilizar un decoder UTF-8 que rechace secuencias inválidas.

No utilizar simplemente:

```csharp
Encoding.UTF8.GetString(bytes)
```

como mecanismo de validación.

Debe utilizarse un encoder/decoder configurado con:

```text
DecoderFallback.ExceptionFallback
```

o equivalente.

## Casos

Debe aceptar:

```text
ASCII
UTF-8 válido
acentos
ñ
caracteres Unicode válidos
```

Debe rechazar:

```text
UTF-8 truncado
secuencias inválidas
continuation bytes inválidos
overlong encodings
bytes ilegales
```

## Tests

Agregar:

```text
ValidUtf8_String
InvalidUtf8_String
TruncatedUtf8_String
MultibyteUtf8_String
MaxValidUtf8_String
OversizedUtf8_String
```

---

# 6. CORRECCIÓN 3 — STRING LENGTH HARDENING

Mantener:

```text
MAX_STRING_BYTES = 10000
```

La validación debe ocurrir antes de:

```text
allocation
```

El algoritmo debe continuar siendo:

```text
read 7-bit length
        ↓
validate length
        ↓
validate remaining bytes
        ↓
allocate exact buffer
        ↓
read exact bytes
        ↓
strict UTF-8 decode
```

Nunca:

```text
read string
↓
validate afterward
```

---

# 7. CORRECCIÓN 4 — BUDGET GLOBAL DEL BINARIO

Actualmente existe:

```text
stitchCount * 9
```

Mantenerlo.

Pero añadir límites defensivos para:

```text
paletteCount
mapCount
string bytes
machine profile
hoop profile
stitches
total document size
```

El parser debe trabajar bajo un presupuesto global razonable.

Ejemplo conceptual:

```text
MAX_BINARY_DOCUMENT_BYTES
```

No es necesario elegir un valor exageradamente pequeño.

Debe ser suficiente para:

```text
Foundation
+
diseños grandes razonables
```

pero impedir:

```text
entrada arbitrariamente grande
```

## Regla

Un contador controlado individualmente no sustituye un presupuesto global.

---

# 8. CORRECCIÓN 5 — VALIDACIÓN DE MÉTRICAS BINARIAS

Después de leer:

```text
TotalStitches
TotalJumps
TotalTrims
TotalColorChanges
TotalStops
EstimatedTimeSeconds
EstimatedThreadMeters
```

validar:

```text
TotalStitches >= 0
TotalJumps >= 0
TotalTrims >= 0
TotalColorChanges >= 0
TotalStops >= 0
```

Y:

```text
double.IsFinite(EstimatedTimeSeconds)
double.IsFinite(EstimatedThreadMeters)
```

Además:

```text
EstimatedTimeSeconds >= 0
EstimatedThreadMeters >= 0
```

---

# 9. CORRECCIÓN 6 — CONSISTENCIA DE MÉTRICAS

El archivo no debe poder declarar:

```text
stitchCount = 100
TotalStitches = 999999
```

sin que el parser lo detecte.

Como mínimo validar:

```text
TotalStitches
<=
stitchCount
```

si el significado del formato actual así lo determina.

Si el modelo permite que `stitchCount` incluya jumps/trims:

```text
TotalStitches
+
TotalJumps
+
TotalTrims
+
TotalStops
...
```

debe documentarse exactamente.

No inventar una ecuación si el modelo actual no la garantiza.

El objetivo es definir la semántica del contador.

---

# 10. CORRECCIÓN 7 — PROJECT INPUT INMUTABLE DURANTE COMPILE

Este es un bloqueador importante.

Actualmente:

```text
Compile(project)
```

puede modificar:

```text
project.Objects[*].StitchParams
```

mediante:

```text
ApplyWorkProfile()
```

Esto rompe:

```text
same input
→
same output
```

porque el input deja de ser el mismo.

## Solución

Crear una copia de los parámetros:

```text
original StitchParams
        ↓
DeepClone / Clone
        ↓
effective StitchParams
        ↓
ApplyWorkProfile
        ↓
generation
```

Nunca:

```text
project object
↓
mutate
```

durante una compilación.

---

# 11. CORRECCIÓN 8 — DETERMINISMO REAL CON WORK PROFILE

Agregar prueba:

```text
Compile_WithWorkProfile_IsDeterministic
```

Flujo:

```text
project original
↓
deep clone A
↓
deep clone B
↓
Compile A
Compile B
```

Verificar:

```text
same stitches
same coordinates
same types
same color
same needle
same sequence
same metrics
same bounds
```

Además verificar:

```text
original project
```

no fue modificado por ninguno de los dos Compile.

---

# 12. CORRECCIÓN 9 — MAX STITCHES PER OBJECT

Actualmente el límite se aplica después de:

```text
underlay
tie-in
tie-off
```

y mediante:

```csharp
Take(MaxStitchesPerObject)
```

Esto puede cortar estructura esencial.

## Debe definirse una política

Preferencia Foundation:

```text
generación
↓
validación
↓
budget
↓
resultado válido
```

Si excede:

```text
MaxStitchesPerObject
```

preferentemente:

```text
controlled failure
```

en lugar de truncamiento silencioso.

Alternativamente:

```text
truncamiento explícito
+
diagnóstico
+
garantía de integridad estructural
```

Pero no:

```text
Take()
```

ciego.

---

# 13. CORRECCIÓN 10 — MAX STITCHES TEST

Agregar:

```text
MaxStitchesPerObject_Exceeded
```

Debe verificar:

```text
input exceeds limit
↓
defined behavior
```

Y no simplemente:

```text
count <= max
```

si el objeto queda mutilado.

También:

```text
TieOffPreserved
UnderlayIntegrityPreserved
SequenceIntegrityPreserved
```

cuando el diseño pueda producir esos elementos.

---

# 14. CORRECCIÓN 11 — SEQUENCE INDEX

Mantener:

```text
ushort
```

durante Foundation si forma parte deliberada del formato V1.

Contrato:

```text
0..65534
=
válido
```

```text
65535+
=
rechazado
```

El valor:

```text
65535
```

no debe confundirse con:

```text
65536 elementos
```

Debe documentarse:

```text
máximo número de puntadas representables en V1
```

y el motivo.

---

# 15. CORRECCIÓN 12 — BINARY FORMAT V1

Contrato definitivo:

```text
Magic = ATB1
Version = 1
```

V1:

```text
solo acepta version 1
```

Rechazar:

```text
0
2
3
ushort.MaxValue
```

hasta que exista decoder explícito.

No utilizar:

```text
version <= current
```

como sustituto de versioning real.

---

# 16. CORRECCIÓN 13 — TRAILING DATA

Mantener:

```text
ms.Position == ms.Length
```

para V1.

Un archivo:

```text
valid payload
+
garbage
```

debe rechazarse.

Cuando exista una futura versión con extensiones:

```text
payload length
+
extension blocks
```

debe definirse formalmente.

No cambiar este comportamiento durante Foundation.

---

# 17. CORRECCIÓN 14 — VARINT

Mantener:

```text
máximo 5 bytes
```

y:

```text
validación quinto byte
```

Agregar pruebas de:

```text
0
1
-1
int.MinValue
int.MaxValue
5th byte valid
5th byte invalid
6 bytes
unterminated sequence
EOF
```

Debe garantizarse:

```text
no infinite loop
no overflow silencioso
no undefined decode
```

---

# 18. CORRECCIÓN 15 — STRING COUNT BUDGET

Además del límite individual:

```text
10000 bytes/string
```

considerar:

```text
paletteCount * strings
```

Un archivo con:

```text
10000 colors
x
4 strings
x
10000 bytes
```

puede ser excesivo.

Implementar un contador de presupuesto consumido.

Conceptualmente:

```text
documentBudget
↓
cada string consume bytes
↓
cada collection consume entries
↓
cada stitch consume bytes
↓
si budget excedido
→ reject
```

---

# 19. CORRECCIÓN 16 — SATIN

Satin actual debe considerarse:

```text
Foundation simplified satin
```

No:

```text
industrial satin digitizer
```

Foundation sólo exige:

```text
no crash
bounded
deterministic
valid stitch output
```

Los siguientes temas pertenecen a Phase 4:

```text
column rails
centerline
turning
corner strategy
split columns
short-stitch compensation
pull compensation
density transitions
underlay strategies
```

No intentar completar todo ahora.

---

# 20. CORRECCIÓN 17 — TATAMI

Igualmente:

```text
Foundation Tatami
=
basic bounded fill
```

No declararlo como digitización profesional completa.

Phase 4 será responsable de:

```text
density map
edge compensation
underlay
pattern angle
row staggering
short fills
complex polygons
holes
multiple regions
islands
```

---

# 21. CORRECCIÓN 18 — PARÁMETROS SILENCIOSOS

Los defaults actuales de Satin/Tatami pueden mantenerse durante Foundation.

Pero deben quedar documentados como:

```text
normalization
```

No:

```text
validation failure
```

Ejemplo:

```text
Density <= 0
→ normalized to safe default
```

No afirmar:

```text
input valid
```

si en realidad fue corregido.

---

# 22. CORRECCIÓN 19 — TIE-IN / TIE-OFF

Mantener protección contra:

```text
TieStitchCount <= 0
TieInLength < 0
TieOffLength < 0
```

Pero documentar:

```text
normalization policy
```

Y agregar pruebas de:

```text
0
negative
less than stitch count
equal
greater
```

El objetivo es:

```text
finite
bounded
deterministic
```

---

# 23. CORRECCIÓN 20 — UNDERLAY

Debe mantenerse:

```text
EnableUnderlay
```

y:

```text
no recursive underlay
```

Además:

```text
ColorIndex
NeedleIndex
Flags
SequenceIndex
```

deben conservar la identidad del objeto principal.

---

# 24. CORRECCIÓN 21 — OPTIMIZATION

Foundation sólo ofrece:

```text
color grouping
+
needle grouping
+
deterministic object ordering
```

No afirmar:

```text
optimal travel
```

La optimización avanzada será Phase 4/6 según necesidad.

Debe documentarse:

```text
Foundation Optimization
```

como:

```text
deterministic grouping heuristic
```

---

# 25. CORRECCIÓN 22 — OPTIMIZATION STABILITY

La secuencia debe ser estable ante empates.

Definir un tercer criterio:

```text
original sequence/order
```

o un identificador estable.

Ejemplo:

```text
Color
↓
Needle
↓
Original SequenceOrder
```

No depender únicamente de:

```text
Dictionary enumeration
```

como contrato de negocio.

---

# 26. CORRECCIÓN 23 — HASH

Mantener el comportamiento:

```text
same logical project
→
same hash
```

Y:

```text
geometry change
→
hash change

stitch parameters change
→
hash change
```

Excluir:

```text
volatile metadata
```

Pero revisar periódicamente qué campos son:

```text
identity
metadata
content
configuration
```

No excluir automáticamente un campo sólo porque sea incómodo.

---

# 27. CORRECCIÓN 24 — HASH + COLLECTION ORDER

Determinar si el orden de:

```text
Objects
ThreadPalette
ColorToNeedleMap
```

forma parte del contenido lógico.

Si un Dictionary cambia su orden de inserción pero representa el mismo mapping:

```text
same logical mapping
```

idealmente:

```text
same hash
```

si el contrato del proyecto define hash semántico.

Si el orden sí importa:

```text
documentarlo
```

---

# 28. CORRECCIÓN 25 — FILE STORE

Foundation FileStore debe garantizar:

```text
root containment
path normalization
invalid filename rejection
directory handling
overwrite policy
corrupt file handling
```

Especial atención:

```text
../
..\ 
absolute paths
UNC paths
drive-qualified paths
reserved names
```

Debe existir test de traversal real.

---

# 29. CORRECCIÓN 26 — ATOMIC WRITE

Cuando el proyecto se guarde:

```text
write temp
↓
flush
↓
replace
```

o mecanismo equivalente.

Evitar:

```text
File.WriteAllBytes(final)
```

si un fallo durante escritura puede dejar el archivo final corrupto.

Si Atomic Write todavía no forma parte de Foundation FileStore:

```text
documentarlo como requisito mínimo de Phase 2
```

pero no fingir que existe.

---

# 30. CORRECCIÓN 27 — SVG INPUT

SVG debe ser tratado como input no confiable.

Debe manejar:

```text
malformed XML
empty file
huge file
invalid coordinates
NaN
Infinity
degenerate paths
unsupported commands
```

No permitir dependencias externas.

No permitir:

```text
network fetch
external entity resolution
```

---

# 31. CORRECCIÓN 28 — IMAGE INPUT

`GenerateImageStitches()` continúa siendo placeholder.

Eso está permitido.

Debe quedar explícito:

```text
Image digitization
=
NOT Foundation feature
```

Phase 4 deberá implementar:

```text
raster preprocessing
segmentation
color reduction
vectorization
stitch mapping
```

---

# 32. CORRECCIÓN 29 — TEXT INPUT

`GenerateTextStitches()` también continúa siendo placeholder.

Foundation no necesita un font engine completo.

Phase 4:

```text
font loading
glyph outlines
kerning
text layout
stroke/fill strategy
```

---

# 33. CORRECCIÓN 30 — RANDOM SEED

Actualmente:

```text
RandomSeed
```

no tiene efecto.

Esto debe permanecer documentado.

No usar el nombre como promesa falsa de reproducibilidad.

Cuando exista aleatoriedad real:

```text
RandomSeed
```

deberá formar parte del input lógico del hash y del determinismo.

---

# 34. CORRECCIÓN 31 — METRICS RESET

`CalculateMetrics()` debe garantizar que los diccionarios:

```text
StitchesPerColor
ThreadMetersPerColor
```

no acumulen valores si el método se reutiliza.

Antes de recalcular:

```text
Clear()
```

o reconstruirlos.

---

# 35. CORRECCIÓN 32 — METRIC SEMANTICS

Documentar:

```text
TotalStitches
TotalJumps
TotalTrims
TotalColorChanges
TotalStops
```

y distinguir:

```text
generated stitches
sewing stitches
control stitches
```

No asumir que:

```text
GlobalSequence.Count == TotalStitches
```

porque existen:

```text
jump
trim
stop
tie
underlay
```

---

# 36. CORRECCIÓN 33 — FINITE GEOMETRY

Todos los puntos derivados de:

```text
angle
rotation
density
spacing
intersections
compensation
```

deben evitar producir:

```text
NaN
Infinity
overflow
```

Antes de convertir:

```text
double
→
int
```

debe existir un límite razonable.

---

# 37. CORRECCIÓN 34 — INTEGER OVERFLOW

Revisar:

```text
x + deltaX
y + deltaY
width + x
height + y
distance calculations
count calculations
```

Un archivo malicioso no debe convertir:

```text
int.MaxValue + 100
```

en coordenadas aparentemente válidas.

---

# 38. CORRECCIÓN 35 — TEST MATRIX

La suite Foundation final debe cubrir:

```text
normal
empty
minimum
maximum
negative
zero
overflow
truncated
malformed
duplicate
degenerate
future version
unknown version
invalid encoding
invalid path
```

No todos los algoritmos requieren todas las categorías.

Pero cada parser sí debe tener:

```text
happy path
boundary
corruption
```

---

# 39. GATE A — BUILD

Ejecutar:

```powershell
dotnet restore
dotnet build --configuration Release
```

Resultado:

```text
0 errors
0 warnings
```

---

# 40. GATE B — TEST

Ejecutar:

```powershell
dotnet test --configuration Release --no-restore
```

Resultado:

```text
100% passing
```

---

# 41. GATE C — SERIALIZATION

Debe demostrar:

```text
JSON round-trip
Binary round-trip
Version 1
Version 0 rejected
Future version rejected
String bounds
Strict UTF-8
VarInt boundaries
Truncated files
Trailing bytes
Metric validation
Budget validation
```

---

# 42. GATE D — STITCH ENGINE

Debe demostrar:

```text
Running
Triple
Satin
Tatami
Zigzag
Underlay
Tie-in
Tie-off
Optimization
Metrics
Sequence
```

---

# 43. GATE E — DETERMINISM

Debe comprobarse con:

```text
default profile
work profile
thread profile
machine profile
optimization enabled
optimization disabled
underlay enabled
underlay disabled
```

El resultado debe ser reproducible.

Además:

```text
Compile()
```

no debe modificar el proyecto fuente.

---

# 44. GATE F — INPUT SECURITY

Debe rechazar o controlar:

```text
invalid magic
invalid version
invalid VarInt
invalid UTF-8
invalid string size
invalid collection count
inflated stitch count
truncated file
trailing garbage
invalid metrics
NaN
Infinity
overflow
malformed SVG
path traversal
```

---

# 45. GATE G — REPOSITORY HYGIENE

Confirmar:

```text
AtlasEmbroidery
```

contiene únicamente su propio dominio.

No introducir:

```text
AtlasMI
AtlasMail
AtlasSEP
AtlasNOC
otros proyectos
```

---

# 46. CIERRE DEFINITIVO DE FOUNDATION

Cuando A-G estén PASS:

```text
FOUNDATION = CLOSED / VERIFIED
```

Ese cierre significa:

```text
Foundation feature-complete
```

No significa:

```text
producto terminado
```

No significa:

```text
digitizador profesional terminado
```

No significa:

```text
machine bridge terminado
```

No significa:

```text
IA terminada
```

Significa:

```text
el núcleo ya es una base técnica estable
```

---

# 47. PHASE 2 — FORMATS & NORMALIZATION

Objetivo:

```text
convertir formatos externos
↔
modelo AtlasEmbroidery
```

Arquitectura:

```text
IEmbroideryFormatReader
IEmbroideryFormatWriter
IEmbroideryNormalizer
IEmbroideryFormatValidator
```

Primero:

```text
DST
```

Después, sólo cuando exista evidencia real:

```text
PES
PEC
JEF
EXP
VP3
U01
XXX
TBF
```

Cada formato debe tener:

```text
Reader
Writer
Header handling
Color handling
Needle handling
Jump handling
Trim handling
Stop handling
Bounds
Metadata
Round-trip
Malformed tests
Golden files
```

No afirmar:

```text
PES supported
```

hasta tener pruebas reales.

---

# 48. PHASE 3 — VALIDATOR V1

Crear:

```text
EmbroideryValidator
```

Reglas:

```text
Geometry
Bounds
Hoop
Stitch length
Density
Jump length
Trim count
Color changes
Needle count
Micro stitches
Duplicate stitches
Sequence
Machine constraints
```

Resultado:

```text
PASS
WARNING
CRITICAL
UNKNOWN
```

Cada warning:

```text
RuleId
Evidence
Message
Recommendation
```

---

# 49. PHASE 4 — BASIC DIGITIZATION

Convertir geometría en puntadas profesionales progresivamente.

## Running

```text
spacing
corners
closed paths
```

## Triple

```text
repeat strategy
```

## Satin

```text
rails
centerline
column generation
turning
corners
density
```

## Tatami

```text
polygon fill
holes
islands
density
angle
underlay
edge compensation
```

## Zigzag

```text
width
spacing
turning
```

## Pull Compensation

```text
fabric
density
direction
```

---

# 50. PHASE 5 — MATERIAL LAB

Modelo:

```text
Material
Thread
Needle
Stabilizer
Machine
Hoop
WorkProfile
```

Perfiles iniciales:

```text
cotton
pique
denim
towel
cap
patch
hoodie
canvas
uniform
delicate
unknown
```

`Unknown` debe ser conservador.

---

# 51. PHASE 6 — SIMULATION V2

Agregar:

```text
playback
layers
stitch speed
jump visualization
trim visualization
density map
thread estimate
time estimate
hoop boundaries
machine limits
```

La simulación debe distinguir:

```text
model
```

de:

```text
physical evidence
```

No afirmar que la simulación reemplaza un stitch-out.

---

# 52. PHASE 7 — MACHINE BRIDGE

Arquitectura:

```text
Format Adapter
        ↓
Transport Adapter
        ↓
Machine Adapter
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

Nunca inventar protocolos.

Cada máquina:

```text
manufacturer
model
firmware
format
transport
evidence
```

---

# 53. PHASE 8 — ATLAS DOCTOR

Entrada:

```text
symptom
machine
material
design
job
history
```

Salida:

```text
possible causes
evidence
safe test
one-variable change
result
```

Problemas:

```text
thread break
needle break
bird nest
skipped stitches
puckering
gaps
registration
trim
transfer
file
power
```

Primero determinista.

IA después.

---

# 54. PHASE 9 — PRODUCTION

Entidades:

```text
Customer
Quote
Order
Job
Machine
Inventory
Thread
Needle
Material
Quality
Rework
History
```

Crear:

```text
JobPackage
```

Debe contener:

```text
design hash
design version
artifact
machine
material
thread
needle
hoop
validator result
instructions
first article result
```

---

# 55. PHASE 10 — LEARNING / LAB

Registrar:

```text
design
parameters
machine
material
result
incident
```

Feedback:

```text
Good
Bad
Comment
```

Separar:

```text
private knowledge
```

de:

```text
shared knowledge
```

---

# 56. PHASE 11 — RELIABILITY / RECOVERY

Agregar:

```text
audit
checksums
backup
restore
health
recovery
emergency mode
```

La prueba real será:

```text
job
↓
failure
↓
restore
↓
verify hash
↓
resume
↓
no duplicate execution
```

Un backup sólo es válido cuando se restaura correctamente.

---

# 57. PHASE 12 — AI ASSISTED EMBROIDERY

La IA podrá:

```text
analyze design
suggest digitization
suggest parameters
detect risks
suggest corrections
help diagnose failures
```

Flujo obligatorio:

```text
AI proposal
↓
Validator
↓
Human approval
↓
Execution
↓
Audit
```

Nunca:

```text
AI
↓
machine
```

sin validación y autorización.

---

# 58. ARQUITECTURA MAESTRA FINAL

```text
                    DESIGN
                       │
                       ▼
                 GEOMETRY MODEL
                       │
                       ▼
                DIGITIZATION
                       │
                       ▼
                 STITCH PLAN
                       │
                       ▼
                  VALIDATOR
                       │
                       ▼
                 SIMULATION
                       │
                       ▼
                   ARTIFACT
                       │
                       ▼
                 MACHINE BRIDGE
                       │
                       ▼
                  PRODUCTION
                       │
                       ▼
                    QUALITY
                       │
                       ▼
                   EVIDENCE
                       │
                       ▼
                  KNOWLEDGE
                       │
                       ▼
                      AI
```

La dirección de dependencia debe ser controlada.

No crear dependencia circular entre:

```text
AI
Production
Machine
Domain
```

---

# 59. PRINCIPIO DE SEGURIDAD

Toda automatización futura debe mantener:

```text
Proposal
↓
Validation
↓
Authorization
↓
Execution
↓
Evidence
```

No:

```text
Proposal
↓
Execution
```

---

# 60. PRINCIPIO DE REPRODUCIBILIDAD

Un diseño debe poder responder:

```text
¿Qué se generó?
¿Por qué?
¿Con qué parámetros?
¿Con qué máquina?
¿Con qué material?
¿Con qué versión?
¿Con qué formato?
¿Con qué algoritmo?
```

Por eso deben existir:

```text
hash
version
parameters
artifact
machine profile
material profile
validator result
```

---

# 61. PRINCIPIO DE NO INVENTAR

Nemotron nunca debe inventar:

```text
machine protocol
format support
machine capability
hardware behavior
AI capability
test result
coverage
compatibility
```

Si no existe evidencia:

```text
UNKNOWN
```

es un resultado válido.

---

# 62. PROCEDIMIENTO OBLIGATORIO DEL AGENTE

Siempre:

```text
READ
↓
UNDERSTAND
↓
PLAN
↓
EDIT
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

Nunca:

```text
guess
↓
edit
↓
claim pass
```

---

# 63. COMMITS

Preferir commits pequeños y semánticos:

```text
fix: harden strict utf8 decoding
test: add invalid utf8 coverage
fix: prevent work profile mutation
test: verify compile determinism with work profile
fix: validate binary metrics
test: add metric corruption cases
fix: enforce stitch budget semantics
test: add max stitch integrity coverage
```

No mezclar:

```text
feature
+
refactor massive
+
formatting
+
unrelated cleanup
```

---

# 64. RESULTADO FINAL OBLIGATORIO

Nemotron debe entregar:

```text
1. SHA final
2. archivos modificados
3. correcciones realizadas
4. tests agregados
5. tests ejecutados
6. build ejecutado
7. warnings
8. errors
9. coverage
10. determinism result
11. serialization result
12. security result
13. Gate A
14. Gate B
15. Gate C
16. Gate D
17. Gate E
18. Gate F
19. Gate G
20. Foundation final status
```

Si todo está correcto:

```text
FOUNDATION CLOSED / VERIFIED
```

Si un gate falla:

```text
FOUNDATION NOT CLOSED
```

---

# 65. DESPUÉS DEL CIERRE

Una vez cerrado Foundation:

```text
NO REOPEN FOUNDATION
```

para agregar:

```text
PES
JEF
advanced satin
advanced tatami
machine bridge
AI
production
```

Cada cosa debe ir a su fase.

---

# 66. DEFINICIÓN DE PRODUCTO FINAL

AtlasEmbroidery debe evolucionar hacia:

```text
DESIGN
   ↓
DIGITIZE
   ↓
VALIDATE
   ↓
SIMULATE
   ↓
EXPORT
   ↓
MACHINE
   ↓
PRODUCE
   ↓
VERIFY
   ↓
LEARN
   ↓
IMPROVE
```

Y eventualmente:

```text
AI
```

como asistente sobre ese sistema.

---

# 67. PRINCIPIO RECTOR FINAL

AtlasEmbroidery no se construye acumulando funciones.

Se construye acumulando:

```text
evidence
+
determinism
+
validation
+
reproducibility
+
safe automation
```

Primero:

```text
CORE
```

Después:

```text
CAPABILITY
```

Después:

```text
AUTOMATION
```

Después:

```text
INTELLIGENCE
```

Nunca al revés.
