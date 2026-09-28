# §2.8.2 — height suppression at zero content rows

Task #80. Written against `main` @ `25facff`.

**Scope note.** The dispatch framed #80 as partly a product question about the suppression heuristic.
It is mostly not. §1 shows the natural-height path's suppression branch **can only ever fire in the
degenerate case**, where it deletes the pane instead of helping it. That is a mechanism defect with a
determinate fix (§3). One genuine product call survives, and it is stated in §5 rather than decided.

---

## 1. The finding — line 159's branch is unreachable except when harmful

`PaneTreeRenderer.cs:156-159`, the path taken when `node.ClipRows is null` (no degrade-ladder budget):

```csharp
if (node.ClipRows is null)
{
    var naturalTotal = contentRows.Count + (bordered ? 2 : 0);
    heightSuppressed = bordered && naturalTotal < 3 && !ownDeclaredTiny;
}
```

Work the arithmetic. The branch requires `bordered`, so `naturalTotal == contentRows.Count + 2`.
Therefore:

```
naturalTotal < 3  ⟺  contentRows.Count + 2 < 3  ⟺  contentRows.Count < 1  ⟺  contentRows.Count == 0
```

**On the natural-height path, height suppression fires if and only if there are zero content rows.**
There is no other input that satisfies it. This is not a case the heuristic handles badly among others
it handles well — it is the *only* case it ever sees.

Now follow what it does there. `heightSuppressed` is passed to `PaneBorderRenderer.Wrap` as
`omitEdges` (`:163`), and `Wrap` gates both edge rows on it (`:57`, `:66`):

```csharp
if (edges.Top && !omitEdges) { rows.Add(...); }
rows.AddRange(contentRows.Select(...));      // contentRows is empty
if (edges.Bottom && !omitEdges) { rows.Add(...); }
```

With `contentRows` empty and `omitEdges` true, **`Wrap` returns an empty list. The pane renders
nothing at all** — it does not become a thin box or a blank line; it is absent from the output.

Without suppression the same pane renders its top and bottom edges: a 2-row empty box, which is what
a bordered pane containing nothing is supposed to look like.

**So the mechanism's every activation on this path destroys the pane.** §2.8.2's stated bargain is in
`PaneBorderRenderer.cs:22-25` — a pane that *"cannot close its box … drops the top and bottom edge
rows entirely (reclaiming both for content)"*. **Reclaiming space for content is only meaningful when
content exists.** At zero content rows the two reclaimed rows go to no beneficiary, and the cost is
the whole pane.

This is the same defect shape as #73: **a predicate whose stated rationale carries a precondition the
predicate never tests.** The rationale says "for content"; nothing checks that there is any.

### 1.1 The `ClipRows` path has the same hole, reached differently

`:56-59`:

```csharp
if (node.ClipRows is int budget)
{
    heightSuppressed = bordered && budget < 3 && !ownDeclaredTiny;
    maxContentRows = (heightSuppressed || !bordered) ? Math.Max(0, budget) : Math.Max(0, budget - 2);
}
```

Here the heuristic is genuinely useful and mostly correct. Traced:

| `budget` | `heightSuppressed` | `maxContentRows` | rows out | verdict |
|---|---|---|---|---|
| 0 | true | 0 | 0 | correct — a 0-row budget should produce 0 rows |
| 1 | true | 1 | 1 content | **correct, and this is the case the feature exists for** |
| 2 | true | 2 | 2 content | correct |
| 3 | false | 1 | 1 content + 2 edges | correct |

The bargain pays off at `budget` 1 and 2: real content survives where an unsuppressed box would have
spent the entire budget on chrome. **Nothing in §3 should disturb this.**

The hole is that `maxContentRows` is a *cap*, not a guarantee. A pane with `budget: 2` whose items all
resolve to null renders **zero** content rows, and then `Wrap` with `omitEdges: true` emits nothing —
the same disappearance as §1, with two rows of budget available and unused.

Note this decision is deliberately made *before* content renders. The comment at `:45-51` is explicit
that this avoids *"a circular dependency between 'how much content to keep' and 'will the border be
suppressed'."* **That reasoning is correct and §3 must not break it.** The fix therefore cannot live
at `:58`.

### 1.2 Why this is a collision, not just a cosmetic miss

The codebase already has a mechanism for "this pane has nothing to say, should it disappear?" — it is
`PaneCollapse` (§2.11.2), and it has rules. `ConfigCheck.cs:784` records the load-bearing one:

> an explicit `minSize` suppresses collapse (§2.3's floor table: "author said so; always wins")

Height suppression performs a **second, uncontrolled collapse that honours none of those rules.** A
pane the author protected with an explicit `minSize` — a pane collapse is forbidden to remove — still
vanishes through `:159`, because this path never consults collapse at all. Two mechanisms decide the
same question and only one of them was designed to.

That is the strongest argument that §1 is a defect rather than an undocumented feature: if empty panes
should disappear, `PaneCollapse` is where that is decided, and it is already written.

---

## 2. Files to change

- `src/ClaudeTuiLine/PaneTreeRenderer.cs` — the fix (§3).
- `src/ClaudeTuiLine/PaneBorderRenderer.cs` — **doc comment only** (§3.3). No behaviour change.
- `tests/ClaudeTuiLine.Tests/BorderSuppressionPredicateTests.cs` — new cases (§4).

`SizeResolver.ShouldSuppressBorder` is the **width** predicate and is **not** in scope. Do not touch
it; #80 is entirely about the height axis.

## 3. The fix

### 3.1 The rule

**Height suppression requires a beneficiary.** Restated as the implementable condition:

> Drop the top and bottom edge rows only when there is at least one content row to reclaim them for,
> **or** when the row budget is too small to draw the box anyway.

The second clause preserves §1.1's correct cases. Concretely, at zero content rows:

- **budget ≥ 2, or no budget at all** → do **not** suppress. Draw the empty box (2 rows). There is
  room for it and nothing to gain by dropping it.
- **budget 0 or 1** → suppress, as today. The box genuinely does not fit, and emitting nothing is the
  honest outcome. This is unchanged behaviour.

The natural-height path has no budget, so it always lands in the first case — which, per §1, means
**`:159`'s suppression branch becomes unreachable.** That is the intended outcome, not an accident:
§1 showed it was only ever reachable in the case this spec forbids.

### 3.2 Where it goes

**Not at `:58`.** The `ClipRows` decision must stay content-blind to preserve the non-circularity the
`:45-51` comment protects.

**At the `Wrap` call site, `:163`**, where `contentRows` exists and the row count is known. Today:

```csharp
var borderedRows = PaneBorderRenderer.Wrap(contentRows, innerWidth, effectiveBorder, borderColorMarkup, suppressed, heightSuppressed);
```

Introduce the beneficiary test immediately above it and pass the corrected flag. Something of this
shape — the implementor may name things differently, but the condition must be exactly this:

```csharp
// §2.8.2 reclaims the edge rows FOR content; with no content rows there is no beneficiary and
// suppression would erase the pane rather than shrink it. Below a 2-row budget the box cannot be
// drawn either way, so suppression still stands.
var budgetFitsBox = node.ClipRows is not int clip || clip >= 2;
var omitEdges = heightSuppressed && (contentRows.Count > 0 || !budgetFitsBox);
var borderedRows = PaneBorderRenderer.Wrap(contentRows, innerWidth, effectiveBorder, borderColorMarkup, suppressed, omitEdges);
```

**This is not circular.** `contentRows` is fully resolved by `:154`; the value is read once,
downstream, and never feeds back into the budget that produced it.

**`heightSuppressed` itself must keep its current value** — do not reassign it at `:159` or `:58`.
`:166` writes `rowCounts[pane] = borderedRows.Count`, which the degrade ladder consumes, and that
count must reflect what was actually emitted. Deriving a separate `omitEdges` and leaving
`heightSuppressed` alone keeps the two concerns separable and keeps the diff auditable.

### 3.3 The doc comment

`PaneBorderRenderer.cs:21-27` documents `omitEdges` as the height-axis twin of `suppressed`. It should
state the precondition the parameter now carries — that the caller has established there is something
to reclaim the rows for. One or two sentences, at the existing `<param>` block. **Do not restate the
call-site condition there**; it belongs to the caller and duplicating it invites the two to drift.

## 4. Verification

Render end-to-end through `PaneTreeRenderer.Render`, as #73b's tests did — the claim is about emitted
output, not about `SizeResolver` arithmetic.

1. **The headline case.** A bordered pane, no `maxRows`, no `ClipRows`, whose items all resolve to
   null so it renders zero content rows. **Assert it emits exactly 2 rows** (top edge, bottom edge),
   not 0. *This test must be shown to FAIL on `main` @ `25facff` before the fix.* If it passes on
   `main`, §1 is wrong and the implementor must stop and report rather than proceed.
2. **The `minSize` collision (section 1.2).** Same as item 1 but with an explicit `minSize` set — the case
   collapse is forbidden to remove. Assert the pane survives. This is the test that documents *why*
   §1 is a defect and not a feature, so it should carry a comment naming §2.11.2.
3. **`ClipRows: 2`, zero content rows.** Assert 2 rows out (the empty box), not 0. This is §1.1's
   variant and it is the one most likely to be missed by a fix aimed only at `:159`.
4. **`ClipRows: 1`, zero content rows.** Assert **0** rows out. Unchanged behaviour — the box does not
   fit. This is the guard against over-correcting §3.1.
5. **`ClipRows: 0`.** Assert 0 rows out. Unchanged.
6. **The feature still works — `ClipRows: 1` with one content row.** Assert exactly 1 row out,
   the content, no edges. **This is the highest-value regression test in the list**: it is §1.1's
   whole reason for existing, and a careless fix that requires `contentRows.Count > 0` *and*
   `budget >= 2` would break it.
7. **`ClipRows: 2` with two content rows.** Assert 2 rows, no edges. Unchanged.
8. **`ownDeclaredTiny` still wins.** A pane with `maxRows: 2` and a border keeps its border and loses
   content, per `:50-51`. Assert unchanged by this task.
9. **Unbordered panes are untouched.** A borderless pane with zero content rows emits 0 rows before
   and after.

Items 1 and 3 are the defect; item 6 is the thing most likely to be broken while fixing it.

## 5. The product call I am NOT making

**Should a bordered pane with no content render as an empty box, or vanish?**

§3 rules it renders an empty box, on the mechanism grounds in section 1.2: vanishing is `PaneCollapse`'s
decision, it has rules including the `minSize` override, and height suppression bypassing them is a
collision rather than a policy. I am confident in that as *engineering*.

But it is visible behaviour, and someone may prefer that empty bordered panes disappear. **If that is
wanted, the right change is to route it through `PaneCollapse` so `minSize` still wins** — not to
leave `:159` as an unlabelled second collapse path. Flagging for Jim; §3 stands unless he says
otherwise, and reversing it later costs items 1-3 of §4 and nothing else.

## 6. What must not change

1. **The non-circularity of the `ClipRows` decision** (`:45-51`). The fix reads `contentRows` strictly
   downstream. Nothing about *how much content to keep* may depend on the suppression outcome.
2. **`ShouldSuppressBorder` and the width axis.** Not in scope.
3. **`:166`'s `rowCounts[pane] = borderedRows.Count`.** The ladder must keep seeing the true emitted
   count. Items 1 and 3 change that count from 0 to 2 by design — that is the fix working, and the
   ladder should see it.
4. **`ownDeclaredTiny`.** `maxRows < 3` remains an author choice that keeps the border.
5. **#73b's width-reclaim behaviour.** Different axis, same file. Do not disturb it.

## 7. NEEDS-EVIDENCE

**E1 — does §4 item 1 fail on `main` @ `25facff`?** This is the whole spec's load-bearing claim and
I derived it by reading, not by running. Write item 1, run it against unmodified `main`, and report
the actual emitted row count. **Expected: 0 rows. If it emits 2, §1 is wrong — stop and report.**

**E2 — what does `PaneAssembler.RenderLeafRows` return for a pane whose items all resolve to null?**
I assumed zero rows. If it instead returns one blank row, then `contentRows.Count == 1`, `:159` never
fires, and the natural-height half of §1 is moot (§1.1 would survive on its own). I was wrong about
exactly this distinction on a previous task — an empty segment's *text* is not an empty segment
*list* — so it is the assumption I trust least here. Report the row count for that input before
writing item 1.

**E3 — is there an existing test asserting a bordered empty pane emits 0 rows?** `grep` for it in
`tests/`. If one exists and passes deliberately, §5's product call has already been made by someone
and this spec needs re-dispatching rather than implementing.

## 8. Confidence

**High on §1's arithmetic.** `contentRows.Count + 2 < 3 ⟺ contentRows.Count == 0` is not a judgment
call, and `Wrap`'s two `!omitEdges` gates make the empty-output consequence mechanical.

**High on section 1.2** being the right frame — `ConfigCheck.cs:784` states the `minSize`-beats-collapse rule
in as many words, and nothing on this path consults it.

**Medium-high on §3.1's budget clause.** The `budget < 2` carve-out is derived from the table in §1.1
rather than from any spec text, so it is my construction. It is the minimum needed to leave the
working cases alone, but if §2.8.2 has language about sub-2 budgets I have not seen, that language
wins over my table.

**Conditional on E2.** If `RenderLeafRows` emits a blank row rather than none for an all-null pane,
§1's natural-height half collapses and only §1.1 remains. The fix in §3 is unchanged either way — it
would just be narrower in reach — but the spec should be corrected rather than quietly over-claiming.
