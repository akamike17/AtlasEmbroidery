# AtlasEmbroidery — Roadmap Acceptance Criteria (Phases 2–12)

**Baseline:** Foundation CLOSED at commit `57c0e8e` (rev3.md complete)
- 246 tests passing, 60.4% line / 45.1% branch coverage
- Gates A–G: PASS
- No external dependencies beyond .NET 8 stdlib (SkiaSharp deferred to Phase 2+)

---

## PHASE 2 — FORMATS & NORMALIZATION

### Objective
Bidirectional conversion between external embroidery formats and AtlasEmbroidery internal model (`AtlasProject` / `StitchPlan`).

### Required Capabilities
| Format | Read | Write | Header | Colors | Needles | Jumps | Trims | Stops | Bounds | Metadata | Round-trip |
|--------|------|-------|--------|--------|---------|-------|-------|-------|--------|----------|------------|
| DST    | ✅   | ✅    | ✅     | ✅     | ✅      | ✅    | ✅    | ✅    | ✅     | ✅       | ✅         |
| PES    | ⏳   | ⏳    | ⏳     | ⏳     | ⏳      | ⏳    | ⏳    | ⏳    | ⏳     | ⏳       | ⏳         |
| PEC    | ⏳   | ⏳    | ⏳     | ⏳     | ⏳      | ⏳    | ⏳    | ⏳    | ⏳     | ⏳       | ⏳         |
| JEF    | ⏳   | ⏳    | ⏳     | ⏳     | ⏳      | ⏳    | ⏳    | ⏳    | ⏳     | ⏳       | ⏳         |
| EXP    | ⏳   | ⏳    | ⏳     | ⏳     | ⏳      | ⏳    | ⏳    | ⏳    | ⏳     | ⏳       | ⏳         |
| VP3    | ⏳   | ⏳    | ⏳     | ⏳     | ⏳      | ⏳    | ⏳    | ⏳    | ⏳     | ⏳       | ⏳         |
| U01    | ⏳   | ⏳    | ⏳     | ⏳     | ⏳      | ⏳    | ⏳    | ⏳    | ⏳     | ⏳       | ⏳         |
| XXX    | ⏳   | ⏳    | ⏳     | ⏳     | ⏳      | ⏳    | ⏳    | ⏳    | ⏳     | ⏳       | ⏳         |
| TBF    | ⏳   | ⏳    | ⏳     | ⏳     | ⏳      | ⏳    | ⏳    | ⏳    | ⏳     | ⏳       | ⏳         |

✅ = implemented & tested | ⏳ = not started

### Required Interfaces
```csharp
public interface IEmbroideryFormatReader
{
    string FormatName { get; }
    string[] Extensions { get; }
    AtlasProject Read(Stream stream, FormatReadOptions? options = null);
    FormatValidationResult Validate(Stream stream);
}

public interface IEmbroideryFormatWriter
{
    string FormatName { get; }
    string DefaultExtension { get; }
    void Write(AtlasProject project, Stream stream, FormatWriteOptions? options = null);
}

public interface IEmbroideryNormalizer
{
    AtlasProject Normalize(AtlasProject project, NormalizationProfile profile);
}

public interface IEmbroideryFormatValidator
{
    FormatValidationResult Validate(AtlasProject project, MachineProfile? machine = null);
}
```

### Required Tests & Evidence (per format)
1. **Golden files** — 5+ real files per format (varying complexity)
2. **Round-trip test** — `Read → Write → Read` produces semantically equivalent `StitchPlan`
3. **Malformed tests** — truncated, corrupted header, invalid stitch data, oversized
4. **Edge cases** — empty design, single stitch, max stitches, max colors, max needles
5. **Color/needle mapping** — verify palette preservation and needle assignment
6. **Bounds validation** — design fits declared hoop / machine limits
7. **Metadata preservation** — author, creation date, design name, notes

### Security / Determinism
- All readers treat input as **untrusted**: bounds checks, size limits, no external entity resolution
- Writers produce deterministic output for same input (fixed timestamps or omitted)
- No network I/O in readers/writers
- Maximum document size enforced (configurable, default 50 MB)

### Completion Gates
| Gate | Criterion |
|------|-----------|
| G2.1 | DST: 100% test pass on golden files + 20 malformed cases |
| G2.2 | Interfaces implemented, DI-registered, documented |
| G2.3 | Normalizer handles: stitch type mapping, density conversion, coordinate system transform |
| G2.4 | Format validator emits `PASS/WARNING/CRITICAL` with `RuleId`, `Evidence`, `Message`, `Recommendation` |
| G2.5 | No format claimed "supported" without golden files + round-trip evidence |

### Must NOT Be Considered Complete
- Format support claimed without golden files and round-trip tests
- PES/PEC/JEF/EXP/VP3/U01/XXX/TBF without full reader+writer+tests
- Normalizer that loses stitch semantics (e.g., converts tatami → running silently)

### Dependencies
- Foundation (serialization, StitchPlan, StitchPoint, AtlasProject, MachineProfile, HoopProfile)

---

## PHASE 3 — VALIDATOR V1

### Objective
Static analysis of `StitchPlan` / `AtlasProject` against machine, material, and quality rules.

### Required Capabilities
| Rule Category | Rules (minimum) |
|---------------|-----------------|
| Geometry | Zero-length stitches, duplicate consecutive points, self-intersecting paths |
| Bounds | Design exceeds hoop, design exceeds machine max field, negative coordinates |
| Hoop | Object outside usable area, hoop clash with machine arms |
| Stitch Length | `< MinStitchLength`, `> MaxStitchLength` (per machine profile) |
| Density | `> MaxDensityStitchesPerMm2` (per material), tatami row spacing consistency |
| Jump | `> MaxJumpLength` (per machine), jump into forbidden zone |
| Trim | Excessive trim count (> threshold per 10k stitches), trim on short jumps |
| Color Changes | Excessive color changes, color change without trim |
| Needle Count | `> MachineProfile.NeedleCount` |
| Micro Stitches | Count of stitches `< 0.5mm` (configurable threshold) |
| Duplicate Stitches | Same XY + type + color within tolerance |
| Sequence | Non-monotonic SequenceIndex, gaps > 1 |
| Machine Constraints | Max speed vs stitch density, max acceleration zones |

### Required Interface
```csharp
public interface IEmbroideryValidator
{
    ValidationResult Validate(StitchPlan plan, ValidationContext context);
}

public enum ValidationSeverity { Pass, Warning, Critical, Unknown }

public sealed class ValidationIssue
{
    public string RuleId { get; set; }
    public ValidationSeverity Severity { get; set; }
    public string Message { get; set; }
    public string Evidence { get; set; }      // JSON: coordinates, values, thresholds
    public string Recommendation { get; set; }
    public object? Context { get; set; }       // Rule-specific data
}

public sealed class ValidationResult
{
    public IReadOnlyList<ValidationIssue> Issues { get; set; }
    public bool HasCritical => Issues.Any(i => i.Severity == ValidationSeverity.Critical);
    public bool HasWarning => Issues.Any(i => i.Severity == ValidationSeverity.Warning);
    public ValidationSummary Summary { get; set; }
}
```

### Required Tests & Evidence
1. **Rule unit tests** — 1 test per rule (happy path, boundary, violation)
2. **Integration tests** — 10+ real designs with known issues, verify detection
3. **False positive rate** — < 5% on clean production designs (measured)
4. **Performance** — validate 100k stitch plan < 500ms
5. **Determinism** — same input → identical `ValidationResult` (issue order stable)

### Security / Determinism
- Validator is pure function (no side effects, no I/O)
- Rules configurable via `ValidationProfile` (enable/disable, thresholds)
- No reflection-based rule discovery (explicit registration)

### Completion Gates
| Gate | Criterion |
|------|-----------|
| G3.1 | All 15+ rule categories implemented with unit tests |
| G3.2 | Integration suite: 10 designs, 0 false negatives on seeded defects |
| G3.3 | Performance: 100k stitches < 500ms, 1M stitches < 5s |
| G3.4 | `ValidationResult` serializable (JSON) with full fidelity |

### Must NOT Be Considered Complete
- Rules implemented without `Evidence` and `Recommendation` fields
- Validator that mutates input
- "AI-powered" validation without deterministic baseline

### Dependencies
- Phase 2 (normalized `StitchPlan` input), Foundation (models, MachineProfile, MaterialProfile)

---

## PHASE 4 — BASIC DIGITIZATION

### Objective
Professional-grade geometry → stitch conversion for core stitch types.

### Required Capabilities

#### Running Stitch
- [ ] Configurable spacing (micras)
- [ ] Corner handling: sharp, rounded, mitered
- [ ] Closed path handling (start/end overlap)
- [ ] Triple/bean stitch (3-pass) with correct offsets

#### Satin (Column)
- [ ] Rail generation (left/right edges)
- [ ] Centerline calculation
- [ ] Column generation with ramping
- [ ] Turning (corner strategy: mitered, capped, rounded, auto)
- [ ] Density control (column spacing)
- [ ] Short-stitch compensation (configurable threshold)
- [ ] Pull compensation (per material profile)
- [ ] Auto-split wide columns (> MaxColumnWidth)
- [ ] Underlay strategies: edge walk, zigzag, double zigzag, center walk, contour

#### Tatami (Fill)
- [ ] Polygon fill with scanline algorithm
- [ ] Hole/island support (even-odd rule)
- [ ] Density map (variable density per region)
- [ ] Angle control (global + per-object)
- [ ] Row staggering (alternate, random, fixed offset)
- [ ] Edge compensation (pull/push)
- [ ] Underlay: edge walk + tatami/center walk
- [ ] Short fill handling (< 3mm)
- [ ] Complex polygons: multiple regions, islands

#### Zigzag
- [ ] Width + spacing control
- [ ] Turning strategy
- [ ] Edge finishing

#### Pull Compensation
- [ ] Fabric-aware (material profile)
- [ ] Density-aware
- [ ] Direction-aware (warp/weft)

### Required Interfaces
```csharp
public interface IDigitizer
{
    StitchPlan Digitize(AtlasProject project, DigitizationProfile profile);
}

public sealed class DigitizationProfile
{
    public StitchType DefaultFillType { get; set; } = StitchType.Tatami;
    public StitchType DefaultOutlineType { get; set; } = StitchType.Satin;
    public bool AutoUnderlay { get; set; } = true;
    public bool AutoPullCompensation { get; set; } = true;
    public QualityLevel Quality { get; set; } = QualityLevel.Standard; // Draft, Standard, High
}
```

### Required Tests & Evidence
1. **Visual regression** — PNG renders of digitized output vs reference (per stitch type)
2. **Stitch count accuracy** — predicted vs actual within 5%
3. **Density verification** — measured stitches/mm² matches target ±10%
4. **Pull compensation** — simulated stitch-out dimensions within tolerance
5. **Corner quality** — no gaps, no overlaps > 0.2mm
5. **Underlay coverage** — underlay extends beyond top stitching by Inset amount
6. **Determinism** — same input → identical stitch sequence (coordinates, types, flags)

### Security / Determinism
- No randomness without explicit `RandomSeed` in profile
- All geometric calculations bounded (no NaN/Infinity)
- Integer overflow checks on coordinate math

### Completion Gates
| Gate | Criterion |
|------|-----------|
| G4.1 | Running/Triple: corner/closed-path tests pass, density ±5% |
| G4.2 | Satin: rail/centerline/turning/auto-split tests pass, 5+ shapes |
| G4.3 | Tatami: fill/holes/islands/density/angle/underlay tests pass, 5+ polygons |
| G4.4 | Zigzag: width/spacing/turning tests pass |
| G4.5 | Pull compensation: 3 material profiles, dimensional accuracy ±0.1mm simulated |
| G4.6 | Visual regression suite: 20+ reference images, CI comparison |

### Must NOT Be Considered Complete
- "Satin supported" without rail generation, turning, auto-split, pull comp
- "Tatami supported" without holes, islands, variable density, edge compensation
- Digitizer that produces NaN coordinates or crashes on degenerate input

### Dependencies
- Phase 2 (normalized input), Phase 3 (validator for digitization output), Foundation (geometry, StitchEngine)

---

## PHASE 5 — MATERIAL LAB

### Objective
Data-driven material/thread/needle/stabilizer profiles with confidence levels.

### Required Capabilities
| Entity | Properties (minimum) |
|--------|---------------------|
| Material (Fabric) | Category, weight GSM, thickness, elasticity, nap direction, max density, recommended pull comp, knockdown required |
| Thread | Material, weight (wt), color (ThreadColor), tensile strength, recommended needle size |
| Needle | System (DBx1, etc.), point type, size (Nm), max stitch length |
| Stabilizer | Type, layers, weight GSM, adhesion |
| Machine | Max field, needle count, max speed, max stitch/jump length, format support |
| Hoop | Dimensions, usable area, machine compatibility |
| WorkProfile | Combined profile with derived parameters + confidence level |

### Confidence Levels
```csharp
public enum ConfidenceLevel
{
    Unknown = 0,      // No data, conservative defaults
    Custom = 1,       // User-provided, unverified
    Validated = 2,    // Tested on similar job
    Verified = 3,     // Production-verified, documented evidence
    Calibrated = 4    // Machine-specific calibration data
}
```

### Required Tests & Evidence
1. **Profile CRUD** — create, read, update, delete, clone, export/import JSON
2. **Derived parameters** — WorkProfile correctly computes RecommendedDensity, PullComp, MaxSpeed from components
3. **Conservative defaults** — `Unknown` profile produces safe (slower, more underlay) parameters
4. **Conflict detection** — incompatible needle/thread/fabric flagged
5. **Template library** — 10+ system templates (cotton, pique, denim, towel, cap, patch, hoodie, canvas, uniform, delicate)

### Completion Gates
| Gate | Criterion |
|------|-----------|
| G5.1 | All 7 entity types with DeepClone, serialization, validation |
| G5.2 | WorkProfile derivation logic tested with 20+ combinations |
| G5.3 | 10 system templates load without error, produce valid StitchPlan |
| G5.4 | Unknown profile demonstrably more conservative than Verified |

### Must NOT Be Considered Complete
- Profiles without confidence tracking
- Derived parameters hardcoded instead of computed
- Template library with < 10 entries

### Dependencies
- Foundation (MaterialProfile, MachineProfile, HoopProfile, WorkProfile, serialization)

---

## PHASE 6 — SIMULATION V2

### Objective
Time-based playback with physical evidence separation.

### Required Capabilities
| Feature | Specification |
|---------|---------------|
| Playback | Frame-by-frame, variable speed, pause/seek |
| Layers | Color-by-color, object-by-object, time-sliced |
| Stitch speed | Simulated machine RPM → time per stitch |
| Jump visualization | Trajectory, duration, clearance check |
| Trim visualization | Trim points, tail length |
| Density map | Heatmap (stitches/mm²) overlay |
| Thread estimate | Meters per color, total, waste factor |
| Time estimate | Stitch time + jump time + trim time + color changes |
| Hoop boundaries | Visual clamp, collision warning |
| Machine limits | Speed/acceleration zones, needle change time |

### Required Interface
```csharp
public interface IEmbroiderySimulator
{
    SimulationResult Simulate(StitchPlan plan, SimulationOptions options);
    IAsyncEnumerable<SimulationFrame> SimulateStreaming(StitchPlan plan, SimulationOptions options);
}

public sealed class SimulationResult
{
    public TimeSpan TotalTime { get; set; }
    public double TotalThreadMeters { get; set; }
    public IReadOnlyList<SimulationLayer> Layers { get; set; }
    public IReadOnlyList<SimulationRisk> Risks { get; set; }  // Physical evidence
    public DensityMap DensityMap { get; set; }
}

public sealed class SimulationRisk
{
    public RiskType Type { get; set; } // ThreadBreak, NeedleBreak, BirdNest, Puckering, Registration
    public string Evidence { get; set; } // Measured value vs threshold
    public Point Location { get; set; }
    public double Severity { get; set; } // 0-1
}
```

### Required Tests & Evidence
1. **Time accuracy** — simulated time vs real machine log (±15% on 5+ designs)
2. **Thread estimate** — simulated vs actual cone usage (±10%)
3. **Risk detection** — seeded defects (long jumps, high density) detected with correct location
4. **Streaming** — 1M stitch plan streams frames without OOM
5. **Determinism** — same plan → identical SimulationResult

### Security / Determinism
- Simulation is pure (no side effects, no hardware access)
- RandomSeed controls any stochastic visualization (particle effects)
- Physical evidence (`SimulationRisk`) separated from model predictions

### Completion Gates
| Gate | Criterion |
|------|-----------|
| G6.1 | All playback controls work, 100k stitches < 100ms/frame |
| G6.2 | Time estimate validated against 5 real machine logs |
| G6.3 | Thread estimate validated against 5 real cone weights |
| G6.4 | Risk types implemented with Evidence strings |
| G6.5 | Streaming simulator passes 1M stitch memory test |

### Must NOT Be Considered Complete
- Simulation claimed to "replace stitch-out" — must state: "Model only, not physical evidence"
- Risks without `Evidence` field
- Time estimates without machine profile calibration

### Dependencies
- Phase 2 (StitchPlan), Phase 3 (validator risks), Phase 4 (digitization output), Phase 5 (material/machine profiles)

---

## PHASE 7 — MACHINE BRIDGE

### Objective
Transport-agnostic machine communication architecture.

### Architecture
```
Format Adapter → Transport Adapter → Machine Adapter
```

### Required Capabilities
| Level | Capability | Required For |
|-------|------------|--------------|
| 0 | Export file to disk/USB | All |
| 1 | Write to media (SD, USB, floppy) | Standalone machines |
| 2 | Network transfer (FTP, SMB, proprietary) | Networked machines |
| 3 | Status polling (ready, running, paused, error) | Monitoring |
| 4 | Queue management (send, reorder, delete, priority) | Multi-job |
| 5 | Telemetry (position, speed, thread break, tension) | Real-time |
| 6 | Control (start, stop, pause, needle change) | Automation |

### Required Interfaces
```csharp
public interface IFormatAdapter
{
    string FormatName { get; }
    byte[] Encode(StitchPlan plan);
    StitchPlan Decode(byte[] data);
}

public interface ITransportAdapter
{
    string TransportName { get; }
    Task<TransferResult> SendAsync(byte[] data, MachineEndpoint endpoint);
    Task<byte[]> ReceiveAsync(MachineEndpoint endpoint);
}

public interface IMachineAdapter
{
    string Manufacturer { get; }
    string Model { get; }
    string FirmwareVersion { get; }
    IFormatAdapter Format { get; }
    ITransportAdapter Transport { get; }
    Task<MachineStatus> GetStatusAsync();
    Task<JobQueue> GetQueueAsync();
    Task<Telemetry> GetTelemetryAsync();
    Task<ControlResult> SendCommandAsync(MachineCommand command);
}
```

### Required Tests & Evidence
1. **Per-machine evidence** — Manufacturer, Model, Firmware, Format, Transport, Evidence (log/photo)
2. **Level 0** — Export to DST/PES/XXX verified on target machine
3. **Level 1-2** — Transfer success rate > 99% on 50+ jobs
4. **Level 3-4** — Status/queue polling accurate for 1hr continuous
5. **No invented protocols** — Every adapter references manufacturer spec or captured traffic

### Completion Gates
| Gate | Criterion |
|------|-----------|
| G7.1 | Architecture interfaces implemented, DI-registered |
| G7.2 | Level 0: 3+ format adapters with golden files |
| G7.3 | Level 1-2: 1+ transport adapter with transfer logs |
| G7.4 | Level 3-4: 1+ machine adapter with status/queue evidence |
| G7.5 | Zero "invented" protocols — all adapters cite evidence |

### Must NOT Be Considered Complete
- Machine support claimed without `evidence` field populated
- Protocol reverse-engineered without documentation
- Level 5/6 without safety interlocks (emergency stop, user confirmation)

### Dependencies
- Phase 2 (format adapters), Phase 5 (machine profiles), Foundation (serialization)

---

## PHASE 8 — ATLAS DOCTOR

### Objective
Deterministic diagnostic assistant for embroidery failures.

### Required Capabilities
| Input | Output |
|-------|--------|
| Symptom + Machine + Material + Design + Job + History | Ranked possible causes with Evidence, Safe Test, One-Variable Change, Expected Result |

### Symptom Taxonomy (minimum)
Thread Break, Needle Break, Bird Nest, Skipped Stitches, Puckering, Gaps, Registration Error, Trim Failure, Transfer Failure, File Error, Power/Communication

### Required Interface
```csharp
public interface IAtlasDoctor
{
    DiagnosisResult Diagnose(DiagnosisInput input);
}

public sealed class DiagnosisInput
{
    public string Symptom { get; set; }
    public MachineProfile Machine { get; set; }
    public MaterialProfile Material { get; set; }
    public AtlasProject Design { get; set; }
    public JobContext Job { get; set; }
    public IReadOnlyList<JobHistory> History { get; set; }
}

public sealed class DiagnosisResult
{
    public IReadOnlyList<PossibleCause> Causes { get; set; }
}

public sealed class PossibleCause
{
    public string Cause { get; set; }
    public string Evidence { get; set; }      // Why this cause fits
    public string SafeTest { get; set; }      // Non-destructive verification
    public string OneVariableChange { get; set; } // Single parameter to change
    public string ExpectedResult { get; set; }
    public double Confidence { get; set; }    // 0-1
}
```

### Required Tests & Evidence
1. **Knowledge base** — 50+ symptom→cause mappings with literature/evidence references
2. **Deterministic ranking** — same input → same ranked causes
3. **Safe test validity** — each SafeTest is non-destructive and actionable
4. **One-variable discipline** — each OneVariableChange isolates exactly one parameter
5. **No AI without validation** — AI proposals go through Validator → Human approval

### Completion Gates
| Gate | Criterion |
|------|-----------|
| G8.1 | 10+ symptoms with ≥3 causes each, all with Evidence/SafeTest/OneVariableChange |
| G8.2 | Deterministic: same input → identical ranked causes |
| G8.3 | AI proposals (if any) routed through Validator + Human approval gate |
| G8.4 | Knowledge base versioned, source-cited |

### Must NOT Be Considered Complete
- AI direct-to-machine without validation + authorization
- Causes without Evidence or SafeTest
- "ML model" without deterministic fallback

### Dependencies
- Phase 3 (validator), Phase 4 (digitization knowledge), Phase 5 (material/machine), Phase 6 (simulation risks), Phase 7 (telemetry)

---

## PHASE 9 — PRODUCTION

### Objective
End-to-end job management from quote to quality verification.

### Required Entities
Customer, Quote, Order, Job, Machine, Inventory (Thread, Needle, Material), Quality, Rework, History

### Required Capability: JobPackage
```csharp
public sealed class JobPackage
{
    public string DesignHash { get; set; }        // Content hash
    public string DesignVersion { get; set; }
    public byte[] Artifact { get; set; }          // Machine-ready format
    public MachineProfile Machine { get; set; }
    public MaterialProfile Material { get; set; }
    public ThreadProfile Thread { get; set; }
    public NeedleProfile Needle { get; set; }
    public HoopProfile Hoop { get; set; }
    public ValidationResult ValidatorResult { get; set; }
    public string Instructions { get; set; }       // Operator notes
    public FirstArticleResult? FirstArticle { get; set; }
}
```

### Required Tests & Evidence
1. **CRUD** — all entities with audit trail
2. **JobPackage integrity** — hash matches design, validator result included
3. **Traceability** — thread cone → job → quality result
4. **First article workflow** — mandatory before production run
5. **Rework tracking** — links back to original job + root cause

### Completion Gates
| Gate | Criterion |
|------|-----------|
| G9.1 | All 11 entities implemented with serialization |
| G9.2 | JobPackage round-trip (serialize/deserialize) preserves all fields |
| G9.3 | First article workflow enforced (cannot start run without) |
| G9.4 | Traceability query: thread lot → jobs → quality results < 100ms |

### Must NOT Be Considered Complete
- JobPackage without DesignHash + ValidatorResult
- Production entities without audit trail
- First article optional

### Dependencies
- Phase 2 (artifact), Phase 3 (validator), Phase 5 (profiles), Phase 7 (machine), Phase 8 (doctor for rework analysis)

---

## PHASE 10 — LEARNING / LAB

### Objective
Structured experiment registry for continuous improvement.

### Required Capabilities
| Record | Fields |
|--------|--------|
| Design | Hash, parameters, digitization profile |
| Machine | Profile, firmware, calibration date |
| Material | Profile, lot, environmental conditions |
| Result | Quality metrics, incidents, operator feedback |
| Incident | Type, timestamp, severity, root cause, corrective action |

### Feedback Loop
```
Good → reinforce parameters
Bad  → trigger Atlas Doctor → corrective action → verify
Comment → knowledge base annotation
```

### Knowledge Separation
- Private knowledge (per shop) — encrypted, local-only
- Shared knowledge (community) — opt-in, anonymized, versioned

### Completion Gates
| Gate | Criterion |
|------|-----------|
| G10.1 | Experiment record CRUD with all 5 entity types |
| G10.2 | Good/Bad feedback loop triggers Doctor automatically |
| G10.3 | Private/shared knowledge separation enforced at storage layer |
| G10.4 | Export/import of shared knowledge (anonymized) |

### Dependencies
- Phase 8 (Doctor), Phase 9 (production data)

---

## PHASE 11 — RELIABILITY / RECOVERY

### Objective
Auditable, restorable system state.

### Required Capabilities
| Feature | Specification |
|---------|---------------|
| Audit log | Immutable append-only, all mutations (design, job, machine, user) |
| Checksums | SHA-256 on all artifacts, verified on read |
| Backup | Automated, incremental, encrypted, off-site capable |
| Restore | Point-in-time, selective (design/job/machine), verified by hash |
| Health | Self-check: disk, DB, network, licenses, certificate expiry |
| Recovery | Job failure → pause → diagnose → resume from last checkpoint |
| No duplicate execution | Idempotent job start (token-based) |

### Real Test
```
Job → Failure → Restore → Verify Hash → Resume → No duplicate stitches
```

### Completion Gates
| Gate | Criterion |
|------|-----------|
| G11.1 | Audit log captures 100% of mutations, tamper-evident |
| G11.2 | Backup/restore cycle tested monthly, RTO < 15min, RPO < 1hr |
| G11.3 | Health endpoint returns actionable status |
| G11.4 | Failure injection test: job resumes from checkpoint without duplicate execution |

### Dependencies
- Phase 9 (production), Foundation (hash, serialization)

---

## PHASE 12 — AI ASSISTED EMBROIDERY

### Objective
AI as assistant **on top of** validated deterministic pipeline.

### AI Capabilities (proposal only)
- Analyze design → suggest digitization strategy
- Suggest parameters (stitch type, density, underlay, pull comp)
- Detect risks (via Validator + simulation)
- Suggest corrections (via Doctor)
- Help diagnose failures (via Doctor + telemetry)

### Mandatory Flow
```
AI Proposal → Validator → Human Approval → Execution → Audit
```
**Never:** AI → Machine (without validation + authorization)

### Required Interface
```csharp
public interface IAIAssistant
{
    Task<AIProposal> AnalyzeDesignAsync(AtlasProject project, AnalysisContext context);
    Task<AIProposal> SuggestParametersAsync(StitchPlan plan, MaterialProfile material);
    Task<AIProposal> DetectRisksAsync(StitchPlan plan, MachineProfile machine);
    Task<AIProposal> SuggestCorrectionsAsync(DiagnosisInput input);
}

public sealed class AIProposal
{
    public string ProposalId { get; set; }
    public string Description { get; set; }
    public object Payload { get; set; }        // Structured suggestion
    public double Confidence { get; set; }
    public string Reasoning { get; set; }
    public ValidationResult Validation { get; set; } // Pre-computed
    public bool RequiresHumanApproval { get; set; } = true;
}
```

### Completion Gates
| Gate | Criterion |
|------|-----------|
| G12.1 | All 4 AI capabilities implemented as proposals (not actions) |
| G12.2 | Every proposal includes pre-computed ValidationResult |
| G12.3 | Human approval gate enforced (cannot bypass) |
| G12.4 | Audit log records: proposal, validation, approval, execution, outcome |
| G12.5 | Zero direct AI→Machine paths in codebase |

### Must NOT Be Considered Complete
- AI that writes files / sends to machine without human approval
- Proposals without ValidationResult
- "Autonomous digitization" without human-in-the-loop

### Dependencies
- Phase 3 (Validator), Phase 4 (digitization), Phase 6 (Simulation), Phase 8 (Doctor), Phase 11 (Audit)

---

## CROSS-PHASE PRINCIPLES (Enforced at All Phases)

| Principle | Enforcement |
|-----------|-------------|
| **No invented protocols/formats/capabilities** | Every external integration cites manufacturer spec or captured traffic |
| **Determinism by default** | Same input → same output; randomness opt-in via `RandomSeed` |
| **Validation before execution** | Proposal → Validator → Approval → Execution (all phases) |
| **Evidence-based** | Every claim (support, accuracy, compatibility) has test log / golden file / measurement |
| **Security first** | All external input untrusted; bounds checks; size limits; no network in core |
| **Reproducibility** | Design can answer: what/why/parameters/machine/material/version/format/algorithm |
| **Small semantic commits** | `feat:`, `fix:`, `test:` — no mixed feature+refactor+format commits |
| **Gates before merge** | All phase gates PASS in CI before merge to main |

---

## VERSIONING & RELEASE STRATEGY

| Version | Contains |
|---------|----------|
| 1.0.0 | Foundation (Phases 0-1) — **CURRENT** |
| 2.0.0 | Phase 2 (Formats) |
| 3.0.0 | Phase 3 (Validator) |
| 4.0.0 | Phase 4 (Digitization) |
| 5.0.0 | Phase 5 (Material Lab) |
| 6.0.0 | Phase 6 (Simulation V2) |
| 7.0.0 | Phase 7 (Machine Bridge L0-L2) |
| 8.0.0 | Phase 8 (Atlas Doctor) |
| 9.0.0 | Phase 9 (Production) |
| 10.0.0 | Phase 10 (Learning) |
| 11.0.0 | Phase 11 (Reliability) |
| 12.0.0 | Phase 12 (AI Assistant) |

Each minor version = patch/bugfix within phase. Major version = phase completion.

---

## DEFINITION OF DONE (Global)

A phase is **DONE** iff:
1. All its **Completion Gates** are PASS (verified in CI)
2. All **Required Tests** exist and pass
3. All **Interfaces** are implemented, documented, DI-registered
4. **Security/Determinism** requirements met
5. **Must NOT** items verified absent
6. **Dependencies** satisfied (previous phases CLOSED)
7. Evidence artifacts committed (golden files, logs, measurements)

**No phase may be declared complete without measurable evidence.**