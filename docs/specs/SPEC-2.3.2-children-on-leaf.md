# §2.3.2 — "children on a leaf": the resolved reading and its structural trigger

Task #24. No spec path was dictated in the dispatch, so I chose this one; move it if the
repo has a convention I have not seen.

Derived by reading `main`. `key-not-applicable` appears nowhere in `src/` or `tests/` on
`main`, so #24's diff is not there yet — every line number below is a `main` line number and
may have shifted under impl2's changes. The *reasoning* does not depend on the line numbers;
the placement question in §4 does.

---

## 1. The ruling

Reading (a) is correct — `children` present on a pane, with an empty list — but not for the
reason it was offered, and the reason changes where the check can live.

Reading (b) is wrong, and §2.3.2 forecloses it in its own words:

> The same code covers `items` on a pane that also declares `children`, `children` on a leaf,
> `gutter` on a horizontal split, **and whatever the next one is. It is a predicate — *is this
> key read on this node?* — not a list**, per §9.4.1's argument about enumerations.

A phrase in that sentence is an example of the predicate, not an entry in a checklist. "Was
this meant as a fourth case?" is therefore the wrong question: it is a case iff the predicate
says so. It does.

### Why the obvious reading is wrong

The tempting reading — and the one I held before reading the code — is that "children on a
leaf" means a pane carrying `children` with no `split` key, since `split` is what declares a
container. §9.4.2 even appears to describe it: *"A misspelled `split` turns a container into
something that is not a container. The pane's `children` are then a key nothing reads, and half
the statusline disappears."*

`Config.cs:795` refutes it:

```csharp
private static PaneSplit NormalizeSplit(PaneSplit parsedSplit, int childCount) =>
    childCount == 0 ? PaneSplit.None : (parsedSplit == PaneSplit.None ? PaneSplit.Vertical : parsedSplit);
```

Non-empty `children` with **no** `split` normalizes to `Vertical`. The pane is a container.
This is deliberate and §2.2 rules it — `Config.cs:786-794` states that every downstream reader
treats "has children" and "is a split container" as the same question, and that this is the one
place the question gets decided.

So a pane carrying a non-empty `children` list is *never* a leaf, whatever `split` says or
fails to say. The only way a pane carrying a `children` key is a leaf is `childCount == 0`.

### The structural trigger

**A pane is a leaf iff `Children is not { Count: > 0 }`.** This is not a new definition; it is
the one already in force at `ConfigCheck.cs:486` and `:517`, and it agrees with `NormalizeSplit`
by construction.

**The diagnostic fires iff the `children` key is present and the list is empty**, which in C#
is exactly the list pattern:

```csharp
if (pane.Children is { Count: 0 })
```

`{ Count: 0 }` is true only when non-null and empty — a null `Children` (key absent) does not
match. That single pattern *is* the trigger; nothing else is needed.

### Why it cannot be implemented on the assembled tree

`Pane.Children` is `IReadOnlyList<Pane>`, and `ResolvePane` (`Config.cs:642`) produces it as:

```csharp
var childCfgs = cfg.Children ?? (IReadOnlyList<PaneConfig>)Array.Empty<PaneConfig>();
```

`"children": []` and no `children` key at all both yield a zero-length list. **The information
this diagnostic depends on — that the author wrote the key — exists only in `PaneConfig` and is
destroyed by assembly.** Unlike #24's other three cases, this one has no assembled-tree
implementation, correct or otherwise.

---

## 2. Exact change

One file: `src/ClaudeTuiLine/ConfigCheck.cs`.

Walk the raw pane tree with the existing `WalkRawPanes(surfacePane, "/surface/pane")` — the same
walker `CheckLeafOnlyKeysOnSplits` (`:477`) and `CheckBorderInsideOnLeaf` (`:508`) use — and for
each `(pane, path)`:

```csharp
if (pane.Children is { Count: 0 })
{
    yield return new Diagnostic(path + "/children", DiagnosticSeverity.Warning, "key-not-applicable",
        "\"children\" has no effect on a leaf; a pane is a split only when its children list has at least one entry");
}
```

- **Severity `Warning`** — §2.3.2 rules it explicitly.
- **Code `key-not-applicable`** — §2.3.2 rules it; §9.6.1's registry row at `SPEC-V2-FRAMEWORK.md:5340`
  carries it.
- **Path `<pane path>/children`** — matches the sibling checks, which append the offending key.
- **Message** follows §2.3.2's requirement that the message say where the key *does* apply, and
  the house phrasing already set by `:493` and `:499` (`"X" has no effect on Y; <where it applies>`).
  The second clause is load-bearing per §2.3.2: an author who wrote `children: []` needs to know
  that non-emptiness is what makes a split, not merely that the key was ignored.

Guard clause, copied from both siblings — if `config?.Surface?.Pane is not { } surfacePane`,
`yield break`. A config with no surface has no panes to check.

---

## 3. Decisions I made, and why

**No suppression against the empty-pane diagnostic.** A pane with `children: []`, no items, and
no `minSize` will also trip `CheckEmptyPanes` (`:720`). Both fire. They name different things —
one says the `children` key does nothing, the other says the pane will collapse — and they have
different repairs (fill the list in, versus delete the pane). Neither message misdirects the
author toward the wrong edit, so there is nothing to suppress.

**The double-reporting hazard I raised earlier is void, and I withdraw it.** I had worried that
`{"split": "vertikal", "children": [...]}` would emit both `unknown-enum-value` and
`key-not-applicable`, with the second telling the author to delete correct config. `NormalizeSplit`
makes it unreachable: the typo parses to `None`, `childCount > 0` sends it to `Vertical`, the pane
stays a container, and `key-not-applicable` never fires. No suppression rule is needed anywhere.

---

## 4. What the Implementor must check before writing this — placement

**This case cannot share a method with #24's other three unless that method walks the raw
`PaneConfig` tree.**

`distribute`/`gutter` on a horizontal split, and `items` alongside `children`, are all
implementable either way — `Pane.Split == PaneSplit.Horizontal` reads fine off the assembled
tree, and impl2 may well have written them there. If so, this fourth case does not belong in that
method and must go in a new `CheckChildrenOnLeaf(UserConfig? config)` modelled line-for-line on
`CheckBorderInsideOnLeaf`, registered alongside it in the `diagnostics.AddRange(...)` block near
`ConfigCheck.cs:75`.

If #24's method already takes `UserConfig?` and walks via `WalkRawPanes`, add the block there
instead. **Do not move the existing three to satisfy this one.**

---

## 5. What must not change

1. **`NormalizeSplit` stays exactly as it is.** It is §2.2's ruling in code and every downstream
   reader depends on "has children" ≡ "is a split container". This diagnostic reports on the raw
   config; it does not alter assembly.
2. **No new leaf predicate.** `Children is not { Count: > 0 }` is already the definition at `:486`
   and `:517`. A third spelling of the same question is how the two drift apart.
3. **`CheckLeafOnlyKeysOnSplits` and `CheckBorderInsideOnLeaf` are not touched**, and their codes
   `leaf-only-key-on-split` / `border-inside-on-leaf` are not renamed to `key-not-applicable` —
   see §7, which is a separate question and not #24's.
4. **`children: []` remains legal.** This is a `Warning`. It does not become an `Error`, and it
   does not change what renders.

---

## 6. Verification

Append to §2.3.2's verification list:

1. `{"surface": {"pane": {"children": [], "items": [{"item": "model"}]}}}` emits exactly one
   `key-not-applicable` at `/surface/pane/children`, severity `warning`.
2. `{"surface": {"pane": {"items": [{"item": "model"}]}}}` — no `children` key — emits **no**
   `key-not-applicable`. This is the test that fails if someone writes `Count == 0` against
   `Pane.Children` instead of the pattern against `PaneConfig.Children`, and it is the only test
   that distinguishes the two implementations. It must exist.
3. `{"surface": {"pane": {"children": [{"items": []}, {"items": []}]}}}` — non-empty children,
   no `split` key — emits no `key-not-applicable` on `/surface/pane/children`. Guards against a
   regression to the "no split key ⇒ leaf" misreading.
4. A nested `children: []` two levels down reports at `/surface/pane/children/1/children`, not at
   the root — confirms the check rides `WalkRawPanes`' path accumulation rather than assuming the
   root.
5. `{"surface": {"pane": {"split": "vertikal", "children": [{"items": []}]}}}` emits the
   `unknown-enum-value` for the bad `split` and **no** `key-not-applicable`. Pins §3's ruling.
6. A pane with `children: []`, no items, and no `minSize` emits **both** `key-not-applicable` and
   the empty-pane diagnostic. Pins the no-suppression decision so a later reader does not "fix"
   the duplicate.

---

## 7. Flagged, not decided — for the Orchestrator, not for #24

**There are already two shipped diagnostic codes for §2.3.2's predicate.** `leaf-only-key-on-split`
(`:493`, `:499`) and `border-inside-on-leaf` (`:526`) are both "a known key with a legal value on a
node that never reads it" — §2.3.2's definition exactly. #24 introduces `key-not-applicable` as a
third code for the same class, in a section whose own argument is that this is a predicate rather
than an enumeration.

That is a coherence problem and I think it is real, but it is **not #24's to solve**. #24 is
implemented and waiting at the merge gate; widening its scope there to unify three diagnostic codes
is how a finished task stops being finished. Recommend a follow-up task, and note that unification
would be a user-visible change to diagnostic codes, so it needs a compatibility call I have not
made.

I could not verify whether §9.6.1's registry lists `leaf-only-key-on-split` and
`border-inside-on-leaf` alongside `key-not-applicable`. If it lists all three, the spec has already
accepted the enumeration and the follow-up is smaller than it looks.

**A fifth case falls out of the predicate that nobody has named.** `Config.cs:794`: *"an explicit
`split` with no children drops back to a leaf."* So `{"split": "vertical", "items": [...]}` with no
`children` is a known key with a legal value that is read, normalized away, and does nothing —
textbook §2.3.2, and reachable by the same `WalkRawPanes` walk via
`pane.Split is not null && pane.Children is not { Count: > 0 }`. Recommend a follow-up rather than
folding it into #24, for the same merge-gate reason.

**Spec erratum in §9.4.2, low priority.** *"A misspelled `split` turns a container into something
that is not a container. The pane's `children` are then a key nothing reads, and half the statusline
disappears."* `NormalizeSplit` makes this false: the pane stays a container and normalizes to
`Vertical`. The real consequence of misspelling `horizontal` is a silently **wrong axis**, not a
vanished pane. Still a genuine motivation for the §9.4.2 diagnostic, but the stated symptom is wrong
and a future reader may write a test against it.

---

## 8. Confidence

High on the ruling and the trigger. `NormalizeSplit` is unambiguous, the DTO's nullability supplies
the discriminator, and two sibling checks already establish both the leaf predicate and the walker,
so this is the fourth instance of a settled pattern rather than a new mechanism.

The one thing I did not verify is §4 — whether #24's existing three cases walk raw or assembled
panes. That is a read of impl2's diff, not an experiment; whoever has the diff in front of them can
answer it in a few seconds, and it only decides placement, not behaviour.
