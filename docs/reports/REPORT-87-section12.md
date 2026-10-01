# #87 §12 report — compound map threading + verification tests

Worktree: `claude-tui-line-task-87-compound`, branch `task-87-compound`, rebased
onto main (`6b67c1a`).
Commits: `8211d54` (§12 threading), `0d04f55` (SPEC-V2-FRAMEWORK.md /
SPEC-85-ADDENDUM doc edits, §8's files-to-touch table), `f5159a2` (fix
SplitFlexTests.cs stale call sites surfaced by the rebase).

## §12.7.1 code-review confirmation (explicit ask)

Compound map built exactly once per render pass, same instance to both entry
points, in both `Program.RunAsync` and `Program.RunPreview` independently:

- `RunAsync`: `var compounds = LeafItems.BuildCompoundMap(pane, values, ctx, tokens);`
  built once, passed into `ComputeRows(..., compounds, ...)`.
- `RunPreview`: same pattern, its own single build, passed into both the
  json-branch and bare-branch `ComputeRows` calls.
- Inside `ComputeRows`, that one instance flows into both
  `PaneCollapse.Collapse(pane, values, ctx, compounds, unavailableIds)` and
  `HeightLadder.Resolve(..., compounds, ...)` — no second `BuildCompoundMap`
  call anywhere downstream. `HeightLadder`'s private `Measure` helper (used by
  its public `Resolve` and all three degrade-ladder rungs) forwards the same
  instance into both `SizeResolver.Resolve` and `PaneTreeRenderer.Render`.

`PaneCollapse.Collapse` is not a third independent entry point: it's called
from inside `ComputeRows`, downstream of the one build, so it reuses that
instance — consistent with, not a violation of, §12.7.1's "build once."

## Diff (9 production files)

- `LeafItems.cs`: `BuildCompoundMap`/`CollectCompounds` (whole-tree walk,
  omits suppressed compounds); `Resolve` takes `compounds`, floor-colour
  fallback wired in.
- `SegmentBuilder.cs`: `ApplyColorFloor` — colours only spans with no colour
  of their own (§12.9).
- `LeafContent.cs`: `Decide`/`TryBuildLink` take `compounds`, cross-pane link
  fallback, Plain-only substitution.
- `PaneAssembler.cs`, `PaneTreeRenderer.cs`: `compounds` threaded through
  render/recursion.
- `SizeResolver.cs`: `compounds` threaded through all 3 `Resolve` overloads
  and the full min-rows solver chain down to `CandidateSegments`.
  `ResolveVerticalEven`/`AllocateEvenOnePass` deliberately untouched — they
  never measure content.
- `HeightLadder.cs`: `compounds` threaded through `Resolve`, its 3 degrade
  rungs, and the shared `Measure` helper.
- `PaneCollapse.cs`: `Collapse`/`IsStructurallyEmpty` take `compounds`.
- `Program.cs`: builds the map once in `RunAsync` and once in `RunPreview`,
  per above.

## Tests (14 new, 5 files: `PaneAssemblerSpansTests.cs`, `HyperlinkTests.cs`,
`ConfigCheckTests.cs`, `HeightLadderTests.cs`, `EndToEndItemValuesTests.cs`)

- 14 `ItemSelector_InAnotherPane_ResolvesCompoundFromWholeTreeMap`
- 15 `LinkTemplate_NamingCompoundDeclaredInAnotherPane_SubstitutesPlainTextOnly`
- 16 `ColorRuleFromNamingCompoundItem_DiagnosticFiresAndResolvedColorFallsThroughToDefault`
- 17A `CommandIdSelector_WithNoCollision_ResolvesOrdinarilyWithoutCompoundsFallback`
- 17B `CollidingId_BetweenOrdinaryValueAndCompoundsMap_OrdinaryValueWins`
- 18 `SuppressedCompound_SelectedFromAnotherPane_ResolvesToNullValueAndDisplay`,
     `LinkTemplate_NamingSuppressedCompound_DropsTheLinkEntirely`,
     `ItemSelectorNamingCompoundElsewhere_ReportsNoDiagnostic`
- 19 three configs (text-only, built-in-part, compound-alongside-ordinary),
     byte-identical rendered markup real-vs-empty compounds map
- 20 `CompoundColorFloor_FillsOnlyPartsWithNoColourOfTheirOwn` (all 3 parts
     asserted in one test)
- 21 `SizeResolver_And_PaneAssembler_AgreeOnCompoundContent_ForCrossPaneSelector`

**17B finding**: id collision between an ordinary item and a compound *is*
structurally possible — no duplicate-id diagnostic exists anywhere in
`src/ClaudeTuiLine/`. Wrote a real collision test (ordinary value wins, per
spec) rather than a structural-impossibility note. This is an observation,
not a defect against #87's scope — flagging in case the Architect wants a
future diagnostic for author-facing id collisions.

## §8 doc edits (SPEC-V2-FRAMEWORK.md, SPEC-85-ADDENDUM-spans-threading.md)

- SPEC-V2-FRAMEWORK.md §9.6.1: registry rows added for `part-compound-source`
  (error) and `color-from-compound-source` (warning) — both were introduced
  in code at `56c23f7` but never registered.
- SPEC-V2-FRAMEWORK.md §3.3:~2680: note that a part's `item` may not name a
  compound (`part-compound-source`).
- SPEC-V2-FRAMEWORK.md §3.3:~2732-2733: note extending part-colour authority
  over a selecting item's `color`, per SPEC-87's colour-floor ruling.
- SPEC-85-ADDENDUM-spans-threading.md §12.8.5: marked resolved for holes #1
  (`ItemSelector`) and #3 (`LinkPlaceholder`); hole #4 (`ColorFrom`) resolves
  to warning-and-fallback, not real resolution; hole #2 (`ArgvPlaceholder`,
  `placeholder-compound-source`) remains explicitly open/unimplemented.
- SPEC-85-ADDENDUM `:556` amendment per SPEC-87's incidental finding (§11):
  `ColorFrom`'s severity was not actually open-ended — the
  severity-follows-the-form precedent already existed.
- Ran `tools/check-citations.sh` before and after: pre-existing failures
  unchanged in kind/count (only line-number shifts from the insertions); my
  new cross-document references avoid bare `§N.N` citations that could
  collide with local headings (fixed one such collision — `§12.9` inside
  SPEC-V2-FRAMEWORK.md — before committing).

## Rebase

Rebased onto main (`6b67c1a`, includes #91 and #47 since this branch's
`56c23f7` parent). Rebase itself was conflict-free. The rebase pulled in
`tests/ClaudeTuiLine.Tests/SplitFlexTests.cs` (new since #88/#91), whose
`SizeResolver.Resolve`/`PaneTreeRenderer.Render` call sites predated #87 and
needed the same mechanical `compounds` threading as the original 21-file
fixup — 15 sites, fixed in `f5159a2`.

## Verification (smoke-test scope + full suite, both clean)

- `dotnet build src/ClaudeTuiLine/ClaudeTuiLine.csproj`: exit 0.
- New-tests-only filtered run (subagent, pre-rebase): 14/14 passed.
- Full suite, post-rebase (`dotnet test tests/ClaudeTuiLine.Tests/...`):
  exit 0, **1498/1498 passed**, 0 failed, 0 skipped. (Ran the full suite
  myself as a final check even though full-suite verification is
  cdtui-worker's job, not mine — reporting it here since it's already clean
  and free.)

## §12.9 colour precedence — final, not provisional

Confirmed and implemented as final: compound part colours always win; a
selecting item's own `color` is a floor that only fills parts with no colour
of their own. Not described as provisional anywhere in code or tests.

## Follow-up noted, not implemented (§12.10.4)

Bundling `values`/`ctx`/`tokens`/`pane`/`compounds` into one resolution-context
record was considered and deliberately deferred. Rationale: the parameter
count through `SizeResolver`/`HeightLadder` is now high enough that a context
record would help, but doing it now would touch every frame in both call
chains for reasons unrelated to #87 and would make the additivity claim
(§12.4 — byte-identical output for non-cross-referencing configs, test 19)
much harder to argue by inspection. Out of scope for #87.

## No production-code defects found

Neither I nor the dispatched test-writing subagent hit an actual production
defect while writing/running the 14 new tests — all reasoning matched real
behavior on first pass (per the subagent's report).

Nothing left uncommitted in the worktree.
