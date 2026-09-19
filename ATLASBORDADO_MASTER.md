# ATLASBORDADO — ESPECIFICACIÓN MAESTRA

**Objetivo:** suite offline-first de digitalización, validación,
simulación, conexión a máquina, producción, diagnóstico y recuperación
para bordado.

> Principio rector: **que un archivo abra no significa que se pueda
> bordar bien.**

## 1. Visión de producto

AtlasBordado no será sólo un conversor DST ni un clon de Wilcom. Tendrá
un núcleo rico y varios dominios desacoplados:

- **Design / Digitizing:** crear, importar, reconstruir y digitalizar
  imágenes, vectores y bordados.
- **Stitch Engine:** convertir objetos a puntadas e instrucciones.
- **Atlas Validator:** detectar incompatibilidades, riesgos y defectos
  previsibles antes de máquina.
- **Materials / AutoSetup:** plantillas combinables de tela, hilo,
  aguja, estabilizador, bastidor y máquina.
- **Simulation:** recorrido puntada por puntada, densidad, capas,
  tiempo, hilo, límites y riesgos.
- **Formats:** lectura/escritura por adaptadores.
- **Machine Bridge:** archivo, USB, LAN, Wi-Fi, serial u otros
  transportes documentados.
- **Atlas Doctor:** diagnóstico determinista de incidencias.
- **Learning / Lab:** aprender de pruebas reales sin molestar al
  operador.
- **Production:** clientes, cotizaciones, órdenes, inventario, colas y
  calidad.
- **Reliability:** logs, respaldos, restauración y servidor de
  emergencia.

Experiencias sobre el mismo núcleo:

1.  **Automático:** “quiero bordar esto”.
2.  **Asistido:** Atlas propone, explica y el usuario decide.
3.  **Profesional:** control completo.
4.  **Operador/Producción:** ejecutar trabajos aprobados sin exponer
    herramientas innecesarias.

## 2. Reglas no negociables

### Offline-first

Sin Internet deben funcionar diseño, formatos, Validator, plantillas,
simulación, cotización, producción, Machine Bridge local, logs y
respaldos. IA/nube/comunidad serán opcionales.

### Original inmutable

`Original → propuesta → validación → corrección → aprobada → compilada`.
Nunca sobrescribir el original; comparar y revertir siempre.

### Archivo válido ≠ bordado sano

Validar sintaxis/formato por un lado y viabilidad física por otro.

### No inventar certeza

Clasificar hallazgos como: - **Determinista** - **Riesgo calculado** -
**Recomendación** - **Hipótesis** - **Desconocido**

### Evidencia

Cada regla técnica debe poder guardar fuente, alcance, versión,
máquina/material aplicable, pruebas y fecha. Un consejo de foro es una
hipótesis hasta validarse.

## 3. Modelo interno `.ATB`

El ATB será la fuente editable. Debe representar geometría, raster
original, vectores, texto, objetos de bordado, tipos/parámetros de
puntada, underlay, compensación, ángulos, conectores, secuencia, hilos,
agujas, trims, jumps, stops, cambios de color/aguja, bastidor, material,
máquina, versiones, procedencia, confianza de reconstrucción, resultados
del Validator y hashes de artefactos.

Importar un DST/PES/etc. no debe fingir que recuperamos información
perdida. Atlas puede reconstruir objetos **probables** y marcar
confianza.

## 4. Entrada universal

### Arte

PNG/JPEG/WebP/BMP/TIFF cuando proceda, SVG y PDF. Pipeline:

`preservar → limpiar → segmentar → reducir colores → detectar bordes → simplificar → vectorizar/reconstruir → proponer bordado → generar objetos → puntadas → validar → comparar`

La optimización puede modificar el arte para que borde bien, pero
siempre muestra `Original / Optimizado / Simulación`.

### Bordado

Arquitectura por adaptadores. Prioridad de escritura inicial: DST,
PES/PEC, JEF, EXP, VP3, U01, XXX, TBF. Lectura se amplía con corpus de
pruebas.

pyembroidery es una referencia valiosa: documenta decenas de formatos y
comandos STITCH, JUMP, TRIM, STOP, COLOR_CHANGE, NEEDLE_SET y SEQUIN;
también reconoce que la conversión puede perder información.

### Propietarios

EMB y otros formatos ricos no se tratarán como “DST con otra extensión”.
Sólo se soportará lo demostrable/documentado y se informará pérdida.

## 5. Digitalización y Stitch Engine

Tipos extensibles: running, triple/bean, satin/column, tatami/fill,
zigzag, motifs, contour, cross stitch, photo/thread-art, sketch,
appliqué, knockdown, 3D/puff y sequins cuando corresponda.

Por objeto: - density/spacing; - stitch min/max; - angle/transitions; -
underlay; - pull compensation; - overlap; - tie-in/tie-off; -
start/end; - trim policy; - travel; - color/needle; - material/machine
overrides.

La secuenciación será multiobjetivo: reducir cambios/trims/jumps **sin**
sacrificar estabilización, registro, capas, calidad o estrategia
específica de superficie. No basta un “shortest path”.

## 6. Atlas Validator

Estados: - 🟢 Apto - 🟡 Advertencia - 🔴 Crítico - ⚪ Desconocido

Familias de reglas:

### Geometría

Dimensiones, área, hoop, orientación, límites, splits y colisiones
previsibles.

### Puntadas

Longitud, micro-puntadas, penetraciones cercanas/repetidas, densidad
local, capas, jumps, trims, tie-ins, stops y comandos incompatibles.

### Registro/deformación

Pull/push, overlap, stitch angles, dependencias, outlines, tatami grande
y estrategias para superficies curvas.

### Material

Estabilidad, elasticidad, textura/nap, grosor, backing, topping, aguja,
hilo, velocidad y hooping.

### Máquina

Área, hoops, agujas, comandos, límites de stitch/jump, trims,
accesorios, transporte, modelo/controlador/firmware.

### Producción

Tiempo, cambios, bobbin planning, consumibles, first article y
checkpoints.

## 7. Material Lab + AutoSetup

El novato ve plantillas simples:

`Playera algodón | deportiva | polo/piqué | gorra | mezclilla | toalla | parche | sudadera | lona | uniforme | delicada | no sé`

Atlas usa un perfil conservador. Si la máquina reporta datos
incompatibles, la advertencia se vuelve insistente. Si no hay
telemetría, prioriza estabilidad sobre velocidad.

### Plantillas

Crear, editar, duplicar, activar, inactivar, archivar, restaurar,
versionar y comparar. Las plantillas del sistema se duplican; no se
destruyen.

### Composición

Evitar 800 presets: combinar
`Material + Tipo de trabajo + Máquina + Hilo/Aguja + Proceso/Bastidor` y
generar un Work Profile versionado.

### Datos

Tela, composición/construcción, gramaje si existe, grosor, elasticidad,
nap; hilo/material/peso; aguja/sistema/tamaño/punta;
estabilizador/tipo/capas;
máquina/modelo/controlador/firmware/hoops/capacidades.

### Confianza

- 🟢 Validada: documentación + pruebas + evidencia física.
- 🔵 Verificada: documentación técnica.
- 🟡 Experimental.
- ⚪ Personalizada.

## 8. Simulación

V1 será determinista: recorrido, penetraciones, stitches/jumps/trims,
cambios, stops, densidad, capas, tiempo/hilo aproximados, movimientos
largos, límites y riesgos.

No prometer simulación física perfecta de tela. La deformación depende
de tensión, estabilización, hooping, hilo, aguja, máquina y
mantenimiento. El **test stitch-out/first article** sigue siendo
evidencia crítica.

Flujo:
`Job Package → muestra → evaluar → registrar → aprobar versión → congelar parámetros → lote → spot checks`.

## 9. Atlas Doctor

V1 no dependerá de fotos/video/IA.

Síntomas: thread break, needle break, bird nesting, bobbin agotada,
skipped stitches, loops, puckering, gaps, registro/outline desplazado,
mala cobertura, atasco, vibración, trim/jump, color/needle incorrecto,
hoop error, archivo invisible, transferencia fallida, parada repetible o
aleatoria.

Razonamiento: - mismo punto → subir probabilidad de
archivo/densidad/capas/comando; - aleatorio →
hilo/ruta/aguja/tensión/bobbin/mantenimiento; - un color/aguja → revisar
ese canal; - sólo gorra → perfil, digitización, hooping, aguja, presser
foot; - archivo invisible → formato, área, nombre, medio y límites; -
tras bobbin/thread break → revisar reanudación; - bird nest → detener
antes de agravar atasco.

Salida: causas probables + evidencia + prueba más segura/barata +
cambiar una variable + registrar resultado.

## 10. Errores raros que Atlas debe contemplar

1.  **Archivo correcto pero invisible:** foros reportan archivos que no
    aparecen por exceder área/capacidad aunque la extensión sea
    correcta.
2.  **Bobbin detectada tarde:** puede quedar un hueco;
    guardar/recomendar margen de retroceso según máquina.
3.  **Rotura siempre en la misma puntada:** investigar densidad,
    micro-puntadas, capas y geometría antes de culpar tensión.
4.  **Bird nesting que escala:** el nudo inferior puede atascarse bajo
    placa y contribuir a roturas/daño.
5.  **Hoop sensor mecánico:** distinguir hoop configurado, reportado y
    físicamente confirmado.
6.  **Presser-foot/hoop collision:** límites lógicos no protegen contra
    máquina descalibrada.
7.  **Pérdida de energía:** guardar job/hash/stitch
    lógico/color/checkpoint; cada máquina reanuda distinto.
8.  **Transferencia depende de servicios auxiliares:** health checks de
    dependencias, no sólo ping.
9.  **Resize de stitch file:** comprimir puntadas puede disparar
    densidad; preferir regenerar desde objetos.
10. **Gorras:** no son “playeras curvas”; estrategia/orden/hooping/aguja
    cambian.
11. **Metálico:** perfil especial; experiencias reportan menor
    velocidad, aguja adecuada y ruta suave.
12. **Consejos contradictorios:** Knowledge Base debe guardar
    procedencia y desacuerdo.

## 11. Machine Bridge

Cada `MachineCapabilityProfile` declara formatos, área, hoops, agujas,
trims, comandos, velocidad, status, upload, queue, telemetry, start/stop
si está oficialmente soportado, transportes, firmware y quirks.

Niveles: 0 exportar archivo; 1 medio; 2 transferencia; 3 estado; 4 cola;
5 telemetría; 6 control remoto soportado.

Separar `FormatAdapter`, `TransportAdapter`, `MachineAdapter`.

Familias: Tajima, Barudan, Brother, Happy, Ricoma, ZSK,
Dahao/controladores chinos y Generic media/serial. No deducir protocolo
por la marca de la carcasa.

## 12. Compilación y formatos

Pipeline:

`ATB → normalized stitch plan → capability transform → encoder → read-back → semantic diff → validación cruzada → artifact`

Guardar SHA-256.

Pruebas: round-trip, golden files, malformed, max stitch/jump, trims,
color/needle changes, stops, sequins, truncados, fuzzing, equivalencia
visual y de comandos.

## 13. Optimización automática

Perfiles: - Calidad - Equilibrado - Producción - Conservador

Mostrar puntadas, colores, cambios, trims, tiempo, hilo, riesgos y
cambios visuales. El usuario decide. Modo Auto puede iterar, pero
conserva historial.

## 14. Modo Profesional / override

🟢 producir; 🟡 producir o aplicar sugerencia; 🔴 corregir.

Un profesional autorizado puede forzar riesgos no deterministas con
audit log. No permitir “override” ficticio de una imposibilidad física
conocida, como un área fuera del hoop/perfil.

## 15. Learning / Lab

Feedback mínimo:

`¿Cómo salió? 👍 Bien | 👎 Mal | comentario opcional`

Atlas ya conoce diseño, versión, parámetros, material, máquina, archivo,
tiempo e incidencias.

Feedback comunitario será voluntario y separado del conocimiento
privado. Diseños, clientes, logs e imágenes no salen por defecto.

## 16. Producción

Dominios: Customers, Quotes, Orders, Inventory, Jobs, Machine Queue,
Quality, Rework/Scrap, History.

`Job Package` versionado: cliente/orden, diseño/hash,
dimensiones/ubicación, material/template, machine profile, thread/needle
map, hoop, artifact, Validator, instrucciones y first-article.

Costeo separado del Stitch Engine: prenda, hilo, backing, consumibles,
tiempo máquina, mano de obra, setup, desperdicio, margen y rework.

## 17. Reliability / Recovery

### Audit Log

Usuario, equipo, hora, diseño/version, parámetros antes/después,
optimizaciones, Validator, override, export/hash, transferencia,
máquina, errores, recuperación y resultado. Logs estructurados, rotación
y correlation/job IDs.

### Backups

DB + ATB + artifacts + plantillas + perfiles + biblioteca +
clientes/órdenes + aprendizaje + configuración. Copia local/segundo
medio/NAS, servidor de emergencia y copia externa cifrada opcional.

### Restore

Pruebas periódicas reales: restore completo/selectivo, checksums,
integridad DB, referencias y arranque.

### Emergency Server

Standby preparado, configuración versionada, sincronización, failover
sencillo, runbook y health checks.

E2E de desastre: lote activo → matar principal → levantar emergencia →
distinguir enviados/pendientes → recuperar versión/hash → continuar sin
duplicar.

## 18. Seguridad

Roles: Admin, Digitizer Pro, Operator, Production Manager, Viewer.
Mínimo privilegio, overrides restringidos, Machine Bridge aislado, nada
expuesto a Internet por defecto, parsers con límites, paths seguros,
secretos fuera del repo, backups externos cifrados y audit trail
protegido.

## 19. Casos de uso obligatorios

1.  PNG → AutoSetup → DST.
2.  SVG → edición Pro → DST/PES.
3.  DST → audit → reconstrucción → reparación.
4.  Resize ATB → regenerar.
5.  Resize peligroso de stitch file → warning.
6.  Gorra.
7.  Polo/piqué.
8.  Toalla.
9.  Hilo metálico.
10. Lettering pequeño.
11. Multi-hoop.
12. Bobbin agotada/recovery.
13. Thread break repetible.
14. Bird nesting.
15. Archivo fuera de área.
16. Transferencia USB/LAN.
17. Caída de servidor.
18. Override Pro.
19. Feedback.
20. Reorden reproducible.

## 20. Estrategia de pruebas

- unitarias de geometría, puntadas, reglas y perfiles;
- fuzz/property tests de parsers;
- golden corpus;
- cross-software;
- Machine Lab por modelo/firmware;
- Material Lab con coupons y una variable por prueba;
- first article antes de lotes críticos;
- disaster recovery drills.

## 21. Diez vueltas buscando fugas

1.  **Universalidad:** muchos formatos ≠ todas las máquinas →
    capabilities/adapters.
2.  **Auto-digitize:** bonito en pantalla ≠ sano → Validator/Simulation.
3.  **Novato:** Material Lab puede espantar → presets/“No sé”.
4.  **Pro:** reglas rígidas estorban → override auditado.
5.  **Learning:** encuestas cansan → feedback mínimo/contextual.
6.  **Simulation:** render ≠ física → risk model + stitch-out.
7.  **Production:** riesgo de espagueti → dominios + Job Package.
8.  **Recovery:** backup no probado no sirve → restore drills.
9.  **Doctor:** “rompe hilo = tensión” es simplista →
    evidencia/repetibilidad.
10. **Internet:** consejo viral ≠ regla →
    provenance/confidence/versionado.

## 22. Fases de desarrollo para DeepSeek

### F0 Research corpus

Formatos, máquinas, materiales, reglas, fixtures y golden files.

### F1 Núcleo

ATB, geometry, stitch plan, versionado, logs, tests.

### F2 DST

Reader/writer/normalization/read-back/semantic diff/fuzz.

### F3 Validator V1

Hoop, dimensions, stitch length, jumps, trims, density, overlaps.

### F4 Digitización básica

SVG, shapes, running, satin, fill, underlay, compensation, sequence.

### F5 AutoSetup/Materials

CRUD/versionado/combinación/confianza.

### F6 Imagen

Cleanup, colors, segmentation, vectorization, alternatives.

### F7 Simulation

Playback, density, layers, time/thread, machine limits.

### F8 Formats

PES/JEF/EXP/VP3/etc. por adapter y corpus.

### F9 Machine Bridge

Media primero; conexiones documentadas/hardware después.

### F10 Doctor/Learning

Troubleshooting determinista + feedback.

### F11 Production

Clientes, órdenes, cotización, inventario, cola, QC.

### F12 Reliability completo

Replica, restore UI, emergency server, disaster E2E.

## 23. Reglas para el agente

1.  Read → plan corto → write → verify → test.
2.  No narrar durante horas.
3.  No declarar terminado sin evidencia.
4.  No inventar APIs/protocolos.
5.  Si comportamiento contradice código, verificar
    proceso/PID/binario/puerto/config antes de editar.
6.  Commits coherentes.
7.  Build limpio.
8.  Tests relevantes + regresión.
9.  Toda regla de bordado lleva fuente/evidencia.
10. Toda compatibilidad lleva modelo/firmware/transport probado.
11. No romper offline.
12. No acoplar Production con Stitch Engine.
13. No borrar originales.
14. No silenciar warnings.
15. Conservar corpus de errores reales.

## 24. Fuentes iniciales

### Formatos/open source

- https://github.com/EmbroidePy/pyembroidery
- https://github.com/EmbroidePy/pyembroidery-CLI
- https://inkstitch.org/
- https://github.com/inkstitch/inkstitch/issues

### Soporte/campo

- Ricoma, needle breaks/caps:
  https://support.ricoma.com/hc/en-us/articles/12609351251603-Why-Do-My-Needles-Keep-Breaking-When-I-Embroider-on-Caps
- Brother, power-loss resume:
  https://help.brother-usa.com/app/answers/detail/a_id/164432/
- HoopMaster, repeatable placement:
  https://hoopmaster.com/hoopmaster-information
- Embroideres community: https://forum.embroideres.com/
- Reddit Machine_Embroidery:
  https://www.reddit.com/r/Machine_Embroidery/
- Reddit MachineEmbroidery: https://www.reddit.com/r/MachineEmbroidery/

### Software a mantener en matriz competitiva

Wilcom EmbroideryStudio/Hatch, Tajima/Pulse, Brother PE-DESIGN, Janome
Artistic Digitizer, Stitch Era, Embrilliance, Ricoma Chroma, Melco
DesignShop e Ink/Stitch.

## 25. Definición de “al mil”

AtlasBordado está “al mil” cuando:

1.  sabe qué conoce y qué no;
2.  conserva original y versiones;
3.  da defaults conservadores al novato;
4.  da control al profesional;
5.  valida antes de producir;
6.  explica en lenguaje humano;
7.  aprende sin estorbar;
8.  separa formato/material/máquina/producción;
9.  funciona offline;
10. se recupera de falla del servidor;
11. reconstruye exactamente qué se mandó a máquina;
12. prueba compatibilidad en vez de presumirla;
13. considera el stitch-out evidencia;
14. permite reproducir un trabajo meses después.

**Meta:** AtlasBordado no termina en “guardar DST”; termina cuando el
trabajo puede producirse, repetirse, diagnosticarse y recuperarse con
evidencia.
