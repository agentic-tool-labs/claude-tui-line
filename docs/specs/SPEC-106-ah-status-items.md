# SPEC-106 — `ah` and `ah-short` items: agent-hierarchy status

- **Status:** rev 3. Implemented. No contract or user question is open. E7 (§F.2) was waived by the user (§K).
- **Author:** Architect
- **Request:** `20261005-034321-ogef` (spec-106-ah-status-items), from the claude-tui-line-orchestrator.
  Rev 2 is amendment request `20261005-090048-13hl`. It folds in the upstream answers to CQ1–CQ6
  (agent-tools Architect, `0071` r3.12) and the user's decisions U1 and U2. §D.4 lists every
  section that changed.
  Rev 3 is amendment request `20261005-140139-4qr7`. It rules on review `20261005-105037-1qhl`:
  finding 1 (a symlinked or non-regular `status.json` can block the read), nits 4 and 6, and the
  read buffer's size. §D.5 lists every section that changed.
- **Upstream contract:** agent-tools spec 0071, Phase 2a. That spec's section 7 is the contract this one
  satisfies. The status-file format belongs to `ah`, so this repo does not edit it. Paths (local
  only):
  - `/Users/jimcline/git/repos/agent-tools-0071/agent-hierarchy/docs/specs/0071-hierarchy-status-view.md`
    (section 7 at line 1092, the one consumer rule at line 837, locating the file at line 934, E7 at line 1654);
  - `/Users/jimcline/git/repos/agent-tools-0071/agent-hierarchy/docs/status-file.md`;
  - `/Users/jimcline/git/repos/agent-tools-0071/agent-hierarchy/tests/fixtures/status/*.json`.
- **Prerequisite:** none in this repo. The real producer is `ah` 0.110.0, which is not released yet,
  and nothing here needs it: every test uses the copied fixtures.

Implementer: implementor
Reviewer: reviewer

> Citation note for every `docs/specs/*.md` file: `tools/check-citations.sh` resolves each
> un-backticked numeric `§N.M` against `SPEC-V2-FRAMEWORK.md`. Write upstream (0071) section
> references inside backticks, e.g. `0071 §7.2`. This spec's own sections are lettered (§A–§K),
> and the checker ignores lettered references.

---

## §A — Goal and scope

### A.1 Goal

Add two opt-in builtin items. Both show the live agent-hierarchy summary that `ah` precomputes
into `<git root>/.claude/hierarchy/status.json`:

| id | value (raw, plain) | default render | example |
|---|---|---|---|
| `ah` | the current timeline entry's `text` | `ah: ` + text, all in the tone colour | `ah: 2 live · 1 out · 1 blocked` |
| `ah-short` | the current entry's `short` | `ah: ` + short, all in the tone colour | `ah: 2/1 1b` |

The reader is purely a consumer. It never works out eta, stall, liveness, counts or text. It
picks the timeline entry that is current at `now` and shows it.

**Who sees it (CQ1, settled).** The only session test is membership. A session whose id is in
`member_sessions` sees nothing. Every other session in the checkout sees the item: the
Orchestrator's, plain sessions, and `--agent` sessions that are on no team. The status file names
no "Orchestrator" identity, so the item is not Orchestrator-only in the strict sense. If `ah`
ever widens `member_sessions`, it does so under `schema: 1`, and this reader needs no change.
Whether to widen it is a parked user question for agent-tools, not for this spec.

### A.2 Out of scope

- Any change to `ah`, to the status-file format or to the fixtures' content.
- A daemon, a cache, a subprocess, a `node`/`herdr` call, a network call, any filesystem write.
- A new priority or overflow mechanism for narrow panes.
- `AGENT_HIERARCHY_DIR` and `ah`'s fallback directory for non-git trees. This is a deliberate
  ceiling, shared with the `ah` mod; see `0071 §6.2`. CQ2 confirms it.
- Hiding the item by `StatusInput`'s agent name, by role, or by any other heuristic (CQ1).

### A.3 Mapping to the upstream section 7

| Upstream | Here |
|---|---|
| `0071 §7.1` Items | §C.1, §C.5 |
| `0071 §7.2` Data | §C.2, §C.3, §C.4, §C.6 |
| `0071 §7.3` Config | §C.7 |
| `0071 §7.4` Safety | §C.8 |
| `0071 §7.5` Narrow widths | §C.9 |
| `0071 §7.6` Tests | §E |
| `0071 §7.7` House rules | §G |
| `0071 §4.4` one consumer rule | §C.3 |
| `0071 §6.2` locating the file | §C.2 |
| E7 | §F.2 |

---

## §B — Baseline (verified by reading, 2026-10-05, main @ `eddca0c`)

- Registry: `ItemDefinition(Id, Reports, ResolveValue, BuildDefaultSegment, ColorKind, DefaultLinkTemplate?)`
  in `src/ClaudeTuiLine/ItemRegistry.cs`. `DefaultIds` (`ItemRegistry.cs:213-216`) is an
  **exclusion list**: a new row is in the default set unless its id is added to that `Where`.
- Resolution is already lazy by reference. `ItemValueResolver.Resolve`/`ResolveAsync`
  (`ItemValueResolver.cs:46-51`, `:83-88`) call `ResolveValue` only for ids that the config
  places or references (`CollectIds`). `BuildDefaultSegment` runs only for placed items. So an
  unplaced `ah` costs nothing, provided the probe is invoked only from those two delegates.
- `ItemContext` (`src/ClaudeTuiLine/ItemContext.cs:34`):
  `ItemContext(StatusInput input, string? gitBranch, EngramProbeResult? engram, Func<string?> remoteUrlProbe, ItemSettingsJsonConfig? itemSettings = null)`.
  The lazy precedent is `_remoteUrl = new Lazy<string?>(remoteUrlProbe, LazyThreadSafetyMode.None)`.
  It is constructed at 43 sites. Only two are production: `Program.cs:78` (the stdin render,
  `RunAsync`) and `Program.cs:365` (`RunPreview`). There is also `SyntheticFixture.cs:56-58`. The rest are tests.
- `StatusInput` has `Cwd` (`cwd`) and `SessionId` (`session_id`) (`StatusInput.cs`).
- There is no clock abstraction. Production code uses `DateTimeOffset.UtcNow` directly, for
  example `Program.cs:56` passes it to `EngramProbe.BuildAsync`.
- The label precedent is `SegmentBuilder.LabeledSegment(format, value, color, settings)`
  (`SegmentBuilder.cs:290-323`):
  - with `showLabel:false`, the value alone renders in `color`;
  - with no `labelColor`, the whole formatted string renders in `color`;
  - with a `labelColor`, the label renders in it and the value in `color`.
  `BuildAutocompact`/`BuildOutputStyle` (`:327-341`) are its callers, with `"autocompact:{}"` and
  `"style:{}"`.
- Escaping is central. `SingleColor` and `BuildSpanMarkup` (`SegmentBuilder.cs:36-37`, `:93-97`)
  call `Markup.Escape` on the text. They do **not** validate the colour tag; it is interpolated raw.
- The test pattern for pure resolvers is `SegmentBuilder.ResolveAutocompact(projectDir, homeDir, envWindow)`,
  a pure overload that tests drive with temp dirs (`SegmentBuilderTests.cs:20-32`). The
  `ResolveAutocompact(ItemContext)` wrapper feeds it real inputs.
- Source-generated JSON contexts: `StatusInputJsonContext` (`StatusInput.cs:154-158`) and
  `ConfigJsonContext` (`Config.cs:747-771`). `<PublishAot>true</PublishAot>`
  (`src/ClaudeTuiLine/ClaudeTuiLine.csproj:10`), target framework `net10.0`.
- Config precedent:
  - `LabeledItemSettings` (`Config.cs:83-96`; `ShowLabel`, `LabelColor`, `Extra`) is inherited by
    `OutputStyleItemSettings` and `AutocompactItemSettings` (`Config.cs:133`, `:137`);
  - `EngramItemSettings.StateColors` → `EngramStateColorsJsonConfig` (`Config.cs:143-222`);
  - the colour checks are at `ConfigCheck.cs:727-748`;
  - the schema entries are `engramItemSettings`/`engramStateColors` (`SchemaCommand.cs:437-490`).
- `ColorResolution.ResolveLiteral` (`ColorResolution.cs:212-216`) uses `Style.TryParse`, and
  accepts `grey`.
- `--items` builds every registry row against `SyntheticFixture.CreateItemContext()`
  (`ItemsCommand.cs:55-62`). Its `example` is `BuildDefaultSegment(ctx)?.Plain`.
  `tools/check-examples.sh` checks the README against that output:
  - rule C is the items table under the `<!-- items-table: … -->` marker (`README.md:318`), and it
    is checked for completeness;
  - rows look like ``| `model-short` | abbreviated model name *(opt-in)* |``.
- Test fixtures under `tests/ClaudeTuiLine.Tests/fixtures/**` are already copied to the output
  directory (`ClaudeTuiLine.Tests.csproj:25-27`). No csproj change is needed.
- Truncation: `SegmentTruncation.cs:14-43` appends `…` when it fits
  (`contentBudget = innerWidth - ellipsis.Length`) and hard-clips otherwise. A null item is
  omitted with its separator (`PaneAssembler.cs:140-143`). The test render entry point for one
  pane at a width is `PaneAssembler.RenderLeafRows` (`NarrowSplitPaneTests.cs:21-32`).
- Version `0.5.0` sits at:
  - `src/ClaudeTuiLine/ClaudeTuiLine.csproj:15`;
  - `src/ClaudeTuiLineMcp/ClaudeTuiLineMcp.csproj:12`;
  - `src/ClaudeTuiLineShared/ClaudeTuiLineShared.csproj:10`;
  - `.claude-plugin/plugin.json:4`.
  `marketplace.json` has no version.
- `bench/bench.sh` has these limits:
  - it hardcodes `NEW_BIN=$REPO_DIR/publish/claude-tui-line` and compares it against the old bash
    script `~/.claude/statusline-command.sh`;
  - it reports the mean or median only, has no p95, and takes no env overrides.
  The config path can be overridden with `CLAUDE_TUI_LINE_CONFIG` (`ClaudeTuiLineShared/ConfigPath.cs:27-30`).
  The cache dir can be overridden with `CLAUDE_TUI_LINE_CACHE` (`ItemCache.cs:69`).

---

## §C — Design

### C.1 Registry rows

Append two rows at the end of `ItemRegistry`'s list, `ah` first. Add both ids to the
`DefaultIds` exclusion `Where` (`ItemRegistry.cs:214`).

| field | `ah` | `ah-short` |
|---|---|---|
| `Reports` | `"agent-hierarchy status for this repo's live teams — live members, work out, and blocked/overdue/stalled counts — read from .claude/hierarchy/status.json, which agent-hierarchy keeps current; hidden in the sessions of live team members"` | `"an abbreviated ah status, for panes too narrow for the full one"` |
| `ResolveValue` | the current entry's `text`, or null | the current entry's `short`, or null |
| `BuildDefaultSegment` | §C.5 with `text` | §C.5 with `short` |
| `ColorKind` | `Semantic` | `Semantic` |
| `DefaultLinkTemplate` | none | none |

**One resolve path.**
- Both rows read the same memoized current entry from `ItemContext` (§C.4).
- Their resolve and build functions in `SegmentBuilder.cs` are one implementation, parametrised
  only by which string field of the entry they take. There are not two copies.
- An empty or whitespace field resolves to null, the same as a missing one. A null value hides
  the item.

### C.2 Locating the file (`0071 §6.2`)

Input: `StatusInput.Cwd`.

1. If `Cwd` is null, empty or not an absolute path, there is no file.
2. Walk from `Cwd` up through each parent to the filesystem root. Stop at the **first** directory
   that contains an entry named `.git`, whether it is a directory or a file.
   - A linked worktree's or a submodule's `.git` **file** stops the walk there. The walk does
     not continue to an outer repository.
   - A directory that does not exist is not an error; it simply has no `.git`.
3. The file is `<that directory>/.claude/hierarchy/status.json`.
4. If no `.git` is found up to the root, there is no file.

The walk checks existence only. It never reads `.git`, never follows a `gitdir:` pointer and
never runs `git`.

**CQ2, settled.** `ah`'s writer uses this same rule. A linked worktree is its own pool, and `ah`
writes `<worktree>/.claude/hierarchy/status.json` there. There is no fallback to the main
checkout. A worktree with no pool has no file, so the item shows nothing. The reader does not
honour `AGENT_HIERARCHY_DIR` or `ah`'s non-git fallback (§A.2).

### C.3 Reading and selecting (the one consumer rule, `0071 §4.4`)

Inputs: the located path, the viewing session id (`StatusInput.SessionId`), and `now`
(a `DateTimeOffset`). Output: the current entry, or null. **It never throws.** Any exception
inside the probe yields null. A catch-all at the probe boundary is intended here: a sidecar file
must never break the statusline.

The document is **absent** (null) when any of these holds. The field rows are exact: CQ4 and
CQ5 settled them upstream (rev 2). The file rows are this reader's reading of "unreadable" (rev 3).

| condition | note |
|---|---|
| no file located, or nothing exists at the path | |
| the path is not a non-empty regular file, judged **before opening it** and **without following a final symbolic link**: it is a directory, a symbolic link (to anything: a valid file, a directory, a device, or nothing), or its size is 0 | Rev 3. Git can store a symlink, so a hostile repo can commit `status.json` → `/dev/tty` (reading blocks on, and consumes, terminal input) or → a FIFO (`open(2)` blocks). FIFOs, sockets and device nodes report size 0, so the size-0 rule keeps `open` from blocking on one placed directly at the path. An empty regular file is invalid JSON anyway, so no valid document is lost. `ah` writes by rename, so a real `status.json` is never a symlink. **Only the final component is judged:** parent directories may be symlinks (macOS `/tmp` and `/var` are), and the check must not resolve or reject them. |
| the open fails, or the opened file is not seekable | A FIFO that gets past the pre-open check (a race, or a FIFO with a writer attached) is not seekable, so its size cannot be taken from the handle. It is never read. |
| size > 262,144 bytes | Exactly 262,144 is read. The size is the **opened file's own length, taken from the open handle**, and it is checked before any byte is read or parsed. Rev 3: the read buffer is exactly that size, and the read stops at that size or at end of file, whichever comes first; the bytes actually read are parsed. A size taken from the path before opening may serve as an early-out, but the handle's size decides, because `ah` replaces the file by rename and the path can name a newer file by the time it is opened. A file changed in place mid-read (`ah` never does this) yields torn bytes, which fail to parse and so are absent. |
| not valid JSON | |
| `schema` missing, not a JSON number, ≠ `1`, or not written as the integer `1` | e.g. `"schema": "1"`; `schema: 2` is absent by contract. Rev 3 (nit 4): `1.0` and `1e0` are absent too. This is stricter than numeric equality, deliberately: `ah`'s writer only ever emits `1`, the reader may hold `schema` as an integer, and the strictness fails toward hidden. |
| `expires_at` missing, not a string, not ISO-8601, or `now ≥ expires_at` | equality counts as expired |
| `member_sessions` missing, not an array, or any element not a string | Missing is **absent, not empty**: it fails toward showing nothing. An empty array `[]` is valid. |
| `timeline` missing, not an array, or empty | |
| any `timeline` element whose `at` is missing, not a string, or not ISO-8601 | every entry's `at` is checked, not only the picked one; an element that is not an object has no `at` |
| in the **picked** entry (step 1): `visible` missing or not a boolean; `tone`, `text` or `short` missing or not a string | the four are checked on the picked entry only |

Throughout, a JSON `null` counts as missing. Nothing else is read or checked: `enabled`,
`written_at`, `teams`, the per-entry counts, and every field of a non-picked entry except `at`.
A wrong type in one of those does **not** make the doc absent. Unknown fields are ignored,
because adding a field does not change `schema`.

Otherwise:

1. **Pick the entry.** The current entry is the **last** entry, in array order, whose
   `at ≤ now`. If no entry qualifies (`now` is before all of them), use the **first** entry.
   Equality qualifies.
2. **Check the picked entry** against the last row of the table. A failure makes the doc absent.
3. **Hide** (null) if the entry's `visible` is `false`.
4. **Hide** (null) if `SessionId` is non-empty and appears in `member_sessions`. The comparison
   is ordinal. A null or empty `SessionId` is never a member. **This is the only session test
   (CQ1).** No agent name, role or other `StatusInput` field is consulted. The read-and-select
   step does not take one as input, so it cannot.
5. Return the entry's `tone`, `text` and `short`, unmodified. Do not trim, re-format or
   recompute them.

- `tone` may be any string (CQ3: tone values are open under `schema: 1`). An unknown string
  still shows, in the idle colour (§C.5). A non-string `tone` is malformed, so the doc is absent
  (table).
- An empty or whitespace `text` or `short` is still a string, so the doc is present. Only the
  item that reads that field resolves to null (§C.1). The other item still shows.

**Parsing.**
- Use a new `JsonSerializerContext` subclass with `[JsonSerializable]` over a minimal DTO
  containing only the fields above. Snake_case names go through `[JsonPropertyName]`, as in
  `StatusInput`.
- Timestamps deserialize as `DateTimeOffset`. ISO-8601 UTC with milliseconds parses natively.
  System.Text.Json's `DateTimeOffset` reader accepts only ISO-8601, which makes it the
  "not ISO-8601" check. Do not add a culture-based or lenient parse fallback.
- Keep System.Text.Json's strict defaults. Do not enable `AllowReadingFromString` number
  handling, because `"schema": "1"` must stay absent. Do not set
  `UnmappedMemberHandling.Disallow`, because unknown fields must be ignored.
- **Picked-entry-only typing.** A wrong-typed `visible`, `tone`, `text` or `short` in a
  **non-picked** entry must not make deserialization fail. For example, in a valid `work.json`
  with the first entry's `tone` changed to `5`, `now = 12:05` still yields the second entry.
  How the DTO achieves this is the Implementor's call; one option is to hold those four as
  `JsonElement` and check their kinds after the pick. Both `at` and every top-level field the
  table reads may be strongly typed, since a type error there is meant to fail.
- Deserialize from the byte buffer, with no string round-trip.
- Do not call the reflection-based `JsonSerializer` overloads, because AOT needs the
  source-generated context.

**Where it lives.**
- The new file `src/ClaudeTuiLine/HierarchyStatus.cs` holds the DTO, the JSON context, the
  locate step, the read-and-select step, and the small current-entry record (tone, text,
  short).
- The read-and-select step must be callable on its own, with an explicit path (or cwd), session
  id and `now`, so that tests can drive it with temp files and a pinned time. This follows the
  `ResolveAutocompact(projectDir, homeDir, envWindow)` precedent.

### C.4 `ItemContext` seam, laziness, clock (`0071 §7.2`)

- `ItemContext` gains **one optional trailing constructor parameter**: a delegate that produces
  the current entry or null.
  - It is stored as a `Lazy<…>` with `LazyThreadSafetyMode.None`, exactly like `_remoteUrl`, and
    exposed as a read-only property.
  - **When the parameter is omitted, the entry is null.** Nothing touches the filesystem. This
    keeps all 40-odd test construction sites unchanged and hermetic.
  - The repo's own `.claude/hierarchy/` exists, and once `ah` 0.110.0 is installed it will hold a
    live `status.json`. A default real probe would make tests read it.
- `Program.cs:78` and `Program.cs:365` pass the **real** probe. That delegate:
  1. locates the file from `input.Cwd` (§C.2);
  2. reads and selects (§C.3) with `input.SessionId` and `DateTimeOffset.UtcNow`, read at the
     moment the delegate runs.

  That delegate **is** the injectable clock. Production binds it to `UtcNow`, and tests build
  the same delegate over the same read-and-select step with a pinned `now`. No `TimeProvider`
  or clock interface is added.
- `SyntheticFixture.CreateItemContext` passes a delegate that returns a canned entry: tone
  `warn`, text `2 live · 1 out · 1 blocked`, short `2/1 1b`. These are the `warn.json` fixture's
  values. `--items` therefore never reaches the filesystem, and its examples become
  `ah: 2 live · 1 out · 1 blocked` and `ah: 2/1 1b`.
- Memoization: with `ah` and `ah-short` both placed, and both `ResolveValue` and
  `BuildDefaultSegment` running, the probe delegate runs **once** per render. With neither id
  placed or referenced, it runs **zero** times. §E.2 tests both counts.

### C.5 Rendering (`0071 §7.1`)

- **Segment:** `SegmentBuilder.LabeledSegment(format, value, toneColour, <the item's own settings>)`
  with format `"ah: {}"` for **both** items. This is the house `label:value` form with one space
  after the colon (user decision U1). `ah` passes its `itemSettings` block and `ah-short` passes
  its own (user decision U2, §C.7). Neither item reads the other's block.
  - Default: `[<toneColour>]ah: <value>[/]`, with the value escaped.
  - `showLabel:false` in that item's block: `<value>` alone, in the tone colour.
  - `labelColor` set in that item's block: `ah: ` in the label colour, then the value in the tone
    colour (the existing `LabeledSegment` behaviour).
- **Tone → colour.** For tone `t` in {`work`, `warn`, `bad`, `idle`}:
  - use the configured `stateColors.<t>` **from the rendering item's own block** if it is
    non-empty **and** `ColorResolution.ResolveLiteral` accepts it;
  - otherwise use the default: `work`=`blue`, `warn`=`yellow`, `bad`=`red`, `idle`=`grey`.

  The two items share one implementation of this mapping, parametrised by the settings block.
  They do not each have a copy.

  An invalid configured colour falls back to that tone's default at render time. `--check`
  reports it separately (§C.7). The fallback is required because `SingleColor`/`BuildSpanMarkup`
  interpolate the tag unvalidated. A bad tag would otherwise produce invalid Spectre markup and
  could throw during the render.
- **Unknown tone.** A string tone outside the four, including one in a different case such as
  `"Work"`, renders with the **`idle`** colour (that item's configured `idle`, or the default)
  and still shows. CQ3 settled this. A missing or non-string `tone` never reaches rendering,
  because it makes the doc absent (§C.3).
- **ColorKind `Semantic`.** An item-level `color` on a placed `ah` does not override the tone
  colour; that is the existing Semantic nesting. Users recolour through `stateColors`. A colour
  `match` rule still works wherever a rule reads `ah`'s **raw value**, for example a border or a
  `colors` token with `from: ah` and `match` on `blocked`. That is why the raw value is the plain
  `text` (`0071 §7.1`).
- No `link`.

### C.6 SyntheticFixture and `--items`

§C.4 covers this. The canned entry is the only data `--items` sees. The README table and
`check-examples` rule C must agree with `ah: 2 live · 1 out · 1 blocked` / `ah: 2/1 1b`.

### C.7 Config (`0071 §7.3`)

**Each item has its own block (user decision U2).** `itemSettings.ah` governs only `ah`.
`itemSettings.ahShort` governs only `ah-short`. A key set in one block never affects the other
item. The key name follows the house convention for hyphenated ids, which is camelCase:
item `output-style` uses settings key `outputStyle` (`Config.cs:68-69`, `SchemaCommand.cs:362`).

Configuration file shape:

```json
"itemSettings": {
  "ah": {
    "showLabel": true,
    "labelColor": "grey",
    "stateColors": { "work": "blue", "warn": "yellow", "bad": "red", "idle": "grey" }
  },
  "ahShort": {
    "showLabel": false,
    "stateColors": { "bad": "magenta" }
  }
}
```

Both blocks have the same three optional keys: `showLabel`, `labelColor` and `stateColors`.
Each falls back independently to the defaults: `showLabel` true, no `labelColor`, and the
§C.5 tone colours.

- **`Config.cs`:**
  - add **one** settings class deriving from `LabeledItemSettings`. It inherits `showLabel`,
    `labelColor` and `Extra`, so unknown keys are reported. It has one property, `stateColors`;
  - add a state-colours class with four nullable strings `work`/`warn`/`bad`/`idle`, modelled on
    `EngramStateColorsJsonConfig`. All four are null by default: the §C.5 defaults live in the
    render code, not in the config object, which matches engram's convention;
  - add **two** properties on `ItemSettingsJsonConfig`, JSON keys `ah` and `ahShort`, both of
    that one settings type. It is one type used twice, not two classes;
  - add `[JsonSerializable]` entries for the two new types to `ConfigJsonContext`.
- **`SchemaCommand.cs`:**
  - add structure `ahItemSettings`. Optional keys: `showLabel`, `labelColor`, `stateColors`.
    Description: "Settings for one agent-hierarchy status item. The ah and ah-short items each
    have their own block. Reading the status file costs one small file read per render, and only
    when an ah item is placed or referenced. Nothing runs in the background.";
  - add structure `ahStateColors`. Optional keys: `work`, `warn`, `bad`, `idle`. Description:
    "Colours for an ah item, by the status tone. Defaults: work blue, warn yellow, bad red, idle
    grey.";
  - add `ah` and `ahShort` to the `itemSettings` structure's optional keys (`SchemaCommand.cs:353`),
    with fields `Field("ah", "ahItemSettings", "Settings for the ah item.")` and
    `Field("ahShort", "ahItemSettings", "Settings for the ah-short item.")`. Both point at the
    one structure;
  - **mechanical fallback:** if the schema builder or its tests reject a structure that two
    fields reference, add `ahShortItemSettings` as an identical copy of `ahItemSettings` and point
    `ahShort` at it. This is not a spec gap; do not report back for it. Note it in the report;
  - follow the field and description shape of `engramItemSettings`/`engramStateColors` exactly.
- **`ConfigCheck.cs`:** write **one** validation routine over a settings block and its
  JSON-pointer base, and run it for both `/itemSettings/ah` and `/itemSettings/ahShort`. Do not
  write two copies. For each block it checks:
  - `stateColors.{work,warn,bad,idle}`, modelled on `ConfigCheck.cs:727-748`: each non-empty
    value that `ColorResolution.ResolveLiteral` rejects produces `UnknownColor` at
    `<base>/stateColors/<tone>`;
  - `labelColor`, in the same shape as the autocompact check at `ConfigCheck.cs:616-618`:
    `UnknownColor` at `<base>/labelColor`;
  - rev 3 (nit 6, ruled in): unknown keys inside `stateColors`, such as `blocked` or `Work`, get
    the existing `unknown-key` diagnostic. Use the same unknown-key walker registration
    (`WalkRawObjects`) that already reports unknown keys at block level, adding the `stateColors`
    object of each block. Without it, a typo there is ignored silently: the item keeps the
    default colour and `--check` says nothing. Do **not** extend this to engram's `stateColors`,
    which stays as it is.
- **README:**
  - add two table rows under the marker at `README.md:318`. The form follows
    ``| `ah` | agent-hierarchy status: live, out, blocked/overdue/stalled *(opt-in)* |`` and
    ``| `ah-short` | abbreviated agent-hierarchy status *(opt-in)* |``. Place them where the
    rule-C ordering expects; if the rule is order-sensitive, follow the registry order;
  - under `### Item settings` (`README.md:357`), add a short passage on `itemSettings.ah` and
    `itemSettings.ahShort`. It covers:
    - the keys and their defaults;
    - that each item has its own block;
    - the three hide rules: no file or doc absent, entry not visible, session in
      `member_sessions`;
    - who sees it (§A.1): every session in the checkout except live team members', so the
      Orchestrator's session, plain sessions and `--agent` sessions on no team.

    Do not say "only in the Orchestrator's session".

### C.8 Safety (`0071 §7.4`)

- All text from the file reaches markup only through `LabeledSegment` → `SingleColor`/`BuildSpanMarkup`.
  Both already call `Markup.Escape`. No other path may interpolate `text`, `short` or `tone`
  into markup.
- `tone` is never used as a colour tag. It only selects one of four keys (§C.5).
- A `text` of `[red]x[/]` renders literally as `[red]x[/]`, and building the `Markup` does not
  throw (§E.2).
- The file is never written, created or locked. It is read with a bounded buffer.
- Rev 3: the probe never opens anything at the status path that is a symbolic link, a
  directory, or empty, and never reads from an opened file that is not seekable (§C.3). A repo
  cannot make the statusline block on a terminal, a FIFO or a device. The parent directories
  are not checked, and this is deliberate: a repo cannot create a FIFO or device named
  `status.json` elsewhere on the disk for a parent symlink to reach.
- Rev 3, accepted ceiling: the pre-open check and the open are two steps. A process running
  as the user that swaps the file for a FIFO between them can still block one render. That
  process already controls the account. Closing the gap needs an `O_NOFOLLOW | O_NONBLOCK` open
  through P/Invoke, which this spec does not add.

### C.9 Narrow widths (`0071 §7.5`)

- `ah` has no special handling. In a too-narrow pane it is cut by that pane's existing overflow
  mode. For `Truncate` that means `SegmentTruncation`'s ellipsis rule.
- `ah-short` is the narrow form. With single-digit counts its maximum is `ah: 9/9 9b 9o 9s` at
  16 cells. The fixtures produce 7 to 13 cells. Wider values are cut by the normal rules.
  `showLabel:false` in `ah-short`'s own block removes the 4-cell `ah: ` label.
- There is no new priority mechanism, and no automatic switch from `ah` to `ah-short`. The user
  places `ah-short` in a narrow pane, as with `model-short`.

---

## §D — Decisions

### D.1 Made here, with rationale

- **The default probe is null.** The real probe is wired only at `Program.cs:78` and `:365`.
  This keeps tests hermetic and avoids touching 40-odd test sites.
- **The delegate is the clock.** The repo has no clock abstraction, and the pure read-and-select
  step taking `now` is the same pattern as `ResolveAutocompact`'s explicit inputs. No
  `TimeProvider` is added (YAGNI).
- **Malformed is all-or-nothing.** Any defect in a field the reader reads makes the whole
  document absent, never a partial render. CQ4 fixed the exact field list (§C.3).
- **Unknown tone falls back to idle and still shows.** Hiding would turn a forward-compatible
  addition into a blank item. CQ3 confirms that tone values are open under `schema: 1`.
- **Invalid configured colour falls back to the default.** This prevents a markup exception
  caused by config.
- **No cache.** One process renders once, and the `Lazy` gives one parse per render. Upstream's
  E7 fallback, "cache the parsed doc by mtime in-process", buys nothing in a process-per-render
  binary. If E7 fails, return to the Architect (§F.2).
- **E7 method.** `bench/bench.sh` cannot run E7 as it stands. It is old-bash-vs-`publish/` only,
  with no p95 and no config override, and `publish/` is off limits. §F.2 therefore runs the same
  calibrated hyperfine discipline directly and leaves `bench.sh` untouched. Upstream accepted
  this, with conditions (CQ6); §F.2 carries them.
- **Rev 3: the read requires a non-empty regular file, not merely a non-symlink.** "Not a
  symlink" alone closes the hostile-repo route, because git stores only regular files and
  symlinks. A FIFO placed locally would still block `open`, though. .NET has no public pre-open
  "is a regular file" test on Unix. The behaviour is reached with what it does report: the path's
  own metadata (link flag, directory, size 0) before opening, then seekability and length on
  the handle. Every rule fails toward hidden, and no valid document is ever rejected by them.
- **Rev 3: the buffer is the opened file's exact size.** The fixed 262,145-byte buffer was a
  large-object allocation, zeroed on every render, to read a document that is usually under
  8 KB. Sizing from the handle makes the per-render cost proportional to the file and obviously
  small, without needing a measurement. `ArrayPool` is not used: a process that renders once
  starts with an empty pool, so renting buys nothing.

### D.2 User decisions (settled, rev 2)

- **U1 — label form.** The house `label:value` form, with one space after the colon:
  `ah: 2 live · 1 out` and `ah: 2/1 1b`. It is neither `ah 2 live` nor `ah:2`. The format
  constant is `"ah: {}"` for both items (§C.5).
- **U2 — separate settings.** `ah-short` has its own block, `itemSettings.ahShort`, with its own
  `showLabel`, `labelColor` and `stateColors`. `itemSettings.ah` does not govern `ah-short`
  (§C.5, §C.7).

### D.3 Contract answers (settled upstream, `0071` r3.12; do not reopen)

| # | answer | where it lands |
|---|---|---|
| CQ1 | Membership test only. Every session not in `member_sessions` sees the item. No agent-name or role heuristic. Widening the set is a parked user question for agent-tools, and would happen under `schema: 1` with no change here. | §A.1, §A.2, §C.3 step 4, §C.7 README, §G, §H |
| CQ2 | The writer uses the same walk-up rule. A linked worktree is its own pool, with no fallback to the main checkout. No `AGENT_HIERARCHY_DIR` and no non-git fallback in readers. | §C.2 |
| CQ3 | Tone values are additive under `schema: 1`. An unknown string tone gets the idle colour. A non-string tone is malformed. | §C.3, §C.5 |
| CQ4 | Exact field list. Absent on a missing field, a wrong JSON type or a non-ISO-8601 timestamp in: `schema`, `expires_at`, `member_sessions` (array of strings; missing is absent, not empty), `timeline` (non-empty, every `at`), and the picked entry's `visible`, `tone`, `text` and `short`. Unread fields are not checked. | §C.3, §E.2 |
| CQ5 | 262,144 bytes. Larger is absent; exactly 262,144 is read. The size is checked before parsing. | §C.3, §E.2 |
| CQ6 | Direct hyperfine on a Release build outside `publish/` is accepted, with conditions: A/A first, ≥ 200 runs per arm with warm-up, a doc that really reaches the present path, p50 and p95 per arm. Over 1 ms goes back to agent-tools with a read/parse/render split before any cache. | §F.2 |

### D.4 Rev 2 change list

- Label `ah ` → `ah: ` everywhere (U1): §A.1, §C.4, §C.5, §C.6, §C.9, §E.
- `ah-short` gets its own `itemSettings.ahShort` block (U2): §C.5, §C.7, §E.2, §E.4, §E.5.
- Who sees it is membership-only, and the "Orchestrator-only" wording is gone (CQ1): §A.1, §C.1
  `Reports`, §C.3, §C.7 README, §G release notes, §H.
- The absent rules now use the exact CQ4 list:
  - a missing `member_sessions` is now absent (it was treated as empty);
  - a missing or non-string `tone` is now absent (it fell back to idle);
  - a missing `text` or `short` now makes the doc absent (it hid one item);
  - type checks apply to the picked entry only.

  Tests were added for each in §E.2.
- Narrow test width 12 → 13, because `ah: 2/2 1o 1s` is 13 cells: §E.1.
- E7 (CQ6):
  - the A/A rule is now `< 0.5 ms`, else more runs, else inconclusive;
  - a present-path sanity check runs before timing;
  - p50 and p95 per arm;
  - over 1 ms, the split is read/parse/render with named arms: §F.2.

### D.5 Rev 3 change list (review `20261005-105037-1qhl`)

- **Finding 1, non-regular file (spec-defect):** §C.3 absent table, two new file rows. The path
  must be a non-empty regular file, judged before opening without following a final symlink.
  An opened file that is not seekable is never read. §C.8 states the guarantee and the race
  ceiling, §D.1 the rationale, §E.2 the tests, and §I the risks.
- **Buffer:** §C.3 size row. The size comes from the open handle, and the buffer is exactly that
  size. This replaces the fixed 262,145-byte buffer. The CQ5 boundary is unchanged, and so are
  its tests.
- **Nit 4, ruled in as documentation only:** §C.3 `schema` row. The integer `1` only; `1.0` and
  `1e0` are absent. Behaviour is unchanged. One §E.2 case pins it.
- **Nit 6, ruled in:** §C.7 `ConfigCheck`. Unknown keys under each block's `stateColors` are
  reported. One §E.4 case per block covers it.
- §E.4: the "unverified assumption" about `itemSettings["ah-short"]` is replaced by the
  review's verification that the condition holds.
- §F.2: a note on waiving E7.
- Not changed: findings 2, 3, 5 and 8 are implementation defects, already routed to the
  Implementor. Nit 7 is out of scope (§H keeps `LabeledSegment` unchanged).

---

## §E — Tests (`0071 §7.6`)

xUnit, in the new file `tests/ClaudeTuiLine.Tests/HierarchyStatusTests.cs`, plus the updates in
§E.5.
- Temp directories follow the `SegmentBuilderTests.cs:20-32` pattern: `Path.GetTempPath()` plus
  a GUID, cleaned up afterwards.
- No test constructs the real `Program` probe, and no test uses the repo's own directory as `cwd`.
- `T0` = `2026-01-01T12:00:00.000Z`, the instant at which every fixture was generated.

### E.1 Shared vectors

- Copy all seven files **byte-identical** from
  `agent-tools-0071/agent-hierarchy/tests/fixtures/status/` into
  `tests/ClaudeTuiLine.Tests/fixtures/ah/`: `bad`, `hidden`, `idle`, `member-session`,
  `pipeline`, `warn`, `work` (`.json`). This is a cross-repo copy of test data, pinned to
  `schema: 1`.
- One test asserts that every copied file has `"schema": 1`, so a regenerated schema-2 copy fails
  loudly here.
- Each vector test stages `<tmp>/repo/.git/` (a directory) plus
  `<tmp>/repo/.claude/hierarchy/status.json` (the fixture). It then renders through
  `PaneAssembler.RenderLeafRows`, as `NarrowSplitPaneTests.cs:21-32` does, with an `ItemContext`
  whose probe delegate is the real locate-and-read step with a **pinned** `now`.

**Expected renders at `now = T0`.** "Normal" is a single-item `Truncate` pane 120 cells wide.
"Narrow" is the same pane 13 cells wide; rev 1 used 12, but the `ah: ` label makes the widest
fixture's short form 13 cells. Session id `sess-orch` unless stated. No `itemSettings`, so colours
are the defaults.

| fixture | `ah` normal | `ah-short` normal and narrow (13) | colour |
|---|---|---|---|
| `idle` | `ah: 2 live · 0 out` | `ah: 2/0` | grey |
| `work` | `ah: 1 live · 1 out` | `ah: 1/1` | blue |
| `warn` | `ah: 2 live · 1 out · 1 blocked` | `ah: 2/1 1b` | yellow |
| `bad` | `ah: 2 live · 2 out · 1 overdue · 1 stalled` | `ah: 2/2 1o 1s` (exactly 13, fits) | red |
| `pipeline` | `ah: 2 live · 1 out` | `ah: 2/1` | blue |
| `hidden` | **hidden** (no segment, no separator) | **hidden** | — |
| `member-session`, session `sess-demo-reviewer` | **hidden** | **hidden** | — |
| `member-session`, session `sess-orch` | `ah: 1 live · 1 out` | `ah: 1/1` | blue |
| `member-session`, session null | `ah: 1 live · 1 out` | `ah: 1/1` | blue |

The `member-session` rows are the whole CQ1 behaviour: a member is hidden and everyone else sees
the item. There is no row for "a non-Orchestrator session that is not a member", because the
reader cannot tell one from the Orchestrator, and must not try.

**`ah` at narrow (13).** The expected string is whatever the existing truncation produces. The
test pins that literal after checking three properties:
- it is at most the pane's inner width in cells;
- it is a prefix of the normal plain text followed by `…`, or a hard clip if `…` does not fit;
- its markup keeps the tone colour.

Hidden fixtures are hidden at every width. This test proves that `ah` goes through the normal
overflow path; it does not specify a new truncation.

### E.2 Reader unit tests (read-and-select step, explicit `now`)

Unless stated, each case below is `work.json` with one change, at `now = T0`.

**Absent cases** (each yields null):
- a git root with no `status.json`;
- `status.json` that is a directory;
- malformed JSON (`{`);
- `"schema": 2`;
- `schema` missing;
- `"schema": "1"` (a string);
- `now == expires_at` (`2026-01-02T12:00:00.000Z`);
- `expires_at` missing, `"expires_at": 5`, `"expires_at": "tomorrow"`;
- `member_sessions` missing, with session id `sess-orch` **and** with session id null. Missing
  is absent, not empty;
- `"member_sessions": null`, `"member_sessions": "sess-x"`, `"member_sessions": [1]`,
  `"member_sessions": ["a", null]`;
- `timeline: []`, `"timeline": {}`, `timeline` missing;
- any entry with `"at": "not-a-time"`, and any entry with `at` removed. Use the **last** entry,
  so the picked first entry is itself well formed; every `at` is checked;
- picked entry (`now = T0`, so the first entry): `visible` removed; `"visible": "true"`; `tone`
  removed; `"tone": 5`; `text` removed; `"short": null`. Each makes **both** items hidden;
- a valid document padded with trailing spaces to **262,145** bytes;
- `"schema": 1.0` (rev 3, nit 4);
- rev 3, non-regular file at the status path (Unix only; on Windows these cases do not run.
  Use the test project's existing platform-conditional convention, or an early return if it
  has none):
  - `status.json` is a **symbolic link to a valid copy of `work.json`** elsewhere in the temp
    dir → absent. The target is valid, so only the link rule can make it absent;
  - `status.json` is a dangling symbolic link → absent;
  - `status.json` is a **FIFO** with no writer → absent, and the call **returns**. Run the call
    under a bounded wait of at most 5 s. A timeout fails the test; it must never hang the
    suite. How the FIFO is created (a libc `mkfifo` P/Invoke in the test project, or the
    `mkfifo` tool) is the Implementor's call;
  - **forbidden:** no test may link to, open or read `/dev/tty` or any other real device. On a
    regression, such a test would block on, or eat, the developer's keyboard input. The
    symlink-to-a-valid-file case already proves the link rule for every target.

  The non-seekable rule (§C.3, third row) has no unit test, because staging a FIFO that gets
  past the pre-open check is not deterministic across platforms. The Reviewer verifies it by
  reading.

**Present, and must not be absent** (CQ4: unread fields are not checked):
- non-picked entry wrong types: the first entry's `"tone": 5` and `"visible": "yes"`, at
  `now = 12:05` → the second entry (`1 live · 1 out · 1 overdue` / `1/1 1o`, bad). The same
  document at `now = T0` is absent, because the bad entry is then the picked one;
- unread fields wrong types: `"enabled": "yes"`, `"teams": 7`, `"written_at": "garbage"`, and
  the picked entry's `"live": "x"` → present, `1 live · 1 out`;
- an unknown top-level field and an unknown entry field → present;
- `"member_sessions": []` → present (this is `work.json` as shipped).

**Present at the boundary:**
- the same document padded to exactly **262,144** bytes is present;
- `now = expires_at − 1 ms` is present (the stalled entry on `work`).

**Empty field, one item:** the picked entry's `"text": ""` → `ah` is hidden and `ah-short`
still renders `ah: 1/1`.

**Hide cases** (null): `hidden.json` at `T0` and at `13:00`; `member-session.json` with session
`sess-demo-reviewer`.

**Timeline pick** (`work.json`):

| `now` | text | short | tone |
|---|---|---|---|
| `11:59:59.999Z` (before the first) | `1 live · 1 out` | `1/1` | work |
| `12:00:00.000Z` (exact first) | `1 live · 1 out` | `1/1` | work |
| `12:04:00.000Z` (exact) | `1 live · 1 out · 1 overdue` | `1/1 1o` | bad |
| `12:05:00.000Z` (between) | `1 live · 1 out · 1 overdue` | `1/1 1o` | bad |
| `12:06:30.000Z` (exact last) | `1 live · 1 out · 1 stalled` | `1/1 1s` | bad |
| `2026-01-01T23:00:00.000Z` (after the last) | `1 live · 1 out · 1 stalled` | `1/1 1s` | bad |

Plus `bad.json` at `12:01:30.000Z` → `2 live · 2 out · 2 stalled` / `2/2 2s`, and
`pipeline.json` at `12:10:00.000Z` → `2 live · 1 out · 1 overdue` / `2/1 1o`.

**Tones** (built segment markup, for each of `ah` and `ah-short`):
- each of `work`/`warn`/`bad`/`idle` maps to `blue`/`yellow`/`red`/`grey` by default;
- each configured `stateColors` value in the item's own block is used when valid;
- an invalid configured value (`"notacolour"`) falls back to that tone's default, and building
  the `Markup` does not throw;
- tone `"mystery"`, and tone `"Work"` (a case variant), → the idle colour: default `grey`, or
  that item's configured `idle` when set.

**Separate blocks (U2):** on `bad.json`, set only `itemSettings.ah.stateColors.bad = "magenta"`
and `itemSettings.ahShort.stateColors.bad = "green"`. Then `ah` renders magenta and `ah-short`
renders green. With only `itemSettings.ah` set, `ah-short` renders the default red.

**Walk-up** (locate step):
- from `<tmp>/repo/a/b/c`, with `.git` a directory at `<tmp>/repo`, the file is found;
- `.git` as a **file** at `<tmp>/outer/inner/.git` stops the walk. With a visible `status.json`
  only at `<tmp>/outer/.claude/hierarchy/` and `<tmp>/outer/.git/` a directory, cwd
  `<tmp>/outer/inner/x` yields null. With the file placed at
  `<tmp>/outer/inner/.claude/hierarchy/` it is found;
- no `.git` anywhere: cwd `<tmp>/nogit/a` yields null. **Precondition:** first assert that no
  ancestor of `<tmp>` contains `.git`, and fail with a clear message if one does;
- `Cwd` null, empty or relative yields null.

**Markup injection:** a document whose current entry has text `[red]x[/]` and short `[bold]y`.
- `Markup.Remove`, or the segment's `Plain`, equals `ah: [red]x[/]` / `ah: [bold]y`.
- Constructing a Spectre `Markup` from the segment markup does not throw.

**Label and separate blocks** (`warn.json`):
- defaults give `ah: 2 live · 1 out · 1 blocked` and `ah: 2/1 1b`;
- `itemSettings.ahShort.showLabel:false` gives `ah-short` = `2/1 1b`, while `ah` keeps its label;
- `itemSettings.ah.showLabel:false` gives `ah` = `2 live · 1 out · 1 blocked`, while `ah-short`
  keeps `ah: 2/1 1b`;
- `itemSettings.ahShort.labelColor:"grey"` gives two spans for `ah-short`, `ah: ` in grey and
  `2/1 1b` in yellow. `ah` stays a single yellow span.

**Laziness and memo:**
- a render of a config **without** `ah`/`ah-short` invokes a counting probe delegate **0** times;
- a render with **both** placed invokes it **exactly once**;
- `ItemValueResolver.Resolve`, then the leaf render, with an `ItemContext` built through the
  public constructor.

**Raw value:** `ah`'s `ResolveValue` equals the entry's `text` exactly, with no `ah: ` prefix.
This is what lets match rules on raw values work.

### E.3 `--items` / SyntheticFixture

- `ItemsCommand.Build()`:
  - `ah` → example `ah: 2 live · 1 out · 1 blocked`, color kind `semantic`, `default:false`;
  - `ah-short` → `ah: 2/1 1b`, the same.
- `tools/check-examples.sh` passes (README rule C).

### E.4 Config

Each case runs for **both** blocks, `ah` and `ahShort`:
- `--check` on a config with `itemSettings.<key>.stateColors.work = "notacolour"` → `UnknownColor`
  at `/itemSettings/<key>/stateColors/work`;
- an invalid `labelColor` → `UnknownColor` at `/itemSettings/<key>/labelColor`;
- an unknown key under `itemSettings.<key>` → the existing `unknown-key` diagnostic;
- rev 3 (nit 6): `itemSettings.<key>.stateColors.blocked = "red"` → the existing `unknown-key`
  diagnostic, at the pointer the walker already produces for an unknown key
  (`/itemSettings/<key>/stateColors/blocked`);
- a fully valid block → no diagnostics.

Also, a config with `itemSettings["ah-short"]`, the item id rather than the settings key, is
checked as follows. If `--check` already reports unknown keys directly under `itemSettings`, pin
that diagnostic for `ah-short`. If it does not, omit this case. Do not add detection for it.
Rev 3: the review verified that the condition holds (`ItemSettingsJsonConfig.Extra` is walked,
`ConfigCheck.cs:1444`), so this case is required.

### E.5 Existing assertions to update

- `ItemsCommandTests.cs:32-39`: the non-default list gains `ah` and `ah-short`. The name of the
  test method is the Implementor's call.
- `SchemaCommandTests.cs:17-25`: `RequiredStructureNames` gains `ahItemSettings` and
  `ahStateColors`, plus `ahShortItemSettings` only if the §C.7 mechanical fallback was used.
  Any test that lists the `itemSettings` optional keys gains `ah` and `ahShort`.
- Any other test that enumerates every registry row, or counts builtins, and fails only because
  of the two new rows: update its expectation. Do **not** change what it asserts. List each one in
  the report.

---

## §F — Verification

### F.1 The Implementor's checks (smoke level; the full suite runs in the verification pass)

1. The new test class and the two updated test files pass.
2. `tools/check-examples.sh` and `tools/check-citations.sh` pass.
3. An AOT publish **to a scratch dir, never `publish/`** reports no new trim or AOT warning
   (IL2026/IL3050/IL2xxx) that names the new file or types:
   `dotnet publish src/ClaudeTuiLine -c Release -o "$T/bin" > "$T/publish.log" 2>&1`, then grep
   the log.

### F.2 E7 — NEEDS-EVIDENCE (run after the code lands; route it to the Implementor or a task-runner)

**Question.** Does placing `ah` + `ah-short` against a 20 KB status file add at most 1 ms to
render p95?

**Waiving E7 (rev 3).** No design decision in this spec waits on E7. The read is a few `stat`s,
one open, and one exact-size read of a file that is usually under 8 KB; nothing is cached, and
nothing will be cached either way. E7 is also an acceptance item of upstream `0071`. Waiving it
is therefore the user's call **and** must be reported to agent-tools (`at-orchestrator`). If it
is waived, record the waiver and who agreed to it in §K instead of numbers.

The method is direct hyperfine against a Release build outside `publish/`. `bench/bench.sh` is
not used, and upstream accepted that (CQ6). These CQ6 conditions are mandatory:
- A/A calibration first;
- at least 200 runs per arm, with warm-up;
- a document that really takes the **present** path;
- p50 and p95 for every arm.

**Procedure.** Sandbox-safe; every variable is assigned in the script. Run on the post-change
tree.

1. Create the scratch area: `T="$(mktemp -d)"`, then `[ -n "$T" ] && [ -d "$T" ] || exit 1`.
   Set `REPO=/Users/jimcline/git/repos/claude-tui-line`.
2. Build: `dotnet publish "$REPO/src/ClaudeTuiLine" -c Release -o "$T/bin" > "$T/publish.log" 2>&1`.
   It must exit 0.
3. Stage the pool: `mkdir -p "$T/pool/.git" "$T/pool/.claude/hierarchy"`. No `git` command is
   needed; the walk checks existence only.
4. Write `$T/pool/.claude/hierarchy/status.json`:
   - start from `tests/ClaudeTuiLine.Tests/fixtures/ah/work.json`;
   - append copies of `teams[0].dispatches[0]` until the file is 19.5–20.5 KB;
   - set `expires_at` to `2099-12-31T00:00:00.000Z`;
   - keep `"member_sessions": []`, so `bench-session` is not a member;
   - leave the timeline as it is. Real `now` is after its last `at`, so the current entry is the
     `visible: true` `stalled` one, and the full document is parsed.
5. Write the stdin: `$T/stdin.json` = `$REPO/bench/fixture.json` with `cwd` set to `$T/pool` and
   `session_id` set to `bench-session`. The walk from that `cwd` must reach `$T/pool/.git`.
6. Write the configs: `$T/a.json` is a minimal valid config whose only placed item is `model`.
   `$T/b.json` is the same with `ah` and `ah-short` added, and no `itemSettings`. Both must pass
   `CLAUDE_TUI_LINE_CONFIG=… "$T/bin/claude-tui-line" --check`.
7. Run every command with `CLAUDE_TUI_LINE_CACHE="$T/cache"` and `COLUMNS=120` exported, and
   with `CLAUDE_TUI_LINE_CONFIG=<a|b>` set per command.
8. **Present-path check, before any timing.** Render B once with `$T/stdin.json`. Its output
   must contain `ah: 1 live · 1 out · 1 stalled` and `ah: 1/1 1s`. Render A once; its output must
   contain neither. If B lacks them, **stop**: the bench would be timing the early-absent path.
   Report what B printed.
9. **Calibration (A/A):** `hyperfine --warmup 10 --runs 300 --input "$T/stdin.json" --export-json "$T/cal.json"`
   with A twice.
   - If |p95(A1) − p95(A2)| < 0.5 ms, proceed.
   - Otherwise re-run calibration once with `--runs 600`. If it is still ≥ 0.5 ms, stop and
     report **inconclusive** with both calibrations.
10. **Measurement (A/B):** the same flags as the passing calibration, with A vs B,
    `--export-json "$T/e7.json"`.
11. Compute p50 and p95 from each result's `times` array.
12. Clean up: `[ -n "$T" ] && rm -rf "$T"`. Check that no `claude-tui-line` process is left
    running.

**Report:**
- p50 and p95 for A1, A2, A and B;
- the A/A p95 gap and the B − A p95 delta;
- the run count used, the status-file byte size, and the hyperfine version.

If hyperfine is not installed, stop and report it; do not substitute a different timer.

**What each result decides:**

| result | decision |
|---|---|
| present-path check fails | stop. The setup is wrong, not the code. Report B's output. |
| A/A p95 gap still ≥ 0.5 ms after the 600-run retry | inconclusive. Report both calibrations. No design change. |
| B − A p95 ≤ 1.0 ms | E7 passes. No cache. Record the numbers in §K and in the PR description. |
| B − A p95 > 1.0 ms | **Stop, do not merge, and add no cache.** Run the split below and report it. The Orchestrator sends it to agent-tools (`at-orchestrator`) before any cache is considered (CQ6). An in-process mtime cache only helps when one process serves more than one render, and this binary does not. |

**Split, run only when B − A > 1.0 ms.** Run one hyperfine invocation, with the same flags, over
five commands. Each command reads its own stdin by redirection inside the command string
(`… < "$T/stdin-X.json"`), using hyperfine's default shell, so all arms pay the same shell cost.
Each `stdin-X.json` differs from `stdin.json` only in `cwd`, which points at that arm's pool.
Each pool is `<pool>/.git/` plus `<pool>/.claude/hierarchy/`.

| arm | config | pool's `status.json` | what it adds over the previous arm |
|---|---|---|---|
| A | a | `$T/pool` (any) | baseline |
| N | b | none | init plus the walk-up (N − A) |
| R | b | the byte `x` followed by the 20 KB document (invalid JSON at byte 0) | the bounded read (R − N) |
| H | b | the 20 KB document with every timeline entry `"visible": false` | the parse (H − R) |
| B | b | the 20 KB document as in step 4 | the render (B − H) |

Report p50 and p95 for each arm, and the four differences.

---

## §G — House rules (`0071 §7.7`) and release chores

- `SPEC-V2-FRAMEWORK.md §1` (the load-bearing rule): two registry rows plus their resolve and
  build functions.
  - The only plumbing is the data source. That means the `ItemContext` parameter, the two
    `Program.cs` sites and `SyntheticFixture`, the same shape that engram and remote-url used.
  - There are **no control-flow edits** to `ItemValueResolver`, `PaneAssembler`,
    `SegmentTruncation`, `LeafItems`, `LabeledSegment` or any render or sizing code.
- Version `0.5.0` → `0.6.0` at exactly the four sites in §B (three `.csproj` files and
  `.claude-plugin/plugin.json`).
- `docs/RELEASE-NOTES.md`: a new top entry `# claude-tui-line 0.6.0 — agent-hierarchy status items`,
  in the same format as the 0.5.0 entry. It says:
  - the two opt-in items;
  - that they need agent-hierarchy ≥ 0.110.0 to show anything;
  - that they are hidden in live team members' sessions, and every other session in the checkout
    shows them. Do not say "Orchestrator only";
  - the `itemSettings.ah` and `itemSettings.ahShort` keys, one block per item.
- §-citations: every `docs/specs/*.md` file must pass `tools/check-citations.sh`, including this
  one. Upstream references stay backticked.
- Never build into `publish/`.

---

## §H — What must NOT change

- The default item set. For every config that does not mention `ah`/`ah-short`, renders are
  byte-identical, including every existing golden fixture.
- The existing `ItemContext` construction sites, apart from `Program.cs:78`, `Program.cs:365` and
  `SyntheticFixture.cs:56-58`. They gain no new argument.
- `LabeledSegment`, `SingleColor`, `BuildSpanMarkup`, truncation, pane assembly, value resolution
  and colour resolution.
- `bench/bench.sh` and `bench/fixture.json`.
- Anything under `/Users/jimcline/git/repos/agent-tools*`.
- The probe may not spawn a process, write, lock, or read anything other than the existence of
  `.git` entries and the one `status.json`.
- Visibility may not depend on anything except the document and the session id. In particular
  it may not depend on `StatusInput`'s agent name or role, the transcript, environment variables,
  or `AGENT_HIERARCHY_DIR` (CQ1, CQ2).

---

## §I — Risks for the Implementor

- **Laziness is easy to break.** Never evaluate the probe in the `ItemContext` constructor, in
  `SyntheticFixture` setup or in any eager pre-pass. Only `ah`/`ah-short`'s two delegates may read
  the property. The 0/1 invocation-count test (§E.2) is the guard.
- **`DefaultIds` is an exclusion list.** Forgetting it makes both items default. `ItemsCommandTests`
  catches that.
- **AOT.** A reflection `JsonSerializer.Deserialize<T>(…)` compiles and passes the tests, but warns
  or fails under AOT. Use the generated context's type info. §F.1 step 3 checks this.
- **Colour tags are not validated downstream.** The §C.5 fallback is the only guard against a
  config-supplied tag breaking the markup.
- **Test hermeticity.** This repo contains `.claude/hierarchy/`. A test that uses the real probe
  with the repo as `cwd` passes or fails depending on whether a live team is running. Do not
  do that.
- **The temp-dir walk-up test** assumes no ancestor of `Path.GetTempPath()` contains `.git`. The
  precondition assert makes a violation loud instead of silently wrong.
- **`·` (U+00B7)** counts as one cell in the existing width code, as it does for the fixture text.
  Do not special-case it.
- **Over-typing the DTO.** Strongly typing `tone`, `text`, `short` or `visible` on **every**
  timeline entry makes System.Text.Json reject a document that the contract says is present
  (§C.3, "picked-entry-only typing"). The §E.2 "present, and must not be absent" cases are the
  guard.
- **A missing `member_sessions` is not `[]`.** A nullable list that defaults to empty quietly
  inverts CQ4's fail-toward-hidden rule. §E.2 tests both a missing value and `null`.
- **Two settings blocks, one code path.** Thread the item's own block through the shared
  resolve/build and colour mapping. Do not branch on the item id inside them beyond choosing the
  field and the block.
- **Rev 3, symlink check scope.** Judge only the final component, without following it. Code
  that resolves the full path, or compares the full path with its real path, rejects every
  macOS temp dir (`/var` → `/private/var`), so every §E vector test fails.
- **Rev 3, whole-file read helpers.** A convenience "read all bytes" API that falls back to
  reading until end of file when the length is 0 or unknown (as `File.ReadAllBytes` does)
  reintroduces the unbounded, blocking read. The size check on the handle must sit between the
  open and the read.

---

## §J — Confidence and escalation

- Confidence is **high** on the reader and item design: it is a small, bounded consumer that
  copies existing precedents.
- No Ultra-Advisor escalation is needed.
- Rev 2 has no open contract or user questions: CQ1–CQ6 and U1–U2 are settled (§D.2, §D.3).
- The only open item is E7 (§F.2), a measurement taken after the code lands.
- Parked outside this spec: whether `ah` should widen `member_sessions`. That is a user question
  for agent-tools, and the answer needs no change here.
- Rev 3, for agent-tools' information (nothing here waits on it):
  - every reader of `status.json`, the `ah` mod included, has the same symlink and FIFO hazard.
    `status-file.md` step 1 says "unreadable" without defining it. This spec's definition
    (§C.3) fails toward hidden;
  - this reader treats `"schema": 1.0` as absent, which is stricter than numeric equality. The
    writer never emits it.

---

## §K — Closeout

- **E7 waived by the user (2026-10-05).** Not measured. The statusline refreshes about once a
  second, so a 1 ms budget per render is immaterial, and hyperfine is not installed. No design
  decision waited on E7 (§F.2), and nothing is cached. at-orchestrator acked the waiver for the
  upstream `0071` acceptance item.
- Implementation: `b55a51d` (items), `b618809` (review fixes), `a158f95` (rev 3 read hardening).
