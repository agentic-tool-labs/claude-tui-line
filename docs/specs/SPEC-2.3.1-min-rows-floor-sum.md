# §2.3.1 / §2.3 — the over-constrained sum check, and what greedy actually does instead

Task #67. Written against `/Users/jimcline/git/repos/claude-tui-line`, branch `main` at `25fa255`
(#25 merged; `maxT` now reads `RowCountAt` at each candidate's floor, as specified).

No worktree was needed — this is a document. Implementation can read it from the repo root.

impl3's report is correct and its reproduction is real. The answer to the question it asked is
"no, but", and the "but" is worth more than the "no".

---

## 1. The direct answer: greedy's fill path cannot have this gap, by construction

`AllocateOnePass` and `SolveMinRows` allocate in **opposite directions**, and that is the whole
difference.

**Greedy subtracts from a running remainder.** Step 5 (`:423`) clamps percent to
`Math.Max(0, rem)`; step 6 (`:441-447`) computes `remClamped = Math.Max(0, rem)` and divides *that*
among the fill panes. A fill pane can therefore be granted `0`, but the fill grants can never sum
past what was left. `AllocateWithDrop`'s `grants[i] < 1` test (`:468`) then catches the `0` and
drops. **The guard works because the sum was already bounded before the guard ran.**

**Min-rows computes each width independently and sums afterwards.** `SolveMinRows` derives `lo[ci]`
per candidate from `Floor` (`:609`) with no reference to `r`, and `return lo` at `:648` hands them
all back unexamined. The one place the function *does* test the sum is the feasible path at `:641`:

```csharp
if (feasible && sum <= r)
```

So `SolveMinRows` checks `sum <= r` on every path that succeeds and on no path that fails. **The
fallback inherits none of the guarded path's invariants** — the same shape as §9.4.2's D3, where a
prefix rule shipped as an escape hatch from a bounded rule and inherited none of its bounds.

That is the defect, and it is min-rows-specific in the fill/content case. impl3's diagnosis is
right.

---

## 2. Greedy has the analogous gap anyway — through fixed panes

The peer's question was whether `AllocateOnePass` can over-allocate. It can, by a route neither of
us was looking at.

**Step 2 is unclamped** (`:370-377`):

```csharp
if (kinds[i].Kind == SizeKind.Fixed)
{
    grants[i] = kinds[i].FixedValue;
    rem -= grants[i];      // rem may go negative and is never floored here
}
```

**And the drop check explicitly exempts fixed panes** (`:468`):

```csharp
if (ClassifySize(current[i].Size).Kind != SizeKind.Fixed && result.Grants[i] < 1)
```

So two `size: 30` panes in a 46-column split are granted 30 and 30. `rem` is `−14`, nothing
downstream reads it (no content, percent, or fill panes to clamp), the `< 1` test skips both panes
because they are `Fixed`, and `AllocateWithDrop` returns an allocation summing to 60 in a budget of
46. No drop, no note.

**`AllocateMinRowsOnePass` has the identical bug**, at `:555-562` — the same unclamped fixed loop,
copied. So the fixed-pane over-allocation is one defect present in both policies, while the
floor-sum over-allocation is a second defect present only in min-rows.

---

## 3. A third finding, from impl3's own repro config under the other policy

Run impl3's exact reproduction — two `fill` panes, both with floor 24, `r = 46` — through **greedy**
instead. Step 6 gives `each = 46 / 2 = 23`. Both panes are granted 23 against a floor of 24, `23 >= 1`,
so no drop and no note.

`Floor` returns a declared `minSize` first (`:327-330`), so if that 24 came from `minSize: 24` —
which is the natural way to write the repro — then **an explicitly declared `minSize` is silently
violated**. And `Floor`'s own doc comment defines it as "the minimum outer width a pane can be
reduced to **before it is dropped**", which is exactly the threshold `AllocateWithDrop` does not
test. The drop predicate is `< 1`; §2.3's stated predicate is `< Floor`.

**One config, both policies, two different wrong answers in opposite directions.** Min-rows returns
floors and overruns the budget; greedy respects the budget and underruns the floors. That symmetry
is the strongest evidence that the drop predicate — not either allocator — is the thing that is
under-specified.

---

## 4. Ruled: the fix for #67, and only #67

Add the missing invariant test to `ResolveVerticalMinRows`'s drop-retry loop (`:515-540`), beside
the existing `tooSmall` test rather than inside `SolveMinRows`.

```csharp
var avail = Math.Max(0, splitOuterWidth - BoundaryCost(split, current.Count, collapse));
var overAllocated = result.Grants.Sum() > avail;

if ((!tooSmall && !overAllocated) || current.Count <= 1)
{
    return result;
}
```

**Why the loop and not `SolveMinRows`.** Patching `return lo` guards one branch of one function.
The loop guards the *invariant* — Σgrants ≤ avail — regardless of which path produced the
violation, which means it also catches §2's fixed-pane overrun on the min-rows side for free. It is
also where drop decisions already live, it already has `splitOuterWidth` and `collapse` in hand, and
it is the structural mirror of `AllocateWithDrop` that the comment at `:507-510` says this loop is
deliberately maintaining.

**`BoundaryCost` must be recomputed per iteration** with `current.Count`, not hoisted — the gutter
count falls with each drop, so a hoisted `avail` would be wrong on exactly the iterations that
matter.

**Termination is unchanged**: each iteration strictly shrinks `current`, and `current.Count <= 1`
still short-circuits.

**Name the residual case rather than pretend it is closed.** At `current.Count <= 1` the loop
returns a possibly-over-allocated single pane, because there is nothing left to drop. Greedy has
the same terminal behaviour. That is out of scope here, but see §7 — I do not know what the
compositor does with a pane wider than its budget, and nobody should assume it clips.

---

## 5. What must not change

1. **`SolveMinRows`'s feasible path.** `if (feasible && sum <= r)` at `:641` and the `int?`
   short-circuit in `MinWidthForRowCount` are §2.3.3's preferred form and are correct. This change
   adds a check downstream of them; it does not touch them.
2. **`return lo` stays.** Floors are the right *answer* for the over-constrained case per §2.3.3;
   what was missing is the caller noticing that the answer does not fit. Do not make `SolveMinRows`
   return a sentinel or a nullable — that relocates the decision into the function that has no
   authority to drop panes.
3. **The greedy path is untouched by #67.** §2 and §3 are separate tasks; see §6.
4. **The note text and §9.8.2 position convention** stay identical to `:483` and `:538`. An
   over-allocation drop is the same event as a too-small drop from the reader's side.
5. **`maxT` at `:616-620`** — #25's fix. Leave it.

---

## 6. Recommended as separate tasks, deliberately not folded in

**#67a — fixed panes over-allocate on both paths (§2).** `AllocateOnePass:370-377` and
`AllocateMinRowsOnePass:555-562`. The `!= SizeKind.Fixed` exemption at `:468` is probably correct in
intent — a fixed pane granted its declared width is not "too small" — but it means nothing ever
tests whether the fixed panes *collectively* fit. The min-rows half of this is fixed for free by §4;
the greedy half is not. Small blast radius, no product call needed.

**#67b — the drop predicate should be `Floor`, not `1` (§3).** This is the deeper one and it is
**not** a pure bug fix: changing `grants[i] < 1` to `grants[i] < Floor(...)` will start dropping
panes in configs that currently render, including configs where the only symptom today is a pane
slightly under its declared `minSize`. That is a **product call and I am not making it** — it trades
"renders cramped" for "renders one pane fewer, with a note". Both are defensible; the second is what
§2.3 says. Route to Jim.

Both are the same root: **the drop predicate is under-specified in §2.3**, which defines `Floor` as
the drop threshold and then never says the allocator must test it. If #67b is approved, §2.3 needs
a sentence saying so explicitly, or this returns.

---

## 7. Verification

1. **impl3's repro, min-rows.** Two `fill` panes, `gutter: 1`, floors 24, `r = 46`. Assert Σgrants ≤
   avail **and** that a `pane 2 dropped: …` note is emitted. Asserting only the sum passes if the
   drop happens silently, which is the failure #25 §5 just fixed and which would regress unnoticed.
2. **The same config under `greedy`.** Assert current behaviour explicitly — both panes at 23 — as a
   characterization test, so that if #67b is approved the change shows up as a deliberate diff on a
   named test rather than as a surprise. Comment it as characterizing, not endorsing.
3. **Drop-to-one.** Three candidates whose floors cannot fit in any combination. Assert the loop
   terminates, that it stops at one pane, and that two notes were emitted with positions 3 then 2.
4. **No regression on the feasible path.** A config where a feasible `T` exists must produce
   byte-identical output to `25fa255`. The new check must be unreachable when `:641` succeeded,
   since water-fill already caps at `r`. If this test fails, the check is in the wrong place.
5. **`BoundaryCost` recomputation.** A config that requires two drops where the per-iteration gutter
   count changes the verdict — i.e. it is over-allocated at three children and fits at two purely
   because a gutter was released. This is the test that fails if `avail` is hoisted out of the loop.

**NEEDS-EVIDENCE (N1).** What does the compositor do with a pane whose resolved width exceeds the
surface budget — clip, or shear the row? §2.4 rule 1 requires every row padded to exactly the pane's
width and calls a short row "the ugliest possible failure". I could not determine whether an
*over*-wide pane is clipped at composition or propagates. **What it decides:** whether §4's residual
`current.Count <= 1` case is cosmetic or is a live rendering corruption that needs its own task.
Cheap to answer with the repro config forced down to one pane.

---

## 8. Confidence

**High** on §1 (the asymmetry and why greedy's fill path is safe), §2 (fixed-pane over-allocation on
both paths), and §4 (the fix and its placement). All three are read directly off the code above.

**High on the mechanics of §3, lower on its significance.** That greedy grants 23 against a floor of
24 follows from `:441-447` and `:327-330` and I am confident in it. Whether that is a defect worth
changing depends on how load-bearing `minSize` is meant to be, which is §6's product call and not
mine.

**One thing I did not verify.** I did not trace whether any caller between `ResolveVerticalMinRows`
and the compositor re-clamps grants. If one does, #67's severity drops from "renders wrong" to
"internally inconsistent but harmless", though the fix is still correct. N1 covers it.

Not escalation-worthy. The change in §4 is small, local, well-tested by item 4's byte-parity
condition, and reversible. §6b is the only item carrying real risk and I have routed it rather than
decided it.
