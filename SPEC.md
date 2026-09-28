# SPEC — claude-tui-line: .NET rebuild of the Claude Code statusline

> **Status: v1 — implemented, and superseded for architecture by
> [SPEC-V2-FRAMEWORK.md](SPEC-V2-FRAMEWORK.md), which is the current source of truth.**
>
> This document is **not** archived, because parts of it are still cited as live authority from
> v2 — §6b (config is re-read on every render), §6 (`Plain.Length` is the width metric), line 353
> (an empty surface emits zero bytes), and Phase 1 (the `chromeReserve` width fix) as a
> prerequisite. Those rulings stand.
>
> Everything about *structure* here describes one hardcoded statusline with 14 built-in segments,
> which v2 replaced with a pane tree and an item registry. Where the two differ on architecture,
> v2 wins and this file is history. Where v2 cites this file by number, this file is normative.
>
> **New rules do not go here.** §10 requirement 2 used to carry the build-and-deploy discipline;
> it now lives at v2 §14, because a rule reachable only from a document nobody is pointed at is
> not a rule. v2 §14.4 explains that finding.

Behavioral contract: **CAPTURE.md in this directory is normative.** This spec adds the design
decisions, project structure, and acceptance criteria; where behavior is concerned, CAPTURE.md
wins and this spec only records deviations from it (listed in §8).

## 1. Stack

- **.NET 10** (SDK 10.0.301 confirmed installed), C#, single console project.
- **Spectre.Console** (latest stable NuGet) for styling/markup and ANSI rendering.
  Terminal.Gui was rejected: the statusline is spawned once per render (up to 1/second),
  prints rows, and exits — there is no event loop to host.
- **Native AOT** publish (`PublishAot=true`). This is load-bearing, not an optimization:
  JIT startup alone (~60–120ms) would blow the ~44ms whole-render budget from CAPTURE.md.
  Xcode CLT (required for AOT linking on macOS) is confirmed present.
- **System.Text.Json with a source-generated `JsonSerializerContext`** for the stdin
  contract — reflection-based serialization is unavailable under AOT.

## 2. Project layout

```
claude-tui-line/
  CAPTURE.md                      # behavioral contract (normative)
  SPEC.md                         # this file
  src/ClaudeTuiLine/
    ClaudeTuiLine.csproj
    Program.cs                    # top-level flow: stdin → parse → segments → wrap → emit
    StatusInput.cs                # records for the stdin JSON + JsonSerializerContext
    Config.cs                     # user config (~/.claude/claude-tui-line.json) — §6b
    Segment.cs                    # record Segment(string Markup, string Plain)
    SegmentBuilder.cs             # builds the ordered segment list from StatusInput
    GitBranch.cs                  # async `git branch --show-current` probe
    EngramTelemetry.cs            # 64KB tail read + eligibility + verbs + fact count
    SurfaceLayout.cs              # the ONE place chromeReserve is subtracted from COLUMNS
    RowLayout.cs                  # greedy wrap against a width it is handed
  tests/ClaudeTuiLine.Tests/
    ClaudeTuiLine.Tests.csproj    # xunit, runs on the JIT runtime (not AOT)
    fixtures/*.json               # stdin fixtures
    fixtures/telemetry/*.jsonl    # engram log fixtures
    ...
  bench/
    fixture.json                  # a full-featured stdin sample
    bench.sh                      # calibrated old-vs-new comparison (see §7)
```

The csproj: `net10.0`, `PublishAot`, `InvariantGlobalization=true`, `StripSymbols=true`,
`OptimizationPreference=Speed`, nullable enabled, warnings-as-errors. No other NuGet
dependencies beyond Spectre.Console.

## 3. Render pipeline (Program.cs)

1. **Start the git probe first** (§5) so it overlaps everything else.
2. Load the user config (§6b) — missing/invalid file ⇒ defaults, never an error.
3. Read stdin to end; deserialize `StatusInput` with the source-gen context.
   Invalid/empty JSON ⇒ treat every field as absent (per CAPTURE.md that yields no output;
   exit 0 — never crash, never print an error to stdout).
4. Build segments in CAPTURE.md order via `SegmentBuilder`. Each segment is a
   `Segment(Markup, Plain)` pair — `Plain` is the visible text used for width; `Markup` is
   Spectre markup. **All values interpolated from input must pass through
   `Markup.Escape`** (a `[` in a branch or file name must not be parsed as markup).
5. Await the git probe (segment 2 slot), read Engram telemetry (segment 13).
6. Wrap with `RowLayout` (§6), then emit: border enabled ⇒ one `Panel` around all rows
   (§6b); border disabled ⇒ one `MarkupLine` per row as before. No segments ⇒ emit
   nothing at all (no empty box).

### Output configuration — the pipe trap

Claude Code captures stdout through a pipe, and Spectre.Console **disables ANSI when stdout
is not a TTY**. The console must be constructed explicitly:

```csharp
var console = AnsiConsole.Create(new AnsiConsoleSettings {
    Ansi = AnsiSupport.Yes,
    ColorSystem = ColorSystemSupport.Standard,
    Interactive = InteractionSupport.No,
    Out = new AnsiConsoleOutput(Console.Out),
});
```

`ColorSystemSupport.Standard` is deliberate: the capture's look is the 16-color mid-tone
palette, and Standard forces named colors down to the base SGR codes.

**`console.Profile.Width` must also be set explicitly** (found during implementation):
Spectre defaults it to 80 on non-TTY output and silently re-wraps renderables against it —
which split already-packed rows inside the Panel.

**`RowLayout` is the sole authority on line breaks.** Spectre must never re-break its
output. The governing rule is therefore:

- **No panel rendered** (border disabled, or suppressed per §6b) ⇒ large sentinel width,
  always. Rows go out exactly as `RowLayout` produced them.
- **Panel rendered** ⇒ the surface width `COLUMNS - chromeReserve`, matching `panel.Width`. A
  panel only renders when the §6 fallback did *not* apply, so `COLUMNS` is known and parseable
  in this branch.

`RowLayout` deliberately emits rows wider than the packing budget in two distinct cases, and
binding `Profile.Width` to the terminal width corrupts both:

1. The single-line fallback (`COLUMNS` unset/unparsable or below the §6 threshold). Bash
   emits one raw line and lets Claude Code truncate it. Measured: a borderless render at
   `COLUMNS=6` with `Profile.Width = 5` emitted **50 rows** where bash emits 1.
2. An oversized single segment, which CAPTURE.md requires be given its own row and **never
   split**. Measured across a 16-width parity sweep: at `COLUMNS` 21–24 the 24-column
   segment `jimcline/claude-tui-line` was broken into `jimcline/claude-tui-` + `line`, where
   bash emits it whole. Widths 20 and below, and 25 and above, were byte-identical — so this
   defect lived in a four-column band that single-width spot checks at 6 and 200 both missed.

Case 2 is why the earlier, narrower rule — sentinel only when the fallback predicate fires —
was insufficient: an oversized segment is not a fallback, so the predicate returns false and
the finite width re-wraps a row that was intentionally overwide. Inside a rendered panel an
oversized segment *does* wrap, because a box that cannot contain its content is not a box;
that is an accepted consequence of the border feature and has no bash counterpart to violate.

### Color mapping

The emitted SGR codes must match the capture. Required codes, with the Spectre color
expected to produce each under `ColorSystem.Standard`:

| Capture color | SGR | Spectre markup tag |
|---|---|---|
| cyan    | 36 | `teal`   |
| green   | 32 | `green`  |
| yellow  | 33 | `olive`  |
| magenta | 35 | `purple` |
| blue    | 34 | `navy`   |
| red     | 31 | `maroon` |
| dim     | 2  | `dim`    |

NEEDS-EVIDENCE (implementor): pipe a render through `cat -v` and confirm the codes above.
If a mapping downgrades wrong (e.g. emits 90-range bright codes), substitute the Spectre
color that produces the required code and record the correction here.

## 4. Stdin contract (StatusInput.cs)

Records mirroring the jq paths in CAPTURE.md, all properties nullable, unknown JSON ignored.
`used_percentage` fields are `double?` (they arrive fractional); round half-to-even to int
for display and thresholds (parity with printf `%.0f`). Token counts are `long?`, displayed
integer-divided by 1000. `thinking.enabled` is `bool?`; only `true` renders.

## 5. Git branch probe (GitBranch.cs)

`git --no-optional-locks -C <cwd> branch --show-current`, stdout captured, stderr discarded,
non-zero exit or empty output ⇒ no segment. Launch as `Process` with redirected streams
immediately at startup, await at segment-build time. Guard with a **2-second timeout**
(kill + no segment) so a hung git cannot wedge the statusline — the 300ms-debounce kill in
Claude Code makes long hangs invisible anyway, but the process should still exit cleanly on
its own.

## 6. Layout (RowLayout.cs)

Port the greedy algorithm exactly as CAPTURE.md describes, with one correction to the width
budget (see the MEASURED subsection below).

**`RowLayout` does not compute its own width.** It is handed an `int? availableWidth` and is
indifferent to where that number came from. `SurfaceLayout.ComputeWidth(columnsEnv,
chromeReserve)` is the single place `COLUMNS` is parsed and `chromeReserve` is subtracted;
nothing downstream reads either. This seam exists because the v2 pane tree sizes content
against a *pane*, which has no relationship to `COLUMNS` at all.

- Surface width = `COLUMNS - chromeReserve`, `chromeReserve` defaulting to **3** (measured),
  overridable via `layout.chromeReserve`. From the environment variable only —
  `Console.WindowWidth` throws on redirected stdout, so never call it.
- When the border is enabled (§6b), the packing width is the surface width minus a
  `borderReserve` of 4: two columns for the box verticals, two for the 1-cell inner padding.
- Unset/unparsable `COLUMNS`, or usable width **< 20** (bash-exact: `avail < 20`; an earlier
  draft of this section said "< 21", which was wrong) ⇒ single unwrapped line. The `< 20`
  test applies to the *content* width, after both reserves.
- Separator ` | ` (dim pipe, 3 columns); a segment never splits; width measured on
  `Segment.Plain`.

Width metric: `Plain.Length` (UTF-16 code units) — **deliberate parity** with bash's
`${#string}`; both mis-measure CJK/emoji identically. Do not add a wcwidth implementation
in v1 (recorded as deviation-candidate in §8).

### MEASURED: the usable statusline width is `COLUMNS - 3`, not `COLUMNS - 1` — FIXED

Measured in a live Claude Code session with a ruler statusline emitting lines of exactly
known width: with `COLUMNS=112`, lines of width 112, 111, and 110 were truncated with `…`;
110 was the widest that survived intact at 109. **Usable width is `COLUMNS - 3`.**

Consequences:

- The §6b border, whose rows always fill the width exactly, overflowed by 2 columns on
  every row, so Claude Code truncated the right-hand box vertical off the entire panel. This
  is what the border actually looked like in use, and it is why the wire-in was reverted.
- The borderless path carries the same latent overflow, but it only manifests when a packed
  row lands within 2 columns of the budget — greedy packing usually leaves slack, which is
  why bash has looked fine for a long time at `COLUMNS - 1`.

**The fix shipped**, and it changed the width budget rather than the border: `chromeReserve`
(default 4, see SPEC-98 below) is subtracted once in `SurfaceLayout`, `borderReserve` (4) applies
to the box, and `panel.Width` is the surface width. Verified at `COLUMNS=112`: every rendered row
measures exactly 109 characters ANSI-stripped, against 111 before the fix.

This **breaks byte-parity with bash** at widths where the extra 2 columns change the packing.
That is intended and permanent: bash is the captured baseline, but on this measurement bash is
itself 2 columns optimistic, so parity with it would mean reproducing the defect. Parity runs
that must still pass are pinned to `chromeReserve: 1` explicitly.

**This was not caught by any test.** Every check compared the binary to bash through a pipe,
where both used the same wrong constant and agreed perfectly. Nothing exercised what Claude
Code does with the output in a real terminal. A pipe-based parity suite cannot detect a
shared misunderstanding of the render surface.

### MEASURED (SPEC-98, 2026-08-20): a stacked, titled `fill` pane needs `COLUMNS - 4`, not `COLUMNS - 3`

The measurement above was single-row and untitled. A stacked flex layout with a bordered,
titled `size:"fill"` pane (rows fully painted with border glyphs edge to edge, no trailing
whitespace for Claude Code to trim) overflowed and was truncated with `…`, even though the
tool's own emitted rows were provably exactly `COLUMNS - chromeReserve` wide with zero
internal overflow (captured directly from stdout).

Root cause: Claude Code adds a 2-column left indent to the statusline it displays, and its own
printable budget is 2 columns short of `COLUMNS` — a `2 + 2 = 4` reserve, not `3`. Confirmed
empirically both ways at `COLUMNS=90`: `chromeReserve: 3` truncated (measured broken-render
width 88, i.e. an 89-column display against an 88-column budget); `chromeReserve: 4` rendered
clean. `…` was confirmed to *replace* the boundary cell, not append past it, so this is an exact
measurement, not a range.

`DefaultChromeReserve` is now **4**, applied uniformly regardless of row count — a single-row
render pays one column it doesn't strictly need, deliberately, to avoid a second reserve value
and the row-count-dependent sizing that would require. `chromeReserve` remains overridable via
`layout.chromeReserve` in the user config. This value is measured on one machine and one Claude
Code version; it may need re-measuring after a Claude Code UI change.

Re-measuring is `claude-tui-line --calibrate` (SPEC-101-calibrate-chrome-reserve.md): a ruler/verify
protocol run against a live Claude Code pane rather than a guess, since this process can never read
back its own rendered, truncated output. `--calibrate` starts a ruler-phase probe; `--calibrate --saw
<digit>` resolves the reported digit to a candidate reserve and enters a verify phase; `--calibrate
--confirm`/`--reject` accepts or discards the candidate, writing `layout.chromeReserve` on confirm.
`layout.calibrationPrompt` (default `true`) controls whether claude-tui-line appends a one-line nudge
to the rendered statusline on first run on a machine and after a Claude Code version change.

## 6b. Border (new feature — beyond CAPTURE.md parity)

A colored box around the whole statusline, user-recolorable **live**. This is the one piece
of new behavior in v1; everything else is a faithful rebuild.

### Rendering

Spectre `Panel` wrapping all wrapped rows as a single renderable:

- Content: the `RowLayout` rows joined with `\n` inside one `Markup`.
- `Padding(1, 0)` (1 cell left/right, none vertical).
- Box style and border color from config (below); border style via
  `.Border(...)`/`.BorderStyle(new Style(color))`.
- `panel.Width` = the surface width `COLUMNS - chromeReserve` (rows were packed for the §6
  content width, so the panel never re-wraps them).
- The border adds two terminal rows (top/bottom). That is inherent to the feature and
  accepted.

**Narrow-width suppression.** The border is skipped entirely — rows written exactly as in
the borderless path — whenever §6 takes its single-line fallback: `COLUMNS` unset or
unparsable, or content width `COLUMNS - chromeReserve - 4 < 20` (i.e. `COLUMNS < 27` at the
default reserve of 3). A Panel wraps
its content to its own width regardless of how the content was packed, so at these widths
it turns the one long fallback line into one terminal row per surviving column: measured
at `COLUMNS=6`, a bordered render emitted **192 rows** of a single character each, and at
`COLUMNS=3` the content vanished entirely leaving two rows of box-drawing. Bash emits one
line at every one of these widths and lets Claude Code truncate it. A decorative border
must never cost content or turn one status row into hundreds, so below the threshold the
border loses. Note this makes the fallback test in §6 load-bearing for §6b as well: both
paths must consult the same threshold constant, not two copies of `20`.

### Config file — `~/.claude/claude-tui-line.json`

Read on **every render**; with `refreshInterval: 1` an edit takes effect within a second —
no restart, no signal. Cost is one small-file read per render (negligible against the 44ms
budget). Missing file, unreadable file, invalid JSON, or missing keys ⇒ defaults for
whatever is absent. Never an error, never output pollution.

```json
{
  "border": {
    "enabled": true,
    "color": "grey",
    "style": "rounded"
  }
}
```

- `enabled`: default **true** (the feature is the point; `false` restores the borderless
  bash look).
- `color`: any value `Spectre.Console.Style.Parse` accepts — named colors (`"red"`,
  `"grey"`, `"purple"`) or hex (`"#ff8800"`). Parse failure ⇒ default `grey`. Under
  `ColorSystem.Standard` (§3) every value downgrades to the 16-color palette; true-color
  borders are a future enhancement (§11) because raising the color system would also
  change the segment palette's emitted codes away from bash parity.
- `style`: `rounded` (default) | `square` | `heavy` | `double` | `ascii` | `none`
  (`none` ⇒ render as if `enabled: false`). Unknown value ⇒ `rounded`.

Parsed with a source-generated context (AOT rule, §1) into `Config.cs`
(`record BorderConfig`, `record UserConfig`); add `src/ClaudeTuiLine/Config.cs` to the §2
layout.

**Config path resolution.** `CLAUDE_TUI_LINE_CONFIG`, when set and non-empty, is the config
file path. Otherwise `$HOME/.claude/claude-tui-line.json`. An override naming a file that
does not exist or does not parse falls back to defaults exactly like a missing default-path
file — never an error, never output pollution.

This resolution lives in `ConfigLoader` as a public function, not in `Program.cs`. The class
that owns the path owns the rule for finding it, and `Program.cs` is top-level statements
whose local functions no test assembly can reach — resolution logic placed there is
unit-testable only through a process spawn. Empty-string-is-unset is part of the rule and
must be covered by a test.

This exists because `HOME` was the only lever on the config path, and `HOME` *also* steers
`EngramTelemetry`'s log (§7, absent `ENGRAM_HOME`). Any black-box test wanting a scoped
border config had to relocate `HOME` and then remember to re-pin `ENGRAM_HOME`, or silently
lose the engram segment — a trap that produced two false verification results during
implementation, including a spurious parity diff. Separating the two knobs removes the
coupling rather than documenting it. It mirrors the `telemetryPath` parameter on
`EngramTelemetry.Build`, exposed at the process boundary because `Program.cs` is an entry
point rather than a function a test can call with arguments.

EVIDENCE SATISFIED: `Panel` + `BoxBorder` render correctly from the published Native AOT
binary, and a config edit recolors the border between two renders with no restart. The
same evidence run at absurdly narrow widths produced the failure that the narrow-width
suppression rule above now forbids.

## 7. Engram telemetry (EngramTelemetry.cs)

Implement exactly the semantics in CAPTURE.md §"Segment 13". Design choices for the port:

- Tail read: open `FileStream`, seek to `max(0, len-65536)`, read to end, split on `\n`,
  drop the first (possibly torn) line **only when the seek was non-zero** (the bash
  `tail -c` had the same torn-line exposure; dropping it is the §8.d deviation).
- Line eligibility and field extraction may use `JsonDocument.Parse` per *inspected* line
  (skip lines that fail to parse) — unlike bash, real parsing costs nothing measurable
  here, and it removes the substring-extraction fragility. The *eligibility rules
  themselves* (session id match OR shared-kind list; primer-only fact count; newest
  started-without-finish per running kind; 10s/900s windows) must not change.
- Timestamps: `DateTimeOffset.TryParse` with invariant culture handles `Z`, `+00:00`, and
  fractional seconds — the bash dot-truncation hack is unnecessary. Unparsable ⇒ treat the
  record as stale (bash parity: iso_epoch failure degrades the same way).
- "Now" is `DateTimeOffset.UtcNow`.
- Missing/unreadable log file ⇒ no segment, no error.

## 8. Recorded deviations from CAPTURE.md (all accepted at spec time)

a. Output-style suppression is case-insensitive `"default"` (bash matched only
   `default`/`Default`).
b. Timestamp parsing accepts full ISO-8601 via `DateTimeOffset.TryParse` instead of
   dot-truncation + two `date` dialects. Strictly more tolerant, never less.
c. Git probe gains a 2s timeout (bash had none).
d. Torn first line of the 64KB tail window is discarded explicitly (bash could regex-reject
   it by luck of the `^\{` anchor; explicit is equivalent-or-better).
e. Engram record fields are parsed as JSON rather than by substring — same extracted
   values, different mechanism. Bash's mechanism was a fork-cost workaround that does not
   apply in-process.
f. ANSI escape ordering/reset placement inside a colored run may differ byte-for-byte from
   bash's `printf` output. Parity target is **semantic**: same visible text, same SGR
   color per span, same row breaks — not byte-identical streams.
g. A partial `workspace.repo` (owner or name null, object present) suppresses segment 3
   entirely. Bash would render a lopsided `owner/` or `/name` — jq's `+` treats null as
   identity — which is degenerate output, not behavior to preserve.
h. Telemetry records with future timestamps are treated as stale, not fresh. Bash's
   `now - ts <= window` arithmetic reads a future timestamp as maximally fresh; under
   clock skew that pins verbs on screen. Rejecting negative ages is strictly safer.

## 9. Tests (xunit, non-AOT)

Table-driven where possible; every test feeds fixtures, no test shells out except the bench.

- **SegmentBuilder**: each of the 14 segments — present/absent conditions, exact `Plain`
  text and `Markup` for representative inputs; threshold boundaries 49/50/79/80; PR state
  mapping incl. unknown states; style suppression; ctx with and without token counts;
  markup-escape of hostile names (`[red]x[/]` as a branch name).
- **RowLayout**: no-COLUMNS mode; exact-fit boundary; oversized single segment; separator
  accounting across 3+ rows.
- **EngramTelemetry**: fixture logs covering — own-session hook record vs foreign-session
  hook record (leak test from the capture); shared-kind eligibility; placeholder session id;
  primer-only fact count (the recall-record trap in CAPTURE.md); fresh vs stale instants at
  the 10s edge; started/finished/crashed index+embedding incl. the 900s bound; both-verbs
  ordering; torn first line; missing file.
- **StatusInput**: full fixture, empty object, invalid JSON, unknown-fields tolerance.
- **Config/Border**: missing file ⇒ defaults; invalid JSON ⇒ defaults; partial object
  (only `color` set) ⇒ other keys default; bad color string ⇒ grey; unknown style ⇒
  rounded; `enabled:false` and `style:"none"` ⇒ borderless output identical to the
  pre-border golden rows; border-on ⇒ packing width shrinks by 4 (a segment set that fits
  one row borderless splits at the boundary with border on); no segments ⇒ zero output
  even with border enabled.

## 10. Acceptance criteria

1. `dotnet test` green.
2. `dotnet publish -c Release` (AOT) succeeds; binary at `publish/claude-tui-line`.

   **The build-and-deploy discipline that used to be written out here now lives at
   [SPEC-V2-FRAMEWORK.md](SPEC-V2-FRAMEWORK.md) §14** — the one command that produces the
   artifact, identity by hash rather than timestamp, the separation of building from deploying,
   and the rule that writing into `publish/` requires approval because it replaces something the
   user is running.

   It moved rather than being copied: two documents stating the same rule is the defect this
   project has spent the most effort removing, and the copy that nobody is pointed at is the one
   that goes stale. v2 §14.4 records why it was unreachable here in the first place — this
   document is not what `README.md` sends a contributor to read.
3. `bench/fixture.json` piped to the binary emits colored rows; `cat -v` shows the §3 SGR
   codes; visually comparable to `bash ~/.claude/statusline-command.sh` on the same input
   (COLUMNS set identically). Bash-parity comparisons run with the border disabled via a
   test config; a separate border-on render shows the box, and editing the config file's
   `color` between two renders changes the border color with no restart.
4. **Benchmark, run with the calibration discipline from CAPTURE.md**: measure
   old-vs-old first and require ~0ms gap before trusting old-vs-new. Use `hyperfine` if
   installed (`command -v hyperfine`), else a 50-iteration timing loop. Target: new p50
   **≤ 44ms**; expected ~15–25ms. Report the numbers.
5. No output and exit 0 on: empty stdin, invalid JSON, missing telemetry file with an
   otherwise-empty input.

## 11. Explicitly out of scope for v1

- Wiring into `~/.claude/settings.json` (the Orchestrator does that after review).
- wcwidth-accurate CJK/emoji measurement (§6).
- Any new segments, or theming beyond the §6b border config — otherwise a faithful rebuild.
- True-color (24-bit) border colors: requires raising the console's ColorSystem, which
  would also change the segment palette's emitted codes away from bash parity (§6b).
- `git init` / committing — VCS setup is the user's call.
