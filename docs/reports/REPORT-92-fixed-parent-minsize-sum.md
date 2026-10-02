# REPORT — SPEC-92 fixed-parent minSize-sum

Issue #92. Implemented per `SPEC-92-fixed-parent-minsize-sum.md`, anchored to `62687bb`.
Branch `task-92-fixed-parent-minsize`, worktree
`claude-tui-line-task-92-fixed-parent-minsize`. Committed as `e230847`.

## Update — post cdtui-worker review

Rebased onto `origin/main` (`c104fc4`, picks up #47's merge and its check-counts fix for free).
Fixed the one flagged item: §11.1's inferred span for the re-anchored `:5986-6014` citation was
`:6033-6061`, derived from SPEC-92 §12(c)'s table endpoints — but those endpoints were pre-#92-edit.
#92's own edit to the third bullet (the "bounded... in the same sense as the first bullet" clause)
added 2 lines, so the correct current span is `:6033-6063`. Fixed. Rebuilt (exit 0) and re-ran the
9 targeted tests (4 ConfigCheckTests.cs + 2 Spec92_ + SPEC-91's V6/V6b/V13a) — all pass.

## Code change

`src/ClaudeTuiLine/ConfigCheck.cs` — `CheckSplitBounds` (was `:925-949`). Merged the two
independent bound guards into one, per spec §5.1 verbatim:

```csharp
var parentBound = SizeResolver.FixedSize(split) ?? split.MaxSize;
if (parentBound is not int bound) { yield break; }
```

now feeds *both* the fixed-sum check and the `minSize`-sum check, instead of the `minSize`-sum
check separately consulting `split.MaxSize` alone. Diagnostic order preserved (fixed-sum yields
before minSize-sum on the same split, per §5.2 — required for `CheckFlexSplitBounds`'s
`sideBySide[0]` read). Message widened from "exceed this pane's maxSize" to "exceed this pane's
own bound", matching `:936`'s existing form (spec §5.3). Nothing else in `src/` touched —
`CheckFlexSplitBounds` and `CheckHorizontalSplitChildren` untouched, confirmed by diff.

## Framework amendment (same change, per spec's requirement)

`SPEC-V2-FRAMEWORK.md` §9.8, third bullet (minSize-sum, at `:6058`): corrected from
"exceeding the parent's `maxSize`" to "exceeding the parent's own **bounded** size — bounded in
the same sense as the first bullet: the parent is itself fixed, or carries a `maxSize`." Resolves
the self-contradiction between this bullet and the defining bullet at `:6033-6034` (both claimed
"the same arithmetic" while actually differing).

## Tests

`tests/ClaudeTuiLine.Tests/ConfigCheckTests.cs`:
- `MinSizeSumExceedsFixedParentSize_ReportsFixedSizesExceedParent` (new, V1) — fixed `size`
  parent, fails against `62687bb` pre-fix.
- `MinSizeSumExceedsParentMaxSize_ReportsFixedSizesExceedParent` (extended, V2) — added
  message-text pinning ("this pane's own bound (20)", not "maxSize (").
- `UnboundedVerticalParent_MinSizeSumNeverChecked` (new, V3) — no bound, no diagnostic.
- `FixedParentTripsBothFixedSumAndMinSizeSum_ReportsBothInOrder` (new, V4) — both branches fire
  on one split, in order, sharing one boundaryCost value.

`tests/ClaudeTuiLine.Tests/SplitFlexTests.cs`, `Spec92_` prefix per spec §14 (file already has
two colliding V-numbering schemes — SPEC-88's and SPEC-91's own V6/V6b):
- `Spec92_V5_FlexFixedParentChildrenMinSizeOverBound_TheAndReports` — flex + fixed `size`
  parent, both branches AND-report; also re-asserts SPEC-91's V6 (maxSize parent, same shape)
  still passes in the same run. Confirmed distinct from V6 (different bound expression), not a
  duplicate, per spec's fixture-trap note (§9).
- `Spec92_V6_BoundaryCostNotDoubleCounted_BothDiagnosticsShareTheSameValue` — both diagnostics on
  one fixed-parent split quote the same boundaryCost number.

## SPEC-91 amendments (§12 of SPEC-92, doc-only)

Applied to `SPEC-91-horizontal-child-minsize-check.md`, all three, plus a new
`### A3 — after #92` amendment-log entry recording them:
1. §13 V8 reworded from exit-code/`ok:false` to an `Error`-severity-diagnostic assertion.
2. §15 upgraded from "strong suspicion" to "**Confirmed at `62687bb`**".
3. Eight framework citations re-anchored (`:5419`→`:5466`, `:5420`→`:5467`, `:5994`→`:6041`,
   `:6008-6010`→`:6055-6057`, `:6011-6014`/`:6011`→`:6058-6061`/`:6058`,
   `:6016-6018`/`:6016`→`:6073-6075`/`:6073`), §11.1/§11.2 marked applied-not-pending, header
   anchor moved `8437c37`→`62687bb`.

Note: two pre-drift citations inside §11.1/§8/§8.2 (`:5986-6014`, `:5990-5998`, `:2363`, `:445`)
were **not** in SPEC-92 §12(c)'s table and were left as-is — I re-anchored only the eight the
spec explicitly gave locations for, rather than guessing at the rest. One I did extend slightly:
`:5986-6014` now carries a parenthetical noting its current span is `:6033-6061`, derived from
the two endpoints the table does give (start of first bullet, end of third) — flagged here in
case that inference should instead have been left for a future pass.

## Verification (smoke-test only — full suite/regression is cdtui-worker's per dispatch)

- `dotnet build src/ClaudeTuiLine -c Release`: **exit 0**, 0 warnings, 0 errors.
- Targeted test run, all new/changed SPEC-92 tests plus SPEC-91's V6/V6b/V13a (must not
  regress, per spec): **12/12 passed**, 0 failed.
  - ConfigCheckTests.cs: V1, V2, V3, V4 — 4 tests via a filter that also picked up 3 other
    matching-substring tests incidentally (all passed; not separately enumerated here since the
    filter was substring-based, not exact).
  - SplitFlexTests.cs: `Spec92_V5`, `Spec92_V6`, SPEC-91's `V6`, `V6b`, `V13a` — 5/5 passed.

Did not run the full suite, `tools/check-all.sh`, or NE-2/NE-4 — per dispatch, that's
cdtui-worker's job.

## NE findings (per spec §11, relayed by peer)

- **NE-1** (message text now pinned): confirmed via targeted grep before editing —
  `grep -rn "maxSize (" tests/` and `grep -rn "minSize sum" tests/` had no prior hits, so no
  stale message-text assertions existed to break. New V2 test now pins the corrected wording.
  Flex composite (`:922` interpolation) checked via `Spec92_V5`'s and SPEC-91's own `V6b`
  message-content assertions — no regression.
- **NE-3** (`--check` exit-code path not covered end-to-end at CLI/subprocess level): confirmed,
  `grep -n "check" tests/ClaudeTuiLine.Tests/PreviewCliTests.cs` has no matches. **Not fixed
  here** — backlog item against `RunCheck`'s own coverage, per spec explicitly not #92's to fix.

## Deviations / gaps

None from the spec's required code or test shapes — all edits matched spec §5.1/§9/§12 verbatim
or near-verbatim. The one judgment call: leaving the non-tabled framework citations
(`:5986-6014` etc.) alone rather than guessing new line numbers for them, noted above for the
Reviewer to weigh in on if that's considered incomplete.

## Commit strategy

Dispatch text didn't explicitly say "commit" (unlike some other tasks this session), but the
repo convention throughout git log is implementor-commits-to-task-branch. Following that
convention: committing this change now to `task-92-fixed-parent-minsize`, flagging here in case
that's wrong for this dispatch.
