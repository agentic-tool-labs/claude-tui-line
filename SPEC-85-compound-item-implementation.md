# SPEC-85 — Implement compound items (`parts`) end to end

Implements the defect filed in `SPEC-3.3-compound-item-schema-not-implemented.md`. That document
is the root-cause analysis and is **not** restated here — read it first. This document is the
implementation design for its "Open questions" §2.

**The decision to implement is already made and is not open for re-litigation.** The canonical use
case is the user's own request — "dim the `agent:` label, keep the value a different colour, as one
item" — which `/claude-tui-line:edit` currently has to satisfy with a two-derived-items workaround.
That workaround is not merely cosmetically inferior: the two items wrap and drop *independently*,
so a narrow surface can drop the value and leave a bare `agent:` behind.

**The behaviour is already fully specified.** `SPEC-V2-FRAMEWORK.md` §3.3 (lines 2657–2745) states
every semantic rule, and §9.6.1 (lines 5413–5414) already reserves the two diagnostic codes. This
document does not invent semantics; where it appears to, that is a bug in this document and §3.3
wins. What this document adds is *where the code goes*.

---

## 1. Current state — verified, not assumed

| Fact | Evidence |
|---|---|
| `--items --json` advertises `compound` with `required:["id","parts"]`, `optional:["color","overflow","link"]` | `src/ClaudeTuiLine/ItemsCommand.cs:27`, `:49` |
| `PaneItemJsonConfig` has 14 properties + `[JsonExtensionData] Extra`; no `parts`, no `kind` | `src/ClaudeTuiLine/Config.cs:204-263` |
| Domain type is `public sealed record PaneItem(...)`, 14 positional members | `src/ClaudeTuiLine/Pane.cs:230-244` |
| Sole JSON→domain mapping is `ToPaneItems` | `src/ClaudeTuiLine/Config.cs:782-798` (only `new PaneItem(` site in `src/`) |
| `unknown-key`'s allowed set is **reflection over `JsonTypeInfo`**, not a hand-written list | `src/ClaudeTuiLine/ConfigCheck.cs:849-853` (`KnownKeys`), emitted at `:875-876` |
| The objects walked for unknown keys are enumerated by `WalkRawObjects` | `src/ClaudeTuiLine/ConfigCheck.cs:883-932` |
| `part-source-count` / `part-forbidden-key` appear **only** in `SPEC-V2-FRAMEWORK.md` and `STATUS.md`. **Zero references in `src/` or `tests/`.** | repo-wide grep |
| One rendered item is `public sealed record Segment(string Markup, string Plain)` | `src/ClaudeTuiLine/Segment.cs:7` |
| Item suppression is `resolved.Value is null → continue` | `src/ClaudeTuiLine/PaneAssembler.cs:138`; mirrored in `PaneCollapse.cs:75` and `SizeResolver.cs:1009` |
| Wrap/drop/measure all key on `Segment.Plain.Length` | `src/ClaudeTuiLine/RowLayout.cs:123,125,127,137-138` |
| Inter-item separator `" [dim]|[/] "` is inserted **only between `Segment`s** in `Compose` | `src/ClaudeTuiLine/RowLayout.cs:8,140-141` |

### 1.1 The one genuinely hard finding

`SegmentTruncation.RestyleSimple` (`SegmentTruncation.cs:106-113`) rebuilds a cut segment's markup
via `TryGetSimpleWrap` (`:115-143`), which succeeds **only** for markup that is exactly
`[color]<escaped Plain>[/]` or wholly unstyled. For any composite markup it returns `false`, and
`RestyleSimple` then **degrades the segment to fully unstyled text** (`:110`).

A compound item's markup is composite by construction. So without the change in §5 of this
document, a compound item that is truncated (`SegmentTruncation.Truncate`, via
`RowLayout.cs:111`) or wrapped (`SegmentTruncation.WrapToWidth`, `:53-71`) would **silently lose
every per-part colour** — the exact feature being built. §3.3 calls this out ("the one genuinely
new implementation hazard here and needs its own test") and requires that surviving spans keep
their markup.

There is no SGR-bleed risk — the degradation is safe, just colourless. Nothing is currently
*broken*; it is a wall this feature walks into.

> Note, out of scope but worth filing separately: the same degradation already affects builtins
> whose markup is composite (e.g. `context`'s `ctx:62% (125k/200k)`) whenever they are truncated
> or wrapped. §5's change fixes that class too if those builders opt in. **Do not** opt them in as
> part of this change.

---

## 2. Resolved question — the discriminator

**Decision: there is no `kind` key. A compound item is inferred purely from the presence of
`parts`. `kind` remains an unrecognized key and continues to produce `unknown-key`.**

Rationale, in order of weight:

1. `--items --json` already declares `compound.required = ["id","parts"]`. `kind` is not in it. The
   contract that must be honoured is the one already shipped.
2. §3.3's worked example (`SPEC-V2-FRAMEWORK.md:2668`) carries no `kind`.
3. The other three kinds are *all* inferred from their distinguishing key — `item` → builtin,
   `command` → command, `from` → derived. Introducing a discriminator for one kind only makes the
   config language inconsistent with itself, and would immediately raise "why is `kind` optional
   for three kinds and required for the fourth".

Consequence: the defect doc's repro (`"kind":"compound"` warns as `unknown-key`) stays true after
this ships, and that is **correct behaviour, not a residual defect**. It must be documented, or
the next person re-files the same report — see §7's doc task.

Also decided: `parts` and `from` on the same item, or `parts` and `command`, is a config error.
See §4.4.

---

## 3. Config model

### 3.1 New JSON type — `src/ClaudeTuiLine/Config.cs`

Add immediately after `PaneItemJsonConfig` (i.e. after line 263):

```csharp
/// <summary>
/// SPEC-V2-FRAMEWORK.md §3.3: one fragment of a compound item. Exactly one of
/// <see cref="Text"/>/<see cref="Item"/>/<see cref="From"/> is the part's source; the rest of the
/// vocabulary is the same one a pane item carries, because a part is an item fragment.
/// <see cref="Parts"/> and <see cref="Link"/> are declared solely so §3.3's one-level and
/// item-level-link rules are reported as <c>part-forbidden-key</c> errors rather than as
/// <c>unknown-key</c> warnings.
/// </summary>
public sealed class PaneItemPartJsonConfig
{
    [JsonPropertyName("text")]   public string? Text { get; set; }
    [JsonPropertyName("item")]   public string? Item { get; set; }
    [JsonPropertyName("from")]   public string? From { get; set; }
    [JsonPropertyName("extract")] public string? Extract { get; set; }
    [JsonPropertyName("case")]   public string? Case { get; set; }
    [JsonPropertyName("format")] public string? Format { get; set; }

    [JsonPropertyName("color")]
    [JsonConverter(typeof(ColorExprJsonConverter))]
    public ColorExprJsonConfig? Color { get; set; }

    [JsonPropertyName("parts")]  public List<PaneItemPartJsonConfig>? Parts { get; set; }
    [JsonPropertyName("link")]   public string? Link { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
```

Add to `PaneItemJsonConfig` (before the `Extra` member at line 261):

```csharp
/// <summary>SPEC-V2-FRAMEWORK.md §3.3: this item's fragments, concatenated with no separator between them.</summary>
[JsonPropertyName("parts")]
public List<PaneItemPartJsonConfig>? Parts { get; set; }
```

**Why `Parts`/`Link` are declared on the part type rather than special-cased in the checker.**
`KnownKeys` (`ConfigCheck.cs:849-853`) is reflection over the bound type — §9.4.2's deliberate
design, so the accepted-key set is never a second hand-maintained mirror of the shape. Intercepting
two specific strings inside the unknown-key walk would reintroduce exactly that mirror. Declaring
them means they bind, and a normal property check in §4.2 rejects them at `error` severity as §3.3
requires (an `unknown-key` warning would be the wrong severity).

Accepted cost: `KeySuggestion.Suggest` may now offer `link` as a "did you mean" for a typo inside a
part. That is a cosmetic wart on a message, not a behaviour change.

### 3.2 Domain type — `src/ClaudeTuiLine/Pane.cs`

Add a part record and one member on `PaneItem`:

```csharp
public sealed record PaneItemPart(
    string? Text,
    string? Item,
    string? From,
    string? Extract,
    string? Case,
    string? Format,
    ColorResolution.ColorExpr? Color);
```

`PaneItem` (`Pane.cs:230-244`) gains **one trailing optional positional member**, appended after
`MaxLines` so no existing construction site changes:

```csharp
    int? MaxLines = null,
    IReadOnlyList<PaneItemPart>? Parts = null);
```

`PaneItem.Parts` is `null` for every non-compound item — never an empty list. `Parts is not null`
is the compound predicate everywhere downstream; do not test `Count > 0` (an explicitly empty
`parts: []` is a config error per §4.2, and must still be *recognised* as a compound so the error
fires rather than the item silently behaving as a builtin).

### 3.3 Mapping — `Config.cs:782-798`

Extend `ToPaneItems`'s `new PaneItem(...)` with a final argument:

```csharp
        i.MaxLines,
        ToPaneItemParts(i.Parts))).ToList()
```

and add:

```csharp
private static IReadOnlyList<PaneItemPart>? ToPaneItemParts(List<PaneItemPartJsonConfig>? parts) =>
    parts?.Select(p => new PaneItemPart(
        p.Text,
        p.Item,
        p.From,
        p.Extract,
        p.Case,
        p.Format,
        ParseColorExpr(p.Color, p.Item))).ToList();
```

The second argument to `ParseColorExpr` is the id a bare colour *rule* keys its `from` off when the
rule omits one. Mirror the item-level convention (`Config.cs:785` passes `i.Id ?? i.Item`); a part
has no id of its own, so `p.Item` is the only candidate — a `text` or `from` part with a rule that
omits `from` therefore has no implicit source, exactly as an item with neither would.

### 3.4 AOT / source-gen contexts

`PaneItemPartJsonConfig` must be registered or trimming will break it at runtime with no compile
error. Add `[JsonSerializable(typeof(PaneItemPartJsonConfig))]` to:

- `ConfigJsonContext` — `src/ClaudeTuiLine/Config.cs:462-473` (alongside `PaneItemJsonConfig` at `:470`)
- `CheckJsonContext` — `src/ClaudeTuiLine/ConfigCheck.cs:44-47`

`List<PaneItemPartJsonConfig>` may also need registering; follow whatever the existing
`List<PaneItemJsonConfig>` registration does in each context.

---

## 4. Validation — `src/ClaudeTuiLine/ConfigCheck.cs`

### 4.1 `unknown-key` inside a part (no new code path)

`KnownKeys` is reflection-driven, so adding `Parts` to `PaneItemJsonConfig` makes `parts` a known
key automatically — **this alone closes the filed defect's headline symptom.**

For unknown keys *inside* a part, `WalkRawObjects` (`ConfigCheck.cs:883-932`) must additionally
yield one tuple per part. Where it currently walks each item, also walk that item's `parts`:

- `extra` → the part's `Extra`
- `typeInfo` → `PaneItemPartJsonConfig`'s
- `label` → `"a compound part"` (so the message reads `unknown key 'colour' on a compound part`)
- `path` → `"{itemPath}/parts/{i}"`

`CheckUnknownKeys` itself (`:855-880`) needs no change.

### 4.2 New checks

Add one method, `CheckCompoundParts`, and register it wherever the other `Check*` methods are
composed. It walks items via the same mechanism the existing item checks use
(`ItemValueResolver.WalkItems`, per `ItemValueResolver.cs:338`), and for each item with
`Parts is not null`, for each part at index `i` (path `{itemPath}/parts/{i}`):

| Condition | Code | Severity | Message |
|---|---|---|---|
| Count of non-null `Text`/`Item`/`From` on the part ≠ 1 | `part-source-count` | error | zero: `a compound part must name exactly one source — 'text', 'item', or 'from'`; more than one: name which ones were present |
| Part's `Parts is not null` | `part-forbidden-key` | error | `'parts' may not appear inside a compound part — compound items are one level deep` |
| Part's `Link is not null` | `part-forbidden-key` | error | `'link' belongs on the item, not on a part — it wraps the whole compound` |
| `parts` present but the list is empty (`Count == 0`) | `part-source-count` | error | `a compound item must declare at least one part`; path is `{itemPath}/parts` |

`text` is checked for *presence*, not for non-emptiness: `{"text":""}` names a source, and is a
legal (if pointless) empty literal — not a `part-source-count` error. A part carrying `extract` or
`case` alongside `text` is **not** an error; §3.3 gives a part the item vocabulary and those keys
are simply inert on a literal. Do not invent a diagnostic for it.

Emit `part-source-count` and `part-forbidden-key` independently — a part with two sources *and* a
nested `parts` produces both. Do not short-circuit after the first.

Both codes are `error` severity (`SPEC-V2-FRAMEWORK.md:5413-5414`), which means `--check` returns
`ok:false`. That is the intended escalation from today's silent `ok:true` warning.

### 4.3 Reused codes — no new code strings

Per §3.3 (`SPEC-V2-FRAMEWORK.md:2734-2736`), a part naming an unknown id and a part naming an
unknown colour reuse `unknown-item-id` and `unknown-color`. Both fall out for free once §6.1 adds
the reference extractors and §3.3's mapping produces a real `ColorExpr` — the existing
`CheckReferences` and colour-literal checks already validate whatever the scan and the colour-expr
list hand them, and the JSON Pointer already says where. **Do not add part-specific variants of
either code.**

Two existing checks now also apply one level in, at no cost, and this is correct:

- `from-derived-source` — a part's `from` naming a derived item. §3.2.1's rule is unchanged by
  position.
- `unknown-enum-value` — a part's `case` that is neither `upper` nor `lower`. Extend
  `CheckItemEnums` (`ConfigCheck.cs:499`) to walk part `Case` values with
  `ItemValueResolver.IsUnrecognizedCase`, same as it does for item `Case`.

### 4.4 Kind collision

An item carrying `parts` alongside `from`, `command`, or `item` is two kinds at once and has no
defined meaning.

**Decision: emit `key-not-applicable` (warning, existing code) on the conflicting key**, message
naming `parts` as the kind that won, and have the resolver treat `Parts is not null` as decisive.

This is deliberately a warning rather than an error because `key-not-applicable` already means
exactly this ("a known key with a legal value on a node that never reads it", §9.6.1) and because
the behaviour is well-defined and safe. **I am flagging this as the one place I chose rather than
found** — §3.3 is silent on kind collision. If the project prefers an error, that is a reasonable
alternative and needs a new code in §9.6.1's registry, which is a heavier commitment.

Same treatment for item-level `format` and `maxLines` alongside `parts`: neither is in
`--items --json`'s `compound.optional` list, and neither is read (a compound's text comes from its
parts; `maxLines` is a provider-stage cap and a compound has no provider). `key-not-applicable`,
warning.

---

## 5. Rendering

This is where the design work actually is. Three files change, in this order.

### 5.1 `Segment` carries an optional span list — `src/ClaudeTuiLine/Segment.cs`

```csharp
/// <param name="Plain">The span's contribution to the segment's Plain, in order.</param>
/// <param name="Markup">The span's own markup — a colour wrap, or a builtin's composite markup.</param>
public readonly record struct StyledSpan(string Plain, string Markup);

public sealed record Segment(string Markup, string Plain, IReadOnlyList<StyledSpan>? Spans = null);
```

`Spans` is `null` for every segment built today — the parameter is optional and trailing, so no
existing construction site changes and no existing behaviour changes. It is non-null only for a
compound item.

**Invariant, and it must be asserted by a test:** when `Spans` is non-null,
`string.Concat(Spans.Select(s => s.Plain)) == Plain` and
`string.Concat(Spans.Select(s => s.Markup)) == Markup`. `Spans` is a *decomposition* of the
segment, never an alternative source of truth. Nothing downstream may read `Spans` to compute
width — width is `Plain.Length`, unconditionally, and §3.3's "width is unaffected" rule depends on
that staying true.

`Spans` carries `Markup` rather than a colour name so that a part naming a semantic builtin can
carry that builtin's own composite markup verbatim, satisfying §3.3's "a part naming a semantic
item keeps its value-derived threshold colour".

### 5.2 Span-aware slicing — `src/ClaudeTuiLine/SegmentTruncation.cs`

Read `SegmentTruncation.cs:1-48` before writing this — `Truncate`'s signature and its ellipsis
handling are in that range and are not reproduced here.

Add an offset-based entry point, because the current `Restyle(Segment, string newPlain)` takes the
*text* and cannot tell which region of the original it came from:

```csharp
internal static Segment RestyleSlice(Segment original, int start, int end)
```

- When `original.Spans is null` → `return Restyle(original, original.Plain[start..end]);`
  Byte-for-byte today's behaviour. No existing caller changes semantics.
- When `original.Spans is { } spans` → walk the spans accumulating plain offsets, and build the
  slice from three regions:
  1. Any span entirely outside `[start, end)` is dropped.
  2. Any span entirely inside is copied **verbatim, markup included**. This is §3.3's "the
     surviving spans keep their markup".
  3. A span the boundary lands inside is cut, and its own markup is rebuilt by delegating that
     *single span* through the existing `Restyle`/`RestyleSimple` path (treating the span as a
     one-span `Segment`). If that span's markup is a simple wrap it keeps its colour; if it is
     composite it degrades to unstyled — but **only that span**, never the whole item.

  Then reassemble: `Plain` = concatenated span plains, `Markup` = concatenated span markups,
  `Spans` = the surviving span list.

Rule 3 is a decision worth stating plainly: it bounds §1.1's degradation to the one severed
fragment instead of the whole item, and it means no new markup parser is introduced. §3.3 asks that
a cut "close any span the cut lands inside" — routing the severed span through `RestyleSimple`
satisfies that, because `RestyleSimple` emits a balanced `[c]…[/]` or nothing at all. **An
unbalanced SGR must never be emitted**; that is what would bleed colour into the border.

Then change the two call sites to use offsets:

- `WrapToWidth` (`:53-71`) — it already has `i` and `end`; replace
  `Restyle(segment, segment.Plain[i..end])` at `:66` with `RestyleSlice(segment, i, end)`.
  Also `:58`'s zero-width case becomes `RestyleSlice(segment, 0, 0)`.
- `Truncate` (in `:1-48`) — compute the cut index it already computes and call `RestyleSlice`
  with it. Route the cut index through `SafeCutIndex` (`:78-81`) exactly as today; surrogate-pair
  safety is unchanged and must stay.

The ellipsis is appended **outside** the last surviving span, unstyled, matching whatever `Truncate`
does today. Do not restyle the ellipsis into the severed span's colour.

### 5.3 Building the compound segment — `src/ClaudeTuiLine/LeafItems.cs`

`LeafItems.Resolve` (`:24`) produces `ResolvedItem(PaneItem Config, string? Value, Segment? Display)`,
and `ResolveDisplay` (`:41-50`) decides the display. Add a compound branch as the **first** test in
`ResolveDisplay`, before the `Format` branch:

```csharp
if (item.Parts is { } parts)
{
    return BuildCompound(item, parts, values, ctx);
}
```

`BuildCompound` returns `Segment?` — `null` suppresses (see §5.5). Its algorithm, in order:

**Step 1 — resolve each part's raw text**, into an array positionally aligned with `parts`:

- `text` part → the literal, verbatim. Never null.
- `from` part → `values.GetValueOrDefault(from)`, then `extract` then `case`, using the **same
  helpers `ResolveDerived` uses** — `ItemValueResolver.ExtractValue` and
  `ItemValueResolver.ApplyCase` (`ItemValueResolver.cs:468`, `:504`). Both are currently `private`;
  make them `internal`. Do **not** re-implement extract/case semantics — a second copy of the
  "first capture group, else whole match, no match ⇒ null" rule is precisely the drift this
  codebase's §1 warns about.
- `item` part → resolve as that item renders. If `ItemRegistry.Find(id)` hits, use
  `BuildDefaultSegment(ctx)` so the builtin's own default format and semantic colour are preserved
  (§3.3's semantic-precedence rule); otherwise (a `command` item id) use
  `values.GetValueOrDefault(id)`.
- Then apply the part's own `format` via `LeafItems.ApplyFormat` (`:52`) if set. Note
  `ApplyFormat`'s existing contract: an absent/empty format is `"{}"`.
- A part whose source is `item`/`from` and which resolved to `null` **or empty string** is an
  *empty value part*. §3.3 says "resolved to empty", and a command item legitimately returning `""`
  must behave the same as one returning nothing.

**Step 2 — literal adjacency drop.** §3.3's rule, verbatim in effect:

> A literal part is dropped when **any value part adjacent to it resolved to empty**, evaluated
> against the *original* array positions rather than against what earlier removals left behind.

So for the literal at original index `i`: drop it if `parts[i-1]` or `parts[i+1]` exists, is a
value part (`item`/`from`), and resolved empty. Look **both ways** — §3.3 has an explicit, bolded
correction on this point: the earlier one-directional rule left
`[{"text":"("},{"from":"pr"},{"text":")"}]` rendering a bare `(` for an absent PR, which §3.3
classes as the render-wrong failure class. Evaluate every literal against the *original* array, in
one pass, before removing anything; deciding each literal against an already-mutated array makes
the result depend on traversal direction.

A literal adjacent only to other literals is never dropped by this rule.

**Step 3 — assemble spans.** For each surviving part in original order, with non-empty resolved
text, emit one `StyledSpan`:

- Resolve the part's colour: the part's own `Color` if set, else the **item-level** `Color`
  (§3.3: "Item-level `color` is the default for parts that do not set one"), resolved through
  `ColorResolution.Resolve(expr, values, tokens)` the same way `PaneAssembler.cs:144` does.
- If a colour resolves → `Markup = "[" + color + "]" + Markup.Escape(text) + "[/]"`.
  Build this through `SegmentBuilder.BuildItemSegment(text, color)` and take its `.Markup`, so the
  escaping and the `RawSgrReset` convention (`SegmentBuilder.cs:73-81`) stay in one place.
- If no colour resolves **and** the part was an `item` part that produced a registry segment, use
  that segment's markup verbatim (semantic colour survives).
- Otherwise → unstyled: `Markup.Escape(text)`, via the same builder with `color: null`.

**Step 4 — build the `Segment`**: `Plain` = concatenated span plains, `Markup` = concatenated span
markups, `Spans` = the span list. **No separator between spans** — §3.3's headline behaviour, and
the reason this is one `Segment` rather than several (`RowLayout.Compose`, `:140-141`, inserts the
`" [dim]|[/] "` separator *between `Segment`s only*).

Add `SegmentBuilder.BuildCompoundSegment(IReadOnlyList<StyledSpan> spans)` to own step 4, so the
`Spans`/`Plain`/`Markup` invariant from §5.1 is established in exactly one place.

### 5.4 `LeafContent.Decide` must not wipe compound markup

`LeafContent.cs:36-39` currently does:

```csharp
if (resolved.Config.Color is not null && !IsSemantic(resolved.Config))
{
    markup = Spectre.Console.Markup.Escape(text);
}
```

An item-level `color` on a decorative item **discards the item's internal markup** so the outer
colour becomes the sole colour. For a compound that is catastrophic — it would erase every per-part
colour whenever the author also set an item-level default, which is the common case.

Change the guard to exclude compounds:

```csharp
if (resolved.Config.Parts is null && resolved.Config.Color is not null && !IsSemantic(resolved.Config))
```

This is correct rather than a special case: for a compound, item-level `color` is **already
consumed** in §5.3 step 3 as the per-part default. It is not an outer wrap, so there is nothing for
it to replace. The existing comment at `LeafContent.cs:51-55` explains the decorative-replacement
rule; extend it to say a compound has consumed its item-level colour at the part level. Do not
touch `IsSemantic` itself — a compound has no registry entry and the question does not arise.

The `link` branch (`:41-46`) needs **no change** and must keep working: it wraps the whole
compound's markup in one OSC 8 hyperlink, which is exactly §3.3's "`link` stays at item level and
wraps the whole compound". Its `resolved.Value is { } ownValue` guard is satisfied by §5.5.

### 5.5 Suppression — reuse the existing predicate, do not invent one

The existing predicate is `resolved.Value is null` at `PaneAssembler.cs:138`, mirrored at
`PaneCollapse.cs:75` and `SizeResolver.cs:1009`. Because all three read the *same*
`LeafItems.Resolve` output, setting a compound's `Value` correctly makes the whole item suppress as
one unit in the renderer, in the collapse predicate, and in the sizing pass — with **zero changes
to any of those three files**. That is the whole reason this design routes suppression through
`Value`.

`LeafItems.Resolve` must set, for a compound:

- `Value` = the concatenated plain text of the surviving spans (§5.3 step 4's `Plain`), **or `null`
  if that concatenation is empty**.
- `Display` = the `Segment` from §5.3, or `null` under the same condition.

This yields §3.3's rules exactly:

- Every value part empty → all literals adjacent to them drop (step 2), nothing survives, `Value`
  is `null` → **the whole item suppresses as one unit**, never partially.
- A compound of only literals is a constant and renders — §3.3 explicitly permits this.
- An entirely absent `from` source in every part behaves identically to every part resolving empty,
  because `values.GetValueOrDefault` returns `null` for both.

`Value` is also what `LeafContent.TryBuildLink` expands `{}` into (`LeafContent.cs:84`). Setting it
to the concatenated plain text means a compound's `link` template's `{}` is the whole rendered
text. **This is a consequence I am flagging, not a rule §3.3 states.** It is the only defensible
reading — a compound has no single "raw value" — but if the project wants `{}` to be something else
on a compound, that is a product call, and §3.3 would need a sentence.

### 5.6 Width, wrap, and drop as one unit — the property that matters

Trace, confirmed against source:

1. `PaneAssembler.cs:165-167` adds **one** `Segment` per item to `packedGroup`.
2. `RowLayout.PackRow` (`:120-133`) decides row membership on `segments[i].Plain.Length` at `:125`.
3. `RowLayout.WidthOf` (`:137-138`) sums `s.Plain.Length` plus separator widths.
4. `RowLayout` truncates only the last segment on a capped row (`:111`).

A compound is one `Segment` whose `Plain` is the *already-concatenated* text, so **parts are joined
before any overflow, wrap, or drop logic runs, not after**. The label and its value therefore wrap
together, drop together, and are measured together. `agent:` can no longer survive without its
value at 60 columns — which is the layout half of why this feature matters, per the filed defect's
Scope section.

Nothing in `RowLayout`, `SizeResolver`, or `PaneRenderer` needs to change. The `Spans` field is
invisible to all of them. **If a change to any of those three files seems necessary, stop — the
design has been misread, and `Plain` is no longer the sole width metric (§2.4 / Defect 0).**

---

## 6. Resolution set — `src/ClaudeTuiLine/ItemValueResolver.cs`

§3.3 closes with: *"This is the sixth construct that names an item by id … §5's resolution set must
enumerate it, and defect 11 is what happens when it does not."* This section is not optional.

### 6.1 Two new reference extractors

Append to `ReferenceExtractors` (`ItemValueResolver.cs:233-288`) — the file's own comment at
`:231` names this exact work. **Append; do not edit `CollectIds` or `ScanReferences`.**

```csharp
// §3.3: a compound part's `item` selector.
new ReferenceExtractor(
    new[] { typeof(PaneItem).GetProperty(nameof(PaneItem.Parts))! },
    ctx => ctx.Items
        .Where(entry => entry.Item.Parts is not null)
        .SelectMany(entry => entry.Item.Parts!
            .Select((part, i) => (part, i))
            .Where(t => t.part.Item is { Length: > 0 })
            .Select(t => new IdCandidate(t.part.Item!, $"{entry.Path}/parts/{t.i}/item",
                ReferenceKind.Reference, ReferenceForm.ItemSelector)))),

// §3.3: a compound part's `from`.
new ReferenceExtractor(
    new[] { typeof(PaneItem).GetProperty(nameof(PaneItem.Parts))! },
    ctx => ctx.Items
        .Where(entry => entry.Item.Parts is not null)
        .SelectMany(entry => entry.Item.Parts!
            .Select((part, i) => (part, i))
            .Where(t => t.part.From is { Length: > 0 })
            .Select(t => new IdCandidate(t.part.From!, $"{entry.Path}/parts/{t.i}/from",
                ReferenceKind.Reference, ReferenceForm.DerivedFrom)))),
```

**No new `ReferenceForm` value.** Reusing `ItemSelector` and `DerivedFrom` is deliberate and gives
the right §9.4.1 severities for free: both delete the fragment outright, both are `error`
(`unknown-item-id`), and `DerivedFrom` additionally brings `from-derived-source` — which is exactly
right, since a part's `from` naming a derived item is the same §3.2.1 violation one level in. §3.3
confirms the reuse (`SPEC-V2-FRAMEWORK.md:2734-2736`).

Both extractors hang off `PaneItem.Parts` as their `Members`. `SPEC-V2-FRAMEWORK.md:5219` notes
that §3.3's form previously "has no member to hang a coverage test on" — after §3.2 of this
document, it does, and §9.5.1's structural coverage test can see it.

### 6.2 Compound items declare an id but are not derived items

`ScanReferences` (`:396-414`) classifies a self-declared id as derived (`From` non-empty) or command
(`Command` non-empty). A compound is **neither**, and must not be added to `DerivedItemIds`.

Reason, and it is load-bearing: `DerivedItemIds` is what makes a `from` pointing at that id raise
`from-derived-source`. A compound item's id is a legitimate `from` source for nothing — but it is
also not a *derived* item, and mislabelling it would produce a misleading diagnostic. Leave the
classification alone; `Parts` simply does not participate.

Separately: a compound's id must **not** be resolvable as a source. A part's `from`, or a top-level
derived item's `from`, naming a compound item has no value to read — `ResolveDerived`
(`:433-464`) never writes a value for it, so it resolves to `null` and the referencing item
suppresses silently. **This is a NEEDS-EVIDENCE item — see §9, E2.**

### 6.3 Part sources are resolved at render time, not in `ResolveDerived`

Do **not** extend `ResolveDerived` (`:433-464`). It keys results by item id and a part has no id, so
there is nowhere to put the result. Instead, §6.1's extractors ensure every part source id is in
`CollectIds`'s output and therefore has a value in the dictionary, and §5.3 step 1 applies
`extract`/`case`/`format` per part at display time.

This preserves `ResolveDerived`'s chaining guarantee (`:425-432`): a part's `from` reads the
pre-derived snapshot the same as any derived item, so a part can never observe another derived
item's result regardless of declaration order.

---

## 7. Documentation

### 7.1 `SPEC-V2-FRAMEWORK.md` §3.3 (line 2657)

The behavioural text is correct and must **not** be rewritten. Add, after the worked example (after
line 2673):

- A short "Implemented as of SPEC-85" note citing `Config.cs` (`PaneItemPartJsonConfig`),
  `LeafItems.BuildCompound`, `SegmentTruncation.RestyleSlice`, and `ConfigCheck.CheckCompoundParts`.
- **The discriminator ruling from §2 of this document, stated explicitly**: a compound item is
  recognised by the presence of `parts`; there is no `kind` key; writing `"kind":"compound"`
  produces `unknown-key`. Without this sentence the filed defect gets re-filed.

### 7.2 §9.6.1 (lines 5413-5414)

The two rows are already correct. No change to the table. If the registry carries any
implemented/unimplemented marking convention elsewhere, apply it — otherwise leave both rows alone.

### 7.3 §9.6.2 (line 5437 onward)

The `kinds` block (line ~5458) is already correct and must not change — it is the contract this
work makes true. Add one sentence to the "Why `kinds` is a section and not a column" discussion
(line ~5529, which already names "§3.3's `parts`"): **an item's kind is inferred from its
distinguishing key — `item`, `command`, `from`, or `parts` — and is never declared.**

### 7.4 `STATUS.md`

Line 126 lists compound items as outstanding; line 607 records that both codes are "in §9.6.1's
registry". Update both to reflect shipped state, and log the change per the repo's existing
STATUS.md convention.

### 7.5 Out of scope

`/claude-tui-line:edit`'s translation table still recommends the two-derived-items workaround. It
should eventually prefer a compound for the label+value case. **Do not change it in this work** —
file it as a follow-up so this change stays reviewable.

---

## 8. What must NOT change

1. **`Segment.Plain.Length` remains the sole width metric.** No caller may measure via `Spans`.
2. **`RowLayout.cs`, `SizeResolver.cs`, `PaneRenderer.cs`, `PaneCollapse.cs`, `Compositor.cs`** —
   zero changes. A diff touching any of them means the design was misread.
3. **`Segment` gains only a trailing optional parameter.** Every existing `new Segment(...)` and
   every existing `SegmentBuilder` call compiles and behaves identically.
4. **`PaneItem` gains only a trailing optional positional member**, after `MaxLines`.
5. **`RestyleSlice` with `Spans == null` is byte-for-byte today's `Restyle`.** Non-compound
   truncation and wrapping must be provably unchanged.
6. **No new diagnostic code strings.** Only the two already in §9.6.1's registry
   (`part-source-count`, `part-forbidden-key`) plus existing ones. §9.6.1 is the registry and a code
   not in it does not exist.
7. **`--items --json`'s output is unchanged.** `ItemsCommand.cs:27,49` already describe the shipped
   shape; `tests/ClaudeTuiLine.Tests/ItemsCommandTests.cs:28-29` must still pass untouched.
8. **`SafeCutIndex`'s surrogate-pair handling** (`SegmentTruncation.cs:78-81`) stays on every cut
   path, including the new one.
9. **No unbalanced SGR may ever be emitted** by the new slicing path.

---

## 9. NEEDS-EVIDENCE

I could not run anything. Each item below states what to run and what each outcome decides. Route
these to the Implementor and re-dispatch me with results if any answer forces a design change.

**E1 — Does the §9.5.1 reference-coverage test actually catch the new member?**
Run: add `Parts` to `PaneItem` (§3.2) **without** §6.1's extractors, then run the test suite.
- If a coverage test **fails** naming `PaneItem.Parts` → the safety net works; add §6.1 and it goes
  green. Design unchanged.
- If nothing fails → the coverage test does not cover this member shape, and §6.1's extractors are
  unguarded. Report back; the test itself may need extending, which is in scope for this work.

**E2 — What happens today when a `from` names an id that has no resolved value?**
Run: a config with a derived item whose `from` names a *compound* item's id, then `--check` and a
render.
- If `--check` reports `unknown-item-id` → §6.2 needs no work; a compound's id is correctly not a
  valid source.
- If it passes `--check` clean and renders nothing → there is a silent hole, and §6.2 needs an
  explicit rule (most likely: add compound ids to a set that `from`/argv references reject, mirroring
  `CommandItemIds`). **Re-dispatch me if this is the outcome** — it needs a diagnostic code
  decision, which touches §9.6.1's registry.

**E3 — What does `SegmentTruncation.Truncate` do with the ellipsis today?**
Read `SegmentTruncation.cs:1-48` (a read, not an experiment). §5.2 assumes the ellipsis is appended
outside the styled region. If it is instead folded into the restyled markup, §5.2's last paragraph
needs rewriting.

**E4 — Baseline: does a composite-markup builtin lose its colour when truncated today?**
Run: render `context` in a pane narrow enough to truncate it, with ANSI captured.
- Confirms §1.1's reading of `TryGetSimpleWrap` against real output, and establishes whether the
  pre-existing builtin degradation is real (worth filing separately) or whether some other path
  already handles it — in which case §5.2 may be able to reuse that path instead.

**E5 — Does an empty `parts: []` currently round-trip?** After §3.1, run `--check` on
`{"id":"x","parts":[]}` and confirm it produces §4.2's `part-source-count` error rather than
binding to `null` and being treated as a non-compound item. This is the difference between
`Parts is not null` and `Parts is { Count: > 0 }` as the compound predicate (§3.2).

---

## 10. Verification

Maps one-to-one onto the filed defect's "Verification (once fixed)" list.

1. **Schema/checker agreement** (defect item 1). `--items --json | jq .kinds.compound`, build a
   config using exactly `required + optional`, run `--check --json`. Expect
   `{"ok":true,"diagnostics":[]}` — no `unknown-key`.
2. **The §3.3 worked example renders as one item** (defect item 2). Enter
   `{"id":"agent-badge","parts":[{"text":"agent:","color":"grey"},{"from":"agent","extract":"[^:]+$","case":"upper","color":"aqua"}]}`
   verbatim. Assert on the raw markup: `agent:` grey, the agent name aqua, **no `|` separator
   between them**, and one item's worth of width.
3. **Whole-item suppression** (defect item 3). Same config with the `agent` source absent. Assert
   the item contributes **zero** cells and no bare `agent:` survives — and that the pane's row count
   matches a config with the item removed entirely.
4. **Discriminator** (defect item 4). `--check` on the same item with `"kind":"compound"` added.
   Expect exactly one `unknown-key` warning on `.../kind`, `ok:true`, and the item rendering
   correctly regardless. This is the documented outcome of §2, asserted so it cannot silently drift.
5. **Drop-as-one-unit.** Render the `agent-badge` config at a width where the two-derived-items
   workaround drops the value and keeps the label. Assert the compound drops or wraps *both*
   fragments together. This is the regression test for the property the whole feature exists for.
6. **Truncation keeps per-part colour** — §3.3 requires this test by name. Truncate a compound
   mid-second-span. Assert: the first span's markup is intact and coloured, the emitted markup is
   balanced (no unclosed tag), and `Plain.Length` equals the width budget.
7. **Wrap keeps per-part colour.** Same, through `WrapToWidth`, across a row boundary — §2.6's
   trap 2 (style re-emitted on every continuation row) applies per span.
8. **Literal adjacency, both directions.** `[{"text":"("},{"from":"pr"},{"text":")"}]` with `pr`
   absent renders **nothing** — not a bare `(`. §3.3 calls this out specifically; it is the
   render-wrong class.
9. **Literals-only compound is a constant** and renders.
10. **Diagnostics.** A part with zero sources, a part with two, a part with nested `parts`, and a
    part with `link` — each produces its documented code at `error`, with the JSON Pointer
    `.../items/N/parts/M`. A part naming an unknown id gives `unknown-item-id`; an unknown colour
    gives `unknown-color`; a bad `case` gives `unknown-enum-value`.
11. **`Segment` invariant** (§5.1): for every compound segment, concatenated span `Plain` equals
    `Segment.Plain` and concatenated span `Markup` equals `Segment.Markup`.
12. **No regression.** Full suite green, `ItemsCommandTests.cs:28-29` untouched, and
    `tools/check-examples.sh` / `check-citations.sh` / `check-counts.sh` pass after the §7 doc edits.

---

## 11. Risk and confidence

**Confidence: high on §§2, 3, 4, 6, and 5.3–5.6.** Those are mechanical translations of §3.3's
already-settled semantics onto source locations verified by reading, and the suppression and
width properties fall out of existing machinery rather than needing new machinery.

**Confidence: medium on §5.1–5.2 — the `Segment.Spans` + `RestyleSlice` change.** It touches
`Segment`, a type used everywhere, and the truncation path. The mitigations are real: the parameter
is trailing and optional, and the `Spans == null` path is byte-for-byte today's code. But it is the
riskiest part of this change and the place to concentrate review.

**Escalation — I recommend the Ultra-Advisor be asked one question before §5.1 is implemented:**

> `SegmentTruncation.RestyleSimple` degrades any composite-markup segment to unstyled text when
> cut. §3.3 requires compound items to keep per-part colour through truncation. Is adding an
> optional `Spans` decomposition to `Segment` the right seam, or should truncation instead become
> span-aware by construction (i.e. `Segment` *is* a span list, with `Markup` derived) — accepting a
> larger refactor that would also fix the same latent degradation in composite builtins?

I have specified the additive option because it is reversible and its blast radius is bounded, and
because §3.3 says compounds "do not add machinery". But §3.3 also asserts "`Segment` already holds
multiple styled spans", which is **not true of the current code** — `Segment` holds a markup
*string*. That gap between the spec's mental model and the implementation is exactly the kind of
thing worth one expensive opinion before building on it. If the answer is "make `Segment` a span
list", §5 is rewritten and §§3, 4, 6 are unaffected.

**Decisions I made that are properly the user's**, flagged rather than buried:

- §4.4 — kind collision (`parts` + `from`/`command`/`item`) as a *warning* via the existing
  `key-not-applicable` rather than a new error code.
- §5.5 — a compound's `link` template `{}` expands to the whole concatenated rendered text.
- §7.5 — leaving `/claude-tui-line:edit`'s translation table pointing at the workaround, as a
  follow-up rather than part of this change.
