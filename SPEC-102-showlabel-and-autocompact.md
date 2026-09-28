# SPEC-102 — shared `showLabel` item setting + new `autocompact` item

Status: **READY TO IMPLEMENT.** No blocking evidence gaps, no open questions.
One non-blocking cosmetic item (E3) is noted and explicitly not a gate.
Rev 3 — see the amendment notes at the end for what changed across revisions.

---

## A. Shared `showLabel` mechanism

### A.1 What exists today (verified)

- Label-bearing items render via one chokepoint:
  `LeafItems.ApplyFormat(format, value)` (`src/ClaudeTuiLine/LeafItems.cs:242-243`)
  — `(format ?? "{}").Replace("{}", value)`.
- Format constants live in `src/ClaudeTuiLine/SegmentBuilder.cs`:
  `WorktreeFormat "worktree:{}"`, `PullRequestFormat "PR {}"`,
  `EffortFormat "effort:{}"`, `OutputStyleFormat "style:{}"`,
  `AgentFormat "agent:{}"`, `VimFormat "[{}]"`.
- Per-item settings are plain classes on `ItemSettingsJsonConfig`
  (`src/ClaudeTuiLine/Config.cs:48-138`), each with its own
  `[JsonExtensionData] Extra`. They reach render code through
  `ItemContext.ItemSettings?.<Item>` passed by the lambdas in
  `src/ClaudeTuiLine/ItemRegistry.cs:53-87`. There is **no shared base type**.
- output-style has **no settings class**; its build lambda hardcodes
  `SingleColor("dim", LeafItems.ApplyFormat(OutputStyleFormat, raw))`.

### A.2 Design decision — per-item key (CONFIRMED by user)

`showLabel` is defined **once** and consumed **once**, but configured **per
item**: `itemSettings.<item>.showLabel` (e.g.
`itemSettings.outputStyle.showLabel`), matching the existing shape
(`itemSettings.worktree.showBranch`, `itemSettings.directory.depth`).

A single global `itemSettings.showLabel` was rejected and the user confirmed
the per-item shape: a global switch cannot express "hide `style:` but keep
`worktree:`", and it breaks the file's established per-item nesting.

### A.3 Shared settings base — `LabeledItemSettings`

New type in `src/ClaudeTuiLine/Config.cs`, placed immediately above
`DirectoryItemSettings`:

```csharp
/// Base for item settings whose item renders a "label:value" prefix.
/// showLabel == null or true renders the label; false renders the bare value.
public abstract class LabeledItemSettings
{
    [JsonPropertyName("showLabel")]
    public bool? ShowLabel { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
```

Rules:

- **Do NOT retrofit the existing six settings classes in this pass.** They keep
  their own `Extra` and are untouched — zero regression surface. (A class may
  declare `[JsonExtensionData]` only once, so adoption later means *moving* that
  class's `Extra` up into the base at the same time it starts deriving.)
- Adoption cost for any future label-bearing item is exactly two edits: derive
  its settings class from `LabeledItemSettings`, and pass
  `settings?.ShowLabel` into the shared helper (A.4). No new mechanism.
- Register the new concrete types for source-gen JSON alongside the existing
  `[JsonSerializable(...)]` attributes in `Config.cs`.
  `LabeledItemSettings` is abstract and is never serialized directly; do not add
  a `JsonSerializable` entry for it.

### A.4 Shared render helper — the single implementation

Add one overload next to the existing `ApplyFormat` in
`src/ClaudeTuiLine/LeafItems.cs`:

```csharp
public static string ApplyFormat(string? format, string value, bool? showLabel) =>
    showLabel == false ? value : ApplyFormat(format, value);
```

- `null` (key absent) and `true` both keep today's formatted output →
  backward compatible by construction.
- `false` yields the bare resolved value, with no label, separator, or
  surrounding decoration.
- Do **not** attempt to parse the label out of the format string. "Ignore the
  format entirely when showLabel is false" is the whole rule, and it is the
  reason a single helper covers `"style:{}"`, `"PR {}"` and `"[{}]"` alike
  without per-item special cases.
- Do not change the existing two-arg `ApplyFormat`; every current caller keeps
  compiling and behaving identically.

### A.5 output-style item changes

1. New settings class in `Config.cs`:

```csharp
public sealed class OutputStyleItemSettings : LabeledItemSettings { }
```

   (no own members, no own `Extra` — it inherits both.)

2. New property on `ItemSettingsJsonConfig`:

```csharp
[JsonPropertyName("outputStyle")]
public OutputStyleItemSettings? OutputStyle { get; set; }
```

   JSON key is `outputStyle` (camelCase of the item id `output-style`) — the
   established convention in that class is a camelCase property name, not the
   hyphenated item id. Verify against the file's naming policy when
   implementing; if the class relies on a global naming policy rather than
   explicit `[JsonPropertyName]`, follow whichever the sibling properties do.

3. In `src/ClaudeTuiLine/SegmentBuilder.cs`, the output-style build path becomes
   settings-aware, mirroring `ResolveWorktree`'s optional-settings signature:

```csharp
SingleColor("yellow", LeafItems.ApplyFormat(OutputStyleFormat, raw, settings?.ShowLabel))
```

4. In `src/ClaudeTuiLine/ItemRegistry.cs` (~line 73), the output-style entry's
   build lambda must now thread `ctx.ItemSettings?.OutputStyle` through, exactly
   as the worktree entry (line 68) threads `ctx.ItemSettings?.Worktree`.

5. **Default color changes from `"dim"` to `"yellow"`.** Fixed by the user — not
   an implementation choice. `yellow` is in the 16-color set
   (`ColorResolution.cs:225-231`), so no color-table change is needed.

Resulting behaviour for style name `Explanatory`:

| config | rendered |
|---|---|
| no `outputStyle` key (today's configs) | `style:Explanatory` in yellow |
| `outputStyle.showLabel: true` | `style:Explanatory` in yellow |
| `outputStyle.showLabel: false` | `Explanatory` in yellow |

`ResolveOutputStyle` (`SegmentBuilder.cs:289-293`) is **unchanged** — it returns
the raw value used by color rules, and must keep doing so. Color expressions
that match on the output-style value must not start seeing the `style:` prefix
or lose it.

### A.6 Scope boundary (explicit)

- **This pass wires `showLabel` into output-style and the new autocompact item
  only.**
- worktree, pr, effort, agent, vim are label/decoration-bearing and are
  *eligible*, but are **not** changed here: each retrofit costs a settings-class
  edit plus a moved `Extra`, and every one of them risks a golden-fixture churn
  that is unrelated to the change the user asked for. The mechanism is ready;
  adoption is a later, per-item, one-line decision.
- `vim`'s format `"[{}]"` is brackets, not a label — if it ever adopts
  `showLabel`, that is a deliberate product call about the brackets, not a
  mechanical retrofit. Note this before anyone bulk-applies the setting.

---

## B. New `autocompact` item

### B.1 Data source — RESOLVED against the live binary and current docs

Autocompact has **two** dimensions, and the second one is a **token count or
the literal `"auto"`** — not a percentage. Rev 1 of this spec described
`autoCompactWindow` as a percentage; **that was wrong** and is corrected here.

| key | type | values | default |
|---|---|---|---|
| `autoCompactEnabled` | boolean | `true` / `false` | **`true`** |
| `autoCompactWindow` | string or number | `"auto"`, or a token count 100K–1M | model-tuned (behaves as `"auto"`) |

`autoCompactWindow` accepted forms (all documented):

- plain token count — `200000`
- `k` / `M` suffix — `500k`, `1M`
- bare number 100–1000 meaning thousands — `200` == `200000`
- the literal string `"auto"` — return to the model-tuned window

Sources (verified):
- https://code.claude.com/docs/en/settings-reference.md — both keys, and
  `autoCompactEnabled` default `true`.
- https://code.claude.com/docs/en/model-config.md — the value domain quoted
  above, and `/autocompact auto`.
- `claude --help` on the live binary (v2.1.241):
  `--autocompact <auto|tokens>   Auto-compact window size (auto, or 100k–1M tokens)`
- Ground truth on this machine: `~/.claude/settings.json:220` contains
  `"autoCompactWindow": 250000` — a token count, confirming the shape.

**Not in the statusline payload.** Re-verified: the documented statusline stdin
JSON schema (https://code.claude.com/docs/en/statusline.md) contains zero
compact/autocompact fields. Do **not** add anything to
`src/ClaudeTuiLine/StatusInput.cs`.

**Other sources that set it** (matters for the accuracy caveat):

- `/autocompact <value>` slash command (runtime).
- `--autocompact <value>` CLI flag.
- `CLAUDE_CODE_AUTO_COMPACT_WINDOW=<plain token count>` env var — accepts a
  plain token count only, no `k`/`M` suffix.
- managed/enterprise settings, and `--setting-sources` which can restrict which
  settings files load at all.

Of these, **only the env var is visible to a statusline process** — it is
inherited by the statusline child process. The rest are invisible. See B.2.

### B.2 Resolution algorithm

New resolver in `src/ClaudeTuiLine/SegmentBuilder.cs` (or a small dedicated
file if `SegmentBuilder.cs` is already unwieldy — either is acceptable; keep it
out of `StatusInput.cs`):

```
ResolveAutocompact(projectDir, homeDir, envWindow) -> string?

  // 1. enabled?
  enabled = first non-null "autoCompactEnabled" boolean found, scanning
            settings files highest-precedence first (list below);
            if none define it -> true          // documented default
  if (!enabled) return "off"

  // 2. window size
  if envWindow (CLAUDE_CODE_AUTO_COMPACT_WINDOW) is set and parses as a plain
      token count in [100000, 1000000] -> use it
  else window = first non-null "autoCompactWindow" value found, same file order
  if window is absent, is "auto", or fails to normalize -> return "auto"
  return FormatTokens(normalized)
```

Settings file order (highest precedence first), per
https://code.claude.com/docs/en/settings.md:

1. `<projectDir>/.claude/settings.local.json`
2. `<projectDir>/.claude/settings.json`
3. `<homeDir>/.claude/settings.json`

The two dimensions are resolved **independently** — `autoCompactEnabled` may
come from one file and `autoCompactWindow` from another. That is how Claude
Code's own settings merge works; do not require both to come from the same
file.

**Window normalization** (`autoCompactWindow` → token count):

| input | normalized |
|---|---|
| JSON number 100000–1000000 | as-is |
| JSON number 100–1000 | ×1000 |
| string `"auto"` (any case) | *auto* |
| string `"500k"` / `"500K"` | 500000 |
| string `"1M"` / `"1m"` | 1000000 |
| string of digits | same rules as the numeric cases |
| anything else, or out of range | treat as *auto* |

**Display formatting** (`FormatTokens`) — abbreviated form only:

- `1000000` → `1M`
- otherwise → `<n/1000>K`, integer division, e.g. `250000` → `250K`.
- **Never render the raw token count**, neither alone (`250000`) nor alongside
  the abbreviation (`250K (250000)`). Confirmed by the user; the statusline is
  width-constrained and this item is decorative.

**Error handling — non-negotiable:**

- Missing, unreadable, or malformed JSON settings file → skip that file
  silently and continue to the next. Swallow `IOException`, `JsonException`,
  and permission errors.
- A present-but-wrong-typed value (e.g. `autoCompactEnabled: "yes"`) is treated
  as *not defined* — continue to the next file.
- A bad settings file must never crash, throw, or blank the statusline.

**Inputs:**

- `projectDir`: the workspace/project directory from `StatusInput`
  (`Workspace`) if it exposes one, else `Input.Cwd`. Do **not** walk parent
  directories — Claude Code's project settings live at the project root it
  reports.
- `homeDir`: `Environment.GetFolderPath(SpecialFolder.UserProfile)`.
- `envWindow`: `Environment.GetEnvironmentVariable("CLAUDE_CODE_AUTO_COMPACT_WINDOW")`.

All three are **parameters, not ambient lookups**, so tests can point them at
temp directories and a fake env value without touching the developer's real
`~/.claude/settings.json`. Keep that signature.

**Accuracy caveat** — state it in the item's `Reports` text: this item reports
the setting as visible from the environment variable and the readable settings
files. A `/autocompact` command issued mid-session, a `--autocompact` CLI flag,
an enterprise/managed policy, or a `--setting-sources` restriction can all make
the live value differ. This is inherent to reading settings from outside the
process, not a defect.

**Env-var precedence is an assumption.** Claude Code's general convention is
env-over-settings, and this spec follows it. It is low-stakes: it only matters
when the env var and a settings file disagree, and the visible outcome is one
wrong number in a decorative item. Not worth blocking on — but see E3 if
someone wants it nailed down.

### B.3 Registry entry

In `src/ClaudeTuiLine/ItemRegistry.cs`, append a new `ItemDefinition` to the
`Items` array:

- `Id`: `"autocompact"`
- `Reports`: `"Auto-compaction state and window size (autoCompactEnabled / autoCompactWindow from the visible settings files and CLAUDE_CODE_AUTO_COMPACT_WINDOW; /autocompact, --autocompact and managed settings are not visible)"`
- `ResolveValue`: `ctx => SegmentBuilder.ResolveAutocompact(ctx)` — returns
  `"off"`, `"auto"`, or a formatted size like `"250K"`. This is the raw value
  color rules match on, so a rule can color `off` differently from a size.
- `BuildDefaultSegment`: formatted via the shared helper —
  `LeafItems.ApplyFormat(AutocompactFormat, raw, ctx.ItemSettings?.Autocompact?.ShowLabel)`
  with `private const string AutocompactFormat = "autocompact:{}";` added
  alongside the other format constants in `SegmentBuilder.cs`.
- `ColorKind`: use the **same** `ItemColorKind` the existing state item
  `thinking` uses — read its entry in the registry and match it. Do not pick
  independently.
- No `DefaultLinkTemplate`.
- Placement: immediately **after** `output-style`. Declaration order is default
  render order, so this position is deliberate.

**Opt-in, not default-on.** Add `autocompact` to the opt-in-only set alongside
`model-short`, `remote-url`, `repo-host`, `linear` — a new item must not
silently appear in every existing user's statusline. Find how those four are
marked opt-in and mark this one the same way.

Settings class:

```csharp
public sealed class AutocompactItemSettings : LabeledItemSettings { }
```

plus `[JsonPropertyName("autocompact")] public AutocompactItemSettings? Autocompact { get; set; }`
on `ItemSettingsJsonConfig`, and a `[JsonSerializable]` registration.

### B.4 Rendering — FINAL

**One value string carries both dimensions** — state and size collapse into a
single token, so the item stays one segment, needs one format constant, and
routes through the A.4 chokepoint with no special-casing.

| situation | raw value | default render | with `showLabel: false` |
|---|---|---|---|
| `autoCompactEnabled: false` | `off` | `autocompact:off` | `off` |
| enabled, `autoCompactWindow: 250000` | `250K` | `autocompact:250K` | `250K` |
| enabled, `autoCompactWindow: "500k"` | `500K` | `autocompact:500K` | `500K` |
| enabled, `autoCompactWindow: 1000000` | `1M` | `autocompact:1M` | `1M` |
| enabled, `autoCompactWindow: "auto"` | `auto` | `autocompact:auto` | `auto` |
| enabled, no window key anywhere | `auto` | `autocompact:auto` | `auto` |
| no keys at all (the common case) | `auto` | `autocompact:auto` | `auto` |

Design notes:

- **Abbreviated size only** — `250K`, never `250K (250000)`. Confirmed by the
  user; this is settled, not a placeholder.
- When compaction is **off**, the window size is meaningless — `off` alone is
  rendered, never `off (250K)`. Showing a threshold that will never fire is
  noise.
- The threshold is surfaced **as the item's value**, not as a second segment or
  a second config toggle. This satisfies "show the threshold too" without
  doubling the config surface, and `showLabel` governs it exactly as it governs
  every other label-bearing item.

---

## C. Evidence items

**E1 — RESOLVED.** Default of `autoCompactEnabled` when absent is **`true`**
(documented, settings-reference.md). The absent case therefore renders as
enabled, falling through to the window dimension. No code branch needed beyond
`?? true`.

**E2 — RESOLVED.** `/Library/Application Support/ClaudeCode/managed-settings.json`
does **not** exist on this machine. B.2's precedence list stands unchanged; do
not add a managed-settings path. The caveat in B.2 covers the case where such a
policy exists on another machine.

**E3 — OPEN, NON-BLOCKING.** Does `CLAUDE_CODE_AUTO_COMPACT_WINDOW` actually
override a settings-file `autoCompactWindow`, or does the settings file win?
- *How to check:* set the env var to a value different from
  `~/.claude/settings.json`'s `autoCompactWindow` (currently `250000`), start
  Claude Code, and run `/autocompact` with no argument (or otherwise observe the
  reported effective window).
- *Decides:* only the order of the two lookups in B.2 step 2. If settings win,
  swap the two clauses.
- *Do not block implementation on this.* Implement env-first as spec'd; the
  fix if E3 comes back the other way is a two-line reorder plus one test.

---

## D. Backward compatibility & things that must NOT change

- Every existing config with no `outputStyle` / `autocompact` key must render
  byte-identically to today **except** for output-style's color, which changes
  from `dim` to `yellow` by explicit user decision.
- The `autocompact` item is opt-in, so no existing statusline gains a segment.
- Unknown keys under any item settings object must keep landing in `Extra`.
- `ResolveOutputStyle`'s return value (raw style name, `null` for
  default/empty) is unchanged — color rules and link templates depend on it.
- The two-arg `ApplyFormat` keeps its exact current behaviour.
- No change to `StatusInput` or the payload parsing path.
- Existing six settings classes are not edited at all in this pass.

**Fixture/documentation sweep the Implementor must do** (mechanical, not a
design call): grep the whole repo — `src/`, `tests/`, `ClaudeTuiLineMcp/`,
docs/README, and any golden fixtures — for `"dim"` in an output-style context,
for `style:`, and for any hardcoded **item count** (the registry goes 18 → 19).
`GoldenParityTests`, `ItemFormatParityTests`, `ItemsCommandTests`,
`SchemaCommandTests`, and `EndToEndItemValuesTests` are the likely hits. The
config schema is generated by reflection over the `[JsonSerializable]` types
(`ConfigTools.cs` only projects the live envelope), so the new settings classes
surface in `--schema` automatically — but `SchemaCommandTests` may still assert
a shape that now has more properties.

---

## E. Test coverage the Implementor should add

In `tests/ClaudeTuiLine.Tests/`, following the existing style
(`SegmentBuilderTests.cs` helper `CtxWithSettings()` and the
`Worktree_ShowBranch_*` tests at lines ~203-300 are the model):

Part A:
1. `ApplyFormat(fmt, v, null)` == `ApplyFormat(fmt, v)` — the backward-compat
   identity, asserted directly on the helper.
2. `ApplyFormat("style:{}", "Explanatory", false)` == `"Explanatory"`.
3. output-style with no settings → `style:<name>`, colored `yellow` (assert the
   markup tag, not just the plain text — the color change is the regression
   risk).
4. output-style with `showLabel: false` → `<name>` only, still `yellow`.
5. output-style with `showLabel: true` → identical to case 3.
6. `ResolveOutputStyle` still returns the bare name (and `null` for
   `"default"`/empty) regardless of `showLabel`.
7. Config round-trip: `{"itemSettings":{"outputStyle":{"showLabel":false}}}`
   parses; an unknown sibling key lands in `Extra`.
8. Two placements of `output-style` in one config both see the same settings
   (mirrors the existing `Directory_TwoPlacementsOfSameId_*` test).

Part B — window normalization (table-driven, one test method with cases):
9. `250000` → `250K`; `200` → `200K`; `"500k"` → `500K`; `"500K"` → `500K`;
   `"1M"` → `1M`; `1000000` → `1M`; `"auto"` → `auto`; `"AUTO"` → `auto`.
10. Out-of-range / junk → `auto`: `50`, `99999`, `2000000`, `"banana"`, `true`,
    `null`.

Part B — resolution:
11. `autoCompactEnabled: false` anywhere visible → `off`, **even when a window
    is also set** (asserts the off-wins rule in B.4).
12. No keys at all → `auto` (the documented-default path, and the common case).
13. Precedence, enabled: `settings.local.json` beats `settings.json` beats home.
14. Precedence, window: same order, asserted independently of the enabled flag.
15. The two dimensions resolve from *different* files (enabled in home, window
    in project) and both take effect.
16. `CLAUDE_CODE_AUTO_COMPACT_WINDOW=300000` overrides a settings-file window of
    `250000` → `300K`. (If E3 resolves the other way, this test inverts.)
17. Env var with a `k`/`M` suffix (`"500k"`) is **not** accepted — the env var
    is documented as plain-count only; falls back to the settings file.
18. Malformed JSON settings file → skipped, next file consulted, no throw.
19. Missing/unreadable files → resolves without throwing.
20. `showLabel: false` on autocompact → bare `250K` / `auto` / `off`.
21. Registry: `autocompact` is present and is **not** in the default-on set.

Tests must pass `projectDir`, `homeDir`, and `envWindow` explicitly and must
never read the developer's real `~/.claude/settings.json`.

---

## F. Confidence

- Part A: high confidence.
- Part B: high confidence. The value shape is confirmed from three independent
  sources (docs, the live `claude --help`, and a real
  `autoCompactWindow: 250000` in the user's own settings file). The only
  residual uncertainty is E3, which is cosmetic and non-blocking.
- No Ultra-Advisor escalation warranted: nothing security-, migration-, or
  concurrency-sensitive; the added public surface is two optional config keys
  and one opt-in item.
- **No open questions.** All three product decisions the Architect declined to
  make have been answered by the user: per-item `showLabel` (A.2), yellow as
  output-style's color (A.5), and abbreviated-size-only rendering (B.4).

---

## Amendment notes

### Rev 3 (this revision) — B.2/B.4 only

- **Q1 resolved by the user: render `250K` alone, never `250K (250000)`.** This
  is what rev 2 already specified, so no behaviour changed — the "open question"
  hedge in B.4 and F is removed and the rule is now stated as settled, with
  `FormatTokens` in B.2 explicitly forbidding the raw count in either form.
- Status promoted to READY TO IMPLEMENT.

### Rev 2 — Part B rewritten

1. **`autoCompactWindow` is a token count or `"auto"`, not a percentage.**
   Rev 1 described it as a percentage. That was wrong — it came from a docs
   summary that omitted the value domain. Corrected against
   `model-config.md`, `claude --help` (v2.1.241), and the user's real
   `~/.claude/settings.json:220` (`"autoCompactWindow": 250000`). The user's
   report was accurate and my original finding was not. Note this is the *same
   key* named in rev 1 — the key name was right, the value domain was wrong.
2. **The item now surfaces both dimensions** (B.4), collapsed into one value
   string so it still flows through the single `ApplyFormat` chokepoint — no
   second segment, no second config toggle, `showLabel` unchanged.
3. **`off` suppresses the size** — a threshold that will never fire is noise.
4. **A fourth, *visible* source appeared:** the env var
   `CLAUDE_CODE_AUTO_COMPACT_WINDOW`, which a statusline child process inherits.
   B.2 reads it, and E3 tracks the one open question about its precedence.
5. **E1 and E2 both resolved** (`autoCompactEnabled` defaults to `true`; no
   managed-settings file on this machine), so Part B stopped being
   evidence-gated.
6. Part B test list expanded from 7 cases to 13, mostly normalization coverage.

Parts A, D, and the Part A tests are unchanged since rev 1.
