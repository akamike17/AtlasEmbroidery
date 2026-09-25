# AtlasEmbroidery Format Forensics Master Document

**BASE COMMIT:** `c9d8d42e8663777dd9c2828b7cf1be1165cb776f`  
**DATE:** 2026-09-25  
**PREPARED BY:** Forensic analysis of pyembroidery, EmbroideryIO, PEmbroider, vpype-embroidery

---

## 1. SOURCES CONSULTED

| Source | URL | Purpose |
|--------|-----|---------|
| pyembroidery | https://github.com/EmbroidePy/pyembroidery | Primary reference implementation (readers/writers, format registration, command semantics) |
| EmbroideryIO | https://github.com/EmbroidePy/EmbroideryIO | Architectural semantics (low/middle/high-level command distinction) |
| PEmbroider | https://github.com/CreativeInquiry/PEmbroider | Independent producer for DST/EXP/JEF/PEC/PES/VP3/XXX |
| vpype-embroidery | https://github.com/EmbroidePy/vpype-embroidery | Supported-format matrix, interoperability expectations |

---

## 2. CURRENT SUPPORTED-FORMAT MATRIX (from pyembroidery)

| Extension | Description | Reader | Writer | Notes |
|-----------|-------------|--------|--------|-------|
| **Tier 1 (Mandatory)** |
| .pes | Brother Embroidery Format | ✓ | ✓ | Multiple versions (1-6), delegates to PEC for stitch data |
| .dst | Tajima Embroidery Format | ✓ | ✓ | **CLOSED** at baseline c9d8d42 |
| .exp | Melco Expanded Embroidery | ✓ | ✓ | Simple 2-byte records, 127 max delta |
| .jef | Janome Embroidery Format | ✓ | ✓ | DST-compatible encoding, 121 max delta |
| .vp3 | Pfaff/Viking Embroidery | ✓ | ✓ | JEF-based stitch encoding, complex header |
| **Tier 2 (Investigate)** |
| .pec | Brother Embroidery Format | ✓ | ✓ | 12-bit delta encoding, base for PES |
| .xxx | Singer Embroidery Format | ✓ | ✓ | Needs investigation |
| .u01 | Barudan Embroidery Format | ✓ | ✓ | Needs investigation |
| .tbf | Tajima/Barudan | ✓ | ✓ | DST variant |
| **Tier 3 (Read-only / Legacy)** |
| .sew | Janome | ✓ | ✗ | |
| .shv | Husqvarna Viking | ✓ | ✗ | |
| .10o | Toyota | ✓ | ✗ | |
| .100 | Toyota | ✓ | ✗ | |
| .hus | Husqvarna | ✓ | ✗ | Compressed, needs dedicated investigation |
| .phb/.phc | Brother Stitch | ✓ | ✗ | |
| .jpx | Janome | ✓ | ✗ | |
| .stx | Data Stitch | ✓ | ✗ | |
| .tap | Happy | ✓ | ✗ | |
| .zxy | ZSK TC | ✓ | ✗ | |
| .col/.edr/.inf | Color formats | ✓ | ✓ | Not machine formats |
| .pmv | Brother Stitch | ✓ | ✓ | Stitch format, not machine |
| .svg/.png/.csv/.txt/.json/.gcode | Exchange/debug | Various | Various | Auxiliary |

---

## 3. TIER CLASSIFICATION

### Tier 1 — Primary Targets (Writer + Reader with Golden Vectors)
1. **PES** — Brother, widely used, writer delegates to PEC
2. **EXP** — Melco, simple binary, good independent reference
3. **JEF** — Janome, DST-compatible stitch encoding (already understood)
4. **VP3** — Pfaff/Viking, JEF-based, complex header
5. **PEC** — Brother base format, 12-bit delta encoding

### Tier 2 — Investigate (Writer if Evidence Sufficient)
- XXX, U01, TBF, HUS (compressed), etc.

### Tier 3 — Readers / Legacy Compatibility
- 20+ formats with readers only, no independent writer evidence

---

## 4. PER-FORMAT TECHNICAL FINDINGS

### 4.1 PES (Brother)

**Header:** Multiple versions (1, 2, 3, 4, 5, 5.5, 5.6, 6, 7, 8, 9, 10) detected by signature `#PES0001` through `#PES0100`.  
**Stitch Data:** Delegates to **PEC block** (embedded). All versions eventually read PEC.  
**Coordinate Units:** 0.1mm (same as DST)  
**Max Delta:** PEC uses 12-bit signed (±2047), PES v6 uses 2047  
**Thread Data:** Full thread palette in header (color index → thread mapping)  
**Color Changes:** Interpolated from duplicate colors  
**Compression:** None (PEC block is raw)  
**Independent Reference:** pyembroidery `PesReader` / `PesWriter` + `PecReader` / `PecWriter`  
**Golden Vectors:** Possible via pyembroidery PEC encoding  
**Writer Feasibility:** HIGH — implement PEC writer first, then PES wraps it

**Key Implementation Note:** PES writer writes `#PES0060` header + placeholder for PEC block offset → writes PEC data → backfills offset. PEC encoding is 12-bit with FLAG_LONG for large moves.

### 4.2 EXP (Melco Expanded)

**Header:** Minimal (no fixed header, stitches start at offset 0)  
**Record Format:** 2 bytes normal, 4 bytes for control codes  
```
Normal:  [dx][dy]           (signed 8-bit each, dy negated)
Jump:    0x80 0x04 [dx][dy]
Trim:    0x80 0x80 0x07 0x00
Color:   0x80 0x01 0x00 0x00
Stop:    0x80 0x01 0x00 0x00
```
**Coordinate Units:** 0.1mm  
**Max Delta:** 127 (signed 8-bit)  
**Thread Data:** None in file (color changes implicit)  
**Compression:** None  
**Independent Reference:** pyembroidery `ExpReader` / `ExpWriter`  
**Golden Vectors:** Trivial to generate  
**Writer Feasibility:** HIGH — simplest format

### 4.3 JEF (Janome)

**Header:** 512-byte text header (DST-compatible) + binary thread table + stitch data  
**Stitch Encoding:** **IDENTICAL to DST** (3-byte balanced ternary, y-negated, 121 max delta)  
```
Byte layout per record: [b0][b1][b2] — same bit positions as DST
Control: Normal=0x03, Jump=0x83, ColorChange=0xC3, Stop=0xC3, End=0xF3
```
**Coordinate Units:** 0.1mm  
**Max Delta:** 121 (same as DST)  
**Thread Data:** Thread table in header (color index → JEF thread set)  
**Compression:** None  
**Independent Reference:** pyembroidery `JefReader` / `JefWriter` (shares `DstWriter.encode_record`!)  
**Golden Vectors:** Can reuse DST golden vectors with JEF header  
**Writer Feasibility:** HIGH — stitch encoder already correct from DST work

### 4.4 VP3 (Pfaff/Viking)

**Header:** Binary header with stitch offset, color count, thread palette (Husqvarna thread set)  
**Stitch Encoding:** **JEF/PEC-style 2-byte records** with 0x80 prefix for controls  
```
Normal:  [dx][dy]           (signed 8-bit)
Jump:    0x80 0x04 [dx][dy]  
ColorChange: varies by thread mapping
Stop:    0x80 0x01 [dx][dy]
Trim:    interpolated
```
**Coordinate Units:** 0.1mm  
**Max Delta:** 127 (signed 8-bit)  
**Thread Data:** Complex mapping to JEF thread set, handles duplicate color indices  
**Compression:** None  
**Independent Reference:** pyembroidery `Vp3Reader` / `Vp3Writer`  
**Golden Vectors:** Generate via pyembroidery  
**Writer Feasibility:** MEDIUM — complex thread mapping logic

### 4.5 PEC (Brother Base)

**Header:** Text header (LA:, spaces, 0xFF 0x00) + binary fields + graphics  
**Stitch Encoding:** Variable-length (1 or 2 bytes per coordinate)  
```
Normal (short):  [dx][dy]           (signed 7-bit, FLAG_LONG=0)
Normal (long):   [dx_hi|flags][dx_lo][dy_hi|flags][dy_lo] (12-bit)
Jump:    FLAG_LONG + JUMP_CODE (0x10)
Trim:    FLAG_LONG + TRIM_CODE (0x20)
ColorChange: 0xFE 0xB0 (in PES) / handled in PEC block
```
**Coordinate Units:** 0.1mm  
**Max Delta:** 2047 (12-bit signed)  
**Thread Data:** Color index table in header  
**Graphics:** 48x38 icon data after stitch block  
**Compression:** None  
**Independent Reference:** pyembroidery `PecReader` / `PecWriter`  
**Golden Vectors:** Generate via pyembroidery  
**Writer Feasibility:** HIGH — required for PES writer

---

## 5. COMMAND SEMANTICS MAPPING

| Atlas StitchType | DST | PEC | PES | EXP | JEF | VP3 |
|------------------|-----|-----|-----|-----|-----|-----|
| Running/Satin/Tatami/etc. | STITCH | STITCH | STITCH | STITCH | STITCH | STITCH |
| Jump | Jump (0x80) | JUMP_CODE | JUMP_CODE | 0x80 0x04 | Jump (0x80) | 0x80 0x04 |
| Trim | (Jump sequence) | TRIM_CODE | TRIM_CODE | 0x80 0x80 | (none native) | (settings) |
| ColorChange | ColorChange (0xC0) | 0xFE 0xB0 | interpolated | 0x80 0x01 | ColorChange (0xC0) | thread mapping |
| Stop | ColorChange (0xC0) | same | interpolated | 0x80 0x01 | ColorChange (0xC0) | color_toggled |
| End | 0xF3 0x00 0x00 | implicit | implicit | implicit | 0xF3 0x00 0x00 | 0x10 |

**Critical Finding:** JEF stitch encoding is **byte-identical to DST**. VP3 uses EXP-style 2-byte records. PEC uses 12-bit variable encoding. PES wraps PEC.

---

## 6. COORDINATE ENCODING SUMMARY

| Format | Units | Max Delta | Encoding | Y-Axis |
|--------|-------|-----------|----------|--------|
| DST | 0.1mm | 121 | Balanced ternary (3 bytes) | **Negated** |
| JEF | 0.1mm | 121 | Balanced ternary (3 bytes) | **Negated** (same as DST) |
| PEC | 0.1mm | 2047 | 12-bit variable (1-2 bytes) | **Negated** |
| PES | 0.1mm | 2047 | Via PEC | **Negated** |
| EXP | 0.1mm | 127 | Signed 8-bit (2 bytes) | **Negated** |
| VP3 | 0.1mm | 127 | Signed 8-bit (2 bytes) | **Negated** |

**All machine formats negate Y-axis** (y = -y before encoding). This is consistent across pyembroidery implementations.

---

## 7. INDEPENDENT REFERENCES & GOLDEN VECTOR SOURCES

| Format | Primary Reference | Secondary Reference | Golden Vector Generation |
|--------|-------------------|---------------------|-------------------------|
| DST | pyembroidery (DstWriter) | Tajima spec / KDE wiki | ✅ 34 vectors implemented |
| PES | pyembroidery (PesWriter → PecWriter) | PEmbroider | Via PEC encoding |
| PEC | pyembroidery (PecWriter) | PEmbroider | Direct |
| EXP | pyembroidery (ExpWriter) | PEmbroider | Direct |
| JEF | pyembroidery (JefWriter) | PEmbroider | Reuse DST + JEF header |
| VP3 | pyembroidery (Vp3Writer) | PEmbroider | Direct |

**Note:** vpype-embroidery delegates to pyembroidery — NOT independent.  
**PEmbroider** is the only truly independent implementation for cross-validation.

---

## 8. KNOWN INFORMATION LOSS

| Source → Target | Loss | Documentation Required |
|-----------------|------|------------------------|
| Any → DST | No explicit TRIM (uses jump sequence) | Document TRIM→Jump normalization |
| Any → EXP | No thread data, no metadata | Document header loss |
| PES → PEC | PES metadata (version-specific) lost | Document version loss |
| VP3 → DST | Complex thread mapping, hoop info | Document thread/hoop loss |
| DST → PES | Balanced ternary → 12-bit (precision diff) | Document coordinate quantization |

---

## 9. CURRENT ATLAS IMPLEMENTATION STATUS

| Format | Reader | Writer | Tests | Golden Vectors | Status |
|--------|--------|--------|-------|----------------|--------|
| DST | ✓ | ✓ | 401 | 34 (pyembroidery) | **CLOSED** |
| SVG | ✓ | ✗ | — | N/A | READ-ONLY |
| PES | ✗ | ✗ | — | — | OPEN |
| EXP | ✗ | ✗ | — | — | OPEN |
| JEF | ✗ | ✗ | — | — | OPEN |
| VP3 | ✗ | ✗ | — | — | OPEN |
| PEC | ✗ | ✗ | — | — | OPEN |

---

## 10. RECOMMENDED IMPLEMENTATION ORDER

```
1. PEC          (base for PES, 12-bit encoding, independent ref)
   ↓
2. PES          (wraps PEC, version handling)
   ↓
3. EXP          (simplest, 2-byte records, trivial golden vectors)
   ↓
4. JEF          (DST-compatible stitch encoding, reuse DST encoder)
   ↓
5. VP3          (complex thread mapping, JEF-style records)
   ↓
6. XXX / U01 / TBF  (Tier 2, as evidence permits)
```

**Rationale:** PEC is the foundation for PES (which delegates to PEC). EXP is simplest for establishing test patterns. JEF reuses the already-verified DST balanced ternary encoder. VP3 is most complex due to thread mapping.

---

## 11. RISKS

| Risk | Impact | Mitigation |
|------|--------|------------|
| PES version fragmentation | Medium | Implement PEC first, handle version in PES wrapper |
| VP3 thread mapping complexity | High | Document mapping, accept best-effort |
| HUS compression | High | Defer to separate investigation |
| DST golden vector maintenance | Low | 34 vectors frozen, round-trip protects encoder |

---

## 12. UNVERIFIED ITEMS

- [ ] PEmbroider cross-validation for any format
- [ ] Real-world sample files for each format
- [ ] HUS compression algorithm
- [ ] VP3 hoop handling
- [ ] PES version 6 truncated format
- [ ] Color format (COL/EDR/INF) integration

---

## 13. CLOSURE CRITERIA CHECKLIST (per format)

For each format to be marked **CLOSED**:
- [ ] Reader implemented
- [ ] Writer implemented (if claimed)
- [ ] Independent golden vectors (pyembroidery/PEmbroider)
- [ ] Critical command tests (STITCH, JUMP, COLOR_CHANGE, END)
- [ ] Boundary tests (max delta, min/max coordinates)
- [ ] Malformed input tests (truncated, invalid control, bad header)
- [ ] Round-trip test (semantic preservation)
- [ ] Metadata behavior documented
- [ ] Information loss documented
- [ ] Full test suite passing
- [ ] Release build: 0 warnings/errors
- [ ] Git commit + push + HEAD==origin verified

---

## 14. PRODUCTION CODE CHANGED IN THIS PASS

**NO** — This pass was documentation/research only per rev8.md §25.

---

## 15. NEXT ACTIONS

1. Implement **PEC reader/writer** with golden vectors from pyembroidery
2. Implement **PES reader/writer** wrapping PEC
3. Implement **EXP reader/writer** (simplest)
4. Implement **JEF reader/writer** (reuse DST encoder)
5. Implement **VP3 reader/writer** (thread mapping)
6. Update this document with implementation evidence

---

**DOCUMENT:** `docs/FORMAT_FORENSICS_MASTER.md`  
**COMMIT:** (pending — documentation only this pass)  
**PUSH:** N/A  
**HEAD == ORIGIN:** YES