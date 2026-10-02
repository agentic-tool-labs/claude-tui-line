# Report: SPEC-47 fallback-pane diagnostic (final — post Revision 4)

Branch `task-47-fallback-diagnostic`, worktree
`/Users/jimcline/git/repos/claude-tui-line-task-47-fallback-diagnostic`, based on local `main`@`8437c37`.
Two commits:
- `2e2c16b` — pure relocation (§5.0.2): `LoadRenderConfig`/`ComposeResolutionFailureReason`/
  `ComposeUnreadableReason`/`BuildFallbackConfig` moved from `Program.cs` top-level-statement local
  functions into `internal static class ConfigResolution` (new file
  `src/ClaudeTuiLine/ConfigResolution.cs`). No signature/behavior change in this commit.
- `775a5d7` — behavioral change + tests: `FallbackResult` guard (§4.3), null-`configPath` rung 3
  (§3.3), newline-stripped resolution-failure reason (§5.1 test 3), and all new/modified tests.

This supersedes the Revision-1 report below the `---` line; that content is kept only as history —
Fix (A)'s original diagnostic-`PaneItem` design it describes was withdrawn in Revision 2.

## What shipped

- **`ConfigResolution.FallbackResult(string? configPath, string reason, int protectedLength)`**
  (§4.3): `reason` is non-nullable with no default. All three `LoadRenderConfig` fallback paths
  (parse error, asserted-missing file, resolution-throw catch) now route through it — the old
  catch-all that returned a `null` `UnreadableReason` no longer compiles as written; the type itself
  enforces the coupling. `ConfigResolution.cs:63-67`.
- **`ConfigResolution.ComposeResolutionFailureReason(Exception ex)`** — `"config could not be
  resolved: {message}"`, message run through `StripNewlines` first (`ConfigResolution.cs:77-81`)
  since `Exception.Message` is not guaranteed newline-free and the diagnostic is a one-row channel.
- **`ConfigUnreadableMessage.Format`** (§3.3): `path` parameter is `string?`. Rungs 1-2 wrapped in
  `if (path is not null)`; a null path enters rung 3 (pathless) directly — no sixth rung.
  `width!.Value` at the reason-budget line got one added `!` (CS8629) since the compiler can no
  longer flow-prove `width` non-null once rungs 1-2 are conditional. `ConfigUnreadableMessage.cs:23-55`.
- **`Program.cs:35`** — call site updated to `ConfigResolution.LoadRenderConfig(explicitConfigPath)`.
- **`Program.cs:43`** — `configPath!` null-forgiving operator dropped; `Format` accepts null now.

## §5.2 gap — struck by design, not by omission

Tests 1/2 (force `ResolveTopLevel`/`ResolveRootPane` to throw) remain unwritten. Per §5.0.1's
reachability-vs-controllability ruling: the Revision-4 relocation into `ConfigResolution` fixed
*reachability* (tests can now call `LoadRenderConfig` directly, which they do — see below) but not
*controllability* (nothing can make `ResolveTopLevel`/`ResolveRootPane` throw for any real
`UserConfig` — that's §1.3's dormant-invariant point, unchanged by the move). No fault-injection
hook was added — categorically rejected, §5.0.0/§7.

The three §5.2 self-closing requirements, done:
- (a) Gap named explicitly, at its current location: the untested lines are
  `ConfigResolution.cs:47-49` (the `try` body: `ResolveTopLevel`, `ResolveRootPane`, and the
  success-path return) and the `catch (Exception ex)` block at `ConfigResolution.cs:54-57`.
- (b) Comment on the catch block citing §5.2 — present, `ConfigResolution.cs:51-53`.
- (c) No action needed now; the first future `throw` added under either resolver makes this
  block reachable-and-testable in the same commit, per §1.3/§5.2.

## §5.1 tests — final state

| # | What | Where | Status |
|---|------|-------|--------|
| 3 | `ComposeResolutionFailureReason` strips newlines | `ConfigResolutionTests.cs` | written, passes |
| 4 | `Format(null, reason, width)` → rung 3, no placeholder, no throw | `ConfigUnreadableMessageTests.cs::NullPath_EntersRung3Directly_NoPlaceholderNoException` | written, passes — **this is E2's proof; reported explicitly per Revision 4** |
| 5 | CLI subprocess coverage of parse-error/missing-file paths | `PreviewCliTests.cs` (existing, unchanged) | confirmed sufficient, left as-is per Revision 4 constraint (not converted) |
| 6 | Width-degradation sweep, resolution-failure-shaped reason, all 5 rungs | `ConfigUnreadableMessageTests.cs::ResolutionFailureReason_DegradesThroughAllFiveRungs_AsWidthNarrows` | written, passes |
| 7 | `LoadRenderConfig` reachable-tier: parse error, asserted missing file | `ConfigResolutionTests.cs` (2 tests) | written, passes |
| 8 | `FallbackResult` output matches `BuildFallbackConfig`'s unchanged shape | `ConfigResolutionTests.cs::FallbackResult_ResolvedConfigAndPaneMatchMainsUnchangedOutput` | written, passes — this is the one test Revision 3 struck that the refactor genuinely restores |
| 9 | `tools/check-all.sh` | — | run, see below |

New file: `tests/ClaudeTuiLine.Tests/ConfigResolutionTests.cs` (6 tests). Modified:
`tests/ClaudeTuiLine.Tests/ConfigUnreadableMessageTests.cs` (+2 tests).

## Verification run

- `dotnet build src/ClaudeTuiLine -c Release` — exit 0, no errors/warnings.
- `dotnet test tests/ClaudeTuiLine.Tests -c Release --filter
  "FullyQualifiedName~ConfigResolutionTests|FullyQualifiedName~ConfigUnreadableMessageTests"` —
  **17/17 passed**, 0 failed, 0 skipped. Smoke-test scope only, per dispatch ("full verification is
  cdtui-worker's job"); did not run the full suite.
- `tools/check-all.sh` — **exit 1**, but both failures are pre-existing and unrelated to this diff:
  1. `check-citations`: 13 undefined section citations across various SPEC files (§1.3, §10.11,
     §12.4.x, §12.5.x, §12.8.x, §12.9, §4.4, §5.0, §5.2-5.5, §7.2, §7.5, §8.1-8.9, §9.0) — several of
     these numbers match SPEC-47's own subsection scheme, so this may be SPEC-47 citing its own
     subsections (e.g. `§5.0`) without a matching heading; flagging rather than fixing, since editing
     spec-doc headings is not this diff's scope and I didn't diagnose which file(s) hold the bad
     citations.
  2. `check-doc-tokens`: `README.md:162` cites `border` as a literal token not present in the
     `--accepted --json` registry.
  Neither touches a file this diff changed (`ConfigResolution.cs`, `ConfigUnreadableMessage.cs`,
  `Program.cs`, the two test files). Reporting up rather than fixing, since diagnosing/fixing either
  is outside SPEC-47's scope and I have not verified whether these two failures pre-date this branch
  or were introduced by unrelated work already on `main`@`8437c37`.

## Fix (A) structural guarantee — code-review-only, not test-assertable

§4.3's core claim — "no caller can obtain a fallback pane without also supplying a reason" — is a
type-level guarantee (`reason` non-nullable, no default), not a runtime behavior. It can't be
asserted by a test (there's no code path left that would demonstrate its absence); it's provable
only by reading `FallbackResult`'s signature and confirming all three `LoadRenderConfig` call sites
route through it (they do — `ConfigResolution.cs:27,34,56`). Flagging for the Reviewer to check by
inspection rather than by a claimed test.

## What the Reviewer should look hardest at

- `ConfigUnreadableMessage.cs`'s `width!.Value` at the reason-budget line (line 47) — confirm
  reachability still guarantees non-null `width` now that rungs 1-2 are behind `if (path is not
  null)`.
- The commit split: confirm `2e2c16b` is genuinely behavior-preserving (diff should be pure code
  motion + the 3 `using`/CS0103 fixes) and all behavioral changes are isolated to `775a5d7`.
- The two `check-all.sh` failures above — confirm they predate this branch (e.g. `git stash` this
  diff and re-run on `8437c37`) before treating them as out-of-scope.

---

# Report: SPEC-47 fallback-pane diagnostic (Revision 1 — historical, superseded)

Branch `task-47-fallback-diagnostic`, worktree
`/Users/jimcline/git/repos/claude-tui-line-task-47-fallback-diagnostic`, based on local `main`@`8437c37`
(matches dispatch's stated HEAD).

## Fix (B) — implemented, §3 complete

- `src/ClaudeTuiLine/Program.cs`: catch-all at the third `BuildFallbackConfig` call site (was
  `:894-898`) now binds the exception and returns `ComposeResolutionFailureReason(ex)` as the
  reason instead of `null`. Added `ComposeResolutionFailureReason(Exception ex) =>
  $"config could not be resolved: {ex.Message}"` as a sibling of `ComposeUnreadableReason`, per
  §3.1/§3.2's recommended shape.
- `src/ClaudeTuiLine/ConfigUnreadableMessage.cs`: `Format`'s `path` parameter changed from
  `string` to `string?` (§3.3/E2). When `path is null`, rungs 1-2 (full path, elided path) are
  skipped entirely and rendering enters directly at rung 3 (pathless) — no new rung added, no
  placeholder path synthesized, matching the ruling in §3.3.
- `src/ClaudeTuiLine/Program.cs:43` (the `Main` call site): dropped the `configPath!`
  null-forgiving operator now that `Format` accepts null legitimately.
- §3.4: did **not** rename `UnreadableReason`/`ComposeUnreadableReason` — per spec, noted as
  follow-up only.

## Fix (A) — withdrawn by Revision 2, see current report above

E1 found no literal-text-carrying `PaneItem` construction exists; `Format`-with-null-`Item` renders
nothing. Revision 2 replaced the original diagnostic-`PaneItem` design with §4.3's `FallbackResult`
type-level guard — implemented, see above.

## §5 — resolved by Revisions 3/4, see current report above

Original blocker (no test seam for `ResolveTopLevel`/`ResolveRootPane`) resolved via Option 2
(relocation into `ConfigResolution`) for reachability; tests 1/2 stay struck per §5.2 since
controllability is a separate, unresolved axis. See current report above for final test disposition.
