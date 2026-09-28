# SPEC-103 — Fix dead `itemSettings.*.showLabel`, add `labelColor`, add engram fragment colors

**Status: READY TO IMPLEMENT.** No blocking evidence gaps. Two scope decisions
deferred to the Implementor with explicit stop-and-report rules (§A.4, §B.7).

Three parts, one theme — *per-item settings overriding a hardcoded rendering
decision*:

- **Part A** — bug fix: `showLabel` is dead in `--preview`.
- **Part B** — new `labelColor` on the shared `LabeledItemSettings` base.
- **Part C** — new `itemSettings.engram` fragment colors (folded in per the
  addendum request `20260823-191949-frk4`; deliberately **not** a separate spec).

Extends SPEC-102 §A's `LabeledItemSettings` mechanism. Supersedes nothing.

Parts A, B, and C are independent. A is one line and could ship alone.

---

## 0. Summary of what's actually wrong (Part A)

**The bug report's diagnosis was close but the blast radius is smaller than
stated.** `showLabel` is NOT dead in the live statusline. It is dead only in
`--preview` (and `--items`), because those render from a synthetic fixture whose
`ItemContext` never receives the user's `ItemSettings`.

This distinction is load-bearing: it changes the fix from "rewire the render
path" to "pass one argument into the fixture", and it means users running the
real statusline today already have working `showLabel` — the feature shipped
functional and only *looks* broken through the diagnostic surface.

Evidence, all verified by reading source at HEAD:

| `ItemContext` construction site | passes `ItemSettings`? |
|---|---|
| `src/ClaudeTuiLine/Program.cs:76` (`RunAsync` — the live statusline) | **YES** — `topLevel.ItemSettings` |
| `src/ClaudeTuiLine/Program.cs:362` (`RunPreview`, real-stdin branch) | **YES** — `topLevel.ItemSettings` |
| `src/ClaudeTuiLine/Program.cs:335` (`RunPreview`, synthetic branch) | **NO** |
| `src/ClaudeTuiLine/ItemsCommand.cs:53` (`--items`) | **NO** |
| `src/ClaudeTuiLine/SyntheticFixture.cs:43` | **NO** — cannot; no parameter exists |

`SyntheticFixture.CreateItemContext()` takes zero parameters:

```csharp
// src/ClaudeTuiLine/SyntheticFixture.cs:43-44
public static ItemContext CreateItemContext() =>
    new(Input, gitBranch: "feat/eng-1234", engram: new EngramResult(3, "◉ recalled"), remoteUrlProbe: () => "https://github.com/acme/acme-web");
```

`ItemContext`'s constructor declares `ItemSettingsJsonConfig? itemSettings = null`
(`src/ClaudeTuiLine/ItemContext.cs:36`), so the omission is silent — it binds the
default rather than failing to compile. That is why every link in the chain the
Orchestrator traced looked correct: **each one is correct.** The value is simply
never supplied at the head of the synthetic chain.

The selecting branch:

```csharp
// src/ClaudeTuiLine/Program.cs:328-336  (RunPreview)
var usedSynthetic = string.IsNullOrWhiteSpace(rawInput);
StatusInput input;
ItemContext ctx;
if (usedSynthetic)
{
    input = SyntheticFixture.Input;
    ctx = SyntheticFixture.CreateItemContext();   // <-- settings dropped here
}
```

`--preview` run from a terminal with nothing piped in has
`Console.IsInputRedirected == false` → `rawInput` stays null → `usedSynthetic` is
true → the settings-less branch. That is exactly how the bug was reproduced.

**Why the existing unit tests did not catch this.** `SegmentBuilderTests.cs:434`
(`OutputStyle_ShowLabelFalse_OmitsLabel`) and `:989`
(`Autocompact_ShowLabelFalse_ReturnsBareValue`) pass today, because they build the
context through a `CtxWithSettings(...)` helper that injects settings directly.
They test the segment builder, which was never broken. Nothing tested the wiring
at the fixture. §E.1 closes that hole.

---

## A. Part A — the bug fix

### A.1 Change `SyntheticFixture.CreateItemContext` to accept settings

`src/ClaudeTuiLine/SyntheticFixture.cs:43`

```csharp
public static ItemContext CreateItemContext(ItemSettingsJsonConfig? itemSettings = null) =>
    new(Input, gitBranch: "feat/eng-1234", engram: new EngramResult(3, "◉ recalled"),
        remoteUrlProbe: () => "https://github.com/acme/acme-web", itemSettings);
```

The parameter is optional so existing zero-arg callers keep compiling; the two
call sites below are then updated deliberately.

### A.2 Pass the user's settings at the preview call site

`src/ClaudeTuiLine/Program.cs:335`

```csharp
ctx = SyntheticFixture.CreateItemContext(topLevel.ItemSettings);
```

`topLevel` is already in scope — bound at `Program.cs:310` from
`ConfigLoader.LoadAll(configPath)`, and already used by the sibling branch at
line 362. No new plumbing, no new load.

**This single line is the whole bug fix.** §A.3 and §A.4 are consistency work.

### A.3 What must NOT change

- `OutputStyleFormat` (`"style:{}"`) and `AutocompactFormat`
  (`"autocompact:{}"`) — unchanged, both still `SegmentBuilder.cs:285` / `:296`.
- The default `"yellow"` color for both items — unchanged.
- `LeafItems.ApplyFormat`, both overloads — unchanged. They are correct.
- `ItemRegistry.cs:73` / `:76` rows — unchanged. They are correct.
- The `item.Format` override path in `LeafItems.ResolveDisplay` — unchanged. The
  Orchestrator's live workaround config depends on it and must keep working.
- `ItemContext`'s constructor signature — unchanged.
- With no `itemSettings` block at all, rendering must stay **byte-identical** to
  today on every surface. §B.2 and §C.3 are designed around this.

### A.4 `--items` — Implementor decision, with a stop rule

`ItemsCommand.cs:53` (`var ctx = SyntheticFixture.CreateItemContext();`) has the
same omission. `--items` renders an `example` field per item, which is arguably
generic documentation rather than a reflection of the user's config, so this is
not clearly a bug.

**Rule:** if `ItemsCommand` already has the loaded top-level config in scope,
pass it — one line, consistent with §A.2. **If it does not** (i.e. `--items` never
loads config today), **do not add a config load to make it possible** — that is a
behavior change to a documentation command and is out of scope. Stop, leave
`ItemsCommand.cs:53` alone, and report it.

I could not settle this from the read I did; it is a genuine scope call, and the
acceptance criteria only cover `--preview`. Deferring rather than guessing.

---

## B. Part B — `labelColor`

### B.1 Config surface

`src/ClaudeTuiLine/Config.cs`, on the existing shared base (~line 79-85):

```csharp
public abstract class LabeledItemSettings
{
    [JsonPropertyName("showLabel")]
    public bool? ShowLabel { get; set; }

    /// Color for the label fragment only (the literal text around "{}" in the
    /// item's format), leaving the value's own color untouched. Null renders the
    /// whole item in one color, as before. Inert when ShowLabel is false.
    [JsonPropertyName("labelColor")]
    public string? LabelColor { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
```

`OutputStyleItemSettings` and `AutocompactItemSettings` stay empty derived
classes — they inherit it. A third item adopting `LabeledItemSettings` later
gets both settings with zero edits here, which is SPEC-102 §A's stated intent.

A plain `string?` (not `ColorExpr`) is deliberate — see §C.2, which hits the same
constraint for a concrete structural reason.

### B.2 The rendering helper — one shared implementation

New in `src/ClaudeTuiLine/SegmentBuilder.cs`, next to the two item builders:

```csharp
/// Renders "<label><value><suffix>" honoring both LabeledItemSettings knobs.
/// Falls back to the exact single-color segment this produced before labelColor
/// existed whenever no label color is in play, so default output is unchanged.
internal static Segment LabeledSegment(string format, string value, string color, LabeledItemSettings? settings)
{
    if (settings?.ShowLabel == false)
    {
        return SingleColor(color, value);
    }

    if (settings?.LabelColor is not { Length: > 0 } labelColor)
    {
        return SingleColor(color, LeafItems.ApplyFormat(format, value));
    }

    var marker = format.IndexOf("{}", StringComparison.Ordinal);
    if (marker < 0)
    {
        return SingleColor(color, LeafItems.ApplyFormat(format, value));
    }

    var prefix = format[..marker];
    var suffix = format[(marker + 2)..];

    var spans = new List<StyledSpan>(3);
    if (prefix.Length > 0)
    {
        spans.Add(new StyledSpan(prefix, BuildSpanMarkup(prefix, labelColor)));
    }
    spans.Add(new StyledSpan(value, BuildSpanMarkup(value, color)));
    if (suffix.Length > 0)
    {
        spans.Add(new StyledSpan(suffix, BuildSpanMarkup(suffix, labelColor)));
    }

    return BuildCompoundSegment(spans);
}
```

Reuses what already exists — `StyledSpan` is
`public readonly record struct StyledSpan(string Plain, string Markup)`
(`src/ClaudeTuiLine/Segment.cs:6`), `BuildSpanMarkup` is `SegmentBuilder.cs:93`,
`BuildCompoundSegment` is `SegmentBuilder.cs:116`. No new segment type, and
`ApplyFormat` stays a pure string function — deliberately **not** widened to
return something richer, since only the two-color case needs spans and paying for
it on every caller would be the wrong trade.

**The two early returns are the backward-compat guarantee.** When `labelColor` is
unset the function returns literally `SingleColor(...)`, the same call as today,
producing a `Segment` with `Spans == null`. Segments carrying spans travel a
different downstream path (truncation, color floor), so *not* emitting spans in
the default case is what keeps existing golden output byte-identical.

**Known limitation, accepted:** the split takes the **first** `{}`, whereas
`ApplyFormat` replaces **all** occurrences. Both format constants contain exactly
one, so the two agree today. A hypothetical multi-`{}` format with `labelColor`
set would color only around the first. Worth a `ponytail:` comment naming the
ceiling; not worth code.

### B.3 Wire both items

`src/ClaudeTuiLine/SegmentBuilder.cs:287-288` and `:300-301` become:

```csharp
internal static Segment? BuildOutputStyle(OutputStyleInfo? style, OutputStyleItemSettings? settings = null) =>
    ResolveOutputStyle(style) is { } raw ? LabeledSegment(OutputStyleFormat, raw, "yellow", settings) : null;

internal static Segment? BuildAutocompact(ItemContext ctx, AutocompactItemSettings? settings = null) =>
    ResolveAutocompact(ctx) is { } raw ? LabeledSegment(AutocompactFormat, raw, "yellow", settings) : null;
```

`LabeledSegment`'s parameter is typed as the **base** `LabeledItemSettings?`, so
both derived types pass without conversion and a third item needs no new
overload. `ResolveOutputStyle` / `ResolveAutocompact` stay untouched — color rules
still match on the bare value, exactly as SPEC-102 required.

### B.4 Rendering table

Assume value `Explanatory`, default color `yellow`.

| config | rendered |
|---|---|
| (no `itemSettings`) | `style:Explanatory`, all yellow — **byte-identical to today** |
| `showLabel: true` | same as above |
| `showLabel: false` | `Explanatory`, yellow, no prefix |
| `labelColor: "grey"` | `style:` grey + `Explanatory` yellow |
| `showLabel: false, labelColor: "grey"` | `Explanatory`, yellow — labelColor **inert, not an error** |
| `labelColor: ""` | treated as unset (the `{ Length: > 0 }` guard) |

Identical for autocompact with the `autocompact:` prefix.

### B.5 Interaction with the item's own `color` — pre-existing, out of scope

A per-item `color` override reaches `SegmentBuilder.BuildItemSegment` at
`PaneAssembler.cs:174`, which wraps the whole markup and drops spans:

```csharp
// SegmentBuilder.cs:105-108
public static Segment BuildItemSegment(string plain, string markup, string? color, IReadOnlyList<StyledSpan>? spans = null) =>
    string.IsNullOrEmpty(color)
        ? new Segment(markup, plain, spans)
        : new Segment($"[{color}]{markup}[/]", plain, null);
```

Because the inner tags survive inside `markup` and inner markup wins, a per-item
`color` on `output-style` is **already** largely ineffective today — the builder
hardcodes `"yellow"` inside the markup it returns. `labelColor` neither creates
nor worsens this. The same is true of `engram` (§C). Do **not** try to fix it
here; it is a separate defect (or a separate deliberate design), and conflating
the two would expand this diff into the color-resolution path for no
acceptance-criteria gain. Flagging it so the Reviewer does not read it as a
regression introduced by this spec.

### B.6 Schema — hand-written, must be edited

**Correcting a prior assumption:** this project's schema is **not**
reflection-generated. `src/ClaudeTuiLine/SchemaCommand.cs:408-432` enumerates each
settings class literally. `labelColor` will not appear on its own.

In **both** the `outputStyleItemSettings` and `autocompactItemSettings` entries:

1. Add `"labelColor"` to the known-keys array (currently `new[] { "showLabel" }`,
   lines 413 and 426).
2. Add a field entry beside the existing `showLabel` one (lines 416 and 429):

```csharp
Field("labelColor", "string", "Color for the \"style:\" label only, leaving the value's color unchanged. Unset renders the whole item in one color."),
```
```csharp
Field("labelColor", "string", "Color for the \"autocompact:\" label only, leaving the value's color unchanged. Unset renders the whole item in one color."),
```

Leave the `Parse("""{"showLabel":false}""")` examples as they are, or extend to
`{"showLabel":false,"labelColor":"grey"}` — cosmetic, Implementor's choice, but
note `SchemaCommandTests` may assert on them.

§C.5 adds a third entry to this same list for engram.

### B.7 `--check` validation (recommended, not required by acceptance)

An unknown color string in `labelColor` — or in engram's two keys (§C) — would
silently render nothing useful. `ConfigCheck.CheckItemSettings` (reached from
`ConfigCheck.cs:74`) already has the settings objects in hand, and
`ColorResolution.ResolveLiteral(spec)` returning null is the existing "unknown
color" test used by `CheckLiteralSpec` (`ConfigCheck.cs:285-299`).

Add, for each of `OutputStyle.LabelColor`, `Autocompact.LabelColor`,
`Engram.FactsColor`, `Engram.VerbColor`: if non-empty and
`ColorResolution.ResolveLiteral` returns null, emit the same `unknown-color`
diagnostic shape at path `itemSettings/<item>/<key>`.

Do not route it through `CheckLiteralSpec` itself — that takes a `ColorExpr` and
also emits `color-down-converted` warnings, which would need the terminal's color
system threaded in for a decorative label. Not worth it. **If this turns out to
be more than ~15 lines total across all four keys, skip it and report** — it is a
nicety, not an acceptance criterion.

**Acceptance requires only that `--check --json` stays clean for a valid config
using these keys** (§D) — the extension data captures unknown keys, and since all
are now declared properties, none will be reported as unknown. That falls out of
§B.1 and §C.1 for free.

---

## C. Part C — engram fragment colors

### C.0 What's hardcoded today

```csharp
// src/ClaudeTuiLine/SegmentBuilder.cs:611-632
internal static Segment? BuildEngram(EngramResult? engram)
{
    if (engram is null || (engram.Facts is null && engram.Verb is null))
    {
        return null;
    }

    string? factsPlain = engram.Facts is { } facts ? $"engram:{facts}" : null;
    string? factsMarkup = factsPlain is not null ? $"[dim]{factsPlain}[/]" : null;

    string? verbPlain = engram.Verb;
    string? verbMarkup = verbPlain is not null ? $"[purple]{Markup.Escape(verbPlain)}[/]" : null;
    ...
}
```

Two fragments, two baked-in tags, no state branching and no settings parameter.

### C.1 Config surface — its own class, not `LabeledItemSettings`

Correct call in the addendum brief: engram has no label/value split and nothing
suppressible, so `LabeledItemSettings` is the wrong shape and forcing it there
would make the base class incoherent for both consumers. New sibling class in
`src/ClaudeTuiLine/Config.cs`, alongside the existing per-item settings classes:

```csharp
/// Settings for the engram item. Each color overrides one fragment; unset keeps
/// that fragment's built-in color, so a partially-specified block is valid.
public sealed class EngramItemSettings
{
    [JsonPropertyName("factsColor")]
    public string? FactsColor { get; set; }

    [JsonPropertyName("verbColor")]
    public string? VerbColor { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
```

Note it declares its **own** `[JsonExtensionData]` — it does not derive from
`LabeledItemSettings`, so there is no conflict with that base's `Extra` (a class
may declare `[JsonExtensionData]` only once across its inheritance chain).

And on `ItemSettingsJsonConfig` (`Config.cs:48-76`), which has no engram property
today:

```csharp
[JsonPropertyName("engram")]
public EngramItemSettings? Engram { get; set; }
```

Register `EngramItemSettings` on the `JsonSerializerContext` with
`[JsonSerializable]` the same way the other settings classes are. The existing
`[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false)]` applies
unchanged — the explicit `[JsonPropertyName]` attributes above make the casing
exact rather than inferred.

### C.2 Color type — plain `string?`, for a structural reason

The brief left `ColorExpr` vs. plain string to my judgment. **Plain string**, and
not merely as a scope cut:

`ColorResolution.Resolve(ColorExpr?, values, tokens)` (`ColorResolution.cs:70`)
requires the resolved values dictionary and the token table. `BuildEngram` is
reached through the registry's `BuildDefaultSegment(ctx)` delegate
(`ItemRegistry.cs:81`), which receives **only** an `ItemContext` — that carries
neither. Threading both into every `BuildDefaultSegment` to give one decorative
fragment threshold-coloring would be a large, cross-cutting change to the item
pipeline for a feature nobody asked for.

The same argument covers `labelColor` in §B.1. If value-driven fragment coloring
is ever genuinely wanted, that is its own spec with its own justification for
widening the delegate signature.

Accepted consequence: `factsColor`/`verbColor`/`labelColor` take a literal color
name or hex — not `@token` references and not inline rules. State this in the
schema description so the limitation is discoverable rather than surprising.

### C.3 The change to `BuildEngram`

Add an optional settings parameter and substitute the two tags. Everything else
in the method — the null handling, the join, the `Segment` construction — is
untouched:

```csharp
internal static Segment? BuildEngram(EngramResult? engram, EngramItemSettings? settings = null)
{
    if (engram is null || (engram.Facts is null && engram.Verb is null))
    {
        return null;
    }

    var factsColor = settings?.FactsColor is { Length: > 0 } fc ? fc : "dim";
    var verbColor = settings?.VerbColor is { Length: > 0 } vc ? vc : "purple";

    string? factsPlain = engram.Facts is { } facts ? $"engram:{facts}" : null;
    string? factsMarkup = factsPlain is not null ? $"[{factsColor}]{factsPlain}[/]" : null;

    string? verbPlain = engram.Verb;
    string? verbMarkup = verbPlain is not null ? $"[{verbColor}]{Markup.Escape(verbPlain)}[/]" : null;

    // ...remainder unchanged...
}
```

The `{ Length: > 0 }` guards mirror §B.2, so an empty string is treated as unset
rather than emitting `[]`. With `settings` null both locals take today's literals,
making default output byte-identical by construction.

Registry row `src/ClaudeTuiLine/ItemRegistry.cs:81` gains the settings argument,
matching how `output-style` and `autocompact` already do it:

```csharp
ctx => SegmentBuilder.BuildEngram(ctx.Engram, ctx.ItemSettings?.Engram),
```

`ResolveEngram(ctx.Engram)` — the value half of the row — stays unchanged.

### C.4 Pre-existing inconsistency, flagged not fixed

`factsPlain` is interpolated into markup **without** `Markup.Escape`, while
`verbPlain` is escaped. Today `factsPlain` is `$"engram:{facts}"` where `facts` is
a count, so nothing needs escaping and this is latent rather than live. It is not
introduced or worsened by this change.

Adding the escape would be correct and one word long, but it is out of scope and
could in principle alter golden output. **Implementor: do not change it.**
Reviewer: noted here so it reads as known, not missed.

### C.5 Schema entry

A third `StructureEntryJson` in `SchemaCommand.cs`, beside the two from §B.6:

```csharp
new StructureEntryJson(
    "engramItemSettings",
    "EngramItemSettings",
    "Settings for the engram item.",
    Array.Empty<string>(),
    new[] { "factsColor", "verbColor" },
    new[]
    {
        Field("factsColor", "string", "Color for the \"engram:N\" fragment. A literal color name or hex; token references and inline rules are not supported here. Default dim."),
        Field("verbColor", "string", "Color for the activity fragment (e.g. \"◉ recalled\"). A literal color name or hex; token references and inline rules are not supported here. Default purple."),
    },
    Array.Empty<string>(),
    Parse("""{"factsColor":"cyan","verbColor":"red"}""")),
```

Also add the `engram` property to whatever schema structure describes
`itemSettings`' own keys, alongside `outputStyle` / `autocompact` — the
`ItemSettingsJsonConfig` entry in the same file.

### C.6 Rendering table

Fixture values `engram:3` and `◉ recalled`.

| config | facts | verb |
|---|---|---|
| (no `itemSettings.engram`) | dim | purple — **byte-identical to today** |
| `factsColor: "cyan"` | cyan | purple |
| `verbColor: "red"` | dim | red |
| both set | cyan | red |
| `factsColor: ""` | dim (treated as unset) | purple |

### C.7 The stale item description — separate spec-defect, not fixed here

`--items --json` describes engram as *"Its colour reflects whether the store is
reachable and active."* `BuildEngram` has no state branching whatsoever, so that
description is **false today**, independent of this change.

Two readings — the description is aspirational for a feature never built, or
state-conditional coloring regressed out at some point — and I cannot tell which
from static reading. Either way it is a pre-existing defect, it is not what the
addendum asked for, and fixing it means either rewriting the description or
building conditional coloring.

**Do not address it in this spec.** Recommend the Orchestrator file it separately
and put the "which reading is right" question to the user, since one answer is a
docs edit and the other is a feature.

Note that this spec makes the description *less* wrong in practice: after Part C
the color is at least user-controllable, even though it still does not reflect
reachability.

---

## D. Acceptance criteria (restated as verifiable checks)

**Part A / B:**

1. Minimal config, single `output-style` item, `itemSettings.outputStyle.showLabel: false`
   → `--preview` output has **no** `style:` prefix.
2. Same config with `showLabel: true`, and with the key omitted → prefix **present**,
   and the two outputs identical to each other.
3. Both of the above hold for `autocompact` / `autocompact:`.
4. `itemSettings.outputStyle.labelColor: "grey"` → `style:` renders grey while
   `Explanatory` stays yellow. Same for autocompact.

**Part C:**

5. `itemSettings.engram.factsColor: "cyan"` → `engram:3` cyan, `◉ recalled` still purple.
6. `itemSettings.engram.verbColor: "red"` → `◉ recalled` red, `engram:3` still dim.
7. Both set together → both take effect independently.
8. Neither set → byte-identical to today (dim + purple).

**All parts:**

9. `--check --json` on a config using every new key reports no `unknown-key`
   diagnostic for any of them.
10. With no `itemSettings` block at all, `--preview` output is byte-identical to
    pre-change output for all three items.

Criteria 5-8 are only observable through `--preview` **because of Part A** — the
synthetic fixture supplies engram's values, and before A.2 it discarded settings.
Part C is therefore untestable via preview without Part A. Implement A first.

---

## E. Tests the Implementor should add

Smoke level only, per this project's convention (full-suite verification belongs
to the Reviewer/task-runner, not the Implementor).

**E.1 — the regression guard that would have caught this bug.** The most
important test in this spec. Assert that
`SyntheticFixture.CreateItemContext(settings)` returns a context whose
`ItemSettings` is the object passed, and — better, since it tests the real defect
— that `--preview` with a synthetic fixture and `showLabel: false` omits the
label. `PreviewCliTests.cs` is the right home; the existing `SegmentBuilderTests`
coverage is precisely what failed to catch this, so adding more there would
repeat the mistake.

**E.2** — `LabeledSegment` with `labelColor` unset returns a segment with
`Spans == null` and markup equal to the old `SingleColor(...)` output. This is the
backward-compat contract stated in §B.2, and it is the one most likely to be
broken by a well-meaning refactor.

**E.3** — `LabeledSegment` with `labelColor` set produces spans whose
concatenated `Plain` equals the un-colored formatted string, and whose prefix span
markup carries the label color while the value span carries the item color.

**E.4** — `showLabel: false` + `labelColor` set → bare value, no spans, no error.

**E.5** — `BuildEngram(engram, null)` markup is exactly today's
`[dim]engram:3[/] [purple]◉ recalled[/]`. The default-preservation contract.

**E.6** — `BuildEngram` with only `FactsColor` set changes the facts tag and
leaves the verb tag `purple`, and the mirror case for `VerbColor`. One test each
way; this is the independence claim in criteria 5-7.

---

## F. Decisions made, and decisions refused

**Made:**
- Fix at the fixture (§A.1/A.2) rather than anywhere downstream — every downstream
  link was verified correct, and this is the single point where the value is lost.
- `labelColor` as `string?` on the shared base; engram's two colors as `string?`
  on their own class. Both constrained by §C.2's structural argument, not taste.
- `EngramItemSettings` as a sibling class, not a `LabeledItemSettings` subclass —
  the shapes are genuinely different and merging them would corrupt the base's
  meaning for both.
- Spans only when two colors are actually in play (§B.2), preserving the exact
  prior segment shape otherwise.
- `ApplyFormat` left alone rather than widened to return a richer type.
- Default colors expressed as fallback locals (§C.3) rather than moved to
  constants — keeps the diff minimal and the defaults visible at the use site.

**Refused / deferred:**
- §A.4 `--items`: deferred to the Implementor with a hard stop rule, because it
  depends on a fact about `ItemsCommand`'s config access I did not verify and
  because it is a scope question, not a correctness one.
- §B.5 per-item `color` vs. the hardcoded builder colors: pre-existing,
  deliberately untouched.
- §B.7 `--check` validation: recommended with an explicit bail-out threshold.
- §C.4 the missing `Markup.Escape` on `factsPlain`: latent, not live, out of scope.
- §C.7 engram's false `--items` description: **needs a user decision** (docs fix
  vs. build the feature). Recommend a separate ticket; explicitly not resolved here.

**Not escalated.** Nothing here is security-, migration-, or concurrency-
sensitive; the added public surface is three optional config keys plus one new
settings class, and the bug fix is one argument at one call site. Confidence high
on all three parts.

---

## G. Correction to the incoming brief

The brief stated the feature was "verified non-functional" and shipped dead. More
precisely: **it is functional in the live statusline and non-functional in
`--preview`.** The repro was performed through `--preview`, which selects the
synthetic-fixture branch whenever nothing is piped to stdin.

Two consequences worth carrying forward:

1. The Orchestrator's live-config workaround (replacing `itemSettings` with a
   per-item `format` override) was **not** necessary for the real statusline — the
   `itemSettings` form was already working there. It can be reverted whenever
   convenient; nothing in this spec depends on which form is in the config.
2. `--check` came back clean and the schema listed the key because both were
   genuinely correct. The diagnostic surface was not lying — the *preview* surface
   was, and it is also the surface used to check the fix. Any future "config key
   has no effect" report should test through the live path before concluding the
   feature is dead.

---

## Amendments

**Rev 2** — folded in Part C (engram fragment colors) per addendum request
`20260823-191949-frk4`, as a new §C with its own config class, plus additions to
§B.6/§B.7 (schema and check wiring now cover three items), §D (criteria 5-8),
§E (E.5, E.6), and §F. Title and status header updated. Part A and Part B are
unchanged from rev 1 — no behavior in them was revised by the addendum.

The one cross-part finding worth calling out: **Part C cannot be verified through
`--preview` unless Part A ships first** (§D), because engram's values come from
the synthetic fixture that Part A repairs. That ordering is a real dependency
between two otherwise-independent parts.
