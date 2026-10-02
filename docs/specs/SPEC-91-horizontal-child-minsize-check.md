# SPEC-91 — `CheckHorizontalSplitChildren` has no `minSize` counterpart

Issue #91. Ruling on whether `--check` should diagnose a declared-horizontal split whose child
declares a `minSize` exceeding the parent's bound, and if so at what severity and under what code.

Surfaced as an incidental finding during #88 (`SPEC-88-responsive-split-fallback.md` §12(2)), which
documented it and deliberately left it out of scope.

**All `src/` and `tests/` citations are anchored to commit `62687bb`** (`Merge #91: horizontal
split child minSize structural check`; originally `8437c37`, `#88: split:"flex" responsive split
fallback`, re-anchored per A3), except §5.4 and §9.2 which cite the `task-91-horizontal-minsize`
worktree. Cite those anchors when checking for drift.

**This file is authoritative.** `SPEC-91-amendment-A2-v13c.md` is a companion recording A2's
derivation and postmortem; where the two differ, this file governs.

---

## Amendment log

### A1 — after #88 landed

This spec was first written while #88 was in flight, so it held #91 and cited `ConfigCheck.cs` at
pre-#88 line numbers. #88 merged as `8437c37`, inserting a `Flex` branch into `CheckStructuralSizes`
and adding `CheckFlexSplitBounds`, shifting every line number by roughly +38. A1: resolved NE-3;
re-anchored all citations; rewrote §9 for the landed world; activated V6 and added V6b; upgraded
§15's finding to task #92.

### A2 — after cdtui-impl4 implemented

**§9.2 was wrong.** It asserted that `SPEC-88`'s V13(c) would turn red, and called that the expected
outcome. It does not, and it should not. See §9.2 for the corrected ruling and §9.2.1 for how the
error happened. A2 changes §9.2, §5.4 (new), §13 V9, §14 NE-1, and §16.

**No ruling in §1 changes.** A2 corrects a prediction about one test, not the design.

### A3 — after #92

Applied per `SPEC-92-fixed-parent-minsize-sum.md` §12, in the same change as #92's code fix. Three
edits, none touching §1's ruling:

1. **§13 V8** reworded from an exit-code/`ok:false` assertion to an `Error`-severity-diagnostic
   assertion — the input `RunCheck`'s untouched gate consumes, not `RunCheck` itself.
2. **§15** upgraded from "a strong suspicion, not a confirmed defect" to **confirmed at `62687bb`**,
   per SPEC-92 §2.1's proof that `SizeResolver.FixedSize` shares the allocator's own classification.
3. **Eight framework citations re-anchored** for drift (`8437c37`→`62687bb`): `:5419`→`:5466`,
   `:5420`→`:5467`, `:5994`→`:6041`, `:6008-6010`→`:6055-6057`, `:6011-6014`/`:6011`→`:6058-6061`/
   `:6058`, `:6016-6018`/`:6016`→`:6073-6075`/`:6073`. §11.1's and §11.2's amendments are noted
   **applied, not pending**, now living at `:6063-6071` and `:5466` respectively. The header's
   citation anchor moved from `8437c37` to `62687bb`.

---

## 1. Ruling

**Add the check.** Emit a diagnostic, at severity **`Error`**, under the **existing**
`fixed-sizes-exceed-parent` code, from `CheckHorizontalSplitChildren` in
`src/ClaudeTuiLine/ConfigCheck.cs` (`:951-969`).

| Question | Ruling | §  |
|---|---|---|
| Add at all, or document as accepted? | **Add** | §3, §4 |
| Severity | **`Error`** | §6 |
| Diagnostic code | **Reuse `fixed-sizes-exceed-parent`** — no new code | §7 |
| Backward-compat risk | **Accept and ship as error** | §8 |
| Sequencing | **After #88** — satisfied; #88 merged at `8437c37` | §9 |

**This is a small change with an outsized coordination hazard.** The code edit is roughly six lines.
§9 is the part that will cause damage if skipped, and it is why this spec is long.

---

## 2. What the gap actually is

The dispatch framed this as "`minSize` is unvalidated." That is not accurate, and the accurate
framing is what makes the ruling determinate.

`minSize` is already validated in two places in `ConfigCheck.cs`:

- `:866-870` — `minSize > maxSize` on a single pane. Code `min-exceeds-max`, severity `Error`.
- `:940-948` (inside `CheckSplitBounds`) — children's `minSize` **sum** plus boundary cost exceeds
  the parent's `maxSize`. Code `fixed-sizes-exceed-parent`, severity `Error`.

So the real gap is narrower and more defensible: **`CheckStructuralSizes` routes on declared split
direction, and only one of its branches checks `minSize`.**

`CheckStructuralSizes` (`:862-903`), post-#88:

| Declared split | Routes to | Checks fixed | Checks `minSize` |
|---|---|---|---|
| `Vertical` (`:877-887`) | `CheckSplitBounds` (`:925`) | yes (sum) | **yes** (sum) |
| `Horizontal` (`:888-894`) | `CheckHorizontalSplitChildren` (`:951`) | yes (per child) | **no** ← the gap |
| `Flex` (`:895-901`) | `CheckFlexSplitBounds` (`:911`) | via both of the above, AND-combined | inherits the gap |

The asymmetry is in the second row, and #88's `Flex` branch inherits it (§9.3). Nothing in the code
or the framework justifies checking a floor in one direction and not the other.

### 2.1 Why the two branches have different *forms* (sum vs per-child), and why that is correct

This is not an inconsistency and must not be "fixed." The comment at `:879-882` explains it:

> `§2.8` (horizontal width allocation) is out of scope for this phase — `SizeResolver` itself
> doesn't divide width among a horizontal split's children, so summing their fixed/`minSize`
> against the parent would claim a contention that isn't there yet. Revisit this scoping once §2.8
> lands.

And `:953-955`:

> A horizontal split gives every child the full parent width (§2.8 not yet implemented), so there is
> no sum to check — only whether any single child's own fixed size already exceeds the width it will
> be given.

So:

- **Vertical** — children share the constrained axis. They contend. **Sum** is the right check.
- **Horizontal** — each child receives the parent's *full* extent. They do not contend.
  **Per-child** is the right check.

`SPEC-88` §4.5.3 states the same duality and calls it "§2.3's sum-vs-max duality, mirrored into
`--check`", mapping `CheckSplitBounds` to `sideBySideFloor` and `CheckHorizontalSplitChildren` to
`stackedFloor`.

**The form is right in both branches. Only the coverage differs**, and only in the horizontal one.

---

## 3. Why this is a gap and not a deliberate exclusion

Worth settling explicitly, because `:879-882` *is* a deliberate scoping decision and it would be
easy to read it as covering this too. It does not.

That comment declines to **sum** a horizontal split's children — it says summing "would claim a
contention that isn't there yet." It says nothing about a *per-child* check, and the method it
guards performs one, on `FixedSize`, immediately below. The scoping decision is about the
*arithmetic form*, not about which size keys are in scope.

Two independent confirmations that this is understood as a defect and not a design position, both
in `SPEC-88`:

- §6 V11-V13 region — "**The one thing this ruling knowingly under-reports** … it has **no `minSize`
  counterpart**." Recorded as a *pre-existing gap*, "**Accepted, and deliberately not fixed here**",
  and noted as **failing open**.
- §12 incidental finding (2) — "structurally impossible and goes **undiagnosed today**",
  "**Flagged for the Orchestrator to route separately.**"

#88 declined to fix it because closing it "changes what `--check` reports for existing
declared-horizontal configs and could newly reject configs in the field, which is a product decision
and not this task's." That is a scope decision about *#88*, not a ruling that the gap should stand.
This spec is the separate routing #88 asked for.

---

## 4. The invariant, and why it is width-independent

`--check`'s contract is set by framework §9.8 (`SPEC-V2-FRAMEWORK.md:5963-6018`), which is unusually
explicit and decides most of this spec.

§9.8 rules that `--check` **never consults a width** — it does not read `COLUMNS` and does not
resolve sizes, because §12.6's `validate` MCP tool calls it from a process with no terminal at all,
and "a validator whose answer depends on the caller's window is not a validator."

It further rules that degrading at a narrow width is **designed behaviour, not a defect**, so the
diagnostic must be *structural*:

> So the diagnostic is **structural**, and the invariant is narrow but real: a contradiction no
> terminal width can resolve.

**The condition #91 adds satisfies that invariant exactly.** Let a declared-horizontal split have a
declared bound `B` — its own fixed size or its `maxSize` (`:956`, `parentBound =
SizeResolver.FixedSize(split) ?? split.MaxSize`). Every child receives the parent's full extent, so
every child receives exactly `B` columns. A child declaring `minSize = M` with `M > B` therefore
cannot be satisfied — **at any terminal width**, because `B` came from the config, not from
`COLUMNS`.

This is the same substitution §9.8 `:6000-6003` blesses for the existing checks:

> So the width the check compares against is the renderer's, computed by the renderer's code, with
> the parent's **declared** size or `maxSize` standing in for the terminal width. That substitution
> is what keeps this width-independent: same function, a number from the config rather than from
> `COLUMNS`.

And where there is no declared bound, §9.8 `:6073-6075` rules that `--check` must stay silent:

> Where the parent is `fill` or `content`, there is no bound to contradict and `--check` says
> nothing. That is not a gap.

The existing `if (parentBound is not int bound) { yield break; }` guard at `:957-960` already
implements this, and the new check sits inside it, inheriting the behaviour for free.

---

## 5. The change

**File:** `src/ClaudeTuiLine/ConfigCheck.cs`
**Method:** `CheckHorizontalSplitChildren` (`:951-969`)
**Plus** the comment update in §5.4. **Nothing else in `src/` is touched.** In particular
`CheckFlexSplitBounds` (`:911-923`) is not edited, even though its behaviour changes as a
consequence (§9.3).

### 5.1 Current code, as merged at `8437c37`

```csharp
    private static IEnumerable<Diagnostic> CheckHorizontalSplitChildren(Pane split, string path)
    {
        // A horizontal split gives every child the full parent width (§2.8 not yet implemented), so
        // there is no sum to check — only whether any single child's own fixed size already exceeds
        // the width it will be given.
        var parentBound = SizeResolver.FixedSize(split) ?? split.MaxSize;
        if (parentBound is not int bound)
        {
            yield break;
        }

        for (var i = 0; i < split.Children.Count; i++)
        {
            if (SizeResolver.FixedSize(split.Children[i]) is int childFixed && childFixed > bound)
            {
                yield return new Diagnostic($"{path}/children/{i}", DiagnosticSeverity.Error, "fixed-sizes-exceed-parent",
                    $"this pane's fixed size ({childFixed}) exceeds its parent's bound ({bound}); a horizontal split gives every child the full parent width");
            }
        }
    }
```

### 5.2 Required code

Only the loop body changes. The signature, the `parentBound` expression, the `yield break` guard,
and the existing diagnostic's path, code, severity and message text are all unchanged.

```csharp
    for (var i = 0; i < split.Children.Count; i++)
    {
        var child = split.Children[i];

        if (SizeResolver.FixedSize(child) is int childFixed && childFixed > bound)
        {
            yield return new Diagnostic($"{path}/children/{i}", DiagnosticSeverity.Error, "fixed-sizes-exceed-parent",
                $"this pane's fixed size ({childFixed}) exceeds its parent's bound ({bound}); a horizontal split gives every child the full parent width");
        }
        else if (child.MinSize is int childMin && childMin > bound)
        {
            yield return new Diagnostic($"{path}/children/{i}", DiagnosticSeverity.Error, "fixed-sizes-exceed-parent",
                $"this pane's minSize ({childMin}) exceeds its parent's bound ({bound}); a horizontal split gives every child the full parent width");
        }
    }
```

The method's leading comment must also be updated, since it currently says "only whether any single
child's own **fixed size** already exceeds the width it will be given." Replace "fixed size" with
"fixed size or floor". Do not otherwise rewrite it — its §2.8 explanation is still correct and still
load-bearing. **This is a different comment from the one at `:879-882`**, which §12 forbids touching.

### 5.3 Three details of the above, each deliberate

**(a) It reuses the already-computed `bound`, and does not compute its own.**

`bound` is `SizeResolver.FixedSize(split) ?? split.MaxSize`. Both disjuncts are correct ceilings for
the floor comparison: if the parent is fixed at `B` the child gets exactly `B`; if the parent has
`maxSize B` the child gets at most `B`. In both cases `M > B` is unsatisfiable.

Note this differs from `CheckSplitBounds`, whose *sum* min-check consults only `split.MaxSize`
(`:940`) and ignores a fixed parent. That asymmetry is **task #92** — see §15. **Do not "align" the
two here** (§12).

**(b) `child.MinSize` is read directly; there is no `SizeResolver` call.**

The fixed branch must go through `SizeResolver.FixedSize` because "fixed size" is *derived* — a
pane's fixed extent depends on how its `size` token parses. `minSize` is not derived: it is a
declared integer key read straight off the pane (`Config.cs:158`), and `CheckSplitBounds:942`
already reads it directly as `c.MinSize ?? 0`. Introducing a resolver call here would invent an
indirection the codebase does not have.

This is **not** the §9.8 `:5990-5998` "call `SizeResolver`'s own boundary-cost function" rule. That
rule governs **boundary cost**, which is genuinely derived arithmetic that a hand-written sum
double-counts. There is no boundary cost in this check at all — each child gets the full parent
extent, so there is no divider to reserve. Adding one would be wrong.

**(c) `else if`, not a second independent `if` — at most one diagnostic per child.**

Where a child declares *both* a fixed size and a `minSize` over bound, only the fixed one is
reported.

The reason is not brevity. `{"size": N, "minSize": M}` with `M > N` is an **explicitly unresolved
question in another spec**: `SPEC-2.3-drop-predicate.md:323` lists it as surfaced by its §3(d) with
"**Three defensible answers**" and does not settle it. Emitting two diagnostics for that pane would
be #91 taking an implicit position on which declaration governs — pre-empting a ruling that belongs
to that spec.

`else if` costs nothing in detection power. It suppresses the `minSize` diagnostic **only** when the
fixed diagnostic already fired on the same child, so every structurally-impossible child still
produces exactly one error. A child with `size: 5, minSize: 30` under `bound: 10` still reports, via
the `minSize` branch, because the fixed branch did not fire.

This also matches `SPEC-88` V13(b)'s stated preference for "exactly **one** diagnostic" per fault.

### 5.4 Required: update V13(c)'s stale comment (A2)

**File:** `tests/ClaudeTuiLine.Tests/SplitFlexTests.cs`, the comment at `:517-520` inside
`V13c_SameConfigsDeclaredVerticalAndHorizontal_ByteIdenticalToCurrentMain`.

It currently reads:

```csharp
        // §7/V13(c): CheckSplitBounds and CheckHorizontalSplitChildren are called unchanged for
        // declared vertical/horizontal — this pins their exact pre-#88 output, including
        // CheckHorizontalSplitChildren's documented minSize gap (it only checks FixedSize), which
        // must NOT be "helpfully" fixed as part of this change.
```

Two sentences are now false in a way that matters. The comment describes the `minSize` gap as live
and **instructs future editors not to close it** — a gap #91 has just deliberately closed. Left
alone it is a standing instruction to undo this spec, sitting in a test file, with no indication its
reason expired. Same hazard as §9.1's stale `SPEC-88` §7 prohibition, somewhere people read more
often than a spec.

**#91 owns this edit.** The comment became false *because of* #91, and it lives in a file #91
already modifies (V6/V6b at `:533-610`). §12's "do not touch another spec's file" constraint governs
`SPEC-88-responsive-split-fallback.md`, not test-file comments this change invalidates.

**Required replacement** — wording is the implementor's, these facts are not:

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

The final clause pre-empts exactly the change that was contemplated when V13(c) was found green, so
the next reader does not re-raise it.

---

## 6. Severity: `Error`

Consistent with every comparable diagnostic, and required by §9.8's own logic.

All three structural checks §9.8 enumerates are errors:

- `fixed-sizes-exceed-parent` — framework `:5466` table: `error`
- `min-exceeds-max` — framework `:5467` table: `error`; §9.8 `:6055-6057` notes §9.4 called it a
  warning "in an earlier draft and has been **corrected**"
- the `minSize`-sum case — framework `:6058`, same code as the first, therefore also `error`

The severity axis in this project tracks **"unachievable everywhere"**, not "how confident the check
is" and not "how new the check is." The framework's gloss for `min-exceeds-max` is literally
"unachievable everywhere | error". A child floor above its parent's declared ceiling is unachievable
everywhere.

`DiagnosticSeverity` has exactly two members (`ConfigCheck.cs:9-13`, `Error` and `Warning`). There
is no third, softer channel, and §8.3 explains why `Warning` must not be pressed into service as one.

---

## 7. Diagnostic code: reuse `fixed-sizes-exceed-parent`. Do not mint a new one

Framework §9.8 `:6011-6014` already decides this, for the exactly-parallel `minSize`-sum case:

> Children's `minSize` sum, plus **the same boundary cost**, exceeding the parent's `maxSize`.
> **Same code as the first: it is the same contradiction with the floor rather than the exact size**
> — and therefore the same arithmetic, taken from §2.10.

So the framework's stated rule is: **a floor-based contradiction takes the same code as the
exact-size contradiction.** #91's condition is that rule applied to the per-child form. Reuse is not
a convenience here; it is the framework's ruling.

Four further reasons:

1. **The codebase already does it.** `fixed-sizes-exceed-parent` is emitted from four sites as of
   `8437c37` — `:921` (flex composite), `:935` (fixed sum), `:945` (**`minSize` sum**), `:966`
   (fixed per-child). A `minSize`-derived contention already ships under this code. Minting a new
   code for #91 would leave `:945` as the inconsistent one.
2. **§9.6.1's registry.** Codes are a compatibility surface — §9.8 `:6053` cites §9.6.1's rule that
   "a code that is not in it does not exist." A new code requires a new §9.4 registry row and
   becomes permanently supported API. Reuse requires neither.
3. **Code reuse is the established norm.** `key-not-applicable` is emitted from ten sites,
   `part-source-count` from three, `unknown-item-id` from four. Codes identify a *kind of fault*,
   not a call site.
4. **`SPEC-88` §7 pins it** — "**The `fixed-sizes-exceed-parent` code's meaning and severity**
   (§9.6). Reused, not redefined, and no new code is registered." #91 stays inside that constraint.

**The messages remain distinguishable** — a consumer branches on `code`, and a human reads the
`message`, which names `minSize` explicitly and differs from the fixed variant's wording.

### 7.1 One honesty problem this creates, and the amendment that fixes it

The §9.4 registry gloss at framework `:5466` reads:

> | `fixed-sizes-exceed-parent` | declared fixed sizes cannot fit the parent at any width | error | 9.8 |

That gloss is **already inaccurate on `main`**, because `:945` files a `minSize` contention under it
today. #91 makes it more inaccurate. See §11 for the required amendment. This is a documentation
defect #91 inherits and should fix, not one it creates.

---

## 8. Backward compatibility — the real question, and the ruling

The dispatch is right that this is the crux: closing the gap can newly reject configs that pass
`--check` today.

### 8.1 The blast radius, stated precisely

`RunCheck` (`Program.cs:660-704`) ends:

```csharp
var hasError = diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
...
return hasError ? 1 : 0;
```

and `--check --json` reports `ok: !hasError`. So a new **error** genuinely gates: a
previously-passing config newly **exits 1** and newly reports **`ok: false`**, including through
§12.6's `validate` MCP tool.

The affected config is narrow, and every clause is required:

1. a pane with `split: "horizontal"` **declared** (not defaulted — absent `split` normalizes to
   `vertical`), **and**
2. that pane has a **declared bound** — its own fixed size, or a `maxSize`; a `fill`/`content`
   parent is untouched (§4), **and**
3. a **direct child** declares `minSize` **strictly greater** than that bound.

Plus, post-#88, the `flex` case in §9.3 — a different population, discussed there.

### 8.2 Why the risk is acceptable

**The decisive argument: these configs were never working.**

§9.8's stated fear — `:6041` — is precisely calibrated:

> a false `error` is the worst outcome available here, because exit 1 sends the user to fix
> something that already works.

That is the harm to avoid, and #91 does not cause it. A child floor above its parent's declared
ceiling is unsatisfiable at *every* width (§4). Such a config is **already** misrendering today, and
`SPEC-88` calls the current behaviour **failing open**. `--check` newly reporting it is the
validator becoming correct about a config that was already broken — not a working config newly
rejected.

So this is **not** the §9.4 antipattern §9.8 `:5980-5981` warns about ("a validator which warns
about things that work correctly gets ignored on the occasions it is right"). It is the opposite.

**Supporting points:**

- **No false positives are possible.** The check fires only on declared numbers, compared against a
  declared bound, with no width and no derived arithmetic. Contrast §9.8 `:5990-5998`, where the
  double-counting risk came entirely from hand-written boundary arithmetic — which this check does
  not perform (§5.3(b)).
- **The signal is high.** `minSize` is a number the author deliberately named. Framework `:2363` and
  §2.11.1 both treat an explicit `minSize` as a strong declaration of intent.
- **No in-tree test breaks.** Verified at `8437c37` and again post-implementation: no fixture
  declares a horizontal split with a child `minSize`. `HorizontalSplitChildrenWithinParentBound_…`
  (`:1841-1862`) uses `Size` only, and `SPEC-88`'s V13(c) likewise — see §9.2.
- **The repair is obvious and local.** The message names the child's `minSize`, the parent's bound,
  and why every child gets the full parent width.

### 8.3 Rejected: ship as `Warning` first, promote to `Error` later

Rejected for three reasons:

1. **It misuses the severity axis.** Severity here means "is this achievable at any width" (§6), not
   "how recently was this check added." A `Warning` would assert something false about the config.
2. **There is no deprecation channel to build it on.** The enum has two members. No version
   negotiation, no `--strict`, no per-code suppression anywhere in `ConfigCheck.cs`.
3. **It would contradict the framework twice over.** §9.8 `:6055-6057` records `min-exceeds-max`
   being *corrected from warning to error* — the project moved the opposite way on a directly
   analogous floor diagnostic. And a warning that a config is structurally impossible is exactly the
   "validator that warns about things that work" §9.4 says gets ignored.

### 8.4 Rejected: document as accepted behaviour, change nothing

Rejected. It leaves an asymmetry with no principled defence (§2), it has been logged as a defect
three times (#88 twice, #91 once), and it keeps `--check` silent about a config that cannot render.

---

## 9. Coordination with #88 — READ BEFORE IMPLEMENTING

#88 merged at `8437c37`, so the **ordering** constraint is satisfied. Two coordination hazards
remain live, and one predicted hazard did not materialise.

### 9.1 `SPEC-88`'s text still forbids this change — SPEC-91 governs

`SPEC-88` §7 "What must NOT change" still reads, unamended:

> **`CheckSplitBounds` and `CheckHorizontalSplitChildren` themselves.** §4.5.3 adds a `Flex` branch
> to their *caller* and calls both unchanged. In particular, **do not add a `minSize` check to
> `CheckHorizontalSplitChildren`** to close the gap §4.5.3 documents — that changes declared-
> horizontal behaviour and is out of scope (§12). **V13(c) guards this.**

**#91's implementor will read that as forbidding this spec, and so will a Reviewer validating the
diff.** Resolve as follows:

**That prohibition was scoped to #88's own diff and is discharged.** `SPEC-88` §12(2) — the same
document — asks for exactly this work to be "**routed separately**" and says "**if it is fixed
later**", which is now. §7's list is what *#88* must not change while landing `flex`; it is not a
standing prohibition binding all future work. **`SPEC-91` governs.**

Note the final clause, "V13(c) guards this," is **factually wrong even on its own terms** — see
§9.2. The `SPEC-88` text needs amending so the next reader is not misled (§9.5), but that is
cdtui-architect's edit and **#91 is not blocked waiting on it.** If a Reviewer flags the conflict,
this section is the answer.

### 9.2 V13(c) does **not** go red, and must not be made to (A2 — corrects a wrong earlier ruling)

**This section previously asserted V13(c) would turn red and called that the expected outcome. That
was wrong.** The corrected ruling:

`V13c_SameConfigsDeclaredVerticalAndHorizontal_ByteIdenticalToCurrentMain`
(`SplitFlexTests.cs:498-531`) uses `MaxSize = 40` with two children at `Size = "50"` and **declares
no `MinSize` at all**. #91's check is gated on `child.MinSize is int`, so it cannot fire. The two
diagnostics V13(c) asserts (`:527-530`) both come from the pre-existing `FixedSize` branch, which
#91 leaves byte-identical. **V13(c) is correctly green, and stays green.**

**Ruling: leave V13(c)'s fixture alone. Do not add a `minSize`-bearing case to it.** Four reasons:

1. **Purpose.** V13(c) is #88's regression guard — the `Flex` branch must not perturb the declared
   directions. That purpose is intact and a `FixedSize`-only fixture is the right instrument.
2. **The coverage already exists.** #91's check is exercised by V1, V2, V3, V5, V7 in
   `ConfigCheckTests.cs` and V6/V6b in `SplitFlexTests.cs`. A `minSize` case on V13(c) would
   duplicate V1/V2 and add no assurance.
3. **Conflation.** A test guarding two specs' invariants fails for two unrelated reasons and tells
   you less on failure. V13(c) failing should mean exactly one thing.
4. **The anchor has moved.** V13(c) compares against "current `main`", which post-#91 *includes*
   #91. A #91-sensitive case there is self-referential.

**Its comment is stale regardless and must be fixed — see §5.4.**

**V13(c) staying green is evidence *for* the implementation.** #91 is supposed to fire on a child
`minSize` over bound and on nothing else. A correctly-scoped change leaves a `FixedSize`-only parity
fixture untouched; had V13(c) gone red, that would have meant #91 was perturbing configs with no
`minSize`, which would be a genuine defect.

#### 9.2.1 How the error happened, and what survives it

I reasoned: V13(c) pins declared-horizontal output → #91 changes declared-horizontal output →
V13(c) fails. The middle step is too coarse. #91 changes declared-horizontal output **only for
configs carrying a child `minSize` over bound** — a qualifier this spec already carried at §13 V9.
**So §9.2 and §13 V9 contradicted each other, and the correct statement was the one already
written.** A self-diff failure, and a spec defect, not an implementation defect.

It was compounded by asserting an outcome for a fixture I had not read, in the same breath as
instructing the implementor to read it first.

**What survives:** the *locate V13(c) before writing code* instruction was right and worked exactly
as intended — it is what surfaced the discrepancy early instead of at review. The general trap it
guarded against ("do not make a red baseline test green by weakening the check") remains sound
advice; it simply describes a failure that does not occur for this fixture.

### 9.3 #91 changes `flex` behaviour, through code #91 does not touch

#88's `CheckFlexSplitBounds` (`:911-923`) is:

```csharp
    var sideBySide = CheckSplitBounds(split, path, collapse).ToList();
    var stacked = CheckHorizontalSplitChildren(split, path).ToList();

    if (sideBySide.Count == 0 || stacked.Count == 0)
    {
        yield break;
    }

    yield return new Diagnostic(path, DiagnosticSeverity.Error, "fixed-sizes-exceed-parent",
        $"this flex split's children exceed its parent's bound in both arrangements: side by side ({sideBySide[0].Message}) and stacked ({stacked[0].Message})");
```

`stacked` is `CheckHorizontalSplitChildren`'s output. **#91 makes that method produce diagnostics it
previously did not**, so a `flex` pane whose children have `minSize` over bound now satisfies the
AND where it previously short-circuited at `stacked.Count == 0`. `SPEC-88` §12(2) predicted this
("§4.5.3's under-report closes for free"). Correct behaviour — impossible in both arrangements is
exactly #88's AND criterion — but it is **a behaviour change to `flex` caused by #91, in a method
#91 does not edit**, and it is untested unless #91 adds V6.

There is also a **message-composition consequence**: `stacked[0].Message` will now sometimes be
#91's new `minSize` wording, interpolated into the flex composite message. V6b covers it.

**Not to be confused with #88's V13(a)**, which must stay clean: `{"split":"flex","maxSize":40}`
with two `minSize: 30` children. Each child's 30 is **under** the bound of 40, so #91's per-child
check does **not** fire, `stacked` stays empty, and the AND still yields nothing. **V13(a) still
passes.** The distinction is `minSize > bound` per child (fires) versus `Σ minSize > bound` (does
not, in the per-child form) — §2.1's duality doing its job.

### 9.4 Ordering — satisfied

#88 merged at `8437c37`. #91 may be implemented. Branch from `8437c37` or later so
`CheckFlexSplitBounds` is present — implementing against a pre-#88 base would silently skip §9.3.

### 9.5 `SPEC-88` amendments still outstanding — cdtui-architect's, not #91's

`SPEC-88` is unamended (919 lines). Four places are stale:

- §7 — the "do not add a `minSize` check" prohibition, discharged by this spec (§9.1).
- §7's "V13(c) guards this" — factually wrong; V13(c)'s fixture cannot detect the gap (§9.2).
- §12(2) — incidental finding resolved; should point to this spec.
- (Separately, the §3.4.2 / V11 amendment from the Ultra-Advisor's world-(C) ruling.)

**#91's implementor must not make these edits.** #91 proceeds without them.

---

## 10. Forward compatibility with §2.8

The check rests on "a horizontal split gives every child the full parent width," which holds only
because §2.8 is unimplemented (`:879-882`, `:953-955`).

**It stays sound when §2.8 lands.** Under §2.8 width is *divided*, so each child receives some share
`s_i ≤ B`. A child with `minSize = M` where `M > B` satisfies `M > B ≥ s_i` — still unsatisfiable, a
fortiori.

So §2.8 cannot make this check *wrong*, only **incomplete**: once children contend, a sum check is
also needed. At that point both branches converge on sum-plus-per-child, and #91's per-child
`minSize` check survives unchanged as one half of it. Same forward-compatibility the existing
per-child `FixedSize` check already enjoys.

---

## 11. Framework amendments

Two, both to `SPEC-V2-FRAMEWORK.md`. Both correct documentation **already** inaccurate on `main`.

### 11.1 §9.8's bullet list does not cover the per-child form at all

§9.8 `:5986-6014` (pre-drift; the same span is now `:6033-6063`) enumerates the structural checks in
three bullets, **all sum checks**, written for the vertical/shared-axis model. The existing per-child
horizontal `FixedSize` check (`:962-968`) is **already an implementation extension beyond this
list**; a reader reconstructing `--check` from §9.8 alone would not produce it.

**Applied by #91 — not pending.** The added paragraph now lives at framework `:6063-6071`.

**Required amendment (as applied)** — added after the three bullets, before the `fill`/`content`
paragraph (now at `:6073-6075`, was `:6016`):

> **The arithmetic form follows the axis, not the size key.** Where a split's children *share* the
> constrained axis they contend, and the check is a **sum** plus the boundary cost — the three
> bullets above. Where each child receives the parent's **full** extent — a declared-horizontal
> split, for as long as §2.8 is unimplemented — there is no contention and therefore no sum, and the
> check is **per child**: any single child whose own declared fixed size, **or whose own declared
> `minSize`**, exceeds the parent's bound is impossible on its own. Both per-child cases are the same
> contradiction as the first bullet and carry the same code, `fixed-sizes-exceed-parent`. There is no
> boundary cost in the per-child form: each child receives the full extent, so no divider is
> reserved, and adding one would reintroduce exactly the double-count the first bullet's rule closes.

The final sentence pre-empts an implementor misapplying §9.8's boundary-cost rule here (§5.3(b)).

### 11.2 §9.4's registry gloss understates the code

**Applied by #91 — not pending.** The corrected gloss now lives at framework `:5466`.

`:5466` (was `:5419`) previously read:

> | `fixed-sizes-exceed-parent` | declared fixed sizes cannot fit the parent at any width | error | 9.8 |

Already inaccurate on `main` (`:945`). Amended to name floors:

> | `fixed-sizes-exceed-parent` | declared fixed sizes **or floors** cannot fit the parent at any width | error | 9.8 |

Severity, code and section reference unchanged. Per framework `:445`, editing a documented closed
set can fail `check-all.sh`'s doc-token check; this edits table prose rather than adding a member,
so it should not trip it — but see §14 NE-2.

---

## 12. What must NOT change

- **`CheckSplitBounds` (`:925-949`) — entirely.** In particular its min-check's use of
  `split.MaxSize` alone at `:940`. That is **task #92** (§15), not #91.
- **`CheckFlexSplitBounds` (`:911-923`) — entirely**, including its AND short-circuit and composite
  message format. Its *behaviour* changes as a consequence of #91 (§9.3); its *code* does not.
- **`CheckStructuralSizes`'s routing (`:862-903`)**, including all three branches and the
  `:879-882` §2.8 scoping comment.
- **The existing fixed per-child diagnostic** at `:962-968` — condition, path, code, severity and
  **message text verbatim**. §13 V4 guards this.
- **`parentBound`'s expression and the `yield break` guard** (`:956-960`).
- **V13(c)'s fixture** (`SplitFlexTests.cs:500-515`) — §9.2. Only its comment changes (§5.4).
- **`DiagnosticSeverity`** (`:9-13`) — no third member.
- **The `fixed-sizes-exceed-parent` code's meaning, severity, and registry row** beyond §11.2's
  prose widening.
- **`RunCheck`** (`Program.cs:660-704`).
- **`--check`'s width-independence.** No `COLUMNS` read, no resolve pass, no boundary-cost call.
- **`SizeResolver`, `PaneCollapse`, `Config.cs`, `Pane.cs`** — untouched.
- **`SPEC-88-responsive-split-fallback.md`** — not #91's file to edit (§9.5).

---

## 13. Verification

V1-V5, V7, V8 in `tests/ClaudeTuiLine.Tests/ConfigCheckTests.cs`, beside the existing horizontal
cases at `:1817-1862`. V6/V6b in `SplitFlexTests.cs`.

- **V1 — fires (`maxSize` parent).** `{"split":"horizontal","maxSize":40}`, one child
  `{"minSize":50}` → exactly one diagnostic: `fixed-sizes-exceed-parent`, `Error`, path
  `/surface/pane/children/0`, message naming `50` and `40`.
- **V2 — fires (fixed parent).** `{"split":"horizontal","size":"10"}`, one child `{"minSize":20}` →
  same shape. Guards §5.3(a) — a `maxSize`-only test would pass against an implementation wrongly
  consulting `split.MaxSize` alone, mirroring `CheckSplitBounds:940`. **This is the test that
  distinguishes the correct implementation from that plausible wrong one.**
- **V3 — no bound, no diagnostic.** A `fill`/`content` horizontal parent with a child
  `{"minSize":999}` → **no** `fixed-sizes-exceed-parent`. Guards framework `:6016-6018`.
- **V4 — the fixed check is unchanged.** `:1817-1837` and `:1841-1862` pass **unmodified**, messages
  byte-identical to `8437c37`.
- **V5 — at most one diagnostic per child (§5.3(c)).** Horizontal parent bounded at 10, child
  `{"size":"20","minSize":30}` → **exactly one** diagnostic, message naming the **fixed size (20)**.
  Paired with child `{"size":"5","minSize":30}` under the same bound → exactly one, naming the
  **`minSize` (30)**. The pair proves `else if` suppresses only the redundant case.
- **V6 — `flex` inherits the fix (§9.3).** `{"split":"flex","maxSize":40}` with two `minSize: 50`
  children → the AND produces **exactly one** diagnostic, path = **the split's own path** (per
  `:921`). **And #88's V13(a) must still pass** in the same run. If both cannot be green, the
  implementation is wrong.
- **V6b — the composite message quotes the new wording.** For V6's config, the message names **both**
  arrangements and its "stacked (…)" clause contains #91's `minSize` text. Guards `:922`.
- **V7 — no boundary cost leaked in.** V1's message names the bound as `40`, not boundary-adjusted.
- **V8 — an `Error`-severity diagnostic is produced.** `--check` on V1's config yields at least one
  diagnostic with `DiagnosticSeverity.Error`. This pins the input `RunCheck`'s gate consumes
  (`diagnostics.Any(d => d.Severity == Error)` → exit 1, `ok: false`). `RunCheck` itself is untouched
  by #91 (§12) and is deliberately not re-tested here.
- **V9 — V13(c) unmodified and still green (A2).** `SPEC-88`'s V13(c) passes with **no fixture
  change** — only its comment is updated per §5.4. Its continued green is an assertion that #91 is
  correctly scoped: the change must not perturb configs declaring no `minSize`. **A red V13(c) is a
  defect, not an expected re-baseline.**
- **V10 — full suite.** `dotnet test tests/ClaudeTuiLine.Tests` — **fully green** (§14 NE-1).
- **V11 — `tools/check-all.sh`** passes, or fails only in ways already failing on `main` (§14 NE-2).
  §11's amendments must land in the **same change** as the code, per framework `:445`.

---

## 14. NEEDS-EVIDENCE

I do not run anything. Each item states what to run and what each outcome decides.

- **NE-1 — does any existing test fail? (A2: expectation corrected.)**
  *Resolved by inspection and by implementation.* No fixture in `ConfigCheckTests.cs` declares a
  horizontal split with a child `minSize`, and `SPEC-88`'s V13(c) is `FixedSize`-only (§9.2).
  **Expected outcome is a fully green suite — including V13(c).**
  **To run:** `dotnet test tests/ClaudeTuiLine.Tests`.
  → **All green:** proceed.
  → **Any failure, V13(c) included:** stop and report the test and its fixture. **Do not "fix" it.**
  A failure now most likely means this spec is wrong. (Earlier drafts predicted a deliberate V13(c)
  re-baseline; that prediction was withdrawn in A2 — a red V13(c) is a defect.)

- **NE-2 — does `tools/check-all.sh` accept §11's amendments?**
  Run before and after. It was failing on `main` before #88 for unrelated reasons
  (`check-citations`, `check-counts` on `SPEC-2.3-drop-predicate.md:172`), so the bar is **no new
  failures**, not exit 0. Re-establish the baseline at `8437c37` first.
  → **No new failures:** proceed.
  → **A new failure naming §9.4's table or §9.8:** report the exact message; §11.2 needs revisiting.

- **NE-3 — RESOLVED.** #88 landed as `8437c37`. Branch from `8437c37` or later (§9.4).

- **NE-4 — are there real-world configs in the blast radius?**
  §8.1 defines the shape; §9.3 adds the `flex` population. Any example, fixture, README snippet or
  `docs/` config that matches will newly fail `--check` and must be fixed in the same change.
  → **None found:** §8.2's risk assessment stands unqualified.
  → **Any found:** report. That is evidence the pattern occurs in the field and would reopen §8.3's
  staged-rollout question **as a product call for Jim rather than an engineering one**.

---

## 15. Task #92 — the shared root cause

**`CheckSplitBounds`'s `minSize` sum check ignores a fixed parent.** `:929` computes
`parentBound = SizeResolver.FixedSize(split) ?? split.MaxSize` for the *fixed* sum check, but the
*`minSize`* sum check at `:940` consults `split.MaxSize` alone. So a **fixed** vertical parent —
`size: "40"` — whose children's `minSize` sum is 50 goes undiagnosed. Framework `:6058` has the same
narrow wording, so the code may be faithfully implementing a too-narrow bullet.

**It is not confined to the vertical path.** Because `CheckFlexSplitBounds` (`:913`) delegates to
`CheckSplitBounds`, a **`flex` pane with a fixed size** (not `maxSize`) and a child whose `minSize`
exceeds it will, even after #91:

- fire `CheckHorizontalSplitChildren` (per-child; uses `FixedSize(split) ?? MaxSize`), but
- **not** fire `CheckSplitBounds`'s min branch (gated on `MaxSize`, null here),

so the AND yields **nothing**, for a pane impossible in both arrangements. **#91 does not close that
case**; the `maxSize`-parent case works only because `Σ minSize ≥ any single minSize`.

So #92 is a **shared root cause feeding both the vertical sum check and #88's flex combinator.**

**Out of scope for #91**, for the reason #88 used to punt this one. §12 forbids touching it here.

**Caution for implementor and reviewer:** do not "strengthen" V6 by switching it to a fixed-size
flex parent. That variant will fail, and the failure is **#92, not a #91 defect**.

**Confirmed at `62687bb`** and specified in `SPEC-92-fixed-parent-minsize-sum.md`.
`SizeResolver.FixedSize` (`SizeResolver.cs:175`) delegates to the allocator's own `ClassifySize`,
so no `size` token form complicates the bound — see SPEC-92 §2.1.

---

## 16. Decisions, and what I did not decide

**Decided, with confidence:**

1. **Add the check** (§3).
2. **Severity `Error`** (§6).
3. **Reuse `fixed-sizes-exceed-parent`** (§7).
4. **Accept the backward-compat risk, no staged rollout** (§8).
5. **`else if`, one diagnostic per child** (§5.3(c)).
6. **`SPEC-91` governs over `SPEC-88` §7's stale prohibition** (§9.1).
7. **V13(c) keeps its fixture and stays green; only its comment changes** (§9.2, §5.4) — **A2,
   reversing this spec's earlier claim that it would go red.**

**Flagged, not decided — for the Orchestrator or Jim:**

- **The residual product question (§8.2, NE-4).** No *working* config is newly rejected, because the
  affected configs cannot render correctly at any width. Sound, but it is an engineering judgment
  about a **user-visible gating change**. If NE-4 finds the pattern in practice, **this becomes
  Jim's call, not mine.**
- **The `SPEC-88` amendments (§9.5)** — another architect's file. Specified, not made.
- **Task #92 (§15)** needs its own ruling.
- **Optional, declined here:** V13(c)'s name (`…ByteIdenticalToCurrentMain`) points at a moving
  reference. Renaming would be an improvement but widens this diff for no correctness gain; note it
  against #92 instead.
- **Incidental:** `SplitFlexTests.cs` now carries two V-numbering schemes — `SPEC-88`'s `V6a`/`V6b`
  (`:139`, `:159`) and `SPEC-91`'s `V6`/`V6b` (`:536`, `:586`). Compiles and CI output is
  unambiguous, and the section header at `:533` mitigates it. Prefixing #91's with `Spec91_` would
  be tidier; **not worth holding a merge for.**

**Confidence: high** on §1's rulings — framework §9.8 and `:6011-6014` decide most of this directly.

**A2's lesson, recorded rather than buried:** this spec asserted a test outcome without reading the
fixture, while instructing its implementor to read it first. §9.2 and §13 V9 then disagreed with
each other, and the implementor caught it. **The risk in this task was never the ruling; it was the
coordination** — §9.1, §9.2, §9.3 — and one of the three turned out to be my own error.
