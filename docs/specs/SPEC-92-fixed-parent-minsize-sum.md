# SPEC-92 — `CheckSplitBounds`'s `minSize`-sum check skips a fixed-size parent

Issue #92. Ruling on why a vertical split with a **fixed** size (rather than a `maxSize`) never has
its children's `minSize` sum checked, what to change, and why the framework must be amended before —
not after — the code.

Surfaced during #91 (`SPEC-91-horizontal-child-minsize-check.md` §15), which documented it and was
forbidden from touching it (SPEC-91 §12).

**All citations anchored to commit `62687bb`** (`Merge #91: horizontal split child minSize structural
check`). Every line number below was re-read at that commit.

**Citations in this document are anchored by commit and quoted by content.** SPEC-91 specified two
framework amendments by line number while `SPEC-V2-FRAMEWORK.md` was under concurrent change; the
numbers had drifted by +47 to +57 before anyone applied them. They landed correctly only because the
implementor matched on content rather than following the numbers. **Do not target an edit in this
document by line number alone — match the quoted text.**

---

## 1. Ruling

**The framework is internally inconsistent, and the code faithfully implements the wrong half of
it.** Fix the framework first, then the code.

| Question | Ruling | §  |
|---|---|---|
| Is this a code bug or a framework defect? | **Framework defect first, code fix following** | §3 |
| Which framework text governs? | **`:6033-6034` and `:6047-6050`** — not `:6058` | §3 |
| Code change | Widen the guard **and** merge the two bound computations | §5 |
| Diagnostic message | Must change — it currently names a key the config may not declare | §5.3 |
| Severity / code | **`Error`**, reuse **`fixed-sizes-exceed-parent`** | §6 |
| Backward-compat risk | **Accept** — lighter than #91's, see §7 | §7 |
| Flex consequence | #88's AND under-reports today; this closes it | §4 |

---

## 2. The gap, confirmed from source

`CheckSplitBounds` at `src/ClaudeTuiLine/ConfigCheck.cs:925-949`, verbatim at `62687bb`:

```csharp
925    private static IEnumerable<Diagnostic> CheckSplitBounds(Pane split, string path, bool collapse)
926    {
927        var boundaryCost = SizeResolver.BoundaryCost(split, split.Children.Count, collapse);
928
929        var parentBound = SizeResolver.FixedSize(split) ?? split.MaxSize;
930        if (parentBound is int bound)
931        {
932            var fixedSum = split.Children.Sum(c => SizeResolver.FixedSize(c) ?? 0);
933            if (fixedSum + boundaryCost > bound)
934            {
935                yield return new Diagnostic(path, DiagnosticSeverity.Error, "fixed-sizes-exceed-parent",
936                    $"children's fixed sizes ({fixedSum}) plus boundary cost ({boundaryCost}) exceed this pane's own bound ({bound})");
937            }
938        }
939
940        if (split.MaxSize is int maxBound)          // <-- the gap
941        {
942            var minSum = split.Children.Sum(c => c.MinSize ?? 0);
943            if (minSum + boundaryCost > maxBound)
944            {
945                yield return new Diagnostic(path, DiagnosticSeverity.Error, "fixed-sizes-exceed-parent",
946                    $"children's minSize sum ({minSum}) plus boundary cost ({boundaryCost}) exceed this pane's maxSize ({maxBound})");
947            }
948        }
949    }
```

`:929` computes the bound as `FixedSize(split) ?? split.MaxSize`. `:940` ignores that and consults
`split.MaxSize` alone. **So `{"split": "vertical", "size": "40", "children": [{"minSize": 30},
{"minSize": 30}]}` is undiagnosed**: the parent is fixed at 40 columns, its children's floors sum to
60 plus boundary cost, and no terminal width can satisfy that — but `:940`'s guard is false, because
`MaxSize` is null.

This is **not** a suspicion. SPEC-91 §15 hedged it as "a strong suspicion, not a confirmed defect"
pending `SizeResolver.FixedSize`'s behaviour. That hedge is discharged — see §2.1.

### 2.1 `SizeResolver.FixedSize` is not a complication

`src/ClaudeTuiLine/SizeResolver.cs:175`:

```csharp
    internal static int? FixedSize(Pane pane) => ClassifySize(pane.Size) is { Kind: SizeKind.Fixed } spec ? spec.FixedValue : null;
```

with the doc comment at `:170-173` stating it is "the same classification `ClassifySize` already
runs for the allocator, not a second parse of the same string."

That is exactly the property #92 needs: `FixedSize` returns non-null precisely when the allocator
treats the pane as fixed, so `:929`'s bound is the real allocated extent and no `size` token form
can make it lie. **SPEC-91 §15's NEEDS-EVIDENCE caveat dissolves rather than needing an experiment.**

---

## 3. The fork, and why it resolves against `:6058`

The obvious reading is "the code is too narrow, widen it." That reading is wrong on its own, because
the framework bullet is narrow in the *same* way, and a code-only fix would leave the next
implementor re-deriving this gap from unchanged text.

But the framework does not actually rule narrowly. It contradicts itself.

**`SPEC-V2-FRAMEWORK.md:6033-6035` — the first §9.8 bullet, which defines the term:**

> - Children's **fixed** sizes, plus the boundary cost, exceeding the parent's own **bounded** size
>   — where bounded means the parent is itself fixed, or carries a `maxSize`.
>   Code: `fixed-sizes-exceed-parent`.

**`:6047-6050` — the general substitution rule for the whole section:**

> So the width the check compares against is the renderer's, computed by the renderer's code, with
> the parent's **declared** size or `maxSize` standing in for the terminal width. That substitution
> is what keeps this width-independent: same function, a number from the config rather than from
> `COLUMNS`.

**`:6058-6061` — the third bullet, the one the code implements:**

> - Children's `minSize` sum, plus **the same boundary cost**, exceeding the parent's `maxSize`.
>   Same code as the first: it is the same contradiction with the floor rather than the exact size —
>   and therefore **the same arithmetic**, taken from §2.10. Two bullets computing one boundary two
>   ways is how the double-count gets back in through the door the bullet above just closed.

**The ruling.** `:6033-6034` defines *bounded* as "the parent is itself fixed, **or** carries a
`maxSize`" — both disjuncts, explicitly, as a definition. `:6047-6048` restates the same disjunction
as the section's governing substitution. `:6058` then names only one disjunct **while asserting in
its own next sentence that it uses "the same arithmetic" as the first bullet.**

A bullet cannot both use the same arithmetic as the first bullet and use a narrower bound than the
first bullet. **`:6058`'s "`maxSize`" is a drafting error, and its own parity claim is the proof.**

This matters for how the amendment is framed, not just for whether it happens:

- It is **not** a policy change. The framework already says these configs are diagnosable at
  `:6033-6034` and `:6047-6050`. `--check`'s silence is a **failure to implement a stated rule**.
- So no one could have relied on the silence as designed leniency, which is what makes §7's
  backward-compatibility analysis lighter than SPEC-91 §8's had to be.
- And `:6058`'s closing warning — "two bullets computing one boundary two ways is how the
  double-count gets back in" — is an argument *for* §5.2's merge, from the framework's own mouth.
  The code currently computes the bound two ways in one method. That is the shape the bullet warns
  against, one level up.

---

## 4. Why this matters now: #88's AND combinator under-reports

`CheckFlexSplitBounds` at `:911-923`, verbatim at `62687bb`:

```csharp
911    private static IEnumerable<Diagnostic> CheckFlexSplitBounds(Pane split, string path, bool collapse)
912    {
913        var sideBySide = CheckSplitBounds(split, path, collapse).ToList();
914        var stacked = CheckHorizontalSplitChildren(split, path).ToList();
915
916        if (sideBySide.Count == 0 || stacked.Count == 0)
917        {
918            yield break;
919        }
920
921        yield return new Diagnostic(path, DiagnosticSeverity.Error, "fixed-sizes-exceed-parent",
922            $"this flex split's children exceed its parent's bound in both arrangements: side by side ({sideBySide[0].Message}) and stacked ({stacked[0].Message})");
923    }
```

Take `{"split": "flex", "size": "40", "children": [{"minSize": 50}, {"minSize": 50}]}` at `62687bb`:

| Half | Result today | Why |
|---|---|---|
| `stacked` (`:914`) | **fires** | #91's per-child check: each child's 50 > bound 40 |
| `sideBySide` (`:913`) | **empty** | `:940`'s guard is false (`MaxSize` null); `:932`'s `fixedSum` is 0 because no child declares `size`, and `0 + boundaryCost > 40` is false |

`sideBySide.Count == 0`, so `:916` short-circuits and **the AND yields nothing** — for a pane that is
impossible in both arrangements, which is precisely #88's stated criterion for reporting.

So this is a live under-report in code that landed two commits ago, not a latent tidy-up. It is the
strongest argument for spec'ing #92 now rather than deferring it.

**After §5's fix**, `sideBySide` fires (minSum 100 + boundary cost > 40), both halves are non-empty,
and the AND reports correctly.

**Note the `maxSize` variant already works** — `{"split": "flex", "maxSize": 40, ...}` with the same
children is SPEC-91's V6 and reports today. The two fixtures differ by exactly one key and have
opposite outcomes. §9 makes that explicit so nobody "normalizes" them later.

---

## 5. The change

**File:** `src/ClaudeTuiLine/ConfigCheck.cs`, method `CheckSplitBounds` (`:925-949`). **Nothing else
in `src/` is touched.** `CheckFlexSplitBounds` and `CheckHorizontalSplitChildren` are not edited,
though flex behaviour changes as a consequence (§4).

### 5.1 Required code

```csharp
    private static IEnumerable<Diagnostic> CheckSplitBounds(Pane split, string path, bool collapse)
    {
        var boundaryCost = SizeResolver.BoundaryCost(split, split.Children.Count, collapse);

        var parentBound = SizeResolver.FixedSize(split) ?? split.MaxSize;
        if (parentBound is not int bound)
        {
            yield break;
        }

        var fixedSum = split.Children.Sum(c => SizeResolver.FixedSize(c) ?? 0);
        if (fixedSum + boundaryCost > bound)
        {
            yield return new Diagnostic(path, DiagnosticSeverity.Error, "fixed-sizes-exceed-parent",
                $"children's fixed sizes ({fixedSum}) plus boundary cost ({boundaryCost}) exceed this pane's own bound ({bound})");
        }

        var minSum = split.Children.Sum(c => c.MinSize ?? 0);
        if (minSum + boundaryCost > bound)
        {
            yield return new Diagnostic(path, DiagnosticSeverity.Error, "fixed-sizes-exceed-parent",
                $"children's minSize sum ({minSum}) plus boundary cost ({boundaryCost}) exceed this pane's own bound ({bound})");
        }
    }
```

### 5.2 Why the two guards merge into one, and why that is provably safe

The minimal fix is to change `:940` to `if ((SizeResolver.FixedSize(split) ?? split.MaxSize) is int
maxBound)`. **Do not do that.** It leaves the bound computed twice in one method, which is the
condition that produced this bug and would let the two drift again.

The merge is behaviour-preserving, and the argument is short enough to check:

`parentBound` is `FixedSize(split) ?? split.MaxSize`, so `parentBound` is null **if and only if**
`FixedSize(split)` is null *and* `split.MaxSize` is null. In that case the old `:940` guard
(`split.MaxSize is int`) is *also* false. So the merged `yield break` skips exactly the cases the two
original guards both skipped — no input reaches one guard but not the other.

Diagnostic **order is preserved**: the fixed diagnostic still yields before the `minSize` one, and
both still yield within the same `bound`. A config tripping both still produces both, in the same
sequence. Nothing downstream re-sorts (`:921` reads `sideBySide[0]`, which is unchanged).

### 5.3 The message must change, and this is not cosmetic

`:946` currently ends `exceed this pane's maxSize ({maxBound})`.

Widening the guard without touching the message ships a diagnostic that **names a key the config
never declared**: a user writing `{"size": "40"}` would be told about a `maxSize` of 40 that appears
nowhere in their config, sending them to look for a key they never wrote. `:936` already has the
correct form for a bound that may come from either key — "this pane's own bound" — and the fix is to
mirror it exactly.

This is a **user-visible string change on an existing diagnostic**, and the `maxSize`-parent case
that works today will change its message. See §11 NE-1.

---

## 6. Severity and code — unchanged from #91's reasoning

**`Error`**, reusing **`fixed-sizes-exceed-parent`**. No new registry row.

This is not a fresh ruling; it is the existing one continuing to apply. The check already emits at
this severity and code (`:945-946`) — #92 widens *when* it fires, not *what* it reports. Framework
`:6058-6059` ("Same code as the first") and `:5466`'s registry row, already amended by #91 to read
"declared fixed sizes **or floors** cannot fit the parent at any width", both cover the widened case
without further change.

`DiagnosticSeverity` has two members; there is no channel on which to stage this more softly, and §7
explains why none is wanted.

---

## 7. Backward compatibility

**Accept the risk. No staged rollout.** The analysis is SPEC-91 §8's, with one extra leg that makes
it stronger.

**Blast radius.** A config newly errors iff: a split pane declares a **fixed** size (not `maxSize`,
which already works), **and** its children's `minSize` sum plus boundary cost exceeds it. Plus the
`flex` population in §4. A `fill`/`content` parent is untouched — the merged `yield break` preserves
framework `:6073-6075`'s silence exactly.

**Why acceptable:**

1. **These configs already cannot render.** A fixed parent of 40 whose children's floors demand 60
   is unsatisfiable at every width, because 40 came from the config rather than from `COLUMNS`. The
   harm framework `:6041` names — "a false `error` … sends the user to fix something that already
   works" — is not in play. `--check` becomes correct about a config that was already broken.
2. **The extra leg, which #91 did not have.** The framework *already states* these configs are
   diagnosable (`:6033-6034`, `:6047-6050`). The current silence is an unimplemented rule, not a
   documented leniency, so no one could have relied on it as designed behaviour. #91 had to argue
   that newly-erroring configs were already broken; #92 argues that *and* that they were already
   specified as errors.
3. **No false positives are possible.** Declared numbers, compared against a declared bound, with no
   width involved. The boundary cost comes from `SizeResolver.BoundaryCost` (`:927`) — the renderer's
   own function, per framework `:6037-6038` — and is **unchanged**; §5 reuses the value already
   computed at the top of the method and introduces no new arithmetic.

**Rejected: ship as `Warning`.** Same three reasons as SPEC-91 §8.3 — it misstates the severity axis
("unachievable everywhere"), there is no deprecation channel, and framework `:6055-6057` records
`min-exceeds-max` being *corrected from warning to error*, the opposite direction.

---

## 8. What must NOT change

- **`CheckFlexSplitBounds` (`:911-923`) — entirely.** Its AND short-circuit and composite message
  format stay. Its *behaviour* changes via §4; its *code* does not.
- **`CheckHorizontalSplitChildren` (`:951` onward)** — #91's method, untouched by #92.
- **`CheckStructuralSizes`'s routing**, including the `§2.8` scoping comment in the Vertical branch.
- **`SizeResolver.BoundaryCost`, `SizeResolver.FixedSize`, `ClassifySize`** — read, never modified.
- **The fixed-sum diagnostic's message text** (`:936`) — byte-identical. Only `:946` changes.
- **Diagnostic order within `CheckSplitBounds`** — fixed before `minSize` (§5.2).
- **`DiagnosticSeverity`** — no third member.
- **`RunCheck`** (`Program.cs`) — not touched, and per SPEC-91 §12 not re-tested here (§11 NE-3).
- **Framework `:6033-6035`, `:6047-6050`, `:6063-6071`, `:6073-6075`** — only `:6058` changes (§12).

---

## 9. The fixture trap — read before writing tests

**#91's and #92's flex fixtures differ by exactly one key and have opposite expected outcomes.**

| Fixture | Spec | Before #92 | After #92 |
|---|---|---|---|
| `{"split":"flex","maxSize":40, children minSize 50}` | SPEC-91 V6 | reports | reports (unchanged) |
| `{"split":"flex","size":"40", children minSize 50}` | SPEC-92 V5 | **silent** | reports |

SPEC-91 §15 already carried a caution not to "strengthen" its V6 by switching it to a fixed-size
parent, because that variant would fail — and the failure would be #92, not a #91 defect. **That
caution now inverts**: after #92 both report, so the two tests stop being distinguishable by outcome
and start looking like duplicates.

They are not duplicates. `maxSize` and fixed `size` reach the bound through different expressions
(`split.MaxSize` versus `SizeResolver.FixedSize(split)`), and V5 is the only test that would catch a
regression re-narrowing `:940`. **Every #92 test must state in a comment why its parent uses `size`
rather than `maxSize`**, or the next person tidying fixtures will collapse them and silently delete
the regression guard. This is the same failure mode as #88's V11 stand-in: a guard that looks present
and no longer discriminates.

---

## 10. Verification

V1-V4 in `tests/ClaudeTuiLine.Tests/ConfigCheckTests.cs`; V5-V6 in `SplitFlexTests.cs`. Per §9, each
must comment why the parent's size key is what it is.

- **V1 — fires on a fixed parent.** `{"split":"vertical","size":"40"}`, two children `{"minSize":30}`
  → one `fixed-sizes-exceed-parent`, `Error`, path = the split's own path, message naming the summed
  floors and the bound. **This is the test that fails against `62687bb`.**
- **V2 — the `maxSize` parent still fires, with the new wording.** Existing `maxSize` min-sum
  coverage passes, with `:946`'s message updated to "this pane's own bound". Any existing test
  asserting the old "maxSize" wording must be re-baselined (NE-1) — that is an expected message
  change, not a defect.
- **V3 — no bound, still silent.** A `fill`/`content` split with children `{"minSize":999}` → **no**
  diagnostic. Guards framework `:6073-6075` and the merged `yield break` (§5.2).
- **V4 — both diagnostics, in order.** A fixed parent tripping the fixed sum *and* the `minSize` sum
  → **two** diagnostics, fixed first. Guards §5.2's order-preservation claim.
- **V5 — the flex AND now reports (§4).** `{"split":"flex","size":"40"}`, two children
  `{"minSize":50}` → exactly one diagnostic at the split's own path, message naming **both**
  arrangements. Fails against `62687bb`. **SPEC-91's V6 must still pass in the same run.**
- **V6 — boundary cost is not double-counted.** V1's message reports the same `boundaryCost` value
  the fixed-sum branch would report for the same split. Guards framework `:6060-6061`.
- **V7 — full suite.** `dotnet test tests/ClaudeTuiLine.Tests` green, modulo NE-1's re-baselines.
- **V8 — `tools/check-all.sh`** shows no *new* failures versus a baseline captured at `62687bb`
  first. It is red on `main` for unrelated reasons, so exit 0 is not the bar.

---

## 11. NEEDS-EVIDENCE

I do not run anything. Each item says what to run and what each outcome decides.

- **NE-1 — which existing tests assert `:946`'s message text?**
  §5.3 changes a shipped diagnostic string, and `:922` interpolates that string into the **flex
  composite message**, so the blast radius includes flex tests that assert composite text, not only
  vertical ones. Before editing, run `grep -rn "maxSize (" tests/` and `grep -rn "minSize sum" tests/`.
  → **Matches found:** re-baseline them to the new wording as part of this change. Expected, not a
    defect.
  → **No matches:** note it — the message text is then unpinned, and V2 should pin it.

- **NE-2 — full suite before and after.**
  `dotnet test tests/ClaudeTuiLine.Tests`, capturing to a file and reporting only failures plus the
  exit code.
  → **Only NE-1's message assertions fail:** proceed.
  → **Anything else fails:** stop and report the test and its fixture. Do not adjust the check to
    make it green.

  **#88's V13(a) is expected to stay green, and this is settled by arithmetic, not by running it.**
  V13(a) is `{"split":"flex","maxSize":40}` with two children at `minSize: 30`. Its parent declares
  `maxSize`, so `FixedSize(split)` is null and the merged bound is `null ?? 40 = 40` — **identical to
  the `maxBound` the old `:940` guard produced.** `minSum` (60) and `boundaryCost` are likewise
  unchanged, so `CheckSplitBounds` returns exactly what it returned before, and V13(a) asserts no
  message text. #92 cannot perturb it. If it nonetheless turns red, that is a genuine conflict
  between #88's combinator semantics and this spec — **stop and escalate to the Ultra-Advisor rather
  than adjusting either fixture**, because it would mean one of the two specs has mis-modelled the
  AND.

- **NE-3 — is `Error → exit 1` covered end-to-end anywhere?**
  Carried over from SPEC-91's V8 ruling (§12). `PreviewCliTests.cs:27,68` assert `ok: false` through
  a real process spawn via `PreviewCliRunner`, but I did not confirm they exercise `--check`.
  Run `grep -n "check" tests/ClaudeTuiLine.Tests/PreviewCliTests.cs`.
  → **Covered:** nothing to do; SPEC-91's narrowed V8 is fully justified.
  → **Not covered:** log a backlog item against `RunCheck`'s own coverage. **Not #92's to fix**, and
    not a blocker.

---

## 12. Amendments to SPEC-91 — apply as part of this change

`SPEC-91-horizontal-child-minsize-check.md` needs three edits. **Match on the quoted text, not on
line numbers.** Add an `### A3 — after #92` entry to its amendment log recording all three.

**(a) §13 V8 — narrow it to what shipped.** cdtui-worker's #91 verification noted the shipped test
asserts only that an `Error`-severity diagnostic is present, while V8's text claims an end-to-end
exit-code assertion. **The test is right and the spec text is wrong.** An exit-code assertion inside
#91's diff would test `RunCheck`, which SPEC-91 §12 lists under "must NOT change"; re-testing unedited
generic composition is not the job of a change that merely feeds it a new input.

Replace V8's bullet text with:

> - **V8 — an `Error`-severity diagnostic is produced.** `--check` on V1's config yields at least one
>   diagnostic with `DiagnosticSeverity.Error`. This pins the input `RunCheck`'s gate consumes
>   (`diagnostics.Any(d => d.Severity == Error)` → exit 1, `ok: false`). `RunCheck` itself is
>   untouched by #91 (§12) and is deliberately not re-tested here.

**(b) §15 — upgrade from suspicion to confirmed.** Replace:

> Not verified against `SizeResolver.FixedSize`'s behaviour for every `size` token form — a strong
> suspicion, not a confirmed defect.

with:

> **Confirmed at `62687bb`** and specified in `SPEC-92-fixed-parent-minsize-sum.md`.
> `SizeResolver.FixedSize` (`SizeResolver.cs:175`) delegates to the allocator's own `ClassifySize`,
> so no `size` token form complicates the bound — see SPEC-92 §2.1.

**(c) Re-anchor the framework citations.** SPEC-91 cites `SPEC-V2-FRAMEWORK.md` at pre-drift line
numbers. Current locations at `62687bb`:

| SPEC-91 cites | Actually at `62687bb` | Content |
|---|---|---|
| `:5419` | **`:5466`** | §9.6.1 registry row for `fixed-sizes-exceed-parent` |
| `:5420` | **`:5467`** | registry row for `min-exceeds-max` |
| `:5994` | **`:6041`** | "a false `error` is the worst outcome available here" |
| `:6008-6010` | **`:6055-6057`** | `min-exceeds-max` bullet, warning→error correction |
| `:6011-6014` | **`:6058-6061`** | the `minSize`-sum bullet |
| `:6016-6018` | **`:6073-6075`** | `fill`/`content` silence |
| §11.1's target | **`:6063-6071`** | the arithmetic-form paragraph, applied by #91 |
| §11.2's target | **`:5466`** | the "or floors" gloss, applied by #91 |

Also update SPEC-91's header anchor from `8437c37` to `62687bb`, and note that §11.1/§11.2 are
**applied, not pending**.

---

## 13. Framework amendment

One edit to `SPEC-V2-FRAMEWORK.md`, in the **same change** as the code (framework `:445` requires
documented closed sets and their implementations to move together).

**Locate by content**, not line number: the third §9.8 bullet beginning "Children's `minSize` sum".
Replace its first sentence:

> - Children's `minSize` sum, plus **the same boundary cost**, exceeding the parent's `maxSize`.

with:

> - Children's `minSize` sum, plus **the same boundary cost**, exceeding the parent's own **bounded**
>   size — bounded in the same sense as the first bullet: the parent is itself fixed, or carries a
>   `maxSize`.

**Leave the rest of the bullet unchanged.** Its "Same code as the first … therefore the same
arithmetic" sentence is the justification for this edit (§3) and becomes true rather than
self-contradictory once the first sentence is corrected.

No registry row changes — `:5466`'s gloss already reads "declared fixed sizes **or floors**" after
#91's §11.2 amendment, which covers the widened case.

---

## 14. Decisions, and what I did not decide

**Decided, with high confidence:**

1. **Framework `:6058` is defective**; `:6033-6034` and `:6047-6050` govern (§3). The bullet's own
   "same arithmetic" claim is the proof, so this does not rest on my judgment about intent.
2. **Amend the framework in the same change as the code** (§13), not after.
3. **Merge the two bound computations** rather than patching the guard (§5.2), with the
   null-equivalence argument given so a reviewer can check it rather than trust it.
4. **Change `:946`'s message** (§5.3) — omitting this ships a diagnostic naming an undeclared key.
5. **`Error`, existing code** (§6) — continuation of #91's ruling, not a new one.
6. **Accept the compat risk** (§7).
7. **#88's V13(a) is unaffected** (§11 NE-2) — settled by arithmetic, not left as an open risk.

**Flagged, not decided:**

- **Whether `RunCheck`'s exit-code composition is covered at all** (NE-3) — a possible gap in
  someone else's coverage, surfaced here, owned elsewhere.
- **The V-numbering collision in `SplitFlexTests.cs`** (SPEC-91 §16) is about to get worse: #92 adds
  V5/V6 to a class already holding two schemes. **Recommend `Spec92_` prefixes** for #92's tests, and
  that whoever owns the file decide on a convention. Still not worth blocking a merge.
- **NE-1's blast radius is the one thing I could not bound by inspection.** I did not enumerate which
  tests assert the `minSize`-sum message, and `:922`'s interpolation means flex composite assertions
  are in scope too. If NE-1 returns a large set, that is worth a second look before editing — a
  message change touching many tests is a signal the wording is load-bearing somewhere I have not
  considered.

**Confidence: high** on §1's rulings. §3 is the load-bearing one and it rests on quoted text that
contradicts itself, rather than on inference about what the framework meant.
