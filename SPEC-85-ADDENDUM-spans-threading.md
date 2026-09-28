# SPEC-85 §12 — Addendum: Spans threading, outer-wrap ordering, and `from-compound-source`

**This file is an addendum to `SPEC-85-compound-item-implementation.md` and is part of that spec.**
It is a separate file only because the Architect role has no `Edit` tool, and reproducing the
757-line parent document through `Write` would risk silent transcription damage to a document the
repo's own `tools/check-citations.sh` validates. **Whoever next edits the parent spec should append
this content as its §12 (or link it from §11) — nothing here contradicts §§1–11 except where
explicitly marked "AMENDS".**

Everything below was verified by reading the working tree as of this session (uncommitted §§3–6
implementation present). Nothing here is hypothesised.

---

## 12.1 What is actually broken

Three defects, one root cause: the `Spans` decomposition is produced correctly and consumed
correctly, but nothing connects the producer to the consumer, and the two transformations that sit
between them were never taught about it.

### D-A — `Spans` is unreachable from the render pipeline (the primary defect)

- `LeafItems.BuildCompound` (`src/ClaudeTuiLine/LeafItems.cs:70-172`) correctly returns a
  `Segment` carrying `Spans` (via `SegmentBuilder.BuildCompoundSegment`,
  `src/ClaudeTuiLine/SegmentBuilder.cs:100-101`).
- `LeafContent.ItemDecision` (`src/ClaudeTuiLine/LeafContent.cs:18`) is
  `(string Text, string Markup)` — **it has no `Spans` member**, so `LeafContent.Decide`
  (`:31-49`) reads `resolved.Display!.Plain` and `.Markup` and drops `.Spans` on the floor.
- `PaneAssembler.cs:165-167` rebuilds the production `Segment` from the decision via
  `SegmentBuilder.BuildItemSegment(decision.Text, decision.Markup, color)` — the three-argument
  overload (`SegmentBuilder.cs:89-92`), which always constructs `new Segment(markup, plain)` with
  `Spans == null`.

Consequence: every `Segment` that reaches `RowLayout`/`SegmentTruncation` has `Spans == null`, so
`SegmentTruncation.Truncate`'s span branch (`SegmentTruncation.cs:26-29`) and
`RestyleSlice`'s span branch (`:91-119`) are dead code in production. §5.2's whole purpose —
per-part colour surviving truncate/wrap — does not happen. The unit tests pass because they
construct `Segment`s with `Spans` by hand.

### D-B — the outer item-colour wrap double-applies to compounds

`PaneAssembler.cs:144` computes `color = ColorResolution.Resolve(resolved.Config.Color, values, tokens)`
unconditionally and `:166` wraps every item's markup in `[{color}]…[/]`.

For a compound this is wrong twice over:

1. **Semantically.** `LeafItems.BuildCompound:153` already resolves `part.Color ?? item.Color` per
   part. The item-level colour is *consumed* as the per-part default. §5.4 states the rationale in
   the parent spec and the code comment at `LeafContent.cs:55-57` repeats it — "there is no outer
   wrap left to apply here" — yet `PaneAssembler` applies one anyway. An author who writes
   `"color":"grey"` on the item and `"color":"aqua"` on one part gets `[grey]` wrapped around
   markup whose spans already carry `[grey]`/`[aqua]`.
2. **Structurally.** It puts markup *outside* the span decomposition, breaking §5.1's invariant
   (`concat(span.Markup) == Segment.Markup`) the instant `Spans` is threaded through.

### D-C — the `link` wrap also sits outside the decomposition

`LeafContent.Decide:41-46` does `markup = OscHyperlink.Wrap(url, markup)`. This must keep working
(§3.3: `link` wraps the whole compound), but it likewise puts bytes outside `concat(span.Markup)`.

### D-D and D-E — two pre-existing bugs in the uncommitted `TruncateSpans`

Found while designing this; they are independent of D-A and must be fixed in the same change,
because D-A's fix is what first makes them reachable.

- **D-D — `TruncateSpans` violates §5.1's invariant it was written to uphold.**
  `SegmentTruncation.cs:80` returns
  `new Segment(styledContent.Markup + escapedEllipsis, styledContent.Plain + ellipsis, styledContent.Spans)`.
  `Markup` and `Plain` both include the ellipsis; `Spans` does not. §10.11's invariant test would
  fail on any truncated compound — today it does not fail only because D-A means no compound
  segment ever reaches this code.
- **D-E — `TruncateSpans` has no OSC 8 link handling at all.**
  The non-span `Truncate` path takes deliberate care (`:45-56`) to close a link *before* the
  ellipsis, citing "§3.2 rule 3 / ruling d: clicking '…' must never navigate". `TruncateSpans`
  (`:62-81`) has no `TryUnwrap` call, so a linked compound would either lose its link entirely or
  (worse) leave the ellipsis inside the clickable region. Same for `RestyleSlice`'s span branch
  (`:91-119`), which reassembles purely from spans and silently discards any outer wrap present on
  `original.Markup`.

---

## 12.2 The design, in one paragraph

Keep §5.1's philosophy (additive, `Spans == null` for everything non-compound, byte-for-byte
unchanged elsewhere) and **do not add a second field to `Segment`**. Instead:

- **Thread `Spans` through `ItemDecision`** as a trailing optional member.
- **Delete the outer colour wrap for compounds** rather than trying to preserve it through slicing.
  This is not a workaround — §5.4 already argues the wrap is semantically wrong for a compound. It
  removes D-B's structural problem instead of building machinery around it.
- **Keep the `link` wrap, and revise §5.1's invariant to hold *modulo an OSC 8 unwrap*.** This is
  not a new concept: `SegmentTruncation.Restyle` (`:166-176`) already treats an OSC 8 wrap as
  living strictly outside a segment's style markup, unwrapping and re-wrapping around it. The
  revised invariant states exactly the property `Restyle` already relies on.
- **Make the one construction site structurally incapable of breaking the invariant**, by having
  `SegmentBuilder.BuildItemSegment` clear `Spans` whenever it applies a colour wrap.

Net: one new optional parameter on `ItemDecision`, one on a `SegmentBuilder` overload, three
touched call sites, and two bug fixes in `SegmentTruncation`. No new types, no new `Segment` field.

---

## 12.3 AMENDS §5.1 — the revised `Spans` invariant

Replace §5.1's invariant paragraph with:

> **Invariant (revised, §12.3).** When `Spans` is non-null, let
> `styleMarkup = OscHyperlink.TryUnwrap(Markup, out _, out var inner) ? inner : Markup`. Then:
>
> - `string.Concat(Spans.Select(s => s.Plain)) == Plain`, and
> - `string.Concat(Spans.Select(s => s.Markup)) == styleMarkup`.
>
> That is: `Spans` decomposes the segment's **style markup**, and an OSC 8 hyperlink is understood
> to wrap that style markup from outside rather than to participate in it. This is the same
> layering `SegmentTruncation.Restyle` (`SegmentTruncation.cs:159-176`) already documents and
> depends on — a link "wraps a segment's *style* markup, not its Plain text".
>
> No other decoration may be placed outside the decomposition. In particular a `[color]…[/]` wrap
> may **not** be applied to a segment that carries `Spans`; any code path that applies one must
> clear `Spans` (see §12.6).
>
> `Spans` remains a decomposition, never an alternative source of truth. Width is `Plain.Length`,
> unconditionally.

**Why the invariant is weakened rather than the link removed.** §3.3 requires `link` to wrap the
whole compound, and the OSC 8 sequence contributes zero cells to `Plain`, so it cannot be modelled
as a span without either (a) inventing zero-width spans with bespoke boundary rules — which would
make it easy for a slice to drop the closing sequence and emit an unbalanced OSC 8, the precise
thing §8.9 forbids — or (b) adding a second field to `Segment`, which §8.3 rules out. Reusing the
unwrap/re-wrap idiom the file already uses is strictly less new machinery than either.

**Alternative considered and rejected: clear `Spans` whenever a `link` is applied.** It keeps the
original invariant verbatim and is two lines. Rejected because the resulting behaviour is a latent
bug report — "my compound's per-part colours work until I add a `link`, then they vanish as soon as
the pane gets narrow" — and because `RestyleSlice` and `TruncateSpans` need `TryUnwrap` anyway to
fix D-E.

---

## 12.4 `LeafContent` — `src/ClaudeTuiLine/LeafContent.cs`

### 12.4.1 `ItemDecision` gains a trailing optional member

Replace `LeafContent.cs:18`:

```csharp
    /// <param name="Spans">
    /// SPEC-V2-FRAMEWORK.md §3.3: a compound item's per-part decomposition, carried through to the
    /// <see cref="Segment"/> the assembler builds so truncation and wrapping can preserve per-part
    /// colour (SPEC-85 §5.2). Null for every non-compound item, and cleared by any transformation
    /// here that puts markup outside the decomposition (SPEC-85 §12.3).
    /// </param>
    public readonly record struct ItemDecision(string Text, string Markup, IReadOnlyList<StyledSpan>? Spans = null);
```

`ItemDecision` is constructed in exactly one place (`LeafContent.cs:48`) and named in exactly one
other (`:31`); a trailing defaulted positional member changes no other source line. Verified by
`grep -rn 'ItemDecision' src/ tests/ --include=*.cs` → 3 matches, all in `LeafContent.cs`.

### 12.4.2 `Decide` reads and forwards `Spans`

Replace `LeafContent.cs:31-49` with:

```csharp
    public static ItemDecision Decide(LeafItems.ResolvedItem resolved, IReadOnlyDictionary<string, string?> values)
    {
        var text = resolved.Display!.Plain;
        var markup = resolved.Display!.Markup;
        var spans = resolved.Display!.Spans;

        if (resolved.Config.Parts is null && resolved.Config.Color is not null && !IsSemantic(resolved.Config))
        {
            markup = Spectre.Console.Markup.Escape(text);
            spans = null;
        }

        if (resolved.Config.Link is { Length: > 0 } linkTemplate
            && resolved.Value is { } ownValue
            && TryBuildLink(linkTemplate, ownValue, values, out var url))
        {
            markup = OscHyperlink.Wrap(url, markup);
        }

        return new ItemDecision(text, markup, spans);
    }
```

Two things to note, both deliberate:

- **`spans = null` in the decorative-replacement branch.** That branch discards the segment's
  internal markup wholesale, so any decomposition of it is stale. The branch is already guarded by
  `Parts is null` (§5.4) and only a compound produces `Spans` today, so this line is currently
  unreachable — it is there so the invariant cannot be broken later by a builtin that starts
  emitting `Spans`. Do not remove it as dead code.
- **`spans` is *not* cleared by the link branch.** Under §12.3's revised invariant the OSC 8 wrap
  is outside the decomposition by definition, and `TryUnwrap` recovers `concat(span.Markup)`
  exactly. This is the change that makes D-C safe.

### 12.4.3 Comment update

The block comment at `LeafContent.cs:51-57` already explains the compound carve-out correctly and
needs no change.

---

## 12.5 `PaneAssembler` — `src/ClaudeTuiLine/PaneAssembler.cs`

### 12.5.1 Compounds skip the outer colour wrap (resolves D-B)

**Decision: yes, a compound skips the item-level colour wrap entirely. The condition is
`resolved.Config.Parts is not null`** — `resolved.Config` is the `PaneItem`, so compound-ness is
already visible at this call site with no plumbing.

After `PaneAssembler.cs:144`, add:

```csharp
            // SPEC-85 §5.4/§12.5: a compound has already consumed its item-level colour as the
            // per-part default (LeafItems.BuildCompound), so wrapping one around it here would
            // both double-apply the colour and place markup outside the span decomposition
            // §12.3's invariant requires stay intact.
            var itemColor = resolved.Config.Parts is null ? color : null;
```

Then replace `PaneAssembler.cs:165-167` with:

```csharp
            var singleLine = lines[0];
            packedGroup.Add(singleLine == decision.Text
                ? SegmentBuilder.BuildItemSegment(decision.Text, decision.Markup, itemColor, decision.Spans)
                : SegmentBuilder.BuildItemSegment(singleLine, color));
```

### 12.5.2 The two paths that intentionally keep `color` and drop `Spans`

Both remaining uses of `color` in this method stay **exactly as they are** — they use `color`, not
`itemColor`:

- **`PaneAssembler.cs:152` (the multi-line block path).** `SplitBlockLines` returned more than one
  line, and this path discards `decision.Markup` entirely and rebuilds each line from plain text.
  Per-part colour is unrecoverable there regardless, so the item-level colour is the only colour
  left and applying it uniformly is the closest surviving approximation of intent. A compound whose
  parts contain embedded newlines therefore renders as N uniformly-coloured rows with no per-part
  colour. **This is accepted, documented behaviour, not a defect** — record it in §7.1's doc note.
- **`PaneAssembler.cs:167`'s else-branch** (`singleLine != decision.Text`, i.e. a stripped trailing
  newline, rule D2). The two-argument overload produces `Spans == null` by construction, which is
  *required*: the stripped `Plain` is shorter than the one the spans decompose, so the spans no
  longer satisfy §12.3's invariant. Do not "improve" this by forwarding `decision.Spans`.

### 12.5.3 `SizeResolver` must not change

`SizeResolver` also calls `LeafContent.Decide`, but its output is a width, and width is
`Plain.Length` (§8.1). Neither the dropped colour wrap nor the added `Spans` changes `Text`, so the
measurement pass and the render pass cannot disagree. **`SizeResolver.cs` gets zero changes**
(§8.2). Verification step in §12.9 item 6 asserts this by inspection.

---

## 12.6 `SegmentBuilder` — `src/ClaudeTuiLine/SegmentBuilder.cs`

Replace the three-argument overload at `SegmentBuilder.cs:83-92` with a four-argument form (the
new parameter is trailing and defaulted, so every existing call site compiles and behaves
identically):

```csharp
    /// <summary>
    /// Builds one item segment from its own already-tagged markup, with an optional outer colour
    /// wrapped around it. SPEC-V2-FRAMEWORK.md §6: a config <c>color</c> nests around an item's
    /// internal markup rather than replacing it — Spectre gives the inner tags their own span and
    /// leaves the outer colour to claim whatever text they don't.
    /// <paramref name="spans"/> is a compound item's per-part decomposition (SPEC-85 §5.1). A
    /// colour wrap places markup outside that decomposition, so applying one clears
    /// <paramref name="spans"/> rather than emitting a segment that violates §12.3's invariant;
    /// callers that need both must not pass a colour (see SPEC-85 §12.5.1).
    /// </summary>
    public static Segment BuildItemSegment(string plain, string markup, string? color, IReadOnlyList<StyledSpan>? spans = null) =>
        string.IsNullOrEmpty(color)
            ? new Segment(markup, plain, spans)
            : new Segment($"[{color}]{markup}[/]", plain, null);
```

The `null` in the coloured branch is the structural guarantee: **there is no way to construct a
`Segment` from this builder whose `Markup` carries a colour wrap outside its `Spans`.** It is
deliberately silent rather than throwing — this is a render path, and §12.5.1 already ensures the
combination never arises in production.

`BuildCompoundSegment` (`:100-101`) and the two-argument `BuildItemSegment` (`:73-81`) are
unchanged.

---

## 12.7 `SegmentTruncation` — `src/ClaudeTuiLine/SegmentTruncation.cs` (fixes D-D, D-E)

### 12.7.1 `RestyleSlice` must preserve an outer link

Replace `SegmentTruncation.cs:89-124` with:

```csharp
    internal static Segment RestyleSlice(Segment original, int start, int end)
    {
        if (original.Spans is not { } spans)
        {
            return Restyle(original, original.Plain[start..end]);
        }

        // §12.3: an OSC 8 link wraps the style markup from outside the decomposition, so it is
        // unwrapped before slicing and re-applied after — the same layering Restyle uses, and
        // what re-opens the link on every continuation row when WrapToWidth chunks a segment.
        var linked = OscHyperlink.TryUnwrap(original.Markup, out var url, out _);

        var surviving = new List<StyledSpan>();
        var offset = 0;
        foreach (var span in spans)
        {
            var spanStart = offset;
            var spanEnd = offset + span.Plain.Length;
            offset = spanEnd;

            if (spanEnd <= start || spanStart >= end)
            {
                continue;
            }

            if (spanStart >= start && spanEnd <= end)
            {
                surviving.Add(span);
                continue;
            }

            var sliceStart = Math.Max(start, spanStart) - spanStart;
            var sliceEnd = Math.Min(end, spanEnd) - spanStart;
            var restyled = Restyle(new Segment(span.Markup, span.Plain), span.Plain[sliceStart..sliceEnd]);
            surviving.Add(new StyledSpan(restyled.Plain, restyled.Markup));
        }

        if (surviving.Count == 0)
        {
            // Nothing survives: emit a bare empty segment rather than an empty link or an empty
            // colour wrap. §8.9 — no decoration may be emitted around no text.
            return new Segment(string.Empty, string.Empty);
        }

        var plain = string.Concat(surviving.Select(s => s.Plain));
        var styleMarkup = string.Concat(surviving.Select(s => s.Markup));
        return new Segment(linked ? OscHyperlink.Wrap(url, styleMarkup) : styleMarkup, plain, surviving);
    }
```

Behaviour notes, all load-bearing:

- The `original.Spans is null` early return is untouched: **`RestyleSlice` on a non-compound
  segment remains byte-for-byte today's `Restyle`** (§8.5).
- The severed-span cut at the bottom of the loop calls `Restyle` on a *single-span* `Segment`,
  which itself handles a per-span link and otherwise degrades that one span through
  `RestyleSimple`. Unchanged from the current implementation; §5.2 rule 3 still holds and the
  degradation is still bounded to one fragment.
- `Restyle`/`RestyleSimple` always emit a balanced `[c]…[/]` or nothing, and `OscHyperlink.Wrap`
  always emits both halves, so no cut can produce an unbalanced sequence (§8.9).
- The empty case now returns a genuinely empty `Segment` rather than one whose `Markup` might be
  an empty link. This changes `WrapToWidth(segment, 0)`'s compound behaviour only.

### 12.7.2 `TruncateSpans` — invariant and link handling

Replace `SegmentTruncation.cs:59-81` with:

```csharp
    // Span-aware counterpart of Truncate's body above, for a compound segment (Spans != null).
    // §5.2/§12.7: the ellipsis is appended outside the last surviving span, unstyled, and — when
    // the compound carries an item-level link — outside the link too, so the marker is never
    // clickable (§3.2 rule 3 / ruling d, matching Truncate's non-span path at :50-56).
    private static Segment TruncateSpans(Segment segment, int innerWidth, string ellipsis)
    {
        if (innerWidth <= 0)
        {
            return RestyleSlice(segment, 0, 0);
        }

        if (!MarkerFits(innerWidth, ellipsis))
        {
            var hardCut = SafeCutIndex(segment.Plain, Math.Min(innerWidth, segment.Plain.Length));
            return RestyleSlice(segment, 0, hardCut);
        }

        var contentBudget = innerWidth - ellipsis.Length;
        var cutIndex = SafeCutIndex(segment.Plain, Math.Min(contentBudget, segment.Plain.Length));
        var styledContent = RestyleSlice(segment, 0, cutIndex);
        var escapedEllipsis = Spectre.Console.Markup.Escape(ellipsis);
        var newMarkup = styledContent.Markup + escapedEllipsis;
        var newPlain = styledContent.Plain + ellipsis;

        // The ellipsis sits outside any link the content carries, so the result is no longer a
        // link-wrapped decomposition and cannot satisfy §12.3's invariant — a truncated segment is
        // terminal (nothing slices it again), so it drops its Spans instead. With no link the
        // ellipsis is just one more unstyled span and the decomposition survives intact.
        if (styledContent.Spans is not { } contentSpans || OscHyperlink.TryUnwrap(styledContent.Markup, out _, out _))
        {
            return new Segment(newMarkup, newPlain);
        }

        var spans = new List<StyledSpan>(contentSpans) { new(ellipsis, escapedEllipsis) };
        return new Segment(newMarkup, newPlain, spans);
    }
```

This is the fix for **D-D** (the ellipsis is now a span, so `concat(span.Plain) == Plain` and
`concat(span.Markup) == Markup`) and for **D-E** (a linked compound's marker is outside the link,
matching the non-span path's stated rule).

`Truncate` (`:20-57`), `WrapToWidth` (`:129-147`), `SafeCutIndex`, `Restyle`, `RestyleSimple`, and
`TryGetSimpleWrap` are **unchanged**.

---

## 12.8 E2 — `from` naming a compound item

### 12.8.1 The decision

**A `from` (item-level or part-level) whose target id is a compound item is an `error`-severity
config diagnostic under a new code, `from-compound-source`.**

Rationale, in order of weight:

1. **It can never work, statically.** A compound item never writes a value into the resolution
   dictionary: `ItemValueResolver.ResolveDerived` (`ItemValueResolver.cs:455-478`) writes only for
   items with a non-empty `From`, builtins write via the registry, and command items write their
   stdout. `Parts` participates in none of those. So `values.GetValueOrDefault(compoundId)` is
   `null` in every possible run. This is categorically unlike ordinary silent suppression, which is
   runtime-conditional (the branch is absent *today*, the PR is absent *today*).
2. **Exact precedent exists at the same severity.** `from-derived-source`
   (`ConfigCheck.cs:122-126`) is the same shape — a statically-impossible `from` target — and is
   `error`. `placeholder-derived-source` (`:127-131`) and `placeholder-command-source` (`:137-141`)
   follow the identical pattern.
3. It today passes `--check --json` clean, because `ConfigCheck.cs:108` treats an id as `known`
   when `scan.SelfDeclaredIds.Contains(reference.Id)`, and a compound item does self-declare its
   id. Confirmed empirically by the Implementor (`ok:true, diagnostics:[]`, renders nothing).

**Rejected alternatives:**

- *Reuse `from-derived-source`.* Requires adding compound ids to `DerivedItemIds`, which §6.2
  forbids for a good reason — that set also drives `placeholder-derived-source` and would
  mislabel the compound everywhere. And the message would call a compound "a derived item".
- *`key-not-applicable`, warning.* Wrong meaning (§9.6.1: "a known key with a legal value on a node
  that never reads it") and wrong severity — here the key is read and can never succeed.
- *No diagnostic.* Rejected: this is exactly the class of silent-render-nothing hole §6.2 was
  written to close, and the Implementor's E2 run proves it is reachable from a clean `--check`.

### 12.8.2 `ItemValueResolver.ScanReferences` — a new id set

In `src/ClaudeTuiLine/ItemValueResolver.cs`, in the classification loop at `:418-436`:

```csharp
        var derivedItemIds = new HashSet<string>(StringComparer.Ordinal);
        var commandItemIds = new HashSet<string>(StringComparer.Ordinal);
        var compoundItemIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in items)
        {
            if (entry.Item.Id is not { Length: > 0 } ownId)
            {
                continue;
            }

            if (entry.Item.From is { Length: > 0 })
            {
                derivedItemIds.Add(ownId);
            }

            if (entry.Item.Command is { Count: > 0 })
            {
                commandItemIds.Add(ownId);
            }

            // §3.3/§12.8: a compound declares an id but produces no scalar value, so nothing may
            // source `from` it. Deliberately NOT folded into derivedItemIds — §6.2.
            if (entry.Item.Parts is not null)
            {
                compoundItemIds.Add(ownId);
            }
        }
```

`ReferenceScan` gains one member, `CompoundItemIds`, declared **immediately after
`CommandItemIds`** so the record reads in the same order as the loop above. Update the single
construction site, `ItemValueResolver.cs:444`, to pass `compoundItemIds` in that position. There is
exactly one `new ReferenceScan(` site; readers use named properties, so no other line changes.

Note the predicate is `Parts is not null`, matching §3.2 — an explicitly empty `parts: []` is still
a compound (and separately a `part-source-count` error), and must not be treated as a valid `from`
source just because it declared no parts.

### 12.8.3 `ConfigCheck.CheckReferences` — the new branch

In `src/ClaudeTuiLine/ConfigCheck.cs`, insert a new `else if` into the chain in `CheckReferences`
**immediately before** the existing `from-derived-source` branch at `:122`:

```csharp
            else if (reference.Form == ReferenceForm.DerivedFrom && scan.CompoundItemIds.Contains(reference.Id))
            {
                yield return new Diagnostic(reference.Path, DiagnosticSeverity.Error, "from-compound-source",
                    $"'{reference.Id}' is a compound item; a compound item has no single value for 'from' to read");
            }
```

Placement is load-bearing, not cosmetic:

- It goes **inside** the existing `else` chain (i.e. after the `if (!known)` branch), so an id that
  does not exist at all still reports `unknown-item-id` and only that.
- It goes **before** `from-derived-source` so that an item carrying both `parts` and `from` — where
  §4.4 rules that `parts` wins — reports the compound reason rather than the derived one. Exactly
  one diagnostic is emitted per pointer, and it names the kind that actually decided the behaviour.

**Message and pointer conform to the file's conventions**, checked against its neighbours:

- Shape `'{id}' is a <kind>; <why the rule exists>` matches `from-derived-source` ("`'{id}' is
  itself a derived item; a derived item cannot source from another derived item`") and
  `placeholder-command-source` ("`'{id}' is a command item; a command item's argv placeholder may
  not name another command item`").
- Lower-case, no trailing period, single quotes around identifiers and key names — as everywhere
  else in the file.
- Pointer is `reference.Path`, which the extractors already set correctly for both positions:
  `{itemPath}/from` for an item-level `from`, and `{itemPath}/parts/{i}/from` for a part's `from`
  (§6.1). **Both cases are covered by this one branch at no extra cost** — that is the payoff of
  §6.1's decision to reuse `ReferenceForm.DerivedFrom` for part `from`s.

### 12.8.4 AMENDS §7.2 and §8.6 — the registry grows by one row

§8.6 currently reads "**No new diagnostic code strings.**" That constraint is amended: this change
adds exactly one, `from-compound-source`. §7.2 currently says §9.6.1's table needs no change; it
now does.

Add to `SPEC-V2-FRAMEWORK.md` §9.6.1's registry, adjacent to `from-derived-source`, following
whatever column shape that table uses:

| code | severity | meaning |
|---|---|---|
| `from-compound-source` | error | a `from` (item-level or on a compound part) names a compound item, which produces no scalar value to source |

**This is a user-owned call and I am flagging it, not burying it.** Growing §9.6.1's registry is a
public-contract commitment — the registry is explicitly "the registry, and a code not in it does
not exist" (§8.6). If the project would rather not add a code, the only defensible fallback is to
leave the hole open and document it, because both reuse options above are actively misleading. I
recommend adding the code.

### 12.8.5 Parallel holes — flagged, deliberately NOT in scope

The same root cause (a compound self-declares an id, so `ConfigCheck.cs:108`'s `known` test passes,
but the compound never populates `values`) makes four *other* reference forms silently resolve to
nothing when they name a compound id:

| form | where | today's outcome |
|---|---|---|
| `ReferenceForm.ItemSelector` | `{"item":"<compound-id>"}`, item-level or on a part | passes `--check`, renders nothing |
| `ReferenceForm.ArgvPlaceholder` | `{id}` in a command item's argv | passes `--check`, substitutes nothing |
| `ReferenceForm.LinkPlaceholder` | `{other-id}` in a `link` template | passes `--check`, suppresses the link |
| `ReferenceForm.ColorFrom` | a colour rule's `from` | passes `--check`, rule never matches |

`ArgvPlaceholder` has an exact precedent (`placeholder-derived-source`, error) and would take a
sibling code, `placeholder-compound-source`. The other three have no clean precedent and their
severities are a product call (`unknown-link-target` and `unknown-color-source` are *warnings*,
consistent with §3.2's link-is-best-effort rule).

> **Amendment (SPEC-87-compound-reference-resolution.md, incidental finding):** "no clean
> precedent" was too pessimistic for `ColorFrom` — SPEC-87's severity-follows-the-form rule was
> itself the clean precedent, simply not looked for here. `ColorFrom` is resolved below at warning
> severity on that basis, not left open-ended.

**Do not implement these in this change.** File them as a follow-up so this change stays reviewable
(same reasoning as §7.5). They are recorded here so the next person does not re-derive them.

**Resolved by SPEC-87-compound-reference-resolution.md** (`56c23f7`, `a7eb859`): `ItemSelector`
(item-level) and `LinkPlaceholder` now reach the compound whole-tree via that spec's compound-map
lookup mechanism. `ItemSelector` on a part is instead an **error** (`part-compound-source`) rather
than a resolution, closing the same hole from the other side. `ColorFrom` gets a **warning**
diagnostic (`color-from-compound-source`) and falls through to its default colour — that spec
rules this the correct outcome, not a stopgap. `ArgvPlaceholder` is **not** touched by SPEC-87 and
remains open; the `placeholder-compound-source` code proposed above is still unimplemented.

---

## 12.9 Exact change list

Every file and site that changes. A diff touching anything not on this list means the design was
misread.

| # | File | Site | Change |
|---|---|---|---|
| 1 | `src/ClaudeTuiLine/LeafContent.cs` | `:18` | `ItemDecision` gains trailing `IReadOnlyList<StyledSpan>? Spans = null` (§12.4.1) |
| 2 | `src/ClaudeTuiLine/LeafContent.cs` | `:31-49` | `Decide` reads `Display!.Spans`, clears it in the decorative branch, forwards it (§12.4.2) |
| 3 | `src/ClaudeTuiLine/PaneAssembler.cs` | after `:144` | new `itemColor` local (§12.5.1) |
| 4 | `src/ClaudeTuiLine/PaneAssembler.cs` | `:165-167` | pass `itemColor` and `decision.Spans` on the single-line path (§12.5.1) |
| 5 | `src/ClaudeTuiLine/SegmentBuilder.cs` | `:83-92` | 3-arg `BuildItemSegment` gains trailing `spans`; clears it when colouring (§12.6) |
| 6 | `src/ClaudeTuiLine/SegmentTruncation.cs` | `:89-124` | `RestyleSlice` unwraps/re-wraps a link; empty slice returns a bare empty segment (§12.7.1) |
| 7 | `src/ClaudeTuiLine/SegmentTruncation.cs` | `:59-81` | `TruncateSpans` puts the ellipsis in `Spans`, and outside a link, dropping `Spans` when linked (§12.7.2) |
| 8 | `src/ClaudeTuiLine/ItemValueResolver.cs` | `:418-436`, `:444` | build and pass `compoundItemIds` (§12.8.2) |
| 9 | `src/ClaudeTuiLine/ItemValueResolver.cs` | `ReferenceScan` decl | new `CompoundItemIds` member after `CommandItemIds` (§12.8.2) |
| 10 | `src/ClaudeTuiLine/ConfigCheck.cs` | before `:122` | new `from-compound-source` branch (§12.8.3) |
| 11 | `SPEC-V2-FRAMEWORK.md` | §9.6.1 | one new registry row (§12.8.4) |
| 12 | `SPEC-V2-FRAMEWORK.md` | §3.3 note | add the multi-line-compound caveat from §12.5.2 |

**Unchanged, and a diff touching them is a defect:** `RowLayout.cs`, `SizeResolver.cs`,
`PaneRenderer.cs`, `PaneCollapse.cs`, `Compositor.cs`, `Segment.cs`, `Pane.cs`, `Config.cs`,
`LeafItems.cs`, `OscHyperlink.cs`, and `SegmentBuilder.BuildCompoundSegment`.

---

## 12.10 Verification — additions to §10

These are in addition to §10's twelve items, not a replacement. Items 1–4 are the ones that would
have caught D-A; 5 would have caught D-D.

1. **End-to-end per-part colour through the real pipeline (the D-A regression test).** Build a pane
   through `PaneAssembler` (not by hand-constructing a `Segment`) from
   `{"id":"agent-badge","color":"grey","parts":[{"text":"agent:"},{"from":"agent","color":"aqua"}]}`
   and assert the resulting `Segment.Spans is not null` and has two entries. **This test must fail
   against the current working tree** — if it passes before the change, the test is not exercising
   the production path and is worthless.
2. **Truncation preserves per-part colour end to end.** Same config, rendered into a pane narrow
   enough to cut mid-second-span. Assert the first span's `[grey]` wrap survives in the emitted
   markup and the markup is balanced.
3. **No double colour wrap (D-B).** Same config. Assert the emitted `Segment.Markup` does **not**
   start with `[grey]` wrapping the whole thing — i.e. `Markup == concat(span.Markup)` exactly.
4. **Link + compound (D-C).** Add `"link":"https://x/{}"` to the item. Assert
   `OscHyperlink.TryUnwrap(segment.Markup, out var url, out var inner)` is true, `inner ==
   concat(span.Markup)`, and `Spans` is still non-null and still two entries.
5. **Truncated compound satisfies the invariant (D-D).** Truncate a compound with a marker.
   Assert `concat(span.Plain) == Plain` and `concat(span.Markup) == Markup`.
6. **Truncated linked compound: the ellipsis is not clickable (D-E).** Truncate a linked compound.
   Assert the OSC 8 close sequence appears *before* the ellipsis in `Markup`, mirroring the
   assertion the non-span `Truncate` path already has.
7. **Wrapped linked compound re-opens the link per row.** `WrapToWidth` a linked compound across
   two rows; assert both chunks are `TryUnwrap`-able with the same URL.
8. **Non-compound regression.** For a representative non-compound item with an item-level colour
   and a link, assert the produced `Segment` is byte-identical (`Markup`, `Plain`, `Spans == null`)
   to the pre-change output. §8.5's "byte-for-byte" requirement, asserted rather than assumed.
9. **`SizeResolver` agreement.** Assert the width `SizeResolver` computes for a compound equals the
   rendered `Segment.Plain.Length` (§12.5.3) — this is what would catch a measure/render split if
   someone later moves the colour decision.
10. **`from-compound-source` fires.** `--check --json` on a config whose derived item's `from` names
    a compound id: expect `ok:false` and exactly one diagnostic, code `from-compound-source`,
    severity `error`, path `.../items/N/from`.
11. **`from-compound-source` fires on a part's `from` too**, with path `.../items/N/parts/M/from`.
12. **Precedence.** An item with both `parts` and `from`, referenced by another item's `from`:
    expect `from-compound-source` and **not** `from-derived-source` — exactly one diagnostic on
    that pointer.
13. **No false positive.** A `from` naming an ordinary command or builtin item still produces no
    diagnostic.
14. **Full suite green**, plus `tools/check-examples.sh`, `check-citations.sh`, `check-counts.sh`
    after the §12.8.4 registry edit.

---

## 12.11 Risk, confidence, and what I did not decide

**Confidence: high** on §§12.4–12.7 and 12.9. Every line was written against source I read in this
session, the changes are additive-with-defaults, and §12.6's builder makes the invariant
structurally unbreakable rather than merely documented. This is a bounded, mechanical fix — it does
**not** need the broader rearchitecture §11 raised with the Ultra-Advisor, and I do not recommend
escalating it. The reason it stays bounded is §12.5.1: deleting the outer colour wrap for compounds
removes the hard case instead of building machinery to carry it through slicing.

**Confidence: high** on §12.8's diagnostic design; **the registry commitment in §12.8.4 is the
user's call**, not mine, and is flagged as such.

**Residual risks the Implementor should know about:**

- **The `ReferenceScan` record edit (§12.8.2) is positional.** Inserting `CompoundItemIds` in the
  middle silently shifts arguments at the one construction site if the order is not updated to
  match. Adding it and forgetting `:444` is a compile error; adding it in the *wrong position* is
  not. Double-check the argument order against the declaration.
- **§12.10 item 1 is the acceptance gate for this whole change.** If it passes before the fix, the
  test is not going through `PaneAssembler` and the D-A defect is not actually being covered.
- **`ItemDecision` is a `readonly record struct` with a defaulted positional member.** That is legal
  C#, but the default only applies at call sites using the constructor — `default(ItemDecision)`
  produces a null `Markup`. No current code does that; do not introduce it.

**Decisions I made that are properly the user's**, flagged rather than buried:

- **§12.8.4** — adding `from-compound-source` to §9.6.1's registry, amending §8.6's "no new
  diagnostic code strings" rule. Recommended, but it is a public-contract growth.
- **§12.5.2** — a compound containing embedded newlines renders as uniformly-coloured rows with no
  per-part colour, because the block path discards markup. Accepted as a documented limitation
  rather than fixed.
- **§12.7.2** — a *truncated linked* compound drops its `Spans`. Harmless today (a truncated
  segment is terminal), but it is a behavioural corner someone could later be surprised by.
- **§12.8.5** — leaving the four parallel reference-form holes open as a follow-up rather than
  closing them here.
