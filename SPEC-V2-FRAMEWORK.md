# SPEC v2 — pane surface + status item framework

Goal, in the user's words:

> *"a statusline TUI framework where users can add pre-built status items, wire in their own
> (even if it means calling shell or py scripts for their placeholders) and it all is rendered
> inside the TUI surface."*

> *"The render surface inside our TUI shell should support panes, split vertically,
> horizontally, etc. The first iteration is one pane that fills the status line."*

v1 (SPEC.md) is a faithful rebuild of one hardcoded statusline. v2 turns it into a framework:
the render surface becomes a **pane tree**, and the 14 captured segments become **registry
rows** that a user-defined item joins as a peer rather than as a special case bolted on.

Prerequisite: SPEC.md Phase 1 (the `chromeReserve` width fix) must be verified in a live
session first. Panes make width errors catastrophic instead of cosmetic — a pane that
miscomputes its width by two columns corrupts every pane to its right — so the surface must be
measured correctly before anything is composed on it.

## 1. The load-bearing rule

**One implementation per behavior; one registry per enumerable kind.** Adding a status item
costs *one registry row* plus whatever is genuinely unique about it, and zero edits to the
renderer, the compositor, the layout engine, or any test harness. If adding an item means
touching `SegmentBuilder`'s control flow, the abstraction has not landed.

Concretely, and stated as the history it now is: this rule was written against a
`SegmentBuilder.Build` that dispatched per item through a long run of hand-written `if` blocks. They
were to collapse into a loop over a resolved item list, with the per-item logic that survives living
in the row. That collapse has landed — see §1.1, which also explains why this paragraph must not be
rewritten to describe the code as it stands today.

### 1.1 The half of this rule that everything cites, and that is not written above

Two problems, and the second is the one that matters.

**The paragraph above is stale, and stale in the way it exists to forbid.** `SegmentBuilder.Build`
is now fourteen lines containing exactly one `if` — a null check — around
`foreach (var id in ItemRegistry.DefaultIds)`. The collapse it calls for has already happened. What
remains is a present-tense claim about live code (*"current 14 hand-written `if` blocks"*), which is
a number written into prose about a thing that changes — which is, in those exact words, the defect
§4 names when it rules that a flag belongs on a row because *"a number written into prose is a
second registry that goes stale."* §1 demonstrates the failure it defines, in its only worked
example. The number is now true by coincidence: fourteen is the method's line count.

Do not repair this by writing a fresh count. That re-commits it. The before-state is real history
and worth keeping, so it is stated as history: the rule was written against a `Build` that dispatched
per item through hand-written branches. What must not persist is prose asserting what the code looks
like *today*.

**The rule above is about cost, and every section that cites it is about drift.** Read the rule
literally: adding an item costs one registry row and zero edits elsewhere. That is a claim about how
*expensive extension* is, and it is satisfied completely by a registry that is trivial to append to
and silently missing half its entries. Now read what §1 is actually invoked for — Defect 11's
resolution set behind the config surface, §9.5.1's extractor table behind it, §9.4.2's unknown keys,
§9.6.2.2's version drift, §12.7.1's payload copied into three commands. Not one of those is "adding
a thing was expensive." Every one is **a registry that fell behind the surface it mirrors, with
nothing to notice.** The document has been citing §1 for a failure mode §1's letter does not
describe, and getting the right answer for eight sections by borrowing its spirit.

**Ruled, as the second half of the load-bearing rule: a registry must be mechanically tied to the
kind it enumerates.** One registry is necessary and not sufficient — the cheap-to-extend registry
and the silently-incomplete registry are the same object, and only a check distinguishes them.
"Mechanically" excludes a sentence instructing whoever comes next, because Defect 11 and §9.5.1 are
both that sentence, already ignored once each. In practice this means the enumeration is derived
from the types (§9.4.2's `[JsonExtensionData]`, §9.6.1's code registry), or a test fails when the two
disagree (§9.6.2.2's drift test), or coverage **fails closed** so an unclassified new member breaks
the build rather than going unchecked (§9.5.1).

And the test §1 offers — *"if adding an item means touching `SegmentBuilder`'s control flow, the
abstraction has not landed"* — is by its own terms a one-time test, phrased for a landing that has
now happened. It cannot fail again, because nothing runs it. A rule whose only check was a
milestone is a rule that stops being enforced the moment it succeeds, which is precisely how a
registry begins falling behind: not by anyone deciding to duplicate it, but by the check that would
have caught them having been retired as passed.

#### 1.1.1 The inventory: nine accepted-value lists, none of them tied to its parser

§1.1 states the rule and never checks the codebase against it. Doing that turns up one class of
duplicate, nine instances of it, and — more usefully — a reason it happened that is structural
rather than careless.

`ConfigCheck.cs:289–297` held nine hand-written token lists, one per kind, each a second copy of a
set that already existed somewhere else as the cases of a `switch`. Task #38 closed eight of the
nine, turning each kind's accepted set into a `static readonly` collection colocated with its
parser; `ConfigCheck.cs:293` now holds only `size`'s list, exempted for the reason given below.
The table records where each kind's set lives today:

| Kind | Where the value is parsed | Where the accepted list is written |
|---|---|---|
| `border.style` | `Config.cs:672` `BorderStyleParsing.TryParse` | `Config.cs:660` `BorderStyleParsing.Accepted` |
| `size` | no single closed-set parser — mixed literals and forms | `ConfigCheck.cs:293` |
| `valign` | `Pane.cs:39` `ParseCore` | `Pane.cs:30` `PaneValignParsing.Accepted` |
| `align` | `Pane.cs:82` `ParseCore` | `Pane.cs:73` `PaneAlignParsing.Accepted` |
| `overflow` | `OverflowMode.cs:37` `Parse` | `OverflowMode.cs:22` `OverflowModeParsing.Accepted` |
| `case` | `ItemValueResolver.cs:361` `ParseCaseMode` | `ItemValueResolver.cs:353` `CaseAccepted` |
| `split` | `Config.cs:483` `ParseSplitCore` | `Config.cs:474` `SplitAccepted` |
| `distribute` | `Pane.cs:128` `ParseCore` | `Pane.cs:120` `PaneDistributeParsing.Accepted` |
| `colorSystem` | `Config.cs:392` `ParseColorSystemCore` | `Config.cs:383` `ColorSystemAccepted` |

**All nine agreed at the time this was written**, which was the whole problem — §1.1's healthy
registry and its silently-incomplete twin were the same object, and nothing distinguished them. That
is no longer true of eight of the nine: they are not a second copy that happens to agree, they are
the one object the parser and the diagnostic both read. `size` still has the shape this paragraph
warns about, which is why the two failure directions below still apply to it.

**The border row was the one worth reading closely, because someone had already tried to fix this
and it had not taken.** `BorderStyleParsing` was deliberately extracted, and the comment above it —
then at `Config.cs:625`, now `Config.cs:652` after #38's rebuild — said it existed so the checker
could ask *"was this string one of the recognized ones" through the exact same switch the loader
uses, **rather than a second copy of the token list***. (That comment now reads "accepted-token
lookup" instead of "switch," reflecting #38's change from a shared parser to a shared data
collection — see below.) Twenty lines away, `ConfigCheck.cs:289` was still a second copy of the
token list, all six literals — proof that extracting the parser alone had not been enough.

That is not someone ignoring the comment. **A `switch` is not enumerable.** You can ask it "do you
accept this?" and you can never ask it "what do you accept?" — so extracting a shared parser makes
two acceptance *behaviours* agree, and cannot make the documented *set* agree, because the set was
never a value anyone could obtain. The extraction achieved everything its mechanism was capable of
achieving, and the gap it was written to close stayed open.

This is §1.1's own distinction arriving in an unfamiliar costume. A shared parser *looks* mechanical
— it is a function, it is called, it cannot be forgotten. But with respect to the accepted set it is
still only a sentence instructing whoever comes next, and §1.1 already ruled that a sentence is not
a tie.

**What ties them is making the set data.** One `static readonly` collection per kind, holding the
accepted tokens; the parser becomes a lookup against it, and the diagnostic reads the same object.
Then there is no second copy to drift, and adding a token is one edit by construction rather than by
discipline. Task #38 built exactly this, for every kind but `size`.

**Do not force `size` into that shape.** Its list is `an integer`, `a percentage`, `content`,
`fill`, `auto` — three literals and two descriptions of a *form*, which is why it has no closed-set
parser. The shared representation has to carry accepted forms alongside accepted literals, or the
size diagnostic gets less readable in exchange for a tidiness it cannot benefit from.

The failure this prevents runs in both directions, and neither announces itself:

- **Token added to the parser, not to the list.** `--check` reports a value that works as an error:
  *"'ellipsis' is not a overflow — expected wrap, truncate, or overflow"*. The config is valid, the
  renderer honours it, and the validator calls the user wrong.
- **Token removed from the parser, left in the list.** The diagnostic advertises a value that now
  falls through to the default, which §9.4.3 exists to make loud and which this makes quiet again by
  telling the user to go there.

The first direction is the more damaging, for a reason particular to this project: **the diagnostic
is the only statement of the accepted set most users will ever read.** Nobody opens `Pane.cs`, and
the error message arrives exactly when they are looking for one.

Whether the README and this spec carry a third and fourth copy is **not established here** and is
the next thing to check — §9.6.2.2's drift test covers item examples, not enum tokens, so there is
no reason to assume it is covered and no evidence yet that it is not.

#### 1.1.2 The third and fourth copies exist — and only one of the three kinds should be checked

§1.1.1 closed by saying that whether the README and this spec carry a third and fourth copy of the
nine token lists was "not established here and is the next thing to check". This section checks it.
The answer is yes, with two already drifted; but the obvious remedy — deriving the prose from the
registry §1.1.1 specifies — is **wrong**, and the reason it is wrong is the same reason §1.1.1
exempts `size`.

##### The inventory

| Where | What it is | Agrees with §1.1.1's per-kind registry? |
|---|---|---|
| `README.md:136–150` | a table whose column is headed **`accepted values`** | **no** — see below |
| `README.md:152–153` | `border.style`, six literals in prose | yes |
| `SPEC-V2-FRAMEWORK.md:160–168` | the pane schema sketch | partially — `Size` omits `content` and `fill` |
| `SPEC-V2-FRAMEWORK.md:2622` | `colorSystem`, three literals | yes |
| `SPEC-V2-FRAMEWORK.md:3693–3696` | `split` and `colorSystem`, written out | **no** — see below |
| `SPEC-V2-FRAMEWORK.md:844–846` | the `overflow` mode table | yes |

Two have already drifted, and neither drift announces itself:

- **`split`** — `README.md:138` and `SPEC-V2-FRAMEWORK.md:3694` both give `"vertical"` /
  `"horizontal"`. `SplitValues` is `{ "none", "horizontal", "vertical" }`. Both omit `none`.
- **`distribute`** — `README.md:142` gives `"min-rows"` alone. `DistributeValues` is
  `{ "greedy", "min-rows" }`. It omits `greedy`.

**The instructive one is `SPEC-V2-FRAMEWORK.md:3693–3696`, because that paragraph states the rule
and breaks it in the same sentence.** It is arguing that §9.4.1's trigger must be a predicate rather
than a list, and it says of `distribute`: *"writing its members out here is how this list was wrong
about that key too, so it is cited rather than copied."* In the same sentence it copies `split`'s
members — and gets them wrong, omitting `none`, the identical omission as `README.md:138` — and
copies `colorSystem`'s three in full. The discipline was discovered, written down, and not applied
to the two keys either side of the clause stating it.

That is not carelessness any more than §1.1.1's border row was. Citing costs a lookup at writing
time; copying is free and reads better. Nothing checks the difference, so the cheaper habit wins
every time, exactly as §1.1 predicts.

##### Why the docs must not be generated from the registry

The tempting fix is to make the prose derive from the registry, so a fifth copy cannot drift. Do
not do this. **The README currently knows strictly more than the registry does.**

`README.md:140` documents `size` as a column count, a percentage, `"content"`, or `"fill"` — and
then says `"auto"` is a **deprecated alias for `"fill"`**, and that it does *not* mean `"content"`.
`SizeValues` at `ConfigCheck.cs:293` carries none of that: no deprecation, no aliasing, and no
warning about the wrong guess a reader is actually likely to make. Generating that row from the
registry would delete three facts to gain a consistency the row did not lack.

This is §1.1.1's `size` exemption arriving a second time, in a second medium, for the same reason.
§1.1.1 refused to force `size` into the registry because its list is "three literals and two
descriptions of a *form*". Prose is that observation generalised: **documentation's job is to
describe a form, and a registry can only hold a set.** Where the two coincide, generation is
possible; where they do not, generation destroys the part that was worth writing.

##### The three kinds, and the seam between them

The seam is §1.1.1's own: does the thing being written down have members, or does it have a shape?

1. **Enumerable** — `border.style`, `valign`, `align`, `overflow`, `case`, `split`, `distribute`,
   `colorSystem`. Eight of the nine. A closed set of literals, and the registry §1.1.1 specifies
   holds exactly that set. Prose here quotes real tokens, and a quoted token that is not in the
   registry is a defect with a victim: someone writes it into a config and the binary rejects it.
2. **A form with special cases** — `size`, and only `size`. Cannot be checked for completeness
   against anything, because there is no set to be complete against. `"an integer"` is not a value
   anyone types; it is English describing a shape.
3. **Illustrative mention** — `README.md:195`, `SPEC-V2-FRAMEWORK.md:1104`, `:1106`, `:3027–3031`,
   and every other example config that happens to contain `"overflow": "wrap"`. These are not lists
   of accepted values. They are one value, chosen because it demonstrates something.

**Kind 3 must never be checked for completeness, and the reason is not cost.** An example naming
one token is not claiming the others do not exist, and a check that demanded otherwise would flag
every example in the document. Worse, the only way to satisfy such a check is to make examples
exhaustive — which makes every example worse to read in order to make a checker quiet. A rule that
degrades the artifact it protects is not a rule worth having.

##### The ruling

**1. The check is a subset assertion, never an equality assertion.** Every literal token quoted in
`README.md` or `SPEC-V2-FRAMEWORK.md` as an accepted value of an enumerable key must be a token the
registry accepts. The docs are **not** required to name every token the registry holds.

The direction is the whole design. Docs ⊆ registry catches the failure with a victim — prose
promising a value the binary rejects. Docs = registry would additionally demand that `README.md:138`
list `none` and `README.md:142` list `greedy`, which are omissions an author made on purpose:
`none` means "this is not a split", and `greedy` is the default the key exists to opt out of.
Forcing them in makes the table more complete and less useful. The asymmetry also disposes of
`size` for free — a subset check has nothing to say about a key with no set, so no exemption
machinery is needed.

**2. The authority on the full accepted set is `--check`, not the prose.** `ConfigCheck`'s
`UnknownEnumValue` and `FormatAccepted` (`ConfigCheck.cs:397–406`) already emit the complete list —
*"'ellipsis' is not a overflow — expected wrap, truncate, or overflow"* — at the one moment a reader
needs it, having just typed something wrong. That is a better delivery mechanism than a table read
weeks earlier, and it is already built. The docs' job is to be a guide that points at the authority,
not a second authority competing with it.

**3. Consequently, `README.md:136`'s column heading must stop saying `accepted values`.** It
promises completeness the table has never had and, under ruling 1, is not required to have. Retitle
the column, and state once near the table that `--check` reports the full accepted set for any key.

**The two drifts above are symptoms of that heading, not defects in their own right, and they are
discharged by retitling it — not by adding the missing tokens.** Once the column no longer claims to
be exhaustive, `README.md:138` omitting `none` and `README.md:142` omitting `greedy` are correct as
written and must be left alone. Do not "finish the job" by adding them afterwards; that reinstates
the completeness claim in the table body after removing it from the heading, which is worse than
either position held consistently.

> **Ruling 3 is a product call rather than an architectural one.** It was escalated and approved
> before adoption. The alternative — keep the promise and make the table genuinely complete — is
> coherent, merely more brittle and less readable. Reversing it costs one column heading, so it is
> cheap to revisit if the README's audience turns out to want exhaustiveness.

**4. Spec prose cites; it does not copy.** `SPEC-V2-FRAMEWORK.md:3694` already got this right for
`distribute` and wrong for `split` and `colorSystem` in the same sentence. Generalise the practice
it discovered: when this document needs to refer to the members of an enumerable key, cite the
section that defines them.

At `SPEC-V2-FRAMEWORK.md:3694` specifically, **the fix is to replace `split`'s copied members with a
citation, not to add the missing `none`.** That paragraph is an argument about §9.4.1's trigger; it
has no reason to enumerate anything, and completing the list there would create a fifth maintained
copy in the act of fixing the fourth. Converting all prose everywhere to citations is not in scope —
the three known sites are `:3693–3696`, `:2622`, and `:160–168`.

##### Where the check may live, and where it may not

`tools/check-all.sh:15–19` already rules on this and the ruling is inherited, not remade:
`check-docs.sh` must run on a machine with no toolchain, and **must never learn to skip a check it
cannot perform**. A docs-versus-registry check reads a compiled artifact. It therefore **cannot** go
in `check-docs.sh` without breaking that guarantee, and it must not be added there with a
skip-if-unavailable branch — that is precisely the failure mode `check-all.sh` was created to end.

It belongs on the toolchain side, alongside `tools/check-examples.sh`, invoked from
`tools/check-all.sh`. Precedent: §9.6.2.2's drift test already checks item examples against the
binary; this is the same shape applied to a different kind, and should not become a second
independently-written mechanism if the existing one can carry it.

##### Sequencing — nothing in this section is buildable yet

This section rules on the docs question and deliberately depends on a registry that **does not exist
today**. §1.1.1 specified it; no implementation followed (`ConfigCheck.cs` is untouched since the
Phase 5 CLI commit). Anything below therefore queues:

```
#37  ScanReferences / ReferenceExtractors  (in flight)
  └── #38  the registry §1.1.1 specifies   (reopened; waits on #37 — shares ItemValueResolver.cs)
        └── the external door onto the registry   (needs-implementation, below)
              └── the subset check of this section
```

**The external door is a required prerequisite and is not scope creep.** The subset check must read
the accepted set as data from outside the assembly, and a registry no external consumer can read is
a `switch` with extra steps — the exact object §1.1.1 was written to eliminate. The repo already has
the pattern: `--items` and `--colors` enumerate their kinds through the CLI. Tokens want the same
door, whether as a new command or folded into an existing one.

That door is a **public surface addition and must be specified before it is built**, not chosen
mid-task by whoever picks up the check. It is a consumer of #38's output and cannot start before
#38 lands.

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

##### NEEDS-EVIDENCE

One item remains. Two others were closed by reading rather than measurement — see **The extraction
rule** above; the prediction that the marker question needed an experiment was wrong, because the
answer was already in `tools/check-examples.sh`. The remaining item is not answerable by reading
and must be measured before the change is called done:

1. **Does `tools/check-docs.sh` still complete on a toolchain-free machine after the change?** The
   guarantee at `check-all.sh:15–19` is the one thing here that cannot be verified by inspection.
   *If it does not* — the check was added in the wrong file; move it, do not add a skip.

##### What must not change

- **`size` gains no registry entry and no completeness check**, here or downstream. §1.1.1 exempted
  it; this section exempts it again, for the same reason, in a second medium.
- **The two drifts are not fixed by adding tokens.** See ruling 3. `README.md:138` and `:142` stay
  as written once the column heading is retitled.
- **Example configs are not touched.** No example is made exhaustive to satisfy a checker.
- **`check-docs.sh` does not learn about the registry** and does not gain a skip branch.
- **`ItemValueResolver.cs` is not touched by this section.** It is in flight under #37, and `case`'s
  registry row belongs to #38's rebuild, which waits for #37 to land.
- **This section does not specify the registry.** It depends on §1.1.1's design and must not restate
  it; if the two ever disagree, §1.1.1 governs.

##### Verification

1. `README.md:136`'s column no longer claims `accepted values`, and the table is followed by one
   sentence naming `--check` as the authority on the full set.
2. `README.md:138` still omits `none` and `README.md:142` still omits `greedy` — confirming ruling 3
   was applied as a heading change rather than a completion exercise.
3. `SPEC-V2-FRAMEWORK.md:3694` cites rather than enumerating `split`'s members.
4. The subset check fails when a token is added to `README.md:144` that the registry does not accept.
   **This must be demonstrated by making it fail, not by asserting it would** — a check whose red
   path has never run is what `tools/check-all.sh:4–10` exists to prevent, and asserting this one
   works without watching it fail would reproduce that exact defect inside its own remedy.
5. The check passes against the docs as they stand, with the omissions in item 2 intact — confirming
   subset semantics rather than equality.
6. `tools/check-docs.sh` still runs to completion on a machine with no .NET toolchain.
7. The pane-keys table in `README.md` carries a `pane-token-table` marker, and
   `tools/check-doc-tokens.sh` reports a nonzero count of tokens checked. A run that checks zero
   tokens and exits 0 is the failure mode the marker exists to prevent, so the count is part of the
   output, not an internal detail.
8. The script names, on every run, the keys it skipped for having no closed set. Today that list is
   exactly `size`.
9. `border.style`'s six literals are a row in the pane-keys table and are checked by item 7's run;
   the prose sentence that carried them is gone.
10. Adding `` `"diagonal"` `` to the `split` row makes the check fail, and adding `` `"none"` `` to
    it does not. The first proves the subset direction is enforced; the second proves it is a subset
    rather than an equality check, against a token the registry accepts and the docs deliberately
    omit. **Both must be demonstrated by running them**, for verification item 4's reason.
11. Removing the marker line makes the check fail rather than pass vacuously.

#### 1.1.3 The external door — one command that reports what the binary accepts

##### 1. The surface: a new mode command, `--accepted`

###### New command, not folded

`--items` and `--colors` are already separate mode commands with their own files
(`src/ClaudeTuiLine/ItemsCommand.cs`, `src/ClaudeTuiLine/ColorsCommand.cs`), each with its own
`*ResultJson` record. Folding into either is wrong on meaning: `--items` enumerates statusline
items, `--colors` enumerates colours. Neither noun covers "config keys with constrained
values." A third sibling is both correct and consistent with what is already there.

###### The name is `--accepted`

| Candidate | Verdict |
|---|---|
| **`--accepted`** | **Chosen.** One bare word, matching `--check` / `--version` / `--items` / `--colors` / `--preview`. It is the codebase's own noun: `Accepted`, `AcceptedTokens`, `FormatAccepted`, and `ColorsCommand`'s `alsoAccepted` field. |
| `--tokens` | **Rejected.** "Token" already means a colour token here — `@name`, the `colors` table, `ColorTokenExtractors`, `ColorTokenReference`. On this binary `--tokens` reads as "dump my colour tokens," which is a different command. |
| `--enums` | **Rejected.** `size` is in this surface and is not an enum; a name that excludes one of its own rows by definition is a name that will be argued with later. |
| `--schema` | **Rejected for `--accepted`** — it promises a full config schema (types, nesting, required-ness) that `--accepted`'s flat key→tokens payload does not deliver. **Taken by `--schema --json` (task #84)**, which does deliver it: item kinds, accepted values, colour names, and the structural shape of the config document. See SPEC-84-mcp-schema-explorer.md §5.0. |
| `--accepted-values` | **Runner-up.** Accurate, but the five existing modes are all single bare words and there is no reason to be the first exception. |

###### `--accepted` requires `--json`

`--accepted --json` emits JSON. **Bare `--accepted` is a usage error** naming the correct form.

This is deliberate. `--items` defaults to plain text and puts JSON behind `--json`
(`Program.cs:624` vs `:628`). If `--accepted` emitted JSON bare, adding a plain-text form later
would either break every existing caller or strand JSON as the bare default forever. Requiring
`--json` now — while the surface has no users at all — makes the plain-text form purely
additive whenever someone wants it, and makes the call site in tooling read identically to its
sibling: `tools/check-examples.sh:119` already runs `"$CLAUDE_TUI_LINE_BIN" --items --json`.

The cost is one extra flag on a machine-read command. The alternative is a breaking change
scheduled for an unknown future date.

##### 2. Output shape

House style, matching `ItemsCommand` and `ColorsCommand`: a `record` with
`[property: JsonPropertyName(...)]`, version first, serialized through a source-generated
`JsonSerializerContext`.

```csharp
public sealed record AcceptedKeyJson(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("accepted")] IReadOnlyList<string>? Accepted,
    [property: JsonPropertyName("alsoAccepted")] string? AlsoAccepted);

public sealed record AcceptedResultJson(
    /* version — use the SAME JsonPropertyName ItemsResultJson uses for its first field.
       Match it exactly; do not invent a second spelling. */
    string Version,
    [property: JsonPropertyName("keys")] IReadOnlyList<AcceptedKeyJson> Keys);
```

Sample output:

```json
{
  "version": "…",
  "keys": [
    { "key": "border.style", "accepted": ["rounded","square","heavy","double","ascii","none"], "alsoAccepted": null },
    { "key": "valign",       "accepted": ["top","middle","bottom"], "alsoAccepted": null },
    { "key": "size",         "accepted": null, "alsoAccepted": "an integer, a percentage, content, fill, or auto" }
  ]
}
```

###### Three fields, and no more

`pointer`/`pointers` (the JSON-Pointer locations each key may appear at) was considered and
**deliberately omitted**. #43 matches documentation rows to registry rows by key name; pointers
are not load-bearing for it, and every field on a public surface is a commitment. Recorded here
so that adding one later is a decision rather than a drift.

###### The invariant

**For every row, at least one of `accepted` (a non-empty list) or `alsoAccepted` (a non-empty
string) must be present.** A row with neither is a bug and the command must fail rather than
emit it.

This is what makes the surface fail-closed. It is the same shape as §9.5.1's `PendingForm`
ruling: a gap must be *stated*, because silence cannot be distinguished from an omission.

###### The eight sources

Each row reads its tokens from the registry #38 colocated with the parser. **The command must
read these properties directly and must not hold a copy of any token list.**

| Key | Source | At |
|---|---|---|
| `border.style` | `BorderStyleParsing.AcceptedTokens` | `Config.cs:670` |
| `colorSystem` | `ConfigLoader.ColorSystemAcceptedTokens` | `Config.cs:390` |
| `split` | `ConfigLoader.SplitAcceptedTokens` | `Config.cs:481` |
| `valign` | `PaneValignParsing.AcceptedTokens` | `Pane.cs:37` |
| `align` | `PaneAlignParsing.AcceptedTokens` | `Pane.cs:80` |
| `distribute` | `PaneDistributeParsing.AcceptedTokens` | `Pane.cs:126` |
| `overflow` | `OverflowModeParsing.AcceptedTokens` | `OverflowMode.cs:29` |
| `case` | `ItemValueResolver.CaseAcceptedTokens` | `ItemValueResolver.cs:359` |
| `size` | *none* — see §3 | — |

##### 3. `size` is listed, and declares no closed set

`size` appears as a row with `accepted: null` and a prose `alsoAccepted`.

**It is listed rather than omitted** because absence must mean something falsifiable. If `size`
were simply missing, #43 could not distinguish "deliberately exempt, skip it" from "fell out of
the registry and nobody noticed" — and it would resolve that ambiguity by skipping, which is
fail-open in precisely the direction the check exists to close. With every constrained key
listed, **a key absent from `--accepted` means the binary does not know that key at all**, and
#43 gains a third check for free: documentation describing a config key that does not exist.

**It declares no closed set** because it has none, and because `ConfigCheck.cs:293`'s
`SizeValues` is not a registry. Its only consumer is `UnknownEnumValue` (`ConfigCheck.cs:397`),
which passes it to `FormatAccepted` (`ConfigCheck.cs:400–406`) to build the human sentence
*"'foo' is not a size — expected an integer, a percentage, content, fill, or auto."* It is a
**message-formatting array**, shaped for prose. Publishing it as a machine-readable `accepted`
list would be a category error, and would tell #43 that `"an integer"` is a literal token a user
may type.

The `alsoAccepted` string should be sourced from `SizeValues` via `FormatAccepted` rather than
hand-written, so the prose cannot drift from the diagnostic. That is the one place the
formatting helper is reused outside diagnostics; it may need its visibility widened, which is a
smaller change than a second copy of the sentence.

###### `size` stays undecomposed — ruled, not left open

`SizeValues` is `{ "an integer", "a percentage", "content", "fill", "auto" }` — three literal
tokens a user actually types, and two descriptions of a form. A strictly better-looking surface
would split them: `accepted: ["content","fill","auto"]` plus `alsoAccepted: "an integer, or a
percentage"`, the idiom `ColorsCommand` already uses (`recommended` alongside `alsoAccepted`,
`ColorsCommand.cs:12`/`:35`). It would let #43 check three real tokens instead of skipping the
key.

**Ruled: not taken.** §1.1.2's *What must not change* states outright that *"`size` gains no
registry entry and no completeness check, here or downstream,"* and §1.1.1 exempted it first —
#38 shipped honouring that exemption. Reopening a ruling two spec sections and a merged commit
rest on is not something this section should do as a side effect of building the CLI door onto
it. The alternative is recorded, not lost: tracked as its own task, to be picked up only if the
exemption itself is deliberately reconsidered — and if it is, §1.1.1, §1.1.2, and this section
all need amending together in the same change.

##### 4. Files to touch

| File | Change |
|---|---|
| `src/ClaudeTuiLine/AcceptedCommand.cs` | **New.** `AcceptedResultJson`, `AcceptedKeyJson`, the key table, and `Build()`. Mirror `ItemsCommand.cs`'s structure. |
| `src/ClaudeTuiLine/Program.cs` | Add `case "--accepted":` alongside `:458–483`'s other mode cases. |
| `src/ClaudeTuiLine/Program.cs:501` | Add `--accepted` to the mutual-exclusion comment and the mode set. |
| `src/ClaudeTuiLine/Program.cs:508` | Add `--accepted` to the mutual-exclusion error message. |
| `src/ClaudeTuiLine/Program.cs:517–518` | Add the `("--accepted", new[] { "json" })` row to the allowed-arguments table. |
| `src/ClaudeTuiLine/Program.cs` | Emit via `Console.Out.WriteLine(JsonSerializer.Serialize(result, AcceptedJsonContext.Default.AcceptedResultJson))`, matching `:624` and `:645`. |
| `tools/check-all.sh` | **Append after line 43**, not between 42 and 43 — see §6. |

###### Visibility

Four of the eight `AcceptedTokens` properties are `public`
(`PaneValignParsing`, `PaneAlignParsing`, `PaneDistributeParsing`, `OverflowModeParsing`); four
are `internal` (`BorderStyleParsing`, `ColorSystemAcceptedTokens`, `SplitAcceptedTokens`,
`CaseAcceptedTokens`). `AcceptedCommand` lives in the same assembly, so this blocks nothing.

**Ruling: leave the split alone in #42.** It is pre-existing, it is invisible from outside, and
widening four properties to `public` to satisfy an aesthetic would enlarge the library's public
API for a command that does not need it. Note it, do not fix it here.

##### 5. What #43 can read — the contract this establishes

Stated explicitly, because it determines #43's shape.

For each key `K` documented with accepted values, and each documented value `v`:

1. `K` absent from `--accepted --json`'s `keys` → **error.** The documentation describes a key
   the binary does not know.
2. `v ∈ K.accepted` → **pass.**
3. `v ∉ K.accepted` and `K.alsoAccepted` is null → **error.** Documentation promises a value the
   binary rejects. This is the case §1.1.2 was written to catch.
4. `v ∉ K.accepted` and `K.alsoAccepted` is non-null → **unverifiable**, reported by name, not
   an error. The key has an open form the checker cannot parse; a human reads the list.

Case 4 exists so that an open-formed key produces a *visible* list of unchecked values rather
than a silent skip. Today only `size` reaches it.

**The reverse direction is never checked.** A token in `accepted` that no document mentions is
not a finding — that is §1.1.2's ruling and it is not reopened here.

##### 6. Citations to re-point

Verified by direct read at `7a30bfb`, not inferred:

1. **`tools/check-all.sh:42–43` is correct as cited today** (`:42` is `check-docs.sh`, `:43` is
   `check-examples.sh`). Wiring in a third check **appends after line 43**, making that block
   42–44. §1.1.2's citation at its *"wire it into `tools/check-all.sh:42–43`"* line must become
   **42–44** in the same change. Appending rather than inserting is what keeps `:42` and `:43`
   pointing at the same two lines they always did.
2. `check-all.sh:15–19` (the fold ruling) and `check-all.sh:4–10` (the never-ran story) are both
   **accurate and unaffected**.

##### 7. A second-order duplication, named and scoped out

`ConfigChecker.CheckEnums` enumerates the nine kinds as nine hand-written call sites
(`ConfigCheck.cs:299, 304, 333, 344, 349, 354, 359, 364, 369, 388, 393`). `AcceptedCommand` will
enumerate them again as a key table. **That is two lists of the nine kinds** — the §1.1 failure
mode, one level up from the one #38 just fixed.

It is genuinely smaller than #38's: each entry is a key name and a property reference, not a
copied token list, so the two cannot disagree about *what a kind accepts* — only about *which
kinds exist*. And unifying them properly is harder than it looks, because the call sites read
their value from three different walk contexts (top-level config, pane, item), so a single table
row would need a context-dependent accessor.

**Scoped out of #42 deliberately.** Tracked as its own follow-up task: make `AcceptedCommand`'s
table the single registry of kinds and have `CheckEnums` iterate it. Named here so that #42 does
not ship a silent second list.

##### What must not change

1. **`ConfigCheck.cs:293`'s `SizeValues` stays exactly as it is** — mixed literals and forms,
   local to `ConfigCheck`. It is a message-formatting array for `FormatAccepted`, not a
   registry, and §1.1.1's exemption still governs.
2. **No token list is copied into `AcceptedCommand`.** Every one of the eight rows reads its
   parser-colocated `AcceptedTokens` property. A hand-written array in the new file reintroduces
   precisely what #38 removed.
3. **`check-docs.sh` does not learn about `--accepted`** and gains no skip branch. This surface
   needs a built binary, so its consumer belongs beside `check-examples.sh`, per
   `check-all.sh:15–19`.
4. **The reverse-direction check is not added.** Registry tokens absent from documentation are
   not findings.
5. **§1.1.2's two documented drifts stay.** `README.md:138` still omits `none`, `:142` still
   omits `greedy` — they are ruling-3 evidence, not defects.
6. **Line citations in this section** are invalidated by construction if the cited code moves.
   Any task restructuring `Program.cs`, `ConfigCheck.cs`, or the parser files must include
   re-pointing them.

##### Verification

1. `--accepted --json` exits 0 and emits JSON with a `version` field and a `keys` array of
   exactly **nine** rows: the eight enumerable kinds plus `size`.
2. Bare `--accepted` exits non-zero with a usage error naming `--accepted --json`.
3. `--accepted` combined with any of `--check`, `--version`, `--items`, `--colors`, `--preview`
   produces the mutual-exclusion error from `Program.cs:508`.
4. Every row satisfies the invariant: `accepted` non-empty, or `alsoAccepted` non-empty, or
   both. **Demonstrate the failure path** — temporarily null both on one row and confirm the
   command fails rather than emitting it.
5. `size`'s row has `accepted: null` and an `alsoAccepted` string, and that string is
   byte-identical to what `FormatAccepted(SizeValues)` produces. Changing `SizeValues` changes
   the emitted prose without any edit to `AcceptedCommand`.
6. Adding a token to any one of the eight parser registries changes that key's `accepted` array
   with no edit to `AcceptedCommand`. **Demonstrate on at least one kind**; asserting it would
   reproduce the defect `check-all.sh:4–10` exists to prevent.
7. `grep` finds no string literal in `AcceptedCommand.cs` matching any accepted token of the
   eight enumerable kinds — the mechanical form of *what must not change* item 2.
8. **Out of #42's scope, belongs to #43.** The appended `check-all.sh` check and the
   `42–43`→`42–44` citation are #43's subset-check logic (§5), not #42's — #42 only builds the
   surface #43 consumes. Leave `check-all.sh` and the §1.1.2 citation untouched until #43 lands.
9. `tools/check-all.sh` green.
10. `tools/check-docs.sh` still completes on a machine with no .NET toolchain.

##### NEEDS-EVIDENCE

Empirical, resolved by whoever builds this rather than asserted in advance:

- **(a) What `JsonPropertyName` does `ItemsResultJson` use for its version field?**
  Read `ItemsCommand.cs:23–33`. `AcceptedResultJson` must use the identical spelling. If the
  siblings disagree with each other, report that rather than picking — a third spelling is worse
  than either.
- **(b) Is `FormatAccepted` reachable from `AcceptedCommand` as written?** It is
  `private static` at `ConfigCheck.cs:400`. If widening it to `internal` is more disruptive than
  expected, the fallback is for `AcceptedCommand` to hold the sentence and for a test to assert
  it equals `FormatAccepted(SizeValues)` — a check instead of a shared call. Prefer the shared
  call; take the fallback only if the widening drags other things with it.
- **(c) Resolved, correction.** `--colors` is not JSON-only — it has its own bare-mode render
  (`RenderMarkupLines`), documented as the deliberate exception to §9.6.2.2's plain-only rule,
  distinct from both JSON and `--items`' `RenderPlainText`. Doesn't change §1's ruling —
  `--json`-required for `--accepted` is stated as deliberate regardless of precedent — but the
  "settled as no" premise above didn't hold as stated.

##### Open questions for the user

1. ~~Should `size` be decomposed into three literal tokens plus a prose form?~~ **Ruled above:
   not now.** It would be a strict improvement to #43's coverage, and it reopens §1.1.1's and
   §1.1.2's exemption — tracked as its own follow-up task rather than decided as a side effect of
   #42.
2. **Is `--accepted` a supported public interface or internal tooling?** This section assumes
   *public and documented*, on the grounds that a hidden flag a checked-in tool depends on is a
   private API with extra steps that someone will eventually delete. This is genuinely open —
   flagged for Jim, not decided here. #42's implementation is unaffected either way; #43 (which
   builds on this surface as a dependency) is held until it's answered, since that is the point
   past which changing the answer costs more.

## 2. The render surface and panes

### 2.1 What the surface is

The statusline is a rectangle: **width** = `COLUMNS - chromeReserve` (Phase 1), **height** =
however many lines we print, since Claude Code renders one row per stdout line. Panes
partition that rectangle.

### 2.2 The pane tree

A pane is either a **leaf** (holds items, renders them) or a **split** (holds children).

```
Pane
    Split      none | horizontal | vertical
    Children   Pane[]          (split only)
    Size       "auto" | "40%" | 24        (share of the PARENT's split axis)
    Border     border config, per pane, independent of any other pane.
               An IMPLICIT border applies to leaves only — see §8.
    Overflow   wrap | truncate | overflow    (§2.6) — inherited by this pane's items
    Ellipsis   string, default "…"           marker for truncation, "" for a hard clip
    MaxRows    int, default unbounded        rows this pane may occupy
    Items      item[]          (leaf only)
```

- **`vertical`** splits side by side — children divide the parent's **width**, each spanning
  the parent's full height. This is the hard one; it requires real compositing (§2.4).
  Concretely: `height(vertical split) = max(height(children))`, and **every** child is then
  rendered at exactly that height, its border drawn around the full extent rather than around
  its content. `valign` distributes the leftover inner rows above and below the content; it
  never changes a pane's extent. A box that stops short of its siblings is this rule broken.
- **`horizontal`** splits top to bottom — children divide the parent's **height**, each sized
  independently within the parent's width (§2.3.5). This is nearly free, because rows are
  already the output unit.

Naming follows tmux/vim convention: a "vertical split" produces a vertical divider.

**Leaf-or-split is decided exactly once, at config resolution, and normalized before anything
downstream sees the tree.** A non-empty `children` with `split` absent normalizes to
`vertical` — a user who wrote `children` has stated the intent to split unambiguously, and
side-by-side is the statusline-shaped default. `--check` notes the omission. Conversely a pane
with `split` set and no children is a leaf, and the stray `split` is dropped.

This is a normalization rule, not merely a default, and the distinction is the point: the
ambiguous state must not survive into the renderer, so no code downstream needs a rule for it and
none can invent a different one. The failure this prevents is two modules disagreeing about
whether the same pane is a container — one resolving borders as a leaf while the other renders it
as a split, producing a pane whose items silently never render. There is ONE predicate, derived
during resolution, and border resolution and render branching both read it rather than each
recomputing the question from raw config fields.

### 2.3 Sizing

Along the split axis, each child declares `size`:

- **integer** — exactly that many cells (or rows), clamped to what remains.
- **`"content"`** — **the anchor**: exactly what the pane's content intrinsically needs, and
  no more. The pane declares its width; the layout does not impose one.
- **`"NN%"`** — that share of the parent's axis extent, rounded down.
- **`"fill"`** (default) — an equal share of whatever is left. This is the pane that *absorbs*
  the consequences of everyone else's sizing, and wraps its content into whatever it ends up with.
- **`"auto"`** — a **deprecated alias for `fill`**. Accepted, resolves to `fill`, and `--check`
  reports `deprecated-size-alias` (warning).

  It is the one value in this vocabulary whose plain-English reading names a *different value that
  also exists*. "Auto" sounds like "size itself to its content", which is `content` — the anchor,
  and the opposite of taking an equal share of the leftovers. So an author who writes `auto`
  meaning intrinsic sizing gets the absorbing pane instead, and the only symptom is a layout that
  looks a bit off at some widths. That is §7.1's class inside the config vocabulary: accepted,
  plausible, and not what was asked for.

  Kept rather than removed because §8's own example config uses it, and because deprecating in
  place is the safe inversion — an author who meant `fill` loses nothing and learns the canonical
  name, while an author who meant `content` finally gets told. Do not add a third spelling of
  either.

Resolution is deterministic, in this order: **fixed → content → percent → fill**. Explicit
numbers win over derived ones; derived-but-firm wins over relative; `fill` takes the remainder,
split left-to-right with the **last** `fill` child absorbing the rounding remainder.

`minSize` / `maxSize` bound a `content` pane, because an anchor with no ceiling can starve
everything beside it. A `content` pane is clamped to `maxSize` (and to what actually remains);
its content then degrades under its own §2.6 rules rather than the layout stretching to obey.

`maxSize` is not enough on its own, though, because it is a fixed number the config author has
to guess. The layout also has to stop an anchor from starving its siblings *as a function of
how much room actually exists*. That is the **viability floor**: the width below which giving a
pane space is pointless.

```
floor(p):
    p.minSize set             ->  p.minSize                (author said so; always wins)
    p is a vertical split     ->  Σ floor(children) + gutters
    p is a horizontal split   ->  max(floor(children))
    p.size is fixed           ->  the fixed size
    p.size is "content"       ->  p.minSize, else 0
    p.size is fill/percent    ->  MinUsableWidth (20) + (p bordered ? borderReserve : 0)
```

Note which kinds get the default 20: **`fill` and percent only.** A `content` pane's floor is 0
absent an explicit `minSize`, and a fixed pane's floor is what it declared. Imposing 20 on a
`content` pane would be wrong on its own terms — a pane sized to its content is by definition
usable at that size — and it would forbid the very degrade §2.9 depends on.

A split's floor follows its orientation, for the same reason its allocation does: the axis a
split **divides** sums, the axis it **shares** maxes. A vertical split's children divide width,
so its width floor is their sum plus gutters; a horizontal split's children are each sized
independently against the full width (§2.3.5) and impose nothing on one another, so its width
floor is the largest of theirs. Getting this backwards over-states the floor
of a nested horizontal split, which caps a `content` sibling harder than the room warrants and
shows up much later as an unexplained degrade rather than as an arithmetic bug.

**Allocation within one split, one pass:**

1. `avail` = this pane's inner width − Σ gutters.
2. Grant every **fixed** pane its size. `rem` = `avail` − Σ fixed.
3. `reserve` = Σ `floor(p)` over every unresolved percent/`fill` sibling.
4. For each **`content`** pane, in declaration order:
   - `cap`   = `rem` − `reserve` − Σ (`minSize` else 0) of the `content` panes after it
   - `grant` = clamp(measured, `p.minSize`, min(`p.maxSize`, `cap`))
   - `rem` −= `grant`
5. **Percent** panes: `pct × avail` — the pre-allocation figure, not `rem` — clamped to `rem`.
6. **`fill`** panes split `rem` evenly; leftover cells go to the leftmost.

Step 4 is the rule that keeps an anchor from eating the surface, and it has **no trigger
condition**. The cap is applied on every pass unconditionally; it simply does not bind when
space is ample. At §2.9's wide case the cap is far above the anchor's ask and is inert; at its
narrow case the same formula produces a cap below the ask, so it binds and forces the degrade.
Same formula, no branch.

The figures are §2.9's and are deliberately not restated here. They were, and the copy had drifted:
this paragraph asserted the anchor asks for **43** while §2.9 measures **66** for the same pane in
the same config at the same width. §2.9 states that its integers are *measured, not asserted* —
they come from rendering that config — so a second copy of them somewhere else is a hand-maintained
duplicate of an output, and the version that goes stale is always the one no test reads. Any prose elsewhere in this spec that reads like a
special "the surface is too tight" mode is describing this formula's *outcome* — there is no
second code path, and implementing one violates §1.

No child may resolve below its **drop floor**, and children granted less are **dropped entirely**
rather than rendered at a nonsense width, with the freed space redistributed. A pane's drop floor
is its `floor(p)` from the table above, never less than 1 cell — a `content` pane's `floor(p)` is
0, because it asked for its width and was granted it, so 1 cell is the operative minimum there and
the only pane it ever drops is one granted nothing at all.

A **`fill` or percent** pane whose resolved inner width falls under `MinUsableWidth` (20)
suppresses its own border first (SPEC.md §6b narrow-width suppression, now applied per pane rather
than per surface), and is dropped only if it still does not fit. **Suppression precedes the drop
test, and lowers the floor it is tested against**: once the border is gone the pane no longer needs
the reserve that paid for it, so its drop floor is `MinUsableWidth` alone. Testing the
border-inclusive floor against a pane that would have survived without its border drops a pane §2.3
requires be kept.

**The allocator must test this predicate; deriving the floor is not enough.** Every allocation path
in §2.3.1 can hand back a grant below the floor it just computed, because none of them subtract the
floor from anything: the fill divide splits whatever remains, which may be less than the floors it
was divided among, and §2.3.3's over-constrained fixpoint returns floors that were never summed
against the budget at all. A grant below its floor that is not dropped is a `minSize` the author
declared and the renderer silently ignored — the failure this floor exists to prevent. An
implementation that computes `floor(p)` for allocation and then drops on "was anything left" has
not implemented this rule.

`MinUsableWidth` governs `fill` and percent panes **only** — for border suppression exactly as
it does for the viability floor above, and for the same reason: *a pane sized to its own content
is by definition usable at that size.* A `content` pane is never squeezed; it requested its width
— borderReserve included — and was granted it, so suppressing the border it already paid for
would leave it holding dead space where its box should be. A fixed pane is the same case: the
author named the number. Suppression exists for a pane squeezed below viability by *someone
else's* sizing, which is precisely what `fill` and percent are. One predicate governs both rules;
letting the floor and the suppression disagree about what `MinUsableWidth` means is what produced
a `content` pane granted 12 cells for 8 columns of text and no border at all.

**Over-constrained splits.** If `rem − reserve` goes negative, the split cannot honour its own
floors — three bordered panes in a 40-column terminal, say. The cap must not be allowed to go
negative and sub-floor panes must not be produced silently. This routes into the same drop loop
as the <1-cell case: drop the **last** child, recompute from step 1, repeat. One loop, two
triggers — not a new mechanism. If it reduces to a single child, that child takes the full
width, and §2.6's surface-level fallback applies only if it is by then the sole pane of the
*surface*. This is a layout outcome, not an error, and nothing is logged.

**Intrinsic width is measured, never estimated.** A pane reports what it needs by actually
assembling its content: the width it would need to render unwrapped on a single row. That is
worth knowing before using it — a `content`-sized pane asks for its *entire* unwrapped width,
which for the 14-segment statusline is far more than any terminal has. `content` is for anchors
whose natural size is small and meaningful; `fill` is for everything else. That is exactly the
division in §2.9.

**Sizing iterates to a fixpoint, and the fixpoint is guaranteed to exist.** A single pass is
not enough: an anchor that asks for 39 and is clamped to 25 wraps, and its longest wrapped row
may be only 18 — stopping there strands 7 columns inside the anchor while the `fill` sibling is
cramped. Freed space must reach the sibling.

So:

1. Every `content` pane reports its preferred intrinsic width. Resolve all sizes by running the
   six-step allocation above.
2. Any `content` pane granted **less** than it asked for — which now includes being capped by
   step 4 on account of a sibling's floor, not only by `maxSize` or by exhaustion —
   re-reports its intrinsic width *under the width it was actually granted*, i.e. after
   wrapping at that width — i.e. its longest wrapped row (§2.9).
3. Re-resolve. Repeat while any request changed.

**A pane that degrades stays degraded for this render.** When an anchor is capped to 32, falls
back to text needing 12, and its `fill` sibling consequently balloons from 13 to 44, the anchor
must **not** re-expand on noticing the slack. The monotone clamp below forbids it, and this is
the exact case that clamp exists for: re-expansion here is a genuine two-cycle, not a
hypothetical one.

**Termination is enforced, not assumed: a pane's request may never increase between passes.**
If a re-measurement returns a larger width than that pane's previous request, it is clamped to
the previous request. Requests are therefore non-negative integers that strictly decrease until
they stop changing, so the loop converges — in practice in two passes. Cap it at **3** as a
backstop and use the last resolved sizes if the cap is hit.

The clamp is what makes this safe, and it is deliberately not a good-behavior assumption about
content: a renderer that asked for *more* space when given *less* would cycle forever, so the
layout refuses the request rather than trusting every present and future renderer not to make
it.

An earlier draft of this section banned re-measurement outright, reasoning that it could
oscillate. That was over-cautious and it was wrong about the mechanism. The loop runs **within
a single render**, and a single render is deterministic — same inputs, same output — so it
cannot produce flicker across renders no matter how many passes it takes. Only a changing input
can do that, which is not something layout gets to prevent.

`gutter` (default `0`) is the number of blank cells between siblings in a vertical split,
subtracted from the available extent before children are sized.

**`distribute` — sizing every pane correctly still gives the wrong surface.**

Everything above resolves each pane's width from that pane's own declaration. Every pane can be
individually correct and the surface still wrong, because the quantity a reader cares about is
not any pane's width — it is how many rows the whole surface occupies. A `content` pane capped
just below its natural width wraps to two rows while its `fill` sibling sits half empty, and
neither pane is in a position to notice: the cap was satisfied, the fill was satisfied, and the
surface grew by a row that a different split would not have cost.

No static configuration fixes this, and that is a measured claim rather than a design opinion.
Sweeping `maxSize` for a real two-pane config across terminal widths 100–240 produced no value
that wins everywhere: `42` holds a flat 4 rows at every width; removing the cap reaches 3 rows
at 160+ but costs **7** at 100, because the right pane takes its natural width and the left
wraps four times paying for it. Any single number is a bet on one terminal width, and the user
resizes.

So a split may declare how it divides extent among its children:

```json
{ "split": "vertical", "distribute": "min-rows", "children": [ ... ] }
```

- **`greedy`** (default) — the resolution above, unchanged. It stays the default because it is
  what every existing config already means, and a layout policy is not something to change under
  a user who did not ask.
- **`min-rows`** — choose the allocation minimising the surface's total row count.
- **`even`** — equal extents, for a layout that must not move as content changes. Stability is
  sometimes worth more than tightness; a status bar that reflows on every token count is
  harder to read than one that wastes a column.

**Two of those three do not exist, and this document is the reason nobody noticed.** The parser
recognises `min-rows` and nothing else; every other string — including both `greedy` and `even` —
falls through to greedy. Three authorities currently give three different answers about what this
key accepts:

| authority | the language it states |
|---|---|
| this section | `greedy`, `min-rows`, `even` |
| §9.4.1's closed-set list | `distribute ("min-rows")` |
| `PaneDistributeParsing.Parse` | `min-rows`, else greedy |

`even` is the expensive one. §2.4 offers it to a user who wants a layout that holds still —
"a user who wants stability has both `fixed` and `distribute: "even"` to ask for it in the config
rather than receiving it as an accident of the compositor" — and what they get for writing it is
greedy sizing, which is the reflowing layout the sentence was written to steer them away from.
The recommendation and the failure are the same act. Worse, once §9.4.1's `unknown-enum-value`
lands as specced, `even` becomes a **`--check` error on a value this document recommends by name**.

Ruled, all three in the language:

- **`greedy` is accepted explicitly.** Naming the default is how an author records that they
  considered the choice, and a language where the default is the one value you may not write is a
  language that punishes being explicit.
- **`even` is implemented**, and it is the cheap one: divide the extent left after fixed and
  percent equally among the remaining candidates, ignoring both intrinsic measurement and the
  content/`fill` distinction. That last part is the point rather than a simplification — `even`
  means the widths do not depend on the content, which is exactly the property that makes the
  layout stop moving.
- **A `content` pane under `even` still degrades under §2.6** at whatever width it is handed. It
  does not get to overrun its share; `even` fixes the extent, not the content.

§9.4.1's list is corrected there rather than here, and the correction is the smaller half of that
fix — see **§9.4.3**, which is why all three of these disagreed without anything reporting it.

`min-rows` binds only the extent left over after the existing rules have had their say. Fixed and
percent panes are *not* candidates — they were given an exact answer and the policy does not
overrule it — and `minSize`/`maxSize` remain hard bounds on every candidate considered. The
policy chooses among allocations that are already legal; it never invents one that isn't.

Ties break toward the **most even** allocation. Two allocations costing the same rows are equally
good by the stated objective, and evenness is the tiebreaker a reader will perceive as
deliberate rather than arbitrary.

**The search must be over breakpoints, not widths.** The naive reading of "minimise rows" is to
lay the surface out at every candidate width and count, which at 180 columns is ~180 full layouts
per render against a 44 ms budget currently spending 13 ms. That would trade a feature for the
performance property this project was rebuilt to get. It is also unnecessary: rows-as-a-function-
of-width is a **monotonically non-increasing step function**, and it can only step where a pane's
greedy packing gains or loses a row — at most once per item. A pane with 14 items has at most 14
breakpoints, so the candidate set is *tens* of allocations, not hundreds, and the optimum is
guaranteed to sit at a breakpoint because the function is flat between them.

An implementation that samples every width is therefore not a slower version of this — it is a
different algorithm with the same output and the wrong cost, and it must not ship on the grounds
that the tests pass. **Latency is re-measured against the p90 budget as an acceptance condition
for this feature, not as follow-up work.**

#### 2.3.1 The allocation algorithm

Search the **answer**, not the input. The naive framing — try allocations, count rows — searches
a space that grows with the number of panes. Inverting it collapses the problem: instead of
asking "how few rows does this allocation cost?", ask "is a surface of `T` rows achievable at
all?" That question is cheap, and it is monotone in `T`, which is the whole game.

**The two functions this rests on.**

For a candidate pane `i`, `rows_i(w)` is its greedy row count at width `w` — the existing packer,
called unchanged. It is non-increasing in `w`. Define its inverse:

```
minWidth(i, T) = min { w : rows_i(w) <= T }
```

the narrowest width at which pane `i` fits in `T` rows or fewer — **when one exists at all; see
§2.3.3, which rules the empty case and is reached on the worked example below at `T = 1`.** Compute
it by binary search over `[minSize_i, maxSize_i]`, which is valid precisely because `rows_i` is
monotone. That is
`O(log w)` packings per pane per `T`, and it is what keeps the search over breakpoints rather
than widths: the binary search *lands on* a breakpoint without ever enumerating the widths
between them.

**`maxSize` is optional, so that interval is half-open as written.** When a candidate declares no
`maxSize` the upper bound is `R` — the extent actually remaining — and never the pane's intrinsic
width. Intrinsic looks like the natural ceiling and is the wrong one: a pane narrower than its
content is exactly the pane min-rows needs to consider, since a `content` pane that wraps to two
rows may be what lets the whole surface fit in `T`. Bounding the search at intrinsic would make
the algorithm unable to see the allocations the feature exists to find, and it would return a
legal, suboptimal answer with no symptom.

**Feasibility.** A vertical split places panes side by side, so the surface is as tall as its
tallest pane. Therefore a surface of `T` rows is achievable exactly when every candidate can be
made to fit in `T` rows with the extent available:

```
feasible(T)  ⇔  Σ minWidth(i, T)  ≤  R
```

where `R` is the extent remaining after fixed and percent panes and gutters have taken theirs. If
any `minWidth(i, T)` exceeds that pane's `maxSize`, `T` is infeasible regardless of `R` — the
bound was declared hard and the policy does not overrule it.

**The search.** `minWidth(i, T)` is non-increasing in `T`, so `feasible(T)` is monotone: once a
row count is achievable, every larger one is too. Scan `T` upward from 1 and stop at the first
feasible value. `T` is bounded by the largest `rows_i` any candidate reports at its own `minSize`
— see §2.3.3, which rules this bound and explains why the item count is not it. That ceiling is
usually small but is not bounded by ~20; measured latency against this section's own acceptance
condition below shows a linear scan over `T` still meets budget even under the corrected bound, so
the scan remains linear rather than moving to a binary search. The result is exact, not a
heuristic.

**This is O(N) in the number of panes, which is why it is the algorithm and not merely one.** A
search over allocations would be exponential in `N` and would have forced restricting the feature
to two-pane splits. Searching over `T` instead makes the pane count almost free: each additional
pane adds one term to a sum. The two-pane case that motivated the feature is just `N = 2`, with no
special path.

**Distributing the surplus.** The winning `T` leaves `S = R − Σ minWidth(i, T)` unallocated.
Giving a pane more width can never increase its row count, so distributing `S` is always safe and
is purely the tiebreak from above made concrete — spend it on evenness:

- Water-fill: repeatedly give one cell to the currently-narrowest candidate, subject to its
  `maxSize`. Equivalently, level the widths up to a common ceiling and hand any remainder left to
  right.
- **A `content` pane is capped at its natural width.** Extra columns beyond what its content needs
  are not a benefit to it — fitting its content is the entire meaning of `content` — so surplus
  past that point flows to the `fill` siblings instead.
- If every candidate is capped and surplus remains, the split leaves it unconsumed, exactly as
  `greedy` would. The policy chooses among legal allocations; it does not invent a place to put
  extent that no pane asked for.

**Worked, against the config that motivated this.** Two candidates, `R = 108` at `COLUMNS = 112`
— 112 less the 3-column `chromeReserve` is a 109-column surface, less 1 for the gutter between
the two panes, exactly as §2.9 computes it.
`T = 1`: the left pane's ten items need far more than 108 columns on one row, so
`minWidth(left, 1)` alone exceeds `R` — infeasible. `T = 2`: still infeasible. `T = 3`:
`minWidth(left, 3) + minWidth(right, 3) ≤ 108` — feasible, so `T = 3` wins and the surplus is
water-filled. Three packings-with-binary-search per pane per `T`, four values of `T`, two panes:
tens of packer calls, which is the cost this section demanded.

**`min-rows` replaces §2.3's fixpoint; it does not run on top of it.** The two are alternative
answers to the same question, and the spec never said so — it presents the fixpoint as
unconditional ("Sizing iterates to a fixpoint") and then presents this algorithm as though it
slotted in beside it. The implementation branches between them, correctly, on a fact this document
does not contain.

It has to be a branch. `rows_i(w)` is already wrap-aware — that is the entire content of
`minWidth(i, T)` — so min-rows has priced wrapping before it chooses. Running the fixpoint
afterwards would re-measure every `content` pane at the width min-rows deliberately gave it, find
a narrower longest-wrapped-row, and shrink it; the monotone clamp would then make that shrink
permanent. The freed extent goes to a `fill` sibling that did not need it, and **the surface comes
out taller than the `T` the search proved achievable.** The feature would defeat itself, on the
configs it was written for, while every pane remained individually legal — §7.1's class at the
level of two algorithms rather than one value.

State it as the property rather than as the branch: **exactly one width-resolution policy runs per
split.** A second policy applied to the first one's output is not a refinement of it.

One consequence to fix rather than record: §10 requirement 6's three fixpoint tests reach the resolver through
a `measureOverride` seam that the greedy path threads and the min-rows path does not take at all.
So the pass-cap test, the monotone-clamp test, and the convergence test **cannot be run against
`min-rows`**, and the paragraph above is the argument for why they would fail if they could be.
The seam belongs on both paths.

**Acceptance conditions**, both of which must be demonstrated rather than argued:

1. **Optimality** — on a config small enough to brute-force, the allocation this returns must
   equal the best found by exhaustively laying out every legal width. The brute force belongs in
   the test, never in the shipped path; it exists to prove the fast algorithm agrees with the
   slow, obviously-correct one.
2. **Latency** — p90 re-measured against the budget (§5) with `min-rows` active across widths
   100–240. A regression here fails the feature, per the paragraph above.

#### 2.3.2 Keys that are valid, spelled right, and meaningless where they are written

`distribute` divides extent among siblings that sit side by side. A **horizontal** split does not
divide extent — its children stack downward and are each sized independently within the split's
inner width (§2.3.5) — so there is nothing for the policy to choose. Independent sizing is not
division: two stacked children may each take the whole width, because they occupy different rows
and never contend for the same cell. `{"split": "horizontal", "distribute": "min-rows"}` is read, is
a legal value of a legal key, and does nothing whatsoever. The resolver never reaches the branch.

`gutter` is the same shape: §2.3 defines it as blank **cells between siblings in a vertical split**,
subtracted from the extent before children are sized. On a horizontal split there is no such extent
and the key is inert.

Neither is a typo, so §9.4.2 does not see them and neither does §9.4.1 — the key is known and the
value is in the language. **This is a third silence, and it is the one with the most convincing
alibi:** the author wrote a documented key with a documented value, and the config looks right in
review. Only the render disagrees, by not differing at all.

Ruled: **`key-not-applicable`, severity `warning`**, for a known key with a legal value in a
position where nothing reads it. The message says where it *would* apply — `distribute has no
effect on a horizontal split; it divides extent among side-by-side children`. That second clause is
the whole value of the diagnostic, because an author who wrote `distribute` on a horizontal split
has the axis convention backwards, and telling them only that the key is ignored leaves them to
re-derive which way round it goes.

The same code covers every key that meets that predicate — *is this key read on this node, given
the shape the node actually has once it is normalized?* It is a predicate, not a list, per §9.4.1's
argument about enumerations, so what follows is a census of what satisfies it today, not a closed
set that a new case has to be added to:

- `distribute` on a horizontal split — the policy divides extent among side-by-side children, and a
  horizontal split has no such extent to divide.
- `gutter` on a horizontal split — blank cells between siblings in a vertical split, so there is
  nothing between horizontally-stacked children for it to insert.
- `items` on a pane that also declares a non-empty `children` — only a leaf pane's items are
  resolved, so the list is never read.
- `children` present but empty — a pane is a split only when its children list has at least one
  entry (§2.2), so an empty list leaves a leaf behind and the key does nothing.
- `split` declared `vertical` or `horizontal` on a pane with no children — a childless pane
  normalizes to a leaf whatever axis it names (§2.2), so the declared axis is inert. This fires
  independently of the empty-`children` case above: a pane that declares both gets both warnings,
  because one condition is one diagnostic and neither repair implies the other.

That census grows without a spec amendment. What is fixed is the predicate and the message
convention stated above.

**This predicate does not swallow `leaf-only-key-on-split` (§2.6) or `border-inside-on-leaf`
(§2.10.1), and they are not folded into it.** All three share a frame — a key that does not suit the
shape the pane actually has — but §9.6.1's codes are what a consumer of `--check --json` branches
on, and the frame is not the unit it branches on. `border-inside-on-leaf` is not an inert key at
all: `"inside"` on a leaf *is* read, and it resolves that leaf's border to none, so the repair is
choosing a different value rather than deleting the key. `leaf-only-key-on-split` is genuinely inert
the way this section's cases are, but it ships as its own code with its own message naming
`overflow` and `ellipsis` and the fact that they do not inherit; §9.6 fixes a shipped code's meaning
permanently, so folding it in here would change the code an existing config already emits. One class
of mistake, three shapes, three codes — deliberately.

**`gutter` is not extended to mean blank rows on a horizontal split**, which is the symmetric
reading and the tempting fix. A gutter row is a permanent terminal row spent on nothing, at
`refreshInterval: 1`, and §2.4 already refuses that trade for an empty pane. Panes that need
visual separation on the vertical axis have §2.10's borders, which cost the same row and carry
information. A feature that costs a row should be asked for by name.

#### 2.3.3 `minWidth` is a minimum over a set that can be empty

§2.3.1 specifies the whole algorithm for the case where an answer exists. The inversion is right,
the monotonicity argument is right, and the cost analysis is right. What it never says is what any
of it returns when a pane cannot be made to fit at all — and that is not a hostile edge case, it is
the first iteration of the scan on the section's own worked example.

**The empty set.** `minWidth(i, T) = min { w : rows_i(w) <= T }` is a minimum over a set with no
members whenever pane `i` cannot fit in `T` rows at any width available to it. §2.3.1's worked
example walks straight into it and then describes it wrongly: "`minWidth(left, 1)` alone exceeds
`R` — infeasible". By the definition three paragraphs above, `minWidth(left, 1)` does not exceed
`R`. It does not exist. The prose reads as though the function always returns a number that can be
compared and summed, and an implementer writing `feasible(T)` as a sum will make it return one.

Ruled: **`feasible(T)` is false the moment any candidate has no such width, and it decides that
before it sums anything.** The empty case is a property of the candidate, not a large number, and
it must not be laundered into arithmetic.

**If a sentinel is used anyway, it may not be `int.MaxValue`.** This is the specific way the
laundering fails. Two candidates that both cannot fit sum to −2 under 32-bit wraparound, −2 is
comfortably `≤ R`, and `feasible(T)` returns true for the narrowest `T` in the scan. The search
then stops at the first `T` it tries, the surplus distribution hands out an extent no pane can use,
and the surface renders taller than the `T` the algorithm just certified — with every pane
individually legal and nothing to report. That is §2.3.1's own stated failure class, produced by
the one line it did not write down. A sentinel that cannot be summed into a false pass — `R + 1`,
tested before the addition — is acceptable; the short-circuit is preferred because it cannot be
got wrong twice.

**`T` is not bounded by the largest item count.** §2.3.1 bounds the scan at "the largest item count
in any candidate pane — around 20 in practice", reasoning that a row holds at least one item. Under
§2.6 that is false: an item wider than the pane wraps, so a pane holding three items where one of
them wraps across four rows has `rows_i` of six. The bound is therefore too low exactly when the
terminal is narrow, which is the condition under which anyone wants min-rows at all. The scan runs
to the largest `rows_i` any candidate reports **at its own `minSize`** — the tallest the surface can
be forced to become — which is the real ceiling and is still a small number.

**When no `T` is feasible.** Even with the corrected ceiling, `Σ minWidth(i, T) ≤ R` can fail at
every `T` when the candidates' minimum widths do not fit side by side. min-rows is a policy that
chooses among allocations that are already legal, and here there are none to choose from, so it has
no answer to give and must not invent one. The split falls back to `greedy`, which already has
defined behaviour for an over-constrained row — it drops panes and says so through §9.8.1's
`pane {n} dropped` note. A second note is not added for the fallback itself: the dropped-pane note
is the observable consequence, and a note reporting that an algorithm declined to run is a message
about the implementation rather than about the layout.

#### 2.3.4 `flex` — a split whose orientation is a function of width

`"split": "flex"` arranges children side by side when they fit and stacked when they do not. It
sits side by side while the vertical floor of §2.3 is within the available width, stacks when
that is exceeded and the horizontal floor is not, and when neither fits stays side by side so the
drop ladder runs — a rearrangement that does not make the split fit is not worth the change in
shape.

`flex` names an arrangement policy rather than a divider, which is why it does not follow the
tmux/vim naming of `vertical` and `horizontal` at `:775`. Those two are unchanged and remain
absolute: `vertical` is always side by side, `horizontal` always stacked.

The declared value is what the author wrote and is fixed at config resolution per §2.2; the
*effective* orientation is computed per render and is always concrete. `flex` is a declared value
only and never reaches anything downstream of the size resolver, which cannot distinguish a
stacked `flex` pane from a declared horizontal one.

**A `flex` pane's width floor is the lesser of its two orientations' floors**, because it can
render in either. This matters beyond the pane itself: an ancestor split's drop decision reads a
child's floor *before* that child's own orientation is decided, so the floor must answer the
orientation-independent question or the ancestor will drop a pane that could have rendered — or
drop a sibling competing with it.

**The same rule governs `--check`'s structural-size validation, in dual form: a `flex` pane's
declaration is reported as over-constrained only when it is over-constrained in BOTH
arrangements.** A pane whose children cannot share the parent's width but can each fit within it
is not an error — it is the case `flex` exists for, and reporting it would make `flex` unusable.
Floors take the minimum over adoptable orientations; impossibility takes the conjunction. These
are the same statement.

The decision is made from width alone and is never revisited once rows are known. Reverting it
because the stacked result busts `maxRows` would make width depend on height, the fixpoint §2.8.1
forbids. Stacking can therefore cost rows; the render note is what makes that visible. Because a
`flex` pane advertises the lower floor, it is allocated less width in a competitive layout than a
declared-vertical pane would be, and so stacks more readily there — the price of the parent being
able to keep every pane alive.

**Border-edge propagation runs at config load, before any width is known, and treats a `flex`
pane as vertical.** A stacked `flex` pane may therefore carry edges chosen for the side-by-side
arrangement. This is accepted: the alternative is a width-dependent resolved tree, which §2.2
forbids.

`gutter` and `distribute` apply while the pane is side by side and are ignored while it is
stacked, exactly as §2.3.2 ignores them on a declared horizontal split. They are never diagnosed
as inapplicable on a `flex` pane, because it can render side by side. Beyond the structural check
above, `--check` predicts none of this: per §9.8 it is width-independent.

#### 2.3.5 A stacked child is sized independently within its parent's width

A horizontal split's children stack downward, each occupying its own band of rows. They do not
divide the parent's width and never contend for it, so each child's width is resolved on its own
against the same ceiling: the split's inner width, `outer − reserve(p)`.

A stacked child's `size` is read with the same vocabulary as a side-by-side child's, resolved
against that ceiling rather than against a share of it: `fixed` takes its declared cells,
`percent` takes that fraction of the ceiling, `content` takes its natural width measured at the
ceiling as cap, and `fill` — the default, and the behaviour of every stacked child before this
section existed — takes the ceiling. `minSize` and `maxSize` then bound the result.

**The ceiling always wins.** A child whose declared size, or whose `minSize`, exceeds the ceiling
is granted the ceiling and the clamp is reported as a render note (§9.8.1). It is never dropped:
dropping a stacked child frees width for nobody, so the over-constrained handling of §2.3 has
nothing to do here.

A child granted less than the ceiling is positioned within it by its own `selfAlign` (§3.1),
which is where the leftover width goes. A grant that resolves below `RowLayout.MinUsableWidth`
(a narrow `percent` or a small `maxSize`) stands as declared — there is no drop, and the
renderer's existing narrow-width degradation governs; no render note is emitted for it, since a
clamp note reports the framework overriding the author, and here the author got what they asked
for.

### 2.4 Compositing — the part that must not be improvised

A leaf pane renders to a **`PaneBuffer`**: an ordered list of rows, each carrying its markup
*and* its measured plain width. A split composes its children's buffers into one buffer. The
root buffer is printed, one row per line.

Extending the v1 invariant, which stays in force:

> **`RowLayout` is the sole authority on line breaks within a pane. The compositor is the sole
> authority on the surface. Spectre must never re-break either one.**

Compositing rules, all load-bearing:

1. **Every row of a pane buffer is padded to exactly the pane's width** before it is joined to
   a sibling. A short row is not "close enough" — one missing cell shears every column to its
   right for that row only, which is the ugliest possible failure and the hardest to spot in a
   screenshot.
2. **Sibling buffers in a vertical split are padded to a common height** with full-width blank
   rows. Ragged heights corrupt the same way ragged widths do.
3. **Width is measured on ANSI-stripped text**, never on the markup string, using the same
   `Plain.Length` metric as v1 (§11 keeps wcwidth out of scope deliberately).
4. **Trailing whitespace is trimmed once, at the very end, on the composed root rows** — and
   only when the rightmost contributing pane has no background color set, because with a
   background those cells are visible. This is what preserves byte-parity in the single-pane
   case (§2.7).
5. **`Profile.Width` stays at the sentinel** for all pane rendering. Panes are sized by our
   arithmetic; Spectre is used for styling and border glyphs, not for layout. The one thing
   that must never happen is Spectre deciding a break we did not ask for.

**Emptiness — nothing to say means nothing drawn, but only where the user did not ask for a
shape.** SPEC.md:353 required "no segments ⇒ zero output even with border enabled". That rule
survives the pane rewrite and it applies at two levels — the surface and the pane — with the pane
level splitting on whether the user named a size, so there are three cases:

- **An empty surface emits nothing at all** — no border, no blank row, zero bytes. A statusline
  is a permanent fixture at `refreshInterval: 1`; an empty box occupies terminal rows forever
  in exchange for no information, which is strictly worse than absence.
- **An empty `content` or `fill` pane collapses**, taking its gutter with it, and its siblings
  divide the extent it released. Rendering a two-column box around nothing is the same trade as
  above, in miniature. **§2.11 defines which emptiness counts** — not every empty pane qualifies,
  and the one that does not is the one the sizing loop emptied itself.
- **An empty `fixed` or `percent` pane keeps its extent and its border.** The user named a
  number, and the same principle that keeps those panes out of `min-rows` candidacy (§2.3)
  keeps them here: an explicit instruction is not overruled because the framework judged the
  result pointless. Reserving declared space is often exactly the intent — a pane that holds
  its position while its content comes and goes.

This deliberately accepts that a collapsing pane shifts its siblings. That shift is visible and
self-explanatory; a permanent empty box is neither, and a user who wants stability has both
`fixed` and `distribute: "even"` (§2.3) to ask for it in the config rather than receiving it as
an accident of the compositor.

#### 2.4.1 The trim is per row, and it is not a layout step

Rule 4 says trailing whitespace is trimmed "only when the rightmost contributing pane has no
background color set", as though a surface had one such pane. **A root horizontal split does not.**
Its children stack, so row 1's rightmost cells come from one child and row 9's from another, each
with its own background. Evaluate the condition once and one of two things happens:

- decided from a background-less pane ⇒ the backgrounded pane's rows are trimmed, and its colour
  band ends several cells short of the surface on exactly the rows that carry it;
- decided from the backgrounded pane ⇒ nothing is trimmed anywhere, which costs only bytes.

The failure is asymmetric, and the direction that renders wrong is the one an implementer reaches
first, because "the rightmost contributing pane" reads like a property of the surface. **The
condition is evaluated per composed row, against the pane that contributed that row's rightmost
cells.** The same applies to a vertical split whose right-hand child is itself a horizontal split.

**The trim is also not a layout step, and §10 must not measure through it.** Rule 1 pads every row
to exactly the pane's width; the trim then removes padding from some rows and not others, so after
it runs **the composed rows are no longer equal width** — by design, and only in cells that were
blank. §10's rectangle invariant is therefore a property of the composed buffer, not of the emitted
bytes: it is asserted before the trim, which is the last transformation applied to the surface and
sits after every layout assertion. A test that measures the emitted line and still passes is
measuring a surface where no row had trailing space, which §10.1 already names as the problem —
the assertion holds for a reason unrelated to what it claims.

### 2.5 A pane's content sees only its own pane

**Content is laid out against the pane's inner width, never against `COLUMNS`.** A pane is the
entire world its content knows about.

A pane's **inner width** is its resolved outer width minus its own chrome:

```
inner = outer - (bordered ? borderReserve : 0)        borderReserve = 4  (2 verticals + 2 padding)
```

`chromeReserve` is subtracted exactly once, at the root, to get the surface width. It never
appears again anywhere below that.

Everything downstream takes inner width as a parameter:

- `RowLayout.Wrap` packs segments to the **pane's** inner width. Greedy packing, the separator
  budget, and the row-break decisions are all pane-local.
- The narrow-width rules are evaluated **per pane**: a `fill` or percent pane whose inner width
  falls below `MinUsableWidth` (20) suppresses its own border and re-measures (§2.3 — `content`
  and fixed panes never suppress, having sized themselves); a wide terminal split into
  four columns hits these thresholds constantly, so they are ordinary operating conditions now,
  not an edge case for tiny terminals.
- Truncation (§2.6) measures against the pane's inner width.
- `command` providers receive `CLAUDE_TUI_LINE_PANE_WIDTH` = the inner width of the pane the
  item lives in, so a user script can size its own output to the space it actually has.

#### 2.5.1 The exported width, the fixpoint, and the cache key make three

`CLAUDE_TUI_LINE_PANE_WIDTH` is not a free hint. Three rulings from three different sections
intersect on it, and the intersection was not looked at when any of them was made:

- **§2.5** exports the pane's inner width to every `command` provider.
- **§4.2.3** put every input the child can see into the cache key, which now includes that width.
- **§2.3** resolves sizes by a fixpoint of up to three passes, in which a `content` pane's grant
  and its `fill` sibling's grant both change between passes — that is the entire purpose of the
  loop.

Compose them and a `command` item is a **cache miss on every pass**, because the key it is looked
up under contains a width that the loop is in the business of changing. The process is spawned up
to three times per render, at `refreshInterval: 1`. Nothing reports this; the statusline is correct
and the machine is doing three times the work.

It is also circular where it bites hardest. A `content` pane's width is *derived from measuring its
content*; if that content includes a command whose output depends on the width, the pane's width
depends on a value that depends on the pane's width. §2.3's monotone clamp does not save this — it
constrains the *pane's request*, not the *command's output*, and a script that prints more when
given more room is behaving reasonably. Termination is guaranteed; agreement is not.

Ruled, in two parts:

1. **A `command` item is spawned at most once per render.** The width it receives is its pane's
   grant from the **first** pass, and later passes reuse the value rather than re-fetching it. The
   width that enters the §4.2.3 cache key is the one actually exported, so the key stays honest —
   it describes what the child saw, which is the property §4.2.3 states.
2. **A `content` pane's items are measured with the variable unset.** Not with a guess, not with
   zero: unset, which §4.2.3 already treats as a distinguished value rather than a missing one. A
   pane whose width is defined as the measurement of its own content cannot also be an input to
   that measurement, and no ordering of the passes fixes that.

The rule those two share is worth stating on its own, because it is what decides the next pane kind
someone adds: **a pane exports its width only if its width does not depend on what the export
returns.**

The accepted cost is a stale hint on a `content` or `fill` pane that later degrades — the script
sized itself to a pane wider than the one it ended up in. That is the safe direction and it needs
no new machinery: output too wide for its pane is precisely what §2.6 exists to resolve, and it
resolves it the same way it resolves any other over-long value. The reverse — a script told it had
less room than it got — would leave the pane visibly short with nothing able to notice.

**Enforcement, not just intent:** `COLUMNS` is read exactly once, in the surface-sizing code at
the root. No leaf-rendering code path may reach it — not `RowLayout`, not `SegmentBuilder`, not
any provider. Leaf rendering is a pure function of `(items, innerWidth)`.

That purity is directly testable, and §10 requires it: **the same leaf pane at the same inner
width renders identically whether it is the root pane of an 80-column terminal or the third
child of a split in a 200-column one.** If those two outputs differ, something below the
compositor is still reading the surface width, and that is the defect the rule exists to
prevent.

#### 2.5.2 The purity property is right; the tuple it is stated over is wrong

§2.5.1 ends by handing §10 a test: "the same leaf pane at the same inner width renders identically
whether it is the root pane of an 80-column terminal or the third child of a split in a 200-column
one." The intent behind it is correct and is the whole point of the section — nothing below the
compositor may read the surface width. The sentence stating it is false, and §2.6 is what makes it
false.

§2.6 rules that a pane under `MinUsableWidth` takes the single-line fallback when the surface has
exactly one pane and never takes it when the surface has more, and it gives a root pane the
`overflow` default while a pane inside a split defaults to `truncate`. Both are deliberate, both
are right, and both are stated as functions of where the pane sits. So the two positions §2.5.1
names as an identity test are precisely the two positions §2.6 distinguishes: at an inner width
below 20 the root pane emits one deliberately over-wide row and the third child of a split wraps or
truncates, from the same items at the same inner width. The test as written asserts that these
agree. They must not.

The implementation already knows this. `PaneRenderer.RenderLeaf` takes
`(items, innerWidth, overflow, ellipsis, notes, allowFallback, rowBudget, markerRequired)` — the
two inputs §2.5.1 names, plus the two that §2.6 varies by position, plus the collector, plus the
two SPEC-2.6-vertical-marker-splice.md §9 adds for the vertical-overflow marker splice. The
signature is right and the sentence is a summary of it written by someone who was thinking about
`COLUMNS`.

Ruled: **leaf rendering is a pure function of its full input tuple — `items`, `innerWidth`,
`overflow`, `ellipsis`, `allowFallback`, `rowBudget`, and `markerRequired` — and §10's test fixes
all of them, not two of them.** Same tuple, same bytes, at any position in any surface at any
terminal width. That is a real property, it is the one §2.5.1 wanted, and unlike the version it
wrote down it can actually hold.

**`allowFallback` is a surface fact, computed at the root and passed down.** This is the part worth
enforcing rather than recording. It is derived from the surface's pane count, so a leaf that
computes it for itself has to ask a question about the surface — which is the defect §2.5.1's
enforcement paragraph forbids, wearing different clothes. `COLUMNS` is the obvious way to reach
around the compositor and this is the quiet one: no leaf may consult the pane count, the tree, or
its own position. It receives the flag or it does not render.

A note on why this survived: the false version of the test passes. Every fixture at an inner width
of 20 or more, with `overflow` given explicitly rather than defaulted, renders identically in both
positions — so a suite that never sizes a leaf below `MinUsableWidth` and never leans on the
default confirms a property the spec does not have. §10.1's blank-surface controls exist for the
same class of green.

### 2.6 Overflow — wrap or truncate, chosen per pane

Content that does not fit its pane is a routine condition, not an error, and how it resolves is
the **user's choice**, configured per pane and overridable per item.

Two levels of "does not fit" exist and must not be conflated:

- **Segment packing** — several items do not fit on one row. Existing greedy packing flows them
  onto additional rows. Every mode does this; it is not what `overflow` selects.
- **A single value wider than the pane** — no packing can help. This is what `overflow` decides.

**`overflow` values:**

| value | a segment wider than the pane inner width | row can exceed pane width? |
|---|---|---|
| `"wrap"` | hard-broken across continuation rows; nothing is lost | never |
| `"truncate"` | cut to fit, ending with the marker; the tail is lost | never |
| `"overflow"` | emitted whole, spilling past the pane | yes — legacy only |

**`"wrap"`** — the pane grows taller instead of losing text. In a vertical split a taller pane
forces its siblings to pad to the same height (§2.4), so one wrapping pane grows the whole
surface; `maxRows` is what bounds that.

**`"truncate"`** — the pane keeps its height and loses the tail of the value. The marker is
`ellipsis`, default `…`, and setting it to `""` gives a hard clip that sacrifices no cell —
which is what a very narrow pane usually wants. The marker's own width is budgeted against the
inner width, and if the inner width is not greater than the marker width the marker is dropped
rather than allowed to consume the whole pane.

**`"overflow"`** — v1's behavior, preserved for byte-parity and nothing else. It is **only
legal when the surface has exactly one pane.** Inside any split it corrupts the neighbor to its
right, so `--check` rejects it there and the renderer treats it as `"truncate"`.

**Defaults**, which are deliberately context-sensitive:

- Single root pane ⇒ `"overflow"`. This is the compatibility default and is what makes the
  §2.7 parity gate achievable at all — widths 21–24 exercise exactly this path.
- Any pane inside a split ⇒ `"truncate"`. Corrupting a neighbor is never an acceptable default;
  losing the tail of one over-long value is.

**Vertical overflow** is governed by the same marker. When wrapping produces more rows than the
pane's `maxRows` (or the surface's, §2.8), the surplus rows are dropped and the last surviving
row ends with the marker, so truncation is always visible rather than silent.

**"Ends with the marker" means the marker replaces the row's last cells, never that it is appended
to them.** The horizontal case two paragraphs up budgets the marker's width against the inner width
and says so; the vertical case said only "ends with", and appending is the reading that sentence
invites. The last surviving row is routinely a full row — it is the row that was full enough to
force the wrap being truncated — so appending puts it one to two cells over the pane width, and
§2.4's rule 1 names that exact failure as the ugliest one available: a single over-wide row shears
every column to its right, on that row only, in a way a screenshot barely shows.

So the marker is budgeted identically on both axes, with the same two riders: if the inner width is
not greater than the marker width the marker is dropped rather than allowed to consume the row, and
an `ellipsis` of `""` is a hard clip that spends no cell. One rule, applied twice — stating it
once per axis is how the two got to disagree.

**The `MinUsableWidth` single-line fallback is a property of the SURFACE, not of a pane.**

`RowLayout` emits one unwrapped, deliberately over-wide row when its available width is under
20. That is correct v1 behavior for a whole terminal too narrow to pack into, and it is the
`overflow` mode expressed at the layout level.

Inside a split it is a defect. §2.3 explicitly permits a pane to resolve below 20 columns — it
suppresses its border and keeps rendering — so a narrow pane is a routine outcome of splitting
a wide terminal, not a tiny-terminal edge case. If such a pane took the single-line fallback it
would emit one long row straight through its neighbour.

The rule:

- **Surface has exactly one pane** ⇒ the fallback applies as in v1. Parity depends on it.
- **Surface has more than one pane** ⇒ no pane ever takes the fallback. A pane under 20 columns
  packs and then wraps or truncates per its own `overflow` mode, exactly like any other pane.
  Narrow is not special; it is just small.

Consequently **the overflow mode is applied after `RowLayout` unconditionally**, never only to
rows that packed "normally". Any path that hands a row onward without measuring it against the
pane width is the bug this rule exists to prevent.

**Two implementation traps, both non-negotiable:**

1. **Break on plain text, never inside an escape sequence.** Width is measured and cut on the
   ANSI-stripped string; the markup is re-applied to each resulting piece.
2. **Style is re-emitted on every continuation row.** A hard-broken segment must carry its
   color onto each row it occupies. A style that opens on row 1 and is never reopened leaves
   rows 2+ unstyled — or worse, bleeds into the pane beside it.

#### 2.6.1 "The surface has exactly one pane" is a fact about the config, not about this render

§2.6 conditions two rules on how many panes the surface has: the single-line fallback applies with
exactly one pane and never with more, and the `overflow` default is `overflow` for a single root
pane and `truncate` for a pane inside a split. Both are right. Neither says when the count is taken,
and the count is not stable.

A split that collapses under §2.11 reduces — §2.2 says so directly, "if it reduces to a single
child, that child takes the full" extent, and §2.11 is careful that a collapsed child means `N − 1`
rather than `N` with one blank. So a two-pane surface whose second pane goes empty *is* a surface
with exactly one pane, by the only reading §2.6 offers. Take the count at render time and the
survivor's `overflow` default flips from `truncate` to `overflow` and its fallback eligibility flips
from off to on, because a sibling had nothing to say.

At `refreshInterval: 1` that is not a corner case, it is a flicker. A pane whose neighbour holds a
value that comes and goes — a git branch outside a repo, a command that returns empty on a cache
miss — switches overflow behaviour once a second. The visible symptom is a statusline that
alternates between a tidy truncated row and a row running past the surface into nothing, with no
config change and nothing to report. Worse, the over-wide row is legal only under the v1 parity
argument, and that argument is about a config that declares one pane; it says nothing about a config
that declares two and got one.

Ruled: **the pane count that decides both rules is the count the config declares, fixed when the
config is loaded, and collapse never changes it.** A surface that collapses to a single pane keeps
`truncate` and keeps the fallback disabled. Both rules keep the reason they were written for —
parity belongs to a config that asks for one pane, and "never corrupt a neighbour" belongs to a
config that has neighbours, whether or not they have anything to say this second.

This makes `allowFallback` and the resolved `overflow` mode config-load facts rather than render
facts, which is what §2.5.2 needs to be true for `allowFallback` to be a value passed down from the
root. A flag recomputed per render from a tree that changes per render is not a surface fact; it is
the leaf asking about its own position with an extra step.

### 2.7 Iteration 1 — one pane filling the statusline

Ships the entire pane machinery with exactly one leaf pane at the root, and is verified by a
single hard claim:

> **Output is byte-identical to the pre-pane build across the full width sweep**, border on and
> border off.

Any diff at any width is a compositor bug, not a behavior change, and is fixed rather than
re-baselined. Splits ship only after that claim holds — a compositor debugged against a known
output is cheap; one debugged against a moving target is not.

### 2.8 Height

`surface.maxRows` (default `8`) bounds the whole surface, and a pane's own `maxRows` bounds it
individually. Nothing about a statusline should be able to eat half the terminal because one
pane got chatty — a live risk once `overflow: "wrap"` exists, since wrapping trades width for
height. **`maxRows` is a hard ceiling: the surface never emits more rows than it allows.**

#### 2.8.1 There is no height fixpoint, and there must not be one

The obvious symmetry — a row-budget fixpoint mirroring §2.3's width fixpoint — does not work,
and reaching for it is the trap this subsection exists to close.

Width converges because every pass makes requests monotonically smaller. Height has no such
direction. The lever that reduces a wrapping pane's height is *more width*, and in a vertical
split more width for pane A is less width for pane B, which then wraps taller — and since
`height(vertical split) = max(height(children))` (§2.2), the surface may not shrink at all. Two
coupled dimensions pulling opposite ways, with no monotone quantity, is not something to iterate
to convergence once per second on the user's critical path.

So height is resolved by a **deterministic degrade ladder**: an ordered sequence of steps, each
of which *strictly* reduces row count, applied only until the budget is met. It terminates
because the ladder is finite and every rung is strictly reducing.

**"Strictly reducing" is a requirement on every rung, not an observation about the ones written so
far.** No rung may increase the surface's row count, on any input, including at its own boundary
conditions — the step that takes a pane's last item, the step that drops a pane's last row. A rung
that can grow the surface breaks the termination argument above, and it breaks it silently,
because rung 4 will clip the excess and the render will look correct. Rung 4 clipping something a
higher rung produced is not the ladder working; it is the ladder's proof failing with the symptom
suppressed. Any new rung must be checkable against this property in isolation, before the ladder
is run end to end.

1. **Measure.** Rows within budget — stop. This is the overwhelmingly common case and must cost
   nothing.
2. **Demote `wrap` to `truncate`**, one pane at a time, in **reverse declaration order**, re-measuring
   after each. Later-declared panes degrade first; the first-declared pane is the author's
   primary content and is the last thing to lose fidelity. This overrides an explicit
   `overflow: "wrap"`, which is a real surprise and is accepted deliberately — a surface that
   silently grows without bound is the worse surprise, and `maxRows` is the author stating which
   of the two they want.
3. **Drop trailing items** from the tallest pane, one at a time, re-measuring after each, using the
   existing §3.1 block ordering rather than a second priority scheme.

   **Rung 3 may take a pane's last item, and a pane it emptied renders zero content rows.** It does
   *not* fall through to the default content a pane with no declared items renders. Those are two
   different states that happen to share a representation:

   - **Declared empty** — the author wrote a pane with no items. Whatever the renderer does for that
     case is the author's request, and it is unaffected by this rule.
   - **Emptied by degradation** — the author declared items and the ladder took them, because the
     surface could not afford them. The pane asked for content and was refused. Rendering substitute
     content here answers a refusal with more rows, which is the opposite of what the rung was for.

   An implementation must therefore be able to tell the two apart. It cannot do so from the item list
   alone — both are empty — so **the ladder records which panes it emptied**, and it records this
   alongside the ladder's other per-render output rather than on the pane's own configuration record.
   Which pane the ladder emptied is a property of *this* render at *this* terminal width; the pane
   record describes what the author declared, and §5.0.1's rule applies unchanged one level down:
   data with different lifetimes must not share a record. The same reasoning governs any other
   quantity the ladder computes per pane.

   **Emptied is not collapsed.** The pane keeps its box: it still occupies its allocation and still
   draws its border, with an empty interior. §2.11's collapse rules decide whether a pane exists at
   all, and they are not reached by this path — see the emptied-by-degradation case there, which
   rules the same way and gives the reason. §2.8.2's border suppression is likewise unaffected and
   still keyed on the pane's row budget, not on whether it has content: an emptied pane whose budget
   is three rows or more draws its border around an empty interior; one under three rows suppresses
   its border, because that rule was never about what the pane contains.
4. **Clip**, as the last resort.

**The ladder is the only thing that enforces a row budget — either row budget.** §2.6 describes
pane-level `maxRows` as dropping surplus rows and marking the last survivor, which reads as a
second mechanism that fires during layout, before the ladder ever runs. It is not one. If it were,
a pane that exceeded *its own* `maxRows` would be clipped immediately — rung 4, the harshest — and
rungs 2 and 3 would never get the chance they exist for. An author who set `maxRows` on a pane
asking for a bounded pane would receive the most destructive available degrade, while an author who
set it on the surface received the gentlest, from the same key meaning the same thing one level up.
§2.6's paragraph describes **what rung 4 does when it fires**, not when it fires.

**Ties break by reverse declaration order, at every rung.** Rung 3 says "the tallest pane" and two
panes are routinely equally tall — this is a vertical split, where §2.4 pads siblings to a common
height, so equal heights are the normal state rather than a coincidence. Leaving that unbroken
makes the outcome depend on enumeration order in a ladder whose stated justification is that it is
deterministic. Reverse declaration order is already rung 2's rule and already carries the argument:
the first-declared pane is the author's primary content and loses fidelity last.

**"Declaration order" here means surface-wide document order — a pre-order depth-first traversal
of the pane tree, taken in reverse.** It is a total order over every pane on the surface, not the
sibling-scoped ordering §2.3 step 4 uses inside a single split. The ladder selects one pane at a
time from the whole surface, so it routinely compares two panes with no common parent, for which a
sibling-scoped order is not merely stricter but undefined.

#### 2.8.2 Clipping must close the border

A bordered pane clipped mid-box emits a top edge and two verticals with no bottom edge. That
does not read as "truncated", it reads as "crashed" — the failure mode §7 exists to prevent, so
the ladder must not produce it.

When step 4 clips a bordered pane, the **last emitted row becomes that pane's bottom edge**,
replacing the content row that would otherwise have occupied it. The box always closes. The
`ellipsis` marker goes on the last surviving *content* row, so a clipped surface still never
looks like a complete one.

Clipped rows remain subject to §2.4: every emitted row is exactly `COLUMNS - chromeReserve`
display columns. Degrading height never licenses a ragged row.

**Below three rows a bordered box cannot close, so the border is suppressed rather than clipped.**
A bordered pane spends one row on its top edge and one on its bottom, leaving `budget - 2` for
content; at a budget of 2 that is a box containing nothing, which §2.4 already refuses, and at 1 it
is a top edge alone — precisely the "crashed" render this subsection exists to prevent, produced by
the rung written to prevent it. So: **a pane whose row budget is under 3 suppresses its own border
and spends the whole budget on content.**

This is the height-axis twin of §2.3's `MinUsableWidth` suppression, and it inherits that rule's
qualification for the same reason: suppression is for a pane squeezed by a budget it did not
choose. A pane that declared its own `maxRows` under 3 asked for this shape and keeps its border,
losing content instead — the author named the number, exactly as §2.4 reasons about `fixed` and
`percent` panes. Suppression applies where the *surface* budget, or the ladder acting on it, is
what pushed the pane under three rows.

#### 2.8.3 A pane may shrink-wrap its height

`height(vertical split) = max(height(children))` (§2.2), and every pane fills its band. A pane
with two rows of content beside a pane with three therefore draws a three-row box with one row
of nothing inside it. `valign` does not help: it positions content *within* the box, so it
relocates the blank row rather than removing it.

A pane may instead declare **`height: "content"`** (default `"fill"`), and its border box closes
immediately under its last content row. The band rows it does not occupy are surface background,
outside any border.

The vocabulary is deliberately the same as §2.3's width `size` — `content` means "as tall as
what is in me", `fill` means "take the band" — because it is the same question asked on the
other axis, and a second spelling for one idea is §1's rule being broken for the sake of a
prettier word.

**`valign` keeps its meaning and gains a second subject.** Under `fill` it places the content in
the box; under `content` the box is exactly the content, so `valign` places the *box* in the
band. One concept — where does the short thing sit inside the tall space — and no new knob.

Three things this does not do, each worth stating because each is a plausible expectation:

- **It does not make the statusline shorter.** Total rows are still `max` over the children; the
  left pane's three rows still cost three rows. This changes where border glyphs are drawn, not
  how much vertical space the surface occupies. Anyone reaching for it to save a row wants
  §2.3's `distribute: "min-rows"` instead.
- **It does not interact with the degrade ladder.** `maxRows` bounds the surface; shrink-wrapping
  reduces no pane's content and so can never bring a surface inside a budget it was outside.
- **It is not the empty-pane rule.** §2.4 collapses a pane with *no* content. This is a pane with
  *less* content than its siblings, which is the ordinary case rather than the degenerate one.

**This must be authored into §2.10, not bolted onto it afterwards.** Under the shared-edge model
two adjacent panes share one column of vertical edge; when one box is shorter, that column is
shared for part of its run and belongs solely to the taller pane for the rest, and the junction
where the shorter box closes against it is a glyph case the grid does not otherwise produce.
That is tractable — the glyph table gains rows — but a border grid designed on the assumption
that every column is a full-height rectangle will not accommodate it later without being redone.

### 2.9 The worked example — two panes, an anchor and an absorber

This section is cited nine times elsewhere in this document and, until now, did not exist. Not
loosely cited: §2.3 defers the definition of wrap-aware re-measurement to it, §2.3.1's `R` is
computed "exactly as §2.9 computes it", §10 asks for a test asserting the sibling's final inner
width "(§2.9)", and §11 states outright that **"Acceptance is §2.9"** for a whole phase. A phase's
acceptance criterion pointed at a section number that resolved to nothing, and the prose read
perfectly well without it, which is exactly why it survived nine citations.

A section number is a reference of the same kind as a diagnostic code, and §9.6.1's rule for those
transfers unchanged: **a section that is not there does not exist, no matter how many places cite
it.** The content was always here — it was the tail of §2.8, where nothing pointed.

The user's stated next test, and what "splits work" means concretely:

> *Left pane: my current statusline. Right pane: the model, effort, thinking and context
> segments, with the pane border and the model text sharing one colour.*

```json
{
  "surface": {
    "maxRows": 8,
    "pane": {
      "split": "vertical",
      "gutter": 1,
      "children": [
        { "size": "fill", "overflow": "wrap",
          "border": { "enabled": true, "color": "grey" } },
        { "size": "content", "overflow": "wrap",
          "border": { "enabled": true, "color": "@model-accent" },
          "items": [ { "item": "model", "color": "@model-accent" },
                     { "item": "effort" }, { "item": "thinking" }, { "item": "context" } ] }
      ]
    }
  }
}
```

**The model pane is the anchor; the statusline pane absorbs.** The right pane is `content`, so
it declares its own width from the text it must draw. The left pane is `fill` with
`overflow: "wrap"`, so it takes whatever remains and reflows into it. Nothing in this config
names a column count — change the model name or the terminal width and the layout re-derives
itself.

The left pane omits `items`, so it gets the default list — the 14 default-set builtins (§8), i.e.
today's statusline, reflowed to whatever width the anchor leaves it.

**The arithmetic at `COLUMNS=112`**, as the shipped build actually resolves it:

```
surface   = 112 - 3 (chromeReserve)             = 109
           - 1 (gutter)                         = 108
right     = content: its 4 items unwrapped on one row + borderReserve 4 = 66
left      = fill: 108 - 66 = 42  → inner = 42 - 4 = 38
```

The anchor asks for its whole unwrapped row, gets it, and the statusline reflows into the 38
that remain. **This is the case that motivates `distribute` (§2.3).** Both panes are individually
correct — `content` got exactly what it asked for, `fill` got the remainder — and the surface is
still wrong: the right pane spends 66 columns on one row while the left pane wraps into six.
A layout can satisfy every declared size and still be the wrong layout, because the quantity the
reader cares about is total rows.

**The same config at `COLUMNS=60`**, which is where the §2.3 cap earns its place:

```
surface   = 60 - 3 - 1 (gutter)                          = 56
floor(left) = MinUsableWidth 20 + borderReserve 4        = 24      (it is `fill`, so it has one)
pass 1    right asks 66; cap = rem 56 - reserve 24       = 32      (§2.3 step 4)
          right granted 32  →  its inner = 32 - 4        = 28
```

Note what did *not* happen: right asked for 66 and a naive reading would clamp only against the
remaining 56, granting it and leaving the left pane nothing usable. It is the §2.3 step-4 cap —
`rem − reserve`, reserving the `fill` sibling's floor — that pulls the grant down to 32.
**That cap is the mechanism; there is no "surface is too tight" predicate anywhere.**

**The second pass has to free something, and for a while it did not.** Under the 28-column
inner grant the right pane wraps to three rows whose longest is 22, so it needs 26 columns, not
32. §2.3 requires it to re-report that and hand the 6 columns back to its sibling — "freed space
must reach the sibling" — and for a period the build re-reported the width it had been granted
instead, squeezing the left pane to its 20-column floor so that `jimcline/claude-tui-line`
wrapped mid-word while six columns sat unused inside the anchor.

The cause is worth recording, because it was introduced by a deletion rather than by a bug: the
degradation that used to shrink a re-measured request was the banner-to-text fallback, and when
the banner renderer was removed nothing else implemented one. The fixpoint loop survived intact
while the only thing that made it converge to something *smaller* went away, leaving machinery
that runs and cannot change its answer. **A re-measurement under a narrower grant must return the
longest wrapped row, not the grant.** That is the wrap-aware measurement `distribute` also needs,
so it is specified once, here, and used by both.

**That requirement is now implemented, and the deletion above is repaired.** `ResolveVertical`
re-measures with the grant rather than without it (`measure(child, result.Grants[i])`),
`MeasureRequest` turns the grant into an inner cap and returns `LongestWrappedRowWidth` — the
longest row the segments actually wrap to at that cap, not the cap — the result is clamped
monotonically against the previous pass's request, and the split then reallocates from the
reduced requests, bounded at three passes. So a shrinking re-measurement is once again something
the loop can produce, which is exactly what the banner-to-text fallback used to be the only
source of.

Read directly from those paths; what that reading does **not** establish is the end-to-end
number. §10 requirement 6's fixpoint tests drive the loop through `measureOverride`, so they certify the
monotone clamp and the pass cap against a stub, not that the *real* measurement frees six
columns at `COLUMNS=112`. That assertion is still owed, and §10 requirement 6's rule applies to it — assert
that the sibling gains what the anchor gives up, not that `right == 32`.

**These integers are measured, not asserted.** They come from rendering the config above at those
two widths. Tests must assert **behaviour and invariants** — the cap binds at `COLUMNS=60`, the
re-measure returns the longest wrapped row, requests are monotone non-increasing, and every
composed row is exactly the surface width — and **not** `right == 32`.

**This costs height.** The left pane packs its segments into the 38 columns the anchor leaves it
at `COLUMNS=112`, so it runs six rows; add the border and the surface is eight rows tall. That is
the real price of a vertical split at this width, and it is what `distribute: "min-rows"` exists
to reclaim.

**What this test actually proves**, and why it is the right acceptance gate: the two panes have
**different natural heights** — one row of model text against however many rows the statusline
packs into whatever the anchor leaves it. Every compositing rule in §2.4 fires at once. Ragged
padding, height mismatch, `valign`, and a per-pane border all fail visibly here and are nearly
invisible in a single-pane test.

### 2.10 Borders are a grid the compositor owns, in one of two visual languages

A border is **not a property of a pane**. It is a property of the **boundary**, and the
compositor is the only thing that draws one. **Panes stop drawing boxes**: a pane renders
*content only*, into its own inner rectangle, and the compositor overlays a single resolved
**border grid** across the finished surface. This is not a new authority — it is §2.4's existing
rule ("the compositor is the sole authority on the surface") finally applied to borders too, and
it deletes the per-pane border-wrapping path along with the re-padding defence that path needed.

That much is unconditional. What is a choice is whether a boundary between two siblings is drawn
as **one line or two** — and both are legitimate visual languages, so the framework offers both
rather than ruling one out:

```json
"border": { "collapse": false }
```

- **`"collapse": false` (default)** — *separate boxes*. Each pane's edges are its own; adjacent
  panes are two complete boxes with the gutter between them. **This is what ships today**, and
  it is the default because a change of visual language is not something a framework should
  perform on an existing config during an upgrade.
- **`"collapse": true`** — *shared edges*. Adjacent edges resolve to one physical line that both
  panes touch. This is CSS's `border-collapse: collapse`, which is where the name comes from.

**The reason to collapse is width, not taste.** With gutter `g`, a separate boundary spends
`g + 2` columns — A's right edge, the gutter, B's left edge — while a collapsed one spends
exactly `1`. Every interior boundary hands back `g + 1` columns to content. On a statusline at
`COLUMNS=112` that is the difference between a pane wrapping and not, which is why this is a
config knob and not a stylesheet.

Everything below — per-edge selection, the junction table, the reserve decomposition, the
degrade order — applies to **both** models. Only the collapsing rule and the arithmetic differ.

**Per-edge selection.** Each pane declares which of its four edges it wants:

```json
"border": { "edges": { "top": true, "right": false, "bottom": true, "left": true },
            "color": "grey", "style": "rounded" }
```

with Excel-style shorthands, which are the friendly form and expand to exactly the above:

```
"border": "all"      every edge of every pane, dividers included
"border": "outline"  the outer boundary of this pane/split only, no interior dividers
"border": "inside"   interior dividers only, no outer boundary
"border": "none"
```

**Collapsing** applies only under `collapse: true`. A physical line is drawn if **any** adjacent
pane asks for it. Where two panes disagree about colour or style, the **first requester in tree
declaration order wins** — chosen because it is deterministic and explainable, not because it is
clever. `--check` (§9) reports every conflict it resolves, since a silently-dropped colour is
exactly the kind of thing a user will otherwise spend an evening on.

Under `collapse: false` no conflict can arise, because neighbours never contend for one line:
each pane's edges are drawn as it declared them, in its own colour and style. That is a second
reason it is the safe default — the mode that needs a tie-break rule is the one the user opts
into.

**Junctions.** At each grid position, the glyph is a pure function of which of the four
directions carry a line. Implement it as one 16-entry table per style, keyed by the `NESW`
neighbour mask — `0b0011 -> ┐`, `0b1111 -> ┼`, `0b0000 -> ` ` `, and so on. Never a chain of
special cases; the table generalises to any nesting depth for free, and a rounded style simply
supplies rounded glyphs for the four corner masks.

**Sizing.** `borderReserve` was a constant 4 that quietly conflated two different things: the
edge glyphs and the content padding. Split them.

```
reserve(p)  =  (edges p is charged for)  +  padLeft + padRight
```

Under `collapse: true` a shared edge is charged **once, to the split**, never twice to the two
neighbours — that double-charge is the whole bug this section exists to avoid. So for a vertical
split with `N` children and interior dividers on:

```
collapse: true    avail = splitInnerWidth − (N − 1)              // one column per boundary
```

**`collapse: false` is not a parent-side boundary formula at all, and an earlier draft of this
section was wrong to write one.** It said `avail = splitInnerWidth − (N − 1) × (g + 2)` and
claimed that was the arithmetic in force. It is neither.

It is not what the code does. `SizeResolver` charges the split's own border once to the split,
subtracts a flat `gutter × (N − 1)`, and lets each child's own border cost ride inside that
child's recursive floor and request. Border reserve is **per pane**, not per boundary.

And it does not add up. Under `collapse: false` the panes are separate boxes, so `N` children own
`2N` edge columns; `(N − 1) × (g + 2)` charges only `2(N − 1)` of them. It drops the outermost
pair — the leftmost child's left edge and the rightmost child's right edge — and under-reserves by
exactly 2 columns at every `N`. A surface built on it emits rows two columns too wide, which is
the §2.4 ragged-row violation, arriving from the sizing model rather than from a rendering bug.

So: **under `collapse: false` the boundary cost is the gutters alone, `gutter × (N − 1)`, and each
pane's edges are its own `reserve(p)`, accounted where that pane is measured.** That is what the
renderer already does. This section introduces one new formula — the `collapse: true` line above —
and changes nothing else about §2.3.

**Nothing outside `SizeResolver` may restate this arithmetic, including this document.** The
formula above is here to explain the model, not to be transcribed: the boundary cost lives in one
named function that the renderer calls, and §9.8's structural checks call **that same function**
rather than a copy. Two expressions of one arithmetic is how the draft above came to be wrong
without anything noticing, and it is the §1 rule with the serial numbers filed off — when
`collapse` lands, the renderer changes and a checker holding its own copy silently does not.

Under `collapse: true` the divider **occupies the gutter**: `gutter` must be ≥ 1 and defaults to
1; a `gutter` greater than 1 centres the line with blanks either side (`  │  `). Everything else
in §2.3 — the floor table, the six-step allocation, the fixpoint — is untouched and takes the
per-pane `reserve(p)` wherever it currently reads `borderReserve`.

**`height: "content"` is cheap under `collapse: false` and expensive under `collapse: true`.**
Separate boxes are independent, so a pane whose box closes early introduces no new glyph case at
all — its neighbour's edges are unaffected because they were never shared. Collapsed edges are
where the difficulty lives: a shorter box makes one column shared for part of its run and solely
the taller pane's for the rest, plus a junction where the short box closes against it. The
practical consequence is a sequencing one — **`height: "content"` can ship against the default
model without waiting for collapsing**, and only its collapsed-mode junctions need the grid.

**Edges are static config, and this is load-bearing.** Colours may be derived from a provider's
value (§6); edges may **never** be. An edge has extent, so a value-derived edge would put a
provider's output inside the sizing loop, and the §2.3 fixpoint's convergence argument does not
survive that. Static edges are known before sizing begins, which is precisely why this whole
feature needs no new sizing machinery.

**Degrade.** §2.3's narrow-width suppression gets sharper here: a squeezed pane drops its
**vertical** edges first, because columns are what is scarce and horizontals cost none. Losing
one line beats losing the whole box.

**Back-compatibility.** A surface with a single pane has no neighbours, so nothing collapses and
its four outer edges render exactly as today. The golden parity gate covers only the
no-`surface` single-pane config and is therefore unaffected by this section — if it moves, that
is a defect in the reserve decomposition, not an expected consequence.

#### 2.10.1 Five things the above does not say, ruled

Walking §2.10 over cases turned up five gaps, four of them in the §7.1 class — the config is
valid, `--check` is silent, and the surface renders something plausible and wrong.

**1. `outline` and `inside` are subtree instructions, not this node's four booleans.** The
shorthands are introduced as expanding "to exactly the above", meaning the declaring pane's
`{top,right,bottom,left}`. That is true of `all` and `none` and false of the other two: "no
interior dividers" and "interior dividers only" describe a *split's descendants*, which no
number of booleans on the split itself can express. Read literally, `"border": "outline"` on a
split sets the split's own four edges and leaves every child bordered as before — the outer box
plus every interior line, the exact opposite of what was asked, with nothing to report it. So:

- On a **split**, `outline` turns the split's own four edges on and every descendant's off;
  `inside` turns the split's own four off and interior dividers on.
- On a **leaf**, `outline` ≡ `all` and `inside` ≡ `none`, because a leaf has no interior.
  `inside` on a leaf silences the pane's border and is almost never meant: warning
  `border-inside-on-leaf` (§9.6.1).
- **A descendant's own explicit `border` overrides what an ancestor's shorthand set for it** —
  nearest declaration wins. Without this, `outline` is a mute an author cannot locally escape.

**2. `collapse` is a surface-level key and nothing else.**

```json
"surface": { "border": { "collapse": true } }
```

§2.10 makes the compositor overlay **one** resolved border grid. One grid cannot be half
collapsed: the boundary between a `collapse: true` subtree and a `collapse: false` sibling has no
defined width — one column by the first rule, `g + 2` by the second — and the sizing model has no
way to ask which. Accepting the key on a pane and quietly ignoring it is the §7.1 failure in its
purest form, so it is an **error**, code `collapse-not-surface-level`, not a silent ignore.

**3. The degrade rule does not work under `collapse: true`.** "A squeezed pane drops its vertical
edges first" assumes the edge is the pane's own column. Under collapsing it is not: the line is
drawn if **any** adjacent pane asks for it, so one pane dropping its right edge frees exactly zero
columns while the neighbour still wants its left. The sizing model would credit a column it never
recovered, and a row comes out wide — §2.4's ragged-row violation, arriving from the degrade step.

Under `collapse: true`, therefore, **degrade operates on boundaries, not on panes**: it drops an
interior divider entire, freeing exactly the 1 column that divider costs, outermost edges last.
Under `collapse: false` the existing per-pane rule stands untouched, because there a pane's
vertical edge really is its own column.

**4. A junction can have arms in more than one style.** "One 16-entry table per style" presumes a
single style at the position, but the tie-break resolves colour and style **per edge**, so four
arms may arrive from four panes carrying three styles. Ruling: the junction glyph comes from the
style of the **first requester in tree declaration order among the arms present at that
position** — the same tie-break as the edge itself, applied to the junction as if it were one more
participant. One rule rather than two, and `collapsed-edge-conflict` already reports that the
disagreement happened.

**5. `reserve(p)` is width-only, and horizontal edges cost rows.** Once edges are selectable
individually, `top` and `bottom` are no longer a pair that exists or does not — and the reserve
decomposition above says nothing about rows, while §2.8 reasons throughout about a box that
"closes under its last content row", written when both horizontals were always present. Name the
counterpart:

```
rowReserve(p)  =  (top ? 1 : 0) + (bottom ? 1 : 0)
```

under the same regime as `reserve(p)` — one named function, no transcription (see the paragraph
above). §2.8's `height: "content"` shrink-wrap adds `rowReserve(p)` to the pane's content row
count. §2.8's worked example, which spends `borderReserve 4` inline, stays correct only for the
all-edges case and is to be read as `reserve(p)` for that pane rather than as a constant.

#### 2.10.2 The grid is resolved per cell, and that is what makes `height: "content"` tractable

Authored in response to §2.8.3's standing requirement that the shrink-wrap interaction "must be
authored into §2.10, not bolted on afterwards."

§2.10 and §2.10.1 already rule most of this feature: the authority shift to the compositor, the
two visual languages, per-edge selection with subtree-scoped shorthands, the 16-entry junction
table, the `reserve(p)`/`rowReserve(p)` decomposition, both sizing formulas, both degrade rules,
and the per-edge tie-break. What follows does not revisit any of that. It closes the one gap
that stands between §2.10 as written and §2.8.3 as merged, and it names two things the section
assumes without stating.

**1. The ruling: the grid is a per-cell structure.** The border grid is resolved per cell. For
each `(row, column)` position on the finished surface, a 4-bit `NESW` mask records which of the
four directions carry a line *at that position*. The mask is a property of the cell — not of a
pane, not of an edge, and, load-bearing: not of a column run. §2.10's junction table is then keyed
by that per-cell mask exactly as already written, unchanged.

This resolves §2.8.3's concern, and corrects its diagnosis on one point. §2.8.3 says the closing
junction of a shorter box is "a glyph case the grid does not otherwise produce." That is not true:
a 16-entry table over a 4-bit mask produces all sixteen cases by construction, and the closing
junction is mask `0b1101` (`┤`) or `0b0111` (`├`) — both already in the table, both already
reachable from ordinary nesting. Nothing needs to be added to the table.

What §2.8.3 correctly identified is the representation hazard underneath. A border grid stored as
*edge runs* — "column 34 carries a vertical line for the height of this split" — cannot express a
column that is shared for part of its run and solely one pane's for the rest, the "designed on the
assumption that every column is a full-height rectangle" failure §2.8.3 warns will "not accommodate
it later without being redone." Per-cell resolution simply does not have the failure mode. The cost
is one `int` (or one byte) per surface cell, on a surface that is at most a few hundred columns by
a handful of rows. This sentence is the whole of §2.8.3's requirement on §2.10: once the grid is
per-cell, shrink-wrapped heights need no further accommodation in the border model.

**2. Construction: from panes to masks.** Under `collapse: true` the compositor builds the grid
once per render, after sizing and before drawing. Each pane's border box occupies a rectangle of
surface cells: columns from the pane's resolved outer rectangle (§2.3); rows spanning the full band
under `height: "fill"`, or under `height: "content"` (§2.8.3), the rows the box occupies *after*
`valign` has placed the box within the band — §2.8.3 is explicit that under `content`, `valign`
positions the box rather than the content inside it. Each edge a pane declares (§2.10's per-edge
selection, after §2.10.1 rule 1's shorthand expansion and nearest-declaration override) contributes
one run over its box rectangle: `left`/`right` contribute vertical runs down the box's first/last
column; `top`/`bottom` contribute horizontal runs across the box's first/last row. For a vertical
run in column `c` spanning rows `r0..r1`, set at each row `r` in that range: bit `S` if `r < r1`,
bit `N` if `r > r0`. For a horizontal run in row `r` spanning columns `c0..c1`, set at each column
`c`: bit `E` if `c < c1`, bit `W` if `c > c0`.

**One step precedes the OR, and only under `collapse: true`.** For each interior boundary — a
column shared by two horizontally-adjacent panes, or a row shared by two vertically-adjacent
panes — collect the runs its contributors supply along that boundary and replace them with a
single run spanning their convex hull: from the lowest `r0` to the highest `r1` (or `c0`/`c1` on
the horizontal axis). Accumulate masks from that hull run, not from the individual contributions.
This is the step that fills an interior gap between two shrink-wrapped neighbours (§3), and it is
the only place the hull rule enters the construction. A pane's own outer edges, and every edge
under `collapse: false`, are accumulated directly from their contributions with no hull step —
they have no co-contributor to take a hull with.

The mask at a cell is the bitwise OR of every contribution landing on it (after the hull
substitution above, where it applies) — the direct encoding of §2.10's "a physical line is drawn
if **any** adjacent pane asks for it," applied per cell rather than per boundary. It also produces corners
with no corner special-case: a box's top-left cell receives `S` from its left edge's run (it is
that run's `r0`) and `E` from its top edge's run (it is that run's `c0`), giving mask `0b0110` →
`┌`. Under `collapse: false` no grid is built at all — panes are separate boxes, each rendering its
own edges in its own colour and style, which is what `PaneBorderRenderer` already does. The
per-cell grid is machinery that exists only for the collapsed model; the default path never
constructs it.

**3. A shared column whose neighbours are different heights.** The vertical run in a shared column
is the **convex hull** of the contributing panes' runs. §2.10's "any adjacent pane asks for it"
decides *whether* a boundary carries a line and *who* contributes to it; the hull decides *how far*
that line runs. The two are separate questions and the paragraph below settles the second.
With a two-row pane beside a three-row pane under `collapse: true`: over the rows both boxes
occupy, the column is a shared edge and §2.10.1 rule 4's tie-break decides its style and colour;
over the rows only the taller box occupies, the line is still drawn as that pane's own right edge,
uncontended; at the row where the shorter box closes, its `bottom` run terminates against the
vertical, and that cell accumulates `N`+`S` from the vertical and `W` (or `E`) from the horizontal,
giving `┤` (or `├`) straight out of the table. **Interior gaps are filled; the ends are not extended.** Two `content`-height neighbours with
opposing `valign` leave a band of rows that neither box occupies. The shared column is drawn
continuously across that gap: the vertical run in a shared column spans from the first row any
contributing box occupies to the last, with interior holes filled. The rule stated as one line:
**the run is the convex hull of its contributors' extents.** Fill holes, never extend ends.

**What the line means over a row where no box exists.** §2.10's first sentence already answers it:
*"A border is not a property of a pane. It is a property of the boundary."* Under `collapse: true`
the shared column is not either neighbour's edge — §2.10's sizing rule charges it once to the
split, never to the two panes, precisely because it belongs to the boundary. A boundary separates
two panes' allocated regions, and those regions span the band whether or not the boxes inside them
are full height. So at a gap row the line is the boundary doing exactly what it does at every other
row: separating the region on its left from the region on its right. Nothing about that lapses
because a box happened to close early.

**Why the ends are still not extended.** A border segment must be anchored at both ends by a
corner or a junction. Filling an interior hole keeps both ends anchored — there is a box above it
and a box below it. Extending past the outermost contributor produces a line terminating in open
space with no glyph to close it, which reads as a rendering artifact rather than as structure.
Concretely: two `content` panes both occupying rows 1–3 of a 7-row band share a column over rows
1–3 and not over rows 4–7, because there is no second anchor down there. This is the case that
distinguishes the convex hull from "the boundary spans the split," and the hull is the rule.

**This does not contradict §2.8.3.** That section rules that band rows a shrink-wrapped box does
not occupy are "surface background, outside any border" — a statement about the pane's own box,
governing its interior and its own four edges. A shared boundary is not the pane's border, so
§2.8.3 does not reach it. The unoccupied rows remain background inside each pane's region; the
boundary between the regions is the compositor's, and it is continuous.

**Colour and style over a filled gap.** §4.1 below resolves a shared cell's colour by first
requester among the arms present at that cell. At a filled gap row no arm is present, so that
tie-break has no input and would be undefined. Ruling: a filled span takes the colour and style of
the first requester in tree declaration order among the boundary's contributors — resolved once
per boundary from the contributor set, not per cell from the arms. A filled gap therefore never
introduces a colour change of its own; it continues whichever contributor the boundary already
resolved to.

**A filled gap is not a conflict.** `collapsed-edge-conflict` reports disagreement between panes
that both asked for a line. No pane asked for the gap row, so filling it reports nothing.

**4. Two things §2.10 assumes without stating.**

*Colour resolves per cell, and the diagnostic does not.* §2.10.1 rule 4 resolves the junction
glyph's style per position; the same must hold for colour, for the same reason — a shared column
that changes owner partway down (§3 above) may legitimately change colour partway down, since over
the unshared rows it is unambiguously the taller pane's edge in the taller pane's colour. But
`collapsed-edge-conflict` (§9.6.1) is reported once per conflicting boundary, not once per cell: a
three-row disagreement between two panes is one authoring mistake and must produce one warning.
Reporting per cell turns a single mis-declared colour into a burst of identical diagnostics.

*The word `collapse` is already taken in this codebase.* `src/ClaudeTuiLine/PaneCollapse.cs`
implements §2.4/§2.11/§2.11.1/§2.11.2 — *pane* collapse, the pre-pass that prunes structurally
empty panes before sizing. It is unrelated to `border.collapse`, which shares edges between boxes.
The border-grid implementation must not live in `PaneCollapse.cs`, and must not introduce a type
or method named `Collapse` unqualified. Name the new machinery for what it is — `BorderGrid`,
`EdgeMask`, `BorderGrid.Resolve` — and reserve bare `Collapse` for the existing pane-pruning
pre-pass.

**5. Glyph coverage.** §2.10 specifies one 16-entry table per style, with five styles registered at
`Config.cs:662-670` (`rounded`, `square`, `heavy`, `double`, `ascii`), all five sourced from
Spectre.Console's `BoxBorder`. `PaneBorderRenderer.cs` reads exactly eight parts from it: the four
corners and the four edges. Confirmed (Spectre.Console 0.57.2): `BoxBorderPart` has only these
eight corner/edge members — no `Cross`/`TeeLeft`/`TeeRight`/`TeeTop`/`TeeBottom`. The five 16-entry
junction tables therefore cannot be derived from `BoxBorder` and must be hand-authored in this
repo, per style — a materially larger unit of work than "call `GetPart` with more arguments," and
it lands inside #8c (see §6).

**6. Scope: three tasks, not one.** §2.10 plus §2.10.1 is a rewrite of the border architecture, not
a feature addition: it deletes the per-pane border-wrapping path, moves border drawing to the
compositor, replaces a constant with two functions, adds a config sub-object with subtree-scoped
shorthands and an override rule, adds five hand-authored glyph tables, adds a second sizing
formula, adds a second degrade rule, and adds three diagnostic codes. Split, each independently
shippable:

- **#8a — reserve decomposition, no new config.** Replace the `BorderReserve = 4` constant
  (`PaneBorderRenderer.cs:16`, read from `SizeResolver.cs:69`, `PaneTreeRenderer.cs:24`,
  `Program.cs:199`, `Program.cs:759`) with the named `reserve(p)`/`rowReserve(p)` functions §2.10
  and §2.10.1 rule 5 require. Every pane still has all four edges, so every rendered byte must be
  identical — the golden parity gate is the acceptance test, and §2.10's back-compat paragraph is
  explicit that if it moves, that is a defect in the decomposition, not an expected consequence.
  Prerequisite for both others.
- **#8b — per-edge `edges` config, still `collapse: false`.** The `edges` object, the four
  shorthands, §2.10.1 rule 1's subtree semantics and nearest-declaration override, and the
  `border-inside-on-leaf` warning. Panes still draw their own boxes, so no grid or junction table
  is needed. `rowReserve(p)` from #8a becomes load-bearing here, since `top`/`bottom` stop being a
  pair.
- **#8c — the compositor grid and `collapse: true`.** The per-cell grid of §1–§3 above, the five
  junction tables (§5), the `collapse: true` sizing formula, boundary-based degrade (§2.10.1 rule
  3), `collapsed-edge-conflict`, `collapse-not-surface-level`, and deletion of the per-pane
  wrapping path. This is where §2.8.3's shrink-wrap interaction lands.

The ordering is forced: #8b and #8c both consume #8a's functions, and #8c consumes #8b's edge
selection. `collapse: false` being the default means nothing user-visible changes until #8c.

**What must not change:** the default stays `collapse: false`; §2.10's junction table stays 16
entries keyed by `NESW` (per-cell resolution is what makes the existing sixteen sufficient, nothing
above adds a glyph case); no arithmetic is transcribed — boundary cost lives in the one named
`SizeResolver` function, and §9.8's structural checks call that function, never a second expression
of §2.3's allocation; `collapse: false`'s boundary cost stays `gutter × (N − 1)` with each pane's
edges in its own `reserve(p)`; the golden parity gate does not move across #8a or #8b for the
all-edges single-pane config; §2.11 pane collapse is untouched — `PaneCollapse.cs` keeps its
current meaning and name.

##### Verification

1. **Per-cell grid.** A config with two `collapse: true` siblings, left pane `height: "content"`
   with two rows, right pane three rows — assert the shared column carries a vertical glyph on all
   three rows and a `┤` at the row where the left box closes.
2. **Junction moves with `valign`.** Same config with the shorter pane `valign: "bottom"` — assert
   the `├`/`┤` junction appears at the shorter box's top row instead, and the glyph above it is the
   taller pane's plain vertical.
3. **Interior gap is filled.** Left pane `content`/`valign: "top"` occupying rows 1–3, right pane
   `content`/`valign: "bottom"` occupying rows 5–7, in a 7-row band under `collapse: true`. Assert
   the shared column carries a vertical glyph on all seven rows including row 4, that row 4's glyph
   is the plain vertical for the resolved style, and that row 4 carries the same colour as rows
   1–3.
4. **Ends are not extended.** Both panes `content`/`valign: "top"` occupying rows 1–3 of a 7-row
   band. Assert the shared column carries glyphs on rows 1–3 only, and that rows 4–7 of that column
   are blank. This is the case that fails if the boundary is implemented as spanning the split's
   full inner height rather than as the convex hull of its contributors.
5. **Mask coverage.** A unit test that feeds all 16 masks to each of the five style tables and
   asserts a non-empty glyph for the fifteen non-zero masks and a space for `0b0000`.
6. **Corner derivation.** A lone bordered pane's four corner cells resolve to masks `0b0110` /
   `0b0011` / `0b1100` / `0b1001` and render the same four glyphs the current `PaneBorderRenderer`
   emits for that style.
7. **Diagnostic granularity.** Two `collapse: true` panes disagreeing on a shared edge's colour over
   three rows produce exactly one `collapsed-edge-conflict`.
8. **`collapse-not-surface-level`.** `collapse` on a pane rather than on `surface` is an error, not
   a warning and not a silent ignore.
9. **#8a acceptance.** Golden parity output is byte-identical before and after the reserve
   decomposition.
10. **Naming.** No type or method named bare `Collapse` is introduced outside `PaneCollapse.cs`.

### 2.11 An empty pane collapses — but only the kind of empty that is knowable before sizing

§2.4 already rules this: an empty `content` or `fill` pane collapses and takes its gutter with it;
an empty `fixed` or `percent` pane keeps its extent and its border, because the author named a
number and reserving declared space is often the intent. **That division stands and this section
does not touch it** — at `refreshInterval: 1` a fixed pane that collapsed and re-expanded as you
moved in and out of a repository would make the whole line jump once a second, and `content` is
already how an author asks for the other behaviour.

Defect 12 is that the renderer does not implement §2.4's half of it: a `content`-sized pane
holding nothing still draws its border, so a surface split into "git things" and "model things"
shows an empty box eating twenty columns the moment you `cd` out of a repository. The box is not
the bug; claiming the width is.

What §2.4 leaves unsaid is **which** emptiness qualifies, and that is the part with a trap in it.

**A pane becomes empty two ways, and conflating them does not terminate.**

- **Structurally empty** — `items: []`, or every item in it resolved to no value. §5 resolves
  every value *before* sizing begins, so this is knowable in a pre-pass, from the resolved
  dictionary alone, with no width in hand.
- **Emptied by degradation** — §2.8's ladder dropped its trailing items, or §2.3's over-constrained
  loop dropped the pane's contents to make the surface fit. This is a *function of the width* and
  is only knowable inside the sizing loop.

**Collapse applies to the first and never to the second**, and the reason is convergence rather
than taste. Collapsing a degradation-emptied pane frees its width; the freed width lets §2.8
un-drop the items it just dropped; the un-dropped items un-empty the pane; the pane reclaims its
width; the surface is over budget again and the ladder drops them once more. That is a cycle with
no fixpoint, and §2.3's convergence argument — every rung strictly reducing — is exactly what it
breaks. A pane the ladder emptied keeps its border, and renders zero content rows rather than the
content a pane with no declared items renders — §2.8.1's rung 3 states that rule and the reason
for it. If the right answer there is to remove the pane entirely, that is a rung the ladder should
own, decided with the width in hand, not a side-effect of a collapse rule reaching into the loop.

**What collapsing does**, for the structural case, on the `content` and `fill` panes §2.4 makes
eligible:

- The pane occupies **no width** and draws **no border**.
- **The parent drops the corresponding boundary with it.** §2.10's cost is a function of the child
  count, so a collapsed child means `N − 1`, not `N` with one child rendered blank. Leaving the
  gutter or divider behind is how a collapse turns into a visible seam with nothing on either side
  of it.
- **A split whose children all collapse collapses itself**, resolved bottom-up in the same pre-pass.
  One pass suffices because emptiness only ever propagates upward.
- **If the root collapses, the surface emits nothing** — zero rows, not an empty box and not a
  blank line. There is genuinely nothing to say, and §7's exit-0-with-valid-stdout still holds.

**Only `items: []` is a diagnostic, and the runtime case is not.** §9.4 warns about a pane with no
items because an author wrote an empty pane and probably did not mean to. A pane whose items are
all valueless outside a repository is working exactly as designed, gets no diagnostic, and could
not get one anyway: `--check` does not resolve values (§9.8), so the two cases are not even
distinguishable from where the validator stands.

#### 2.11.1 An explicit `minSize` suppresses collapse

**Two sections currently answer this differently, which is worse than either being wrong.**
§2.3's floor table reads `p.minSize set -> p.minSize (author said so; always wins)`. The rule
above says a collapsed pane "occupies no width". For a `content` pane with `minSize: 12` holding
nothing, one says twelve columns and the other says zero, and a reader consulting either comes
away confident.

**`minSize` wins, and collapse does not apply.** The pane keeps its floor and its border. Three
reasons, in order of weight:

- It is §2.4's own test. A `fixed` pane keeps its extent because "the author named a number and
  reserving declared space is often the intent." `minSize` is a number the author named. The
  distinction §2.4 draws is not really `content`-versus-`fixed`, it is *declared extent versus
  inferred extent*, and `minSize` is declared.
- It is the only vocabulary for "size to content, but hold this much." Let collapse override it
  and that sentence becomes inexpressible — the author's remaining option is `fixed`, which gives
  up content sizing entirely.
- It inverts safely. To get collapse, omit `minSize`; there is no way to be surprised into a pane
  that vanishes, only into one that stays.

**A follow-on: §9.4's `pane-no-items` warning must not fire on these.** Its stated rationale is
"it collapses, so the declaration did nothing" — which is false for a `content`/`fill` pane with
an explicit `minSize`, since that pane holds its space and is a legitimate spacer for exactly the
reason `fixed` and `percent` are. The registry row in §9.6.1 is amended accordingly. A diagnostic
whose reason has stopped applying is a false positive that teaches authors to ignore the checker.

#### 2.11.2 An item that did not answer is not an empty item

§5 resolves values before sizing, so the pre-pass sees one thing: a value or no value. But a
`command` item (§4) reaches no-value by two different roads — it ran and returned nothing, or it
**timed out, exited nonzero, or was killed** at the 150 ms budget (§7). The rule above collapses
the pane either way, and at `refreshInterval: 1` that means a pane that vanishes and returns as a
script drifts across its timeout: the whole line jumping once a second, which is the precise
failure §2.11 invokes two paragraphs earlier as the reason `fixed` panes do not collapse. It came
back in through the door this section had just closed.

So the resolver distinguishes **absent** — resolved, no value — from **unavailable** — did not
answer. **A pane holding an unavailable item does not collapse for that render.** It keeps its
extent and its border and renders whatever else it has, possibly nothing.

In §7.1's terms: "the tool did not answer in time" is not the author saying this pane is empty. A
layout that reshapes on a 150 ms timeout is present, plausible, wrong, and has nobody to report
it — the user sees a statusline that twitches and no reason anywhere for why.

This distinction is not new machinery. §5's TTL cache already has to know the difference to decide
what is cacheable, and §9.4.1's two-tier severity already separates "reported nothing" from
"failed". What is new is that the collapse pre-pass must read it.

#### 2.11.3 The config-error fallback pane is never reached — collapse has nothing to do with it

`ConfigResolution.LoadRenderConfig` (`ConfigResolution.cs:15-58`) has three places that need the
fallback pane, and each is a distinct failure:

- an asserted `--config` (or a config found by the §5 search order) that fails to parse
  (`ConfigResolution.cs:24-28`);
- an asserted `--config` naming a file that does not exist (`ConfigResolution.cs:34`);
- the config parsed fine but `ConfigLoader.ResolveTopLevel`/`ResolveRootPane` threw while resolving
  it (`ConfigResolution.cs:45-57`) — SPEC-47 §3's addition; the first two already reported a reason
  before SPEC-47, this third one previously did not.

All three return through `ConfigResolution.FallbackResult` (`:63-67`), whose `reason` parameter is
required, not optional (SPEC-47 §4.3): there is no way to obtain the fallback pane built by
`BuildFallbackConfig` (`:103-122`, still `Array.Empty<PaneItem>()`, still structurally empty — that
is unchanged) without also supplying a diagnostic reason for it, and `BuildFallbackConfig` itself is
private to `ConfigResolution` — nothing outside this file can reach it any other way.

`Program.cs:40-45` reads that reason before any rendering happens: if it is non-null, one row is
printed (`ConfigUnreadableMessage.Format`) and the function returns 0 — nothing below it, including
the collapse pre-pass and the renderer, ever runs. Because all three call sites above go through
`FallbackResult`, the fallback pane's reason is never null, so this short-circuit fires on every
path that produces it.

**So the fallback pane's silence is not a property of §2.11's collapse rule, and does not need to
be.** It never reaches collapse to be judged empty or held open; `Program.cs:40-45` returns before
the pane is ever handed to the renderer. §2.11's collapse pre-pass has no special case for it and
needs none — nothing here is an exemption to that section's convergence reasoning.

**What this does not do.** The fallback pane does not carry the diagnostic. It cannot: its `Items`
are `Array.Empty<PaneItem>()`, unchanged from before, and it is never rendered regardless. The one
row a reader sees is printed by `Program.cs:43`, above and outside the pane entirely. The guarantee
is not "a single mechanism protects the fallback pane" — it is the conjunction of three separate
call sites each being unable to skip `FallbackResult`'s required reason, plus one short-circuit that
reads whatever reason results. Losing any one of the three call sites' coupling to `FallbackResult`
would silently reopen this defect for that path alone; the others would still be safe.

## 3. Item model

An item resolves to a **block**: zero or more rows, **plus a state**. One row is the ordinary case
and is what every v1 segment is. More than one row is a user-defined item (§4) whose command
emitted multiple lines.

This generalization is worth taking now rather than later: a one-row-only item model would have
to be unwound the first time a user's script prints two lines.

```
StatusItemDefinition
    Id           string          stable key, used in config and cache
    Provider     provider        how the VALUE is obtained (§4)
    Format       string          "{}" placeholder, e.g. "ctx:{}%" — default "{}"
    Color        string          Spectre color name, or a threshold rule (§6)
    Overflow     string          optional per-item override of the pane's §2.6 mode
```

**The state is not decoration on the block; it is the half of the model §2.11.2 reads.** An
earlier version of this section said "zero rows means suppressed" and stopped there, which gives
an item that answered with nothing and an item that *did not answer* the identical
representation — and §4 distinguishes those two precisely because §2.11.2's collapse rule must
not fire on a timeout. Written as "zero rows", the distinction is erased by the item model before
the compositor is ever in a position to honour it, and no amount of care downstream can recover
it.

So a resolved item carries `present` | `absent` | `unavailable`, matching §4's vocabulary exactly
rather than paraphrasing it. **A `present` block with zero rows is a contradiction and must not be
constructible** — that is the invariant which keeps the third state from silently becoming
optional.

**Two fields were removed from the struct above rather than specified, because neither exists and
neither should.**

- `Align left|center|right` was listed as "within the pane". No such key is accepted:
  `PaneItemJsonConfig` has no `align`, and `PaneAssembler` aligns whole rows using the *pane's*
  `align`. The document has been advertising an item-level capability that a config cannot
  express and the renderer does not implement — the `color207` failure again, where what is
  recommended silently does nothing. It is also incoherent as specified: items are packed
  several-to-a-row (§3.1), and three items sharing one row cannot each have their own alignment
  within the pane. Alignment of a **block that occupies its own rows** would be meaningful and
  is available if it is ever wanted; that is a different feature from the one this line claimed.
- `Enabled bool` was a second mechanism for "do not render this item", where one already exists
  and is simpler: do not place it. §1 forbids exactly this. If temporarily disabling an item
  without deleting its config is ever wanted, it is an authoring affordance belonging to §12's
  `edit`, not a field in the item model.

Both are worth removing rather than quietly leaving: a struct in a spec is read as the set of
things that work.

The provider is the only axis that distinguishes one item from another. Everything downstream —
formatting, colouring, packing, wrapping — is identical for a builtin and for a user's shell
script, which is what makes a user-defined item a first-class item rather than a bolt-on.

**A provider takes one `ItemContext`, never a growing parameter list.** The context carries the
session payload plus environment values probed lazily and memoized **for this render** — git
branch, remote URL, and whatever the next item needs. Memoization within the process is the floor,
not the design: §5.1 caches these probes across renders in the same store the `command` items use,
and this sentence says "for this render" rather than "for the process" so that the two sections
are not describing the same mechanism with different lifetimes. This is the §1 rule applied to the registry's own
signature: threading each new input as its own parameter means the Nth item that needs a new
probe re-edits all N rows, which is precisely the cost the registry exists to remove. The
signature widens once and then never again.

Laziness is not a nicety here. `refreshInterval: 1` makes startup cost the render cost, so a
probe that spawns a subprocess must run only when an item actually asks for it, and at most once
per render.

### 3.1 Blocks and packing

Row packing (§2.6) operates on single-row items. A multi-row block **occupies its own rows**:
it never shares a row with a neighbouring item and never has items packed beside it. A pane's
content is therefore a vertical sequence of packed single-row groups and standalone blocks, in
config order.

Panes also gain `valign` (`top` | `middle` | `bottom`, default `top`), which decides where the
content sits when a pane is shorter than its siblings — the padding from §2.4 goes below, split,
or above accordingly. Without it, a 1-row model pane beside a 6-row statusline sits awkwardly at
the top of its box — visibly so in §2.9.

**Packing runs before wrapping, and the order is not interchangeable.** "Row packing operates on
single-row items" and "a multi-row block occupies its own rows" together leave open which is
which for an item that is single-row *until the pane wraps it*. The two readings produce visibly
different statuslines: pack-then-wrap fills a row with three items and flows the overflow onto
continuation rows; wrap-then-pack makes any item long enough to wrap into a block, so those three
items stop sharing a row at all, and the pane's whole shape changes because one value grew.

Pack first. An item's block count is a property of **what the provider returned** — one line or
several — never of the width it was later granted, so wrapping cannot promote an item to a block.
This is also what §2.6's traps already assume when they require every continuation row of a styled
segment to carry the style: continuation rows exist inside a packed row, which is only true in
this order.

### 3.2 Hyperlinks

An item may carry a `link` whose value is a URL template. The item's text is emitted wrapped in
an OSC 8 hyperlink, so a terminal that supports them makes it clickable and one that does not
shows the text unchanged.

```json
{ "item": "git-branch", "link": "{remote-url}/tree/{}" }
```

`{}` is the item's own value; `{other-id}` is another item's resolved value. No second lookup
mechanism — the same registry that resolves items resolves these.

**A referenced item does not have to be displayed, and this is the normal case rather than an
edge case.** The example above is exactly it: `remote-url` is a 40-plus-column URL that no one
wants occupying a statusline, and the entire point of referencing it is to make the short branch
name clickable instead. If `{other-id}` resolved only against items already placed in a pane,
the feature would work solely in the configuration nobody wants, and the example printed above
would not work at all. Resolution goes to the registry, which does not know or care what is on
screen.

`remote-url` is not the only link base available, and not always the cheapest one. For linking
`repo` specifically, `repo-host` (§9.6.2.1) resolves the host straight from the session payload —
`{ "item": "repo", "link": "https://{repo-host}/{}" }` — with no subprocess at all, where
`{remote-url}` costs a lazily-probed, 30-second-cached `git remote get-url`. The two can
legitimately disagree (a fork whose origin is the fork while the harness reports the upstream), so
this is not "prefer `repo-host` always" — it is the answer when the base and the item it decorates
are both `repo`.

A referenced id that names nothing must be distinguishable from one that names a real item,
because silently dropping the link makes a typo and a working config produce identical output.
`--check` **reports** a `link` template naming an unknown id — as a `warning`, per §9.4.1, since
the item still renders its text and only the decoration is lost. At render time the link is
dropped per the best-effort rule below, but that path is the fallback, not the diagnosis.

**Zero-width is the whole problem.** `\e]8;;URL\e\\text\e]8;;\e\\` costs no columns and is
roughly 40–80 characters, so anything that measures it as text puts the pane border that many
columns early and §2.4's rectangle invariant breaks. Three rules, none optional:

1. **`Plain` carries the link text only; `Markup` carries the sequence.** This is the split that
   already keeps SGR out of the width metric (§6); hyperlinks join it rather than getting a
   parallel mechanism.
2. **OSC is not CSI and must be scanned separately.** A CSI scan ends at the first letter; an
   OSC 8 runs to a string terminator — `\e\\` or BEL. Scanning an OSC as a CSI stops inside the
   URL and counts its tail as visible text. §10's existing "a hard break never lands inside an
   escape sequence" was written and tested against SGR only and does **not** cover this.
3. **A wrapped or truncated link closes itself.** A continuation row re-opens the hyperlink, and
   a truncated one emits the closing `\e]8;;\e\\` before the ellipsis. An unterminated OSC 8
   leaks: the terminal keeps hyperlinking subsequent output, including the user's next prompt.

**Derived items** (§4.3) cover the case where the thing to link is not an item but a fragment of
one — which is the motivating case for them, though not the only one:

```json
{ "id": "issue", "from": "git-branch", "extract": "[A-Za-z]{2,}-[0-9]+", "case": "upper",
  "link": "https://linear.app/example/issue/{}", "color": "blue" }
```

An empty match suppresses the item under the existing missing-field rule (§3), so a branch with no
issue id in it produces no link rather than a link to nothing.

`remote-url` is a new registry row: origin normalized to https, `git@host:path` rewritten,
`.git` stripped. It probes via `git remote get-url origin` rather than reading git config
directly, so `insteadOf` rewrites are honored. It is a value like any other, so it is linkable,
formattable, and testable on its own rather than being logic buried inside the branch item.

**Its cost follows the reference, not the placement, and that needs saying out loud.** Resolution
is demand-driven — §5 resolves exactly the ids in the resolution set — so `remote-url` costs
nothing at all until something names it. But once a `link` template names it, the subprocess runs
**every render**, which at `refreshInterval: 1` is a `git` process per second, whether or not the
URL is ever shown. That is the feature working as designed: the whole point of the worked example
is to reference it without displaying it. It does mean "opt-in because it shells out" is a weaker
guard than it looks, since a `link` opts in on the user's behalf without the id appearing in any
pane. The obligation this creates is on the provider, not the config: **a provider whose cost is a
subprocess caches within a render and should cache across renders under a TTL**, exactly as a
`command` item must (§4). An item is not entitled to spawn a process per second because a
decoration referenced it.

**A raw OSC 8 emitted by a `command` provider (§4) is measured the same way**, since a user's
script can emit one directly — which is exactly how this defect was found. `Segment.Plain` is
contracted escape-free, so sanitizing belongs where Plain is built from raw command output, not
at the measurement sites. Preserving the script's own colour and links in `Markup` is desirable
but secondary: correct width first, fidelity if the markup path can carry raw ANSI without a
fight.

Preserving that fidelity brings rule 3's leak with it in a second form. A script that opens an
SGR and never resets bleeds into every segment after it, exactly as an unterminated OSC 8 bleeds
into the user's next prompt. **A segment built from raw command text terminates its own styling
regardless of what the script emitted.**

The strip helper's OSC scan has two ways to be wrong in opposite directions. Its terminator is
`\e\\` **or** BEL, and missing BEL leaves a shell script's link counted as text; and it must be
non-greedy, since two OSC sequences on one row otherwise collapse into one match that swallows
the visible text between them and measures the row far too narrow.

#### 3.2.1 Resolved questions

- **`case`** is `upper` or `lower`. An unrecognized value passes through unchanged today — an
  author who writes `"case": "title"` gets the raw value and no signal, which is the
  silent-acceptance flaw the config-diagnostics work owns. Not fixed here.

  This used to cite `"auto"` resolving to `fill` as the parallel case. It is not one: `auto` is a
  *recognised* value with a defined meaning (§2.2), now a deprecated alias reported as
  `deprecated-size-alias`. The two are opposites — one is a legal spelling of something real, the
  other is a value with no meaning at all — and §7.1 rules them in opposite directions, so citing
  one as an example of the other pointed the fix the wrong way.
- **A `{other-id}` that does not resolve drops the link, not the item.** The text still renders,
  plainly. The missing-field rule governs an item's own `{}` value; a decoration's unmet
  dependency must not delete information.
- **A truncated linked segment closes the link before the ellipsis and keeps its colour.** The
  ellipsis is the pane's artifact, not part of the target; clicking `…` must never navigate.

#### 3.2.2 The renderer's restyle path is the hazard

`PaneRenderer.TryGetSimpleWrap` matches only markup ending in exactly `[/]`. Wrapping coloured
markup in an OSC 8 open/close breaks that match, and the restyle then degrades to unstyled plain
on any wrap or truncate of an oversized linked-and-coloured segment — dropping the colour **and**
the link, with no error. A link-aware restyle that detects, strips, and reapplies the OSC 8
wrapper around the existing SGR logic makes rule 3's per-row re-open fall out of wrapping for
free; truncation still needs its own branch so the closer precedes the ellipsis.

The test that matters here asserts the link *survives* a wrap of a coloured segment. A row that
is merely the right width passes even when both the colour and the link have been silently
thrown away.

### 3.3 Compound items — several sources, several colours, one item

`format` decorates one value and `color` paints all of it, so a label cannot be dimmer than the
value it labels. Splitting the label into its own item does not work either: the separator is a
*between-items* construct, so `agent:` and `ORCHESTRATOR` come out as `agent: | ORCHESTRATOR`.
There is no way to say "these fragments are one thing" while colouring them differently.

A **compound item** declares `parts` instead of a value, and renders them concatenated with
**no separator between them**:

```json
{ "id": "agent-badge", "parts": [
    { "text": "agent:", "color": "grey" },
    { "from": "agent", "extract": "[^:]+$", "case": "upper", "color": "aqua" }
] }
```

A part carries the same vocabulary a pane item already carries — `extract`, `case`, `format`,
`color` — because a part *is* an item fragment, and inventing a second vocabulary for it would
put two spellings on one behaviour. What a part adds is exactly one source, and it must be
exactly one of:

- `text` — a literal
- `item` — a registry or `command` id, rendered as that item renders. Not a compound id: a
  part naming another compound is the nesting this section forbids (`part-compound-source`,
  §9.6.1)
- `from` — a derived value, per §3.2's derivation rules

**This is not a second rendering path.** A compound resolves to the same thing every item
resolves to: one `Segment`, one `Plain` string that is the concatenation of the parts, and
markup carrying a colour change per part. `Segment` already holds multiple styled spans — that
capability is what builtins use to colour `62%` inside `ctx:62% (125k/200k)`. Compounds expose
it to config; they do not add machinery. §4.1's `match` + `colors` compiles to the same span
list, and must keep doing so: `match` stays the better surface for carving *one* string, `parts`
is the general form for composing *several*, and both produce one list of `(text, colour)` spans
that the renderer cannot tell apart.

Rules, each closing a silent failure:

- **Width is unaffected.** `Plain` is the concatenation; markup never contributes width. Defect
  0's invariant is untouched, and this is the reason compounds are safe to add to a layout
  engine that sizes by measurement.
- **`truncate` cuts by `Plain` across the concatenation**, and the surviving spans keep their
  markup — including closing any span the cut lands inside. A truncation that severs a colour
  span mid-way and emits an unclosed SGR bleeds colour into the border. This is the one
  genuinely new implementation hazard here and needs its own test.
- **A compound whose value spans more than one line loses per-part colour.** The block-layout
  path (§3.1) renders each line as its own single-colour `Segment`, not through the span
  decomposition above — an accepted, documented limitation, not a silent one.
- **A literal is bound to its adjacent values and disappears with them.** A literal part is
  dropped when **any value part adjacent to it resolved to empty**, evaluated against the
  *original* array positions rather than against what earlier removals left behind. `agent:` does
  not survive an absent agent, and ` ✓` does not survive an absent PR. This is §4.1's "the literal
  text bound to it goes too", with array adjacency doing the job the format string's adjacency
  does there.

  > This rule previously read "the one after it if there is one, otherwise the one before", which
  > left a hole in the failure it claims to close. A value wrapped in a literal pair —
  > `[{"text":"("},{"from":"pr"},{"text":")"}]` — drops `)` and keeps `(`, so an absent PR renders
  > a bare `(` on the statusline. That is the **render-wrong** class, the same one defect 14 sits
  > in: not an absence the user can notice, but visible output that is simply incorrect, with no
  > diagnostic because nothing failed. Looking both ways is also the simpler implementation, since
  > it never needs to know whether a *following* literal exists in order to decide about a
  > preceding one.
  >
  > Evaluating against original positions is what keeps this order-independent. Deciding each
  > literal against the array as earlier drops have already mutated it would make the result depend
  > on traversal direction, which is the same objection this section raises against nesting.
- **If every value part is empty, the item renders nothing** and collapses per §2.4. A compound
  of only literals is a constant, which is legal and occasionally what someone wants.
- **Item-level `color` is the default** for parts that do not set one.
- **A part may not contain `parts`.** One level, for §3.2.1's reason: nesting makes resolution
  order observable, and an order-dependent config is one whose behaviour depends on how the
  parser happened to walk it.
- **A part may not carry `link`.** `link` stays at item level and wraps the whole compound.
  Per-part links mean nesting OSC 8 inside a styled span, which is §3.2.2's restyle hazard with
  more edges; it is deferred deliberately rather than overlooked.
- **Semantic colour precedence is unchanged.** A part naming a semantic item keeps its
  value-derived threshold colour unless that part sets `color` — §6's rule, applied one level in.
  That authority extends one level further out: when another item selects this compound and sets
  its own `color`, that colour is a floor only — it fills parts with no colour of their own and
  never overrides a part's explicit or threshold colour (SPEC-87-compound-reference-resolution.md,
  colour-precedence ruling).
- **`--check` rejects** a part with zero or more than one source (`part-source-count`), a part
  containing `parts` or `link` (`part-forbidden-key`), a part naming an unknown id, and an unknown
  colour. A part that names nothing is not an empty part; it is a config the author got wrong.

  The last two reuse the existing `unknown-item-id` and `unknown-color` codes rather than taking
  part-specific ones — it is the same condition in a new position, and the JSON Pointer already
  says where. The codes are named here rather than left to the implementation because §9.6 fixes
  a code's meaning permanently once it ships, so an ad-hoc name chosen mid-build is a name the
  project keeps. All four are `error` severity: zero-or-many sources and a forbidden key have no
  defined meaning, which is §9.4's line.

**This is the sixth construct that names an item by id**, after pane `items`, colour-token
`from`, derived `from`, link `{other-id}`, and §4.2's argv placeholders. §5's resolution set must
enumerate it, and defect 11 is what happens when it does not.

## 4. Providers

Two kinds in v2. The provider is the *only* thing that differs between a built-in item and a
user-defined one — everything downstream is identical.

**`builtin`** — a function of the parsed stdin JSON plus render context (git branch, engram
telemetry), each registered as one row in a single static table keyed by id.

The registry is the ONLY place the set of builtins is enumerated. No second list anywhere —
not in tests, not in config validation, not in docs generation, **and not in this paragraph**.
What follows is orientation for a reader, not a definition: `directory`, `git-branch`, `repo`,
`worktree`, `pr`, `model`, `effort`, `thinking`, `output-style`, `context`, `rate-limits`,
`agent`, `engram`, `vim`, plus two that render only where explicitly configured — `model-short`,
a shortened form for narrow anchor panes, and `remote-url` (§5.1), which shells out to git.
`--items` is the authority; if it and this list disagree, this list is the one that is wrong.

**Membership in the default list is a `default` flag on the row, not a count.** The rows carrying
it are exactly the captured segments, so adding a row without the flag — which is what both
opt-ins are — extends the framework without touching the parity baseline. Do not restate the
size of either set anywhere: §8 gets its default list by reading the flag, `--items` reports the
flag per row, and a number written into prose is a second registry that goes stale on the next
row. That the default set and the captured set happen to be the same size as each other is a
fact about today's rows, not a rule, and nothing may be derived from it.

**Every registry row carries a default `format`, and it is the only place an item's display
text is constructed.** A row resolves a *raw value* (`62`, `high`, `approved`) and a `format`
that turns it into display text (`ctx:62% (125k/200k)`, `effort:high`, `PR #42`). This is the
same `format` a `command` item declares below and the same one a `{ "item": "<id>" }` entry may
override (§8) — one mechanism, not a builtin-only shortcut.

Both are load-bearing, because the raw value and the display text have different consumers:
colour rules read the **raw value**, since `from: "context"` with numeric thresholds needs `62`
and not `ctx:62% (125k/200k)`, while `contains: "Sonnet"` matches the model string itself. The
surface renders the **display text**. Resolving one from the other at use time is what keeps
them from drifting.

**A row's two outputs, and how a composite item supplies them.** Most rows produce display text
by applying a format string to the raw value — `"effort:{}"`, `"PR {}"`, `"[{}]"`, or plain `{}`
where the value is already display-ready. Some items cannot: `context` displays
`ctx:62% (125k/200k)`, where the parenthetical is built from two further fields and appears only
when both are present, while its raw value must stay the bare number `62` so a numeric threshold
rule (§6.4) can compare against it. No format string can wrap data that was never passed through
it, and none can express a conditional.

So a row supplies **either** a format string applied to the value, **or** a text function with
access to the full resolved input — never both, and the registry exposes a single accessor that
hides which one a row used. This is not a second display mechanism competing with the first: the
behaviour is "how item X displays", exactly one implementation of it lives in item X's row, and
both the default list and an explicit `items` array call the same accessor. Format strings are
the common case, not the rule; a text function is the general case, not an escape hatch.

**The accessor returns markup, not plain text.** Splitting display into "text from the registry
row, colour from somewhere else" reintroduces the exact duplication this section exists to remove
— one path colours, the other does not, and the plain text matches so a text-only equivalence
test reports success. Some items colour *within* themselves: `ctx:62% (125k/200k)` colours only
`62%`, and `5h:30% / 7d:85%` colours each window independently against its own threshold. That
granularity is finer than any item-level colour expression can express, it is part of the
captured baseline, and it must survive. So a row's accessor yields both `Plain` and `Markup`
(§2.4 keeps `Plain` as the sole width metric), and an item rendered from an explicit `items`
array is coloured identically to the same item in the default list.

An item-level `color` from config (§6) applies on top, and where an item colours its own
fragments the config colour governs the parts the item did not claim. A config colour never
silently discards an item's internal threshold colouring.

**Thresholds are one evaluator with two callers.** §6.4's numeric comparison is a single
implementation, used both by a config-declared `thresholds` token and by an item's own internal
rule. That — not relocating the 50/80 rule into config — is what "expressed *through* this
mechanism" requires: moving the rule out of the item would flatten per-fragment colouring and
break parity, which is a worse outcome than the duplication it would remove.

For a `from: "rate-limits"` rule the raw value is the **maximum across the windows** (`85` for
`5h:30% / 7d:85%`), since a threshold on usage means the most-constraining limit. Its display
text keeps both windows. An item whose raw value cannot be a single number cannot be a numeric
threshold source, and `--check` reports that rather than the rule silently never firing.

A per-item `format` in config (§8) always overrides to apply to the **raw value**, replacing the
row's default text entirely — **including the row's own internal markup**. A format string
restructures the text, and per-fragment tags are bound to the structure it replaces, so they
cannot survive it. `{ "item": "context", "format": "ctx:{}%" }` yields plain text carrying only
the item-level `color`, if any. This is the predictable reading: an override replaces, it never
merges. For a composite item that means the user trades the richer default
away — `{ "item": "context", "format": "ctx:{}%" }` renders `ctx:62%` and loses the
`(125k/200k)`. That is the correct behaviour and it is predictable: `{}` is always the raw value,
in every position, for every item.

Note what this rules out. "Let the configured form be narrower than the default form" is not
available, because the default list renders through this same accessor — a narrower configured
`context` would render a narrower `context` in the default statusline too, and break golden
parity against the captured bash output. The parity baseline is not a preference here; it is what
makes the unification checkable.

The consequence that matters: **the default list is not a separate rendering path.** A leaf with
no `items` renders the default-set builtins (§8) through exactly the code an explicit `items`
array uses, with
the same formats. If a configured `{ "item": "context" }` and the default list can produce
different text for the same item, there are two display implementations and the framework is
being bypassed by its own baseline — which also means the golden parity test is guarding a path
the framework does not use, and is no longer evidence about the framework at all.

**`command`** — an external process producing one line on stdout.

```json
{
  "id": "k8s",
  "command": ["kubectl", "config", "current-context"],
  "format": "k8s:{}",
  "color": "cyan",
  "ttlSeconds": 30,
  "timeoutMs": 150
}
```

- **Argv array, not a shell string.** `["python3", "/path/thing.py"]`. This is the default and
  the documented form: no quoting rules, no word splitting, no injection surface.
- **`shell: true` is in scope, not optional.** Pipes are a normal thing to want in a statusline
  script, and telling a user to wrap their one-liner in a file is the kind of friction that
  makes a framework unused. The rule: `command` is an **array** normally; when `shell: true` is
  set, `command` is a **string** run as `sh -c "<string>"`. Both forms accepted, decided by the
  `shell` flag rather than by sniffing the JSON type. Everything downstream
  — stdin, env, cwd, timeout, cache key, stale-on-failure — is identical for both forms.

  **The JSON type is not itself the fault, and `--check` must not report it as one.**
  `CommandJsonConverter` normalizes both forms to a single argv list, so `"command": "date"` and
  `"command": ["date"]` are the same value by the time anything runs, and both execute correctly.
  A diagnostic on the *shape* would fire on a config that works — which §9.4 forbids more
  strongly than it requires any diagnostic at all.

  The config that cannot work is narrower, and it survives normalization: **an argv of exactly
  one element whose text contains whitespace, with `shell` not `true`**. That is
  `"command": "git status"` — normalized to `["git status"]` and exec'd as a binary literally
  named `git status`, which does not exist. `["git status"]` written directly is the identical
  value and the identical failure, so the check is on the **value**, not on the source shape,
  and catching both is correct rather than a false positive. `--check` reports it as an
  **`error`**, code `command-shape`: exec fails, §7 suppresses the item, and §9.4.1's test puts
  a suppressed item in the deleted bucket.

  Checking the value is also what keeps this a check-only change. The converter discards the
  source shape deliberately — its own comment: *"so `PaneItemJsonConfig.Command` stays one plain
  type end to end"* — and neither reviving that shape through a provenance field nor walking the
  raw JSON a second time is warranted. Both recover a fact the check does not need.

  **The mirror-image fault is real and worse.** `CommandProvider.RunAsync` passes `command[0]`
  and nothing else to `sh -c`, so `shell: true` with `["echo", "hi"]` runs `sh -c "echo"` and
  **silently discards every element after the first**. The item renders — it just renders the
  output of a command the author did not write, with no error anywhere. `--check` reports an
  array of **more than one element** under `shell: true` as an **`error`**, code
  `command-shell-argv`.

  This one also changes the **render** path, which no other diagnostic in this section does.
  §7's contract is a pair: a bad config yields nothing at render time, and `--check` says why.
  Rendering the *wrong output* is outside that pair — it is the single outcome the design has no
  answer for, because the user gets no signal at all, not even an absence to notice. So
  `CommandProvider` suppresses the item rather than spawning a command it knows is missing
  arguments, which puts the fault back inside §7 where `--check` can explain it. `shell: true`
  with `["kubectl", "config", "current-context"]` is an easy config to write by cargo-culting the
  flag, and silently returning bare `kubectl`'s usage text is a bad way to find that out.

  Two conditions, two codes, because the fixes differ: `command-shape` is repaired by adding
  `shell: true` or by splitting the string into real argv, `command-shell-argv` by joining the
  array into one string or by dropping the `shell` flag. Codes are cheap and §9.6 makes their
  meanings permanent, so a consumer that wants to distinguish these should not have to parse
  a message to do it.

  The element count is load-bearing: a **single**-element array under `shell: true` is exactly
  what the string form normalizes to, so it is correct and must not be reported. Only `> 1` is
  detectable as a fault, and only `> 1` loses anything.
- **stdin**: the command receives the *same session JSON* Claude Code sent us, verbatim. This
  is what makes user scripts first-class — they see everything the builtins see.
- **env**: `COLUMNS` is exported and `CLAUDE_TUI_LINE_ITEM_ID` is set to the item id.
  `CLAUDE_TUI_LINE_PANE_WIDTH` carries the inner width of the pane the item lives in, so a
  script can size its own output — but note the circularity, because as first written this
  variable was not implementable. Values are resolved *before* sizing (§5), so at spawn time no
  pane has a width yet; and for a `content` pane the demand is backwards anyway, since the item
  determines the pane's width rather than the reverse. The resolution:
  - The variable carries the pane's inner width **from the previous render**, recorded in the
    §5.0.1 widths store at render time and read back on the next spawn. A statusline redraws
    every second and layouts are stable, so this is correct at every tick where the terminal
    width has not just changed.
  - **The record lives in `widths/`, not in the cache entry**, and §5.0.1 is the reason: the
    value cache is machine-wide and pane width is per-terminal, so putting them in one record
    hands each concurrent session the other's width once a second. The store is keyed by the
    cache key *and* `COLUMNS`, which is also what makes the resize case fall out correctly —
    a new terminal width is a new key with no record yet, so the tick after a resize reports
    **absent** rather than confidently reporting the old width.
  - It is **absent on the first render**, absent for the first tick at a new terminal width, and
    absent for items in a **`content`-sized pane**. A script must treat it as optional and behave
    sensibly without it. Absent is a real state a script can detect and fall back from; a
    plausible wrong number is not, which is why nothing here ever substitutes a guess.
  - Omitting it for `content` panes is what makes this safe rather than merely stale: it
    removes any path where a script's output width feeds the width it is told about. A `fill`,
    percent, or fixed pane absorbs its remainder independently of its own content, so no
    self-feedback exists there.
  - It is advisory. Pane-level `overflow` (§2.6) is the authoritative mechanism for content
    that does not fit, and it works whether or not a script consulted this variable.
- **cwd**: the session's `.cwd`, so `git`-flavored commands behave as the user expects.
- **Output**: stdout with the trailing newline stripped. Each line becomes one row of the item's
  block (§3.1), optionally capped at `maxLines` (§4.0.1, no cap by default); excess lines are
  dropped.

  > **Not built, and this paragraph said otherwise in the present tense for as long as it has
  > existed.** `CommandProvider` takes `stdout.Split('\n', 2)[0]` — the first line, unconditionally
  > — and no `maxLines` key exists anywhere in the config types. A `command` item is single-line
  > today. Everything in this bullet about multiple rows and a cap is a design, not a description,
  > and §9.8.2 rules that the note below cannot be the thing that carries `--preview`'s note
  > channel, because a note needs a producer. Found by the implementor going to read the mechanism
  > this text told them to reuse — which is the only way this class is ever found, since the
  > document was perfectly self-consistent about it. §4.0.1 settles what the cap is for and what
  > it does when nobody sets one; this bullet previously said "default 4", which §4.0.1 overturns.

  **`--preview` reports the drop as a render note** (§9.8.1), naming the
  item and the cap — not `--check`, which never runs the command and therefore cannot know
  (§9.1.1). The note goes to stderr in the human form and to `notes[]` in the JSON form; stdout
  stays byte-comparable either way, so `/migrate` can diff a preview against the original script's
  output.

  ANSI in the output is passed through but its width is measured stripped, so a script may color
  itself.

  **Empty output ⇒ the item is `absent`. A timeout, a nonzero exit, or a kill ⇒ the item is
  `unavailable`.** These are not the same outcome and §2.11.2 is why: the collapse pre-pass reads
  the distinction, so a pane whose only item timed out keeps its extent instead of vanishing and
  returning once a second as a script drifts across its budget. An earlier draft of this bullet
  read "nonzero exit ⇒ treated as empty", which is the collapse-on-timeout behaviour §2.11.2
  exists to forbid, written into the section that produces the value. §7 still governs what the
  *user sees* for both — nothing — but what the layout is told differs.

  *This previously read "first line of stdout", which contradicted §3's block model — §3 defines
  a multi-row block as "a user-defined item whose command emitted multiple lines", and §3.1
  already specifies how such a block packs. Only one of the two could be true; the block model
  is the one the rest of the spec is built on, so the single-line reading is the one that goes.*

#### 4.0.1 `maxLines` bounds an item against its siblings, not the surface against a runaway script

The bullet above described a cap that has never existed, in the present tense, with a stated
default of `4`. Two things have to be settled before it can be built, and neither of them is the
truncation: what the cap is *for*, and what it does when nobody sets one.

**It is not what stops a script flooding the surface.** That was the bullet's stated rationale, and
it puts a second truncation policy on the same axis as §2.6, which is already "the authoritative
mechanism for content that does not fit". Two mechanisms answering one question from different
inputs is defect 15's shape exactly: the row budget comes from §2.8's ladder and the boundary
behaviour from §2.6, and an item-level cap that also removes rows means a row can vanish for two
unrelated reasons that nothing reconciles.

The surface is already bounded. A `fill`, percent, or fixed pane resolves its row budget without
reference to its content, so a script emitting ten thousand lines costs exactly the rows that pane
was going to spend anyway. The one place that argument fails is a `height: "content"` pane (§2.8),
whose height *is* its content — there the bound that would have contained the runaway is derived
from the thing that ran away. **That gap is §2.8's to close, as a maximum on shrink-wrap growth,
and it is a pane key.** A per-item cap could not close it regardless: three items capped at four
lines each still grow a content pane to twelve rows.

**What it is for is fairness inside a pane.** Items share a pane's rows (§3.1), so an item that
suddenly emits forty lines does not overflow the surface — it evicts its siblings from a budget
they were sharing. That failure is real, it is genuinely per-item, and nothing else in this
document addresses it. So `maxLines` survives, on the item, with that as its job.

**Default: no cap.** `4` was written into prose and never measured against anything. More than
that, a default cap is *silent* truncation on the render path, because the render path has no note
channel — §9.8.1's notes belong to `--preview`. A user with a legitimate five-line item would see
four rows, permanently, with the framework's only explanation reachable exclusively by someone who
already suspected it. An always-on producer whose notification exists only under a diagnostic flag
is §9.8.2's defect arriving from the other side: there the channel had no producer, here the
producer would have no channel.

So the cap fires only where a user asked for one, and a `maxLines` note therefore always names a
number that user typed. That is what makes the note actionable at all — §12.3's advice to raise the
cap presumes a cap the reader can find in their own config.

**Ordering.** The cap applies at the provider, before §3.1 packs the block, so everything
downstream sees a block already the length it will render at. Drops are reported through the
collector of §9.8.2, which makes this the channel's first real producer — and is why §9.8.2 ships
the channel with `--preview` rather than waiting for this feature, and why this feature arrives
with its own note rather than the two being built as one.

`--check` still never reports it (§9.1.1): a cap is a property of output, and only running the
command produces output.

### 4.1 User-defined items are first-class, and that is the extension point

A `command` item is not a lesser thing that gets placed where a builtin would go. Its `id` is
registered in the same namespace, so it may be referenced anywhere a builtin id may be: as
`{ "item": "<id>" }` in a pane, as `from:` in a colour token (§6.3), and as `{other-id}` inside
a `link` template (§3.2). **This is the answer to "there is no builtin for what I want"** — the
user, or a model acting for them, defines one and it behaves identically from that point on.

If it did not work this way the builtin list would be a ceiling, and §1's rule would be doing the
opposite of its job: a registry only the project can add rows to is a hardcoded list with extra
steps.

#### Colouring parts of one item

`"color"` paints the whole item. Some items need more than one colour inside them — the case that
forces this is a diff stat, where additions and deletions being the same colour defeats the point
of showing them. §4 already establishes that builtins colour their own fragments
(`ctx:62% (125k/200k)` colours only the `62%`); what was missing was any way for a *user-defined*
item to do the same without hand-writing escape sequences into a shell script.

A command item may declare a `match` — a regex with **named groups** — splitting its output into
named parts that `format` positions and `colors` paints:

```json
{
  "id": "git-diff-stat",
  "command": ["git", "diff", "--shortstat"],
  "match": "(?:(?<added>\\d+) insertion)?.*?(?:(?<removed>\\d+) deletion)?",
  "format": "+{added} -{removed}",
  "colors": { "added": "green", "removed": "red" },
  "ttlSeconds": 5
}
```

Keeping the colour in config rather than in the script is what makes it checkable: the names are
validated, the colours resolve through §6 like any other colour, and the user can recolour
without editing a shell command. A script that prefers to emit its own ANSI still may — that path
is unchanged — but it is then opaque to `--check` and cannot be recoloured from config.

Rules, each closing a way this could otherwise fail silently:

- **`{}` still means the whole matched output.** `match` adds names; it does not withdraw the
  default. An item with a `match` and a `format` of `{}` renders exactly as it would without one.
- **A group that did not participate renders empty, and the literal text bound to it goes too.**
  `+{added}` contributes nothing when `added` is unset, rather than rendering a bare `+`. This is
  §7's rule — a missing field renders nothing, never `null` — applied one level down.
- **A regex that does not match suppresses the item**, exactly as empty output does. Not "fall
  back to the raw text": a failed `match` means the output was not the shape the config claimed,
  and rendering past that is how a statusline displays something wrong with full confidence.
- **`--check` validates the cross-references, not just the syntax** — that the regex compiles,
  and that every `{name}` in `format` and every key in `colors` names a group that exists in it.
  A `colors` key with no matching group is §3.2.1's dangling-`{other-id}` defect wearing a
  different hat, and gets the same treatment: reported, not ignored.
- **Per-part colour is static; thresholds stay value-derived.** `colors` assigns a fixed colour
  per part. A part whose colour must depend on its value uses a §6.3 token with `from:`, and §6's
  precedence is unchanged — a configured colour replaces a decorative one, never a value-derived
  threshold.

### 4.2 Handing a resolved value to a user's own command

§4.1 makes a user-defined item first-class, but a `command` item can only see what its own
process can discover. Everything the framework has *already resolved* — the model name, the
agent, the context percentage — is invisible to it, so a script that wants to react to one has
to re-derive it, usually by parsing the same stdin JSON the framework already parsed. That is
a second implementation of a behaviour that exists, which §1 forbids.

**A `command` item's argv may contain `{item-id}` placeholders, expanded before the process is
spawned.**

```json
{ "id": "agent-badge", "command": ["~/bin/agent-badge.sh", "{agent}", "{model}"],
  "ttlSeconds": 5 }
```

This is deliberately *not* a new syntax. It is the same `{}` / `{other-id}` vocabulary §3.2
already defines for link templates, resolved by the same resolver against the same registry —
one mechanism wearing a second hat, rather than a fourth way to name a value. It inherits
§3.2.1's constraint unchanged: a placeholder names a **registry id or a `command` id, never a
derived item**, and a `command` item's placeholders may not name another `command` item.
Command-to-command references would need a topological sort, which §3.2.1 deferred; allowing
them here would smuggle in the order-dependence that rule exists to prevent.

**Expansion is argv-only, and this is a security boundary rather than a convenience.**
`CommandProvider` passes each argv entry through `ProcessStartInfo.ArgumentList`, where it
reaches the child verbatim with no shell involved — a value containing `;`, `$(...)`, or a
newline is data. When `shell: true`, `command[0]` is instead handed to `sh -c`, and substituting
a resolved value into that string is command injection: a branch named `; rm -rf ~` would
execute. Values reaching a statusline are *attacker-influenceable* in the ordinary case — a
branch name comes from whoever opened the pull request.

So, for `shell: true`, the framework substitutes nothing and instead exports each referenced
value into the child environment as `CLAUDE_TUI_LINE_VAL_<ID>`, with the id upper-cased and
non-alphanumerics replaced by `_`. The script reads `"$CLAUDE_TUI_LINE_VAL_AGENT"`, quoted,
and the value stays data. This joins the `CLAUDE_TUI_LINE_ITEM_ID` and
`CLAUDE_TUI_LINE_PANE_WIDTH` variables `CommandProvider` already sets.

Only *referenced* values are exported, never all of them. Exporting everything would force
every item to resolve on every render, and `remote-url` is lazily probed (`ItemContext`)
precisely because it costs a subprocess — an eager export would reintroduce that cost
unconditionally and silently.

**These placeholders must join the up-front resolution set (§5), and this is the load-bearing
part.** §5 requires every value be resolved once before sizing begins, and enumerates the set as
"every item referenced by any pane's `items`, plus every item named by a colour token's `from`".
That enumeration is incomplete today: it omits link-template `{other-id}` references, which is
the mechanical cause of defect 11 — `{remote-url}` resolves only when `remote-url` happens to be
placed in a pane and therefore lands in the dictionary for a different reason. Adding argv
placeholders to that set without fixing the enumeration would reproduce the same bug in a second
place, with the same silence.

**`--check` errors on an argv placeholder naming an unknown id (`unknown-item-id`), a derived
item (`placeholder-derived-source`), or — from a `command` item — another `command` item
(`placeholder-command-source`).** All three are errors under §9.4's discriminator, and that
deserves saying plainly, because §3.2.1 rules that a dangling `{other-id}` in a **link** drops
the link and renders the text anyway. Same syntax, different severity, and the difference is not
inconsistency:

- A link is decoration over text that stands on its own, and §3.2.1 defines what an unmet
  dependency does. Satisfiable — a **warning**.
- An argv placeholder is *data handed to another process*, and nothing defines what an unresolved
  one expands to. The literal `{gitbranch}`? An empty string? A dropped argv entry? Each is a
  different command line, the script sees a different `$1`, and the spec picks none of them.
  Unsatisfiable — an **error**.

§9.5 lists both as id diagnostics because both come out of `ReferenceExtractors`; they share a
walk, not a severity.

**Two referenced ids that mangle to the same environment variable are also an error
(`placeholder-env-collision`).** The `CLAUDE_TUI_LINE_VAL_<ID>` rule upper-cases and replaces
non-alphanumerics, so `agent-short` and `agent.short` both become `AGENT_SHORT`. Whichever the
framework exports second wins and the script reads a value belonging to the other item, with
nothing anywhere reporting it — the same silence this section's substitution ban exists to avoid,
arriving by the route the ban opened.

**A placeholder naming a known id that is empty at render time substitutes the empty string, and
the argv entry survives.** `{"command": ["mytool", "--branch", "{git-branch}"]}` outside a repo
runs `mytool --branch ""`, not `mytool --branch`.

This case needs ruling for the reason this section already gave about the *unknown* case, four
paragraphs up: "The literal `{gitbranch}`? An empty string? A dropped argv entry? Each is a
different command line, the script sees a different `$1`." That argument is exactly as true of an
id that is known and simply empty — and there, unlike the unknown case, **`--check` cannot help**,
because emptiness is a runtime condition. The section made the argument and then applied it only
where a checker could act, which left the harder half unspecified.

Preserving arity is the choice that cannot go silently wrong. Dropping the entry shifts every
positional after it, so `mytool --branch {git-branch} --format json` outside a repo becomes
`mytool --branch --format json` and the tool binds `--format` as the *value* of `--branch`. That
is the render-wrong class again: a command that runs, exits 0, and reports something the user
never asked for. An empty argument can be wrong, but it cannot re-bind a flag to the wrong value.

The same rule under `shell: true`: the variable is exported **empty rather than unset**, so a
quoted `"$CLAUDE_TUI_LINE_VAL_GIT_BRANCH"` expands to one empty argument and arity is preserved
there too. Unset would leave the script's own `${VAR:-default}` free to fire, which silently
converts "this render had no branch" into "this render had whatever the script's default is" —
a different value, indistinguishable downstream.

**The item is not suppressed for an empty placeholder.** A command that handles empty input is a
reasonable thing to write, and suppressing would delete behaviour the author chose. This is the
opposite call from defect 14's `shell: true` argv fault, and deliberately: there the config has no
defined meaning, whereas here it has one and the value simply happens to be empty.

#### 4.2.1 Inheriting a vocabulary inherits the member that makes no sense here

"The same `{}` / `{other-id}` vocabulary §3.2 already defines" is the right design and it carries
one member across that does not survive the trip. In a link template `{}` means *this item's own
value*. A `command` item's own value is **what the command prints**, which does not exist until
the command has run, so `{"command": ["tool", "{}"]}` asks to be given its own output as an
argument. There is no answer to substitute.

**Bare `{}` in a `command` item's argv is an error (`placeholder-self-reference`).** It is a
declaration fault, visible without executing anything, and therefore inside §9.1.1's boundary.

The second inherited problem is that argv entries are far more likely than link templates to
contain braces meaning nothing at all. `jq '{name: .name}'` is an ordinary argv entry, and under a
naive reading it is a placeholder naming `name: .name`, which resolves to nothing, which this
section makes an **error** — so a working command becomes a config the framework refuses. The
grammar has to distinguish, and guessing is not available: `jq '{a}'` is valid jq shorthand *and*
a well-formed placeholder reference.

So, stated once and shared with §3.2:

- `{{` is a literal `{`; `}}` is a literal `}`.
- Otherwise `{` … `}` is a placeholder **only if** its contents are empty or match the item-id
  charset. `{name: .name}` contains a space, a colon and a dot, so it is literal and needs no
  escaping — which is what keeps the common case from requiring the author to know any of this.
- Anything else passes through unchanged.
- A placeholder-shaped reference naming no known id is `unknown-item-id`, as already ruled.

`jq '{a}'` therefore errors, and that is the correct outcome rather than a regrettable one: it is
genuinely ambiguous, `--check` catches it before it ever ships, and the diagnostic can name the
repair (`'{{a}}'`). An ambiguity resolved silently in either direction would instead be found by
someone wondering why their filter stopped matching.

#### 4.2.2 An unavailable source is not an empty one

The empty-value ruling above covers an item that answered with nothing. It does not cover an item
that **did not answer** — §4 distinguishes `absent` from `unavailable` precisely because a command
that timed out or exited nonzero has told us nothing, and §2.11.2 exists because collapsing the two
turns a timing accident into a layout decision.

Substituting the empty string for an `unavailable` source collapses them again, one section away
from where the distinction was made, and does it at the worst point: the value is handed to another
process, which cannot tell "there is no branch" from "git did not finish in 150ms" and will act on
the first reading. The result is a command that runs, exits 0, and reports something untrue.

**A `command` item with a placeholder naming an `unavailable` source is itself `unavailable`, and
is not spawned.** Same call as §4's, not a new policy — and it also declines to pay for a subprocess
whose input is already known to be wrong. Like the empty case, `--check` cannot see this: it is a
runtime condition, and §9.1.1's boundary holds.

#### 4.2.3 `shell: true` moves an input out of the cache key

This is the security ruling above colliding with §5, and it is silent in both directions.

§5 keys the value cache on `id` + hash of the **resolved argv** + `cwd`. For the argv path that is
complete: placeholder values are *in* the argv, so a different model or branch is a different key.
For `shell: true` the framework deliberately substitutes nothing — the argv is the same `sh -c
'…'` string on every render, and the values arrive through the environment instead. **So the key
no longer covers them.** A script reading `$CLAUDE_TUI_LINE_VAL_MODEL` is cached under a key that
ignores the model: switch models and it keeps reporting the old one for up to `ttlSeconds`, with
nothing anywhere reporting that it did.

The security fix caused it. Moving an input from argv to the environment was correct and necessary,
and it removed that input from a key defined by *which channel* an input travels in rather than by
*what the child can see*. `CLAUDE_TUI_LINE_PANE_WIDTH` is in the same position and always was — it
is exported to every `command` item, so a script that formats itself to the pane width caches one
answer and reuses it at every other width, which defeats the exact feature the variable exists to
provide.

**The cache key covers every input the child process can see**: the resolved argv, `cwd`, and every
environment variable the framework sets for it — each exported `CLAUDE_TUI_LINE_VAL_*` and
`CLAUDE_TUI_LINE_PANE_WIDTH`. Stated as a property rather than as a list of channels, so the next
input added is covered by the rule instead of needing to be remembered.

The cost is real and worth naming: because the width is exported unconditionally, **a resize is a
cache miss for every `command` item**, and the tick after a resize pays every command's cost at
once. That is correct — the alternative is rendering values computed for a width that is no longer
the width. If it ever becomes unacceptable, the lever is to export the width *only to items that
ask for it*, not to drop it from the key. Dropping it from the key restores the silent-wrong; the
export condition is where the cost actually lives.

(§5.0.1's first-tick-after-resize rule makes this slightly cheaper than it sounds: the width is
**absent** at that tick, and absent is one key shared by every terminal width, so the miss lands on
the tick after rather than during the drag.)

**Definition of done includes reclassifying `PaneItem.Command` in §9.5.1's coverage test.** That
section's exemption table carries `PaneItem.Command` as `PendingForm`, citing this section, because
`Command` has no reference form until placeholders exist. §9.5.1's own NEEDS-EVIDENCE (a) is answered:
no landed-detection marker was available while §4.2 was unimplemented, so the coverage test cannot
assert the reclassification on its own — this section's implementation must move the row from
`PendingForm` to a covered entry in `ReferenceExtractors` by hand, in the same change that adds the
placeholder-resolution logic.

### 4.3 Derived items

A derived item takes another item's value and reshapes it. It runs no process and reads no payload
field of its own: its entire input is one other item's resolved value.

```json
{ "id": "issue", "from": "git-branch", "extract": "[A-Za-z]{2,}-[0-9]+", "case": "upper" }
```

- **`from`** names the source item's id — required, and the thing that makes this kind a kind.
- **`extract`** is a regex applied to the source's value. The **first capture group** becomes the
  result, or the whole match when the pattern has none.
- **`case`** is `"upper"` or `"lower"`; anything else passes the text through unchanged.
- **`format`**, **`color`**, **`overflow`**, and **`link`** then apply exactly as they do to any
  other item.

The pipeline is `from → extract → case → format`, in that order, and **`extract` sees the raw
provider value rather than the rendered text.** That ordering is the whole design: a regex written
against `worktree:api(feature/ABC-123)` and one written against the bare branch are different
regexes, and the second is the one an author can write without knowing what decoration the builder
chose. Putting `extract` after `format` would make every derived item's pattern depend on
decoration it does not control.

**The source does not have to be displayed.** §5's resolution set is the set of *referenced* ids,
not the set of shown ones, so `from: "git-branch"` resolves the branch whether or not `git-branch`
appears in any pane. That is the feature, not a leak: it is how you show a scraped fragment without
also showing what it was scraped from.

**`from` names a real registry or command id only.** Pointing it at another derived item does not
resolve and suppresses the item — `from-derived-source`, an error under §9.4 rather than a warning,
because the result is a construct that never renders. The tempting alternative is a single
order-dependent pass, which would make chains work whenever the author happened to declare them in
the right order. That makes config line order silently load-bearing: reorder two lines and a link
disappears with nothing reported. Chaining can be added later behind a topological sort;
order-dependence cannot be withdrawn once configs rely on it.

**Why one general mechanism rather than a registry row per source.** An issue id scraped from a
branch name, a hostname pulled out of a remote URL, a ticket prefix — each would otherwise be a
code change and a new builtin. Here each is a config row. That is the §1 rule applied to the item
model, and it is why `extract` is a regex rather than a fixed set of named extractions.

This section exists because `derived` is one of §9.6.2's four item kinds and had no definitional
home: its keys were introduced inside §3.2's hyperlink worked example, where they read as part of
linking rather than as a kind of item. §9.6.2 cited it as `§4.3` — a section that did not exist —
which is §13.3's finding, and the fix that section prescribes: give the content the heading the
citations already assumed.

## 5. Execution model — the hard part

The process is spawned **every second**. Naive shelling out per item per tick would destroy
the 12.6ms budget that justified Native AOT in the first place.

**Values are resolved exactly once per render, before sizing begins.** Every item referenced by
any pane's `items`, plus every item named by a colour token's `from` (§6), a derived item's
`from` (§3.2), a link template's `{other-id}` (§3.2), a `command` item's argv placeholder
(§4.2), or a compound part's `item` / `from` (§3.3) — **each of them even when the referenced
item is never displayed** — is fetched in a
single up-front phase — builtins synchronously, `command` providers concurrently through the
cache with one shared timeout window — producing a plain synchronous dictionary. Sizing reads
that dictionary. Post-sizing colour resolution reads the same dictionary. No provider ever runs
twice in one render.

**The enumeration above is the whole feature, and getting it wrong fails silently.** An earlier
version of this paragraph listed only pane `items` and colour-token `from`, and that omission is
the mechanical cause of defect 11: a link's `{remote-url}` resolved only when `remote-url`
happened to be placed in a pane and therefore entered the dictionary for an unrelated reason,
so the feature appeared to work in tests and did nothing in the configuration users actually
want. Any future construct that names an item by id is incomplete until it is added to this
list. A reference that resolves in one config and silently drops in another is the failure mode
this list exists to prevent — so it is not "the set of displayed items", it is **the set of
referenced items**, and those are not the same set.

This is a **correctness requirement, not an optimisation**, and it is the reason the §2.3
fixpoint terminates. That fixpoint's convergence argument assumes a pane's intrinsic measurement
is a deterministic function of the width it was granted. Fetch values inside the loop and that
assumption dies: a clock, a counter, a flaky command returning a different string on pass 2
makes the measurement non-deterministic, breaks the monotone-decreasing invariant the 3-pass cap
is built on, and produces oscillation that no cap can fix — only mask. Resolving up front makes
the values *constant for the render*, which is exactly the hypothesis the fixpoint needs. The
latency win — never spawning a process three times for three passes — follows for free.

Items in panes later dropped by §2.3's over-constrained loop will have been fetched
unnecessarily. That waste is accepted deliberately: recovering it means moving fetching back
inside the sizing loop, which is the thing this rule exists to forbid.

**Cache with TTL.** Each `command` item's result is cached. Within `ttlSeconds` (default 30)
the cached value is used and **no process is spawned at all**. The steady-state cost of a
custom item is a map lookup, not a fork.

- Cache location: `$XDG_CACHE_HOME/claude-tui-line/items/`, else
  `~/.cache/claude-tui-line/items/`. Overridable by `CLAUDE_TUI_LINE_CACHE` for tests.
- Cache key: `id` + `cwd` + a hash of **every input the child process can see** — the resolved
  argv and every environment variable the framework sets for it (each `CLAUDE_TUI_LINE_VAL_*`,
  `CLAUDE_TUI_LINE_PANE_WIDTH`). `cwd` is in the key because a command like `git status --short`
  means different things in different sessions, and the cache is shared by every session on the
  machine. **The rest is stated as a property rather than as "the argv"** — see §4.2.3, where
  keying on the argv alone silently stopped covering a `shell: true` item's placeholder values
  the moment §4.2 moved them into the environment for security reasons.
- **One file per cache key**, named for the key — *not* a single `items.json` holding every
  entry. This is load-bearing, not filesystem taste. With one shared file, two sessions
  refreshing two different items each read-modify-write the whole map, and last-write-wins
  silently discards the other's refresh: the losing item stays stale, re-spawns next tick, and
  a busy machine thrashes forever without ever erroring. Per-key files make last-write-wins
  correct *per item*, which is the granularity the value actually has. Reading five keys is
  five opens — microseconds against a 13ms budget.
- Entry: `{ value, capturedAt, exitCode }`.
- Writes are atomic: temp file in the same directory, then rename. Concurrent statusline
  processes will still race on the *same* key; there last-write-wins is genuinely correct and
  no locking is used. A torn or unparsable cache file is treated as empty, never an error.

#### 5.0.1 `paneWidth` cannot live in the value entry

The obvious design puts `paneWidth` in the entry beside the value — the inner width this item's
pane resolved to on the last render, fed to `CLAUDE_TUI_LINE_PANE_WIDTH` on the next spawn (§4),
because the spawn happens *before* sizing and the previous render's width is the only estimate
available. It has to be rewritten on every render, cache hits included, or it goes stale with
respect to a resize.

**That is wrong, and it is wrong for the reason the per-key-file rule already establishes one
level up.** The value and the width have *different sharing scopes*. A value keyed by
`id` + argv + `cwd` is legitimately shared by every session on the machine — that is the point.
The pane width is a property of one terminal. Two sessions in the same repo at different terminal
widths therefore collide on a record where last-write-wins is *not* correct: each overwrites the
other's width every second, and each spawn receives the other terminal's width. The command
formats itself for a pane it is not in, output that is present, plausible and wrong, with nothing
in the render to suggest it (§7.1).

The rule generalises past this instance: **data with different sharing scopes must not share a
last-write-wins record.** §5's own argument for one file per key is the same argument at the
granularity above this one, and it does not stop being true here.

So:

- The width lives in a **separate store**, `widths/`, keyed by the cache key **plus `COLUMNS`**.
  Two sessions at the same terminal width resolve the same pane widths, so between *those* two
  last-write-wins is genuinely correct — which is the test §5 already applies to the value store.
- **Write only when the resolved width differs from what is stored.** In the steady state — no
  resize — that is zero writes, which is what makes "the steady-state cost of a custom item is a
  map lookup, not a fork" true. Rewriting a file every second for every command item was the
  claim's counterexample, and it was in the same section as the claim.
- A missing width record is not an error — it is the ordinary state on an item's first render,
  before any pane has been sized for it. Omit `CLAUDE_TUI_LINE_PANE_WIDTH` entirely rather than
  passing a guess: a command that adapts to width can detect an unset variable and fall back,
  and cannot detect a plausible wrong number.
- Same atomic write, same tolerance for a torn file: treat it as absent, never as an error.

**Timeouts and concurrency.** On a cache miss, all due commands are spawned **concurrently**
and awaited with an individual `timeoutMs` (default 150). Total added latency is therefore one
timeout window, not the sum. On timeout the process is killed with its whole tree, the same way
`GitBranch` already does it.

**Stale-on-failure.** If a command times out or fails, the last cached value is used **even if
expired**, so a flaky command degrades to a slightly old value instead of flickering out. If
there is no cached value at all, the item is suppressed. The next tick retries.

**That suppression must be marked `unavailable`, not merely empty.** §2.11.2 draws the
distinction and depends on it: an item that resolved to nothing is *absent*, an item that timed
out or errored is *unavailable*, and a pane holding an unavailable item does not collapse for
that render. Suppress it flatly and the two become the same state one layer down, so a command
that is 200 ms slow on one tick silently restructures the statusline — a pane collapsing, its
neighbours resizing, everything reflowing — on a timing accident, with no diagnostic anywhere and
nothing the next tick does to explain what the user just saw. The marker is what keeps a
transient failure from being read as a layout instruction.

**Never block the render.** A pathological command cannot exceed its timeout, and the render
proceeds with whatever is available. **On the render path** the exit code is always 0 and stdout
is always valid — Claude Code runs this once a second and has nowhere to show a failure, so
there is no such thing as a useful nonzero exit here.

That is a statement about the render path only, and §9.4's exit codes (0/1/2/3) are not an
exception to it. The CLI subcommands are invoked by a person or a tool that reads the code and
acts on it; the statusline hook is invoked by a supervisor that would only render the failure as
a blank line. Same binary, two callers, and the contract belongs to the caller — §9.1's "the
render path is untouched" is the same boundary drawn from the other side.

### 5.1 Built-in probes are cached in the same store, not a second one

`remote-url` shells out to git. `ItemContext` makes the probe lazy so it only costs a subprocess
when something actually references it — but *referencing* it is the normal case for anyone using
`{remote-url}` in a link, and then it costs a subprocess **every second, forever**, for a value
that changes approximately never.

**It uses `ItemCache`.** Not a parallel cache for built-ins: the store already does file-per-key,
atomic temp-and-rename, silent-on-failure, shared across every session on the machine. A second
cache with its own eviction and its own bugs is the §1 violation this project keeps paying for
elsewhere, and there is nothing about a built-in probe that a `command` item's cache does not
already handle.

Four rulings, of which the second is the one that bites:

- **Key on `cwd`, not the repository root.** Two directories in one repo get two entries holding
  the same URL, which looks wasteful and is correct: finding the repo root costs the subprocess
  this exists to avoid, so paying it to deduplicate the cache would spend the saving to achieve
  the saving.

- **A probe that finds nothing is a cached result, not a cache miss.** Outside a git repo, or in
  a repo with no remote, `Probe` returns null — and null is the *answer*, cached like any other.
  Treating absent-value as not-yet-probed re-spawns git on every render in exactly the case where
  the spawn can never return anything, which is the pathological case wearing the costume of the
  normal one. This mirrors §5's existing rule that a clean run with empty output is a legitimate
  value.

- **A fixed TTL of 300 seconds, with no config key.** `PaneItem.TtlSeconds` exists and is
  tempting, but `remote-url` is frequently referenced *only* from a link template, where there is
  no pane entry to carry it — and if it is referenced from a pane entry and a link at once, two
  TTLs contend for one cache entry with no principled winner. A fixed default has no such
  ambiguity. Five minutes bounds how long a click can go to a stale repository after `git remote
  set-url`, against 300× fewer subprocesses; widening this to a config key later is easy, and
  withdrawing one after configs depend on it is not.

- **No lock, and no stampede mitigation.** Several sessions can miss together and each spawn git.
  At a 300-second TTL that is N spawns per five minutes rather than N per second, and the fix for
  it — a lock file in the render path — adds a failure mode strictly worse than the cost it
  removes.

The laziness stays exactly where it is. The cache goes *inside* the `Lazy`, so an unreferenced
`remote-url` still touches neither git nor the filesystem: referenced → `Lazy` fires → cache read
→ hit returns, miss probes and writes. Reversing those two would make every render pay a file
read for a value nobody asked for.

## 6. Coloring

Anywhere this spec accepts a colour — an item's `color`, a border's `color` (§2.10) — the value
is a **colour expression**: a literal, a token reference, or an inline rule. One grammar, three
forms, valid in every position. A border and an item can therefore be driven by the *same*
expression, which is the point of the whole section.

### 6.1 Literals

A plain string names a colour:

- **The 16 standard names** — `blue`, `yellow`, `fuchsia`, `grey`, `maroon`, `olive`, … These
  resolve **through the user's terminal theme**. `blue` is whatever the user's colour scheme
  calls blue. This is the right default for a statusline that has to sit inside somebody else's
  terminal and not clash with it.
- **The 256 palette names and indices** — `steelblue1`, `gold1`, `hotpink`, or `color(213)`.
- **Hex** — `#ff5fd7`.

The last two are **absolute**: they ignore the terminal theme and render the same everywhere.
That difference is the reason to keep all three rather than replacing the 16 with the wider
set — an author picks theme-following or exact, per colour, and both are legitimate. Do not
"upgrade" the 16 names to their 256-palette equivalents.

### 6.2 Colour system, and why widening is opt-in

```json
"colorSystem": "standard" | "256" | "truecolor"     // top-level, default "standard"
```

**The default is `standard`, and that is load-bearing.** Raising the colour system changes the
SGR bytes Spectre emits for the builtin segments, which is exactly what the golden parity
baseline pins. Making the wider palette opt-in means the default render stays byte-identical to
the captured bash statusline **by construction**, and the parity gate keeps its meaning without
anyone having to regenerate it. §13 listed palette widening as out of scope precisely because
it "needs its own decision"; this is that decision, and it is the config knob rather than a
profile raise.

Under `standard`, a 256-name or hex colour is **down-converted to the nearest of the 16** — it
is not an error and does not suppress the item. `--check` reports it as a **`warning`** so an
author who wrote `#ff5fd7` and got approximately-magenta knows why. (An earlier draft called this
a "notice"; there are exactly two severities and that was a third one by accident. It is a warning
under §9.4.1's test — the item is delivered, reduced. It is reported at all, rather than dismissed
as untidy, because §6.1 promises hex and 256-palette colours are *absolute*, and under `standard`
that promise does not hold.)

A baseline regenerated because it was inconvenient has stopped meaning anything. If widening
ever *does* become the default, that is a separate decision requiring a visually confirmed
re-capture recorded explicitly as a loss of coverage — not a side effect of a colour change.

#### 6.2.1 What down-converts, and under which system

Everything above discusses `standard`, which is the common case and not the whole rule. Each
literal form has a **minimum colour system** — the narrowest one that renders it as written:

| literal form | example | minimum system |
|---|---|---|
| one of the sixteen named tokens | `blue`, `fuchsia` | `standard` |
| a 256-palette name | `deepskyblue1`, `orange3` | `256` |
| a bare palette index ≥ 16 | `141`, `207` | `256` |
| a bare palette index ≤ 15 | `4` | `standard` |
| a hex literal | `#ff5fd7` | `truecolor` |

**The numeric form is a bare digit string, not `colorNNN`.** An earlier draft of this table wrote
`color141`, on the assumption that Spectre accepts the prefixed spelling. It does not, in 0.57.2:
`Style.TryParse("color207")` **fails**, while `Style.TryParse("207")` resolves to palette entry
207. Verified by reflecting over the installed assembly rather than from documentation, because
the two disagreed. The prefixed form is not a second accepted spelling to also support — it is not
accepted at all, and every place this document recommended it was recommending a value that
resolves to no colour. A colour that fails to parse renders as no colour, silently, which is the
one failure mode §6.2.1 exists to warn about.

**`color-down-converted` fires when a literal's minimum exceeds the resolved `colorSystem`.**
That is three cases, not one: 256-forms under `standard`, hex under `standard`, and **hex under
`256`** — the third invisible to anyone reading only the paragraphs above, because they never
mention a system the author might have widened *to*. Attributes (`default`, `dim`, `bold`) are
not colours and never fire.

The message must name the palette actually rendered through — sixteen under `standard`, 256
under `256`. "Approximated to the nearest of the sixteen" is simply false in the third case, and
a diagnostic whose stated reason does not apply to the input that triggered it is how authors
learn to disbelieve the ones that do.

**"Attributes never fire" is a property check, not a fourth name list.** `Style.TryParse` does not
reject `default`, `dim` or `bold` — it accepts them as decoration-only specs and `ResolveLiteral`
returns **`Color.Default`** for each (established by reflecting over 0.57.2, not assumed). So the
implementation does not need to recognise the attribute names: a literal resolving to
`Color.Default` carries no palette entry, is therefore outside this diagnostic's predicate
entirely, and is exempt by construction. Matching on the three names instead would be a fourth
enumeration of a closed set — the thing §4's registry rule and §9.4.1's predicate rule both exist
to prevent — and it would go stale the moment Spectre accepts a fourth decoration.

**`null` and `Color.Default` must not collapse into one branch**, tempting as it is now that both
mean "not a palette colour". `null` is *failed to parse*, which is §6.2.1's entire subject: it
renders as no colour, silently, and is the case a warning has to reach. `Color.Default` is *parsed
successfully and names no colour on purpose*. One is a fault, the other is a valid config, and the
predicate that separates them is the difference between the diagnostic firing on `dim` — the most
common value in the document — and never firing on the typo it was written for.

**A hex literal that happens to land exactly on a 256-palette entry still fires under `256`.**
This is deliberate, and it is not the false positive it looks like. Suppressing it would mean
shipping a table of 256 RGB triples whose correctness nothing in this project would ever check —
an unverifiable exactness claim, which is worse than a warning that is merely uninteresting. And
the warning is *right* even then: an author who means palette entry 207 should write `207`, which
says so and survives a colour-system change, rather than a hex literal that renders correctly
today by coincidence. The diagnostic points at a real improvement in that case, not at a phantom
defect.

**How the tier of a *named* token is determined — and the one hand-written list this project
accepts.** Spectre's `Color` exposes `R`/`G`/`B` and nothing else: no palette index, no name
readback, no API of any kind that distinguishes `red` from `grey37` once parsed. Confirmed by
reflection over 0.57.2's public surface, not inferred. So there is no computed discriminator, and
the choice is between a name list and abandoning the named-token row of the table above.

Ruled: **a constant holding the sixteen basic names, and nothing larger.** A name that parses and
is not in it is a 256-palette name, minimum system `256`. This is not a retreat from "do not
pattern-match against a name list" — that ruling was about enumerating the *extended* palette,
which is Spectre's list, roughly 240 entries long, changes with the library, and would be a second
registry in the §1 sense. The sixteen are a different object: closed by the ANSI standard, fixed
for forty years, already written out in §6.3 and in STATUS.md's empirically verified table.

The deciding argument is that **the constant has to exist anyway**. §9.6.3 requires `--colors` to
print the recommended palette, which is exactly these sixteen, and observes that no file in `src/`
currently enumerates them — they are named only in prose. So `--colors` needs the list, and the
tier discriminator needs the same list. **One constant, two consumers, never two lists**; §1
applies here in its ordinary form, and the failure it names is the one to avoid — `--colors`
printing a palette the tier check disagrees about.

### 6.3 Named tokens

A top-level `colors` table defines reusable, value-driven colour tokens. Referenced with a
leading `@`:

```json
{
  "colors": {
    "model-accent": {
      "from": "model",
      "match": [
        { "contains": "Sonnet", "color": "blue"    },
        { "contains": "Opus",   "color": "yellow"  },
        { "contains": "Fable",  "color": "fuchsia" }
      ],
      "default": "grey"
    }
  },
  "surface": { "pane": { "children": [
    { "border": { "color": "@model-accent" },
      "items":  [ { "item": "model", "color": "@model-accent" } ] }
  ] } }
}
```

The token exists so that the border and the text cannot drift. Written as two inline rules,
they are two copies of one mapping and someone eventually updates one of them.

- **`from` is required in the `colors` table** and names the item id whose value drives the
  rule. A token has no owning item — a *border* has no value of its own — so there is nothing
  to default to. This is the `from` that §5 refers to: the named item is fetched even when it
  is never displayed in any pane.
- **Tokens are flat.** A token's `color` values must be literals (§6.1); a token may not
  reference another token. This makes reference cycles impossible by construction rather than
  by detection, and one level of indirection is all the drift problem needs.
- An unknown `@name` resolves to no colour, silently, per §7. `--check` reports it.

### 6.4 Rule forms

Two rule shapes. Both may be written inline as an item's `color`, or in the `colors` table as a
token. Inline, `from` defaults to **the item the colour is attached to**; in the table it is
required.

**`thresholds`** — numeric, first satisfied wins, evaluated in declaration order:

```json
"color": { "thresholds": [ {"min": 80, "color": "maroon"}, {"min": 50, "color": "olive"} ],
           "default": "green" }
```

Applied when the value parses as a number. The existing 50/80 rule for `context` and
`rate-limits` must be expressed *through* this mechanism, not kept as a parallel code path —
same rule as §1.

**`match`** — string, first match wins, evaluated in declaration order. Each entry carries
exactly one predicate:

- `"contains": "Sonnet"` — case-insensitive substring.
- `"equals": "vim"` — case-insensitive full match.

**Substring is the default idiom on purpose.** Model display strings carry version numbers that
change — `Sonnet 5`, `Sonnet 5.1` — while the family name does not. An `equals` rule against a
model name is a bug with a delayed fuse: it works until the day the version bumps and then
silently falls through to `default`, which looks like nothing happened rather than like a
failure.

No regex. A statusline re-renders every second and a pathological pattern would be a
per-second stall with no way for the user to see why.

**`default`** is optional in both forms. Absent, a value that matches nothing takes **no
colour** — it does not inherit the previous rule's colour and does not suppress the item. A
missing, failed, or empty source value takes the `default` branch.

### 6.5 When colours resolve

**Colour resolution happens in the same up-front phase as value resolution (§5), immediately
after it, and before sizing begins.** It produces a resolved colour map alongside the value
dictionary; sizing and the final render pass both read already-resolved colours.

Nothing a colour expression needs depends on layout — a token reads an item value, and item
values are all known once §5's fetch phase completes. So there is no reason to defer, and
deferring costs something real: a placeholder colour threaded through measurement is a
placeholder that can leak into output on any path that skips the later fixup. Resolve once,
early, and let every downstream consumer read a finished value.

This is safe for the §2.3 fixpoint for the reason §5 gives — colour affects `Segment.Markup`
and never `Segment.Plain`, which is the width metric — but the fixpoint's safety is not why
the ordering is specified. It is specified because one resolution point beats two.

### 6.6 Defect 15: border colour has two resolvers, and they do not accept the same language

§6.5 ends "one resolution point beats two." Border colour has two, one layer below where that
sentence was looking, and they disagree about what a colour *is*.

| | pane-tree path | single-pane path |
|---|---|---|
| call site | `PaneTreeRenderer.cs:76` | `Program.cs:145` |
| resolver | `ColorResolution.Resolve` | `ColorResolution.ResolveBorderColor` → `ResolveLiteral` |
| produces | a **spec string**, used as a markup tag | a Spectre **`Color`**, via `Style.TryParse(...).Foreground` |
| consumed by | `PaneBorderRenderer.Wrap` | `Panel.BorderStyle(new Style(...))` |
| fallback | `?? "grey"` | `?? Color.Grey` |

Both are live in production; which one runs is decided by the shape of the user's config, not by
anything the user can see. Item colour has no such split — `PaneAssembler.cs:66` calls `Resolve`
and the spec goes into markup, which is why this stayed invisible: the divergence exists only on
the border key.

**The consequence is a capability gap, not just a duplication.** A markup tag carries decorations;
a `Color` cannot. `ResolveLiteral` takes `.Foreground` off the parsed style and discards everything
else, so a decoration-only spec — `dim` and `bold`, both of which §6.1 documents and both of which
`Style.TryParse` accepts — survives one path and is dropped by the other. `border.color: "dim"` is
therefore expected to render dim in a split config and undim in a single-pane one, with no
diagnostic on either, because both paths *succeeded*.

> **Now verified, and it is worse than the prediction.** This paragraph read "unverified, and it
> must be checked before the fix is written" — the claim was inferred from the signature rather
> than observed. It was checked, against the pinned Spectre 0.57.2, in a throwaway console app
> built for §9.6.3.1's round-trip assertion and not for this: `Style` is a value type,
> `Foreground` reflects as a non-nullable `Spectre.Console.Color`, and `TryParse("dim")`,
> `TryParse("bold")` and `TryParse("default")` all return true with `Foreground == Color.Default`,
> while `TryParse("olive")` returns a real colour. The symptom above is real.
>
> **The gap is wider than `dim` and `bold`.** The same run put every remaining decoration keyword
> through it — `italic`, `underline`, `invert`, `conceal`, `strikethrough`, and both blinks — and
> all seven behave identically. That is the general fact rather than three special cases: a
> **decoration-only spec has no colour component to contribute**, so `.Foreground` is `Color.Default`
> for all of them, and `ResolveLiteral` discards every one. The table above says a `Color` cannot
> carry decorations; the measurement says which decorations, and it is all of them. §6.1 documents
> `dim` and `bold`, which is why those are the two named — but `border.color: "italic"` fails the
> same way, and the fix below covers the class rather than the two.

Note where that evidence came from: a test being written for a *different* section, whose author
recognised the reasoning as inferred and built a harness rather than agreeing with it. §9.4's
lesson applies to spec prose as much as to diagnostics, and the discharge of an "unverified" block
is worth as much as the block was.

**Fix: one resolver, and `ResolveLiteral`'s return type is the actual bug.** `ColorResolution.Resolve`
becomes the sole border-colour resolver, as it already is for items. `ResolveBorderColor` stays,
but only as a thin adapter over it, and it must return a **`Style`** rather than a `Color` —
`Style.TryParse(spec)` whole, not `.Foreground` — so decorations survive into
`Panel.BorderStyle`. The two fallbacks collapse into the one constant they only coincidentally
agree on today.

**The test that proves it must use a decoration, and asserting on `Style` equality is not enough.**
A test that sets `border.color: "olive"` passes today on both paths, because a colour is the one
input where the two resolvers agree — that is the whole shape of this defect. Drive it with `dim`
through the single-pane path, and assert the resolved `Style` carries the **decoration**, not
merely that resolution returned something non-default. `Color.Default` is what the broken path
returns *successfully*, so any assertion phrased as "it resolved" passes against the bug. This is
§10.1's rule arriving through a second door: an assertion that cannot fail against the defect it
was written for is not a test of it.

The general form is §9.8's rule, which was written about a checker transcribing the renderer's
arithmetic: **two expressions of one thing drift silently.** This is the same failure with both
copies inside the renderer, which is worse, because there is no checker/renderer boundary to
suggest looking. It was found by asking what `--colors` is allowed to print — a question about a
CLI flag that had nothing to do with borders.

### 6.6.1 Where the broken path actually runs, and what the fix must not break

§6.6 says "both are live in production; which one runs is decided by the shape of the user's
config." That is true and too generous. Reading the dispatch settles it, and the answer is
narrower in reach and worse in stakes than the sentence implies.

`Program.cs:94` gates the tree pipeline on
`surfaceWidth is int && (pane.Items.Count > 0 || (pane.Split != PaneSplit.None && pane.Children.Count > 0))`.
Every pane with content — items, or a split with children — goes to `PaneTreeRenderer`, the path
that keeps decorations. So the `Panel` branch at `Program.cs:142` is reachable by exactly one kind
of pane: **one with a border, no items, and no children.** Not "single-pane configs." An *empty*
bordered pane.

Two consequences follow, and they point opposite ways.

**The stakes go up, not down.** On the only path where the narrow resolver runs, the border is the
entire visible output — there is no content inside it to carry the styling instead. A defect that
drops decorations everywhere except where the decoration is the only thing on screen is not an
edge case that mostly misses. It is a defect that only fires where it is total.
`SafeLoadAll`'s fallback pane (`Program.cs:496`) is built with `Array.Empty<PaneItem>()`,
`PaneSplit.None` and no children, so it routes here by construction — and its border style is
`BoxBorder.Rounded`, hard-coded one line above it (`Program.cs:492`), not a nullable read. It
always draws. So the **config-error rendering** is one of the two things this affects, not merely
one that could be.

**And it touches Defect 12 — but less than the task title suggests.** Task #4 reads "empty pane
still draws its border", and read as a title it says the `Panel` branch's only live case is about
to disappear. §2.11 says something narrower: collapse reaches only a **structurally empty**
`content` or `fill` pane with no `minSize`. An empty `fixed` or `percent` pane keeps its extent
and its border on purpose (§2.4 — the author named a number), and so does one with an explicit
`minSize` (§2.11.1). Those panes still have no items and no children, so they still route here.

**Defect 12 narrows this branch's live set; it does not empty it.** The adapter is needed either
way, and deleting the branch is off the table. What does leave the set is `SafeLoadAll`'s fallback
pane, for a reason that turned out to matter well beyond border colour — see §2.11.3, where
collapsing that pane is what makes a config error render zero rows.

The ordering constraint that survives is the modest one: whichever of §6.6 and §2.11 lands second
should re-check this paragraph rather than assume it, because both edit the same branch's
reachability. That is bookkeeping, not a coupling — and stating it as a coupling, which an earlier
draft of this section did, overstated it in the direction that would have cost an implementor the
adapter.

**`--check` is an aggravator here, not a co-defect.** `ConfigCheck.cs:194` calls `ResolveLiteral`
too, and the instinct on finding a third caller of the narrow resolver is to file it as more of
the same. It is not. Its comment already reasons correctly about decorations — a spec resolving to
`Color.Default`, "e.g. `default`/`dim`/`bold` — decoration, not a palette index", has no palette
dependency — and `--check` therefore **accepts `border.color: "dim"` and is right to.** Acceptance
there is `Style.TryParse` and nothing more, which is the same question the markup path asks.

That makes it worse rather than better. The user is not merely unlucky; they are *told* the spec
is valid by the tool whose job is to say so, and then one renderer honours it and the other does
not. A silent wrong render is §7.1's third outcome. A silent wrong render that a checker
affirmatively blessed first is that outcome with its own contradiction built in.

**So `ResolveLiteral` survives the fix, and its scope must be written down.** §6.6 says
`ResolveBorderColor` becomes an adapter returning a `Style`; it does not say what becomes of
`ResolveLiteral`, and the reflex — delete the lossy thing — is wrong. §6.2.1's
minimum-colour-system check genuinely needs a `Color` to rank a palette, and that is a question
about the colour component alone, where discarding decorations is correct rather than lossy.

`ResolveLiteral` is a **palette query, not a resolver.** Its permitted callers are the ones asking
what colour system a spec requires — §6.2.1's check and §9.6.3's `--colors`. **No render path may
call it.** Say so at its definition, because the failure mode is not that someone disagrees; it is
that a `Color`-returning function is the convenient thing to reach for when a Spectre API wants a
colour, and the call site type-checks. That is how this defect got written the first time.

The general shape, worth naming because this repo keeps meeting it: **a function that parses a
rich value and returns one field of it is a lossy narrowing wearing a parser's name.** It compiles
everywhere, it succeeds on every input the full parser accepts, and the discarded capability has
no error to attach itself to. `ResolveLiteral` parses a whole `Style` and returns `.Foreground` on
the next line — the loss is one property access wide, and nothing in the type system, the tests,
or `--check` was ever going to point at it.

## 7. Failure behaviour

This section is cited **27 times** in this document and had no heading. Every "§7 makes the
renderer cope with everything", every "§7's class", every argument that a diagnostic's severity is
not about whether the renderer survives — all of them resolved to nothing, while §7.1 sat below as
a subsection of a section that was not there. It is the most-cited reference in the document and
the most thoroughly dangling, which is not a coincidence: a reference used constantly is one every
reader already knows the meaning of, so nobody follows it, so nobody finds out it goes nowhere.
See §2.9 for the same defect caught the same way, and §13.3 for the rule now covering both.

Inherited from v1 and non-negotiable: **the statusline never errors, never pollutes stdout,
always exits 0.** A misconfigured item, a missing binary, a script that writes garbage, a
malformed pane tree — each degrades locally. One bad item suppresses itself; one bad pane
renders empty; neither takes out the line.

Config validation failures are silent by design. A `--check` CLI flag (§9) is how a user finds
out they typo'd something, because a statusline is the wrong surface for error text.

### 7.1 The third outcome: output that is wrong rather than absent

The pairing above — degrade silently at render, explain at check time — assumes two outcomes. A
config is either fine, or it produces *less* than intended and `--check` says why. **There is a
third, and it is the only one with no signal at all: output that is present, plausible, and
wrong.**

The user cannot notice it, because there is nothing missing to notice. `--check` cannot report it,
because nothing failed. And the render path cannot degrade around it, because from the inside it
looks like success. Four instances are known, three of them found by walking rules over cases
rather than by anything failing:

| where | the wrong output |
|---|---|
| defect 14 (§4.1) | `shell: true` with a multi-element argv runs `sh -c "kubectl"` and renders bare kubectl's usage text |
| defect 15 (§6.6) | `border.color: "dim"` renders dim through one border resolver and undim through the other |
| §3.3 | an absent value between two literals left a bare `(` on the line |
| §4.2 | a dropped argv entry re-binds the next flag, so `--branch` takes `--format` as its value |

**The rule these produced, for every future ruling on a runtime-missing value:** when a config has
a defined meaning and only the *value* is absent, prefer the option that cannot silently produce
different-but-plausible output, even when another option looks tidier. Concretely, that has meant
preserving arity over dropping an entry, exporting empty over leaving unset, and dropping a
bound literal over emitting it alone. When the config itself has no defined meaning, the call
inverts — suppress the item (defect 14), which puts the fault back where `--check` can explain it.

**Where these keep coming from is worth naming.** Both §3.3 and §4.2 argued carefully about the
case a checker can catch and went quiet on the case only the renderer sees; §6.6 had two code
paths nobody had reason to compare. So the question to ask of any new rule is not "is this
right?" but **"what does this do when the value is missing at render time, and who would ever
find out?"** If the answer to the second half is "nobody", the rule is not finished.

## 8. Config

Iteration 1 — one pane, which is also what an omitted `surface` block means:

```json
{
  "border": { "enabled": true, "color": "grey", "style": "rounded" },
  "layout": { "chromeReserve": 3 },
  "items":  [ { "item": "directory" }, { "item": "git-branch" }, { "item": "context" } ]
}
```

Later, with splits:

```json
{
  "surface": {
    "maxRows": 4,
    "pane": {
      "split": "vertical",
      "gutter": 1,
      "children": [
        { "size": "auto", "overflow": "wrap",
          "items": [ { "item": "directory", "overflow": "truncate" },
                     { "item": "git-branch" } ] },
        { "size": "32%", "border": { "enabled": true, "color": "blue" },
          "overflow": "truncate", "ellipsis": "…", "maxRows": 2,
          "items": [ { "item": "context" },
                     { "id": "k8s", "command": ["kubectl", "config", "current-context"],
                       "format": "k8s:{}", "color": "cyan", "ttlSeconds": 30 } ] }
      ]
    }
  }
}
```

- `surface` **absent** ⇒ a single root leaf pane holding top-level `items`, with the top-level
  `border`. This is what keeps every existing config rendering exactly as it does today.
- `surface` **present** ⇒ top-level `items` is ignored; the pane tree is authoritative.
- **`border.enabled` defaults to true on a leaf and false on a split container.** An explicit
  `enabled` always wins; the default applies only where the author wrote nothing. One predicate,
  leaf vs split — *not* a special case for the root. A split container is a layout device, and
  adding a split to a config must never silently add chrome: under a uniform default, nesting
  three deep would spend 12 columns on boxes before any content existed. The border stays on the
  panes that hold content, which is also where its color is worth setting.
- `items` **absent** in a leaf ⇒ the default list: **the builtins whose `default` flag is true**,
  in CAPTURE.md order. That is a proper subset, not all of them — `model-short` and `remote-url`
  are opt-in and render only where an author places them. Read as "all builtins" the default set
  gains `remote-url`, which shells out to git on every render, and the promise that you only pay
  for that if you asked for it is quietly broken. §9.6.2's `default` flag is the one definition
  of which is which; this sentence must never become the second — which is also why it states a
  predicate rather than a count. A count here is a second registry with one entry, and the way it
  fails is that both numbers stay in the prose long after a row lands and only one of them is
  still true (§4).
- `items` **present** ⇒ exactly those, in that order. An unknown builtin id is **suppressed at
  render time and reported by `--check` as `unknown-item-id` (error)**. Both halves are the rule:
  suppression alone is §7.1's third outcome — a config that renders short, plausibly, forever,
  with nothing anywhere saying why — and the diagnostic is what makes the silence recoverable
  rather than merely quiet.
- A `{ "item": "<id>" }` entry may override `format`/`color`/`overflow` on a builtin.
- `overflow` and `ellipsis` are inherited by a pane's items unless an item overrides
  `overflow` itself. A long path set to `truncate` inside an otherwise wrapping pane is the
  motivating case: wrap everything, but do not let one directory name cost three rows.

Config is still read on every render, so an edit takes effect within a second (SPEC.md §6b).

## 9. CLI surface

v1's binary did exactly one thing: render. v2 adds the modes below, none of which may interfere
with the statusline path — **no args ⇒ render**, unchanged.

> **Discharged.** This paragraph used to read "the binary currently does exactly one thing. v2 needs
> three more", and both halves had gone stale. `--check`, `--items`, `--colors` and `--version` have
> since shipped (`--preview` is the one still in progress), so the present tense sent readers to
> build what was already built. Worse, `--version` was specified later, in §9.7, and never added
> here — so a count written before it existed sat above a list that had also drifted, and this
> paragraph became a second authority on *how many modes there are*, disagreeing with the one that
> is right. **§9.4 owns that question**, and owns it in a form that survives the next command being
> added. What follows defines what each mode *does*; it does not define the set.

- `--check` — validate config, print human-readable diagnostics for unknown ids, bad colors,
  malformed panes, sizes that cannot fit, and `overflow: "overflow"` on a pane inside a split
  (§2.6); exit nonzero on error.
- `--version` — print the version and exit 0, reading it from the assembly so there is one source
  rather than a string maintained here. It is a mode like the rest, and the only one with no JSON
  shape, which is why `--version --json` is a usage error (§9.4). See §9.7.
- `--preview [--columns N]` — render to stdout at a fixed width, for iterating on a config
  without waiting for the real statusline. Prints each row's measured width alongside, so
  overflow and ragged compositing are visible rather than inferred.
- `--items` — emit the registry as JSON, in **two sections**: `items`, one row per builtin id, and
  `kinds`, the key vocabulary of each way an item can be written. See §9.6.2 for the shape and for
  why the keys are not per-row.
- `--colors` — print the **recommended** palette **rendered in its own colour**, so the choice is
  made by looking rather than by guessing what `olive` is. `--colors --json` emits the same list
  unstyled for a program, and must declare that it is a recommendation rather than the accepted
  set — see §9.6.3. The palette is theme-mapped (§6.2), so printing it through the user's own
  terminal is the only honest preview; a swatch in documentation shows the author's theme.

`--items` exists because §12's authoring tools need to know what the framework can do, and any
other way of knowing it is a copy. A skill or command with an item list embedded in its prose is
a second registry that drifts the moment a row is added — the §1 failure, relocated into
documentation where nothing type-checks it. The binary is the only thing that knows its own
capabilities, so the authoring surface asks it rather than remembering.

`--check` also takes `--json`, emitting the same diagnostics as structured records with a
`path` (JSON Pointer into the config), `severity`, `code`, and `message`. A human reads the
prose form; a program that has just written a config reads this one and knows *which key* it
got wrong without parsing English.

### 9.1 The render path is untouched, and that is a hard constraint

**No arguments ⇒ read the payload on stdin and render, byte for byte as today.** The statusline
runs once a second; every millisecond of argument parsing is paid 86,400 times a day. Parse
argv only far enough to notice it is empty, and take the existing path when it is.

No flag may change what the no-flag path emits. A flag that alters rendering is not a CLI
feature, it is a second renderer, and §12.6's "two adapters over one core" applies here first.

#### 9.1.1 `--check` never executes a `command` item; `--preview` always does

This was never written down, and both halves are load-bearing in opposite directions.

**`--check` is static.** It reads the config, resolves ids, validates the vocabulary, and runs
nothing. Three independent reasons, any one of which is sufficient:

- **Validation runs on configs nobody has approved yet.** `/edit` checks a config a model just
  wrote; `/migrate` checks one it just generated from someone else's shell script; §12.6's tools
  check whatever config path the caller names. If checking executed `command` items, then
  "validate this config" would mean "execute the commands in this config", and the one operation
  a user reaches for *before* trusting a file would be the operation that trusts it.
- **`--check`'s answer must be a function of the config alone.** §9.8 already establishes this
  for width; machine state and wall-clock time are the same argument. Running commands makes the
  same config check clean at one moment and dirty at the next, and `--check`'s exit code is the
  gate `/edit` and `/migrate` accept or reject a write on. A gate that flickers is not a gate.
- **A check must not cost what a render costs.** §5's whole budget analysis is about paying for
  subprocesses once per second, not once per validation of a config with a dozen items.

The consequence to hold onto: **`--check` cannot report anything about a command's output** —
not its width, not its line count, not whether it produced anything at all. Every diagnostic in
§9.6.1 that touches a `command` item (`command-shape`, `command-shell-argv`, the `placeholder-*`
codes) is a statement about the *declaration*, and that is not an accident of what has been
specified so far. It is the boundary.

**`--preview` is the opposite, deliberately.** It runs the same pipeline the statusline runs
(§9.3), which means it spawns the user's commands, honours their TTLs, and shows their real
output. A preview that skipped `command` items would show a statusline nobody will ever see, and
§12.3 and §12.4 both put a preview in front of the user as the *evidence* for accepting a write.
Evidence assembled by skipping the interesting half is not evidence.

So the pair is: `--check` answers "is this config well-formed?" without side effects, and
`--preview` answers "what does this actually look like?" by accepting them. The authoring
commands call both, back to back, on the same file — which is exactly why the difference has to
be stated rather than inferred from each command's description. §12.6.7 bounds what an MCP tool
may *write*; this bounds what validating one may *run*.

### 9.2 `--config <path>` — global, and the reason the authoring surface works

Overrides the §5 search order for every subcommand *and* for the render path. Absent, the search
order is unchanged.

This flag is load-bearing for §12, not a convenience. `/migrate` and `/edit` must validate and
preview a **candidate** config without installing it, because §12.3 requires showing the user a
result before anything is written. Without `--config` the only way to preview a config is to make
it live, which inverts the whole safety property those commands are built on.

A `--config` path that does not exist, or does not parse, is an error (§9.4) and never a silent
fallback to the searched config. Reporting on a different file than the one named is worse than
failing.

#### 9.2.1 The same flag on the render path, where failing is not available

That rule and §5's "never block the render" point in opposite directions, and they meet on one
real command line: a `statusLine.command` of `claude-tui-line --config ~/my.json` whose file is
later deleted, or saved with a trailing comma. §9.2 says do not silently fall back; §5 and §9.1
say the render path exits 0 and never blocks, because Claude Code runs it once a second and has
nowhere to show a failure. Both cannot be followed literally, and neither may simply lose.

**The false resolution is to fall back to defaults.** It satisfies §5's letter and produces the
§7.1 outcome in its purest form: a statusline that renders, looks entirely reasonable, is not
the one the user configured, and says nothing about it — once a second, indefinitely. The user's
config file is sitting right there with a syntax error in it and nothing anywhere connects the
two. That is the outcome this document treats as worse than a crash.

**Ruled: the render path exits 0 and renders the reason.** Not the config, not the defaults —
one row of plain text naming the fault and the path, truncated to the usable width:

```
claude-tui-line: /Users/x/my.json: unexpected ',' at line 12
```

**That reason string is illustrative and no real one looks like it** — `System.Text.Json` produces
something four times as long with the line number at the far end. §9.2.2 rules how the real one is
composed, and the difference is not cosmetic: it decides what survives truncation. Do not treat this
fence as a format to reproduce.

The statusline is the only output channel this path has, so a diagnostic that goes anywhere else
is a diagnostic nobody receives. stderr is discarded, the exit code is not displayed, and a log
file nobody knows about is not a channel. Plain text with no border, because the border
machinery is configured by the file that could not be read.

**The distinguishing question is whether a config was asserted, not whether one was found.**

| situation | render path does |
|---|---|
| no `--config`, no file at any searched path | built-in defaults, silently — a legitimate state, and §8's documented one |
| no `--config`, a searched file exists but does not parse | render the reason |
| `--config <path>`, file missing | render the reason |
| `--config <path>`, file does not parse | render the reason |

Row two is the one that is easy to get wrong, and it is not a special case: writing a file to
`~/.claude/claude-tui-line.json` asserts a config exactly as naming one on the command line does.
The absence of an assertion is the only thing defaults are the right answer to.

This does not weaken §5. Exit code 0, no blocking, no retry, no waiting on anything — the render
completes on the tick it was asked for. What changes is only what it draws, and drawing an
explanation costs the same as drawing a pane.

For every path other than the render path — `--check`, `--preview`, `--items` — §9.4's exit 3
applies unchanged. Those have a caller who can read an exit code, which is precisely the
distinction §5's exit-code rule was scoped to.

#### 9.2.2 The diagnostic row is an interface, and "truncated to the usable width" is four unanswered questions

§9.2.1 rules *that* the render path draws the reason, and shows one sample of what that looks
like. A sample is not a format. The row it describes is the only thing a user will ever see when
their config breaks — they will read it, screenshot it, and paste it into an issue — which makes
it an interface in exactly the sense §9.8.1 means when it pins the collector notes: the moment
something is written to be read, an unpinned string drifts and nothing fails.

**The prefix is the literal string `claude-tui-line`, never `argv[0]`.** Under a plugin install
`argv[0]` is an absolute path into a versioned directory, and under a shim it is whatever the
shim was named. The person reading this row is trying to find out which tool is complaining so
they can go look for its config; a forty-character path answers a question they did not ask and
buries the one they did.

**The path is the one that was actually read, resolved.** In row two of §9.2.1's table the user
never typed a path — the config was found by the §5 search order — and that is precisely the row
where naming the file is the entire information content. "Something in your config is broken" is
not actionable when the config could be in any of the searched locations.

**The reason is the payload; the path is context.** This decides the degradation order, which
matters more than it sounds like it should: statuslines are routinely 60 columns inside a split
pane, and a home-directory path plus a parse error does not fit in 60 columns. Truncating right
to left — the obvious implementation — throws away the reason and keeps the path, producing a row
that says a file is bad without saying what is wrong with it. That is the one substring of the
message with no value on its own, since the user can already see the file.

**Ruled: a degradation ladder, applied in order, each rung tried only when the one above it does
not fit.** The ladder has five rungs:

1. `claude-tui-line: <path>: <reason>` — the whole row.
2. The same, with `<path>` elided from the middle to whatever budget remains after the prefix and
   the reason, keeping the leading `/` or `~` and the file name.
3. `claude-tui-line: <reason>` — the path dropped entirely.
4. The same, with `<reason>` truncated from the right and marked with an ellipsis.
5. As much of `claude-tui-line` as fits. Below about eighteen columns nothing useful is possible,
   and this rung exists so that the code has a defined answer rather than an exception.

Each rung is a test, which is the point of writing it as a ladder rather than as a sentence about
truncation.

Two constraints on how the fitting is computed. The width is the full terminal width — this row
has no border and no gutter — and it must come from the same function the render path uses, for
the reason §9.3.4 gives: a second measurement is a second answer. And the ellipsis is the
built-in one, never the configured `ellipsis`, on the same grounds §9.2.1 gives for drawing no
border — that setting lives in the file that could not be read.

**`<reason>` is composed, not passed through, and §9.2.1's fenced sample is illustrative.** That
sample reads `unexpected ',' at line 12`. The real string is `System.Text.Json`'s, and it is not
close:

```
The JSON array contains a trailing comma at the end which is not supported in this mode. Change the
reader options. Path: $.surface.pane.items[1] | LineNumber: 9 | BytePositionInLine: 6.
```

Rung 4 truncates the reason **from the right**, so at any ordinary width that row keeps *"Change the
reader options"* — advice addressed to whoever wrote the parser call, actionable by nobody holding
this config — and drops the line number, which is the only part that cannot be recovered by looking
at the file. The ladder was designed against a reason string shaped like the sample, and against the
real one it degrades to exactly the failure §9.2.2 opens by rejecting: a row that says a file is bad
without saying where.

So put the irreplaceable part first: `line <n>, <path within the document>: <message>`. The position
is not scraped out of the text — `JsonException` carries `LineNumber`, `BytePositionInLine` and
`Path` as **properties**, so this is a structured read, not string surgery on a message .NET is free
to reword. The message text is then appended raw and is the part rung 4 is allowed to eat, because
it is the part a user can reconstruct by opening the file at the line we just named.

The general rule the ladder was missing, and which any future rung must satisfy: **truncation has to
degrade toward what the user cannot otherwise obtain.** Reason text is recoverable; position is not.
Nothing here normalizes or rewrites .NET's wording — inventing our own parser vocabulary would be a
second registry of a table we do not own (§9.6.3 makes the same argument for colour names).

**Two corrections from building it, both found by measuring rather than by reading.**

**`LineNumber` is 0-indexed and the row is 1-indexed.** `JsonException.LineNumber` counts from
zero; every editor the reader will open the file in counts from one. Display `LineNumber + 1`. The
paragraph above justifies the whole composition by saying the message is "the part a user can
reconstruct by opening the file at the line we just named" — which is true only if the number names
the line they will actually land on. An off-by-one here does not degrade the row, it inverts its
argument. (`BytePositionInLine` is 0-indexed on the same convention, and is not in the composed
form; if a rung ever adds a column, it takes the same `+ 1`.)

**Only `line <n>` is protected from rung 4, not the whole composed prefix.** A real JSON Pointer is
long — `$.surface.pane.items[0]` is twenty-three columns, putting the full prefix near thirty-four
— so protecting all of it means that at 45 columns the ladder cannot fit the protected region and
falls to rung 5, the bare tool name. That is the correct behaviour of the rule as first written and
the wrong outcome: it trades a row that still says *where* for a row that says nothing, at exactly
the widths this ladder exists for.

Apply the irreplaceability rule one level finer than it was first applied. `line <n>` cannot be
recovered by any means available to the reader. The Pointer largely can — it is what they will see
when they open the file at that line — and the message text certainly can. So the protected region
ends after the line number; the Pointer and the message together are one truncatable tail, cut from
the right. At 45 columns the row degrades to a shortened Pointer rather than to nothing, and rung 5
returns to being the floor it was described as rather than the ordinary case. When the tail is cut
away entirely, the separator goes with it — no row ends in a dangling `,` or `: `.

### 9.3 Where `--preview` gets its payload

`--preview` renders the same pipeline the statusline renders, so it needs the same stdin JSON.

- **stdin has data** → use it. This is what `/migrate` uses to compare against the original
  script on identical input — identical, and also *complete*, which is §12.3.1's ruling and not
  something this branch can supply on its own.
- **stdin is empty or a TTY** → use a built-in synthetic payload, **and say so on stderr**. A
  preview built from invented values that does not admit it is invented is how a user concludes
  an item is broken when it was never given anything to show.

The synthetic payload is a fixed constant, not randomised, so two previews are comparable.

**It is also the only synthetic payload in the binary.** `--items` needs one too, for §9.6.2's
`example` field, and it uses this one rather than defining its own — wrapped in an `ItemContext`
with canned `GitBranch`/`Engram`/`RemoteUrl`, since those come from probing the machine rather
than from stdin. Two constants would let `--items` and `--preview` disagree about what an item
looks like, which is worse than either being wrong alone: `/migrate` consults both in the same
session and has no way to notice they were built from different inputs.

**"In the binary" is a boundary, and §12.7.1 finds three payloads outside it.** The same argument
governs the command layer and was never made there. Read that section together with this paragraph;
the uniqueness this one claims is not yet a property of what ships.

`--columns N` sets the width. Absent, use `COLUMNS`, then a default of 100. The usable width
is still `N - chromeReserve`; preview must not quietly render 3 columns wider than reality, or
it will disagree with the statusline exactly at the width where wrapping starts to matter.

#### 9.3.1 The payload itself

§9.3 above mandates that this constant exist and be shared, and stopped there — which left the
one part that has to be *decided*. It is decided here, as a literal, because "a fixed synthetic
payload" is not a value and two people implementing that sentence produce two different fixtures:

```json
{
  "cwd": "/home/you/code/acme-web",
  "workspace": { "repo": { "owner": "acme", "name": "acme-web" } },
  "worktree": { "name": "acme-web", "branch": "feat/eng-1234" },
  "pr": { "number": 128, "review_state": "APPROVED" },
  "model": { "display_name": "Claude Sonnet 5" },
  "effort": { "level": "medium" },
  "thinking": { "enabled": true },
  "output_style": { "name": "Explanatory" },
  "context_window": {
    "used_percentage": 34.0, "total_input_tokens": 68000, "context_window_size": 200000
  },
  "rate_limits": {
    "five_hour": { "used_percentage": 22.0 }, "seven_day": { "used_percentage": 41.0 }
  },
  "agent": { "name": "acme-reviewer" },
  "vim": { "mode": "NORMAL" },
  "session_id": "00000000-0000-4000-8000-000000000000"
}
```

wrapped in an `ItemContext` whose machine-probed fields are canned to match: `gitBranch` =
`"feat/eng-1234"`, `remoteUrl` = `"https://github.com/acme/acme-web"`, and an `EngramResult` with
**`Facts` = 3** and **`Verb` = `"◉ recalled"`**, which `BuildEngram` renders as
`engram:3 ◉ recalled`.

That third value used to read "a small non-zero activity count — whatever shape makes `engram`
render *present*", which is the same defect as the `output_style` one below wearing a milder face:
a constraint stated as an outcome, leaving the value to whoever implements it. It is pinned now for
the reason the whole fixture is pinned — two people implementing "whatever makes it render" produce
two fixtures, and §9.3's entire point is that there is one.

**Note what is pinned and what is described.** `Facts` and `Verb` are the fixture — inputs, and
normative. `engram:3 ◉ recalled` is a *description of what the builder currently does with them*,
and it is the kind of sentence §9.6.2.1 warns about: an assertion about the implementation, in a
document that cannot check it. It is written here anyway, because a fixture whose effect nobody
states is how the `output_style` defect survived — but if it ever disagrees with `BuildEngram`,
**the builder is the fact and this clause is the finding.**

Four rules govern it, and each one rules out a fixture someone would otherwise reasonably write.

**Every field is populated, including the ones real payloads usually omit.** This is the one place
where completeness beats realism. Real Claude Code payloads routinely carry no `pr` and no `vim`,
so a fixture built to look like a real payload omits them — and then `--items` reports two items
whose `example` is empty, from which a user correctly concludes those items produce nothing. An
item with no example has no entry in the field that exists to show it. Whatever an item needs in
order to render, the fixture has.

**`output_style.name` is `"Explanatory"` and must not be set back to `"default"`**, which is what
this fixture said until the implementor built `--items` against it and found the contradiction.
`"default"` is the *realistic* value — it is what most real payloads carry — and `BuildOutputStyle`
deliberately suppresses any style name equal to it, because an output style of "default" is noise
on a statusline. Both of those are correct. Together they meant the one item this fixture exists to
demonstrate could never demonstrate itself: `--items` would report `output-style` with an empty
example, permanently, and a user would correctly conclude the item produces nothing.

That is this rule's own failure mode, committed in the literal the rule governs, and it is worth
naming because of *how* it got there. Every other field was written by asking "what would a real
payload hold here?" — the right question fifteen times and the wrong one once. The rule is not
"populate every field"; it is **populate every field with a value that survives the renderer**, and
the only fields where those differ are the ones with suppression logic behind them. Suppression is
invisible from the payload side, which is why this needed a rule and not care.

**`worktree.branch` (and the canned `gitBranch` that must equal it) is `feat/eng-1234` and must not
be set back to `main`.** `main` is the *realistic* value and it is what this fixture said until
`linear` was built against it — `linear` extracts a ticket id from the branch with
`[A-Za-z]{2,}-[0-9]+`, so `main` yields no match, the item suppresses itself, and `--items` reports
it with a permanently empty example. That is the `output_style` defect above in a second guise, and
it generalises the rule: **suppression is not the only renderer logic invisible from the payload
side — extraction is too.** Any field a builder parses, matches, or extracts from must hold a value
that survives that parse, not merely a value a real payload could carry. The branch is deliberately
prefixed rather than a bare `eng-1234` so the example shows the extraction rather than disguising it
as an identity.

**Redundant fields must agree with each other.** `used_percentage: 34.0` is `68000 / 200000`;
`worktree.branch` is the same `"feat/eng-1234"` the canned `gitBranch` reports; `workspace.repo`
names the same repo as the canned remote URL and the worktree. A fixture with `used_percentage: 80` and
40k of 200k tokens describes a state Claude Code cannot produce, `--preview` renders it faithfully,
and the first person to notice spends their afternoon looking for the bug in `context`.

The type permits any combination, so **the agreement is asserted by test, not by this paragraph and
not by the comment in `SyntheticFixture.cs`.** Three assertions, and each one can fail:

- `used_percentage == 100 × total_input_tokens ÷ context_window_size`
- the canned `gitBranch` equals `worktree.branch`
- the canned remote URL's owner and name equal `workspace.repo`'s, and `worktree.name`

This is §9.8's rule turned on the fixture. Writing an invariant down where the values live is the
same move as a checker transcribing the renderer's arithmetic: two expressions of one thing, with
nothing but prose between them, and the prose does not fail the build when someone tunes a number.
The fixture satisfies all three today — it was checked when this clause was written — which is
exactly the moment to add the assertion, while agreement is a fact rather than a repair.

**The values are deliberately unremarkable, and specifically are not near a threshold.** The
tempting alternative is to sit `context` at 82% so the example visibly exercises §6's colour ladder
— but this fixture is the baseline every user compares their real statusline against, and a
baseline that renders alarming teaches the wrong resting state. It is also answering the wrong
question: `--items`' `example` field answers *what does this item's text look like*, and `--colors`
(§9.6.3) plus `--preview` are where colour behaviour is exercised. `Claude Sonnet 5` for the same
reason — §6's model rule makes it the calm one.

**The values are visibly synthetic.** `/home/you/…`, `acme`, an all-zeroes session id. §9.3 requires
saying on stderr that a preview is invented, and stderr is the stream most likely to be discarded by
whatever is capturing the output. The payload should still admit what it is once that notice is
gone.

A note on what this fixture is *not* for: it is not a test fixture. Tests keep constructing whatever
inputs they need. This one is user-facing output, which is why it is specified in prose rather than
left to whoever writes it first.

#### 9.3.2 `--preview` does not degrade under a pipe, and the rule that says so is §9.6.3.1's

§9.6.3.1 rules that bare `--colors` writes through the ordinary auto-detecting console and loses its
colour under a pipe. Read as a precedent, that answer propagates to `--preview` and is wrong there.
Read as what it is — the output of a test — it gives `--preview` the opposite answer, which is the
correct one. **Bare `--preview` writes through the same console configuration the render path uses.**

The test §9.6.3.1 applies without naming: **a form may degrade only while some other form still
carries the whole payload.**

- For `--colors`, `--json` carries the names, the names are the entire payload, and a reader who
  pipes the bare form loses a convenience they can recover one flag away.
- For `--preview`, §9.8.1 pins `rows[]` as `{ "text": …, "width": … }`. That shape holds no styling
  and is not going to grow any — `text` is deliberately the diffable form. So if the bare form
  degrades as well, **no form of `--preview` carries styling to a caller that is not a terminal.**

Which is every caller that matters. §12.3 and §12.4 both capture this output through a harness
rather than a TTY, and §12.6's tools will too. "Colour it by value" is among the most common things
the authoring surface is asked for; a preview that cannot render colour cannot answer it, and
`/edit`'s closing before-and-after — the deliverable that command exists to produce — degrades to two
blocks of grey without anything reporting that it did.

**Use the render path's console configuration itself rather than constructing a matching one.** This
is §1's rule: one implementation per behaviour. `--preview` exists to show what the render path
produces, so the single behaviour it must never hold its own opinion about is how that output gets
styled. Stating the configuration here in flags would create the second authority, and this document
already knows what two authorities cost.

That also disposes of `NO_COLOR` and every variable like it, without this section needing to know
what any of them do: whatever the statusline does, the preview does. Showing a user something their
real statusline will not do is the failure being avoided — not the honouring or the overriding of
any particular convention.

That this was already load-bearing is visible in §12.3, which instructs the migrator to strip escape
sequences from both renders before comparing them. That step has assumed `--preview` emits escapes
since it was written. A command prompt depending on behaviour no section states is the same tell
that produced §9.6.2.2 and §9.6.3.1, and this is its third instance in §9 — which is the argument
for reading the bare form of every remaining command as unspecified until someone rules on it,
rather than waiting to be surprised a fourth time.

Unchanged by any of this: notes go to stderr in the human form and to `notes[]` in the JSON form
(§9.8.1), `rows[].text` stays plain and stays the form built for diffing, and `--preview --json` is
unstyled like every other JSON output (§9).

**The fourth bare form was audited and needs no ruling, which is worth recording so nobody reopens
it.** Bare `--check` writes its diagnostics to stdout — they are its payload, not commentary on it,
so §9.8.1's stderr split does not reach here — and on a clean config it prints **nothing** and
exits 0. Silence is the correct answer and not an omission: it is what a validator is expected to
do, and the ambiguity a reader might fear — "clean, or did it fail to run?" — is not reachable by
the callers that would be hurt by it. §12.3 and §12.4 both invoke `--check --json`, which answers
`ok: true` explicitly. The bare form's consumer is a person at a terminal, for whom an empty
response after a validation command has exactly one meaning.

So the rule this subsection argues for is *audit* every bare form, not *rule on* every bare form.
Three of the four needed a decision and one did not, and the difference was only visible after
looking. Adding output to this one to match the others would be symmetry bought at the cost of the
convention every tool on the machine already teaches.

#### 9.3.3 What §9.6 did not say: bare `--preview`, and why there is no gutter

The fourth bare form, and the first where the document had already answered — wrongly. §9.6 said
`--preview --json` returns each row's measured width "rather than printing widths in a gutter as the
human form does." Nothing else in the document describes the human form, which makes that clause its
entire specification, arrived at as a contrast drawn to explain the JSON rather than as a decision
about the text.

**There is no gutter. Bare `--preview` writes the rendered surface to stdout and nothing else** —
one line per row, styled per §9.3.2, byte-for-byte what the render path would have emitted at that
width.

The reason is a consumer the gutter clause did not have in view. §12.3's fidelity step and §12.4's
before/after step both capture `--preview`'s stdout and diff it: migrate against the *original*
statusline's own output, edit against an earlier capture of itself. A width gutter puts a prefix on
every line that the original never produced, and the instruction those steps give — strip escape
sequences from both — does not remove it. The one comparison the bare form exists to serve is the
one a gutter silently breaks, and it breaks it in the direction where every line differs, which
reads as a total mismatch rather than as a formatting artifact.

**This is not an exception to §9.6, and not a new rule.** §9.8.1 already rules that render notes go
to stderr *precisely so stdout stays byte-comparable*, and a width printed beside a row is
information about the render rather than the render. It was on the wrong stream by §9.8.1's own
test from the moment it was written; §9.6 only predates the test being pointed at it.

**The widths still get said — on stderr, as a note, unconditionally.** One line naming the resolved
column count and where it came from (the `--columns` argument, `COLUMNS`, or the fallback), emitted
before the notes §9.8.1 assigns to this command. Unconditional rather than only-when-implicit,
because §12.5 appends three runs to one file and a width line is what tells a reader which block of
notes belongs to which run. It costs nothing on a stream nothing diffs.

`--preview --json` is unaffected: `rows[].width` stays where §9.6 put it, for the reason §9.6 gives.

#### 9.3.4 Three things `--preview`'s first implementation had to decide

All three came back from the implementation as "the spec pins the content but not this", and all
three are load-bearing. Ruled here rather than in a message, because a ruling that lives in a
conversation is a ruling nobody can cite.

**A `row` in `--preview --json` is a line of the rendered surface, borders included.** The
implementation populated `rows[]` from the pipeline's pre-Panel content rows, which is the cheaper
read and the wrong object. Two reasons, and the second is the one that decides it:

- §9 says preview exists so that "overflow and ragged compositing are visible rather than inferred".
  Ragged compositing is a property of the *composed* surface — a bordered pane beside an unbordered
  sibling is exactly where it appears, and pre-Panel rows are measured before the border exists.
- Bare `--preview` and `--preview --json` would otherwise disagree about how many rows there are.
  A bordered pane writes three lines and would report one. §12's prompts tell an LLM to read either
  form — "`--preview --json` carries the same notes in `notes[]`. Use whichever you will actually
  check" — and *the same* is a promise about one render seen two ways, not two renders. A caller
  diffing `rows[].text` against the bare output gets a mismatch with nothing to explain it.

`text` is the line with ANSI stripped; `width` is that line's width **computed by the same function
the layout used**, never a second measurement. A width the renderer did not use is a number that
can disagree with the layout it claims to describe, and the disagreement would appear exactly where
the tool is being trusted most.

*That line's* width, and the emphasis is load-bearing now that a row is a rendered line. Once
`rows[]` holds bordered lines there is a second number available for a content row — the width the
content itself measured, before the border and padding were wrapped around it — and it is genuinely
useful, because rows in a split pipeline are ragged and raggedness is a thing this tool exists to
reveal. It is still not `width`. `text` and `width` sit in the same object, and a consumer reads
them as describing each other; a `width` of 18 beside a 39-character `text` is a field that lies,
which is worse than a field that is missing. **Ruled: `width` always describes `text`, on every
row, border lines included. The pre-border content width is reported as `contentWidth`, present on
content rows and absent on border lines, which have no such number.** One field per question is the
whole of it — the alternative is not two answers but one wrong one.

**Preview reads the `paneWidth` stamp and must never write it,** and the implementation's
`stampWidths: false` is right for a larger reason than caution. The stamp is not in-process state:
`ItemCache.StampPaneWidth` writes it to the cache **on disk**, which is the same cache the live
statusline reads on its next tick. So `--preview --columns 60` would write `60` into the entry the
user's real statusline then hands to their `command` items — at their actual terminal width. The
user's statusline would render wrong, once, for a reason nothing on screen connects to a preview
they ran in another window. A read-only preview that corrupts the render path through a shared
store is the render path not being untouched (§9.1), one indirection out.

The cost is a divergence worth naming: preview reads a stamp written at the *live* width, so a
`command` item that formats to its pane width sees the wrong one under `--columns 60`. That is the
one-render-behind signal being one render behind in a different window, and it is not fixable by
stamping — preview cannot know the width before it lays out either. **§5.0.1 is where it does get
fixed**, and this rules what that store must do: key the widths store **by resolved surface width**,
so a preview at 60 reads and writes the 60 entry, the live render at 120 reads and writes the 120
entry, and neither can see the other. Then preview stamping becomes correct rather than forbidden,
and the boolean goes away instead of becoming permanent. Until then, `stampWidths: false`.

**A note's text is pinned the moment anything is told to read it.** §9.3's preamble lines — the
synthetic-input admission and the columns-resolution line — are content-pinned only, and their
wording stays the implementation's, because they are addressed to a person reading a terminal.
Render notes are different: `migrate.md` teaches an LLM to tell a width drop from a `maxLines` cap
by quoting `pane N dropped: children need S columns at C columns` (or, when the drop is a floor
violation instead of an over-allocation, `pane N dropped: G columns is under its F-column floor at
C columns`) and `item 'X' emitted N lines; M kept (maxLines)` verbatim. A quoted string in a prompt
is an interface, whether or not anyone declared it one.

So **every note the collector can emit has its text pinned in §9.8.1's list**, including the ones
nothing quotes yet — `segment truncated to fit N columns` among them. The rule is not
that quoted notes are pinned; it is that an unpinned note *will* be quoted, by whoever writes the
next prompt, and will then drift with nothing failing. This is §1's one-implementation rule applied
to a string: the note text has one home, and prompts cite it rather than each carrying a copy.

### 9.4 Exit codes and severities

| exit | meaning |
|---|---|
| 0 | success; for `--check`, no `error`-severity diagnostics |
| 1 | `--check` found at least one `error` diagnostic |
| 2 | usage error — unknown flag, missing argument, mutually exclusive flags |
| 3 | the config could not be read or parsed at all, so nothing could be done with it |

3 is separate from 1 deliberately. "Your config has four problems" and "I could not read your
config" call for different next actions, and a program that gets 1 will try to fix a JSON Pointer
that does not exist.

3 belongs to the *config*, not to `--check`: `--preview` returns it too when the config it was
pointed at cannot be read. (`--items` reads no config — §9.6.2's rows are the builtin registry —
so it cannot reach 3.) The **render path returns 0 regardless** and draws the reason instead —
§9.2.1, which is where that asymmetry is argued rather than merely asserted.

Two severities, and the split resolves defects 3–6 (silent acceptance):

- **`error`** — the config does not do what it says. An unknown item id; an unknown value for
  **any key whose accepted values are a closed set** (§9.4.1 tier 1); an unknown colour name;
  `overflow: "overflow"` on a pane inside a split (§2.6); a pane whose fixed sizes cannot fit its
  parent.
- **`warning`** — it is satisfiable, but probably not what was meant. A `content` or `fill` pane
  with no items (`pane-no-items`); a `link` naming an id that resolves to nothing
  (`unknown-link-target`); under `collapse: true`, two panes asking for different colours or
  styles on one shared edge (`collapsed-edge-conflict`, §2.10).

**Two warnings that used to be on that list have been removed, both for firing on configs that
are fine.** They are recorded rather than quietly dropped, because each was wrong in a way worth
not repeating.

- **`command` item with no `timeoutMs`.** The premise was that an unbounded subprocess inside a
  once-a-second loop presents as a frozen statusline. There is no unbounded subprocess:
  `CommandProvider` defaults to `DefaultTimeoutMs = 150` and kills the whole process tree. The
  warning would have sent every author to set a key that was already set for them. Choosing the
  value deliberately is still good practice — 150 ms is tight for a real script — but good
  practice is what documentation is for, not what a diagnostic is for.
- **A pane with no items**, unqualified. Narrowed rather than removed, because §2.11's own
  division already separates the two cases: an empty `content`/`fill` pane collapses, so writing
  it accomplished nothing and the author almost certainly meant something else. An empty
  `fixed`/`percent` pane keeps its extent — that is a **spacer**, a legitimate and deliberate
  construct, and warning on it would be a false positive on working intent.

The shared lesson: a diagnostic's premise is a claim about the implementation, and it goes stale
exactly like any other. Both of these were true when written and stopped being true when a
default landed and when §2.11 was ruled.

The dangling `link` moved from `error` to `warning` here, and that correction is the reason the
next paragraph exists.

That last one refines the rule rather than bending it. Two colours cannot both hold on one
physical line, so it looks unsatisfiable — but §2.10 *defines* the resolution (first requester in
tree declaration order), so the config has a well-defined meaning and renders deterministically.
**Unsatisfiable means no defined meaning, not "some of what you typed had no effect."** It is a
warning because a written `color` was silently discarded, and §2.10 is right that this is the kind
of thing a user otherwise spends an evening on; it is not an error because the spec sanctions the
outcome. Under `collapse: false` it cannot arise at all — neighbours never contend for one line.

#### 9.4.1 The test, in the form that actually decides cases

"No defined meaning" is not enough on its own, and a ruling on the five `ReferenceExtractors`
forms is what exposed it. §7 and §3.2.1 define a fallback for *everything* — a dangling `link`
drops the link, a dangling derived `from` suppresses the item, a colour rule falls through to its
`default`. If "the spec wrote down a fallback" made a thing satisfiable, nothing would ever be an
error and we would be back at "does the renderer cope" under a new name.

**First, two tiers, because the consequence test only governs the second one.**

- **Tier 1 — not in the language.** An unknown value for **any key whose accepted values are a
  closed set** (`unknown-enum-value`); an unknown colour name (`unknown-color`); `overflow:
  "overflow"` in a position §2.6 forbids (`overflow-forbidden-position`). These are **always
  errors**, and consequence never enters into it. The document is not a valid instance of the
  schema, and how gracefully the renderer absorbs a token that is not in the language is beside
  the point — the paragraph below on unknown enum values is the argument, and it stands unchanged.

  **The trigger is the predicate, not a list.** As written this section named six keys — `size`,
  `style`, `align`, `valign`, `overflow`, `case` — and the list was short by at least three:
  `split` (§2.2 — cited rather than copied, for the same reason as `distribute` below), `distribute`
  (§2.3 — and writing its members out here is how this list was wrong about that key too, so it is
  cited rather than copied), and top-level `colorSystem` (§6.2 — cited for the same reason). Every
  one of those three fails silently and consequentially:

  - A misspelled `split` reverts to the default vertical axis; the pane stays a container with all
    its children intact, and the difference is a silently wrong axis rather than a vanished one.
  - A misspelled `distribute` reverts to greedy sizing, which is exactly the layout the author
    wrote the key to avoid, and the difference is a row count rather than an absence.
  - `"colorSystem": "24bit"` falls back to `standard`, and the author then gets §6.2.1's
    `color-down-converted` warnings on the literals they widened the profile *for* — a diagnostic
    that is correct, unexplainable from where they are standing, and points away from the typo.

  So the rule is stated as a predicate and the enumeration is illustration, for the same reason
  §4's is: a list restated in four places is four things to keep true, and the way it fails is
  that a key is added with a closed value set and nobody thinks to touch §9.4. If a key accepts a
  fixed set of strings, an unrecognised one is `unknown-enum-value`. There is no key with a closed
  value set that is deliberately unchecked.

  **One code across all of them, not one per key.** The JSON Pointer in `path` already names the
  key exactly, so a per-key code would only repeat it, and the repair is identical in every case:
  replace the value with one from the recognized set. That the set of keys can grow without the
  code changing is the point — a per-key code would make adding a key a change to the §9.6
  compatibility surface. Compare §4.1, which *does* split `command-shape` from
  `command-shell-argv` — there the repairs genuinely differ. Same rule, opposite answers, which
  is how you can tell the rule is doing work rather than decorating a decision already made.

  **The message must name the accepted set**, since the whole failure mode here is an author who
  cannot see what they got wrong: `"24bit" is not a colorSystem — expected standard, 256, or
  truecolor`. A code and a pointer localise the fault; only the accepted set repairs it, and
  every one of these sets is short enough to print.

  **`unknown-color`, spelled the American way, on purpose.** Every code string matches the
  config key it is about, and the key is `"color"`. This document says colour throughout, so the
  mismatch looks like a typo and will eventually tempt someone into "fixing" it. §9.6 makes that
  a breaking change to a shipped compatibility surface. It is spelled to match the config, and
  it stays that way. The same goes for `unknown-color-source` and `unknown-color-token`.

  `overflow: "overflow"` gets its own code rather than folding into `unknown-enum-value` because
  the value is not unknown — it is a perfectly good member of the enum that this position
  forbids, and the repair is to pick a different mode rather than to correct a misspelling. This
  is the same distinction, and the same reasoning, that gives `from-derived-source` its own code
  below.
- **Tier 2 — in the language, referent missing.** A `from`, a `link` `{other-id}`, an `@name`, an
  `{ "item": ... }` selector. The grammar accepts any string in these positions, so validity is
  not the question; existence is. **The consequence test decides these, and only these.**

Getting this backwards is easy and would be quietly destructive: applied to tier 1, the
consequence test demotes every typo to a warning, because an unknown `size` falls back to `fill`
and an unknown colour falls back to no colour and in both cases the pane renders fine. That is
precisely the silent acceptance (defects 3–6) this whole section exists to end.

For tier 2, the question is not whether a fallback exists. It is **what the fallback does to the
thing the config asked for**:

- **Delivered in reduced form → `warning`.** The author asked for something and got it, minus a
  decoration. `{ "item": "git-branch", "link": "{nope}/tree/{}" }` renders `main`, unlinked. A
  reasonable author might not even mind. This is the "some of what you typed had no effect" case.
- **Deleted → `error`.** The author asked for something and got nothing. Nobody writes an item
  into a config intending it never to appear, so the fallback is not a lesser version of the
  request; it is the request's negation.

Applied to the reference forms, which is where the boundary is easiest to get wrong:

| form | fallback | severity |
|---|---|---|
| `{ "item": "<id>" }` selector naming nothing | the item never renders | **error**, `unknown-item-id` |
| derived `from` naming nothing | the item never renders | **error**, `unknown-item-id` |
| derived `from` naming another derived item | the item never renders (§3.2.1) | **error**, `from-derived-source` |
| `link` `{other-id}` naming nothing | text renders, link dropped | **warning**, `unknown-link-target` |
| colour rule `from` naming nothing (inline or `colors` table) | text renders, colour falls back | **warning**, `unknown-color-source` |
| `@name` naming no token (§6.3) | text renders, colour lost | **warning**, `unknown-color-token` |
| `command` argv `{id}` placeholder (§4.2) | undefined — the child gets a command line nobody wrote | **error**, §4.2's three codes |

**Three of these name an item id and none of them reuses `unknown-item-id`, deliberately.** §9.5
already ruled that sharing the reference walk is not sharing the verdict; the corollary is that
the *code* has to carry the verdict too, or a consumer branching on it cannot tell an error from
a warning without also reading `severity`. One code with two severities is a code whose meaning
is not fixed, which §9.6 forbids. `unknown-color-token` is doubly distinct: its `@name` resolves
against the `colors` table, a different namespace from item ids entirely, so conflating the two
lookups would report a good config as dangling.

The two `from` forms landing in *different* buckets is the point, and the tempting mistake is to
group by reference syntax — every one of them is a `from`-or-`{}` lookup through one null-safe
path, so they look alike from inside the resolver. Group by consequence instead. A colour is decoration
over text that stands without it; a derivation is the item's *only* source of value, so losing it
loses the item. Same code path, opposite outcomes.

`from-derived-source` gets its own code rather than reusing `unknown-item-id` because the id is
not unknown — it exists, and this construct forbids it. §3.2.1 defers chaining behind a
topological sort, so a user who writes it has asked for something deliberately unbuilt, and a
message saying "no item named 'agent-short'" would be a lie about a config whose real problem is
that the item is there.

**The line between them is satisfiable versus unsatisfiable — not "does the renderer cope."**
That distinction cannot be the test, because §7 makes the renderer cope with everything: it
degrades rather than failing, by design. If coping meant `warning`, nothing would ever be an
`error`, including the unknown enum values ruled on below. So the question is whether a width, a
terminal, or a state exists in which the config means what it says. If none does, the user wrote
something that cannot be true, and that is an `error` however gracefully it is absorbed.

`minSize` greater than `maxSize` **is an error**, and was listed as a warning here in an earlier
draft. There is no width at which both constraints hold; the renderer clamps to *something*, but
the intent is unachievable everywhere. §9.8 gives it the code `min-exceeds-max` and this line is
the correction, not a second opinion.

**Unknown enum values are errors even though the renderer accepts them.** Same principle, and it
is not a contradiction: `--check` is diagnostic and changes no runtime behaviour, so calling a
typo an error breaks nobody's running statusline while making it visible. The renderer keeps its
fallback. Silence was the defect; the fix is a diagnostic, not a runtime change.

`--check` reports what is **invalid or unresolvable**, never what is untidy. No diagnostics for
formatting, ordering, or unused-but-valid keys. A checker that warns about things that work gets
ignored, including on the day it is right.

#### 9.4.2 Unknown *keys*, which nothing reported at all

§9.4.1 covers unknown **values** of known keys. Nothing anywhere covered an unknown **key**, and
the deserializer's default is to ignore one — so `{"item": "context", "colour": "aqua"}` parses
cleanly, renders uncoloured, and is reported by nothing. So does `"ttl": 5` for `ttlSeconds`,
`"maxLines"` for `maxRows`, and the `align` this document itself advertised on items until §3 was
walked. **Every key name typo in every config object is currently silent.**

This is the same failure class §9.4.1 was written for, in the half that was not looked at, and it
is worse in one specific way: an unknown value at least has a known key to attach a message to.
An unknown key produces a config where the *absence* of an effect is the only symptom, and
absence is what a user attributes to their own misunderstanding of the feature.

It also lands exactly where §12 is most exposed. `/edit` and the §12.6 tools have a model write
JSON and gate the write on `--check`, and **a plausible-but-wrong key is the single most likely
thing a model gets wrong** — far likelier than an unknown enum value, because the enum sets are
short and printed while the key vocabulary is long and adjacent to every other JSON schema the
model has seen. The gate is currently blind to it.

**Every config object rejects keys it does not define, as `unknown-key`, severity `warning`.**
Warning rather than error because §9.4.1's test asks whether a state exists in which the config
means what it says: the rest of the config does mean what it says, and only this key is dead.
Erroring would also make any future key addition break every older binary hard, which is a cost
with no matching benefit here.

**The message names the nearest known key for the same object** — `unknown key 'colour' on an
item — did you mean 'color'?` A code and a path identify the fault; only the suggestion repairs
it, and this is the diagnostic where the gap between the two is widest, because the user believes
they already wrote the right key.

Two consequences that must not be left implicit:

- **The known-key set is derived from the config types, not listed.** A hand-maintained list of
  valid keys is a second registry, and it fails in the direction that makes the diagnostic
  actively harmful: a newly added key missing from the list is reported as unknown on a config
  that is correct, and a warning that fires on valid input is a warning users learn to ignore.
- **§12's gate surfaces warnings, not only errors.** A model-written config that trips
  `unknown-key` is never intentionally doing so, and a gate that passes it writes a statusline
  that silently does not do what was asked. This is a requirement on §12, not a third severity.

Both bullets say what must be true and neither says how, and the first one hides a mechanism
choice with a wrong answer that passes every test.

**"Derived from the config types" cannot mean reflection here.** This binary is `PublishAot`, and
every config type is bound through a source-generated `JsonSerializerContext`
(`ConfigJsonContext`). Walking `typeof(UserConfig).GetProperties()` compiles, works in the test
host — which is not AOT — and is exactly the shape the trimmer is entitled to remove from the
published binary. The failure is silent and it is in the shipped artifact only: the known-key set
comes back short or empty, and a warning that fires on valid input is the outcome the bullet was
written to prevent, now reachable only for users and never for us.

**Ruled: the known-key set comes from the source-generated `JsonTypeInfo`** —
`ConfigJsonContext.Default.<Type>.Properties`, each entry's `Name`. This is not merely the
AOT-safe way to do it; it is the *same* metadata the deserializer binds with, so the set cannot
disagree with what actually parses. Reflection would have been a second derivation of the same
answer, and §9.3.4 already rules on what happens to those.

**Ruled: unknown keys are captured with `[JsonExtensionData]`, not by walking the JSON against a
mirror of the config shape.** A walk has to know which type each JSON node corresponds to, and
that mapping is a hand-maintained mirror of the very structure the bullet forbids hand-maintaining
— the second registry, reintroduced one level up. `[JsonExtensionData]` makes the deserializer do
the routing, which means the per-object scoping the diagnostic needs (`'colour'` is unknown *on an
item*, and must not be compared against pane keys) falls out of binding rather than being
reimplemented beside it. The cost is one property per config type, and one thing to remember:
extension data round-trips, so any path that re-emits a config must drop it rather than echo the
user's typo back as though the tool had accepted it.

**Case is already decided and the decision is load-bearing.** Every context sets
`PropertyNameCaseInsensitive = false`, so `"Color"` genuinely does not bind and reporting it is
correct rather than a false positive. The unknown-key comparison must therefore be case-sensitive
too — matching the binder, not being independently strict about it — and a case-only mismatch is
the single most valuable suggestion this diagnostic can make, because it is the one a user rereads
their config five times without seeing.

**The suggestion needs a threshold, because a confidently wrong suggestion is worse than none.**
"The nearest known key" with no bound turns `{"zzzzzz": 1}` into *did you mean 'color'?*, which
sends the user to change a key they never wrote. There are two candidate rules, and a key is
suggested when it satisfies either:

- Its edit distance from the unknown key is at most 2 **and** strictly less than half the unknown
  key's length. This catches transposition and single-character slips and refuses to reach.
- One of the two is a prefix of the other, **and the shorter of the two is at least three
  characters.** This is what catches the abbreviation class — `ttl` for `ttlSeconds` — which is a
  different mistake from a typo and which no distance bound small enough to be safe will ever
  reach.

  **The length floor is not decoration.** Without it the prefix rule has no bound of any kind,
  where the distance rule has two, and the asymmetry is not a decision anyone made — the prefix
  rule was written as an escape hatch from the distance bound and inherited none of its
  discipline. Unbounded, `{"c": 1}` qualifies against `case`, `color`, `colors`, `colorSystem`,
  and `children`, and the ranking below hands one of them over as *did you mean 'case'?* — the
  confidently wrong suggestion this threshold exists to prevent, produced by the threshold itself.
  Three is the length of `ttl`, the shortest abbreviation this section cites; below it a key is a
  fragment with no evidence about what it was a fragment of. A half-length bound mirroring the
  distance rule was considered and rejected: `ttl` against `ttlSeconds` is three against ten, and
  it would fail.

**When none qualifies, the message names no key at all.** `unknown key 'zzzzzz' on an item` is a
complete and useful diagnostic on its own; it is the code and the path doing their job, and there
is no obligation to guess on top of it.

##### Ranking, when more than one key qualifies

Qualification is two rules; ranking must not collapse them into one. **A prefix match outranks a
distance match, always, whatever the distances are.** `ttl` → `ttlSeconds` is a prefix match at
edit distance 7, and any key qualifying at distance 2 would otherwise beat it — discarding the
prefix match in precisely the case the prefix rule was written for. The rule above says no safe
distance bound reaches the abbreviation class; ranking a prefix match by edit distance ranks it on
the scale that sentence declares inapplicable to it. The two rules produce incomparable
quantities, and the ranking must compare within a class, never across.

So the order is layered, and each layer is total before the next is consulted:

1. **Class.** Prefix matches rank above distance matches. A prefix match and a distance match
   therefore never tie.
2. **Within prefix matches**, the shorter candidate wins — the one that adds least to what the
   author actually typed.
3. **Within distance matches**, the smaller edit distance wins.

**Whatever remains tied at the top is named together, not chosen between.** `unknown key 'xy' on
an item — did you mean 'ab' or 'cd'?` A tie is not the tool reaching too far — that is what the
threshold above governs — it is the tool holding two equally good answers. Naming one of them is a
coin flip presented as advice, and naming neither discards information the tool has. This binds
for ties of three or more as well, with no cap: a cap would be an arbitrary number, and falling
silent above it would make the diagnostic worse exactly as the ambiguity gets worse.

**Sorting is ordinal, and the spec says so because the language default is wrong here.** Tied
candidates are ordered with `StringComparer.Ordinal`. .NET's `OrderBy(k => k)` on strings uses
`Comparer<string>.Default`, which is culture-sensitive — so the obvious implementation of "sorted"
varies with the machine's locale and reintroduces exactly the build-to-build variability this rule
exists to remove, inside the fix for it. `ConfigCheck.cs:778` already sorts the unknown keys
themselves this way; this is the same discipline one level in.

**The suggestion list is rendered by `ConfigCheck.FormatAccepted`,** the joiner
`unknown-enum-value` already uses (`ConfigCheck.cs:464–469`): `a`, `a or b`, `a, b, or c`. Reusing
it is not a convenience — a second joiner three lines from the first is §1's defect in miniature,
and reusing it makes `unknown-key` read in the same voice as the other diagnostic a user meets in
the same run.

**This changes no JSON shape.** The suggestion is interpolated into the diagnostic's message
string at `ConfigCheck.cs:781`; `Diagnostic` carries a path, a severity, a code, and a message, and
has no suggestion field anywhere in the codebase. §9.6's diagnostic shape is untouched, and this
rule must not be taken as licence to add a structured field for it.

##### One class this diagnostic knowingly does not reach

This section opens with three motivating typos. `colour` → `color` is caught by distance;
`ttl` → `ttlSeconds` is caught by prefix; **`maxLines` → `maxRows` is caught by neither, and that
is intended.**

`maxLines` is not a typo. `Lines` and `Rows` are the same concept in different vocabulary, and the
edit distance between them is 4 — reachable only by a bound loose enough to turn `{"zzzzzz": 1}`
into *did you mean 'color'?*, which is the outcome the threshold exists to prevent. Vocabulary
confusion is a third mistake class, alongside the typo and the abbreviation, and neither candidate
rule addresses it.

The correct behaviour for `maxLines` is therefore the bare diagnostic: `unknown key 'maxLines' on
a pane`, naming no key. That is the paragraph above doing its job, not a gap in it.

**This is recorded so nobody fixes it.** The tempting repair is to loosen the distance bound until
`maxLines` is reached, which trades this section's stated principle for one example. If the
vocabulary class is ever worth catching, it needs a mechanism of its own — a synonym relation is
not a distance — and that is a new rule to be specified, not a constant to be raised.

#### 9.4.3 Why none of §9.4.1 was implementable as the code was shaped

> **Resolved, and pinned to a revision.** The code quoted below is `Pane.cs` at **`8306620`**, which
> was `HEAD` when this section was written. It is no longer the working tree: all three parsers now
> have a private `ParseCore` returning `T?`, a public `Parse` = `ParseCore(value) ?? default`, and a
> public `IsUnrecognized` calling that same core — the exact shape this section rules for, arrived at
> independently, with `ConfigCheck.CheckPaneEnums` as the second caller. Kept in the past tense
> rather than deleted, because the rule in it governs every closed set added after this one, and the
> mechanism is the part worth keeping.
>
> **Pin the revision when a spec quotes code.** This section and the session fixing it disagreed for
> a full round-trip about what the code said, and both were reading real files — one the committed
> revision, one an uncommitted working tree. That is this document's own two-authorities defect with
> the second authority being *time*, and it is the variant with no possible symptom: nobody is wrong,
> the readings just do not refer to the same thing. A quoted snippet without a revision is a claim
> about a moment that has already passed by the time anyone reads it.

§9.4.1 had been in this document for some time and reported nothing, and the reason was not that
nobody got to it. It was one line, repeated:

```csharp
public static PaneDistribute Parse(string? value) => value?.Trim().ToLowerInvariant() switch
{
    "min-rows" => PaneDistribute.MinRows,
    _          => PaneDistribute.Greedy,     // <- the diagnostic dies here
};
```

`PaneAlign.Parse`, `PaneValign.Parse`, and `PaneDistributeParsing.Parse` were all **total functions
into the enum**. There is no value such a function cannot answer, so by the time any caller holds
the result the fact that the input was not in the language has been destroyed — not lost in transit,
*consumed*, by the one function positioned to notice it. `--check` cannot report what it cannot be
told, and no amount of care in `--check` recovers it, because the information is gone before
`--check`'s code runs.

`OverflowMode.Parse` in the same codebase returned `OverflowMode?` and answered `null`. **Both
shapes were already here, and the three keys §9.4.1 singles out as failing silently were exactly the
three with the total shape.** That is not a coincidence to note; it is the mechanism, and it means
these were one defect with three instances rather than three defects.

So the rule, and it governs every closed set including ones added later:

**A parse for a closed value set does not choose the fallback.** It reports the value as
unrecognised — `null`, or a discriminated result — and the *caller* decides what to render. The
renderer's caller still substitutes the default, so §7's "cope with everything" is untouched and
nothing about the rendered output changes. `--check`'s caller reports `unknown-enum-value`. One
function, two callers, two behaviours, which is the arrangement that makes both possible at once.

The inverse arrangement — a total parse plus a separate validation pass that re-examines the raw
strings to decide what was legal — is the two-authorities defect this document keeps finding, and
here it would be the worst instance of it: the validator's idea of the language and the parser's
idea of the language would be two lists, and the way you would discover they had drifted is a
`--check` that passes a config the renderer then ignores.

This is also the load-bearing half of §2.3's `distribute` correction. Adding `even` and `greedy` to
that `switch` fixes one key on one day; changing the shape is what stops the next key from arriving
pre-broken.

#### 9.4.4 Modes are exclusive by construction, not by enumeration

§9.6.3.1 says `--colors` is mutually exclusive with `--check`, `--items`, and `--version`. True, and
unmaintainable: it is a list of pairs written when there were four commands, it does not mention
`--preview`, and the reason it does not is not oversight — it is that nobody adds a row to a
pairwise table when they add a command. Four commands is six pairs and five is ten. §1's failure,
relocated into argv.

**`--check`, `--version`, `--items`, `--colors`, and `--preview` are modes. Exactly zero or one may
appear; two or more is exit 2.** Zero is the render path — not a special case but the definition:
the renderer is what the binary does when no mode was selected, which is why the render path takes
no flag of its own and why §9.1 can call it untouched.

Stated that way the rule holds for the sixth command without being edited, and §9.6.3.1's list
becomes a consequence rather than a second authority.

**`--json`, `--columns N`, and `--config <path>` are modifiers, not modes**, and the question they
raise is not exclusivity but applicability. **A modifier the selected mode does not read is exit 2,
not ignored.** Silently accepting `--check --columns 80` tells a user they checked at 80 columns,
which §9.8 says is not a thing `--check` can do; the flag was typed because someone believed
something false, and the exit code is the only chance to say so. This is §2.3.2's
`key-not-applicable` diagnostic, one surface out — with the difference that argv has no severity
axis, so what is a warning in config is a usage error here.

Which modifier applies where is not a new list either. `--config` is read by the modes that load
config — `--check`, `--preview`, **and the render path**. `--columns` is read by the mode that
renders, which is `--preview` alone.

> **Corrected.** This sentence used to derive that set from "which modes can reach exit 3", giving
> `--check` and `--preview` and silently excluding render. That derivation was already wrong when
> written and became load-bearing once §9.2.1 shipped: the render path reads config and, when it
> cannot, exits **0** with a diagnostic row rather than exit 3. Exit-3 reachability was never the
> property being asked about — *loading config* is — and picking the wrong discriminator excluded
> the one mode whose config-reading §9.2.1 exists to specify. Building the table from the corrected
> rule immediately surfaced a live regression that the old wording would have ratified. The general
> form: **a derived list is only as good as the property it is derived from, and a derivation that
> happens to produce the right answer today is not thereby correct.** §9.4.4's whole argument is
> that derived beats enumerated; it does, and this is the failure mode that buys — an enumeration
> is wrong visibly, a bad derivation is wrong with a reason attached. `--json` is read by the
modes §9.6 gives a JSON shape to — `--check`, `--items`, `--colors`, `--preview` — and `--version`
has none, so `--version --json` is exit 2. Every one of those is a lookup in a section that already
had to be right for another reason.

### 9.5 `--check` reuses `ReferenceExtractors`

Every diagnostic about an id — an unknown item, a `from` naming nothing, a `link` placeholder
that resolves to nothing, an argv placeholder that does — is derived from the **same**
`ReferenceExtractors` table §5 uses to build the resolution set. `--check` does not get its own
config walk.

Sharing the walk is not sharing the verdict. Severity is assigned per construct by §9.4's rule,
so the same dangling id is a warning in a `link` and an error in a `command` item's argv — §4.2
works that difference through. The extractor answers *which ids this config names*; nothing more.

This is not tidiness. Defect 11 was a resolution set that had fallen behind the config surface,
and it was invisible because nothing cross-checked it. A second walk in the checker recreates
exactly that: a `--check` that passes while the resolver drops an id, disagreeing silently, in
the one tool whose entire job is to not be silent. Adding a reference form must remain a single
append that both the resolver and the checker inherit.

#### 9.5.1 Sharing a walk makes both sides wrong together, and three things above are not true yet

§9.5 is seventeen lines that assert an arrangement rather than specify one, and all three of its
load-bearing claims fail against the code as it stands. Worse, its central argument is inverted: two
walks that drift apart *disagree*, and a disagreement is findable. One shared walk that falls behind
the config surface makes the resolver and the checker wrong **in the same direction, simultaneously,
and in agreement** — `--check` passes, the id resolves to nothing, and §7 renders it as absent.
Sharing is still right, but it is not the safety property §9.5 claims. It concentrates the risk into
one table, and the table therefore needs a guarantee §9.5 never gives it.

**`--check` cannot reuse it, because it is private.** `ReferenceExtractors` is a `private static`
member of `ItemValueResolver` (`ItemValueResolver.cs:138`), with its only caller in the same file.
The heading states as settled fact a thing the access modifier forbids. This matters more than a
missing keyword usually does: an implementer building `--check` finds the shared table unreachable
and the second walk trivial, so the arrangement this section exists to mandate loses on convenience
at the exact moment it is being decided. Widen the member deliberately, and treat its accessibility
as part of the ruling rather than an implementation detail beneath it.

**The extractor's return type cannot carry the verdict §9.5 promises.** The declared shape is
`Func<ScanContext, IEnumerable<string>>` — bare ids. But §9.5's second paragraph says the same
dangling id is a warning in a `link` and an error in a `command` item's argv, and §9.4's diagnostics
name the offending key by JSON Pointer. A `string` supports neither: by the time the checker holds
it, the construct it came from and its position in the document are both gone. So the sentence
*"the extractor answers which ids this config names; nothing more"* reads as a boundary and is in
fact the defect — it was written to keep **verdicts** out of the extractor, which is correct, and it
took provenance out with them, which is not. The extractor must not decide severity; it must report
what severity is a function of, because it is the only code that knows. Yield a record carrying the
id, the construct that named it, and the JSON Pointer to it. The resolver then selects the id and
discards the rest, which is what makes sharing cheap rather than a compromise.

**Nothing enforces the invariant, which is stated as an instruction to a person.** *"Adding a
reference form must remain a single append"* is addressed to whoever adds the next one, and Defect
11 is the proof that this instruction has already been missed once — that is the whole reason this
section exists. There is no coverage test. Everywhere else this document confronts the same problem
it refuses a hand-maintained list and makes the type system do the enumerating: §9.4.2 hands unknown
keys to `[JsonExtensionData]` rather than a table of valid ones, §9.6.1 derives the code registry
from the types, §9.6.2.2 asserts drift rather than trusting agreement.

Do the same here, and note the shape carefully: a test cannot prove *"every reference-bearing field
is covered"* without already knowing which fields those are, which is the registry it is trying to
avoid. So invert it. Walk the config types, collect every member that could name an id, and require
each to be **either covered by an extractor or on an explicit exemption list**. A newly added field
is neither, so it fails the build and forces its author to classify it. That is the property worth
buying: not that coverage is complete today, but that it **fails closed** — the next Defect 11
announces itself at the commit that causes it rather than in a statusline that renders short. A test
that can only pass by someone remembering something is the sentence again, compiled.

##### What "covered" means, and what this check cannot prove

Written against the code at `4ef9c7d`; every claim below was verified by reading `src/ClaudeTuiLine/`
directly, and the line citations are as of that commit, subject to the re-pointing rule in *What must
not change*, item 4 below.

A first pass at the coverage test proposed reflecting over `PaneItem`, `ColorExpr`, and `ColorRule` —
the three types `ScanContext` exposes — with a filter picked by inspection rather than stated as a
rule, yielding 16 candidates split 6 covered / 10 exempt. Checked against the code, that classification
has one defect and one omission, both worth recording because a fail-closed test is only as good as
what it fails closed *over*.

###### The candidate filter, stated

A count is only meaningful if the rule that produced it is written down. "Plausible reference-carrying"
is a judgement, and a judgement re-made per member is how a real reference gets waved through. The rule
is mechanical: for each root type, and transitively for each type reached from one —

1. A member whose type is a record declared in `ClaudeTuiLine` — or a collection of one — is
   **recursed into**, and is not itself a candidate.
2. A member whose type is declared *outside* the `ClaudeTuiLine` assembly is not recursed. It gets an
   explicit exempt row naming the foreign type, so it is visible rather than silently absent. (Today:
   `PaneBorder.Style`, a Spectre.Console `BoxBorder?`.)
3. Otherwise, a member is a **candidate** iff its type transitively contains `string` — a `string`, a
   `string?`, a collection of `string`, a dictionary keyed or valued by `string`, a tuple containing one.
4. Everything else — `bool`, `int?`, every enum — is out of scope and needs no row.

Rule 3 is deliberately wider than "is a string": a future `IReadOnlyDictionary<string, string> Args`
would carry references and must land in the candidate set without anyone deciding it should. Enums are
excluded by rule 4 rather than exempted by hand — an enum's values are a closed set fixed at compile
time and cannot name an item.

When a root or reached type is abstract, reflect over the abstract type's own declared members **and**
each concrete subtype's. `ColorExpr` today declares nothing of its own and each of
`Literal`/`TokenRef`/`Inline` declares only its own positional property, so nothing is lost today — but
a member added to the `ColorExpr` base later would be invisible to a walk that used `DeclaredOnly` on
the subtypes alone. Walking both is one extra line and removes the footgun permanently.

###### Roots: `Pane` and `PaneBorder` must be included

The proposed root set excluded `Pane` on the grounds that "no extractor bucket reads a `Pane`-level
property directly; `Pane` is pure tree-navigation that `Walk` consumes." The first clause is true and
the second is false. `Pane.cs:18` —

```csharp
public sealed record PaneBorder(ColorResolution.ColorExpr Color, BoxBorder? Style);
```

— and `Pane.cs:149` gives every `Pane` a **non-nullable** `PaneBorder Border`. A pane's border carries
a full `ColorExpr`, which may be a `TokenRef` (an `@name` colour-token reference) or an `Inline` rule
whose `From` is an item-id reference. `Pane.Border` is not navigation; it is a reference-carrying
member.

`Walk` handles it correctly today — `ItemValueResolver.cs:110` adds `pane.Border.Color` at
`<path>/border/color` before touching items, and the recursion at `:126` carries that to every child.
**There is no live bug.** The defect is in the test, not the scanner: with `Pane` excluded, the
candidate set can never grow when a reference-carrying field is added to `Pane` or `PaneBorder` — and
those are precisely the types where §3.3's compound items and any future pane-level feature will land.
The test would stay green across exactly the change it exists to catch, in the corner of the model most
likely to change.

Adding both roots costs two new exempt rows and buys the coverage:

| Member | Disposition | Reason |
|---|---|---|
| `Pane.Size` | exempt — never a reference | A size *form* (§4.1): an integer, a percentage, or one of `content`/`fill`/`auto`. Never an id. |
| `Pane.Ellipsis` | exempt — never a reference | Literal display text substituted on truncation. |
| `Pane.Border` | recursed | → `PaneBorder` |
| `Pane.Items` | recursed | → `PaneItem` |
| `Pane.Children` | recursed | → `Pane` |
| `PaneBorder.Color` | recursed | → `ColorExpr` |
| `PaneBorder.Style` | exempt — foreign type | Spectre.Console `BoxBorder?`; not recursed per filter rule 2. |

`Pane`'s remaining members — `Split`, `Overflow`, `MaxRows`, `MinSize`, `MaxSize`, `Gutter`, `Valign`,
`Align`, `Distribute` — are enums and numerics, excluded by filter rule 4.

###### Covered (7) — confirmed

These map one-to-one onto the five `ReferenceExtractors` and the two `ColorTokenExtractors` entries at
`ItemValueResolver.cs:140–189`, verified against each extractor body:

| Member | Extractor | Form |
|---|---|---|
| `PaneItem.Id` | #1 | `Declaration` |
| `PaneItem.Item` | #1 | `ItemSelector` |
| `PaneItem.From` | #2 | `DerivedFrom` |
| `PaneItem.Link` | #3, via `LeafContent.LinkPlaceholderIds` | `LinkPlaceholder` |
| `ColorRule.From` | #4 (inline) and #5 (`colors`-table token) | `ColorFrom` |
| `ColorExpr.TokenRef.Name` | colour-token extractor | `ColorTokenReference` (top/border position, unrestricted) |
| `ColorValue.TokenRef.Name` | colour-token extractor | `ColorTokenReference` (rule-branch position, restricted to constant tokens — SPEC-44-color-token-in-rule-branches.md §3.2/§4.3) |

Note on `ColorRule.From`: `Config.cs:865` parses an inline rule as
`new Inline(ParseColorRule(rule, rule.From ?? owningItemId))`, so `From` is populated with the owning
item's id even when the config omits it — extractor #4 then yields that id and it must resolve. For a
border, `Config.cs:828` passes `owningItemId: null`, so a border's `from`-less rule yields no candidate.
That is the behaviour `ColorResolution.cs:30` already documents, and it is correct.

###### Exempt — seven confirmed, one rejected

**`PaneItem.Command` is not exempt.** The proposed reason was "a literal/config value, not a
reference." `ItemValueResolver.cs:135–139` says the opposite in the codebase's own words:

```csharp
// Adding a form (§4.2's argv placeholders, §3.3's compound-item parts) means appending here,
// not editing either consumer.
```

`Command` **is** argv. §4.2's placeholders will land in exactly this member. Filing it under the same
undifferentiated "exempt" bucket as `PaneItem.Case` means that when §4.2 ships, nothing prompts anyone
to revisit the row — the test stays green across the one change it was built to catch. See below for
the fix.

The other seven hold, several for stronger reasons than first given — the reason is the durable part of
a row; "a config value" tells a future reader nothing they can check:

| Member | Reason |
|---|---|
| `PaneItem.Format` | A format string substitutes `{}` with the item's **own** value and nothing else — `LeafItems.cs:53` is a literal `.Replace("{}", value)`, not a pattern match. Contrast `Link`, whose `LeafContent.cs:22` regex `\{([^{}]*)\}` matches `{other-id}` and *is* a reference form. `LeafContent.cs:65–67` states outright that a `format` string has no bearing on the URL. |
| `PaneItem.Extract` | A regex pattern applied to the item's own value (`ExtractValue`). Never an id. |
| `PaneItem.Case` | One of a closed set of case-transform tokens. Never an id. |
| `ColorExpr.Literal.Spec` | `ColorResolution.cs:59` returns it as a spec. Anything `@`-prefixed became a `ColorExpr.TokenRef` at parse time (`Config.cs:862–863`) and is therefore already covered. |
| `ColorValue.Literal.Spec` | Same species, at the rule-branch leaf: a literal colour spec; anything `@`-prefixed became a `ColorValue.TokenRef` at parse time (`Config.cs:872–873`) and is therefore already covered. See SPEC-44-color-token-in-rule-branches.md §4.1. |
| `MatchRule.Contains` | A predicate over an item's **value**, not over ids. |
| `MatchRule.EqualsValue` | Same. |

`ColorRule.Default`, `ThresholdRule.Color`, and `MatchRule.Color` are **not** exempt rows — each is
typed `ColorValue`/`ColorValue?`, so a walk recurses into `ColorValue` rather than treating the member
itself as a candidate (SPEC-44-color-token-in-rule-branches.md §4.3):

| Member | Disposition | Reason |
|---|---|---|
| `ColorRule.Default` | recursed | → `ColorValue` |
| `ThresholdRule.Color` | recursed | → `ColorValue` |
| `MatchRule.Color` | recursed | → `ColorValue` |

The two colour-valued literal strings share one reason, worth stating once, in the generalized form of
SPEC-44-color-token-in-rule-branches.md §4.1: **every colour-valued string passes through one of exactly
two sigil-inspecting parse sites — `ParseColorExpr`'s (`Config.cs:862–863`, producing `ColorExpr.TokenRef`)
or `ParseColorValue`'s (`Config.cs:872–873`, producing `ColorValue.TokenRef`) — so any string still
holding an `@` prefix at runtime remains a literal, not a reference.** That single fact is what makes
both safe. It used to be the fact that would change if anyone ever taught a rule branch to accept a
token; SPEC-44-color-token-in-rule-branches.md is exactly that change, and the fact survives in this
generalized two-site form rather than being lost — see that spec for the full model
(`ColorValue`, the constant-token restriction, and the new `non-constant-color-token` diagnostic).

**Note against `ColorResolution.cs:139`'s `StandardThreshold`.** `ResolveStandardThreshold`
(`ColorResolution.cs:152–153`) assumes every branch of `StandardThreshold` is a `ColorValue.Literal` —
it pattern-matches on `Literal` and falls back to a bare `"green"` otherwise. That is safe only because
`StandardThreshold` is a fixed, source-constructed ladder with no `TokenRef` branches. If
`StandardThreshold` ever becomes config-driven, a `ColorValue.TokenRef` could reach that line and would
silently coerce to `"green"` instead of resolving through the colour-token table — revisit that line
first (already flagged in-source at `ColorResolution.cs:144–150`).

###### Exemptions come in two kinds

A single `exempt` bucket cannot distinguish "this can never be a reference" from "this is not a
reference *yet*", and that difference is the entire value of the row. Require two:

```csharp
enum ExemptionKind
{
    // The member's contents can never name another item or colour token,
    // by the shape of the value itself.
    NeverAReference,

    // The member will become a reference form under a spec section not yet
    // implemented. Carries that section so whoever implements it is routed here.
    PendingForm,
}
```

Every `PendingForm` row carries the spec section that will make it live. Today there is exactly one:

| Member | Kind | Spec |
|---|---|---|
| `PaneItem.Command` | `PendingForm` | §4.2 — argv placeholders |

`ItemValueResolver.cs:138` also names *§3.3's compound-item parts*. That form has no member to hang a
row on yet; when §3.3 introduces one it lands as a candidate by construction under the filter above, and
must be classified then.

The enforcement test asserts every candidate is covered, `NeverAReference`, or `PendingForm`. Whether it
can additionally fail on a `PendingForm` row whose named section has since landed is NEEDS-EVIDENCE
below; if not cheaply detectable, the row's presence in the test source is still the marker, and §4.2's
definition of done must include "reclassify `PaneItem.Command`".

###### The standing limit: fail-closed over members, fail-open over sites

Reflection over the types `ScanContext` exposes proves that **every reference-carrying member of a
scanned type is handled**. It does not — and cannot — prove that **`Walk` visits every place those
types live**. The test would be exactly as green today if `ItemValueResolver.cs:110` were deleted and
pane border colours were never scanned at all. That is not a flaw to be fixed by widening the roots; it
is a different question. Member coverage is a property of the type graph; site coverage is a property of
a hand-written traversal body. Reflection sees the first and is blind to the second.

Pair the coverage test with a cheap, deterministic complement that pins the traversal: scan a fixture
config exercising, in one document, a top-level pane border colour, a nested child pane's border colour,
an item-level `color`, and a `colors`-table token, and assert the set of JSON-Pointer paths in the
resulting `ScanContext.ColorExprs` equals an expected literal set. That anchors
`ItemValueResolver.cs:110`, `:120`, and the recursion at `:126` — the three statements reflection cannot
see — with no new machinery. A deleted or mis-pathed walk site fails it immediately.

The registry form of `Walk` — declaring its walk sites as data, a table of `(container type, member
selector, path segment)` that `Walk` iterates, at which point reflection could assert site coverage too
— is the durable answer for when `Walk` grows a fourth and fifth site, which §3.3 will supply. It is not
mandated here: `Walk` is 20 lines with three sites today, and the fixture assertion covers it at a
fraction of the cost. Recorded so the choice is a decision rather than a drift.

###### What must not change

1. **`Walk`'s behaviour.** It is correct as written. Nothing above licenses editing
   `ItemValueResolver.cs:108–128`; the tests are being fitted to the scanner, not the reverse.
2. **The extractor tables' shape.** `ReferenceExtractors` and `ColorTokenExtractors` stay two separate
   tables. `ItemValueResolver.cs:177–180` already rules on why, and that ruling stands: `IdCandidate`
   must never have to mean "an id or a colour-token name depending on which `Kind` this is."
3. **The six covered rows.** They are the extractor table's contents. If a covered row and an extractor
   ever disagree, the extractor is right.
4. **Line citations.** Every `ItemValueResolver.cs:NNN` and `Pane.cs:NNN` reference above is invalidated
   by construction if that code is restructured. Any task that restructures cited code must include
   re-pointing these citations in its definition of done.

###### Verification

1. The enforcement test enumerates candidates from roots
   `{Pane, PaneBorder, PaneItem, ColorExpr, ColorValue, ColorRule, ThresholdRule, MatchRule}` by the
   filter above, and every candidate is covered, `NeverAReference`, or `PendingForm`.
2. Deleting a `ReferenceExtractors` row fails the test for every member that loses **all** its
   covering rows, naming each. This is per-member, not per-row: a row may carry more than one member
   (row 1 carries both `PaneItem.Id` and `PaneItem.Item`), and two rows may deliberately cover the
   same member from different sources (rows 4 and 5 both carry `ColorRule.From` — the inline and
   `colors`-table cases — so deleting either alone leaves that member covered by the other and does
   not fail the test). The property that must hold is that no member is ever silently left uncovered
   by every surviving row, not that each row maps to exactly one failure.
3. Adding a `string? Foo` to `PaneItem`, to `Pane`, **or to `PaneBorder`** fails the test until
   classified. The `PaneBorder` case is the one the original root set could not catch and is the reason
   this section exists.
4. Adding a `string` member to the `ColorExpr` **or `ColorValue`** abstract base fails the test (guards
   the `DeclaredOnly` footgun above). `ColorValue` is a second abstract root with nested concrete
   subtypes, same shape as `ColorExpr` (`Literal`/`TokenRef`) — the walk-both-declared-and-subtype rule
   above applies to it unchanged.
5. The exemption table distinguishes `NeverAReference` from `PendingForm`, and `PaneItem.Command` is the
   latter, citing §4.2.
6. The fixture assertion passes, and fails if `ItemValueResolver.cs:110` is deleted.
7. `tools/check-all.sh` green.

###### NEEDS-EVIDENCE

- **Can a `PendingForm` row detect that its spec section has landed?** Check whether §4.2's
  implementation introduces a distinguishable marker the test could assert on — a `ReferenceForm` enum
  member, a new extractor count, anything. If yes, add the assertion to Verification item 5. If no, drop
  it and put "reclassify `PaneItem.Command`" into §4.2's definition of done instead. Either outcome is
  acceptable; leaving it undecided is not.
- **Does the candidate filter above actually yield 16 on the current code?** Run the enumeration and
  print the candidate list. Expected: the 16 already identified, plus `Pane.Size`, `Pane.Ellipsis`,
  `PaneBorder.Style` — 19. A different number means the filter as written does not match the traversal
  as implemented, and the filter text is what must be corrected, not the number.
- **Does `BoxBorder` recursion terminate if filter rule 2 is dropped?** Only matters if someone later
  argues foreign types should be recursed. Not blocking.

### 9.6 JSON shapes

`--json` applies to `--check`, `--items`, `--preview`, and `--colors`.

```json
{ "ok": false,
  "diagnostics": [
    { "path": "/surface/pane/children/1/items/0/item", "severity": "error",
      "code": "unknown-item-id", "message": "no item named 'gitbranch'" }
  ] }
```

```json
{ "columns": 112, "usableColumns": 109,
  "rows": [ { "text": "…", "width": 109 } ],
  "notes": [ { "message": "pane 2 dropped: children need 120 columns at 109 columns" } ] }
```

`--preview --json` returns each row's text **and** its measured width — a model parsing rows should
not have to strip decoration to get them, and the width is the number that makes overflow visible
rather than inferred. This sentence used to draw that contrast against "printing widths in a gutter
as the human form does," which was the only description of the human form anywhere and was wrong;
§9.3.3 rules the bare form and says why the gutter could not have been right. `notes[]` carries what §9.8 assigns to `--preview` and to nothing else — what was
dropped or truncated at this width; §9.8.1 rules its shape and why it is not `diagnostics`.

**`code` values are a compatibility surface.** Once a code ships, its meaning is fixed; a new
condition gets a new code rather than a widened old one. `/edit` and the §12.6 tools branch on
these, and a code that quietly changes meaning makes every consumer wrong at once.

**`--json` emits JSON at every exit code, including 2 and 3.** The tempting reading of §9.4 is
that exit 3 means nothing could be checked, so there is nothing to serialize, so the failure goes
to stderr as prose. That hands a caller who explicitly asked for JSON a non-JSON stdout in exactly
the case it most needs to branch — and `/edit` and §12.6 reach `--check --json` on configs a model
just wrote, which is where unparseable is a *likely* outcome rather than a remote one. A flag that
guarantees a format except when something goes wrong does not guarantee a format.

So exits 2 and 3 emit the failure envelope, which is the shape §9.6.1's second table already
defines for invocations that failed rather than for places in a config:

```json
{ "ok": false, "code": "config-unreadable",
  "path": "/Users/x/my.json",
  "message": "unexpected ',' at line 12" }
```

**`diagnostics` is absent, not empty.** `[]` is the answer to "I checked and found nothing", which
is the opposite of what happened, and a consumer that tests `diagnostics.length === 0` would
report a broken config as clean. `path` here is a **filesystem path**, not the JSON Pointer that
entries in `diagnostics` carry — the two never appear in the same object, which is what keeps that
from being ambiguous.

Exit 2 uses the same envelope with `code: "usage"`. Note that both are already true of the human
form, which prints prose to stderr in these cases; `--json` is the surface that needed the ruling
because a program cannot fall back to reading English.

**`path` is absent for `code: "usage"`, for the same reason `diagnostics` is.** A bad flag is not
about a file, so there is no path to report — and `""` is not that statement, it is the claim that
the path is the empty string. It survives a null check, it concatenates, and a caller that formats
`could not read ${path}` prints `could not read ` with no indication anything is missing. The
envelope's fields are present when they have an answer; the schema is not a fixed set of slots to
fill. This is the identical ruling one field over, and getting the two different would be worse
than getting either wrong, because a consumer that learned `diagnostics`' rule would reasonably
assume it generalises.

The exception, when a usage error *is* about a file — `--config` given a path plus an unrecognised
flag — is to report the usage error without the path anyway. Exit 2 says the invocation was never
run; naming a file we never opened invites the reader to go look at it.

**Exit codes outside {0, 1, 2, 3} mean claude-tui-line itself failed.** `--check` deliberately has
no catch-all around its own execution: an internal exception must not be caught and reported as a
clean config, which is §7.1's render-wrong class in the one command whose entire purpose is
detecting defects. But the consequence needs saying out loud, because the alternative to swallowing
is a runtime's own exit code and a stack trace on stderr, and that is a value §9.6.1's registry does
not define. A caller testing `exit == 0` handles it correctly by accident. A caller switching on
0/1/2/3 falls through the bottom, silently, in the one case that most needs to be loud. So: the four
codes are the *contract*, not the range. Anything else is a crash, is a bug in this tool rather than
in the config, and `--json` makes no promise about stdout in that case — a process that died did not
finish writing.

#### 9.6.1 The code registry

**This table is the registry. A code that is not in it does not exist**, and a new condition adds
a row here in the same change that specifies it. That rule is not ceremony: writing it out found
that the two *largest* error classes in §9.4 — every unknown enum value and every unknown colour
name, which between them are the whole reason §9.4 exists — had no code string anywhere, while
§3.3 already referred to "the existing `unknown-item-id` and unknown-colour codes" as though one
had been defined. Scattered across six sections, that is invisible. Gathered into one table it is
the first thing you notice.

Severity is fixed per code, and that is what lets a consumer branch on `code` alone. Where one
condition would otherwise carry two severities in two constructs, it is two codes (§9.4.1).

**Config diagnostics** — entries in the `diagnostics` array, each with a JSON Pointer `path`:

| code | condition | severity | §  |
|---|---|---|---|
| `unknown-enum-value` | a value not in the language for **any key with a closed value set** — `split`, `size`, `distribute`, `style`, `align`, `valign`, `overflow`, `case`, `colorSystem`, and any added later. The message names the accepted set | error | 9.4.1 |
| `unknown-color` | colour name matching no palette entry | error | 9.4.1 |
| `overflow-forbidden-position` | `overflow: "overflow"` where §2.6 forbids it | error | 9.4.1 |
| `unknown-item-id` | `{ "item": … }` selector, derived `from`, or argv `{id}` naming nothing | error | 9.4.1 |
| `from-derived-source` | derived `from` naming another derived item — present, but forbidden here | error | 9.4.1 |
| `from-compound-source` | item-level or part-level `from` naming a compound item — a compound writes no value into the resolution dictionary, so this can never work | error | 3.3 |
| `part-compound-source` | a compound part's `item` naming another compound — one compound inside another is the nesting §3.3 forbids | error | 3.3 |
| `color-from-compound-source` | a colour rule's `from` naming a compound — a compound has a colour per part and no single value for a rule to read; the rule falls through to its default colour | warning | 3.3 |
| `unknown-link-target` | `link` `{other-id}` naming nothing; link dropped, text survives | warning | 9.4.1 |
| `unknown-color-source` | colour rule `from` naming nothing, inline or in the `colors` table | warning | 9.4.1 |
| `unknown-color-token` | `@name` naming no entry **in the `colors` table** — a different namespace from item ids | warning | 6.3 |
| `command-shape` | one-element argv containing whitespace, without `shell: true` | error | 4.1 |
| `command-shell-argv` | `shell: true` with more than one argv element; arguments would be dropped | error | 4.1 |
| `placeholder-derived-source` | argv `{id}` placeholder naming a derived item | error | 4.2 |
| `placeholder-command-source` | argv `{id}` placeholder naming another `command` item | error | 4.2 |
| `placeholder-env-collision` | two ids mangling to one `CLAUDE_TUI_LINE_VAL_<ID>` under `shell: true` | error | 4.2 |
| `placeholder-self-reference` | bare `{}` in a `command` item's argv — an item asking for its own not-yet-produced output | error | 4.2.1 |
| `unknown-key` | a key no config object defines, silently dropped by the deserializer; message names the nearest known key | warning | 9.4.2 |
| `key-not-applicable` | a known key with a legal value on a node that never reads it — `distribute` or `gutter` on a horizontal split, `items` alongside a non-empty `children`, an empty `children` list, or an explicit `split` on a childless pane; message says where the key *does* apply. A predicate, not a closed list (§2.3.2) | warning | 2.3.2 |
| `part-source-count` | a compound part with zero, or more than one, source | error | 3.3 |
| `part-forbidden-key` | a compound part carrying `parts` or `link` | error | 3.3 |
| `fixed-sizes-exceed-parent` | declared fixed sizes or floors cannot fit the parent at any width | error | 9.8 |
| `min-exceeds-max` | `minSize` greater than `maxSize` on one pane — unachievable everywhere | error | 9.8 |
| `collapsed-edge-conflict` | adjacent panes disagree about a shared edge under `border.collapse` | warning | 2.10 |
| `collapse-not-surface-level` | `border.collapse` declared on a pane — the compositor resolves one grid for the whole surface, so a per-pane value has no defined meaning | error | 2.10.1 |
| `border-inside-on-leaf` | `"border": "inside"` on a leaf pane — a leaf has no interior, so this silences its border entirely | warning | 2.10.1 |
| `color-down-converted` | a literal whose minimum colour system (§6.2.1) exceeds the resolved `colorSystem` — it is approximated to the nearest colour the resolved palette has. The message names *which* palette, since that is not always the sixteen | warning | 6.2.1 |
| `deprecated-size-alias` | `size: "auto"`, which resolves to `fill`. Warned rather than accepted silently because "auto" reads as "size to content", naming a *different value that exists*. The message must say it resolved to `fill` and name `content` as the other candidate — an author told only "deprecated" re-spells it `fill` and keeps the layout they did not want | warning | 2.3 |
| `leaf-only-key-on-split` | `overflow` or `ellipsis` declared on a split — only leaf panes consult them and they do **not** inherit, so the declaration does nothing. Exactly those two keys; `align`/`valign` are not in scope for this code | warning | 2.6 |
| `pane-no-items` | a `content` or `fill` pane declaring no items **and no explicit `minSize`** — it collapses, so the declaration did nothing. **Not** `fixed`/`percent`, nor a `content`/`fill` pane with a `minSize`: all three hold their extent and are legitimate spacers (§2.11.1) | warning | 9.4 |

**Tool-protocol codes** — a different channel, and consumers must not confuse the two. These
appear as a top-level `{ "ok": false, "code": … }` describing a failed *invocation*, never as an
entry in `diagnostics` describing a place in the user's config. They have no `path`, because
there is no config position to point at:

| code | condition | § |
|---|---|---|
| `config-unreadable` | the config could not be read or parsed, so nothing could be done with it — exit 3. `path` is a filesystem path, and `diagnostics` is **absent** rather than empty | 9.6 |
| `usage` | unknown flag, missing argument, or mutually exclusive flags — exit 2 | 9.6 |
| `stale-revision` | the config changed under a §12.6 tool between read and write | 12.6 |
| `cli-not-found` | the binary could not be located; the error names every path tried | 12.6 |

### 9.6.2 `--items`: two sections, and why the keys are not per-row

This section replaces the field list this bullet used to carry. That list named five things —
"every item's id, what it reports, its default format, whether its colour is decorative or
semantic (§6), and which config keys it accepts" — and checking it against `ItemRegistry.cs`
found that two of the five were wrong to ask for. Both are worth recording, because one of them
is the §1 failure appearing *inside* the paragraph that warns about it.

**The shape.**

```json
{
  "version": "…",
  "items": [
    { "id": "git-branch", "reports": "the current branch, or nothing outside a repo",
      "color": "decorative", "default": true, "example": "feat/eng-1234" }
  ],
  "kinds": {
    "builtin":  { "required": ["item"],    "optional": ["format", "color", "overflow", "link"] },
    "derived":  { "required": ["id", "from"], "optional": ["extract", "case", "format", "color", "overflow", "link"] },
    "command":  { "required": ["id", "command"], "optional": ["shell", "ttlSeconds", "timeoutMs", "format", "color", "overflow", "link"] },
    "compound": { "required": ["id", "parts"], "optional": ["color", "overflow", "link"] }
  }
}
```

#### 9.6.2.1 What each item reports

`reports` is the only field on a row that is not derived from something. `id`, `color`, and
`default` read straight off `ItemRegistry`, and `example` is produced by *running*
`BuildDefaultSegment` against §9.3.1's fixture — the `"main"` in the shape above is an
illustration of that output, not a string stored anywhere. `reports` is prose, it is written once
here, and the eighteen strings are:

| id | `reports` |
|---|---|
| `directory` | the working directory |
| `git-branch` | the current branch, or nothing outside a repo |
| `repo` | the workspace repo as `owner/name` |
| `worktree` | the worktree's name when the session is in one, plus its branch when showBranch is enabled |
| `pr` | the pull request number and its review state |
| `model` | the model's display name |
| `model-short` | an abbreviated model name, for panes too narrow for the full one |
| `effort` | the reasoning effort level |
| `thinking` | whether extended thinking is on |
| `output-style` | the active output style |
| `context` | how much of the context window is in use. Its colour follows that percentage through the configured thresholds, so it warms as the window fills |
| `rate-limits` | usage against the five-hour and seven-day limits. Its colour follows the *higher* of the two through the thresholds, since the nearer limit is the one that will stop you |
| `agent` | the name of the active agent, when the session is running one |
| `engram` | recent Engram memory activity. Its colour reflects whether the store is reachable and active rather than a magnitude, so it is a state indicator and not a gauge |
| `vim` | the current vim mode, when vim mode is enabled |
| `remote-url` | the git remote's URL. Opt-in rather than default because resolving it shells out to git |
| `repo-host` | the host the workspace repo lives on, from the session payload rather than a git probe |
| `linear` | the Linear ticket id extracted from the current git branch, uppercased; links to the issue when `itemSettings.linear.workspace` is set |

The three with a second sentence are the `Semantic` ones (§6). For a decorative item the colour is
the author's choice and needs no explanation; for these three the colour *is* information, and a
row that describes the text while leaving the colour unexplained gives an authoring tool the
smaller half. `rate-limits` taking the higher of two windows and `engram` being a state rather than
a magnitude are both facts you would otherwise have to read the implementation to learn, and both
change what a sensible `thresholds` override looks like.

**If the implementation disagrees with one of these strings, that is a finding, not a string to
quietly correct.** `reports` states what the item is *for*; the builder states what it currently
does. Where they differ, one of the two is wrong and which one is a judgement call — silently
rewording the table to match the code converts every behavioural drift into documentation, which is
the failure this document spends §1 on. Raise it.

That rule paid for itself immediately, and against this document rather than the code. Three
passages in this spec asserted that `git-branch` emits a `⎇` glyph: the shape example above, this
paragraph's description of it, and — worst — the argument below for why `example` replaced "default
format", which used the glyph as its *proof* that a builder emits decoration no format string could
express. `BuildGitBranch` is `SingleColor("green", branch)` and always was. **CAPTURE.md settles
it** — the bash statusline the tool is parity-checked against renders segment 2 as a bare branch
name in green, so the code is correct and the glyph was never anywhere but here.

The generalisable part is not the wrong character. **An illustration invented to explain a rule was
later cited as evidence for a different rule**, and by then nothing marked it as invented. The
argument for `example` over "default format" survives on `worktree`, which really does emit
`worktree:NAME(BRANCH)` from C# — but it survives by luck, because the example it actually rested
on was false. Neither `check-citations.sh` nor `check-counts.sh` can catch this: both check
documents against themselves, and this is a claim about the code. **An example that names a
specific behaviour is an assertion about the implementation and ages exactly like one**, which is
the argument for keeping worked examples few, real, and re-checked whenever the section is
reopened.

`tools/check-examples.sh` now catches precisely this instance — it runs `--items --json` and
compares. It is scoped to the eighteen builtins' default renders (§9.6.2.2), so it retires the
specific trap this glyph fell into without retiring the general warning above: every example
outside that scope is still an unverified assertion, and this paragraph is still the reason to
treat it as one.

**Why `kinds` is a section and not a column.** The accepted keys do not vary by item id. Every
builtin takes `format`, `color`, `overflow`, and `link`, and nothing else; what varies is *how the
item is written* — §4.1's `command`, §4.3's `from`/`extract`/`case`, §3.3's `parts`. Putting a key
list on each row would store one per-kind fact seventeen times and grow an eighteenth copy with the
next item added. That is precisely the drift the very next paragraph of §9 warns about, and it
would have been committed by the sentence above it. The corroboration that two sections is right
was already in the document: §12.6's `list_items` row says the tool must return "what each emits,
what options it takes — **and the schema for defining a new one** (§4.1)". A per-row key column
cannot express "the schema for defining a new one", because a new one has no row yet.

**Why "default format" is gone.** There is no default format string to report. A row's rendering
is a builder function (`ItemDefinition.BuildDefaultSegment`), not a template — `worktree` emits
`worktree:NAME(BRANCH)` from C#, and no `"worktree:{}({})"` exists anywhere to print. Satisfying
the field as written
would mean introducing a format-string layer under every builder so that a CLI flag has something
to name, which is the CLI dictating the render architecture to serve its own output. `example`
replaces it and answers the question an authoring tool actually has — *do I need to add a `format`
of my own, or does this item already carry its own decoration?* — which a template would answer
only indirectly. `--items` has no stdin payload, so the example is rendered against a canned
`ItemContext`, and that is the reason the field is honest: it is the same `BuildDefaultSegment`
the renderer calls, not a string re-typed into a table.

**That `ItemContext` is built on §9.3's synthetic payload and must not be a second constant.**
This paragraph originally said "one canned synthetic `ItemContext` fixture" and left it there,
which was this document's own §1 failure committed inside the section correcting one — §9.3
already defines a fixed, non-randomised synthetic `StatusInput` for `--preview`, and an `example`
built from a different one would show an item one way under `--items` and another under
`--preview`, with `/migrate` reading both as authority in the same session. So: **one
`StatusInput` constant**, and `--items` wraps it with canned values for the fields `ItemContext`
adds beyond the payload — `GitBranch`, `Engram`, `RemoteUrl`. Those three are the only new
constants, and they exist because those values come from probing the machine rather than from
stdin, which `--items` must not do.

**Two fields the old list omitted.** `reports` and `default` were both missing and are both
load-bearing. `reports` is the description, and it belongs on `ItemDefinition` as a **required**
positional field rather than a lookup table beside it — required is the whole point, because it
makes "add a row without describing it" fail to compile instead of silently shipping an item that
`--items` announces as a bare id. `default` distinguishes the items in the default pipeline from
`model-short` and `remote-url`, which are opt-in (`ItemRegistry.DefaultIds` already knows). The
flag is what a consumer reads; the size of either group is not reported and must not be inferred
from the row count of one `--items` run (§4). §12.2's migration depends on that bit: an item that will not render unless explicitly
placed is a different mapping decision from one that appears on its own, and a tool that cannot
tell them apart will map a branch readout onto `remote-url` and produce a config that renders
short with nothing wrong in it.

#### 9.6.2.2 What the shape did not say: bare `--items`, and `version`

§9.6 says `--json` applies to `--check`, `--items`, `--preview` and `--colors`, which promises each
of them a non-JSON default — then demonstrates one only for `--preview`. The implementor correctly
declined to invent the other, and left `--items` unwired rather than guess. Ruled here.

**Bare `--items` prints a table, and it is a view of `ItemsCommand.Build()`'s result rather than a
second walk of the registry.** One id column, one example, one `reports`, in two labelled groups:

```
Default items — rendered unless you remove them:
  directory     acme-web                            the working directory
  git-branch    feat/eng-1234                       the current branch, or nothing outside a repo
  …

Opt-in items — rendered only where you place them:
  model-short   Sonnet 5                            an abbreviated name, for panes too narrow …
  remote-url    https://github.com/acme/acme-web    the git remote's URL. Opt-in because …
  linear        ENG-1234                            the Linear ticket id extracted from …

Item kinds: builtin, command, derived, compound. Run with --json for the schema of each.
```

Those example values are the §9.3.1 fixture's, rendered — `directory` is `Basename(cwd)`, which is
why it reads `acme-web` and not the fixture's full `/home/you/code/acme-web`.

Four rulings in that, none of them about formatting:

- **The groups are labelled by what the flag means, not by its name.** "Default items" as a bare
  heading tells a human nothing; `default: true` means *this renders whether or not you name it*,
  and the person reading the plain form is exactly the person who does not know that yet. The JSON
  keeps the boolean, because a model reading `default` does not need it explained.
- **No key lists, no per-kind schema — one pointer line to `--json` instead.** §4.1's command-item
  schema does not fit a terminal, and a truncated schema is worse than an absent one because it
  reads as complete. The pointer names the four kinds so the reader knows what they are missing.
- **The example column prints `Plain`, never markup, and never ANSI — including when stdout is a
  TTY.** A colour-when-interactive rule would make this two output surfaces that drift, for a
  benefit `--preview` already delivers properly. This output gets piped into `grep` and it should
  be the same bytes either way.
- **The plain form is a convenience view; the JSON is the contract.** Columns may be added, dropped
  or re-widened without that being a compatibility break, and §9.6's stability guarantees do not
  extend here. Stated so it does not silently become a second frozen surface — which is what
  happens to every human-readable output nobody labelled.

**Once this flag exists, it is the oracle for every item example in this document — and that is
mechanically checkable.** §9.6.2.1 says a spec example naming a specific rendered value is an
assertion about the implementation that no document-versus-document check can verify. `--items`
closes exactly that gap for the eighteen builtins: its `example` field is `BuildDefaultSegment` run
against §9.3.1's fixture, so a check that runs `--items --json` and greps this document for
example values that disagree is a document-versus-*code* check, which is the class §13.3's two
checks cannot reach. It is worth building *because* the alternative already failed three times in a
single session (see STATUS.md) — the last two of them inside the edit that documented the failure.
Writing an illustrative value is frictionless; verifying it means reading a builder. Nothing but a
machine closes that gap.

**It exists: `tools/check-examples.sh`,** the third check, and the only one that runs the code. It
enforces four rules, all exact, none guessing at what a line "looks like":

- a `"example": "…"` **inside a fenced block** must be a value some item renders;
- inside a fenced block that reproduces `--items` plain output — identified by its own trailing
  `Item kinds:` pointer line, not by resembling a table — a row whose first column is a known item
  id must carry that item's live example in its second;
- a markdown table preceded by a line that is **nothing but** an `items-table` HTML comment must
  **enumerate the live set exactly** — no id absent, none listed that no longer exists, and each
  row's `(opt-in)` marker agreeing with that item's `default: false`;
- a prose **count** of the builtins — "all N built-in items", "the N builtins" — must
  be the live count. The numeral must be immediately followed by `builtin(s)` or `built-in item(s)`,
  which is what keeps "the nearest of the sixteen" (the ANSI palette, closed by the standard, not by
  this registry) and SPEC.md's "14 built-in segments" (v1's concept, true in past tense) out of it.
  STATUS.md is exempt: it is append-only, and a check that demands a retrospective be rewritten to
  stay green teaches people to rewrite retrospectives.

The fourth rule exists because a count is the one claim that decays with nobody editing anything.
Whoever adds item seventeen has no reason to look upstream at a sentence written when there were
sixteen, and the sentence has no way to notice it was overtaken. §9's own opening carried exactly
that failure — "v2 needs three more" above a list of four, itself missing `--version` — and survived
four flags shipping.

The third rule checks a different thing from the first two: they ask whether a documented *value*
is real, and it asks whether a documented *list* is complete, which no per-row check can see. The
README's item table is what it was written for. §9 forbids an item list in a skill or command's
prose and both prompts say not to copy one out of the README — so nothing automated trusts that
table, and it stays, because it is what a person reads before deciding to build. Not-forbidden is
not checked, though, and it asserts two things the binary knows. The marker must be an HTML comment
because prose has no in-band string to anchor on the way `--items` output does, and the README's
other tables must stay unscanned.

Columns are split on runs of two-or-more spaces, so the padding widths in the illustration above
are not load-bearing. Pinning them would freeze the half this section explicitly left free.

**Rule 1 is fenced-only, and the first run is why.** It flagged STATUS.md's sentence recording that
the `⎇ main` example had been *removed* — correct by the letter, wrong by the point. A retrospective
that may not quote the value it is retiring cannot describe a defect at all, and a check that says
so gets switched off within a week. The distinction that survives is that **a fenced block asserts
and prose discusses**; the original defect was in a fenced shape, so nothing was given up. That the
check's very first execution produced a false positive rather than a finding is the more useful
result: the class it guards is narrow, and the cost of drawing it one notch too wide is the check
itself.

**Rule 3 had to learn the same lesson, and the way it surfaced is worth keeping.** Its marker test
was a substring test — "the line contains `<!--` and contains `items-table`" — and the bullet three
paragraphs above, the one *documenting rule 3*, quotes the marker inside a sentence. So the check
read its own specification as a marker, opened a table scan, found no table under it, and reported
this file as omitting every item in the registry. **A check that cannot survive being described is a
check nobody can write documentation for**, which is a slower version of getting switched off. The
fix is rule 1's distinction again, in the shape prose markers need: the marker counts only when the
**whole line is the comment** — opens with `<!--`, closes with `-->`. README.md's marker carries a
trailing note inside the comment, so this could not be an exact match on the marker alone.

That this went unnoticed is the second finding. `check-examples.sh` is deliberately outside
`check-docs.sh` (it needs a binary), and for most of its life its only runner was GitHub Actions'
`build` job — which never executed on this repository, Actions never having been billed. So between
the day rule 3 was documented and the day something ran it, the only check that compares
documentation against code was failing and nothing said so. The exclusion from `check-docs.sh` is
still right. The conclusion is that **a check whose only runner is an unproven one is not yet in
service**, no matter how correct the check is.

That conclusion is what eventually retired the workflow rather than the check. Keeping a runner that
had never run made the repository *look* covered, which is worse than looking uncovered; the checks
moved to `./tools/check-all.sh` — `check-docs.sh` then `check-examples.sh`, both run even when the
first fails. Clone-and-build-locally is the supported story (§14.3), so the gate has to be runnable
that way. `CLAUDE_TUI_LINE_BIN` still points it at an existing build to skip the `dotnet run`.

If it cannot obtain a binary, or the item list comes back empty, it exits 2 and says so — never a
clean report it did not earn. That is what keeps it honest about the toolchain it needs, and it is
load-bearing in a way it was not before: the old `spec`/`build` job split enforced the distinction
structurally, and now nothing does but this exit code.

**`version` carries the same string `--version` prints**, read from the assembly through the one
`AssemblyVersionInfo.InformationalVersion` accessor, never re-derived. §9.7 already makes the
assembly the source of truth for the tool's version; the only thing left to rule was whether
`--items` gets to answer differently, and it does not. A consumer that reads `version` out of
`--items --json` and compares it against `--version` must never see two answers — that is the
§9.7 drift test's whole premise, one surface lower.

### 9.6.3 `--colors` prints a recommendation, because the accepted set is not ours and is not finite

This bullet used to say "print every accepted colour name". It cannot, and the reason is
structural rather than a matter of effort.

**There is no colour registry in this codebase, and there should not be one.**
`ColorResolution.ResolveLiteral` is two lines: it hands the string to
`Spectre.Console.Style.TryParse` and takes the resulting `Foreground`. The accepted set is
therefore Spectre's entire table — roughly 256 named colours — plus `#rrggbb` hex, which is
infinite. No file in `src/` enumerates the sixteen; they are named only in prose. `--colors`
consequently has nothing to enumerate, and the two ways of giving it something are both wrong:

- **Hardcode our own list of accepted names.** This is a second registry of a table we do not
  own, drifting against a third-party library on every upgrade — worse than the §1 case, because
  no amount of care on our side keeps it true.
- **Reflect over Spectre's `Color` statics.** Same table, so no drift — but it is reflection over
  a library type in a Native AOT binary, and printing 256 swatches answers a question nobody
  asked. A person scanning for a colour is not helped by 256 of them.

So `--colors` prints a **curated recommendation**: the sixteen theme colours plus `default`,
`dim`, and `bold`. That list is a genuine new thing and must live in exactly one place in code,
because it exists nowhere today.

**Its correctness condition is testable, and the test is the point.** A curated list can rot in a
way a derived one cannot — a Spectre upgrade could rename a colour and the list would go on
recommending it, with the failure landing on the user as a silently uncoloured item (§7). So every
entry is asserted to round-trip: each name through `ResolveLiteral`, non-null result. The list is
allowed to be hand-written precisely because that test refuses to let it be wrong.

**`--colors --json` must say which set it is.** This matters more than it sounds, and it is the
same failure as §9.6.2's missing `kinds` section wearing different clothes. §12's authoring tools
treat `--colors` as authority the way they treat `--items`, and §12.3 instructs the migrator to
preserve colours "by name, from `--colors`". A bare list of nineteen names reads as exhaustive, so
a model asked for `#ff8800` — which parses fine and always has — will consult the list, not find
it, and refuse or silently substitute. The output therefore carries the distinction explicitly:

```json
{
  "recommended": [ { "name": "olive", "themeMapped": true } ],
  "alsoAccepted": "Any Spectre.Console color name (256-palette, e.g. deepskyblue1) or #rrggbb hex.
                   These parse everywhere a name is accepted; how faithfully they render depends on
                   `colorSystem` (§6.2), which defaults to `standard` and approximates them to the
                   nearest of the sixteen. config-check flags this against your actual colorSystem
                   with its color-down-converted diagnostic.",
  "palette": [ { "number": 0, "name": "black", "themeMapped": true } ]
}
```

**`palette` (task #93) carries the full 256-colour table, `recommended` unchanged.** `recommended`
stays curated and frozen at its current nineteen — it means "reach for these", and 240 colours
approximated under the default `colorSystem` are not that. `palette` is a new sibling array: every
entry Spectre's 256-colour table has, in palette-index order (`number` 0–255), each carrying its
`name` and whether it is one of the sixteen ANSI standard colours (`themeMapped`, computed by set
membership against the same constant `recommended` uses, never by `number <= 15`). Because the
sixteen standard names *are* indices 0–15, index order alone puts them first — no separate ordering
rule is needed. `palette` is enumerated from `Spectre.Console.Color.FromInt32(i).ToString()`, never
hardcoded and never built by reflecting over `Color`'s static properties — reflection returns 291
aliased entries (`fuchsia`/`magenta` and similar), not 256 canonical ones. `--colors`' bare form
lists all 256, index and name per line, the same way it already lists the sixteen: a bare numeric
index is itself a usable colour spec, so showing it is a second spelling, not decoration. Full
design in SPEC-93-colors-256-palette.md.

This `alsoAccepted` text describes how a *configured* `colorSystem` renders a value in the
statusline itself — it says nothing about how `--colors`' own swatches render, and deliberately:
the swatches always render at `standard` (§9.6.3.1 below), independent of any `colorSystem:
truecolor` a project has configured, because `--colors` predicts the render path's *default*, not
a caller's override.

`themeMapped` is the reason to recommend these nineteen at all, and it is the honest form of the
advice: the sixteen follow the user's terminal theme, so a statusline built from them stays
readable when the theme changes, and one built from hex does not. That is a recommendation with a
stated reason, which a model can weigh against a user who explicitly wants `#ff8800` — unlike a
bare list, which it can only obey or violate.

#### 9.6.3.1 What this section did not say: the bare form, and what the round-trip test proves

§9.6.2.2 exists because §9.6.2 specified a JSON envelope and left the bare form to whoever got
there first. This section had the identical hole, and one consequence sharper than anything in
§9.6.2.2's case.

**Bare `--colors` prints ANSI. It is the deliberate exception to §9.6.2.2, and the reason is not
symmetry but payload.** §9.6.2.2 rules that bare `--items` emits `Plain` only, never styled, even
on a TTY. Read next to each other those two rulings contradict; read for what each command is
*for*, they do not. An item's example is a string, and colour is decoration applied to it — a
reader loses nothing when it is stripped, and a pipe gains a clean value. A colour swatch has no
payload except the colour: strip the ANSI from `--colors` and every row reads `olive`, `teal`,
`fuchsia`, which is precisely the guessing §9's bullet says the command exists to end. So `--colors`
renders each name in its own colour, through the user's terminal, because a swatch in documentation
shows the author's theme and not the reader's.

This is written down because the collision is live rather than theoretical: the plain-only rule and
this one land on the same implementer within days of each other, the plain-only rule is stated
absolutely, and it is the one they will have implemented most recently. **Two authorities
disagreeing is worse than either being wrong alone** — the recurring defect of this project — and
the resolution is always the same: find the principle that makes both follow, rather than letting
one claim an exception. The principle here is *is the styling the value, or a coat of paint on the
value?*

`--colors --json` stays unstyled, per §9's own bullet — a program consuming the list wants names.

**"Through the user's terminal" governs `Ansi` support specifically — whether escapes are emitted
at all — and not the terminal's real capability along every axis, nor the forced-ANSI console the
renderer uses.** Bare `--colors` writes through an auto-detecting console, which degrades to bare
names under a pipe or a redirect. The render path deliberately builds its own console with
`Ansi = AnsiSupport.Yes` because the statusline's one consumer is Claude Code, which captures
stdout through a pipe and still expects styling — that is a workaround for a specific caller, and
exporting it to a human-facing flag would generalise a special case into a default. A colour-listing
command that ignores what the terminal reports is also the one command that has no business
overriding it.

**The colour system is a second, independent axis, and it is pinned rather than detected.**
`AnsiConsoleSettings` separates *whether* to emit escapes (`Ansi`) from *which encoding* to emit
them in (`ColorSystem` — 4-bit indexed, 8-bit, or 24-bit truecolor). Bare `--colors` auto-detects
the first and pins the second to `ColorSystemSupport.Standard` — the same colour system §6.2
defaults to for the render path. A terminal that negotiates truecolor (e.g. via `COLORTERM`) would
otherwise let the sixteen names render as raw RGB, bypassing the terminal's own ANSI palette
remapping entirely — defeating this section's reason for recommending them, since the theme-mapping
property belongs to indexed SGR, not to the names themselves. `tools/colors.sh:21,23` fixes the
sixteen names' SGR codes (`30`–`37`, `90`–`97`); bare `--colors` must reproduce them exactly.

The degraded case is not a payload loss, because the payload has a correct destination one flag
away: `--json` is the form for a consumer that is not a terminal, and it is the contract. Forcing
ANSI would have no escape hatch at all, which is the asymmetry that settles it — a user who wants
colour through a pipe can ask for it by not piping, and a user who wants the names in a file has
`--json`. Whatever the library does with `NO_COLOR` and friends is what this command does; a
convention that spans every tool on the machine is not one to reimplement here.

**The list already exists in code.** This section says the curated list "exists nowhere today" and
"must live in exactly one place in code". Both were true when written; the first is now stale.
`ColorResolution.StandardColorNames` is that constant, already serving §6.2.1's minimum
colour-system check, and `--colors` is its second consumer rather than its author. Adding a second
list here would be the §1 defect this section spent four paragraphs arguing against.

**The round-trip test proves less than this section claims, and the gap is exactly on the three
entries that are not colours.** The stated condition is "each name through `ResolveLiteral`,
non-null result". `ResolveLiteral` returns `style.Foreground`, and Spectre's `Style.Foreground` is
a non-nullable `Color` — the `(Color?)null` cast on the failure branch is the tell. So the
assertion is only ever testing *did `Style.TryParse` succeed*, which is genuinely what is wanted
for the sixteen: a Spectre rename makes the parse fail and the test catches it. But `bold` and
`dim` parse as **decorations**, contributing `Color.Default` as a foreground, and `default` names
that value outright. For those three, non-null passes and proves nothing.

So the assertion splits by what the entry is:

- the **sixteen theme colours** must parse *and* yield a foreground that is not `Color.Default` —
  the stronger form, and the one that actually catches a rename;
- **`default`, `dim`, `bold`** must parse, and are asserted to be exactly the three entries **of
  this command's nineteen** that resolve to `Color.Default`. Pinning the count is what stops a real
  colour quietly joining them through a future rename, which is the failure the weaker assertion
  would have waved through.

That scope is load-bearing and the sentence above is wrong without it. It is a claim about the
curated list, **not** about Spectre's parser: `Style.TryParse` accepts every decoration keyword the
library has, and each of them parses to a `Color.Default` foreground for exactly the reason `bold`
and `dim` do. Written as a claim about the parser it is false, and an implementer who tests it that
way — enumerating what parses rather than iterating the nineteen rows — gets a failure that looks
like a defect in this ruling and is not. Iterate the list.

The list is `ColorResolution.StandardColorNames` (sixteen, exactly the ANSI palette, closed by the
standard rather than by Spectre's version) plus the three named here. Nineteen is that sum and not
a number to hard-code — in the test or in this paragraph, which is why the sentence above names the
sum rather than repeating the nineteen.

**Confirmed against the library, and the reasoning holds.** This paragraph read "confirm the
nullability against Spectre before relying on the reasoning above" — it was read off a cast rather
than off the library, which is precisely the standard of evidence §9.6.2.1 warns about. Checked
against the pinned 0.57.2: `Style` is a value type, `Foreground` reflects as a non-nullable
`Spectre.Console.Color`, and `ResolveLiteral`'s `(Color?)null` on the failure branch is therefore
the only thing making "non-null" mean anything at all — exactly "did `TryParse` succeed", and
nothing more. §6.6 records what the same run found about decorations, which is more than this
section needed and settles a defect two sections away.

**`themeMapped` is false for `default`, `dim`, and `bold`.** They are not theme-mapped colours;
they are a reset and two decorations. Marking them `true` to keep the rows uniform would assert
they follow the terminal theme, which is the one thing the field means.

**The remaining four, ruled here so they do not each become a separate stop:**

- `--colors --json` **carries the same top-level `version` field** as `--items --json`, from the
  same `AssemblyVersionInfo.InformationalVersion` accessor. §12.6.8 surfaces `cliVersion` from
  whichever call a tool happens to make, and a field present on one JSON command and absent on
  its sibling is a distinction no consumer can act on.
- `--colors` is **a mode, so it is exclusive with every other mode** — exit 2 alongside any of them.
  This bullet used to enumerate `--check`, `--items`, and `--version` by name, which was true when
  those were all there were and silently stopped being the whole list the moment `--preview` was
  added. §9.4.4 states the rule the enumeration was standing in for.
- It **reads no config and probes nothing**, so it cannot reach exit 3 and always exits 0 — the
  same standing as `--items` in §9.6.2.2. It renders at §6.2's *default* colour system
  (`standard`), never a configured one — see §9.6.3.1's colour-system-pinning paragraph.
- The plain form is a **convenience view; the JSON is the contract.** Column layout may change
  without that being a compatibility break. Stated for the same reason as §9.6.2.2's version of
  this ruling: a human-readable output nobody labels becomes a frozen surface by default.

### 9.6.4 `--schema --json` — the config schema, aggregated and structural (task #84)

`--schema --json` is the fourth sibling in this family, and the one that finally delivers what
§9.6's `--accepted` naming discussion (above) said `--schema` promises: types, nesting, and
required-ness for the config document itself, not just item kinds or accepted tokens. Full design
in SPEC-84-mcp-schema-explorer.md; this entry records the shape as a public surface, per that spec's
D5.

The envelope embeds `--items --json`, `--colors --json`, and `--accepted --json`'s own results
**verbatim** — the same record instances those commands' `Build()` methods return, never a
re-derived copy — under `items`, `colors`, and `accepted` respectively, plus two new sections:

- **`kindSupport`** — one entry per item kind (`builtin`, `derived`, `command`, `compound`), each
  carrying `supported: bool` and `unsupportedKeys: string[]`, computed by diffing the kind's
  advertised `required ∪ optional` keys against `PaneItemJsonConfig`'s real `[JsonPropertyName]`
  properties. This is why `compound` reports `supported: false` today (SPEC-3.3) and will flip to
  `true` automatically once task #85 adds `parts` to the model — no coordination and no code change
  in this command.
- **`structures`** — twelve hand-authored entries, one per structural node type in the config
  document (`config`, `border`, `borderEdges`, `layout`, `surface`, `pane`, `item`, `colorRule`,
  `threshold`, `match`, `colorExpr`, `compoundPart`), each naming the `Config.cs` record it
  describes (`null` for `compoundPart`, which has no backing type yet), its wire keys, and a minimal
  worked example.

Like `--accepted`, it reads no config and probes nothing, so it always exits 0; bare `--schema` (no
`--json`) is a usage error naming `--schema --json`, exit 2, for the same reason `--accepted` requires
`--json` (§9.6.3's discussion, restated in SPEC-84-mcp-schema-explorer.md §5.4): requiring `--json`
now, while the surface has no users, keeps a future plain-text form purely additive. `--schema` is a
mode, exclusive with every other mode, per §9.4.4.

**This is a public, frozen surface from its first release** (SPEC-84-mcp-schema-explorer.md §9 Q2,
D5), the same tier as `--accepted --json`: its key names, and the `structures` entry shape, are a
compatibility commitment — adding a section or field later is additive, renaming or removing one is
breaking.

### 9.7 `--version`, and the two places a version number wants to live

`--version` prints the version and exits 0. `--items --json` carries the same string as a
top-level `version` field, which is what §12.6.8 surfaces as `cliVersion` — an MCP server that
spawns a possibly-stale binary needs to be able to say which binary it got, and a model
diagnosing "I set that and nothing happened" should be able to see a version without a second
call.

The hazard is that a version number now has **two** homes: the `.csproj`, which the assembly is
built from, and `.claude-plugin/plugin.json`, which is a hand-written manifest and cannot be
generated. That is a second registry in the §1 sense, and it will drift — silently, because
nothing reads both.

Rulings:

- The **`.csproj` is the source** for what the binary reports. `--version` returns the assembly's
  informational version. It must not be a string literal in `Program.cs`, and it must not be read
  from `plugin.json` at runtime: an AOT binary published into `${CLAUDE_PLUGIN_DATA}/bin` has no
  guarantee the manifest is adjacent, and a version that reads as empty off a missing file is
  worse than one that is merely stale.
- **The `.csproj` must therefore declare a `<Version>`, and today it does not.** This is not a
  detail of implementation. With the element absent, MSBuild supplies `1.0.0` — silently, with no
  warning, and indistinguishable from a deliberate choice — while `plugin.json` says `0.1.0`. So
  as things stand, naming the `.csproj` as the source of truth names a file that does not contain
  the value, and `--version` would ship reporting a number that is not this project's, which is
  the precise outcome the ruling below exists to prevent. A defaulted version is worse than a
  stale one: stale means a real number from a real release, and `1.0.0` here means nothing at all.
- A **test asserts the two match**, comparing the assembly version against `plugin.json`'s
  `version`. This is the whole mitigation, and it is cheap: without it the drift is invisible
  until a user reports a version that does not correspond to anything. It also fails immediately
  on the bullet above, which is the intended behaviour — the first thing it catches is the
  undeclared default, before any release exists to be confused by it.

There is no third home. `.claude-plugin/marketplace.json` carries no version, and must not gain
one: a marketplace entry is a pointer to the plugin, and duplicating the version into it would
add a copy this test does not cover, which is how the two-registry problem returns wearing a
third name.

Consistent with §12.6.8, this is reporting, not gating — nothing refuses to run on a mismatch.
The test fails the build; the binary does not fail the user.

### 9.8 `--check` is width-independent, and what "cannot fit" therefore means

§9.4 lists *a pane whose fixed sizes cannot fit its parent* as an `error`, which invites an
implementation that runs the real `SizeResolver` at the current `COLUMNS` and reports when the
drop-and-retry path would fire. **That is the wrong reading**, for two independent reasons, either
of which is sufficient.

**`--check` never consults a width.** Not "no `--columns` override" — it does not read `COLUMNS`,
and it does not resolve sizes. §12.6's `validate` tool calls it from a stdio MCP server, a process
with no terminal at all (§12.6.2, §12.6.3). A width-dependent `--check` would there validate
against a width that does not exist, and would return a verdict that changes depending on which
terminal happened to spawn the client. A validator whose answer depends on the caller's window is
not a validator.

**Degrading at a narrow width is designed behaviour, not a defect.** The whole §2 ladder — wrap,
then truncate, then drop — exists so that a config too big for the current terminal produces a
sensible line rather than a broken one. Reporting that as an `error` flags a config that is
working exactly as specified, and §9.4 already ruled that a validator which warns about things
that work correctly gets ignored on the occasions it is right.

So the diagnostic is **structural**, and the invariant is narrow but real: a contradiction no
terminal width can resolve.

- Children's **fixed** sizes, plus the boundary cost, exceeding the parent's own **bounded** size
  — where bounded means the parent is itself fixed, or carries a `maxSize`.
  Code: `fixed-sizes-exceed-parent`.

  **The check calls `SizeResolver`'s own boundary-cost function. It does not compute one, and it
  does not transcribe a formula out of §2.10.** That rule earns its keep twice over. Gutters and
  border reserve are not independent addends — under `collapse: true` the divider *occupies* the
  gutter — so a hand-written sum double-counts and reports a config that fits as one that cannot,
  and a false `error` is the worst outcome available here, because exit 1 sends the user to fix
  something that already works. And §2.10 itself carried a wrong `collapse: false` formula for
  some time, under-reserving by 2 columns at every `N`; a checker that had been built from the
  prose would have inherited the error and disagreed with the renderer in the one tool whose job
  is to agree with it.

  So the width the check compares against is the renderer's, computed by the renderer's code, with
  the parent's **declared** size or `maxSize` standing in for the terminal width. That substitution
  is what keeps this width-independent: same function, a number from the config rather than from
  `COLUMNS`.

  This is computable without a width because §2.10 makes edges **static config**, never derived
  from a provider value. That rule was written for the §2.3 fixpoint's convergence argument, and
  it is what makes this diagnostic possible at all.
- `minSize` greater than `maxSize` on the same pane. Code: `min-exceeds-max`. This is an
  **error**; §9.4 listed it as a warning in an earlier draft and has been corrected there rather
  than here.
- Children's `minSize` sum, plus **the same boundary cost**, exceeding the parent's own **bounded**
  size — bounded in the same sense as the first bullet: the parent is itself fixed, or carries a
  `maxSize`.
  Same code as the first: it is the same contradiction with the floor rather than the exact size —
  and therefore the same arithmetic, taken from §2.10. Two bullets computing one boundary two ways
  is how the double-count gets back in through the door the bullet above just closed.

**The arithmetic form follows the axis, not the size key.** Where a split's children *share* the
constrained axis they contend, and the check is a **sum** plus the boundary cost — the three
bullets above. Where each child receives the parent's **full** extent — a declared-horizontal
split, for as long as §2.8 is unimplemented — there is no contention and therefore no sum, and the
check is **per child**: any single child whose own declared fixed size, **or whose own declared
`minSize`**, exceeds the parent's bound is impossible on its own. Both per-child cases are the same
contradiction as the first bullet and carry the same code, `fixed-sizes-exceed-parent`. There is no
boundary cost in the per-child form: each child receives the full extent, so no divider is
reserved, and adding one would reintroduce exactly the double-count the first bullet's rule closes.

Where the parent is `fill` or `content`, there is no bound to contradict and `--check` says
nothing. That is not a gap — there is genuinely no width-independent claim to make, and inventing
one would produce exactly the noise the previous paragraph rules out.

**The width-dependent information is real and belongs to `--preview`.** "At 80 columns this config
drops your right pane" is worth knowing; it is simply not a config error. `--preview` has a width
by construction, so it reports what it dropped or truncated at each width it rendered — as a note
alongside the rows, never as a diagnostic and never affecting the exit code.

#### 9.8.1 Render notes are a channel, and `--preview --json` was missing it

The paragraph above says "as a note alongside the rows", and §9.6's `--preview --json` shape has
`columns`, `usableColumns`, and `rows` — nowhere to put one. §12.6.9 later adds `diagnostics[]`,
which is a different thing. So the human form of `--preview` reports what it dropped and the JSON
form does not, and the JSON form is the one a model reads. The information §9.8 has just argued
belongs to `--preview` and to nothing else would be available only to the caller who does not need
it programmatically.

**`--preview --json` gains `notes[]`**, and render notes become one named channel with two
renderings: the human form prints them to stderr, the JSON form carries them in `notes[]`. §4's
`maxLines` cap notice is a note in exactly this sense and is reported through this mechanism rather
than through an arrangement of its own.

```json
{ "columns": 112, "usableColumns": 109,
  "rows": [ { "text": "…", "width": 109 } ],
  "notes": [
    { "message": "pane 2 dropped: 23 columns is under its 24-column floor at 109 columns" },
    { "message": "item 'diffstat' emitted 7 lines; 3 kept (maxLines)", "item": "diffstat" }
  ] }
```

Three rulings on the shape, each closing a way this could have become the thing it must not be:

- **A note has no `code`.** §9.6.1's registry exists so a consumer can *branch* on a fault, and
  there is no branch to take on "this got truncated at 109 columns" other than showing it to a
  person. Giving notes codes would grow a permanent compatibility surface for no consumer, and
  §9.6.1's "a code that is not in it does not exist" would then demand registry rows for facts
  that are not faults.
- **A note never appears in `diagnostics`, and a diagnostic never appears in `notes`.** They answer
  different questions — "your config is wrong" versus "here is what happened at this width" — and
  merging them makes a config that is working exactly as §2's ladder specifies read as broken. That
  is the failure §9.4 names when it says a validator that warns about things that work gets ignored
  on the occasions it is right.
- **Notes never affect the exit code**, restating §9.8 above so that the JSON form cannot quietly
  acquire a different rule from the human one.

**The pinned texts.** §9.3.4 rules that a note is an interface the moment a prompt tells anyone to
read it, and that every note the collector can emit therefore has its text pinned here rather than
living only at the `Add` call site. `migrate.md` teaches a model to tell a width drop from a
`maxLines` cap by reading these strings; an unpinned one gets quoted into the next prompt and then
drifts with nothing failing. Placeholders are written `{like this}` and are substituted at the call
site:

<!-- pinned-notes: checked against the collector's call sites by tools/check-notes.sh -->
```
pane {n} dropped: {grant} columns is under its {floor}-column floor at {columns} columns
pane {n} dropped: children need {sum} columns at {columns} columns
pane {n}: {requested} columns requested, clamped to {avail} at {columns} columns
pane {n}: flex split stacked; children need {sideBySideFloor} columns at {columns} columns
segment truncated to fit {columns} columns
item '{id}' emitted {n} lines; {kept} kept (maxLines)
```

The block above is checked mechanically, because "adding a producer means adding a line here" is
an instruction to a future editor and every other instruction of that shape in this document has
already decayed once. `tools/check-notes.sh` reads every `RenderNoteCollector.Add` call site in
`src/` and fails if its text is not in the list, and `check-docs.sh` runs it — a check whose only
runner is an unproven runner is not in service, which §9.6.2.2 learned the expensive way.

The first two are the live producers §9.8.2 adds — two messages, not one, because §2.3's drop-retry
loops fire for two different reasons (SPEC-2.3-drop-predicate.md #67b): a pane granted less than its
own drop floor, and a split whose children together claim more than it has. The two repairs differ
(lower `minSize` or widen the terminal, versus shrink the declared fixed/percent sizes), so naming
the wrong one sends the reader to the wrong knob — see that spec's §4 for the ruling and the
precedence rule when both conditions hold on the same drop. The third line is §4.0.1's and does not
fire until `maxLines` exists. Adding a producer means adding a line here in the same change — this
list is the definition, and the call site is a use of it.

Two things about the wording are deliberate rather than incidental. Neither pane-drop message says
*no width remained* — that phrasing was ruled inaccurate for both cases it used to cover: a pane
under its floor was not granted zero, and an over-allocated split's children claimed too much, not
too little. The segment-truncation note keeps its own, different wording for the same reason: width
plainly remained there too — there were `{columns}` of it — and it was merely insufficient, which is
a third distinct cause from either pane-drop reason. And every text names the width it happened at,
since a note that cannot be tied to a width is unusable in a tool whose entire subject is what
changes with width.

#### 9.8.2 A note channel with no producer, and the collector that fixes it

§9.8.1 established that render notes are a channel and that `--preview --json` was missing it. It
then illustrated the channel with two cases, and **neither of them can produce a note today.**

- The `maxLines` cap is the example §9.8.1 leans on hardest, and §4 now records that the feature it
  reports on does not exist — `command` items are single-line, there is no cap, so there is nothing
  to be capped and nothing to say about it. §4.0.1 settles what it will be when built, and rules
  that it never fires unasked — which is what keeps this note rare rather than routine.
- Pane-dropping and segment truncation *do* happen — `SizeResolver.AllocateWithDrop` drops panes
  below their floor or over budget, `PaneRenderer` truncates segments — and at the time this section
  was written, both were **silent**: no signal reached any caller, and no code could emit the
  pane-drop message §9.6's own JSON example showed.

**So `--preview` must not ship with `notes[]` stubbed to empty.** An always-empty array is not a
partial implementation of this channel, it is the channel's failure mode with a success message on
it: a consumer reading `notes: []` concludes nothing was dropped, and §12.3 and §12.4 both instruct
a reader to treat that conclusion as load-bearing — migrate keys "the mapping is fine, the layout
does not fit" off a note being present, and edit distinguishes "my change dropped a pane" from "it
was already dropped" by diffing note lists. Empty-because-unbuilt and empty-because-nothing-happened
are indistinguishable at the consumer, and the second is by far the more common truth, so the wrong
reading is the one that always wins.

**Pane-drop and truncation instrumentation is therefore in scope for `--preview` itself**, not a
follow-on. The `maxLines` note is not: it arrives with the feature, as its own task.

**How the signal leaves the render path: a collector, passed down, never null.**

`SizeResolver` and `PaneRenderer` take a notes collector and append to it. The render path
constructs one and discards it; `--preview` constructs one and serializes it. The alternative —
changing return types so drops ride out through the values — is more honest in the small and does
not survive `PaneRenderer`'s per-segment truncation, which happens deep inside a draw loop whose
return value is a drawn row.

**The collector is never nullable, and that is the whole design.** A sink that the render path
passes `null` to is two paths again — the instrumented one that only `--preview` exercises and the
uninstrumented one that actually ships to users — which is Defect 15's exact shape: the same
behaviour with two capability sets, agreeing on every input anyone thinks to test. Passing a real
collector and throwing it away costs one allocation per render and buys a single path. §9.1's
constraint is satisfied on the letter that matters: what the render path *draws* is unchanged, and
it is unchanged because the drawing code is the same code, not because it took the other branch.

Two things fall out that are worth having independently. Layout tests can assert on the notes a
render produced without going anywhere near `--preview`, which is §10.1's discipline reaching a
place that had no observable at all. And the collector becomes the one place a note is defined, so
`--preview --json`'s `notes[]`, the human form's stderr lines (§9.3.3), and §12.6.10's per-render
`notes[]` are three renderings of one object rather than three chances to word it differently.

## 10. Testing requirements

The v1 lesson was expensive and is now policy. Read **§10.1 first** — it states a property every
assertion in this list has to satisfy, and four of them do not satisfy it as written.

**The numbered items below are cited as "§10 requirement N", never as "§10.N".** §10.1 is a
heading, it is not requirement 1, and the two series would otherwise collide — see §13.3, where
that collision is what stopped these bullets from being promoted to subsections.

1. **Parity gate for iteration 1** (§2.7): byte-identical to the pre-pane build across the
   full width sweep, border on and off. This is the whole justification for landing panes as a
   no-op first.
2. **Pane content is position-independent** (§2.5): the same leaf pane at the same inner width
   renders identically as the root of an 80-column surface and as the third child of a split in
   a 200-column one. A failure here means something below the compositor is reading `COLUMNS`.
3. **The rectangle invariant**: for any pane tree, at any width, every composed root row has
   the *same* ANSI-stripped width, and that width never exceeds `COLUMNS - chromeReserve`.
   This one assertion catches ragged padding, height mismatch, and overflow together. It must
   be shown to FAIL against a deliberately broken compositor (drop the padding step) before it
   is trusted.

   **There is exactly one escape-stripping implementation in the repo and every test drives
   it.** It handles CSI and OSC, each scanned by its own rule (§3.2). A second copy is not a
   duplication nuisance but a lying instrument: an SGR-only stripper scores an OSC-broken row
   as clean, and a pattern missing its `ESC` prefix leaves the escape byte counted as text.
   Both of those existed here and both passed a green suite.
4. **Every overflow mode obeys the rectangle invariant** (§2.6). Same pane, same too-long
   value, same width, three modes: `wrap` emits more rows and loses no characters; `truncate`
   emits the same row count and ends with the marker; `overflow` exceeds the width and is
   therefore rejected in a split. Also assert the two traps directly — a hard break never lands
   inside an escape sequence, and every continuation row of a styled segment carries the style.
   `ellipsis: ""` must clip without sacrificing a cell, and a marker wider than the pane must be
   dropped rather than fill it. A wrapped link-and-colour segment must keep **both** across every
   continuation row, and a truncated one must close its link before the ellipsis (§3.2.2) — a
   row of the correct width passes even when the restyle path has silently discarded both.
5. **Re-measurement under a narrower grant returns the longest wrapped row**, not the grant
   (§2.9). This needs a test that asserts the *sibling's* final inner width, because a
   re-measure that simply echoes its grant passes any test phrased as "the anchor did not
   exceed its cap".
6. **Intrinsic sizing is verified by derivation, not by a golden number** (§2.3): change the
   model string, change `COLUMNS` — the anchor's resolved width must equal its measured content
   width plus its own chrome every time, and the `fill` sibling must equal the exact remainder.
   Assert `maxSize` clamps rather than stretches.
   **The fixpoint needs three tests of its own**, because it is the subtlest thing in the
   layout: (a) *convergence* — an anchor clamped below its unwrapped width re-measures to its
   longest wrapped row and the freed columns actually land in the `fill` sibling, verified by
   asserting the sibling's final inner width, not merely that the anchor shrank; (b) *the monotone clamp* — a
   deliberately misbehaving stub renderer that requests MORE width when granted less must be
   clamped to its previous request and the loop must still terminate; (c) *the pass cap* — a
   stub that changes its request every pass must stop at 3 passes and render with the last
   resolved sizes rather than looping.
   These three are `greedy`-path tests and are not parameterized over `distribute: min-rows`,
   which replaces the fixpoint rather than refining it (§2.3.1). Min-rows carries its own
   analogous set — monotonicity of `rows_i(w)`, the `T` ceiling of §2.3.3, and the empty-set case
   at `T = 1` — since "3-pass cap" and "monotone clamp of a request" name nothing that exists in a
   single `T`-search.
7. **No pipe-diff-only verification.** Byte-parity against bash remains a useful regression
   check on the builtin path, but it cannot validate anything about the render surface, because
   both sides can share a wrong constant. Every width claim needs an invariant, not a diff.
8. **Command providers are tested with real processes**, not mocks — a script that succeeds,
   one that exits nonzero, one that hangs past its timeout, one that emits 400 characters, one
   that emits nothing. The timeout path is the most likely to be wrong and the least likely to
   be exercised by accident.

   **Each of those asserts the item's resulting *state*, not its rendered string** (§4). The
   script that emits nothing must produce `absent`; the one that exits nonzero and the one that
   hangs must produce `unavailable`. All three render as no text, so an assertion phrased
   against the output cannot tell them apart — and §2.11.2's collapse rule reads the state, not
   the text. A suite that only checks the string scores the collapse-on-timeout defect as
   passing, which is precisely the defect §2.11.2 was written to forbid.
9. **Cache behavior tested directly**: a hit within TTL spawns no process (assert on a marker
   the script writes), an expired entry re-spawns, a corrupt cache degrades to a miss, and a
   failed command falls back to an expired value.

   **The widths store is a second store and needs its own tests** (§5.0.1). The record behind
   `CLAUDE_TUI_LINE_PANE_WIDTH` is keyed by the cache key *and* `COLUMNS`, so the sequence is:
   render at one width and a record appears; render at a different width and there is no record
   yet, so the variable is **absent from the child's environment** rather than present with the
   old number; render back at the first width and the first record is found again. Assert on the
   child environment directly — a script that reports whether the variable is set — because the
   failure being guarded against is the variable being *present and wrong*, and no assertion
   about the rendered row can see that. A test that only checks the value when it is set will
   pass against an implementation that never clears it.
10. **Perf regression**: median render latency stays under the v1 measurement of ~12.6ms with
   zero command items configured, and the added cost of N cached command items is a lookup.
   Measure with the existing bench harness, including its self-calibration.
11. **Revert always finds the original** (§12.2). The test is the sequence that breaks a naive
    implementation: migrate, edit, migrate again, revert — and assert the restored command is
    the user's, not claude-tui-line's. Assert too that a second `origin` is refused, that no
    command deletes or overwrites a backup file, and that a hand-edited `settings.json` is
    reported rather than clobbered. The second migrate is, by construction, the overwrite case
    (§81): assert its consent gate discloses the existing config before writing — item 5's
    exists-branch wording, naming the path the first migrate wrote — and that its report scopes
    the `/revert` claim to `statusLine` rather than promising the config file back. This is the
    one area where a bug destroys something the user cannot rebuild, so it is tested against the
    filesystem in a temp HOME, not mocked.
12. **`--check` never spawns a process; `--preview` always does** (§9.1.1). One config, one
    `command` item whose script touches a marker file. `--check` leaves no marker; `--preview`
    leaves one. This is bullet 9's marker technique again, and it is the *only* way to state the
    property: a `--check` that executed the config's commands would produce identical stdout, an
    identical exit code, and identical diagnostics. Nothing in the observable result
    distinguishes the safe implementation from the unsafe one, so the assertion has to look
    outside the result. Every §12 command runs `--check` on a config a model has just written,
    which is what makes this bullet the difference between safe and merely intended.
13. **The four config-assertion cases, on both paths** (§9.2.1). The matrix is `{no --config,
    --config <path>} × {no file there, file there but unparseable}`, run against the check path
    and the render path. Check path: silent defaults for the first cell, exit 2 with
    `config-unreadable` for the other three. Render path: **exit 0 in all four**, silent defaults
    for the first, and one plain row naming the reason for the other three. Assert the row's
    *content* — that it names the path and the parse failure — not merely that some row was
    emitted, because the defect this exists to prevent is a plausible statusline rendering from
    defaults while the user's config sits unread.
14. **Every `--json` exit path emits the envelope** (§9.6). Drive `--json` to exit 0, 1, 2 and 3
    and parse each stdout as JSON. A suite that only exercises the success path certifies a flag
    that silently falls back to prose in exactly the cases where a programmatic caller most needs
    to branch. On the failure envelopes assert `diagnostics` is **absent**, and write that
    assertion as "the key is not present" — `JsonElement`'s ergonomics make an absent key and an
    empty array equally easy to read as "nothing wrong", which is the whole reason the
    distinction is specified.
15. **Every colour literal this project recommends is proven to parse** (§6.2.1). One
    table-driven test over every colour spelling that appears in an accepted-values list, in a
    `--check` message's repair advice, or in `--colors` output, asserting each parses through the
    same code path the renderer uses. An unparseable colour does not raise — it renders as no
    colour — so a spelling that does not exist can survive in documentation and in repair advice
    indefinitely. `color207` did exactly that here, inside the diagnostic whose entire job was to
    warn that a colour would not appear.
16. **The version drift test** (§9.7): the assembly's informational version equals
    `plugin.json`'s. It must be shown failing first, which costs nothing — it already fails. The
    `.csproj` declares no `<Version>`, so MSBuild supplies `1.0.0` while `plugin.json` says
    `0.1.0`.
17. **Notes never reach the exit code and never mix with diagnostics** (§9.8.1). A `--preview
    --json` of a config that degrades at the given width — content clipped, a pane dropped —
    carries `notes[]`, no diagnostic about the degradation, and exit 0. The inverse is the other
    half of the test: a config with a real fault carries the diagnostic and no note standing in
    for it.

### 10.1 A test that passes on a blank surface is not testing content

Bullets 2, 3, 4 and 6 are all assertions about width. Every one of them is satisfied by a surface
on which every item resolved to nothing. The rows are still equal in width, still under the cap,
still position-independent; the `fill` sibling still receives the exact remainder; the anchor's
measured content width plus its chrome still equals its resolved width, because zero plus chrome
is a perfectly consistent answer. **A pane tree that renders entirely empty passes the rectangle
invariant flawlessly.**

That is the §7.1 render-wrong class arriving in the test suite instead of in the output. The
suite's strongest assertions are structural; structure survives the loss of all content; and the
failure this project is most exposed to — a provider returning nothing because the payload
changed shape, an item id quietly unresolved, a cache handing back an empty string — moves the
surface from correct to blank without moving a single width.

So **every layout test carries a blank-surface control**: the same tree, every item forced empty,
asserting both that the width invariants still hold *and* that the two runs are distinguishable —
at minimum one assertion that the populated run's ANSI-stripped content differs from the blank
run's. A suite in which those two runs produce the same rows would not notice the difference in
production either.

Bullet 3 already carries the right instinct in requiring the rectangle invariant be shown to fail
against a deliberately broken compositor before it is trusted. The two controls are not
substitutes and passing one says nothing about the other: **a broken compositor is a control for
the padding the assertion measures; a blank surface is a control for the content the assertion
does not measure.** Every width assertion in this document needs both.

## 11. Phasing

1. **Phase 1** — `chromeReserve` width fix. *Done; awaiting live confirmation.*
2. **Phase 2** — the single-pane surface: §2.1 through §2.8, root leaf pane only. The default
   stays `"overflow"` so the §2.7 parity claim holds; `wrap` and `truncate` ship opt-in and fully
   tested, so that splits land on machinery already proven rather than on code written the same
   week it first matters.
3. **Phase 3** — splits, and everything that exists only because there is more than one pane:
   §2.9's re-measurement, §2.10's border grid, §2.11's collapse rule. **Acceptance is §2.9**,
   eyeballed live.
4. **Phase 4** — the item layer: the whole of §3, §4 and §5. That boundary is "how a value gets
   from a provider to a pane", which is why the cache, the widths store, derived and compound
   items, and argv placeholders are all inside it rather than scattered across later phases.
5. **Phase 5** — the CLI surface. **§9 is the list**; this line does not repeat it, because it
   already got this wrong once — it named three flags when §9 specifies five, and the two it
   omitted are the two §12's commands need in order to offer the user a choice rather than
   guess at one.
6. **Phase 6** — the authoring surface (§12): the backup ledger first, then `migrate`, `revert`,
   and `edit`.
7. **Phase 7** — the MCP tools (§12.6), stateless, over the same binary Phase 6's commands drive.
   Last by the user's own ordering: the slash commands establish what the operations *are*, and
   the tools then expose operations that already exist rather than defining them a second time in
   a second surface.

Phase 6 depends on Phase 5 and not the other way round: §12's commands are prompts driving the
binary, so every one of them is guesswork until `--items` and `--check --json` exist to be
driven. Within Phase 6 the ledger comes first for the same reason — `migrate` and `revert` are
both defined in terms of it, and a migrate that ships before the ledger is a tool that replaces
a user's statusline with no way back.

Splits now come **before** the item registry, reordered because the two-pane test needs no
providers at all — it needs only a compositor. Getting a visible result out of
the surface work early is worth more than getting the registry in early, and §2.9 exercises the
compositor far harder than any registry change would.

Each phase is wired into the live session and eyeballed before the next begins. That is the
only step that caught the last defect — and §10.1 explains why it keeps being the step that does.
A person looking at the statusline notices that it is blank; a suite whose strongest assertions
are about width does not. The eyeball is the blank-surface control, run by hand. §10.1 exists so
that the control also runs in the test suite, where it is repeatable and where nobody has to
remember to look.

**Phase 7 is the exception and needs a different acceptance**, because there is nothing to eyeball
— an MCP tool's output is a JSON payload consumed by a model. Its acceptance is a round trip: a
model, given only the tool descriptions, produces a config that `--check` passes and that renders
what was asked for. That is the property §12.6 actually needs and the one no unit test states.

### 11.1 This list is not the authority on what is outstanding

Phases 2, 3 and 4 each *used to* enumerate the features they covered, and every one of those
enumerations had gone stale — for the ordinary reason, which is that the spec kept growing after
the phase list was written and nothing linked the two. §2.8's `height: "content"`, §2.10's
per-edge borders and `border.collapse`, §2.11's collapse rule, §3.2's hyperlinks, §3.3's compound
items, §4.2's argv placeholders, §5.0.1's widths store and §5.1's probe caching were all specified,
and **no phase mentioned any of them.** Read literally, Phase 4 was "item registry + `command`
providers, cache, TTL, timeouts" and therefore finished, while three of those eight sections were
unbuilt work inside its boundary.

> **Discharged.** The list above was rewritten to the boundary form this section rules for, and
> every one of the eight now falls inside a stated boundary: §2.8 within Phase 2's "§2.1 through
> §2.8", §2.10 and §2.11 named outright in Phase 3, and §3.2, §3.3, §4.2, §5.0.1 and §5.1 within
> Phase 4's "the whole of §3, §4 and §5" — which goes on to name the cache, the widths store,
> derived and compound items, and argv placeholders as inside it. The critique is kept in the past
> tense rather than deleted, because the reasoning is the reusable part and the failure recurs
> every time a list of features outlives the document it summarises. What must not survive is its
> *present* tense: a reader who acts on "no phase mentions any of them" would re-fix a list that
> is already correct.

The proof that this list is not load-bearing is that §3.2 shipped anyway. Hyperlinks were
specified, built, tested and merged without ever being assigned a phase, and nothing anywhere
noticed — which is the answer to whether the list is what actually sequences the work.

So, the same rule this document applies to the item registry and to the CLI surface, applied to
itself: **a phase names a capability boundary and cites the sections inside it; it does not
enumerate features.** What is outstanding lives in the tracker, which is updated as a matter of
course because work is dispatched from it. This section answers a different and slower-moving
question — what has to exist before what, and why — and that question is the whole reason it is
worth keeping. The dependency claims below it are correct and were not touched.

A phase enumerating its own contents is the same defect as a count restated in prose: two places
that must agree, only one of which anybody has a reason to update.

## 12. Authoring surface — plugin commands and LLM-driven editing

The framework is configured by a JSON file, and the person configuring it should not have to
learn that file. Three plugin commands cover the lifecycle: adopt an existing statusline, change
it in conversation, and go back.

### 12.1 The binary is the oracle; the commands are prompts

None of this is renderer work. The commands are prompt files that drive a model, and the model
already has file editing — what it lacks is *what the framework can do* and *whether what it
just wrote is right*. Both come from the binary (§9), never from prose:

- **What exists** — `--items`, not a list written into a skill. An item list in a command file
  is a second registry, and it goes stale on the next row added. This is the §1 rule applied to
  documentation, where nothing type-checks the copy.
- **Whether it is valid** — `--check --json`, which names the offending key by JSON Pointer. A
  model that has just written a config needs to know *which* key it got wrong.
- **What it looks like** — `--preview --columns N`, at **80 and 60** (§12.1.1), because most
  layout mistakes only appear when something has to wrap. This bullet read "at the user's real
  width" until §12.1.1, which is a width no command can obtain.

The loop is therefore fixed and the same for every authoring command: **query, edit, check,
preview, show the user.** A model writing config from memory and declaring success is the
failure mode this structure exists to prevent — §7 makes a bad config silent, so an unverified
edit produces a wrong statusline with no error anywhere.

#### 12.1.1 The commands have no terminal either — §12.6.3's rule, one layer up

§12.6.3 ruled that the MCP server must be given its width because "the server has no terminal", and
that a preview at an inferred width "is a faithful preview of a layout the user will never see,
which is worse than no preview, because it will be believed." The slash commands run under exactly
the same condition and were never given the rule. §12.1's bullet above said "at the user's real
width" — and no command has ever been able to obtain one.

**Measured, not reasoned.** A model executing these commands runs them through a tool harness with
no controlling terminal: `tty` reports "not a tty", `stty size < /dev/tty` fails with *device not
configured*, and `COLUMNS` is `0`. In that environment `tput cols` returns `80` — identical to
`tput -T xterm cols`, the static terminfo `cols` capability — because there is no window to ask.

So `COLUMNS=$(tput cols)`, which §12.3, §12.5 and §12.7 all wrote, is **the literal constant 80
wearing the costume of an adaptive width.** That is worse than writing `80` would have been: a
reader reviewing the prompt sees a command that adapts, and no output ever contradicts them.

**Ruling: the commands render at explicit, named widths, and never call `tput`.** 80 and 60 — the
pair §12.6.3 already gives the server. Every report names the width it rendered at. The narrow run
is not decoration; it is the one that catches wrapping, which is the class of mistake this whole
loop exists to surface.

**The duplicate this had already produced.** §12.4 asked for the terminal's width *and* 80 *and* 60,
so `/claude-tui-line:edit` ran three previews at two distinct widths — 80, 80, 60. §12.6.3 had said
"pair" all along; the prompt drifted to three and nothing compared them. The cost was not the wasted
run: the prompt then reasoned *from* the count, telling its reader that a width-independent
`maxLines` note "fires identically at all three widths and appears three times." It appears twice.
A reader applying that rule to a note seen twice concludes it is width-dependent, which is the
opposite of what the passage exists to teach — and the step-3 "before" capture used the single
inferred width, so the before/after note diff the whole method rests on was comparing one width
against three runs.

**What this changes about reading an empty render.** §12.5 step 7 tells the reader that output of
nothing is "a real finding about the backup" — but at a width that is not the user's, with the
minimal stdin payload these commands use, empty output is *inconclusive*. §12.7 step 5 already says
this about the same evidence, warning that "a correct install reads as a half-broken one" and that
the user's first act is then to debug something that works. Two commands drawing opposite
conclusions from one observation is what §12.7 itself calls worse than either being wrong alone.

The distinction that actually holds: **a nonzero exit or anything on stderr is a real finding; empty
stdout alone is not.** Report the second as inconclusive, name both reasons it can happen, and give
the user the one-line command to run in their own terminal — which is the only place the real width
exists, and which is a thing to hand over rather than to simulate.

#### 12.1.2 §12.6 is where the shared rules got written, and it is the wrong place

§12.1.1 is not a one-off. §12.6 was written last and most carefully, so it became the place
cross-cutting rules for the whole authoring surface landed — and each was scoped to the server
because that was what was being written at the time. **A rule caused by the transport belongs in
§12.6. A rule caused by a condition belongs here, where every command reads it.** The tell is a
section that opens "same root cause as §…": that phrase marks a rule whose scope was drawn at the
layer it was noticed in rather than the layer it holds at.

Four of §12.6's ten are conditions the slash commands share:

- **§12.6.2 — the environment is not the user's shell.** `$CLAUDE_TUI_LINE_CONFIG` set in an
  interactive shell need not be visible to a non-interactive one, so §5's search order, run
  identically in both places, resolves to a *different file*. Nothing errors, and every layer
  honestly reports success. §12.3 already says "do not assume the default"; what was missing is the
  other half of §12.6.2 — **the report names the config path that was actually written.** §12.4
  step 8's report listed what changed, the checkpoint, and every choice made for the user, and
  never once said which file it wrote.
- **§12.6.3 — no terminal.** Hoisted as §12.1.1.
- **§12.6.5 — concurrent writes.** Its own first sentence reads "an MCP call, **a slash command**,
  and a hand edit in an editor can now interleave" — it names the command layer in the premise and
  then gives the mechanism to the server alone. The commands have no `baseRevision`, so their
  protection is the weaker pair §12.6.5 already names as the fallback: re-read the config
  immediately before writing rather than trusting a copy read several steps ago, and rely on §12.2's
  checkpoint to keep a clobber recoverable.
- **§12.6.7 — the complete list of files that may be written.** A boundary this explicit should not
  exist for the ambient layer and be absent from the layer a user invokes deliberately.

**The command layer's write list, which is not §12.6.7's.** Hoisting that list verbatim would have
been wrong, and the differences are the reason this needed writing out rather than cross-referencing:

1. the config file, at the resolved or explicitly-given path — §12.3 and §12.4 only
2. the ledger and its artifacts under `~/.claude/claude-tui-line/backups/` — append-only, per §12.2
3. `~/.claude/settings.json`, **only** the `statusLine` key, preserving every other key and the
   file's formatting — from §12.5 **and §12.7**. §12.6.7 says "only from `revert`" because there is
   no `setup` tool; at this layer that clause is false, and copying it across would have forbidden
   §12.7 from doing the one thing it exists to do.
4. the `scriptOriginalPath` recorded on the entry being restored — **only** from §12.5, **only**
   when no file exists there, **never** overwriting one that does
5. build outputs under the plugin's own data directory — §12.7 only, and the reason this layer needs
   a fifth entry at all: no MCP tool compiles anything

Scratch files are the other difference. §12.6.7 forbids temp files outside the target's directory
because an atomic rename needs the same filesystem — that constraint binds only the file being
renamed into place. Drafting a candidate config or capturing `--preview` stderr under `/tmp` is
fine, and §12.3 and §12.4 both depend on it.

Everything else §12.6.7 refuses, this layer refuses too: no logs, no caches, no state directory,
and nothing outside `~/.claude` and the plugin's own data directory.

### 12.2 The backup ledger

Shared by every command that writes. It lives at `~/.claude/claude-tui-line/backups/` — under
the user's Claude directory rather than plugin data, because a backup that a plugin reinstall
can delete is not a backup.

`ledger.jsonl` is append-only in the strict sense ruled in §12.2.1 — one entry per line, added
without rewriting any existing one. Each entry records the UTC timestamp, the previous
`statusLine.command` verbatim, a copy of any script that command referenced, **a copy of
`claude-tui-line.json` whenever one exists**, the SHA-256 of each captured artifact, and a `kind`:

- **`origin`** — the state before claude-tui-line ever touched this machine. **Written exactly
  once, ever.** If an `origin` entry exists, no command may write another.
- **`checkpoint`** — any state captured since.

**When no config file exists, the entry records the absence rather than omitting the fields:**
`configOriginalPath` is the path §5's search order resolved to, and `configCopy` is `null`. This
mirrors `"statusLine": null` and for the same reason — *nothing was here* and *this ledger cannot
say* are opposite facts that call for opposite recoveries, and three missing fields cannot tell
them apart from an entry written before configs were captured at all. Absence needing a
distinguished value rather than a missing key is now the third instance in this project (see also
§12.6.9's `revision: "absent"`), which is enough to treat as the default assumption for the fourth.

That distinction is the whole point of the ledger rather than a timestamped file. Migrate, edit,
migrate again, and a naive "back up whatever is there now" captures *claude-tui-line's own*
command as the thing to restore; revert then cheerfully restores the tool the user is trying to
escape. `origin` is written once and revert targets it by default, so the escape hatch survives
any number of intervening changes.

**An `origin` may never record a `statusLine` already pointing at a claude-tui-line binary**, and
"no `origin` exists yet" is not sufficient grounds to write one. A user can reach that state
without this tool having run — hand-editing `settings.json`, then invoking `setup` afterwards —
and the once-ever rule makes the resulting false `origin` permanent. That is strictly worse than
the second-use failure above: there, the escape hatch degrades; here, it is poisoned at creation
and nothing downstream has cause to doubt it. In that case append a `checkpoint` and leave
`origin` unwritten. A missing `origin` is honest and already handled — §12.5 lists the
checkpoints and flags which point at a claude-tui-line binary.

Four rules, none optional. `docs/backup-ledger.md` states the same four and is what the command
prompts actually read at runtime; this is the design, that is the procedure.

1. **Nothing in the backup directory is ever overwritten or deleted by any command.** Reverting
   is itself a change and appends a `checkpoint`; it does not consume the `origin`.
2. **The user's original script is copied, never moved and never modified.** Restoring a command
   that points at a file the user has since deleted is a broken revert, which is why the copy is
   taken even though installing does not touch the script.
3. **Only the `statusLine` key of `settings.json` is read or written.** Writes are atomic — temp
   file in the same directory, then rename, per §5 — and preserve unrelated keys and formatting.
   A recorded SHA-256 that no longer matches means the user edited it by hand since; that is
   reported, and it is theirs to resolve, not the tool's to overwrite. **Which artifact that hash
   check applies to is not obvious and is ruled in `docs/backup-ledger.md`** — the live
   `settings.json` is *expected* to differ at revert time, so checking it there would make revert
   refuse on every run.

4. **An entry captures every artifact, not the one its command intends to change.** `/edit` writes
   only `claude-tui-line.json`; `setup` writes only `settings.json`. An entry scoped to the caller
   is a backup of the wrong file for whichever command later needs it, and the failure is silent:
   the rollback runs, restores something real, reports success, and the damaged file is untouched.
   The question to ask at each call site — and the one nothing inside the procedure can answer —
   is **does what this saves include what this command is about to change?**

   Its other half is **one entry per command invocation, taken before the first write** — not one
   per file written. The two are the same ruling seen from either end: an entry holds everything,
   so nothing needs a second entry. Without the second half the first reads as an instruction to
   back up before each write, and `migrate` — which writes the config and then `settings.json` —
   would take two, the second capturing the config it had just written. That is a restore point
   for a state that existed for one instant and that nobody would ever want back (migrated config,
   original `statusLine`), permanently, since rule 1 forbids removing it. **The procedure is for
   the command that has not yet backed up.** `commands/migrate.md` step 8 says so at its own call
   site; it is a rule here so that the next command to write two files does not have to rediscover
   it.

#### 12.2.1 A JSON array cannot be appended to

"The ledger is append-only" is the sentence above, and `docs/backup-ledger.md` expanded it into a
procedure that contradicts it: *"A JSON array, **append-only**. Read it, append one entry, write the
whole array back."* An array cannot be appended to. Its closing bracket has to move, so adding an
entry rewrites every prior byte of the file. "Append-only" there states a *semantic* — rule 1, never
edit or remove an entry — while the operation it introduces is *rewrite everything*, and the
semantic holds only if whoever performs the rewrite is careful.

Who performs it is what turns this from untidy into dangerous. Nothing in `src/` writes the ledger
and nothing is planned to; per §12.1 the commands are prompts, so every writer is a language model
following `docs/backup-ledger.md`, and the file-writing tool it has replaces a file whole. Step 8 of
that procedure — "Write `ledger.json` back" — is therefore *a model re-emitting every prior entry
from context*. Each entry carries a `statusLine` recorded verbatim **including keys it does not
recognise**, two or three SHA-256 digests, and a `configCopy`. That is precisely the content a model
reproduces least reliably — opaque hashes, unfamiliar keys, absolute paths — and it grows with every
invocation. Rule 1 forbids editing an entry, but under a whole-file rewrite that rule is not merely
unenforced, it is uncheckable: a dropped entry and an entry never written leave the same file.

The entry most exposed is the oldest, and the oldest is `origin`. Rule 4's condition writes `origin`
if and only if none exists **and** the current `statusLine` does not already point at a
claude-tui-line binary. Once the tool is installed the second clause is permanently false. So losing
the `origin` line produces no error, no retry, and no gap the next command can notice — it produces
a machine on which every later entry is a correctly-written `checkpoint`, forever, and the user's
pre-installation state is gone. §12.5 rules that a missing, empty, or unreadable ledger stops the
command and forbids reconstructing one, which is right, and which means nothing recovers from this.
§12.2's own opening argument is that the naive design lets the escape hatch close quietly exactly as
it becomes needed; it then specified a write path that reintroduces that same failure one layer
down, through the file instead of through the policy.

**The ledger is the root of the recovery tree.** `settings.json` has a backup and
`claude-tui-line.json` has a backup, and both of those backups are the ledger. The ledger has none.
It therefore needs a stronger durability rule than either artifact it protects, and it currently has
a weaker one: rule 3 gives `settings.json` temp-file-then-rename atomicity, and step 8 gives the
ledger a bare overwrite.

So, ruled:

1. **The ledger is JSON Lines — one entry per line — and the file is `ledger.jsonl`.** The
   requirement behind the format is not stylistic. It is that adding an entry must not require
   reproducing the bytes of an existing one. JSON Lines has that property; a JSON array
   structurally cannot.
2. **The append is a real append.** One line redirected onto the end of the file. Reading the ledger
   in order to *decide* is required and unchanged — step 7 must still determine whether an `origin`
   exists. Reading it in order to *write it back* is forbidden, and the whole-file write tool is
   never pointed at the ledger.
3. **A reader skips a trailing partial line rather than failing on it.** One short line appended to
   a local file is very nearly all-or-nothing, and "very nearly" is the case the format exists to
   survive: a torn append costs the newest entry and leaves every earlier one byte-identical. That
   is only worth anything if the reader tolerates it. §12.5's fatal stop is for a ledger that cannot
   be read at all, not for one whose last line is short — every complete line before the tear is
   still the ledger.
4. **If this ever moves into the binary, `FileMode.Append` is not enough.** .NET's append mode is
   seek-then-write rather than POSIX `O_APPEND`, so two concurrent writers can resolve the same
   offset and one silently overwrites the other. Open with real `O_APPEND`, or serialize. §12.6.5's
   compare-and-swap governs `settings.json` and the config and deliberately does not reach here: the
   correct outcome for two concurrent ledger writes is that **both entries land**, not that the
   second one wins.

Decide it now, because it is free exactly once. `~/.claude/claude-tui-line/backups/` does not exist
on any machine yet — no ledger has ever been written, so there is nothing to migrate and no `origin`
at risk. That stops being true the first time `setup` runs for real, and a format migration would
have to rewrite the file whole: the operation this section exists to forbid, performed on the one
file whose oldest entry can never be recreated.

### 12.3 `/claude-tui-line:migrate`

Adopts an existing statusline. An existing statusline is an arbitrary program — the user's real
one is 280 lines of bash — so this is a model's job, not a parser's, and the command's value is
in constraining what the model is allowed to conclude.

Every element found in the source maps into exactly one of three tiers:

1. **A builtin item**, when `--items` offers an equivalent — a branch readout becomes
   `git-branch`.
2. **A `command` provider (§4) wrapping the original logic**, when it does not. This tier is why
   migration can be lossless: worst case every element shells out to a snippet of the user's own
   script, and they still gain panes, borders, sizing, and colour rules over logic that already
   worked.
3. **Unmappable**, which is *reported to the user*, never silently dropped.

Tier 3 existing is what makes tiers 1 and 2 trustworthy. A migration that cannot say what it
failed to carry across will quietly lose an element, and the user will not notice until the day
they needed it.

**Fidelity is checked, not asserted.** After generating the config, run the original script and
`--preview` against the same stdin payload and compare the escape-stripped text. *Same* is not
sufficient — §12.3.1 rules which payloads, because a payload that omits a field makes the check
pass on the elements that read it. This is not
byte-parity — the layout differs by design, that is the point — it is a *content* check: every
visible token the original produced must appear in the new render or be on the tier-3 list.
Anything else is a silent drop wearing a success message.

**Nothing is written until the user says yes.** The command shows the proposed config, the
side-by-side preview, and the tier-3 list, and only then writes — recording `origin` first if
this is the first time.

**Tier 2 inherits a budget the original never ran under.** `ttlSeconds` and `timeoutMs` default to
30 s and 150 ms, and 150 ms is tight for a real script — the user's statusline was a program that
could take as long as it liked, and every fragment lifted out of it is now a `command` provider
that gets killed at the deadline and renders as nothing (§7). So migrate sets both **deliberately
on every command item and reports what it chose**, rather than letting the default apply by
omission. This is the one place a faithful port can silently lose an element that mapped cleanly
in every other respect, which is why it is a ruling and not a default: the failure looks exactly
like the item having never been migrated at all.

**Colour is preserved by kind, not by appearance.** Where the original used one of the sixteen
standard ANSI colours, migrate carries it across *by name*, because those are theme-mapped and
keep following the user's terminal theme exactly as the original did. Where the original named a
*specific* shade — a 256-palette index or a truecolor escape — a name is a downgrade, so the
256 name or `#rrggbb` is used instead, and the report says that this also requires `colorSystem`
(§6.2, whose default profile approximates them to the nearest of the sixteen). A colour that
varied by value in the original is a colour rule with `match` or `thresholds`, not a fixed colour.
`--colors`'s `recommended` list is a recommendation and not the accepted set; migrate does not
refuse a colour for being absent from it.

**The config is written to the path §5's search order resolves to, never to the default by
assumption.** If `$CLAUDE_TUI_LINE_CONFIG` points elsewhere and the default is written anyway, the
renderer reads the file migrate did not write: nothing errors, the statusline does not change, and
every preceding step still reports success. The command reports which path it wrote.

**The config is written before `statusLine` is repointed.** The moment `statusLine.command`
changes, the binary begins running once a second against whatever config is on disk at that
instant; in the other order that is a window of built-in defaults the user never approved. Ordering
two writes is free, and it is the whole fix.

Migrate does **not** run the ledger procedure a second time at write. Its backup was taken before
it read anything, and one entry per invocation is §12.2 rule 4.

**The consent gate discloses whether a config already exists at the resolved path, because
approving the migration replaces it.** The existence fact is free — §12.2's entry must already
distinguish a captured config from `configCopy: null`, so the command holds it before the consent
step runs. This is a disclosure, not a comparison: §12.6.11's compare-and-branch remains out of
scope for `migrate` per #39, and reading the existing config's *contents* would reintroduce
precisely the new-read cost that ruling rejected.

**Migrate's report does not claim `/revert` restores the config file.** `commands/revert.md`
deliberately restores only the `statusLine` entry in `settings.json` (§12.5), not the config, so a
migration that replaced an existing config names the ledger's `configCopy` path and says restoring
it back is a manual copy — the ledger keeps it forever (§12.2 rule 1), but no command performs that
copy.

#### 12.3.1 The fidelity check passes on the empty set

"Run the original script and `--preview` against the same stdin payload" says *same*, and says
nothing about *what*. `commands/migrate.md` step 6 fills that in with a payload it wrote itself:

```
payload='{"cwd":"'"$PWD"'","model":{"display_name":"Claude Opus 5"}}'
```

Two of `StatusInput`'s thirteen fields. Every element of the user's script that reads `session_id`,
`context_window`, `rate_limits`, `pr`, `vim`, `agent`, `effort`, `thinking`, `output_style`,
`worktree` or `workspace` produces nothing under it — and produces nothing without erroring, so
step 6's guard for *"if the original script errors on this synthetic payload, say so rather than
treating its empty output as a match"* never fires. The check is "every visible token the original
produced must appear in the new render." On an element that produced no token, it holds vacuously.
Both sides are silent, the comparison passes, the tier-3 list stays empty, and the command reports
a faithful migration of an element it never exercised. **The elements hardest to carry across are
exactly the ones this payload makes invisible**, because the ones that render from a bare `cwd` are
the easy ones.

§9.3.1 already ruled this, for a different consumer, in the section's own first rule: *"Every field
is populated, including the ones real payloads usually omit. This is the one place where
completeness beats realism. Real Claude Code payloads routinely carry no `pr` and no `vim`, so a
fixture built to look like a real payload omits them"* — and then `--items` shows those items with
an empty `example`, from which a user correctly concludes they produce nothing. The payload above is
precisely that fixture, written by asking what a real payload holds. Migrate is the worse case of
the same mistake: for `--items` the consequence is a visibly empty example, and here it is a pass.

The reason it was hand-rolled is a missing seam rather than carelessness. §9.3's two branches are
*stdin has data → use it*, annotated **"This is what `/migrate` uses to compare against the original
script on identical input"**, and *stdin empty → the built-in fixture*. So §9.3 names migrate as the
consumer of the branch that has no fixture, while the fixture — the thing §9.3.1 exists to make
complete — is reachable only from inside the process. No flag emits it. `commands/migrate.md` had
nothing to pass to the user's script, so it invented something.

Ruled:

1. **The fidelity check runs against a payload in which every field is populated**, on the same
   grounds and for the same failure as §9.3.1's first rule. A payload that omits a field cannot
   distinguish a correctly migrated element from a dropped one.
2. **That payload is §9.3.1's fixture, and the binary emits it** — a flag that writes the fixture
   JSON to stdout, so both sides of the comparison can be fed identical, complete, pinned bytes.
   §9.3's claim that there is exactly one synthetic payload is false as soon as a second consumer
   lives outside the process and cannot reach the first one. This flag is what makes it true.
3. **The check runs at two payloads, and the second is not optional.** *(Superseded by §12.7.2: the
   emitted payload carries the real `cwd`, so one payload covers both halves.)* The fixture's `cwd`
   is `/home/you/code/acme-web` — deliberately not a real path, per §9.3.1's visibly-synthetic rule —
   so every element that shells out against the working tree resolves empty under it, which is this
   same vacuous pass one layer down. The second run carries the real `$PWD`. The fixture run covers
   elements fed from stdin; the real-`cwd` run covers elements fed from the filesystem. Neither
   covers the other, which is why one payload was never going to be enough.
4. **Under the fixture, a disagreement in a machine-probed value is not a finding.** §9.3.1 cans
   `gitBranch` and the remote URL because they come from probing rather than from stdin, and the
   user's script probes for real. Differences there are expected and reported as such; it is the
   stdin-derived elements the fixture run is asserting about.

The failure this rules out is the one §12.3 says the whole step exists to prevent — a silent drop
wearing a success message — arriving through the verification rather than around it.

### 12.4 `/claude-tui-line:edit`

Conversational editing: "move context into the right pane", "make the border follow the model".
Mechanically it is §12.1's loop plus a `checkpoint` written before the first edit of a session,
so undoing one bad idea never requires going all the way back to `origin`.

Two constraints on the model, both learned from this project's own failures:

- **Re-read `--items` rather than trusting recall.** Item ids and accepted keys change between
  versions; a remembered id resolves to nothing and is silently suppressed (§7).
- **Never widen the request.** Reformatting the whole config while adding one item makes the
  diff unreviewable and buries an unintended change where nobody will look for it.

**What a rollback restores is the config, not the checkpoint's `statusLine`.** `/edit` changes
`claude-tui-line.json` and nothing else, so recovery means copying the entry's `configCopy` back
over the config file. Restoring that entry's `statusLine` — a key this command never touched —
changes nothing, leaves the broken config exactly where it is, and looks precisely like a fix.
This is §12.2 rule 4 seen from the recovery end, and the ruling is stated at both ends because
each end fails silently on its own.

**A `configCopy` of `null` rolls back by deleting the file.** There was no config before the
command ran; that is the state, and re-creating an empty or default one is not it. Same shape as
`"statusLine": null` restoring by removing the key.

**Rule 4 is verified at the call site, not assumed.** After appending the checkpoint, `/edit`
confirms the entry actually carries `configOriginalPath`, with `configCopy` either naming a copy
or explicitly `null`, and **stops** if the fields are simply missing — missing means the procedure
did not look, and an unrecoverable edit is not worth saving one round trip. Nothing inside the
ledger procedure can answer whether what it saved covers what this caller is about to change.

**Seeding a config is a write, so the checkpoint precedes it.** When no config file exists and the
user agrees to create one, the checkpoint is taken *before* the file is created. In the other order
the entry records the seeded file as the state to roll back to, so a failed edit lands the user on
a config they did not have when the command started, and "no config, defaults apply" stops being
reachable at all.

**A passing `--check` is not evidence the result looks right.** An edit is verified by previewing
at 80 and 60 columns, because most layout mistakes only appear when something has to wrap. This
read "at the terminal's width *and* at 80 and 60" until §12.1.1, which is where that third width
turned out to be 80 again. Three things are then reported honestly: whether the intended change
appeared (an item resolving to empty renders invisible, and invisible reads as absent), whether
anything else moved (adding an item rewraps its neighbours — correct behaviour, but the user
should hear it here rather than discover it later), and whether it still degrades rather than
breaks at the narrow widths.

**On failure, roll back first and explain afterwards.** A broken statusline runs once a second for
the entire length of the explanation. And the rollback is itself previewed before being reported —
a recovery announced on the strength of having written a file is the same defect this section
opens with.

### 12.5 `/claude-tui-line:revert`

Restores from the ledger — `origin` by default, a named `checkpoint` on request. It restores
**both** the `statusLine.command` and, if the recorded script is missing from its original
location, the copied script, because restoring a command that points at nothing leaves the user
with no statusline at all and no obvious cause.

It appends a `checkpoint` for the state it replaced, so reverting a revert is possible. And it
prints the restored command, because a user reaching for revert is already having a bad time and
deserves to see exactly what they got back.

**Hashes: which file, and the version of this instruction that disables the command.** This
section used to read "it verifies the SHA-256 of what it restores against the ledger", which is
the collapsed form `docs/backup-ledger.md` identifies as wrong — it does not say *which* file, and
the obvious reading takes the live one. The live `settings.json` is **supposed** to differ at
revert time: claude-tui-line is installed now and was not when the backup was taken. A revert
following that reading reports "you hand-edited this" and stops on every single run, which is the
escape hatch refusing precisely when it is reached for. The table in `docs/backup-ledger.md` is
normative and says: hash the **backup copies** before restoring from them, and the user's
**original script at its original path**; never the live `settings.json`, and never the live
`claude-tui-line.json`.

**The script has three cases, not one.** A restored `statusLine` points at a path, not at
contents, so restoring the command is not restoring the statusline:

- **missing** → restore the copy alongside the command, and say so. §12.5's opening paragraph
  covers this case, and it is the one people think of.
- **present, hash matches** → nothing to do. The ordinary case.
- **present, hash differs** → the user has edited that script since. **Do not overwrite it and do
  not proceed silently** — report it and let them choose between the live version and the copy.

The third is the case worth specifying, because it is the only one with no symptom: the revert
succeeds, the statusline renders, and it is simply not the statusline that was backed up.

**A ledger that is missing, empty, or unreadable stops the command.** Do not reconstruct a
statusline from this repo, from the conversation, or from anything else — a fabricated statusline
the user believes is theirs is worse than none. Say where the ledger would have been, and offer to
remove the `statusLine` key entirely, which is a clean and honest state.

**Revert deliberately does not restore `claude-tui-line.json`, and does not delete it either.**
§12.2 rule 4 makes every entry capture that file, so restoring it is *available* — and it is
wrong here. The two artifacts move independently: this command answers "put my old statusline
back", not "undo my layout work", and rolling the configuration back as a side effect of
unpointing `statusLine` destroys work the user never asked to touch. `/claude-tui-line:edit` owns
config rollback. Report that the copy exists so the option is visible.

This section is decisions; `commands/revert.md` is the procedure that carries them out, and
`docs/backup-ledger.md` is the shared ledger procedure both of them follow.

### 12.6 The MCP server — ambient access, added after the CLI

Slash commands require the user to know a command exists. The MCP server exists so that
"make the statusline border green" works in the middle of an unrelated conversation, which is
the access pattern people actually have. It ships **after** §12.3–12.5, and the ordering is
deliberate: the CLI is what it wraps, so building it first would mean designing a transport
around an interface that does not exist yet.

**It is stateless, and that is the whole design.** stdio MCP keeps one process alive — the
client spawns it and talks JSON-RPC over the pipe, so it must survive between calls — but it
holds nothing between them. Every call re-reads the config from disk and re-derives its answer
from the same code the CLI runs. Two consequences, both the point:

- **It cannot drift from the CLI**, because it has no independent knowledge to drift with. An
  MCP server that cached the item list would become a second registry, which is §1's failure
  wearing a different hat.
- **It cannot serve a stale config.** A user who hand-edits the file between two calls gets the
  file they wrote, not a remembered parse of the file they replaced.

Nothing about it touches the renderer's shape. The statusline stays a one-shot AOT binary
spawned once per second; the MCP server is a separate process with separate lifetime needs, and
conflating the two is a mistake worth naming because it argues against a design for no reason.

Whether it re-spawns the CLI per call or calls the same internal functions directly is an
implementation detail and either is acceptable. **What is not acceptable is a third
implementation of any behaviour.** CLI and MCP are two adapters over one core.

**Mutating tools carry the §12.2 obligations unchanged.** Ambient access means a config can now
change without the user having watched a diff go by, so an MCP edit writes a `checkpoint` before
it mutates and returns the resulting diff for the model to show. The safety property that makes
`/edit` acceptable is the ledger, not the fact that a human typed a slash.

#### The tool surface

The goal is that a model can carry a request from "make the model name pink when I'm on Fable"
all the way to a rendered statusline without the user touching anything. That needs the full
loop — discover, change, validate, show — so the surface is not read-only.

| Tool | Wraps | Purpose |
|---|---|---|
| `list_items` | `--items --json` | What items exist, what each emits, what options it takes — **and the schema for defining a new one** (§4.1), so a model that finds no builtin fit knows it can author a `command` item rather than giving up. |
| `list_colors` | `--colors --json` | The palette (§6), so a colour request resolves to a real name. |
| `get_config` | reads the config path | The current config plus which path it came from (§5 search order) — the model must not guess which file is live. |
| `set_config` | writes + `--check` | Write a full config. Validates **before** committing; a config that fails `--check` is rejected and the old one stays. Checkpoints first. |
| `validate` | `--check --json` | Check a candidate config without writing it. |
| `preview` | `--preview` | Render to a given width and return the rows, so the model can *see* the result rather than assert it. |
| `revert` | the §12.2 ledger | Roll back to the origin, or to a named checkpoint. |

Two rules make this safe enough to hand to a model:

**`set_config` never commits an invalid config.** It validates, and on failure returns the
diagnostics instead of writing. A statusline is a thing the user stares at all day; breaking it
silently through an ambient tool call is the worst outcome available, and it costs one
validation to make impossible.

**`preview` is how the model checks its work, not `set_config`'s return value.** A write that
succeeded is not evidence the result looks right. The loop is the same one §12.1 fixes for the
slash commands — *query → edit → check → preview* — and the MCP tools exist so the model can run
that loop itself. This is why `preview` returns rendered rows rather than a success flag.

#### The worked example this surface has to satisfy

> *"Add git diff stat to my status line at the end of the first pane, make adds green and
> deletes red."*

Nothing here is a new tool; it is the existing loop applied to a request that touches all three
hard parts — an item that may not exist, a position in a tree, and colour *inside* one item.

1. `list_items` — no builtin diff stat. The response carries the §4.1 command-item schema, so the
   absence is a fork in the road rather than a dead end.
2. `get_config` — returns the whole config, which is why it returns the whole config: the model
   has to navigate to "the first pane" itself, and `surface.pane.children[0].items` is only
   locatable if the tree is in front of it.
3. `set_config` — writes a `git-diff-stat` command item with `match`, `format`, and per-part
   `colors`, and appends `{ "item": "git-diff-stat" }` to that pane's `items`. Validation runs
   before the commit; a bad regex or an unknown colour name comes back as diagnostics and the
   user's working statusline is untouched.
4. `preview` — renders it, so the model can see `+42 -17` in the right pane in the right colours
   instead of reporting success on the strength of a 200 response.

The user typed one sentence and never saw a config file. That is the bar for this surface: if a
request of this shape needs the user to open an editor, the tools have not done their job.

#### 12.6.1 The wire contract

Every tool returns a JSON object. Failures come back as a **result the model can read**, not as a
JSON-RPC protocol error:

```json
{ "ok": false, "code": "stale-revision", "message": "...", "diagnostics": [] }
```

Protocol errors stay reserved for genuine protocol faults — malformed request, unknown tool. The
reason is that the model's recovery path runs through reading the failure, and many clients
surface a protocol error to the user as an opaque box while handing the model nothing to act on.
That converts a fixable typo into a dead end.

`diagnostics` is `--check`'s array passed through **unchanged** — same `pointer`, same `code`,
same `severity` (§9.6). A wrapper that flattens a diagnostic into a sentence destroys the only
part that is actionable: §9.6 made `code` a compatibility surface, and the JSON Pointer is what
tells a model *which key* it got wrong rather than that something is wrong.

| Tool | Arguments | Returns |
|---|---|---|
| `list_items` | — | `items[]`, the §4.1 command-item schema, `cliVersion` |
| `list_colors` | — | `colors[]` (§6) |
| `get_config` | `configPath?` | `config`, `configPath`, `source`, `revision` |
| `set_config` | `config`, `configPath?`, `baseRevision?` | `ok`, `diagnostics[]`, `revision`, `checkpoint`, `configPath`, `source` |
| `validate` | `config` \| `configPath` | `ok`, `diagnostics[]`, `configPath`, `source` |
| `preview` | `columns?`, `config?`, `configPath?` | `renders[]` — each `{ columns, rows[], notes[] }` (§12.6.10) — plus `configPath`, `source`, `diagnostics[]` |
| `revert` | `confirm?`, `target?` | unconfirmed: `entries[]`. confirmed: `restored` |

**Every tool that resolves a config path returns `configPath` and `source`**, not just
`get_config`. §12.6.2 already requires the model to state which file it acted on, and originally
only one tool could: a model that called `set_config` without a prior `get_config` had written
somewhere it could not name, and `preview` could render a file that was not the user's and report
rows with nothing to compare against. The rule is that resolving a path and reporting which one
you resolved are the same obligation — whoever does the first does the second.

#### 12.6.2 The server's environment is not the user's shell

This is the hazard most likely to produce a baffling bug report. MCP clients commonly spawn stdio
servers with a minimal environment inherited at client start. `$CLAUDE_TUI_LINE_CONFIG` set in the
user's shell may simply not be visible to the server — so §5's search order, run identically in
both places, resolves to a *different file*. Nothing errors. The model edits a config, the user
sees no change, and every layer honestly reports success.

- Resolution runs **per call** and is never cached; §12.6's statelessness already requires this,
  and this is the case that shows why it matters.
- `get_config` returns `configPath` and `source` — `"env"`, `"default"`, or `"none"` — and the
  model is expected to state the path when it reports what it changed.
- Every tool that reads or writes config takes an optional explicit `configPath` that overrides
  resolution outright. This is §9.2's `--config` at the tool layer and exists for the same reason:
  without it, the only way to act on a specific file is to hope the search order agrees with you.

#### 12.6.3 `preview` takes its width; it never infers one

Same root cause as §12.6.2. `COLUMNS` in the server's environment describes nothing — the server
has no terminal. A preview rendered at an inferred width is a faithful preview of a layout the
user will never see, which is worse than no preview, because it will be believed.

`preview` therefore takes `columns` explicitly. Given none, it renders at **80 and 60** and
returns both, labelled — the widths where layout decisions actually become visible, and the same
pair §12.4 step 7 requires of the slash command. It applies `chromeReserve` exactly as the
renderer does (§9.3): a preview three columns wider than reality is a preview of a different
layout.

#### 12.6.4 `revert` without confirmation is the listing

`revert` is the one ambient tool that can undo work the user did deliberately, and ambient means
nobody watched a diff go by first.

Called without `confirm: true`, it **writes nothing and returns the ledger** — every entry, its
kind (`origin` / `checkpoint`), its timestamp, and what restoring it would do. Called with
`confirm: true` and an explicit `target`, it restores.

There is deliberately no separate `list_backups` tool. The cheapest call is the one that shows the
options first, so a model cannot skip look-before-you-leap by reaching for a shorter one.

#### 12.6.5 Concurrent writes: compare-and-swap, not last-writer-wins

Ambient access means an MCP call, a slash command, and a hand edit in an editor can now interleave.
Last-writer-wins silently discards whichever change was not last — and that is usually the user's,
because the model writes faster.

`get_config` returns a `revision`, a hash of the file's bytes as read. `set_config` takes an
optional `baseRevision`; if supplied and no longer matching, the write is **refused** with
`code: "stale-revision"` and the model re-derives its intended change against the current config
instead of clobbering (§12.6.11 corrects an earlier "re-reads" here), because a caller that merely
re-reads and resubmits its original `config` defeats the refusal by complying with it.

It is optional rather than required so a first write to a machine with no config file works
without ceremony. A model that read the config is expected to hand back what it was given; one
that did not is writing blind, and the §12.2 checkpoint is what keeps that recoverable.
**§12.6.9 supersedes that first sentence's reasoning**: optionality covers a caller omitting
`baseRevision`, but it left the fresh-machine case — the moment two agents are most likely to act
on the same file — with no compare-and-swap at all. `revision: "absent"` is the fix, and it means
a first write has a value to send and no ceremony to avoid.

#### 12.6.6 When the CLI is not there

Every tool fails with `code: "cli-not-found"`, naming the paths searched. It **never** falls back
to a remembered item list — §12.1's rule does not relax by moving to a different transport, and
the failure mode it guards against (a remembered id that resolves to nothing, renders as nothing,
and reports no error) is identical here. A model receiving `cli-not-found` should point the user
at `/claude-tui-line:setup` rather than improvise.

Whether the server spawns the CLI or links the core is left open by §12.6, and this rule survives
either choice: if it spawns, it reports the paths it tried; if it links, `cli-not-found` cannot
arise and the tool simply works.

#### 12.6.7 The complete list of files an MCP tool may write

Four, and no others:

1. the config file, at the resolved or explicitly-given path — whole-file, atomically
2. the ledger and its artifacts under `~/.claude/claude-tui-line/backups/` — append-only, per §12.2
3. `~/.claude/settings.json` — **only** the `statusLine` key, **only** from `revert`, atomically,
   preserving every other key and the file's formatting
4. the **`scriptOriginalPath` recorded on the ledger entry being restored** — **only** from
   `revert`, **only** when no file exists at that path, and **never** overwriting one that does

The fourth was missing while the surrounding text described it, and a list that says "and no
others" is exactly the wrong place for an omission. §12.5 and §12.6 both require `revert` to put
the copied script back when it has gone, because a restored command pointing at nothing leaves
the user with no statusline and no obvious cause. An implementor obeying this list literally would
have skipped that write and produced precisely the failure the restore exists to prevent.

Note how narrowly it has to be stated. "Restore the script" is otherwise an arbitrary-path write
driven by the contents of a data file, which is a different and much larger permission than the
other three. The path must come from the entry being restored, the file must be absent, and only
`revert` may do it.

No temp files outside the target's own directory, since an atomic rename requires the same
filesystem. No logs, no caches, no state directory. The server is stateless, and a state file is
the first step toward exactly the drift §12.6 exists to prevent.

#### 12.6.8 Version reporting, not version gating

`list_items` reports `cliVersion` alongside the items. It does **not** refuse to operate on a
mismatch.

The skew is real — a plugin update can leave a rebuilt server pointed at a stale binary — but it
is narrow, because `/claude-tui-line:setup` builds both from one tree. A gate that refuses to run
is a worse outcome than the skew it prevents for a user whose statusline is working fine. Report
it, and let the model raise it if something looks wrong.

#### 12.6.9 Four more rulings the wire contract needed

**`confirm: true` requires an explicit `target`.** The table marks `target` optional while
§12.6.4's text says "called with `confirm: true` and an explicit `target`, it restores", which
leaves `confirm: true` alone reading either as an error or as "restore `origin`" — the default
`/claude-tui-line:revert` uses. Ruled: it is an error, `code: "target-required"`, returning the
same listing the unconfirmed call returns.

This **deliberately diverges from §12.5**, and the divergence is the whole point. The slash
command may default to `origin` because a human typed the command and read its name. An ambient
tool call is reached without the user having asked for anything of the kind, and a single boolean
should not be able to roll a config back to the state it had before this tool was installed. The
cost of being wrong is asymmetric, so the cheap call stays the one that only looks.

**`revision` for a file that does not exist is `"absent"`, not null or missing.** §12.6.5 makes
`baseRevision` optional so a first write needs no ceremony, which handles the *caller* omitting
it — but it leaves the fresh-machine case with no compare-and-swap at all: two sessions both
find no config, both write, and the second silently discards the first. That is the exact failure
CAS exists to prevent, occurring at the one moment two agents are most likely to act on the same
file. So `get_config` reports `revision: "absent"` when `source` is `"none"`, and `set_config`
given `baseRevision: "absent"` refuses with `stale-revision` if a file now exists. Create becomes
atomic in the mechanism that already exists rather than in a second one.

**`preview` returns `diagnostics[]` when it was given an inline `config`.** §7 makes a bad config
render *silently degraded* rather than fail, so previewing an invalid candidate returns plausible
rows — and §12.6 has just told the model that looking at rows is how it checks its work. Without
diagnostics, "the preview looked right" is evidence for a config `set_config` will reject. The
check has already run; returning it costs nothing and stops preview from being quietly weaker
than the loop it anchors.

**`preview` executes the config's `command` items — including an inline one — and is therefore
not the cautious call.** §9.1.1 splits the two: `check` runs nothing, `preview` runs the real
pipeline. Over MCP that split matters more than it does at the CLI, because the inline `config`
argument the ruling above just made useful means a caller can hand the server a config that was
never written to disk and have its commands spawned. Ruled: it still runs them. A preview that
skipped them would return rows that no statusline will ever produce, and the model would accept a
config on the strength of a picture of a different config — the §7.1 outcome, arriving through
the tool built to prevent it.

What has to be said instead is the ordering, because the tool surface invites the opposite guess.
`check` is cheap, side-effect-free, and safe to call on anything; `preview` is neither, and
"I'll preview it first, just to be safe" is backwards. **A caller entitled to call `preview` on a
config is a caller entitled to call `set_config` with it** — same trust, one step earlier. The
tool descriptions must say so, since a model reads them and nothing else, and both §12.6.4's
look-before-you-leap default and §12.6.6's no-improvising rule work by making the cautious call
the obvious one. That only holds if the model knows which call is the cautious one.

#### 12.6.10 `preview` has no `notes`, which is §9.8.1's defect one layer up

§12.6.1's table returns `renders[]` — each `{ columns, rows[] }` — plus a top-level
`diagnostics[]`. §12.6.9 then spends a ruling arguing that `diagnostics[]` must be there, because
§7 makes a bad config render *silently degraded* and "the preview looked right" is otherwise
evidence for a config `set_config` will reject.

Every word of that argument applies to render notes, and none of it was applied to them. A dropped
pane produces a preview that also looks right — shorter, with nothing in the response saying it
should have been longer. §9.8.1 already found this exact hole in `--preview --json` and closed it
for the reason that decides it here too: *the JSON form is the one a model reads*. The MCP surface
is not merely one such form, it is the only one a model reaches when nobody typed a slash command.
And §9.8.1's second ruling — a note never appears in `diagnostics`, a diagnostic never appears in
`notes` — means the model cannot recover the information from the field it does get. It is not
degraded; it is absent, and correctly so, guarding a channel that was never built at this layer.

**`preview` gains `notes[]`, and it belongs to each render rather than to the response.** This is
the part that does not carry over unchanged from §9.8.1, where a single render made a top-level
array unambiguous. §12.6.3 has `preview` render at **80 and 60** when given no `columns`, so a
response-level array cannot say which width dropped the pane — which is the entire content of the
note. The split falls straight out of §9.8: a diagnostic is width-independent, so it belongs to the
response; a note is what happened *at a width*, so it belongs to the render.

The one wrinkle is worth stating because it looks like a bug: a `maxLines` cap is width-independent
and will therefore appear in **every** render's `notes`, identically. That is the honest
representation rather than a duplicate — it is what the CLI already does across §12.4's three
widths, where the same notes arrive on stderr (§9.8.1), and de-duplicating it into a response-level array would recreate the ambiguity this
ruling exists to remove. Three copies of a cap note is one finding.

**`rows[]` stays unstyled here, and that is not in tension with §9.3.2.** §9.3.2 rules that the
bare CLI `--preview` must not degrade, because its output lands in a terminal and styling is part
of what is being previewed. The MCP tool's consumer is a model relaying to a chat surface, which
cannot render ANSI at all — escapes there are not colour, they are noise in the middle of a
sentence. So the same test §9.3.2 applies gives the opposite answer again: the payload is not lost
by omitting styling, because the channel could never have carried it. A model that needs to show a
user what a colour change looks like has the CLI, whose output goes where colour means something.

#### 12.6.11 "Re-read instead of clobbering" makes the bytes current and leaves the intent stale

§12.6.5 refuses a stale `set_config` and tells the model to "re-read instead of clobbering."
`/claude-tui-line:edit` step 5, which has no `baseRevision` to be refused by, reaches the same
instruction from the other direction: re-read immediately before editing, "rather than editing the
copy you read at step 2." Both are right. Both stop one step short of the same place, and they
stop there in the same words, which is what made it look settled.

Re-reading makes the *content* current. It does nothing to the *intent*, which was derived against
the older content and is then applied unchanged to the newer one. "Remove the second item in the
first pane" is a JSON Pointer resolved at read time; a hand edit that reordered that pane between
the two reads leaves the pointer aimed at a different item. The command removes the second item —
of a config it never showed anyone — and every step after it passes. `--check` validates the
result, the preview renders it, step 8 shows the user a config that is exactly what was written and
is not what was asked for.

What re-reading *does* prevent is worth keeping, and neither section overstates it: the naive
alternative writes the step-2 copy back with the edit applied, discarding the intervening write
whole. Re-reading turns a total loss into a misapplied edit. `edit.md` is scrupulous about the
rest, too — it says step 4's checkpoint makes a clobber "recoverable rather than preventable,"
which is the honest claim and the right one. The single word that overreaches is calling the
re-read "the whole of your protection."

The missing step costs one comparison, against data both layers already hold. **Ruled:**

1. **Compare the re-read against the first read and branch on the answer.** Unchanged — the
   ordinary case — proceeds exactly as now. Changed: do not apply the pending edit. Re-derive it
   against the new content, then apply it.
2. **Say so.** A concurrent write to the file being edited is information the user does not
   otherwise have, and one of the three writers §12.6.5 names is *them*, in an editor. They are the
   only party who can say whether the two edits belong together. Naming what moved costs a
   sentence; §12.2's checkpoint is the backstop for when nobody read that sentence.
3. **The same for the server.** §12.6.5's sentence should say re-derive, not re-read. A caller that
   answers `stale-revision` by fetching a fresh `revision` and resubmitting its original `config`
   has written a retry loop that is not a retry.

Rule 3 is the one that matters, because compare-and-swap makes it look handled. That resubmission
*succeeds*, and the mechanism records a clean swap over a write that discarded someone's work — so
the arrangement now carries evidence of its own correctness. Without CAS the clobber is silent;
with CAS and a naive retry it is silent and attested. **The refusal was never the protection; it
was the notification.** What the caller does next is the protection, and that is the half §12.6.5
left to the caller's judgment while appearing to have specified it.

This is the same shape as §12.6.9's fresh-machine finding, one turn later in the exchange. There,
the gap was a caller who never supplied `baseRevision`; here it is a caller who supplies it,
is correctly refused, and then defeats the refusal by complying with it.

#### 12.6.12 A bare `stale-revision` refusal manufactures the retry it exists to prevent

§12.6.11's rule lands cleanly on the command path and does not transfer to the MCP path, because
the two layers divide the same knowledge differently. In `/claude-tui-line:edit` one actor does
everything: the model reads the file, works out the change, re-reads, and writes. It holds both
copies, so "compare them and branch" is an instruction it can actually follow.

At the MCP layer nobody holds both halves. **The server has the revisions and no idea what the
caller meant; the model has the intent and cannot see the file.** `set_config` receives a finished
config, not "remove the second item" — the intent was resolved into bytes before the call was made,
which is exactly the resolution §12.6.11 says goes stale.

That asymmetry decides what the refusal has to contain. A bare `{ "code": "stale-revision" }` leaves
the model one affordance: call `get_config`, take the new revision, resend the config it already
built. That is not a caller behaving badly — it is the only move the response makes available, and
it is precisely the clobber §12.6.11 names. **A refusal that offers no way to re-derive is a
refusal that produces the naive retry**, and it will do so while logging a correct rejection first.

1. **The `stale-revision` error must carry the current config and the current revision in its
   payload**, not a code and a sentence. Then the model's cheapest next move is to look at what
   changed, which is the branch — and the expensive move, ignoring a config it was just handed and
   resending its own, is one no model takes by accident. The refusal stops being a wall and becomes
   the second read.

2. **The tool description must state the compare-and-branch rule**, because on this path it is the
   only documentation anything reads. Nothing loads `commands/edit.md` when a model calls
   `set_config` directly — the description *is* the procedure, and a rule that lives only in a
   command file is absent from the layer with no command. Description and error payload are not
   redundant: the description is what makes the first call careful, the payload is what makes the
   retry correct, and neither substitutes for the other.

3. **The server must not attempt the merge itself.** It is the actor without the intent, so merging
   would mean inferring from two configs what the caller was trying to do — and inferring it
   *authoritatively*, since the result would then be written and reported as a clean compare-and-swap
   success. That is §12.6.11's failure with an extra step and better paperwork. The server's job ends
   at handing back enough for someone who knows the intent to redo the work.

The rules point opposite directions at the two layers, which is worth stating plainly so neither is
read as the other. In `/edit` the model must not proceed **when the two reads differ**. Over MCP it
must not proceed **when the write is refused** — it has no two reads to compare, and the refusal is
its only signal that there was anything to compare. Same finding; the trigger moves because the
knowledge does.

### 12.7 `/claude-tui-line:setup`

Numbered last and run first. `setup` is the command the README sends every new user to, it is the
only one that can create the `origin` entry, and until now it was the only command in this section
with no subsection at all — five decisions lived exclusively inside `commands/setup.md`, where the
CLI and the MCP server would never see them. §14.4 is about exactly this shape.

It checks the toolchain, builds, backs up per §12.2, writes `statusLine`, and shows a render. The
rulings that are not obvious:

**The SDK check runs in the project directory, not the user's.** `dotnet --version` reports the SDK
selected *for the current directory*, and any `global.json` above it can pin a different one.
Checking in one directory and building in another can pass a check for an SDK the build never uses,
and then report the resulting failure as a build error rather than a toolchain one.

**An unset path variable names a real directory, and it is a worse one.** The build goes to
`"${CLAUDE_PLUGIN_DATA:-$HOME/.claude/claude-tui-line}/bin"`, and the fallback is load-bearing
rather than defensive padding: unset, `"${CLAUDE_PLUGIN_DATA}/bin"` expands to **`/bin`**, making
the command a release publish into the system binary directory — failing on permissions, or with a
privileged shell, succeeding. `settings.json` would then point at `/bin/claude-tui-line` and the
preview would confirm it renders. Resolve the directory once into a variable and use that variable
everywhere, so there is one place this can be wrong.

**Verify by running the value, not the variable.** Read `statusLine.command` back out of
`settings.json` and run *that string, verbatim*. Not the path expression that produced it — that
path was already proven by the build, and it is not what is in doubt. The one untested thing after
the write is **the expansion**: `settings.json` does not interpolate shell or plugin variables, so a
literal `${CLAUDE_PLUGIN_DATA}` written through unexpanded produces a command that does not exist.
Testing the variable verifies the half that was never at risk — the preview renders perfectly,
setup reports success, and the user gets a blank statusline with nothing pointing at why. §12.5's
revert already verifies this way, by printing and running the command it restored; two commands
answering the same question differently is worse than either being wrong alone, and this is the one
that runs on every install.

**Say that the preview payload is minimal.** It carries a `cwd` and a model name and nothing else,
so items depending on workspace, session, or usage fields render absent and will appear once it is
live. Unsaid, a correct install reads as a half-broken one and the user's first act is debugging
something that works. *This ruling is superseded by §12.7.1 — saying it is a mitigation, and the
problem has a fix.*

**If the backup was a `checkpoint` rather than an `origin`, say so and give its timestamp.** Bare
`/claude-tui-line:revert` then does *not* restore the state just backed up — it restores the older
`origin`, correctly and by §12.5's design — and a user who was told "your statusline is backed up"
will not expect that. The default is right; leaving the difference unsaid is what is wrong.

**Build output, and how it relates to §14.** This is a third location, and deliberately so: §14
governs `publish/`, the deploy target a developer's own live statusline executes, which is why
writing there needs approval. `setup` writes to the plugin's data directory instead — a location
that belongs to the installed plugin, that survives plugin updates, and that cannot collide with a
working tree the user may also be building in. §14.1's one-command rule still applies to what it
runs; what differs is the destination, and that a user invoking `setup` has plainly asked for it.

#### 12.7.1 One literal, three commands, and a uniqueness claim scoped to the wrong boundary

`{"cwd":"$PWD","model":{"display_name":"Claude Opus 5"}}` appears character-for-character in
`commands/setup.md`, `commands/migrate.md` and `commands/revert.md`. §9.3 says of its fixture: **"It
is also the only synthetic payload in the binary."** Narrowly true, and the boundary is wrong. The
duplication lives in the command layer, which is the layer the user actually sees output from.

§9.3's own argument against a second constant was that *"two constants would let `--items` and
`--preview` disagree about what an item looks like … `/migrate` consults both in the same session
and has no way to notice they were built from different inputs."* Every word of that applies to the
three copies above, and none of it was said there. `docs/backup-ledger.md` exists as a separate file
for exactly this reason — four commands need the procedure and four copies would drift. This is the
same shape, unextracted, and the copies have already begun to acquire separate prose explaining
them.

The three uses are not one use, which is why a single apology does not cover them:

- **`setup`** shows the render to a first-time user, under the heading *"Show the user what they
  will get."* Under this payload they get a `cwd` and a model name. §12.7's remedy is to *say* the
  render is minimal — a mitigation for a problem that has a fix. The question a new user is actually
  asking is whether the install worked, and a third of the surface rendering blank answers it wrong.
  §12.7 says so itself — *"a correct install reads as a half-broken one and the user's first act is
  debugging something that works"* — and then offers the sentence as the cure.
- **`migrate`** compares two renders of it, which is §12.3.1: the check passes on the empty set.
- **`revert`** runs the restored command against it to prove the string executes. This is the one
  case minimality does not damage, because an empty render still demonstrates the command ran.

**Ruled: `setup` previews at §9.3.1's fixture.** With every field populated the render is complete,
the user sees the statusline they are getting, and the "it is minimal" paragraph is not needed —
replaced by §9.3's stderr admission, which says *the data is invented*. §12.7's paragraph says *the
tool is incomplete*. Those are different messages and only one of them is true.

**This is why §12.3.1's emit flag is not migrate's alone.** Setup's step 5 must run
`statusLine.command` **verbatim** — §12.7's ruling above, and correct, since the expansion is the
untested thing. That forecloses `--preview`'s empty-stdin fallback: the verbatim command is the
render path, and the render path requires a payload on stdin. Verbatim-command and complete-payload
become jointly satisfiable only once the binary can hand its fixture to a pipe. The flag is
therefore load-bearing for three commands, not a convenience for one, and `revert` should use it as
well — for consistency rather than because its own case is broken, since three copies of a literal
is how the fourth comes to be written.

Note the tension §12.3.1 rule 3 already identified, and that it resolves the other way here. The
fixture's `cwd` is `/home/you/code/acme-web`, deliberately not a real path, so filesystem-derived
items go blank under it. Migrate needs verification coverage and therefore needs both payloads;
setup needs one honest, good-looking render and needs only this one. A git item reading blank
against an invented path, with stderr saying the payload is invented, is legible. Two-thirds of the
statusline blank with a paragraph of explanation is not.

#### 12.7.2 The emitted payload carries the real `cwd`, and that collapses the two-payload rule

The paragraph above, and §12.3.1 rule 3, both assumed the flag would emit §9.3.1's fixture byte for
byte. Checking the render path first — the discipline this document keeps rewarding — shows that
would not have worked, and that the fix is smaller than either section's workaround.

`--preview` is not one path. On **empty stdin** it uses `SyntheticFixture.Input` together with
`CreateItemContext()`, whose `gitBranch`, Engram result and remote URL are all canned: fully
deterministic. On **non-empty stdin** it parses the payload but still probes the real machine for
those three, exactly as the render path does. So a fixture arriving *through a pipe* takes the
probing branch. Emitting it verbatim would pair an invented `cwd` with real machine state — not
merely blank items, but an **incoherent** render, which is worse than the minimal one it replaces
and defeats the whole argument of §12.7.1.

**Ruled: the flag emits the fixture with `cwd` replaced by the process's working directory, and
nothing else changed.** One flag, one behaviour, all three commands. This is not a second fixture in
§9.3's sense — that rule forbids a second *authored* constant, and here exactly one field is
derived from the environment rather than written down. §9.3.1's pinned constant is unchanged and
stays visibly synthetic for `--items` examples and the empty-stdin path, which is where determinism
is actually needed.

Pin the relationship with a test, because this is precisely the assumption a later reader will make
wrongly: the emitted payload equals `SyntheticFixture.Input` in **every field except `cwd`**, which
equals the process working directory. Without that test, "emit the fixture" reads as byte-equality
and someone eventually asserts it.

What this buys, and the reason it is a simplification rather than a concession: with a real `cwd`,
the filesystem-derived items resolve coherently, so a **single** payload exercises the stdin-derived
and the filesystem-derived halves at once. §12.3.1's second run existed only to reach the half the
invented path could not, and it is no longer needed. Migrate compares one payload, setup previews
one payload, revert executes one payload.

The honest limit, stated because §12.7.1 must not be read as claiming more than it earns: this
render is **coherent and complete, not deterministic**. It varies with the machine it runs on, since
the git and remote-url probes are real. Setup and revert want exactly that — they are showing a user
their own machine. Migrate is unaffected because it compares two renders of the same payload in the
same session, where the probes agree with each other by construction.

## 13. Out of scope for v2

- ~~True-color / 256-color palettes.~~ **Resolved — see §6.2.** The decision this bullet was
  waiting on is the opt-in `colorSystem` knob, defaulting to `standard`, which keeps the parity
  baseline valid by construction instead of trading it away.
- Long-running provider daemons, watch-mode providers, push updates.
- Interactive elements. A statusline is a render target, not a TUI app: **this process never reads
  input and never holds focus.** Two things that phrasing used to blur, both of which matter
  elsewhere in this document:
  - It does not say the terminal is never resized. It says no resize *event* is delivered — the
    process exits and is re-run, and a new width simply arrives as a different `COLUMNS` on the
    next tick. Resize is a case this document handles carefully (§5.0.1, §4), and a bullet under
    a heading reading "out of scope" must not be readable as licence to skip it.
  - It does not say nothing we emit can be clicked. §3.2 emits OSC 8 links precisely so the
    terminal can make them clickable. The boundary is that the interaction is entirely the
    terminal's: we write a string, we never learn that anyone clicked it.
- Static analysis of what a `command` item *prints*. Ruled in §9.1.1 and repeated here because
  this is where boundaries belong: `--check` validates a declaration and never executes it, so no
  diagnostic can ever be about a command's output, its format, or its width.
- Per-item wcwidth — `Plain.Length` remains the width metric (SPEC.md §6). Still the ruling, but
  see **§13.1**: what was missing was the consequence, not the decision.

### 13.1 What `Plain.Length` costs, stated

`Plain.Length` counts UTF-16 code units. Terminal layout needs display columns. The two agree for
the ASCII this statusline mostly carries and disagree for everything else:

| text | code units | columns |
|---|---|---|
| `abc` | 3 | 3 |
| CJK — `日本語` | 3 | **6** |
| non-BMP emoji — `🎉` | 2 | 2 |
| ZWJ sequence — `👩‍💻` | 5 | **2** |
| combining mark — `e` + U+0301 | 2 | **1** |

Keeping `Plain.Length` remains right: a wcwidth table is a real dependency, §2.7's parity baseline
is stated in terms of this metric, and what this statusline actually renders — paths, branch names,
model names, counts — is overwhelmingly ASCII. What changes is that the word "deliberately" stops
standing in for a consequence nobody had written down.

**The consequence: a pane containing wide characters draws wider than the compositor believes, and
the rectangle invariant does not notice.** §10 bullet 3 measures ANSI-stripped width with the same
metric the renderer sizes with, so a row that visibly overruns its border scores as exactly the
right width, and the assertion this document calls the one that catches ragged padding, height
mismatch and overflow together reports success. That is §10 bullet 7's own warning — *both sides
can share a wrong constant* — arriving in the place bullet 7 did not look, which is the measuring
instrument rather than the bash comparison. And it is §10.1's shape once more: the suite is not
wrong about what it measures, it is silent about what it does not.

So the limitation is recorded as a **test asserting the current, known-wrong behaviour** — a CJK
string in a fixed-width pane produces a row that passes the rectangle invariant and is visibly too
wide — carrying a comment citing this section. That is what makes a stated limitation survive a
refactor: anyone introducing a width-aware measurer breaks that test and has to come here and
decide, instead of discovering afterwards that the parity baseline moved.

### 13.2 Slicing at code-unit boundaries was never a decision

`Plain.Length` approximating *width* is accepted above. `Plain` being **cut** at code-unit
boundaries is a different thing, and nobody chose it.

`PaneRenderer.WrapSegment` cuts with `Plain.Substring(i, innerWidth)`; `TruncateSegment` cuts with
`Plain[..contentBudget]`. Neither checks whether the cut lands between a high surrogate and its low
surrogate. A non-BMP character straddling the boundary is split into two lone surrogates — not a
narrow row or a clipped glyph, but **invalid UTF-16 on its way to stdout.**

The emoji row in the table above is what separates the two problems: `🎉` is 2 code units and 2
columns, so the width metric is accidentally *correct* for it, and the slice is broken anyway. One
defect is not a symptom of the other and fixing the metric would not fix this.

§2.6's wrap traps require that a hard break never land inside an escape sequence. The equivalent
sentence was never written about a character, so the guard exists for one and not the other, and
the test that would have caught it was never asked for. **Filed as defect 16.** The fix is a
boundary check at the cut — advance by one unit when the index falls between surrogates — in
**both** paths, because both cut and only the wrap path is usually remembered.

#### 13.2.1 "A boundary check at the cut, in both paths" is three sites, one direction, and a loop that stops

§13.2 files the defect and prescribes the fix in a sentence. Every load-bearing part of that
sentence is wrong or missing, in ways that a test asserting "the output contains no lone
surrogate" passes anyway.

**There are three cut sites, not two.** `PaneRenderer` cuts `Plain` in each of these places:

- `TruncateSegment`, the too-narrow-for-the-ellipsis branch — a bare prefix, and the site nobody
  lists, because the sentence in §13.2 names the wrap path and the truncate path and this one is
  inside the truncate path but is not the cut that section was looking at.
- `TruncateSegment`, the normal branch, cutting to `innerWidth - ellipsis.Length`.
- `WrapSegment`, cutting each chunk out of the middle of the string.

§9.2.2's degradation ladder adds a fourth. Counting them here would only date this paragraph the
next time one is added — which is the failure §9.6.2.2's rule D exists to catch, and it is the
same failure in code. **Ruled: one helper computes every cut, and no site does index arithmetic on
`Plain` directly.** A boundary check repeated at each site is correct exactly until someone adds a
site, and the person adding it has no reason to look here.

**The cut rounds down, and "advance by one unit" reads as up.** Taking the extra code unit keeps
the pair intact and puts the row one column over budget — and because `Plain.Length` *is* the width
metric (§13.1), one column over budget is a real overflow: the pane draws wider than it was
allotted, which pushes a border or wraps the terminal row. Rounding down leaves one blank column,
which nobody can see. So the helper returns the largest index at or below the requested one that
does not fall between a high surrogate and its low surrogate.

**Rounding down alone does not terminate.** At `innerWidth == 1` against a non-BMP character, the
rounded-down cut is zero, the chunk is empty, the carried index does not move, and the wrap loop
spins — in a process Claude Code runs once a second, forever, on a config that merely contains an
emoji in a narrow pane. **Ruled: when the rounded-down cut would be zero and content remains, the
helper takes the whole pair and the row goes one column over.** This is the one place the forward
direction is right, and it is right because the alternative is not a wider row but a hang.

**The wrap path needs a carried index, not a boundary check.** `WrapSegment` advances by a fixed
stride, so shortening one chunk does not move where the next one starts: the low surrogate that was
trimmed off the end of row N is still sitting at the start of row N+1, and the defect survives in
the path §13.2 was actually looking at while the truncate path goes green. The loop must carry the
index the helper returned. This is why the fix cannot be a check bolted onto the existing shape.

The test that distinguishes a real fix from a passing one is not "no lone surrogates in the
output" — the naive fix satisfies that at the ends and fails it in the middle. It is: **wrap a
string of non-BMP characters at a width that guarantees a mid-pair cut, and assert every row is
independently valid UTF-16 and that concatenating the rows reproduces the input exactly.** Nothing
lost, nothing duplicated, no row over budget except the documented single-pair case.

### 13.3 A section number is a reference, and four of them resolved to nothing

Walking §2 turned up `§2.9` cited nine times with no such section, which prompted checking every
`§N.M` in the document against its own headings. Four numbers are cited and never defined:

| cited | times | what it means |
|---|---|---|
| `§7` | 27 | the failure-behaviour rules. `§7.1` existed as a subsection of nothing |
| `§2.9` | 9 | the two-pane worked example, living unheaded at the tail of §2.8 |
| `§10.6` | 3 | bullet 6 of §10's list, cited in subsection form |
| `§4.3` | 1 | derived items — `from` / `extract` / `case` |

All four are now closed, and they needed three different fixes — which is the first thing worth
recording, because "four dangling references" reads as one problem with one remedy.

**This is §9.6.1's registry rule, which the document applies to diagnostic codes and does not
apply to itself.** "A code that is not in it does not exist" is exactly as true of a section
number, and both are references whose whole value is that following them lands somewhere. The
consequences are not hypothetical: §11 defines a phase's acceptance criterion as "Acceptance is
§2.9", and §9.4's severity argument turns on "§7 makes the renderer cope" — load-bearing claims
resting on pointers into empty space.

The distribution is the interesting part. **`§7` is both the most-cited reference in the document
and the most thoroughly missing**, and that is causal rather than ironic: a reference used
constantly is one every reader already knows the meaning of, so nobody follows it, so nobody learns
it goes nowhere. Frequency of citation is *negatively* correlated with the chance anyone checks.
The single-citation dangle, `§4.3`, is the one a reader would most likely have caught.

**How each was closed, and why the remedy is not uniform:**

- **`§7` and `§2.9` — the content existed and had no heading.** Promoted in place. This is the
  default fix and the one the general rule prescribes.
- **`§4.3` — the content existed, had a heading, and was under the wrong one.** `from`/`extract`/
  `case` were introduced inside §3.2's hyperlink worked example, where they read as part of linking
  rather than as one of §9.6.2's four item kinds. The single citation was not wrong about where it
  *should* be. Promoted to a real §4.3 and the worked example now points at it, which is the fix
  the citation was already describing.
- **`§10.6` — the citation was wrong and had to be rewritten.** This reverses what an earlier draft
  of this section prescribed, and the reversal is the useful part. That draft said to promote §10's
  bullets to subsections rather than rewrite the citations, on the general principle that bullets
  renumber silently while headings are visible when they move. The principle is right and the
  application was wrong: **§10.1 already exists as a heading, and it is not bullet 1.** Promoting
  the bullets would have produced two different meanings for `§10.1` in the same document —
  converting a dangling reference into an ambiguous one, which is strictly worse, since a dangling
  reference at least fails when followed. The three citations now read "§10 requirement 6", and §10
  says outright that its numbered items are cited that way.

  The general rule survives with a precondition: promote content to headings **when the number is
  free**. When a section already numbers its subsections on a different axis, a positional list
  inside it cannot join that series, and the citations are what must change.

- **Check this mechanically, not by reading.** Every instance here survived many careful readings
  of the surrounding prose, because prose citing a section reads correctly whether or not the
  section exists — the sentence carries the meaning and the number is decoration until someone
  tries to follow it. Comparing the set of cited numbers against the set of heading numbers is
  three lines of shell and belongs in `check-docs.sh`, alongside the §9.7 version-drift check in
  the test suite, which exists for the same reason: two things that must agree, and no symptom
  when they stop.

## 14. Building, and the difference between a build and a deploy

**Relocated from SPEC.md §10 requirement 2.** Nothing here is new; it was already normative, and
it was already being enforced by repetition in session messages rather than by anyone reading it.
It moved because of where it was: v1's acceptance criteria, in a document `README.md` does not
point contributors at. §13.3 established that content sitting under the wrong heading is a
distinct fault from content having no heading, and that its fix is relocation. This is the same
fault one document wider — content sitting in the wrong *document* — and it is the more dangerous
form, because a reader following the README's single pointer to this file has no way to discover
that a safety rule about their build lives somewhere else. See §14.4.

### 14.1 One command produces the artifact

```
dotnet publish src/ClaudeTuiLine/ClaudeTuiLine.csproj -c Release -o publish
```

Exactly one command, with no separate copy step. This was previously written as "copy step or
`-o publish`", offering two equally valid methods, and that ambiguity is what allowed the deployed
artifact to drift: the SDK-default output lands in
`src/ClaudeTuiLine/bin/Release/net10.0/osx-arm64/publish/`, the live statusline runs
`publish/claude-tui-line`, and nothing kept them in sync. The two diverged silently, and every
parity result during v1 Phases 1–3 was measured against an artifact the user does not run.

**A deploy step that depends on a human remembering to copy a file is not a deploy step.**

### 14.2 Identity is a hash, never a timestamp

Because the drift in §14.1 was invisible, identity is established by SHA-256. A newer mtime on the
deployed binary does not mean it came from the current source — mtime records when a file was
written, and the whole failure mode is a file that was written from stale input. Any claim that
something is "shipped and verified" names the hash it was verified against.

#### 14.2.1 A hash of the output answers identity, not provenance

§14.2 rejects mtime with the right argument and then substitutes something that fails the same
test. Its case against mtime is that *"the whole failure mode is a file that was written from stale
input"* — mtime tells you **when** the artifact was written, not **what from**. True. But a SHA-256
of the deployed binary tells you **which** artifact it is, and equally not what from. Both answer a
question about the output. Neither reaches the input, which is where the failure lives.

What the hash does buy is real and worth keeping: two people, or the same person across two sessions,
can establish they are talking about the same binary, and a claim of "shipped and verified" becomes
auditable rather than remembered. That is a reporting discipline and §14.2 should be read as one. It
is not detection. Running `sha256sum publish/claude-tui-line` twice and getting the same answer
confirms nothing has been rebuilt; it cannot distinguish an artifact built from current source from
one built from a tree three commits stale, because the stale artifact hashes perfectly consistently
too. Consistency is exactly what a stale file has.

**Ruled: provenance requires the artifact to carry its source identity, not to be measured after the
fact.** The mechanism already exists elsewhere in this document and §14 has never cited it: §9.7
gives the binary a `<Version>` and a `--version` that reports it. Comparing what
`publish/claude-tui-line --version` says against the version in the source tree answers the question
§14 is actually asking — *is what the user runs built from what I am editing?* — and answers it by
asking the artifact, which is the only party that knows. Hash the artifact to name it; ask the
artifact to date it.

Note what this makes of §9.7's drift test. It was scoped as an internal consistency check between
the assembly version and `plugin.json`, and it is also the missing half of §14. Two sections solving
complementary halves of one problem without referencing each other is the condition under which both
get called complete, so: §14 depends on §9.7, and a change to how the version is stamped is a change
to whether deploys can be verified at all.

**Secondary, and deliberately weighted as minor:** `-o publish` in §14.1 is a relative path, and a
relative path names a different real directory per working directory — the shape §12.7 rules on for
an unset variable expanding to `/bin`. Here it is mostly self-guarding, because the `.csproj`
argument is relative too, so a wrong directory fails to find the project rather than publishing
somewhere unexpected. The case it does not guard is a **second clone or a worktree**: both are real
repo roots, both satisfy the `.csproj` path, and only one of them holds the `publish/` the live
statusline executes. That is §14.1's original drift with the two directories renamed, and it is the
scenario §14.2's hash discipline was invented for — which is the argument for §9.7 again, since a
version answers *which tree* while a hash only answers *which file*.

#### 14.2.2 §9.7's test compares two files in the source tree, and the deploy check is still unwritten

§14.2.1 ruled that provenance requires asking the artifact, and named §9.7's `<Version>`/`--version`
as the mechanism. That is right about the mechanism and too quick about the check. §9.7's test
compares the **assembly version against `plugin.json`** — both of which live in the source tree,
next to each other, and are read by the same test run. It proves the tree is internally consistent.
It cannot observe `publish/` at all, and nothing else does either.

So the check §14 actually needs is a distinct step that does not exist yet: **run the deployed
binary and ask it.** `<binary> --version`, compared against the version the tree declares. That is
the only reading that crosses the boundary where the failure lives.

**Which binary is a parameter, not a constant, and hardcoding it would reintroduce the defect at a
different address.** There are two deployment targets, not one: `publish/`, which is where this
repository builds, and `${CLAUDE_PLUGIN_DATA:-$HOME/.claude/claude-tui-line}/bin`, which is where
`commands/setup.md` step 2 installs a real user's copy. A check wired to `publish/` reports green on
the developer's machine while the installed binary a user is actually running is a release behind —
the same all-green-and-wrong shape §14.1 is about, relocated from *which version* to *whose copy*.
So the path is the first argument. `publish/claude-tui-line` is a convenience default for the
common case, never the only thing the check can see.

The tree-side reading must come from `plugin.json`, not from a build. The premise of deploy
verification is a machine where the deployed thing may be wrong, which includes machines where the
toolchain is broken or absent; a check that has to compile before it can answer is unavailable
exactly when it is needed. And "could not determine" must exit differently from "mismatch" — folding
an indeterminate result into failure trains people to ignore a red result, and folding it into
success is a silent pass. Three outcomes, three exit codes: match, mismatch, cannot tell.

It cannot be a unit test, and the reason is the whole point rather than a limitation. A test runs
against what the test run just built; the artifact in question is one somebody deployed at some
earlier time, which is precisely the fact under investigation. A test that built its own copy to
interrogate would be asking the fresh build whether the fresh build is fresh. This belongs in
§14.2's verification step, beside the hash and doing the half the hash cannot: **the hash names the
file, `--version` dates it.**

The failure it catches, stated concretely because every existing check is green throughout it: bump
the version, rebuild, and deploy the *previous* binary. `.csproj` and `plugin.json` agree, so §9.7's
drift test passes. The deployed file hashes consistently with itself on every subsequent check, so
§14.2 passes. §14.3's single-command rule was never violated — the right command ran, it simply ran
before the change. Nothing anywhere is red, and the running statusline is last week's. That is
§14.1's drift surviving every mechanism §14 built to stop it, because all of them measure the
artifact and none of them ask it.

**This does not create the third home §9.7 forbids**, and the distinction is worth stating because
the sentence there reads as though it might. §9.7 rules out a third place a version is *authored* —
`marketplace.json` gaining a hand-maintained copy that the drift test does not cover. The deployed
binary's reported version is not authored anywhere; it is generated from the `.csproj` by the build.
Reading a value back out of a derived artifact is a *check* on the registry, not a second registry.
That is the same authored-versus-derived line §1 turns on, and it is the line that decides whether
adding a reader is a cost or a safeguard.

**One question was flagged rather than ruled here, because ruling it without evidence is how §9.7
got its own defect. It has since been measured, and the answer was yes.** §14.3 says a reviewer
"reproduces a hash" from the SDK-default output directory while deploys go to `publish/`. That
comparison is only meaningful if a native AOT publish is byte-identical across two different `-o`
destinations from the same source. Modern MSBuild is deterministic by default and `obj/` is shared
regardless of `-o`, both of which pointed toward yes — but AOT binaries can embed build paths, and
"points toward" is not a measurement. The same commit was published to two scratch directories,
neither of them `publish/`, and the two binaries' sha256 matched. §14.3's reproduce-a-hash sentence
stands, and §14.2's hash is a check any reviewer can perform rather than one only the deployer can.

The determinism result is what makes §14.2's hash and this section's `--version` genuinely
different instruments rather than two spellings of one. Determinism means the hash answers *which
source produced this*, exactly and cheaply. It says nothing about *when the producing happened* —
byte-identical output from the same commit is byte-identical whether it was published this morning
or last month, which is the whole of §14.1's failure. The hash names the file; `--version` dates it;
determinism is why the first of those is trustworthy and why it cannot be stretched to cover the
second.

The check this section calls for now exists as `tools/verify-deploy.sh` — first argument the binary
path, tree side read from `plugin.json`, three exit codes. It is deliberately not in
`check-all.sh`: everything there answers a question about the working tree and runs anywhere the
repo is checked out, while this one asks about a particular installed copy and is meaningless
without being told which. Running it is a step in deploying, not a step in checking out.

### 14.3 Producing the artifact and deploying it are different acts, and only the second is restricted

`publish/` is what the user's live statusline executes, so writing there replaces something of the
user's *while it is running*. That is a deploy, and it requires approval. It is not the
implementor's to run, and no peer message can authorize it.

Development and verification build to the SDK-default output instead
(`src/ClaudeTuiLine/bin/Release/net10.0/osx-arm64/publish/`). That is where an implementor or a
reviewer exercises the binary, measures latency, and reproduces a hash — freely, with no approval,
because nothing the user runs is touched.

This does not reintroduce §14.1's drift. That drift came from *two* ways to produce the deployed
artifact, one of which was a human remembering to copy a file. There is still exactly one command
that writes `publish/`, it is still the one printed in §14.1, and identity is still established by
hash — the only thing added is who may run it. A build nobody deployed cannot diverge from a
deploy, and the hash comparison of §14.2 is precisely how you confirm it didn't.

AOT trim and analysis warnings from Spectre.Console must be inspected rather than accepted in
bulk: warnings affecting only unused features (tables, live display, exception rendering) are
acceptable and get listed in the implementation report; warnings on markup or rendering paths are
defects.

### 14.4 Why this section's existence is itself the finding

The audit that produced this section went looking for a *stale* document — the ordinary
two-authorities risk, where a contributor reads v1 and applies superseded rules. That is not what
`SPEC.md` is. It is cited as live authority from this file in four places (§6b for per-render
config reload, `SPEC.md:353` for the empty-surface rule, §6 for `Plain.Length` as the width
metric, and Phase 1 as a prerequisite), so it is not superseded and must not be archived.

The real defect was the opposite shape, and worse. **A load-bearing safety rule was reachable only
through a document nobody is pointed at.** The rule was never wrong, never stale, and never
disputed; it was simply not where its readers are. It survived because it was restated by hand in
session after session, which reads like the rule working and is actually the rule being carried
by something that does not persist.

Two things generalise:

- **"Is this document stale?" is the wrong audit question.** It only finds authorities that
  disagree. Ask instead: *what does this document say that no reader of the current one will ever
  see?* A superseded rule announces itself the moment two readers compare notes. An unreachable
  one has no symptom at all — everyone who knows it keeps following it, and everyone who doesn't
  never learns there was something to follow.
- **A rule enforced by repetition is not enforced.** If a constraint has to be restated in every
  session for the work to come out right, the restating is load-bearing and the document is not.
  The test is whether a competent stranger reading only what the README points at would arrive at
  the same behaviour.

`tools/check-citations.sh` cannot catch this class. It verifies that citations *within* this
document resolve, which is a closed-world check; this defect is about what is missing from the
world entirely. Cross-document reachability is not mechanically checkable in three lines of shell,
which is exactly why it is written down here instead.

**A resolving citation can still point at the wrong section, and that is a third gap left open on
purpose.** §9.6.3 read "§12.2 instructs the migrator to preserve colours" for as long as it stood.
§12.2 is the backup ledger and says nothing about colour; the ruling is §12.3's. The citation
resolved, so the check passed — it proves a section *exists*, never that it says what the citing
sentence claims. The only mechanically checkable sub-case is a citation carrying a quoted phrase,
where the phrase could be searched for in the cited section. That is **15 of this document's 630
citations**, and several of those quotes are deliberate paraphrase, which a checker would report as
failures. A fourth check would cover a fortieth of the surface while crying wolf on part of it —
below the bar `check-counts.sh` sets in its own header, and an ignored check occupies the slot a
real one would have. So this one stays manual, and stays written down.

**It recurred within the hour, in the paragraph above's own author.** §9.6.3.1 was given a sentence
ending "§13.1's rule applies to a count in prose the same as to one in code" — and §13.1 is *What
`Plain.Length` costs, stated*, which has no rule about counts. Same session, minutes after the fix
above was committed, by someone actively looking for this. That is the finding: "be more careful"
is not a mitigation, because the failure is not carelessness. Writing a citation is an act of
recall, and recall returns a number that feels right; nothing in the act of writing it prompts a
lookup, and the prose reads correctly either way.

What caught it was cheap and mechanical enough to be a habit rather than a discipline: **read the
cited heading's title, not just its number.** One `grep` against the heading, and the mismatch
between "what I am about to claim it says" and "what it is called" is immediate — it needs no
judgement and no reading of the section body. The citation that survives that check can still be
wrong, but it can no longer be wrong in the way both of these were, which is the common way: not a
subtly misread section, but an entirely unrelated one whose number was close to hand.

**The habit found two more on its first use, and neither was newly written.** §9.6.3.1 cited `§2`
twice — for `--colors --json` being unstyled, and for the bullet saying the command exists to end
guessing. Both belong to **§9**, whose bullet list says both things in as many words; §2 is the
render surface and mentions neither. They resolved, so the check passed, and they had been read
past every time the section was revised. Being cheap is the whole property: reading a heading title
costs nothing, which is why it gets done on a citation nobody suspects — and an unsuspected
citation is the only kind this defect ever hides in.

The instructive part is that the first sentence was fixed by **deleting the citation** rather than
repointing it. There is no section stating that rule; the sentence never needed a pointer, and the
citation was there for the authority it borrowed rather than for anything a reader would follow. A
citation that cannot be checked in five seconds and that the sentence does not need is not neutral
— it is the next entry in this table.
