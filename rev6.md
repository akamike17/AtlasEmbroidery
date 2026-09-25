# AtlasEmbroidery — Autonomous Continuation / Surgical Phase 2 Completion

## Mission

Continue the development of `akamike17/AtlasEmbroidery` from the current repository state.

This is an **autonomous engineering continuation**, not a planning exercise.

The agent is authorized to:

* inspect the entire repository;
* inspect Git history and current branch state;
* inspect all relevant source, tests, documentation, fixtures, and configuration;
* perform deep architectural and forensic audits;
* edit existing files;
* create new files;
* add and modify tests;
* run builds;
* run unit/integration tests;
* run static analysis where available;
* inspect generated artifacts;
* compare Git diffs;
* use external technical documentation when required to validate file-format behavior;
* implement corrections;
* repeat the audit after every significant correction;
* create commits;
* push commits to the current working branch.

Do **not** repeatedly ask for permission to perform normal engineering work.

Do **not** stop merely because a task requires several iterations.

Do **not** stop after discovering the first problem.

Continue through the entire scope until the repository reaches a defensible verified state.

The agent must distinguish between:

1. **routine engineering decisions** — decide autonomously;
2. **technical ambiguity** — investigate and resolve with evidence;
3. **true external blockers** — stop only when progress genuinely requires something unavailable, such as credentials, inaccessible external data, unavailable hardware, or a legally/licensing-constrained corpus that cannot reasonably be obtained.

If a true blocker exists, document exactly:

* what is blocked;
* why it is blocked;
* what was attempted;
* what evidence proves the blocker;
* what can still be completed without it.

Do not manufacture a PASS around a blocker.

---

# 1. Repository Identity

Repository:

`akamike17/AtlasEmbroidery`

Current known master state:

`d56aace680b4b4c026b83b8763c116a7bfe3ed67`

Known current commit:

`feat(phase2): implement format interfaces, DST/SVG adapters with validation, round-trip, golden files, malformed tests`

The current repository reportedly contains Phase 2 work involving:

* format interfaces;
* DST adapter;
* SVG adapter;
* validation;
* round-trip tests;
* golden files;
* malformed-input tests.

Known current evidence includes:

* approximately 284 tests;
* 11 reported round-trip failures;
* approximately 57% line coverage;
* approximately 44% branch coverage.

These numbers are **starting evidence only**.

Re-run everything.

Never trust an old report over executable repository evidence.

---

# 2. Non-Negotiable Engineering Rules

## 2.1 Evidence beats documentation

Never declare a feature complete because:

* `rev4.md` says complete;
* a commit message says complete;
* a previous report says PASS;
* a test name sounds positive;
* an implementation exists.

A feature is complete only when current executable evidence supports the claim.

If documentation conflicts with tests, source behavior, Git state, or real external interoperability evidence:

**the documentation is wrong until proven otherwise.**

Correct the documentation.

---

## 2.2 Do not hide failures

Never:

* convert failing tests into skipped tests merely to obtain green CI;
* weaken assertions to make tests pass;
* delete failing tests without technical justification;
* suppress exceptions;
* mark known failures as warnings;
* change expected values solely to match broken implementation;
* remove coverage requirements;
* fabricate external corpus evidence.

A green test suite obtained by reducing test quality is not a PASS.

---

## 2.3 No premature phase closure

Do not close Phase 2 merely because the implementation exists.

The minimum closure requirement is:

> implementation + tests + independent protocol evidence + semantic correctness + deterministic behavior + documentation consistency + clean Git evidence.

If one of these remains unresolved, Phase 2 remains OPEN.

---

## 2.4 Preserve Foundation

Treat the previously verified Foundation as frozen unless current evidence demonstrates a genuine defect.

Do not redesign Foundation merely to make Phase 2 easier.

Do not perform unrelated refactoring.

Do not mix AtlasEmbroidery with:

* AtlasMI;
* AtlasMail;
* AtlasGT;
* AtlasBuho;
* AtlasSEP;
* AtlasNOC;
* unrelated repositories.

This repository must remain isolated.

---

# 3. FIRST OPERATION — FULL READ-ONLY AUDIT

Before modifying anything, inspect:

```text
git status
git branch --show-current
git log --oneline --decorate -30
git diff
git diff --cached
git ls-files
```

Then inspect the complete relevant structure.

At minimum investigate:

```text
Domain/
Tests/
Infrastructure/
Application/
Documentation/
*.md
*.csproj
*.sln
*.json
.gitignore
```

Locate every implementation related to:

```text
Format
DST
SVG
PES
JEF
EXP
VP3
HUS
XXX
TAP
thread
stitch
command
jump
trim
stop
color
palette
validation
normalization
round-trip
golden
fixture
```

Do not assume paths.

Search the repository.

Build a dependency map before changing code.

---

# 4. READ THE CURRENT DOCUMENTATION

Read all relevant project documentation, especially:

```text
rev*.md
README*
docs/**
```

Determine:

* what Phase 1 actually delivered;
* what Phase 2 claims to deliver;
* what Phase 2 actually implements;
* what the tests actually prove;
* which claims are unsupported;
* which claims contradict executable evidence.

Create an internal matrix:

| Capability              | Documentation | Source | Tests | External Evidence | Actual Status |
| ----------------------- | ------------- | ------ | ----- | ----------------- | ------------- |
| DST read                | ?             | ?      | ?     | ?                 | ?             |
| DST write               | ?             | ?      | ?     | ?                 | ?             |
| DST validation          | ?             | ?      | ?     | ?                 | ?             |
| DST semantic round-trip | ?             | ?      | ?     | ?                 | ?             |
| DST binary determinism  | ?             | ?      | ?     | ?                 | ?             |
| SVG read                | ?             | ?      | ?     | ?                 | ?             |
| SVG write               | ?             | ?      | ?     | ?                 | ?             |
| normalization           | ?             | ?      | ?     | ?                 | ?             |
| palette handling        | ?             | ?      | ?     | ?                 | ?             |

Do not leave the matrix unresolved.

---

# 5. BUILD BASELINE

Run the repository's real build commands.

Determine the actual:

* SDK;
* target framework;
* test framework;
* analyzer configuration;
* warning policy;
* coverage tooling;
* integration-test requirements.

Run at minimum:

```text
dotnet restore
dotnet build
dotnet test
```

Use the repository's own prescribed commands if they differ.

Capture:

* test count;
* passed;
* failed;
* skipped;
* warnings;
* errors;
* duration;
* coverage if configured.

Do not trust previous numbers.

---

# 6. DST IS THE PRIMARY BLOCKER

Do not proceed to additional embroidery formats until DST is technically defensible.

The current evidence reportedly contains:

> 11 round-trip failures.

Treat those failures as real defects until individually explained.

Do not simply modify the expected result.

---

# 7. DST IMPLEMENTATION FORENSIC AUDIT

Inspect the entire DST implementation.

Known relevant files include:

```text
Domain/Formats/Dst/DstFormatAdapter.cs
Domain/Formats/Dst/DstGoldenFiles.cs
Tests/Domain.Tests/Formats/DstRoundTripTests.cs
Domain/Formats/FormatTypes.cs
```

Trace every path:

```text
external bytes
    ↓
DST parser
    ↓
decoded commands
    ↓
StitchPlan
    ↓
normalization
    ↓
encoder
    ↓
DST bytes
```

Document every transformation.

Pay particular attention to:

* coordinate units;
* sign handling;
* integer rounding;
* coordinate origin;
* extents;
* offsets;
* header fields;
* stitch record encoding;
* command flags;
* jump handling;
* trim handling;
* stop handling;
* color changes;
* end-of-design;
* padding;
* record termination;
* maximum stitch length;
* malformed records;
* truncated files;
* empty designs;
* single-stitch designs;
* negative coordinates;
* large coordinates;
* repeated coordinates;
* zero-length stitches;
* long jumps;
* multiple color changes.

---

# 8. UNIT AUDIT — DO NOT GUESS

Determine exactly what coordinate unit the domain model uses.

If the domain model uses microns, prove it from source and tests.

Then determine exactly what DST uses.

Do not assume.

Investigate independent technical references and/or known-good independent implementations.

A suspicious existing value is:

```text
MaxStitchLength = 1270
```

with documentation reportedly describing this as approximately:

```text
12.7 mm
```

If the internal unit is microns, then:

```text
1270 µm = 1.27 mm
```

while:

```text
12.7 mm = 12,700 µm
```

Do not automatically change the value.

First determine the actual encoding semantics.

Then implement the correct conversion explicitly.

Add tests that prove:

```text
domain units
    ↔
DST encoding units
```

including boundary values.

No implicit unit conversions.

Prefer named conversion functions/constants such as:

```text
MicronsToDstUnits(...)
DstUnitsToMicrons(...)
```

rather than magic arithmetic scattered throughout the adapter.

---

# 9. DST COMMAND ENCODING AUDIT

Audit the current command representation.

Known suspicious implementation details include:

```text
DstFlags:
Jump        = 0x01
ColorChange = 0x02
Trim        = 0x04
Stop        = 0x08
End         = 0x03
```

and a low-nibble flag encoding approach.

There is also reportedly:

```text
StitchControlByte = 0x80
```

which may be unused.

Do NOT assume the existing bit layout is correct merely because encoder and decoder agree.

Internal symmetry proves only:

> encoder ↔ decoder compatibility.

It does not prove:

> compatibility with actual DST producers.

Investigate independent references and independent implementations.

For every command encoding rule, document:

* source;
* evidence;
* byte representation;
* decoded meaning;
* test fixture;
* edge cases.

If external sources disagree, document the disagreement and determine behavior through actual interoperable evidence where possible.

---

# 10. EXTERNAL DST EVIDENCE

The existing golden-file system reportedly generates its own golden files using the project's own encoder.

That produces:

```text
our encoder
    ↓
our golden file
    ↓
our decoder
```

This is NOT independent interoperability evidence.

Replace the conceptual evidence model with:

```text
independent DST producer
    ↓
real DST file
    ↓
our decoder
    ↓
semantic StitchPlan
```

and:

```text
independent DST producer
    ↓
real DST file
    ↓
our decoder
    ↓
our encoder
    ↓
our decoder
    ↓
semantic comparison
```

Acquire or identify an independent corpus.

Prefer at least five real DST files with meaningful variation:

1. simple design;
2. multiple jumps;
3. color changes;
4. dense/complex design;
5. negative or offset-heavy geometry;
6. long jumps where available;
7. multiple thread changes where available.

Do not commit copyrighted/commercial files if licensing is unclear.

If external files cannot legally be committed:

Create a reproducible fixture manifest containing as much as legally possible:

```text
fixture identifier
source
license/status
SHA-256
file size
expected metadata
expected stitch count
expected bounds
expected command counts
```

Provide a local corpus directory convention, for example:

```text
Tests/Fixtures/External/Dst/
```

and make tests detect whether the corpus is present.

Do not report external corpus tests as PASS if the corpus was not actually available.

---

# 11. SEPARATE BINARY ROUND-TRIP FROM SEMANTIC ROUND-TRIP

This is mandatory.

Current tests reportedly perform binary comparison after round-trip.

That is too strict as the sole correctness criterion.

Implement two independent concepts.

## 11.1 Binary determinism

Test:

```text
same semantic model
    ↓
encoder
    ↓
bytes A

same semantic model
    ↓
encoder
    ↓
bytes B
```

Then:

```text
A == B
```

This proves deterministic output.

---

## 11.2 Semantic round-trip

Test:

```text
DST A
 ↓
Read
 ↓
Model A
 ↓
Write
 ↓
DST B
 ↓
Read
 ↓
Model B
```

Compare:

```text
geometry
stitch sequence
command semantics
jump semantics
trim semantics
stop semantics
color-change semantics
bounds
stitch count
command count
palette/thread semantics where representable
```

Do NOT require:

```text
DST A bytes == DST B bytes
```

unless a specific test is explicitly intended to verify binary reproducibility.

---

# 12. CREATE A REAL SEMANTIC COMPARATOR

Do not compare entire objects blindly.

Create an explicit semantic comparison mechanism.

It should be able to produce useful diagnostics such as:

```text
Mismatch:
Index: 184
Expected:
    X = ...
    Y = ...
    Type = Jump

Actual:
    X = ...
    Y = ...
    Type = Running
```

Also report:

```text
expected stitch count
actual stitch count

expected jump count
actual jump count

expected color-change count
actual color-change count

expected bounds
actual bounds
```

For floating-point values, use an explicit documented tolerance only where mathematically justified.

Do not use arbitrary tolerances to hide encoding errors.

---

# 13. PRESERVE COMMAND SEMANTICS

Inspect whether `ReadAsync()` collapses imported DST content into something equivalent to:

```text
Imported DST
    ↓
single ShapeObject
    ↓
mostly Running stitches
```

If so, determine exactly what information is being lost.

DST import must preserve supported semantics.

At minimum investigate:

```text
running stitch
jump
trim
stop
color change
end
```

If the format cannot distinguish two concepts reliably, document that fact.

Never silently convert semantically meaningful commands into ordinary running stitches.

If the domain model cannot represent an imported command, determine whether:

1. the model should be extended;
2. the information should be represented another way;
3. the information genuinely cannot be preserved.

Make the decision based on architecture, not convenience.

---

# 14. PALETTE / THREAD SEMANTICS

Audit current palette behavior.

If the reader does something equivalent to:

```text
GenerateDefaultPalette(colorCount)
```

then this is not true thread metadata preservation.

Determine exactly what DST provides.

Distinguish:

```text
preserved information
```

from:

```text
synthesized information
```

The API/documentation must not claim exact thread identity when the source format does not provide it.

Use terminology such as:

```text
reconstructed palette
default palette
synthetic color assignment
```

only where technically appropriate.

Tests must verify the documented behavior.

---

# 15. NORMALIZE() MUST BE REAL OR HONESTLY INCOMPLETE

Search for:

```text
Normalize
TODO
FIXME
placeholder
not implemented
NotImplementedException
```

If `Normalize()` contains a placeholder such as:

```text
// Placeholder - actual deduplication would need StitchPlan
```

then do not count normalization as complete.

Either:

### Option A — Implement it properly

or:

### Option B — Clearly mark it as incomplete

Do not create a fake implementation merely to satisfy a status document.

If implemented:

* define normalization rules;
* define what information may be changed;
* define what must never change;
* add before/after semantic tests;
* test idempotence:

```text
Normalize(Normalize(x)) == Normalize(x)
```

where appropriate.

---

# 16. VALIDATOR AUDIT

Inspect validation behavior carefully.

Known concern:

```text
maxCheck = Math.Min(1000, ...)
```

If this causes only the first 1000 stitches to be validated, determine whether that is acceptable.

For authoritative validation, the default should not silently validate only a prefix.

If performance requires a fast mode, make it explicit:

```text
FastValidation
StrictValidation
```

or equivalent.

Document the distinction.

---

# 17. EXCEPTION-HANDLING AUDIT

Search for broad patterns:

```text
catch (Exception ex)
```

especially in parsers and validators.

Expected malformed input should be handled.

Unexpected programmer errors must not silently become:

```text
Invalid file
```

or:

```text
Validation warning
```

Distinguish:

```text
malformed external input
```

from:

```text
internal software defect
```

Add tests for malformed input while preserving visibility of genuine implementation errors.

---

# 18. MALFORMED DST TEST MATRIX

Expand malformed tests.

At minimum test:

```text
empty file
short header
header only
truncated record
odd byte count
partial record
invalid command
missing end
multiple end records
garbage after end
extreme coordinate
integer overflow boundary
negative coordinate
zero movement
extremely long movement
huge stitch count
corrupt metadata
invalid encoding
```

Tests must prove:

* no crashes;
* no infinite loops;
* no memory explosions;
* useful diagnostics;
* deterministic rejection;
* no silent data corruption.

---

# 19. FUZZ / PROPERTY-STYLE TESTING

If the repository architecture allows it, add bounded property tests for DST decoding.

Properties should include:

```text
decoder never hangs
decoder never reads outside buffer
decoder either produces valid semantic output or controlled failure
encoder output is deterministic
valid generated designs can be decoded
semantic round-trip preserves supported commands
```

Keep fuzzing bounded and reproducible.

Do not introduce an enormous dependency for trivial testing.

---

# 20. EDGE-CASE MATRIX

Create explicit tests for:

### Geometry

```text
(0,0)
positive coordinates
negative coordinates
mixed signs
very small movements
maximum supported movement
just above maximum
large design bounds
```

### Commands

```text
run
jump
trim
stop
color change
end
```

### Sequences

```text
run → run
run → jump
jump → run
jump → trim
color change → run
multiple color changes
end
```

### Design structure

```text
empty
one stitch
one jump
one color
multiple colors
dense design
large design
```

---

# 21. DETERMINISTIC ENCODING

Verify that identical semantic input produces identical output bytes.

Run the encoder multiple times.

Check:

```text
SHA256(output1) == SHA256(output2)
```

Do not allow:

* timestamps;
* random IDs;
* nondeterministic ordering;
* dictionary iteration differences;
* environment-specific metadata;

unless explicitly required by the format.

If metadata requires timestamps, define deterministic test behavior.

---

# 22. SVG AUDIT

Do not ignore SVG merely because DST is the current blocker.

After DST corrections, audit SVG for:

* coordinate system;
* units;
* scaling;
* transforms;
* path parsing;
* unsupported SVG constructs;
* curves;
* lines;
* groups;
* colors;
* stroke width;
* fill semantics;
* viewBox;
* malformed XML;
* deterministic output;
* semantic round-trip where applicable.

Determine exactly what SVG means in AtlasEmbroidery.

SVG is not automatically an embroidery format.

Document the conversion semantics:

```text
SVG geometry
    ↓
embroidery geometry
    ↓
stitch generation
```

and what information is intentionally discarded.

---

# 23. FORMAT INTERFACE AUDIT

Inspect:

```text
Domain/Formats/FormatTypes.cs
```

and all interfaces.

Verify that format adapters have consistent contracts for:

```text
Read
Write
Validate
Capabilities
Metadata
errors
cancellation
streams
disposal
```

Avoid format-specific hacks leaking into shared abstractions.

If an abstraction is too weak for DST command preservation, improve it deliberately.

Do not make unrelated architecture changes.

---

# 24. THREAD SAFETY / STREAM OWNERSHIP

Audit every adapter for:

* stream ownership;
* `leaveOpen`;
* async correctness;
* cancellation tokens;
* disposal;
* repeated reads;
* repeated writes;
* concurrent use assumptions.

Add tests where appropriate.

---

# 25. RESOURCE LIMITS

Embroidery files can be malformed or intentionally huge.

Determine safe limits for:

```text
file size
stitch count
header size
memory allocation
coordinate magnitude
metadata size
```

Ensure malformed input cannot trivially trigger uncontrolled memory consumption.

Do not add arbitrary limits without documenting why they exist.

---

# 26. TEST QUALITY AUDIT

Inspect tests for:

* tests that only exercise the happy path;
* tests generated from the same implementation under test;
* weak assertions;
* assertions that only check “no exception”;
* golden files generated by the encoder itself;
* tests that never inspect semantic content;
* tests that pass for the wrong reason.

Every important format behavior must have an assertion tied to an externally meaningful requirement.

---

# 27. COVERAGE

Run actual coverage.

Do not chase a percentage blindly.

Prioritize uncovered branches in:

```text
parsers
encoders
command decoding
validation
malformed input
boundary handling
error handling
```

If coverage remains below project target, either:

1. add meaningful tests;
2. document a justified exclusion.

Do not exclude code merely to improve the percentage.

---

# 28. DOCUMENTATION REPAIR

After implementation stabilizes, repair all stale documentation.

Especially inspect:

```text
rev4.md
README.md
phase documents
format status tables
test reports
```

Documentation must reflect actual evidence.

For each format record:

```text
Implemented
Tested
Externally verified
Known limitations
Not supported
```

Do not use:

```text
Complete
Production ready
Interoperable
Verified
```

unless evidence supports the exact claim.

---

# 29. PHASE 2 STATUS MODEL

Use explicit statuses.

Recommended:

```text
NOT STARTED
IN PROGRESS
IMPLEMENTED
LOCALLY VERIFIED
EXTERNALLY VERIFIED
BLOCKED
COMPLETE
```

Do not collapse:

```text
implemented
```

into:

```text
complete
```

---

# 30. DST ACCEPTANCE GATES

DST may only be marked COMPLETE when all applicable gates pass.

## Gate DST-A — Build

```text
dotnet build
```

must pass.

---

## Gate DST-B — Tests

All required tests pass.

No unexplained failures.

No hidden/skipped failures.

---

## Gate DST-C — Unit Correctness

Coordinate conversions proven.

Command encoding proven.

Boundary conditions tested.

---

## Gate DST-D — Semantic Round-Trip

Real supported DST semantics survive:

```text
read → write → read
```

---

## Gate DST-E — Binary Determinism

Same semantic input produces deterministic bytes.

---

## Gate DST-F — Malformed Input

Malformed/truncated data is rejected safely.

---

## Gate DST-G — External Evidence

At least one genuinely independent source/corpus validates interoperability.

Prefer multiple real fixtures.

---

## Gate DST-H — Documentation

Documentation matches actual behavior.

---

## Gate DST-I — Git Integrity

No unrelated changes.

No accidental generated garbage.

No secrets.

No temporary debugging files.

No untracked forensic artifacts unless intentionally documented.

---

# 31. DO NOT MOVE TO PES/JEF/etc. PREMATURELY

Do not implement additional embroidery formats merely to increase the feature count while DST remains unreliable.

The correct priority is:

```text
DST
 ↓
verified abstraction
 ↓
external evidence
 ↓
stable format contract
 ↓
next format
```

Once DST is genuinely complete, inspect the repository for the next highest-value format.

Do not blindly implement every format in one pass.

---

# 32. FORMAT EXTENSION POLICY

For every future format:

1. obtain protocol documentation;
2. inspect independent implementations;
3. obtain real-world fixtures;
4. define semantic mapping;
5. implement reader;
6. implement writer;
7. implement validator;
8. add malformed tests;
9. add deterministic output tests;
10. add semantic round-trip;
11. add external interoperability evidence;
12. document limitations;
13. only then mark complete.

Never repeat the current golden-file trap:

```text
encoder-generated golden
```

being treated as:

```text
independent external validation
```

---

# 33. GIT DISCIPLINE

Before changes:

```text
git status
git log --oneline -20
```

After changes:

```text
git diff
git status
```

Before commit:

```text
dotnet build
dotnet test
```

plus all project-specific checks.

Inspect the final diff manually.

Do not commit:

```text
bin/
obj/
temporary files
IDE caches
local secrets
API keys
credentials
private corpus files
```

unless explicitly required and safe.

---

# 34. COMMIT STRATEGY

Make commits coherent.

Preferred structure:

```text
fix(phase2): correct DST unit and command semantics
```

then:

```text
test(phase2): add independent DST semantic corpus coverage
```

then:

```text
docs(phase2): reconcile DST verification status
```

However, do not manufacture many commits solely for appearance.

If a single coherent surgical commit is cleaner, use one.

Commit messages must describe what was actually done.

---

# 35. PUSH POLICY

Push successful commits to the current branch.

Do NOT:

```text
force push
rewrite public history
delete branches
reset shared history
```

unless explicitly required by a genuine repository emergency.

Never use:

```text
git push --force
```

as routine cleanup.

---

# 36. FINAL VERIFICATION LOOP

After implementation:

```text
git status
git diff
git log --oneline -10
dotnet build
dotnet test
```

Then repeat the relevant format tests independently.

If possible:

```text
clean checkout / clean build
```

or equivalent verification.

Ensure tests do not depend accidentally on:

* stale binaries;
* local files;
* untracked fixtures;
* developer-specific paths;
* environment variables not documented.

---

# 37. FINAL FORENSIC REVIEW

Before declaring anything complete, reread the changed source from top to bottom.

Do not rely solely on the diff.

Look specifically for:

```text
TODO
FIXME
placeholder
temporary
hack
debug
throw new NotImplementedException
catch (Exception
magic numbers
magic units
silent fallback
generated golden
fake fixture
```

Resolve every relevant item.

---

# 38. FINAL REPORT REQUIREMENTS

At the end, produce a Spanish engineering report.

The report must contain:

## 38.1 Repository

```text
Repository:
Branch:
Base SHA:
Final SHA:
Working tree:
```

## 38.2 Work performed

List concrete changes.

## 38.3 Tests

Report actual execution:

```text
Build:
Tests:
Passed:
Failed:
Skipped:
Coverage:
```

Do not invent numbers.

## 38.4 DST status

Explicitly report:

```text
DST:
NOT STARTED
IN PROGRESS
IMPLEMENTED
LOCALLY VERIFIED
EXTERNALLY VERIFIED
BLOCKED
COMPLETE
```

with evidence.

## 38.5 External corpus

List:

```text
fixture
source
license/status
hash
result
```

Do not claim a fixture was tested if it was not actually present.

## 38.6 Known limitations

List every meaningful limitation.

## 38.7 Documentation changes

List modified reports/docs.

## 38.8 Git evidence

Provide:

```text
commit SHA
commit message
push result
working tree status
```

## 38.9 Remaining blockers

Only real blockers.

Do not manufacture blockers to avoid finishing.

---

# 39. ABSOLUTE PROHIBITIONS

Never:

* fake test results;
* fake external interoperability;
* fake coverage;
* claim a generated fixture is independent;
* silently discard DST commands;
* hide parser exceptions;
* weaken assertions;
* delete failing tests to obtain green;
* mark a phase complete while known failures remain;
* introduce unrelated architectural rewrites;
* mix other Atlas repositories into this work;
* force-push;
* commit secrets;
* invent protocol behavior.

---

# 40. AUTONOMOUS EXECUTION RULE

Work continuously.

Do not ask:

> “Should I inspect this file?”

Inspect it.

Do not ask:

> “Should I fix this failing test?”

Investigate and fix it.

Do not ask:

> “Should I update the documentation?”

Update it.

Do not ask:

> “Should I run the tests again?”

Run them.

Do not ask:

> “Should I commit?”

If the implementation is coherent, verified, and the repository is clean of unrelated changes, commit it.

Do not ask for routine permission.

Only stop for a **true external blocker**.

---

# 41. DEFINITION OF DONE

The task is NOT:

> “make the current tests green.”

The task is:

> Make AtlasEmbroidery's current format infrastructure technically defensible, beginning with DST, using executable tests, independent protocol evidence, semantic verification, deterministic output, robust malformed-input handling, and documentation that tells the truth.

The final state must allow another engineer to inspect the repository and independently understand:

```text
what is implemented
what is verified
what is externally verified
what is unsupported
what is synthesized
what remains incomplete
why each conclusion is justified
```

Do not optimize for a prettier report.

Optimize for evidence.

---

# 42. EXECUTION ORDER

Follow this order unless repository evidence proves a different order is necessary:

```text
1. Read repository
2. Inspect Git
3. Baseline build/test
4. Audit documentation
5. Map Phase 2
6. Audit DST implementation
7. Audit coordinate units
8. Audit command encoding
9. Audit reader semantics
10. Audit palette semantics
11. Audit validator
12. Audit exception handling
13. Build independent DST evidence
14. Fix DST implementation
15. Add semantic comparator
16. Separate semantic vs binary round-trip
17. Add malformed/boundary tests
18. Add deterministic tests
19. Re-run full suite
20. Re-audit source
21. Repair documentation
22. Review Git diff
23. Commit
24. Push
25. Re-run final verification
26. Report exact evidence
27. Only then determine next format
```

---

# 43. FINAL PRINCIPLE

**Do not declare victory because the code looks finished.**

Declare completion only when:

```text
SOURCE
  +
TESTS
  +
PROTOCOL EVIDENCE
  +
REAL FIXTURES
  +
SEMANTIC VERIFICATION
  +
DETERMINISM
  +
MALFORMED INPUT SAFETY
  +
DOCUMENTATION
  +
GIT EVIDENCE
```

all agree.

If they disagree:

**investigate the disagreement and resolve it.**

Do not paper over it.

Proceed autonomously.
