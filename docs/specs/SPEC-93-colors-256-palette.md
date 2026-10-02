# SPEC-93 — `--colors` reports the full 256-colour palette

STATUS: **REVISION 2. IMPLEMENTATION-READY.** E1, E2, and E3 are all answered
(§6) and nothing in the design changed as a result — the mechanism is fixed to
`Color.FromInt32(i).ToString()`. Revision 2 adds two findings that came out of
the evidence rather than out of the questions asked (§3.2.1, §5.1), and one
question remains Jim's rather than mine (§8, Q-JIM-1) — it is non-blocking and
does not affect the JSON shape.

## 1. Goal

`--colors` currently reports 19 entries: the sixteen ANSI standard colour names
plus the three non-colour keywords `default`, `dim`, `bold`. It should report
the entire 256-colour palette, with the sixteen named theme colours still first.

Jim's request, verbatim as relayed: *"`--colors` should show all 256
Spectre.Console palette colors, with the current 16 named theme colors listed
first (as today), then the rest."*

This is an **enumeration change only**. No change to how colours are parsed,
resolved, or rendered. §2.3 records the check that confirms that framing, and
§2.4 records the one place where it is subtly not true.

## 2. What is actually there now

### 2.1 The command

`src/ClaudeTuiLine/ColorsCommand.cs` — 55 lines total.

```csharp
public sealed record ColorEntryJson(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("themeMapped")] bool ThemeMapped);

public sealed record ColorsResultJson(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("recommended")] IReadOnlyList<ColorEntryJson> Recommended,
    [property: JsonPropertyName("alsoAccepted")] string AlsoAccepted);
```

`ColorsCommand.Build()` concatenates `ColorResolution.StandardColorNames` (16,
`themeMapped: true`) with a `NonColorKeywords` list of `default`/`dim`/`bold`
(3, `themeMapped: false`), producing 19. `RenderMarkupLines()` wraps each name
in Spectre markup for the human-readable form.

`AlsoAcceptedText` (ColorsCommand.cs:35-36) reads:

> Any Spectre.Console color name (256-palette, e.g. deepskyblue1) or #rrggbb
> hex. These parse everywhere a name is accepted; how faithfully they render
> depends on colorSystem (§6.2), which defaults to standard and approximates
> them to the nearest of the sixteen.

### 2.2 The sixteen, and why they are hand-maintained

`ColorResolution.cs:218-231`. The doc comment is load-bearing for this ticket
and is quoted here in full because it constrains §3.2:

> STATUS.md's empirically-verified core sixteen: the ANSI standard palette,
> closed by the standard itself rather than by this library's version. §6.2.1's
> minimum-colour-system check and §9.6.3's `--colors` output both need exactly
> this set — one constant, two consumers, hand-maintained here only because it
> **cannot drift out from under a library upgrade the way Spectre's much larger
> 256-name palette could.**

Read that last clause carefully. The author of `StandardColorNames` had already
considered the 256-name palette and deliberately declined to hardcode it, on the
ground that a hand-copied list of a library-owned set goes stale silently. That
decision is not overturned by this ticket. §3.2 honours it.

Note the declared type (`ColorResolution.cs:225`): `StandardColorNames` is an
`IReadOnlyCollection<string>` backed by a `HashSet<string>` with
`StringComparer.OrdinalIgnoreCase`. **It is a set. It has no enumeration
order.** §3.3 and §5.1 both depend on this.

### 2.3 Parsing is already palette-wide — the "enumeration only" framing holds

`ColorResolution.ResolveLiteral` (ColorResolution.cs:212-216) is a thin wrapper
over `Style.TryParse`, and its own doc comment (:206-210) says so explicitly:

> Parsing itself was never limited to 16 colors (`Style.TryParse` already
> accepts all three forms); §6's "widen the palette" is a rendering-profile
> change (Program.cs's `ColorSystem`), not a parsing one.

So every name this ticket newly *lists* was already accepted before this ticket.
Nothing about acceptance changes. **The peer's scoping assumption is correct.**

### 2.4 …with one qualification the original framing missed

Enumeration is not quite inert, because `--colors` output is a
*recommendation surface*. Today a user reading it sees 16 colours, all of which
are safe under the default `colorSystem: standard`. After this change they see
256, of which 240 will be approximated to the nearest of sixteen unless they
also raise `colorSystem`. The set of things the command *advertises* changes
character even though the set of things the parser *accepts* does not.

§3.4 rules on it. Flagged here because "pure enumeration, no behavioural change"
is true of the code and misleading about the user-visible effect, and the
Implementor should not lean on the former to skip §3.4.

### 2.5 The existing degradation diagnostic

`ConfigCheck.cs:280-295` already implements a per-literal degradation warning:

```csharp
var minimum = MinimumColorSystem(spec.Trim(), resolved.Value);
if (ColorSystemRank(colorSystem) < ColorSystemRank(minimum))
{
    yield return new Diagnostic(path, DiagnosticSeverity.Warning, "color-down-converted",
        $"'{spec}' is a {LiteralTierLabel(minimum)} literal; this terminal's color system ({PaletteLabel(colorSystem)}) will approximate it to the nearest supported color");
}
```

And the tiering rule, `ConfigCheck.cs:274-279`:

> §6.2.1: every literal form names a minimum color system it needs — hex is
> truecolor; **a bare palette index ≤15 is standard, ≥16 is 256**; a name is
> standard only if it's one of the sixteen ANSI standard names (STATUS.md),
> else 256 […]. A spec that resolves to `Color.Default` (e.g.
> "default"/"dim"/"bold" — decoration, not a palette index) has no palette
> dependency and so never needs more than standard.

Two consequences, both used below:

1. **The degradation warning already exists**, it is per-literal, and it is
   keyed to the user's *actual* configured `colorSystem` rather than to a static
   assumption. It is strictly better than any prose `--colors` could emit. §3.4.
2. **Bare numeric palette indices are an accepted literal form.** Confirmed by
   E3. This is why §3.3 emits `number` as a first-class field rather than as
   inert metadata.

### 2.6 Existing test assertions on this surface

`test/…/ColorsCommandTests.cs`:

- `:15` — `Assert.Equal(19, result.Recommended.Count);`
- `:16` — `Assert.Equal(ColorResolution.StandardColorNames.Count, themeMapped.Count);`
- `:92` — `Assert.Contains("\"recommended\":", json);`
- `:95` — `Assert.Contains("\"alsoAccepted\":", json);`

`test/…/AcceptedCommandTests.cs:27-28, :92` also touch `alsoAccepted`, but for
a different command's key list; they are not affected by this ticket.

**`:15` is a hard length assertion on `recommended`.** Extending `recommended`
in place breaks a committed test. §3.1 does not extend it.

## 3. Design

### 3.1 `recommended` is frozen. The palette goes in a new sibling array.

`recommended` keeps its exact current contents and length: 19 entries, sixteen
`themeMapped: true` then `default`/`dim`/`bold`. A new top-level array
`palette` carries all 256.

Two independent grounds, either sufficient:

- **The field name would become false.** `recommended` means "these are the ones
  to reach for." 240 approximated-under-default colours are not
  recommendations. A field whose name stops describing its contents is a
  documentation defect that no amount of prose elsewhere repairs.
- **`ColorsCommandTests.cs:15` pins the length**, and it pins it deliberately —
  a count assertion on a curated list is the test author saying "this list is
  curated, notice if that changes."

That the correct design leaves an existing committed test passing *unmodified*
is corroboration, not the reason. If the design had required changing `:15`, the
right move would have been to change it and say why; it does not.

**Do not delete or weaken `ColorsCommandTests.cs:15`.** It is now load-bearing
for the invariant that `recommended` stayed curated. See §7.

### 3.2 The 240 are enumerated from Spectre, never hardcoded

`ColorResolution.cs:218-224` (quoted in §2.2) declines to hardcode the 256-name
palette specifically because a hand-copied list drifts silently against a library
upgrade. Adding a 240-name string array to this repo re-opens precisely that
hazard, and re-opens it in a worse place — a display list, where drift produces
a wrong-but-plausible name rather than a compile error.

**Mechanism, fixed by E1 (§6.1): `Spectre.Console.Color.FromInt32(i).ToString()`
for `i` in `0..255`.** Verified to yield lowercase palette names across the
range: 0→`black`, 1→`maroon`, 15→`white`, 16→`grey0`, 231→`grey100`,
232→`grey3`, 255→`grey93`.

`StandardColorNames` stays exactly as it is — 16 entries, hand-maintained, same
justification. It is closed by the ANSI standard, not by the library version, so
its rationale is untouched by this ticket. **Do not refactor
`StandardColorNames` to derive from the enumerated palette.** They have
different stability arguments and merging them discards the stronger one.

### 3.2.1 Do not use reflection, and do not "simplify" to it later — REVISION 2

E1 also tested a reflection fallback: enumerate `typeof(Spectre.Console.Color)`'s
public static `Color`-typed properties. It "works" in the sense that it compiles
and the sampled names match. **It returned 291 properties, not 256.**

That 35-property excess is the point. Spectre exposes alias names for some
palette entries (the xterm palette has several: `fuchsia`/`magenta`,
`aqua`/`cyan`, and others), plus non-palette members such as `Default`.
Reflection therefore yields a list that is *longer than the palette*, contains
multiple distinct names mapping to the same index, and has no index available
without a second lookup. Making it correct would require dedup-by-number and a
non-palette-member filter — logic that is not specified anywhere, that would
have to encode which alias is canonical, and that is exactly the kind of
hand-maintained knowledge §3.2 exists to avoid.

`FromInt32(i).ToString()` has none of these problems: it is keyed by index, so
it yields exactly one canonical name per palette entry, exactly 256 of them, in
order, by construction.

**Reflection is rejected — not as a worse-but-viable option, but as one that
produces a wrong list unless supplemented with unspecified logic.** It is
recorded here rather than deleted because it was offered as a working fallback
and reads as a plausible simplification to a future reader who counts 291 as
"more complete" rather than as "aliased." §5.1's test 7 is the guard.

### 3.3 Entry shape and ordering

New record, alongside the existing one rather than replacing it:

```csharp
public sealed record PaletteEntryJson(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("themeMapped")] bool ThemeMapped);
```

`ColorsResultJson` gains one member, appended last so existing positional
construction sites break loudly rather than silently rebinding:

```csharp
public sealed record ColorsResultJson(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("recommended")] IReadOnlyList<ColorEntryJson> Recommended,
    [property: JsonPropertyName("alsoAccepted")] string AlsoAccepted,
    [property: JsonPropertyName("palette")] IReadOnlyList<PaletteEntryJson> Palette);
```

**Do not add `number` to `ColorEntryJson`.** That would change the entry shape
inside `recommended`, which §3.1 just froze. Two records is correct here; the
duplication is three field declarations and it buys an unchanged consumer
contract.

**`palette` contains all 256, including indices 0–15** — not 16–255. A complete
indexed array is what a consumer wants; a partial one forces every consumer to
union two arrays and re-derive the ordering, and consumers that forget produce a
16-colour hole. The sixteen appear in both arrays, and that is fine: this is a
read-only diagnostic, and `themeMapped: true` identifies them within `palette`
without the consumer needing to cross-reference.

**Ordering is palette index order, 0 through 255. Nothing else.** This satisfies
Jim's "16 named first, then the rest" for free, because the sixteen ANSI
standard names *are* palette indices 0–15 — `black`=0 … `white`=15, confirmed by
E2. It also gives the conventional structure at no cost:

| Range | Content |
|---|---|
| 0–15 | the sixteen standard names (`themeMapped: true`) |
| 16–231 | the 6×6×6 colour cube |
| 232–255 | the 24-step grayscale ramp |

So grayscale lands last, the cube is in spectrum order, and no sort key or
grouping convention has to be invented or maintained. **Flat list, index order,
no grouping.** Any grouping we imposed would be a convention someone has to
keep; index order is a property of the palette itself.

**`themeMapped` is `true` iff the name is in
`ColorResolution.StandardColorNames` — compute it by membership test
(`StandardColorNames.Contains(name)`), never by `Number <= 15`.** The membership
test is the definition; the index range is a consequence. E2 confirms they agree
today, and §5's test 3 keeps them agreeing, but if they ever diverge the
membership test is the one that is right.

`StandardColorNames` is `OrdinalIgnoreCase` (§2.2), so the membership test is
case-insensitive and needs no normalisation of Spectre's output.

### 3.4 Degradation: point at the existing check, do not restate it

Should each non-theme entry carry a degradation note? **No.**

`ConfigCheck.cs:292-293` already emits `color-down-converted` per literal, keyed
to the user's actual `colorSystem`. Any static note in `--colors` would be a
second statement of the same rule, less accurate than the first (it cannot know
the user's setting), in a file that has no reason to track changes to §6.2.1's
tiering. Two sources for one rule is how they diverge.

240 identical notes would also be pure output bloat — the note is a property of
the *tier*, not of any individual colour.

What to do instead: **amend `AlsoAcceptedText` (ColorsCommand.cs:35-36) to name
the check.** Its current text already explains the degradation correctly; it
just does not tell the reader that the tool will flag it for them against their
real config. Add a sentence to that effect naming `config-check` and the
`color-down-converted` diagnostic id.

Exact wording is the Implementor's, subject to: it must name the
`color-down-converted` id, must not restate the tiering rule from
`ConfigCheck.cs:274-279`, and must not claim any colour is unsafe — under a
raised `colorSystem` all 256 render faithfully.

### 3.5 Human-readable output

Same content, same order, sixteen first. Emit one entry per line, preserving
`RenderMarkupLines()`'s existing per-name markup swatch.

One-per-line rather than a packed grid: the output stays greppable and
pipeable, which for a CLI diagnostic matters more than compactness, and it is
the smaller change. But 256 lines where there were 19 is a real UX shift and the
grid question is genuinely a product call — see §8 Q-JIM-1. **Implement
one-per-line now**; a grid is a follow-up, not a blocker, and it does not affect
the JSON.

Include the palette index in the human output alongside the name. E3 confirms a
bare index is itself a usable colour spec, so this is a second spelling the user
can copy, not decoration. Format is the Implementor's choice.

## 4. Files to touch

| File | Change |
|---|---|
| `src/ClaudeTuiLine/ColorsCommand.cs` | Add `PaletteEntryJson`; add `Palette` to `ColorsResultJson`; build the 256 list per §3.2; extend `RenderMarkupLines()`; amend `AlsoAcceptedText` per §3.4 |
| `src/ClaudeTuiLine/ColorResolution.cs` | **Optional**: a small palette-enumeration helper if it reads better than inlining the 0..255 loop in `ColorsCommand`. `StandardColorNames` itself: **no change** |
| `test/…/ColorsCommandTests.cs` | Add the §5 tests. **Do not modify `:15` or `:16`** |
| `SPEC-V2-FRAMEWORK.md` ~5755-5869 | Update the `--colors` documentation for the new `palette` field |

Nothing else. In particular **no change to** `ConfigCheck.cs`, `Config.cs`,
`SchemaCommand.cs`, `ColorResolution.ResolveLiteral`, or `Program.cs`'s
`ColorSystem` handling.

## 5. Tests

1. `palette` has exactly 256 entries.
2. `palette` numbers are exactly 0..255, each once, in ascending order.
3. `palette` entries 0–15 have `themeMapped: true`; 16–255 have
   `themeMapped: false`. (E2's finding, made permanent.)
4. The set of `themeMapped: true` names in `palette` equals
   `ColorResolution.StandardColorNames`. **Compare as sets** — see §5.1.
5. `recommended` still has exactly 19 entries — i.e. `ColorsCommandTests.cs:15`
   still passes, unmodified.
6. **Every name in `palette` round-trips through
   `ColorResolution.ResolveLiteral` to a non-null `Color`.** This pins the
   invariant that `--colors` never advertises a name that `config-check` would
   reject with `UnknownColor` (`ConfigCheck.cs:285`). E1 sampled seven indices;
   this test covers all 256, which is the difference between a spot check and a
   guarantee.
7. **Every `palette` name is non-empty and unique across all 256 entries.** This
   is §3.2.1's guard: it fails loudly if anyone swaps the enumeration to
   reflection, since aliasing would surface as duplicate names.
8. The `--json` output contains `"palette":`.

### 5.1 Do not assert an order on `StandardColorNames` — REVISION 2

E2 was reported as the sixteen matching `StandardColorNames` "exactly in set and
order." **The set half is what was verified and what matters. The order half is
not meaningful**: `StandardColorNames` is a `HashSet<string>`
(`ColorResolution.cs:225`), and `HashSet` enumeration order is not part of its
contract — it is an implementation detail that can change under a runtime
upgrade without any source change here.

It happens to enumerate in insertion order today because of how the collection
was built, and that is precisely what makes this a trap: a test asserting the
order would pass now and could break later for reasons unrelated to this code.

So: test 4 compares **sets** (`SetEquals` or equivalent), not sequences. Test 3
carries the index-position claim, and it does so against `palette`'s own
ordering — which is defined by `FromInt32`'s index, a real contract — rather
than against a hash set's incidental one.

## 6. Evidence — all items answered

### 6.1 E1 — enumeration mechanism. ANSWERED: mechanism (a).

`Color.FromInt32(i).ToString()` compiles and yields lowercase palette names.
Sampled: 0→`black`, 1→`maroon`, 15→`white`, 16→`grey0`, 231→`grey100`,
232→`grey3`, 255→`grey93`.

Reflection (b) also runs but is **rejected** — it returns 291 properties rather
than 256. See §3.2.1, which is the substantive Revision 2 finding.

§3.2 is unblocked. No hardcoded list, no reflection.

### 6.2 E2 — indices 0–15 are exactly `StandardColorNames`. ANSWERED: confirmed.

Indices 0–15 yield `black, maroon, green, olive, navy, purple, teal, silver,
grey, red, lime, yellow, blue, fuchsia, aqua, white`, matching
`StandardColorNames` as a set under `OrdinalIgnoreCase`.

§3.3's "the requested ordering is free" argument stands.

The report also claimed an order match; §5.1 explains why that half is not
something to rely on or to encode in a test.

### 6.3 E3 — bare numeric indices parse. ANSWERED: confirmed.

`Style.TryParse("42", out var style)` → `true`, `Foreground = springgreen2`.
So `ResolveLiteral("42")` is non-null and a bare index is a genuine colour spec,
consistent with `ConfigCheck.cs:274-279`'s tiering comment. §3.5's human-readable
index display is a usable second spelling rather than decoration.

## 7. Do not change

- `ColorResolution.StandardColorNames` — contents, type, or its
  hand-maintained-ness (§3.2).
- `ColorsCommandTests.cs:15` and `:16`.
- `recommended`'s contents, length, order, or entry shape (§3.1).
- `ColorEntryJson`'s member list (§3.3).
- Anything in the parse/resolve path: `ResolveLiteral`, `Style.TryParse` usage,
  `ConfigCheck`'s tiering, `Program.cs`'s `ColorSystem`.
- No hardcoded list of the 240 (§3.2).
- **No reflection-based enumeration** (§3.2.1) — it returns 291, not 256.
- Do not compute `themeMapped` from `Number <= 15` (§3.3).
- Do not add a per-entry degradation note (§3.4).
- Do not assert an enumeration order on `StandardColorNames` (§5.1).

## 8. For Jim — not mine to decide

**Q-JIM-1: 256 lines of human-readable output, one per line, or a packed grid?**

§3.5 implements one-per-line because it is greppable and it is the smaller
change. But `--colors` goes from 19 lines to 256, which no longer fits a screen,
and a grid of swatches is arguably the better *browsing* experience for picking
a colour — which is what the command is for. This is a product/UX judgement
about who reads this output and how, and I do not have that context.

**Non-blocking.** The JSON shape is unaffected, so implementation can proceed on
§3.1–§3.4 regardless; only §3.5's rendering would change, and it is confined to
`RenderMarkupLines()`.

## 9. Confidence

**High** on §3.1 (frozen `recommended` + sibling array), §3.2 + §3.2.1
(enumerate via `FromInt32`, never hardcode, never reflect), §3.3 (entry shape
and index ordering), §3.4 (do not duplicate the degradation warning), and §5.1
(no order assertion on a hash set). Each rests on a fact read out of the
codebase or returned by measurement rather than on preference.

The empirical uncertainty that gated Revision 1 is closed: E1, E2, and E3 all
returned, and none of them changed a ruling. What they did change is §3.2.1 and
§5.1 — two hazards that were visible only *in the evidence itself* rather than
in the questions that prompted it. Both are recorded as prohibitions in §7,
because both look like reasonable simplifications to someone reading the
measurement without the surrounding argument.

**Escalation:** none recommended. Blast radius is one command's output plus one
test file; nothing touches parsing, config semantics, or any interface a config
file depends on.
