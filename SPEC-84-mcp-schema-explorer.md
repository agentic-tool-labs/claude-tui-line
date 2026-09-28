# SPEC-84 — MCP config-schema explorer tool

Status: design, **ready to implement — no open decisions**. Author: architect (task #84).
Depends on: SPEC-12.6-mcp-tools.md (§1, §7.1), SPEC-83 (shared library / allow-list),
SPEC-3.3-compound-item-schema-not-implemented.md, SPEC-V2-FRAMEWORK.md §1.1.3, §9.6.

**Amendment 1** (same session, before implementation): §5.0 added — SPEC-V2-FRAMEWORK §1.1.3
explicitly *rejected* `--schema` as a flag name during #42's design, and this spec proposes it.
The reasoning for why that rejection does not bind #84 is now recorded rather than left for a
reviewer to discover. §4's file table and §5.3 also gained exact line references from §1.1.3 and
§9.6.2.

**Amendment 2** (same session, before implementation): the three open questions are **closed by
user decision** and the spec now states the answers rather than the questions — Q1 full scope
(CLI subcommand included), Q2 public/frozen surface, Q3 keep the name `--schema` and amend
§1.1.3. See §9. Separately, `Config.cs` was read directly, so §5.3's placeholder record names are
replaced with the real ones (E4 closed), and E3/E5 are closed by source reading. That reading
**corrected two structural errors** in Amendment 1's §5.3: there is no distinct split/branch record
(one `PaneConfig` serves both roles), and there is no compound-part record at all. §2 F9 and §5.3
carry the corrections; §7 V4 gained the exemption rule they force.

---

## 1. Goal

An LLM agent driving `/claude-tui-line:edit` currently has to grep `SPEC-V2-FRAMEWORK.md` and
friends to learn the config schema — which keys exist, what values they accept, and what a pane /
split node / colour rule / item actually looks like. Documentation is not the binary, so the agent
can and does act on stale or aspirational schema (SPEC-3.3 is exactly that failure, in the CLI's
own output).

This spec adds **one MCP tool, `get_config_schema`**, that returns the config schema live from the
installed binary, and the **one CLI subcommand it reads from**. After this lands, an agent editing
a config never needs to read a spec file to know the schema.

---

## 2. What the investigation found (facts the design rests on)

Verified by reading source and specs on `main`. Cited so the implementor does not re-derive them.

**F1 — MCP's current tool surface is two tools.** `src/ClaudeTuiLineMcp/ConfigTools.cs` declares
exactly `get_config` (line 17) and `set_config` (line 45). There is no discovery tool.

**F2 — MCP spawns the CLI; it does not link the core. This is a ruling, not an accident.**
`src/ClaudeTuiLineMcp/CliRunner.cs:6-12` states SPEC-12.6 §1: "the server SPAWNS the CLI rather
than linking the core in-process … a successful spawn is evidence the statusline binary actually
works on this machine; that evidence is unavailable to a linked server (§1.3(b))." `CliRunner`
already spawns `--check --config <path> --json` and `--items --json`.

**F3 — the allow-list forbids the alternative.** `src/ClaudeTuiLineMcp/ClaudeTuiLineMcp.csproj`
carries exactly one `ProjectReference`, to `ClaudeTuiLineShared.csproj`, with a comment recording
that the absence of a `ClaudeTuiLine.csproj` reference is deliberate (NETSDK1151). Both halves are
enforced by `tests/ClaudeTuiLineMcp.Tests/AllowListTests.cs` — `V4` (no `ClaudeTuiLine.*` symbol
except `ConfigLoader.ResolveConfigPath`) and `V4b` (no core `ProjectReference`, exactly one shared
`ProjectReference`).

**F4 — `ClaudeTuiLineShared` is one 32-line file.** `src/ClaudeTuiLineShared/ConfigPath.cs`,
`ConfigPath.ResolveConfigPath`, no dependencies, no `PackageReference` in its csproj.

**F5 — the three discovery commands are not movable to Shared.** Each `Build()` reaches deep into
the core:
- `ItemsCommand.Build()` (`src/ClaudeTuiLine/ItemsCommand.cs:51-70`) uses `SyntheticFixture.CreateItemContext()`,
  `ItemRegistry.All`, `ItemDefinition.BuildDefaultSegment`, `ItemRegistry.DefaultIds`, `AssemblyVersionInfo`.
- `ColorsCommand.Build()` (`ColorsCommand.cs:38-49`) uses `ColorResolution.StandardColorNames`.
- `AcceptedCommand.Build()` (`AcceptedCommand.cs:33-63`) reads **nine** parser-colocated registries
  (`BorderStyleParsing`, `ConfigLoader.ColorSystemAcceptedTokens`, `ConfigLoader.SplitAcceptedTokens`,
  `PaneValignParsing`, `PaneAlignParsing`, `PaneDistributeParsing`, `OverflowModeParsing`,
  `ItemValueResolver.CaseAcceptedTokens`, `PaneHeightParsing`) plus `ConfigChecker.SizeValues`.

Relocating these to `ClaudeTuiLineShared` would drag the item registry, the renderer's synthetic
fixture, every parser, and the config checker along with them — i.e. it would move the core into
the "dependency-free" library and re-create #83's problem in a new shape.

**F6 — the structural schema is exposed nowhere.** `--items`/`--colors`/`--accepted` cover item
kinds, colour names, and per-key enum tokens. Nothing machine-readable describes the *document*:
the config root, a pane, a split node, a colour rule, a compound part. That is the half of #84's
ask that no existing CLI output can satisfy. SPEC-V2-FRAMEWORK §1.1.3 says so in its own words when
rejecting the `--schema` name: a full config schema means "types, nesting, required-ness", and
`--accepted` "does not deliver" it.

**F7 — the CLI's advertised item schema is currently a lie, and it is a filed one.**
`ItemsCommand.cs:49` and SPEC-V2-FRAMEWORK.md:5458 advertise
`compound: required ["id","parts"], optional ["color","overflow","link"]`.
`src/ClaudeTuiLine/Config.cs`'s `PaneItemJsonConfig` (line 204) declares `item, format, color,
overflow, id, command, shell, ttlSeconds, timeoutMs, link, from, extract, case, maxLines` — no
`parts`, no `kind`. `ConfigCheck.cs:855-876` therefore emits `unknown-key` warnings (severity
`warning`, so `ok` stays `true`) for a config written strictly from the CLI's own advertised
schema. SPEC-3.3 documents this in full. Task #85 (compound items) is in flight and will change it.

**F8 — compound's *intended* rules are already specified**, and the `structures` table must match
them rather than invent them: SPEC-V2-FRAMEWORK.md:2664 (`parts` renders concatenated),
:2726 ("A part may not carry `link`" — `link` stays at item level and wraps the whole compound),
:2720 (all-empty compound renders nothing and collapses per §2.4), and the two diagnostic codes at
:5413-5414 — `part-source-count` (a part with zero or more than one source) and `part-forbidden-key`
(a part carrying `parts` or `link`).

**F9 — the real shape of `src/ClaudeTuiLine/Config.cs`** (read directly; this replaces Amendment 1's
placeholders and corrects two of its assumptions). Public model types and their line numbers:

| Type | Line | Role |
|---|---|---|
| `UserConfig` | 8 | the config document root |
| `BorderConfig` | 40 | border block (also accepts a shorthand string) |
| `BorderEdgesConfig` | 86 | per-edge border toggles |
| `LayoutConfig` | 109 | layout block |
| `SurfaceConfig` | 124 | the surface/pane-tree block |
| `PaneConfig` | 146 | **a pane — leaf *and* branch, one type** |
| `PaneItemJsonConfig` | 204 | an item |
| `ColorExprJsonConfig` | 329 | a colour expression (**union**: literal string or rule object) |
| `ColorRuleJsonConfig` | 341 | a colour rule |
| `ThresholdJsonConfig` | 364 | one threshold arm |
| `MatchJsonConfig` | 382 | one match arm |

Non-model internals, listed so the implementor does not mistake them for schema:
`PaneSizeConverter` (270), `CommandJsonConverter` (291), `ColorExprJsonConverter` (408),
`BorderConfigConverter` (440), `ResolvedConfig` (488), `ConfigReadResult` (931). `ResolvedConfig` is
the *post-resolution* model, **not** the wire schema — `structures` describes the `*JsonConfig` /
`UserConfig` layer only.

Three properties of F9 are load-bearing for the design:

- **F9a — there is no split/branch record.** `PaneConfig` is both. A branch pane carries
  `split` + `children`; a leaf pane carries `items`. Amendment 1's §5.3 listed "the branch/split
  node" as a separate `structures` entry; that entry does not exist and must not be invented.
- **F9b — there is no compound-part record.** No `PartJsonConfig`, no `parts` property anywhere.
  This is F7 seen from the model side, and it means the `compoundPart` structure entry has **no
  record to reflect against** (§7 V4 exempts it explicitly).
- **F9c — every model type carries an `Extra` catch-all with no `[JsonPropertyName]`**
  (`Dictionary<string, JsonElement>?`), the bucket the unknown-key check reads. Two further
  properties also lack the attribute and are converter-driven union forms, not wire keys:
  `BorderConfig.Shorthand` (a bare string in place of the object) and `ColorExprJsonConfig`'s
  `Literal` / `Rule`. **The general rule this forces: a wire key is a property carrying
  `[JsonPropertyName]`, and nothing else.** §5.2, §5.3, and §7 V3/V4 all apply exactly that rule.

---

## 3. Decisions

### D1 — One tool, not several. `get_config_schema`.

**Decided: one tool**, with an optional `sections` filter.

Rationale:
- Every MCP tool's name + description occupies context in **every** session that connects the
  server, used or not. Three discovery tools is three permanent context costs to save round trips
  in one workflow.
- The consumer is an agent about to edit a config. It does not want one enum; it wants the schema.
  Three narrow tools turn one question into three round trips, each carrying full MCP envelope
  overhead — strictly more tokens than one payload.
- The whole payload is small: ~16 item rows, ~19 colour entries, 10 accepted-key rows, and a
  structural table of ~9 node types. Low thousands of tokens, once.
- The CLI's three-flag split is not a counter-precedent. §1.1.3 chose a *third command* over
  folding into `--items`/`--colors` on a **meaning** argument ("Neither noun covers 'config keys
  with constrained values'"), not on a granularity argument. A tool whose noun is "the schema"
  legitimately covers all of them.

`sections` exists for the case where an agent genuinely wants one slice (e.g. re-checking accepted
tokens mid-edit) without re-paying for the rest.

**Amendment (task #93):** the one-tool decision itself is re-affirmed, but the size premise above
it — "~19 colour entries... low thousands of tokens, once" — was superseded. That count named the
`recommended` list but missed the 256-entry `palette` array, which the Architect's own closing note
on task #93's verdict flagged as roughly 44% of the raw payload. `schema-mcp-query.md` fixes the
size problem the count missed, without reopening D1's one-tool-vs-several question.

### D2 — Data source: one new CLI subcommand, spawned. NOT moved into `ClaudeTuiLineShared`.

**Decided: `claude-tui-line --schema --json`, spawned by MCP via the existing `CliRunner`/`CliLocator`
pattern.**

Rationale:
- F5 rules out relocation. The schema-producing logic is inseparable from the registries and
  parsers it reads; moving it moves the core.
- F2 records that spawning is the established, reasoned pattern, with a benefit
  (spawn = proof the installed binary works) that a linked implementation cannot provide.
- F3 means a linked implementation is also test-forbidden. `AllowListTests.V4b` fails the moment
  anyone tries.
- F6 means MCP needs a structural table that does not exist yet. It must be authored *next to*
  `Config.cs`, which is in the core, which MCP cannot reference. A CLI subcommand is the only
  channel.

**This departs from the dispatch's original framing** ("a NEW MCP TOOL, not a CLI addition"). The
framing assumed CLI discovery already covers the ask; F6 shows it does not cover structure. The
alternative — MCP hand-maintaining its own copy of the pane/split/item structure — is rejected: it
is a second source of truth for the config shape, it would drift from `Config.cs` silently, and
drift in *this* artifact is precisely the bug (SPEC-3.3) the tool exists to prevent. **The user has
ruled on this (§9 Q1): full scope, the CLI subcommand is in.**

**One spawn, not four.** `--schema --json` aggregates items + colors + accepted + structure into a
single envelope, built by calling the existing `ItemsCommand.Build()` / `ColorsCommand.Build()` /
`AcceptedCommand.Build()` — not by re-deriving them. Four spawns per tool call would cost four
process starts for data the CLI can emit in one.

### D3 — Output shape: the framework's existing `--json` conventions, extended. Not JSON Schema.

**Decided: an envelope embedding the existing payloads verbatim**, plus a new `structures` section.

Rationale: consistency, per the dispatch's stated preference, and because embedding verbatim means
the items/colors/accepted halves cannot drift from the single-purpose commands *at all* — they are
literally the same records. JSON Schema would require translating three bespoke shapes into a
fourth vocabulary, adding a lossy hand-written mapping with no consumer asking for it. An LLM reads
the framework's shape as easily as it reads JSON Schema.

### D4 — Truthfulness (compound / #85): derived, never hardcoded.

**Decided: each item kind carries `supported: bool` and `unsupportedKeys: string[]`, computed by
comparing the kind's advertised `required ∪ optional` against the keys `PaneItemJsonConfig`
actually declares.**

This is the load-bearing decision for the #85 coordination question. Because the flag is *computed*
from the config model rather than written down:
- If #84 lands first, `compound` reports `supported: false, unsupportedKeys: ["parts"]` — truthful,
  and it stops an agent from writing SPEC-3.3's silently-dead item.
- If #85 lands first, or later, `parts` appears on `PaneItemJsonConfig` and the flag flips to
  `true` **with no edit to #84's code and no coordination between the two tasks**.

Neither task blocks the other and neither needs to know the other's schedule. That property is the
reason for the mechanism.

Constraint: the core is `PublishAot=true`, so the comparison must **not** use runtime reflection
over `PaneItemJsonConfig` (trimming makes that unsound — see E1). Production code declares the
key list explicitly; a **test** uses reflection to assert the declared list equals
`PaneItemJsonConfig`'s real `[JsonPropertyName]`-carrying properties. Drift becomes a red test,
never a wrong answer at runtime.

This mirrors the precedent §9.6.3 set for `--colors`: a hand-curated list is acceptable *precisely
because* a test refuses to let it be wrong ("The list is allowed to be hand-written precisely
because that test refuses to let it be wrong").

### D5 — `--schema --json` is a public, frozen surface.

**Decided by the user (§9 Q2): public**, on par with `--check` / `--items` / `--preview` /
`--colors` / `--accepted` (the last by the same ruling in task #43).

Consequences the implementor must honour:
- The envelope's key names and the `structures` entry shape are a **compatibility commitment from
  the first release**. Adding a section or a field later is fine; renaming or removing one is a
  breaking change.
- §7's V1 and V4 anti-drift tests stay **strict** as written. They are not "nice to have" coverage;
  they are what makes a frozen surface safe to freeze.
- It gets documented in `SPEC-V2-FRAMEWORK.md` §9.6 alongside its siblings (§4), not left as an
  undocumented convenience.

---

## 4. Files to touch

### New

| Path | Purpose |
|---|---|
| `src/ClaudeTuiLine/SchemaCommand.cs` | `--schema --json` payload: records, `JsonSerializerContext`, `Build()` |
| `tests/ClaudeTuiLine.Tests/SchemaCommandTests.cs` | V1–V6 below |
| `tests/ClaudeTuiLineMcp.Tests/GetConfigSchemaToolTests.cs` | V7–V10 below |

### Modified

| Path | Change |
|---|---|
| `src/ClaudeTuiLine/Program.cs` | add `case "--schema":` alongside the other mode cases |
| `src/ClaudeTuiLine/Program.cs` | add `--schema` to the mutual-exclusion comment, mode set, and error message |
| `src/ClaudeTuiLine/Program.cs` | add the `("--schema", new[] { "json" })` allowed-arguments row |
| `src/ClaudeTuiLine/Program.cs:576-579` | add the bare-`--schema` usage-error guard, mirroring bare `--accepted` (§5.4) |
| `src/ClaudeTuiLineMcp/CliRunner.cs` | add `RunSchemaAsync()` |
| `src/ClaudeTuiLineMcp/ConfigTools.cs` | add the `get_config_schema` tool |
| `SPEC-12.6-mcp-tools.md` | append a §12.6.x describing the third tool |
| `SPEC-V2-FRAMEWORK.md` §9.6 | document `--schema --json` as a sibling of `--items`/`--colors`/`--accepted`, and as a public surface per D5 |
| `SPEC-V2-FRAMEWORK.md:470` | replace the `--schema` "Rejected" table row — exact replacement text in §5.0 |
| `README.md` | one line in the CLI-flags list, if one exists |

`Program.cs`'s mode-dispatch line numbers are inherited from §1.1.3's own file-touch table for
`--accepted` and have shifted; the implementor locates those sites **by content, not by line**. The
one line reference above that was re-verified against the current file is `576-579` (the bare
`--accepted` guard) — see §5.4.

### Must NOT change

- `ItemsCommand.cs`, `ColorsCommand.cs`, `AcceptedCommand.cs` — their `Build()` results are
  **consumed**, never copied, re-derived, or altered. `--items --json`, `--colors --json`,
  `--accepted --json` keep byte-identical output.
- `ClaudeTuiLineMcp.csproj` — no new `ProjectReference`. `AllowListTests` V4/V4b must keep passing
  unchanged.
- `ClaudeTuiLineShared` — nothing moves into it. It stays `ConfigPath.cs` alone.
- `get_config` / `set_config` behaviour, including the CAS/`baseRevision` contract.
- `Config.cs` / `ConfigCheck.cs` — #84 reports the model, it does not extend it. Adding `parts`
  is #85's job.

---

## 5. CLI: `--schema --json`

### 5.0 The name: `--schema`, and the §1.1.3 amendment that goes with it

SPEC-V2-FRAMEWORK §1.1.3's name table currently contains, at **line 470**:

> | `--schema` | **Rejected.** Promises a full config schema — types, nesting, required-ness — that this does not deliver. Over-promising a public surface name is worse than a longer one. |

**Decided by the user (§9 Q3): keep `--schema`.** Read the rejection's reason, not its verdict.
`--schema` was rejected *for `--accepted`'s payload*, because that payload is a flat key→tokens
table and the name would have over-promised. #84's payload is the thing §1.1.3 said `--schema`
promises: types, nesting, and required-ness. The earlier ruling is therefore evidence **for** this
name — it reserved the word for exactly this command by refusing to spend it on a lesser one. No
workaround name (`--config-schema` or otherwise) is needed or wanted; this command earns the word.

Leaving a table row reading "`--schema` — Rejected" in the spec while the binary ships `--schema`
is the documentation-drift failure §1 of that document exists to prevent. **Replace line 470 with:**

> | `--schema` | **Rejected for `--accepted`** — it promises a full config schema (types, nesting, required-ness) that `--accepted`'s flat key→tokens payload does not deliver. **Taken by `--schema --json` (task #84)**, which does deliver it: item kinds, accepted values, colour names, and the structural shape of the config document. See SPEC-84-mcp-schema-explorer.md §5.0. |

Two constraints on that edit:
- Keep it a **single table row** with the same two-column shape (`| Candidate | Verdict |`, table
  header at line 465). Do not restructure the table or renumber the section.
- Do not delete the original rejection reasoning. The row must still teach the lesson it was
  written to teach — over-promising a name is a real cost — while recording that the promise is now
  kept by a different command.

E3 is closed: `--schema` does not appear as a flag literal in `Program.cs`, and does not collide
with any of the existing `--check`, `--version`, `--items`, `--colors`, `--preview`, `--accepted`,
`--fixture`, `--json`, `--config`, `--columns`.

### 5.1 Envelope

```jsonc
{
  "version": "<AssemblyVersionInfo.InformationalVersion>",
  "items":    { /* ItemsResultJson, verbatim */ },
  "colors":   { /* ColorsResultJson, verbatim */ },
  "accepted": { /* AcceptedResultJson, verbatim */ },
  "kindSupport": {
    "builtin":  { "supported": true,  "unsupportedKeys": [] },
    "derived":  { "supported": true,  "unsupportedKeys": [] },
    "command":  { "supported": true,  "unsupportedKeys": [] },
    "compound": { "supported": false, "unsupportedKeys": ["parts"] }
  },
  "structures": [ /* §5.3 */ ]
}
```

`items`/`colors`/`accepted` each carry their own `version` field already; that is accepted
redundancy — verbatim embedding is worth more than de-duplicating a version string. The outer
`version` is the one a consumer should read, and it must use **the same `JsonPropertyName` spelling
`ItemsResultJson` uses**, per §1.1.3 §2's explicit instruction not to invent a second spelling.

`kindSupport` is a sibling of `items`, **not** merged into `items.kinds`, so that the embedded
`ItemsResultJson` stays byte-identical to `--items --json` (a merge would mean re-serialising a
modified copy, which is exactly the drift D3 avoids).

### 5.2 `kindSupport` computation

The 14 keys below were read off `PaneItemJsonConfig` (`Config.cs:204`) and are the complete set of
its `[JsonPropertyName]`-carrying properties, in declaration order. Its fifteenth property, `Extra`,
is the unknown-key catch-all and carries no attribute — it is **not** a wire key and must not appear
in this list (F9c).

```csharp
// Declared, not reflected: the core publishes AOT, and reflecting over PaneItemJsonConfig's
// properties is unsound under trimming. SchemaCommandTests.V3 asserts this list against the
// real type, so drift fails the build instead of producing a wrong answer at runtime.
private static readonly IReadOnlyList<string> ModelItemKeys = new[]
{
    "item", "format", "color", "overflow", "id", "command", "shell",
    "ttlSeconds", "timeoutMs", "link", "from", "extract", "case", "maxLines",
};
```

For each kind in `ItemsCommand`'s `Kinds` table: `unsupportedKeys` = `(required ∪ optional)` minus
`ModelItemKeys`, order-preserving (required first, then optional), compared **ordinal
case-sensitive** — JSON keys in this config are case-sensitive. `supported` = `unsupportedKeys.Count == 0`.

Read the kinds table off `ItemsCommand.Build().Kinds` (already `public`). Do **not** promote the
`private static readonly Kinds` field or add a second accessor: one table, one reader.

### 5.3 `structures`

The new material — the half of #84 that no existing command covers. One entry per structural node
type in the config document. Entry shape:

```jsonc
{
  "name": "pane",
  "record": "PaneConfig",
  "description": "A rectangular region of the statusline. A branch pane carries split+children; a leaf pane carries items.",
  "required": [],
  "optional": ["split", "children", "size", "..."],
  "fields": [
    { "name": "valign", "type": "string", "description": "...", "acceptedKey": "valign" }
  ],
  "notes": [],
  "example": { /* a minimal valid instance */ }
}
```

`record` names the `Config.cs` type the entry describes, and is what §7 V4 joins on. It is `null`
for an entry with no backing record (only `compoundPart`, per F9b).

`acceptedKey`, when present, names the row in the `accepted` section that lists this field's valid
tokens — that cross-link is what lets an agent go from "a pane has a `valign`" to "`valign` accepts
these four tokens" without a second call. It must be `null` or a key that exists in
`accepted.keys[].key` (asserted by V5).

`notes` is a string array for constraints that are not expressible as a key list — union forms,
mutual exclusions, and current-status caveats. It is where F9's awkward cases go instead of being
silently dropped.

**Wire keys only.** Every entry's `required`/`optional`/`fields` list the type's
`[JsonPropertyName]` values and nothing else. `Extra` never appears (F9c). Neither do the
converter-driven properties that carry no attribute — they are described in `notes` instead.

#### Required entries

Exactly these nine, with these `record` bindings (all line numbers in `src/ClaudeTuiLine/Config.cs`):

| `name` | `record` | Line | Wire keys (all optional unless noted) |
|---|---|---|---|
| `config` | `UserConfig` | 8 | `border`, `layout`, `items`, `surface`, `colorSystem`, `colors` |
| `border` | `BorderConfig` | 40 | `enabled`, `color`, `style`, `edges`, `collapse` |
| `borderEdges` | `BorderEdgesConfig` | 86 | `top`, `right`, `bottom`, `left` |
| `layout` | `LayoutConfig` | 109 | `chromeReserve` |
| `surface` | `SurfaceConfig` | 124 | `maxRows`, `pane`, `border` |
| `pane` | `PaneConfig` | 146 | `split`, `children`, `size`, `minSize`, `maxSize`, `border`, `overflow`, `ellipsis`, `maxRows`, `gutter`, `valign`, `align`, `distribute`, `height`, `items` |
| `item` | `PaneItemJsonConfig` | 204 | `item`, `format`, `color`, `overflow`, `id`, `command`, `shell`, `ttlSeconds`, `timeoutMs`, `link`, `from`, `extract`, `case`, `maxLines` |
| `colorRule` | `ColorRuleJsonConfig` | 341 | `from`, `thresholds`, `match`, `default` |
| `threshold` | `ThresholdJsonConfig` | 364 | `min` (**required** — non-nullable `double`), `color` |
| `match` | `MatchJsonConfig` | 382 | `contains`, `equals`, `color` |

That is ten rows; `colorExpr` and `compoundPart` below bring the table to twelve entries total.
Two further entries have no ordinary key list and are specified individually:

- **`colorExpr`** — `record: "ColorExprJsonConfig"` (line 329). A **union**, not an object with
  keys: either a colour string, or a `colorRule` object. Its two C# properties (`Literal`, `Rule`)
  carry no `[JsonPropertyName]` and are driven by `ColorExprJsonConverter` (line 408), so
  `required` and `optional` are both **empty arrays** and the union is described in `notes`:
  *"Accepts either a colour string (a standard name from the `colors` section, or a `@name`
  reference), or a `colorRule` object."* V4 exempts it (§7).

- **`compoundPart`** — `record: null`. **No such type exists in `Config.cs`** (F9b). It is included
  anyway because `kindSupport.compound.supported == false` alone tells an agent that compound is
  dead without telling it what #85 will make legal. Its content comes from F8's already-specified
  rules — exactly one source per part, and neither `parts` nor `link` on a part — and its
  `description` must state the current status and cite SPEC-3.3. Its `notes` must carry the two
  diagnostic codes `part-source-count` and `part-forbidden-key`. V4 exempts it (§7).

#### Corrections to Amendment 1

Amendment 1's placeholder list named a separate branch/split node entry. **There is none** (F9a):
`PaneConfig` is both, distinguished by which keys are populated. Do not create a second entry for
it; state the leaf/branch discriminator in `pane`'s `description` and `notes`
(*"A branch pane sets `split` and `children`; a leaf pane sets `items`. Setting both is a config
error."*).

#### Union and converter notes required in `notes`

These are real shapes a config author will hit, and none of them is visible from a key list:

- `border` (and `surface.border`, `pane.border`) accepts a **shorthand string** in place of the
  object — `BorderConfig.Shorthand` carries no `[JsonPropertyName]` and is driven by
  `BorderConfigConverter` (line 440).
- `pane.size` is a string driven by `PaneSizeConverter` (line 270); its accepted forms come from
  `ConfigChecker.SizeValues` via the `accepted` section, so set `acceptedKey` on it.
- `item.command` is driven by `CommandJsonConverter` (line 291), typed
  `IReadOnlyList<string>?` — note whether a bare string is also accepted, which the converter
  decides; the implementor reads the converter and writes what it actually does, rather than
  guessing.
- `threshold.min` is a non-nullable `double` and is the one genuinely **required** key in the whole
  table. Every other listed key is optional.

#### Scope guard

`ResolvedConfig` (line 488) and `ConfigReadResult` (line 931) are **not** in `structures`. They are
the post-resolution and read-result models, not the wire schema, and including them would advertise
shapes no config author can write.

**The `structures` table is hand-authored** — there is no runtime derivation available under AOT.
V4 is what keeps it honest: it asserts, by reflection *in the test*, that each entry's
`required ∪ optional` matches its `record`'s `[JsonPropertyName]` values. A field added to
`Config.cs` without a `structures` update fails the build. This is §9.6.3's curated-list-plus-test
precedent applied to structure.

Each `example` is a worked example, and §9.6.2.1 is emphatic that "an example that names a specific
behaviour is an assertion about the implementation and ages exactly like one." Keep them minimal,
and prefer examples V4's reflection already constrains over prose-only illustrations.

### 5.4 Plain text

`--schema` without `--json` is a **usage error** naming `--schema --json`, matching `--accepted`
exactly (§1.1.3: "Bare `--accepted` is a usage error naming the correct form"). §1.1.3's reasoning
transfers verbatim and is worth restating because it is about *this* kind of surface: requiring
`--json` now, while the surface has no users, makes a plain-text form purely additive later, where
emitting JSON bare would strand it as the default forever.

E5 is closed. The bare-`--accepted` guard lives at `Program.cs:576-579`:

```csharp
if (accepted && !json)
{
    return WriteUsageError(json, "bare --accepted is not supported; use --accepted --json");
}
```

`WriteUsageError` (`Program.cs:761`) returns **exit code 2**. Mirror it exactly — same helper, same
exit code, same message shape:

```csharp
if (schema && !json)
{
    return WriteUsageError(json, "bare --schema is not supported; use --schema --json");
}
```

Do not introduce a new error helper or a different exit code.

### 5.5 Exit code and failure modes

`--schema --json` reads no config and probes nothing (same posture as `--accepted`, per
`AcceptedCommand`'s doc comment). **Always exits 0** on success; **2** for the bare-flag usage
error (§5.4) and for a mutual-exclusion violation. There is no other failure mode short of a crash.

`--schema` is mutually exclusive with `--check`, `--version`, `--items`, `--colors`, `--preview`,
and `--accepted`, per the mode-command rule in `Program.cs`.

---

## 6. MCP tool: `get_config_schema`

### 6.1 Signature

```csharp
[McpServerTool(Name = "get_config_schema")]
[Description(
    "The claude-tui-line config schema, read live from the installed binary: every item kind and "
    + "its required/optional keys, every config key's accepted values, the recommended colour "
    + "names, and the structural shape of a config document (root, pane, split, item, colour "
    + "rule). Use this before writing or editing a config instead of consulting documentation — "
    + "documentation can be stale, this is what the binary actually accepts.")]
public static async Task<object> GetConfigSchema(
    [Description("Limit the response to these sections: items, colors, accepted, structures, kindSupport. Omit for all.")]
    string[]? sections = null)
```

Naming: `get_config_schema` matches the existing `get_config` / `set_config` verb_noun convention
in `ConfigTools.cs`.

No `configPath` parameter. The schema is a property of the binary, not of any config file.

**Amendment (task #93, `.claude/hierarchy/specs/schema-mcp-query.md`):** this signature is
superseded. The no-args response is no longer the full envelope — see that spec's §3.1/§3.2 for
the queryable/enumerable `select` parameter and the projected default index.

### 6.2 Behaviour

1. `CliRunner.RunSchemaAsync()` — spawn `<cli> --schema --json`, mirroring `RunCheckAsync`'s
   structure exactly: `CliLocator.Locate()`, `ProcessStartInfo` with `UseShellExecute = false` and
   both streams redirected, `ArgumentList.Add` per argument (never a concatenated command string),
   both streams read to end around `WaitForExitAsync` the same way `RunCheckAsync` does,
   `JsonNode.Parse` in a `try`/`catch` returning null on garbage.
2. CLI not found → return the same shaped "CLI missing" result `get_config` returns, including
   `searchedPaths`. Do not invent a new error shape; copy `ConfigTools.cs`'s existing handling.
3. Non-zero exit, or stdout that does not parse as JSON → return an error object carrying the exit
   code and a short message. **Never return a partial or fabricated schema** — a plausible-looking
   wrong schema is worse than an error, because the caller will act on it.
4. `sections` omitted/null/empty → return the whole envelope. Otherwise return an envelope
   containing `version` plus only the named sections. An unrecognised section name is an **error**
   listing the valid names — not silently ignored, because a silently-dropped section reads to the
   caller as "this schema has no colours". (Same fail-closed shape as §1.1.3's invariant and
   §9.5.1's `PendingForm` ruling: a gap must be stated, because silence cannot be distinguished
   from an omission.)

   **Amendment (task #93, `.claude/hierarchy/specs/schema-mcp-query.md`):** "return the whole
   envelope" no longer describes the no-args default. `sections` gained a sibling, `select`, and the
   no-args response is now a projected default index (prose elided, the 256-entry colour palette
   replaced with a derived omission marker) rather than the raw envelope. Read that spec before
   implementing this step.
5. No separate presence probe. Unlike `get_config` (which probes because it is otherwise
   read-only and would happily serve a config on a machine with no CLI), this tool's *own* spawn is
   the presence check — a successful `--schema --json` is the evidence.

### 6.3 Concurrency / safety

Read-only. No config file is opened, no ledger entry is written, no lock is taken. It does not
interact with `set_config`'s CAS contract in any way.

---

## 7. Verification

Core (`tests/ClaudeTuiLine.Tests/SchemaCommandTests.cs`):

- **V1** — `SchemaCommand.Build()`'s `items`/`colors`/`accepted` sections serialise byte-identically
  to `ItemsCommand.Build()` / `ColorsCommand.Build()` / `AcceptedCommand.Build()` serialised
  standalone. This is the anti-drift test for D3, and D5 (public surface) is why it stays strict.
- **V2** — `kindSupport` has exactly one entry per key in `items.kinds`, no more, no fewer.
- **V3** — reflection over `PaneItemJsonConfig`'s **`[JsonPropertyName]`-carrying** properties
  yields exactly `ModelItemKeys` (set equality, ordinal). Properties without the attribute — i.e.
  `Extra` — are excluded from the comparison, per F9c. This is what makes D4's declared list safe.
- **V4** — for every `structures` entry with a non-null `record`, reflection over that record type's
  `[JsonPropertyName]` values equals the entry's `required ∪ optional` exactly (set equality,
  ordinal).
  - **Exemptions, and only these two:** `compoundPart` (`record: null`, no such type — F9b) and
    `colorExpr` (union with no attributed properties — F9c). Encode the exemption as
    "entries with `record: null` are skipped" plus an explicit allow-list containing only
    `colorExpr`, so a future entry cannot quietly opt out by omitting `record`.
  - The test must also assert the **entry set itself**: exactly the twelve names in §5.3, no more,
    no fewer. Without that, deleting an entry passes trivially.
- **V5** — every non-null `acceptedKey` in `structures[].fields[]` exists in `accepted.keys[].key`.
- **V6** — a `kindSupport` assertion written so that it passes **both** before and after #85:
  assert `compound.supported == !compound.unsupportedKeys.Any()` and
  `compound.unsupportedKeys == (compound's advertised keys minus ModelItemKeys)`. Do **not** assert
  `compound.supported == false` — that would make #85 land as a red test in an unrelated file.
  A separate test may assert the *current* state if it is named to say so and #85's spec is told to
  flip it.

MCP (`tests/ClaudeTuiLineMcp.Tests/GetConfigSchemaToolTests.cs`):

- **V7** — `AllowListTests` V4 and V4b still pass, **unmodified**. If either needs an edit, the
  design has been misread.
- **V8** — with `CliLocator` finding nothing, `get_config_schema` returns the CLI-missing shape
  with `searchedPaths` populated, and does not throw.
- **V9** — `sections: ["accepted"]` returns `version` + `accepted` and nothing else.
- **V10** — `sections: ["colours"]` (an invalid name) returns an error listing valid names.

Manual acceptance, after implementation:

```bash
claude-tui-line --schema --json | jq '.kindSupport, (.structures | map(.name))'
claude-tui-line --schema          # expect: usage error naming --schema --json, exit 2
claude-tui-line --schema --items  # expect: mutual-exclusion error, exit 2
```

Also confirm `tools/check-all.sh` (and any doc-token checker such as `tools/check-doc-tokens.sh`)
still passes — §1.1.3 wired `--accepted --json` into documentation checking, and a new sibling
command must not disturb it. Because D5 makes this a public surface, if that checker maintains a
list of public `--json` commands, `--schema` belongs on it.

---

## 8. NEEDS-EVIDENCE

Two items remain. Both are genuine runtime questions and belong to the Implementor, at
implementation rates. **E3, E4, and E5 are closed** — they were source-reading questions and were
answered by reading source (§5.0, §2 F9 / §5.3, §5.4 respectively).

- **E1 — AOT.** The core publishes with `PublishAot=true`. Confirm `--schema --json` publishes and
  runs: `dotnet publish src/ClaudeTuiLine/ClaudeTuiLine.csproj -c Release`, then run the published
  binary with `--schema --json`.
  - *Passes* → design stands.
  - *Fails with a trimming/serialisation warning* → the new records need their own
    `[JsonSerializable]` source-generated context, as `ItemsJsonContext` / `AcceptedJsonContext` /
    `ColorsJsonContext` do (`ItemsCommand.cs:34-38`). The specific risk is nesting types from three
    existing contexts inside a new envelope record; add `[JsonSerializable(typeof(SchemaResultJson))]`
    and verify the nested types resolve.
  - This spec already avoids the larger AOT hazard by forbidding runtime reflection (§5.2, §5.3).
    Note that V3 and V4 *do* reflect — that is fine and intended, because they are tests, which run
    on the JIT runtime and are never published AOT.

- **E2 — MCP publish still works.** #83 exists because a publish combination failed (NETSDK1151).
  This spec adds no project reference, so it *should* be unaffected — confirm with
  `dotnet publish src/ClaudeTuiLineMcp/ClaudeTuiLineMcp.csproj -c Release`.
  - *Fails* → stop and re-dispatch. A publish regression changes the architecture question, not
    just the implementation.

---

## 9. Decisions taken by the user (formerly open questions)

All three are **closed**. Recorded here because the reasoning matters to anyone reading the spec
later, not because anything is still pending.

**Q1 — the CLI half. RESOLVED: full scope, as designed.** The original dispatch asked for "a NEW
MCP TOOL, not a CLI addition", and delivering the structural half of the ask requires a source of
truth next to `Config.cs` that MCP can only reach by spawning (§2 F3, F5, F6). The user accepted the
design as specced: **both** the verbatim items/colors/accepted embedding **and** the new
`structures` / `kindSupport` sections, via a new CLI subcommand that MCP spawns. The narrower
MCP-only option — spawning the three existing commands and deferring `structures` — was
**rejected**; it would have delivered only the half the user could already get from the CLI.

**Q2 — is `--schema --json` public and frozen? RESOLVED: yes, public.** Same tier as
`--accepted --json`, following task #43's precedent. This is now D5; see there for what it obliges.
The anti-drift tests (V1, V4) stay strict by consequence.

**Q3 — amending §1.1.3's name table. RESOLVED: keep `--schema`, amend the table.** The prior
rejection was scoped to `--accepted`'s flat payload over-promising; this command actually delivers
the full structural schema, so it earns the name. No workaround name is used. §5.0 carries the exact
replacement text for `SPEC-V2-FRAMEWORK.md:470` and the two constraints on that edit.

---

## 10. Risks for the Implementor

- **The verbatim-embedding requirement (D3/V1) is the easy thing to get wrong.** The temptation is
  to flatten or "tidy" the three payloads into the envelope. Do not. Serialise the same record
  instances the existing `Build()` methods return.
- **Do not add a `ProjectReference`.** If the MCP side feels like it wants one, the design has been
  misread — go back to §2 F3.
- **Do not hardcode `compound: false`.** §D4/V6. A hardcoded flag makes #85 a coordination problem;
  a computed one makes it a non-event.
- **Do not invent a split/branch structure entry.** F9a. One `PaneConfig` serves both roles. The
  discriminator goes in `pane`'s description and notes, not in a second entry.
- **`Extra` is not a wire key.** Every model type has one, none of them carries
  `[JsonPropertyName]`, and including it anywhere in `structures` or `ModelItemKeys` would advertise
  a key that means "unknown keys land here". F9c; V3/V4 enforce.
- **`--schema --json` is frozen from day one** (D5). Get the envelope key names and the
  `structures` entry shape right before merging; renaming a field afterwards is a breaking change.
- **#85 is in flight and touches `Config.cs`.** If #85 lands mid-implementation, `ModelItemKeys`,
  V3, and `compoundPart`'s entry will need updating together — that is V3/V4 doing their job, not a
  conflict. Rebase rather than working around it.
- **Payload size.** If `structures` grows large enough that the full response becomes an unpleasant
  context cost, `sections` is the release valve — that is why §6.1 has it. Do not "solve" size by
  truncating.

---

## 11. Confidence

High on the architecture and now on the detail. D2 is close to forced by F3+F5+F6; D4 is a clean
answer to the #85 coordination question; §5.0's naming question and D5's public-surface question
are settled by user decision rather than assumed. §5.3's field-level detail is no longer
placeholder — it is read off `Config.cs` and pinned by V4.

The residual uncertainty is narrow and named: whether `CommandJsonConverter` accepts a bare string
as well as an array (§5.3 tells the implementor to read the converter rather than guess), and E1's
AOT serialisation question, which has a stated remedy if it fails.

**No escalation recommended.** Nothing here is security-sensitive, touches auth or data migration,
or is hard to reverse: the tool is read-only and adds no dependency. The one property worth a second
look at review time is D5's freeze — a public surface is the one thing in this spec that is
genuinely costly to change later, and it is being frozen deliberately rather than by accident.
