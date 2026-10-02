# SPEC-101 implementation report (for ctl-arch1 review)

Base §§1-11 plus Amendment 1 §12 both implemented, built clean, tests green
(1669/1669 in ClaudeTuiLine.Tests, 42/42 in ClaudeTuiLineMcp.Tests).

## Files touched

- `src/ClaudeTuiLine/CalibrateCommand.cs` (new) — ruler/verify row builders,
  digit→reserve arithmetic (verified against the §3.3 worked example:
  columns=90, d=5 → reserve=4), `TryRunHookProbe`, `MaybeAppendNudge` (§12.3's
  8-rule trigger table, §12.4's one-row nudge), the `--calibrate` sub-flag
  dispatcher, §6.4's JsonNode-based config read-modify-write.
- `src/ClaudeTuiLine/Program.cs` — `TryRunHookProbe()` inserted after the
  stdin read/before `ParseInput` (§5.1); `MaybeAppendNudge(...)` inserted
  after `DrawRows(...)`/before `return 0` (§12.4); `--calibrate` plus its 8
  sub-flags wired into `RunCli`'s mode table, with `--config` rejected per
  §6.2 (calibrate's `acceptedModifiers` is `["json"]` only).
- `src/ClaudeTuiLine/StatusInput.cs` — `Version` (nullable, `"version"`) per
  §12.1.
- `src/ClaudeTuiLine/Config.cs` — `LayoutConfig.CalibrationPrompt` /
  `ResolvedConfig.CalibrationPrompt` (default true) per §12.7.
- `src/ClaudeTuiLineShared/ConfigPath.cs` — `ResolveCalibrationStatePath()`,
  `ResolveCalibrationRecordPath()` (both beside the config file).
- `src/ClaudeTuiLineShared/ConfigWriter.cs` (new) — `WriteAtomic` moved here
  per §6.3's decision rule (ClaudeTuiLineMcp already referenced
  ClaudeTuiLineShared); `ClaudeTuiLineMcp/ConfigFile.cs` now delegates.
- `src/ClaudeTuiLine/SchemaCommand.cs` — `chromeReserve` description now
  mentions `--calibrate`; added `calibrationPrompt` field + optional-key
  entry (a pre-existing `SchemaCommandTests` reflection check caught the
  latter being missing on first pass — fixed).
- `tests/ClaudeTuiLine.Tests/CalibrateTests.cs` (new, 10 tests per §8),
  `tests/ClaudeTuiLine.Tests/CalibrationPromptTests.cs` (new, 15 tests
  covering §12.9's 10 items — the trigger table gets one test per rule).
- `tests/ClaudeTuiLine.Tests/ConfigTests.cs` — added to a new
  `[Collection(nameof(CalibrationEnvVarCollection))]` (defined in
  CalibrateTests.cs, `DisableParallelization = true`): it, CalibrateTests,
  and CalibrationPromptTests all mutate the process-wide
  `CLAUDE_TUI_LINE_CONFIG`/`HOME` env vars, and xUnit parallelizes across
  classes by default — without this they raced each other's file paths
  (caught by the first test run: `DirectoryNotFoundException`s and stale
  timestamps that went away once serialized).
- `README.md` (new "calibrate" paragraph under "Layout, briefly"), `SPEC.md`
  (§6 gained a `--calibrate` paragraph).

## Discrepancies found vs. the spec text, resolved without asking

- §6.4 cites "ConfigTools.cs:88" as the unknown-key-preserving merge pattern
  to copy; the actual method there takes an already-`JsonNode`-typed param
  and doesn't demonstrate a merge. Implemented the merge directly in
  `CalibrateCommand.WriteChromeReserveToConfig` (parse existing bytes as
  `JsonNode`, get/create `layout` as `JsonObject`, set `chromeReserve`, write
  back) — satisfies the actual requirement (preserve unknown keys via
  untyped DOM) regardless of the imprecise citation.

## What's NOT done — genuinely blocking, needs Jim on a live pane

- **E1** (§9, blocking): does Claude Code render a pure-digit probe row
  verbatim? Does U+2500 (─) render single-width, or does ASCII `-` need to
  be the fallback? Does an already-tall statusline drop a row when the
  nudge appends one more?
- **E4** (§12.10, blocking for the auto-prompt half only), sharpened per
  your earlier guidance: (1) is a top-level `version` present at all in
  Jim's current real payload? (2) if not, is there a version-ish key under
  another name or nesting? (The one real fixture on disk,
  `real_captured_workspace.json`, has `"version":"2.1.233"` — old enough
  that renaming/relocation since then is the live risk, not absence.)

No commit has been made — repo convention is no commit without an explicit
user request, and none has been given for this work yet.
