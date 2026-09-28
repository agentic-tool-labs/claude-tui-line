# SPEC-88 AMENDMENT — the flex orientation predicate for content-sized children

STATUS: **NORMATIVE. Replaces SPEC-88 §3.2 in full and restates §3.1's
predicate.** Ruled by Ultra-Advisor escalation from `SPEC-94` §6 (high
confidence), with one scoping sharpening added here (§3.3) that the ruling did
not make and that the branch order in `SizeResolver.cs` requires.

Ticket: #94. Defect and diagnosis: `SPEC-94-flex-content-orientation.md`.

> **Why a separate file rather than an in-place edit of SPEC-88.** The Architect
> role has no `Edit` tool, and rewriting a ~1050-line spec wholesale to change
> two sections risks silently dropping content. The repo already uses this
> pattern (`SPEC-91-amendment-A2-v13c.md`,
> `SPEC-85-ADDENDUM-spans-threading.md`). **The Implementor should add a
> one-line pointer to this file at SPEC-88 §3.1 and §3.2** so the amendment is
> discoverable from the spec it amends — see §7.

## 1. What was wrong

SPEC-88 §3.2 currently reads, in full:

> ### 3.2 Interaction with `size: "content" | "fill"`
>
> **None at the decision point** — the predicate is computed over **floors**,
> and a child's floor is defined by §2.3 regardless of its `size` mode. Once the
> effective orientation is chosen, allocation proceeds by the existing rules for
> that orientation.
>
> The consequence is intended: `size` names *"share of the PARENT's split
> axis"* (framework `:756`), so the effective orientation determines which axis
> a child's `size` governs — identical to what the author would get by declaring
> that orientation outright.

**The first paragraph is withdrawn. It is false, and the reasoning that produced
it is the part worth understanding.**

The argument was: the predicate compares floors; floors are defined uniformly by
§2.3; therefore `size` mode cannot affect the comparison. Every step is true and
the conclusion does not follow, because it treats "defined uniformly" as
"meaningful uniformly." `Floor()` **is** defined for a content-sized leaf — it is
defined to be `0` (`SizeResolver.cs:399`). A uniform definition that returns a
degenerate value for one input class makes the predicate degenerate for that
class rather than making it uniform.

The consequence: for a `flex` pane whose children are all content-sized leaves,
`SideBySideFloor` is identically `0`, so §3.1's case 1 (`sideBySideFloor ≤ W`)
fires at **every** width and the stacked branch is unreachable. `flex` could
never stack for that shape — at 100 columns, at 60, at 1. Confirmed live
(`SPEC-94` §5 E1: `SideBySideFloor=0`, `StackedFloor=0`, `Vertical` returned).

**The second paragraph of §3.2 stands unchanged** — `size` naming the share of
the parent's split axis, and the effective orientation determining which axis it
governs, is correct and is not affected by this amendment.

Note that §3.4.2's proven invariant `sideBySideFloor ≥ stackedFloor` holds
throughout this defect as `0 ≥ 0`. The invariant is true; it constrains the two
floors' relative order and says nothing about whether either is meaningful. No
assertion guarding it would have fired. This is why the defect shipped green.

## 2. The replacement predicate

For a pane `p` with declared `Split == Flex`, available outer width `W`, and
`p`'s own `collapse` / `excludeLeft` / `excludeRight`:

```
need(childᵢ) = MeasureRequest(childᵢ, null, ctx, values, compounds,
                              excludeLeft: collapse ∧ i > 0,
                              excludeRight: collapse ∧ i < n-1)
                 if childᵢ is a CONTENT-SIZED LEAF          (§3.3)

             = Floor(childᵢ, collapse,
                     excludeLeft: collapse ∧ i > 0,
                     excludeRight: collapse ∧ i < n-1)
                 otherwise

sideBySideNeed(p) = Σᵢ need(childᵢ) + boundary
                    where boundary is SideBySideFloor's existing expression:
                    collapse ? max(0, n-1) : p.Gutter × max(0, n-1)

stackedFloor(p)   = max Floor(childᵢ, collapse, false, false)     -- UNCHANGED

effective(p) = Vertical    if sideBySideNeed ≤ W
             = Horizontal  if sideBySideNeed > W  ∧  stackedFloor ≤ W
             = Vertical    otherwise
```

Only the side-by-side quantity changes. The three-case structure, the
third-case ruling (neither fits → side by side, so the drop ladder runs), and
`stackedFloor` are all exactly as SPEC-88 §3.1 already states them.

### 2.1 The asymmetry is load-bearing — do not "make it consistent"

**The side-by-side test uses natural widths. The stacked test keeps floors.**
This looks like an inconsistency and it is the substance of the fix.

Side by side, a child must fit *beside* its siblings, so the question is how
much width it actually wants: its natural unwrapped content width. Stacked, a
child gets the full width and may wrap freely down the rows it needs, so the
question is only whether it can survive at all: its floor.

Jim's child 1 has a natural width well above 97 columns and wraps perfectly well
when given all 97. Using natural width on **both** sides would make
`stackedNeed > W` too, pushing the case into §3.1's third branch (neither fits →
side by side) and **un-fixing the bug it was introduced to fix**. Recorded
explicitly because "the two branches should use the same measure" is an obvious
and wrong simplification.

### 2.2 Why measuring at `null` is cycle-free

This was `SPEC-94` §4.2's stated hazard and the reason for escalation. It does
not apply to this formulation, and the reason is specific rather than general.

`MeasureRequest(pane, null, …)` (`SizeResolver.cs:1048-1053`) sets
`innerCap = null`, so `MeasureInnerContentWidth` (`:1058`) returns
`UnwrappedWidth(segments)` (`:1091-1099`) — `Σ Plain.Length + SeparatorWidth ×
(n-1)` over `CandidateSegments`, plus the pane's own border reserve. That is a
pure function of the pane's own resolved items and the value context. It does
not read `W`, a grant, or an orientation, and it does not recurse into children.

`overflow: "wrap"` is irrelevant here: wrapping only enters via
`LongestWrappedRowWidth(segments, cap)` (`:1106`), which is reached **only** when
a cap is passed. The width-dependent measurement that made `SPEC-94` §4.2
hazardous is exactly the capped path, and this predicate never takes it.

So the decision reads `(constants, W)` once. There is no fixpoint to protect and
no §2.8.1 revert.

**Explicitly rejected: measuring at cap `W`, or at `W` divided among the
children.** That is the formulation `SPEC-94` §4.2 correctly identified as
circular. It is also near-tautological — measuring a child at the width you are
deciding whether to give it — and self-defeating on the stacked side. It is
recorded here as rejected because it is the reading someone arrives at by
assuming a measurement "obviously" needs a width.

This is also the same call `ResolveVertical`'s first pass already makes
(`Measure(c, null, …)`, `:252`), so the orientation decision measures exactly
what allocation would measure a moment later.

## 3. Scope: content-sized **leaves** only — sharpening the ruling

The escalation ruling said "for `size:"content"` children." **This amendment
narrows that to content-sized children with no split of their own**, and the
narrowing is not a preference — the code requires it in both directions.

### 3.1 A content-sized split child is not degenerate and needs no change

`Floor()` tests `p.Split != PaneSplit.None && p.Children.Count > 0` at `:376`,
**before** reaching the `ClassifySize(p.Size)` switch at `:395`. So a pane with
children never reaches `SizeKind.Content => 0`; it returns
`StackedFloor` / `SideBySideFloor` / their `min` instead.

**The degenerate `0` is therefore reachable only for a content-sized leaf**, and
that is provable from the branch order rather than inferred from examples. The
fix should apply exactly where the degeneracy is.

### 3.2 Applying natural measurement to a split child would be actively wrong

`CandidateSegments` (`:1066-1087`) returns `SegmentBuilder.Build(ctx)` — **the
default builtin segment list** — whenever `pane.Items.Count == 0`, which is true
of every container pane. It never recurses into children.

So `MeasureRequest(splitChild, null, …)` reports the width of a default
statusline that pane will never render. Feeding that into an orientation
decision would produce a number with no relationship to the pane's actual
content.

This behaviour is **pre-existing** — content sizing already measures children
this way during allocation — and this amendment does not change it. But it would
newly route an *orientation* decision through it, so §3.3's predicate avoids it
rather than documenting it.

### 3.3 The rule

> `need(child)` uses `MeasureRequest(child, null, …)` **iff**
> `ClassifySize(child.Size).Kind == SizeKind.Content` **and** `child` has no
> split of its own (`child.Split == PaneSplit.None || child.Children.Count == 0`
> — the same test `Floor` applies at `:376`, in the same form, so the two cannot
> drift). Otherwise `need(child)` is `Floor(child, …)`, unchanged.

Use `SizeResolver.IsContentSized(pane)` (`:90`) for the size half rather than
restating `ClassifySize(...).Kind == SizeKind.Content`.

**Known gap, deliberately not closed here:** a `flex` pane whose children are
content-sized *splits* still decides orientation on their floors, which is
correct-but-conservative — their floors are meaningful, so the predicate is not
degenerate, but a nested content split's natural width is not consulted. No
reported symptom, and closing it requires deciding what a container's natural
width even means (§3.2 shows the current answer is "the default builtins,"
which is not it). Follow-up ticket, not this one. **Do not close it
opportunistically inside #94's diff.**

## 4. The render note must interpolate the new quantity

SPEC-88 §5 and framework `:6148` specify:

```
pane {N}: flex split stacked; children need {X} columns at {Y} columns
```

`SizeResolver.cs:233` currently interpolates `sideBySideFloor` as `{X}`. **`{X}`
must become `sideBySideNeed`** — the §2 quantity.

Left as-is, the note would read **"children need 0 columns"** in precisely the
case this amendment exists to fix, since `sideBySideFloor` is `0` for exactly
the shape that now stacks. A diagnostic that reports `0` as the reason for
stacking is worse than no diagnostic.

Firing condition, template, and the rule that no note fires for side-by-side or
for declared `vertical`/`horizontal` splits are **unchanged** (SPEC-88 §5).

## 5. What does NOT change

- **`Floor()` itself — no change at any line.** `SizeKind.Content => 0` at
  `:399` stays. It feeds the drop ladder, `AllocateOnePass`, `ClampToAvail`, and
  the `min-rows` solver; a global change there is a far larger blast radius than
  this ticket and would alter allocation everywhere.
- **`Floor(flexPane)` — SPEC-88 §3.4's `min(sideBySideFloor, stackedFloor)`
  contract — unchanged.** A pane's floor is what an ancestor must reserve for it
  to survive, which is a different question from which orientation it prefers.
  §3.4.2's invariant and its proof are untouched.
- **The decision point.** `ResolveNode:189-191`, still above the `distribute`
  dispatch, still once per resolution. SPEC-88 §4.1's reason (so `min-rows` sees
  the same decision greedy does) and §4.2's ordering against the height ladder
  both hold. Every argument needed (`ctx`, `values`, `compounds`,
  `measureOverride`) is already in scope at `ResolveNode:177`, so this is a
  **signature extension of `ResolveFlexOrientation`, not a restructuring.**
- **`DropFloor`'s `Math.Max(1, …)`** (`:443`) is not the fix and must not be
  repurposed as one — it yields ~2 against 97 columns and still never stacks
  (`SPEC-94` §4.1).
- **`SideBySideFloor` and `StackedFloor` keep their current contracts and
  callers.** `Floor` and §3.4 still need them as they are; the new quantity is
  additional, not a replacement. Do not redefine `SideBySideFloor` in place.
- SPEC-88 §3.2's **second** paragraph (§1 above), §3.1's three-case structure,
  and §3.3's `gutter`/`distribute` ruling.

## 6. `measureOverride` must be honoured

`ResolveVertical` wraps measurement in a local closure that prefers
`measureOverride` when supplied (`:246-252`), because §10.6's fixpoint stubs are
position-blind by design. The orientation predicate must use **the same
convention** — a test that stubs measurement must see its stub drive the
orientation decision too, or V-series tests will exercise a different
measurement path than production.

`MeasureRequest` does not itself consult `measureOverride`; the caller does.
Thread it the same way `ResolveVertical` does rather than calling
`MeasureRequest` directly and bypassing it.

## 7. Implementation checklist

1. Extend `ResolveFlexOrientation`'s signature with `ctx`, `values`,
   `compounds`, `measureOverride`; pass them from `ResolveNode:190`.
2. Add the `sideBySideNeed` computation per §2, scoped per §3.3, honouring
   `measureOverride` per §6.
3. Change `:233`'s interpolation to `sideBySideNeed` (§4).
4. Add a one-line pointer to this file at SPEC-88 §3.1 and §3.2, and mark §3.2's
   first paragraph withdrawn. Do not delete it — §1 explains why the reasoning
   is worth keeping visible.
5. Update framework `:6148`'s surrounding text only if it describes `{X}` as a
   floor; the note template itself is unchanged.

## 8. Regression tests

1. **Jim's shape.** `flex`, `gutter: 0`, two `size: "content"` bordered leaf
   children whose combined natural width exceeds `W`. Assert it renders
   **stacked**, both panes present, nothing dropped. This is the reported bug and
   it must be a committed test, not a manual check.
2. **The note reports a real number.** Same config: assert the `flex split
   stacked` note fires and that `{X}` is the actual side-by-side need — assert it
   is **non-zero**, which is what catches a regression to `sideBySideFloor`.
3. **Still side by side when it genuinely fits.** Two content leaves whose
   combined natural width is under `W`. Byte-identical to declaring `vertical`.
4. **Fill children at a width strictly between `StackedFloor` and
   `SideBySideFloor`.** `SPEC-94` E3 was run at a width where both floors were
   equal, so it exercised §3.1's third case rather than the stacked branch and
   was inconclusive by construction. The retest must pick a width strictly
   inside the gap.
5. **Content-sized split child.** A `flex` pane with a content-sized child that
   has its own children: assert the orientation decision uses its floor and does
   **not** invoke natural measurement on it (§3.2's default-builtins hazard).
6. SPEC-88 §6's existing V1–V6 continue to pass unmodified.
