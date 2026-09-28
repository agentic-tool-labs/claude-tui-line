# SPEC-95 Implementor Report — branch `task-95` — Revision 2 update

## Files changed (still NOT committed)
- `src/ClaudeTuiLine/SizeResolver.cs` — §5.1/§5.1.1/§5.2/§5.4(a) production fix
- `tests/ClaudeTuiLine.Tests/SplitFlexTests.cs` — §10 tests 1–9
- `tests/ClaudeTuiLine.Tests/MinRowsDistributeTests.cs` — `OverConstrained_MinRows_EmitsDroppedPaneNote` updated per §5.1.2

## §5.1.1 fix

`SearchFloor` now tests `MaxSize` first: if `c.MaxSize` is set and `< MinUsableWidth +
OwnBorderReserve(c)`, returns the unmodified `Floor(c,...)` (pre-#95 behavior), full stop — the
broadened floor is only applied when there's no such constraint. Matches the spec's three-branch
form exactly (maxSize clause tested first and wins).

## `MinRowsDropNoteTests` — confirmed passing byte-identical

Re-ran `MinRows_OverConstrainedThreeChildSplit_EmitsPaneDroppedNotesAndDropsToOneChild` after the
§5.1.1 fix: **passes unchanged**, `lo=0, hi=1` (all three children carry `maxSize:1`, below the
broadened floor, so the maxSize clause fires and each candidate falls back to the pre-#95 `Floor()`
of 0), both drop notes intact. Did not touch this file.

## `MinRowsDistributeTests.OverConstrained_MinRows_EmitsDroppedPaneNote` — resolved, derivation shown

Re-ran under §5.1.1 first: still fails (leaf carries no `maxSize`, so it isn't exempted). Derived
the new cascade from `SPEC-2.3.1-min-rows-floor-sum.md` §2/§4, not from observed output:

Config: `gutter:1`, children `[fixed 500, fixed 500, content leaf (no maxSize, no border key →
config default is bordered, OwnBorderReserve=4)]`. `surfaceWidth=57`.

- **Pass 1** (3 children): fixed panes grant their literal 500 each, unclamped (§2.3.1-min-rows-floor-sum §2's known, out-of-scope, fixed-pane-overrun defect — present on both greedy and min-rows, unaffected by this ticket). Leaf: sole `SolveMinRows` candidate, no `maxSize` → broadened floor = `20 + 4 = 24` (`lo=24`), `hi = Max(Min(∞,57),24) = 57`; resolves on the feasible path at its floor (24). `avail = 57 - 2×1 = 55`. `Σgrants = 500+500+24 = 1024 > 55` → **overAllocated**, drop pane 3. Note: `"pane 3 dropped: children need 1024 columns at 57 columns"`.
- **Pass 2** (2 fixed children, no `SolveMinRows` candidates left): `avail = 57 - 1 = 56`. `Σgrants = 1000 > 56` → overAllocated (§2.3.1 §4: "catches §2's fixed-pane overrun on the min-rows side for free" — this is the SAME mechanism that already fired pre-#95, unrelated to the leaf's floor). Drop pane 2. Note: `"pane 2 dropped: children need 1000 columns at 57 columns"` — **unchanged text**, since the leaf is already gone by this pass.
- **Pass 3** (1 fixed child, `count <= 1`): `ClampToAvail`, `avail=57`, grant 500 > 57 → clamp to 57. (Not asserted by this test, matches pre-existing unasserted behavior.)

**Survivor count: 1 — unchanged from before #95** (the old test already asserted
`Assert.Equal(1, resolved.Children.Count)`; that assertion is untouched). The only change is pane
3's own drop-note total (1024, not 1000), because its grant is no longer the degenerate 0 — exactly
§5.1's intended effect, not a new claim about which/how-many panes survive.

Updated: pane 3's assertion (`"...children need 1024 columns..."`), and amended the stale
lines-318-330 comment to state the leaf no longer bottoms out at 0 and why (SearchFloor). Pane 2's
assertion text was already correct and is untouched.

## §10 tests 8–9 (new, in `SplitFlexTests.cs`)

- **`Spec95_V8_MaxSize_SurvivesTheBroadenedSearchFloor`** — single bordered content leaf,
  `MaxSize:5` (below the 24-column broadened floor), declared-vertical min-rows pane, resolved at
  width 80. Asserts `OuterWidth <= 5` directly on the grant.
- **`Spec95_V9_DeclaredVertical_MinRows_SizesContentLeavesOutsideFlex`** — three bordered content
  leaves, no `maxSize`, declared-vertical (not flex) min-rows pane, width 200. Asserts all three
  get non-zero grants and none is dropped.

## Test results

Full suite (`dotnet test`, no filter): **1524/1524 passed, 0 failed** (was 1522/1522 with 2
failures before this round; +2 for tests 8/9, both previously-failing tests now resolved).

## E2 re-run (required by §9 since §5.1.1 changes `searchFloor`)

Published `Release` binary rebuilt after the §5.1.1 fix; re-ran Jim's real config
(`~/.claude/claude-tui-line.json`) at all 5 widths:

| Width | Orientation | Pane 1 width | Pane 2 width | Rows/pane | Total rendered rows | Dropped | Fits maxRows:10 |
|---|---|---|---|---|---|---|---|
| 80  | Side-by-side | 35 | 42 | 4 | 6  | nothing | yes |
| 100 | Side-by-side | 49 | 48 | 4 | 6  | nothing | yes |
| 120 | Side-by-side | 59 | 58 | 3 | 5  | nothing | yes |
| 40  | Stacked | 37 | 37 | 3 | 10 | nothing | yes, exactly 10 |
| 24  | Stacked | 21 | 21 | 3 | 10 | one segment truncated to 17 cols in pane 2 — not a dropped pane | yes, exactly 10 |

**Byte-identical to the pre-§5.1.1 run** — confirmed, not assumed. Jim's children declare no
`maxSize`, so §5.1.1's new clause never fires for his shape.

## Status

All work from the Revision-2 dispatch is done and verified: §5.1.1 implemented, both flagged
pre-existing tests resolved per their own instructions (`MinRowsDropNoteTests` untouched and
passing; `MinRowsDistributeTests` updated with a spec-derived cascade, survivor count unchanged),
tests 8–9 added, full suite green (1524/1524), E2 re-confirmed unchanged. Still **not committed** —
routes to cdtui-worker next per standing instruction, not merged.
