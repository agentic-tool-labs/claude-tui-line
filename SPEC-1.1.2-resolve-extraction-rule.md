# §1.1.2 addendum — resolving the extraction rule, and the scope of task #43

**Spec path note.** No spec path was dictated in the dispatch. I have used the repo's established
anchored-splice fragment pattern and chosen this path myself. If the Orchestrator wants it
elsewhere, move it — nothing below depends on the filename.

**Status of the evidence.** Both open NEEDS-EVIDENCE items this addendum closes were resolved by
*reading*, not by running anything. No experiment was conducted, directly or by delegate. What the
check does when it runs remains unmeasured, and item 3 below stays open for that reason.

---

## Why this exists

§1.1.2 left two NEEDS-EVIDENCE items open, and the implementor of #43 stopped on the second rather
than guess — correctly. This addendum resolves both, states the extraction rule precisely enough to
implement without further judgment, and settles the scope question the dispatch raised.

The resolution is not either of the two options the dispatch offered. It is a third, and it is third
because the repo already contains the mechanism §1.1.2 was reaching for without knowing it was there.

### The finding that decides it

`tools/check-examples.sh` rule C already anchors a checkable enumeration table on an in-band HTML
comment marker, and `README.md:168` already carries one:

```
<!-- items-table: checked against `--items --json` by tools/check-examples.sh (rule C) -->
```

The rationale in that script's header is written about *this exact table*:

> a prose table has no in-band string of its own to anchor on the way --items output does — and the
> README's other tables (config keys, colours) must stay unscanned.

"Config keys" is the pane-keys table. It is unscanned by `check-examples.sh` because it is not that
script's business — not because a table may not be marked. §1.1.2's own NEEDS-EVIDENCE 2 predicted
the "material" branch would need "an explicit marker, and that is a spec change". The spec change is
this document; the mechanism is not new, and §1.1.2's instruction that the check "should not become
a second independently-written mechanism if the existing one can carry it" is honoured by carrying
over the *convention* even though the script is a sibling.

### Why not the two options offered

**Shape-matched table scanning** (scan any row matching `` | `key` | …quoted literals… | ``) fails
open. A table reformat, a column added, or a second lookalike table elsewhere in the README and the
scan silently matches nothing while still exiting 0. A green check in the tree reads as coverage:
that is the argument `tools/check-all.sh:4–10` makes about `ci.yml`, and reproducing it inside
§1.1.2's own remedy would be the same defect wearing the remedy's clothes.

**Hand-picked prose anchors** (`grep 'Border style is one of'`) fail open the same way and worse.
Someone rewords the sentence — a copy-edit, not a semantic change — and the anchor stops matching
with no signal. It is also a fourth copy of §1.1.2's inventory, living in a script, which is the
disease §1.1 is about: nothing checks the checker's own list, so it drifts by the same mechanism
§1.1.2 documents. A list of line-anchored patterns is not coverage; it is a snapshot of one
afternoon's reading, asserted forever.

The third option removes the uncheckable sites instead of checking them. Two of the four go away by
rulings §1.1.2 has already made.

---

## Splices

### Splice 1 — resolve NEEDS-EVIDENCE 1 and 2

**Anchor.** In §1.1.2's `##### NEEDS-EVIDENCE` section, list items 1 and 2 currently read:

> 1. **Can `tools/check-examples.sh` host the token check, or does it need a sibling?** Read its
>    structure and report whether it is parameterised over "thing extracted from markdown, compared
>    against binary output" or hard-wired to item examples.
>    *If parameterised* — extend it; no new file.
>    *If hard-wired* — add `tools/check-doc-tokens.sh` and wire it into `tools/check-all.sh:42–43`.
> 2. **What is the true extraction rule for "a quoted token asserted as an accepted value"?** Kind 3
>    must not be swept up. Determine empirically, by running a candidate extractor over both files,
>    how many kind-3 mentions a naive backtick-scan falsely captures.
>    *If near zero* — an extractor scoped to the table and the prose lists is sufficient.
>    *If material* — the docs need an explicit marker, and that is a spec change, not a script change.

**Action.** Delete both list items. Renumber the surviving item 3 to item 1. Immediately above the
`##### NEEDS-EVIDENCE` heading, insert the new `##### The extraction rule` section given in Splice 2.

**Also amend** the sentence introducing the section, which currently reads:

> Not answerable by reading, and deliberately not answered here. Each outcome selects a different
> implementation, so measure before building:

Replace with:

> One item remains. Two others were closed by reading rather than measurement — see **The extraction
> rule** above; the prediction that the marker question needed an experiment was wrong, because the
> answer was already in `tools/check-examples.sh`. The remaining item is not answerable by reading
> and must be measured before the change is called done:

### Splice 2 — the new section

Insert immediately before `##### NEEDS-EVIDENCE`:

---

##### The extraction rule

§1.1.2's NEEDS-EVIDENCE 1 and 2 are resolved here, both by inspection.

**1 — a sibling, not an extension.** `tools/check-examples.sh` is hard-wired to item examples: all
four of its rules key on item ids and `--items --json` output. It is not parameterised over
"thing extracted from markdown, compared against binary output". Per that item's own branch: add
`tools/check-doc-tokens.sh` and wire it into `tools/check-all.sh:42–43`.

**2 — the docs carry an explicit marker, and the marker convention already exists.** A naive
backtick-scan does sweep kind-3 mentions in material quantity, so this is that item's second branch.
But the marker it calls for is not a new invention: `check-examples.sh` rule C already anchors a
checkable table on an in-band HTML comment, `README.md:168` already carries one, and that script's
header names the pane-keys table as a thing deliberately left unscanned *by it*. The convention
carries over unchanged.

**The scanned region.** `check-doc-tokens.sh` scans exactly those markdown tables preceded by a
`pane-token-table` HTML comment marker, in the files it is given. The pane-keys table in `README.md`
gains one, in the same self-describing form rule C's uses — naming the checker and what it compares
against, so a reader of the README learns the table is checked without leaving the README:

```
<!-- pane-token-table: quoted literals checked against `--accepted --json` by tools/check-doc-tokens.sh -->
```

Nothing else is scanned. No prose sentence, no fenced block, no unmarked table, in either file. The
rule is general — any file, any marked table — so a spec table can be brought in later by marking it,
with no script change. None is marked today.

**What counts as a checkable token.** Within a scanned table's body rows:

- **Column 1 names one or more keys**, each a backtick-fenced token with no quotes inside the
  backticks. `` `minSize` / `maxSize` `` is a real two-key row today, so multiple keys per row is
  the specified case, not an accident to be tolerated.
- **A checkable token is a backtick-fenced, double-quoted literal**, anywhere in the row —
  `` `"vertical"` ``. A backtick-fenced *bare* token is a key name, never a value, and is never
  checked.
- **Every checkable token in a row must be accepted by every key column 1 names.** Sharing a row is
  an assertion that the row's values apply to all of the keys in it.

This is a rule the README already obeys, unprompted, in all thirteen of its current rows: values are
written `"quoted"`, key names bare. That is what makes it a discovered convention rather than an
imposed one, and it is why it needs no exception list. It disposes of the `split` row's mention of
`` `children` `` and the `border` row's `` `enabled` ``, `` `style` ``, `` `color` `` — key names,
correctly written bare, correctly not checked — with no heuristic and no hand-listing.

**Keys with no closed set are skipped, visibly.** `--accepted --json` reports `accepted: null` for
`size`, whose `alsoAccepted` is a *prose description* (`AcceptedCommand.cs:46` renders it through
`FormatAccepted`), not a second token list. There is nothing there to compare against, which is
§1.1.2's `size` exemption arriving a third time. A row whose keys all report `accepted: null` is
skipped — **and the script prints the keys it skipped.** A skip set that silently grows past `size`
is how an exemption becomes a hole; naming it on every run is what stops that.

**An unknown key is fail-closed, not skipped.** If column 1 names a key `--accepted --json` does not
report, the row contributes no checkable tokens — but if such a row *contains* a quoted literal, that
is a failure, reported as such. The README today has six rows for keys the registry does not govern
(`children`, `gutter`, `ellipsis`, `maxRows`, `border`, `items`) and none of them contains a quoted
literal, which is precisely why the rule is safe to state fail-closed now rather than after the first
one appears. Same shape as §9.5.1's `PendingForm` and `AcceptedCommand.ValidateInvariant`: a gap is
stated, never silently emitted.

**The uncheckable sites are removed, not excepted.** §1.1.2's inventory lists prose sites the marker
cannot reach. Three of the four are disposed of without the checker growing a second mechanism:

- **`README.md:152–153`, `border.style`'s six literals in prose** — `border.style` is a first-class
  key in `--accepted --json` (`AcceptedCommand.cs:37`). Move the six literals into the pane-keys
  table as a `` `border.style` `` row, where the existing extractor checks them for free, and let the
  sentence go. The README loses nothing: the sentence was a table row that had been written as prose.
- **The `split`/`colorSystem`/`distribute` sentence in this document** — ruling 4 already requires it
  to cite rather than copy. Once applied, it holds no literals and there is nothing to check. **This
  addendum's scoping depends on that ruling actually being applied**, which is why it is in #43's
  scope below rather than deferred.
- **The `overflow` schema sketch (`Overflow   wrap | truncate | overflow`)** — deliberately left
  uncovered, and named here so it is a known gap rather than a silent one. Its tokens are unquoted,
  so they do not match the convention above; it sits inside a fenced block, which is where kind 3
  lives and which the extractor must never scan; and converting it to a citation would gut a sketch
  whose entire job is to show shape. Three independent reasons, and the honest disposition is to say
  so rather than to widen the extractor until it reaches one line.

**What this check does not do.** It is a subset assertion over the tokens the docs *do* quote. It
says nothing about a key the docs do not document at all — `height` is a registry key with no README
row today, and that is permitted by ruling 1's direction and must not fail this check. It is a docs
gap for someone to own separately, not a check failure.

---

### Splice 3 — verification items

In §1.1.2's `##### Verification` list, after existing item 6, append:

> 7. The pane-keys table in `README.md` carries a `pane-token-table` marker, and
>    `tools/check-doc-tokens.sh` reports a nonzero count of tokens checked. A run that checks zero
>    tokens and exits 0 is the failure mode the marker exists to prevent, so the count is part of the
>    output, not an internal detail.
> 8. The script names, on every run, the keys it skipped for having no closed set. Today that list is
>    exactly `size`.
> 9. `border.style`'s six literals are a row in the pane-keys table and are checked by item 7's run;
>    the prose sentence that carried them is gone.
> 10. Adding `` `"diagonal"` `` to the `split` row makes the check fail, and adding `` `"none"` `` to
>     it does not. The first proves the subset direction is enforced; the second proves it is a subset
>     rather than an equality check, against a token the registry accepts and the docs deliberately
>     omit. **Both must be demonstrated by running them**, for verification item 4's reason.
> 11. Removing the marker line makes the check fail rather than pass vacuously.

---

## Scope: the two unapplied rulings belong to #43

Both are in scope. Neither is a separate task.

**Ruling 3's retitle** is a precondition for the check being honest, not adjacent cleanup. The column
heading `accepted values` is an equality claim. The check implements subset. Shipping the check under
that heading ships a green signal standing behind a promise the check does not verify — a reader
trusting the heading is misled *more* once a checkmark appears next to it. §1.1.2's verification items
1, 2, and 5 are also one entangled set: item 5's "passes against the docs as they stand, with the
omissions in item 2 intact" is only a meaningful assertion once items 1 and 2 have landed.

One detail the dispatch did not raise. The blockquote already below the table reads *"Run `--check`
against your config first: it reports every one of these instead of swallowing them"* — that is about
**unrecognised values**, which is a different claim from ruling 3's required *"`--check` reports the
full accepted set for any key"*. The retitle needs that second sentence added; it is not already
there in different words.

**Ruling 4's citation fix** is load-bearing for the extraction rule above. This addendum closes the
spec-prose gap by *deleting the literals*, not by checking them, and that only works if the deletion
happens. Note the sentence copies **both** `split`'s members and `colorSystem`'s three; ruling 4
identifies both as wrong and its concrete instruction names `split`. Convert both.

---

## What must not change

- **`size` gains nothing.** No registry entry, no completeness check, no comparison against
  `alsoAccepted`. Its prose row stays exactly as written, with `"24"`, `"40%"`, and the `"auto"`
  deprecation note intact and unchecked. §1.1.2 refuses this for the third time and this addendum
  does not reopen it.
- **`README.md:138` still omits `none`; `README.md:142` still omits `greedy`.** Verification item 10
  exists to prove the check permits this. Do not add them.
- **No example config is touched**, and no fenced block is scanned.
- **`check-docs.sh` does not learn about the registry** and gains no skip branch.
- **`check-examples.sh` is not modified.** Its marker convention is copied; its code is not extended,
  refactored, or shared. Two scripts with one convention is the intended shape; a shared library
  between them is not in scope and should not be introduced opportunistically.
- **The check must not exit 0 having compared nothing.** Every failure path — no binary, no `jq`,
  marker absent, marked table empty — dies loudly, matching `check-examples.sh:109–139`'s discipline.

---

## NEEDS-EVIDENCE

Restated for the Implementor. Neither was run by me.

**N1 — the marker substring hazard.** `check-examples.sh:167` records that its own header quotes
`<!-- items-table -->` in a sentence, and that a naive substring match self-triggers on the script's
own text. `check-doc-tokens.sh` will document its marker the same way and hit the same trap, and this
addendum quotes the marker too. *Report:* whether the marker match is anchored such that a quotation
of the marker in prose or in a script comment does not activate a scan. *Decides:* if it is not, the
match must be anchored to a line that is the comment and nothing else — a script whose own
documentation triggers it is a check that scans its own header.

**N2 — verification item 3's toolchain-free guarantee** (§1.1.2's surviving NEEDS-EVIDENCE item).
Unchanged and still open: does `tools/check-docs.sh` still complete on a machine with no .NET
toolchain after the change? *If it does not* — the check was added in the wrong file; move it, do not
add a skip.

---

## Open — for the user, not for me

**The `border.style` sentence becomes a table row.** That is a small editorial change to the README's
shape, made on the architectural grounds that it converts an uncheckable site into a checkable one at
zero mechanism cost. Whether the README *reads* better with six border styles in a table cell than in
a sentence is a product judgment about the document's audience, and it is not mine. If the answer is
that the sentence should stay as prose, say so and the disposition changes to "named uncovered site",
alongside the `overflow` sketch — the rest of this addendum is unaffected.

**Stale line references.** §1.1.2 cites `SPEC-V2-FRAMEWORK.md:3693–3696` for a paragraph now at
roughly `:4455–4460`, and its inventory table's line numbers have drifted generally. I have not
mandated updating them: churning them creates a diff that touches the section for no behavioural
reason, and they will drift again. Flagging rather than deciding, because "how much do we invest in
keeping line citations live" is a repo-wide policy question this section should not settle alone.

---

## Confidence

High on the extraction rule and on the scope call. Both rest on evidence read directly rather than
inferred: the marker convention and its rationale are in `check-examples.sh`; the README's
quoted-value/bare-key convention holds across all thirteen current rows; `AcceptedCommand.cs` gives
the exact key strings and confirms `alsoAccepted` is prose.

One correction worth recording, since it nearly went the other way: I had intended to rule that a
key's checkable set is `accepted ∪ alsoAccepted`, and abandoned it only on reading
`AcceptedCommand.cs:8` — `alsoAccepted` is "a prose description for a key with no closed set". A
union rule would have had the checker comparing tokens against an English sentence. The lesson
generalises past this instance: **a JSON field named like a second list is not a second list**, and a
spec that rules on a data shape it has not read is guessing with extra confidence.

No escalation recommended. Nothing here is hard to reverse — the whole change is one new script, one
marker line, and three README/spec edits — and no decision touches security, concurrency, or a public
interface. The one genuine judgment I declined to make is the README readability question above,
which is the user's rather than the Ultra-Advisor's.
