# SPEC-104 — Engram item build-out: CLI-sourced values + state-based colouring

Status: **IMPLEMENTED AND MERGED**, with one small follow-up tracked in §L.1.

**rev 4 changes (post-implementation corrections — read §L first if you are closing this out):**
- **§L is new** — closeout, and the one outstanding item (`lastEventAge`).
- §D.1 corrected: `EngramState` is **`public`**, not `internal`. rev 3's text was wrong and
  the compiler said so; see §D.1 for why.
- §E.1 gains `LastEventAt` on `EngramProbeResult`, which `lastEventAge` (§C.4) needs and rev 3
  omitted. **Until that ships, `lastEventAge` must not be in the accepted vocabulary** — see
  §L.1, which is a correctness item, not a nicety.
- §G gains G.12.

**rev 3 changes:** §C.8 `binaryPath` + resolution order + security constraints (§C.8.3),
settling §H.3. §F.3 binaryPath validation. G.9–G.11.

**rev 2 changes:** §D.4 rewritten to parse-stdout-before-exit-code. **§H.2 withdrawn** — it
asked for a dangerous experiment that turned out to be unnecessary. §I.1 resolved by the user:
byte-identical defaults.

Covers two Orchestrator requests, merged deliberately (§A.2):
- `20260823-202153-1tj0` — configurable engram values sourced from the `engram` CLI.
- `20260823-202444-17nv` — state-based reachability colouring (the user's §C.7 answer:
  "I want the engram item truly built out with item settings" — feature, not docs fix).

---

## §A — Framing

### A.1 — HARD PREREQUISITE: SPEC-103 must land first *(satisfied — 103 merged before this)*

SPEC-103 Part C introduces `EngramItemSettings` with `factsColor`/`verbColor`, and changes
`SegmentBuilder.BuildEngram`. **This spec is additive to SPEC-103's post-merge state.**

- `factsColor` and `verbColor` are **inherited keys**. Do NOT remove or rename them.

### A.2 — Why one spec and not two

Reachability colouring and CLI-sourced values read the *same probe*. `engram status --json`
returns `Server`, `Initialised`, `Pid`, `Port` — simultaneously the value source and the state
source. Specifying them apart would produce two designs racing to add the same subprocess to
the same call site. They are one feature.

### A.3 — Considered and rejected: do this with a `command` item

**Rejected on a concrete technical ground, not preference:** `extract` is a *regex*
(`ItemValueResolver.cs:515-524`), and there is **no JSON field selection anywhere in this
codebase**. Selecting `WindowCount` would mean regex-scraping JSON, and `ExtractValue` returns
`null` on no-match, so a broken selector renders as silently-absent rather than as an error.

Independently, the user asked for the item to be "truly built out with item settings". That
mandate is dispositive; the above is why it is also *correct*.

### A.4 — Scope boundary vs SPEC-103 Part C

SPEC-103 Part C = presentation of the existing two telemetry fields. SPEC-104 = new data source
+ new fields + state. They compose (§C.3); they are not the same concern.

---

## §B — Baseline state (verified pre-implementation)

| Fact | Source |
|---|---|
| `public sealed record EngramResult(long? Facts, string? Verb);` | `EngramTelemetry.cs:7` |
| Built by parsing `$ENGRAM_HOME/telemetry.jsonl` (fallback `~/.engram/telemetry.jsonl`) | `EngramTelemetry.cs:41-47`, `:128-143` |
| `EngramTelemetry.Build(...)` called at **two** sites | `Program.cs:56-59`, `Program.cs:342-345` |
| Enclosing methods BOTH `async` | `RunAsync` `Program.cs:15`; `RunPreview` `Program.cs:289` |
| Config (`topLevel`) bound at `Program.cs:44`, **before** the engram probe at `:56` | `Program.cs:44` |
| Fire-early/await-late probe idiom | `GitBranch.ProbeAsync` `Program.cs:42` → awaited `:69` |
| Disk TTL cache, cross-process | `ItemCache.cs:138,158` |
| Cache key builder | `ItemCache.KeyFor(id, resolvedArgv, cwd, paneWidth, env)` `ItemCache.cs:119` |
| Command item defaults | `CommandProvider.cs:20-21` — TTL 30s, timeout 150ms |
| JSON source-gen is **case-sensitive** (`PropertyNameCaseInsensitive = false`) | `Config.cs` serializer context |
| Schema is **hand-written** | `SchemaCommand.cs:408-432` |
| `engram` absent from a *stripped* (`env -i`) PATH; present at `~/.local/bin/engram` interactively | §H.3 evidence — see §C.8.1 for what this does and does not prove |

---

## §C — Config surface

### C.1 — `EngramItemSettings`

```csharp
public sealed class EngramItemSettings
{
    // --- inherited from SPEC-103 Part C. DO NOT REMOVE. ---
    [JsonPropertyName("factsColor")] public string? FactsColor { get; set; }
    [JsonPropertyName("verbColor")]  public string? VerbColor  { get; set; }

    // --- SPEC-104 ---
    [JsonPropertyName("fields")]           public List<EngramFieldJsonConfig>? Fields { get; set; }
    [JsonPropertyName("activityWindow")]   public string? ActivityWindow { get; set; }   // default "1h"
    [JsonPropertyName("ttlSeconds")]       public int? TtlSeconds { get; set; }          // default 30
    [JsonPropertyName("timeoutMs")]        public int? TimeoutMs { get; set; }           // default 150
    [JsonPropertyName("idleAfterSeconds")] public int? IdleAfterSeconds { get; set; }    // default 300
    [JsonPropertyName("binaryPath")]       public string? BinaryPath { get; set; }       // §C.8
    [JsonPropertyName("stateColors")]      public EngramStateColorsJsonConfig? StateColors { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}
```

`ttlSeconds`/`timeoutMs` defaults reference `CommandProvider.DefaultTtlSeconds`/
`DefaultTimeoutMs` (30 / 150) rather than re-declaring literals, so the two subprocess paths
cannot drift.

### C.2 — `EngramFieldJsonConfig`

Mirrors `PaneItemPartJsonConfig` (`Config.cs:433-465`).

```csharp
public sealed class EngramFieldJsonConfig
{
    [JsonPropertyName("field")]  public string? Field { get; set; }   // vocabulary in §C.4
    [JsonPropertyName("format")] public string? Format { get; set; }  // "{}" placeholder
    [JsonPropertyName("color")]  public string? Color { get; set; }   // plain string — §C.6

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}
```

Deliberately NOT included: `extract`, `case`, `from`. Values here are already typed and
selected; regex-scraping a value we just parsed structurally would re-introduce exactly the
fragility §A.3 rejects.

### C.3 — Composition with SPEC-103's `factsColor`/`verbColor`

`fields` unset (the default) ⇒ exactly SPEC-103 Part C: two fragments, `facts` then `verb`,
coloured by `factsColor`/`verbColor` falling back to `dim`/`purple`.

`fields` set ⇒ authoritative for *which* fragments render and in what order. Colour precedence,
highest first:

1. `fields[i].color`
2. `factsColor` / `verbColor` (only for `field: "facts"` / `field: "verb"`)
3. the field's built-in default colour (§C.4)

### C.4 — Field vocabulary

Closed enum. An unknown `field` is a `--check` error (§F.3), not a silent skip.

**Tier 1 — telemetry (already parsed, NO subprocess):**

| `field` | Renders | Default colour |
|---|---|---|
| `facts` | `engram:<n>` | `dim` |
| `verb` | the verb string | `purple` |
| `lastEventAge` | humanised age of newest telemetry line (§E.3) | `dim` |

> **`lastEventAge` is GATED — see §L.1.** It requires `EngramProbeResult.LastEventAt` (§E.1).
> Until that ships, it must NOT be accepted by `--check`. A key that validates clean and
> renders nothing is the exact defect SPEC-103 existed to fix; do not reproduce it here.

**Tier 2 — `engram status --json` (subprocess):**

| `field` | JSON key | Renders | Default colour |
|---|---|---|---|
| `server` | `Server` | `Running` / `Stopped` | `dim` |
| `version` | `Version` | `1.0.0` | `dim` |
| `uptime` | `UptimeSeconds` | humanised (§E.3) | `dim` |
| `port` | `Port` | `7433` | `dim` |
| `pid` | `Pid` | `30695` | `dim` |

**Tier 3 — `engram activity --since <window> --json` (subprocess):**

| `field` | JSON key | Renders | Default colour |
|---|---|---|---|
| `lastKind` | `LastKind` | `embedding` | `dim` |
| `lastAge` | `LastAgeSeconds` | humanised (§E.3) | `dim` |
| `windowCount` | `WindowCount` | `226` | `dim` |
| `kind:<name>` | `Kinds[].Count` where `Kind == <name>` | integer, `0` if absent | `dim` |

`kind:<name>` matches **case-sensitively and exactly**. Absent kind renders `0`, not nothing — a
count of zero is information, and disappearing fragments make a statusline jump width.

`Home` / `StartedFrom` / `Initialised` / `WindowSeconds` / `SkippedLines` are intentionally not
exposed. `Initialised` feeds state (§D.2); the rest are diagnostics.

### C.5 — Probe activation (the perf contract)

**The set of selected fields determines which subprocesses run:**

- No Tier 2 field ⇒ **`engram status` never spawned.**
- No Tier 3 field **and** no state colour needing it ⇒ **`engram activity` never spawned.**
- Default config (`fields` unset) ⇒ **zero subprocesses, no binary resolution (§C.8)** —
  byte-identical to pre-change cost.

### C.6 — Colour type: plain `string?`, not `colorExpr`

`ColorResolution.Resolve(ColorExpr?, values, tokens)` needs the resolved values dictionary and
the token table. These colours are consumed inside `BuildEngram`, reached via the registry's
`BuildDefaultSegment(ctx)` delegate, which receives **only** an `ItemContext` — carrying
neither. Threading both through every `BuildDefaultSegment` for one decorative fragment is a
large cross-cutting change nobody asked for.

Accepted consequence: literal colour names/hex only; no `@token`, no inline colour rules.

### C.7 — `stateColors`

```csharp
public sealed class EngramStateColorsJsonConfig
{
    [JsonPropertyName("active")]      public string? Active { get; set; }
    [JsonPropertyName("idle")]        public string? Idle { get; set; }
    [JsonPropertyName("unreachable")] public string? Unreachable { get; set; }
    [JsonPropertyName("unavailable")] public string? Unavailable { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}
```

Non-null state colour ⇒ **overrides every fragment's colour** for that render. Null ⇒ per-field
colours (§C.3) apply unchanged.

**All four default to null.** The user chose byte-identical defaults. Settled — do not revisit,
and do not "helpfully" ship a non-null default. §F.4's docs amendment keeps the description
honest.

### C.8 — `binaryPath` and binary resolution

#### C.8.1 — What the §H.3 evidence actually establishes

`env -i /bin/sh -c 'command -v engram'` → not found; `which engram` → `~/.local/bin/engram`
interactively.

**`env -i` strips the environment completely.** It proves `engram` is not on a *default/empty*
`PATH`. It does **not** prove the statusline's child fails to find it: the statusline is spawned
by Claude Code, itself launched from the user's interactive shell, which normally passes its
profile-sourced `PATH` down. Bare `engram` probably *does* work for most users. `binaryPath`
exists for the minority where it does not (launchd/GUI-launched sessions, minimal environments,
non-standard installs). **The design is correct under either reading**, which is why no further
measurement was warranted.

#### C.8.2 — Resolution order

Resolve **once per render**, only when Tier 2/3 fields are selected (§C.5). First hit wins:

1. **`binaryPath` if set** — used verbatim. If missing or not executable ⇒ **probe failure
   (§D.4 branch 2). Do NOT fall through to 2–3.**
2. **Bare `"engram"`**, resolved by the OS via `PATH`.
3. **Fixed fallback probe list**, first existing executable wins:
   - `$HOME/.local/bin/engram`
   - `/usr/local/bin/engram`
   - `/opt/homebrew/bin/engram`

Step 3 gives a zero-config user on a stripped-`PATH` machine working Tier 2/3 probing. Costs
only `File.Exists` checks, no spawn, and never runs for a default config (§C.5).

**Step 1's no-fallthrough rule is deliberate.** If a user explicitly names a binary and we
silently run a *different* one because theirs was missing or mistyped, we execute something they
did not ask for and report its output as engram's state. Explicit configuration that is wrong
must fail, not get quietly substituted.

#### C.8.3 — SECURITY constraints (mandatory, not stylistic)

This key names an executable spawned on a render path — the one genuinely security-relevant
decision in this spec.

- **Never resolve relative to the current working directory.** claude-tui-line runs inside
  whatever repository the user has open. If `binaryPath` or any fallback entry were resolved
  against `cwd`, a repository containing a file named `engram` would execute simply because the
  user opened it. Clone a repo, the statusline renders, the attacker's binary runs.
  - A `binaryPath` containing a directory separator **must be rooted** (`Path.IsPathRooted`).
    Rooted ⇒ used as-is. Separator but not rooted ⇒ **`--check` error** (§F.3) and probe failure
    at runtime.
  - A `binaryPath` with **no** separator is a `PATH` lookup name, never a relative file. Do not
    prepend `./`, do not `Path.GetFullPath` it.
- **Fallback list is absolute paths only**, hardcoded as above. `$HOME` from the `HOME`
  environment variable. Never derived from `cwd`, from `ENGRAM_HOME` (a *data* directory), or
  from any config value.
- **No shell.** Spawn directly with an argv array, `UseShellExecute = false`, as
  `CommandProvider` does for non-`shell` items. No `sh -c`, no concatenating the path into a
  command line — a path with spaces or shell metacharacters must be inert.
- `activityWindow` is passed as its **own argv element**, never interpolated into a string.

#### C.8.4 — Not doing: an `ENGRAM_BIN` environment variable

Rejected: a third resolution source and a second place to look when this misbehaves, solving a
case `binaryPath` already covers. No `ENGRAM_BIN` convention exists in engram (it defines
`ENGRAM_HOME`, a data dir). If one appears, it slots between steps 1 and 2 as a one-line change.

---

## §D — State model

### D.1 — The enum *(corrected in rev 4)*

```csharp
public enum EngramState { Active, Idle, Unreachable, Unavailable }
```

**rev 3 said `internal`. That was wrong and the compiler caught it (CS0051):**
`EngramProbeResult` is a public record, and a public record's primary-constructor parameter
cannot be of a less-accessible type. `public` is the correct fix — it preserves
`EngramProbeResult`'s accessibility, which §E.1 pins deliberately, and it matches `EngramResult`
already being a public record.

The alternative — making `EngramProbeResult` internal too — was available but is not worth the
churn: `EngramResult` is already public, so internalising its successor would be an unrelated
accessibility change smuggled into this spec. Nothing outside `ClaudeTuiLine` /
`ClaudeTuiLine.Tests` references either type.

This was a spec-defect, not an implementation defect. Recorded rather than quietly fixed.

### D.2 — Derivation, cheapest signal first

First match wins:

1. **`Unavailable`** — telemetry file absent/unreadable/unparseable AND (if status probed)
   `Initialised == false`.
2. **`Unreachable`** — status probed AND (`Server != "Running"` OR `Initialised == false` OR
   `Pid` null). Requires Tier 2; not detectable without it.
3. **`Idle`** — newest known activity older than `idleAfterSeconds`. Age source, best first:
   `LastAgeSeconds` (Tier 3) → newest telemetry line timestamp (Tier 1).
4. **`Active`** — otherwise.

**Tier 1 alone distinguishes `Unavailable` / `Idle` / `Active` with no subprocess.** Only
`Unreachable` needs the probe.

### D.3 — State applies even when `fields` is unset

Computed for the default two-fragment render too. With §C.7's null defaults it has no visible
effect, but the plumbing must not be conditional on `fields`.

### D.4 — Probe failure ≠ unreachable

A probe that times out, or fails because the binary could not be resolved (§C.8), must NOT be
reported as `Unreachable`. Those are *our* failure, not engram's state, and rendering "engram is
down" because our own subprocess budget expired is a lie the user will act on.

**Parse stdout BEFORE judging the exit code:**

1. stdout **parses as the expected JSON shape** ⇒ trust it. Derive state from
   `Server`/`Initialised`/`Pid` per §D.2, **regardless of exit code.**
2. Otherwise (no stdout, unparseable, timeout, binary unresolved, kill) ⇒ **probe failed.**
   Status *unknown*, NOT unreachable. Fall back to Tier 1 derivation; render Tier 2/3 fields as
   absent per §E.4.
3. Stale cache present on failure ⇒ use it, as `CommandProvider.cs:72` already does.

**Why not branch on exit code:** if the CLI reports a stopped server as valid JSON on stdout,
branch 1 reads it correctly whether it exits 0 or non-zero. If it emits nothing parseable, branch
2 is correct either way. **The exit code is not load-bearing**, so no experiment was needed. Do
not add an exit-code branch back.

A non-zero exit with good JSON is a *success* path. Do not early-return on `ExitCode != 0` before
attempting the parse — that is the natural way to write it and it is wrong here.

---

## §E — Data plumbing

### E.1 — Probe result type *(gains `LastEventAt` in rev 4)*

```csharp
public sealed record EngramStatusInfo(
    bool Initialised, string? Server, int? Pid, int? Port,
    string? Version, long? UptimeSeconds);

public sealed record EngramActivityInfo(
    string? LastKind, long? LastAgeSeconds, long? WindowSeconds,
    long? WindowCount, IReadOnlyDictionary<string, long> Kinds);

public sealed record EngramProbeResult(
    EngramResult? Telemetry,
    EngramStatusInfo? Status,
    EngramActivityInfo? Activity,
    EngramState State,
    DateTimeOffset? LastEventAt);   // rev 4 — see §L.1
```

**`LastEventAt` stores a timestamp, not a precomputed age, deliberately.** Tier 2/3 results are
disk-cached with a 30s TTL; a stored *age* would be served stale from cache, while a timestamp
re-derives correctly at render time. `EngramTelemetry.BuildWithTimestamp` already computes this
value for §D.2's Idle fallback — rev 3 simply failed to retain it past
`EngramProbe.BuildAsync`.

(Known, accepted: `lastAge` from Tier 3 *is* a precomputed age and *is* cached, so it can read
up to `ttlSeconds` stale. Acceptable for a statusline; not worth a second probe.)

`ItemContext.Engram` is `EngramProbeResult?`, not `EngramResult?`.

**This is a deliberate breaking internal change, chosen over widening `EngramResult` with
optional parameters.** SPEC-103's entire root cause was an optional trailing parameter
(`itemSettings = null`) that a call site omitted *silently* — it compiled, and the feature
shipped dead. Changing the type makes the compiler enumerate every construction site. A change
the compiler catches beats a change it does not. **Do not soften this into an optional parameter
to reduce the diff — the larger diff is the point.**

### E.2 — `EngramCli.cs`

```csharp
internal static class EngramCli
{
    internal static Task<EngramStatusInfo?> ProbeStatusAsync(
        string? binary, string cacheDir, int ttlSeconds, int timeoutMs);

    internal static Task<EngramActivityInfo?> ProbeActivityAsync(
        string? binary, string window, string cacheDir, int ttlSeconds, int timeoutMs);
}
```

- `binary` null ⇒ return null immediately, no spawn (§D.4 branch 2).
- Cache via `ItemCache.KeyFor` / `TryRead` / `Write`, synthetic ids `"engram:status"` /
  `"engram:activity"`, argv as the resolved command line **including the resolved binary path**,
  so changing `binaryPath` or `activityWindow` invalidates naturally. `cwd: null`,
  `paneWidth: null`, empty env dict.
- Timeout, tree-kill, stale-fallback **identical to** `CommandProvider` (`:145-176`). If
  extracting its helper would change `CommandProvider`'s public surface, copy the block instead
  — do not perturb a working hot path to save a few lines.
- `UseShellExecute = false`, argv array, no shell (§C.8.3).

**PascalCase gotcha:** the engram CLI emits `"Home"`, `"Server"`, `"WindowCount"`, and the
source-gen context sets `PropertyNameCaseInsensitive = false`. Every DTO property needs an exact
`[JsonPropertyName("Server")]`. Register both DTOs with `[JsonSerializable]`. Getting this wrong
yields silent all-null deserialisation that looks exactly like a probe failure — so §G.2 asserts
on parsed values, not non-nullness.

Related to §D.4: "parses" must mean more than "did not throw". An all-null record from a casing
mismatch deserialises *successfully*. Treat a status response whose `Server` and `Pid` are both
null as a parse failure (branch 2), not a stopped server — otherwise the casing bug masquerades
as `Unreachable`, exactly the lie §D.4 exists to prevent.

### E.3 — Humanised durations

`uptime`, `lastAge`, `lastEventAge`: `<60s` → `"45s"`; `<60m` → `"12m"`; `<24h` → `"3h"`; else
`"2d"`. Integer truncation, no decimals, no "ago" suffix (the `format` key supplies wording).
Reuse an existing duration helper if one exists.

### E.4 — Absent values

A selected field whose probe failed or whose JSON key is null renders as **nothing**, separator
suppressed. If *every* selected field is absent, `BuildEngram` returns `null` and the item
disappears entirely — matching pre-change behaviour when `EngramResult` is null. Exception:
`kind:<name>` renders `0` per §C.4 when the activity probe *succeeded* and the kind was merely
absent.

### E.5 — Wiring, both call sites, via ONE helper

`Program.cs:56-59` and `Program.cs:342-345` were duplicated. **No third copy.**

```csharp
internal static Task<EngramProbeResult?> EngramProbe.BuildAsync(
    string? sessionId, DateTimeOffset now, EngramItemSettings? settings, string cacheDir);
```

`BuildAsync` owns §C.8's binary resolution (once, only if Tier 2/3 fields are selected).

Follows the `GitBranch.ProbeAsync` idiom (`Program.cs:42`→`:69`): start *after* the config load
(line 44, since it needs `settings`), `await` just before `ItemContext` construction. Both
enclosing methods are already `async` — no sync-over-async, no `.Result`, no `.Wait()`. Wrapped
in the same `try/catch ⇒ null` both sites already use.

### E.6 — `SyntheticFixture`

Supplies a synthetic `EngramProbeResult` with plausible `Status`/`Activity` so `--preview`
exercises the new fields **without spawning anything** and **without resolving a binary**. The
fixture must never shell out.

---

## §F — Surfaces

### F.1 — Registry

`ItemRegistry.cs:81` — engram row passes `ctx.Engram` and `ctx.ItemSettings?.Engram` into
`BuildEngram`. `ResolveEngram` keeps returning the same plain-text value for the default case, so
`from`/`extract` on a derived item referencing `engram` does not change meaning.

### F.2 — Schema (hand-written — will NOT self-update)

`SchemaCommand.cs:408-432` enumerates every settings class **literally**. Entries for the six new
`EngramItemSettings` keys, `EngramFieldJsonConfig`, `EngramStateColorsJsonConfig`, and the §C.4
vocabulary. `SchemaCommandTests` updated — expected churn, not a regression.

`binaryPath`'s description states that a value containing a directory separator must be absolute,
and a bare name is looked up on `PATH`.

### F.3 — `--check` validation

- Unknown `field` ⇒ error naming the closest valid field.
- `fields` present but empty ⇒ error.
- `ttlSeconds` < 0 or `timeoutMs` <= 0 ⇒ error.
- `idleAfterSeconds` <= 0 ⇒ error.
- **`binaryPath` with a separator but not rooted ⇒ error** (§C.8.3), message says absolute
  required.
- **`binaryPath` empty string ⇒ error.**
- `binaryPath` rooted but nonexistent ⇒ **warning, not error** — a config may be checked on a
  different machine from where it runs.
- `activityWindow` ⇒ accept any non-empty string, defer to the CLI (§H.1). Invent no grammar.
- **`lastEventAge` ⇒ see §L.1.** Must be rejected while unimplemented.

### F.4 — The `--items` description

The engram item's description claimed *"Its colour reflects whether the store is reachable and
active."* With §C.7's null defaults that is **false for an unconfigured user**. Amended to state
that state-based colouring applies **when `itemSettings.engram.stateColors` is configured**.

With §I.1 resolved toward byte-identical defaults, this docs amendment is the *only* thing making
the description honest.

---

## §G — Verification

- **G.1** Default config: `--preview` byte-identical, no subprocess spawned, no binary resolution
  attempted. Assert on a spawn/resolve counter, not just rendered text.
- **G.2** `fields:[{field:"windowCount"}]` against stubbed activity JSON renders `226`. Assert the
  parsed integer, not non-nullness (§E.2's casing trap).
- **G.3** `kind:embedding` → `16`; `kind:nonexistent` → `0`.
- **G.4** Probe timeout ⇒ Tier 1 fallback, **not** `Unreachable` (§D.4).
- **G.5** `stateColors.unreachable` + stubbed stopped-server JSON ⇒ all fragments take the
  override.
- **G.6** Stubbed **valid stopped-server JSON on stdout with non-zero exit** ⇒ `Unreachable`, not
  probe-failure (§D.4 branch 1).
- **G.7** SPEC-103-era config renders exactly as under SPEC-103 (§C.3 precedence).
- **G.8** `--check` rejects unknown `field` and empty `fields`.
- **G.9** `binaryPath` rooted-but-nonexistent ⇒ probe failure, **fallback list NOT consulted**.
- **G.10** `--check` rejects `binaryPath:"bin/engram"` and `binaryPath:""`.
- **G.11** Resolution never consults `cwd`: a file named `engram` in the working directory is
  **not** selected (§C.8.3).
- **G.12** *(rev 4)* Every field name accepted by `--check` renders a non-empty fragment given a
  probe result that supplies its source. **No accepted field may be inert** — this is the §L.1
  guard, generalised so a future vocabulary addition cannot repeat it.

**Must not move:** `GoldenParityTests`, `ItemFormatParityTests`, `EndToEndItemValuesTests`,
`SegmentTruncationSpansTests`.

No test may invoke the real `engram` binary.

---

## §H — Evidence items (all resolved)

- **H.1 — RESOLVED.** `--since` grammar undocumented beyond `1h` in both `engram --help` and
  `engram activity --help`. Accept any non-empty string, defer to the CLI (§F.3). A rejected
  window is a probe failure (§D.4 branch 2) degrading to Tier 1 — acceptable.
- **H.2 — WITHDRAWN in rev 2. Do not run it.** It asked for the engram server to be stopped to
  observe an exit code. **Unsafe** — the engram server is the live memory store for every active
  Claude session on this machine, and stopping it would disrupt memory capture across all of them;
  an experiment whose blast radius is the user's running environment needs a far better
  justification than this one had. **And unnecessary** — §D.4 parses stdout before consulting the
  exit code, correct under both possible answers. §G.6 covers it with a stub.
- **H.3 — RESOLVED in rev 3.** Evidence in §B, interpreted in §C.8.1, design in §C.8. Note
  §C.8.1: `env -i` overstates the problem and the design is correct under either reading.

---

## §I — User decisions

- **I.1 — RESOLVED.** User chose **byte-identical defaults**. Null `stateColors` stand (§C.7);
  §F.4's docs amendment is the load-bearing close for §C.7. Do not revisit.
- **I.2 — `idleAfterSeconds` default 300** (5 min). A judgment about what "active" means for this
  user's workflow. Non-blocking, user-overridable.

---

## §J — What must NOT change

- `telemetry.jsonl` parsing (`EngramTelemetry.cs`) — still the Tier 1 source and the default.
- `OutputStyleFormat` / `AutocompactFormat` and their rendering (SPEC-103's territory).
- `CommandProvider`'s public surface and behaviour (§E.2's caveat).
- The `format`-override path on pane items.
- Default render output, default subprocess count, default binary-resolution count: all
  zero-delta (§C.5, §G.1).

---

## §L — Closeout and follow-up *(new in rev 4)*

### L.1 — `lastEventAge` must not remain accepted-but-inert  **[OUTSTANDING]**

**Status at implementation:** `lastEventAge` is in the §C.4 vocabulary and `--check` accepts it,
but selecting it renders nothing — `EngramProbeResult` (rev 3) had no timestamp to source it
from.

**This is a correctness item, not a nicety.** A config key that passes `--check`, appears in
`--schema`, and silently does nothing is *precisely* the defect SPEC-103 was written to fix
(`showLabel` validated clean and had no effect). Shipping a second instance of that exact shape —
in the spec written to fix the first — is the one outcome to avoid here.

**Decision: implement it (option a), do not drop it (option b).** `lastEventAge` is the **only**
age signal available without a subprocess; `lastAge` requires spawning `engram activity`.
Dropping it removes the sole zero-cost age display and cuts against §C.5's whole tiering premise.
The fix is small: the value already exists (`EngramTelemetry.BuildWithTimestamp`) and merely needs
retaining — §E.1's `LastEventAt`, populated in `EngramProbe.BuildAsync`, rendered via §E.3.

**Required interim, if the follow-up is not immediate:** remove `lastEventAge` from the accepted
vocabulary so `--check` **errors** on it. A rejected key is honest; an accepted inert one is not.
Do not ship the accepted-and-inert state in a released build.

§G.12 generalises the guard so a future vocabulary addition cannot repeat this.

### L.2 — Accepted as-is

- `EngramState` accessibility (§D.1) — spec-defect, corrected in text, implementation was right.
- `lastAge` cache staleness up to `ttlSeconds` (§E.1) — inherent to caching, acceptable for a
  statusline.
- §I.2's `idleAfterSeconds = 300` — user-overridable, no evidence it is wrong.

---

## §M — Confidence

Confidence **high** on §C–§G, and the implementation confirmed the load-bearing assumptions:
both call sites already `async`, settings in scope before the probe, disk cache working across
per-render processes.

**§C.8.3 remains the part to review hardest** — the only place a config value becomes an executed
program. The cwd-resolution prohibition is what keeps a hostile repository from getting code
execution on a statusline render. If any part of §C.8 is ever simplified, that constraint is not
the part to simplify.

Two spec-defects surfaced during implementation, both recorded rather than quietly patched: §D.1's
accessibility contradiction, and §L.1's missing `LastEventAt`. The second is the one that matters,
because its failure mode is silence.
