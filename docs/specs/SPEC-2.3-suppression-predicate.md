# §2.3 — border suppression tests the wrong width, and does not reclaim the width it saves

Task #73 (verification item 4 of `SPEC-2.3-drop-predicate.md`; blocks that task's close).
Written against `/Users/jimcline/git/repos/claude-tui-line` branch `main`, and worktree
`claude-tui-line-task-71` @ `d28d7bf` (since merged).

## Amendment history

- **A1** — N1 answered (suppression does not reclaim). Turned §6 from a question into a ruling.
- **A2 — CORRECTS AN ERROR IN A1.** A1's §6.1 claimed defect A must never land without defect B, on
  the grounds that A-alone is worse than neither. **That was wrong, and I am retracting it.** The
  arithmetic is in §6.1; the short version is that A-alone is a strict improvement, the two fixes are
  **not coupled**, and **#73 §1-§4 should proceed to merge as it already is.** If you paused work on
  A because of A1, resume. A2 also promotes defect B to its own task with a go/no-go answer (§6.3).

**Ruling: the spec is right, the code is wrong, in two independent places — but they are two
independent fixes, not one.**

---

## 1. Defect A — the predicate compares against the wrong width

`SPEC-V2-FRAMEWORK.md:879` — suppression triggers on resolved **inner** width `< MinUsableWidth` (20).

`SizeResolver.cs:67-76`:

```csharp
public static bool ShouldSuppressBorder(Pane pane, int outerWidth)
{
    if (pane.Border.Style is null || outerWidth >= RowLayout.MinUsableWidth)
    {
        return false;
    }
    var kind = ClassifySize(pane.Size).Kind;
    return kind is SizeKind.Fill or SizeKind.Percent;
}
```

Suppress iff bordered **and `outerWidth < 20`** and fill/percent. The constant is compared against
**outer**, the spec says **inner**.

**Corroboration independent of the prose.** `Floor` at `:355` is
`RowLayout.MinUsableWidth + OwnBorderReserve(p, excludeLeft, excludeRight)`. That `+ reserve` term
exists *only* because 20 is a budget on **content**. Under the outer reading the floor would simply
be `MinUsableWidth` and the term would be meaningless. `:709`
(`innerWidth = outerWidth - OwnBorderReserve(candidate)`) is a third site built on the inner reading.

`OwnBorderReserve` (`:100-102`) is `2 + left + right` — **4** for a fully-edged pane, not 2. The
disagreement band is 4 columns wide.

---

## 2. What defect A costs

#71's `DropFloor` (`SizeResolver.cs:365-369`):

```csharp
var floor = ShouldSuppressBorder(pane, grant)
    ? RowLayout.MinUsableWidth
    : Floor(pane, collapse, excludeLeft, excludeRight);
```

A bordered `fill` pane, reserve 4:

| grant | code: suppress? | code: floor | code | spec: suppress? | spec: floor | spec |
|---|---|---|---|---|---|---|
| 19 | yes (19 < 20) | 20 | **dropped** | yes (inner 15 < 20) | 20 | **dropped** |
| 22 | no (22 ≥ 20) | 24 | **dropped** | yes (inner 18 < 20) | 20 | **survives** borderless |
| 24 | no | 24 | survives, bordered | no (inner 20) | 24 | survives, bordered |

Below 20 the code suppresses and the pane is dropped anyway. In [20, 23] suppression is the one
thing that would save the pane, and the predicate declines to fire.

**As coded, border suppression never changes the outcome of any pane on the drop path.** It fires
only where it cannot help, and stays silent exactly where it would.

This is a **regression #71 created**. Before #71 the drop predicate was `grant < 1`, so a pane at
outer 19 suppressed and rendered borderless. #71's floor converts that pane to *dropped*.

---

## 3. The circularity — why the code tests outer, and how to test inner without it

Testing inner looks self-referential: inner depends on reserve, suppression zeroes reserve. The
author was demonstrably alert to this — `PaneTreeRenderer.cs:47-51` names the same circularity on
the *height* axis and dodges it there deliberately.

**Ruled: the predicate is defined on the PRE-suppression inner width — one evaluation, no fixed
point.**

> Suppress iff the pane is bordered, is `fill` or percent, and `outerWidth − reserve < 20`, where
> `reserve` is the pane's own border reserve **as if the border were drawn**.

Non-circular because it is monotone in one direction: suppression only ever *removes* reserve, so
post-suppression inner (`= outer`) is strictly greater than pre-suppression inner. Suppressing can
never re-trigger the condition, so there is nothing to iterate. This is also exactly the reading
`Floor`'s `MinUsableWidth + OwnBorderReserve` already encodes.

---

## 4. The change for defect A

**Change `ShouldSuppressBorder` to take the inner width, and make both callers compute it.**

```csharp
public static bool ShouldSuppressBorder(Pane pane, int innerWidth)
```

with the guard becoming `innerWidth >= RowLayout.MinUsableWidth`. Taking inner rather than computing
it internally is deliberate: the two callers derive reserve differently and only they have the
inputs.

- **`PaneTreeRenderer.cs:43`** already has the value one line above (`:41-42` compute `borderReserve`
  from `effectiveBorder`, which handles `collapse` edge-exclusion, then `innerWidth`). Pass
  `innerWidth` instead of `node.OuterWidth`. No new arithmetic.
- **`DropFloor` (`SizeResolver.cs:365-369`)** must pass
  `grant − OwnBorderReserve(pane, excludeLeft, excludeRight)`, using **the excludes it already
  receives**. It currently passes `grant` — an outer width — into a parameter the spec defines as
  inner.

**This reverses the note at `SizeResolver.cs:363`** ("no longer needs `OwnBorderReserve`. Reuses
`ShouldSuppressBorder` as-is"). The instinct — do not restate the predicate — was right and survives;
only the argument changes.

### The collapse mismatch this also closes

`ShouldSuppressBorder` has no `excludeLeft`/`excludeRight`, while `Floor:355` and `DropFloor` both
do. Under `collapse: true` the allocator reasons about edge-excluded reserve while the suppression
predicate reasons without it — so a pane can be *allocated* borderless and *rendered* bordered, or
the reverse. Threading inner width from callers that already hold the correct excludes closes this,
which is the main reason to prefer it over adding two more parameters.

---

## 5. Scope — a separate task, not a #71 amendment

#71 has merged. This is its own task with its own diff and its own tests.

---

## 6. Defect B — suppression does not reclaim the reserve

**N1 asked whether `PaneBorderRenderer.Wrap` lays out at `outer` or `outer − reserve` when
suppressed. Answer, now confirmed twice (independently by impl3 against the code): `outer −
reserve`.** `PaneBorderRenderer.cs:38-39` computes `outerWidth = width + OwnBorderReserve(border)`
and `:64` adds the same reserve to every content row, unconditionally — only the glyph-vs-space
choice at `:41,43-44` toggles on `suppressed`. The docstring at `:14-19` states it as intent:
*"Suppression keeps the same reserved geometry … but draws blank chrome instead of glyphs — one code
path for both cases, not a separate borderless layout."*

**So suppression today changes glyphs to spaces and returns no columns to content.**

### 6.1 CORRECTION — A and B are not coupled, and A may land alone

A1 asserted that fixing A without B is worse than fixing neither. **That is wrong.** Working the
three states for a bordered `fill` pane at outer 22, reserve 4:

| | outcome at outer 22 |
|---|---|
| today (neither fix) | **dropped**, with a below-floor note |
| A alone | **survives**, laid out at inner 18, 4 columns of blank chrome |
| A + B | **survives**, laid out at inner 22 |

A-alone renders 18 columns of content where today renders none. That is a **strict improvement**, not
a regression. My A1 framing — that it converts a behaviour Jim sanctioned in #67b into one he did not
— does not hold up: #67b's ruling was about the floor causing drops, and A moves the suppressible
pane's boundary from 24 to 20, which is what §2.3 said all along. `MinUsableWidth` is a heuristic
threshold, not a hard rendering requirement, so landing at inner 18 is degraded but coherent output.

**Ruled: no ordering constraint. #73 §1-§4 proceeds to merge on its own.**

One genuine residual, worth a line in the commit but not a blocker: with A alone, `DropFloor`
promises inner 20 while the renderer delivers 18 — the allocator's model and the rendered result
disagree by the reserve. Nothing currently reads the grant in a way that makes this visible, but B is
what makes the two agree again.

### 6.2 Defect B is real — the spec settles it

> …**suppresses its own border first** … **and is dropped only if it still does not fit.**

"Suppresses first … dropped only if it *still* does not fit" is only meaningful if suppression
changes whether it fits. Under blank-chrome suppression, "still does not fit" is identical to "does
not fit" and the entire sentence is inert. `:879`'s parenthetical — *"now applied per pane rather
than per surface"* — points the same way: §6b's original per-surface suppression reclaimed the
surface's own border columns, and this is that mechanism moved down a level, not a different one.

**And the allocator already assumes reclamation.** `DropFloor` returns `MinUsableWidth` — a bare 20,
with no reserve added — when suppressed. That *is* reclamation arithmetic, sitting in the codebase
today. Two of the three artefacts (spec prose, allocator) say reclaim; one (the renderer and its
docstring) says do not.

**That impl3 confirmed the current behaviour is deliberate does not change this.** Deliberate and
correct are different claims, and neither of the two decisive pieces of evidence — §2.3's "still does
not fit", `DropFloor`'s bare 20 — is touched by the docstring's intent. A deliberate choice can be
inconsistent with the spec it implements.

### 6.3 GO / NO-GO on defect B: **GO**

The peer asked for a real answer, so: **go.** Implement reclamation. A suppressed pane lays its
content out at its full outer width; `Wrap` zeroes the reserve, the padding, and the corner glyphs
when `suppressed` is true. impl3's traced candidate fix (non-circular, keeps row widths consistent)
is the right shape and should be picked up.

**Item 7 of §8 stands as written — do not strike it.** It is the assertion that currently fails, and
that is exactly what makes it worth having.

**Scope: defect B is its own task, not part of #73.** impl3 was right to decline it as a behaviour
reversal outside a predicate-fix's scope, and #73 §1-§4 is already out for verify. Splitting it costs
nothing (§6.1 removed the ordering constraint) and keeps a behaviour reversal in a diff where it can
be seen.

### 6.4 The counter-argument, stated honestly

The blank-chrome design buys one real thing: **sibling alignment.** A suppressed pane that reclaims
its columns starts its content 2 columns left of an unsuppressed sibling stacked above or below it,
and that misalignment is visible.

I am overriding it anyway. Suppression only fires on `fill`/percent panes already below the usable
width — a degraded state the whole ladder exists to claw content back from. Preferring alignment to
usable content *in the degraded state* inverts the ladder's purpose. **But this is a deliberate,
documented design decision being reversed, not an oversight being corrected, and it should be flagged
as such in the commit rather than slipped in.** It is also the one thing in this file worth putting
in front of Jim, since it changes how a degraded pane *looks*, not merely whether it survives.

---

## 7. What must not change

1. **The `fill`/percent restriction.** Suppression stays limited to `fill` and percent panes.
2. **`MinUsableWidth = 20`.** The constant is right; only the width compared against it is wrong.
3. **`Floor:355`'s `MinUsableWidth + OwnBorderReserve`.** It is correct and is the evidence for both
   defects. Do not "simplify" it to match the old predicate.
4. **Height suppression** (`PaneTreeRenderer.cs:52-60`) is a separate mechanism on a separate axis
   and is out of scope. It may have the same reclaim-vs-blank question on the row axis — **do not fix
   that here**, but file it if you see it.
5. **`OwnBorderReserve`'s `2 + left + right`.** The base 2 is content padding
   (`PaneBorderRenderer.cs:64` adds `" "` each side), not a glyph count.
6. **§2.4 rule 1.** A reclaiming suppressed pane still pads every row to its full outer width. The
   rectangle invariant is not what changes — only where the content boundary sits inside it.

---

## 8. Verification

Items 2-6 belong to defect A (task #73). Items 1, 7, and 8 belong to defect B and travel with that
task.

1. **(B) A suppressed pane's content occupies its full outer width.** A bordered `fill` pane, full
   edges (reserve 4), forced into suppression. Assert the content row's usable width equals the
   pane's outer width, not `outer − 4`, and that the rendered row is still exactly outer columns
   wide. **The headline test for B; fails on `main` today.**
2. **(A) The [20, 23] band survives.** Bordered `fill`, reserve 4, granted 22. Assert it is **not**
   dropped and renders **without** border glyphs. Under A alone its inner width is **18**; under
   A + B it is **22**. Assert only the survival and the absent glyphs here — item 7 owns the width.
3. **(A) Below-20 still drops.** Same pane granted 19. Assert dropped, with #67b's below-floor note.
4. **(A) At-and-above the floor keeps its border.** Same pane granted 24. Assert it survives **with**
   its border and inner width 20. The boundary the fixes must not move.
5. **(A) Reserve variants.** A pane with `edges: {left: false, right: false}` has reserve 2, so its
   band is [20, 21] and its floor 22. Assert the boundary moves with the reserve — this fails if
   anyone hardcodes 24 or 4.
6. **(A) `collapse: true` agreement.** A vertical split with `collapse: true` and an interior child
   near the boundary. Assert the allocator's suppression decision and the renderer's agree — a pane
   allocated borderless renders borderless. Covers §4's collapse mismatch, which nothing tests today.
7. **(B) The reclaimed width is the full outer width.** Item 2's config, asserting inner width
   **22**. This is the assertion §6.3 declines to strike: it fails under A alone and passes under
   A + B, which is precisely its job — it is the tripwire that says B has not landed yet.
8. **(B) Blank chrome is gone, not merely invisible.** A suppressed pane adjacent to a sibling:
   assert no run of spaces sits where the border glyphs were. Distinguishes "reclaimed" from "drew
   spaces", which items 1 and 7 could both pass under a partial fix that widens content but keeps the
   padding.

**Derived expectations, not observed ones.** Every number above is derivable: reserve
`= 2 + 1 + 1 = 4`, floor `= 20 + 4 = 24`, suppressed floor `= 20`. Write them from that arithmetic.
**Do not run the implementation and record what it prints** — the same-author inversion hazard
`SPEC-2.3-drop-predicate.md` §6 item 1 was pinned against applies here with equal force.

---

## 9. Confidence

**High** on defect A (§1-§4). Every claim is read off verbatim code — `ShouldSuppressBorder`,
`Floor`, `OwnBorderReserve`, `DropFloor` — plus one normative spec clause. §2's table is arithmetic
over those, not inference.

**High** on §6.1's correction. It is a three-row table over the same arithmetic, and I should have
worked it before asserting the coupling in A1 rather than after. **A1's §6.1 was an overstated
blocker, and an architect overstating a blocker to force an ordering is a failure mode worth naming:
it spends someone else's time on a constraint that does not exist.** The retraction is the finding,
not something to bury — but if anyone paused #73 on account of it, that pause was my cost to have
caused.

**Medium-high on §6.3's GO, and deliberately flagged.** §6.4 is a real counter-argument and the
current behaviour is deliberate, documented design confirmed by two independent readers. I am
overriding it on §2.3's "dropped only if it still does not fit" plus `DropFloor`'s bare-20, which I
read as jointly decisive. **If a second opinion is being spent anywhere on this file, spend it on
§6.3** — and it is worth Jim seeing, since it changes the appearance of a degraded pane.

**One thing I did not verify:** whether height suppression (`PaneTreeRenderer.cs:52-60`) has the same
reclaim-vs-blank split on the row axis. It looks structurally similar. Out of scope here; worth its
own look.
