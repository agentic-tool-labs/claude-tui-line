# SPEC-44 — Colour tokens (`@name`) inside `MatchRule` / `ThresholdRule` branches

Status: **implemented and merged** (8044f82). Ultra-Advisor confirmed §3 (see §9). Amended after that review
to close two findings: the sigil-site citation (§4.1, §7) and error severity (§5.1).
Amends: `SPEC-V2-FRAMEWORK.md` §9.5.1 — specifically the "Aside, not a ruling" at
`SPEC-V2-FRAMEWORK.md:5173–5175`, which left this open. This file supersedes that aside.
Owner: architect (#44). Does **not** touch any file owned by the parallel #80/#3.1 track.

---

## 1. The question, and why the status quo is not an answer

`SPEC-V2-FRAMEWORK.md:5173–5175` records:

> Aside, not a ruling: a consequence of the above is that a colour token cannot be used *inside* a rule
> branch — only in the `ColorExpr` position. Whether that is a deliberate product decision or an
> accident is unresolved.

It is an accident, and the current behaviour is worse than "not allowed". Today:

- `Config.cs:877–878` builds `ThresholdRule(t.Min, t.Color!)` and `MatchRule(m.Contains, m.EqualsValue, m.Color!)`
  from raw config strings, filtering only on non-empty. A value of `"@danger"` passes straight through.
- `ColorResolution.cs:16` / `:18` declare `Color` as a plain `string`. Nothing inspects it for a sigil.
- At render time `ResolveRuleColor` (`ColorResolution.cs:85–96`) returns that string as a **colour spec**;
  `ResolveBorderColor` (`:74–77`) does `Style.TryParse("@danger")`, which fails, and falls back to
  `new Style(Color.Grey)`.

The precise defect is that **`"@danger"` is handed to the Spectre colour parser as if it were a literal
spec.** That is a wrong-output bug, not merely a missing feature, and it is invisible to `--check` because
no `TokenRef` node exists for the reference pass to find. Both are fixed by the type change in §3
regardless of what severity §5.1 assigns.

**Ruling: `@name` is legal in a rule-branch colour position, in the restricted form defined in §3.**

---

## 2. The fact that shapes the design

The theme table does **not** map a name to a colour. `ColorResolution.cs:55–63`:

```csharp
public static string? Resolve(ColorExpr? expr, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, ColorRule> tokens) =>
    expr switch
    {
        null => null,
        ColorExpr.Literal lit => lit.Spec,
        ColorExpr.TokenRef tok => tokens.TryGetValue(tok.Name, out var rule) ? ResolveRuleColor(rule, values) : null,
        ColorExpr.Inline inline => ResolveRuleColor(inline.Rule, values),
        _ => null,
    };
```

`tokens` is `IReadOnlyDictionary<string, ColorRule>`. **A colour token names a whole rule**, which is then
evaluated against the item values. A token is therefore already capable of being value-dependent —
thresholds, matches, a `from` pointing at another item.

This is what makes the naive reading of #44 dangerous. "Allow `@name` in a branch" read literally means
"allow a branch to evaluate another rule", which introduces arbitrary-depth nested evaluation, reference
cycles (`a`'s branch → `@b`, `b`'s branch → `@a`), and a second dynamic dispatch inside a branch that has
already fired.

Note also: today a *constant* rule already behaves as a named colour. `ResolveRuleColor` with an empty
`From` returns `rule.Default` immediately (`ColorResolution.cs:87–89`). So `@accent` where
`accent = {default: "red"}` is already the idiom for "a named colour" in `ColorExpr` position. §3 makes
that same idiom work one level down.

---

## 3. The ruling

### 3.1 A new model type: `ColorValue`

Introduce a two-case type for *colour-valued leaves*. It is deliberately **not** `ColorExpr` and
deliberately **has no `Inline` case**, so a rule inside a rule branch is unrepresentable rather than
merely forbidden.

In `src/ClaudeTuiLine/ColorResolution.cs`, alongside `ColorExpr` (`:39–46`):

```csharp
public abstract record ColorValue
{
    public sealed record Literal(string Spec) : ColorValue;
    public sealed record TokenRef(string Name) : ColorValue;
}
```

Retype the three colour-valued leaves:

| Member | Today | After |
|---|---|---|
| `ColorResolution.ThresholdRule.Color` (`ColorResolution.cs:16`) | `string` | `ColorValue` |
| `ColorResolution.MatchRule.Color` (`ColorResolution.cs:18`) | `string` | `ColorValue` |
| `ColorResolution.ColorRule.Default` (`ColorResolution.cs:32`) | `string?` | `ColorValue?` |

`ColorRule.Default` is included on purpose. A grammar where `@accent` works on a match branch but not on
the same rule's `default` is an arbitrary hole; it is the first thing a user would hit and file. This is a
scope decision I am making, not one the dispatch asked for — flagged here and in §9.

`ColorExpr` (`ColorResolution.cs:39–46`) is **unchanged**: it keeps `Literal`, `TokenRef`, and `Inline`.
`ColorValue` is a sibling, not a base or a subtype of it. Do not attempt to unify them (see §6 item 1).

### 3.2 Only *constant* tokens may be referenced from `ColorValue` position

A `ColorValue.TokenRef(name)` is legal iff `tokens[name]` is a **constant rule**, defined as a `ColorRule`
where all four hold:

1. `From` is null or empty,
2. `Thresholds` is null or empty,
3. `Match` is null or empty,
4. `Default` is a `ColorValue.Literal`.

Condition 4 is the anti-cycle rule and it is load-bearing: a token reachable from a branch resolves in
**exactly one hop** to a literal spec. Token-to-token chaining is not permitted anywhere in `ColorValue`
position. There is therefore no cycle to detect, no depth limit to pick, and no traversal state to carry.
Do not implement cycle detection; if you find yourself needing it, condition 4 has been violated.

### 3.3 The asymmetry, stated deliberately

`ColorExpr.TokenRef` — the existing, top-position form — keeps its **full, unrestricted** semantics. A pane
border may reference a token that is a live threshold rule, and that continues to work exactly as it does
today. `ColorResolution.cs:60` is not to be changed.

`ColorValue.TokenRef` — the new, leaf-position form — is restricted to constant tokens per §3.2.

Ultra-Advisor confirmed this asymmetry is coherent rather than arbitrary, and supplied the sharper reason,
which is now the official justification: **`ResolveRuleColor` requires a driving value (`rule.From` →
`values`) to evaluate a non-constant rule, and that value context exists at `ColorExpr` position but not
inside an already-matched branch.** The restriction therefore tracks a real semantic boundary — a
non-constant token in branch position would have nothing to evaluate against — rather than being a grammar
wart. An implementor or reviewer reading §3.2 and §3.3 in isolation may read them as contradictory; they
are not, and this paragraph is the reconciliation. Preserve it.

### 3.4 What tells the extractor a branch value is a colour token

Nothing at extraction time, and that is the point. Per the dispatch's standing rule — *which namespace a
string belongs to is determined solely by the field it was read from, never by the string's own shape* —
the discrimination happens **once, at parse time**, and is recorded in the node type:

- The `@` sigil is **surface syntax**, consumed by the parser and never seen again.
- After parsing, the fact "this is a colour token" is carried by the node being a `ColorValue.TokenRef`,
  whose `Name` field is by construction in the theme-table namespace.
- No consumer downstream of the parser inspects a string's shape to decide what namespace it is in.

Any extractor, classifier, or validator that sniffs a string for a leading `@` (or for "looks like a colour
name") outside the two parse sites of §4.1 is a design defect and must be rejected in review.

---

## 4. Implementation

### 4.1 One `@`-stripping idiom, two parse sites

**Corrected after Ultra-Advisor finding 1. Earlier drafts of this spec cited `Config.cs:537` as the sole
`@`-parse site. That citation was wrong** — it was inherited from `SPEC-V2-FRAMEWORK.md:5161`, and
`Config.cs:537` is inside the doc comment on `LoadAll`. See §7 item 3; the framework spec's claim is itself
wrong and must be corrected, not merely re-pointed.

The **real** site is `Config.cs:862–863`, inside `ParseColorExpr` (declared `Config.cs:859`):

```csharp
private static ColorResolution.ColorExpr? ParseColorExpr(ColorExprJsonConfig? cfg, string? owningItemId) => cfg switch
{
    null => null,
    { Literal: { Length: > 0 } lit } => lit[0] == '@'
        ? new ColorResolution.ColorExpr.TokenRef(lit[1..])
        : new ColorResolution.ColorExpr.Literal(lit),
    { Rule: { } rule } => new ColorResolution.ColorExpr.Inline(ParseColorRule(rule, rule.From ?? owningItemId)),
    _ => null,
};
```

Note the sigil test is `lit[0] == '@'`, **not** `StartsWith`. A grep for `StartsWith('@')` finds nothing,
which is how the stale citation survived this long. Match the existing idiom exactly.

The invariant, restated in the form that survives this change and is mandatory:

> **Exactly two functions in the codebase inspect a string for a leading `@`** — `ParseColorExpr`
> (`Config.cs:859`) for the expression position, and `ParseColorValue` (new, below) for the leaf position.
> Both live in `Config.cs`. They are the sole producers of `TokenRef` nodes of either kind. No third site
> may be added; no site outside these two may test for the sigil.

Add to `src/ClaudeTuiLine/Config.cs`, immediately adjacent to `ParseColorExpr` so the pair is visible as a
pair:

```csharp
// The leaf-position counterpart to ParseColorExpr's sigil test above. These two are the only
// @-inspecting sites in the codebase, per SPEC-44 §4.1.
private static ColorResolution.ColorValue ParseColorValue(string raw) =>
    raw[0] == '@'
        ? new ColorResolution.ColorValue.TokenRef(raw[1..])
        : new ColorResolution.ColorValue.Literal(raw);
```

`ParseColorValue` assumes a non-empty `raw`, exactly as `ParseColorExpr`'s `{ Length: > 0 }` pattern
guarantees for its own input. Every call site below is already guarded by a non-empty check; keep it that
way rather than adding a redundant length test inside the helper.

Call it at the three construction sites, replacing the raw string pass-through at `Config.cs:877–878`:

```csharp
cfg.Thresholds?.Where(t => !string.IsNullOrEmpty(t.Color))
   .Select(t => new ColorResolution.ThresholdRule(t.Min, ParseColorValue(t.Color!))).ToList(),
cfg.Match?.Where(m => !string.IsNullOrEmpty(m.Color))
   .Select(m => new ColorResolution.MatchRule(m.Contains, m.EqualsValue, ParseColorValue(m.Color!))).ToList(),
```

and for `Default`, wherever `ColorRuleJsonConfig.Default` (`Config.cs:339–340`) is read into
`ColorResolution.ColorRule` — `ParseColorValue(cfg.Default)` when non-empty, `null` otherwise.

`@` is hereby **reserved** as the first character of any colour-valued config string. No escape hatch
(`@@` or otherwise) is specified, deliberately: no Spectre.Console colour spec begins with `@` (named
colours, `#rrggbb`, `rgb(r,g,b)`), so there is nothing to escape. If a future colour form needs a literal
leading `@`, that is a new decision — do not invent an escape now.

### 4.2 Resolution

`ColorValue` is mapped to a spec string by one helper, in `ColorResolution.cs`:

```csharp
private static string? ResolveColorValue(ColorValue? value, IReadOnlyDictionary<string, ColorRule> tokens) =>
    value switch
    {
        null => null,
        ColorValue.Literal lit => lit.Spec,
        // Per SPEC-44 §3.2 a branch token is constant, so this is one hop with no recursion.
        ColorValue.TokenRef tok =>
            tokens.TryGetValue(tok.Name, out var rule) && rule.Default is ColorValue.Literal lit2
                ? lit2.Spec
                : null,
        _ => null,
    };
```

Returning `null` for an unknown or non-constant token is the documented silent-degrade behaviour of
`ColorResolution.cs:52–53` ("an unknown `@name` resolves to no colour, silently"), extended unchanged to
the leaf position. §5.1 explains why that is correct rather than a swallowed error.

Thread `tokens` into `ResolveRuleColor` (`ColorResolution.cs:85–96`), which is called from `Resolve`
(`:60`, `:61`) — both call sites already hold `tokens`, so this is a parameter addition, not a plumbing
exercise.

`ResolveNumeric` (`:117–131`) and `ResolveString` (`:140–159`) change return type from `string?` to
`ColorValue?` — they now return `t.Color` / `m.Color` / `rule.Default` unchanged, which are already
`ColorValue`. `ResolveRuleColor` applies `ResolveColorValue` to their result. Keep the mapping in
`ResolveRuleColor` only; do not push `tokens` down into `ResolveNumeric`/`ResolveString`, which stay
value-table-free and therefore stay trivially testable.

`ResolveNumeric` and `ResolveString` are `public` and are called directly by
`tests/ClaudeTuiLine.Tests/ItemFormatParityTests.cs:207`, `:225`, `:233`. Their signature change is a
deliberate test-visible break; update those call sites (§10 item 5). Do not add string-returning overloads
to avoid touching the tests — two spellings of the same resolution is exactly the duplication §6 item 2
forbids.

`ResolveStandardThreshold` (`ColorResolution.cs:109`) becomes:

```csharp
public static string ResolveStandardThreshold(double value) =>
    ResolveNumeric(value, StandardThreshold) is ColorValue.Literal lit ? lit.Spec : "green";
```

`StandardThreshold` (`ColorResolution.cs:104`) is a hardcoded rule whose branches are the literals
`"maroon"` / `"olive"`, so the `Literal` case always hits when a threshold matched. The `"green"` arm now
covers two situations that were distinct in the original `?? "green"` — "no threshold matched" (the
pre-existing meaning, preserved) and "the result was not a literal" (unreachable today). That widening is
safe **only** because `StandardThreshold` is constructed in source rather than parsed, so a `TokenRef` can
never appear in it. If a later change ever makes `StandardThreshold` configurable, this line silently
starts coercing tokens to `"green"` and must be revisited — recorded in §7 item 9.

### 4.3 Validation and the reference-extraction pass

`ColorValue.TokenRef.Name` is a **reference** and must be discoverable by the §9.5.1 machinery like every
other reference in the model. This is the reason §3.1 keeps the token in the model rather than substituting
it away at parse time: §9.5.1 exists to guarantee every reference is enumerable by reflecting over the
model, and a parse-time substitution would create the first reference in the system invisible to it.

- Extend `ColorTokenExtractors` (`ItemValueResolver.cs:244–251`) so `ColorValue.TokenRef.Name` yields a
  `ColorTokenReference` (`ItemValueResolver.cs:544`) candidate, exactly as `ColorExpr.TokenRef.Name` does
  today at `:247–251`.
- `ConfigCheck.cs:142` already iterates `scan.ColorTokenReferences` to emit diagnostics, so branch tokens
  flow into `--check` through the existing loop with no new reporting path.
- The §3.2 constant-token check is a **separate** validation from existence, and runs only for candidates
  originating in `ColorValue` position. Message must name both the token and why:
  `"colour token '@danger' is used in a rule branch at <pointer>, so it must be a constant colour (a 'default' literal with no 'from', 'thresholds', or 'match')"`.

`Walk` (`ItemValueResolver.cs:108–128`) must now reach the branch colours. Note this is a **new walk site
class**: today `Walk` visits `ColorExpr` nodes, and the branches hang off `ColorExpr.Inline.Rule` and off
`colors`-table rules. Both must be traversed, or tokens in branches will never be seen by `--check`.

This is the one place where the standing limit recorded at `SPEC-V2-FRAMEWORK.md:5210–5217` bites —
*fail-closed over members, fail-open over sites*. Reflection proves every reference-carrying **member** is
classified; it cannot prove `Walk` **visits** every place those members live. A missed traversal site here
would leave the coverage test green while branch tokens silently never validate. The fixture assertion of
§10 item 7 is the only guard against that, and it is not optional.

---

## 5. Behaviour, including the edge cases

| Config | Result |
|---|---|
| `color: "red"` on a branch | Unchanged. `ColorValue.Literal("red")`, resolves to `"red"`. |
| `color: "@accent"`, `accent = {default: "red"}` | Legal. Resolves to `"red"`. **This is the feature.** |
| `color: "@accent"`, `accent = {from: "x", thresholds: [...]}` | No colour at runtime; `--check` **warning** (§5.1) — a non-constant token in branch position. |
| `color: "@accent"`, `accent = {default: "@other"}` | No colour at runtime; `--check` **warning** — chaining, §3.2 cond. 4. |
| `color: "@nope"`, no such token | No colour at runtime; `--check` **warning** — unresolved reference, via the existing pass. |
| `default: "@accent"` on a rule | Legal, same constant-token restriction. |
| `border.color: "@accent"`, `accent` a live threshold rule | Unchanged and still legal — `ColorExpr` position, §3.3. |
| `color: ""` on a branch | Unchanged: filtered out at `Config.cs:877–878` before construction. |
| A colour spec legitimately starting with `@` | Does not exist; `@` is reserved (§4.1). |

In every failing case the item still renders — uncoloured — and the defect is reported by `--check`. What
must **not** survive is the §1 behaviour of passing `"@danger"` to `Style.TryParse` and painting Grey.

### 5.1 Severity: warning, not error — and why the earlier draft was wrong

**Corrected after Ultra-Advisor finding 2.** An earlier draft of this spec required branch-token failures
to "fail at config load, loudly". That contradicted an established, deliberate convention and is withdrawn.

`ItemValueResolver.cs:448–454` states the governing policy in the codebase's own words:

> severity groups by what a dangling reference costs the config, not by syntax

and applies it: `ItemSelector` / `DerivedFrom` delete the item outright and therefore **error**, while a
dangling reference that costs only a colour is a **warning**. `ArgvPlaceholder` errors despite sharing
`LinkPlaceholder`'s syntax — the same walk, a different severity — which is the clearest possible statement
that severity here is a cost question, not a syntax question. `ColorResolution.cs:52–53` corroborates at
runtime: an unknown `@name` resolves to no colour, silently.

Applying that policy to both branch-token failure modes:

- **Unknown token** (`@nope`). Costs a colour. Exactly the same cost as an unknown token in `ColorExpr`
  position, which warns today. → **Warning.** Uniform by construction; no asymmetry is created, and this is
  the case Ultra-Advisor asked about.
- **Non-constant or chained token** (§3.2 violation). The reference resolves; the grammar forbids its use
  in this position. Tempting to call this a hard error because it is a *grammar* violation — but the stated
  policy is explicitly "not by syntax", and the cost is identical: one item renders without a colour. →
  **Warning**, for consistency with the policy rather than with my instinct.

So there is **no severity asymmetry between border tokens and branch tokens**, and none needs to be
declared. Both warn. This is a better outcome than the alternative Ultra-Advisor offered (state the
asymmetry deliberately), because the asymmetry turns out not to be necessary once severity is derived from
cost rather than from which position the token sat in.

The distinct diagnostic *codes* still matter — an unknown token and a non-constant token need different
messages, per §4.3 — but they share a severity.

One item for the Implementor to confirm, not a blocker: `ConfigCheck.cs:142`'s loop over
`ColorTokenReferences` must be checked to emit `DiagnosticSeverity.Warning` (`ConfigCheck.cs`, per the
`Diagnostic` / `DiagnosticSeverity` declarations) for the existing unknown-token case. The prose at
`ItemValueResolver.cs:448–454` is the authority and is unambiguous, but the literal emitted value is what
branch tokens must match. If it emits `Error` today, then the policy prose and the code disagree — report
that upward rather than resolving it inside #44, because it changes existing behaviour beyond this spec.

---

## 6. What must NOT change

1. **`ColorExpr` keeps all three cases and its unrestricted token semantics.** `ColorValue` is a sibling
   type, not a replacement, not a base, not a subtype. Do not "simplify" by making `ColorValue` a base of
   `ColorExpr` or by adding an `Inline` case to `ColorValue` — the absence of `Inline` is the entire
   structural guarantee of §3.2, and a shared base would reintroduce `Inline` into branch position through
   the back door. `ColorResolution.cs:60` is untouched.
2. **`ResolveNumeric` / `ResolveString` stay free of the token table.** They select a branch; they do not
   resolve one. Only `ResolveRuleColor` resolves.
3. **`Walk`'s existing behaviour at `ItemValueResolver.cs:110`, `:120`, `:126`.** §4.3 *adds* traversal; it
   does not license editing or re-pointing what is already there. The tests are fitted to the scanner.
4. **The two-table split.** `ReferenceExtractors` and `ColorTokenExtractors` stay separate
   (`ItemValueResolver.cs:176`, `:244`). `ColorValue.TokenRef.Name` goes in the **colour-token** table.
   `IdCandidate` must never come to mean "an id or a colour-token name depending on `Kind`".
5. **The silent-degrade runtime convention** (`ColorResolution.cs:52–53`). Colour problems degrade to no
   colour at render time and are reported by `--check`, never thrown at render. §5.1 depends on this.
6. **Line citations.** Every `ColorResolution.cs:NNN`, `Config.cs:NNN`, and `ItemValueResolver.cs:NNN`
   citation in this file is invalidated by construction if that code is restructured. This task itself
   restructures `ColorResolution.cs`, so re-pointing the citations in §7's amended
   `SPEC-V2-FRAMEWORK.md` rows is part of this task's definition of done.

---

## 7. Amendments required to `SPEC-V2-FRAMEWORK.md` §9.5.1

These are edits to `SPEC-V2-FRAMEWORK.md` only. **No other spec file is touched by #44.**

1. **Delete the aside at `:5173–5175`** and replace it with a pointer to this file.
2. **`:5168–5171`** — the "single fact" paragraph currently reads that `@name` is resolved into a
   `TokenRef` at parse time so any string still holding an `@` at runtime is a literal. Restate it in the
   generalized form of §4.1: *all* colour-valued strings pass through one of exactly two sigil-inspecting
   parse sites, producing `ColorExpr.TokenRef` or `ColorValue.TokenRef`; a string still holding an `@` at
   runtime remains a literal. The sentence *"it is the fact that would change if anyone ever taught a rule
   branch to accept a token"* must be updated — that is precisely what SPEC-44 does, and the fact survives
   in generalized form rather than being lost.
3. **`:5161` and `:5162` cite a line that does not do what they claim.** Both name `Config.cs:537` /
   `Config.cs:537–538` as the `@`-prefix site; `Config.cs:537` is inside the doc comment on `LoadAll`
   (`Config.cs:531–538`). The real site is `Config.cs:862–863` in `ParseColorExpr` (`Config.cs:859`).
   Correct both, and restate `:5161`'s "the **only** `@`-prefix site" as the two sites of §4.1.
4. **Systemic citation drift — check the whole cluster, not just the two above.** `:5132` cites
   `Config.cs:540` for `new Inline(ParseColorRule(rule, rule.From ?? owningItemId))`; that statement is
   actually at `Config.cs:865`. `:5135` cites `Config.cs:512` for the border's `owningItemId: null` and is
   suspect for the same reason. The §9.5.1 `Config.cs` citations appear to predate an insertion of roughly
   325 lines. **Re-point every `Config.cs:NNN` citation in §9.5.1**, not only the ones named here; treat a
   citation that still resolves as coincidence until verified.
5. **Exempt table rows `:5161`, `:5163`, `:5164`** — `ColorRule.Default`, `MatchRule.Color`,
   `ThresholdRule.Color` move from the **exempt** table to **recursed → `ColorValue`**.
6. **Covered table (`:5123–5130`)** — add `ColorValue.TokenRef.Name` → colour-token extractor →
   `ColorTokenReference`. Covered becomes 7.
7. **New exempt row** — `ColorValue.Literal.Spec`, `NeverAReference`, reason: *a literal colour spec;
   anything `@`-prefixed became a `ColorValue.TokenRef` at parse time (§4.1) and is therefore covered.*
8. **Root set (`:5248`)** — add `ColorValue` to
   `{Pane, PaneBorder, PaneItem, ColorExpr, ColorRule, ThresholdRule, MatchRule}`. Extend Verification
   item 4 to *"adding a `string` member to the `ColorExpr` **or `ColorValue`** abstract base fails the
   test"*; `ColorValue` is a second abstract root with nested concrete subtypes, same shape as `ColorExpr`,
   so the `:5074–5078` wording holds as written — confirm the implementation walks its nested subtypes.
9. **New note against `ColorResolution.cs:104`'s `StandardThreshold`** — record that
   `ResolveStandardThreshold` assumes its branches are literals (§4.2), and that making
   `StandardThreshold` configurable requires revisiting that line, which would otherwise coerce a
   `TokenRef` silently to `"green"`.

### `NeverAReference` — explicit answer to the dispatch's question 3

**No. This ruling does not change `NeverAReference`'s status, and I agree with the dispatch's finding 2.**

It remains a test-only taxonomy in `ReferenceExtractorCoverageTests.cs:26`, with zero production
consumers. Nothing here promotes it to a runtime model property, and the bigger-change fork the dispatch
warned about is **not** triggered.

What does change is the *contents* of the exemption table: three rows leave it (item 5 above) and one new
row joins it (item 7). That is a table edit of exactly the kind the table is designed to absorb, not a
change to what the enum means or where it lives. Rows moving is expected; the enum acquiring a production
consumer would be a different and much larger change, and is not proposed.

---

## 8. The rejected alternative, retained for the record

An earlier draft offered a fallback: keep `@` illegal in branches and reject it loudly, at roughly a tenth
of the implementation cost. **Ultra-Advisor ruled against it and it is not to be implemented.** Its case
rested entirely on the §3.3 asymmetry being incoherent; once that was confirmed coherent (§3.3), all that
remained was implementation cost against a config-author want — "name your palette once, use it
everywhere" — that it leaves broken one level down.

One fact from that analysis is worth keeping, because it constrains any future retreat: **§3's config
surface is a strict superset of the rejected option's.** Shipping the restrictive form first and widening
later would have been non-breaking; shipping §3 and retreating later is breaking, since configs using `@`
in branches would stop loading. There is no cheap way back once this ships.

---

## 9. Decisions, and the Ultra-Advisor ruling

**Ultra-Advisor verdict: §3 (legal, restricted to constant-rule tokens).** Confidence: high on the
coherence question, high-moderate on §3 overall. The §3.3 asymmetry was confirmed to track a real semantic
boundary (§3.3 carries the reasoning). **Overturn condition, recorded verbatim from the ruling:** if
NEEDS-EVIDENCE 1 (does `Walk` descend into `ColorRule` branches at all) comes back bad, §4.3 is materially
bigger than costed — *that is a schedule call for the Orchestrator, not a design reversal.*

**Decisions I made (overturn freely with reason):**
- Constant-token restriction (§3.2) rather than fully general nested rules.
- `ColorRule.Default` included in scope alongside the two branch types (§3.1) — the dispatch asked only
  about `MatchRule`/`ThresholdRule`; excluding `Default` leaves an arbitrary hole.
- One-hop, no chaining (§3.2 cond. 4) in preference to chaining plus cycle detection.
- Token kept in the model (§4.3) rather than substituted away at parse, so §9.5.1 stays the single source
  of reference discovery.
- Warning rather than error for both failure modes (§5.1) — derived from the codebase's stated policy,
  against my own initial instinct.
- No `@` escape hatch (§4.1).

**Still not mine, and now the only open product question:** whether `@`-in-branches should eventually be
fully general (nested rules), which config authors may want for shared threshold ladders. Ruled out here as
unrequested scope with real hazards. If the product wants it, this spec is the wrong starting point and it
should be re-specified, not patched.

**No implication for `SizeResolver.cs`.** This ruling is confined to the colour model
(`ColorResolution.cs`, `Config.cs` colour parsing, `ItemValueResolver.cs` extractor tables, `ConfigCheck.cs`
diagnostics) and touches no sizing, splitting, or layout code. #78's `ResolveVerticalEven` work does not
intersect this spec.

---

## 10. Verification

1. `colors: {accent: {default: "red"}}` + a threshold branch `color: "@accent"` renders **red**. This is
   the acceptance test for the feature and fails on today's code (renders Grey per §1).
2. A branch `color: "@accent"` where `accent` has a `from`/`thresholds`/`match` renders **uncoloured** and
   produces a `--check` **warning** carrying the §4.3 message and a JSON Pointer.
3. A branch `color: "@accent"` where `accent = {default: "@other"}` behaves likewise (chaining, §3.2
   cond. 4).
4. A branch `color: "@nope"` with no such token produces a `--check` **warning** through the **existing**
   `ConfigCheck.cs:142` loop — assert the diagnostic carries a JSON Pointer and that its severity equals
   the severity that same build emits for an unknown token on a pane border. Asserting the two are equal,
   rather than asserting a hardcoded `Warning`, is what pins §5.1's no-asymmetry claim even if the shared
   severity is later changed.
5. A branch `color: "red"` still renders red; `ItemFormatParityTests.cs:225`'s
   `RateLimitsThresholdRule_UsesMaxAcrossWindows_OverLimitDoesNotFallToDefault` still passes after its
   `ThresholdRule(80, "red")` construction is updated to the `ColorValue` form. Same for `:207` and `:233`.
6. A pane border `color: "@accent"` where `accent` is a **live threshold rule** still resolves dynamically
   — the §3.3 asymmetry, and the regression this ruling is most likely to cause.
   `ColorExprWalkFixtureTests.cs:60`'s assertion that the child border is a
   `ColorResolution.ColorExpr.TokenRef` must still pass unmodified. `HyperlinkTests.cs:420` resolves a
   `ColorExpr.TokenRef` directly and must also still pass unmodified.
7. The `ColorExprWalkFixtureTests` fixture is **extended** with a rule-branch token, and its expected
   JSON-Pointer set grows accordingly. This is the guard for §4.3's new walk sites against the
   fail-open-over-sites limit at `SPEC-V2-FRAMEWORK.md:5210–5217`; without it a missed traversal site
   validates as green.
8. `ReferenceExtractorCoverageTests` passes with the amended table (§7 items 5–8), and adding a `string`
   member to either the `ColorExpr` **or** `ColorValue` abstract base fails it.
9. Deleting the new `ColorTokenExtractors` row for `ColorValue.TokenRef.Name` fails the coverage test,
   naming that member.
10. `tools/check-all.sh` green.

## 11. NEEDS-EVIDENCE

I ran nothing. Each item names what the answer decides.

1. **Does `Walk` currently reach `ColorRule` branches at all?** `ItemValueResolver.cs:108–128` visits
   `ColorExpr` nodes; whether it descends through `ColorExpr.Inline.Rule` into `Match`/`Thresholds`, and
   whether it walks the `colors`-table rules' branches, determines whether §4.3 *extends* a traversal or
   *adds* one. **This is the Ultra-Advisor's stated overturn condition (§9)** — if it does not descend,
   report to the Orchestrator before implementing, as a schedule decision.
2. **Is the resolved token table in scope at the `ColorRule` construction site?** `Config.cs:877–878`
   builds the branches; `ColorExprWalkFixtureTests.cs` shows `ResolveTopLevel` producing `topLevel.Colors`
   before `ResolveRootPane`, and `ScanReferences` (`ItemValueResolver.cs:298`) takes `tokens` as a
   parameter — so the constant-token check of §3.2 most likely belongs in the reference-validation pass
   rather than at parse. Confirm, and report which; the spec is then amended to match rather than the code
   being bent.
3. **Does `ColorRuleJsonConfig.Default` (`Config.cs:339–340`) have exactly one read site?** §4.1 assumes
   one place to insert `ParseColorValue`. If there are several, all must route through the single helper —
   the "exactly two `@`-inspecting functions" invariant is what matters, not "one call site".
4. **Count check for the §9.5.1 candidate enumeration.** `SPEC-V2-FRAMEWORK.md:5274–5277` has an open item
   expecting 19 candidates. After this change the expected count shifts (three members become recursed,
   two `ColorValue` subtype members appear). Run the enumeration and report the actual list; if it
   disagrees with §7, the spec text is corrected, not the number.
