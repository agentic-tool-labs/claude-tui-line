# SPEC-101 Amendment 2 (§13) — implementation complete, tests green

## Status

- `CalibrationRecord.cs` (new): keyed `versions: Dictionary<string, VersionEntry>` shape,
  `MigrateIfNeeded()`, `Prune()` (cap 10, evict oldest `promptFirstSeen`). Legacy
  `promptedForVersion`/`promptFirstSeen`/`dismissedVersion` kept as
  `[JsonIgnore(WhenWritingNull)]` fields, read-only in practice.
- `CalibrateCommand.cs`: rules 5-8 rewritten per your corrected design — rule 5 exact-match only
  (with the anti-drift comment you asked for, distinguishing "still valid" from "bother the user"),
  rules 6/7 kept (exact-version, unaffected by coarsening), rule 8 coarsens via `MajorMinor`
  (ordinal-string-only, public now for direct testing) + baseline
  (`CalibratedVersion ?? NewestObservedVersion`). Verify-prompt text now contains "different
  lengths on purpose" and never prints row A/B's numeric widths. `--reject` prints the §6.6
  recovery line. `--dismiss` marks every `Versions` entry and reports "nothing to dismiss" when
  empty/absent.
- `SchemaCommand.cs`: `chromeReserve`'s description now notes that the auto-nudge only fires on a
  major.minor change, so a patch-level chrome shift needs manual `--calibrate` — per your §7
  load-bearing note.
- `dotnet build` (src): exit 0, 0 errors.
- `dotnet test`: 1690/1690 passing, including the full §13.5 rewrite of
  `CalibrationPromptTests.cs` (write-frequency-by-content-diff, not mtime; migration for both the
  same-version and different-version dismissed cases, and the `promptFirstSeen`-absent case;
  pruning; keyed-shape round-trip; unparseable-record-as-absent; dismiss-marks-every-entry; the
  `MajorMinor` table; both baseline-selection cases) and a new §8.11 pair in `CalibrateTests.cs`
  (`--saw` and `--set`) for the verify-text requirement.
- README.md / SPEC.md: checked, neither documents the record's JSON shape or `--dismiss`
  semantics, so §13.6's conditional update doesn't apply — no changes made there.

## One interpretation I made without explicit spec text — flagging per protocol

`RunConfirm()` now sets `record.CalibratedVersion = NewestObservedVersion(record) ??
record.CalibratedVersion` (previously `record.CalibratedVersion = record.PromptedForVersion`,
which no longer exists as a live signal under the keyed shape). Neither §13.3 nor your rule-8
correction specified what "the version currently being calibrated" should resolve to when
`--confirm` runs. I picked "most recently seen version" as the closest analog to the old
single-slot behavior. If a different pane (on an older version) confirms after a newer pane
already prompted, this could set `CalibratedVersion` to a version that pane never actually
verified against. Tell me if you want a different rule here (e.g. threading the actual prompted
version through calibration state instead of inferring it from the record).

## Behavioral note, not a defect

Rule 8's coarsening means an already-seen different-series version (in `Versions` but not
dismissed, not window-expired) re-prompts on every qualifying render until dismissed or the
baseline catches up — only the record *write* is deduped, not the visible nudge. Verified this
matches the existing per-render nudge design (it's a persistent footer line, not a one-shot
toast), but noting it since it wasn't spelled out explicitly.

## Update — RunConfirm binding fix applied

Implemented your ruling exactly:

- `CalibrationState` gains `observedVersion` (string?, `[JsonPropertyName("observedVersion")]`).
- `TryRunHookProbe` now takes `rawInput`, extracts `.Version` via
  `StatusInputJsonContext.Default.StatusInput` (malformed/absent -> null, never guessed), and
  writes `state.ObservedVersion` unconditionally on every probe render (ruler and verify) —
  `observedColumns`'s ruler-only/write-once semantics untouched.
- `RunConfirm` now does `record.CalibratedVersion = state.ObservedVersion ?? record.CalibratedVersion`
  — never `NewestObservedVersion(record)`. `WriteChromeReserveToConfig` still runs unconditionally
  first, so a null `observedVersion` never blocks the config write.
- `Program.cs`'s call site now passes `rawInput` through.
- Added tests: `Confirm_SetsCalibratedVersionFromStateObservedVersion`,
  `Confirm_ObservedVersionAbsent_WritesConfigButLeavesCalibratedVersionUnchanged`, and three
  `TryRunHookProbe_*ObservedVersion*` tests (ruler, verify, malformed-payload-stays-null) in
  `CalibrateTests.cs`.
- One test-authoring bug this surfaced and fixed on my end, not a production bug: my original
  `KeyedShape_DismissingOneVersionDoesNotAffectAnother` test had no `CalibratedVersion` set, so
  `NewestObservedVersion` could return the very version being evaluated as its own baseline
  (self-comparison -> trivially "same series" -> no prompt). That's consistent with your baseline
  formula, not a defect — it's the same self-suppression the `WriteFrequency_*SameVersion*` test
  already relies on. Fixed by giving the test record a `CalibratedVersion` in a third series so the
  baseline is deterministic.
- `dotnet build`: exit 0. `dotnet test`: 1695/1695 green.

## Update — §13.8: rule 8 binding correction applied (the first-run-nudge-dies-after-one-render bug)

Applied exactly as specified:

- Rule 8 now: `calibratedVersion` absent -> always prompt (no series comparison, no baseline
  substitute); `calibratedVersion` present -> prompt iff `MajorMinor(version) !=
  MajorMinor(calibratedVersion)`. Rules 6/7 are the only things bounding the no-baseline case now.
- `NewestObservedVersion` deleted entirely (was only ever used for the now-rejected fallback).
- §13.5 item 4 (baseline-selection tests) deleted, not adjusted — they tested the removed
  behavior. `KeyedShape_DismissingOneVersionDoesNotAffectAnother` simplified: no longer needs a
  `CalibratedVersion` workaround, works as an ordinary case.
- New regression-guard test `Rule8_NoCalibratedVersion_AlreadySeenVersionWithinWindow_StillPromptsOnSubsequentRenders`:
  three consecutive renders of the same never-confirmed version, all assert `True`. This is the
  exact case that silently passed before (write-count assertions didn't check the prompt boolean
  on repeat calls, only that the record stopped changing).
- Also applied the other three §13.8 items from your first message: `TryRunHookProbe`'s doc comment
  now explains it reads `version` out of `rawInput` independently (already true — no code change
  needed, the ordering constraint was already satisfied), `observedVersion` now has
  `[JsonIgnore(WhenWritingNull)]` so a null payload version leaves the key ABSENT (not
  present-as-null), and RunConfirm/RunDismiss both got the contrastive §12.6/§13.8 comment
  explaining state-file-vs-record. Added the discriminating confirm test
  (`Confirm_UsesStateObservedVersion_NotTheRecordsNewestVersionsEntry`, record's newest entry and
  state's observedVersion deliberately different) plus ruler/verify/malformed-payload
  observedVersion tests.
- `dotnet build`: exit 0. `dotnet test`: 1695/1695 green.

## Not yet done

No commit. Per repo convention this needs Jim's explicit go-ahead separately from the earlier
base+Amendment-1 commit (`8ae0180`, already pushed).
