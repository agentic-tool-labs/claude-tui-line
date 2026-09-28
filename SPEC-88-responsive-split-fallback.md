# SPEC-88 — `"split": "flex"`: a split that sits side by side when it fits and stacks when it does not

> **REVISION 1 (supersedes the first draft).** The first draft specified a separate `splitFallback`
> key and argued against a new `split` value. That argument was wrong on its central point and is
> withdrawn — see §2.3.
>
> **REVISION 2 — amendment after an implementation finding.** `Floor()` dispatches on `Split` with
> `if`/`else`, so `Flex` silently fell into the vertical branch, and an ancestor's drop decision
> reads that floor before the flex pane's own dispatch runs. Investigating it showed there is no
> `switch` on `PaneSplit` anywhere in `src/`, so Revision 1's claim that the compiler would
> enumerate the sites needing a decision was **false**. §2.4 owns that error, §3.4 rules on
> `Floor()`, §4.5 added the plumbing requirement, §6/E3 replaced compiler help with a manual audit.
>
> **REVISION 3 — the §4.5.2 audit came back. Two changes:**
> **§4.5.3 is new and rules on `ConfigCheck.cs:851-885` `CheckStructuralSizes`**, the one site the
> audit escalated. **The answer is NOT "run both checks for Flex"** — that would reject `flex`'s
> headline use case at error severity. It is the AND, for the same reason §3.4 is the min. **V10's
> assertion shape is now fully specified** (§6), and **V13** is added for §4.5.3. E7 is resolved and
> closed; §4.5's table now carries the audit's real line numbers and dispositions.
>
> **REVISION 4 (this one) — §3.4.2's derivation was WRONG and is replaced.** The Ultra-Advisor
> adjudicated the V11 question (`report-88-v11-ultra-adjudication.md`) and proved
> **`sideBySideFloor ≥ stackedFloor` is an invariant of the real code**. §3.4.2's worked example was
> unrealizable, so `min` and the widened-`Horizontal` fix are **extensionally equal everywhere
> reachable** and no test can distinguish them. **§3.4's `min` ruling itself is untouched and still
> correct** — only its justification changes, from "the widened form is wrong today" to "the widened
> form depends on an invariant the code does not assert." **V11 is replaced** (§6 item 11): its
> inverted-case requirement was a requirement to construct a witness that provably does not exist.
> §11's confidence claim and §12's new item (3) follow.

## 0. Goal

A pane may be arranged **side by side** (children divide the width, a vertical divider between
them — `README.md:167-170`) or **stacked** (children divide the height, a horizontal divider).
Today that is a static authored choice.

`"split": "flex"` makes it a function of the available width: side by side when the children fit,
stacked when they do not. This is the terminal analogue of a container-query-driven
`flex-direction: row → column` switch.

**Existing configs are untouched.** `flex` is a new token nobody has written; `"vertical"` and
`"horizontal"` keep their exact current meaning, which is now "always side by side" and "always
stacked" respectively.

This document is written to be implementable by someone with no other context.

---

## 1. The facts this design is built on

Read these first; each rules out an approach that otherwise looks reasonable.

### 1.1 The threshold already exists — do not write a second one

`SPEC-V2-FRAMEWORK.md:832-833` defines a split's width floor by orientation, and `:844-846` gives
the reason:

> A split's floor follows its orientation, for the same reason its allocation does: the axis a
> split **divides** sums, the axis it **shares** maxes. A vertical split's children divide width,
> so its width floor is their sum plus gutters; a horizontal split's children each span the full
> width.

So "there is not enough combined width" is **already a computed quantity**. `flex` needs no
threshold key, no column-count heuristic, and no `minWidth` config.

**That rule is already implemented, at `SizeResolver.cs:336-351`**, and the implementation is
richer than the prose: the vertical branch threads `collapse` and `excludeLeft`/`excludeRight` per
§2.10.2, while the horizontal branch passes `false`/`false`. That difference is real and must not
be restated away — §3.1's "call the branch expressions, do not reimplement them" is the rule that
protects it. **It does not, however, invert the two floors' ordering**; §3.4.2 records the proof.

> **The same duality appears a second time, in `--check`.** `CheckSplitBounds`
> (`ConfigCheck.cs:887`) is the sum-plus-boundary form; `CheckHorizontalSplitChildren` (`:913`) is
> the per-child-against-the-bound form. §4.5.3 rules on what a `Flex` pane does there, and the
> answer is the dual of §3.4's. Two places, one rule — which is the point of this section.

> **Amended in Revision 2.** Revisions 0 and 1 wrote the threshold out here as a standalone
> formula (`Σ floor(cᵢ) + totalGutters` vs `max(floor(cᵢ))`). That was a §1.1 drift hazard of
> exactly the kind this section exists to warn about — two places stating one rule, free to
> disagree — and the written form had *already* lost the collapse/exclude threading. **The
> threshold is defined as `Floor()`'s two branch expressions and nothing else.** §3.1 now names
> them rather than restating them.

> **Note for the implementor:** the config key is **`minSize`** (`Config.cs:158`), which `Floor()`
> short-circuits on at `:331-334`. There is **no `minWidth` config key** — `minWidth` appears only
> as internal vocabulary for a *computed* floor (`SizeResolver.cs:761` `MinWidthForRowCount`,
> locals at `:730, :743, :750, :818, :821`) and as the title of framework §2.3.3. Nothing you add
> may imply a `minWidth` key exists.

### 1.2 §2.8.1 forbids reverting the decision

`SPEC-V2-FRAMEWORK.md:1613` — *"There is no height fixpoint, and there must not be one."*

A stacked arrangement consumes more rows than side by side. The tempting refinement is: stack,
measure the rows, un-stack if the result busts `maxRows`. **That is forbidden** — it makes a width
decision depend on a height result.

**Ruling: the orientation is decided from width alone, once, and never revisited.** Rows are then
handled by the existing degrade ladder (`HeightLadder.cs`, §2.8.1) exactly as for a
natively-declared horizontal split. See §7.1.

### 1.3 §2.2 forbids mutating the normalized tree

`SPEC-V2-FRAMEWORK.md:777-781`:

> **Leaf-or-split is decided exactly once, at config resolution, and normalized before anything
> downstream sees the tree.**

The arrangement is a *render-time, width-dependent* choice. It must **not** rewrite `Pane.Split` —
that would make the resolved tree a function of terminal width, breaking the once-at-resolution
guarantee and the cache-key reasoning in §2.5.1.

**Ruling: declared and effective orientation are different things.**

```
declared  (Pane.Split)           ∈ { None, Horizontal, Vertical, Flex }
effective (ResolvedPane, per render) ∈ { None, Horizontal, Vertical }    -- never Flex
```

`Flex` is a declared value only and can never be an effective one. The resolver's job is exactly
the total mapping declared → effective.

> **Amended in Revision 2, resolved in Revision 3.** Revisions 0 and 1 stated the distinction and
> stopped there, as if computing an effective orientation inside the resolver would make it
> available to whoever needed it. It does not: several consumers read `pane.Split` straight off the
> tree. **E7 is now closed — `ResolvedPane` already exists and is the carrier**, needing only an
> `EffectiveSplit` field populated in `ResolveNode` and read at `PaneTreeRenderer.cs:78` and
> `BorderGrid.cs:134`. This was the good outcome of the two E7 branches; the structural case did
> not materialise and no escalation is needed. §4.5 still governs.

### 1.4 What `flex` does NOT change

`gutter` and `distribute` remain meaningful for side-by-side arrangement only (framework `:963`,
`:1170-1176`). `--check` remains width-independent (§9.8, framework `:5963`). The note channel
(§9.8.2, framework `:6103`) remains the way a width-dependent degradation is reported.

---

## 2. Config surface

### 2.1 The decision

Add **`"flex"`** as a fourth accepted value of `split`:

| Value | Arrangement |
|---|---|
| `"none"` | not a split |
| `"horizontal"` | always stacked, horizontal divider |
| `"vertical"` | always side by side, vertical divider |
| **`"flex"`** | **side by side when the children fit; stacked when they do not** |

```json
{
  "split": "flex",
  "gutter": 1,
  "children": [
    { "size": "content", "items": [ { "item": "model" } ] },
    { "size": "fill",    "items": [ { "item": "directory" } ] }
  ]
}
```

No new key. No new key-pair to keep coherent.

### 2.2 Why the TOKEN is cheap to wire

`Config.cs:785-792` is a table:

```csharp
private static readonly (string Token, PaneSplit Value)[] SplitAccepted =
{
    ("none", PaneSplit.None),
    ("horizontal", PaneSplit.Horizontal),
    ("vertical", PaneSplit.Vertical),
};

internal static IReadOnlyList<string> SplitAcceptedTokens { get; } = SplitAccepted.Select(a => a.Token).ToArray();
```

`ParseSplitCore` (`:794-806`) scans it; `AcceptedCommand.cs:39` consumes `SplitAcceptedTokens`.
**One row** adds the token, and `--accepted --json` reports it with no further work. That is the
registry coherence framework §1.1 asks for, obtained by construction.

> **Amended in Revision 2 — the section title was "Why this is cheap to wire" and that oversold
> it.** The *token* is one row. **The behaviour is not cheap**: §4.5 requires an audit of every
> `PaneSplit` read in `src/`, and that audit turned up a genuine hole (§4.5.3). Do not let §2.2 set
> the expectation for the size of this task.

### 2.3 Why the first draft's `splitFallback` was withdrawn

The first draft rejected a new `split` value on two grounds.

**Withdrawn argument 1 — "every member of the set names a divider, and a responsive policy is not
a divider," attributed to §1.1.1.** §1.1.1 does not say this. Framework `:775` says *"Naming
follows tmux/vim convention: a 'vertical split' produces a vertical divider"* — an explanation of
why the existing two are named as they are, not a constraint binding future members. §1.1.1's
actual distinction (`:212`) is *"does the thing being written down have members, or does it have a
shape?"*, which is why `size` is exempt from the registry and `split` is not. `flex` is a member of
a closed set of literals. **The first draft cited a section for a proposition it does not contain.**

**Withdrawn argument 2 — "a fourth enum member forces a new arm into every `PaneSplit` switch."**
Withdrawn as stated — only code reading the *declared* value needs to change, and downstream
consumers read the effective orientation (§1.3). **But see §2.4: the premise underneath this
paragraph was factually wrong in a way that matters more than the conclusion.**

**Three things the first draft missed, all favouring `flex`:**

1. **It makes illegal states unrepresentable.** `splitFallback` admits
   `{"split":"none","splitFallback":"horizontal"}` and
   `{"split":"horizontal","splitFallback":"horizontal"}` — writable nonsense, each needing a
   `key-not-applicable` diagnostic to police, plus tests for it. With `flex` those states cannot
   be written. The diagnostic the first draft specified is deleted, not reimplemented.
2. **It subsumes the deferred open question.** The first draft left "should this also support
   horizontal→vertical?" for the user. `flex` is width-driven in both directions, so the question
   does not arise.
3. **One key is a smaller authoring surface than two.**

**What the first draft got right and is retained:** a name like `"vertical-or-horizontal"` would
indeed be ambiguous, because `"vertical"` names the *divider* and not the stacking axis. That
objection was to a **bad name**, not to the mechanism.

### 2.4 **The compiler will not help. Revision 1 claimed it would. That claim was false.**

Revision 1's §2.3 and §6 step 1 both asserted that `TreatWarningsAsErrors` would fail the build on
non-exhaustive switches over the widened `PaneSplit`, and that the compiler's list would therefore
serve as the checklist of sites needing a decision. §6 step 1 said so verbatim: *"the compiler's
list is the checklist."*

**An audit of `src/` finds no `switch` over `PaneSplit` at all.** Every dispatch site is an `if` /
`else if` chain, an `is`-pattern test, or a ternary (§4.5's table). **Widening the enum produces
zero compiler diagnostics**, and a green build means nothing about coverage.

The manual audit that replaced it found the `Floor()` bug **and** a second genuine hole (§4.5.3)
that no test would have caught. Both would have shipped silently under Revision 1's plan.

#### 2.4.1 This does not reopen `flex` vs `splitFallback` — it widens the gap in `flex`'s favour

Every site in §4.5's table encodes the assumption **"`Split == Vertical` means side by side."**
`splitFallback` breaks that assumption at exactly the same sites — a vertical split with a fired
fallback stacks, and `PaneTreeRenderer` and `BorderGrid` would draw a vertical divider across it —
while adding **no new enum value to search for**. The audit target under `splitFallback` becomes
"every place that assumes a vertical split renders side by side," which is not greppable.

Under `flex`, `grep -rn 'PaneSplit\.' src/ --include=*.cs` returns a finite, auditable list.
**A greppable hazard beats an ungreppable one**; neither is compiler-checkable, and that is the
honest comparison.

### 2.5 One naming risk, flagged not resolved

`size` and `height` both accept `"fill"` (`README.md:160`). `"flex"` and `"fill"` are one letter
apart and both borrow CSS vocabulary, but mean different things on different keys — `flex` is
*direction* on `split`, `fill` is *extent* on `size`/`height`. Not blocking: the term is the user's
own, chosen with CSS flexbox in mind, and the keys are distinct. Noted so the README (§8) can
address it head-on rather than inherit the ambiguity.

### 2.6 Backward compatibility

`flex` is new. No existing config contains it, and neither `"vertical"` nor `"horizontal"` changes
meaning. **No existing config renders differently** — asserted by V3.

Framework `:778-779` and `Config.cs:826`: a non-empty `children` with `split` absent still
normalizes to `vertical`, **not** `flex`. Changing that default would alter existing renders and is
explicitly out of scope (§7).

---

## 3. Threshold semantics

### 3.1 The predicate

> **Amended by `SPEC-88-AMENDMENT-flex-content-orientation.md`:** the side-by-side quantity below
> is `sideBySideFloor` only for children that are not content-sized leaves; see the amendment for
> the replacement predicate and why §3.2's original reasoning does not hold.

For a pane `p` with declared `Split == Flex` and available outer width `W`, let:

- **`sideBySideFloor(p)`** = the value `Floor()`'s **vertical** branch computes for `p`
  (`SizeResolver.cs:343-351`), given `p`'s own `collapse` / `excludeLeft` / `excludeRight`
- **`stackedFloor(p)`** = the value `Floor()`'s **horizontal** branch computes for `p`
  (`SizeResolver.cs:340`), given the same arguments

Then:

```
effective(p) = Vertical    if sideBySideFloor ≤ W
             = Horizontal  if sideBySideFloor > W  ∧  stackedFloor ≤ W
             = Vertical    otherwise               -- neither fits; see below
```

**Compute these by calling the existing branch expressions, not by reimplementing them.** §1.1's
amendment is the reason: the branches carry collapse and border-exclusion threading that any
restatement will lose. That threading does not change which floor is larger (§3.4.2's invariant),
but it does change the *values*, and §3.1's predicate compares them against `W` — so a restatement
would still shift the width at which a pane stacks.

The third case is load-bearing. **When neither arrangement fits, `flex` resolves to side by side**
and the existing drop ladder runs exactly as for a declared `"vertical"` split. Stacking would
change the surface's shape without fixing anything, and the drop ladder is the better-understood
answer to a genuinely over-constrained split. This preserves the first draft's ruling.

### 3.2 Interaction with `size: "content" | "fill"`

> **First paragraph below WITHDRAWN by `SPEC-88-AMENDMENT-flex-content-orientation.md` §1.** It is
> false: `Floor()` is defined to be `0` for a content-sized leaf, which makes the predicate
> degenerate for that shape rather than uniform. Kept here, not deleted, because the amendment's
> §1 explains why the reasoning that produced it is worth understanding. See the amendment for the
> replacement predicate (§2) and its scope (content-sized **leaves** only, §3).

~~None at the decision point — the predicate is computed over **floors**, and a child's floor is
defined by §2.3 regardless of its `size` mode. Once the effective orientation is chosen, allocation
proceeds by the existing rules for that orientation.~~

The consequence is intended: `size` names *"share of the PARENT's split axis"* (framework `:756`),
so the effective orientation determines which axis a child's `size` governs — identical to what the
author would get by declaring that orientation outright.

### 3.3 Interaction with `gutter` and `distribute`

Both are defined only for side-by-side arrangement (framework `:963`, `:1170-1176`).

**Ruling: they apply when the effective orientation is side by side and are ignored when it is
stacked** — identical to a declared `"horizontal"` split ignoring them today. No extra render note;
§5's note already reports the orientation, and a per-key note on every narrow render would drown
the channel.

**`--check` must treat `gutter` and `distribute` as APPLICABLE on a `flex` pane, and must not
warn.** The existing diagnostic keys off the horizontal case, so `Flex` falls through and no
warning fires — **correct with no change**, but assert it (V6) rather than assume it.

### 3.4 **`Floor()`'s contract for a `Flex` pane — the Revision-2 ruling**

> This section governs `SizeResolver.cs:329-361`.

**Ruling: `Floor(p)` for a pane with `p.Split == Flex` returns**

```
min( sideBySideFloor(p), stackedFloor(p) )
```

**i.e. both branch expressions are evaluated with the same `collapse` / `excludeLeft` /
`excludeRight` arguments `Floor()` was called with, and the smaller result is returned.** The
`minSize` short-circuit at `:331-334` is unchanged and still takes precedence.

**This ruling stands unchanged after Revision 4.** What changed is why — see §3.4.2.

#### 3.4.1 Why this is the right contract

`Floor(p)` answers *"the least width at which `p` can render."* A `Flex` pane can render in
**either** orientation, so its floor is the minimum over the orientations it may adopt. This is
§2.3's own rule — *a split's floor follows its orientation* — applied to a pane whose orientation
is whichever one fits, and it follows directly from §3.1's case 2: at any `W` with
`stackedFloor ≤ W < sideBySideFloor` the pane renders stacked, completely, dropping nothing. A
floor reporting `sideBySideFloor` would be claiming the pane cannot render at a width where it
demonstrably can.

An ancestor's `AllocateWithDrop` / `DropFloor` calls `Floor()` on each child
(`SizeResolver.cs:376`, `:406`, `:494`, `:527`, `:609`, `:641`, `:881`, `:914`) **before** that
child's own `ResolveNode` dispatch runs. So a `Flex` child's floor is consumed by its parent's drop
decision while the child's own orientation is still undecided — which is precisely why `Floor()`
must answer the orientation-*independent* question, and why fixing only the resolver dispatch
(§4.1) would not have been enough. The reported symptom — *parents may drop a flex pane, or drop
siblings competing with it, more aggressively than necessary* — is correct, and V10 is the test.

**This section is the one that carries the ruling's weight.** §3.4.2 below is now about *robustness*
only; if you read one justification for `min`, read this one.

#### 3.4.2 **`min` vs "widen the `Horizontal` branch" — REPLACED IN REVISION 4**

> **Revisions 2 and 3 said the widened-`Horizontal` fix was WRONG, and offered a worked example of
> `sideBySideFloor < stackedFloor` to prove it. That example was unrealizable and the claim was
> false.** The Ultra-Advisor's adjudication (`report-88-v11-ultra-adjudication.md`) proved the
> opposite. The old text is deleted rather than annotated, because a spec that leaves a disproven
> counter-example on the page invites someone to re-derive from it.

**The proven invariant: `sideBySideFloor ≥ stackedFloor`, at every node, always.**

Proof (per-node; needs only `Floor ≥ 0`). Let `m = argmax` over the children, so `ST = F_m`.

- **`collapse: false`** — no discounts are applied, so `SS = ΣF_i + g(n−1) ≥ F_m`. ∎
- **`collapse: true`** — the exclusion discount and the boundary cost are *positionally coupled*:
  `discount_m = 1` forces `n ≥ 2`, which forces `boundary ≥ 1`; `discount_m = 2` requires `m` to be
  a middle child, which forces `n ≥ 3`, which forces `boundary ≥ 2`. Therefore
  `SS ≥ F_m − discount_m + boundary ≥ F_m = ST`. ∎

**The position/boundary coupling is the load-bearing step: every column an exclusion removes, the
boundary has already re-added.** That is the fact the old §3.4.2 missed — it treated the discount as
free. Its worked example claimed `Floor(excludeRight) = 98` from a full floor of `100`, a
**2-column discount from a single edge**; real discount is **≤ 1 per edge**, which gives
`99 + 0 + 1 = 100` — a **tie**, not the inequality the example asserted.

**Consequence: `min(SS, ST) = ST` everywhere, so `min` and the widened-`Horizontal` branch are
extensionally equal over the whole validated domain. No witness distinguishes them, and no
re-scoping of a test can find one.**

##### So why still `min`?

Not because the widened branch computes a wrong answer today — it does not. Because it is **correct
only conditionally**, on an invariant that no line of code asserts and no test guards:

1. **The invariant is a property of arithmetic that a future change can break.** Per the
   adjudication's Q4: *"§2.8-class changes (horizontal splits dividing width) break (i) outright;
   extending §2.10.2 exclusion threading attacks (ii)/(iii). In either future, min stays silently
   correct; widened-Horizontal silently becomes the §3.4.1 bug. min is cheap insurance, now
   precisely justified."*
2. **The failure mode is silent and is exactly §3.4.1's bug** — a parent dropping a pane that could
   have rendered. Not a crash, not a diagnostic: a missing pane.
3. **`min` costs one comparison** and states §3.4.1's rule directly, in the form the rule is
   written. The widened branch states a *derived consequence* of that rule, which is why it needs a
   proof and `min` does not.

**Use `min`.** The honest summary for a reader: `min` is not the *correct* choice over an incorrect
one — it is the *unconditional* choice over a conditional one, and §3.4.1 is the reason the
unconditional form is also the more legible one.

##### What this changes about testing

**No test can distinguish `min` from the widened branch**, so do not write one and do not treat a
passing test as evidence for either. **V11 is replaced** (§6 item 11) by a test of the *invariant*
rather than of the two implementations' difference — which is the only thing here that is actually
falsifiable, and which converts the risk in point 1 above from silent to caught.

##### The one theoretical door

The invariant's proof assumes non-negative gutters and `minSize`. Nothing validates that (§12 item
3 — `Config.cs:694` has no sign check). A negative gutter would be degenerate, unsupported config,
not a reachable case — but it is the only route to a real `SS < ST`, so it is recorded here rather
than only in §12.

#### 3.4.3 The cost, and the nesting hazard

`min` requires evaluating **both** branches, and both recurse into the children. For a `Flex` pane
whose children are themselves `Flex`, that doubles per level — **2^d in flex-nesting depth `d`** —
and `Floor()` is called inside the drop-retry loops (`:494`, `:527`), so it is already hot.

Realistic `d` is 1–2 and the cost is then negligible. This is **E6**: measure before optimising,
and memoise on `(Pane, collapse, excludeLeft, excludeRight)` **only if it actually shows**. Do not
add a cache pre-emptively — it would be the first cache in this function, and §2.5.1's cache-key
reasoning would then apply to it, which is design surface this spec has not opened.

> **Revision 4 note.** §3.4.2's invariant means the second branch is provably never the smaller one,
> so an optimiser could in principle skip it. **Do not.** Skipping it *is* the widened-`Horizontal`
> form under another name, and inherits its conditional correctness. If E6 shows a real cost, take
> the memoisation route, not the elimination route.

#### 3.4.4 Consistency with §3.1

A parent allocates the flex child `W_child ≥ Floor(flexChild) = min(sideBySideFloor, stackedFloor)`.
The child then evaluates §3.1 at `W_child`. If `sideBySideFloor ≤ W_child` it sits side by side;
otherwise `stackedFloor ≤ W_child` holds (since `W_child ≥ min` and the min is one of the two) and
it stacks. **Either way the child renders without dropping anything** — the floor the parent
honoured is one the child can actually meet.

---

## 4. Where this evaluates

### 4.1 The orientation decision

In `SizeResolver.cs`, at the dispatch that selects a resolver — `:172` (`Split == None` or no
children → leaf) and `:177` (`== Horizontal`), with vertical as the implicit `else`. That dispatch
is precisely where declared becomes effective, so it is where `Flex` resolves.

The two declared-vertical entry points are:

- `SizeResolver.cs:202` — `ResolveVertical(Pane split, int splitOuterWidth, ItemContext ctx, IReadOnlyDictionary<string, string?> values, Func<Pane, int?, int>? measureOverride, RenderNoteCollector notes, bool collapse)`
- `SizeResolver.cs:588` — `ResolveVerticalMinRows(...)` (the `distribute: "min-rows"` path)

**Requirement: resolve `Flex` ABOVE both, never inside one.** A hook inside `ResolveVertical` alone
would silently not apply to a pane carrying `distribute: "min-rows"`. V5 tests exactly this.

`RenderNoteCollector notes` is already a parameter at both sites, so §5's note needs no new
plumbing.

**Defensive invariant:** `Flex` must be unrepresentable as an effective orientation. Either use a
distinct type, or throw on a `Flex` effective value with a message naming the pane. A silent
fall-through to a default orientation is the failure mode to avoid — and per §2.4 the compiler will
not warn you about one.

> **Amended in Revision 2 — §4.1 was necessary but NOT sufficient, and Revision 1 presented it as
> sufficient.** Resolving `Flex` above both vertical entry points fixes the pane's own rendering.
> It does nothing for `Floor()` (§3.4), which an *ancestor* calls before this dispatch is reached;
> nothing for the consumers in §4.5 that read `pane.Split` without going through the resolver; and
> nothing for `--check`, which never runs the resolver at all (§4.5.3).

### 4.2 Order relative to the height ladder

Decided **during width resolution, strictly before** `HeightLadder.cs` runs. The ladder operates on
the chosen effective orientation and is **not modified by this task**. Per §1.2 its outcome never
feeds back.

### 4.3 Nesting

Per-pane and independent, evaluated top-down as each pane's width becomes known. A `flex` child of
a `flex` parent is resolved on its own terms once its own width is known. No global or multi-pass
coordination. (See §3.4.3 for the one cost this imposes.)

### 4.4 `--check` is width-independent

Framework `:5963` — *"`--check` is width-independent, and what 'cannot fit' therefore means."*

**`--check` must not predict whether a `flex` pane will stack.** It validates the declaration only:

- `"flex"` on a pane with children → **valid, no diagnostic**.
- An unrecognised `split` value → `unknown-enum-value` (§9.4.1), unchanged machinery, now with
  `flex` among the accepted tokens in the message.
- `"flex"` with no children → treated exactly as framework `:781` already treats a split value with
  no children: the pane is a leaf and the stray `split` is dropped. **No new rule.** This is the
  ruling that resolved `ConfigCheck.cs:647` — widen that leaf-check pattern to include `Flex`.
- `gutter`/`distribute` on a `flex` pane → **no diagnostic** (§3.3).

**Width-independent does not mean check-free.** §4.5.3 is a structural check that holds at every
width and therefore stays in `--check`; §4.4 excludes predictions about a *particular* width, not
invariants that fail at all of them.

The first draft's `key-not-applicable` diagnostic for `splitFallback` **does not exist in this
design** and must not be added in another form.

### 4.5 The effective orientation must be PLUMBED, not merely computed

§1.3 says downstream reads an effective orientation. Revisions 0 and 1 never said *how it gets
there*, and several consumers read `pane.Split` straight off the tree.

**E7 is resolved: `ResolvedPane` is the carrier.** It needs an `EffectiveSplit` field populated in
`ResolveNode` and read at `PaneTreeRenderer.cs:78` and `BorderGrid.cs:134`. The structural-change
branch of E7 did not materialise, and the escalation trigger it carried is stood down.

Every `PaneSplit` / `.Split` read in `src/`, with the audit's disposition. **Class A** sites read
the *declared* value at load- or check-time, which is legitimate. **Class B** sites read
`pane.Split` where they need the *effective* orientation.

| Site | Reads | Class | Disposition |
|---|---|---|---|
| `SizeResolver.cs:172` | `!= None && Children.Count > 0` | A | correct as-is |
| `SizeResolver.cs:177` | `== Horizontal` | **decision point** | §4.1 — resolve `Flex` here |
| `SizeResolver.cs:336` | `!= None && Children.Count > 0` | A | correct as-is |
| `SizeResolver.cs:338` | `== Horizontal` | **B — bug** | §3.4 — return `min` of both branches |
| `PaneTreeRenderer.cs:78` | `else if (== Vertical)` | **B** | read `ResolvedPane.EffectiveSplit` |
| `BorderGrid.cs:134` | `== Vertical` | **B** | read `ResolvedPane.EffectiveSplit`; draws the divider |
| `Config.cs:776` | `== Horizontal` | A | §4.5.1 — propagate as vertical; no edit |
| `Config.cs:826` | `NormalizeSplit` | A | `Flex` passes through; correct |
| `Config.cs:673-674` | `NormalizeSplit(ParseSplit(…))`, `!= None` | A | correct as-is |
| `PaneCollapse.cs:27` | `!= None && Children.Count > 0` | A | correct as-is |
| `Program.cs:169` | `!= None && Children.Count > 0` | A | correct as-is |
| `ConfigCheck.cs` distribute/gutter check | `isHorizontal` | A | `Flex` falls through, no warning — correct (§3.3); assert via V6 |
| `ConfigCheck.cs:647` | leaf-check pattern | A | **widen to include `Flex`** — already ruled by §4.4 |
| `ConfigCheck.cs:721`, `:730` | edge-conflict check | A | correct-by-design; the check is legitimately width-dependent and out of scope for `--check` |
| `ConfigCheck.cs:933` | overflow-position check | A | correct-by-design; childless `Flex` normalizes to `None` before this runs |
| **`ConfigCheck.cs:861-878`** | `if (== Vertical) … else if (== Horizontal)` | **A — HOLE** | **§4.5.3 — a `Flex` pane gets zero structural-size validation today** |

#### 4.5.1 Ruling on `Config.cs:776` (load-time border propagation)

Border-edge propagation runs at config load, **before any width is known**, so it cannot consult an
effective orientation — a width-dependent border edge in the resolved tree is exactly the §2.2
violation §1.3 forbids.

**Ruling: a `Flex` pane propagates border edges as a vertical split does**, matching §3.1's case-3
default and keeping the load-time tree width-independent. The consequence is that a *stacked* `flex`
pane may carry edges chosen for the side-by-side arrangement. **This is accepted and must be
documented** in the framework text (§9) rather than silently tolerated.

**If V8 shows the result is visually broken rather than merely imperfect, STOP AND REPORT.**

#### 4.5.2 The audit outcome

Of the sites flagged for verification, three resolved without escalation (`:647` by §4.4;
`:721`/`:730` and `:933` correct-by-design as written). **One was a genuine hole and is ruled on
below.** The audit did its job: this hole is invisible to the compiler, produces no test failure,
and would have shipped silently under Revision 1's plan.

#### 4.5.3 **Ruling on `ConfigCheck.cs:851-885` `CheckStructuralSizes` — Revision 3**

`CheckStructuralSizes` walks every pane and, for panes with children, routes on declared split:

- `Split == Vertical` → `CheckSplitBounds` (`:887`)
- `Split == Horizontal` → `CheckHorizontalSplitChildren` (`:913`)
- **anything else → nothing**

So a `Flex` pane receives **no structural-size validation at all**. That is a hole, and the reporter
is right that it matters: a flex pane whose children's fixed or `minSize` values structurally exceed
its own bound currently passes `--check` clean and surfaces only at render time.

**These two checks are §2.3's sum-vs-max duality, mirrored into `--check`.** That is what makes the
ruling determinate:

| Check | Form | Corresponds to |
|---|---|---|
| `CheckSplitBounds` (`:887`) | `Σ FixedSize(children) + boundaryCost > bound`, and `Σ MinSize(children) + boundaryCost > maxSize` | `sideBySideFloor` |
| `CheckHorizontalSplitChildren` (`:913`) | per child: `FixedSize(child) > bound` | `stackedFloor` |

Both emit `fixed-sizes-exceed-parent` at **error**.

**RULING: run BOTH checks for a `Flex` pane, and emit a diagnostic only if BOTH produce one. The
operator is AND, not OR.**

##### Why not the OR (running both and reporting either)

Because it rejects `flex`'s headline use case at error severity. Take a `flex` pane with
`maxSize: 40` and two children of `minSize: 30`:

- `CheckSplitBounds` sub-check B: `30 + 30 + boundary > 40` → **`fixed-sizes-exceed-parent`, error**
- `CheckHorizontalSplitChildren`: no `FixedSize` exceeds 40 → **nothing**

Side by side it genuinely cannot fit; stacked each child gets the full 40 ≥ 30 and it renders
perfectly. **This is precisely the configuration `flex` exists to serve**, and the OR makes
`--check` fail on it — `fixed-sizes-exceed-parent` is an error, so the config is rejected outright.
A feature whose canonical example fails its own validator is not shippable.

##### Why the AND is right

Same reasoning as §3.4, in dual form. A `Flex` pane is structurally impossible only if it is
impossible in **every** orientation it can adopt:

```
Floor(flex)                 = min over adoptable orientations
structurally-impossible(flex) = AND over adoptable orientations
```

`min` on floors and `AND` on impossibility are the same statement. §1.1's whole point is that this
rule should exist once; expressing it two different ways in the resolver and the checker is the
drift that section exists to prevent.

> **Revision 4 note.** §3.4.2's invariant does **not** propagate to here. The checks are not the
> floors — `CheckHorizontalSplitChildren` tests only `FixedSize` and has no `minSize` counterpart
> (see the under-report below), so the two checks are not in the ordering relation the two floors
> are. **The AND is therefore load-bearing today**, not merely insurance, and V13(a) is a real
> discriminating test in a way V11 never was. Do not let the §3.4.2 revision cast doubt on §4.5.3.

##### This turns the reporter's own observation into the rule

The escalation noted that because §3.1 case 3 makes side-by-side the over-constrained fallback,
`CheckSplitBounds`'s invariant might be *more* relevant to a flex pane than to a declared-vertical
one. **That observation is correct and it is what the AND encodes.** Case 3 fires exactly when
neither orientation fits — i.e. exactly when the horizontal check has *also* failed. The AND
therefore reports precisely in the case where the pane will fall back to side-by-side and drop.
The instinct was right; the OR was the wrong way to act on it.

##### Implementation

In `CheckStructuralSizes`, add a `Flex` branch after the `Horizontal` one. Materialize both
sequences (they are lazy `IEnumerable`s — `.ToList()` them, or the AND cannot be evaluated), and:

- if **either** is empty → yield nothing
- if **both** are non-empty → yield **one** diagnostic

The single diagnostic:

- **path**: the split's own `path` — the split is what is impossible, not any one child. Do **not**
  use `CheckHorizontalSplitChildren`'s `{path}/children/{i}` form here.
- **code**: `fixed-sizes-exceed-parent`, reused. §9.6 fixes a code's meaning and severity once
  shipped; this is the same meaning (children's structural sizes exceed the parent's own bound) at
  the same severity, so reuse is correct and **a new code must not be registered**.
- **severity**: `Error`, matching both existing sites.
- **message**: must name both orientations, because neither existing message is true of a flex
  pane — `CheckHorizontalSplitChildren`'s in particular asserts *"a horizontal split gives every
  child the full parent width"*, which would be actively misleading. Emit along the lines of:
  `"this flex split's children exceed its bound ({bound}) in both arrangements: side by side ({sideBySideDetail}) and stacked ({stackedDetail})"`.
  Exact wording is the Implementor's, but it must state that **both** arrangements were tried.

`BoundaryCost` is called by `CheckSplitBounds` unchanged — for the side-by-side hypothetical that is
the right cost, and the flex branch must not adjust it.

##### The one thing this ruling knowingly under-reports

`CheckHorizontalSplitChildren` checks only `FixedSize`; it has **no `minSize` counterpart**. So a
child with `minSize: 50` under a parent bound of 40 is undiagnosed *today* for a declared-horizontal
split. Under the AND, that pre-existing gap propagates: such a flex pane has `CheckSplitBounds` fire
and `CheckHorizontalSplitChildren` stay silent, so the AND yields nothing — even though stacked is
genuinely impossible too.

**Accepted, and deliberately not fixed here.** Three reasons: it is inherited from a pre-existing
gap in the declared-horizontal path, not introduced by `flex`; it **fails open** (a config that is
error-free and then degrades visibly at render time with §5's note) rather than closed (rejecting a
valid config), and failing open is the correct direction for a check that is explicitly incomplete
by §9.8's design; and closing it means changing what `--check` reports for existing
declared-horizontal configs, which is out of scope for #88 and could break configs in the field.
**Flagged as a separate item in §12.**

---

## 5. The stack must not be silent

Per §9.8.2 (framework `:6103`), a width-dependent orientation change is exactly what the note
channel exists to report.

**Emit one render note when, and only when, a `flex` pane resolves to stacked.** Match the existing
phrasing in `SizeResolver.cs` (`:522, :528, :560, :636, :642, :909, :915`) — lowercase,
`pane {N}`-prefixed, numbers stated:

```
pane {N}: flex split stacked; children need {X} columns at {Y} columns
```

`{X}` is `sideBySideFloor`, `{Y}` the available width — the same two quantities the existing drop
note reports, for continuity. Add via `RenderNoteCollector.Add(string)` (`RenderNote.cs:23`).

**No note when a `flex` pane renders side by side**, and **no note for a declared `"vertical"` or
`"horizontal"` pane.** A note on every render would make the channel useless.

---

## 6. Verification

**Step 1 — THE AUDIT.** *(Replaces Revision 1's `dotnet build` step, which was invalid — §2.4.
Completed; outcome in §4.5 and §4.5.2. Retained here because a later change to `PaneSplit` must
repeat it.)* **A clean `dotnet build` proves nothing about coverage**: there is no `switch` on
`PaneSplit` in `src/`, so widening the enum emits no diagnostics whatsoever.

2. **V1 — it stacks.** A `flex` split whose children's floors sum above the surface width but whose
   max is under it renders stacked. Assert on rendered output, not an internal flag.
3. **V2 — it does not stack when stacking would not help.** Same config at a width below
   `stackedFloor`. Output byte-identical to the same config declared `"vertical"` (§3.1 case 3).
4. **V3 — backward compatibility.** For a `"vertical"` split at a width where a `flex` pane would
   stack, output is byte-identical to current `main`'s. **The load-bearing regression test** —
   capture expected bytes from `main` before changing anything.
5. **V4 — the note fires exactly once, and only on a real stack.** Present in V1; absent in V2, V3.
6. **V5 — `distribute: "min-rows"` also stacks.** V1 plus `distribute: "min-rows"`, proving the
   resolution sits above both entry points (§4.1).
7. **V6 — `--check` diagnostics.** `"flex"` with children → no diagnostic; `gutter`/`distribute` on
   a `flex` pane → **no** diagnostic; a bogus `split` value → `unknown-enum-value` listing `flex`;
   childless `"flex"` → leaf, stray `split` dropped, no diagnostic (`ConfigCheck.cs:647`).
8. **V7 — registry coherence.** `--accepted --json` reports `split` with all four members;
   `tools/check-all.sh` passes.
9. **V8 — `--schema --json`** describes `split` including `flex` (`SchemaCommand.cs:196`, `:207`);
   **and** a bordered `flex` pane that stacks renders sane borders (§4.5.1 — report rather than fix
   if it does not).
10. **V9 — the effective orientation is never `Flex`.** Assert `ResolvedPane.EffectiveSplit` at
    several widths, and that no class-B consumer ever observes `Flex`.

### 6.1 V10 — `Floor()` under sibling competition (assertion shape specified)

**The test class Revision 1 was missing.** The failure is a *parent* dropping something it did not
need to, so V10 is an integration test on rendered output and notes. **It must not assert on
`Floor()` directly** — V10's subject is the parent's drop decision, and a `Floor()`-level V10 would
test the wrong layer.

Construct it so the buggy and fixed versions land on opposite sides of the parent's drop decision:

```
parent: {"split": "vertical", "gutter": g, "children": [
  { fixed size S },                                  // the competing sibling
  { "split": "flex", "gutter": 1, "children": [
      { floor F }, { floor F } ] }                   // the flex child
]}

stackedFloor(flex)    = F
sideBySideFloor(flex) = 2F + 1

correct parent floor  = S + F     + g
buggy   parent floor  = S + 2F + 1 + g
```

**Assert across the whole window `W ∈ [S + F + g, S + 2F + 1 + g)`, not at a single width.** A
single sample can land where both versions agree; the window is exactly the set of widths on which
they differ. For every `W` in it:

1. Content from the fixed sibling **and from both children of the flex pane** is present in the
   rendered output.
2. The `pane {N}: flex split stacked` note fired — proving it stacked, rather than the parent
   having accidentally handed it enough width to sit side by side.
3. **No drop note for the parent pane.** This is the assertion that actually fails against the
   pre-fix code.

Plus **one boundary pin at `W = S + F + g − 1`**: a drop *does* occur there. Without it the test can
pass trivially by never being near the edge, and would not detect a fix that overshoots in the other
direction.

> **Revision 4 note.** V10 remains valid and remains the test that proves `Floor()` was fixed. Note
> what it does *not* prove: the pre-fix bug was `Flex` falling into the **vertical** branch (§3.4's
> Revision-2 finding), and V10 discriminates that from either correct form. It does not, and cannot,
> discriminate `min` from widened-`Horizontal` — nothing can (§3.4.2). **The shipped
> `SplitFlexTests.cs:326-351` tests are in this same category**: keep them, but relabel them as
> guarding the Rev-2 routing bug, not as guarding the `min` choice. A test whose name claims a
> guarantee it does not provide is worse than no test, because the next person trusts it.

11. **V11 — the `SS ≥ ST` invariant, tested directly.** *(REPLACED IN REVISION 4. The old V11
    required a `collapse: true` case where `sideBySideFloor < stackedFloor`; §3.4.2 proves no such
    case exists, so the old V11 was a requirement to construct a nonexistent witness — unsatisfiable
    by any correct implementation.)*

    **Make `SideBySideFloor` and `StackedFloor` `internal`** (they are currently expressions inline
    in `Floor()`'s branches; extract them as named `internal static` methods) and unit-test the
    invariant directly:

    > for every shape in the adversarial set, `SideBySideFloor(p, …) ≥ StackedFloor(p, …)`

    The adversarial set must at minimum include: `collapse: false` and `collapse: true`; `n = 2` and
    `n ≥ 3`; a dominant first child, a dominant middle child, and a dominant last child (the three
    positional cases the §3.4.2 proof splits on); `gutter: 0` and `gutter > 0`; and a child with
    `Floor = 0`. Assert equality is permitted — the proof yields `≥`, and the tie at
    `99 + 0 + 1 = 100` is a real reachable outcome, so a test demanding strict `>` would fail
    correct code.

    **What this test is for:** it is not a test of `min`. It is a **tripwire on the assumption that
    makes `min` and widened-`Horizontal` equivalent.** If it ever fails, `min` has become
    load-bearing, the widened form has become the §3.4.1 bug, and a genuinely discriminating render
    test becomes constructible *at that point*. That is its whole value, and it is the reason the
    extraction to `internal` is worth the small API surface: the invariant is otherwise untestable,
    and an untestable invariant that three sections depend on is exactly the kind of thing that
    breaks silently. **Do not delete this test as redundant** — it looks redundant precisely because
    it is currently passing.

12. **V12 — nested `flex`** resolves correctly, and (E6) does not measurably regress render time at
    nesting depth 2.
13. **V13 — `CheckStructuralSizes` for `Flex` (§4.5.3).** Three cases, and the first is the one that
    matters most:
    - **(a) The AND does not over-report.** `{"split":"flex","maxSize":40}` with two `minSize: 30`
      children → **`--check` reports `ok: true` and emits NO diagnostic.** This is `flex`'s headline
      use case and it must validate clean. *A test suite that omits (a) will happily accept the OR.*
      **Unlike V11, this is a genuinely discriminating test** — see §4.5.3's Revision-4 note.
    - **(b) The AND does report.** A `flex` pane impossible in **both** arrangements → exactly
      **one** diagnostic, code `fixed-sizes-exceed-parent`, severity `Error`, path = the split's own
      path (not `…/children/{i}`), message naming both arrangements.
    - **(c) No regression.** The same two configs declared `"vertical"` and `"horizontal"` produce
      byte-identical diagnostics to current `main`.

**`tools/check-all.sh` must pass.** Per framework `:445`, adding a member to a documented closed set
is *designed* to fail the doc-token check until the README's pane-keys table is updated — do both in
one change.

---

## 7. What must NOT change

- **`PaneSplit`'s existing three members** and their meanings.
- **The normalization default** (framework `:778-779`, `Config.cs:826`) — absent `split` with
  non-empty `children` still normalizes to `vertical`, **not** `flex`.
- **The resolved pane tree.** `Pane.Split` is never rewritten at render time (§1.3).
- **`HeightLadder.cs`** — untouched (§4.2).
- **`Floor()`'s behaviour for `None`, `Horizontal` and `Vertical` panes**, including the `minSize`
  short-circuit (`:331-334`) and the collapse/exclude threading (`:344-348`). §3.4 **adds** a
  branch; it changes nothing existing. V3 guards this; V11 guards the invariant those branches
  satisfy.
- **`CheckSplitBounds` and `CheckHorizontalSplitChildren` themselves.** §4.5.3 adds a `Flex` branch
  to their *caller* and calls both unchanged. In particular, **do not add a `minSize` check to
  `CheckHorizontalSplitChildren`** to close the gap §4.5.3 documents — that changes declared-
  horizontal behaviour and is out of scope (§12). V13(c) guards this.
- **The `fixed-sizes-exceed-parent` code's meaning and severity** (§9.6). Reused, not redefined,
  and no new code is registered by §4.5.3.
- **Load-time border propagation's width-independence** (§4.5.1).
- **Existing drop/clamp behaviour** for declared panes — byte-identical (V2, V3).
- **`--check`'s width-independence** (§4.4).
- **`min` in `Floor()`'s `Flex` branch** — do not "optimise" it into the single-branch form now that
  §3.4.2 proves them equal. See §3.4.3's Revision-4 note.

### 7.1 The row cost — analysis unchanged, one amplification added

Because the decision is width-only and never revisited (§1.2), a `flex` pane can stack to satisfy
width and *then* exceed the row budget, at which point the existing height ladder clips or drops
content. On a very narrow terminal, `"split": "flex"` can trade a dropped pane for dropped *rows*.

**This is inherent, not a defect**, and unavoidable without the height fixpoint §2.8.1 forbids. The
move from `splitFallback` to `flex` did not change this analysis at all — it is a property of the
mechanism, not of the config surface. It does not block implementation: `flex` is opt-in, no
existing config selects it, and §5's note makes it diagnosable.

**Revision 2 adds one amplification, which does not change the framing above.** §3.4 makes a `flex`
pane advertise the *lower* of its two floors to its parent. A parent allocating near the floor will
therefore give a `flex` child less width than it would give a declared-vertical child — so a `flex`
pane in a competitive layout stacks **more readily** than a standalone one. This is correct and
desired: it is the trade the author asked for by writing `flex`, and it is precisely what lets the
parent keep every pane alive instead of dropping one (V10). But it means §7.1's row cost is reached
sooner in exactly the configs where panes compete for width. Bounded in practice by
`size`/`distribute` — a `size: "fill"` flex pane still receives more than its floor. Documented,
not mitigated.

> **Revision 4 sharpening.** §3.4.2's invariant makes this concrete rather than conditional: the
> lower floor is **always** `stackedFloor`. So a `flex` pane advertises its stacked floor to its
> parent in every case, and the "more readily" above is not a tendency — it is the rule. The row
> cost in §7.1 is therefore reached at exactly the width where a declared-horizontal pane would
> reach it, which is a cleaner statement than Revision 2 could make.

---

## 8. Files to touch

| File | Change |
|---|---|
| `src/ClaudeTuiLine/Pane.cs` | `PaneSplit` gains `Flex` (`:6-11`). **No new property** |
| `src/ClaudeTuiLine/Config.cs` | one row in `SplitAccepted` (`:785-792`); `:776` needs no edit per §4.5.1 |
| `src/ClaudeTuiLine/SizeResolver.cs` | `Flex` → effective orientation above both vertical entry points (§4.1); **`Floor()`'s `min` branch (§3.4)**; **extract `SideBySideFloor`/`StackedFloor` as `internal static` methods so V11 can test the invariant (§6 item 11)**; the note (§5); `ResolvedPane.EffectiveSplit` populated in `ResolveNode`; the defensive invariant |
| `src/ClaudeTuiLine/PaneTreeRenderer.cs` | `:78` reads `ResolvedPane.EffectiveSplit`, not `pane.Split` |
| `src/ClaudeTuiLine/BorderGrid.cs` | `:134` reads `ResolvedPane.EffectiveSplit`, not `pane.Split` |
| `src/ClaudeTuiLine/ConfigCheck.cs` | `:647` widen the leaf-check pattern to include `Flex` (§4.4); **`:851-885` add the `Flex` branch per §4.5.3**. `:721`/`:730` and `:933` need no edit |
| `src/ClaudeTuiLine/SchemaCommand.cs` | `split`'s description covers `flex` (`:196`, `:207`) |
| `README.md` | pane-keys table; prose near `:167-170`; distinguish from `size: "fill"` (§2.5) |
| `SPEC-V2-FRAMEWORK.md` | new §2.3.4 (§9) |
| `tests/` | V1–V13; **relabel the shipped `SplitFlexTests.cs:326-351` tests** per §6.1's Revision-4 note |

`AcceptedCommand.cs` needs **no** edit — it consumes `SplitAcceptedTokens`, projected from the
table. **There is no "anywhere the compiler flags" row**; Revision 1 had one, and §2.4 explains why
it was empty.

---

## 9. Framework spec text

Add as **§2.3.4**, after §2.3.3 (`SPEC-V2-FRAMEWORK.md:1229`):

> #### 2.3.4 `flex` — a split whose orientation is a function of width
>
> `"split": "flex"` arranges children side by side when they fit and stacked when they do not. It
> sits side by side while the vertical floor of §2.3 is within the available width, stacks when
> that is exceeded and the horizontal floor is not, and when neither fits stays side by side so the
> drop ladder runs — a rearrangement that does not make the split fit is not worth the change in
> shape.
>
> `flex` names an arrangement policy rather than a divider, which is why it does not follow the
> tmux/vim naming of `vertical` and `horizontal` at `:775`. Those two are unchanged and remain
> absolute: `vertical` is always side by side, `horizontal` always stacked.
>
> The declared value is what the author wrote and is fixed at config resolution per §2.2; the
> *effective* orientation is computed per render and is always concrete. `flex` is a declared value
> only and never reaches anything downstream of the size resolver, which cannot distinguish a
> stacked `flex` pane from a declared horizontal one.
>
> **A `flex` pane's width floor is the lesser of its two orientations' floors**, because it can
> render in either. This matters beyond the pane itself: an ancestor split's drop decision reads a
> child's floor *before* that child's own orientation is decided, so the floor must answer the
> orientation-independent question or the ancestor will drop a pane that could have rendered — or
> drop a sibling competing with it. The side-by-side floor is never below the stacked one — the
> boundary cost always re-adds what a collapsed border exclusion removes — so in practice the lesser
> is the stacked floor; the rule is nonetheless written as the minimum, because it is the minimum
> that follows from "it can render in either," and the ordering is a consequence rather than a
> premise.
>
> **The same rule governs `--check`'s structural-size validation, in dual form: a `flex` pane's
> declaration is reported as over-constrained only when it is over-constrained in BOTH
> arrangements.** A pane whose children cannot share the parent's width but can each take all of it
> is not an error — it is the case `flex` exists for, and reporting it would make `flex` unusable.
> Floors take the minimum over adoptable orientations; impossibility takes the conjunction. These
> are the same statement. Note that the two `--check` predicates are *not* in the ordering relation
> the two floors are, so the conjunction is doing real work there.
>
> The decision is made from width alone and is never revisited once rows are known. Reverting it
> because the stacked result busts `maxRows` would make width depend on height, the fixpoint §2.8.1
> forbids. Stacking can therefore cost rows; the render note is what makes that visible. Because a
> `flex` pane advertises the lower floor, it is allocated less width in a competitive layout than a
> declared-vertical pane would be, and so stacks more readily there — the price of the parent being
> able to keep every pane alive.
>
> **Border-edge propagation runs at config load, before any width is known, and treats a `flex`
> pane as vertical.** A stacked `flex` pane may therefore carry edges chosen for the side-by-side
> arrangement. This is accepted: the alternative is a width-dependent resolved tree, which §2.2
> forbids.
>
> `gutter` and `distribute` apply while the pane is side by side and are ignored while it is
> stacked, exactly as §2.3.2 ignores them on a declared horizontal split. They are never diagnosed
> as inapplicable on a `flex` pane, because it can render side by side. Beyond the structural check
> above, `--check` predicts none of this: per §9.8 it is width-independent.

---

## 10. NEEDS-EVIDENCE

- **E1 — the dispatch site.** §4.1 asserts `SizeResolver.cs:172`/`:177` is the single point
  selecting a resolver. Confirm and give the file:line. *If the resolvers are called from several
  scattered sites* → introduce one `EffectiveSplit(Pane, int availWidth, RenderNoteCollector)`
  helper and route every call through it; report the sites changed.

- **E2 — are child floors available at the dispatch point?** §3.1's predicate needs both branch
  values before choosing an orientation. `Floor()` computes both and is callable, so this is
  probably a non-issue — confirm it, and report the cost of calling `Floor()` twice at dispatch *on
  top of* §3.4's own doubling. **Report before restructuring the resolver.**

- **E3 — the manual audit.** ✅ **RESOLVED.** Outcome in §4.5's table and §4.5.2: E7 clean, three
  `ConfigCheck` rows correct-as-is or covered by §4.4, one genuine hole ruled on in §4.5.3. Retained
  as a procedure for any future `PaneSplit` change, since §2.4 means the compiler will never do it.

- **E4 — `tools/check-all.sh` for a widened closed set.** Framework `:445` says adding a member is
  designed to fail the doc-token check. Run it after adding the token and report exactly which
  checks fail and what each wants updated.

- **E5 — the duplicated pane record.** `Pane.cs` declares `maxRows` twice (`:126`, `:174`),
  implying two pane-shaped types. Confirm whether both carry `Split`. Lower stakes now that E7
  resolved to an existing carrier.

- **E6 — `Floor()`'s cost under nested `flex`** (§3.4.3). Measure at nesting depth 2 before adding
  any memoisation. *If negligible* → change nothing and say so. *If measurable* → report the
  numbers and the proposed cache key **before** adding a cache. **Do not resolve E6 by dropping the
  second branch** — §3.4.3's Revision-4 note.

- **E7 — the effective-orientation carrier.** ✅ **RESOLVED.** `ResolvedPane` exists and is the
  carrier; it needs an `EffectiveSplit` field populated in `ResolveNode` and read at
  `PaneTreeRenderer.cs:78` / `BorderGrid.cs:134`. The structural-change branch did not materialise
  and its escalation trigger is stood down.

---

## 11. Confidence, and what is left to the user

**High** on the config surface (§2), the threshold (§3.1), the ordering constraints (§1.2, §1.3),
and **§4.5.3's AND ruling** — which is settled by §3.4's argument in dual form, plus a concrete
config that the alternative would reject, plus the Revision-4 observation that the two `--check`
predicates are genuinely not in the floors' ordering relation.

**High** on **`Floor()`'s `min` contract (§3.4)**, but on **different grounds than Revisions 2 and 3
claimed**. Those revisions rested it on §3.4.2's counter-example; that example was unrealizable and
the ruling is *not* settled by it. It is settled by §3.4.1 — `min` is the direct statement of "a
pane that can render either way has the lesser floor" — with §3.4.2's proof showing the alternative
is merely conditionally equivalent rather than wrong. **A reader who wants the argument for `min`
should read §3.4.1, not §3.4.2.**

**High** on §4.5's plumbing now that E7 resolved to an existing carrier. Both of Revision 2's
pre-declared escalation triggers have fired or been stood down. **No Ultra-Advisor escalation
recommended, and no open triggers remain** — the one escalation that did run (the V11 question) is
adjudicated and its outcome is folded into this revision.

**Medium** on one thing only: §4.5.3's message wording, which I have specified by requirement
("must state that both arrangements were tried") rather than verbatim. That is the Implementor's to
write.

**A note on what Revision 4 says about this spec's own method.** §3.4.2's error was not a slip in
arithmetic; it was reasoning about the code from a simplified model of it (a discount treated as
free of its boundary) and then presenting the model's output as a fact about the implementation.
That is the same failure §1.1 warns about — a second statement of a rule, free to disagree with the
first — appearing in a section written *to* enforce §1.1. Worth remembering when the next
"obviously the branches can diverge" argument comes up.

**Left to the user:**

1. **The name `flex` itself** (§2.5) — one letter from `"fill"`, which `size` and `height` both
   accept with a different meaning. Flagged so the README can disambiguate rather than inherit it.
2. **Whether the §7.1 row cost is acceptable**, now known to arrive *sooner* in competitive layouts
   than the Revision-1 analysis implied, and now known (Revision 4) to arrive there *always* rather
   than merely typically. Inherent, not a defect. If a stack-then-clip outcome proves worse than the
   original drop, the answer is not to revert the stack (forbidden, §1.2) but to reconsider whether
   `flex` suits panes with a tight `maxRows`.
3. **Whether §4.5.3's under-report is worth closing** — see §12's second item. It is a pre-existing
   gap that `flex` inherits, it fails open, and closing it changes declared-horizontal behaviour.

---

## 12. Incidental findings — NOT part of this task

**(1) A stale citation.** `SPEC-V2-FRAMEWORK.md:98` cites `split`'s parser and accepted table as
`Config.cs:483` / `Config.cs:474`; actual locations on `main` are **`Config.cs:794`** and
**`Config.cs:785`**. `SPEC-V2-FRAMEWORK.md:544` similarly cites `Config.cs:481` for
`SplitAcceptedTokens`, now `:792`. Pre-existing drift, mildly ironic given §1.1 is the section about
citations drifting. This task edits `SplitAccepted`, so whoever fixes the citations should do it
after #88 lands.

**(2) `CheckHorizontalSplitChildren` has no `minSize` counterpart.** `ConfigCheck.cs:913-931` checks
only `FixedSize(child) > bound`. A declared-horizontal split with a child whose `minSize` exceeds
the parent's bound is structurally impossible and goes **undiagnosed today**. Found while ruling on
§4.5.3, where the gap propagates into the AND as a knowing under-report.

**Deliberately out of scope for #88** — closing it changes what `--check` reports for existing
declared-horizontal configs and could newly reject configs in the field, which is a product decision
and not this task's. Flagged for the Orchestrator to route separately. If it is fixed later, §4.5.3's
under-report closes for free with no change to the flex branch, which is a point in favour of the AND
formulation: the flex behaviour is defined in terms of the two checks, so it inherits their
improvements automatically.

**(3) `gutter` and `minSize` are not validated as non-negative.** `Config.cs:694` parses them with
no sign check. Found via the Revision-4 adjudication: §3.4.2's `SS ≥ ST` proof assumes non-negative
gutters and floors, and a negative gutter is the **only** route to a genuine `sideBySideFloor <
stackedFloor` — the case §3.4.2 otherwise proves unreachable.

**Low priority, and not a blocker for #88.** A negative gutter is degenerate, unsupported config
that nobody has written, and V11's invariant test would catch the consequence if one ever appeared.
But it is the single assumption the §3.4.2 proof rests on that the code does not enforce, which is
worth a `minimum: 0` on both keys whenever someone is next in `Config.cs`'s numeric parsing. **Its
existence is also the reason `min` is not merely ceremonial** — it is the one place where the
invariant is a config-validation guarantee rather than an arithmetic one.
