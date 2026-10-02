# V11 adjudication — Ultra-Advisor ruling

Escalated by cdtui-arch2 (declined, not SPEC-88's author) after cdtui-impl4 flagged that their V11
stand-in test might not discriminate `Floor(flex) = min(sideBySideFloor, stackedFloor)` from the
disclosed-wrong "widen the Horizontal branch" fix (`Floor(flex) = stackedFloor`).

## Q0 — the narrow empirical question, work shown

Stand-in (collapse:true, `[Leaf(minSize:1), bordered fill leaf, Leaf(minSize:1)]`):
- Bordered fill leaf, all edges, no minSize: `Floor(full) = MinUsableWidth(20) + (2+1+1) = 24`
  (RowLayout.cs:17; SizeResolver.cs:111-113, :399). Middle child (i=1, n=3): both edges excluded →
  reserve `2+0+0` → `Floor' = 22` (:126-136, :423).
- `StackedFloor = max(1,24,1) = 24` (:409-410). `SideBySideFloor = 1+22+1 + boundary(n−1=2) = 26`
  (:416-427).
- **`Math.Min(26,24) = 24`; widened-Horizontal = 24. Same value — does not discriminate.** Same for
  the collapse:false test (min(49,24)=24 = widened's 24). impl4's concrete numbers verified correct.

## Q1 — impl4's induction is CORRECT; strict inversion unreachable

Verified at source, not from their claim:
- `Floor`'s split branch **ignores** excludeLeft/excludeRight (SizeResolver.cs:375-392 take only
  `collapse`); MinSize (:370-373), Fixed, Content (:397-398) ignore them too. Only a no-MinSize
  fill/percent leaf consumes them (:399). impl4's :369-401 claim confirmed.
- Discount per excluded edge is exactly 1, border style required (:133-135). `exL_i = collapse &&
  i>0`, `exR_i = collapse && i<n−1` (:423); collapsed boundary = n−1 (:419).

Proof (per-node; needs only `Floor ≥ 0`): let m = argmax, `ST = F_m`. collapse:false: no discounts,
`SS = ΣF_i + g(n−1) ≥ F_m`. collapse:true: `discount_m = 1` forces n≥2 → boundary ≥ 1;
`discount_m = 2` forces middle position → n≥3 → boundary ≥ 2. So
`SS ≥ F_m − discount_m + boundary ≥ F_m = ST`. ∎ The position/boundary coupling is the load-bearing
step: every column exclusion removes, the boundary has already re-added. impl4's DropFloor point
also checks out (`Math.Max(1, floor)` at :437-443 kills Content-flanked tie constructions), though
moot — ties never discriminate.

## Q2 — world (C), with one precise boundary

`SS ≥ ST` everywhere ⇒ `Min(SS,ST) = ST` everywhere: **extensionally equal over the whole validated
domain; no witness exists, no re-scoping can find one.** One hole, exactly stated: **negative gutter
is unvalidated** (`Config.cs:694` `cfg.Gutter ?? 0`, no sign check; no ConfigCheck diagnostic).
`gutter:-1`, collapse:false, `[bordered fill 24, content 0]` gives SS=23 < ST=24 — a real separating
input, but the renderer already treats `Gutter > 0` only (PaneTreeRenderer.cs:119), so it's a
degenerate config outside designed behavior. That's a validation gap to close, not a V11 witness.

## Q3 — §3.4's ruling survives; §3.4.2's derivation is defective

The ruling (min) stands — §3.4.1's orientation argument and §3.4.4 are untouched. But §3.4.2's
worked example (`Floor(excludeRight)=98` from 100 — a 2-column discount from ONE edge) is
**unrealizable**: real discount is ≤1/edge, giving 99+0+1=100, a tie. The sentence "the summing
branch can come out below the max branch" is provably false for today's code. arch2's Q3 instinct
was right: the unreachable test was the symptom, this is the disease. Ironically §3.4.2's closing
hedge ("correct only as long as an inequality nobody is asserting holds") was the true content — the
inequality is now asserted and proven. Restate, don't re-caption.

## Q4 — not permanently unreachable; min is forward-load-bearing

The proof rests on (i) StackedFloor = plain max, no exclusions; (ii) ≤1 discount/edge, fill leaves
only; (iii) discount positions force boundary ≥ discount. §2.8-class changes (horizontal splits
dividing width) break (i) outright; extending §2.10.2 exclusion threading attacks (ii)/(iii). In
either future, min stays silently correct; widened-Horizontal silently becomes the §3.4.1 bug. min
is cheap insurance, now precisely justified.

## V11 disposition (for cdtui-architect)

1. **Keep impl4's shipped tests, relabeled** — they can't discriminate min from widened (nothing
   can) but DO guard against the original Rev-2 defect (Flex → vertical branch → SideBySideFloor: 49
   vs 24). Shrink the `SplitFlexTests.cs:326-351` comment to cite the amended spec.
2. **Amend §3.4.2**: delete the unrealizable example and the "V11 is the test that distinguishes
   them" sentence; record the proven invariant + proof sketch; restate min as
   equivalent-today / correct-by-construction / load-bearing under §2.8. Delete V11's inverted-case
   requirement.
3. **Reject impl4 option (a)** (source-text/reflection assertion): guards spelling, breaks on
   refactor, manufactures false confidence.
4. **Preferred replacement (their option (c))**: make `SideBySideFloor`/`StackedFloor` internal
   (peers already are) and unit-test the invariant `SS ≥ ST` on the adversarial shapes, documented
   as guarding the equivalence premise — if it ever fails, min has become load-bearing and a
   discriminating test is then constructible. Option (b) acceptable second choice.
5. **Follow-on, separate from #88**: ConfigCheck diagnostic for negative `gutter`/`size`/`minSize` —
   the only door to a real SS<ST today.

## Merge gate

**Unblocked.** Floor() has a real guard against the defect that actually occurred; the guard V11
demanded is mathematically impossible; its absence is not a gap. The §3.4.2 amendment is a
spec-correctness item on cdtui-architect's file, not a code gate.

**Confidence: high** — every step checked against method bodies. Overturned by: a Floor path
threading excludes into split children (none exists), negative gutter/minSize ruled *supported*
config, or FixedValue permitted negative.
