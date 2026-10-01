# SPEC-98 — stacked `size:"fill"` pane overflows the terminal at narrow widths

Status: **CLOSED for design. Root cause confirmed, constant measured: `DefaultChromeReserve = 4`.
No open evidence items. §6.1 is implementable as written.**

> **AMENDMENT 1** (after E1): H1 refuted by measurement. H3 added; E3 promoted to run first.
>
> **AMENDMENT 2** (Jim: *"can we grab the chrome reservation instead of hardcoding it?"*):
> §6.0 added; the `DrawRows` clamp downgraded to **recommended against**.
>
> **AMENDMENT 3** (after E3): §2's asymmetry mechanism was WRONG — I blamed
> `Compositor.ComposeRoot`'s trim, which never runs on this path. H2/H3 fork dissolved.
> E2 withdrawn as unrunnable, replaced by E5.
>
> **AMENDMENT 4** (after E4): payload option **dead, confirmed by enumeration**. §6.0
> option 1 struck. Incidental finding in §4.1.
>
> **AMENDMENT 5** (after E5) — **the third correction to me, and the most dangerous:**
> 1. **E5's formula `N = 90 − L` was WRONG.** It conflated the *displayed* line width with
>    the width this program *emits*. Claude Code's two-space indent lives outside our
>    stdout, so the formula silently double-counted it and produced `N = 2` — **smaller**
>    than today's 3, in flat contradiction of §2.2, which this same spec had established.
> 2. **E5's measurement method was also wrong.** `awk '{print length}'` counts *bytes*;
>    the box glyphs are multibyte UTF-8.
> 3. The ±1 rounding rule was **not** applied, because a free discriminator existed (E6).
>
> Both defects were caught by the Implementor refusing to ship a number that contradicted
> the spec's own reasoning. Record that as the process working, not as friction.
>
> **AMENDMENT 6** (after E6): **`4` confirmed empirically**, not merely by the
> symmetric-margin prior. The replace-vs-append ambiguity is resolved and the ±1 caveat is
> struck from §6.1's comment instructions. §6.1 §"what the regression test does NOT catch"
> added — the constant cannot be test-protected, and nobody should believe otherwise.

Baseline: `main` @ `185491a`. Citations: commit + quoted code, per SPEC-92.

---

## 1. Symptom

Pane width 90. Root `surface.pane`: `split:"flex"`, `gutter:0`, `distribute:"greedy"`;
children `project` (`size:"content"`, bordered) and `model-pane` (`size:"fill"`,
bordered, titled). Flex stacks. `project` renders correctly; **all three rows of the
`model-pane` box** are truncated by Claude Code with a literal `…`.

---

## 2. Root cause (established, closed)

**The tool's own output is provably correct. Claude Code's printable budget is narrower
than `COLUMNS - 3`, and `chromeReserve` does not account for it.**

E3 captured real stdout at `COLUMNS=90`:

```
87 '╭──────────────────────────────────────────────────────────────────╮                   '
87 '│ claude-tui-line | main | JimCline/claude-tui-line | engram:12872 │                   '
87 '╰──────────────────────────────────────────────────────────────────╯                   '
87 '╭─ Opus 5 : medium ───────────────────────────────────────────────────────────────────╮'
87 '│ ctx:0% (0k/200k) | 5h:6% / 7d:81% | agent:architect                                 │'
87 '╰─────────────────────────────────────────────────────────────────────────────────────╯'
```

Every row exactly `87 == COLUMNS(90) − chromeReserve(3)`, as §3 predicted. **Zero internal
overflow.** The defect is entirely in the budget.

### 2.1 The width model — keep these three apart

Conflating these is what produced both the original bug and Amendment 5's bad formula:

| quantity | at `COLUMNS=90` | controlled by |
| --- | --- | --- |
| `COLUMNS` | 90 | terminal / Claude Code |
| **emitted width** — what this process writes to stdout | 87 (`= 90 − chromeReserve`) | **us**, via `chromeReserve` |
| CC's left indent, prepended after we exit | 2 | Claude Code |
| **displayed width** = emitted + indent | 89 | consequence |
| **CC's displayed budget `B`** | **88** — measured, E5 | Claude Code |

`89 > 88`, so the row is truncated **by exactly one column**. That column is the whole bug.

`chromeReserve` governs **only the emitted width**. The indent is not ours to emit and
cannot be observed at render time, so it must be *covered by* `chromeReserve`, never
subtracted from a displayed figure. Hence:

> **`chromeReserve = COLUMNS − (B − indent)` = `90 − (88 − 2)` = 4** — confirmed by E6.

The reserve decomposes as a symmetric two-column margin either side: 2-column indent on the
left, `COLUMNS(90) − B(88) = 2` on the right.

Consistency check against Amendment 3, which bounded the budget at `70 ≤ B < 87` in
**emitted** columns: `B − indent = 86`, and `70 ≤ 86 < 87`. The earlier reasoning holds;
only the units were left implicit, which is precisely how the bad formula slipped through.

### 2.2 Why only `model-pane` shows the `…`

Evidence, not inference. Both boxes emit 87-column rows (E3). `project`'s last 17 columns
are literal trailing spaces — final glyph at ~column 70, displayed ~72, far under 88.
`model-pane`'s rows end in a **glyph** (`╮`/`│`/`╯`) at emitted column 87, displayed 89.
Jim sees `…` on `model-pane` only. Therefore **Claude Code trims trailing whitespace before
deciding overflow**.

A `content` pane hides an over-wide budget behind its padding; a `fill` pane paints a glyph
into the overflow and exposes it. The asymmetry was never about sizing.

My earlier explanation blamed `Compositor.ComposeRoot`'s `TrimEnd` (`Compositor.cs:56-58`).
**Wrong.** On the split pipeline `ComposeRoot` is never called for the root —
`Program.cs:198` returns `rootContribution.Buffer.Rows` directly — and the stacked branch
pads rather than trims (`PaneTreeRenderer.cs:226`, `contentRows = PadToWidth(rows, innerWidth);`).
The effect was real; the component was misidentified.

### 2.3 H3 (row-count-dependent chrome) — open, and deliberately moot

Nothing distinguishes "the v1 constant was always slightly wrong" from "CC indents only
multi-row statuslines, so 3 was right for the case measured." **I chose not to resolve it**
— §6.1 adopts a fix correct under both. Cost: one column in the single-row case. Saving: an
entire design fork.

---

## 3. The layout chain (empirically confirmed — retained for citation)

`Program.cs:74` → `SurfaceLayout.cs:19` (`columns - chromeReserve`) is the sole source of
surface width. `Program.cs:192` → `HeightLadder.cs:63` → `SizeResolver.Resolve`. In the
stacked branch (`SizeResolver.cs:201-204`) `stackedAvail == outerWidth` (root declares no
border), and `StackedWidth` (`SizeResolver.cs:473-490`) ends in
`Math.Clamp(bounded, 0, avail)` — a `fill` child **cannot** exceed it.
`PaneTreeRenderer.cs:43,50` gives `innerWidth = outer − 4`; `PaneBorderRenderer.cs:48-53`
emits `outerWidth = innerWidth + 4`. Predicted: every row ≤ 87, `model-pane`'s three
exactly 87. **E3 measured exactly that.**

`SizeResolver.StackedWidth`, `ResolveNode`'s `Horizontal` branch, and
`PaneBorderRenderer.Wrap` are conclusively exonerated. SPEC-96/97 correct as merged.

### 3.1 The title is exonerated

`Pane.Title` reaches width computation only at `SizeResolver.cs:1191-1195`, inside
`MeasureRequest`, which the stacked path calls **only** for `SizeKind.Content`
(`SizeResolver.cs:477`). `model-pane` is `Fill` → never runs. At render time
`BuildCaption` truncates to `innerWidth - 2` (`PaneTreeRenderer.cs:356-366`) and `Wrap`
splices only when `free >= 2` (`PaneBorderRenderer.cs:79-97`), never changing
`outerWidth`. E3's top border row measuring exactly 87 confirms it directly.

### 3.2 Flex orientation

`SideBySideNeed` (`SizeResolver.cs:274-291`) = 68 + 24 + 0 = **92**; stacks whenever
`surfaceWidth ≤ 91`. Measured 87 → stacks. The Implementor's corrected repro (using
`workspace.repo` per `StatusInput.cs:47-63`) reproduces both content and stacking.

**Note for the fix:** with `chromeReserve = 4`, `surfaceWidth` at `COLUMNS=90` becomes 86,
still ≤ 91, so this configuration still stacks. The fix narrows the box; it does not change
the orientation, and the §6.1 regression test's fixture stays valid.

---

## 4. Secondary finding (real, latent, NOT this bug)

`PaneBorderRenderer.cs:102-104`:

```csharp
rows.AddRange(contentRows.Select(row => suppressed
    ? row
    : new PaneRow(leftGlyph + " " + row.Markup + " " + rightGlyph, row.Width + reserve)));
```

Content rows wrap at `row.Width + reserve`, **never clamped to `width`**, while top/bottom
use `horizontalSpan == width + 2`. A content row wider than `innerWidth` yields a **ragged
box**. Reachable via `RowLayout.cs:57-60`, where below `MinUsableWidth` the packer
deliberately emits an unwrapped over-wide row; `ShouldSuppressBorder`
(`SizeResolver.cs:78-87`) fires for `Fill`/`Percent` only, so a **`content`**-sized pane at
`innerWidth < 20` keeps its border *and* gets that row.

**File as its own ticket. Do not bundle into SPEC-98.**

### 4.1 Incidental finding from E4 — also its own ticket

`agent` / `agent.name` was **absent from 7 of 9** captured payloads, though `StatusInput.cs`
models it. It works when present (Jim's statusline renders `agent:architect`), so this is
not a live defect — but a sometimes-absent upstream field deserves one deliberate look at
the null path rather than a discovery later. Out of scope; do not fold in.

---

## 5. Evidence log — complete, nothing outstanding

- **E1 — `COLUMNS` seen by the hook. DONE. H1 REFUTED.** `COLUMNS=90` for the affected
  pane, alongside other panes' distinct widths (32/56/63/72/73/89/91/118/122). The hook
  receives the true per-pane width. `settings.json` restored from backup, verified.
- **E3 — emitted row widths. DONE. §3 CONFIRMED.** All six rows exactly 87. No internal
  overflow.
- **E4 — payload width field. DONE. OPTION 1 DEAD.** 9 real `COLUMNS=90` payloads; all 44
  key-paths enumerated across two shapes. No width/columns/viewport/terminal/size field.
  `context_window.context_window_size` (=1000000) correctly flagged and **ruled out as a
  false positive** — a token budget, not a terminal width. `workspace.repo.{host,owner,name}`
  matches `StatusInput.cs`. `$COLUMNS` is the only width signal, and E1 confirmed it accurate.
- **E2 — WITHDRAWN as unrunnable.** It presumed the tool could observe its own rendered
  output; E3 proves it cannot. The Implementor was right to refuse it — I had specified a
  measurement requiring a feedback channel §6.0 option 2 already documented as nonexistent.

### E5 — Budget recovered from Jim's paste. **DONE. `B = 88` displayed.**

Jim's fresh, unretyped paste of the three broken `model-pane` lines: **all three exactly 88
characters**, including the two leading indent spaces and the trailing `…`.

**Method correction (mine was wrong).** `awk '{print length}'` returned 226/92/260 — it
counts **bytes**, and the box-drawing glyphs are multibyte UTF-8. Use a character-aware
count: Python `len()` on UTF-8-decoded text, `wc -m`, or awk under a UTF-8 locale.
**Every future width measurement in this repo must count characters, not bytes.**

**Formula correction (also mine, and worse).** The spec said `N = 90 − L`. That treats `L`
as an emitted width when it is a *displayed* width, silently double-counting CC's indent
and yielding `N = 2` — smaller than today's 3, contradicting §2.2. The correct derivation
is §2.1's table: `chromeReserve = COLUMNS − (B − indent)`.

### E6 — Does a line of exactly `B` survive? **DONE. YES → the constant is 4.**

Run live with Jim's consent: `layout.chromeReserve: 4` set in `~/.claude/claude-tui-line.json`
(the field already exists — `Config.cs:211-212`, consumed at `Config.cs:710`), so no rebuild
and no code change. Jim inspected the 90-column pane and confirmed the `model-pane` box
renders clean, no `…`. Override removed immediately after and the config verified restored
byte-for-byte.

This resolves the replace-vs-append ambiguity: `…` **replaces** the boundary cell, so a line
of exactly `B = 88` displayed columns survives untouched. **4 is measured, not inferred** —
the symmetric-margin argument in §2.1 is now corroboration rather than the basis. The
bracket is two-sided: `3` is known-broken (this bug), `4` is confirmed clean.

---

## 6. Fix

### 6.0 Jim's question: can the chrome reservation be acquired rather than hardcoded?

1. ~~**Read it from the payload**~~ — **DEAD.** E4 enumerated all 44 key-paths; no such
   field exists. Confirmed, not assumed.
2. **Self-measure at runtime** — *impossible.* The hook writes to a pipe and never sees what
   was displayed. No feedback channel. Do not attempt.
3. **Calibrate once, per user, into config** — a `claude-tui-line --calibrate` subcommand
   that renders a width sweep, asks which row was last clean, writes `layout.chromeReserve`.
4. **Re-measure the constant, and surface the existing override** — the escape hatch already
   exists (`Config.cs:211-212`); it is undocumented and undiscoverable, which is why nobody
   reached for it.

**The Implementor's argument, adopted:** a human-in-the-loop `--calibrate` *is* the feedback
channel option 2 lacks. Ruling out automatic self-measurement does not rule out measurement
— it rules out doing it *without a human*. **E6 was exactly that loop, run once by hand, and
it took one config key and one glance.** That is evidence for option 3 being cheap to build
and evidence that option 4 is sufficient today.

The choice is genuinely **3 vs 4**. Against 3: more surface area on a tool Jim maintains
alone. For 3: this constant **has already rotted once** — correct for v1 single-line,
silently wrong the moment multi-row shipped, with no test or diagnostic catching it; and E6
pins it on *one* machine and *one* CC version.

**My recommendation: ship 4 now (small, unblocks Jim today), put 3 to Jim as a follow-up.**
I am not choosing 3-vs-4 for him — it trades surface area against robustness on his tool.

### 6.1 The constant — **`4`, measured (E6)**

`DefaultChromeReserve = 4`. The replace-vs-append ambiguity is resolved; **do not carry a
±1 caveat into the comment** — it would misrepresent a two-sided measurement as a guess.

The reserve is **uniform across row counts** — deliberately, to dissolve the H2/H3 fork
(§2.3). Rejected alternative: resolving twice to learn the row count first, which would
entangle `HeightLadder`'s degrade ladder for one column in the single-row case.

Edits:

- `src/ClaudeTuiLine/Config.cs:676-677` — `DefaultChromeReserve` → `4`.
- Rewrite the `// SPEC.md §6 "MEASURED"` comment to state: the figure; **the case measured**
  (indented multi-row bordered surface at `COLUMNS=90`); the date; that it decomposes as
  2-column indent + 2-column right margin; and that it is uniform across row counts by
  choice. The current comment asserts a number without naming its case — **that ambiguity is
  what let this sit undetected for a whole major version** — and the replacement must not
  repeat it.
- Update `SPEC.md` §6's "MEASURED" paragraph to match.
- Document `layout.chromeReserve` as the user-facing override — README plus the schema
  description at `SchemaCommand.cs:172`.
- **Must not change:** `SurfaceLayout.ComputeWidth` stays the single point of subtraction
  (`Program.cs:71-74` states this invariant; `Program.cs:353`/`373` rely on it for `--preview`).
- Tests: grep the test project for `ChromeReserve` and `COLUMNS`; update every affected
  expectation. **NEEDS-EVIDENCE: I did not enumerate the test project.** If any existing test
  hardcodes `87` or `COLUMNS - 3`, changing it to match the new constant is correct — but say
  so in the commit message rather than silently re-baselining.
- **Regression test to add:** stacked flex split, `content` child plus bordered `fill` child,
  at `COLUMNS=90`, asserting the `fill` pane's emitted row width equals
  `COLUMNS − DefaultChromeReserve`. E3's captured output is the fixture.

**What that regression test does NOT catch — state this in the test's own comment.** It
asserts the emitted width against `DefaultChromeReserve`, so it passes for *any* value of
that constant. It guards the **layout invariant** — that no pane emits wider than the
surface it was granted, which is what SPEC-96/97 established and what a future refactor
could break. It does **not** and **cannot** guard the constant's correctness, because the
truncation boundary is outside this process (§6.2). The only check on the constant is a
human looking at a real pane — E6. Nobody should read a green suite as evidence the reserve
is still right after a Claude Code update.

### 6.2 The `DrawRows` clamp — **RECOMMENDED AGAINST**

Clamping to `surfaceWidth` (87) is a **no-op** — E3 shows the rows already *are* 87.
Clamping to `COLUMNS` (90) is **also a no-op** — 87 < 90. The truncation happens inside
Claude Code's renderer at a boundary this process cannot observe, and **a clamp cannot
enforce a limit it does not know**. It would look like a safety net, change nothing, and
mislead the next reader into believing the output boundary is guarded. E3 demonstrated this
rather than merely predicting it. Do not implement.

A weaker, separate justification survives — guarding a future *internal* width defect (§4) —
but that fix belongs at the root in `PaneBorderRenderer`, not at the output. If Jim wants it
anyway: `!renderingPanel` branch only; truncate on ANSI-stripped width (`AnsiStrip.cs` /
`SegmentTruncation.cs`), never raw string length; no-op when rows already fit; emit a
`RenderNote` when it fires.

---

## 7. What must not change

- `SizeResolver.StackedWidth` / `ResolveNode`'s `Horizontal` branch
  (`SizeResolver.cs:194-206`, `470-491`) — E3 confirms them correct.
- `PaneBorderRenderer.Wrap`'s `horizontalSpan`/`outerWidth` arithmetic (`PaneBorderRenderer.cs:48-53`).
- The title/caption path (`PaneTreeRenderer.BuildCaption`, `SizeResolver.cs:1191-1195`).
- `SurfaceLayout.ComputeWidth` as the single point where `chromeReserve` is subtracted.

---

## 8. Confidence and escalation

- Layout not at fault: **certain.** E3 measured the predicted widths exactly.
- Cause is CC's chrome budget: **certain**, from E3 plus Jim's paste — not inference from
  elimination.
- No upstream width signal exists: **certain.** E4 enumerated the payload.
- `B = 88` displayed: **measured** (E5), character-counted.
- `chromeReserve = 4`: **measured** (E6), two-sided — 3 broken, 4 clean.
- Valid for **one machine and one Claude Code version.** Say so in the comment rather than
  hiding it. When CC changes its statusline chrome this will rot again, and the only
  detector is a human noticing a `…`.
- H2-vs-H3: **deliberately unresolved**, made moot by §6.1's uniform reserve.

Escalation to Ultra-Advisor **not** warranted: one constant, one comment, docs, and a
regression test.

**Process note worth keeping.** Three of this spec's six amendments corrected the Architect,
and two of those came from the Implementor declining to execute an instruction that
contradicted the spec's own reasoning — E2's impossible measurement, and E5's formula
yielding a value the spec had already excluded. Both instructions looked authoritative and
were wrong. The stop-and-report contract is what caught them. The final constant differs
from what a compliant Implementor would have shipped at two separate points.
