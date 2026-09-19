# AtlasEmbroidery

## MD Quirúrgico Final — Auditoría Foundation, Correcciones y Roadmap Completo

**Repositorio:** `akamike17/AtlasEmbroidery`
**Commit auditado:** `160726782f378eb6ee1653896cd97235ba16757d`
**Rama de trabajo:** la rama activa del repositorio
**Agente:** Nemotron
**Objetivo:** cerrar Foundation correctamente y dejar definido el roadmap completo sin crear fases artificiales.

---

# 1. REGLA PRINCIPAL

No declarar Foundation cerrada únicamente porque:

```text
build PASS
+
tests PASS
+
coverage PASS
```

Foundation sólo puede declararse:

```text
CLOSED / VERIFIED
```

cuando el comportamiento real del código coincide con el contrato técnico y los tests demuestran las condiciones críticas.

La prioridad es:

```text
correctness
>
security
>
determinism
>
data integrity
>
boundary behavior
>
coverage
```

La cobertura nunca sustituye una prueba funcional real.

---

# 2. ESTADO REAL DEL COMMIT 1607267

El commit `1607267` incorporó hardening importante:

* protección de `SequenceIndex`;
* límites de conteos binarios;
* protección de VarInt;
* pruebas de round-trip;
* pruebas de determinismo;
* pruebas de parámetros;
* protección de tie-in/tie-off;
* corrección de underlay;
* implementación limitada de `OptimizePlan`;
* 46 tests nuevos.

Pero la auditoría profunda demuestra que todavía existen puntos que deben corregirse antes del cierre formal.

Por lo tanto:

```text
FOUNDATION = NOT CLOSED
```

hasta completar este documento.

No abrir todavía Phase 2.

---

# 3. OBJETIVO DE ESTA ITERACIÓN

Esta iteración no debe agregar características grandes.

Debe hacer exclusivamente:

```text
auditoría
↓
corrección
↓
regresión
↓
validación
↓
cierre
```

No implementar todavía:

* Machine Bridge;
* IA;
* producción;
* UI profesional completa;
* nuevos formatos masivos;
* nube;
* clientes;
* inventario;
* comunidad.

---

# 4. CORRECCIÓN 1 — VERSIONADO BINARIO

Archivo:

```text
Domain/Serialization/AtlasSerializer.cs
```

Actualmente:

```csharp
if (version > Version)
    return null;
```

Problema:

```text
version 0
```

puede continuar aunque el decoder esté implementado para la estructura actual.

## Requisito

Si sólo existe decoder para versión 1:

```text
version != 1
→ reject
```

Alternativamente:

```text
version == 1
→ decoder V1

version == 2
→ decoder V2

etc.
```

Nunca interpretar una versión desconocida como si fuera compatible.

## Tests obligatorios

Agregar:

```text
Version 0 → null
Version 1 → PASS
Version 2 → null
Version ushort.MaxValue → null
```

---

# 5. CORRECCIÓN 2 — READSTRINGSAFE REALMENTE SEGURO

Actualmente:

```text
ReadString()
↓
materializa string
↓
comprueba longitud
```

Eso no proporciona protección suficiente contra entradas hostiles.

## Requisito

Implementar lectura explícita del tamaño UTF-8 antes de materializar.

Flujo:

```text
leer longitud 7-bit
↓
validar longitud
↓
comprobar bytes disponibles
↓
leer exactamente N bytes
↓
decodificar UTF-8
↓
devolver string
```

Límite Foundation recomendado:

```text
MAX_STRING_BYTES = 10000
```

o un valor equivalente documentado.

## Reglas

Nunca:

```text
archivo externo
→ allocation gigante
→ después validar
```

Debe ser:

```text
archivo externo
→ validar tamaño
→ allocation controlada
```

## Corrupción

No devolver:

```csharp
string.Empty
```

cuando la entrada es inválida.

Debe propagarse un error de formato controlado hasta:

```text
Deserialize()
→ null / resultado de error definido
```

---

# 6. CORRECCIÓN 3 — EXCEPCIONES DEL DESERIALIZADOR

Eliminar el patrón indiscriminado:

```csharp
catch (Exception)
{
    return null;
}
```

Separar como mínimo:

```text
EndOfStreamException
InvalidDataException
Decoder/format errors
```

de errores internos de programación.

## Objetivo

Un archivo corrupto:

```text
→ resultado controlado
```

Un bug del programa:

```text
→ no debe desaparecer silenciosamente
```

Si se decide utilizar una excepción interna general como frontera final, deberá registrarse/documentarse correctamente y no utilizarse para ocultar bugs durante desarrollo.

---

# 7. CORRECCIÓN 4 — VARINT

El límite de 5 bytes es correcto como primera protección, pero falta validar el quinto byte.

Para un `uint32/int32`:

```text
bytes 1-4
→ hasta 7 bits cada uno

byte 5
→ sólo los bits válidos para los 32 bits restantes
```

Un quinto byte con bits superiores inválidos debe rechazarse.

## Tests

Agregar:

```text
int.MinValue
int.MaxValue
0
-1
1
máximo encoding válido
quinto byte inválido
6+ bytes
EOF
```

---

# 8. CORRECCIÓN 5 — BUDGET DEL DESERIALIZADOR

No basta con:

```text
count <= 10,000,000
```

Debe existir un presupuesto defensivo de datos.

Antes de reservar estructuras grandes, evaluar:

```text
count
+
mínimo de bytes por elemento
<= bytes restantes
```

Ejemplo conceptual:

```text
stitchCount = 10,000,000
bytes restantes = 100
→ reject inmediatamente
```

Esto evita reservas absurdas ante archivos truncados o maliciosos.

No es necesario calcular el tamaño exacto de cada VarInt antes de leerlo; sí debe existir una comprobación conservadora.

---

# 9. CORRECCIÓN 6 — TRAILING DATA

Después de deserializar una versión cerrada:

```text
stream position
==
stream length
```

debe cumplirse.

Si se desean extensiones futuras, el formato debe definir explícitamente:

```text
payload length
+
extension section
```

No aceptar bytes basura accidentalmente.

Test:

```text
valid binary
+
garbage bytes
→ reject
```

---

# 10. CORRECCIÓN 7 — SEQUENCEINDEX

La protección actual:

```csharp
if (seqIndex > ushort.MaxValue)
    throw ...
```

es conceptualmente correcta.

Pero el test actual NO prueba realmente el overflow.

## Requisito

Crear una prueba que realmente provoque:

```text
GlobalSequence.Count > ushort.MaxValue
```

sin crear una prueba absurdamente lenta.

Si se requiere refactorizar `CalculateMetrics` para inyectar una secuencia grande o separar la asignación de índice en una función testeable, hacerlo.

Debe existir evidencia real de:

```text
65535
→ válido

65536
→ controlled failure
```

---

# 11. CORRECCIÓN 8 — MÉTRICAS

Actualmente:

```csharp
EstimatedTimeSeconds =
    TotalStitches * 60.0 / speed;
```

No puede aceptar:

```text
speed == 0
speed < 0
```

## Contrato

Si velocidad inválida:

```text
reject
```

o:

```text
normalización explícita + warning
```

pero nunca:

```text
Infinity
```

ni:

```text
negative time
```

## Tests

```text
speed = 0
speed = -1
speed = normal
speed = int.MaxValue
```

Y:

```text
double.IsFinite(EstimatedTimeSeconds)
```

debe cumplirse cuando el plan sea válido.

---

# 12. CORRECCIÓN 9 — OPCIONES DEL STITCH ENGINE

Auditar:

```text
MaxStitchesPerObject
EnableUnderlay
EnableAutoTrim
EnableOptimization
RandomSeed
```

Actualmente existen opciones cuyo comportamiento no está completamente conectado.

Cada opción debe cumplir:

```text
declarada
+
utilizada
+
testeada
+
documentada
```

## MaxStitchesPerObject

Debe realmente impedir:

```text
object stitches > configured maximum
```

Nunca reservar/generar indefinidamente.

## EnableUnderlay

Debe controlar si el underlay se ejecuta.

## EnableOptimization

Debe controlar si la optimización se ejecuta.

## EnableAutoTrim

Debe controlar la política correspondiente.

## RandomSeed

Si no existe aleatoriedad actualmente:

```text
documentar que actualmente no tiene efecto
```

No fingir que existe reproducibilidad basada en seed si no se utiliza.

---

# 13. CORRECCIÓN 10 — OPTIMIZEPLAN

El código actual hace:

```text
OrderBy(Color)
ThenBy(Needle)
```

pero posteriormente:

```text
CalculateMetrics()
→ ObjectStitches.OrderBy(Key)
```

por lo que el orden de optimización puede quedar anulado.

## Solución

Definir claramente cuál estructura representa la secuencia oficial.

Preferencia:

```text
ObjectStitches
↓
Optimization
↓
GlobalSequence
```

y `GlobalSequence` debe respetar el orden producido por la optimización.

No volver a ordenar por `Guid` después.

## No venderlo como optimización avanzada

Foundation sólo necesita:

```text
basic deterministic grouping
```

No:

```text
multi-objective optimization
```

La documentación debe decir exactamente:

```text
Foundation optimization:
grouping by color/needle according to deterministic policy.
```

---

# 14. CORRECCIÓN 11 — TEST REAL DE OPTIMIZACIÓN

El test actual comprueba principalmente que:

```text
OptimizePlan existe
```

Eso no es suficiente.

Crear dos objetos deliberadamente desordenados:

```text
Object A → color 2
Object B → color 1
```

y comprobar:

```text
input order != optimized order
```

y:

```text
GlobalSequence
```

debe reflejar realmente el orden optimizado.

También comprobar:

```text
same input
→ same optimized sequence
```

---

# 15. CORRECCIÓN 12 — PARÁMETROS INVÁLIDOS

Actualmente se utilizan defaults silenciosos:

```text
SatinSpacing <= 0
→ 200
```

```text
Density <= 0
→ 400
```

Eso puede modificar un diseño sin informar.

## Contrato recomendado Foundation

Separar:

```text
hard invalid
soft invalid
```

### Hard invalid

Ejemplo:

```text
speed <= 0
```

Debe rechazarse.

### Soft invalid

Ejemplo:

```text
spacing == 0
```

Puede normalizarse si existe un default técnico válido.

Pero debe quedar explícito:

```text
input
→ normalized value
→ diagnostic/warning
```

Nunca cambiar silenciosamente.

---

# 16. CORRECCIÓN 13 — SATIN

Auditar:

```text
ColumnWidth
SatinSpacing
MinColumnWidth
path count
zero-length segments
```

Casos obligatorios:

```text
ColumnWidth = 0
ColumnWidth < 0
Spacing = 0
Spacing < 0
path < 2 points
duplicated points
degenerate geometry
```

Cada caso debe tener comportamiento definido.

---

# 17. CORRECCIÓN 14 — TATAMI

Auditar:

```text
Density
RowSpacing
polygon
intersections
alternate rows
offset
```

Casos:

```text
Density = 0
Density < 0
RowSpacing = 0
RowSpacing < 0
2-point polygon
3-point degenerate polygon
duplicate vertices
zero-area polygon
odd intersections
```

Nunca asumir que:

```text
intersections.Count % 2 == 0
```

si el algoritmo no lo garantiza.

---

# 18. CORRECCIÓN 15 — TIE-IN / TIE-OFF

La división por cero ya fue protegida.

Pero validar además:

```text
TieStitchCount <= 0
TieInLength < TieStitchCount
TieOffLength < TieStitchCount
TieInLength = 0
TieOffLength = 0
```

Debe evitar:

```text
NaN
Infinity
desplazamientos absurdos
```

---

# 19. CORRECCIÓN 16 — UNDERLAY

Mantener:

```text
GenerateStitchesForObjectNoUnderlay()
```

para impedir recursión.

Comprobar:

```text
underlay enabled
↓
terminates
↓
main stitches exist
↓
underlay stitches exist
↓
no recursive underlay
↓
sequence valid
```

Además:

```text
ColorIndex
NeedleIndex
Flags
SequenceIndex
```

deben tener contrato explícito.

---

# 20. CORRECCIÓN 17 — JSON ROUND-TRIP

Verificar:

```text
AtlasProject
→ JSON
→ AtlasProject
```

Debe preservar semánticamente:

```text
geometry
objects
IDs
StitchParams
colors
paths
stitches
sequence
profiles
configuration
```

No se requiere igualdad byte a byte del JSON.

Sí:

```text
semantic equivalence
```

---

# 21. CORRECCIÓN 18 — HASH

`ComputeContentHash()` debe conservar:

```text
same logical content
→ same hash
```

Y detectar:

```text
geometry change
→ different hash

stitch parameter change
→ different hash
```

Metadata volátil:

```text
CreatedAt
ModifiedAt
LastSavedAt
ContentHash
StitchPlanHash
```

no debe modificarlo.

Revisar también IDs y perfiles para determinar cuáles representan contenido real y cuáles son identidad/metadata.

---

# 22. CORRECCIÓN 19 — INPUT FILE STORE / PATHS

Antes de considerar Foundation cerrada, revisar cualquier almacenamiento de proyectos/templates existente.

Obligatorio:

```text
path traversal
invalid filename
directory missing
partial file
corrupt file
overwrite
atomic write
```

Nunca:

```text
filename externo
→ Path.Combine(root, filename)
```

sin normalización y comprobación de que el resultado permanece dentro del root permitido.

---

# 23. CORRECCIÓN 20 — SVG COMO INPUT EXTERNO

Auditar:

```text
malformed XML
empty SVG
huge input
deep nesting
invalid coordinates
NaN
Infinity
degenerate geometry
unsupported commands
```

Debe permanecer:

```text
offline
deterministic
bounded
```

No introducir dependencia de red.

---

# 24. CORRECCIÓN 21 — DEPENDENCIAS

Actualmente el proyecto `net8.0` utiliza:

```text
SkiaSharp 3.116.1
System.Text.Json 9.0.0
Microsoft.Extensions.Logging.Abstractions 9.0.0
Microsoft.Extensions.Options.ConfigurationExtensions 9.0.0
System.Drawing.Common 9.0.0
```

No actualizar por gusto.

Nemotron debe determinar:

```text
necesaria
innecesaria
compatible
plataforma específica
duplicada
```

Si se conserva una dependencia de versión mayor que el runtime objetivo:

```text
documentar razón
```

No hacer upgrade masivo durante Foundation hardening.

---

# 25. CORRECCIÓN 22 — TESTS HONESTOS

Los tests actuales son buenos como primera batería, pero algunos prueban:

```text
"no explota"
```

cuando deberían probar:

```text
"produce el comportamiento correcto"
```

Ejemplos:

### Sequence overflow

No basta reflection.

### Speed zero

No basta:

```text
>= 0
```

porque Infinity cumple.

Debe ser:

```text
finite
```

### OptimizePlan

No basta verificar que existe.

Debe verificarse la secuencia.

### Parameter validation

No basta:

```text
plan != null
```

Debe verificarse:

```text
resultado
+
diagnóstico
+
normalización
```

cuando corresponda.

---

# 26. GATES DE FOUNDATION

## Gate A — BUILD

```powershell
dotnet restore
dotnet build --configuration Release
```

Resultado requerido:

```text
0 errors
0 warnings
```

---

## Gate B — TESTS

```powershell
dotnet test --configuration Release --no-restore
```

Requerido:

```text
100% PASS
```

---

## Gate C — SERIALIZATION

Debe pasar:

```text
JSON round-trip
Binary round-trip
version handling
malformed input
truncated input
string bounds
VarInt boundaries
hash determinism
```

---

## Gate D — STITCH ENGINE

Debe pasar:

```text
running
triple
satin
tatami
zigzag
underlay
tie-in
tie-off
sequence
metrics
optimization
```

---

## Gate E — DETERMINISM

Dos compilaciones:

```text
same project
same configuration
```

deben producir:

```text
same logical stitches
same coordinates
same type
same color
same needle
same flags
same sequence
same metrics
same bounds
```

---

## Gate F — INPUT SECURITY

Debe rechazarse/controlarse:

```text
invalid magic
invalid version
version 0
future version
negative counts
absurd counts
truncated data
invalid VarInt
invalid string length
invalid UTF-8
trailing garbage
malformed geometry
invalid parameters
```

---

## Gate G — REPOSITORY HYGIENE

Confirmar:

```text
AtlasEmbroidery
≠ AtlasMI
≠ AtlasMail
≠ otros proyectos
```

No mover commits anteriores.

No borrar historia.

No mezclar proyectos.

---

# 27. CIERRE DE FOUNDATION

Sólo declarar:

```text
FOUNDATION = CLOSED / VERIFIED
```

cuando:

```text
A PASS
B PASS
C PASS
D PASS
E PASS
F PASS
G PASS
```

No utilizar:

```text
almost
practically
should work
looks good
```

El cierre requiere evidencia real.

---

# 28. ROADMAP DEFINITIVO

AtlasEmbroidery no tendrá decenas de fases artificiales.

Se utilizarán únicamente las siguientes:

```text
F1  Foundation
F2  Formats & Normalization
F3  Validator V1
F4  Basic Digitization
F5  Material Lab / AutoSetup
F6  Simulation V2
F7  Machine Bridge
F8  Atlas Doctor
F9  Production
F10 Learning / Lab
F11 Reliability / Recovery
F12 AI Assisted Embroidery
```

---

# 29. FASE 1 — FOUNDATION

## Objetivo

Construir un núcleo determinista y defendible.

Incluye:

```text
ATB model
geometry
objects
StitchParams
StitchPlan
StitchEngine
sequence
metrics
JSON
binary ATB plan
DST foundation
SVG input
hashing
file storage
tests
```

### Cierre

```text
A-G PASS
```

Resultado:

```text
Foundation CLOSED / VERIFIED
```

---

# 30. FASE 2 — FORMATS & NORMALIZATION

## Objetivo

Crear arquitectura formal de adapters.

Primero:

```text
DST
```

Después, progresivamente:

```text
PES/PEC
JEF
EXP
VP3
U01
XXX
TBF
```

Cada adapter tendrá:

```text
Reader
Writer
Normalizer
Round-trip
Golden corpus
Malformed tests
Semantic diff
```

Regla:

```text
No declarar compatibilidad universal.
```

Compatibilidad significa:

```text
formato
+
versión
+
comportamiento probado
```

---

# 31. FASE 3 — VALIDATOR V1

Crear un Validator formal.

Familias:

```text
Geometry
Stitches
Density
Hoop
Machine
Bounds
Jumps
Trims
Micro-stitches
Color/Needle
Layering
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

El Validator debe distinguir:

```text
deterministic fact
calculated risk
recommendation
hypothesis
unknown
```

---

# 32. FASE 4 — BASIC DIGITIZATION

Construir progresivamente:

```text
SVG
Shapes
Running
Triple/Bean
Satin
Tatami
Zigzag
Underlay
Pull Compensation
Sequence
```

Después:

```text
Text
Motifs
Appliqué
Knockdown
Puff
Sequins
```

No mezclar todo en una sola implementación.

---

# 33. FASE 5 — MATERIAL LAB / AUTOSETUP

Crear perfiles:

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

Confianza:

```text
Validated
Verified
Experimental
Custom
```

Ejemplos iniciales:

```text
playera algodón
polo/piqué
gorra
mezclilla
toalla
parche
sudadera
lona
uniforme
material delicado
no sé
```

El perfil `No sé` debe utilizar configuración conservadora.

---

# 34. FASE 6 — SIMULATION V2

Partir de la simulación determinista.

Agregar:

```text
playback
layers
density maps
thread estimation
time estimation
machine limits
risk visualization
```

No afirmar:

```text
physical simulation
```

sin evidencia física.

La simulación es:

```text
modelo
```

El stitch-out sigue siendo:

```text
evidencia física
```

---

# 35. FASE 7 — MACHINE BRIDGE

Separación obligatoria:

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

Nunca inferir protocolos.

Cada máquina soportada necesita:

```text
model
firmware
transport
format
test evidence
```

Primero:

```text
export
```

Después:

```text
media
```

Después:

```text
transfer
```

Y sólo posteriormente:

```text
status
queue
telemetry
control
```

---

# 36. FASE 8 — ATLAS DOCTOR

Diagnóstico determinista.

Entrada:

```text
symptom
job
machine
material
history
```

Salida:

```text
probable causes
evidence
safe test
one-variable change
result logging
```

Ejemplos:

```text
thread break
needle break
bird nesting
skipped stitches
puckering
gaps
bad registration
trim failure
transfer failure
file invisible
power loss
```

La IA no será requisito del Doctor.

---

# 37. FASE 9 — PRODUCTION

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

Crear:

```text
Job Package
```

Versionado.

Debe incluir:

```text
design/hash
design version
material
machine
thread
needle
hoop
artifact
Validator result
instructions
first article
```

Production nunca debe depender directamente de detalles internos del Stitch Engine.

---

# 38. FASE 10 — LEARNING / LAB

Feedback mínimo:

```text
👍 Bien
👎 Mal
Comentario opcional
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

Separar:

```text
private knowledge
```

de:

```text
community knowledge
```

Nada privado debe salir automáticamente.

---

# 39. FASE 11 — RELIABILITY / RECOVERY

Implementar:

```text
Audit Log
Backups
Restore
Checksums
Health Checks
Emergency Server
Disaster Recovery
```

Prueba real:

```text
job activo
↓
fallo servidor
↓
servidor emergencia
↓
identificar estado
↓
recuperar hash/version
↓
continuar
↓
evitar duplicación
```

Un backup no se considera válido hasta haber sido restaurado realmente.

---

# 40. FASE 12 — AI ASSISTED EMBROIDERY

La IA entra al final.

Puede ayudar con:

```text
auto-digitize
optimization suggestions
troubleshooting
material recommendations
design analysis
```

Pero el flujo será:

```text
IA propone
↓
Validator verifica
↓
usuario decide
↓
sistema registra
↓
se ejecuta
```

Nunca:

```text
IA decide
↓
máquina ejecuta
```

sin las capas de seguridad y autorización correspondientes.

---

# 41. ARQUITECTURA OBJETIVO

La evolución completa debe conservar:

```text
Design
  ↓
Objects
  ↓
Stitch Plan
  ↓
Validator
  ↓
Simulation
  ↓
Artifact
  ↓
Machine
  ↓
Production
  ↓
Evidence
  ↓
Reproducibility
  ↓
Learning
```

Los dominios deben permanecer desacoplados.

---

# 42. REGLAS PERMANENTES PARA NEMOTRON

Procedimiento:

```text
READ
↓
PLAN CORTO
↓
EDIT / WRITE
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

No modificar sin leer primero.

No declarar PASS por intuición.

No inventar:

```text
API
protocol
machine capability
format compatibility
test result
```

No detenerse sólo en diagnóstico cuando existe una corrección segura y definida.

---

# 43. POWERSHELL

Usar:

```powershell
comando1; comando2
```

No asumir:

```text
&&
```

como sintaxis universal de PowerShell.

---

# 44. COMMITS

Preferir commits coherentes:

```text
fix: harden binary deserialization
test: add binary version boundary coverage
fix: harden varint decoding
test: add sequence overflow regression
fix: enforce stitch engine limits
test: verify optimization ordering
fix: validate stitch metrics
test: harden invalid parameter behavior
```

No mezclar:

```text
feature
+
refactor masivo
+
formatting
+
unrelated cleanup
```

---

# 45. NO HACER

Hasta cerrar Foundation:

```text
NO Machine Bridge
NO IA
NO Production
NO Cloud
NO Community
NO massive UI
NO dozens of formats
NO emergency server
NO disaster recovery
```

Y tampoco:

```text
NO crear nuevas fases artificiales
```

---

# 46. RESULTADO OBLIGATORIO DE NEMOTRON

Al terminar las correcciones debe entregar:

```text
1. auditoría realizada
2. problemas encontrados
3. correcciones aplicadas
4. problemas descartados y motivo
5. archivos modificados
6. tests agregados
7. tests ejecutados
8. build ejecutado
9. resultado real
10. cobertura si corresponde
11. commits
12. SHA final
13. Gate A
14. Gate B
15. Gate C
16. Gate D
17. Gate E
18. Gate F
19. Gate G
20. FOUNDATION CLOSED / VERIFIED
```

Si algún gate falla:

```text
FOUNDATION = NOT CLOSED
```

No utilizar:

```text
casi
prácticamente
debería
parece
```

---

# 47. DEFINICIÓN FINAL DEL PROYECTO

AtlasEmbroidery no será simplemente:

```text
programa que genera DST
```

La arquitectura final será:

```text
Diseño
↓
Digitalización
↓
Normalización
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
Diagnóstico
↓
Evidencia
↓
Recuperación
↓
Aprendizaje
```

Pero cada etapa debe ganarse mediante:

```text
código
+
pruebas
+
evidencia
+
reproducibilidad
```

## PRINCIPIO RECTOR

> Primero un núcleo pequeño que podamos defender técnicamente.
> Después le ponemos las alas.
