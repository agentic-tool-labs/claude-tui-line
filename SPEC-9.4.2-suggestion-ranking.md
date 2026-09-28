# §9.4.2 addendum — ranking, ties, and the bound the prefix rule never had

**Spec path note.** No path was dictated in the dispatch. I have followed the repo's anchored-splice
fragment convention and chosen this one. Move it freely; nothing depends on the name.

**Task:** #57. **Implements:** D1 (Jim's ruling), D2 (mine, confirmed), the coverage note, and D3 —
a defect found while reading the code, not in the original dispatch, now also ruled by Jim.

**Amendment, and the only change in this revision.** D3 was written as a recommendation with the
decision routed to Jim. Jim has ruled: adopt the three-character floor. The splice text itself is
unchanged — it was written to be adopted verbatim — and what changed is the framing around it: D3 is
no longer conditional, verification item 3 is no longer conditional, and the "Open" section is now
empty. The confidence note about the constant being fitted to a single data point **stays**, because
it is still true and the implementor should know which of these rules is the cheap one to revisit.

**What I did and did not do.** Everything below comes from reading `SPEC-V2-FRAMEWORK.md` §9.4.2,
`src/ClaudeTuiLine/KeySuggestion.cs`, and `src/ClaudeTuiLine/ConfigCheck.cs:760–800`. I ran nothing.
The one empirical question is stated as NEEDS-EVIDENCE below.

---

## Why this exists

§9.4.2 specifies when a key *qualifies* as a suggestion and is nearly silent on how the qualifying
keys are then *ranked*. Its one ranking sentence — "When more than one key qualifies, the smaller
edit distance wins" — has two defects, and reading the implementation turned up a third in the
qualification rule itself.

### The state of the code, for context

`KeySuggestion.Suggest` (`KeySuggestion.cs:16–46`) is faithful to the spec as written. The defects
below are the spec's, not the implementation's, with one exception noted at D1.

---

## D1 — the tie-break sentence does not break ties

"The smaller edit distance wins" selects among **unequal** distances. Two candidates at equal
distance — an actual tie, and the only case the sentence looks like it addresses — is unspecified.

`KeySuggestion.cs:38` resolves it with `distance < bestDistance`, so the first candidate wins, and
`KeySuggestion.cs:14` documents the consequence: *"Ties are broken by `candidates` order."* That
order is `ConfigJsonContext.Default.<Type>.Properties` (`ConfigCheck.cs:759–761`) — source-generator
output, not a stable contract. **A diagnostic message that can change across builds with no change
to the config**, in the diagnostic §12's gate leans on hardest.

> **A note on how this arrived, because it is the more useful half.** The implementation did not
> get this wrong. It found the spec silent, chose, and wrote the choice down in an XML comment. That
> is a spec gap filled locally and documented rather than escalated — and documenting it is what
> makes it recoverable, so this is a near-miss worth naming rather than a fault. The gap it filled
> was invisible from the spec and visible from the code, which is the direction gaps usually travel.

**Ruled (Jim, product call): on a genuine tie, name every tied candidate rather than choosing one.**
A tie is not the tool reaching too far — §9.4.2's threshold governs that — it is the tool having two
equally good answers. Withholding both discards information the tool actually holds; picking one is a
coin flip presented as advice.

---

## D2 — ranking by distance defeats the prefix rule

§9.4.2 justifies the prefix rule by saying the abbreviation class is "a different mistake from a typo
and which no distance bound small enough to be safe will ever reach." `ttl` → `ttlSeconds` qualifies
by prefix at edit distance **7**. Under "smaller edit distance wins", any key qualifying at distance
≤2 beats it, and the prefix match is discarded — in exactly the situation the prefix rule exists for.

The two rules produce incomparable quantities. Ranking a prefix match by edit distance ranks it on
the scale the section declared inapplicable to it.

**Ruled: a prefix match outranks a distance match, unconditionally.** Confidence high; the argument
is §9.4.2's own sentence.

---

## D3 — the prefix rule has no length bound, and now gets one

**Not in the original dispatch. Found while reading the code. Ruled by Jim: adopt.**

The distance rule carries two bounds: at most 2, *and* strictly less than half the unknown key's
length. The half-length bound exists because short strings are cheap to be near. **The prefix rule
carries no bound at all** — `KeySuggestion.cs:35–36` is a bare bidirectional `StartsWith`, faithfully
implementing "one of the two is a prefix of the other."

So `{"c": 1}` qualifies against `case`, `color`, `colors`, `colorSystem`, and `children`, and the
ranking hands one over as *did you mean 'case'?* — a confidently wrong suggestion emitted by the
section whose stated principle is that a confidently wrong suggestion is worse than none. The prefix
rule was written as an escape hatch from the distance bound and inherited none of its discipline.

This is a **spec** defect: the implementation matches what is written.

**Ruled: the prefix relation qualifies only when the shorter of the two strings is at least 3
characters.**

The justification is thin and stays stated rather than dressed up, because the implementor should
know which rule here is the cheap one to revisit. Three is calibrated to `ttl` — the shortest real
abbreviation §9.4.2 cites — and below it the "abbreviation" reading stops being credible: a one- or
two-character key is a fragment, and there is no evidence about what it was a fragment *of*. A
half-length bound mirroring the distance rule was considered and rejected: `ttl` against `ttlSeconds`
is 3 against 10, which such a bound fails, killing the motivating example to gain symmetry.

---

## Splices

### Splice 1 — bound the prefix rule (D3)

**Anchor.** The second bullet of §9.4.2's threshold list currently reads in full:

> - One of the two is a prefix of the other. This is what catches the abbreviation class —
>   `ttl` for `ttlSeconds` — which is a different mistake from a typo and which no distance bound
>   small enough to be safe will ever reach.

**Replace the whole bullet with:**

> - One of the two is a prefix of the other, **and the shorter of the two is at least three
>   characters.** This is what catches the abbreviation class — `ttl` for `ttlSeconds` — which is a
>   different mistake from a typo and which no distance bound small enough to be safe will ever
>   reach.
>
>   **The length floor is not decoration.** Without it the prefix rule has no bound of any kind,
>   where the distance rule has two, and the asymmetry is not a decision anyone made — the prefix
>   rule was written as an escape hatch from the distance bound and inherited none of its
>   discipline. Unbounded, `{"c": 1}` qualifies against `case`, `color`, `colors`, `colorSystem`,
>   and `children`, and the ranking below hands one of them over as *did you mean 'case'?* — the
>   confidently wrong suggestion this threshold exists to prevent, produced by the threshold itself.
>   Three is the length of `ttl`, the shortest abbreviation this section cites; below it a key is a
>   fragment with no evidence about what it was a fragment of. A half-length bound mirroring the
>   distance rule was considered and rejected: `ttl` against `ttlSeconds` is three against ten, and
>   it would fail.

### Splice 2 — replace the ranking sentence

**Anchor.** The paragraph immediately after the threshold bullets currently reads in full:

> When more than one key qualifies, the smaller edit distance wins. **When none qualifies, the
> message names no key at all.** `unknown key 'zzzzzz' on an item` is a complete and useful
> diagnostic on its own; it is the code and the path doing their job, and there is no obligation to
> guess on top of it.

**Action.** Delete only the first sentence — "When more than one key qualifies, the smaller edit
distance wins." — leaving the paragraph to begin at "**When none qualifies…**", which stands
correctly on its own and is unaffected by everything below. Then insert the new subsection of
Splice 3 immediately **after** that paragraph, so the reader meets the two boundary cases (nothing
qualifies, several qualify) in that order.

### Splice 3 — the new subsection

Insert after the paragraph edited in Splice 2:

---

##### Ranking, when more than one key qualifies

Qualification is two rules; ranking must not collapse them into one. **A prefix match outranks a
distance match, always, whatever the distances are.** `ttl` → `ttlSeconds` is a prefix match at edit
distance 7, and any key qualifying at distance 2 would otherwise beat it — discarding the prefix
match in precisely the case the prefix rule was written for. The rule above says no safe distance
bound reaches the abbreviation class; ranking a prefix match by edit distance ranks it on the scale
that sentence declares inapplicable to it. The two rules produce incomparable quantities, and the
ranking must compare within a class, never across.

So the order is layered, and each layer is total before the next is consulted:

1. **Class.** Prefix matches rank above distance matches. A prefix match and a distance match
   therefore never tie.
2. **Within prefix matches**, the shorter candidate wins — the one that adds least to what the
   author actually typed.
3. **Within distance matches**, the smaller edit distance wins.

**Whatever remains tied at the top is named together, not chosen between.** `unknown key 'xy' on an
item — did you mean 'ab' or 'cd'?` A tie is not the tool reaching too far — that is what the
threshold above governs — it is the tool holding two equally good answers. Naming one of them is a
coin flip presented as advice, and naming neither discards information the tool has. This binds for
ties of three or more as well, with no cap: a cap would be an arbitrary number, and falling silent
above it would make the diagnostic worse exactly as the ambiguity gets worse.

**Sorting is ordinal, and the spec says so because the language default is wrong here.** Tied
candidates are ordered with `StringComparer.Ordinal`. .NET's `OrderBy(k => k)` on strings uses
`Comparer<string>.Default`, which is culture-sensitive — so the obvious implementation of "sorted"
varies with the machine's locale and reintroduces exactly the build-to-build variability this rule
exists to remove, inside the fix for it. `ConfigCheck.cs:778` already sorts the unknown keys
themselves this way; this is the same discipline one level in.

**The suggestion list is rendered by `ConfigCheck.FormatAccepted`,** the joiner
`unknown-enum-value` already uses (`ConfigCheck.cs:464–469`): `a`, `a or b`, `a, b, or c`. Reusing it
is not a convenience — a second joiner three lines from the first is §1's defect in miniature, and
reusing it makes `unknown-key` read in the same voice as the other diagnostic a user meets in the
same run.

**This changes no JSON shape.** The suggestion is interpolated into the diagnostic's message string
at `ConfigCheck.cs:781`; `Diagnostic` carries a path, a severity, a code, and a message, and has no
suggestion field anywhere in the codebase. §9.6's diagnostic shape is untouched, and this rule must
not be taken as licence to add a structured field for it.

##### One class this diagnostic knowingly does not reach

This section opens with three motivating typos. `colour` → `color` is caught by distance;
`ttl` → `ttlSeconds` is caught by prefix; **`maxLines` → `maxRows` is caught by neither, and that is
intended.**

`maxLines` is not a typo. `Lines` and `Rows` are the same concept in different vocabulary, and the
edit distance between them is 4 — reachable only by a bound loose enough to turn `{"zzzzzz": 1}`
into *did you mean 'color'?*, which is the outcome the threshold exists to prevent. Vocabulary
confusion is a third mistake class, alongside the typo and the abbreviation, and neither candidate
rule addresses it.

The correct behaviour for `maxLines` is therefore the bare diagnostic: `unknown key 'maxLines' on a
pane`, naming no key. That is the paragraph above doing its job, not a gap in it.

**This is recorded so nobody fixes it.** The tempting repair is to loosen the distance bound until
`maxLines` is reached, which trades this section's stated principle for one example. If the
vocabulary class is ever worth catching, it needs a mechanism of its own — a synonym relation is not
a distance — and that is a new rule to be specified, not a constant to be raised.

---

## What must not change

- **The qualification bounds on the distance rule.** At most 2, strictly less than half the unknown
  key's length. D3 adds a bound to the *prefix* rule and touches neither of these.
- **Case sensitivity.** Comparison stays `Ordinal`, matching `PropertyNameCaseInsensitive = false`.
  §9.4.2 calls a case-only mismatch "the single most valuable suggestion this diagnostic can make";
  `"Color"` → `color` is distance 1 at length 5 and must keep qualifying.
- **`unknown-key` stays severity `warning`.** Not revisited here.
- **The known-key set still comes from `JsonTypeInfo`, never reflection.** Nothing here reopens that;
  what changes is that its *order* stops being load-bearing.
- **No new JSON field, and no change to §9.6's diagnostic shape.**
- **`{"zzzzzz": 1}` still names no key.** The bare-diagnostic path is not narrowed by any of this.

---

## Verification

1. A genuine tie names both candidates, ordinally sorted, joined by `FormatAccepted` — and names
   them in the same order regardless of the order the candidate list arrives in. **Demonstrate by
   passing the same candidates reversed and asserting the message is byte-identical.** That is the
   assertion D1 is actually about; asserting only that some tie produces some message would pass
   against the current defect.
2. A prefix match beats a distance match with a smaller distance. Concretely: `ttl` against a
   candidate set containing both `ttlSeconds` (prefix, distance 7) and a key at distance ≤2 suggests
   `ttlSeconds`. **This test must be shown to fail against the pre-change code**, since it is the
   whole of D2.
3. `{"c": 1}` names no key.
4. `{"maxLines": 4}` on a pane names no key, and the test says in a comment that this is specified
   behaviour — not, as the current `KeySuggestionTests.cs` comments have it, a divergence from the
   spec.
5. Existing suggestions are unchanged: `colour`→`color` on an item, `colour`→`colors` at top level,
   `Color`→`color`, `styl`→`style`, `defualt`→`default`, `ttl`→`ttlSeconds`. All six are asserted in
   `ConfigCheckTests.cs:1196–1289` today and none may move.
6. `{"zzzzzz": 1}` still names no key.

**A caution on item 5, since it is the one that can fail for a boring reason.** `ttl`→`ttlSeconds` is
the case where D3 and the motivating example are closest: `ttl` is exactly three characters, so it
qualifies by the floor rather than comfortably above it. An off-by-one in the bound — `> 3` instead
of `>= 3` — passes every other test here and breaks only this one. Item 5 is the guard for that, and
it is why the floor is written as "at least three" rather than "more than two".

---

## NEEDS-EVIDENCE

Neither run by me.

**N1 — is D1 live or latent today?** Over each config object's real key set, does any unknown key
produce a tie between two candidates that both qualify? A loop over the key sets; no judgment
required. *If yes* — the current build can emit different text for the same config across rebuilds,
and #57 is a correctness fix. *If no* — D1 is latent, the fix is a hardening, and the verification-1
test is a guard rather than a repro. Either way the ruling stands; only its urgency moves.

**N2 — is D3 reachable through the real walk?** `{"c": 1}` must actually reach `CheckUnknownKeys`
via `[JsonExtensionData]` on some config object. I believe it does — `Extra` captures any unbound
key — but I have not run it. *If it does not*, D3 is theoretical and its priority drops. Note this
does **not** reopen the ruling: an unreachable degenerate case is still a bound the rule should
carry, and the floor costs nothing when it never fires.

---

## Open

Nothing. D3's constant was the one open item and Jim has ruled it: adopt the three-character floor.

---

## Confidence

High on D2 and on the layered ranking — the argument is §9.4.2's own sentence about the abbreviation
class, and the code confirms the defect is live rather than theoretical. High on D1's mechanism and
on the ordinal-sorting requirement, which is a trap the language default walks straight into.

**Lower on D3's specific constant, and that does not change now that it is adopted.** Three is a
floor fitted to a single data point — `ttl` — and if a two-character abbreviation ever turns out to
be a real key, the constant is the thing to revisit, not the rule around it. It is one number in one
bullet and one predicate in `KeySuggestion.Suggest`, so revisiting is cheap; recording that it is the
soft part of this section is what makes it cheap later.

No escalation to the Ultra-Advisor recommended. The blast radius is one internal static class, one
message string, and a test file; nothing here touches a public interface, persistence, or
concurrency, and every ruling is reversible by editing the same paragraph again.
