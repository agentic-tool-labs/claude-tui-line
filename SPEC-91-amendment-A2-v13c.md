# SPEC-91 Amendment A2 — the V13(c) ruling, and a correction to §9.2

Companion to `SPEC-91-horizontal-child-minsize-check.md`. Raised by cdtui-impl4, who located
V13(c) as instructed, found it green rather than red, and stopped rather than deciding.

Verified independently against the worktree
`/Users/jimcline/git/repos/claude-tui-line-task-91-horizontal-minsize`, branch
`task-91-horizontal-minsize`, `tests/ClaudeTuiLine.Tests/SplitFlexTests.cs:498-531`.

---

## 1. My §9.2 was wrong, and the spec contained its own correction

§9.2 stated flatly that V13(c) "will turn red" and called that "the expected outcome." That is
false. V13(c)'s fixture is:

```csharp
        UserConfig Config(string split) => new()
        {
            Surface = new SurfaceConfig
            {
                Pane = new PaneConfig
                {
                    Split = split,
                    MaxSize = 40,
                    Children = new List<PaneConfig>
                    {
                        new() { Size = "50" },
                        new() { Size = "50" },
                    },
                },
            },
        };
```

**No `MinSize` is declared on either child.** #91's new check is gated on `child.MinSize is int`,
so it cannot fire here. The two diagnostics V13(c) asserts (`:527-530`) both come from the
pre-existing `FixedSize` branch, which #91 leaves byte-identical. The test is correctly green.

**How the error happened, stated plainly.** I reasoned: V13(c) pins declared-horizontal output →
#91 changes declared-horizontal output → V13(c) fails. The middle step is too coarse. #91 changes
declared-horizontal output **only for configs carrying a child `minSize` over bound**.

That qualifier was already in this spec, at §13 V9: "only for configs carrying a child `minSize`
over bound." **So §9.2 and §13 V9 contradicted each other, and the correct statement was the one I
wrote myself.** This is a self-diff failure — the defect class this project has already been bitten
by (a document's §4 forbidding what its own §6 required) — and it is mine, not the implementor's.

I compounded it by asserting an outcome for a fixture I had not read, in the same breath as
instructing the implementor to read it first. The instruction was right; I should have followed it.

**Impl4 read the spec correctly, checked the artifact, and stopped at the discrepancy instead of
either "fixing" the test or assuming the spec knew better. That is exactly right.**

## 2. The green is evidence *for* the implementation, not a gap in it

Worth stating before the ruling, because the report reads as a defect and the underlying signal is
favourable.

#91's check is supposed to be narrowly scoped: it fires on a child `minSize` over a declared bound,
and on nothing else. V13(c) is a `FixedSize`-only parity fixture. **A correctly-scoped change leaves
it untouched.** Had V13(c) gone red, that would have meant #91 was perturbing configs with no
`minSize` at all — which would be a real defect.

So V13(c) staying green is a passing scoping guard. It is doing its #88 job — "the flex branch and
its neighbours do not disturb the declared directions" — and now incidentally evidences #91's
containment too.

## 3. Ruling: leave V13(c)'s fixture alone

**Do not add a `minSize`-bearing case to V13(c).** Four reasons:

1. **Purpose.** V13(c) is #88's regression guard: adding the `Flex` branch must not perturb the
   declared vertical/horizontal paths. That purpose is intact and the `FixedSize`-only fixture is
   the right instrument for it.
2. **The coverage already exists and is not thin.** #91's check is exercised by V1, V2, V3, V5 and
   V7 in `ConfigCheckTests.cs`, and V6/V6b in `SplitFlexTests.cs:536,586`. A `minSize` case bolted
   onto V13(c) would duplicate V1/V2 and add no assurance.
3. **Conflation.** A test guarding two specs' invariants fails for two unrelated reasons and tells
   you less on failure. V13(c) failing should mean exactly one thing: something disturbed the
   declared directions.
4. **The anchor has moved.** V13(c) compares against "current `main`". Post-#91, `main` *includes*
   #91 — so a #91-sensitive case in a test named for the current-main baseline is self-referential
   and will confuse the next reader more than it protects anything.

## 4. Ruling: the comment at `:517-520` must be updated, and that is #91's job

Impl4 flagged this and is right. The current comment reads:

```csharp
        // §7/V13(c): CheckSplitBounds and CheckHorizontalSplitChildren are called unchanged for
        // declared vertical/horizontal — this pins their exact pre-#88 output, including
        // CheckHorizontalSplitChildren's documented minSize gap (it only checks FixedSize), which
        // must NOT be "helpfully" fixed as part of this change.
```

Two sentences are now false in a way that matters. It describes the `minSize` gap as live and
**instructs future editors not to close it** — a gap #91 has just deliberately closed. Left alone,
it is a standing instruction to undo this spec, sitting in the test file, with no indication its
reason expired. That is the same hazard as §9.1's stale `SPEC-88` §7 prohibition, in a place people
read more often than the spec.

**#91 owns this edit.** The comment became false *because of* #91, and it lives in a test file #91
already modifies (V6/V6b were added at `:533-610`). This is not `SPEC-88`'s document; the
"don't touch another spec's file" constraint in §12 governs `SPEC-88-responsive-split-fallback.md`,
not test-file comments invalidated by this change.

**Required replacement** (wording is the implementor's, these facts are not):

```csharp
        // §7/V13(c): CheckSplitBounds and CheckHorizontalSplitChildren are called unchanged for
        // declared vertical/horizontal — this pins their output at 8437c37, so the flex branch
        // cannot perturb the declared directions.
        //
        // The fixture is deliberately FixedSize-only. SPEC-91 added a per-child minSize check to
        // CheckHorizontalSplitChildren, and this test stays green precisely because no child here
        // declares one — that is correct scoping, not an oversight. Do not add a minSize-bearing
        // case: it would test SPEC-91's check rather than this test's subject, which is #88's
        // non-interference. SPEC-91's own coverage is V1/V2/V3/V5/V7 in ConfigCheckTests.cs and
        // V6/V6b below.
```

This converts a stale prohibition into an accurate statement of scope, and pre-empts precisely the
change impl4 was contemplating — so the next reader does not re-raise it.

## 5. Optional, not required: the test's name

`V13c_SameConfigsDeclaredVerticalAndHorizontal_ByteIdenticalToCurrentMain` names a moving reference
point, and "current main" now means something different than when it was written. Renaming the tail
to `_ByteIdenticalTo8437c37` or `_DeclaredDirectionsUnperturbedByFlex` would fix that.

**Not required for #91**, and I would rather it were not bundled in: it is a `SPEC-88` test whose
name was already vague before #91 touched anything, and renaming it widens this diff for no
correctness gain. Worth a note wherever #92 is tracked.

## 6. Incidental — a V-number collision now exists in `SplitFlexTests.cs`

Not a blocker, flagged so it is a decision rather than an accident.

`SplitFlexTests.cs` now carries two independent V-numbering schemes in one class:

- `SPEC-88`'s: `V6a_FlexSplit_NoUnknownEnumValueDiagnostic` (`:139`),
  `V6b_GutterAndDistributeOnFlexSplit_NoKeyNotApplicableDiagnostic` (`:159`), and `V11`, `V13a`,
  `V13b`, `V13c`.
- `SPEC-91`'s, added by this change: `V6_FlexChildrenMinSizeOverBound_TheAndNowReports` (`:536`)
  and `V6b_FlexCompositeMessage_QuotesMinSizeWording` (`:586`).

So the class has **two different `V6b_` tests meaning two different things**. The full method names
differ, so this compiles and CI output is unambiguous; impl4's section header at `:533` ("`---- 
SPEC-91: horizontal/flex per-child minSize check ----`") already mitigates it for a reader going
top to bottom.

**Recommendation, not a requirement:** prefix #91's two with the spec — `Spec91_V6_…`,
`Spec91_V6b_…` — so a V-number is never ambiguous about which document defines it. If the
Orchestrator would rather not churn the diff, the section comment is adequate and this can be
declined; I would not hold a merge for it.

## 7. What this amendment changes in the parent spec

Apply to `SPEC-91-horizontal-child-minsize-check.md`:

- **§9.2** — replace the "V13(c) will turn red / expected outcome / re-baseline it" ruling with §1
  and §3 above. The trap warning it contained ("do not make it green by weakening the check") was
  sound advice against a failure that does not occur; the *locate it first* instruction was right
  and worked exactly as intended.
- **§13 V9** — no longer "re-baseline V13(c)". It becomes: V13(c) passes **unmodified** except for
  the §4 comment update, and its continued green is an assertion of #91's scoping.
- **§14 NE-1** — the expected outcome is now **a fully green suite**, V13(c) included. Any failure
  at all is unexpected and should be reported, not fixed.

Everything else in the parent spec stands, including every ruling in §1. This amendment corrects a
prediction about one test, not the design.
