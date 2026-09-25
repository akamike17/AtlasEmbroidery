# AtlasEmbroidery — Master Format Forensics & Multi-Format Implementation

## Mission

Continue the AtlasEmbroidery repository from the current `master` state.

Current DST baseline:

* Commit: `c9d8d42e8663777dd9c2828b7cf1be1165cb776f`
* DST core has already been hardened.
* Current evidence reported: 401 tests passed, 0 failed.
* Release build: 0 warnings, 0 errors.
* Golden vectors and `DstControl.End` protection are already implemented.

The next objective is **NOT to blindly code every embroidery format**.

The objective is to build a **documented, evidence-driven format implementation roadmap**, then implement each format using the same forensic quality standard already established for DST.

---

# 1. NON-NEGOTIABLE RULE

Do NOT assume that another embroidery format works like DST.

Do NOT implement a format by analogy.

For every format:

```text
Consult references
        ↓
Understand binary structure
        ↓
Understand commands
        ↓
Understand metadata
        ↓
Understand coordinate encoding
        ↓
Understand compression if applicable
        ↓
Identify independent reference implementation
        ↓
Create independent golden vectors
        ↓
Implement reader
        ↓
Implement writer when justified
        ↓
Test malformed input
        ↓
Test boundary conditions
        ↓
Test semantic round-trip
        ↓
Test byte-level compatibility where applicable
        ↓
Full test suite
        ↓
Release build
        ↓
Commit / push / verify
```

A format must never be declared CLOSED merely because a file can be opened successfully.

---

# 2. PRIMARY REFERENCES TO CONSULT

Consult these sources online. Do NOT download entire repositories merely for documentation gathering.

## 2.1 pyembroidery

Primary reference:

https://github.com/EmbroidePy/pyembroidery

Use it to investigate:

* readers;
* writers;
* format registration;
* command semantics;
* coordinate encoding;
* metadata;
* color handling;
* format-specific limitations;
* supported commands;
* conversion behavior.

The project explicitly separates:

```text
Read:
File → Reader → Pattern

Write:
Pattern → Encoder → Pattern → Writer → File
```

It also recognizes that conversion can be lossy.

The project mandates support for:

* PES
* DST
* EXP
* JEF
* VP3

and supports additional formats.

Do not treat pyembroidery as an infallible specification.

It is a reference implementation.

Cross-check important behavior against additional references.

---

# 3. EmbroideryIO

Consult:

https://github.com/EmbroidePy/EmbroideryIO

This is important for architectural semantics.

Pay special attention to its distinction between:

### Low-level commands

Commands actually encoded in embroidery files.

These should be preserved in exact order when possible.

### Middle-level commands

Representations that can be translated into format-specific low-level commands.

### High-level operations

Shape/fill composition.

AtlasEmbroidery should NOT contaminate its binary format layer with high-level digitization logic.

The format layer should primarily operate on a canonical stitch/command representation.

---

# 4. PEmbroider

Consult:

https://github.com/CreativeInquiry/PEmbroider

Use it as an independent producer/reference for:

* DST
* EXP
* JEF
* PEC
* PES
* VP3
* XXX

PEmbroider is particularly useful because it provides an independent implementation rather than simply testing AtlasEmbroidery against itself.

Use it for cross-validation where practical.

---

# 5. vpype-embroidery

Consult:

https://github.com/EmbroidePy/vpype-embroidery

This project uses pyembroidery as its backend and documents practical format coverage.

Use it primarily for:

* supported-format matrix;
* interoperability expectations;
* conversion workflows;
* identifying formats worth prioritizing.

Do not treat it as a second independent binary implementation when it simply delegates to pyembroidery.

---

# 6. FORMAT COVERAGE

The currently documented pyembroidery ecosystem includes:

## Mandatory / Tier 1

### PES

Brother

### DST

Tajima

Already CLOSED at the current baseline.

### EXP

Melco / expanded embroidery

### JEF

Janome

### VP3

Husqvarna Viking / Pfaff ecosystem

These five are the primary target set.

---

# 7. TIER 2

Investigate and implement where evidence is sufficient:

* PEC
* XXX
* U01
* TBF
* HUS

Do NOT assume that "read supported" means "write should be implemented".

Writer support requires sufficient specification and independent validation.

---

# 8. TIER 3 — READERS / LEGACY COMPATIBILITY

Investigate these as a separate compatibility layer:

* 10O
* 100
* BRO
* DAT
* DSB
* DSZ
* EMD
* EXY
* FXY
* GT
* INB
* JPX
* KSM
* MAX
* MIT
* NEW
* PCD
* PCM
* PCQ
* PCS
* PHB
* PHC
* SEW
* SHV
* STC
* STX
* TAP
* ZHS
* ZXY

The exact current supported list must be verified from the reference repositories during the investigation.

Do not blindly reproduce an old list.

---

# 9. RELATED / AUXILIARY FORMATS

Keep these conceptually separate from machine-native embroidery formats:

* COL
* EDR
* INF
* PMV
* CSV
* JSON
* SVG
* PNG
* TXT
* GCODE

Some are color formats.

Some are Brother stitch-related formats.

Some are exchange/debug/visualization formats.

Some are output formats for other machines.

Do not place them all under the same "machine embroidery binary format" abstraction.

---

# 10. FORMAT FORENSICS MATRIX

Create:

```text
docs/FORMAT_FORENSICS_MASTER.md
```

For every format include:

| Field                          | Required                |
| ------------------------------ | ----------------------- |
| Extension                      | YES                     |
| Ecosystem / manufacturer       | YES                     |
| Reader supported               | YES                     |
| Writer supported               | YES/NO                  |
| Binary/text                    | YES                     |
| Header                         | YES                     |
| Header size                    | YES/UNKNOWN             |
| Endianness                     | YES/UNKNOWN             |
| Coordinate encoding            | YES                     |
| Coordinate units               | YES                     |
| Maximum delta                  | YES                     |
| Stitch encoding                | YES                     |
| Jump encoding                  | YES                     |
| Trim encoding                  | YES/NO/UNKNOWN          |
| Stop encoding                  | YES/NO/UNKNOWN          |
| Color change                   | YES/NO/UNKNOWN          |
| Needle set                     | YES/NO/UNKNOWN          |
| Sequin mode                    | YES/NO/UNKNOWN          |
| Sequin eject                   | YES/NO/UNKNOWN          |
| END marker                     | YES                     |
| Metadata                       | YES                     |
| Thread data                    | YES/NO                  |
| Compression                    | YES/NO                  |
| Checksum/CRC                   | YES/NO                  |
| Independent reference          | YES                     |
| Golden vectors available       | YES/NO                  |
| Malformed fixtures             | YES/NO                  |
| Round-trip possible            | YES/NO                  |
| Byte-exact comparison possible | YES/NO                  |
| Known information loss         | YES/NO                  |
| Current Atlas implementation   | YES/NO                  |
| Status                         | OPEN / PARTIAL / CLOSED |

Never invent an unknown field.

Use:

`UNKNOWN — requires further evidence`

instead of guessing.

---

# 11. CANONICAL INTERNAL MODEL

Before implementing many writers, inspect the current AtlasEmbroidery internal model.

Determine whether it can represent:

```text
STITCH
JUMP
TRIM
STOP
COLOR_CHANGE
NEEDLE_SET
END
FAST
SLOW
SEQUIN_MODE
SEQUIN_EJECT
```

Do not automatically add every possible command.

Only add commands when the actual formats require them.

The canonical model must be capable of preserving meaningful low-level information without forcing every format to pretend it supports commands it does not have.

---

# 12. COMMAND SEMANTICS

For every format determine:

### STITCH

Normal movement.

### JUMP

Non-stitch movement.

### TRIM

Explicit trim instruction if the format supports it.

### STOP

Machine stop.

### COLOR_CHANGE

Thread/color transition.

### NEEDLE_SET

Needle selection.

### END

True design termination.

### FAST / SLOW

Machine-speed commands where supported.

### SEQUIN_MODE / SEQUIN_EJECT

Only where actually supported.

Do NOT convert commands merely because two formats have similarly named operations.

---

# 13. GOLDEN VECTOR POLICY

This is critical.

Golden vectors MUST be independent.

Acceptable:

* bytes from a documented reference implementation;
* known public fixture files;
* independently generated files from pyembroidery;
* independently generated files from PEmbroider;
* documented real-world sample files.

Unacceptable:

```text
AtlasEncoder
     ↓
generate bytes
     ↓
test AtlasEncoder output
     ↓
PASS
```

That is circular validation.

The DST work already established the correct standard.

Every future binary writer should follow the same principle.

---

# 14. TEST CATEGORIES

Every format must have tests for:

## A. Minimal valid file

Smallest valid design.

## B. Single stitch

Test coordinate encoding.

## C. Positive X

## D. Negative X

## E. Positive Y

## F. Negative Y

## G. Maximum normal delta

## H. Boundary delta

## I. Jump

## J. Color change

## K. Stop

If supported.

## L. Trim

If supported.

## M. Needle change

If supported.

## N. END

## O. Multiple commands

## P. Multiple colors

## Q. Metadata

## R. Malformed header

## S. Truncated file

## T. Invalid command

## U. Invalid length

## V. Invalid compression data

If compressed.

## W. Round-trip

Where semantically meaningful.

---

# 15. BYTE-EXACT VS SEMANTIC COMPATIBILITY

Do not confuse these.

## BYTE-EXACT

The generated file bytes match the reference.

This is the strongest writer evidence.

## SEMANTIC

The generated file produces the same:

* stitches;
* jumps;
* colors;
* commands;
* geometry;

even if the byte representation differs.

## BEST-EFFORT

The format can be read/converter but some information is necessarily lost or ambiguous.

Every format should explicitly state which compatibility level is demonstrated.

---

# 16. LOSS REPORTING

Some formats cannot represent every command.

Example:

```text
Source:
TRIM

Target format:
no explicit TRIM command

Result:
TRIM normalized to equivalent supported behavior
```

That must be documented.

Never silently claim lossless conversion.

---

# 17. METADATA PRESERVATION

For every format inspect:

* design name;
* author;
* stitch count;
* color count;
* thread colors;
* thread names;
* needle information;
* hoop dimensions;
* design dimensions;
* version;
* machine information;
* reserved fields.

Unknown fields must not be fabricated.

If preservation is possible, preserve them.

If not, document the loss.

---

# 18. COMPRESSION

Formats using compression require dedicated investigation.

Do NOT reuse a codec merely because another format appears similar.

For every compressed format document:

```text
compression type
container
block structure
reset behavior
escape markers
maximum lengths
error handling
```

HUS and other compressed formats must be treated independently.

---

# 19. HEADER FORENSICS

For every binary format document:

```text
Offset
Length
Field
Encoding
Meaning
Known values
Unknown/reserved
```

Example:

| Offset | Length | Field        | Encoding     | Status   |
| -----: | -----: | ------------ | ------------ | -------- |
|      0 |      ? | Signature    | ASCII        | VERIFIED |
|      ? |      ? | Version      | ?            | UNKNOWN  |
|      ? |      ? | Stitch count | LE/BE        | VERIFIED |
|      ? |      ? | Metadata     | ASCII/Binary | VERIFIED |

Never invent offsets.

---

# 20. IMPLEMENTATION ORDER

After the forensic matrix is complete, recommend an evidence-based order.

Default expected order:

```text
DST       CLOSED
   ↓
PES
   ↓
JEF
   ↓
EXP
   ↓
VP3
   ↓
PEC
   ↓
XXX
   ↓
U01
   ↓
TBF
   ↓
HUS
   ↓
legacy readers
```

However, Kimi must adjust this order if the documentation/evidence shows a different dependency or difficulty.

Do not force this order if the repository architecture suggests otherwise.

---

# 21. READER VS WRITER POLICY

A reader and writer do NOT have to be implemented simultaneously.

Example:

```text
HUS
Reader: feasible
Writer: insufficient independent evidence

Status:
READ-ONLY VERIFIED
```

That is acceptable.

Do not create fake writers merely to increase format count.

---

# 22. FORMAT DETECTION

Inspect whether AtlasEmbroidery needs:

```text
extension detection
+
signature/header detection
+
fallback detection
```

Do not trust file extensions alone when a reliable signature/header exists.

Malformed or ambiguous files must produce diagnostics rather than arbitrary format selection.

---

# 23. FORMAT REGISTRATION

If the repository has a registry/factory system, make every format plug into the same architecture.

Expected conceptual structure:

```text
IEmbroideryFormat
        |
        +-- Reader
        |
        +-- Writer
        |
        +-- Metadata
        |
        +-- Capability flags
```

Do not create unrelated implementations per format.

But do not over-abstract binary encoders merely to eliminate a few duplicated lines.

Use abstractions only where actual format behavior is shared.

---

# 24. DO NOT OVERBUILD

Do NOT introduce:

* AI digitization;
* machine learning;
* vector databases;
* RAG;
* cloud conversion;
* web services;
* multi-agent orchestration;
* unnecessary CQRS;
* unnecessary dependency injection layers;
* huge generalized binary frameworks.

The immediate goal is:

**correct embroidery file I/O.**

---

# 25. REQUIRED FIRST PASS

Before changing production code:

1. Read the current repository.
2. Read the DST implementation.
3. Read the current tests.
4. Inspect the internal embroidery model.
5. Consult pyembroidery.
6. Consult EmbroideryIO.
7. Consult PEmbroider.
8. Consult vpype-embroidery.
9. Build the format matrix.
10. Identify independent references.
11. Identify which formats can realistically have golden vectors.
12. Identify known information loss.
13. Recommend implementation order.

During this pass:

**DO NOT MODIFY PRODUCTION CODE.**

Only create/update:

```text
docs/FORMAT_FORENSICS_MASTER.md
```

unless an existing documentation file absolutely must be corrected.

---

# 26. SECOND PASS

After the forensic document is complete, implementation may begin.

For each format:

```text
Reader
  ↓
Tests
  ↓
Independent fixtures
  ↓
Writer
  ↓
Golden vectors
  ↓
Malformed cases
  ↓
Round-trip
  ↓
Full suite
```

Do not implement all formats in one giant commit.

Prefer:

```text
feat(format): implement PES reader/writer
test(format): add PES independent golden vectors
```

or equivalent coherent commits.

---

# 27. CLOSURE CRITERIA

A format can only be marked:

```text
CLOSED / VERIFIED
```

when evidence exists for the declared capabilities.

Minimum:

1. Reader implemented.
2. Writer implemented if claimed.
3. Independent golden vectors.
4. Critical command tests.
5. Boundary tests.
6. Malformed input tests.
7. Round-trip where applicable.
8. Metadata behavior documented.
9. Information loss documented.
10. Full tests passing.
11. Release build passing.
12. Zero new warnings/errors.
13. Git commit created.
14. Push verified.
15. `HEAD == origin`.

If any requirement is missing:

```text
OPEN
```

or:

```text
PARTIAL
```

Do not call it CLOSED.

---

# 28. CURRENT DST STATUS

Treat:

`c9d8d42e8663777dd9c2828b7cf1be1165cb776f`

as the baseline.

Do not unnecessarily modify the DST implementation while researching other formats.

DST already has:

* independent golden vectors;
* control byte regression;
* Y-axis regression;
* END protection;
* explicit `EncodeEnd()`;
* 401 passing tests according to the current report.

Any modification to DST must have a concrete evidence-based reason.

---

# 29. FINAL DOCUMENT DELIVERABLE

Create:

`docs/FORMAT_FORENSICS_MASTER.md`

It must contain:

1. Sources consulted.
2. Current supported-format matrix.
3. Tier 1/2/3 classification.
4. Per-format technical findings.
5. Binary/header structure where documented.
6. Coordinate encoding.
7. Command encoding.
8. Metadata.
9. Compression.
10. Independent references.
11. Golden-vector strategy.
12. Known information loss.
13. Reader/writer feasibility.
14. Current Atlas implementation status.
15. Recommended implementation order.
16. Risks.
17. Unverified items.
18. Closure criteria.

---

# 30. FINAL REPORT

At the end report exactly:

```text
FORMAT FORENSICS REPORT

BASE:
FINAL:

DOCUMENT:
docs/FORMAT_FORENSICS_MASTER.md

SOURCES CONSULTED:
- pyembroidery
- EmbroideryIO
- PEmbroider
- vpype-embroidery
- any additional independent source actually consulted

FORMATS ANALYZED:
<number>

TIER 1:
...

TIER 2:
...

TIER 3:
...

CURRENT IMPLEMENTATION:
...

RECOMMENDED IMPLEMENTATION ORDER:
...

INDEPENDENT GOLDEN VECTOR SOURCES:
...

UNVERIFIED ITEMS:
...

PRODUCTION CODE CHANGED:
YES/NO

TESTS:
...

BUILD:
...

COMMIT:
...

PUSH:
...

HEAD == ORIGIN:
YES/NO
```

## Final rule

Do not report what you intended to inspect.

Report what you actually inspected.

Do not report a reference as "independent" if it simply delegates to pyembroidery.

Do not report byte compatibility unless bytes were actually compared.

Do not report a format as CLOSED unless its evidence satisfies the closure criteria.

The objective is not to have the biggest format list.

The objective is to make AtlasEmbroidery's format support **technically defensible, reproducible, and maintainable**.
