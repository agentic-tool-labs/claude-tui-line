# SPEC-101 — `--calibrate`: measure the real chrome reserve on this machine

Status: **OPEN for implementation.** Design closed. E1 and E4 are **CLOSED —
both passed live** (see §13.1). §13.8 is applied. **§13.9 is the one outstanding
change** — a defect in §13.4's baseline rule, found in implementation, affecting
the first-run path for every user. §13.4's product question has been **answered
by Jim — option (b)** — and is now binding design, not an open choice.

**Amendment 1** — §10's product question was raised with Jim and answered: he
wants manual `--calibrate` **plus** a prompt on first run on a machine **plus** a
prompt when the Claude Code version changes. That scope is designed in **§12**,
which also **replaces §5.2** and revises §10. §§1–11 below are otherwise
unchanged from the original. The amendment additionally overturns a fact §12.1
would have got wrong: Claude Code *does* send its version.

**Amendment 2** — live verification with Jim closed E1 and E4 and turned up two
things neither the test suite nor the design review could have found. §13
revises §5.3/§6/§8 (verify-phase wording) and **replaces the record shape and
rules 5–8 of §12.3** (per-version keying, then major.minor nudging). §13.8 adds
the version-provenance rule and §13.9 corrects the baseline rule — both gaps that
only implementation exposed.

Follows SPEC-98 (`SPEC-98-stacked-fill-pane-overflow.md`), which shipped
`DefaultChromeReserve = 4`. This spec does **not** modify SPEC-98, its constant,
or anything it shipped. It is purely additive.

---

## 1. Goal

`DefaultChromeReserve = 4` is a measured constant, but it was measured **once**,
on **one machine**, against **one Claude Code version**, at **one terminal
width**. SPEC-98 §2.3 records the consequence explicitly: the truncation
boundary lives outside this process, so **no automated test in this repo can
ever detect that the constant has rotted.** A future Claude Code release that
changes its statusline padding by one column silently reintroduces the exact
`…` truncation bug SPEC-98 fixed, and the suite stays green.

`--calibrate` is the answer to that: a human-in-the-loop procedure that measures
the real budget on the user's own machine and writes the result to
`layout.chromeReserve` in their config.

Non-goal: changing `DefaultChromeReserve`. It remains the fallback for everyone
who never calibrates.

---

## 2. Why this cannot be a self-contained command

This is the single most important constraint in the spec, and the one most
likely to be designed away by accident.

**The process cannot observe its own rendered output.** SPEC-98's E2 was
withdrawn for exactly this reason and the Implementor was right to refuse it.
Claude Code reads our stdout, applies its own indent and its own truncation, and
draws the result. Nothing we can run — not from the hook, not from a terminal —
can read back what was drawn.

Two corollaries that constrain every part of the design below:

1. **The measurement must happen on the hook path**, because that is the only
   code path whose output Claude Code renders. A `--calibrate` invocation typed
   into a terminal is *not* rendered by Claude Code at all; it is ordinary
   stdout. It therefore cannot measure anything by itself.
2. **A human must report the observation.** There is no way around this. The
   command's job is to make that report as small, as unambiguous, and as
   hard-to-get-wrong as possible — a single digit.

This forces a **stateful, multi-invocation protocol**: the terminal command and
the hook are different processes at different times, and they communicate
through a file on disk.

---

## 3. The measurement method

### 3.1 The ruler

In ruler phase, the hook emits a row of **exactly `COLUMNS` characters**, where
the character at 1-based emitted column `i` is the ASCII digit `(i mod 10)`.

At `COLUMNS = 90` that row is `1234567890123…0` — column 85 holds `5`, column 90
holds `0`.

Because the emitted width is `COLUMNS` (i.e. a chrome reserve of zero), the row
is guaranteed to overflow Claude Code's budget whenever the true reserve is
greater than zero. The user sees the row truncated, with the ellipsis at the
boundary.

The user reports **the last digit they can read** — the digit immediately before
the ellipsis. Call its emitted column `P`.

### 3.2 The arithmetic, and why it is indent-free

```
chromeReserve = COLUMNS − P − 1
```

Check against SPEC-98's measured case: `COLUMNS = 90`, ellipsis at emitted
column 86, so the last readable digit is `5` at `P = 85`, giving
`90 − 85 − 1 = 4`. ✓ Agrees with the value SPEC-98 shipped.

**Note what does not appear in that formula: Claude Code's left indent.**

SPEC-98's most dangerous error was a formula that double-counted the indent and
derived a reserve *smaller* than the one already shipped; it was caught only
because the Implementor refused to ship a constant that contradicted its own
spec's reasoning. The ruler is immune to that class of error **by
construction**: the digits are characters *we* emitted, so `P` is measured in
*our own emitted columns*. The indent shifts the ruler rightward on screen but
does not renumber it. Both halves of the chrome — the left indent and the right
margin — are captured in the single quantity `COLUMNS − P − 1`, and the indent
never has to be known, named, or counted.

Implementors and reviewers: if you find yourself adding or subtracting an indent
term anywhere in this calculation, you have reintroduced SPEC-98's bug. There is
no indent term.

### 3.3 Resolving the digit to a column

A digit is only `P mod 10`, so it must be resolved against a window. Constrain
the reserve to `0 ≤ chromeReserve ≤ 9`, which puts `P` in
`[COLUMNS − 10, COLUMNS − 1]` — exactly ten consecutive columns, whose digits
are a permutation of `0`–`9`. **The digit is therefore unique within the window,
with no ambiguity.**

```
P = COLUMNS − 1 − ((COLUMNS − 1 − d) mod 10)
```

where `d` is the reported digit. Worked example: `COLUMNS = 90`, `d = 5` →
`(90 − 1 − 5) = 84`, `84 mod 10 = 4`, `P = 89 − 4 = 85`. ✓

The `0 ≤ reserve ≤ 9` assumption is the one place this method can silently
produce a wrong answer (a true reserve of 14 would be reported as 4). §3.4 is
what catches that.

### 3.4 The verify phase — non-optional

After computing a candidate `R`, the hook emits **two** probe rows:

| row | emitted width | content | expected on screen |
| --- | --- | --- | --- |
| A | `COLUMNS − R` | `A` + `─`×(W−2) + `A` | ends in `A`, **no** ellipsis |
| B | `COLUMNS − R + 1` | `B` + `─`×(W−2) + `B` | ends in **ellipsis** |

This reproduces E6's two-sided bracket — the experiment that actually closed
SPEC-98 — automatically: one width that fits and one that does not, differing by
a single column, pinning `R` from both sides.

**The two rows being different widths is the measurement, not an artefact.**
That fact is obvious here and was *not* obvious on screen; §13.2 is the
consequence and specifies the wording that must accompany this phase.

**Verify is not a nicety and must not be made skippable.** It is the only check
on two assumptions that the ruler phase takes on faith:

- the `0 ≤ reserve ≤ 9` window of §3.3;
- SPEC-98's measured finding that the ellipsis **replaces** the boundary cell
  rather than being appended past it. If a future Claude Code appends instead,
  §3.2's formula is off by one and verify is what reveals it.

### 3.5 Two rules every probe row must obey

- **Never end a probe row in whitespace.** SPEC-98 established that Claude Code
  trims trailing whitespace *before* deciding overflow — that is precisely why
  the `content`-sized pane hid the bug while the `fill` pane exposed it. A probe
  row ending in a space would measure the wrong thing. Every row above ends in a
  non-space glyph.
- **Probe rows are plain ASCII, unstyled: no ANSI, no Spectre markup.** Widths
  must be exact and digits must be legible. (The `─` in the verify rows is the
  one non-ASCII glyph; see E1.)

### 3.6 Calibration bypasses the layout pipeline

The probe rows are written **raw to stdout**, one per line. They do not go
through `SurfaceLayout`, `RowLayout`, `PaneTreeRenderer`, `PaneBorderRenderer`,
or `Compositor`.

Rationale: a calibrator that runs through the code it is calibrating cannot
detect that code being wrong. In particular it must not subtract a chrome
reserve — the ruler's whole premise is that it is emitted at full `COLUMNS`.

---

## 4. Files to touch

| file | change |
| --- | --- |
| `src/ClaudeTuiLine/Program.cs` | new `--calibrate` mode + sub-flags in `RunCli`'s switch (lines 489–557) and mode dispatch (lines 613–654); probe branch on the hook path in `RunAsync` (§5.1); nudge append (§12.4) |
| `src/ClaudeTuiLine/CalibrateCommand.cs` | **new.** All calibration logic: state I/O, ruler/probe row generation, digit→reserve arithmetic, phase transitions, config write |
| `src/ClaudeTuiLine/CalibrationRecord.cs` | **new.** §12.2 durable record I/O and trigger evaluation — shape and rules revised by §13.3, §13.4, §13.9 |
| `src/ClaudeTuiLine/StatusInput.cs` | add `Version` property (§12.1) |
| `src/ClaudeTuiLineShared/ConfigPath.cs` | add `ResolveCalibrationStatePath()` and `ResolveCalibrationRecordPath()` (§6.1, §12.2) |
| `src/ClaudeTuiLineShared/` (writer location) | see §6.3 — conditional |
| `src/ClaudeTuiLine/Config.cs` | add `layout.calibrationPrompt` (§12.7) |
| test project containing `ConfigTests.cs` | **new** `CalibrateTests.cs` (§8) and `CalibrationPromptTests.cs` (§12.9, §13.5) |
| `SPEC.md`, `README.md` | document the `--calibrate` flow and the prompt |
| `src/ClaudeTuiLine/SchemaCommand.cs` | mention `--calibrate` in the `chromeReserve` description (§7) |

**Must not change:** `DefaultChromeReserve` (still `4`); `SurfaceLayout.cs`;
anything in the layout, sizing, or rendering path; the hook's rendered rows when
neither calibration nor a prompt is active (§12.5).

---

## 5. Hook-path integration

### 5.1 Where the probe branch goes

Insert **immediately after the stdin read** in `RunAsync` — after the
`try { rawInput = await Console.In.ReadToEndAsync(); } catch { rawInput = null; }`
block at Program.cs lines 20–27, and **before** `ParseInput` at line 29.

Three reasons this exact position, all load-bearing:

- **After the stdin read**, so Claude Code's pipe is always drained. Exiting
  without consuming stdin risks EPIPE in the parent.
- **Before `ConfigResolution.LoadRenderConfig`** (line 35), so the ruler renders
  even when the user's config is unreadable. Someone with a broken config is
  exactly the person who may be trying to diagnose widths. (Writing the result
  into an unreadable config is separately refused — §6.4.)
- **Before the git probe, Engram telemetry, and item building**, none of which
  calibration needs. The branch returns, so none of that work is done.

The branch reads `COLUMNS` from the environment itself. This does **not** violate
SPEC-V2-FRAMEWORK §2.5 ("COLUMNS read exactly once, at the surface-sizing
root"), because the calibration branch *returns* and never reaches the
surface-sizing root — COLUMNS is still read exactly once per process. State this
in a comment at the branch; a reviewer checking that invariant will otherwise
flag it.

If `COLUMNS` is unset or unparseable, the branch emits a single row reading
`claude-tui-line: calibration needs COLUMNS` and returns 0.

> **§13.8 note.** The branch needs the payload's `version` in order to record
> `observedVersion`, so it takes `rawInput` and extracts the version itself
> (malformed or absent → `null`, never guessed). The three ordering constraints
> above are unaffected: stdin is already drained, and the branch still precedes
> `LoadRenderConfig`.

### 5.2 Cost and output when calibration is not running

> **Amendment 1: this section is superseded by §12.5.** It survives here only so
> the original single-invariant statement is legible. Implement §12.5.

One `File.Exists` on the state path. The hook runs on every statusline render,
so this must stay at one stat call — do not read, parse, or deserialize anything
when the file is absent. When absent, output must be **byte-identical** to
today's; the existing golden tests are the guard.

### 5.3 Rows emitted, per phase

Both phases lead with a label row so a forgotten calibration is
self-explaining on screen rather than mysterious:

```
ctl calibrate: report last readable digit
```

Keep the label **≤ 60 characters** so it does not itself truncate. **Omit the
label row entirely when `COLUMNS < 70`**, so a narrow pane still gets an
uncorrupted ruler.

- **ruler phase:** label row, then the §3.1 ruler.
- **verify phase:** label row (`ctl calibrate: does row A end in "A", row B in "…"?`),
  then row A, then row B, per §3.4.

> **Amendment 2:** the verify label above is correct and stays — it already asks
> about the row *endings* rather than their lengths. The defect was in the
> **CLI-side instruction text** that accompanies it; see §13.2.

Both phases are multi-row throughout. This is deliberate: multi-row is the case
that actually bit in SPEC-98 and the case the uniform reserve was chosen for. See
§9 E3 for the single-row question this leaves open.

### 5.4 The hook writes what only it knows back into the state

In ruler phase the hook records the `COLUMNS` value it actually saw into the
state file.

This is necessary, not incidental: the reserve arithmetic needs `COLUMNS`, and
the `--calibrate --saw` invocation runs in a *different* terminal process whose
own `COLUMNS` may differ from the one Claude Code passed the hook. Deriving
`COLUMNS` from the calibrating terminal instead would be a silent
wrong-answer bug. Take it from the state file, never from the CLI's environment.

**This rule generalises, and it is the most-violated rule in this spec's
history — three instances so far:** *any* fact known only to the hook must reach
the CLI through a file, never through the CLI's own environment **and never by
inference from some other file**. The hook and the CLI are different processes in
different contexts, and every quantity that differs between them is a
silent-wrong-answer bug waiting to happen.

The three instances, so the pattern is recognisable rather than re-derived:

1. `COLUMNS` — this section.
2. The Claude Code version, for `--dismiss` — §12.6.
3. The Claude Code version, for `--confirm` — §13.8. Found during
   implementation, when an inference from the durable record was substituted for
   the hook's own report of it. Inference is a *third* variant of the same
   mistake, not an exception to the rule.

### 5.5 `observedVersion`

Alongside `observedColumns`, the hook records the payload's `version` into the
state file as `observedVersion`, on **every probe render — ruler and verify
both**. Full specification, including the null case, is §13.8.

---

## 6. CLI surface

All sub-flags below are valid **only** together with `--calibrate`; using one
without it is a usage error via the existing `WriteUsageError`. `--calibrate`
participates in the existing `modeCount` mutual exclusion alongside `--check`,
`--preview`, etc. It accepts the existing `--json` modifier for machine-readable
output.

| invocation | precondition | effect |
| --- | --- | --- |
| `--calibrate` | — | write state `phase=ruler`; print instructions. Restarts/overwrites any calibration in progress |
| `--calibrate --saw <0-9>` | `phase=ruler` **and** state has `observedColumns` | compute `R` (§3.2–3.3); write state `phase=verify, candidate=R`; print §13.2's instructions |
| `--calibrate --no-ellipsis` | `phase=ruler` | the ruler was not truncated at all → report reserve `0`, **write nothing**, clear state. §6.5 |
| `--calibrate --set <0-9>` | any | manual escape hatch: set `candidate` and go to `phase=verify` without a ruler reading |
| `--calibrate --confirm` | `phase=verify` | write `layout.chromeReserve = candidate` to config; set `calibratedVersion` from **`state.observedVersion`** (§13.8); clear state; print `old → new` |
| `--calibrate --reject` | `phase=verify` | verify failed: print §6.6 diagnosis; clear state; **write nothing** |
| `--calibrate --cancel` | any | clear state; write nothing |
| `--calibrate --status` | any | print current phase/candidate, or "no calibration in progress" |
| `--calibrate --dismiss` | any | §12.6 / §13.3 — suppress the prompt for every version currently recorded |

Between every step the user must **cause a statusline redraw** in a live Claude
Code session — the statusline updates on activity, not on file change. Every
instruction message must say so explicitly; this is the step users will get
stuck on.

### 6.1 State file

Path: the directory of `ConfigPath.ResolveConfigPath()` (the env-aware,
no-override form), filename `claude-tui-line.calibration.json`.

```json
{
  "phase": "ruler",
  "expiresAt": "2026-08-20T18:31:00Z",
  "observedColumns": 90,
  "observedVersion": "2.1.238",
  "candidate": 4
}
```

`observedColumns` is absent until the hook has rendered a ruler at least once;
`observedVersion` is absent until the hook has rendered any probe at least once
**and** the payload carried a version (§13.8); `candidate` is absent until
`phase=verify`.

### 6.2 `--calibrate` rejects `--config`

Deliberate, and it removes a whole class of bug rather than handling it.

The hook is invoked with **no arguments** (`args.Length == 0` → `RunAsync`), so
it can never know about a `--config` override and would look for state at the
default location. Allowing `--calibrate --config X` would let the CLI and the
hook disagree about where state lives, producing a calibration that appears to
start and then never shows a ruler — with no error anywhere.

Reject it with a usage error that says why. Both processes always derive the
state path the same way, via §6.1.

### 6.3 Where the atomic writer lives — conditional, decision-free

An atomic temp-file-then-rename writer already exists at
`src/ClaudeTuiLineMcp/ConfigFile.cs:38-39`, but it is in the MCP project, not
reachable from the CLI. Apply this rule mechanically:

- **If `ClaudeTuiLineMcp.csproj` already has a project reference to
  `ClaudeTuiLineShared`:** move `WriteAtomic` into `ClaudeTuiLineShared` and have
  `ClaudeTuiLineMcp/ConfigFile.cs` delegate to it. One implementation, both
  callers.
- **If it does not:** do **not** add a project reference. Implement the writer
  privately in `CalibrateCommand.cs` and file a follow-up ticket noting the
  duplication.

Do not decide this on taste — check the `.csproj` and follow the branch.

All three files this spec writes — config, calibration state, and the §12.2
record — go through that one writer.

### 6.4 The config write

Read-modify-write preserving everything else in the file:

- Parse the existing config as a **`JsonNode`**, not a typed model, and perform
  the merge on that untyped DOM: get-or-create the `layout` object, set
  `chromeReserve` on it, serialize the whole node back. Deserializing to `Config`
  and re-serializing would silently drop every key the typed model does not know.
  (`ConfigTools.SetConfig` at `src/ClaudeTuiLineMcp/ConfigTools.cs:88`
  demonstrates the *principle* — unknown keys survive an untyped DOM — but takes
  an already-`JsonNode`-typed parameter and so does not demonstrate the
  read-modify-write *procedure*. Amendment 2 note: the original wording cited it
  as the pattern to copy, which was imprecise; the bullets here are the
  requirement.)
- Serialize with `WriteIndented = true` and write atomically per §6.3.
- **If the config file exists but does not parse, refuse to write** and tell the
  user to fix it first. Never overwrite a file we could not read — that would
  destroy a config whose only problem was a typo.
- If the config file does not exist, create it containing only
  `{"layout":{"chromeReserve":R}}`.
- Print the old value (or "unset, default 4") and the new one.

No plumbing beyond this is needed: `RunAsync` already reads
`topLevel.ChromeReserve` and passes it to `SurfaceLayout.ComputeWidth`
(Program.cs:74). The new value takes effect on the next redraw.

### 6.5 No ellipsis in ruler phase

A reserve of `0` is a legitimate result, but it is also what a stale state file,
a non-redrawn statusline, or a terminal wider than `COLUMNS` looks like. Report
`0`, explain those alternatives, and **write nothing**. Writing a `0` reserve on
a false negative would truncate every statusline the user has.

### 6.6 Verify failure

Print, do not guess:

- **Row A truncated** → the true reserve is larger than `R`; retry with
  `--calibrate --set <R+1>`.
- **Row B not truncated** → the true reserve is smaller; retry with
  `--calibrate --set <R−1>`.
- **Both wrong / neither row visible** → the ellipsis may no longer replace the
  boundary cell (§3.4), or the statusline did not redraw. Point at SPEC-98 §2.

Never auto-advance the candidate on failure. A failing verify means an
assumption broke, and silently stepping ±1 would paper over exactly the signal
the phase exists to produce.

**Amendment 2 addition.** The reject path must also print the recovery line:

```
If you rejected because the two rows looked like different lengths:
that is expected — rerun with  claude-tui-line --calibrate --set <R>
```

A wrongly-rejected verify is cheap to recover from and the user should be told
how, at the moment they are most likely to have made that mistake. See §13.2.

### 6.7 Expiry

`expiresAt` = 30 minutes from `--calibrate`. An expired state file is ignored by
the hook (which renders normally) and reported as expired by the CLI. This
prevents a forgotten calibration from permanently replacing someone's
statusline with a ruler.

---

## 7. Discoverability

`layout.chromeReserve` existed as a user override long before SPEC-98 and
nobody knew — which is why E6 was cheap once found and expensive to find.
Extend `SchemaCommand.cs`'s `chromeReserve` description to name `--calibrate`
as the way to determine the right value. Keep it free of any hardcoded number,
per the change already made for SPEC-98.

**Amendment 2 raises the stakes on this section.** Under §13.4's option (b) a
patch-release chrome change produces no nudge at all, so discoverability of the
manual command is the *only* remaining path for that case. Treat §7 as
load-bearing rather than a nicety.

---

## 8. Verification

New `CalibrateTests.cs`:

1. **Ruler shape** — for `COLUMNS` ∈ {40, 64, 70, 89, 90, 100, 137}: length is
   exactly `COLUMNS`; character at 1-based `i` is `(i mod 10)`; the row is pure
   ASCII digits; it does not end in whitespace.
2. **Digit → reserve round-trip** — for every `COLUMNS` above × every `R` in
   0..9: generate the ruler, take the digit at column `COLUMNS − R − 1`, feed it
   through §3.3's resolution, assert the recovered reserve equals `R`. This is
   the test that would have caught SPEC-98's formula error.
3. **SPEC-98 anchor** — `COLUMNS = 90`, `d = 5` → reserve `4`, asserted as a
   literal with a comment citing SPEC-98's measurement.
4. **Verify row widths** — row A width `== COLUMNS − R`, row B width
   `== COLUMNS − R + 1`; neither ends in whitespace; widths counted as
   **characters, not bytes** (SPEC-98: `awk '{print length}'` counts bytes and
   silently misreports rows containing box-drawing glyphs).
5. **Label suppression** — `COLUMNS < 70` emits no label row; `≥ 70` does, and
   the label is ≤ 60 characters.
6. **Config write preserves unknown keys** — write a config containing keys the
   typed model does not know, run the write, assert they survive verbatim.
7. **Refuses to write an unparseable config** (§6.4) and **refuses to write on
   `--no-ellipsis`** (§6.5).
8. **Expiry** — an expired state file yields normal statusline output.
9. **Absent state file** — hook output byte-identical to a run with no
   calibration; existing golden tests are the primary guard here.
10. **Phase preconditions** — `--saw` without `phase=ruler`, `--confirm` without
    `phase=verify`, `--saw` before the hook has recorded `observedColumns`, and
    any sub-flag without `--calibrate` all produce usage errors, not writes.
11. **Amendment 2 — verify instruction text** (§13.2). The text printed on entry
    to verify phase contains the literal substring `different lengths on purpose`
    (case-insensitively), and does **not** contain the numeric widths of row A or
    row B. Assert the second half by computing both widths for the test's
    `COLUMNS`/`R` and asserting neither decimal string appears in the output.
    This is a regression guard for a defect that cost a correct measurement.

**What these tests cannot do**, and what the test file's own comment must say:
they verify the *arithmetic and the plumbing*, never that the resulting number
is correct. The boundary being measured is outside this process. That is the
whole reason `--calibrate` exists, and it is why E1 below was blocking.

---

## 9. NEEDS-EVIDENCE

**E1 — CLOSED, PASS.** See §13.1. Retained below as written for the record.

Does Claude Code render a probe row verbatim? Specifically: are pure-ASCII digit
rows passed through without markdown interpretation, whitespace collapsing, or
re-wrapping; and does the `─` (U+2500) in the verify rows render single-width?

*Run:* start a calibration, cause a redraw in a live Claude Code session, look
at the statusline.
*Decides:* ruler renders clean → ship as specced. Digits mangled or `─` renders
wrong-width → the probe glyph set must change (fall back to ASCII `-` for the
verify rows, which is the safe choice if there is any doubt) and §3.1/§3.4 need
amending.

This requires a human looking at a real pane. **Do not attempt to satisfy it
from inside the process** — that is SPEC-98's withdrawn E2 and it is
unrunnable. Route it as a smoke observation with Jim watching.

**E2 (non-blocking, still open).** Is the chrome reserve constant in columns
across terminal widths, or does it scale? SPEC-98 assumed constant (a symmetric
two-column margin either side) and measured at exactly one width.

*Run:* calibrate at two clearly different widths, e.g. `COLUMNS` 90 and 140, and
compare.
*Decides:* same `R` → the constant-column model holds and a scalar
`chromeReserve` is the right shape. Different `R` → a scalar is the wrong shape
and both SPEC-98 and this spec need a follow-up. Worth running once, but not a
blocker for shipping `--calibrate`.

**E3 (out of scope, noted).** SPEC-98 §2.3 left unresolved whether Claude Code
budgets single-row statuslines differently from multi-row ones (H2 vs H3). That
question was made *moot in production* by shipping a uniform reserve, not
answered. This spec calibrates the multi-row case only (§5.3). A single-row
ruler variant would settle H2/H3 cheaply once this machinery exists — but it is
a separate ticket, and adding it here would expand the CLI surface for a
question production no longer depends on.

---

## 10. Decisions made here, and the ones that were not mine

**Made:**

- Human-in-the-loop, not automated — §2 makes automation impossible, not merely
  inconvenient.
- A ruler read once, rather than a binary search over candidate reserves. One
  round trip instead of ~3-4, and every round trip is a context switch for a
  human staring at a pane.
- Writes the **user config**, never `DefaultChromeReserve`. The default remains
  the tested fallback for everyone who never calibrates.
- Verify is mandatory (§3.4) — it is the automated form of the two-sided bracket
  that actually closed SPEC-98, and the only guard on two unstated assumptions.
- `--calibrate` rejects `--config` (§6.2) — eliminates a silent-failure class
  rather than handling it.
- Multi-row probes only (§9 E3).
- **Amendment 2:** the prompt record is keyed by version rather than holding a
  single "last version prompted for" (§13.3), and rule 5 is **not** loosened to
  a major.minor comparison.
- **Amendment 2 / §13.8:** `calibratedVersion` comes from the hook's own report
  in the state file, never from inference over the durable record.
- **Amendment 2 / §13.9:** the nudge baseline is `calibratedVersion` **or
  nothing** — an absent baseline means prompt, never suppress.

**Were not mine — both now answered:**

1. *(Amendment 1)* Whether calibration should ever prompt on its own initiative.
   Originally specced manual-only and flagged for Jim, because it is a
   product-behaviour call: it means the tool putting something on the user's
   statusline that the user did not ask for. **Jim's answer: do all three** —
   manual `--calibrate`, a prompt on first run on a machine, and a prompt when
   the Claude Code version changes. Designed in §12.
2. *(Amendment 2)* How often a version-change nudge should fire on a machine that
   auto-updates weekly. **Jim's answer: option (b)** — nudge only on a
   `major.minor` change, with the record still keyed by the exact full version.
   Designed in §13.4.

One number remains a tunable rather than a decision: the **prompt window**
(§12.3, currently 7 days). It is a one-line change and governs how long a user
who ignores the prompt keeps seeing it. §13.9 makes it load-bearing for the
first-run case rather than only the version-change case.

---

## 11. Risk for the Implementor

- **The formula has no indent term.** §3.2. If you add one, you have
  reintroduced the bug SPEC-98 nearly shipped.
- **Never end a probe row in whitespace.** §3.5. Claude Code trims before
  measuring, so a whitespace-terminated row measures the wrong quantity —
  this is the exact mechanism that hid the original bug in the `content` pane.
- **Count characters, not bytes**, in every width assertion. §8.4.
- **Never let the CLI source a hook-only fact from anywhere but the state file.**
  §5.4, and its three instances: `COLUMNS` (§5.4), the version for `--dismiss`
  (§12.6), the version for `--confirm` (§13.8). Inference from another file is
  the same bug as reading the CLI's own environment.
- **Do not deserialize the config to a typed model to write it.** §6.4 — it
  drops unknown keys silently.
- **Do not auto-advance the candidate when verify fails.** §6.6 — a failed
  verify is a broken assumption, and stepping ±1 hides the signal.
- The quiet hook path must stay cheap and must not perturb rendered rows. §12.5.
- **Amendment 2:** the record is *tool-owned* state, so an unparseable record is
  treated as absent rather than refused — the exact opposite of §6.4's rule for
  the *user-owned* config. §13.3 explains why the two must differ.
- **Amendment 2:** `MajorMinor` coarsening applies to the **nudge decision only**
  (§13.4). It must never touch rule 5, and it must never be used to order or
  compare versions for anything but equality. §13.4 states why.
- **Amendment 2 / §13.9:** never let a version stand in as its own baseline. A
  baseline must be a version the user actually *reconciled with*, which means
  `calibratedVersion` and nothing else.

---

# 12. Amendment 1 — the auto-prompt

Adds: prompt on first run on a machine; prompt when the Claude Code version
changes. Replaces §5.2.

## 12.1 The version IS available — a grep of `StatusInput` is misleading

`StatusInput` (`src/ClaudeTuiLine/StatusInput.cs:5`) models 13 top-level
properties and **none of them is a version**, so the obvious search says Claude
Code sends no version and this feature is impossible.

**That conclusion is wrong.** A real captured payload in this repo,
`tests/ClaudeTuiLine.Tests/fixtures/real_captured_workspace.json`, contains:

```json
{
  "cwd": "…",
  "workspace": { "current_dir": "…", "project_dir": "…", "added_dirs": [], "repo": {…} },
  "version": "2.1.233"
}
```

Claude Code sends a **top-level `version`**. This tool has simply never modelled
it — exactly as it has never modelled `workspace.current_dir`,
`workspace.project_dir`, or `workspace.added_dirs`, all also present in that
capture and all also discarded.

The absence of a field from `StatusInput` is evidence about *this tool*, not
about Claude Code. Do not treat the model as a description of the payload.

(Amendment 2: confirmed live — E4 passed, §13.1.)

**Change required:** add to `StatusInput`

```csharp
[JsonPropertyName("version")] public string? Version { get; set; }
```

Nullable, and every consumer must treat `null` as **"version unknown"**, never as
a version value. `null` must never compare equal to a recorded version, and must
never be written into the record as if it were one.

## 12.2 The durable record — a second file, deliberately not merged

Two files, two lifetimes:

| file | lifetime | purpose |
| --- | --- | --- |
| `claude-tui-line.calibration.json` (§6.1) | transient, 30-min expiry | a calibration procedure in progress |
| `claude-tui-line.calibration-record.json` | **durable, no expiry** | what we have calibrated, prompted for, and been dismissed on |

**Do not merge them.** They fail differently: a stale or corrupt transient file
should cost you nothing but a re-run, whereas losing the record resurrects the
first-run prompt and forgets a dismissal. Merging means one file's failure mode
becomes the other's.

Record lives **beside the config**, per §6.1's directory — **not** in
`ItemCache.ResolveCacheDir()`. The cache directory is disposable by design
(`~/.cache/claude-tui-line/items`, or `$TMPDIR` when `HOME` is unset); clearing
a cache must not re-nag the user or lose a dismissal. Durable state belongs with
the config.

> **Amendment 2 replaces the record shape below.** The original single-slot
> shape is retained here only so §13.3's migration is legible. **Implement
> §13.3's shape**, not this one.

```json
{
  "calibratedVersion": "2.1.233",
  "calibratedReserve": 4,
  "promptedForVersion": "2.1.240",
  "promptFirstSeen": "2026-08-20T18:00:00Z",
  "dismissedVersion": "2.1.240"
}
```

Every field is optional. An **absent file means "first run on this machine"** —
that is the entire first-run detection mechanism, and it needs no separate
marker. (That remains true under §13.3.)

## 12.3 When the prompt shows

Evaluate in this order; the first rule that fires decides.

> **Amendment 2 replaces rules 5–8 and the write-frequency paragraph.** Rules
> 1–4 are unchanged. Implement §13.3's table as revised by §13.4 and §13.9.

1. `layout.calibrationPrompt == false`, or `CLAUDE_TUI_LINE_NO_NUDGE` is set →
   **no prompt**, and **no record file is read or written** (§12.7).
2. A calibration is in progress (§6.1 state present and unexpired) → **no
   prompt.** The user is already doing the thing; nagging them to start it is
   absurd.
3. Record absent → **prompt** (first run).
4. `version` is `null` → **no prompt** beyond rule 3. Unknown version cannot
   establish a change. Never guess, and never treat `null` as a new version.
5. `version == calibratedVersion` → **no prompt.** Already calibrated here.
6. `version == dismissedVersion` → **no prompt.** Declined for this version.
7. `promptFirstSeen` is more than the **prompt window** (7 days) ago → **no
   prompt.** Ignoring it for a week is a decline.
8. Otherwise → **prompt.**

Rule 7 is the safety net that makes this bounded rather than perpetual: even a
user who never dismisses and never calibrates stops seeing it. The 7-day figure
is the tunable §10 flags for Jim.

**Record writes are rare, not per-render.** The hook writes the record only when
`promptedForVersion != version` — i.e. on the first render after a version
change, or the first render ever. Every subsequent render reads and writes
nothing new. Two panes redrawing simultaneously can race that write; the atomic
writer of §6.3 makes the loser harmless, since both write the same content.

> **That last sentence is false in practice and §13.3 is why.** Two panes on
> *different* Claude Code versions do not write the same content, so the race is
> not harmless — it is perpetual. This was found live, not in review.

## 12.4 How the prompt renders: one appended row

**The hook has exactly one output channel.** SPEC-98 §9.2.1 states it directly —
"the render path's only output channel is this one row". There is no
notification side-channel, no stderr the user sees, no toast. So a prompt is
necessarily *some* intrusion into the statusline, and the only question is how
much.

- **Replacing** the statusline (what the ruler phase does) is right for a
  procedure the user explicitly started, and wrong for an unsolicited nudge.
- **Appending one row below** the rendered statusline is the least destructive
  option: the user's configured surface renders intact and *unshifted*, and the
  block simply grows by one line.

**Decision: append exactly one row, after the normal render.** Rejected
alternatives, with reasons, so they are not re-litigated: prepending (shifts the
user's whole layout up by a row); replacing (hostile for something unrequested);
stderr (unverifiable, and likely surfaced as an error).

The nudge row:

- is **plain unstyled ASCII**, no ANSI and no Spectre markup. Since it is
  appended after the pipeline rather than through it, hand-written escapes would
  be measured by nothing — and that is where width bugs come from.
- is **≤ 48 characters**, suggested text
  `calibrate widths: claude-tui-line --calibrate`.
- is **suppressed entirely when the surface width is < 50** rather than
  truncated.

That length cap is not cosmetic. **The nudge exists because the reserve may be
wrong, so it can be truncated by the very condition it is reporting.** Keeping it
far shorter than any plausible miscalibration is what stops it from failing
exactly when it is needed. A truncated nudge is worse than no nudge.

**The nudge is a persistent footer, not a one-shot.** While the §12.3 rules say
prompt, the row appears on **every** render — only the record *write* is deduped
(§13.3), never the visible row. Rule 7's 7-day window is the sole thing that
bounds it, and dismissal is the sole thing that ends it early. This is
deliberate: a statusline has no notification history, so a row the user happened
not to look at once would otherwise be lost entirely. Do not "fix" this into a
show-once toast. **§13.9 exists because an implementation accidentally did
exactly that**, for the first-run case, and nothing failed.

**Insertion point:** unlike the §5.1 probe branch, this is *late* — after rows
have been produced by the pipeline and immediately before they are written to
stdout. The two are at opposite ends of `RunAsync` and must not be conflated.
They are also mutually exclusive by rule 2 of §12.3.

## 12.5 Revised invariants — replaces §5.2

The original "byte-identical when no state file" is too weak now. It is replaced
by **two** invariants, and the pair is stronger than the one it replaces:

1. **Quiet path.** No calibration state and no prompt → rendered rows are
   **byte-identical to today's**. Existing golden tests are the guard, and they
   must run with the prompt disabled via `CLAUDE_TUI_LINE_NO_NUDGE` (§12.7) so
   they keep testing what they were written to test.
2. **Prompt path.** Prompt active → output equals the quiet-path output **plus
   exactly one appended row**. Every pre-existing row must be byte-identical.

Invariant 2 is the one that matters and it is directly testable: *the nudge may
not perturb any row of the user's statusline.* A prompt that changes an existing
row is a bug by definition, not a judgement call.

**Cost, stated honestly rather than claimed to be free:**

- The §5.1 probe check stays one `File.Exists`, on a path that is normally
  absent.
- The prompt check is gated on the config toggle first, which is already in
  memory — so an opted-out user pays **nothing**.
- For everyone else it is one small JSON read per render. The hook already
  reads and parses the config on every render, so this is the same class of cost,
  not a new one. Do not pretend otherwise in comments.

## 12.6 Dismissal, and the version the CLI cannot know

`--calibrate --dismiss` records that the user declined for the current version.
`--calibrate --confirm` (§6) implicitly dismisses by setting `calibratedVersion`.

**The CLI does not know the Claude Code version.** It arrives on stdin, to the
*hook*, in a different process. A terminal invocation of `--dismiss` has no
payload and no way to obtain it.

Therefore: **`--dismiss` takes the version(s) from the record**, never from its
own context. It must never attempt to discover the version itself — not from an
environment variable, not by shelling out to `claude --version`, not from a PATH
lookup. This is §5.4's rule applied a second time, and it is the same failure
mode: a value that differs between the hook's context and the terminal's is a
silent wrong answer.

*(Amendment 2: the original text said "copies `promptedForVersion` into
`dismissedVersion`". With the record keyed by version there is no single such
slot; §13.3 specifies what `--dismiss` now marks. The rule above — take it from
the record, never from the CLI's context — is unchanged and is the load-bearing
part.)*

**`--dismiss` and `--confirm` differ, and the difference is not an
inconsistency.** `--dismiss` reads the *record* because it is a statement about
prompts already shown, which is exactly what the record holds. `--confirm` reads
the *state file* (§13.8) because it is a statement about a measurement just
taken, which only the hook that rendered the probes observed. Same rule — take
the fact from wherever the hook actually recorded it — applied to two different
facts.

If the record has nothing to dismiss, `--dismiss` says so and writes nothing.

## 12.7 Opt-out

Two mechanisms, different audiences:

- **`layout.calibrationPrompt`** (bool, default `true`) — the user-facing
  setting. Placed under `layout` beside `chromeReserve` so everything
  calibration-related is in the one place a user will look for it, even though
  a prompt is arguably not itself a layout concern.
- **`CLAUDE_TUI_LINE_NO_NUDGE`** — env var, for golden tests, CI, screenshots,
  and demos. Set (to anything non-empty) suppresses the prompt.

Either one short-circuits at rule 1 of §12.3, before any record file is touched.

## 12.8 Blast radius on the release that ships this

Every existing user has no record file, so **rule 3 fires for all of them**:
everyone sees the prompt once, for up to the 7-day window, on upgrade.

**"Once" there means one prompting *episode*, spanning days — not one render.**
§13.9 exists because that was read the other way in implementation.

This is deliberate and correct — a user calibrated against an old Claude Code is
precisely who the feature is for — but it is a user-visible change to every
installation, and it should be a release-note line, not a surprise. It is also
the reason the window length (§12.3 rule 7) is worth Jim's eye.

## 12.9 Verification — new `CalibrationPromptTests.cs`

1. **`Version` round-trips** — deserialize
   `tests/ClaudeTuiLine.Tests/fixtures/real_captured_workspace.json` and assert
   `Version == "2.1.233"`. Use the real capture, not a synthetic payload; the
   whole point is that the real shape carries a field the synthetic one does not.
2. **Trigger table** — one case per rule in **§13.3 as revised by §13.4 and
   §13.9**, each asserting prompt/no-prompt. Include `version == null` explicitly
   (rule 4) and assert `null` never matches a recorded version.
3. **Invariant 1** — no state, prompt disabled → rows byte-identical to the
   existing goldens.
4. **Invariant 2** — prompt active → row count is exactly `quiet + 1`, and rows
   `[0..n-1]` are byte-identical to the quiet render. Assert this **row by row**,
   not on the joined string; a joined comparison can hide a perturbation that
   another row's change compensates for.
5. **Nudge width** — ≤ 48 characters, counted as characters not bytes; suppressed
   below surface width 50; does not end in whitespace (§3.5 applies to it too).
6. **Record write frequency** — superseded by §13.5's stronger multi-version
   form.
7. **`--dismiss`** — see §13.5.
8. **Prompt window** — a `promptFirstSeen` older than the window suppresses the
   prompt (rule 7).
9. **Cache dir independence** — deleting `ItemCache.ResolveCacheDir()` does not
   resurrect the prompt or lose a dismissal (§12.2).
10. **A calibration in progress suppresses the prompt** (rule 2).

## 12.10 NEEDS-EVIDENCE for the auto-prompt

**E4 — CLOSED, PASS.** See §13.1. Retained below as written.

Is `version` present in **every** real payload, or only some? The finding in
§12.1 rests on **one** captured fixture.

*Decides:* present in all → §12.3's rules 4–6 work as written. Present in only
some → rule 4 (`null` = unknown) already handles it safely, but the prompt will
be flaky across shapes and §12.3 needs a note saying so.

Note the asymmetry: the design is **already safe** under every outcome, because
rule 4 refuses to guess. E4 decides whether the feature *works*, not whether it
is *safe*.

**E5 — folded into E1's session; see §13.1.** Does Claude Code cap the number of
statusline rows it renders? The appended nudge row takes a user from N to N+1.

---

# 13. Amendment 2 — what live verification changed

Live E1/E4 verification with Jim, plus the two follow-up checks flagged after
automated review was skipped, closed both blocking evidence items and surfaced
two defects. Neither was findable by the test suite: one needed a human's eyes,
the other needed a machine with sixteen concurrent Claude Code processes on it.
Two further gaps (§13.8, §13.9) surfaced only when the amendment was implemented.

## 13.1 Evidence closed

- **E1 — PASS.** Probe rows render verbatim; the arithmetic reproduced the §3.3
  anchor exactly (digit `5` → reserve `4`) on Jim's real pane. No glyph fallback
  needed; `─` renders single-width. **§3.1 and §3.4 ship as specced.**
- **E4 — PASS, closed.** Top-level `version` is present in the real live payload
  and is read correctly (`2.1.237` at the time of the run). §12.3's rules 4–6
  function; no flakiness note needed.
- **E5 — no row loss observed.** The nudge row appended without anything
  dropping off the bottom. No height guard needed.
- **Point A (write-frequency guard) — the guard is correct in isolation**, proven
  twice: same version × 3 renders → 0 extra writes; one version change → exactly
  1 write. But it does not hold on a real multi-pane machine; §13.3.
- **Point B (version-change simulation) — not needed.** Real transitions were
  observed live (`2.1.233 → 2.1.234 → 2.1.237 → 2.1.238`, a client auto-update in
  progress) and rules 5/7/8 fired as the table predicts. The hand-edited
  simulation is superseded by better evidence.

Jim's config now holds `layout.chromeReserve: 4`, set manually after §13.2's
defect caused a correct reading to be rejected.

## 13.2 Spec-defect: the verify prompt let a correct answer be rejected

**What happened.** The ruler produced the correct candidate, `R = 4`. Row A
rendered clean, row B truncated — the textbook confirm signal. Jim noticed that
the two rows were visibly different lengths (86 and 87 columns, exactly as §3.4
requires), read that as evidence the comparison was invalid, and hit `--reject`.
The result was a functional but wasteful reserve of `5` instead of the correct
`4`.

**Classification: spec-defect, not impl-defect.** The code did precisely what
§3.4 specifies. The specified *wording* is what lost the measurement.

**Root cause, stated generally so it is reusable.** When you ask a human to
report an observation, every *other* salient difference they can see and cannot
explain becomes a competing hypothesis about what they are looking at. The
one-column width difference is the entire measurement, and to anyone who has not
read §3.4 it looks like a botched comparison. An instruction that names the
observable but not the confound leaves the confound to be interpreted, and it
will be interpreted as an error.

**Fix — required text.** On entry to verify phase (`--saw`, `--set`), the CLI
must print, before telling the user to redraw:

```
Two probe rows will appear on your statusline. They are DIFFERENT LENGTHS
ON PURPOSE — row B is exactly one column wider than row A, and that
one-column difference IS the measurement.

Ignore the lengths. Look only at how each row ENDS:

  row A should end in   A    (not truncated)
  row B should end in   …    (truncated)

Both true?   claude-tui-line --calibrate --confirm
Anything else?   claude-tui-line --calibrate --reject
```

Three binding requirements on that text, beyond the wording:

1. It must contain the phrase **"different lengths on purpose"** — §8.11 asserts
   it, so the explanation cannot be quietly dropped in a later edit.
2. It must **not print the numeric widths** of row A or row B. A number the user
   cannot interpret invites interpretation. Widths belong in `--calibrate
   --status` and in `--json` output, where the audience is a debugging user or a
   machine, not in the confirm prompt, where the audience is being asked a
   yes/no question about two glyphs.
3. The instruction to cause a redraw (§6) still applies and still comes after.

**Fix — recovery path.** §6.6 gains the reject-path line quoted there, telling a
user who rejected for this reason how to get back. The wrongly-rejected case is
cheap to recover from and expensive to notice, so the recovery must be printed
at the moment the mistake is most likely.

**Not changed:** the §5.3 verify *label row* already asks about row endings
rather than lengths, and stays as written. The `--reject` semantics stay as
written too — §6.6's rule against auto-advancing the candidate is unaffected, and
this defect is not a reason to weaken it.

## 13.3 Design gap: one version slot, many concurrent versions

**What happened.** On Jim's machine the record file's mtime advanced on every
redraw, and `promptedForVersion` cycled through three values in minutes, while
`calibratedVersion` stayed fixed. Cause: **sixteen concurrent Claude Code
processes mid-auto-update, running genuinely different client versions at the
same time, all sharing one global record file.** Each pane's version differs from
`calibratedVersion`, so rule 8 correctly fires for each; each pane then writes
*its own* version into the single `promptedForVersion` slot; the next pane
overwrites it. Nothing converges. The nudge appears and disappears per pane at
what looks like random.

**Classification: spec gap, not impl-defect.** §12.3 implicitly assumed one
Claude Code version active system-wide. That assumption is false on any machine
with several panes open during an update, which is the normal state of Jim's
machine and not an exotic case.

**Diagnosis.** `promptedForVersion` is a **single-valued field modelling a
multi-valued fact.** There are N concurrently-live versions and one slot for
them. Every symptom — the mtime churn, the flapping nudge, the racing writes —
follows from that one mismatch. Fix the shape and all three go away together.

### The fix: key the record by version

**New record shape.** Replaces §12.2's.

```json
{
  "calibratedVersion": "2.1.237",
  "calibratedReserve": 4,
  "versions": {
    "2.1.238": { "promptFirstSeen": "2026-08-20T18:00:00Z", "dismissed": false },
    "2.1.234": { "promptFirstSeen": "2026-08-20T17:41:00Z", "dismissed": true }
  }
}
```

- `versions` replaces `promptedForVersion`, `promptFirstSeen`, and
  `dismissedVersion`. Each key is a Claude Code version string we have prompted
  for; each value carries when we first prompted for it and whether it was
  dismissed.
- `calibratedVersion` / `calibratedReserve` stay **singular and unchanged.**
  Calibration produces one config value, so one slot is the correct model there.
  Do not key those by version. Where `calibratedVersion`'s value comes from is
  §13.8.
- An **absent file still means "first run on this machine"** — unchanged.

**Revised trigger table.** Rules 1–4 of §12.3 are unchanged. Rules 5–8 become:

5. `version == calibratedVersion` → **no prompt.** Already calibrated here.
   **Exact string comparison — never coarsened.** See §13.4.
6. `versions[version]` exists and `dismissed == true` → **no prompt.**
7. `versions[version]` exists and its `promptFirstSeen` is more than the prompt
   window (7 days) ago → **no prompt.**
8. Otherwise → **prompt**, subject to §13.4's major.minor condition **as
   corrected by §13.9**. Write the record **only if `versions[version]` is
   absent**, creating it with `promptFirstSeen = now, dismissed = false`.

**Why that kills the churn.** A write now happens at most **once per distinct
version ever observed**, regardless of how many panes are open. Sixteen panes
across three versions produce three writes, total, ever — not one per render per
pane. The old guard tried to make the write rare by comparing against a slot that
several writers were fighting over; keying makes it rare by construction, with no
guard to get wrong. §13.4 lowers it further still.

**Concurrency, and what deliberately is not fixed.** Two panes on different
versions can still write simultaneously; the atomic rename of §6.3 means the
whole file is last-writer-wins, so one newly-created key can be lost. The losing
pane simply recreates it on its next render. That is bounded (a handful of extra
writes during an update window), self-healing, and costs at most one duplicate
prompt. **Do not add a lock file, retry loop, or merge-on-write.** The state is
advisory; the failure mode is one extra nudge; locking a file that four processes
touch a few times a week is more risk than the bug.

**Pruning.** Cap `versions` at **10 entries.** On insert, if the map would exceed
10, evict the entry with the oldest `promptFirstSeen`. Without this the map grows
by one key per Claude Code release forever, and on a weekly-release cadence that
is a slow leak in a file read on every render. Ten covers any plausible spread of
concurrent versions with room to spare.

**Unparseable record → treat as absent.** Log nothing, throw nothing, render
normally, and let rule 3 fire. This is **deliberately the opposite of §6.4's rule
for the config**, and the distinction is the point: the config is *user-authored*
and overwriting a file the user typed into destroys their work, whereas the
record is *tool-owned* and its worst-case loss is one extra prompt. Never refuse
to function because tool-owned state got corrupted.

**Migration from the old shape.** Jim's machine already has a live old-shape
record, so this is not hypothetical. On read, if `versions` is absent but
`promptedForVersion` is present:

- `versions[promptedForVersion] = { promptFirstSeen: <the old promptFirstSeen, or
  now if absent>, dismissed: (dismissedVersion == promptedForVersion) }`
- Drop `promptedForVersion`, `promptFirstSeen`, `dismissedVersion`.
- If `dismissedVersion` is present but differs from `promptedForVersion`, add it
  as its own entry with `dismissed: true` and `promptFirstSeen = now`.
- Write the migrated shape once, on the next write the rules call for — do not
  write purely to migrate.

**`--dismiss` under the new shape.** It sets `dismissed = true` on **every entry
currently in `versions`**, and prints how many. Rationale: the CLI cannot know
which version's pane the user was looking at (§12.6 — and with concurrent
versions there may genuinely be several), and a user who typed `--dismiss` wants
the unsolicited nudge to stop, not to play whack-a-mole across panes. Erring
toward more suppression is correct for something the user did not ask for in the
first place. If `versions` is empty, report that there is nothing to dismiss and
write nothing.

### Two alternatives, rejected — recorded so they are not revisited

**Rejected: accept the churn as self-resolving.** It does not resolve. Auto-update
staggers panes continuously rather than converging, and a long-lived pane can
hold an old client version for days. Even if it did resolve, a disk write on every
statusline render is precisely the invisible-defect class the Point-A check was
created to catch — accepting it would be discarding the finding rather than acting
on it.

**Rejected: loosen rule 5 to compare only `major.minor`.** This trades away the
detection the entire spec lineage exists to provide, in order to fix a cost
problem that keying already fixes for free. A statusline chrome change is exactly
the kind of thing a *patch* release can ship — SPEC-98 exists because Claude
Code's chrome moved — so coarsening the comparison means a patch bump that breaks
the reserve is silently treated as "still calibrated". **Never trade the
correctness of the trigger for the cost of the write. Fix the write.**

> Note the boundary carefully, because §13.4 sits right beside it: what is
> rejected here is coarsening **rule 5**, which decides whether a calibration is
> still considered *valid*. §13.4 coarsens only rule 8, which decides whether we
> *bother the user*. Those are different questions with different failure modes.

## 13.4 RESOLVED — nudge on `major.minor` change; the record stays exact

**Jim's answer to the §13.4 question was option (b).** This section is now
binding design rather than an open choice.

**The governing principle: store exact, coarsen at the point of decision.** The
record keeps full version strings; only the nudge decision is coarsened, at the
moment it is made. Nothing lossy is ever written to disk, so if this policy is
revisited later the data to support the alternative is still there.

### `MajorMinor(v)`

- Split `v` on `.`. If there are ≥ 2 components, return
  `components[0] + "." + components[1]`. Otherwise return `v` unchanged.
- Comparison is **ordinal string equality only.** Never parse to integers, never
  order versions, never ask "is this newer". The only question this design ever
  asks is *did it change*. Version-ordering logic is where parsing bugs live and
  none is needed here — do not add any.
- **Do not strip pre-release or build suffixes.** `2.1.238-beta.1` → `2.1`.
  `2.1-rc1` → `2.1-rc1`, which compares unequal to `2.1` and therefore costs one
  extra nudge. That is the correct direction of error: **err toward nudging,
  never toward silently suppressing.** A spurious nudge is visible and
  dismissible; a suppressed one is neither.
- `null` never reaches this function — rule 4 handles it first.

### The nudge baseline — **corrected by §13.9**

> The original text here read: "`calibratedVersion` if present; else the entry in
> `versions` with the newest `promptFirstSeen`; else there is no baseline — but
> rule 3 has already decided, so this case cannot reach rule 8." **All three
> clauses were wrong in the same way** and are replaced by the rule below. §13.9
> explains what broke and why.

**The baseline is `calibratedVersion`, or nothing.** There is no fallback.

A baseline must be a version the user has actually **reconciled with** — one
where they looked at a statusline and accepted the reserve. `calibratedVersion`
is the only field that means that. "The last version we happened to nag about" is
not a commitment and must never stand in for one.

### Revised rule 8

Rules 1–7 of §13.3 are unchanged. Rule 8 becomes:

> 8. If `calibratedVersion` is **absent** → **prompt.** There is no baseline, so
>    "has it changed since we calibrated?" is unanswerable, and unanswerable must
>    mean keep prompting. Rules 6 and 7 are what bound it.
>    Otherwise → **prompt iff `MajorMinor(version) != MajorMinor(calibratedVersion)`.**
>
> In either case, write the record **only if `versions[version]` is absent**
> (creating it keyed by the **exact full version**, never by the coarsened form).

### Consequences, stated rather than left to be discovered

- **Write frequency stays bounded.** The write is gated on `versions[version]`
  being absent, which §13.9's change does not touch. Within one `major.minor`
  series a calibrated user neither prompts nor writes.
- **The nudge row itself still appears on every qualifying render**, because only
  the write is deduped. §12.4's persistent-footer paragraph states this
  explicitly; it is intended, and rule 7's window is what bounds it.
- **Rules 6 and 7 are load-bearing, not redundant.** Under the corrected rule 8
  they are the *only* bound on the first-run prompt, since an uncalibrated user
  has no baseline and rule 8 always says prompt. The earlier note calling them
  "largely redundant, keep them anyway" was itself a symptom of the §13.9 bug —
  they looked redundant only because the broken baseline was silently suppressing
  the case they exist to bound.
- **The accepted loss, stated plainly:** a patch release that *does* move the
  chrome budget produces **no nudge at all** for a calibrated user. The
  mitigations are that manual `--calibrate` is always available and §7 makes it
  discoverable — which is why §7 is now flagged as load-bearing.
- **This is not the thing §13.3 rejected**, and the distinction must survive
  future edits: rule 5 still compares **exact** versions, so nothing is ever
  declared *still calibrated* that is not. What option (b) gives up is only the
  *offer* to recalibrate. **Do not let this reasoning drift into rule 5.** If a
  later change makes rule 5 use `MajorMinor`, that is a regression against
  §13.3's rejected alternative, whatever the commit message says.

## 13.5 Verification — additions and replacements

Replaces §12.9 items 6 and 7; adds the rest. All in
`CalibrationPromptTests.cs` unless noted.

1. **Write frequency, single version** — N consecutive renders at one version →
   exactly one record write.
2. **Write frequency across versions — the regression test for §13.3 and
   §13.4.** Two cases, both asserting on **write count, not file mtime** (a test
   that watches mtime is a test that watches the clock):
   - `calibratedVersion = 2.1.237`; render `2.1.233, 2.1.238, 2.1.233, 2.1.238`
     → **zero** prompts and **zero** writes. Same `major.minor` series.
   - `calibratedVersion = 2.1.237`; render `2.1.237, 2.2.0, 2.1.237, 2.2.0` →
     exactly **one** prompt-write, keyed `2.2.0`.
3. **`MajorMinor` table** — `2.1.238`→`2.1`, `2.1`→`2.1`, `2`→`2`, `dev`→`dev`,
   `2.1.238-beta.1`→`2.1`, `2.1-rc1`→`2.1-rc1`, `""`→`""`. Assert ordinal
   equality semantics explicitly.
4. **§13.9 — the first-run persistence test.** Record **present**,
   `calibratedVersion` **absent**, `versions` already holds an entry for the
   rendering version, inside the window, not dismissed → the nudge **appears on
   the second and third consecutive renders**, not only the first. This is the
   exact case that passed silently before §13.9, and it is the highest-traffic
   path in the feature.
   *(This replaces the former "baseline selection" item, which tested the
   newest-`promptFirstSeen` fallback that §13.9 removes. Delete that test rather
   than adapting it.)*
5. **Keyed shape round-trips** — a record with several `versions` entries
   serializes and deserializes without loss, and an entry for a version not
   present does not resurrect a dismissal for a version that is.
6. **Migration** — an old-shape record with `promptedForVersion` /
   `promptFirstSeen` / `dismissedVersion` reads as the equivalent keyed shape,
   including the case where `dismissedVersion != promptedForVersion` (two entries)
   and the case where `promptFirstSeen` is absent.
7. **Unparseable record → treated as absent** — garbage bytes in the record file
   produce a normal render plus a first-run prompt, and throw nothing. Assert the
   render succeeds; a swallowed exception that also swallows the statusline is the
   failure this guards.
8. **Pruning** — inserting an 11th version evicts the entry with the oldest
   `promptFirstSeen`, and exactly ten remain.
9. **`--dismiss` marks every entry** — a record with three `versions` entries,
   after `--dismiss`, has `dismissed == true` on all three; the next render at any
   of those versions does not prompt; a render at a version in a *new*
   `major.minor` series does prompt. With `versions` empty, `--dismiss` writes
   nothing.
10. **Verify instruction text** — §8.11, in `CalibrateTests.cs`.
11. **§13.8 — `observedVersion` provenance.** Four cases:
    - The hook writes `observedVersion` into the state file on a **ruler** render
      and on a **verify** render, in both cases from the payload.
    - `--confirm` sets `calibratedVersion` to the state file's `observedVersion`,
      **not** to anything derived from the record. Construct the discriminating
      case: a record whose newest `versions` entry is `X` while the state file's
      `observedVersion` is `Y`, and assert `calibratedVersion == Y`. A test that
      does not separate those two values proves nothing.
    - `observedVersion` absent → `--confirm` still writes `chromeReserve` to
      config, and leaves `calibratedVersion` unchanged.
    - A payload with `version: null` leaves `observedVersion` absent rather than
      writing a null or empty string into it.

**A test-authoring rule this amendment earned, worth stating once:** several
tests here fix `calibratedVersion` explicitly. Do that deliberately, not
incidentally — a test that leaves it unset is exercising the no-baseline path
whether or not it means to, and §13.9 is what happens when nobody notices which
path a test is on.

## 13.6 Files this amendment touches

| file | change |
| --- | --- |
| `src/ClaudeTuiLine/CalibrationRecord.cs` | new keyed shape, migration, pruning, revised rules 5–8, unparseable→absent, `MajorMinor` (§13.4); **§13.9: baseline is `calibratedVersion` or nothing — delete `NewestObservedVersion`** |
| `src/ClaudeTuiLine/CalibrateCommand.cs` | §13.2's verify instruction text; §6.6's reject recovery line; `--dismiss` marks all entries; widths only in `--status`/`--json`; §13.8's `observedVersion` write and `--confirm` read |
| `src/ClaudeTuiLine/Program.cs` | §13.8 — the probe branch takes `rawInput` to extract the payload's `version` |
| `tests/ClaudeTuiLine.Tests/CalibrationPromptTests.cs` | §13.5 items 1–9, 11 |
| `tests/ClaudeTuiLine.Tests/CalibrateTests.cs` | §8.11 / §13.5 item 10 |
| `src/ClaudeTuiLine/SchemaCommand.cs` | §7 — the `chromeReserve` description is now the only discovery path for a patch-release chrome change |
| `SPEC.md`, `README.md` | only if either documents the record shape, the `--dismiss` semantics, or when the nudge fires |

**Must not change:** anything in §4's must-not-change list; `calibratedVersion` /
`calibratedReserve` staying singular; rule 5 staying an exact comparison
(§13.3, §13.4); §6.4's refuse-to-write rule for the *config* (§13.3's opposite
rule applies only to the record).

## 13.7 Confidence and risk

Confidence in §13.2 is high — the failure was observed directly and the fix is
wording plus a regression assertion.

Confidence in §13.3 is **medium-high**, with one caveat stated plainly: the
sixteen-concurrent-version scenario was observed once, during an auto-update, and
the fix is reasoned from the shape mismatch rather than measured against a second
occurrence. The keyed shape is strictly better than the single slot under every
scenario I can construct, including the single-version one, so the downside risk
is low even if my model of the update behaviour is imperfect. §13.5 item 2 is the
test that would fail if it is.

§13.4 is a product decision Jim made, not a design judgement of mine; my role was
to make its semantics exact and to fence it off from rule 5. The one risk worth
naming is drift: the §13.3-rejected coarsening and the §13.4-adopted coarsening
look identical in a diff and differ entirely in consequence. §11's fourth bullet
and §13.4's closing paragraph exist to make that drift visible to a reviewer.

Confidence in §13.8 is high — it is §5.4's existing rule applied to one more
fact, through the mechanism that already exists for exactly this purpose.

Confidence in §13.9 is high, and the correction makes the design simpler than
what it replaces: one fewer helper, one fewer fallback, one fewer branch.
**Two of Amendment 2's three defects were mine and both were in §13.4's
supporting rules rather than in the decision Jim made.** That is worth a
reviewer's attention on the rest of §13.4 specifically.

## 13.8 Spec gap found in implementation: where `calibratedVersion` comes from

**What was missing.** §13.3 replaced the record shape but never said what
`--confirm` should write into `calibratedVersion` once `promptedForVersion` — the
field it used to copy — no longer existed. The Implementor flagged this rather
than shipping it silently, and inferred
`calibratedVersion = newest entry in versions`.

**That inference is wrong, and the Implementor identified the failure themselves:**
a pane on an older client version confirming after a newer pane has prompted
would record a version that pane never verified against.

**Classification: spec-defect, mine.** And it is the *third* instance of §5.4's
rule, which is why §5.4 now enumerates them. The version a reserve was measured
against is a fact known **only to the hook** — the hook held the payload and
rendered the probe rows. Sourcing it from the durable record is not a different
kind of mistake from sourcing it from the CLI's environment; it is the same
mistake wearing a different hat. **Inference from another file is not an
exception to §5.4.**

### The fix

- **§6.1 state file gains `observedVersion`** (string, nullable). The hook writes
  it from the payload's `version`, alongside the probe render — the same
  mechanism, at the same moment, for the same reason `observedColumns` is written
  (§5.4).
- **Written on every probe render, ruler *and* verify.** Verify also renders
  through the hook, so recording it in both phases makes the `--set` path — which
  skips ruler phase entirely — work with no special case. Do **not** extend
  `observedColumns` to verify phase; leave it ruler-only as specced, because
  `--saw`'s precondition (§6) depends on its current meaning.
- **`--confirm` sets `calibratedVersion = state.observedVersion`.** Never from the
  record, never from the CLI's environment, never by shelling out to
  `claude --version`.
- **If `observedVersion` is absent** — the payload's version was null, or the
  state predates the field — `--confirm` **still writes `chromeReserve` to the
  config**, which must not be blocked, and leaves `calibratedVersion` unchanged.
  **Never guess.** The consequence is bounded and safe: rule 5 cannot fire, and
  under §13.9's corrected rule 8 an absent `calibratedVersion` means *prompt*,
  which is the correct direction of error.

### Multi-pane: note it, do not engineer for it

Whichever pane most recently rendered a probe wins `observedVersion`, and during
a staggered auto-update that may not be the pane the user was looking at. The
consequence is bounded: a wrong `calibratedVersion` costs at most a spurious or a
missing nudge, **never a wrong reserve** — the reserve itself comes from the
user's own observation, not from the version. Calibration is inherently
machine-global because it produces one config value; that is pre-existing and not
something Amendment 2 introduced. Do not add per-pane state to solve it.

### Why this is not inconsistent with `--dismiss`

`--dismiss` reads the *record*; `--confirm` reads the *state file*. Same rule,
two different facts: `--dismiss` is a statement about prompts already shown,
which is what the record holds, while `--confirm` is a statement about a
measurement just taken, which only the hook that drew the probes observed. Take
each fact from wherever the hook actually recorded it. §12.6 carries the same
note.

## 13.9 Defect found in implementation: a version used as its own baseline

**How it surfaced.** While applying §13.8 the Implementor hit a test that behaved
oddly: with no `calibratedVersion` set, the version being evaluated could be
returned as its own baseline, so the series comparison was self-comparison, which
is trivially equal, so no prompt. They classified it as a test-authoring problem
and fixed the test by pinning a `calibratedVersion`.

**The test fix was right. The classification was not.** The same path is
reachable in production, on the most common path in the whole feature:

1. Fresh machine, record absent. Render at `2.1.238` → rule 3 prompts, rule 8
   writes `versions["2.1.238"]`. `calibratedVersion` is still absent, because only
   `--confirm` ever sets it.
2. Next render, same version. Rule 3 no longer fires (the record now exists).
   Rule 5 cannot fire (`calibratedVersion` is null). Rule 6 no (not dismissed).
   Rule 7 no (inside the window). Rule 8: baseline falls back to the newest
   `versions` entry, which is `2.1.238` — the very version being evaluated.
   `MajorMinor` equal → **no prompt.**

So the first-run nudge appeared for exactly **one render** and then never again,
and rule 7's seven-day window became dead code for the first-run case. That
contradicts §12.4 (persistent footer) and §12.8 ("once, for up to the 7-day
window" — one *episode*, not one render), and it fires for **every existing user
on the release that ships this**.

**Classification: spec-defect, mine.** §13.4's original baseline text said
"…else the entry in `versions` with the newest `promptFirstSeen`; else there is
no baseline — but rule 3 has already decided, so this case cannot reach rule 8."
The final clause is the root error: **an absent `calibratedVersion` does not
imply an absent record.** The record comes into existence the moment the first
prompt is written, and from that instant the fallback is live.

**The fix** is §13.4's corrected baseline and rule 8: the baseline is
`calibratedVersion` or nothing, an absent baseline means prompt, and
`NewestObservedVersion` is deleted rather than adjusted.

**Two things worth carrying beyond this spec.**

*A value may only serve as a baseline if it represents a commitment.* The bug is
not arithmetic; it is that "the last version we nagged about" was allowed to
stand in for "the version the user accepted". Self-comparison is what you always
get when a proxy is substituted for the thing it proxies, and it always reads as
"nothing changed".

*A test that leaves a discriminating field unset is silently testing a different
path.* The test that exposed this passed for a reason unrelated to what it
claimed to assert, and it passed for months of design review. §13.5 closes with
the rule that follows from it.
