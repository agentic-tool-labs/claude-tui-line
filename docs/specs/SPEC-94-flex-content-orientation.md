# SPEC-94 — `split: "flex"` never stacks when its children are `size: "content"`

STATUS: **REVISION 2. DIAGNOSIS CONFIRMED LIVE. FIX RULED. READY TO IMPLEMENT.**

The escalation recommended in Revision 1 §6 was carried out and answered (high
confidence). E1 independently confirmed §2 against a live run. **The normative
fix lives in `SPEC-88-AMENDMENT-flex-content-orientation.md`** — implement from
that file; this one is the diagnosis and the record of how the fix was chosen.

Priority: Jim's live daily-driver config is currently degraded (pane 2 silently
dropped).

## 1. Symptom

Reported by Jim against `~/.claude/claude-tui-line.json`, on `main` at `ff53098`.

```
dotnet run --project src/ClaudeTuiLine -c Release -- --preview \
  --config /Users/jimcline/.claude/claude-tui-line.json --columns 100
```
→ `claude-tui-line: pane 2 dropped: 0 columns is under its 1-column floor at 97 columns`

Jim: *"flex doesn't seem to be working, it doesn't render stacked as if
horizontal. The second pane just disappears."*

Shape: top-level pane `split: "flex"`, `gutter: 0`, `distribute: "greedy"`, two
children, **both `size: "content"`**, each bordered.

Independent of `distribute` (identical with `"greedy"` and with it omitted) and
of width (identical at `--columns 100` and `60`).

## 2. Diagnosis — CONFIRMED

### 2.1 The mechanism

`SizeResolver.cs:395-401` — `Floor()` for a childless pane returns
`SizeKind.Content => 0`. **A content-sized leaf has `Floor` = 0.**

`SideBySideFloor` (`:416-427`) sums child floors plus boundary:
`0 + 0 + (gutter 0 × 1)` = **0**.
`StackedFloor` (`:409-410`) is the max of child floors: `max(0, 0)` = **0**.

`ResolveFlexOrientation` (`:222-238`) tests `sideBySideFloor <= outerWidth`
first: `0 <= 97` → returns side-by-side. The stacked branch at `:230` is never
evaluated.

**Confirmed live by E1** (§5.1): `SideBySideFloor=0`, `StackedFloor=0`,
`PaneSplit.Vertical` returned.

### 2.2 The scope of it

Not a mistuned threshold. For a `flex` pane whose children are all content-sized
leaves, `sideBySideFloor` is identically `0`, so case 1 fires at **every** width.
**The pane can never stack.** SPEC-88's fallback is unreachable for that entire
class of config — which is why width and `distribute` both made no difference
(`distribute` is consulted inside `ResolveVertical`, after orientation is fixed).

Everything downstream is correct: `greedy` gives child 1 its large measured
request, child 2 gets 0, the drop ladder reports accurately. **The error message
is true.** It is the symptom of a decision made two steps earlier.

### 2.3 Why no test caught it

SPEC-88 §6's V1/V2 are threshold tests and therefore use configs with non-zero
floors by construction; a content pair sits at `0/0`, outside any range a
threshold test explores.

And SPEC-88 §3.4.2's proven invariant — `sideBySideFloor ≥ stackedFloor`, *"at
every node, always"* — **holds here as `0 ≥ 0`**. It constrains the two floors'
relative order and says nothing about whether either is meaningful. Any
assertion guarding it would have passed. This is the general lesson of the
ticket: an invariant over two degenerate quantities is true and empty.

### 2.4 The codebase had already diagnosed this trap once

`SizeResolver.cs:429-431`, on `DropFloor`:

> §3(a): Floor returns 0 for content panes, so a bare grant-vs-Floor test would
> never fire for them

`DropFloor` compensates with `Math.Max(1, floor)` (`:443`). SPEC-88's
orientation predicate is a **second** bare comparison against `Floor()`-derived
values that did not get the same treatment. **The precedent's remedy does not
work here** — see §4.1.

## 3. Classification: a SPEC-88 spec-defect

SPEC-88 §3.2 ruled that `size` mode has **no** interaction at the decision point
*"because the predicate is computed over floors."* `ResolveFlexOrientation` is a
**correct implementation of §3.1 and §3.2 as written**. The contract was wrong,
not the code.

The withdrawal, with the reasoning that produced the error, is
`SPEC-88-AMENDMENT-flex-content-orientation.md` §1.

**Not a #91 or #92 regression.** Both operate in `ConfigCheck.cs` on the
width-independent `--check` path (SPEC-88 §4.4); neither is on the render path.
Present since SPEC-88 landed.

## 4. How the fix was chosen

### 4.1 Rejected — floor the content contribution at a constant

Give content leaves a non-zero floor in the predicate (e.g.
`RowLayout.MinUsableWidth + OwnBorderReserve`, the `_` branch at `:400`).

**Does not fix the bug.** Jim's `sideBySideFloor` becomes ~2 against 97 columns —
still side by side, child 2 still gets 0, error unchanged. It would only stack
on terminals narrower than roughly twice the minimum usable width.

Recorded because it is the cheapest-looking change, it is what §2.4's
`DropFloor` precedent suggests by analogy, and it produces a green diff that
does not fix the symptom.

### 4.2 Rejected — measure at cap `W`, or at `W` divided among children

This was Revision 1's own framing of the fix, and it is the formulation that
carried the fixpoint hazard: `MeasureRequest` is width-parameterised, both of
Jim's children are `overflow: "wrap"`, so measuring at a cap makes content width
depend on the width being decided. It is also near-tautological, and
self-defeating on the stacked side.

### 4.3 Rejected — single-child overflow heuristic (Revision 1 §4.3)

Its only advantage over the full comparison was cycle-avoidance, which §4.4 gets
for free. It would miss the joint-overflow case for no compensating benefit.

### 4.4 ADOPTED — measure content leaves at **no cap**

`MeasureRequest(child, null, …)` returns `UnwrappedWidth` — a pure function of
the pane's own items and the value context, reading no width, no grant, no
orientation, and never recursing into children. `overflow: "wrap"` enters only
via `LongestWrappedRowWidth(segments, cap)`, which is reached only when a cap is
passed.

**So Revision 1 §4.2's fixpoint hazard was real about the capped formulation and
did not apply to the uncapped one.** I did not distinguish those two cases when I
escalated; the distinction is what dissolved the problem, and it is why
escalating was worth doing rather than guessing.

Normative statement of the adopted predicate:
**`SPEC-88-AMENDMENT-flex-content-orientation.md` §2.**

### 4.5 One sharpening added at amendment time, not part of the ruling

The ruling said "for `size:"content"` children." The amendment scopes it to
content-sized **leaf** children (§3 of the amendment), because:

- `Floor` tests the split branch at `:376` **before** the size switch at `:395`,
  so a content-sized *split* child never reaches `Content => 0` and is not
  degenerate. The fix belongs exactly where the degeneracy is, and the branch
  order proves where that is.
- `CandidateSegments:1068` returns the **default builtins** for any pane with no
  items of its own — every container. Natural measurement on a split child would
  report the width of a statusline it never renders.

Scoping to leaves avoids that hazard rather than documenting it.

## 5. Evidence — all resolved

### 5.1 E1 — floor arithmetic. CONFIRMED.

Live run on Jim's shape at `--columns 100`: `SideBySideFloor=0`,
`StackedFloor=0`, `ResolveFlexOrientation` returned `Vertical`. §2 stands
exactly as written.

### 5.2 E2 — the 100 → 97 gap. STILL OPEN. Separate ticket.

Three columns are consumed between `--columns 100` and the root split's
`outerWidth`. **Irrelevant to this defect** — floor `0` is `≤` any width — and it
does not gate the fix. It remains unexamined and should be its own ticket rather
than absorbed into #94's diff.

### 5.3 E3 — fill children. INCONCLUSIVE AS RUN; defect confined to Content.

E3 was run at 20 columns, where the stacked floor was also 20 — so it exercised
§3.1's **third** case (neither fits → side by side), which is correct behaviour,
not a fill defect. The test could not have observed the stacked branch.

The defect is confirmed confined to `SizeKind.Content`. The regression suite must
retest fill children at a width **strictly between** `StackedFloor` and
`SideBySideFloor` (amendment §8 test 4). Recorded because "E3 passed" would be
the wrong summary — it did not test what it was meant to test.

## 6. Escalation — CLOSED

Revision 1 §6 recommended Ultra-Advisor escalation on the fix, with low stated
confidence. That was carried out and answered at high confidence; §4.4 records
the ruling and §4.5 the one place the amendment goes beyond it.

Nothing further is escalated. The remaining work is implementation.

## 7. Implementation

**Implement from `SPEC-88-AMENDMENT-flex-content-orientation.md`** — §7 is the
checklist, §8 the regression tests, §5 what must not change.

The three most likely ways to get this wrong, all called out in the amendment:

1. Making the stacked test use natural widths too "for consistency" — this
   un-fixes the bug by pushing Jim's case into §3.1's third branch (amendment
   §2.1).
2. Leaving `:233`'s note interpolating `sideBySideFloor`, so the diagnostic
   reads **"children need 0 columns"** in exactly the case being fixed
   (amendment §4).
3. Changing `Floor()`'s `Content => 0` globally — it feeds the drop ladder,
   `AllocateOnePass`, `ClampToAvail`, and the `min-rows` solver (amendment §5).

## 8. Confidence

**High** on §2 (mechanism — now also confirmed live), §2.2 (total, not a
mistuned threshold), §2.3 (why tests missed it), §3 (spec-defect; not a #91/#92
regression), §4.1's rejection, and §4.5's leaf scoping (which follows from
branch order at `:376`/`:395`, not from judgement).

**High, inherited** on §4.4's adopted fix — ruled at the escalation tier, and I
verified its load-bearing claim myself rather than accepting it: `:1048-1058`
does take the `UnwrappedWidth` path when `grantedOuterWidth` is null.

**Moderate** that E2's 3-column gap is benign. Almost certainly deliberate
reserve, but untraced — and it is the kind of small discrepancy that turns
"fits" into "does not."
