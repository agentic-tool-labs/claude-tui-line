# §2.3 — `ResolveVerticalEven` brought to parity with the other two distribution loops

Task #78. Written against `/Users/jimcline/git/repos/claude-tui-line`, branch `main` @ `e9e14d3`.

**No spec path was dictated; I used the house convention.** Say if it should move.

---

## 0. READ THIS FIRST — the branch dependency

**Item 5 requires `ClampToAvail`, which does NOT exist on `main`.** It is on `task-74` @ `b6c9ac0`
and is at pre-merge verify. I confirmed the absence directly: `grep -rn 'ClampToAvail' src/` on
`main` @ `e9e14d3` returns nothing.

**Ruled: #78 forks from `main` only after #74 merges, and it CALLS `ClampToAvail` — it does not
define one.** If you pick up this task and the helper is not present, **stop and report that rather
than writing your own.** Two independent `ClampToAvail` implementations reconciled at merge is
precisely the duplication this task exists to end, and it would be a self-inflicted instance of it.

If the Orchestrator instead directs this task to fork from `task-74`, everything below still applies
unchanged; only the base commit differs.

---

## 1. The defect

`SizeResolver.cs` has **three** loops that allocate width across a split's children and drop the
last child when the allocation does not fit. Two have been maintained; the third has not.

| | `AllocateWithDrop` (`:477-534`) | `ResolveVerticalMinRows` (`:558-617`) | `ResolveVerticalEven` (`:835-859`) |
|---|---|---|---|
| below-floor test | `DropFloor(...)` | `DropFloor(...)` | **`Grants[i] < 1`** — hardcoded |
| over-allocation check (#67a) | yes, `:507-508` | yes, `:591-592` | **absent** |
| note on drop | yes, both branches | yes, both branches | **absent — silent** |
| `RenderNoteCollector` param | yes | yes | **absent** |
| `ClampToAvail` (#74) | yes (on `task-74`) | yes (on `task-74`) | **absent** |

Every row of that table is a fix that landed in two of three mirrored loops. **The structural
mirroring is asserted in the comments and tested nowhere**, so each successive fix has silently
skipped the same loop. §7 addresses that directly, because otherwise #78 is just the fourth
instance of the pattern rather than the end of it.

**Note the scope is five items, not the four originally filed.** The fifth (`ClampToAvail`) was
added after #74 landed; without it #78 ships already one fix behind.

### 1.1 The most likely reason this loop kept getting skipped

`ResolveVerticalEven` **takes no `RenderNoteCollector`**. Every one of the skipped fixes needed to
emit a user-visible note, so porting any of them required a signature change — the one thing that
makes a "just mirror it into the third loop" edit stop looking mechanical. The missing parameter is
not a fifth independent defect; it is the *cause* of the other four. Fix it first (§4.1) and the
rest become the copy they always should have been.

---

## 2. The five items

### 2.1 Item 1 — thread `RenderNoteCollector` in

```csharp
private static AllocResult ResolveVerticalEven(
    Pane split, int splitOuterWidth, RenderNoteCollector notes, bool collapse)
```

Parameter order mirrors the siblings: `notes` immediately before `collapse`.

`AllocateEvenOnePass` (`:869`) is **not** changed. Notes are emitted by the resolver, never by the
one-pass allocator — true of both siblings and it stays true here.

**Callers must be threaded.** I did not enumerate them and am not guessing: the implementor finds
them with `grep -n 'ResolveVerticalEven' src/` and passes the collector through. `ResolveNode`
(`:170`) and `ResolveVertical` (`:202`) both already carry a `RenderNoteCollector notes` parameter,
so this is very likely a zero-plumbing change at the call site — **but verify it rather than assume
it**, and if some caller genuinely has no collector in scope, stop and report: that would be a
finding about the call graph, not something to solve by constructing a throwaway collector.

### 2.2 Item 2 — replace the hardcoded floor with `DropFloor`

Today (`:846`):

```csharp
if (ClassifySize(current[i].Size).Kind != SizeKind.Fixed && result.Grants[i] < 1)
```

Becomes the siblings' shape — a `Fixed` skip, then a `DropFloor` comparison:

```csharp
if (ClassifySize(current[i].Size).Kind == SizeKind.Fixed)
{
    continue;
}

var floor = DropFloor(current[i], result.Grants[i], collapse,
    excludeLeft: collapse && i > 0, excludeRight: collapse && i < current.Count - 1);
if (result.Grants[i] < floor)
{
    tooSmall = true;
    break;
}
```

**This is a real behaviour change and the largest one in the task.** A pane granted 1 column
currently survives; under a floor of (say) 20 it is dropped. That is the intended correction — a
1-column pane is not a rendered pane — but it means #78 is not behaviour-preserving and cannot be
gated on byte-parity. §6 verification is written accordingly.

### 2.3 Item 3 — **the collapse-flag ruling. Do not copy the wrong sibling.**

The two siblings pass **different** arguments to `DropFloor`, and an implementor mirroring whichever
one they happened to read first has a 50% chance of being wrong. This is the single most likely
error in the task.

- `AllocateWithDrop` (`:494`) passes the split's real `collapse` and real edge exclusions.
- `ResolveVerticalMinRows` (`:579`) passes `collapse: false, excludeLeft: false, excludeRight: false`.

MinRows' form is **not** a house style — it is a documented workaround, stated at `:574-578`:
`SolveMinRows`'s own candidate floors come from the collapse-blind 1-arg `Floor` overload, so the
drop test is deliberately made collapse-blind **to match the allocation it is checking**.

**Ruled: `ResolveVerticalEven` uses `AllocateWithDrop`'s form — the real `collapse` and real edge
exclusions, exactly as written in §2.2.** The reason is the rule MinRows itself states: the drop
test must match the allocation it checks. `AllocateEvenOnePass` (`:869`) *takes* `collapse` and is
therefore collapse-aware, so MinRows' rationale does not transfer. Copying MinRows here would make
the test collapse-blind against a collapse-aware allocation — the exact inconsistency `:574-578`
exists to prevent.

**Assumption I did not verify:** that `AllocateEvenOnePass` actually *uses* its `collapse` argument
in computing boundary cost rather than merely accepting it. I read its signature, not its body. If
an implementor finds `collapse` is unused there, **stop and report** — that would be a separate
defect and it would reopen this ruling.

### 2.4 Item 4 — the over-allocation check (#67a) and the notes

Insert before the exit, mirroring `:507-508` / `:591-592`:

```csharp
var avail = Math.Max(0, splitOuterWidth - BoundaryCost(split, current.Count, collapse));
var overAllocated = result.Grants.Sum() > avail;

if ((!tooSmall && !overAllocated) || current.Count <= 1)
{
    return result;
}
```

`avail` is recomputed **inside** the loop, not hoisted — the gutter count, and therefore `avail`,
falls with the child count on every drop. Both siblings say so in comments and both are right.

Then the drop notes, replacing today's silent `current = current.Take(...)` at `:857`. Same two
branches and the **same message strings** as the siblings, so the three loops remain greppable as
one behaviour:

```csharp
if (overAllocated)
{
    notes.Add($"pane {current.Count} dropped: children need {result.Grants.Sum()} columns at {splitOuterWidth} columns");
}
else
{
    var lastIndex = current.Count - 1;
    var lastFloor = DropFloor(current[lastIndex], result.Grants[lastIndex], collapse,
        excludeLeft: collapse && lastIndex > 0, excludeRight: false);
    notes.Add($"pane {current.Count} dropped: {result.Grants[lastIndex]} columns is under its {lastFloor}-column floor at {splitOuterWidth} columns");
}
```

Note `excludeRight: false` in the note's `DropFloor` call — the dropped pane is always the last
child, so it has no right neighbour. Both siblings do this (`:527`, `:611`); it is deliberate, not a
copy error.

The `pane {current.Count}` numbering is the 1-based position in the *current* list, which is stable
across repeated drops because only the tail is ever removed (§9.8.2, quoted in both siblings).

Tie-breaking: **over-allocated wins over below-floor** when both hold on one iteration
(`SPEC-2.3-drop-predicate.md` §4). The `if (overAllocated)` ordering above gives that for free —
keep it in that order.

### 2.5 Item 5 — `ClampToAvail` at the exit

Wrap the return per #74:

```csharp
return ClampToAvail(result, avail, splitOuterWidth, notes);
```

Use whatever signature actually landed on `task-74`; the above is the one
`SPEC-2.3-residual-pane-overwidth.md` §4.1 ruled. If it differs, **the landed code wins** — call it
as it exists and note the discrepancy in your report.

**Item 5 is only sound because item 4 lands with it, and this is worth understanding rather than
taking on faith.** A per-grant clamp guarantees `max(grants) ≤ avail`; it does *not* by itself
guarantee `sum(grants) ≤ avail`. The two coincide only when the sole path to the exit carrying an
over-allocated result has exactly one child. Working it:

- After item 4, reaching the exit with `overAllocated == true` requires `current.Count <= 1`.
- At `Count == 1`, `sum(grants)` **is** the single grant, so clamping it to `avail` gives
  `sum ≤ avail`. Full postcondition.
- At `Count == 0` (a split with no children), `Grants` is empty, `sum` is 0, `avail ≥ 0`, so
  `overAllocated` is false and the branch is unreachable.
- `Count` never falls below 1 by dropping, because the loop returns at `Count <= 1` before
  truncating.

**So the clamp is complete here — but only after item 4.** Ported alone onto today's code, where
the exit can be reached with any child count and an over-allocated sum, it would give a partial
guarantee that *looks* like the sibling loops' complete one. **Do not land item 5 without item 4.**
This is an ordering constraint with arithmetic behind it, and it is written out above so a reader
can check it rather than take my word — I asserted an ordering constraint without doing this
elsewhere today and it was wrong.

---

## 3. What must not change

1. **`AllocateEvenOnePass` (`:869`)** — signature and body. It is the allocation; this task changes
   only the loop that calls it.
2. **`DropFloor` (`:371`)** and **`BoundaryCost`** — consumed, never edited.
3. **The two sibling loops.** #78 makes the third match them; it does not "improve" them en route.
   Any change to `AllocateWithDrop` or `ResolveVerticalMinRows` in this diff is out of scope — report
   it instead.
4. **The note message strings.** Byte-identical to the siblings'. Three loops emitting three
   phrasings of one event is how a user-facing behaviour stops being greppable as one thing.
5. **Drop order.** Always the last child, one per iteration.
6. **`ClampToAvail`'s body.** Called, not modified, and not re-implemented (§0).

---

## 4. Sequencing

1. **Item 1 first** (`notes` parameter + callers). Compiles green on its own and changes no
   behaviour; it is the precondition that made the other four look expensive (§1.1).
2. **Items 2, 3, 4 together.** Items 2 and 4 both feed the same exit condition, and item 3 is a
   ruling *about* item 2 rather than a separate edit. Landing item 2 without item 4 gives a loop that
   drops more panes and still cannot detect over-allocation.
3. **Item 5 last**, and only with item 4 present (§2.5).

Intermediate commits must build. It is fine for step 1 to be its own commit; steps 2-3-4 should not
be split further.

---

## 5. NEEDS-EVIDENCE

**N1 — does `AllocateEvenOnePass` use its `collapse` argument?** §2.3's ruling depends on it. Read
`SizeResolver.cs:869`'s body and report only: whether `collapse` is referenced, and if so whether it
reaches a `BoundaryCost` call or an equivalent gutter computation.

- **Used** → §2.3 stands as written.
- **Unused** → **stop and report.** An allocator accepting a flag it ignores is its own defect, and
  it reopens §2.3 — a collapse-aware drop test against a collapse-blind allocation is the
  inconsistency MinRows documents at `:574-578`.

This is a read, not an experiment; it is listed here rather than done inline only because I read the
signature and not the body, and I would rather name the gap than paper over it.

---

## 6. Verification

**There is no byte-parity gate for this task.** Item 2 deliberately changes which panes get dropped,
so "output unchanged" is the wrong bar and asserting it would be asserting the bug. Every item below
is a positive assertion instead.

1. **An even split whose child falls under its floor is now dropped.** A `split: vertical` with even
   distribution, sized so one non-fixed child receives a grant above 1 but below its `DropFloor`.
   Assert the pane is dropped. **On `main` today this pane survives** — this is the regression test
   for item 2 and it must be shown to fail before the fix (see item 8).
2. **The drop emits a note, and it is the below-floor wording.** Same config. Assert a note matching
   `pane {n} dropped: {g} columns is under its {f}-column floor at {w} columns`, with the exact
   numbers. Assert on the full string, not a substring.
3. **Over-allocation is detected and produces the over-allocated wording.** Two fixed-size children
   whose declared sizes alone exceed the split's budget — the case #67a's comment at `:502-506`
   describes, where the per-pane check is skipped for `Fixed` panes so neither child reports
   too-small. Assert a drop occurs and the note reads `children need {sum} columns at {w} columns`.
   **This is the highest-value item here**: it is the case that is invisible to every other test,
   and on today's code it silently renders an over-wide surface.
4. **Over-allocated wins the tie.** A config where a child is both below floor and the sum exceeds
   `avail` on the same iteration. Assert the *over-allocated* wording, not the floor wording.
5. **The collapse flag reaches `DropFloor`** (item 3). Two configs identical but for `collapse`,
   with a child whose grant sits between its collapse-true floor and its collapse-false floor.
   Assert the pane is dropped in one and survives in the other. **This is the test that catches
   copying MinRows instead of `AllocateWithDrop`** — without it, the wrong sibling's form passes
   everything else.
6. **No grant exceeds `avail` at the exit** (item 5). A single-child even split at a width where the
   child's request exceeds `avail`. Assert the grant equals `avail` and that the clamp note is
   emitted. Per `SPEC-2.3-residual-pane-overwidth.md` section 8.1, use a **bordered** split with a non-zero
   gutter so `avail != splitOuterWidth` — a test where the two are equal cannot distinguish clamping
   to the right quantity from clamping to the wrong one.
7. **The two sibling loops are unaffected.** Their existing tests pass unchanged. This is the guard
   on §3 item 3.
8. **Prove items 1 and 3 can fail.** Run both against `main` @ `e9e14d3` before the fix and confirm
   they fail there for the stated reason. A test that would pass on the unfixed code is not testing
   the fix, and three fixes have already slipped past this loop precisely because nothing was
   watching it.

---

## 7. The thing that actually stops this recurring

Five fixes have now skipped this loop because the three-way mirroring is claimed in prose and
enforced nowhere. Fixing the fifth without fixing that leaves a sixth.

**Ruled: this task adds one structural test asserting all three loops emit drop notes.** The cheapest
form that has teeth: for each of the three resolvers, a config that forces at least one drop, and an
assertion that a note is emitted with the shared wording. It fails the moment a fourth loop is added
without notes, or a note string drifts in one of the three.

I am **not** ruling that the three loops should be unified into one implementation. That is a much
larger change than #78, it needs a survey of what genuinely differs between them (min-rows'
collapse-blindness is a real difference, not an accident), and it should be its own task with its own
spec. **Recommend filing it.** This item buys a tripwire, not the refactor.

If an implementor finds this test cannot be written because one of the three resolvers is
unreachable from any config, **report that** — an unreachable distribution loop is a finding worth
more than the test.

---

## 8. Confidence

**High on items 1, 2, 4, 5 being correct and mechanical** once §2.5's ordering is respected. They are
transcriptions of two working implementations sitting 300 lines away, and I read both verbatim rather
than from memory.

**High on §2.5's arithmetic.** Worked case by case above rather than asserted, deliberately.

**Medium-high on §2.3 (the collapse ruling)** — this is the judgment call in the task. The reasoning
is MinRows' own stated rule (the drop test matches the allocation it checks) applied to a different
allocator, which I find compelling. But it rests on N1, which I have not answered. **If N1 comes back
"unused", do not implement §2.3 — route it back.**

**Medium on §7's structural test being the right shape.** The goal is right and I am confident the
tripwire is worth having; the specific form is the cheapest one I could construct, not necessarily
the best one. An implementor who sees a sharper version should propose it.

**Not escalation-worthy.** No security, auth, migration, concurrency, or public interface. The blast
radius is one private static method plus its callers, the behaviour change is bounded to which panes
drop under width pressure, and every item is locally reversible. The one thing I would not let slide
is §0 — a second `ClampToAvail` is the kind of duplication that is cheap to prevent now and tedious
to unpick later.
