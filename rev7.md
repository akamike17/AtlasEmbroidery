# AtlasEmbroidery — DST Forensic Protocol Audit & Correctness Pass

## Mission

Continue from commit:

```text
e1007b0
```

Repository:

```text
akamike17/AtlasEmbroidery
```

The previous implementation reported:

```text
300 tests passed
0 failed
0 warnings
0 errors
64.1% line coverage
50.0% branch coverage
```

Do **not** interpret this as proof that DST is correct.

This task is a forensic correctness pass over the current DST implementation.

The objective is to establish whether AtlasEmbroidery's DST encoder, decoder, validator, semantic model, and tests actually conform to the independently documented DST/Tajima record structure.

Do not optimize for keeping the current implementation.

Optimize for technical truth.

---

# 1. AUTONOMOUS EXECUTION

Work autonomously.

Do not ask permission for normal engineering actions.

You may:

* inspect the repository;
* inspect Git history;
* inspect source;
* inspect tests;
* inspect documentation;
* inspect external technical references;
* add tests;
* modify implementation;
* modify documentation;
* create fixtures;
* run builds;
* run test suites;
* run targeted mathematical/protocol tests;
* create commits;
* push commits.

Do not repeatedly ask for approval.

Stop only for a genuine external blocker.

Do not force-push or rewrite shared Git history.

Do not modify unrelated Atlas repositories.

---

# 2. STARTING POINT

Start from:

```text
e1007b0
```

First run:

```text
git status
git branch --show-current
git log --oneline --decorate -20
git show --stat --oneline e1007b0
git diff e1007b0^ e1007b0
```

Then inspect the complete current DST implementation.

Do not trust previous reports.

---

# 3. CURRENT CLAIM

The current repository reports:

```text
300/300 tests PASS
```

That is useful evidence for regression stability.

It is NOT sufficient evidence for protocol correctness.

This audit must determine:

```text
Does the encoder actually produce valid DST bytes?
Does the decoder correctly interpret valid DST bytes?
Are all coordinate signs represented correctly?
Are all ternary weights represented correctly?
Are command bits correct?
Are limits correct?
Is END encoded correctly?
Are long movements split correctly?
Are semantic commands preserved?
```

---

# 4. INDEPENDENT DST PROTOCOL BASELINE

Use independent technical references.

The protocol evidence currently available includes:

* KDE's Tajima Ternary documentation;
* independent DST format documentation;
* independent implementations/documentation such as pyembroidery/libembroidery where useful.

Record the references used in the repository documentation.

One important independent reference describes the three-byte structure and the bit mapping:

```text
Byte 1:
Y +1
Y -1
Y +9
Y -9
X -9
X +9
X -1
X +1

Byte 2:
Y +3
Y -3
Y +27
Y -27
X -27
X +27
X -3
X +3

Byte 3:
Jump
Stop/Color
Y +81
Y -81
X -81
X +81
1
1
```

The two low bits are synchronization bits and must be set.

The END record is:

```text
00 00 F3
```

The documented coordinate resolution is:

```text
0.1 mm
```

and the maximum representable absolute delta on one axis in one record is:

```text
1 + 3 + 9 + 27 + 81 = 121
```

Therefore:

```text
±121 units
=
±12.1 mm
```

per axis.

Treat these as protocol evidence to validate against additional independent references, not as permission to blindly copy code.

---

# 5. CRITICAL CORRECTION: BALANCED TERNARY

Audit the current movement encoder mathematically.

The implementation must correctly represent:

```text
-121 through +121
```

using the signed powers:

```text
1
3
9
27
81
```

with each weight independently taking:

```text
-1
0
+1
```

The encoding is not ordinary binary.

It is not:

```text
abs(delta)
→ encode positive magnitude
→ apply one global sign
```

unless that algorithm is mathematically proven equivalent to the required bit representation.

Do not assume it is.

---

# 6. EXHAUSTIVE ENCODING TEST

Create an exhaustive test for every possible axis delta:

```text
-121
-120
...
-2
-1
0
1
2
...
120
121
```

For every value:

1. encode X;
2. decode X;
3. assert exact equality.

Repeat independently for Y.

Then test combinations:

```text
X = -121..121
Y = -121..121
```

Do not necessarily create 58,564 individual named tests if unnecessary; a deterministic exhaustive test is acceptable.

The test must prove:

```text
Decode(Encode(x)) == x
```

for every valid axis value.

---

# 7. BYTE-LEVEL GOLDEN VECTORS

Do not rely only on encode/decode symmetry.

Create explicit byte-level vectors for representative movements.

At minimum:

```text
(0,0)
(1,0)
(-1,0)
(3,0)
(-3,0)
(9,0)
(-9,0)
(27,0)
(-27,0)
(81,0)
(-81,0)
(121,0)
(-121,0)

(0,1)
(0,-1)
(0,3)
(0,-3)
(0,9)
(0,-9)
(0,27)
(0,-27)
(0,81)
(0,-81)
(0,121)
(0,-121)
```

Then mixed values:

```text
(2,2)
(-2,-2)
(4,-4)
(10,-10)
(28,-28)
(82,-82)
(120,-120)
(121,-121)
```

The expected bytes must be independently derived from the protocol mapping.

Do not generate the expected bytes using the implementation under test.

That would recreate the same circular-test problem.

---

# 8. COMMAND BYTE AUDIT

Explicitly test the third byte.

Verify:

```text
normal stitch
jump
color-change/stop
end
```

The standard control pattern must be independently established.

At minimum verify the known records:

```text
normal:
.. .. 03

jump:
.. .. 83

color change / stop:
.. .. C3

end:
00 00 F3
```

Do not blindly assume every machine-specific interpretation is identical.

Document what AtlasEmbroidery supports.

---

# 9. IMPORTANT CONTROL-BIT DISTINCTION

Do not confuse:

```text
DST Jump
DST Stop/Color Change
DST End
```

with application-level concepts such as:

```text
Trim
ColorChange
Stop
Running
```

DST does not necessarily have a dedicated native trim command.

If the application model contains:

```text
Trim
```

determine exactly how AtlasEmbroidery maps it into DST.

If trim is synthesized through jump behavior, document that explicitly.

Do not claim native DST trim support if the protocol does not provide a distinct trim command.

---

# 10. JUMP + STOP COMBINATIONS

Investigate whether the format permits simultaneous jump and stop bits.

Add explicit tests for:

```text
jump
stop
jump + stop
end
```

The protocol references indicate that jump and stop bits can coexist in certain records.

Determine how AtlasEmbroidery represents such combinations.

If the domain model cannot represent them:

* identify the information loss;
* decide whether the model should be extended;
* document the limitation.

Do not silently discard the second command.

---

# 11. MAXIMUM DELTA — RESOLVE 121 VS 127

This is a mandatory blocker.

The current repository reportedly contains conflicting values resembling:

```text
127
```

and:

```text
121
```

Resolve this from protocol evidence.

The five available signed weights are:

```text
1 + 3 + 9 + 27 + 81 = 121
```

Therefore the coordinate representation itself cannot encode `127` as a single-axis balanced ternary delta using those five weights.

Verify the actual source code.

Remove misleading `127` limits if they refer to DST coordinate movement.

Do not simply replace a number.

Trace every usage.

The final code must have one authoritative constant for the maximum DST axis delta.

Use a clearly named constant such as:

```text
MaxAxisDeltaUnits = 121
```

or the project's equivalent.

Then define:

```text
DST unit = 0.1 mm
```

in one authoritative place.

---

# 12. UNIT CONVERSION

Audit the domain coordinate units.

Determine whether AtlasEmbroidery internally uses:

```text
microns
millimeters
decimal millimeters
other
```

Then explicitly convert:

```text
DST units
↔
domain units
```

If domain units are microns:

```text
1 DST unit = 100 µm
121 DST units = 12,100 µm
```

Do not scatter:

```text
* 100
/ 100
* 0.1
```

throughout the code.

Create named conversion functions/constants.

Add boundary tests.

---

# 13. LONG-MOVEMENT SPLITTING

A movement greater than:

```text
±121 DST units
```

must be split.

Test:

```text
122
123
200
242
243
500
12100
```

in both signs.

The splitter must produce a sequence whose accumulated semantic displacement equals the original movement.

For example:

```text
EncodeMovement(242, 0)
```

must produce valid records whose accumulated X movement is exactly:

```text
242
```

and no individual record may exceed:

```text
121
```

units.

---

# 14. SPLITTING SEMANTICS

Determine whether splitting a long movement is allowed for:

```text
normal stitch
jump
color change
trim
```

Do not blindly split all commands identically.

For a jump spanning a long distance, intermediate jump records may be valid.

For a color change, the stop must occur at the correct semantic point.

For END, there must be exactly one valid terminal representation unless the protocol evidence demonstrates otherwise.

---

# 15. END RECORD

Make END explicit.

A valid END record is:

```text
00 00 F3
```

Test:

```text
encoder always emits exactly one terminal END
decoder recognizes it
validator requires it
trailing garbage is handled according to documented policy
missing END is rejected at the appropriate severity
```

Do not treat a random `F3` as valid END unless the full record semantics are satisfied.

---

# 16. HEADER AUDIT

Audit the 512-byte header.

Verify:

```text
LA:
ST:
CO:
+X:
-X:
+Y:
-Y:
AX:
AY:
```

where supported.

Check:

```text
exactly 512 bytes
ASCII-compatible encoding
proper field termination
padding
numeric widths
negative signs
zero values
maximum values
```

The header must not contradict the encoded stitch stream without a documented reason.

---

# 17. HEADER VS BODY

Create a validator test that deliberately creates mismatches:

```text
ST header != actual record count
CO header != actual color-change count
+X != actual maximum X
-X != actual minimum X
+Y != actual maximum Y
-Y != actual minimum Y
```

Determine whether each mismatch is:

```text
warning
critical
repairable
ignored
```

Document the policy.

Do not let the validator silently trust the header over the actual record stream.

The body is the authoritative machine movement sequence.

---

# 18. COLOR SEMANTICS

Confirm that plain DST stores color-change positions/order rather than RGB/thread identity.

Do not claim RGB preservation.

If AtlasEmbroidery creates a synthetic palette:

```text
synthetic
reconstructed
default
```

must be clearly distinguished from source-preserved metadata.

Test:

```text
no color changes
one color change
multiple color changes
consecutive color changes
```

---

# 19. READER SEMANTIC PRESERVATION

Inspect the current reader.

Determine whether it converts all imported records into:

```text
Running
```

or otherwise loses:

```text
Jump
Stop/ColorChange
End
```

This must be corrected.

A semantic reader should preserve every command representable by the domain model.

If the domain model cannot represent something, explicitly address the architectural gap.

---

# 20. SEMANTIC ROUND-TRIP

Create a semantic comparison independent of raw bytes.

Test:

```text
external/reference DST
    ↓
reader
    ↓
StitchPlan
    ↓
writer
    ↓
DST
    ↓
reader
    ↓
StitchPlan
```

Compare:

```text
relative movement
absolute movement
command type
jump boundaries
color-change boundaries
stop semantics
stitch count
bounds
design dimensions
```

Do not require identical bytes unless the specific test is testing deterministic serialization.

---

# 21. BINARY DETERMINISM

Separately test:

```text
same StitchPlan
→ Write
→ bytes A

same StitchPlan
→ Write
→ bytes B
```

Require:

```text
A == B
```

or equal SHA-256.

This proves deterministic serialization.

It does not prove interoperability.

Keep these concepts separate.

---

# 22. INDEPENDENT REFERENCE IMPLEMENTATION

Use at least one independent implementation as a reference.

Candidates include:

* pyembroidery;
* libembroidery;
* another independently implemented DST reader/writer.

Do not copy its implementation blindly.

Use it as a cross-check.

For representative movements:

```text
-121..121
mixed X/Y
jump
stop
end
```

compare:

```text
AtlasEmbroidery bytes
reference bytes
```

and:

```text
reference bytes
→ AtlasEmbroidery reader
```

This breaks the circular-test problem.

---

# 23. EXTERNAL FIXTURE CORPUS

If legally usable fixtures are available, establish a corpus.

Minimum recommended:

```text
5 real DST files
```

Prefer variation:

```text
simple
dense
multi-color
many jumps
large geometry
```

For each fixture record:

```text
filename/id
source
license/status
SHA-256
file size
header ST
actual record count
CO
bounds
jump count
stop/color-change count
```

If files cannot be committed:

* keep them outside Git;
* provide a manifest;
* hash them;
* document exactly where the local test harness expects them.

Never claim the external corpus passed if it was unavailable.

---

# 24. GOLDEN FILE POLICY

Existing project-generated golden files may remain useful.

But classify them correctly.

They prove:

```text
internal regression
deterministic project behavior
```

They do NOT by themselves prove:

```text
external interoperability
```

Update documentation to make this distinction explicit.

---

# 25. MALFORMED INPUT

Retain the existing corrected tests:

```text
header-only missing END
corrupted mid-stream balanced ternary
```

Then expand.

Test:

```text
empty
<512-byte header
512-byte header with no body
partial record
invalid low bits
invalid command
missing END
multiple END
garbage after END
invalid ASCII header
invalid numeric header
overflow header
huge stitch count
```

Every failure must produce controlled behavior.

---

# 26. VALIDATOR EXCEPTION SAFETY

Audit every:

```text
catch (Exception)
```

in DST parsing/validation.

Expected malformed input may become a validation result.

Unexpected programmer defects must remain visible.

Do not transform:

```text
NullReferenceException
IndexOutOfRangeException
InvalidOperationException
```

into a generic:

```text
"invalid DST"
```

unless there is a demonstrated architectural reason.

---

# 27. STRICT VALIDATION

If the current validator only inspects the first 1000 records, resolve this.

Default authoritative validation must inspect the entire body.

If a fast mode is needed, make it explicit.

For example:

```text
Fast
Strict
```

Do not silently validate a prefix while presenting the result as authoritative.

---

# 28. PROPERTY TESTS

Add bounded properties:

```text
Decode(Encode(delta)) == delta
```

for every valid axis delta.

And:

```text
Sum(Split(delta)) == delta
```

for long movements.

And:

```text
Encode(model) is deterministic
```

And:

```text
Decode(valid bytes) never reads beyond input
```

And:

```text
Normalize(Normalize(model)) == Normalize(model)
```

if normalization exists and is intended to be idempotent.

---

# 29. NO FAKE FIXES

Do not fix failures by:

```text
changing expected values without protocol evidence
removing assertions
skipping tests
loosening tolerances
ignoring malformed input
accepting invalid bytes
changing the protocol to fit the implementation
```

Every changed expectation requires a documented reason.

---

# 30. DOCUMENTATION

Create or update the DST technical documentation.

It must explicitly state:

```text
Header:
512 bytes

Record:
3 bytes

Coordinate unit:
0.1 mm

Maximum axis delta:
±121 units / ±12.1 mm

Encoding:
signed powers 1,3,9,27,81

Terminal record:
00 00 F3

Color:
no native RGB/thread identity

Commands:
exactly what AtlasEmbroidery supports

Trim:
exactly how AtlasEmbroidery maps it
```

Also document any deviations or machine-specific behavior.

---

# 31. TEST REPORT

Generate a final test report containing actual values.

Include:

```text
Build
Total tests
Passed
Failed
Skipped
Coverage
External fixtures
Reference implementation comparison
Exhaustive delta test
Long movement test
Malformed input test
```

No estimated numbers.

---

# 32. ACCEPTANCE GATES

DST cannot be marked COMPLETE until all applicable gates pass.

## Gate A — Build

```text
dotnet build --configuration Release
```

PASS.

## Gate B — Regression

All existing tests pass.

## Gate C — Exhaustive Axis Encoding

All:

```text
-121..121
```

pass exact round-trip.

## Gate D — Byte-Level Vectors

Independent expected bytes pass.

## Gate E — Command Encoding

Normal/jump/stop/end verified.

## Gate F — Long Movement

Values above 121 split correctly.

## Gate G — Header

512-byte structure and metadata validated.

## Gate H — Semantic Round-Trip

Supported semantics survive read/write/read.

## Gate I — Independent Reference

Cross-check against an independent implementation.

## Gate J — External Corpus

Real DST fixtures pass where available.

## Gate K — Malformed Input

Corrupt files are safely rejected.

## Gate L — Documentation

Documentation matches implementation and evidence.

## Gate M — Git

Clean, focused diff.

---

# 33. PHASE STATUS

Do not mark Phase 2 CLOSED if any of these remain:

```text
protocol ambiguity
incorrect byte mapping
unit mismatch
121/127 inconsistency
unsupported command silently discarded
missing END handling defect
semantic round-trip failure
external interoperability defect
known malformed-input bug
```

The status must accurately reflect evidence.

---

# 34. FINAL SOURCE REVIEW

After tests pass, reread the complete changed implementation.

Search for:

```text
TODO
FIXME
placeholder
temporary
hack
magic number
127
121
100
0.1
1000
catch (Exception)
NotImplementedException
```

Every occurrence must be intentionally justified.

---

# 35. FINAL GIT VERIFICATION

Run:

```text
git status
git diff
git diff --check
git log --oneline -10
```

Then:

```text
dotnet build --configuration Release
dotnet test --configuration Release --no-restore
```

Inspect the final diff.

Remove unrelated files.

Do not commit debug artifacts.

---

# 36. COMMIT

If the implementation and evidence are genuinely correct, create a focused commit.

Suggested message:

```text
fix(dst): verify and correct Tajima ternary encoding
```

If the change is broader, use a truthful message.

Push to the current branch.

Do not force-push.

---

# 37. FINAL REPORT — SPANISH

Report in Spanish.

Use this structure:

```text
# AtlasEmbroidery — DST Forensic Audit Final

## Repository
Branch:
Base SHA:
Final SHA:

## Result
DST Status:

## Protocol
Coordinate unit:
Maximum delta:
Encoding:
Command encoding:
END:

## Corrections
...

## Tests
Build:
Tests:
Passed:
Failed:
Skipped:
Coverage:

## Exhaustive Encoding
Range:
Result:

## Independent Reference
Reference:
Result:

## External Corpus
Available:
Tested:
Result:

## Semantic Round-Trip
Result:

## Malformed Input
Result:

## Documentation
...

## Git
Commit:
Push:
Working tree:

## Remaining Limitations
...

## Final Verdict
```

The final verdict must be evidence-based.

Do not say:

```text
"production ready"
```

unless the evidence genuinely supports that claim.

---

# 38. MOST IMPORTANT RULE

The objective is not:

```text
300 tests → 301 tests
```

The objective is:

```text
prove that AtlasEmbroidery writes and reads real DST correctly.
```

The current `300/300` result is the starting point.

Now prove the bytes.

Prove the mathematics.

Prove the limits.

Prove the commands.

Prove the long-movement splitting.

Prove semantic preservation.

Prove interoperability.

Then — and only then — close DST.

Proceed autonomously.
