# SPEC-97 — a stacked child is sized by its own `size`/`minSize`/`maxSize`, not given the full width unconditionally

STATUS: **READY TO IMPLEMENT, GATED ON ONE FRAMEWORK AMENDMENT (section 3) AND ON SPEC-96 MERGING FIRST.**
**Recommend Ultra-Advisor review of the section 3 amendment before it lands** — reasons and the
exact question in section 11. The code design (sections 4–6) is high confidence and is not what
I am escalating.

Ticket: #97. Written against `/Users/jimcline/git/repos/claude-tui-line`, branch `main`
@ `1f938b5`. Follows `SPEC-96-stacked-child-width-reserve.md`.

> **Citations are anchored by commit and quoted by content.** Per SPEC-92's standing warning:
> **do not target an edit below by line number alone — match the quoted text.** This document is
> itself an instance of why: see section 1.2.

---

## 0. Where this came from, and what changed during design

`SPEC-96` §8.1 presented three options and recommended **(A)**, fix the overflow only. **Jim chose
(B): implement `size`/`minSize`/`maxSize` for stacked children.** This spec is that work.

**SPEC-96 is NOT superseded and must merge first.** Its one-line reserve fix is a prerequisite:
B allocates *within* a stacked split's inner width, and SPEC-96 is what makes that inner width
correct. B subsumes A's arithmetic rather than replacing it. SPEC-96 also fixes a live, visible
bug in Jim's daily driver; folding it into this feature would gate a one-line fix behind a
framework amendment. **SPEC-96 §8.1 should get a line recording the decision and pointing here** —
a docs-only edit, no reasoning attached.

Two findings during design moved B substantially in both directions. Both are load-bearing and
neither was visible from the dispatch:

- **The dangerous part evaporated (section 5).** `StackedFloor` needs no change, so the flex
  orientation chain is untouched.
- **The easy framing was wrong (section 1).** B is not "implement a section nobody got to." It is
  **a change to a normative rule the code currently obeys.**

---

## 1. B contradicts the framework as written

### 1.1 The current rule is explicit

`SPEC-V2-FRAMEWORK.md` §2.3.2, *"Keys that are valid, spelled right, and meaningless where they
are written"*:

> `distribute` divides extent among siblings that sit side by side. A **horizontal** split does not
> divide extent — its children each span the full width and stack downward — so there is nothing for
> the policy to choose.

**"its children each span the full width"** is the rule B changes. The resolver's stacked branch
implements it faithfully. So — modulo SPEC-96's border-reserve defect, which is a genuine bug
against this same rule — **the code is not lagging the framework here. It agrees with it.**

Per SPEC-92's precedent, quoted directly:

> **The framework is internally inconsistent, and the code faithfully implements the wrong half of
> it. Fix the framework first, then the code.**

The same discipline applies with the same force when the framework is *consistent* and we have
decided to change it. **Section 3's amendment lands before any code in section 4.**

### 1.2 A wrong citation misled everyone, including this spec's own first draft

`ConfigCheck.cs`, in `CheckStructuralSizes`:

```csharp
// §2.8 (horizontal width allocation) is out of scope for this phase — SizeResolver
// itself doesn't divide width among a horizontal split's children, so summing their
// fixed/minSize against the parent would claim a contention that isn't there yet.
// Revisit this scoping once §2.8 lands.
```

**§2.8 is "Height."** Its subsections are §2.8.1 *"There is no height fixpoint, and there must not
be one"*, §2.8.2 *"Clipping must close the border"*, §2.8.3 *"A pane may shrink-wrap its height"*.
**No framework section specifies stacked width allocation.** There is no "§2.8" to land.

This single miscitation produced a durable false belief — that horizontal width allocation was a
specified-but-unimplemented feature waiting its turn. It survived into the dispatch that
commissioned this spec and into my own first pass. `check-citations.sh` cannot catch it: §2.8
exists and resolves, it just says something else.

**Ruled: fix the comment as part of this task** (section 6.2). It is three lines, it is actively
misleading, and this task is the one that proves it wrong.

---

## 2. The semantic ruling: stacked width is NOT allocation

**Stacked children do not compete for width.** They occupy different *rows*. Two stacked children
can both be 80 columns wide inside an 80-column parent with no conflict whatsoever. This single
fact determines the entire design, and it is the thing an implementor is most likely to get wrong.

Therefore, for the stacked axis there is:

- **no shared budget** — `avail` is not divided, it is the independent ceiling each child is
  measured against;
- **no six-step allocation pass** — `AllocateOnePass`'s fixed → reserve → content → percent → fill
  sequence exists to arbitrate contention that does not exist here;
- **no `reserve`/`laterMinSum` bookkeeping** — nothing a child takes is denied to a sibling;
- **no over-allocation check** — `Σ grants > avail` is not a defect when stacked; it is normal and
  correct;
- **and above all, NO DROP LOOP.** Dropping a vertical child frees width for its siblings, which is
  the entire mechanism. Dropping a stacked child frees *nothing*. A drop loop here could never
  improve any outcome.

> **Implementor warning.** `AllocateWithDrop`, `AllocateOnePass`, `ClampToAvail`, and
> `ResolveVerticalEven` are the wrong templates. Pattern-matching on them will produce elaborate
> contention machinery that can never fire, and an `overAllocated` check that reports a defect on
> correct output. **Write this as a per-child pure function (section 4.1). If you find yourself
> writing a loop that removes a child, stop and re-read this section.**

`distribute` and `gutter` therefore remain **inert** on a horizontal split after B, exactly as
§2.3.2 rules today. Their conclusion survives; only §2.3.2's stated *reason* changes.

---

## 3. Framework amendment — do this FIRST

Four edits to `SPEC-V2-FRAMEWORK.md`. Match by quoted content, not line number.

### A1 — §2.3.2's premise, preserving its conclusion

Replace:

> A **horizontal** split does not divide extent — its children each span the full width and stack
> downward — so there is nothing for the policy to choose.

with:

> A **horizontal** split does not divide extent — its children stack downward and are each sized
> independently against the full width (§2.3.5) — so there is nothing for the policy to choose.
> Independent sizing is not division: two stacked children may each take the whole width, because
> they occupy different rows and never contend for the same cell.

**The ruling that `distribute` is inert is unchanged and must stay.** The second sentence exists
precisely so a later reader does not "correct" the inertness ruling on the grounds that stacked
children now have sizes. Keep the `gutter` paragraph that follows verbatim — *"On a horizontal
split there is no such extent and the key is inert"* remains true, since `gutter` is cells
*between siblings*, and stacked siblings share no column boundary.

### A2 — new §2.3.5, the normative rule

Add after §2.3.4 (`flex`). This is the rule B introduces:

> #### 2.3.5 A stacked child is sized independently within its parent's width
>
> A horizontal split's children stack downward, each occupying its own band of rows. They do not
> divide the parent's width and never contend for it, so each child's width is resolved on its own
> against the same ceiling: the split's inner width, `outer − reserve(p)`.
>
> A stacked child's `size` is read with the same vocabulary as a side-by-side child's, resolved
> against that ceiling rather than against a share of it: `fixed` takes its declared cells,
> `percent` takes that fraction of the ceiling, `content` takes its natural width measured at the
> ceiling as cap, and `fill` — the default, and the behaviour of every stacked child before this
> section existed — takes the ceiling. `minSize` and `maxSize` then bound the result.
>
> **The ceiling always wins.** A child whose declared size, or whose `minSize`, exceeds the ceiling
> is granted the ceiling and the clamp is reported as a render note (§9.8.1). It is never dropped:
> dropping a stacked child frees width for nobody, so the over-constrained handling of §2.3 has
> nothing to do here.
>
> A child granted less than the ceiling is positioned within it by its own `selfAlign` (§3.1),
> which is where the leftover width goes.

### A3 — §2.3.4's flex rationale

Replace, in the structural-check paragraph:

> A pane whose children cannot share the parent's width but can each take all of it

with:

> A pane whose children cannot share the parent's width but can each fit within it

**This is prose repair, not a rule change.** The AND-semantics ruling it sits inside is untouched.
Called out separately because §2.3.4 is Ultra-Advisor-reviewed territory (section 11).

### A4 — §9.8.1's note registry

Register the clamp note from section 4.1. Reuse SPEC-96/#74's existing wording rather than
inventing a second phrasing for the same event — see section 4.2.

---

## 4. The resolution rule

### 4.1 `StackedWidth` — a pure per-child function

Add to `SizeResolver.cs`, near `StackedFloor`:

```csharp
// §2.3.5: a stacked child is sized independently against the split's inner width — never a share
// of it, because stacked children occupy different rows and cannot contend for a column. No
// allocation pass, no contention bookkeeping, and no drop: dropping a stacked child frees width
// for nobody. The ceiling always wins over a declared size or minSize, and the clamp is reported.
private static int StackedWidth(Pane child, int avail, ItemContext ctx, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, Segment> compounds, Func<Pane, int?, int>? measureOverride, int position, RenderNoteCollector notes)
{
    var spec = ClassifySize(child.Size);
    var raw = spec.Kind switch
    {
        SizeKind.Fixed => spec.FixedValue,
        SizeKind.Percent => (int)Math.Round(spec.Pct * avail, MidpointRounding.AwayFromZero),
        SizeKind.Content => measureOverride?.Invoke(child, avail) ?? MeasureRequest(child, avail, ctx, values, compounds),
        _ => avail, // Fill, and the unspecified default — the pre-§2.3.5 behaviour of every stacked child.
    };

    var lo = child.MinSize ?? 0;
    var hi = child.MaxSize ?? int.MaxValue;
    var bounded = Math.Clamp(raw, lo, hi);
    var granted = Math.Clamp(bounded, 0, avail);
    if (granted < bounded)
    {
        notes.Add($"pane {position}: {bounded} columns requested, clamped to {avail} at {avail} columns");
    }

    return granted;
}
```

**Clamp order is normative: declared bounds first, ceiling last.** `Math.Clamp(raw, lo, hi)` then
`Math.Min(..., avail)`. Reversing them lets a `minSize` above the ceiling re-inflate the grant and
reintroduces exactly the overflow SPEC-96 fixes. The `Math.Clamp(bounded, 0, avail)` form also
guards `avail == 0`.

**`MidpointRounding.AwayFromZero`** matches the vertical percent step; do not use bare `Math.Round`
(banker's rounding), which would make stacked and side-by-side disagree at `.5`.

### 4.2 The note wording

`ClampToAvail`'s registered note is:

```
pane {n}: {requested} columns requested, clamped to {avail} at {splitOuterWidth} columns
```

**Reuse this exact shape.** It is pinned in §9.8.1 and in STATUS.md's #74/#78 entries, and a second
phrasing for the same event is the drift SPEC-96 §4 is about. Note the trailing quantity in
`ClampToAvail` is the split's *outer* width; pass the split's outer width here too, not `avail`
twice — the placeholder in section 4.1's snippet is a stand-in and **the Implementor must thread
`splitOuterWidth` through to fill it correctly.** Flagged rather than left to inference.

**`{n}` is the 1-based child position**, matching §9.8.2's convention used by every drop note.

### 4.3 Call site

In `ResolveNode`'s stacked branch, as amended by SPEC-96:

```csharp
if (effectiveSplit == PaneSplit.Horizontal)
{
    var stackedAvail = Math.Max(0, outerWidth - OwnBorderReserve(pane));
    var horizontalChildren = pane.Children
        .Select((c, i) => ResolveNode(c, StackedWidth(c, stackedAvail, ctx, values, compounds, measureOverride, i + 1, notes), ctx, values, compounds, measureOverride, rowCountOverride, notes, collapse))
        .ToList();
    return new ResolvedPane(pane, outerWidth, horizontalChildren, EffectiveSplit: PaneSplit.Horizontal);
}
```

The node's own returned `OuterWidth` stays `outerWidth` — unchanged from SPEC-96, and still the
thing to get wrong.

---

## 5. What already works — do not rebuild it

Three de-risking findings. Each removes work the dispatch assumed was needed.

- **`StackedFloor` needs NO change, and neither does any orientation logic.** `Floor` opens with
  `if (p.MinSize is int min) { return min; }` and its leaf branch is
  `SizeKind.Fixed => spec.FixedValue`. Since
  `StackedFloor(p) = p.Children.Max(c => Floor(c, ...))`, a stacked child's declared `size` and
  `minSize` **already** reach `ResolveFlexOrientation`'s stacked test. The floor is already
  consistent with §2.3.5's semantics; B changes what a child is *granted*, never what it *needs*.
  **`ResolveFlexOrientation`, `SideBySideNeed`, `StackedFloor`, `SideBySideFloor`, and `Floor`
  are all untouched by this spec.**
- **The `sideBySideFloor ≥ stackedFloor` invariant survives.** SPEC-88 Revision 4 records an
  Ultra-Advisor adjudication establishing it. `sideBySideFloor` is `Σ floor + boundary`,
  `stackedFloor` is `max floor`; B changes no floor, so the invariant is untouched by construction.
- **The renderer is already ready.** `AlignBox` computes
  `var deficit = Math.Max(0, targetWidth - row.Width);` and pads per `selfAlign`, and the stacked
  branch already calls `SelfAlignRows(contribution.Buffer.Rows, innerWidth, child.Source.SelfAlign, ...)`.
  A stacked child narrower than the parent's inner width is **already** positioned correctly.
  **B is a resolver-only change.** Do not touch `PaneTreeRenderer`.

---

## 6. `--check` / `ConfigCheck`

### 6.1 Keep the per-child comparison — the promissory note is wrong

`CheckHorizontalSplitChildren` compares each child's own fixed size / `minSize` against the
parent's bound **individually, never as a sum**. The comment in `CheckStructuralSizes` anticipates
that §2.8's arrival will make the sum meaningful.

**Ruled: it will not, and B does not change this.** Stacked children occupy different rows, so
their widths never sum against anything (section 2). The per-child comparison was correct before B
and is correct after it. **Do not add a sum check.**

### 6.2 Fix the comment

Replace the three-line comment quoted in section 1.2 with a statement of the real reason:

```csharp
// A horizontal split's children stack and are sized independently against the parent's inner
// width (§2.3.5) — they occupy different rows and never contend for a column, so their fixed
// sizes and minSizes are compared against the parent individually and never summed. This is
// unlike a vertical split, whose children genuinely divide one budget.
```

The removed text cited "§2.8" for horizontal width allocation; §2.8 is Height (section 1.2).

### 6.3 No new diagnostic

`size` on a stacked child becomes **meaningful**, so it needs no `key-not-applicable` warning —
that was SPEC-96 §8.1's option (C), which Jim did not choose. The existing `distribute`/`gutter`
warnings on horizontal splits stay exactly as they are (section 2).

---

## 7. Behaviour change and migration

**B is a behaviour change for existing configs, not purely additive.** Before it, `size` on a
stacked child was silently inert and every stacked child rendered at full width. After it, such a
config renders differently — which is the point, but it means a config that set `size` on a stacked
child *without effect* will change appearance on upgrade.

Unlike `distribute` and `gutter`, `size` was **never documented as inert on a stacked split**, so a
user could reasonably have written it expecting it to work. Those users get what they asked for;
users who wrote it meaninglessly get a surprise.

- **Every stacked child with no `size`, or `size: "fill"`, is unaffected** — `_ => avail` in section
  4.1 is exactly the old behaviour. Test S14 pins this.
- **NE-2** measures the actual blast radius before implementation.
- The change belongs in release notes. **Do not add a migration shim or a compatibility flag** —
  this codebase has no such mechanism and inventing one here is out of scope.

---

## 8. What must NOT change

- **`StackedFloor`, `SideBySideFloor`, `Floor`, `SideBySideNeed`, `ResolveFlexOrientation`** —
  section 5. Any diff here means the design was misread.
- **Any orientation decision.** B changes how wide a stacked child is, never whether a split stacks.
  S9 guards this.
- **`ClampToAvail` and the three drop loops.** Correct, and unrelated. SPEC-96 §7 applies unchanged.
- **`PaneTreeRenderer`, `AlignBox`, `SelfAlignRows`, `PadToWidth`** — section 5. Pad-only semantics
  stay; SPEC-96 §3.2's ruling against render-time truncation stands.
- **`distribute` / `gutter` inertness on horizontal splits** — section 2. The framework amendment
  A1 deliberately preserves the conclusion while replacing its premise.
- **SPEC-88 Rev 3's flex AND-semantics** for the structural check.
- **SPEC-96's reserve fix.** B builds on it; it is not superseded.

---

## 9. Tests

`SplitFlexTests.cs` unless noted. S1–S7 must fail before, pass after.

**Sizing:**
- **S1 — fixed.** Stacked child `size:"20"` in an 80-wide bordered parent → `OuterWidth == 20`.
- **S2 — fixed above the ceiling.** `size:"200"` → granted the ceiling, clamp note emitted with the
  §9.8.1 wording. Pins "the ceiling always wins."
- **S3 — percent.** `size:"50%"` → half the ceiling, `AwayFromZero` rounding. Include an odd
  ceiling so `.5` actually arises and banker's rounding would visibly differ.
- **S4 — content.** `size:"content"` → natural width, capped at the ceiling.
- **S5 — `minSize` above the ceiling.** Clamped to the ceiling, note emitted, **child NOT dropped**.
  The explicit no-drop assertion.
- **S6 — `maxSize`.** Caps a `fill` child below the ceiling.
- **S7 — bounds vs. declared size.** `size:"10"` with `minSize:20` → 20; `size:"60"` with
  `maxSize:30` → 30. Pins the clamp order.

**Regression / structural:**
- **S8 — `selfAlign` on a narrow stacked child.** Render-level: a child granted less than the
  ceiling is positioned per `selfAlign` and its row still pads to the parent's inner width.
- **S9 — orientation is unchanged.** Sweep widths across the reflow threshold for a set of flex
  configs; assert the `EffectiveSplit` sequence is **identical to pre-change**. Follows the V13c
  byte-identical precedent. **If this goes red, stop — section 5's central claim is wrong.**
- **S10 — floors unchanged.** Assert `StackedFloor`/`SideBySideFloor` outputs are unchanged for
  configs with sizes on stacked children, and that `sideBySideFloor ≥ stackedFloor` still holds.
- **S11 — SPEC-96's tree invariant still holds.** Extend `ResolvedTreeInvariantTests.cs`: the
  stacked arm becomes `child.OuterWidth ≤ node.OuterWidth − OwnBorderReserve(node.Source)` for every
  child — unchanged in form, now non-trivially satisfied since children may be narrower.
- **S12 — `distribute`/`gutter` still inert.** The existing warnings still fire on a horizontal
  split, and neither key changes any width.
- **S13 — nested stacked.** A sized stacked child that is itself a bordered stacked split; both
  levels resolve correctly.
- **S14 — no-size configs are byte-identical.** Full width sweep over configs with no `size` on any
  stacked child; output byte-identical to pre-change. **This is the migration guarantee of section
  7** and the most important regression test in this list.
- **S15 — `StackedFloor` coverage.** `Floor`'s own comment says the horizontal branch is *"untested
  in Phase 3 since no acceptance or required test nests a horizontal split inside a vertical one."*
  B makes that path load-bearing. Add a test nesting a horizontal split inside a vertical one.

---

## 10. NEEDS-EVIDENCE

- **NE-1 (blocking).** Full suite green at `1f938b5` **with SPEC-96 merged**. Record the count.
  Every fails-before/passes-after claim is void against a red baseline.
- **NE-2 (blocking — sizes the behaviour change).** Sweep the repo's test configs, `docs/`
  examples, and Jim's `~/.claude/claude-tui-line.json` for any pane with `size`/`minSize`/`maxSize`
  set on a child of a `horizontal` or `flex` split. **Report the list before implementing.** Each
  is a config whose rendering B changes. If Jim's own config is in it, he should see the before/after
  before this merges.
- **NE-3 (interaction, non-blocking).** A narrower stacked child wraps more and therefore grows
  taller. §2.3.4 rules the orientation decision *"is made from width alone and is never revisited
  once rows are known"*, and §2.8.1 forbids a height fixpoint — so extra rows must degrade through
  the existing height ladder, not feed back into width. Confirm a sized stacked child that grows
  taller degrades normally and does not bust `maxRows` in a new way. **If it does, stop and report:
  that is a width↔height cycle, which the framework forbids, and it would invalidate this design.**
- **NE-4 (non-blocking).** Confirm `MeasureRequest(child, avail, ...)` is the right measurement for
  a stacked `content` child. SPEC-94's amendment measures content leaves at **no cap** for the
  *orientation predicate* specifically; section 4.1 uses a capped measure for the *grant*, which is
  the vertical path's convention (`AllocateOnePass` step 4). I believe capped is correct here — the
  ceiling is real, unlike in the predicate where the question is what the child would want — but
  SPEC-94's Ultra-Advisor ruling is nearby enough to be worth one confirming read of
  `SPEC-88-AMENDMENT-flex-content-orientation.md` §2/§3.3 before implementing.

---

## 11. Escalation — recommended, and scoped

**I recommend Ultra-Advisor review of section 3's framework amendment before it lands.** Not the
code design (sections 4–6), which is high confidence and rests on quoted source.

Why: section 3 changes a normative rule inside §2.3.2 and edits rationale prose in §2.3.4 — the
flex section, whose `sideBySideFloor ≥ stackedFloor` invariant an Ultra-Advisor has already
adjudicated once (SPEC-88 Rev 4), and whose AND-semantics ruling an Ultra-Advisor set (Rev 3).
Framework rules here have proven load-bearing in non-obvious ways twice before: SPEC-88 Rev 2
records that Rev 1's central claim about compiler enforcement was simply false, and Rev 4 records
that Rev 3's §3.4.2 derivation was wrong. This section has a track record of confident, wrong edits.

**The exact question to put:**

> Does redefining a stacked child's width from "each spans the full width" to "each is sized
> independently within the full width, ceiling always wins" disturb anything §2.3.4 relies on,
> beyond the floor invariant — which is provably unchanged because `Floor` already honours `size`
> and `minSize` and B changes no floor? Specifically: §2.3.4 justifies a `flex` pane advertising the
> lower floor by saying its children "can each take all of it." Under §2.3.5 they may take less.
> Does that weaken the justification for the minimum-over-orientations floor, or is it purely
> prose repair?

**This is not a blocker on Jim's decision.** He has settled *that* B happens; that is his call and
this spec implements it. The escalation is about whether section 3's amendment is correctly
*scoped* — a framework-ownership question, not a correctness one. If review comes back clean,
implement as written.

---

## 12. Implementation order

1. **NE-1** baseline (with SPEC-96 merged) and **NE-2** blast radius. Stop and report NE-2's list
   before writing code.
2. **Section 11** escalation, if taken. Section 3 does not land until it returns.
3. **Section 3** framework amendment (A1–A4). Run `check-citations.sh`.
4. **S1–S7** written and watched to **fail**.
5. **Section 4** — `StackedWidth` plus the call site. S1–S7 go green.
6. **Section 6** — `ConfigCheck` comment fix. No behaviour change; S12 stays green.
7. **S8, S13, S15** — render, nesting, and the newly load-bearing `StackedFloor` path.
8. **S9, S10, S11, S14** — the regression guards. **Any red here stops the task** and is reported,
   not adjusted: S9/S10 red means section 5 is wrong, S14 red means the behaviour change is wider
   than section 7 claims.
9. **NE-3**, **NE-4**.
10. Full suite against NE-1's baseline; account for every delta.
11. **STATUS.md** entry in the #74/#78 house format: the rule change, the framework amendment, the
    §2.8 miscitation corrected, and the section 7 behaviour change called out for release notes.

---

## 13. Verification

- Every test in section 9 behaves as its bullet states, including those expected green throughout.
- Full suite ≥ NE-1's baseline, every difference explained.
- `check-citations.sh` clean after the amendment.
- `--preview --json` on Jim's config at a stacking width: SPEC-96's tree invariant holds, and any
  stacked child with a declared size shows that size.
- **S14's byte-identical sweep is the release gate.** If configs without stacked sizes are not
  byte-identical, B has changed something it was not supposed to touch.
