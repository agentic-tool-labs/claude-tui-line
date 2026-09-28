# SPEC-87 — compound-reference resolution for `ItemSelector`, `LinkPlaceholder`, and `ColorFrom`

> **STATUS: REVISION 4 — FINAL. No open questions, no gates.** Holes #1b and #4 are implemented and
> landed (commit `56c23f7`). §12 (Revision 2) settles the lookup mechanism for #1a and #3.
> Revision 3 closed §12.9 — the user confirmed part colours win. **Revision 4 closes §12.6.3's E6
> gate: proceed with the threaded parameter as designed** (§12.10), and adds §12.7.1, which fixes
> the one thing E6's answer newly exposed — the map must be built above *both* entry points.
> E1, E2, E4, E6 are answered; E3 and E5 remain implementation-time lookups, not design questions.

> **Scope.** Holes #1, #3, #4 of `SPEC-85-ADDENDUM-spans-threading.md` §12.8.5 (`:542-556`).
> **Hole #2 (`ArgvPlaceholder` → `placeholder-compound-source`) is being implemented separately
> and is out of scope here.** This spec does not touch it.
>
> **This document supersedes §12.8.5's draft leaning toward Warning-and-no-op for these three.**
> That leaning is withdrawn by instruction: where resolution is coherent, resolve. §12.8.5 itself
> only ever *flagged* these as open (`:556`: "The other three have no clean precedent"), so this
> is filling a hole it deliberately left, not overturning a decision it made.
>
> Written as a separate file rather than an addendum edit because it (a) reverses that leaning,
> (b) splits one of the four holes into two cases with opposite answers, and (c) carried one item
> that needed a product call. An in-place amendment would have buried all three.

## 0. The root cause, and why it is not one hole but four

`ConfigCheck.cs:109`:

```csharp
var known = scan.SelfDeclaredIds.Contains(reference.Id) || ItemRegistry.Find(reference.Id) is not null;
```

A compound **self-declares its id**, so `known` is true and the `!known` arm never fires. Only
`DerivedFrom` has a follow-up branch catching a compound target (`:123-127`,
`from-compound-source`). The other forms fall through to no diagnostic at all.

The reason a compound is "known but empty" is the fact this whole spec turns on:

> `SPEC-V2-FRAMEWORK.md:5405` — *"a compound writes no value into the resolution dictionary, so
> this can never work"*

**There are two stores, and the dispatch treats them as one:**

| | populated by | read by |
|---|---|---|
| **resolution dictionary** (`ItemValueResolver`) | builtins, commands, derived | derived `from`, argv placeholders, link placeholders, colour `from` |
| **rendered `Segment`** (`LeafItems` / `SegmentBuilder`) | compounds, and every item at render | the renderer |

`ItemValueResolver.Resolve()` (`ItemValueResolver.cs:35-56`) has **no notion of compound ids at
all**. Compounds are assembled downstream by `LeafItems.BuildCompound()` (`LeafItems.cs:70-172`)
and `SegmentBuilder.BuildCompoundSegment()` (`SegmentBuilder.cs:115-116`), in a separate pass.

**But a compound's plain text does exist.** `SegmentBuilder.cs:116` builds
`string.Concat(spans.Select(s => s.Plain))`, and `LeafItems.cs:41` exposes it as
`ResolvedItem.Value`. So "the compound's plain text" is not a thing to be invented — it is a thing
already computed, one pass too late for the dictionary readers.

That asymmetry is why the four holes do **not** get one uniform answer. Each form is judged on
whether the thing it needs exists in a form it can reach, and whether reaching for it makes
resolution order observable — the property §3.3:2726-2728 exists to prevent.

**§12 is the other half of this section.** §0 says the value exists in the wrong store; §12 says
how a resolution site gets to it. Everything in §2 and §4 below describes *what* to emit; §12
describes *how the emitting site obtains it*, and is required reading before implementing either.

---

## 1. Summary of rulings

| # | Form | Position | Ruling | Code | Severity |
|---|---|---|---|---|---|
| 1a | `ItemSelector` | **item level** — `{"item":"<compound-id>"}` in a pane's `items` | **RESOLVE** — emit the referenced compound's `Segment`, spans intact | — | — |
| 1b | `ItemSelector` | **part level** — `{"item":"<compound-id>"}` inside a compound's own `parts` | **ERROR** | `part-compound-source` (new) | error |
| 3 | `LinkPlaceholder` | `{other-id}` in a `link` template | **RESOLVE** — substitute the compound's `Plain`, markup dropped | — | — |
| 4 | `ColorFrom` | a colour rule's `from` | **NO RESOLUTION** | `color-from-compound-source` (new) | **warning** |

Hole #4 is the one that does not follow the "attempt real resolution" instruction. §5 argues why;
§5.4 records that the argument was put to the user and accepted rather than applied silently.

### 1.1 The severity rule these follow

Severity is **not** chosen per case. Each form already has a severity for naming a *nonexistent*
id (`ConfigCheck.cs:112-120`), and a compound-target diagnostic must match it — naming a compound
cannot be graver than naming nothing at all:

| Form | unknown-id severity (`:114-118`) | compound-target severity |
|---|---|---|
| `DerivedFrom` | error | error — `from-compound-source`, already shipped |
| `ArgvPlaceholder` | error | error — `placeholder-compound-source`, hole #2, in flight |
| `ItemSelector` | error | **error** — §1b above |
| `LinkPlaceholder` | warning | n/a — resolves |
| `ColorFrom` | **warning** | **warning** — §1 above |

This is the load-bearing reason hole #4 is a warning and **not** a reuse of the existing
`from-compound-source` code, which is registered at error severity (`SPEC-V2-FRAMEWORK.md:5405`,
addendum `:534`). Reusing it would make a colour mistake fatal while an *unknown-id* colour
mistake stays a warning, and §9.6 fixes a code's severity permanently once shipped, so the code
cannot be reused at a second severity.

---

## 2. Hole #1a — item-level `ItemSelector` → compound: RESOLVE

### 2.1 Behaviour

`{"item":"<compound-id>"}` appearing in a pane's `items` renders the referenced compound
**exactly as that compound renders**: its `Segment`, with its `Spans` list intact, per-part
colours preserved.

Per §3.3:2683-2690 a compound *"resolves to the same thing every item resolves to: one `Segment`,
one `Plain` string ... and markup carrying a colour change per part."* Emitting that `Segment` is
therefore not a new rendering path — it is handing the renderer the same object the compound would
have handed it in place.

**Do not flatten to `Plain` here.** Per-part colour is the entire point of §3.3; an item selector
that dropped it would silently downgrade the referenced item. §12.9 settles the related question
of what happens when the *selecting* item also sets `color`: the compound's part colours win.

Today this case *"passes `--check`, renders nothing"* (addendum `:550`). This is a rendering
change and needs the regression coverage in §6.

**How the selecting site obtains that `Segment` is §12.** Do not implement this section without
it — §12.4 in particular fixes the lookup as a *fallback*, which is what makes the change
provably additive.

### 2.2 Why this does not make resolution order observable

The objection §3.3:2726-2728 raises against nesting — *"nesting makes resolution order
observable, and an order-dependent config is one whose behaviour depends on how the parser
happened to walk it"* — is what §1b closes, and closing it is what makes §1a safe.

With part-level compound references ruled an error (§3), a part's `item` names only a registry or
`command` id (§3.3:2680), neither of which depends on pane traversal order. The reference graph is
therefore:

```
pane item  ->  compound declaration  ->  registry / command / derived
```

one directed level, **acyclic by construction**, with no compound → compound edge anywhere. No
ordering mechanism is needed, and none is added. This mirrors how `ResolveDerived`
(`ItemValueResolver.cs:463-494`) keeps derived items order-free by computing every one against a
frozen snapshot (`:465`) and writing back only afterwards (`:490-493`) — same guarantee, obtained
structurally here instead of by snapshotting.

**If §1b were *not* an error, §1a would be unsafe** and this spec's answer would change. The two
rulings are a pair; do not implement one without the other, and if they land in separate commits,
**§1b must land first or with it** — never after. Shipping the item-level resolution alone opens
the compound → compound cycle for as long as the gap lasts.

*Revision 2 note:* §1b **has landed** (commit `56c23f7`), so this ordering constraint is satisfied
and §12's mechanism may be built on top of it. §12.5 is the direct consequence: because §1b is an
error, the compound map needs no ordering pass of any kind.

### 2.3 Suppression

If the referenced compound suppresses entirely — every value part empty, so `LeafItems.cs:41`
yields `Value is null` (§3.3:2723) — the selecting item is empty and collapses per §2.4, exactly
as any other empty item does. The compound's own one-unit suppression rule (`LeafItems.cs:136`,
`:140-142`) runs first and unchanged; the selector inherits its outcome rather than
reinterpreting it. **No diagnostic** — this is a runtime state, and §9.8 makes `--check` unable to
see it.

---

## 3. Hole #1b — part-level `ItemSelector` → compound: ERROR

### 3.1 Behaviour

A part whose `item` names a compound id is an **error**-severity diagnostic, new code
**`part-compound-source`**, at the part's JSON Pointer (`.../items/N/parts/M/item`):

```
'<id>' is a compound item; a part may not name a compound, because one compound
inside another is the nesting §3.3 forbids
```

### 3.2 Why an error, when §1a resolves the same form

Two independent grounds, both already in the spec:

1. **§3.3:2680 does not permit it.** A part's `item` is defined as *"a registry or `command`
   id"*. Compound is not in that list. This is the same condition as `part-source-count` /
   `part-forbidden-key` (§3.3:2734) — a part shaped in a way §3.3 does not define.
2. **§3.3:2726 forbids it in substance.** *"A part may not contain `parts`. One level, for
   §3.2.1's reason: nesting makes resolution order observable."* A part naming a compound id
   achieves that nesting by reference. Enforcing the rule against the literal spelling while
   permitting it by id would leave the rule guarding a spelling rather than the property it
   exists to protect.

Error severity follows §1.1 (`ItemSelector`'s unknown-id case is error) and matches the other
three part-shape diagnostics, all of which §3.3:2742-2743 sets at error: *"zero-or-many sources
and a forbidden key have no defined meaning, which is §9.4's line."*

### 3.3 Why a new code rather than reusing `from-compound-source`

Different reference form, different position, different remedy. §3.3:2738-2740 permits reuse when
it is *"the same condition in a new position"* — but this is not the same condition. The `from`
codes say *"there is no value to read"*; this one says *"a compound may not be nested."* The
existing message (`ConfigCheck.cs:126`, "has no single value for 'from' to read") would be simply
wrong here: a part's `item` renders an item, it does not read a value.

The name deliberately joins the existing `part-*` family (`part-source-count`,
`part-forbidden-key`), because that is the family an author greps for.

### 3.4 Self-reference is covered

`{"id":"badge","parts":[{"item":"badge"}]}` is caught by this rule with no special case: `badge`
is in `CompoundItemIds`, so the part-level branch fires. No `placeholder-self-reference`-style
(`ConfigCheck.cs:138-142`) dedicated code is needed, and none should be added — the general rule
already gives a correct message.

---

## 4. Hole #3 — `LinkPlaceholder` → compound: RESOLVE

### 4.1 Behaviour

`{other-id}` in a `link` template, where `other-id` is a compound, substitutes that compound's
**`Plain`** — the colour-code-free concatenation at `SegmentBuilder.cs:116`, exposed as
`ResolvedItem.Value` (`LeafItems.cs:41`).

**Markup is dropped, unconditionally.** A link template is a URL; SGR sequences inside one are
not a degraded result, they are a corrupt one. There is no case where the caller wants them, so
this is not a policy with an alternative — it is the only defined behaviour.

*Revision 2 correction:* the original text here said this "needs no new plumbing beyond reaching
the value." That understated it. "Reaching the value" **is** the plumbing, and it is the same
plumbing §1a needs — see §12. E2 has since been answered: the substitution site is
`LeafContent.TryBuildLink` (`LeafContent.cs:82`), which reads the resolution dictionary only. That
is E2's *"reads the dictionary only"* branch, so §12.6 specifies the one new parameter it takes.

### 4.2 Compounds and links already coexist

§3.3:2729 — *"A part may not carry `link`. `link` stays at item level and wraps the whole
compound."* So a compound is already a legal link *owner*. This spec makes it a legal link
*source* as well. The two are independent; nothing in §3.3 or the addendum's D-C/D-E link work
(`:59`, `:405`) is changed by this.

### 4.3 Failure cases, and why none of them takes a diagnostic

The dispatch asked for a code covering "the referenced compound can't be resolved for some
independent reason." **There is no such reachable case at `--check` time**, for three reasons:

1. **A compound with no surviving part suppresses as one unit** (§3.3:2723, `LeafItems.cs:41`) and
   yields `Value is null` — a *runtime* outcome. §9.8 makes `--check` width- and
   value-independent, so it cannot predict it.
2. **A compound whose parts are themselves invalid is already caught** by the existing part
   diagnostics — `part-source-count`, `part-forbidden-key`, `unknown-item-id`, `unknown-color`
   (§3.3:2734-2736) — plus `from-compound-source` on a part's `from` (`ConfigCheck.cs:123-127`,
   already covering part-level per addendum `:626`), plus §3's new `part-compound-source`. A
   compound that survives `--check` has no invalid part left to fail on.
3. **Transitive invalidity cannot arise**, because §3's ruling means a compound cannot reference a
   compound. The chain bottoms out in one step.

**So: no new diagnostic for hole #3.** An empty compound substitutes as the empty string, which is
whatever the existing link machinery already does for any other item that resolved empty — see E3,
which pins that behaviour rather than assuming it.

---

## 5. Hole #4 — `ColorFrom` → compound: NO RESOLUTION, warning

### 5.1 The ruling

A colour rule's `from` naming a compound id yields a **warning**-severity diagnostic, new code
**`color-from-compound-source`**:

```
'<id>' is a compound item; a compound has a colour per part and no single value
for a colour rule to read
```

The config **validates** — a warning does not fail `--check`. The rule then behaves exactly as it
does for an unresolvable `from` today: it falls through to its default colour. The item simply
does not receive a colour from that source, and the render is otherwise unaffected.

### 5.2 Why resolution is impossible here, not merely awkward

This is the one hole where "attempt real resolution" has no target, and the reason is stronger
than "a compound has several colours so picking one is arbitrary."

**A colour rule's `from` reads a *value*, to drive thresholds or a match** (§6). What it needs
from the target is a scalar the rule can compare against. A compound's `Plain` is a concatenation
of literals and values — `agent:ORCHESTRATOR` from §3.3's own example — and threshold-matching
that string is not a degraded answer, it is a meaningless one.

**Colour flows *into* a compound's parts, never out of it.** §3.3:2732-2733: *"Semantic colour
precedence is unchanged. A part naming a semantic item keeps its value-derived threshold colour
unless that part sets `color`."* Each part independently sources its own colour from its own
value. The compound is the *sink* of that machinery. There is no outward-facing colour on a
compound to read, at any point in the pipeline — not a first part's colour, not a dominant one,
not a computed one. `SPEC-V2-FRAMEWORK.md:5405`'s "this can never work" is as true of a colour
rule's `from` as of a derived item's.

**Therefore no tiebreak is defensible.** "First part's colour" would name a rule §3.3 declined to
have, and "only when the compound has exactly one part" would make a config's validity depend on
an unrelated array length — a config that breaks when an author adds a second part is a worse
failure than the one it replaces.

### 5.3 Why warning, and why not `from-compound-source`

Per §1.1: unknown `ColorFrom` is a **warning** today (`ConfigCheck.cs:117`,
`unknown-color-source`). Colour references degrade gracefully throughout this codebase — a bad
colour never breaks a statusline. A compound target must not be graver than a nonexistent target
in the same position.

`from-compound-source` is registered at error severity (`SPEC-V2-FRAMEWORK.md:5405`, addendum
`:534`) and §9.6 fixes that permanently, so it cannot be reused at warning severity. Hence a new
code. The name keeps the `from-*-source` family's shape while its `color-` prefix marks the
severity difference at the point an author reads it.

### 5.4 Resolved — this was put to the user, not applied silently

The instruction was that holes #1/#3/#4 should attempt real resolution rather than warn-and-no-op.
§5.2 argues hole #4 has no target to resolve against, which lands it on warn-and-no-op — the
position §12.8.5 originally leaned toward and that the instruction otherwise overrides.

**That divergence was raised as a recommendation and the user accepted it.** §5.1 is the design:
warning severity, no resolution, config validates, the item does not get that colour.

The one alternative this analysis found — *legal only when the compound has exactly one part with
a `from`, resolving to that part's source value* — was recorded as **not recommended** for the
reason in §5.2's last paragraph, and was not taken. It is left here so a future reader can see the
option was considered and why it lost, not as a live option.

### 5.5 This ruling constrains §12 — read before designing any shared store

Hole #4 is **already implemented and routed** (commit `56c23f7`). It works by *not finding a
value*: a colour rule whose `from` names a compound finds nothing in the resolution dictionary,
warns, and falls through to its default.

**That makes the resolution dictionary off-limits as the carrier for holes #1a and #3.** If a
compound's `Plain` were written into `values`, a colour rule's `from` would suddenly *find* it,
and §5.1's shipped behaviour would silently invert — the rule would threshold-match a
concatenated string, which §5.2 calls "not a degraded answer, a meaningless one." §12.2 records
this as the decisive rejection.

---

## 6. Verification

Ordered. Nothing here is gated; §5.4 and §12.9 are both closed.

**Hole #1b (`part-compound-source`)**
1. A part whose `item` names a compound id → exactly one diagnostic, code `part-compound-source`,
   error, path `/items/N/parts/M/item`.
2. Self-reference: a compound with a part naming its own id → same diagnostic, no extra
   diagnostics (§3.4).
3. A part whose `item` names a **registry** or **command** id → **no** diagnostic. Guards against
   the branch over-firing on the legal case.

**Hole #1a (item-level resolution)**
4. A pane item `{"item":"<compound-id>"}` renders the compound's full output, per-part colours
   intact. Assert on the rendered bytes, and assert the emitted `Spans` list is non-null and has
   one entry per surviving part — not merely that the plain text matches, which would pass even if
   colour were flattened.
5. Same, where the compound suppresses entirely → the selecting item is empty and collapses
   (§2.3), no diagnostic.
6. **Regression:** the addendum records this case as rendering nothing today (`:550`). Capture
   `main`'s current output for the config in test 4 first, and assert the new output differs in
   exactly the intended way.

**Hole #3 (link substitution)**
7. A `link` template `{compound-id}` substitutes the compound's `Plain`. Assert the emitted URL
   contains **no** SGR bytes (§4.1) — the assertion that can actually fail if markup leaks.
8. Same, where the compound suppresses → empty substitution, matching whatever E3 establishes for
   an empty non-compound item. **No** diagnostic (§4.3).

**Hole #4**
9. A colour rule `from` naming a compound → exactly one diagnostic,
   `color-from-compound-source`, **warning**, and the rule falls through to its default colour.
10. Assert severity is warning, explicitly and by name, **and** that `--check` reports `ok: true`
    for a config whose only finding is this one. §1.1's coherence is the whole argument; a test
    that only checks the code would pass at the wrong severity, and a test that only checks
    severity would miss the config being wrongly rejected.

**Whole-spec**
11. `tools/check-all.sh` passes.
12. `--check --json` reports the three new codes; §9.6.1's registry table in
    `SPEC-V2-FRAMEWORK.md` lists them.
13. **Hole #2 is unaffected:** a config exercising `placeholder-compound-source` produces exactly
    the diagnostic that work introduces, with none of this spec's codes firing alongside it.

**Revision 2 adds tests 14-19 in §12.8; Revision 3 adds test 20 in §12.9.3; Revision 4 adds test
21 in §12.7.1.** Those are the mechanism's own tests and are additional to, not a replacement for,
4-8 above.

---

## 7. What must NOT change

- **`from-compound-source`** — its code, message, error severity, and both its item-level and
  part-level `from` coverage (`ConfigCheck.cs:123-127`; addendum `:626`). Untouched.
- **Hole #2's `placeholder-compound-source`.** Out of scope; do not anticipate, duplicate, or
  refactor around it.
- **`ConfigCheck.cs:109`'s `known` test.** Compounds must keep self-declaring their ids. The fix
  is additional branches after it, matching the existing `from-compound-source` shape — not a
  change to what "known" means. Narrowing `known` would break every legitimate compound reference.
- **The severity of any existing code**, in particular `unknown-color-source` (warning) and
  `unknown-link-target` (warning) at `:116-117`.
- **`ItemValueResolver`'s blindness to compound ids.** Nothing here puts a compound into the
  resolution dictionary. §5405's invariant stands; holes #1a and #3 reach the *rendered* value,
  which is a different store (§0). **§12.2 makes this an implementation constraint, not just a
  description** — writing a compound into `values` would break hole #4's shipped behaviour.
- **`LeafItems`' one-unit suppression** (`:41`, `:136`, `:140-142`) and §3.3:2704-2722's
  literal-adjacency rule. Both run unchanged; this spec consumes their outcome.
- **`ResolveDerived`'s frozen-snapshot ordering guarantee** (`ItemValueResolver.cs:463-494`).
- **§3.3's one-level rule.** §3 enforces it; nothing here relaxes it.
- **`LeafItems.BuildCompound`'s purity.** It writes to no passed-in collection and runs no
  subprocess. §12.3 depends on that being true — calling it once per compound outside the render
  path is only safe because of it. Do not add a mutation or a side effect to it.
- **`ItemContext`'s environment-only contract** (`ItemContext.cs:11-34`). See §12.6.1.
- **§3.3:2732-2733's part-level colour authority.** §12.9 extends it to the selecting item; it
  does not weaken it. A part that sets its own `color` keeps it under every rule in this spec.
- **`SizeResolver`'s measurement determinism.** §12.10 rejects ambient state partly to protect it:
  a solver whose result depends on state not visible in its arguments cannot be reasoned about.

---

## 8. Files to touch

| File | Change |
|---|---|
| `src/ClaudeTuiLine/ItemValueResolver.cs` | distinguish part-level from item-level `ItemSelector` in the scan — see **E1**. `ReferenceScan.CompoundItemIds` already exists (`ConfigCheck.cs:123`) and needs no new plumbing |
| `src/ClaudeTuiLine/ConfigCheck.cs` | two new `else if` arms after `:127`, matching the existing shape: part-level `ItemSelector` + compound → `part-compound-source`; `ColorFrom` + compound → `color-from-compound-source` |
| `src/ClaudeTuiLine/LeafItems.cs` | item-level `ItemSelector` → compound emits the referenced compound's `Segment` (§2.1); `Resolve` takes the new compound-map parameter and `BuildCompound` goes `private` → `internal` (§12.3, §12.6); selecting-item `color` handling per §12.9 |
| `src/ClaudeTuiLine/LeafContent.cs` | `TryBuildLink` (`:82`) and its caller `Decide` (`:51`) take the new compound-map parameter; compound target substitutes `Plain` (§4.1, §12.6) |
| **the compound-map build site** | new — §12.3 fixes what it builds, §12.7 and §12.7.1 fix where. |
| `src/ClaudeTuiLine/PaneAssembler.cs` | the `PaneAssembler` chain — `RenderLeafRows` (`:19`), `RenderItemRows` (`:112`), the `LeafItems.Resolve` site (`:136`) and the `Decide` site (`:143`) — all take `compounds` (§12.10) |
| `src/ClaudeTuiLine/SizeResolver.cs` | the `SizeResolver` chain — `Resolve`, `ResolveNode` (`:170`), `ResolveVertical` (`:202`), `ResolveVerticalMinRows` (`:588`), `AllocateMinRowsOnePass` (`:653`), `SolveMinRows` (`:711`), `MinWidthForRowCount` (`:761`), `RowCountAt` (`:791`), `MeasureRequest` (`:982`), `MeasureInnerContentWidth` (`:989`), `CandidateSegments` (`:1000`), and the `LeafItems.Resolve` / `Decide` sites (`:1009`, `:1016`) — all take `compounds` (§12.10) |
| `src/ClaudeTuiLine/PaneTreeRenderer.cs` | `Render` (`:15`) takes `compounds` |
| `src/ClaudeTuiLine/HeightLadder.cs` | `Resolve` (`:30`) takes `compounds` |
| `src/ClaudeTuiLine/Program.cs` | `ComputeRows` (`:155`) and `RunAsync` — the map is built here and passed into both entry points (§12.7.1) |
| `src/ClaudeTuiLine/PaneCollapse.cs` | `:75` — `LeafItems.Resolve` call site updated |
| `SPEC-V2-FRAMEWORK.md` | §9.6.1 registry rows for the new codes; §3.3:2680 note that a part's `item` may not name a compound; §3.3:2732-2733 note that §12.9 extends part-colour authority over a selecting item's `color` |
| `SPEC-85-ADDENDUM-spans-threading.md` | §12.8.5 marked resolved by this file, holes #1/#3/#4; plus the `:556` amendment in §11 |
| `tests/ClaudeTuiLine.Tests/ConfigCheckTests.cs` | §6.1-3, §6.9-10; existing compound tests at `:81`, `:107`, `:131` are the pattern to follow |
| render/link tests | §6.4-8, §12.8's 14-19, §12.9.3's 20, §12.7.1's 21 |

---

## 9. NEEDS-EVIDENCE

- **E1 — can the scan tell part-level from item-level `ItemSelector`?** *Preferred:* add a
  `ReferenceForm` member (e.g. `PartItemSelector`) — `SPEC-V2-FRAMEWORK.md:5148` anticipates
  exactly this. *Fallback, only if not:* matching `/parts/` in `reference.Path`, which is the kind
  of rule that rots silently.
  — **ANSWERED and implemented in commit `56c23f7`.** Retained for the record.

- **E2 — where are link placeholders substituted?**
  — **ANSWERED: `LeafContent.TryBuildLink` (`LeafContent.cs:82`), one call site at
  `LeafContent.cs:51` in `Decide`. It reads the resolution dictionary only** —
  `IReadOnlyDictionary<string, string?> values`, no `ItemContext`, no tokens. That is the
  dictionary-only branch, so §12 is the larger change E2 warned about. §12.6 specifies it.

- **E3 — what does a link placeholder do today for a non-compound item that resolved empty?**
  §4.3 and test §6.8 require compound suppression to match it. Report the exact behaviour
  (empty substitution / placeholder left literal / link dropped) with file:line. Do **not** invent
  a behaviour for the compound case — mirror this one.
  — **STILL OPEN.** §12.4's fallback rule reduces its blast radius but does not answer it: the
  implementor must still read `TryBuildLink`'s existing miss path and mirror it exactly. This is a
  lookup, not a design question.

- **E4 — does anything today rely on item-level `ItemSelector` → compound rendering nothing?**
  — **ANSWERED: nothing does.** No test or fixture pins the old empty output, so §2.1 may change
  it freely. Test 6 still captures `main`'s baseline as designed.

- **E5 — is `ReferenceScan.CompoundItemIds` populated whole-tree or only within the current pane?**
  — **PARTIALLY ANSWERED, and §12.1 rules on it regardless.** `--check` validates
  `CompoundItemIds` whole-tree, which is why §12.1 fixes the *runtime* map as whole-tree too. The
  implementor must still confirm the construction site before building on it; if it turns out to
  be pane-scoped, that is a **defect to fix**, not a constraint to design around — see §12.1.

- **E6 — what is the caller chain above `LeafContent.Decide` (`:51`)?**
  — **ANSWERED, and the gate it tripped is now resolved by §12.10.** `Decide` has two callers,
  both atop long chains: the `PaneAssembler` chain (6 frames above `Decide`, up to `RunAsync`) and
  the `SizeResolver` chain (7-8 frames, up to `SizeResolver.Resolve`, including a parallel
  measurement sub-branch through `MeasureInnerContentWidth`). That depth tripped §12.6.3's
  "report back before implementing" gate, correctly. **§12.10 rules: proceed with the threaded
  parameter.** The decisive fact is not the depth but that every frame in both chains already
  threads `values` and `ctx` as its own parameters — `compounds` is the same mechanical addition,
  not a new shape of thread. §12.10 also records why the ambient alternative was rejected.

---

## 10. Confidence

**High** on holes #1b and #4 — both rest on §3.3 text quoted verbatim (`:2680`, `:2726-2728`,
`:2732-2733`, `:2742-2743`) plus the severity coherence rule in §1.1, which is derived from the
existing switch rather than chosen. #4's divergence from the original instruction was accepted by
the user (§5.4), so it is settled rather than merely argued. Both are landed.

**High** on hole #3's semantics. Its cost is now known rather than pending: E2 is answered, and
§12.6 is the change.

**High** on hole #1a's behaviour. The acyclicity argument (§2.2) is sound and §1b has landed, §12
supplies the mechanism its premise needed, §12.9's colour precedence was confirmed by the user, and
E4 came back clear — nothing pins the old empty output.

**High** on §12's threading as of Revision 4. E6's answer removed the uncertainty rather than
confirming it: the chains are long, but they already carry `values` and `ctx` at every frame, so
the change is mechanical repetition of an existing convention rather than a novel thread. The
residual cost is diff size, not risk, and §12.4's fallback ordering means even a missed site
degrades to today's behaviour rather than to a wrong render.

**Medium** on one thing only: **§12.7.1**, the requirement that both entry points receive the same
map. It is correct by construction given `BuildCompound`'s purity, but it is the item most likely
to be got subtly wrong, because a second map built independently would be *equal* and therefore
pass every test that compares output — while still being a second build the design forbids. Test 21
is written to catch the observable half of that.

**No Ultra-Advisor escalation recommended**, including for §12 and for E6's depth question. It
touches many render-path call sites, which is why the question was asked twice — but there is no
security, migration, or concurrency surface, every change is additive by §12.4's construction, and
the parameter threading is compile-time-checked by §12.6.4 rather than resting on runtime coverage.

---

## 11. Incidental finding — NOT part of this task

`SPEC-85-ADDENDUM-spans-threading.md:556` states the remaining three holes *"have no clean
precedent and their [treatment is open]"*. For `ColorFrom` that turns out to be too pessimistic:
§1.1's severity-follows-the-form rule is a clean precedent, it was simply not looked for. Worth a
one-line amendment to `:556` when §12.8.5 is marked resolved, so the next reader does not inherit
the belief that the question is open-ended when it is now answered.

---

## 12. The lookup mechanism for holes #1a and #3 — NEW IN REVISION 2

Holes #1a and #3 converge on one question the implementors correctly refused to invent:

> How does a resolution site — `LeafItems.Resolve` for #1a, `LeafContent.TryBuildLink` for #3 —
> reach a compound's rendered value from **anywhere in the tree**, not just its own pane's item
> list?

This section answers it. §12.1-12.5 are the design; §12.6-12.7 are the plumbing; §12.8 is
verification; §12.9 is the colour-precedence ruling; §12.10 is the E6 depth ruling.

### 12.1 Scope: whole-tree, not same-pane

**A resolution site can reach a compound declared anywhere in the config tree.** Not just its own
pane, not just an ancestor pane.

This is not a convenience choice. `--check` already validates `CompoundItemIds` **whole-tree** —
that is why a config naming a compound from another pane passes validation today and then renders
nothing (addendum `:550`), which is precisely the silent failure #87 exists to close. Two options
close it:

- **(a) Widen resolution to whole-tree**, matching `--check`. Chosen.
- **(b) Narrow `--check` to pane scope**, matching the current resolution. **Rejected.**

(b) is rejected because it makes a config's validity depend on *where an item happens to be
declared*, while ids are global everywhere else in this system — a compound id is unique across
the whole config, a registry id is global, a `command` id is global. Introducing one id namespace
that is pane-scoped, in the one place authors are least likely to expect it, trades a visible
failure for a confusing rule.

**If E5's construction site turns out to be pane-scoped**, that is a defect in the scan, not a
premise for the design: fix the scan to be whole-tree and note it in the implementation report.
Do not narrow §12 to match a pane-scoped scan.

### 12.2 Rejected: writing compound values into the resolution dictionary

The obvious mechanism — have `ItemValueResolver` write each compound's `Plain` into `values` under
its id, so every existing dictionary reader finds it for free — is **forbidden**.

It breaks hole #4, which is **already implemented and shipped** (commit `56c23f7`). §5.1's
behaviour is *"the rule finds no value, warns, falls through to its default."* Put a compound into
`values` and the rule finds one: it would threshold-match a concatenation like
`agent:ORCHESTRATOR`, which §5.2 establishes is meaningless rather than merely imprecise. A
warning would still be emitted, so `--check` would look unchanged while the *render* silently
inverted — the worst shape of regression this spec could produce.

More generally: **the reason §1 produced four different answers is that reachability differs per
form.** A single shared store collapses all four back into one answer, which is the very thing §0
identifies as the root cause. `SPEC-V2-FRAMEWORK.md:5405`'s invariant stays intact (§7).

### 12.3 The mechanism: one `id → Segment` map, materialized once

Build **one map from compound id to that compound's rendered `Segment`**, materialized exactly
once per render, after the resolution dictionary is complete.

```
IReadOnlyDictionary<string, Segment> compounds
```

- **Key:** the compound's declared `id`.
- **Value:** the `Segment` produced by `LeafItems.BuildCompound` (`LeafItems.cs:70`) for that
  declaration — the same object the compound would hand the renderer in place, per §2.1.
- **Entry omitted** when `BuildCompound` returns `null` (the compound suppressed as one unit,
  §2.3 / §3.3:2723). A suppressed compound is *absent from the map*, not present-with-empty. That
  is what makes §2.3 and §6.8's suppression behaviour fall out of the fallback rule in §12.4
  rather than needing a special case.

Each consumer takes what its ruling specified:

| hole | consumer | takes |
|---|---|---|
| #1a | `LeafItems.Resolve` | the whole `Segment`, `Spans` intact (§2.1, §12.9) |
| #3 | `LeafContent.TryBuildLink` | `Segment.Plain` only, markup dropped (§4.1) |

**Why one shared `Segment` map rather than a registry of declarations each site builds from:**
building from a declaration means every site duplicates the build, so #1a and #3 could disagree
about the same compound if either ever diverges; and `TryBuildLink` would need three new
parameters (`values`, `ctx`, `tokens`) to call `BuildCompound` at all, when `ctx` and `tokens` do
not otherwise thread into `LeafContent`. One map means one build, one truth, and **exactly one new
parameter at each site**.

Building this map is safe because `BuildCompound` is **pure** — it writes to no passed-in
collection and runs no subprocess. §7 pins that.

### 12.4 The map is a FALLBACK, consulted only on a miss

At both sites, the compound map is consulted **only when the ordinary path yields nothing**:

- **#1a:** `LeafItems.Resolve` tries its existing resolution for `{"item": id}` first. Only if
  that produces no item does it look `id` up in `compounds`.
- **#3:** `TryBuildLink` tries `values[id]` first. Only if that misses does it look `id` up in
  `compounds` and take `.Plain`.

This ordering is load-bearing, for two reasons:

1. **It makes the change provably additive.** Every config that renders today takes the ordinary
   path and never reaches the map, so its output is byte-identical. §12.8's test 19 asserts
   exactly that, and it is the cheapest strong guarantee available here.
2. **It makes suppression correct for free.** A suppressed compound has no map entry (§12.3), so
   a miss on the ordinary path followed by a miss on the map lands on **the existing empty-value
   path** — whatever E3 establishes that is. §2.3 and §6.8 are then satisfied by construction
   rather than by a second code path that could drift from the first.

**Do not reverse the order** as an optimization. Map-first would let a compound id shadow a
registry or `command` id of the same name, which is a behaviour change nobody asked for.

### 12.5 No ordering pass, no topological sort — add neither

The map's entries are **mutually independent**. §1b makes a part-level compound reference an
**error**, so a compound's parts name only registry, `command`, or derived ids — never another
compound (§2.2's acyclicity argument, now backed by landed code). Every compound therefore depends
on `values` alone, and on nothing in the map.

So: build the entries in any order, in one pass. **Do not add a topological sort, a dependency
graph, a two-phase resolve, or a visited-set.** Each would be dead machinery guarding a cycle §1b
already makes unrepresentable.

The degenerate case is also safe: if an *error*-severity config still renders (because the caller
ignored `--check`), a part naming a compound finds no entry in `values`, and degrades to empty —
deterministically, with no dependence on map build order.

### 12.6 Threading: one required parameter at each site

#### 12.6.1 Not on `ItemContext`

`ItemContext` (`ItemContext.cs:11-34`) carries **environment** facts only — `StatusInput`, git
branch, Engram result, remote URL — and is constructed at exactly two sites, `Program.cs:67` and
`Program.cs:343`, from machine and repo facts. It holds no config and no pane tree, and neither
construction site has one available.

Config-derived data in this codebase travels as **its own parameter**: `values` and `tokens`
already do. The compound map is config-derived. It follows the established convention — and per
E6, so does every frame in both chains that must now carry it (§12.10).

#### 12.6.2 `LeafItems.Resolve`

```csharp
public static IReadOnlyList<ResolvedItem> Resolve(
    IReadOnlyList<PaneItem> items,
    IReadOnlyDictionary<string, string?> values,
    ItemContext ctx,
    IReadOnlyDictionary<string, Segment> compounds,                          // new, required
    IReadOnlyDictionary<string, ColorResolution.ColorRule>? tokens = null)
```

The new parameter must sit **before `tokens`** — C# forbids a required parameter after an optional
one. Three call sites will fail to compile, and each must pass the map:

- `PaneAssembler.cs:136`
- `SizeResolver.cs:1009`
- `PaneCollapse.cs:75`

`LeafItems.BuildCompound` (`:70`) changes from `private` to **`internal`** if the map is built
outside `LeafItems`; leave it `private` if the build site is inside `LeafItems`. Per §12.7.1 the
build site is in `Program`, so **`internal` is the expected outcome.**

#### 12.6.3 `LeafContent.TryBuildLink` and `Decide`

`TryBuildLink` (`LeafContent.cs:82`) takes the same map as one new required parameter, and so does
its single caller `Decide` (`:51`).

*Revision 4:* this section originally gated on E6 — "if the chain above `Decide` is more than two
levels deep, report back." **E6 was answered, the gate fired, and §12.10 resolves it: proceed with
the thread.** The gate has served its purpose and is discharged; do not re-raise it.

#### 12.6.4 Required, never optional-with-a-default

**Do not give the new parameter a default value**, at any of these sites, however tempting it is
to keep call sites compiling.

This is the SPEC-88 §2.4 lesson applied here: a default converts a compile error into a silent
behavioural gap. A call site that was never updated would compile, run, and quietly resolve no
compounds — which is exactly today's bug, reintroduced at a site nobody is looking at. A required
parameter makes the compiler enumerate every site that must change. That enumeration is the
principal safety property of this design; do not trade it away.

**This applies with more force after E6, not less.** A 13-frame thread across two chains is exactly
the change where one missed frame is plausible, and the compiler enumerating them is what turns a
long mechanical diff into a safe one.

### 12.7 Where the map is built

**Preferred — two stages, no new tree walk:**

1. **Collect declarations** during the existing pane walk in `Config.ResolvePane`
   (`Config.cs:670`, recursing at `:681`). Commit `4ef9c7d` already flags "ScanReferences is a
   second walk"; a third walk would compound that.
2. **Materialize `Segment`s** after `values` is complete, calling `BuildCompound` once per
   collected declaration.

**Acceptable fallback:** a dedicated walk at stage 2 collecting declarations and materializing in
one pass. Correctness is identical — this is a cost decision, not a design one. Take the fallback
if stage-1 collection would force `ResolvePane` to carry state it otherwise has no use for; a
clean extra walk beats a muddied `ResolvePane`.

#### 12.7.1 One map, above both entry points — NEW IN REVISION 4

E6 exposed something the earlier revisions did not account for: the two `Decide` chains bottom out
at **two different entry points** — `Program.ComputeRows` (`:155`) for the render chain, and
`SizeResolver.Resolve` for the measurement chain.

**The map must be built once, above both, and the same instance passed into each.** Concretely:
build it in `RunAsync` after `values` is complete, and pass it into `ComputeRows` *and* into
`SizeResolver.Resolve`.

**Do not build it independently at each entry point.** Because `BuildCompound` is pure and
deterministic, two independently-built maps would be *equal* — so this mistake produces no test
failure, no visible defect, and no symptom. It is nonetheless wrong: it doubles the build cost on
every render, and it silently reintroduces the two-sources-of-truth condition §12.3 exists to
prevent, so that the next change to make `BuildCompound` non-deterministic (a timestamp, a cache, a
counter) would split measurement from render with no test to catch it.

**Why this matters beyond tidiness:** the measurement chain calls `CandidateSegments` (`:1000`) to
decide widths, and §1a *changes what a selecting item renders*. If measurement and render ever saw
different maps, the solver would size a row for one content and the renderer would draw another —
a class of bug this codebase has no assertion against.

21. **Verification.** Assert that a config selecting a cross-pane compound is sized to fit its
    rendered content — that the row is not truncated or over-allocated relative to what is drawn.
    This is the observable consequence of measurement and render agreeing, and it is the half of
    §12.7.1 a test can actually reach. The single-build requirement itself is a code-review item,
    not a testable one; call it out explicitly in the implementation report.

### 12.8 Verification for §12

Additional to §6's tests 1-13, not a replacement.

14. **Whole-tree reach (§12.1).** A compound declared in pane A, selected by an item in pane B →
    renders the compound's full output with `Spans` intact. This is the test that fails today and
    is the reason §12 exists.
15. **Whole-tree reach for links (§12.1).** Same shape: a `link` template in pane B naming a
    compound declared in pane A → substitutes its `Plain`, no SGR bytes.
16. **Hole #4 is not regressed (§12.2).** A colour rule whose `from` names a compound that is
    *also* selected by an item elsewhere → still exactly one `color-from-compound-source`
    warning, and the rule still falls through to its **default** colour. Assert the rendered
    colour explicitly, not just the diagnostic — the diagnostic would survive the regression
    §12.2 warns about; the colour would not.
17. **Ordinary path wins (§12.4).** A registry or `command` id that shares a name with nothing
    special resolves exactly as before; and if the codebase permits it, a config where an ordinary
    id and a compound id collide must resolve to the ordinary one.
18. **Suppressed compound (§12.3, §12.4).** A compound whose every value part is empty, selected
    by an item in another pane → the selecting item is empty and collapses (§2.3), and a link
    naming it substitutes empty (§6.8) — both matching E3's established behaviour, with **no**
    diagnostic.
19. **Byte-identical baseline (§12.4).** Capture `main`'s rendered output for a corpus of configs
    that use compounds *without* any cross-reference, and assert the new build's output is
    byte-identical. This is §12.4's additivity claim made falsifiable.

### 12.9 Colour precedence: part colours win — RESOLVED, Revision 3

#### 12.9.1 The ruling

When an item selects a compound **and also sets its own `color`**:

```json
{ "item": "badge", "color": "red" }
```

**the compound's part colours win.** The selecting item's `color` does **not** flatten, override,
or recolour any part that carries a colour of its own.

The selecting item's `color` applies **only** to parts that have no colour from any other source —
neither an explicit part-level `color` nor a value-derived threshold colour. It is a *floor*, not
a ceiling: it fills in where nothing else spoke, and is silently ignored where something did.

Rendered `Spans` structure is unchanged either way — §2.1's "do not flatten to `Plain`" governs
regardless of what `color` says.

#### 12.9.2 Why

§3.3:2732-2733 puts colour authority with the part throughout: *"A part naming a semantic item
keeps its value-derived threshold colour unless that part sets `color`."* The part is the only
thing in §3.3 that can override a part's colour. Letting a *selecting* item — which is further
away from the value than the part is — outrank the part would invert that, and would silently
discard the per-part colour §2.1 calls "the entire point of §3.3."

The floor semantics are what keep the selecting item's `color` from being pure dead config: an
author who writes it still gets an effect wherever the compound left a part uncoloured, which is
the only place a single outer colour could have meant anything coherent.

**This was flagged as a product call, not decided by the architect.** The recommendation above was
put to the user with its alternative (selecting `color` wins, flattening the compound to one
colour) and the user **confirmed part colours win**. It is final, not provisional; do not mark it
so in the implementation report.

#### 12.9.3 Verification

20. **Colour precedence (§12.9.1).** A compound with three parts — one with an explicit part-level
    `color`, one with a value-derived threshold colour, one with neither — selected by an item
    that sets `"color": "red"`. Assert: parts one and two keep their own colours **unchanged**,
    and part three renders red. A test that only checks the first part would pass under the
    rejected "selecting colour wins" rule if that rule happened to be applied last; a test that
    only checks the third would pass under a rule that ignores the selecting `color` entirely.
    All three assertions are needed to pin the floor semantics.

### 12.10 The E6 depth ruling: thread it — NEW IN REVISION 4

#### 12.10.1 The ruling

**Proceed with the threaded parameter exactly as §12.6 designs it**, through both chains, to their
full depth. Do not replace it with an ambient, static, `AsyncLocal`, or service-located lookup.

#### 12.10.2 Why the depth does not change the answer

§12.6.3's gate asked for a report if the chain ran deeper than two levels, on the theory that a
long thread means pushing a parameter through frames *"that have no other business with it."* E6's
answer shows that premise does not hold here.

**Every frame in both chains already threads `values` and `ctx`** — usually `pane` as well — as its
own parameters. `compounds` is config-derived data travelling beside config-derived data that is
already there. That is not a new shape of thread; it is one more element in a parameter list whose
existence already encodes §12.6.1's convention. The frames' "business" with `compounds` is exactly
the business they already have with `values`.

So the cost is **diff size**, not architectural strain. Thirteen-odd signatures gain a parameter,
mechanically, in a pattern each already demonstrates. That is a large diff and a small risk, and it
is the correct trade here.

Two further facts reduce the cost from what the raw depth suggests:

- The three `LeafItems.Resolve` sites (§12.6.2) sit **inside these same two chains**, so one thread
  serves #1a and #3 together. This is one piece of work, not two.
- §12.6.4's required parameter means the compiler enumerates every frame. A long mechanical thread
  is precisely the change where compile-time enumeration is worth most.

#### 12.10.3 Why ambient / service-located state is rejected

The alternative offered was an ambient lookup — static state, `AsyncLocal`, or a service locator —
so no frame needs the parameter. **Rejected, on four independent grounds:**

1. **It destroys the design's principal safety property.** §12.6.4 rests on the compiler
   enumerating every site that must change. Ambient state compiles everywhere, including at the
   sites nobody updated, and fails at runtime or — worse — silently returns an empty map. That is
   today's bug with a new hiding place.
2. **It puts hidden state inside a solver.** The `SizeResolver` chain is a search:
   `SolveMinRows` → `MinWidthForRowCount` → `RowCountAt` iterate to a fixpoint. A measurement
   function whose result depends on state not visible in its arguments cannot be reasoned about,
   cached, or unit-tested in isolation, and §2.8.1's no-height-fixpoint discipline exists precisely
   because this codebase has been burned by measurement that was not a pure function of its inputs.
   §7 now pins this.
3. **It contradicts the convention §12.6.1 rests on.** `values`, `ctx`, and `tokens` are all
   explicit parameters. An ambient `compounds` would be the codebase's only ambient config channel,
   sitting beside three explicit ones carrying the same class of data — the reader would
   reasonably conclude one of the two patterns is wrong.
4. **It makes §12.4's additivity claim unverifiable by inspection.** "The map is consulted only on
   a miss, therefore existing configs are byte-identical" is checkable by reading the call sites
   when the map arrives as a parameter. With ambient state, any frame could consult it, and the
   claim degrades from provable to merely tested.

#### 12.10.4 The bundling refactor — noted, deliberately not taken

The honest long-term answer to "every frame threads `values`, `ctx`, `tokens`, `pane`, and now
`compounds`" is to bundle them into one resolution-context record, so the next addition costs one
field rather than thirteen signatures.

**Do not do that in this change.** It touches every frame in both chains for reasons unrelated to
#87, it would make §12.4's byte-identical claim much harder to argue (the diff would no longer be
purely additive), and it would bury the actual behavioural change inside a large mechanical
refactor. Note it in the implementation report as follow-up work with a clear rationale — the
parameter count is now high enough to justify it — and leave it there.
