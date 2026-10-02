# §2.3 — the drop predicate: the allocator must test the floor

Task #67b. Written against `/Users/jimcline/git/repos/claude-tui-line`, branch `main`.

**Amended twice since first written:**

- **A1** — verification item 1 now pins its expected values by derivation rather than describing
  them, after the Orchestrator indicated #67b may go to the agent that authored the greedy
  characterization test. See the note at item 1.
- **A2** — §4 now specifies **two** note messages, not one. #70 adds a second drop reason
  (Σgrants > avail) to the same loop this task modifies, and a single floor-worded message is false
  for it. This supersedes my ruling in `SPEC-2.3.1-min-rows-floor-sum.md` §5 item 4; see §4.

This is the splice flagged as required by `SPEC-2.3.1-min-rows-floor-sum.md` §6:

> Both are the same root: **the drop predicate is under-specified in §2.3**, which defines `Floor`
> as the drop threshold and then never says the allocator must test it.

Having now read §2.3's own body — which I had not when I wrote that — the diagnosis needs
correcting in the direction that makes this change *smaller and better grounded*, not larger. See
§1. The correction is load-bearing: it changes what the implementation must do, not just how the
change is justified.

---

## 1. Correction to my own §6: §2.3 does say it, for half the cases

`SPEC-V2-FRAMEWORK.md:877-881` is a single paragraph carrying **two different drop predicates**:

```
No child may resolve below 1 cell; children that would are **dropped entirely** rather than
rendered at a nonsense width, and the freed space is redistributed. A **`fill` or percent** pane
whose resolved inner width falls under `MinUsableWidth` (20) suppresses its own border first
(SPEC.md §6b narrow-width suppression, now applied per pane rather than per surface), and is
dropped only if it still does not fit.
```

Sentence one is the `< 1` rule. Sentence two is a **floor-based drop rule for `fill` and percent
panes** — the exact class in impl3's reproduction — and it has never been implemented. Both
`AllocateWithDrop:468` and `ResolveVerticalMinRows:522` test `grants[i] < 1` and stop there.

Three consequences, all of which change the work:

1. **This is not purely a product call after all.** For `fill`/percent panes, dropping below the
   floor is what §2.3 has always said. Jim's decision on #67b ratifies the spec for the remaining
   cases (declared `minSize` on other size kinds) rather than inventing a policy. The user-visible
   change he accepted is real, but part of it is a bug fix against existing text.
2. **The drop is two-stage, and the naive patch skips stage one.** §2.3 requires border suppression
   to be attempted *before* the drop. `grants[i] < Floor(...)` with `Floor` as written at
   `SizeResolver.cs:355` — `MinUsableWidth + OwnBorderReserve(...)` — is the **pre-suppression**
   floor. Testing against it drops panes that §2.3 says must first be given the chance to survive by
   dropping their border. That is a new defect, introduced by the fix, in the opposite direction
   from the one being fixed.
3. **The paragraph is self-contradictory as written** and must be rewritten, not appended to.
   Adding "and the allocator must test the floor" below a sentence that says the floor is 1 leaves
   the next reader to pick. That is exactly how `< 1` got written the first time.

---

## 2. The splice

**File:** `SPEC-V2-FRAMEWORK.md`. **Replace lines 877-881 in full** — the paragraph beginning `No
child may resolve below 1 cell;` and ending `dropped only if it still does not fit.` Do not append;
do not keep any part of the old first sentence. The paragraph before it ends `...and implementing
one violates §1.`; the paragraph after begins `` `MinUsableWidth` governs `fill` and percent panes
**only**... ``. Both are untouched.

Replacement text:

> No child may resolve below its **drop floor**, and children granted less are **dropped entirely**
> rather than rendered at a nonsense width, with the freed space redistributed. A pane's drop floor
> is its `floor(p)` from the table above, never less than 1 cell — a `content` pane's `floor(p)` is
> 0, because it asked for its width and was granted it, so 1 cell is the operative minimum there and
> the only pane it ever drops is one granted nothing at all.
>
> A **`fill` or percent** pane whose resolved inner width falls under `MinUsableWidth` (20)
> suppresses its own border first (SPEC.md §6b narrow-width suppression, now applied per pane rather
> than per surface), and is dropped only if it still does not fit. **Suppression precedes the drop
> test, and lowers the floor it is tested against**: once the border is gone the pane no longer needs
> the reserve that paid for it, so its drop floor is `MinUsableWidth` alone. Testing the
> border-inclusive floor against a pane that would have survived without its border drops a pane §2.3
> requires be kept.
>
> **The allocator must test this predicate; deriving the floor is not enough.** Every allocation path
> in §2.3.1 can hand back a grant below the floor it just computed, because none of them subtract the
> floor from anything: the fill divide splits whatever remains, which may be less than the floors it
> was divided among, and §2.3.3's over-constrained fixpoint returns floors that were never summed
> against the budget at all. A grant below its floor that is not dropped is a `minSize` the author
> declared and the renderer silently ignored — the failure this floor exists to prevent. An
> implementation that computes `floor(p)` for allocation and then drops on "was anything left" has
> not implemented this rule.

---

## 3. The predicate, exactly

Both drop-retry loops — `AllocateWithDrop` (`SizeResolver.cs:456`, test at `:468`) and
`ResolveVerticalMinRows` (`:511`, test at `:522`) — currently read:

```csharp
if (ClassifySize(current[i].Size).Kind != SizeKind.Fixed && result.Grants[i] < 1)
```

Both become a test against a shared helper. Its four required properties, each of which a plausible
one-line patch gets wrong:

**(a) Never below 1.** `Floor` returns `0` for `SizeKind.Content` (`:354`). A literal
`grants[i] < Floor(...)` is `grants[i] < 0` for content panes — never true — so a content pane
granted zero width, which `< 1` catches *today*, would silently survive at width 0. The threshold is
`Math.Max(1, ...)`. **This is a regression the obvious patch introduces**, and verification item 3
exists solely to catch it.

**(b) Post-suppression for `fill`/percent.** Per §2 above. When the pane's border would be
suppressed, the floor is `MinUsableWidth` with no border reserve. When it would not, the floor is
`Floor(...)` as computed. The suppression predicate already exists at `SizeResolver.cs:61-69`; reuse
it rather than restating the condition, so the two cannot drift.

**(c) The same collapse/exclude arguments the allocation used.** `Floor` takes
`(p, collapse, excludeLeft, excludeRight)`; the 1-arg overload at `:301` passes `collapse: false`.
`AllocateWithDrop` step 3 computes the positional form — `excludeLeft: collapse && i > 0`,
`excludeRight: collapse && i < current.Count - 1`. The drop test must use the identical expression.
A drop test using the 1-arg overload disagrees with the allocation it is checking **at the first and
last child only**, in collapsed splits only, which is the hardest possible shape of bug to notice.

**(d) The `!= SizeKind.Fixed` exemption stays.** It looks redundant — `Floor` returns `FixedValue`
for a fixed pane (`:353`), and step 2 grants exactly `FixedValue`, so the test would never fire.
It is not redundant: `Floor` returns a declared `minSize` *first* (`:327-330`), so a pane written
`{"size": 20, "minSize": 30}` yields floor 30 against grant 20 and would begin dropping. That is a
new drop trigger for contradictory config, it is outside what Jim approved, and nobody has ruled
whether such a config should drop, clamp, or be a config-check diagnostic. **Keep the exemption and
file the contradiction separately** (§8).

**Note on composition with #70/#67a.** The exemption does not re-open the fixed-pane overrun. #70's
guard tests the *sum* against `avail`, not each pane against its floor, so two `size: 30` panes at 46
columns are still caught there. The per-pane exemption and the whole-split sum guard are independent
tests of independent invariants and compose without interfering.

---

## 4. The note text — two messages, one per drop reason (amendment A2)

The existing note (`:483`, `:538`) is:

```
pane {current.Count} dropped: no width remained at {splitOuterWidth} columns
```

**This becomes false for every drop this change adds.** A pane granted 23 columns against a
24-column floor is dropped while 23 columns very much remained; a reader debugging their config is
told the opposite of what happened and will go looking for a width problem that is not there.

### I got this wrong once already, and the first fix was also wrong

At #67 I ruled (`SPEC-2.3.1-min-rows-floor-sum.md` §5 item 4) that the note stays identical because
"an over-allocation drop is the same event as a too-small drop from the reader's side." That was
already inaccurate — "no width remained" describes the opposite of a split whose children claimed
*too much* width — and I waved it through as cosmetic.

**#70 makes it structural.** Once #70's Σgrants ≤ avail guard lands, `AllocateWithDrop` drops for
**two distinct reasons**, and the first draft of this section replaced one wrong message with a
different wrong message: `{grant} columns is under its {floor}-column floor` is false for an
over-allocation drop, where the dropped pane may be *at or above* its floor and the sum is what
overran. A message that confidently names a floor the pane did not violate is worse than the vague
one it replaced, because a reader can act on it.

I dismissed the branch as one that "carries no information." It carries the only information that
matters: the two cases have **different repairs** — widen the terminal or lower `minSize` for the
first; shrink the declared fixed/percent sizes for the second.

### Ruled

The message depends on the reason the drop fired:

```
below floor:      pane {n} dropped: {grant} columns is under its {floor}-column floor at {w} columns
over-allocated:   pane {n} dropped: children need {sum} columns at {w} columns
```

- `{n}` is `current.Count` in both, unchanged — §9.8.2's position convention is untouched and notes
  still count down as panes are dropped.
- The `grant: 0` case folds into the first message with `floor: 1`, reading `pane 2 dropped: 0
  columns is under its 1-column floor at 46 columns`. No third message.
- `{sum}` is Σgrants **before** the drop, and `{w}` is `splitOuterWidth` — not `avail` — so the two
  messages quote the same width the user set.

**If both conditions hold on the same iteration**, report **over-allocated**. The sum overrunning is
the upstream cause; a pane under its floor is frequently the symptom, and naming the symptom sends
the reader to the wrong knob.

**Cost, stated plainly:** every existing test asserting the old string fails and must be updated, and
#70's guard — if it is written before this lands — will need its note updated too rather than
inheriting the old string. That is a visible diff on a user-visible string, which is appropriate for
a change Jim has already accepted as user-visible, but it is my call and not his. It is cheaply
reversible; if reverted, record that the old text is wrong for **both** new drop classes rather than
letting the inaccuracy go unrecorded a second time.

---

## 5. What must not change

1. **The two-stage order.** Suppress, then test, then drop. Never test-then-suppress.
2. **`floor(p)` itself** — `SizeResolver.cs:325-356` and the §2.3 floor table. This task changes
   what *tests* the floor, never what the floor *is*.
3. **`SolveMinRows`'s feasible path** (`:641`) and **`return lo`** (`:648`). Unchanged, per
   `SPEC-2.3.1-min-rows-floor-sum.md` §5.
4. **#67's and #70's Σgrants ≤ avail guards stay.** They are a different invariant from this one — a
   config can violate either without the other — and this task neither replaces nor subsumes them.
   It changes the message they emit (§4) and nothing else about them.
5. **`MinUsableWidth = 20`** stays a constant in `RowLayout.cs:19`, not a literal in the predicate.
6. **The Fixed exemption**, per §3(d).
7. **§9.8.2's note position convention.**

---

## 6. Verification

**1. impl3's repro under greedy — the test whose expectations invert.**

Config, stated exactly so that every expected value below is *derived* rather than observed:

```json
{"split": "horizontal", "gutter": 0,
 "children": [{"size": "fill", "minSize": 24}, {"size": "fill", "minSize": 24}]}
```

at `splitOuterWidth = 46`, no borders on either child.

`gutter: 0` is load-bearing and was unspecified in this item's first draft. With `gutter: 1` the
fill divide leaves `45 / 2 = 22 remainder 1`, and the remainder-distribution rule is not something I
have read — the expected grants would then be underivable from the spec, and the only way to fill
them in would be to run the code and copy what came out. That is precisely what this test must not
be. At `gutter: 0` there is no remainder and no border reserve, so:

- `avail = 46`, two fill panes, `each = 46 / 2 = 23`, both granted **23**.
- Σgrants = 46 = avail, so **#70's guard does not fire** — this is a pure below-floor drop and must
  produce the below-floor message.
- Each pane's drop floor is its declared `minSize`, **24**. `23 < 24`.
- **Before this change:** `23 >= 1`, so both are kept. Two panes at 23. No note.
- **After this change:** the higher-indexed pane is dropped. One note. The loop re-allocates with
  one child: `avail = 46`, single fill pane granted **46**, and `46 >= 24` terminates.

Assert exactly: one pane in the result, its grant is `46`, exactly one note, and the note reads
`pane 2 dropped: 23 columns is under its 24-column floor at 46 columns`.

**On inverting a characterization test.** The expected values above are derived from the floor
arithmetic, not read off a run. That distinction is the whole point of the test: it exists to make
#67b's behaviour change appear as a deliberate diff, and it can only do that if its new expectations
were computed independently of the new code. **Do not run the new implementation and record what it
printed.** If the implementation disagrees with any number above, that is a finding to report — the
spec is wrong, or the implementation is — and not a value to overwrite. This applies with particular
force if the same agent authored the pre-change version of this test, since updating one's own
expected values to match one's own new output is indistinguishable from the test having passed.

**2. The same config under min-rows.** Same assertions. Both loops, one behaviour.

**3. Content pane at zero width still drops.** A `content` pane granted 0 in a split with no room.
Assert it is dropped and a note emitted. **Fails if the predicate is `< Floor(...)` without
`Math.Max(1, ...)`** — §3(a). This test must exist; it is the only one that distinguishes the
correct predicate from the obvious wrong one.

**4. Border suppression rescues a pane rather than dropping it.** A bordered `fill` pane whose grant
is below `MinUsableWidth + reserve` but at or above `MinUsableWidth`. Assert: **not** dropped,
border suppressed, no note. Fails if the drop tests the pre-suppression floor — §3(b). This is
the regression the naive patch introduces, and nothing else catches it. **Blocked on §8's first
item** — its numbers sit exactly on the inner-versus-outer boundary.

**5. Collapsed split, edge children.** A collapsed split where the first and last children's border
reserve differs from the interior ones, sized so the verdict differs between the 1-arg and 4-arg
`Floor`. Assert the drop decision matches the allocation. Fails if the test calls the 1-arg
overload — §3(c).

**6. Contradictory fixed pane is not dropped.** `{"size": 20, "minSize": 30}` renders at 20 with no
drop and no note. Pins §3(d) so a later reader does not "simplify" the exemption away.

**7. No regression where nothing is under its floor.** A comfortable config produces byte-identical
output to `main`. The new predicate must be inert when every grant clears its floor.

**8. The two messages are distinguishable, and precedence holds.** Three assertions, one per case:

- *Over-allocation only:* two `size: 30` fixed panes at `splitOuterWidth = 46`, `gutter: 0`. Σgrants
  = 60 > 46. Both panes are Fixed and therefore exempt from the floor test, so **only** #70's guard
  can fire. Assert the note reads `pane 2 dropped: children need 60 columns at 46 columns` — and in
  particular that it does **not** mention a floor.
- *Below floor only:* item 1's config, already asserted there.
- *Both at once:* a config where Σgrants > avail **and** some pane is under its floor. Assert the
  **over-allocated** message, pinning §4's precedence rule.

This item is what stops the two messages from collapsing back into one during implementation, and it
is the direct test of amendment A2.

---

## 7. NEEDS-EVIDENCE

**N1 — is `OwnBorderReserve` suppression-aware?** §3(b) assumes it is not: that `Floor`'s
`MinUsableWidth + OwnBorderReserve(p, ...)` at `:355` always includes the border reserve regardless
of whether that border would survive suppression. I did not read `OwnBorderReserve`'s body. **What
it decides:** if it is *already* suppression-aware, §3(b) and verification item 4 are unnecessary and
`Floor` can be used directly; if it is not — which is what I expect from a static structural helper —
they are both required. **How:** read `OwnBorderReserve` in `SizeResolver.cs`; no execution needed.
This is the one thing an implementor must check before writing the predicate.

**N2 — how many existing tests assert the old note string, and what note did #70 ship?** Decides
whether §4 is a small change or a large one, and whether #70's guard emits the old string (in which
case it needs updating too) or introduced its own. **How:** `grep -rn 'no width remained' tests/ src/`
after #70 merges.

**N3 (carried, unchanged) — compositor behaviour with an over-width pane.** From
`SPEC-2.3.1-min-rows-floor-sum.md` §7. Still open, still not blocking this task.

---

## 8. Flagged, not decided

**Inner versus outer width — a live discrepancy, not a wording nit.** §2.3:879 says a fill/percent
pane *"whose resolved **inner** width falls under `MinUsableWidth`"* suppresses its border. The code
at `SizeResolver.cs:69` tests `outerWidth >= RowLayout.MinUsableWidth`. Inner and outer differ by the
border reserve, so these are different thresholds and one of them is wrong. I have not determined
which, and **it directly affects verification item 4's numbers** — item 4 is constructed at the
boundary where the two disagree. Worth resolving before that test is written, though the predicate's
structure does not depend on the answer.

**`{"size": N, "minSize": M}` with `M > N`.** Surfaced by §3(d). Three defensible answers — drop,
clamp the fixed pane up to `M`, or a config-check `Error` at load. My weak preference is the
config-check diagnostic, since it is the only one that tells the author their config is
self-contradictory instead of silently picking a winner. Not decided here; needs its own task and
probably Jim.

**`SolveMinRows` calls the 1-arg `Floor`.** `candidates.Select(Floor)` uses the `collapse: false`
overload, so min-rows floors already ignore collapse. Pre-existing, and #67b makes it load-bearing
because those floors now determine drops. Not folded in — fixing it changes min-rows allocation, not
just its drop decisions, and that deserves its own blast-radius assessment.

---

## 9. Confidence

**High on the splice text and on §3(a)-(c).** All three fall directly out of code I read verbatim:
`Floor`'s content case returning 0, its four-parameter signature against the 1-arg overload, and
§2.3's own two-stage sentence. Item (b) is gated on N1, which is a read and not an experiment.

**High on §1's reframing.** The paragraph at 877-881 is unambiguous and it says what it says.

**High on item 1's arithmetic**, given `gutter: 0` and no borders — it is division with no remainder
and no reserve. If the implementation disagrees, report it rather than editing the numbers.

**Medium-high on §4 as amended, and I have now been wrong about it twice** — first ruling the note
unchanged at #67, then ruling a single message here. Both errors had the same shape: treating the
note as cosmetic and reasoning about drop *mechanics* while the message describes drop *causes*. The
two-message form is right because the two causes have different repairs, but I have low confidence in
my own instinct about this specific string and would not argue hard against a different wording. The
**structure** — one message per reason, over-allocation winning ties — is what I want defended; the
exact phrasing is not.

**Not escalation-worthy**, with one caveat: §8's first item means verification item 4 cannot be
written until someone settles inner-versus-outer. That is a small read, not an escalation, but it
blocks a test rather than merely informing one and should not be discovered at implementation time.
