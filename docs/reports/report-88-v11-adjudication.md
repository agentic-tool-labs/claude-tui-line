# V11 adjudication — declined, escalated, with one finding that changes the severity

Author: cdtui-arch (the #91 architect). **Not the author of `SPEC-88-responsive-split-fallback.md`.**

## 1. Why I am not the adjudicator

I was routed this because I "wrote SPEC-88's `Floor()`/AND-combinator math." I did not. SPEC-88 is
cdtui-architect's. My entire contact with it is from today, reading it as evidence while ruling on
#91: lines `505-545` (§4.5.3), `585-600`, `695-745` (V11-V13 and §7), and `900-920` (§12). I have
**never read §3.4, §3.4.2, or the `Floor()` ruling**, which is precisely the material this question
turns on.

That cdtui-architect is mid-#87 is a scheduling fact, not an ownership transfer. Adjudicating a
spec-adequacy question about another architect's central proof, without its context, is how a
plausible-but-wrong ruling gets laundered into a merge.

**Recommend escalation to the Ultra-Advisor.** This fits its remit exactly: an implementor stopped
rather than guessing, the claim is a mathematical unreachability argument, and the failure mode is
silent and hard to reverse — a subtly-wrong `Floor()` shipping inside a layout engine.

## 2. The finding — "tests pass, so it doesn't block merge" is unsafe here

This is the part I can rule on without owning SPEC-88, because it follows from V11's own text.

SPEC-88 `:695-698` states V11's purpose:

> **V11 — `Floor()` returns the `min`, including the inverted case.** A direct unit test on
> `Floor()` for a `Flex` pane, including a `collapse: true` configuration of the §3.4.2 shape
> where `sideBySideFloor < stackedFloor`. **A test using only ordinary panes cannot distinguish
> `min` from the widened-`Horizontal` fix** and would pass against the subtly-wrong version.

So V11 is not a test that happens to use an exotic configuration. **The exotic configuration is the
entire test.** V11 exists because the spec's author had already determined that ordinary
configurations pass against the wrong implementation.

That makes "the tests as-written pass, so this doesn't block cdtui-worker's merge verification"
an unsafe inference. Passing tests are not evidence of a correct `Floor()` here — V11's premise is
that the wrong implementation *also* passes everything except the inverted case. If the shipped
stand-in does not reach inversion or the tie, it sits on the ordinary-ordering side, which is by
V11's own sentence the region that **cannot** distinguish the two candidates.

If that reading is right, then #88 currently has **no test at all** discriminating the correct
`Floor()` from the plausible wrong one, and the guard V11 was written to provide is absent. That is
a merge-blocking gap, not a documentation-accuracy item to fold into a later amendment.

**To confirm or kill this, ask cdtui-impl4 one question:** for the shipped stand-in at
`SplitFlexTests.cs:326-345`, does `Math.Min(sideBySideFloor, stackedFloor)` and the
widened-`Horizontal` candidate return *different* values? If they return the same value, the test
does not discriminate, regardless of how close to the tie it gets. "Approaches but doesn't hit the
exact tie" suggests it does not — but that is impl4's arithmetic to answer, not mine to assume.

## 3. Three possible worlds, and they need different responses

The peer's question offers two options (accept the stand-in, or re-scope V11). There is a third,
and it is the one impl4's argument actually points at.

**(A) The stand-in discriminates after all.** Then V11's intent is met by other means, and the only
work is a wording amendment so the spec describes what is actually provable rather than §3.4.2's
simplified numbers. Lowest-cost outcome. Requires the §2 check to come back "different values."

**(B) Inversion is unreachable but the two implementations still differ somewhere reachable.** Then
V11 is mis-specified, not unnecessary: it named *one* witness (strict inversion) for a property that
has *other* witnesses. The fix is to re-scope V11 to whatever reachable input does discriminate, and
the shipped stand-in is insufficient until that input is found. Someone has to find it.

**(C) No reachable input distinguishes them — they are extensionally equivalent on every real `Pane`
tree.** Then V11 is not merely unreachable, it is *incoherent*: it demands a test that cannot exist,
and no re-scoping produces one. The correct response is neither option the peer offered. It is to
record in the spec that the two formulations are provably equivalent over reachable inputs, state
why `Math.Min` is nonetheless the right expression (clarity, and robustness if §2.8 later widens the
reachable set), and **delete V11 rather than replace it with a weaker test that implies a guard it
does not provide.** A test that looks like it guards something and doesn't is worse than no test,
because it stops anyone looking again.

Impl4's second paragraph — that even the theoretical tie is destabilised by `DropFloor`'s 1-cell
viability floor (`SPEC-2.3-drop-predicate.md` §3(a)) — is an argument for (C) over (B). That is a
genuinely interesting result and, if it holds, it is a finding about §3.4's design and not just
about a test.

## 4. What the Ultra-Advisor should be asked

1. Is impl4's induction correct — is strict inversion `sideBySideFloor < stackedFloor` genuinely
   unreachable over real `Pane` trees, using the actual `OwnBorderReserve` / `BoundaryCost` /
   `SideBySideFloor` / `StackedFloor` arithmetic rather than §3.4.2's worked numbers?
2. If unreachable: are `Math.Min(...)` and the widened-`Horizontal` formulation extensionally
   equivalent over all reachable inputs — world (C) — or is there some other reachable input that
   separates them — world (B)?
3. If (C): does §3.4's ruling survive intact with its justification restated as "equivalent here,
   `min` chosen for clarity and forward-safety," or does the unreachability indicate something wrong
   in §3.4.2's derivation, which produced a worked example the real arithmetic cannot realise?
4. Does the answer change once §2.8 lands and horizontal splits genuinely divide width? An input set
   that is unreachable today may not stay unreachable, which bears on whether `min` is merely
   equivalent-but-tidier or actually load-bearing later.

Question 3 is the one I would not skip. A spec section whose worked example cannot occur in the
system it describes has a defect somewhere, and "the test was unreachable" may be the symptom rather
than the disease.

## 5. What I am *not* claiming

- I have not verified impl4's induction. I cannot: I have not read §3.4/§3.4.2, and verifying it
  means reasoning over four arithmetic functions I have not looked at. Confirming it is
  NEEDS-EVIDENCE-class work, and routing it through me would spend design-tier tokens re-deriving
  another architect's proof.
- I have not read `SplitFlexTests.cs:326-345` on `task-88-flex`, so my §2 reading of the stand-in is
  inference from the peer's description, flagged as such, with the specific question that settles it.
- I am not ruling on whether #88 may merge. That is the Orchestrator's call informed by §2 — I am
  saying only that "the tests pass" must not be the basis for it.

## 6. Ownership, restated

Any amendment to `SPEC-88-responsive-split-fallback.md` — re-scoping V11, deleting it, or restating
§3.4's justification — is **cdtui-architect's to make**, exactly as agreed when #91 §9.5 was routed.
I have not touched that file and will not. This report is input for whoever does.

This also does **not** fold into #91's batched amendment. #91's spec is a different file with a
different owner, and the only thing the two share is that both wait on #88. Merging an unrelated
spec's correction into #91's rewrite would misattribute the change and bury it.
