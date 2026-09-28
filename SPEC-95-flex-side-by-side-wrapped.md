# SPEC-95 — `split: "flex"` should go side by side when the children can share the width

STATUS: **REVISION 2. §5.1 AMENDED — see §5.1.1, which is normative and changes
the implementation already written on `task-95`.** Diagnosis high confidence.
§7 resolved. E1 effectively closed (§9). One implementation question remains
open (§5.1.2).

Ticket: #95. Follows #94 (`SPEC-94-flex-content-orientation.md`,
`SPEC-88-AMENDMENT-flex-content-orientation.md`, merged at `db40749`).

> **Spec path.** No path was dictated in the dispatch; the request was for "a new
> spec file (`SPEC-95-...`)". This path follows repo convention (ticket number,
> hyphenated slug, repo root). Rename freely — nothing references it yet.

> **Revision 2 changelog.** §5.1.1 added (the search floor must not override a
> declared `maxSize`) after the Implementor surfaced two failing pre-existing
> tests; §5.1.2 added; §7 marked resolved; §9 E1 closed on evidence; §10 tests 8
> and 9 added; §11 revised. Everything else is unchanged from Revision 1.

## 1. The request

After #94 merged, Jim's statusline no longer drops a pane, but it now stacks at
every realistic width. Measured by the Orchestrator sweeping `--columns`:
stacked through 245, side by side only at 250 and above. His terminal reports
80. The cause is not a defect in #94 — `sideBySideNeed` for his pane 1 is its
full unwrapped natural width, 245 columns, so `sideBySideNeed ≤ W` cannot fire
below ~247.

Jim's request, relayed: **side by side should trigger whenever the *wrapped*
content would fit, not only when the unwrapped content fits** — at 100 columns,
if pane 1 wraps to 3 rows and pane 2 to 2 rows and both fit across, he wants
that over stacking.

The Ultra-Advisor's #94 ruling anticipated this exact reopening, listing "a
product decision that a wrappable content child should prefer narrow
side-by-side wrapping over stacking" as the way its ruling could be overturned.
This is that decision arriving.

## 2. Correction to the record: Jim is on `min-rows`, not `greedy`

`SPEC-94` §1 records Jim's shape as `distribute: "greedy"`, and reports the
symptom as "identical with `greedy` and with it omitted."

**His live config today reads `"distribute": "min-rows"`**
(`/Users/jimcline/.claude/claude-tui-line.json`), with `"surface": {"maxRows":
10}`, `gutter: 0`, and two `size: "content"`, `overflow: "wrap"`, bordered leaf
children.

Whether the config changed after #94 was filed or the original record was wrong
is not determinable from here and does not matter to the design — but **§5.2
below depends on which distributor runs, so the stale value must not be carried
forward.** SPEC-94 §1 should be corrected when someone next touches it.

This correction is load-bearing for a second reason: it changes which allocator
produced the original #94 crash. See §3.3.

## 3. Diagnosis — `min-rows` cannot size content leaves, and #94 has been hiding it

**This is the finding that reorders the ticket. The predicate is not the only
thing standing between Jim and side-by-side rendering, and it is not the first
thing that has to change.**

### 3.1 `SolveMinRows` takes its search floor from `Floor()`, which is 0 here

`SizeResolver.cs:808`:

```csharp
var lo = candidates.Select(Floor).ToArray();
```

`Floor` for a content-sized leaf returns `SizeKind.Content => 0` (`:427`). This
is the **same degeneracy #94 diagnosed**, reached through a different caller.
`SPEC-88-AMENDMENT` §5 correctly forbids changing `Floor()` itself; it did not
examine `SolveMinRows`, because on Jim's shape `SolveMinRows` was unreachable
(§3.3).

`lo` is not a cosmetic default. It is:

- the **lower bound of the binary search** in `MinWidthForRowCount(candidate,
  lo, hi, t, …)` (`:829`, `:854`);
- the width at which `maxT` — the **upper bound of the entire `t` search** — is
  computed (`:815-819`);
- the **value returned when nothing is feasible** (`:847`, `return lo`);
- **and, through `:812`, a lower bound on `hi` as well** — see §5.1.1, which is
  where Revision 1 went wrong.

### 3.2 The failure chain, end to end

`SolveMinRows` searches `t = 1 … maxT` for the smallest common row count at
which every candidate's minimum width sums to `≤ r` (`:821-845`). For Jim's
pane, the answer he wants exists — something near `t = 4`, pane 1 around 60
columns, pane 2 around 20. Whether the search can *reach* it depends on `maxT`,
and `maxT` is derived from a measurement at width 0.

With a small `maxT`, the loop never reaches the feasible `t`, and `:847` returns
`lo` — all zeros. Then:

- `ResolveVerticalMinRows`'s `overAllocated` check (`:715`) does **not** fire,
  because a vector of zeros sums to well under `r`;
- `DropFloor` raises a content leaf's 0 floor to `Math.Max(1, 0) = 1` (`:470`);
- a 0-column grant against a 1-column floor trips `tooSmall`, and the last child
  is dropped.

The resulting message is `pane 2 dropped: 0 columns is under its 1-column floor
at 97 columns` — **the message reported in #94, reproduced exactly, including
the literal `0 columns`.**

### 3.3 This is a better explanation of #94's original crash than SPEC-94 §4 gives

`SPEC-94` attributed the drop to greedy's first-come-first-served allocation.
That mechanism is real and is correctly described for `greedy` — but Jim is not
on greedy (§2), so it is not what he hit.

Both paths produce a 0-column grant, which is why the diagnosis held together
against the observed message. They are different defects and only one is Jim's.

**The consequence for this ticket is the important part.** #94 made the pane
stack, which routes it to the *horizontal* resolver — `min-rows` is a
vertical-axis distributor and never runs. The crash did not get fixed; it got
made unreachable. **Restoring side-by-side rendering without §5.1 restores the
crash.**

### 3.4 Why no test caught it

`Floor` returns a non-degenerate `RowLayout.MinUsableWidth + OwnBorderReserve`
for fill and percent panes (`:427`, the `_` branch) and the declared value for
fixed panes. **Only content leaves reach `0`.** A `min-rows` test written with
fill children — the natural way to test a width distributor — has a well-formed
`lo`, a meaningful `maxT`, and a working binary search.

Revision 1 predicted the combination `min-rows` + content leaves was untested.
That prediction was **half right**, and the half it got wrong matters: see
§5.1.1. Existing tests do exercise content leaves under min-rows; what they do
not do is exercise them *with a well-formed floor*, because until now none
existed.

## 4. "Whenever the wrapped content fits" is vacuous as literally stated

Under `overflow: "wrap"`, `PaneRenderer.RenderLeaf` (`PaneRenderer.cs:40-42`)
hard-breaks any segment wider than the pane using
`SegmentTruncation.WrapToWidth`, which cuts **at character boundaries**
(`SegmentTruncation.cs:38`, `:42`). Row breaks between segments are `RowLayout`'s
job and happen at any width (`RowLayout.cs:120-130`).

**So wrapped content "fits" at any width down to about one column.** There is no
longest-unbreakable-unit and no function computing one. A predicate of the form
"side by side iff the wrapped content fits" selects side by side *always* —
rendering two ragged towers at narrow widths. That is the mirror image of the
current complaint.

Something must therefore decide **how narrow is too narrow**, and columns of
content cannot be that something. §5.2 derives the answer from an existing
constant rather than inventing a threshold; §7 records Jim's choice.

Related correction: `overflow` does **not** gate whether content wraps across
rows. Per `PaneRenderer.cs:5`, overflow modes govern only a single segment wider
than the pane's own inner width. The pane-tree default is `Truncate`, not `Wrap`
(`PaneAssembler.cs:204-205`) — including for an explicit `"overflow"`. Jim sets
`"wrap"` on both children, so his case is unaffected, but any rule reasoning
"narrower ⇒ more rows" holds **only** for panes that opted into `wrap`.

## 5. Design

### 5.0 Ruling: framework §2.8.1 does not prohibit this

`SPEC-V2-FRAMEWORK.md:1660` ("There is no height fixpoint, and there must not be
one") was raised as a possible blocker, on the reading that a row count may not
inform a width decision. **It does not carry that meaning, and this spec
proceeds on that ruling.** Grounds:

1. The section forbids a **fixpoint** — iterating width against height to
   convergence. Its entire argument is non-convergence.
2. Its remaining requirements constrain the **degrade ladder**. All are about
   `HeightLadder`, which this spec does not touch.
3. `SolveMinRows` **already** derives widths from row counts during width
   resolution, at `:818` and `:829`. If §2.8.1 forbade that, `min-rows` would be
   in breach of it today.
4. SPEC-88 §4.2's restatement is specifically that *the ladder's outcome* never
   feeds back. Nothing here consults the ladder, which runs strictly afterwards.

**If a Reviewer reads §2.8.1 otherwise, that is a spec-defect for escalation,
not a code change.**

### 5.1 REQUIRED FIRST — a non-degenerate search floor in `SolveMinRows`

Nothing else in this spec is safe to ship without this.

`SolveMinRows:808` must not use a floor of `0`. Introduce a search floor local
to the min-rows solver:

```
searchFloor(c) = Math.Max(Floor(c, collapse:false, false, false),
                          RowLayout.MinUsableWidth + OwnBorderReserve(c))
                   if c is a CONTENT-SIZED LEAF
               = Floor(c, collapse:false, false, false)      otherwise
```

**subject to §5.1.1, which overrides this in the `maxSize` case.**

Use the same content-sized-leaf test `SPEC-88-AMENDMENT` §3.3 fixes — the size
half via `SizeResolver.IsContentSized(pane)`, the leaf half via the same
`Split == None || Children.Count == 0` form `Floor` applies.

Rationale:

- `RowLayout.MinUsableWidth` (20, `RowLayout.cs:17`) is **already** the floor
  `Floor()` gives fill and percent panes (`:427`). This gives content leaves the
  same treatment inside the solver, rather than introducing a new constant.
- It makes `maxT` (`:818`) a row count at a width the renderer can render.
- It makes `MinWidthForRowCount`'s binary search well-founded at its lower end.

**`Floor()` itself must not change** — `SPEC-88-AMENDMENT` §5 stands. This is a
solver-local floor with a distinct name, not a redefinition.

### 5.1.1 NORMATIVE (Revision 2) — the search floor must never override `maxSize`

**Revision 1 was wrong by omission here, and the resulting implementation
silently defeats a user-facing config key.**

`SolveMinRows:812` computes the upper bound as:

```csharp
hi[ci] = Math.Max(Math.Min(candidates[ci].MaxSize ?? r, r), lo[ci]);
```

The outer `Math.Max(…, lo[ci])` means **raising `lo` above a declared `maxSize`
raises `hi` to match.** A pane declared `"maxSize": 1` and given a
`searchFloor` of 20 is searched, and can be granted, at 20 columns. The author's
explicit ceiling is discarded in favour of a default the author never wrote.

`MinUsableWidth` is a heuristic default; `maxSize` is an explicit declaration.
**A default must never override an explicit declaration.** This is not a
close call and it is not a taste question.

**The rule:**

```
searchFloor(c) = Floor(c, …)                                  -- unchanged, pre-fix behaviour
                   if c.MaxSize is int m
                      ∧ m < RowLayout.MinUsableWidth + OwnBorderReserve(c)

               = Math.Max(Floor(c, …),
                          RowLayout.MinUsableWidth + OwnBorderReserve(c))
                   if c is a CONTENT-SIZED LEAF

               = Floor(c, …)                                  -- otherwise
```

Read in that order; the `maxSize` clause is tested **first** and wins.

Where an author has capped a pane below the renderer's own usable minimum, the
solver leaves the candidate exactly as it behaved before this ticket. Such a
pane cannot render usefully at its declared cap, the existing over-constrained
path drops it with a note, and **that outcome is a pre-existing contract this
ticket does not revisit.** Whether capping a pane below `MinUsableWidth` should
warn at `--check` is a reasonable follow-up and is **not** in scope here.

**Consequence, and the reason this section exists.**
`MinRowsDropNoteTests.MinRows_OverConstrainedThreeChildSplit_EmitsPaneDroppedNotesAndDropsToOneChild`
sets `"maxSize": 1` on all three children and documents, at its own lines 13-16,
that this is what forces the solve infeasible "at every T it tries." **That test
is about drop-note emission on the min-rows path (framework §9.8.2), not about
the content-floor degeneracy.** Under this section it must pass **unchanged**,
with `lo = 0`, `hi = 1`, and both drop notes intact.

If it does not pass unchanged, §5.1.1 has been implemented wrongly. **Do not
update that test's expectations.** Doing so would delete a regression test for
one defect while shipping another.

### 5.1.2 OPEN — `MinRowsDistributeTests.OverConstrained_MinRows_EmitsDroppedPaneNote`

This test is a different case from the one above and is **not** resolved by
§5.1.1.

It is two `"size": "500"` fixed children plus a trailing content leaf, and its
comment block (lines 318-330) is a considered contract citing framework
§2.3.3:1220-1222, `SPEC-2.3.1-min-rows-floor-sum.md` §2/§4, and #67 — including
the sentence *"Pre-#67 this test asserted 2 survivors at a silently
over-allocated width; that was the bug, not the contract."* It has been revised
once already, deliberately.

Its comment also asserts that the content candidate bottoms out at width 0 *"on
both the feasible path and the over-constrained fallback."* §5.1 makes that
false whenever the leaf carries no restrictive `maxSize`.

**Required before this test's expectations are touched:**

1. Re-run it under §5.1.1. If the leaf carries a `maxSize` below the search
   floor, it may pass unchanged and there is nothing to decide.
2. If it still fails, the update must be justified against
   `SPEC-2.3.1-min-rows-floor-sum.md` §2/§4 — **the drop cascade must be shown
   to follow from that spec's rules with the leaf's new floor substituted in,
   not from observed output.** Every note must correspond to a real drop.
3. **The surviving child count must be stated explicitly and must not change**
   without a separate ruling. The note text and count following from a changed
   floor is expected; a different number of surviving panes is a different claim
   and needs one.
4. The test's comment must be amended to say the leaf no longer bottoms out at 0
   and why. A stale comment asserting the old mechanism is worse than none.

**If step 2 cannot be discharged from the cited spec, stop and escalate rather
than updating the assertion.** That is the case where a real regression would
otherwise be laundered into a green suite.

### 5.1.3 Ruling: `SearchFloor` is correctly scoped to all of `SolveMinRows`

The alternative raised — restricting the new floor to the flex orientation call
sites and leaving declared-vertical min-rows resolves untouched — is
**rejected**, on three grounds, independent of how §5.1.2 resolves:

1. **It gives one function two behaviours keyed on its caller.** `SolveMinRows`
   would allocate differently depending on who invoked it. One implementation
   per behaviour is a first-class rule here, not a style preference.
2. **It breaks the identity §5.3's soundness argument rests on.** §5.2's
   predicate asks the allocator whether it can allocate, and the design is
   defensible precisely because the decision and the allocation *are the same
   computation*. Scope the floor to the predicate and a flex pane containing a
   declared-vertical min-rows descendant has its ancestor's decision computed
   under one floor and the descendant's allocation under another. Two models
   that can drift is exactly what §5.3 claims this design does not have.
3. **It would preserve a defect because a test encodes it.** Subject to §5.1.1
   and §5.1.2 — which say two specific tests are *not* merely encoding it — the
   general principle holds: a degenerate floor inside a width solver is not a
   contract anyone chose.

Supporting evidence that the broadened scope does not degrade allocations:
`Columns112_MinRows_MatchesBruteForceOptimalRowCount` and
`Columns60_MinRows_MatchesBruteForceOptimalRowCount` assert that the allocation
equals the exhaustive brute-force optimum, and the shared fixture they use has a
content child that floors at 0 pre-fix (noted at `MinRowsDistributeTests:291`).
**Both pass post-fix.** That is independent evidence — not a re-check of the code
under test — that the new floor does not make min-rows allocations worse.

### 5.2 The orientation predicate becomes distribute-aware

```
effective(p), for declared Split == Flex at outer width W:

  distribute == Greedy or Even:
      UNCHANGED from SPEC-88-AMENDMENT §2.

  distribute == MinRows:
      sideBySideNeed ≤ W            → Vertical      (unchanged fast path)
      else if minRowsFeasible(p, W) → Vertical      (NEW)
      else if stackedFloor ≤ W      → Horizontal
      otherwise                     → Vertical
```

where `minRowsFeasible(p, W)` is **`SolveMinRows` reaching its `:840` feasible
branch** rather than falling through to `:847`.

**Greedy and even are deliberately left alone.** Neither can shrink a child, so
a child wanting 245 columns beside a sibling genuinely does not fit under those
modes and stacking is correct. This is their contract, not a defect.

### 5.3 Why this is cycle-free

The decision does not measure a wrapped width. It asks the **actual allocator
that is about to run** whether it can allocate `W`:

```
W, per-child items ─→ searchFloor ─→ maxT ─→ MinWidthForRowCount(·, t) ─→ feasible? ─→ orientation
```

Every arrow points forward. Nothing reads the orientation being decided, and
nothing is revisited. The property that makes this sound is stronger than
non-circularity: **the decision and the allocation are the same computation.**
There is no separately maintained model that could drift. That is also why no
proportional-`Share` function is specified anywhere in this document.

### 5.4 Cost, and the test seam that will notice it

`RowCountAt` is a real packer invocation, counted by
`MinRowsPackerInvocationCount` (`:675`) precisely so tests can assert cost.
Calling `SolveMinRows` from the predicate and again during allocation would
roughly double it for flex panes on the min-rows path.

**(a) Cache the solve** — compute once in `ResolveFlexOrientation`, hand the
result to `ResolveVerticalMinRows`. Preferred, and also the stronger guarantee:
a cached solution cannot differ from the one the decision was made on.

**(b)** Accept the doubling and update the affected cost assertions, stating the
new expected counts.

Existing cost assertions must not be relaxed to whatever the new code produces.

## 6. Rejected

### 6.1 Rejected — a proportional shrink-to-fit allocator

Rejected on three grounds: it needs a minimum width per child, which §4 shows
does not exist; it would be a second allocation rule the predicate and the
renderer must both implement identically forever; and `min-rows` already is a
shrink-to-fit allocator that equalizes row counts rather than column ratios,
which is what a reader perceives.

### 6.2 Rejected — implementing the request literally

Vacuous (§4).

### 6.3 Rejected — making `greedy` shrink

Greedy's first-come-first-served allocation is its contract, and its drop is
unrecoverable by construction: the truncation at `:624` precedes the re-measure
loop at `:285-313`. Separate, larger ticket.

### 6.4 Rejected — bounding feasibility by `surface.maxRows`

The row budget belongs to the height ladder, which runs strictly after width
resolution (SPEC-88 §4.2). §5.2's bound is self-contained.

### 6.5 Rejected — scoping the new floor to flex only

See §5.1.3.

## 7. RESOLVED — the acceptability criterion

**Confirmed by Jim (relayed): option (a), min-rows feasibility — already what
§5.2 specifies.** No design change followed from the answer.

Under §5.1's floor, "feasible side by side" means roughly *every pane gets at
least `MinUsableWidth` (20) plus its own border*.

| | 80 cols | 100 cols | 40 cols |
|---|---|---|---|
| **(a) min-rows feasibility** — ADOPTED | side by side | side by side | stacked |
| **(b) row comparison** — fallback, see below | side by side | side by side | stacked |
| **(c) literal request** — declined | side by side | side by side | side by side, two towers |

**(b) is not rejected — it is retained as the fallback** if a width turns up
where (a) selects side by side and the result is visibly worse than stacking. It
costs `n` extra `RowCountAt` calls to compute `Σ RowCountAt(childᵢ, W)`. §9's E2
was the check for this and came back clean (§9), so (a) stands.

**Secondary question, deferred by the Orchestrator:** should a `flex` pane on
`greedy` or `even` warn that #95 changes nothing for it? Discoverability, not
correctness. Not in scope.

## 8. What must NOT change

- **`Floor()` — no change at any line.** `SizeKind.Content => 0` at `:427`
  stays. §5.1 is deliberately a solver-local floor with a different name.
- **A declared `maxSize`.** §5.1.1.
- **`SPEC-88-AMENDMENT`'s predicate for greedy and even.**
- **The asymmetry in amendment §2.1** — side by side uses natural widths,
  stacked keeps floors.
- **`HeightLadder`.** Not read, not written, not reordered.
- **`ResolveVerticalMinRows`'s drop-retry structure** (`:681-740`) and
  `AllocateMinRowsOnePass`'s step order.
- **`WaterFillSurplus`** (`:911-951`) and its surplus-only contract.
- **The render note's firing condition and template** (SPEC-88 §5, framework
  `:6148`), including amendment §4's fix interpolating `sideBySideNeed`.

## 9. Evidence

### E1 — `RowCountAt(pane, 0)`. CLOSED on evidence, not measured directly.

Revision 1 flagged a possible throw or non-termination at width 0, to be routed
ahead of this ticket.

**It neither throws nor hangs.** The Implementor ran the pre-fix path under a
stash (report item 5) and both pre-existing min-rows tests completed pre-fix
producing real note output. The degenerate path resolves as §3.2 describes:
`maxT` comes out small, the `t` search fails, and `:847` returns the zero
vector.

This closes E1's third branch — the one that would have jumped the queue. **The
exact returned row count was never reported and is not recorded here.** If
anyone wants it pinned, it is a one-line assertion; nothing in this spec depends
on the value.

### E2 — Jim's shape after §5.1 + §5.2. CONFIRMED.

| Width | Orientation | Pane widths | Dropped | Fits `maxRows: 10` |
|---|---|---|---|---|
| 80 | side by side | 35 / 42 | nothing | yes |
| 100 | side by side | 49 / 48 | nothing | yes |
| 120 | side by side | 59 / 58 | nothing | yes |
| 40 | stacked | 37 each | nothing | exactly 10 |
| 24 | stacked | 21 each | nothing | exactly 10 |

Meets the bar: 80 and 100 side by side with both panes present; 40 and 24 stack
without squeezing below `MinUsableWidth`.

**Note for readers of the 24-column row.** A segment is truncated there despite
`overflow: "wrap"`, and both 40 and 24 land on *exactly* 10 rows. That is the
height ladder's rung 2 demoting wrap→truncate against `maxRows: 10`
(framework §2.8.1), working as designed. It is not evidence that wrapping is
broken.

**E2 must be re-run after §5.1.1 lands**, since §5.1.1 changes `searchFloor`.
Jim's children declare no `maxSize`, so the table above is expected to be
unchanged — but "expected" is not "verified."

## 10. Regression tests

1. **Jim's shape at 80 columns.** Side by side, both panes present, nothing
   dropped, each pane ≥ `MinUsableWidth`.
2. **The #94 crash does not return.** No `pane N dropped` note at 80, 97, 100.
3. **`min-rows` sizes content leaves at all.** Direct `SolveMinRows` test with
   two content leaves and a width admitting a feasible `t > 1`: assert the
   feasible allocation, not the `:847` fallback. Must fail before the fix —
   verified by the Implementor via stash (report item 5).
4. **Narrow widths still stack.** Below twice `MinUsableWidth` plus borders.
5. **Greedy and even unchanged.** Same shape under both, several widths.
6. **`SPEC-88-AMENDMENT` §8's tests 1–6 pass**, with test 1 re-pinned to
   `distribute: "greedy"` — not deleted.
7. **Packer cost.** Flex min-rows resolve must not double relative to a
   declared-vertical min-rows resolve of the same tree.
8. **NEW (§5.1.1) — `maxSize` survives the floor.** A content leaf with
   `maxSize` below `MinUsableWidth + OwnBorderReserve` must never be searched or
   granted above its `maxSize`. Assert directly on the grant, not only via the
   drop note, so the guarantee does not rest on a message string.
   `MinRowsDropNoteTests` passing unchanged is necessary but **not** sufficient
   for this — that test observes notes, not widths.
9. **NEW (§5.1.3) — plain declared-vertical min-rows sizes content leaves.** A
   `"split": "vertical"`, `distribute: "min-rows"` pane with content leaves and
   no `maxSize` grants them non-zero widths. Without this, §5.1's effect outside
   flex has only negatively-updated coverage and no positive assertion.

## 11. Confidence, and what I got wrong

**High** on §4, §2, §5.3, and §5.1.3's ruling.

**High** on §5.0's §2.8.1 ruling. Recorded as a ruling rather than an assumption
because it was raised as a blocker and will be raised again.

**High** on §5.1.1. `:812`'s `Math.Max(…, lo[ci])` makes the override
mechanical, and the affected test documents its own use of `maxSize` as the
infeasibility mechanism at its lines 13-16.

**Moderate-to-high** on §3. The failure chain fits the reported error exactly,
including the literal `0 columns`, and E1 has since closed its riskiest branch.

**Moderate** on §5.1's specific floor value. That content leaves need a
non-degenerate search floor is not in doubt; that `MinUsableWidth +
OwnBorderReserve` is the right one is a judgement, defensible because it is what
fill and percent panes already get. It sets the width below which Jim's panes
stack, so it is visible to him.

**Open:** §5.1.2.

### What Revision 1 got wrong

§5.1 specified a floor without checking what else consumed it. `:812` derives
`hi` from `lo`, so the floor silently became a ceiling override. Revision 1's
§3.4 then compounded it by asserting the min-rows + content-leaf combination was
untested — it is tested, by
`MinRowsDropNoteTests.MinRows_OverConstrainedThreeChildSplit_...`, which reaches
infeasibility through `maxSize` rather than through the degeneracy. Revision 1
predicted a green field and there was a test standing in it.

**The general lesson, and the reason this is recorded rather than quietly
fixed:** the two failing tests were reported as "pinning the old degenerate
behaviour," and for one of them that reading was wrong. Had their expectations
been updated to match observed output, the result would have been a green suite,
a deleted drop-note regression test, and a silently broken `maxSize`. **When a
fix makes existing tests fail, the tests' own comments are evidence about intent
and must be read before their assertions are changed.**

I did not escalate the design. #94's escalation was warranted because a genuine
fixpoint hazard was unresolved; here it dissolves structurally (§5.3), and what
remained was empirical (§9) or Jim's to settle (§7). §5.1.1 was a specification
error found by review, which is the mechanism working rather than a case for
escalation.
