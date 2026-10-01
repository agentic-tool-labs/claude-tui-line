# Diagnosis — `MaxLinesSet_OutputExceedsCap_TruncatesAndEmitsPinnedNote` fails after the #73b merge

**This is a diagnosis, not a change spec.** It ends in a NEEDS-EVIDENCE item because the deciding
fact is empirical and I do not run experiments. No code should be written against this document until
E1 comes back.

Reported symptom: merging #73b (`9d798bb`) onto `main` (which carries #31's `maxLines`) leaves
1379/1380 passing, with `CommandProviderTests.cs:246` failing on `Assert.Contains()` against an
**empty** collection. `main` was reset to `f5a2511`; #73b is preserved on `task-73b`.

---

## 1. The merge did not cause this. There is no code path from #73b's diff to this test.

`9d798bb` touches exactly three files:

- `src/ClaudeTuiLine/PaneBorderRenderer.cs`
- `src/ClaudeTuiLine/PaneTreeRenderer.cs`
- `tests/ClaudeTuiLine.Tests/BorderSuppressionPredicateTests.cs`

The failing test calls **`CommandProvider.ResolveAsync` directly**. It constructs its own
`RenderNoteCollector` and passes it in. It never touches `SizeResolver`, `PaneTreeRenderer`,
`PaneBorderRenderer`, or any pane geometry at all:

```csharp
var item = new PaneItem(null, null, null, null, Id: "maxlines-exceeded",
    Command: new[] { "printf 'a\\nb\\nc\\n'" }, Shell: true, MaxLines: 2);
var notes = new RenderNoteCollector();

var value = await CommandProvider.ResolveAsync(
    item, rawStdinJson: null, cwd: null, cacheDir: Path.GetTempPath(), widthsDir: Path.GetTempPath(),
    surfaceWidth: null, paneWidthEligible: false,
    values: new Dictionary<string, string?>(), unavailableIds: Array.Empty<string>(), notes);

Assert.Equal("a\nb", value.Value);
Assert.Contains(notes.Notes, n => n.Message == "item 'maxlines-exceeded' emitted 3 lines; 2 kept (maxLines)");
```

Two consequences, and both matter:

**Both earlier hypotheses are dead.** The Orchestrator's — that #73b's width reclaim changes whether
`maxLines` fires — was already impossible: `maxLines` is a **line-count** cap applied in
`CommandProvider.cs:167`, before any pane geometry exists, so no width can reach it. Mine — that the
reclaim forces a re-layout that constructs a fresh `RenderNoteCollector` and discards first-pass notes
— is equally dead: **the test owns the collector and hands it in.** Nothing can substitute it.
I am retracting that hypothesis rather than editing it away; it was the right shape for a
render-path test and this is not one.

**So the merge is a coincidence of timing, not a cause.** Something else changed between the run that
passed and the run that failed, and §2 is what I think it is.

*(One residual check, E1(a): confirm `CommandProvider.cs` has no reference to `PaneTreeRenderer` or
`PaneBorderRenderer`. I am near-certain it does not — it is a value provider that runs before layout —
but the "no path" claim above is the load-bearing one and it should be verified, not assumed.)*

## 2. The likely cause — a stale cache entry in the shared system temp directory

The test passes **`cacheDir: Path.GetTempPath()`** with the fixed item id **`"maxlines-exceeded"`**.
That is a process-wide, run-persistent, branch-independent location.

The observed failure shape is the fingerprint of a **cache hit**:

- `Assert.Equal("a\nb", value.Value)` **passed** — the value is correct and truncated.
- `Assert.Contains(...)` failed against an **empty** collection — not a wrong note, *no notes at all*.

The truncation and the note are three lines apart in the same guarded block
(`CommandProvider.cs:167` tests `item.MaxLines is int cap && cap > 0 && lines.Count > cap`;
`:170` emits the note). **A live execution cannot produce the truncated value without also producing
the note.** The only way to get one without the other is for that block never to run — which is what
happens when the value is served from cache. The cached value is *already* `"a\nb"`, because it was
written by an earlier run that did truncate.

That explains every part of the report, including the parts the merge theory cannot:

- Why it is not reproducible from the diff — it depends on filesystem state, not on source.
- Why it appeared at a merge — the merge is simply when the suite got run again, on a machine whose
  temp directory now held an entry written by a previous run.
- Why resetting `main` to `f5a2511` looked like a fix — clearing or aging the cache, or running on a
  different working directory, changes the outcome without changing a line of code.
- Why 1379 of 1380 pass — this is the only assertion in the suite that checks a **note emitted as a
  side effect of the uncached path**, so it is the only one a cache hit can falsify.

**This is a pre-existing latent defect in the test, exposed by the merge, not introduced by it.**
It has presumably been passing on a cold cache since #31 landed.

## 3. If §2 holds, what the fix is — and what it is *not*

**Not** a change to `CommandProvider`. The production behaviour is correct: a cached value should not
re-emit a note about work that was not redone.

**The test must use an isolated cache directory.** `Path.GetTempPath()` is shared mutable state, and a
test that asserts on a side effect of the uncached path cannot use a cache that survives it. Give this
test a unique per-test directory (created and removed by the test) for **both** `cacheDir` and
`widthsDir`.

Two further points for whoever writes that fix:

- **`CommandProviderTests` has this pattern throughout** — `Path.GetTempPath()` appears as the cache
  dir across the file. This test is the one that *notices*, because it is the one asserting on a note.
  The others assert on values, which the cache reproduces faithfully. Fixing only this test leaves the
  same landmine under every future note assertion in the file, so prefer a shared per-test-directory
  helper over a one-line patch here. That is a judgment about cost, not a correctness requirement.
- **The test name is stale and should be corrected while it is open.** It says
  `...AndEmitsPinnedNote`, but nothing in `src/` emits anything called a "pinned" note — the only
  case-insensitive match for `pinned` in the whole of `src/` is a comment in `SyntheticFixture.cs:51`
  about §9.3.1's pinned `Input`, which is unrelated. The assertion is on the `maxLines` note. Rename to
  `...TruncatesAndEmitsNote`. A test whose name describes a mechanism that does not exist is how the
  next reader loses an hour.

## 4. NEEDS-EVIDENCE — E1, before any code is written

Run on `main` @ `f5a2511` (or with #73b merged; §1 says it does not matter, and if it *does* matter
that refutes §1 and is the most valuable possible result):

- **(a)** `grep -n 'PaneTreeRenderer\|PaneBorderRenderer' src/ClaudeTuiLine/CommandProvider.cs`.
  Expected: no matches. A match refutes §1 and this whole diagnosis is reopened.
- **(b)** Read `src/ClaudeTuiLine/CommandProvider.cs` lines ~150-185 and confirm the truncation at
  `:167` and the note at `:170` sit in one guarded block with no early return between them — i.e.
  that truncation without a note is impossible on the live path.
- **(c)** Confirm the cache is consulted before that block, and report where the cache read happens
  relative to `:167`.
- **(d)** The decisive one: `rm -rf` the relevant entries under the system temp dir (or point the test
  at a fresh directory) and run **only**
  `CommandProviderTests.MaxLinesSet_OutputExceedsCap_TruncatesAndEmitsPinnedNote`, twice in a row
  without clearing in between.
  **Expected if §2 is right: first run PASSES, second run FAILS.** That is the whole diagnosis in one
  observation, and it is cheap.

**What each result decides.** (d) reproducing pass-then-fail ⇒ §2 confirmed, fix per §3, and #73b is
cleared to merge with no changes. (d) failing on a cold cache ⇒ §2 is wrong, and (b)/(c) become the
place to look for a second path to the truncated value. (a) matching ⇒ stop and re-dispatch; §1 was
wrong and I need to see the match before ruling anything else.

## 5. What must not change

- **Do not "fix" `CommandProvider` to re-emit the note on a cache hit.** A note that says
  *"emitted 3 lines; 2 kept"* about an execution that did not occur is a false statement, and it would
  make the test pass by making the diagnostic lie.
- **Do not disable or `[Skip]` the test.** It is asserting a real and correct behaviour; the defect is
  in how it is isolated.
- **#73b's own behaviour is not implicated.** Nothing here is a reason to revisit §6.3's reclaim
  ruling. If the Orchestrator was holding #73b on `task-73b` on account of this failure, §1 says that
  hold rests on a false premise — subject to E1(a).

## 6. Confidence

**High that #73b is not the cause** (§1). It is a structural claim from the diff's file list against
the test's call graph, and E1(a) is the only thing that could overturn it.

**Medium-high on the cache hypothesis** (§2). The reasoning is tight — an empty note collection
alongside a correctly truncated value is very hard to produce any other way, and `Path.GetTempPath()`
with a fixed id is exactly the setup that permits it. But I have not read the caching code, only the
grep lines around it, so I do not know for certain that a cache hit bypasses `:167`. That is E1(c) and
E1(d), and I would not write the fix before seeing them.
