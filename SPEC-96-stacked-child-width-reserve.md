# SPEC-96 — a stacked split grants every child its own OUTER width, so every child overflows the parent's border box

STATUS: **DIAGNOSIS CONFIRMED FROM SOURCE. FIX RULED. READY TO IMPLEMENT.**
One scope question is open for Jim (section 8.1); it does not block the fix.

Ticket: #96. Written against `/Users/jimcline/git/repos/claude-tui-line`, branch `main`
@ `1f938b5` ("Add itemSettings.worktree.showBranch, default off").

**No spec path was dictated in the dispatch; this follows the house convention
(`SPEC-<ticket>-<slug>.md` at the repo root, next number after 95). Rename freely —
nothing references it yet.**

> **Citations are anchored by commit and quoted by content.** Per SPEC-92's standing
> warning: line numbers in this repo drift while specs are in flight. **Do not target an
> edit below by line number alone — match the quoted text.**

---

## 0. READ THIS FIRST — three premises in the dispatch brief are false

The brief tasked this spec with auditing "every width/height allocation path for the same
missing-clamp pattern," on the theory that `SPEC-2.3-residual-pane-overwidth.md` documents a
partially-unfixed defect with a third drop loop never patched. **That theory is stale. All
three sites are patched, and the vertical axis is sound.** Implementing the brief as written
would have produced a no-op patch over already-correct code while the live defect kept
shipping.

| Brief's premise | Actual state at `1f938b5` | Evidence |
|---|---|---|
| `ResolveVerticalEven` "was never patched at all (no `overAllocated` check)" | **Patched.** It has the over-allocation check, `DropFloor`, a `RenderNoteCollector`, and the `ClampToAvail` call. | `SizeResolver.cs` `ResolveVerticalEven`'s loop exit calls `ClampToAvail(result, avail, splitOuterWidth, notes)`; merged as `6ddd558` ("Merge #78: ResolveVerticalEven parity fix"). STATUS.md's `#78` entry lists all five ported items. |
| Three drop-loop sites share an unclamped `Count<=1` exit | **All three clamp.** `AllocateWithDrop`, `ResolveVerticalMinRows`, and `ResolveVerticalEven` each `return ClampToAvail(...)` at that exit. | Three call sites of `ClampToAvail`, one per loop. |
| The flex/stacking reflow path has "the SAME missing-clamp defect" | **Half right, for the wrong reason.** The stacked path is indeed defective, but it contains *no allocation and therefore no exit to clamp*. It is a missing **subtraction**, not a missing clamp. | Section 1. |

The brief's instinct that "this is bigger than the three drop-loop sites" was correct, and its
instinct that the flex-to-stacked reflow is implicated was correct. The mechanism is not the
one it predicted. **Deliverable 1 (an inventory of unclamped allocation exits) comes back
nearly empty — that inventory is in section 2, and it is evidence the clamp theory was wrong,
not the fix.**

One further correction: `SPEC-95-flex-side-by-side-wrapped.md` §5.4(a) introduced a cached
first-pass allocation reused by `ResolveVerticalMinRows`. That looked like a prime candidate
for a drop-loop bypass. **It is not** — the cache is consumed *inside* the retry loop
(`var result = pendingFirstPass ?? AllocateMinRowsOnePass(...)`, immediately followed by
`pendingFirstPass = null`), so a cached result still faces the `tooSmall`/`overAllocated`
checks and still exits through `ClampToAvail`. Hypothesis eliminated by evidence; recorded so
nobody re-opens it.

---

## 1. The defect

### 1.1 The one-line cause

`SizeResolver.ResolveNode`, stacked branch:

```csharp
if (effectiveSplit == PaneSplit.Horizontal)
{
    var horizontalChildren = pane.Children
        .Select(c => ResolveNode(c, outerWidth, ctx, values, compounds, measureOverride, rowCountOverride, notes, collapse))
        .ToList();
    return new ResolvedPane(pane, outerWidth, horizontalChildren, EffectiveSplit: PaneSplit.Horizontal);
}
```

Every stacked child is resolved at **`outerWidth`** — the parent's *outer* width. The parent's
own border reserve is never subtracted.

Its vertical sibling, four lines below, does subtract it. `ResolveVertical`,
`ResolveVerticalMinRows`, and `ResolveVerticalEven` all open with the same line:

```csharp
var avail = Math.Max(0, splitOuterWidth - BoundaryCost(split, children.Count, collapse));
```

and `BoundaryCost` is defined as:

```csharp
OwnBorderReserve(split) + (collapse ? Math.Max(0, childCount - 1) : split.Gutter * Math.Max(0, childCount - 1))
```

— the parent's own reserve **plus** gutters. Stacked children genuinely need no gutters (they
are separated by rows, not columns), but they need the reserve exactly as much as side-by-side
children do. The stacked branch subtracts neither.

### 1.2 Why it is invisible without a border

```csharp
internal static int OwnBorderReserve(PaneBorder border) => border.Style is null
    ? 0
    : 2 + (border.Edges.Left ? 1 : 0) + (border.Edges.Right ? 1 : 0);
```

`reserve == 0` when no border style is set, so the bug is a no-op for unbordered panes. When a
style *is* set the reserve is **2 to 4 columns** — the unconditional 2 is padding (per
`Program.cs`: *"2 verticals + 2 padding cells"*), charged even with both vertical edges off.

**Preconditions, both required:** the parent is bordered, and its effective split is stacked.

### 1.3 The full causal chain, all six steps confirmed from source

1. **Over-grant.** `ResolveNode` gives each stacked child `OuterWidth == parent.OuterWidth`.
2. **The renderer disagrees.** `PaneTreeRenderer.Render` computes the parent's content box as
   `var preSuppressionInnerWidth = Math.Max(0, node.OuterWidth - borderReserve);` — narrower
   than what the resolver handed the children.
3. **Children render over-wide.** The stacked branch renders each child from its *own*
   `OuterWidth`, which is the parent's outer width, so its rows come back `reserve` columns
   wider than the box they are going into.
4. **Nothing truncates them.** `PadToWidth` is
   `rows.Select(r => r.Width >= width ? r : new PaneRow(r.Markup + new string(' ', width - r.Width), width))`
   — pad-only. `AlignBox` uses `var deficit = Math.Max(0, targetWidth - row.Width);`, which is
   `0` for an over-wide row. Both pass an over-wide row through **unchanged**.
5. **The border is drawn on top.** `PaneBorderRenderer.Wrap` computes `outerWidth = width +
   reserve` from the `innerWidth` it was passed and prepends/appends glyphs, so the composed
   row ends up wider still.
6. **The pane lies about its width.** `Render` returns
   `new Compositor.PaneContribution(new PaneBuffer(borderedRows), node.OuterWidth, ...)` — it
   declares `node.OuterWidth` while its rows are physically `reserve` wider. The Compositor
   places the next contribution at the declared width, so the over-wide rows **overlap** it,
   and at the surface's right edge they **clip**.

This is the reported symptom verbatim: *"the statusline's right side clips/overlaps."* The
"overlaps" half is step 6 and is the tell — a pure clip would not overlap anything.

### 1.4 Why every pane clips at once, and why it looked like a sudden regression

The over-grant is applied to **every child of the stacked split in the same `.Select(...)`**,
so all of them overflow simultaneously. That matches Jim's report precisely: *"in flex when
resizing so the panes stack, then all the panes have it."* A per-pane defect could not do
that; an arrangement-level one does it by construction.

**Nesting compounds it.** A stacked child that is itself a bordered stacked split over-grants
again, so overflow accumulates at `reserve` per bordered stacked level down the tree.

**The "Opus 5" trigger.** The longer model+effort string raises `sideBySideNeed`, which is
compared against `outerWidth` in `ResolveFlexOrientation`. Once it exceeds the width, flex
flips to stacked — and the *latent* stacked defect activates. **The model-name growth is the
trigger, not the cause.** This is why it presented as a sudden regression in code that had not
changed: the config crossed a threshold into a defective code path. It also means the bug is
not new; it has been reachable since `8437c37` (#88, `split:"flex"`) for any bordered stacked
split, and since long before that for a declared `split:"horizontal"` with a border.

### 1.5 A second consequence: the wrong width is persisted

`Program.cs`'s `StampPaneWidths` writes the item-width cache from the resolved tree:

```csharp
var borderReserve = SizeResolver.OwnBorderReserve(pane);
var innerWidth = Math.Max(0, node.OuterWidth - borderReserve);
...
ItemCache.WriteWidth(widthsDir, ItemCache.WidthKeyFor(id, command, cwd, surfaceWidth), innerWidth);
```

An over-granted stacked child therefore persists an inflated `innerWidth`, which returns to
user commands as `CLAUDE_TUI_LINE_PANE_WIDTH` on a later render. A command that sizes its own
output to that variable renders too wide on a *subsequent* frame, after the layout that caused
it is gone. This is a plausible second reason the user experiences the problem as continuous
("happening all the time") rather than only at the moment of reflow.

**Consequence for verification:** entries are keyed by `surfaceWidth`, so they age out as the
terminal is resized, but a user sitting at one fixed size may keep seeing a stale width until
the TTL expires. **Do not read a lingering symptom immediately after the fix as the fix having
failed** — confirm against `--preview --json` (which reads the resolved tree directly) before
concluding anything.

---

## 2. Deliverable 1 — inventory of every width-granting site

Every place a width is chosen for a pane, with its disposition. "Grant exit" means a point
that decides a child's `OuterWidth`.

| # | Site (`SizeResolver.cs`, match by content) | What it grants | Clamped to `avail`? | Disposition |
|---|---|---|---|---|
| 1 | `ResolveNode` leaf: `return new ResolvedPane(pane, outerWidth, Array.Empty<ResolvedPane>(), EffectiveSplit: PaneSplit.None)` | passes through its own grant | n/a — no children | **OK.** Cannot over-grant; it allocates nothing. |
| 2 | `ResolveNode` stacked: `.Select(c => ResolveNode(c, outerWidth, ...))` | parent's **outer** width to every child | **NO — and no `avail` is ever computed here** | **THE DEFECT.** Section 3.1. |
| 3 | `ResolveNode` vertical: `ResolveNode(alloc.Children[i], alloc.Grants[i], ...)` | the allocator's grant | yes, upstream | **OK.** Every producer of `alloc` clamps (rows 4–6). |
| 4 | `AllocateWithDrop` exit: `if ((!tooSmall && !overAllocated) \|\| current.Count <= 1) { return ClampToAvail(...); }` | greedy grants | **yes** (#74, `b6c9ac0`) | **OK.** |
| 5 | `ResolveVerticalMinRows` exit: same guard, `return ClampToAvail(...)` | min-rows grants | **yes** (#74) | **OK.** Also correct for the SPEC-95 cached first pass — section 0. |
| 6 | `ResolveVerticalEven` exit: same guard, `return ClampToAvail(...)` | even grants | **yes** (#78, `6ddd558`) | **OK.** Contradicts the brief. |
| 7 | `AllocateOnePass` — `return new AllocResult(children, grants)` | raw greedy pass | no | **OK by construction.** Reached only from row 4's loop, which clamps. Not a public exit. |
| 8 | `AllocateMinRowsOnePass` — `return new MinRowsPassResult(new AllocResult(children, grants), feasible)` | raw min-rows pass | no | **OK by construction.** Reached from row 5's loop and from `ResolveFlexOrientation`'s predicate; the latter's result re-enters row 5's loop. |
| 9 | `AllocateEvenOnePass` — `return new AllocResult(children, grants)` | raw even pass | no | **OK by construction.** Reached only from row 6's loop. |
| 10 | `SolveMinRows` over-constrained fallback — `return lo` | each candidate's floor, ignoring `r` | no | **OK by construction.** Documented to over-allocate on purpose; row 5's `overAllocated` check exists precisely for it. |
| 11 | `WaterFillSurplus` — `return widths` | surplus distribution | bounded by `hi[]` | **OK.** Feeds row 5. |
| 12 | `ClampToAvail` itself | tightens grants | — | **OK at its call sites, but see section 3.3** — it is a *per-child* clamp, not a sum clamp. |

**Height:** the brief asked for height allocation too. `SizeResolver` resolves **width only**;
the row axis lives in the height ladder and `OwnRowReserve`. No height grant exit exists in
this file to audit. Whether the stacked row axis has an analogous defect is **out of scope
here and explicitly not investigated** — flagged as NE-3 in section 6 rather than silently
implied to be clean.

**Conclusion: exactly one defective site, and it is the only one that performs no allocation.**
An audit scoped to "allocation exits that return without clamping" — precisely what the brief
asked for — would have inspected rows 4, 5, 6, found them all correct, and reported no defect.

---

## 3. Deliverable 2 — the fix

### 3.1 Charge the parent's reserve in the stacked branch (the actual bug fix)

In `SizeResolver.ResolveNode`, replace the stacked branch's body. Match on the quoted text in
section 1.1.

```csharp
if (effectiveSplit == PaneSplit.Horizontal)
{
    // §2.2/§2.10: stacked children share the split's width instead of dividing it, so they are
    // charged no gutters — but the split's OWN border reserve is not theirs to spend, exactly as
    // BoundaryCost charges it on the vertical side. Granting outerWidth here made every stacked
    // child overflow its parent's content box by reserve(p) at once.
    var stackedAvail = Math.Max(0, outerWidth - OwnBorderReserve(pane));
    var horizontalChildren = pane.Children
        .Select(c => ResolveNode(c, stackedAvail, ctx, values, compounds, measureOverride, rowCountOverride, notes, collapse))
        .ToList();
    return new ResolvedPane(pane, outerWidth, horizontalChildren, EffectiveSplit: PaneSplit.Horizontal);
}
```

Three points the Implementor must not get wrong:

- **`OwnBorderReserve(pane)`, not `BoundaryCost(pane, 1, collapse)`.** The two are arithmetically
  equal here (one child ⇒ zero gutters), and reusing `BoundaryCost` would honour §9.5's
  call-the-same-function rule. **Reject it anyway:** passing `childCount: 1` for a split that has
  N children is a lie that happens to compute the right number, and the next person to read it
  cannot tell whether it is deliberate. `OwnBorderReserve` *is* the shared component —
  `BoundaryCost` is literally defined as it plus gutters — so calling it directly duplicates
  nothing.
- **The returned `ResolvedPane` keeps `outerWidth`, not `stackedAvail`.** The node's own outer
  width is unchanged; only what it hands *down* changes. Getting this backwards would shrink the
  pane itself and break `PaneTreeRenderer`'s `node.OuterWidth - borderReserve`.
- **No collapse interaction.** Unlike the vertical branch, `PaneTreeRenderer`'s stacked branch
  calls `Render(child, ...)` **without** `excludeLeft`/`excludeRight` (they default to `false`),
  so stacked children never exclude edges and no `collapse`-aware per-child reserve applies on
  the width axis. Do **not** add `excludeLeft`/`excludeRight` plumbing here; it would silently
  disagree with the renderer.

### 3.2 Do not "fix" the renderer

`PadToWidth` and `AlignBox` are pad-only (section 1.3 step 4), and it is tempting to make them
truncate as a belt-and-braces measure. **Do not.** `SPEC-2.3-residual-pane-overwidth.md` already
rejected exactly this — *"Clip at render time — let the allocator return 50 and have the renderer
truncate to 20"* was considered and rejected in favour of clamping in the allocator. The
established contract is: **the allocator guarantees widths fit; the renderer trusts them.**
Adding truncation would mask future allocator bugs instead of surfacing them, and would
contradict a ruling already on the record.

### 3.3 Fix `ClampToAvail`'s name, or its contract — a live trap

`ClampToAvail` clamps **each grant individually** against `avail`:

```csharp
if (result.Grants[i] <= avail) { continue; }
grants[i] = avail;
```

`avail` is the budget for **all** children collectively. So a two-child result granting 15 and
15 against `avail == 20` passes untouched: neither child individually exceeds 20, but their sum
overflows by 10.

This is **not a live defect** — both call-site guards reach `ClampToAvail` only when the sum
already fits (`!overAllocated`) or when exactly one child remains (`Count <= 1`), and a
single-child clamp against `avail` is exactly right. But the helper's name, and its doc comment
("clamp every returned grant to the split's available width"), promise a general safety net it
does not provide. The next person to call it from a multi-child exit gets silence.

**Ruled: add an assertion, do not change the behaviour.** At the top of `ClampToAvail`, after the
empty check:

```csharp
// Callers reach this only via a guard that already established the sum fits (!overAllocated) or
// that a single child remains. The per-child clamp below is exact for both; it is NOT a sum
// clamp, and a multi-child over-allocated result would pass through it untouched.
Debug.Assert(result.Grants.Count <= 1 || result.Grants.Sum() <= avail,
    "ClampToAvail is a per-child clamp; a multi-child result must already satisfy Sum <= avail.");
```

Renaming it to `ClampEachToAvail` would be clearer still, but the name is pinned in
`SPEC-V2-FRAMEWORK.md` §9.8.1's note registry and in STATUS.md's #74/#78 entries. **Do not
rename it as part of this task** — that is a docs-and-code change with its own blast radius.
Recorded here as a known naming wart.

---

## 4. Deliverable 3 — why this must be structural, and what "structural" means here

### 4.1 The drift is real, and it has already defeated two countermeasures

The brief asks for a shared clamp helper so a fix cannot land in three of four places. **That
countermeasure has already been tried here and already failed:**

- **#74 introduced `ClampToAvail` — the shared helper.** It landed at two of the three drop
  loops. STATUS.md records the third as *"deliberately left untouched (tracked separately as
  #78)."* A shared helper is still N call sites, and N call sites still drift.
- **#78 then added a tripwire test**, described in STATUS.md as *"a structural tripwire test
  asserting all three loops emit drop notes with the shared wording, so a future fix landing in
  two of three loops (as happened here) fails a test instead of shipping silently."* Correct
  instinct — but it **enumerates the three known drop loops**. A path with no drop loop is
  invisible to it. The live defect is in exactly such a path.

Each countermeasure sat one level below the failure. A third helper would sit there too.

### 4.2 The drift is not where the brief thinks

The brief frames this as REPORT-92's pattern: one arm of a mirrored width computation fixed
while its sibling arm is not. The real axis here is different and worse. **Three separate files
each branch on `EffectiveSplit == Vertical` and treat stacked as the unexamined fallthrough:**

- `SizeResolver.ResolveNode` — `if (effectiveSplit == PaneSplit.Horizontal)` ... else vertical
  allocation
- `PaneTreeRenderer.Render` — `else if (node.EffectiveSplit == PaneSplit.Vertical)` ... else
  stacked
- `BorderGrid` — `if (node.EffectiveSplit == PaneSplit.Vertical)`

The vertical arm in each is elaborate and well-tested; the stacked arm is the short one nobody
revisits. SPEC-88 Revision 2 already discovered this class the hard way and wrote it down:
*"there is no `switch` on `PaneSplit` anywhere in `src/`, so Revision 1's claim that the compiler
would enumerate the sites needing a decision was **false**."* The compiler will not help. A
fourth shared helper adds a fourth site to remember.

### 4.3 The ruling: assert the invariant on the output tree

**Add a property test that validates the resolved tree itself, referencing no resolver, no exit
point, and no arrangement-specific code path.** For every `ResolvedPane` node with children:

- **side-by-side** (`EffectiveSplit == Vertical`):
  `Σ child.OuterWidth + BoundaryCost(node.Source, node.Children.Count, collapse) ≤ node.OuterWidth`
- **stacked** (`EffectiveSplit == Horizontal`):
  `child.OuterWidth ≤ node.OuterWidth − OwnBorderReserve(node.Source)` for **every** child
- **leaf**: no constraint
- **universal**: `node.OuterWidth ≥ 0`, and `EffectiveSplit` is never `Flex`

This is drift-proof in the way a helper is not, because it names no implementation site. A
fourth arrangement, a fifth resolver, or a new grant exit cannot escape it by being forgotten —
the only way to evade it is to produce a tree that already fits, which is the property we
actually want. It also subsumes the #78 tripwire's intent without enumerating anything.

### 4.4 Secondary: one choke point for handing a width down

`ResolveNode` recurses in exactly two places — the stacked `.Select(...)` and the vertical
`for` loop. Route both through one helper so the reserve arithmetic has a single home:

```csharp
private static ResolvedPane ResolveChild(Pane child, int grantedWidth, int availWidth, /* ...context... */)
{
    Debug.Assert(grantedWidth <= availWidth, "a child may never be granted more than its parent has to give");
    return ResolveNode(child, grantedWidth, /* ...context... */);
}
```

**This is a convenience and a Debug-build tripwire, not the guarantee** — section 4.3 is the
guarantee. Keep it small; if threading the context through makes it uglier than the two call
sites it replaces, **skip it and say so in the report**. Do not let it grow into a refactor of
`ResolveNode`'s signature.

---

## 5. Deliverable 4 — tests

Add to `tests/ClaudeTuiLine.Tests/SplitFlexTests.cs` unless noted. The existing 17 stacked
assertions there all check `EffectiveSplit` and **none constrains a child's `OuterWidth`** —
that gap is why this shipped.

**Bug-fix cases (T1–T6 must fail before the fix and pass after):**

- **T1 — stacked bordered parent.** Bordered pane, `split:"horizontal"`, two children, outer
  width 80. Assert **every** child's `OuterWidth == 80 - OwnBorderReserve(parent)`. Fails today
  (returns 80).
- **T2 — stacked UNBORDERED parent (over-correction guard).** Same config, no border style.
  Assert every child's `OuterWidth == 80`, unchanged. Must be green **before and after**; it
  pins that the fix is inert when `reserve == 0`.
- **T3 — per-edge reserve arithmetic.** Parametrised over `Edges.Left`/`Edges.Right`
  combinations: assert the child width tracks `2 + left + right` exactly (reserve 2, 3, and 4).
  Catches a fix that hardcodes 2 and ignores the edge flags.
- **T4 — nested stacked compounding.** Bordered stacked parent containing a bordered stacked
  child. Assert the grandchild's width is reduced by **both** reserves, proving the fix applies
  at every level rather than only at the root.
- **T5 — flex reflow across the threshold (the headline repro).** One config, resolved at a wide
  width (side-by-side) and a narrow width (stacked). Assert the section 4.3 invariant holds at
  **both**, and assert `EffectiveSplit` actually differs between them — otherwise the test can
  pass while never exercising the stacked path at all.
- **T6 — the "Opus 5 : high" scenario, concretely.** A content-sized leaf whose text is long
  enough that `sideBySideNeed > outerWidth` forces the flip to stacked, inside a bordered flex
  parent. Assert the invariant. This is the user-reported case; write it with a literal
  long model-name string so it stays readable as a repro. Note that the *specific* string is
  incidental — T5 covers the mechanism; T6 documents the report.

**Structural / regression:**

- **T7 — the tree invariant property test (section 4.3).** Walk the resolved tree over a matrix:
  `{vertical, horizontal, flex} × {greedy, min-rows, even} × {bordered, unbordered} ×
  {collapse true, false} × several widths spanning the reflow threshold`. Assert the invariant at
  every node. **This is the deliverable that prevents the next instance** — put it in its own
  test file (`ResolvedTreeInvariantTests.cs`) so it reads as a global property rather than a
  flex-specific case.
- **T8 — end-to-end render width.** Render a bordered stacked pane and assert **no produced row
  is wider than the node's declared `OuterWidth`**. This catches the whole chain of section 1.3
  at the renderer, independent of the resolver's internals — the assertion that would have caught
  the original report directly.
- **T9 — drop-loop overwidth still clamps (guards #74).** The brief's requested last-child case:
  a `size:50` pane in a 20-column split. Assert the clamp fires and emits
  `"pane {n}: {requested} columns requested, clamped to {avail} at {splitOuterWidth} columns"`.
  Expected **already green** — it pins #74 against regression from this work.
- **T10 — even-split overwidth still clamps (guards #78).** Same, via `distribute:"even"`.
  Expected already green.
- **T11 — existing stacked assertions stay green.** The 17 `EffectiveSplit` assertions must not
  move. If any goes red, the fix changed an orientation *decision* rather than only a granted
  width — **stop and report**, do not adjust the test.

---

## 6. NEEDS-EVIDENCE

I do not run code. These are for the Implementor to establish **before** or **during**
implementation, with what each result decides.

- **NE-1 (blocking the test expectations, not the fix).** Run the full suite on `main` at
  `1f938b5` and record the pass count. STATUS.md's #78 entry cites **1409/1409**. If the
  baseline is not green, stop and report — every "fails before / passes after" claim in
  section 5 is meaningless against a red baseline.
- **NE-2 (may expand scope).** After implementing section 3.1, run T7 (the invariant property
  test) across the full matrix. **If it flags nodes beyond the stacked case**, those are
  additional pre-existing defects this spec did not predict. **Stop and report the list rather
  than fixing them inline** — each needs its own ruling, and a property test discovering extra
  violations is a success, not a reason to widen this task silently.
- **NE-3 (scope boundary, deliberately not investigated).** The row axis was not audited (section
  2). Determine whether `PaneTreeRenderer`'s stacked branch has an analogous row-reserve
  omission — the stacked branch advances `cursorRow` per child and computes
  `targetInnerHeight = targetHeight - OwnRowReserve(pane)`. If a defect exists there it is a
  **separate ticket**; report it, do not fix it here.
- **NE-4 (confirms the section 1.5 tail).** Reproduce Jim's config at a stacking width, then
  inspect the written width-cache entries. Confirms whether stale inflated widths are actually
  persisted, and tells us whether the fix needs a cache-invalidation note in STATUS.md. Not
  blocking; if it is awkward to observe, say so and move on.

---

## 7. What must NOT change

- **`ClampToAvail`'s behaviour or name.** Section 3.3 — assertion only. The name is pinned in
  `SPEC-V2-FRAMEWORK.md` §9.8.1's registry and in STATUS.md.
- **All three drop loops.** They are correct. Touching them re-opens #67a/#71/#74/#78.
- **Any orientation decision.** `ResolveFlexOrientation`, `SideBySideNeed`, `StackedFloor`, and
  the SPEC-94 amendment's no-cap content measurement are settled by an Ultra-Advisor ruling. This
  spec changes **what a stacked split grants**, never **when a split stacks**. T11 guards this.
- **`PadToWidth` / `AlignBox` pad-only semantics.** Section 3.2.
- **The `ResolvedPane` record shape.** No new field is needed.
- **`ConfigCheck`'s flex AND-semantics.** SPEC-88 Rev 3 ruled that a flex pane reports only when
  over-constrained in *both* arrangements. It is tempting to blame that rule for letting this
  through — it did not. This is a runtime arithmetic bug, not a validation gap, and a
  config-time check could not have caught it.

---

## 8. Open decision for Jim — NOT mine to make

### 8.1 Should a stacked child honour its own `size` / `minSize` / `maxSize`?

Today a stacked child's declared size is **ignored entirely** — every child receives the full
parent width. §2.8 (width allocation among stacked children) is unimplemented, and
`ConfigCheck` is written around that assumption (a horizontal parent compares each child's
fixed size against the parent bound *individually*, never as a sum).

Section 3.1 fixes the overflow **without** implementing §2.8: children still all get the same
width, just the correct one. Options:

- **(A) Fix the overflow only — recommended.** Small, provable, matches the reported bug, no
  config semantics change. `size` on a stacked child stays a no-op, as it is today.
- **(B) Also implement §2.8.** Stacked children honour `size`/`minSize`/`maxSize`. A real
  feature with its own spec, its own drop-behaviour questions, and `ConfigCheck` implications.
- **(C) Reject `size` on stacked children at config-check time** so it is visibly ignored rather
  than silently so.

**I recommend (A) and have specified it.** (B) and (C) are product decisions about what `size`
means, which is Jim's call, not mine. **If he picks (B) or (C), re-spec — do not improvise it
into this change.**

---

## 9. Implementation order

1. **NE-1**: baseline test run. Record the count. Stop if not green.
2. **T1–T4, T8** — write them and watch them **fail**. A fix whose tests never failed proves
   nothing.
3. **Section 3.1** — the one-line reserve fix. T1–T4 and T8 go green.
4. **T5, T6** — the flex reflow and reported-scenario cases.
5. **T9–T11** — regression guards. Expect green throughout; if T11 goes red, **stop and report**.
6. **Section 3.3** — the `Debug.Assert` in `ClampToAvail`.
7. **T7 + `ResolvedTreeInvariantTests.cs`** — the property test. Then **NE-2**: if it flags
   anything beyond the stacked case, stop and report the list.
8. **Section 4.4** — the `ResolveChild` choke point, **only if it stays small**. Skip and say so
   otherwise.
9. Full suite. Compare against NE-1's baseline; account for every delta.
10. **STATUS.md** entry, matching the house format of the #74/#78 entries: the defect, the fix,
    the merge commit, and explicitly that **the three drop loops were audited and found already
    correct** — so the next person does not re-derive section 0.

---

## 10. Verification

- Every test in section 5 behaves as its bullet states, including the ones expected green.
- Full suite ≥ NE-1's baseline, with every difference explained.
- `--preview --json` on Jim's real config at a stacking width: no pane's `OuterWidth` exceeds its
  parent's inner width anywhere in the tree.
- Visual check at a terminal width that forces stacking: the right edge is intact, nothing
  overlaps. **Re-read section 1.5 first** — a stale cached width can outlive the fix and must not
  be mistaken for failure.
