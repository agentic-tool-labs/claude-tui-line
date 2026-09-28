# §3.3 — compound item kind is advertised by `--items --json` but not implemented in the config model

## Summary

`--items --json` (§9.6.2) advertises a fourth item kind, `compound`, with a `parts` array whose
entries each carry their own `color` — this is exactly §3.3's documented purpose ("several
sources, several colours, one item"). But the actual config parser (`Config.cs`'s
`PaneItemJsonConfig`, validated by `ConfigCheck.cs`) has no `parts` or `compound` property at all.
Any config that uses a compound item, built strictly from the CLI's own advertised schema, fails
`--check` with `unknown-key` on every field the schema told the author to use.

The CLI's declared schema and its actual validator disagree about whether this kind exists.

## Repro

Binary: `/Users/jimcline/.local/bin/claude-tui-line` (build in use at time of filing; version
string not checked — worth confirming which commit/tag this corresponds to before triage).

```bash
$ claude-tui-line --items --json
# ...
"kinds":{"builtin":{...},"derived":{...},"command":{...},
         "compound":{"required":["id","parts"],"optional":["color","overflow","link"]}}
```

This matches SPEC-V2-FRAMEWORK.md §3.3's own example almost exactly:

```json
{ "id": "agent-badge", "parts": [
    { "text": "agent:", "color": "grey" },
    { "from": "agent", "extract": "[^:]+$", "case": "upper", "color": "aqua" }
] }
```

Building a config item from that exact shape and validating it:

```bash
$ cat item.json
{ "id": "agent-short", "parts": [
    { "text": "agent: ", "color": "dim" },
    { "from": "agent", "extract": "[^:]+$", "color": "aqua" }
] }
# ...spliced into a pane's items array as the only change...

$ claude-tui-line --check --json --config <config-with-the-item-above>
{"ok":true,"diagnostics":[{"path":".../items/5/parts","severity":"warning","code":"unknown-key",
  "message":"unknown key 'parts' on an item"}]}
```

Adding an explicit `"kind":"compound"` discriminator (in case the parser needs it to disambiguate)
does not help — it is *also* reported as an unknown key:

```json
{"path":".../items/5/kind","severity":"warning","code":"unknown-key","message":"unknown key 'kind' on an item"}
{"path":".../items/5/parts","severity":"warning","code":"unknown-key","message":"unknown key 'parts' on an item"}
```

`ok` stays `true` — these are warnings, not errors — so a config author following `--items --json`
literally ends up with an item that silently does nothing (§7/§9.8.1's "silent config error"
territory), discovered only by reading stderr diagnostics or noticing the item never renders.

## Root cause (source-level)

- `src/ClaudeTuiLine/ItemsCommand.cs:27` — `ItemKindJson` includes a `Compound` property, which is
  what makes `--items --json` emit the `"compound"` kind entry with its `required`/`optional`
  field lists.
- `src/ClaudeTuiLine/Config.cs` lines 204–263 — `PaneItemJsonConfig`, the type actually
  deserialized from a live config file, defines only: `item`, `format`, `color`, `overflow`, `id`,
  `command`, `shell`, `ttlSeconds`, `timeoutMs`, `link`, `from`, `extract`, `case`, `maxLines`.
  No `parts`, no `kind`, no per-part structure of any kind. `grep -n "compound\|parts"
  src/ClaudeTuiLine/Config.cs` returns nothing.
- `src/ClaudeTuiLine/ConfigCheck.cs:855` (`CheckUnknownKeys`) walks each item's JSON keys against
  whatever `PaneItemJsonConfig` declares and flags anything else as `unknown-key` (line 876) — so
  it faithfully reports `parts`/`kind` as unrecognized, because they genuinely aren't recognized
  by the model it's checking against.

So `--items --json`'s `compound` entry describes a kind that was designed (SPEC-V2-FRAMEWORK.md
§3.3, with worked examples) and exposed in the discovery API (§9.6.2), but never implemented in
the config schema or the renderer that would act on it. `ItemValueResolver.cs:22` and `:231`
reference §3.3's compound-item parts only in comments about where a future reference form would be
added ("Adding a form (§4.2's argv placeholders, §3.3's compound-item parts) means appending
here") — consistent with this being unimplemented rather than implemented-and-broken.

## Impact

- Any config author who follows `/claude-tui-line:edit`'s own documented procedure — "query the
  binary, don't rely on recall" — and finds `compound`/`parts` in `--items --json` will write a
  config that `--check` accepts (`ok:true`) with only a warning, and that silently renders nothing
  for that item. This is exactly the failure mode §7 of the edit skill warns about: "a bad config
  is silent."
- Blocks the documented "one value, two colours" pattern (§3.3, and the edit skill's own
  translation table: *"make adds green and deletes red" → ... two derived items, each with its own
  extract regex over the same from, **or one item with per-part colours***). The per-part-colours
  half of that alternative does not currently work.
- No data loss or crash risk — `--check` never returns `ok:false` for this, and nothing renders
  incorrectly; the item is simply absent. Severity is "silent no-op / broken discovery contract,"
  not "data corruption."

## Scope / what's not broken

- `builtin`, `derived`, and `command` item kinds all work as documented and match `--check`.
- The two-derived-items workaround (separate items, each `from`/`extract` over the same source,
  each with its own `color`) does work today and is what the edit skill falls back to. It has its
  own side effect worth tracking separately if not already known: splitting one logical value into
  two items means they wrap/drop independently at narrow widths, so a value can be dropped while
  its label survives (e.g. a bare `agent:` with no name at 60 columns, where the single-item form
  would have dropped the whole thing together). That's a layout-degradation consequence of the
  workaround, not of this defect, but the team should know it's the reason compound items matter
  for more than cosmetics — they're also how "drop as one unit" is currently expressed.

## Open questions for whoever picks this up

1. Is `compound` intended to ship soon (i.e., is §11's Phasing section committing to it in an
   upcoming milestone), or should `--items --json` stop advertising a kind that isn't real until
   `Config.cs`/`ConfigCheck.cs` catch up? Either fix closes the immediate defect (schema promises
   something real); which one is a scope/priority call, not something this doc should decide.
2. If implementing: `PaneItemJsonConfig` needs a `Parts` property (list of part objects — each
   with one of `text`/`item`/`from` plus optional `extract`/`case`/`format`/`color`, per §3.3 and
   the `--items --json` shape already shipped), `ConfigCheck.cs` needs to validate parts the same
   way it validates top-level derived/command items (including the `part-source-count` and
   `part-forbidden-key` diagnostic codes already reserved for this in §9.6.1 — those codes exist
   in the spec's registry but should be checked for whether they're wired up anywhere yet), and the
   renderer needs to actually concatenate parts with their individual colors into one item.

## Verification (once fixed)

1. `--items --json` and `--check` should agree: a config built exactly from the `compound` schema
   `--items --json` returns should validate clean (`diagnostics: []`), not warn.
2. The §3.3 `agent-badge` worked example, entered verbatim, should render as one item with the
   `text` part and the `from`-derived part in their respective colors, adjacent with no
   inter-item separator — that's the behavioral difference from the two-derived-items workaround
   this defect currently forces authors into.
3. An item with no matching part or an entirely absent `from` source should suppress the whole
   compound item as one unit (matching §2.3's suppression-predicate behavior for ordinary derived
   items), not partially render.
4. Re-run `--check` with the deliberately-wrong `"kind":"compound"` variant tried during repro to
   confirm the discriminator question is resolved one way or the other (either it's required and
   documented as such in §9.6.2, or the kind is inferred from `parts` alone and `kind` remains
   unrecognized by design — either is fine as long as `--items --json` and the parser agree).

## Confidence

High that the defect exists as described — confirmed by both `--check` output and by reading the
three relevant source files (`ItemsCommand.cs`, `Config.cs`, `ConfigCheck.cs`) directly, not
inferred from behavior alone. Not confident about intended fix direction or priority — that's a
product/roadmap call for the team, not inferable from the code.

## Provenance

Found while running `/claude-tui-line:edit "in the agent item, dim the agent: label"` — the
requested single-item split (dim label + colored value, one unit) is the canonical compound-item
use case, and the command fell back to two derived items only after this defect blocked the
intended approach. Repro binary and config were the user's live machine config at
`~/.claude/claude-tui-line.json`, not a synthetic repro; the JSON above is a minimal extraction.
