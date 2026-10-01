# §2.3 — the last surviving pane can be granted more columns than the split has

Task #74. Written against `/Users/jimcline/git/repos/claude-tui-line` branch `main` (post-#71 merge).

**Ruling: clamp every returned grant to the split's available width at both drop-loop exits, and emit
a note when the clamp bites.**

## Amendment history

- **A1** — resolved the two facts the first draft marked `[PENDING-A1]`. `BoundaryCost` is **not**
  zero for a single child (§8), which changed the verification arithmetic. And `SizeResolver.cs:846`
  turned out to be a **third width-axis drop loop** with three independent defects of its own, which
  changed its disposition from "fold in" to "**its own task** — see §4.3". No `[PENDING]` markers
  remain; this spec is implementable as written.

---

## 1. The defect

Both drop loops terminate on the same guard:

```csharp
// SizeResolver.cs:503 (AllocateWithDrop) and :587 (ResolveVerticalMinRows)
if ((!tooSmall && !overAllocated) || current.Count <= 1)
{
    return result;
}
```

Read the disjunction carefully. The left arm is *"every invariant holds"*. The right arm is
**unconditional** — when one child remains, the loop returns `result` **whether or not
`overAllocated` is true**. The guard is not "we checked and it is fine"; it is "we have run out of
things to drop, so we are handing back a number we have just computed to be wrong."

`overAllocated` is `result.Grants.Sum() > avail` where
`avail = Math.Max(0, splitOuterWidth - BoundaryCost(split, current.Count, collapse))`. With one
child, `Sum()` is that child's grant. So the escape hatch returns **a pane wider than the split it
lives in**.

### Where the over-grant comes from

Two independent producers, one per loop:

1. **`AllocateOnePass` — fixed panes.** `SizeResolver.cs:626-630` assigns
   `grants[i] = kinds[i].FixedValue` and then `rem -= grants[i]`, with no ceiling. `rem` is allowed
   to go negative. A `size: 50` pane in a 20-column split is granted 50.
2. **`SolveMinRows`'s over-constrained fallback.** `:580-583`'s comment states it: *"SolveMinRows's
   over-constrained fallback (`return lo`) hands back each candidate's floor with no reference to
   `r`, so the sum can exceed what the split actually has."* With one candidate, that floor is
   returned unclamped — and post-#71 that floor can be `MinUsableWidth + reserve = 24`.

The comment at `:580` proves the over-allocation was *known*; the `Count <= 1` arm is what lets it
escape unhandled.

### Why this is not a rare degenerate tail

`Count <= 1` is reachable on the **first iteration**, not only after a cascade of drops. A split with
exactly one child — the single commonest shape in this config language — enters the loop with
`current.Count == 1` and takes the right arm immediately, every time, no matter what. Every
single-child split has been running with the over-allocation check effectively disabled.

### What it costs downstream

The grant is not a private number. It is consumed by:

- **§2.4 rule 1** (every row is exactly the surface width). An over-wide pane renders an over-wide
  row. This is the visible failure: a torn or wrapped statusline.
- **The note text itself.** `:515` and `:521` interpolate grants into user-facing notes.
- **#73's suppression predicate**, which after that task's fix takes `grant − reserve` as an inner
  width. Garbage in.
- **The min-rows row count**, which is solved *at* the granted width.

---

## 2. Ruling

> At each drop loop's return, every grant is clamped to the split's available width. When the clamp
> changes a grant, a note records it.

Formally, on the returned `AllocResult`: `grants[i] := Math.Min(grants[i], avail)`, with `avail`
computed from `current.Count` exactly as the loop already computes it.

**On the healthy exit this is a provable no-op** — `!overAllocated` means `Sum() <= avail`, and every
grant is non-negative, so every individual grant is already `<= avail`. Applying the clamp at the
single shared return point rather than only inside the `Count <= 1` arm is deliberate: it makes the
postcondition *"no returned grant exceeds `avail`"* true by construction and readable at one site,
rather than true by a case analysis a future editor has to redo.

**Note the postcondition this buys at these two sites is the full one.** A per-grant clamp only
guarantees `max(grants) <= avail`, not `sum(grants) <= avail`. At `:503` and `:587` those coincide,
because the only way to reach the exit with `overAllocated` true is the `Count <= 1` arm, where the
sum *is* the single grant. That equivalence is why the clamp alone suffices here — and why it does
**not** suffice at the third site (§4.3).

### 2.1 The clamp binds fixed panes too

`:495-499`'s comment says fixed panes' grants are *"their declared size, never shrunk"*. That
sentence is the origin of producer (1), and it is right as an **allocation policy** and wrong as a
**physical guarantee**. A `size: 50` pane in a 20-column terminal does not have a 50th column to
render into; declaring it does not create one. The clamp is not the allocator changing its mind about
fixed sizing — it is the terminal's width asserting itself at the boundary where the allocator stops
being an arithmetic exercise and starts describing real cells.

**The clamp applies to every `SizeKind` without exception, including `Fixed`.** Exempting `Fixed`
here would exempt the exact case that produces the bug.

### 2.2 Ordering — the clamp never causes a drop

The clamp is applied **at the return**, strictly after the `tooSmall` / `overAllocated` tests and
after the drop decision. It must not be hoisted into the loop body. If it were, a clamped grant could
fall under its floor, set `tooSmall`, and drop a pane that the unclamped arithmetic would have kept —
turning a width shortage into a pane deletion, which is not what §2.3's ladder says happens.

A consequence follows and is **accepted**: a clamped last-child pane may render below
`MinUsableWidth`. There is no alternative — it is the only child, it cannot be dropped, and something
has to occupy the split. The note in §5 is the user-visible acknowledgement that this happened.

### 2.3 Monotonicity

More outer width → larger `avail` → the clamp bites less or not at all → the grant is larger or
equal. The fix is monotone in the surface width and introduces no band where one extra column yields
strictly less output. (Stated explicitly because non-monotone degradation is the defect class this
area keeps producing.)

---

## 3. Alternatives rejected

1. **Clip at render time** — let the allocator return 50 and have the renderer truncate to 20.
   **Rejected: two mechanisms answering one question.** "How wide is this pane" would have two
   answers that disagree, and the allocator's — the wrong one — is the one the notes, the min-rows
   row count, and #73's suppression predicate all read. Fixing only the pixels leaves every
   *consumer* of the grant still consuming a lie. The number must be correct where it is produced.
2. **Reject or floor over-large fixed sizes in `ConfigCheck`** — **rejected: the config check has no
   width.** Whether `size: 50` is too large is a function of the terminal at render time, not of the
   config. A check that flagged it would fire on a config that is perfectly valid in a 200-column
   terminal. (There may be a *separate*, narrower config-check question — a fixed size that exceeds
   any plausible terminal — but it is a different task and does not fix this one.)
3. **Drop the last child and render an empty split** — **rejected.** It breaks the loop's termination
   proof (`:467-469`: *"each iteration strictly shrinks the child list, so this always terminates"*
   depends on never dropping below one), and an empty split is worse output than a cramped pane.
4. **Re-solve at the clamped width** — rejected for the min-rows path specifically; see §4.2.

---

## 4. The change

### 4.1 A shared clamp helper

The two loops are deliberately not merged — `:547-550` gives the reason (min-rows has no equivalent
of `AllocateWithDrop`'s per-child request array) and that reasoning stands; do not merge them here.
But the clamp itself is one behaviour and gets **one implementation**, called from both:

```csharp
// Applied at each drop loop's exit, after the drop decision: a grant may exceed the split's
// available width when the last child cannot be dropped (SPEC-2.3-residual-pane-overwidth.md §2).
private static AllocResult ClampToAvail(
    AllocResult result, int avail, int splitOuterWidth, RenderNoteCollector notes)
```

Behaviour:

- If `result.Grants` is empty, return `result` unchanged. (`Count <= 1` admits `Count == 0`; the
  helper must not index into an empty list.)
- For each `i`, if `result.Grants[i] > avail`, record the original value, set the grant to `avail`,
  and emit one note (§5) for that pane.
- If no grant changed, emit **no** note and return an equivalent result.
- `avail` is already `Math.Max(0, ...)` at both call sites, so the helper does not need its own
  floor-at-zero — but it must not assume `avail > 0`. A clamp to `0` is a legitimate outcome for a
  zero-width surface and must not throw.

**Call sites:** `SizeResolver.cs:503-506` and `:587-590`, wrapping the `return result;`. Both already
have `avail` in scope on the line above (`:500`, `:584`) and `notes` as a parameter. **No new
parameters, no signature changes, no new arithmetic** — this is the property that makes #74 a small,
self-contained diff, and it is exactly the property the third site lacks (§4.3).

### 4.2 The min-rows wrinkle — clamp the grant, do NOT re-solve

On the `:587` path the grant is not just a width: `AllocateMinRowsOnePass` has already solved a **row
count** at that width. Clamping the grant afterwards means the row count was chosen assuming 24
columns and the pane now gets 20 — so the content will wrap to *more* rows than were budgeted.

**Ruled: clamp the grant, do not re-run the row-count search.** Reasons: min-rows' contract is a
*minimum* row target, and a narrower width can only produce more rows than that minimum, so the
contract is not violated in the direction that matters; the vertical axis has its own cap (§4.0.1)
which truncates the surplus; and re-solving reintroduces the iteration whose absence is what makes
this loop's termination argument simple.

**This is the one ruling in this document I hold at medium confidence** and the one I would take a
second opinion on. See §9, and verification item 8, which is built to catch me being wrong.

### 4.3 The third exit site — `ResolveVerticalEven` at `:846` is NOT part of this task

`grep -n 'Count <= 1'` returns **three** hits: `:503`, `:587`, and `:846`. The third is inside
`ResolveVerticalEven` (declared `:828`), and despite the *"vertical axis"* section heading at `:823`
it allocates **columns**, exactly like the other two — `AllocateEvenOnePass:864` computes
`avail = Math.Max(0, splitOuterWidth - BoundaryCost(...))` and grants are compared against `< 1`
cells. So it is a third **width-axis** drop loop, and it has the same escape hatch.

But it is not one defect; it is four, and it cannot be fixed the way §4.1 fixes the others:

```csharp
// :836-849
var tooSmall = false;
for (var i = 0; i < result.Grants.Count; i++)
{
    if (ClassifySize(current[i].Size).Kind != SizeKind.Fixed && result.Grants[i] < 1)
    {
        tooSmall = true;
        break;
    }
}

if (!tooSmall || current.Count <= 1)
{
    return result;
}
```

1. **No `overAllocated` check at all.** #67a's fix landed in `AllocateWithDrop` (`:495-501`) and
   `ResolveVerticalMinRows` (`:580-585`) and **never landed here**. Two fixed panes of `size: 50` in a
   20-column split: both are exempt from `tooSmall`, no non-fixed pane exists, so the loop returns
   `[50, 50]` — a sum violation with *two* children, which the §4.1 clamp does **not** fix. Clamping
   per-grant gives `[20, 20]`, still summing to 40 against `avail` 20.
2. **It never got #71's floor either.** `tooSmall` is `Grants[i] < 1` — the pre-#71 predicate. It
   does not call `DropFloor`.
3. **It drops panes silently.** `:851` truncates `current` with **no note**. Compare `:515`/`:521`
   and `:599`/`:605`, which both report. A user loses a pane here with nothing telling them why.
4. **It has no `RenderNoteCollector`.** Signature `:828` is
   `ResolveVerticalEven(Pane split, int splitOuterWidth, bool collapse)`. Fixing (1) or (3) or adding
   §5's clamp note all require threading `notes` in and updating its callers — a signature change.
   This is very likely *why* #67a and #71 both skipped it.

**Ruled: `:846` is its own task, not #74.** The reasoning: #74's diff is complete, self-contained,
and touches no signatures. Folding in a site that needs a new parameter, a ported over-allocation
check, a ported floor, and a new note class would triple the diff and mix four defects into one
review. And a clamp applied there *without* the `overAllocated` port would be a **fix that looks
landed but is not** — the worst outcome available.

**Action required: file the third site as a separate task, citing this section.** #74 must not close
with a note saying "there were three loops and we fixed two" — write down which one is left and why.
Its spec, when written, should treat *"the three loops structurally mirror each other"* — which the
codebase asserts twice, at `:547-550` and `:825-827` — as the standing invariant that (1), (2) and
(3) each violate.

---

## 5. The note

The clamp is user-visible degradation and must be reported, in the same family as `:515` and `:521`.

**Text:**

```
pane {n}: {requested} columns requested, clamped to {avail} at {splitOuterWidth} columns
```

- `{n}` is the 1-based position in `current`, matching the convention `:508-510` establishes and
  argues is stable (only the tail is ever removed, so a surviving child's position never shifts).
- The verb is **not** "dropped". Nothing was dropped; reusing that word would make the two outcomes
  indistinguishable to anyone reading notes, including tests.
- One note per clamped pane. In practice that is at most one, since at these two sites the clamp only
  bites when `Count <= 1` — but the helper is written per-grant and the note follows it, so the two
  cannot drift.

**Notes compose with drops.** A cascade that drops two panes and then clamps the survivor emits two
drop notes *and* one clamp note. Do not suppress or merge them: they describe different events, and
the drop notes are the explanation for why only one pane was left to clamp.

---

## 6. What must not change

1. **The `current.Count <= 1` guard itself.** It is the loop's termination proof (`:467-469`). This
   task makes its *result* correct; it does not remove the escape.
2. **The drop-note text at `:515` and `:521`**, and **#71's over-allocated-wins-the-tie rule**
   (`:510-512`, `:596`). Untouched.
3. **`AllocateOnePass`'s fixed-first step order** and `rem` going negative. The clamp is at the loop
   exit, not inside the one-pass allocator. Do not "fix" `rem` here — that changes how the remainder
   is split among fill panes and is a different behaviour with a different blast radius.
4. **The deliberate non-sharing of the loops** (`:547-550`, `:825-827`). Share the clamp; do not
   share the loops.
5. **`ResolveVerticalEven` and `AllocateEvenOnePass` (`:828-880`).** Out of scope per §4.3 — **do not
   touch them in this diff**, not even the "obvious" one-line note fix.
6. **The min-rows collapse-blindness at `:567-572`.** `SPEC-2.3-drop-predicate.md` §8 scoped it out
   on purpose. Still out of scope.
7. **§2.4 rule 1.** This task exists to *restore* it, not to relax it.

---

## 7. Relationship to #73

Independent fixes, no ordering constraint between them, but they touch adjacent lines and both change
what `DropFloor` is fed. In either order the second rebases cleanly. #73 changes `DropFloor`'s
*argument* (`grant` → `grant − reserve`); this task changes what happens to `grant` *after*
`DropFloor` has been consulted. They do not overlap semantically.

One interaction worth stating: after #73, a clamped last-child pane will more often sit below
`MinUsableWidth`, because the clamp can push it there and §2.2 forbids dropping it. That is the
correct outcome and §5's note is what makes it legible. It is not a #73 regression.

---

## 8. Verification

Every expectation below must be **derived from the config**, not read off a run of the implementation
— the same-author inversion hazard pinned in `SPEC-2.3-drop-predicate.md` §6 item 1.

### 8.1 Making `avail` derivable — read this before writing any config

`BoundaryCost` (`:155-156`) is:

```csharp
OwnBorderReserve(split) + (collapse ? Math.Max(0, childCount - 1) : split.Gutter * Math.Max(0, childCount - 1))
```

**It is not zero for a single child.** The gutter term vanishes at `childCount == 1`, but
`OwnBorderReserve(split)` — *the split's own border* — does not. So to get `avail == splitOuterWidth`
by inspection, a test config needs **both**:

- the **split** to have no `border.style` (so `OwnBorderReserve(split) == 0`), and
- `gutter: 0` **and** `collapse: false` (so the boundary term is 0 at any child count).

Under those two conditions `avail == splitOuterWidth` for every iteration of the drop cascade, which
is what makes items 1-4 arithmetic rather than observation. **State that derivation in a comment on
the test.** A test that gives the split a border and then hardcodes an expected number is pinning a
value it did not derive, and will silently drift.

Note the child panes may still be bordered — the child's reserve is not `BoundaryCost`'s business.

### 8.2 Items

1. **Single fixed child wider than the split.** Borderless split, `gutter: 0`, one child `size: 50`,
   split outer 20 ⇒ `avail = 20`. Assert the grant is **20**, the clamp note is emitted with
   `requested = 50`, and the rendered row is exactly 20 columns. **Fails on `main` today**; the
   headline test.
2. **Single fixed child inside the split.** Same shape, `size: 15`, outer 20. Assert grant **15** and
   **no note**. Pins that the clamp is inert when it should be — without this, item 1 passes under a
   fix that clamps everything to `avail` unconditionally.
3. **Clamp never causes a drop (§2.2).** A config where clamping the survivor would push it under its
   floor. Assert the pane **survives**, is granted `avail`, and emits the clamp note — not a drop
   note. Pins the ordering rule; fails if the clamp is hoisted into the loop body.
4. **Drop cascade then clamp.** Borderless split, `gutter: 0`, three fixed children of `size: 50`,
   outer 20. `avail` stays **20** through every iteration (section 8.1). Assert two drop notes *and* one
   clamp note, in that order, and a final grant of 20. Pins §5's compose rule.
5. **Zero-width surface.** Outer 0, one fixed child ⇒ `avail = 0`. Assert grant **0**, no exception,
   and a well-formed (empty-width) render. Pins the `avail == 0` edge §4.1 calls out.
6. **Empty child list.** A split whose `current` reaches the exit with `Count == 0`. Assert no
   exception and no note. Pins §4.1's empty guard.
7. **The min-rows path clamps too.** The `:587` analogue of item 1, driven through
   `ResolveVerticalMinRows` so `SolveMinRows`'s `return lo` fallback is the producer. Assert the same
   three things. **Both loops must be covered** — a fix applied to only one is the likely partial
   landing, and nothing else in the suite would catch it.
8. **Clamped min-rows still renders a valid rectangle (§4.2).** The item-7 config, rendered end to
   end. Assert every emitted row is exactly the surface width and the height cap applied. This tests
   the §4.2 ruling I hold at lowest confidence — **if it fails, that is a spec-defect to route back
   to me, not an implementation bug to work around.**
9. **Monotonicity spot-check (§2.3).** Item 1's config at outer 19, 20, 21, 22. Assert the grant is
   non-decreasing across the four. Cheap, and it pins the property this area keeps losing.
10. **A split with a border still clamps correctly.** Item 1's config but with `border.style` on the
    **split**, reserve 4 ⇒ `avail = 20 − 4 = 16`. Assert grant **16**, note says `clamped to 16`.
    Deliberately the one item where `avail != splitOuterWidth`: it is what catches an implementation
    that clamps to `splitOuterWidth` instead of to `avail`, which items 1-9 would all let through.

---

## 9. Confidence

**High** on §1 (the defect), §2 (clamp at the allocator exit, and why per-grant suffices at these two
sites), §2.1 (fixed panes are bound), §2.2 (ordering — the clamp must not cause a drop), and §3's
rejections. §1 is read off verbatim code plus the codebase's own comment at `:580-583` conceding the
over-allocation; §2.2 follows from the loop structure directly.

**High** that this is a real, currently-shipping defect on the commonest config shape — the
first-iteration reachability in §1 is not a corner case.

**High** on §4.3's disposition, and it is the finding I would most want read. `ResolveVerticalEven`
missed #67a's over-allocation check *and* #71's floor *and* has never emitted a drop note. Three
successive tasks improved two of three structurally-identical loops and left the third behind — which
suggests the pattern will repeat unless the mirroring is written down as an invariant with a test,
rather than as a comment. **That is a bigger finding than #74 itself**, and I have deliberately kept
it out of this diff rather than let it swell the task.

**Medium on §4.2** — clamping the min-rows grant without re-solving the row count. The argument (a
minimum is a minimum; the height cap absorbs the surplus) is sound as far as it goes, but I have not
read `SolveMinRows`'s body or §4.0.1's cap in this pass, and am reasoning from `:580-583`'s comment
plus the method signatures. Verification item 8 is built to catch me being wrong. **If a second
opinion is being spent on this task, spend it here.**

**Not escalation-worthy overall.** The blast radius is one clamp at two return statements with no
signature changes, the fix is monotone, and every alternative I rejected is recoverable.
