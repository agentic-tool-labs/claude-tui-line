# §2.6 — the vertical ellipsis marker is a trailing splice, not a replacement row

Task #27 follow-up. Written against `/Users/jimcline/git/repos/claude-tui-line` (main) and
`/Users/jimcline/git/repos/claude-tui-line-task-27` @ `4c90382` (impl2's diff).

Placed in the main repo root, not the worktree, so it survives the worktree being removed.

**Amended once (A1):** §5(b) originally left the ordering against §3.1 undecided because I had not
read §3.1. I have now, and it is ruled — **this task is sequenced after the §3.1 block model.** See
§5(b), which is the one part of this file that changes what happens next.

**Amended again (A2):** §4's seam moved from a single pane-wide `Wrap` call to the per-producer-unit
call at §3.1's concatenation site. See the A2 block at the end of §4.

**Amended again (A3) — THE IMPLEMENTABLE ALGORITHM.** A1/A2 ruled *where* the splice attaches but
not *how* `RowLayout.Wrap` packs against a row budget. §9 rules that, end to end, at
statement level. **§9 is normative and is what the implementor builds from.** Sections 1–8 remain
the rationale and the acceptance criteria; where §9 contradicts an earlier section's mechanism, §9
wins (each such point is called out in §9.0).

---

## 1. The ruling, from §2.6's own text

`SPEC-V2-FRAMEWORK.md:1494-1509`:

> **Vertical overflow** is governed by the same marker. When wrapping produces more rows than the
> pane's `maxRows` (or the surface's, §2.8), the surplus rows are dropped and the last surviving
> row ends with the marker, so truncation is always visible rather than silent.
>
> **"Ends with the marker" means the marker replaces the row's last cells, never that it is appended
> to them.** The horizontal case two paragraphs up budgets the marker's width against the inner width
> and says so; the vertical case said only "ends with", and appending is the reading that sentence
> invites. […]
>
> So the marker is budgeted identically on both axes, with the same two riders: if the inner width is
> not greater than the marker width the marker is dropped rather than allowed to consume the row, and
> an `ellipsis` of `""` is a hard clip that spends no cell. One rule, applied twice — stating it
> once per axis is how the two got to disagree.

Three things follow, and none of them is a judgment call:

1. **The marker occupies cells, not a row.** "Replaces the row's **last cells**" is unambiguous
   about the unit. Full-row-replacement budgets the marker in rows.
2. **The last surviving row keeps its content.** The clause presupposes a row that has content for
   the marker to sit at the end of. A row whose entire content is the marker does not "end with" it.
3. **The two axes are explicitly one rule.** The closing sentence — "One rule, applied twice —
   stating it once per axis is how the two got to disagree" — is a standing instruction against
   exactly the outcome in the worktree: two axes, two implementations, two behaviours.

`:1686` independently confirms the unit: *"`ellipsis` marker goes on the last surviving **content**
row"* — on it, not instead of it.

The paragraph at 1498 was written to foreclose *appending* (which overflows the pane width and
triggers §2.4's shearing failure). Full-row-replacement is a third reading it did not enumerate. It
avoids the overflow the paragraph warns about, but by discarding the content the same sentence says
must remain. **Not compliant, and not a coarser version of compliant — a different unit.**

---

## 2. What impl2 got right, and must land unchanged

The `#27` diff fixes a real bug and its degenerate-case handling matches §2.6 exactly:

- **Threading `innerWidth` into `ClipRows`.** Without it the marker could exceed the pane's inner
  width — the §2.4 shearing failure, on the vertical axis. This is the actual bug and it is fixed.
- **Both riders, correctly.** `ellipsis.Length == 0` → hard clip spending no cell; marker dropped
  when the inner width is not greater than the marker width. These are §2.6:1506-1508 verbatim, and
  they carry over to the spliced implementation **unchanged**.

**Land the diff.** This ruling is a follow-up task, not a blocker, exactly as the Orchestrator
scoped it.

---

## 3. Two behavioural defects that disappear under the splice

Offered as corroboration that §2.6 says what it says for a reason — not as independent grounds.

**(a) Non-monotone in the pane's width.** `ClipRows` as written:

```csharp
if (ellipsis.Length == 0 || ellipsis.Length >= innerWidth) { return rows.Take(cap).ToList(); }
var kept = rows.Take(cap - 1).ToList();
kept.Add(new PaneRow(escaped, ellipsis.Length));
```

At `innerWidth == ellipsis.Length` the guard fires and the pane keeps `cap` rows of real content.
At `innerWidth == ellipsis.Length + 1` it keeps `cap − 1`. **One column wider yields strictly less
content** whenever `cap ≤ 4` — content cells go from `innerWidth × cap` to `innerWidth × (cap−1)`.
A degradation ladder that inverts under a one-unit change is the same defect class §10 requirement
6's monotone-clamp test exists to catch on the sizing axis.

**(b) `cap: 1` renders an empty pane.** `rows.Take(0)` plus a marker row is a pane containing `…`
and nothing else. The one row the author was still granted is spent announcing that rows were lost.

Under the splice both vanish structurally: `cap` rows are always kept, and the marker costs
`ellipsis.Length` cells of one row regardless of `cap`. That is the point of budgeting in cells.

**(c) Styling divergence, minor but real.** The marker row is `Markup.Escape(ellipsis)` — plain and
unstyled. `TruncateSegment` restyles the marker to inherit the segment's colour and closes any OSC 8
hyperlink before it, per §3.2 (`:2621-2622`: *"clicking `…` must never navigate"*). Reusing
`TruncateSegment` inherits that ruling for free; a second implementation re-opens it.

---

## 4. Where the fix goes — do NOT extend `PaneRow`

impl2's blocker is real and correctly diagnosed: `PaneBuffer.cs:8` is
`public sealed record PaneRow(string Markup, int Width)`, with no plain-text field, so there is no
markup-safe splice point **at the layer `ClipRows` runs at**. But that layer is a choice, not a
constraint.

`PaneAssembler.RenderLeafRows` builds `rawRows` via `PaneRenderer.RenderLeaf(...)` →
`RowLayout.Wrap(prepared, width, allowFallback)`, and *then* clips the opaque result. The row budget
arrives too late. The fix is to let it arrive on time.

**Ruled: pass the row budget down into the render path so the last kept row's content is truncated
while it is still `Segment`s, and reuse `PaneRenderer.TruncateSegment` for the marker.** `RowLayout`
already knows how segments map to rows; stopping at `cap` rows and handing the final row's segments
to the existing truncation is the whole change.

**Rejected — extending `PaneRow` with a plain-text field.** It duplicates state that `Segment`
already carries (`Segment.Plain` / `Segment.Markup`), and the two copies would have to be kept in
agreement by every row producer forever. `PaneRow` being opaque is a property worth keeping: it is
what makes a rendered row final.

**Rejected — a second marker-splicing implementation at the row layer.** §2.6's closing sentence
forbids it in as many words, and Jim's standing architecture rule ("one implementation per
behaviour") says the same thing.

**AMENDED (A2) — this seam is now stale; the budget resolves at §3.1's concatenation site instead.**
§3.1 (task #75) landed and, per its own §6, requires exactly one concatenation site for the
pane's row list — `PaneAssembler.RenderItemRows`'s local `rows` list, fed from two call sites
(`FlushGroup`'s packed-group flush and the block-line loop). That is now where the row budget must
be resolved and where clipping/splicing happens, **not** a single flat `RowLayout.Wrap` call per
pane as this section originally assumed — §5(b) predicted exactly this collision and deferred to
§3.1's text, which has since landed. Concretely: the budget is checked against `rows` as it is
being assembled, and when the cap lands inside a producer unit's contribution, that one unit
(the packed group or block whose `RenderLeaf` inputs are in scope at that call site — see
SPEC-3.1 §6's amended provenance requirement) is the one re-invoked with the row budget, so the
final kept row is truncated and the marker spliced while its content is still `Segment`s inside
`Wrap`. The mechanism below (stop at `cap` rows, reuse `TruncateSegment` on the last kept row's
segments) is unchanged — only where it attaches moved, from a single pane-wide `Wrap` call to this
per-producer-unit call at the concatenation site.

### The ambiguity §2.6 does not settle, ruled here

"Replaces the row's **last cells**" is written for the routine case, which §2.6 names: *"The last
surviving row is routinely a full row — it is the row that was full enough to force the wrap."*
When that row is **not** full, "last cells" is ambiguous between *immediately after the content* and
*flush to the right edge, with a gap*.

**Ruled: immediately after the content**, i.e. clip to `innerWidth − ellipsis.Length` (a no-op when
the content is already shorter) and place the marker directly after. Reasons: it is what
`TruncateSegment` already does, so reuse is exact; a gap before the marker reads as content the pane
chose not to show; and `Pane.Align` (applied by `AlignRow`, downstream) owns horizontal placement of
the finished row — a marker pinned right would fight a `center` or `right` alignment. For a full row
the two readings coincide, which is why §2.6 never had to choose.

**The marker must be spliced before `AlignRow` runs**, so alignment sees content-plus-marker as one
row. This is the current call order and it must not change.

---

## 5. Two couplings the implementor must not discover late

**(a) `RenderLeaf`'s input tuple is spec'd, and this change extends it.** `:1432` names it as
`(items, innerWidth, overflow, ellipsis, notes, allowFallback)` — §2.5.1's purity/cache-key tuple,
and `:1437` states that §10's test fixes **all** of them. Adding a row budget makes it a seventh
member and therefore part of the cache key. **Two panes identical except for their row cap must not
share a cache entry.** Whoever implements this must update §2.5.1's tuple, §10's test, and the cache
key together, or the seam becomes a correctness bug that only shows under caching. This is the
highest-risk part of the change and it is not in the part anyone is looking at.

> **A3 note:** the tuple grows by **two** members, not one — see §9.5. And the cache-key claim is
> now a NEEDS-EVIDENCE item (§9.8-E2): I searched `PaneRenderer.cs` and found no cache; the tuple
> may be spec-text-only today.

**(b) AMENDED (A1) — ruled: this task is sequenced AFTER the §3.1 block model.**

The first draft left this open pending §3.1's text. §3.1 (`:2494-2519`) settles it:

> Row packing (§2.6) operates on single-row items. A multi-row block **occupies its own rows**: it
> never shares a row with a neighbouring item and never has items packed beside it. A pane's content
> is therefore a vertical sequence of packed single-row groups and standalone blocks, in config
> order.
>
> **Packing runs before wrapping, and the order is not interchangeable.** […] Pack first. An item's
> block count is a property of **what the provider returned** — one line or several — never of the
> width it was later granted, so wrapping cannot promote an item to a block.

Today every row comes from wrapping one flat segment list, which is why §4's seam ("stop `Wrap` at
`cap` rows") is well-defined. Once §3.1 lands, a pane's rows are a **concatenation of packed groups
and standalone blocks**, so "the last kept row" is no longer necessarily produced by `Wrap` at all —
it may be the last row of a block that `Wrap` never saw. **The seam this task adds would have to be
rebuilt on the new row-production shape.**

Because §2 already fixed the shearing bug, this splice carries no user-visible urgency and there is
no reason to build it twice. Build §3.1's block model first, then this, on the final shape.

If the Orchestrator overrides that and lands this first, then §4's seam is **provisional**, and the
§3.1 task must carry an explicit item to re-site it — otherwise the marker silently stops being
applied to any pane whose last row comes from a block, which no existing test would catch.

---

## 6. What must not change

1. **Both §2.6 riders**, exactly as impl2 implemented them.
2. **`PaneRow(string Markup, int Width)`** — no new fields.
3. **`TruncateSegment` is the one implementation** of "cut to fit, ending with the marker". This
   task calls it; it does not fork it, and it does not copy its logic to the row layer.
4. **§3.2's hyperlink ruling** — a truncated link closes before the marker; the marker is never
   clickable. Inherited automatically by reusing `TruncateSegment`; re-verify it survives.
5. **Splice before align**, per §4.
6. **`cap <= 0` returns no rows.** Unchanged.

---

## 7. Verification

1. **`cap: 1`, content overflowing, `innerWidth` comfortably above the marker.** Assert the pane
   renders **one** row containing real content ending in `…`. Today it renders `…` alone. This is
   the headline behaviour change and the clearest statement of the ruling.
2. **Monotonicity across the guard boundary.** Same pane at `innerWidth == ellipsis.Length` and at
   `ellipsis.Length + 1`. Assert content is non-decreasing as width increases. Fails today by
   construction — this is defect (a) as an assertion, and it should be written to fail on the
   current implementation first.
3. **Row count is `cap`, not `cap − 1`.** Any overflowing pane with `cap >= 2`. Guards against a
   partial fix that splices the marker but keeps dropping the last content row.
4. **Both riders survive.** `ellipsis: ""` → `cap` rows, no cell spent, no marker. `ellipsis` wider
   than `innerWidth` → `cap` rows of real content, no marker. Both must behave identically before
   and after this change; they are already correct and this is a regression guard.
5. **Marker styling and hyperlink safety.** A clipped row whose last segment is a coloured OSC 8
   link: assert the link closes before the marker, the marker carries the colour, and the marker is
   not inside the link. Pins §3.2 on the vertical axis, which nothing currently tests.
6. **Alignment interaction.** A short final row under `align: right` with the marker spliced. Assert
   the marker sits immediately after the content and the whole row is then aligned — §4's ruling on
   the non-full row. This is the test that distinguishes the two readings of "last cells".
7. **No row exceeds `innerWidth`.** Every row of every clipped pane. This is the §2.4 shearing
   invariant that motivated the original bug fix, re-asserted on the spliced path.
8. **A pane whose last surviving row comes from a multi-row block** still gets the marker. Only
   writable once §3.1 lands, and it is the test that proves §5(b)'s re-siting was actually done.

§9.7 adds four more (9–12) covering the boundary case, the zero-row unit, the null-width fallback,
and the no-budget identity.

---

## 8. Confidence

**High on the ruling.** §2.6:1498 is not ambiguous and was written to pre-empt a weaker reading of
the same sentence; §1686 corroborates the unit independently. I did not have to reason from
principle — the text decides it.

**High on §4's placement** *given today's row production*, and that qualifier is the substance of
§5(b): the placement is right for the current shape and would need re-siting after §3.1.

**Medium on §4's sub-ruling for the non-full row.** §2.6 does not settle it and both readings are
defensible; I chose the one that reuses `TruncateSegment` exactly and does not fight `align`. If
someone has a reason to prefer flush-right, it is a small change and I would not argue hard.

**§5(a) is the risk I would most expect to be missed** — it is a cache-correctness change hiding
inside a rendering change, and nothing in the task description points at it.

**Not escalation-worthy.**

---

# 9. AMENDMENT A3 — the packing algorithm, ruled at statement level

Added after impl2 reported that §§1–8 fix the *shape* of the change but not the *mechanism* inside
`RowLayout.Wrap`. impl2 was right to stop: `Wrap` is a shared primitive under wide test coverage
(`HeightLadderTests`, `OverflowModeTests`, `PositionIndependenceTests`, `HyperlinkTests`,
`NarrowSplitPaneTests`, `GoldenParityTests`), and guessing at it was not its call to make.

**This section is written against the code as it actually is on `main` (verified by reading, not
from the report).** Line numbers below are the ones I read; three of impl2's citations are wrong and
§9.0 corrects them, because two of the three change the answer.

## 9.0 Corrections to the reported code facts

| Reported | Actual |
|---|---|
| `ClipRows` at `RenderItemRows.cs:47-63` | **No `RenderItemRows.cs` exists.** `ClipRows` is `PaneAssembler.cs:47-63`. Lines right, file wrong. |
| Riders applied "once per `RenderLeaf` call at `PaneAssembler.cs:53`, as a pre-check before `Wrap` runs" | **Wrong, and it changes Q3.** The rider check is `PaneAssembler.cs:54`, *inside* `ClipRows`, which runs **after** `Wrap` on the flattened rows. There is no pre-`Wrap` rider check anywhere. |
| `RowLayout.Wrap` at `RowLayout.cs:32-81` | `RowLayout.cs:33-83`. Signature is `Wrap(IReadOnlyList<Segment> segments, int? availableWidth, bool allowFallback = true)` — **no `ellipsis` parameter today**. |
| Packed rows are "multiple items joined by `\" | \"`" produced by `FlushGroup` | **Wrong, and it dissolves Q2.** `FlushGroup` (`PaneAssembler.cs:98`) passes a bare `List<Segment>` with no separators in it. The separator is `RowLayout`'s own private constant `SeparatorMarkup = " [dim]|[/] "` (`RowLayout.cs:10`, `SeparatorWidth = 3` at `:15`), inserted by `Wrap` only *between two placed segments*. |

Confirmed as reported: `RenderLeaf` `PaneRenderer.cs:13-44` (no row budget); `TruncateSegment`
`PaneRenderer.cs:49-80` (width-only, one `Segment`, **`private`**); `ClipRows` called from
`RenderLeafRows` `PaneAssembler.cs:30-33` as post-processing on flattened `PaneRow`s; three producer
call sites — `RenderDefaultRows` `:65-71` (RenderLeaf at `:69`), `FlushGroup` `:91-101` (RenderLeaf
at `:98`), block-line loop `:116-125` (RenderLeaf at `:120`, one call per block line); none track
provenance.

## 9.1 Answers to the four questions, stated up front

**Q1 — does `Wrap` need to know the budget as it packs? Yes, and the mechanism is
one-row lookahead, not post-hoc clipping.** `Wrap` packs rows `0 … cap−2` at the **full**
`availableWidth`, exactly as today. For the capped row (index `cap−1`) it packs a *tentative* row at
full width purely to learn whether content remains after it. If content remains (or
`markerRequired`), it **discards the tentative row and re-packs from the same segment position at
`availableWidth − ellipsis.Length`**, then splices the marker onto that row's last segment. Only one
row is ever packed twice, and only one resume position ever needs remembering — no parallel
per-row bookkeeping, no retained segment→row map. Post-hoc clipping is explicitly rejected: it is
the same defect class §1 already ruled out, moved down a layer.

**Q2 — which segment's tail gets truncated on a packed row, and does the separator survive? The
question does not arise.** Because the separator is `Wrap`'s own constant and is emitted only
*between* two placed segments (`RowLayout.cs:63-67`, guarded by
`rowWidth + SeparatorWidth + segWidth <= availableWidth`), a trailing separator is **structurally
unrepresentable** — a row cannot end in `" | "`. The truncated segment is always the **last segment
placed on that row**, and `TruncateSegment` stays a one-`Segment` function with no sibling rule. No
new state on `Segment`, no separator-aware caller rule. (§9.3 gives the exact width formula, which
is where the separator's 3 cells are accounted for.)

**Q3 — do the riders move inside `Wrap`? Yes, and the copy at `PaneAssembler.cs:54` is deleted, not
moved.** The riders decide *how much width to reserve while packing*, so they are inseparable from
packing and must live where the packing is. But `TruncateSegment` (`PaneRenderer.cs:51-62`) already
implements exactly the same rule; two copies would violate §6.3 and §2.6's closing sentence. Ruled:
**extract the predicate once** (§9.4) and have both axes call it. Net result: after this change the
rule exists in exactly one place, down from two today.

**Q4 — is a third parameter needed for the boundary case? Yes.** `RenderLeaf` and `Wrap` each gain
**two** new parameters, `int? rowBudget` and `bool markerRequired`, and they are independent.
`rowBudget` alone means "cap the rows"; `markerRequired` means "this pane *is* truncated, so the last
kept row carries the marker regardless of whether this unit itself had anything left over". §9.2
gives the exact rule by which `PaneAssembler` computes both. Rejected — collapsing them into one
parameter ("a budget always implies a marker"): it would make any future defensive cap splice a
spurious marker, and it conflates *how many rows* with *am I the truncated tail*, which is precisely
the distinction the boundary case is made of.

## 9.2 `PaneAssembler` — provenance, and which unit owns the marker

### 9.2.1 The unit

A **render unit** is one `PaneRenderer.RenderLeaf` invocation. There are exactly three producers of
units and they are already one call each: `RenderDefaultRows` (one unit), `FlushGroup` (one unit per
flush), the block-line loop (**one unit per block line**, not one per block). Re-invocation is
per-`RenderLeaf`-call, so this is the right granularity and no producer needs restructuring.

Add, in `PaneAssembler`:

```csharp
private readonly record struct RenderUnit(IReadOnlyList<Segment> Segments, IReadOnlyList<PaneRow> Rows);
```

`Segments` is the exact list handed to `RenderLeaf`, retained so the unit can be re-invoked. This is
the provenance that SPEC-3.1 §6 (as amended) requires, and it is the whole of it — no other
provenance is needed.

- `RenderDefaultRows` returns `IReadOnlyList<RenderUnit>` (a single-element list).
- `RenderItemRows` returns `IReadOnlyList<RenderUnit>`; its local `rows` list (`:88`) becomes
  `var units = new List<RenderUnit>()`. `FlushGroup` appends
  `new RenderUnit(packedGroup.ToArray(), buffer.Rows)` **before** `packedGroup.Clear()` — the
  `ToArray()` copy is load-bearing, `packedGroup` is mutated in place. The block-line loop appends
  `new RenderUnit(new[] { lineSegment }, lineBuffer.Rows)` per line.
- **`units` is still the single concatenation site** SPEC-3.1 §6 demands. Flattening moves into
  §9.2.2; nothing else concatenates.

### 9.2.2 The budget pass

`RenderLeafRows` (`PaneAssembler.cs:13-36`): delete the `ClipRows` call at `:30-33` and delete
`ClipRows` itself (`:47-63`). Replace with:

```csharp
var units = pane.Items.Count == 0
    ? (itemsEmptied ? Array.Empty<RenderUnit>() : RenderDefaultRows(pane, innerWidth, ctx, notes))
    : RenderItemRows(pane, innerWidth, ctx, values, tokens, notes);

var rawRows = ApplyRowBudget(units, maxContentRows, pane, innerWidth, notes);

return rawRows.Select(row => AlignRow(row, innerWidth, pane.Align)).ToList();
```

`ApplyRowBudget`, normatively:

1. If `maxContentRows` is null → return every unit's rows concatenated in order. **No re-invocation,
   no marker.** This is the overwhelmingly common path and it must be byte-identical to today.
2. Let `cap = maxContentRows.Value`. If `cap <= 0` → return empty. (§6.6, unchanged.)
3. Let `total = units.Sum(u => u.Rows.Count)`. If `total <= cap` → return every unit's rows
   concatenated. **No marker** — nothing was truncated.
4. Otherwise let `S_i = units[0..i].Sum(u => u.Rows.Count)` (so `S_0 = 0`). Choose
   **`k = the smallest index such that S_(k+1) >= cap and units[k].Rows.Count >= 1`.**
   - The `>= cap` (not `> cap`) is what selects the unit that lands *exactly* on the boundary. This
     is the Q4 case and this comparison is the entire mechanism for detecting it.
   - The `Rows.Count >= 1` clause is required: a unit may legally contribute **zero** rows (an empty
     item, or the collapsed-pane case of commit `62a5741`), and a zero-row unit satisfies
     `S_(k+1) >= cap` vacuously while having no last row to put a marker on. Skipping it is
     mandatory, not defensive.
   - `k` always exists, because `total > cap` and `cap >= 1`.
5. Emit `units[0..k]`'s rows unchanged.
6. Compute `budget = cap - S_k`. **`budget >= 1` is guaranteed** (by minimality, `S_k < cap`).
7. Re-invoke the owning unit:
   ```csharp
   var reNoted = new RenderNoteCollector();          // deliberately discarded, see below
   var owner = PaneRenderer.RenderLeaf(
       units[k].Segments, innerWidth, ResolveOverflow(pane), pane.Ellipsis, reNoted,
       allowFallback: false, rowBudget: budget, markerRequired: true);
   ```
   Emit `owner.Rows`.
8. **Drop `units[k+1..]` entirely.** Do not emit, do not re-invoke.

`markerRequired: true` is unconditional at this one call site, and that is correct: step 7 is only
reached when `total > cap`, i.e. the pane *is* truncated. It is a parameter rather than an implied
constant for the reason in §9.1 Q4.

**Why the re-invocation's notes are discarded.** `RenderLeaf` emits notes only from its
`OverflowMode.Truncate` branch (`PaneRenderer.cs:33`), keyed on `s.Plain.Length > width` — a
predicate that does not read `rowBudget` or `markerRequired`. The re-invocation therefore produces
*exactly* the notes the first invocation already added to the real collector, so discarding them is
provably lossless and merging them would duplicate. If `RenderNoteCollector` cannot be constructed
standalone, that is a mechanical detail — construct it however the existing call sites do, or add a
null-object; do not change the ruling.

**Explicitly out of scope:** today, notes from units whose rows are *dropped* by the cap still reach
the caller. That is arguably wrong, it is wrong the same way today, and this task does not change
it. Do not "fix" it here.

## 9.3 `RowLayout.Wrap` — the algorithm

New signature (all four originals unchanged and in place; the three additions are optional with
defaults that reproduce today's behaviour exactly, so every existing call site compiles and behaves
identically untouched):

```csharp
public static IReadOnlyList<PaneRow> Wrap(
    IReadOnlyList<Segment> segments,
    int? availableWidth,
    bool allowFallback = true,
    int? rowBudget = null,
    string ellipsis = "",
    bool markerRequired = false)
```

### 9.3.1 Two helpers, extracted from the existing loop

The greedy loop at `RowLayout.cs:53-80` is refactored into a row-at-a-time packer plus a composer.
**This refactor must be behaviour-identical when `rowBudget is null`** — same greedy rule, same
separator, same widths. It is the single highest-regression-risk edit in the task (§9.8-E1).

```csharp
// Packs one row starting at segments[i], advancing i past everything placed.
// The FIRST segment is placed unconditionally regardless of width — this preserves
// RowLayout.cs:57-62's existing `if (!rowStarted)` behaviour and guarantees the
// returned list is never empty.
private static List<Segment> PackRow(IReadOnlyList<Segment> segments, ref int i, int width)
{
    var placed = new List<Segment> { segments[i] };
    var rowWidth = segments[i].Plain.Length;
    i++;
    while (i < segments.Count && rowWidth + SeparatorWidth + segments[i].Plain.Length <= width)
    {
        rowWidth += SeparatorWidth + segments[i].Plain.Length;
        placed.Add(segments[i]);
        i++;
    }
    return placed;
}

// The arithmetic here must match PackRow's incremental accumulation exactly — see the
// same requirement on the single-row fallback at RowLayout.cs:44.
private static int WidthOf(IReadOnlyList<Segment> placed) =>
    placed.Sum(s => s.Plain.Length) + SeparatorWidth * (placed.Count - 1);

private static PaneRow Compose(IReadOnlyList<Segment> placed) =>
    new(string.Join(SeparatorMarkup, placed.Select(s => s.Markup)), WidthOf(placed));
```

### 9.3.2 The body

```csharp
var rows = new List<PaneRow>();
if (segments.Count == 0) return rows;                 // unchanged, RowLayout.cs:36-39

if (availableWidth is null || (allowFallback && availableWidth < MinUsableWidth))
{
    // unchanged, RowLayout.cs:41-47 — see 9.3.3
    ...
    return rows;
}

var width = availableWidth.Value;
var cap = rowBudget ?? int.MaxValue;
if (cap <= 0) return rows;

var spliceMarker = rowBudget is not null && SegmentTruncation.MarkerFits(width, ellipsis);
var contentWidth = spliceMarker ? width - ellipsis.Length : width;

var i = 0;

// Rows 0 .. cap-2 pack at FULL width. With no budget, cap-1 == int.MaxValue-1 and this
// loop consumes everything — which is exactly today's algorithm, unchanged.
while (i < segments.Count && rows.Count < cap - 1)
{
    rows.Add(Compose(PackRow(segments, ref i, width)));
}

if (i >= segments.Count) return rows;                 // content exhausted before the capped row

// ---- the capped row (index cap-1). One-row lookahead. ----
var resume = i;
var tentative = PackRow(segments, ref i, width);
var overflowed = i < segments.Count;

if (!overflowed && !markerRequired)
{
    rows.Add(Compose(tentative));                     // fits in exactly cap rows, nothing lost
    return rows;
}

if (!spliceMarker)
{
    rows.Add(Compose(tentative));                     // §2.6 riders: keep cap full rows, no marker
    return rows;
}

// Re-pack the capped row against the reduced width, then splice.
var j = resume;
var final = PackRow(segments, ref j, contentWidth);
var last = final.Count - 1;
var prefixWidth = final.Take(last).Sum(s => s.Plain.Length) + SeparatorWidth * last;
final[last] = SegmentTruncation.Truncate(final[last], width - prefixWidth, ellipsis);
rows.Add(Compose(final));
return rows;
```

### 9.3.3 Why each piece is the way it is — do not "simplify" these away

- **`width - prefixWidth`, not `contentWidth`, is the budget handed to `Truncate`.** This one
  expression is what makes the algorithm correct in both directions, and it is the reason the
  §4 sub-ruling ("marker immediately after the content") and the §2.4 shearing invariant hold
  simultaneously:
  - *Short final row* (the next segment didn't fit): `width - prefixWidth >= lastLen + ellipsis.Length`,
    so `Truncate`'s `contentBudget` exceeds the segment and clips nothing — the marker lands
    immediately after the content. §4's ruling, for free.
  - *Final segment exactly `width` wide* (possible: `RenderLeaf` pre-clips oversized segments to
    `width`, and `PackRow` places the first segment unconditionally, so `final` can be one segment
    of width `width` even though `contentWidth < width`): `prefixWidth == 0`, so `Truncate` gets
    `width`, clips to `width - ellipsis.Length`, appends the marker — row is exactly `width`.
  - In every case the composed row is `prefixWidth + Truncate(...)`'s output, and `Truncate` never
    exceeds its own `innerWidth` argument, so **`row.Width <= availableWidth` always**. That is §7
    test 7, discharged by construction.
- **`PackRow` at `contentWidth` before splicing, rather than packing at `width` and clipping after.**
  Reserving the marker's width *while packing* is what makes the marker budgeted rather than stolen
  from content, and it is exactly what the horizontal axis does. Packing at `width` and clipping
  afterwards is the rejected reading of §2.6, one layer down.
- **`final` may legally drop a segment `tentative` had held.** When two segments fit in `width` but
  not in `contentWidth`, the second is dropped and the marker takes its cells. This is correct and
  is what "the marker replaces the row's last cells" means. It is also the visible behaviour of the
  `markerRequired` boundary case, where nothing overflowed but a marker is owed.
- **`final` is never empty**, because `PackRow` places its first segment unconditionally. `last >= 0`
  needs no guard.
- **`contentWidth >= 1`** whenever `spliceMarker`, because `MarkerFits` requires
  `ellipsis.Length < width`.
- **The single-row fallback path (`RowLayout.cs:41-47`) ignores `rowBudget`, `ellipsis` and
  `markerRequired` entirely.** Ruled deliberately: that path already abandons width budgeting by
  design (it emits one deliberately overwide row), so spending cells on a marker against a width
  nobody is honouring would be incoherent. It is also nearly unreachable for this task —
  `PaneAssembler` passes `allowFallback: false` at all three producer sites, leaving only the
  `availableWidth is null` case, where the pane produces one row and the only cap that could bite is
  `cap <= 0`, already handled identically. **No behaviour change.** Forward the parameters anyway
  for signature uniformity; the path discards them.
- **`markerRequired: true` with `rowBudget: null` is ignored**, not an error. Document it on the
  parameter; do not throw.

## 9.4 The rider, extracted — and the `SegmentTruncation` move

`TruncateSegment` is `private` in `PaneRenderer` and `RowLayout` now needs it. Making it `internal`
and calling `PaneRenderer.TruncateSegment` from `RowLayout` would invert the existing
`PaneRenderer → RowLayout` dependency. Ruled instead:

**Create `/Users/jimcline/git/repos/claude-tui-line/src/ClaudeTuiLine/SegmentTruncation.cs`,
`internal static class SegmentTruncation`, and MOVE into it, bodies unchanged:** `TruncateSegment`
(rename → `Truncate`), `WrapSegment` (rename → `WrapToWidth`), `SafeCutIndex`, `Restyle`,
`RestyleSimple`, `TryGetSimpleWrap` — i.e. `PaneRenderer.cs:49-175` in full. `PaneRenderer` keeps
only `RenderLeaf` and calls `SegmentTruncation.Truncate` / `SegmentTruncation.WrapToWidth` at
`:34` / `:38`. `RowLayout` calls `SegmentTruncation.Truncate` / `.MarkerFits`. Both dependencies now
point at a leaf class and nothing is circular. **This is a pure move: no behavioural edit to any
moved body beyond the two renames.**

Add one member, which is the extracted rider:

```csharp
/// SPEC-V2-FRAMEWORK.md §2.6, the two riders, in one place for both axes: an empty `ellipsis`
/// is a hard clip that spends no cell, and a marker not strictly narrower than the space it
/// would sit in is dropped rather than allowed to consume it.
internal static bool MarkerFits(int innerWidth, string ellipsis) =>
    ellipsis.Length > 0 && ellipsis.Length < innerWidth;
```

and refactor `Truncate`'s own guard (`PaneRenderer.cs:56-62`) to use it:

```csharp
if (!MarkerFits(innerWidth, ellipsis))
{
    return Restyle(segment, segment.Plain[..SafeCutIndex(segment.Plain, Math.Min(innerWidth, segment.Plain.Length))]);
}
```

**This is behaviour-preserving, verified case by case.** Today's guard is
`innerWidth <= ellipsis.Length`, equivalent to `!(ellipsis.Length < innerWidth)`. The only inputs
that change branch are `ellipsis.Length == 0` with `innerWidth > 0`, which today falls through to
the main branch and computes `contentBudget = innerWidth`, `clipped = plain[..min(innerWidth, len)]`,
`newPlain = clipped + "" == clipped` — **identical output** to the guard branch above. The
`innerWidth <= 0` guard at `:51-54` is a different concern and **stays where it is, unchanged**.

Net: the rider is implemented **once**, and `PaneAssembler.cs:54`'s copy is deleted with `ClipRows`.
That is a reduction from two implementations to one, which is the §6.3 / §2.6-closing-sentence
requirement satisfied rather than merely not-violated.

## 9.5 `PaneRenderer.RenderLeaf` — signature and cache key

```csharp
public static PaneBuffer RenderLeaf(
    IReadOnlyList<Segment> items, int? innerWidth, OverflowMode overflow, string ellipsis,
    RenderNoteCollector notes, bool allowFallback = true,
    int? rowBudget = null, bool markerRequired = false)
```

Both new arguments are forwarded to **both** `Wrap` call sites — the `innerWidth is null` early
return at `:20` as well as the main one at `:43`. The null-width call forwards them even though
`Wrap`'s fallback path discards them (§9.3.3); uniformity beats a special case.

**§2.5.1's purity/cache-key tuple grows from six members to eight:**
`(items, innerWidth, overflow, ellipsis, notes, allowFallback, rowBudget, markerRequired)`.
Update `SPEC-V2-FRAMEWORK.md:1432`'s tuple, `:1437`'s claim that §10's test fixes all of them, and
§10's test itself, in the same change. **Both new members must be in the key.** The sharpest reason
is now concrete rather than hypothetical: under §9.2.2, unit `k` is rendered **twice in a single
pane render** — once with `(null, false)` in step 1/3's measurement pass and once with
`(budget, true)` in step 7 — and a cache that keys on the old six members would return the first
result for the second call and silently drop every marker in the product. See §9.8-E2: I could not
confirm such a cache exists yet.

## 9.6 What must not change (additions to §6)

7. **`RowLayout.Wrap` with `rowBudget: null` must be byte-identical to today**, including the
   fallback path, the separator, and every width. Every existing caller passes no budget.
8. **`SegmentTruncation` is a pure move.** No behavioural edit to `Truncate`, `WrapToWidth`,
   `Restyle`, `RestyleSimple`, `TryGetSimpleWrap`, or `SafeCutIndex` beyond the `MarkerFits`
   extraction ruled in §9.4, which is proven output-identical there.
9. **`SafeCutIndex` stays on every cut.** The surrogate-pair rule (`PaneRenderer.cs:105-113`,
   §13.2 / defect 16) is inherited automatically because the splice routes through `Truncate`; do not
   add a cut anywhere that bypasses it.
10. **`units` stays the one concatenation site** (SPEC-3.1 §6). `ApplyRowBudget` is the one place
    that flattens it.
11. **`RenderNoteCollector` behaviour for emitted units is unchanged**; the re-invocation's notes are
    discarded per §9.2.2.

## 9.7 Verification (additions to §7)

9. **The `markerRequired` boundary case — this is the test the whole of Q4 exists for.** Two units
   whose row counts sum to exactly `cap`, followed by a third unit with at least one row. Assert:
   `cap` rows are emitted; the last row is unit 2's last row **with the marker spliced**; unit 3
   contributes nothing. Then assert the negative control: remove unit 3, so `total == cap` — assert
   the same `cap` rows are emitted with **no marker anywhere**. A `rowBudget`-only implementation
   passes the second and fails the first, which is exactly the defect this test is placed to catch.
10. **A zero-row unit straddling the cap.** A pane where the unit at the boundary contributes zero
    rows (an emptied item). Assert the marker lands on the last *non-empty* unit's last row and that
    no exception is thrown — the `Rows.Count >= 1` clause of §9.2.2 step 4.
11. **Null `COLUMNS` with a `maxContentRows` set.** Assert the single fallback row is emitted
    unchanged and carries no marker (§9.3.3's fallback ruling), and that `cap <= 0` still yields
    zero rows.
12. **No-budget identity.** The full existing suite — `GoldenParityTests`, `HeightLadderTests`,
    `OverflowModeTests`, `PositionIndependenceTests`, `HyperlinkTests`, `NarrowSplitPaneTests` —
    must pass unchanged. This is the regression gate on the `PackRow`/`Compose` refactor and it is
    non-negotiable: if any golden output moves, the refactor is wrong, not the golden.

## 9.8 NEEDS-EVIDENCE — two empirical questions I did not run, and what each result decides

I do not execute. These are for the Implementor to resolve; neither blocks starting the change.

**E1 — does the `PackRow`/`Compose` refactor preserve `Wrap` byte-for-byte?** Run the six suites in
§9.7 test 12 **before** adding any budget logic — i.e. land the refactor as its own commit with
`Wrap`'s public behaviour unchanged, and prove it green. *If green:* proceed to the budget logic on
a known-good base. *If red:* the refactor diverged from the incremental arithmetic (the most likely
culprit is `WidthOf`'s `SeparatorWidth * (count - 1)` versus the loop's accumulation, or the
unconditional first-segment placement); fix the refactor — **do not** adjust a golden file, and do
not proceed until green.

**E2 — does a `RenderLeaf` result cache actually exist in the code today?** `PaneRenderer.cs` as I
read it has no cache; §5(a)'s tuple may be spec-text-only. Search for a memo/cache keyed on
`RenderLeaf`'s arguments (`grep -rn "RenderLeaf" src/` plus any `Dictionary`/`ConcurrentDictionary`
keyed on a tuple in the render path). *If a cache exists:* its key **must** gain both new members —
this is the §5(a)/§9.5 correctness bug, and it is the highest-severity item in the task. *If no
cache exists:* §5(a) reduces to updating `SPEC-V2-FRAMEWORK.md:1432`/`:1437` and §10's test to the
eight-member tuple, and the double-render in §9.2.2 is harmless.

## 9.9 Confidence, and what I am NOT deciding

**High** on Q1 (the one-row-lookahead mechanism), Q2 (dissolved by the actual separator handling —
this one is a fact about the code, not a judgment), Q3 (rider extraction), and Q4 (`markerRequired`
as an independent parameter, with §9.2.2 step 4's `>= cap` as the detector).

**High** on the `width - prefixWidth` formula; I worked all three of its cases through by hand
against `Truncate`'s actual body and they are in §9.3.3 for the reviewer to re-check.

**Medium** on the `SegmentTruncation` extraction being worth its churn. It is the right dependency
direction and it satisfies "one implementation", but it moves ~125 lines and the alternative
(`internal` on `TruncateSegment` plus a `RowLayout → PaneRenderer` reference) is smaller and would
work. I chose the clean seam; **if the Reviewer or Jim prefers the smaller diff, take it — nothing
else in §9 depends on which was chosen.**

**Deliberately not decided, and it is a product call, not mine:** nothing here. The one judgment I
would surface upward is the dropped-unit notes behaviour flagged in §9.2.2 — today a pane reports
notes for rows the user never sees. I have ruled it out of scope for #27 rather than silently
changing it; if Jim wants it changed, it is its own task.

**Not escalation-worthy.** Every fork in §9 was resolvable against the code plus §2.6's text.
