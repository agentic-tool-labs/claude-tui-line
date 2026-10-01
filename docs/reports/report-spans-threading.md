# SPEC-85-ADDENDUM-spans-threading.md §12 — Implementor report

Status: **implementation complete, D-F fixed per peer decision (Option 1),
acceptance bar now passes for real.** 1444/1444 tests green.

## D-F fix (added after the peer's Option 1 decision)

- `src/ClaudeTuiLine/SegmentBuilder.cs` — new `internal static string
  BuildSpanMarkup(string plain, string? color)`: clean `"[color]text[/]"` (or
  unstyled) wrap, no baked-in raw SGR reset.
- `src/ClaudeTuiLine/LeafItems.cs:158,166` — both per-part `StyledSpan.Markup`
  construction sites in `BuildCompound` now call `BuildSpanMarkup` instead of
  `SegmentBuilder.BuildItemSegment(text, color).Markup`. The existing 2-arg
  `BuildItemSegment` and all its other callers are untouched, per the peer's
  scoping.
- `tests/ClaudeTuiLine.Tests/PaneAssemblerSpansTests.cs` — new fact
  `CompoundItem_TruncatedProductionSegment_BothSpansKeepColour`: builds the
  compound through the real `PaneAssembler.RenderItemRows` path, then calls
  `SegmentTruncation.Truncate` with a width that cuts inside the second span
  (forcing `RestyleSlice`'s partial-slice path, not its whole-span-survives
  shortcut that had masked the bug), asserts both `[grey]`/`[aqua]` tags
  survive.
- Full suite: **1444/1444 passing, build exit 0** (1 net new test).

### Re-verification (same narrow-width CLI check that caught the bug)

Re-ran with the same stdin/config that reproduced the failure
(`{"agent":{"name":"team-alpha-worker-seven-long-identifier-testing-long"}}`,
`--columns 60`, forced truncation). Raw ANSI bytes now show `ESC[90m` wrapping
"agent:" AND `ESC[96m` wrapping the truncated name portion, both before the
ellipsis — **confirmed for real, not just via unit test.**

### check-all.sh re-run

Same 4 known/attributable failures as before, unchanged: §9.0 (2 citing
lines), README.md:162 border-token, and the two SPEC-84
(`SPEC-V2-FRAMEWORK.md:470`/`:5913`) citations — none of these are affected by
or related to the D-F fix. My own STATUS.md:104 citation into SPEC-85 §5.1/§5.2
is still flagged by `check-citations.sh` for the reason already diagnosed
(tooling doesn't scan that not-yet-registered spec file for headings, not a
content error) — unchanged and expected, not something the D-F fix touches.

## Files changed (production)

- `src/ClaudeTuiLine/LeafContent.cs` — `ItemDecision` gained trailing
  `IReadOnlyList<StyledSpan>? Spans = null`; `Decide()` forwards
  `resolved.Display!.Spans`, clears it on the decorative-colour-escape branch.
- `src/ClaudeTuiLine/SegmentBuilder.cs` — `BuildItemSegment` gained a 4-arg
  overload `(plain, markup, color, spans = null)` that forwards `spans` when
  `color` is empty, drops them when a colour wrap is applied.
- `src/ClaudeTuiLine/PaneAssembler.cs` —
  - `RenderItemRows`: added `itemColor` (null for compounds, so the single-line
    path no longer double-wraps a compound's already-per-part colour — D-B),
    and threads `decision.Spans` into the 4-arg `BuildItemSegment` call.
  - `RenderUnit` and `RenderItemRows` changed `private` → `internal` (see
    **Deviation 1** below).
- `src/ClaudeTuiLine/SegmentTruncation.cs` — `TruncateSpans` and `RestyleSlice`
  implemented per addendum §12.7.1/§12.7.2 verbatim: span-aware truncation, the
  ellipsis becomes its own unstyled span, OSC 8 unwrap/re-wrap around slicing
  (D-D, D-E).
- `src/ClaudeTuiLine/ItemValueResolver.cs` — `ReferenceScan` gained
  `CompoundItemIds`; `ScanReferences` populates it from
  `entry.Item.Parts is not null`.
- `src/ClaudeTuiLine/ConfigCheck.cs` — new `from-compound-source` diagnostic
  (error), inserted before `from-derived-source` in `CheckReferences`.

## Tests (new)

- `tests/ClaudeTuiLine.Tests/PaneAssemblerSpansTests.cs` — 3 facts, built
  through the real `PaneAssembler.RenderItemRows` path (not hand-constructed
  Segments): acceptance-gate Spans-not-null, D-B no-double-wrap, D-C
  link-wraps-whole-compound-Spans-survive-underneath.
- `tests/ClaudeTuiLine.Tests/SegmentTruncationSpansTests.cs` — 4 facts covering
  D-D (ellipsis is its own span) and D-E (OSC8 handling in
  `RestyleSlice`/`TruncateSpans`).
- `tests/ClaudeTuiLine.Tests/ConfigCheckTests.cs` — 3 new facts for
  `from-compound-source` (item-level `from`, part-level `from`, and
  non-interference with `from-derived-source`).

Full suite: **1443/1443 passing, build exit 0** (task-gopher-run, confirmed
twice).

## Docs

- `SPEC-V2-FRAMEWORK.md` §3.3 — added the accepted-limitation bullet: a
  compound whose value spans more than one line loses per-part colour
  (block-layout path renders per-line, not through Spans).
- `SPEC-V2-FRAMEWORK.md` §9.6.1 — added `from-compound-source` registry row.
- `SPEC-V2-FRAMEWORK.md` §9.6.2 — **deliberately not edited.** The peer's
  dispatch text mentioned it, but the addendum's own authoritative §12.9
  change-list only names §9.6.1 and §3.3, and §9.6.2's existing prose about
  `compound`/`compoundPart` schema entries was already accurate. Flagging as a
  possible follow-up rather than unauthorized scope creep.
- `STATUS.md` — removed the queued "Compound items (§3.3)" bullet, renumbered
  the queued list, fixed a stale cross-reference, added a "Recently landed"
  bullet with the 1443/1443 count and the accepted multi-line-colour
  limitation.

## D-F history (found, fixed — kept for record)

Below is the original finding and root cause, preserved as-is; the fix itself
is described up top under "D-F fix". This was **blocking** at the time it was
found and is now resolved.

Peer's explicit acceptance bar: render the §3.3 agent-badge worked example
through the real CLI at 80 and 60 columns (60 forced truncation) and confirm
two distinct per-part colours survive.

- **80 columns:** two SGR colour spans present (grey `agent:`, aqua value), no
  truncation — passes.
- **60 columns, forced truncation** (used a long synthetic value so truncation
  actually triggers — the first attempt at 60 cols with the short example value
  didn't truncate at all and gave a false pass): the **first span (grey
  "agent:") survives correctly; the second span (aqua) is completely stripped**
  — the surviving pre-ellipsis text renders unstyled. Raw output:

  ```
  ...agent:...team-alpha-worker-seven-long-identifier-testin…  ...
  ^[[90m wraps "agent:" correctly. Nothing wraps the aqua text — the color
  is gone, not just the ellipsis (which is correctly unstyled by design).
  ```

### Root cause (confirmed via code read, not the addendum's D-D/D-E — a third,
previously-unidentified bug)

`LeafItems.cs:158` builds each compound part's `StyledSpan.Markup` via the
**2-arg** `SegmentBuilder.BuildItemSegment(text, color).Markup` (not the new
4-arg one — this is a separate, pre-existing call site building the *per-part*
markup that later becomes a `StyledSpan`, distinct from the item-level 4-arg
call in `PaneAssembler`). That 2-arg overload
(`SegmentBuilder.cs:73-81`) builds:

```csharp
var rawMarkup = Markup.Escape(plain) + Markup.Escape(RawSgrReset); // RawSgrReset = "\x1b[0m"... escapes to "[[0m]]"
return new Segment($"[{color}]{rawMarkup}[/]", strippedPlain);
```

So a part's `StyledSpan.Markup` is actually
`"[aqua]" + EscapedPlain + "[[0m]][/]"` — **not** the simple
`"[aqua]" + EscapedPlain + "[/]"` shape.

`SegmentTruncation.RestyleSlice`'s partial-slice branch (line ~134) calls
`Restyle` → `RestyleSimple` → `TryGetSimpleWrap` on that span in isolation.
`TryGetSimpleWrap` requires the markup's suffix to be exactly `"[/]"`
(`SegmentTruncation.cs:235`); here the suffix is `"[[0m]][/]"`, so the match
fails and `TryGetSimpleWrap` returns `false`, causing `RestyleSimple` to
**gracefully degrade to unstyled markup** (its documented behavior for an
un-recognized wrap shape) — silently dropping the colour.

This is invisible to my new unit tests because they hand-build `StyledSpan`s
via `SegmentBuilder.BuildCompoundSegment` with clean `"[color]text[/]"` markup
directly (matching the addendum's own examples) — they never exercise the real
per-part markup that `LeafItems.cs` actually produces, which carries the extra
baked-in raw SGR reset.

### Why I'm not fixing this myself

This needs a design call, not a mechanical patch — plausible fixes with
different tradeoffs:
1. Stop baking `RawSgrReset` into per-part `StyledSpan.Markup` specifically
   (leave the 2-arg `BuildItemSegment`'s existing non-span callers unchanged,
   add a spans-clean variant for `LeafItems.cs`'s per-part construction).
2. Make `TryGetSimpleWrap`/`RestyleSimple` tolerant of a trailing raw-reset
   suffix before `"[/]"` — but this is `SegmentTruncation`'s general-purpose
   restyle path, used well beyond compounds, so loosening its match rule is a
   contract change with a wider blast radius.
3. Give `RestyleSlice`'s partial-slice branch a span-specific restyle that
   doesn't route through the general `TryGetSimpleWrap` pattern-match at all.

Each has different correctness/scope implications the addendum doesn't
address (the addendum's known bug list is D-A through D-E; this is a D-F not
identified there). Per the Implementor contract, reported this gap rather than
picking one silently — peer chose Option 1 (see "D-F fix" above for what was
actually built).

## Other deviations to flag for the Reviewer

**Deviation 1 — `PaneAssembler.RenderUnit`/`RenderItemRows`: `private` →
`internal`.** The addendum's §12.10 acceptance test requires asserting
`Segment.Spans is not null` "through the real PaneAssembler path (not by
hand-constructing a Segment)", but the only public entry point
(`RenderLeafRows` → `PaneRow(string Markup, int Width)`) has no way to expose a
`Segment`. Resolved via the same `private`→`internal` +
`InternalsVisibleTo("ClaudeTuiLine.Tests")` pattern already used elsewhere in
this codebase (`ItemValueResolver.ExtractValue`/`ApplyCase`) rather than
treating it as a blocking gap. Not called for by the literal addendum text —
flagging for scrutiny, not hiding it.

**Fail-first check not empirically performed.** The addendum's acceptance-gate
test (`CompoundItem_ProductionSegment_CarriesPerPartSpans`) was required to be
confirmed failing against the pre-fix tree and passing after. All changes
landed on top of an already-uncommitted tree with no clean pre-fix checkpoint
to revert to, so this was not done. Stating honestly rather than fabricating
it.

## check-all.sh

Exit 1. `check-counts`, `check-notes`, `check-examples` all pass. Failures,
all in `check-citations`/`check-doc-tokens`:

- `SPEC-2.6-vertical-marker-splice.md:19` cites §9.0 — the known pre-existing
  failure (§9.0 corrects earlier citations per that file's own text; not
  related to this work).
- `README.md:162` cites the `border` token, which `--accepted --json` doesn't
  report — the known pre-existing failure.
- `SPEC-V2-FRAMEWORK.md:470` cites "SPEC-84-mcp-schema-explorer.md §5.0";
  `SPEC-V2-FRAMEWORK.md:5913` cites "...§5.4" of the same file — **not mine**:
  `SPEC-84-mcp-schema-explorer.md` is a separate, unrelated, already-uncommitted
  in-flight task (task #84, schema explorer) sharing this working tree (its own
  untracked files: `SPEC-84-mcp-schema-explorer.md`, `SchemaCommand.cs`,
  `SchemaCommandTests.cs`, `GetConfigSchemaToolTests.cs` — none touched by me).
- `STATUS.md:104` cites "SPEC-85 §5.1/§5.2" — **this is mine** (my own
  "Recently landed" bullet). Confirmed both headings genuinely exist
  (`SPEC-85-compound-item-implementation.md:291` "### 5.1", `:316` "### 5.2"),
  and the `§N.N/§M.M` slash-separated citation style is standard and passes
  elsewhere in this repo (e.g. `SPEC-12.6-mcp-tools.md:352`,
  `STATUS.md:267`). `check-citations.sh`'s regex (`grep -noE
  '§[0-9]+(\.[0-9]+)*'`, line 69) correctly parses `§5.1`/`§5.2` as two
  citations — the failure is that **`check-citations.sh` isn't scanning
  `SPEC-85-compound-item-implementation.md` for headings at all**, the same
  root cause as the two SPEC-84 false-positives above: the checker has some
  fixed/registered set of spec files it scans for defined headings, and
  neither of these two brand-new, not-yet-registered, still-uncommitted spec
  docs (SPEC-84's and SPEC-85's) is in it yet. This is a shared pre-existing
  tooling gap, not something introduced by this segment's changes, and not a
  citation to a section that doesn't exist. Flagging rather than fixing
  `check-citations.sh` myself — registering new spec files there is outside
  this addendum's scope and is presumably task #85's base-implementation
  responsibility (or the Architect's call), not something to silently patch
  as a side effect of the spans-threading work.

Net: **4 failures, 2 pre-existing/known, 2 attributable to already-uncommitted
work in this tree (1 from task #84 unrelated to me, 1 from my own STATUS.md
citation into a genuinely-existing but not-yet-registered SPEC-85 heading)**.
None indicate incorrect content.

## Bottom line for the peer

Code + tests + docs for Gap 1 (D-A/D-B/D-C), D-D, D-E, Gap 2/E2, and D-F
(peer's Option 1) are all done and green. 1444/1444 tests pass, build clean.
Acceptance bar — colour surviving truncation at narrow width — **confirmed for
real** via the same CLI check that originally caught the D-F bug. `check-all.sh`
unchanged at 4 known/attributable failures, none related to this work. Ready
for independent verification (cdtui-worker) per the peer's routing.
