# §3.1 — the block model: what a block is, and where packing stops

Task #75. Written against `/Users/jimcline/git/repos/claude-tui-line`, branch `main`.

This is the first task in the ruled sequence **§3.1 blocks → #27 marker splice → #31 `maxLines`**. §6
below is written specifically to discharge that ordering: it names the seam #27 will attach to, so
#27 becomes a mechanical change rather than a rediscovery.

## Amendment history

- **A1** — D4 was written as a product call I had made but not routed, and §9 flagged it as the one
  decision worth showing Jim. **Jim confirmed D4 as written.** Settled ruling, not an open question.
- **A2** — §6's second bullet originally demanded a `Segment`-backed row list downstream of `Wrap`,
  which does not exist. Corrected to demand *provenance* instead.
- **A3 — THE SEQUENCE HAS CHANGED IN PRACTICE. §7 is rewritten.** #31 is implemented and holding for
  verification (branch `task-31`, worktree `/Users/jimcline/git/repos/claude-tui-line-task-31`,
  commit `8f34bbf`) — it lifts the one-line cap **entirely**, which I have ruled correct against
  §4.0.1. So #31 may now land *before* this task rather than after. **That inverts the risk §7
  originally described, and it moves D2's ownership.** Read §7 before starting; D2's note is also
  updated.
- **A4 — §4 rule 1 IS RELAXED. It was too strong, and it contradicted §6 of this same document.**
  §6 requires #27 to "re-invoke that one unit with a row budget"; a row budget cannot reach `Wrap`
  without changing `Wrap`'s signature, which rule 1 forbade in as many words. That was an internal
  inconsistency in this spec, present since A2 — `SPEC-2.6` Amendment A3 did not create it, it
  surfaced it. §4 rule 1 and §6's A3 note are rewritten; §2.1 gains a pointer. **No other ruling
  changes, and A3 needs no amendment.**
- **A5 — two citation defects in A4's §6 block, corrected. No ruling changes.** Found by re-reading
  `SPEC-2.6` §9 line by line rather than from recollection, at a second architect's insistence after
  the #27/A3 near-miss. A4 said A3 moves **five** methods into `SegmentTruncation.cs` — it moves
  **six** — and cited **§9.2.2** for the single-concatenation-site statement, which is at the end of
  **§9.2.1**. Both were wrong in the note, not in the check: **A4's conclusion re-verified and still
  holds — no conflict between the two documents.** Recorded rather than silently patched because a
  Reviewer validating against this spec would have tripped on both.

---

## 1. Scope correction, before anything else

**`valign` is already implemented and is NOT part of this task.** §3.1's prose introduces it
alongside the block model, which makes it look like one unit of work. It is not — it shipped
already:

- `Pane.cs:33` `enum PaneValign`, `:40` `PaneValignParsing`, `:44-46` the three tokens, `:65`
  `Parse` defaulting to `Top`
- `Config.cs:180-181` the `valign` JSON property, `:665` the parse call
- `ConfigCheck.cs:405-407` unrecognized-value diagnostic, `AcceptedCommand.cs:40` the accepted tokens
- `PaneTreeRenderer.cs:197-203` `PadHeight`, `Compositor.cs:79-82` and `BorderGrid.cs:158-161` the
  sibling-padding splits
- `Compositor.cs:17-19` says so explicitly: *"`Valign` (§3.1) … defaults to `PaneValign.Top`, which
  reproduces the pre-Phase-3"* behaviour

**Do not re-specify, re-implement, or "finish" `valign`.** If something about it is wrong that is a
separate bug report. This task is the block model only.

That leaves §3.1's actual unbuilt content as: **what makes an item a block, and what packing does
when it meets one.**

---

## 2. What §3.1 actually rules, and what it leaves open

§3.1 is `SPEC-V2-FRAMEWORK.md:2495-2518` — 24 lines. It settles three things and only three:

1. **A multi-row block occupies its own rows.** It never shares a row with a neighbouring item and
   never has items packed beside it (`:2497-2500`).
2. **Packing runs before wrapping** (`:2507`), and the order is not interchangeable.
3. **Block count is a property of what the provider returned** — one line or several — **never of
   the width it was later granted** (`:2514-2515`), so wrapping cannot promote an item to a block.

Everything below is a decision §3.1 does not make. Each is ruled here because leaving any of them to
the implementor produces a defensible-but-different statusline.

### 2.1 The ordering rule is about classification, not about two physical passes

This is the most important thing in this document and the easiest to get wrong.

`RowLayout.Wrap(IReadOnlyList<Segment> segments, int? availableWidth, bool allowFallback)`
(`RowLayout.cs:33-83`) today **fuses** packing and wrapping: it greedily fills rows from a flat
segment list. Reading `:2507` as "these must become two separate passes" invites a rewrite of
`Wrap`, which is unnecessary and would violate the one-implementation rule.

For a group of single-row items, greedy fill *is* pack-then-wrap — the two are indistinguishable
because no item can occupy more than one row before wrapping. The ordering rule bites in exactly
one place: **deciding which items are blocks, which must happen before any width is consulted.**

**Ruled: `RowLayout.Wrap` remains the single implementation of packing-and-wrapping, and *this task*
does not modify it. The block model is a layer above it that decides what to hand it.**
Classification happens first, from provider output; `Wrap` is then called once per packed group and
once per block line. That satisfies `:2507` and `:2514` completely.

**A4:** "this task does not modify it" is the claim, and it is narrower than the original wording
("`Wrap` is unchanged"). #27 *does* modify `Wrap`, with cause — see §4 rule 1 as amended. The
one-implementation rule is about **not forking**, not about immutability.

---

## 3. The decisions

### D1 — A block is an item whose rendered value contains a line break, after the §4.0.1 cap

Block-ness is `lineCount > 1`, where lines come from splitting the provider's returned value, and
where the §4.0.1 `maxLines` cap has **already** been applied. §4.0.1 (`:2975+`) fixes this ordering:
the cap applies *at the provider, before §3.1 packs the block*. So this layer never sees more lines
than the cap allows and never applies the cap itself.

Note §4.0.1:3001 rules **"Default: no cap"** — so in the common case there is no cap to have been
applied, and this layer sees whatever the provider returned. That is intended, not an omission.

### D2 — Strip exactly one trailing newline before splitting. This is not optional.

**A3: ownership moved — see section 7.2. If #31 lands first, this rule is #31's to implement, and this
task's job is to verify it rather than to write it.** The rule itself is unchanged.

`echo foo` emits `"foo\n"`. A naive `Split('\n')` yields `["foo", ""]` — **two** lines, so the item
becomes a block with a blank second row, and it is pushed onto its own rows away from its
neighbours. Every well-behaved shell command would become a block the moment the cap is lifted.

**Ruled:** strip **one** trailing `\n` (and a `\r` before it) if present, then split on `\n`, then
`TrimEnd('\r')` each line — preserving the per-line `\r` handling `CommandProvider.cs:160` already
does. `"foo\n"` is **one** line and is **not** a block. `"foo\n\n"` is two lines (one trailing
newline stripped, a genuine blank line remains) and **is** a block.

This is the single highest-risk detail in the task: it is invisible while the cap exists, and when it
surfaces it will look like a packing bug rather than a parsing bug.

### D3 — Interior blank lines are preserved

A script that emits a blank line is expressing layout. Collapsing blank lines is a silent mutation
of provider output, and this codebase has no channel to report it. A blank line is a row, padded to
the pane width per §2.4 rule 1 like any other.

### D4 — No separator is emitted on either side of a block

**Confirmed by Jim (A1). Settled — implement as written.**

§3.3 (`:2641`) establishes the separator as a *between-items* construct. §3.1 says a block never
shares a row with a neighbouring item. Together these leave a separator between a single-row item
and an adjacent block with nowhere to live.

**Ruled: a block suppresses the separator on both of its sides.** Placing it at the end of the
preceding row produces a dangling `foo | ` with nothing after it — the classic trailing-separator
artifact, and a reader cannot tell it from a failed item. A separator's purpose is to disambiguate
two items *sharing a row*; a block is already unambiguously separated by occupying its own rows, so
the separator has no work to do.

Consequence: a pane whose items are `A`, `BLOCK`, `C` renders `A` on its own packed row with no
trailing separator, the block's rows, then `C` on its own packed row with no leading separator.

### D5 — Block count is not row count, and the spec's own sentence invites conflating them

`:2514` says block count is a property of what the provider returned. That is about
**classification**. It does **not** mean a block occupies exactly that many rows.

**A block's lines still wrap.** A 3-line block whose second line exceeds the inner width renders 4+
rows. Ruled: each line of a block is wrapped independently via `RowLayout.Wrap`, and the block's
rows are those wrapped results concatenated in order.

The invariant `:2515` actually states is narrower and must be preserved exactly: **wrapping can
never change whether something is a block.** A one-line item that wraps to three rows is still a
single-row item and still packs with its neighbours. This is the pack-then-wrap consequence
`:2510-2512` spells out, and it is the whole reason the order is not interchangeable.

### D6 — No empty packed group

Two adjacent blocks, or a block as the first or last item, must not produce an empty packed group
and therefore must not emit a blank row. Only groups containing at least one surviving item produce
rows.

### D7 — A suppressed item is not a block and contributes nothing

The §3 missing-field rule suppresses an item whose value resolved to empty. Suppression happens
**before** classification: a suppressed item is not a zero-line block, it is absent, and it does not
split a packed group in two. Two single-row items either side of a suppressed item pack onto the
same row with one separator between them.

### D8 — `allowFallback` semantics are unchanged

`RowLayout.cs:27-31` documents the single-unwrapped-row fallback as a property of the surface
(applies only when the surface has exactly one pane). Blocks do not change it: the fallback is about
wrapping, and it applies per line exactly as it does today.

---

## 4. What must not change

1. **`RowLayout.Wrap` stays the ONE implementation of packing-and-wrapping — and this task does not
   modify it.** The block layer calls it; it does not fork it, reimplement it, or copy its greedy
   fill anywhere else.

   **A4 — this rule previously read "`RowLayout.Wrap`'s signature and body" must not change, full
   stop. That was wrong and is retracted.** It contradicted §6 of this same document, which requires
   #27 to re-invoke a unit *with a row budget* — a budget cannot reach `Wrap` without entering its
   signature. The rule I meant, and the rule that now stands, is **anti-forking, not immutability**.
   Concretely:

   - **For this task (#75): `Wrap` is not touched.** Unchanged from the original rule, and
     verification item 1's byte-parity gate is what enforces it. If an implementor of #75 finds
     themselves editing `RowLayout.cs`, that is a spec gap to route back, not a licence this
     amendment grants.
   - **For #27: `Wrap` may gain optional parameters and may have its body refactored in place**,
     provided (a) it remains the sole implementation, (b) the new parameters default to today's
     behaviour, and (c) `Wrap` called without them is **byte-identical** to today, gated by the
     existing suite. `SPEC-2.6` §9.3 is written to exactly this shape and is **compatible with this
     rule as amended**.

   The distinction that matters: a second copy of greedy fill is a permanent correctness hazard; an
   optional parameter with a byte-identical default is not.
2. **`valign` in its entirety.** §1.
3. **§4.0.1's cap site.** This layer never applies `maxLines`; it consumes already-capped output.
4. **`Segment`'s contract.** `Plain` stays escape-free (§3.2's rule 1). Splitting a value into lines
   must not disturb the `Plain`/`Markup` split or the width metric.
5. **Existing single-row behaviour is byte-identical.** A config in which no item returns more than
   one line must render exactly as it does today. This is verification item 1 and it is the
   regression gate for the whole task.
6. **§2.4 rule 1.** Every row, including a block's blank rows, is padded to the pane's inner width.

---

## 5. The cache key — this task does NOT extend it

`:1432` names `RenderLeaf`'s input tuple `(items, innerWidth, overflow, ellipsis, notes,
allowFallback)` as §2.5.1's purity/cache key.

**Block-ness is derived from the items' resolved values, which the tuple already carries. §3.1 adds
no new input, so the tuple, its §10 test, and the cache key are all untouched.**

Stated explicitly because #27 — the very next task — *does* extend the tuple, and an implementor
working through both in sequence should not carry that change backwards into this one. If it turns
out `items` carries config items rather than resolved values, then provider output is already
outside the key and that is a **pre-existing** defect to report, not one to fix here.
**Assumption I did not verify:** that `items` in the tuple is post-resolution.

---

## 6. The seam #27 attaches to — required, and the reason this task is sequenced first

I ruled in `SPEC-2.6-vertical-marker-splice.md` §5(b) that the marker splice sequences after this
task, because once blocks exist, "the last kept row" may come from a block that `Wrap` never
produced as part of a packed group. That ruling obliges this task to leave a defined attachment
point, and this section is it.

**Ruled: the block layer must concatenate all packed-group rows and block rows into one ordered row
list at exactly one place, and that concatenation site is the designated row-budget seam.** Clipping
to `maxRows` and splicing the ellipsis marker happen there, against that list, without regard to
which producer contributed the last surviving row.

Two requirements follow, and both are on *this* task:

- **One concatenation site, not several.** If groups and blocks are appended to a shared list from
  more than one place, #27 has no single point to attach to and will end up with a row-budget check
  per producer — which is how the two axes got out of step in the first place (§2.6's *"One rule,
  applied twice"*).
- **The last row must remain re-renderable at that site.** The concatenated list may be `PaneRow`-typed
  — `RowLayout.Wrap` composes to `PaneRow` internally and this task does not change that, so a
  Segment-backed row list downstream of `Wrap` does not exist and this section must not demand one
  (**A2**: it originally did; that was a defect, corrected against the #75 implementation). What this
  task must leave instead is **provenance**: at the concatenation site, each producer unit's
  `RenderLeaf` inputs must be in scope alongside the row range it contributed, so #27 can re-invoke
  that one unit with a row budget and splice the marker while the content is still `Segment`s inside
  `Wrap`.

This does **not** ask this task to implement any row budgeting. It asks it to not foreclose #27.

**A4 — the A3 diff is DONE. Result: no conflict. `SPEC-2.6` §9 (Amendment A3) is compatible with
this section, and #75 and #27 may proceed against both documents as they now stand.** This replaces
the earlier note here, which said A3 was unread and warned of a possible conflict. Checked
specifically:

- **The concatenation site.** A3 §9.2.1 introduces a `RenderUnit(Segments, Rows)` record and states,
  at the end of that same §9.2.1, that `units` is the single concatenation site; §9.6 item 10 pins it
  again as a must-not-change. That is this section's first requirement, implemented exactly.
  **(A5: this previously cited §9.2.2, which is the budget pass. The claim was right, the pointer was
  one subsection off.)**
- **Provenance.** A3 retains the exact `Segment` list handed to `RenderLeaf` alongside that unit's
  rows, and re-invokes `RenderLeaf` with it. That is this section's second requirement — and note it
  is *stronger* than what this section asked for (the rows themselves rather than a row range), which
  is fine. It does **not** make the row type `Segment`-backed, so A2's correction still holds.
- **`PaneRow` is not extended**, and `Compose` still produces `PaneRow`. `SPEC-2.6` §4's prohibition
  holds.
- **`Wrap`'s signature.** A3 §9.3 adds three optional parameters and refactors the body into
  `PackRow`/`Compose`. Against §4 rule 1 *as originally written* this was a literal conflict — but
  the defect was rule 1's, not A3's, and rule 1 is amended above rather than A3 being changed. See
  the A4 entry in the amendment history for why: this document already required a row budget to
  reach `Wrap`, so it could not coherently also forbid `Wrap`'s signature from changing.

One non-conflicting scope note, recorded because it affects sequencing rather than correctness: A3
§9.4 creates a new `SegmentTruncation.cs` and moves **six** methods into it — `TruncateSegment`
(renamed `Truncate`), `WrapSegment` (renamed `WrapToWidth`), `SafeCutIndex`, `Restyle`,
`RestyleSimple`, `TryGetSimpleWrap`, i.e. `PaneRenderer.cs:49-175` in full. **(A5: this previously
said five; A3 §9.4 and its own §9.6 item 8 both list six.)** It is a pure move with a sound
dependency-direction argument, but A3 itself rates it Medium confidence and names a smaller
alternative (`internal` on `TruncateSegment` plus a `RowLayout → PaneRenderer` reference), and says
in as many words that if the Reviewer or Jim prefers the smaller diff they should take it, since
nothing else in §9 depends on the choice. **If #27 is time-boxed, that extraction is the part of A3
most safely deferred.** Not a ruling of mine — #27 is not my task — but the Orchestrator should know
it is separable.

---

## 7. Relationship to #31 — REWRITTEN (A3). The ordering has changed.

### 7.1 What changed

The original sequence assumed this task lands before #31, on the reasoning that a configurable cap on
a quantity that cannot exceed 1 yields a green suite with the producer never firing.

**#31 is now implemented and holding for verification** (branch `task-31`, commit `8f34bbf`). It
removes `CommandProvider.cs:160`'s `stdout.Split('\n', 2)[0]` **entirely** rather than preserving it
as a default. I have ruled that correct: §4.0.1:3001 says **"Default: no cap"** in as many words, and
§3's erratum at `:2970-2973` explicitly retires the single-line reading of `command`
(*"the single-line reading is the one that goes"*).

So #31 may land first. This task must be written to be correct in **either** order.

### 7.2 D2 belongs to whichever task lands first — and that is now probably #31

D2's trailing-newline strip is not a block-model refinement; it is a **precondition for the cap being
lifted at all**. The moment `CommandProvider` stops truncating and starts splitting, `"foo\n"` yields
two lines, and every ordinary command — `date`, `whoami`, `git rev-parse` — becomes a two-line item
with a blank second row.

**Ruled: D2 ships with the cap lift.** If #31 lands first, #31 implements D2 and this task's
verification item 4 becomes a *regression check on work already done* rather than a new assertion.
If this task somehow lands first, D2 ships here. Either way **D2 and the cap lift must not be
separated**, and this is an ordering constraint with arithmetic behind it (`"foo\n".Split('\n')`
has length 2), not a stylistic preference.

### 7.3 The new risk — multi-line output reaching a renderer with no block model

The original §7 warned that #31 without #75 gives a cap that never fires. With #31 removing the cap
outright, the risk **inverts**: multi-line provider output can now reach a render path where the
block layer does not yet exist.

`RowLayout.Wrap` takes `Segment`s and has no notion of an embedded `\n`. What it does with one is
undetermined by this spec and undetermined, as far as I can tell, by any other.

**NEEDS-EVIDENCE (N1).** Route to an implementor; do not guess, and do not let this task's design
depend on the guess:

> On branch `task-31` @ `8f34bbf`, with the cap removed, render a config whose `command` item emits
> a genuinely multi-line value (e.g. `printf 'a\nb\nc'`). Report only: the number of rows emitted,
> and whether any row contains a literal `\n` or a control character in its `Plain` text.

- **Rows = 1 with an embedded `\n` in `Plain`** → §3.2 rule 1 (`Plain` stays escape-free) is violated
  the moment #31 merges, independently of the block model. That is a **merge blocker for #31**, and
  the cheapest fix is for #31 to collapse or reject embedded newlines until this task lands.
- **Rows = 3, laid out sanely** → `Wrap` already tolerates it, #31 is safe to merge alone, and this
  task layers the block *semantics* (D4's separator suppression, D6, D7) on top of behaviour that is
  already roughly right.
- **Anything else** (crash, mangled width metric, rows ≠ 1 and ≠ 3) → report verbatim; it is a
  finding in its own right.

### 7.4 What still holds

- This task remains **fully implementable and testable now**. A block's lines can be constructed in
  tests without any provider change, and D1-D8 are all exercisable at the layer they live in.
- The `height: "content"` gap §4.0.1:2990-2994 identifies — a content pane's height *is* its content,
  so nothing bounds it once the cap is lifted — is **neither this task's nor #31's**. §4.0.1 assigns
  it to §2.8 as a pane key that does not yet exist. Do not solve it here.

  **A4 — measured, and the gap is narrower than this reads.** A `height: "content"` pane emitting 200
  lines renders 3 rows, not 200: `surfaceMaxRows` is enforced through the §2.8.1 HeightLadder
  *before* `height: "content"` is consulted, and `PaneHeightContentTests.cs:109-114` asserts that by
  name. So nothing is unbounded today, and the §2.8 work is not urgent. It also means the open
  question is **not** "what should the max be" but **"should `height: "content"` be able to grow the
  surface at all"** — today's answer is no, asserted by a test. That is a product call, not mine.
  Still not this task's problem either way.

---

## 8. Verification

1. **Byte-parity regression gate.** A config where every item returns exactly one line must produce
   output byte-identical to `main` before this change. Run it across the existing pane fixtures, not
   one config. If this fails, the block layer is intercepting the single-row path it should be
   passing through untouched.
2. **A block occupies its own rows.** Items `A`, `BLOCK`(2 lines), `C`, pane wide enough to fit all
   three on one row. Assert `A` is alone on row 1, the block's two lines are rows 2-3, `C` is alone
   on row 4. The width is deliberately generous so the *only* reason they are not packed together is
   the block rule.
3. **No separator adjacent to a block** (D4). Same config with a non-empty separator configured.
   Assert row 1 ends with `A` and no trailing separator characters, and row 4 begins with `C` and no
   leading separator. Assert on the exact row strings, not on a substring match.
4. **Trailing newline is not a block** (D2). A provider value of `"foo\n"` with two neighbouring
   single-row items. Assert all three pack onto **one** row with separators between them, and that
   no blank row is emitted anywhere in the pane. **Per section 7.2 this test must exist regardless of which
   task implements the strip** — if #31 landed first it is a regression check on their work, and it
   is still this task's job to have it.
5. **A blank line inside a block is preserved** (D3). Value `"a\n\nb"`. Assert three rows, the middle
   one empty but padded to the pane's inner width per §2.4 rule 1.
6. **Wrapping does not promote to a block** (D5, `:2515`). One item whose single line is three times
   the inner width, plus two short neighbours. Assert the long item still packs with its neighbours —
   the first row carries the long item's head *and* a separator *and* the next item where they fit —
   rather than the long item being pushed to its own rows. **This is the test that distinguishes
   pack-then-wrap from wrap-then-pack**, which `:2510-2512` names as the whole reason the order
   matters. It is the single most valuable item here.
7. **A block's line wraps** (D5). A 2-line block whose first line exceeds the inner width. Assert the
   block's total rows exceed its line count and that its rows remain contiguous with no packed item
   interleaved.
8. **No blank row between adjacent blocks** (D6). Two consecutive block items, and separately a block
   as the pane's first and as its last item. Assert no empty row at any junction.
9. **A suppressed item does not split a packed group** (D7). Items `A`, `EMPTY`, `C` where `EMPTY`
   resolves to nothing. Assert one row reading `A<sep>C` — one separator, not two, and not a gap.
10. **Single concatenation site** (§6). Not a runtime test: a structural check that the row list is
    assembled in exactly one place, with each producer unit's `RenderLeaf` inputs and contributed row
    range in scope there (provenance).
    Record the file and line in the completion report so #27 can attach to it without re-deriving it.
11. **`Plain` stays escape-free with multi-line input** (section 7.3, A3). A block item's value containing
    `\n`. Assert no emitted row's `Plain` text contains a literal `\n` or any control character —
    §3.2 rule 1. This is the invariant N1 is probing, and it must be asserted here whether or not N1
    comes back clean, because this task is the one that makes multi-line values routine.
12. **`RowLayout.cs` is untouched by #75** (§4 rule 1 as amended by A4). A structural check, and it
    exists because A4 relaxed the rule for #27 and that relaxation must not leak backwards into this
    task. Assert this task's diff contains no change to `RowLayout.cs`. Item 1's byte-parity gate
    would probably catch a behavioural change; this catches a behaviour-preserving refactor that
    quietly forks the greedy fill.

---

## 9. Confidence, and what I am not deciding

**High on §1** (valign is done) — read directly off eight call sites plus `Compositor.cs:17-19`'s own
statement that it implements §3.1.

**High on §2.1**, which I consider the load-bearing insight: §3.1's ordering rule constrains
classification, not pass structure, so `Wrap` survives intact *for this task*. If an implementor
reports that a real two-pass split is unavoidable, that is a spec gap worth routing back — it would
mean I have misread how `Wrap` fuses the two.

**High on D1, D2, D5, D6, D7, D8.** D2 and D5 are the two I would most expect to be implemented
wrongly, and both have a dedicated verification item.

**High on section 7.2** (D2 ships with the cap lift). It rests on `"foo\n".Split('\n')` having length 2,
which is arithmetic rather than judgment. Stated with its derivation deliberately: I asserted an
ordering constraint elsewhere this session that turned out to be invented
(`SPEC-2.3-suppression-predicate.md` §6.1, retracted), so this one is written so a reader can check
it in ten seconds rather than take my word.

**Medium on D3** (preserve interior blank lines). Defensible either way; I chose preservation because
collapsing is a silent mutation and this path has no note channel. Cheap to reverse.

**D4 — settled (A1).** Jim confirmed it as written. Implement it; do not route it back.

**Open, and not mine to close: section 7.3's N1.** What `RowLayout.Wrap` does with an embedded newline
decides whether #31 can merge before this task. I have not run it and will not; it needs an
implementor. **This is the one thing in this document that could change #31's merge decision**, so it
should be answered before #31 is verified rather than after.

**§6's A3 note — CLOSED (A4), and re-verified line by line (A5).** The diff is done and there is no
conflict; see §6. The one apparent conflict was a defect in my own §4 rule 1, which contradicted §6
of this same document, and it is amended above rather than resolved against `SPEC-2.6`. **This is
worth naming plainly: I wrote a "must not change" rule strong enough to forbid the mechanism another
section of the same spec required. Two sections of one document disagreed for two amendments before
an outside document surfaced it** — which is an argument for diffing a spec against itself, not only
against its neighbours.

**A5's own lesson, recorded because it is the same failure one level up.** A4's conclusion was
right, but two of its supporting citations were wrong, and they were wrong because A4 was written
partly from recollection of `SPEC-2.6` §9 rather than wholly from its bytes. A conclusion can be
correct and its evidence still not check out; only re-reading the source distinguishes the two. When
a second architect asked for a real re-read rather than a confirmation, that was the right ask and it
found both defects.

**Medium-low on one thing A4 does not settle, flagged rather than guessed:** whether #27's optional
parameters on `Wrap` are the right long-term shape, or whether the row budget eventually wants its
own type. A4 rules only that the optional-parameter shape does not violate *this* document. The
design merits of A3 §9.3 belong to whoever owns `SPEC-2.6`, and I have deliberately not adjudicated
them — a second architect ruled there with more of the packing algorithm in front of them than I
have.

**Not escalation-worthy.** No security, migration, concurrency, or public-interface exposure; the
blast radius is bounded by verification item 1's byte-parity gate; and every decision above is
locally reversible.
