# SPEC-47 — the fallback pane must diagnose itself, and the catch-all must report a reason

> **STATUS: REVISION 4. Fix (B) is IMPLEMENTED including §3.3 (branch
> `task-47-fallback-diagnostic`, clean build). Fix (A) is RESPECIFIED (Revision 2). §5's test
> strategy is RULED ON (Revision 3) and AMENDED after measurement (Revision 4).**
>
> **Revision 2 changes, confined to §4 and dependents:** E1 reported that **no literal-only
> `PaneItem` construction exists anywhere**, and that `Format`-with-null-`Item` renders *nothing*
> rather than literal text (`LeafItems.cs:55-64`, `:178-179`). That was E1's explicitly-anticipated
> "(A) needs a different shape" branch. **The diagnostic-item form of (A) is withdrawn** (§4.2), and
> replaced by a structural guard that buys the same insurance without touching the config surface
> (§4.3).
>
> **Revision 3 changes, confined to §5-§6:** the Implementor found that **§5's tests 1/2/3/4/6 have
> no seam to write against** — the throw they need cannot be produced, and the functions under test
> are local functions in `Program.cs`'s top-level statements, unreachable even via
> `InternalsVisibleTo`. **§5.0 rules on the three options**, §5 is restructured into reachable and
> unreachable tiers, and **§5.2 makes the unreachable tier self-closing.** E2 is closed.
>
> **Revision 4 changes, confined to §5.0, §5.2, §7, §8, §9:** E4 measured the option-2 extraction
> and it is mechanical (one call site, zero capture, no signature changes). **Option 2 is adopted**
> — §5.0's cost objection is withdrawn and one of its two grounds is recorded as having been simply
> wrong. **But §5.2 is UNCHANGED: tests 1 and 2 stay struck.** Revision 3's re-dispatch trigger
> asserted that a cheap option 2 would restore them; **that assertion was false and §5.0.1 explains
> why.** Reachability and controllability are different axes, and only the first one moved.

> **Scope.** Issue #47 — `SPEC-V2-FRAMEWORK.md` §2.11.3's claim about the `SafeLoadAll` fallback
> pane is stale. This spec supersedes the framing that produced that staleness and specifies the
> fix. It is a **new mechanism** (`LoadRenderConfig`'s diagnostic channel gets a new producer), not
> a clarification of §2.11.3, which is why it is its own file.
>
> **Out of scope:** §2.11.3's replacement text itself. §7 says what it must and must not claim, but
> the framework edit lands with the implementation, not ahead of it.

---

## 1. The defect, located precisely

### 1.1 What the earlier framing got wrong

The investigation described this as a `Config.cs` problem — a catch-all "inside
`ResolveTopLevel`/`ResolveRootPane`". **It is not in `Config.cs` at all.** Neither
`ResolveTopLevel`, `ResolveRootPane`, `SafeLoadAll`, nor `Load` contains a `catch` block; the only
two in `Config.cs` (`:938`, `:964`) belong to unrelated methods.

The catch-all is **`Program.LoadRenderConfig`, `Program.cs:894-898`**, wrapping the calls to those
two methods. That relocation is what makes this fixable cheaply: the handler is already inside the
function that owns the diagnostic channel, so it does not need to be threaded anywhere.

This mattered enough to state, because a spec written against the `Config.cs` framing would have
put a guard in the wrong file and left the real path untouched.

### 1.2 The mechanism

`LoadRenderConfig` (`Program.cs:856`) returns a 5-tuple whose fourth element is the diagnostic
channel:

```csharp
static (ResolvedConfig TopLevel, Pane RootPane, string? ConfigPath,
        string? UnreadableReason, int UnreadableReasonProtectedLength)
LoadRenderConfig(string? explicitConfigPath)
```

`Main` short-circuits on it at `Program.cs:40-45`: if `unreadableReason is not null`, print one row
and `return 0` — nothing below runs.

There are **three** `BuildFallbackConfig()` call sites, and they do not agree:

| site | condition | reason returned | outcome |
|---|---|---|---|
| `:867-869` | `ParseError` | `ComposeUnreadableReason(result)` | short-circuits, prints the reason |
| `:876-877` | `NoFile` **and** `--config` was asserted | `"no such file"`, protected length `0` | short-circuits, prints the reason |
| **`:896-897`** | **anything thrown by `ResolveTopLevel`/`ResolveRootPane`** | **`null`** | **falls through to the renderer** |

The third row is the defect. With `unreadableReason` null, `Program.cs:40`'s guard does not fire,
the fallback pane reaches the renderer, and `PaneCollapse.Collapse` (`PaneCollapse.cs:21-62`) sees
`Split=None`, `MinSize=null`, `size="auto"`, `Items.Count == 0` → `IsStructurallyEmpty` → `null` →
`Program.cs:177-181` → `Array.Empty<PaneRow>()`.

**Zero rows. No diagnostic. No exit code.** Strictly worse than the unreadable-config path, which
at least prints something.

`BuildFallbackConfig` (`:921-940`) builds the pane with `Array.Empty<PaneItem>()` (`:938`), which
is what makes it structurally empty. `PaneCollapse` is behaving **correctly** — it was handed a
pane with nothing in it. The defect is upstream of collapse, in two independent places, which is
why this spec has two fixes rather than one.

### 1.3 Dormant, not live — and why that is not a reason to leave it

Nothing reachable from a user config value throws inside `ResolveTopLevel` (`Config.cs:579-590`) or
`ResolveRootPane` (`Config.cs:641-657`); everything defaults or null-coalesces. Verified one level
deep.

So the bug cannot fire today. It is gated by an **invariant with no enforcement** — "nothing in
this call graph throws" — which no test asserts, no analyser checks, and no reviewer is prompted to
re-verify when either method grows. The first `throw` or unguarded index added anywhere under those
two methods converts a silent-zero-rows failure from impossible to shipping, with nothing in
between.

**Rejected: document it in §2.11.3 as a known-dormant risk and change no code.** That was one of
the two options originally on the table. It is rejected because §2.11.3 would then describe a
fallback pane that works while the code ships one that does not — the exact class of staleness
issue #47 was filed about. A spec that documents a defect instead of fixing it, in the section
whose staleness *is* the ticket, reproduces the problem in a third form.

> **This section is load-bearing for §5.** §5.0's ruling and §5.2's struck tests both turn on the
> same fact: the throw that cannot happen is also the throw that cannot be *staged*. Read §1.3
> before §5, because the whole testability argument is downstream of it — and note that **nothing
> in Revision 4's adopted refactor changes anything in this section.**

---

## 2. What both fixes have in common

The fallback pane exists to make failure **visible**. A fallback that renders nothing has failed at
the only job it has. Both fixes below restore that property, at different levels:

- **(B), §3 — the catch-all reports a reason.** Makes the third row behave like the first two, so
  all three failure paths converge on the one shipped diagnostic channel. This is the substantive
  fix. **Implemented.**
- **(A), §4 — the fallback pane cannot be produced without a diagnostic.** Makes the coupling
  structural rather than conventional, so the fourth call site cannot reintroduce the defect. This
  is insurance, and §4.1 is honest about what it is worth once (B) lands. **Respecified in
  Revision 2 — the original diagnostic-item form is withdrawn.**

**Rejected: a guard or exemption inside `PaneCollapse`.** Special-casing "the fallback pane"
requires collapse to recognise a pane by identity, which nothing in `Pane` supports and which would
put knowledge of `Program`'s error handling inside the layout engine. Collapse's rule — an empty
pane collapses — is correct and general; do not carve a hole in it to compensate for a caller that
built the wrong pane.

---

## 3. Fix (B) — the catch-all returns a reason

> **Unchanged since Revision 1, and implemented.** Retained in full because the Reviewer validates
> against this spec's current state, and because §4.4 and §5 are written in terms of it.

### 3.1 The change

`Program.cs:894-898`. The bare `catch` discards the exception, so it must bind it:

```csharp
catch (Exception ex)
{
    var (fallbackTopLevel, fallbackPane) = BuildFallbackConfig();
    return (fallbackTopLevel, fallbackPane, configPath, ComposeResolutionFailureReason(ex), 0);
}
```

Add a sibling to `ComposeUnreadableReason` (`:909-919`):

```csharp
static string ComposeResolutionFailureReason(Exception ex)
```

returning a reason string with **protected length `0`**.

**Protected length is 0, not a computed value.** `ComposeUnreadableReason` protects the `"line N"`
prefix (`:917-918`) because that prefix is the one irreplaceable thing — the file position a reader
jumps to (§9.2.2, `:901-908`). A resolution failure has no file position; there is nothing in its
reason that survives truncation better than anything else. `:877`'s `"no such file"` is the
existing precedent for a literal reason at protected length 0.

### 3.2 What the reason string says

**NEEDS-JIM is not required here** — this follows from the existing rung ladder rather than being a
free choice. The reason must:

- **Name the failure class**, so the row is not mistaken for a parse error. It is not one: the
  config parsed fine and then resolution threw.
- **Carry `ex.Message`**, because that is the only content-bearing part. `ComposeUnreadableReason`
  emits .NET's own wording verbatim (`:911`) rather than substituting a house message; follow it.
- **Not carry a stack trace or exception type name.** The output channel is one row, degraded
  under width pressure through five rungs. A stack trace is unshowable and a type name displaces
  message text that is more useful.

Recommended shape, mirroring `:877`'s brevity and `:911`'s verbatim-message rule:

```
config could not be resolved: {ex.Message}
```

### 3.3 The null-`configPath` hazard — the one thing that will break this

**This is the part most likely to be got wrong, and it is a live NullReferenceException, not a
style point.**

`configPath` **can be null** when the catch fires. `Program.cs:880` sets it null on the §9.2.1
row-1 path (no `--config`, nothing at any searched path). Resolution then runs against `config ==
null` and pure defaults — and if *that* throws, the catch is entered with `configPath == null`.

`Program.cs:43` then executes:

```csharp
ConfigUnreadableMessage.Format(configPath!, unreadableReason, ...)
```

The `!` is a null-forgiving operator on a value that is genuinely null. Today that assertion is
sound, because every path that sets a non-null reason also has a non-null `configPath`. **Fix (B)
breaks that coupling** — it is the first producer of a reason on a path where `configPath` may be
null. Ship (B) without handling this and the dormant silent-zero-rows bug is replaced by a dormant
crash, which is not an improvement.

**Ruling: `ConfigUnreadableMessage.Format` must accept a null path**, and render the path-less form
when given one. Do not add a placeholder string, do not synthesise a path, and do not skip the
diagnostic when the path is null.

The reason this is the right shape rather than an imposition: **the ladder already has a path-less
rung.** Rung 3 drops the path entirely under width pressure, so a path-less rendering is already
specified, already implemented, and already shipped. A null path should enter the ladder at that
rung instead of the top. That reuses tested behaviour rather than inventing a sixth rung.

> **IMPLEMENTED as specified.** `Format`'s path parameter is now `string?`, entering rung 3 directly
> when null, and the `!` at `Program.cs:43` is removed. E2 is closed (§6).

### 3.4 Why not rename `UnreadableReason`

The field's name and the §9.2.1 comment at `:852-855` both say *unreadable*, and (B) puts a
resolution failure through it. Renaming to something like `DiagnosticReason` would be more honest.

**Do not rename it in this change.** Three reasons: the name appears in shipped §9.2.1 vocabulary,
so the rename is a framework edit with its own review surface; tuple element names are advisory in
C#, so the rename is *not* compile-enforced and a partial rename would leave two names for one
thing; and the change is cosmetic while §3.1 is behavioural — bundling them makes the behavioural
change harder to review. Note it as follow-up work in the implementation report; do not do it here.

**Revision 4 note.** §5.0's adopted extraction introduces a *new type name* (`ConfigResolution`),
which is new vocabulary rather than a rename of shipped vocabulary. It does not conflict with this
ruling, and it does not license doing the rename here. `UnreadableReason` stays as it is.

---

## 4. Fix (A) — the fallback pane cannot be produced without a diagnostic

> **RESPECIFIED IN REVISION 2.** §4.2-§4.4 replace the original §4.2-§4.4, which specified a
> literal-text diagnostic `PaneItem`. E1 disproved that shape's premise.

### 4.1 What this is worth, stated honestly

Once (B) lands, **all three `BuildFallbackConfig` call sites return a non-null reason, so
`Program.cs:40` short-circuits on every one of them and the fallback pane is never rendered at
all.** Fix (A) therefore guards a code path that is provably unreachable at the moment it ships.

That is not an argument against it, but it is the honest framing, and the implementation report
should not claim (A) fixes an observable bug — it does not, once (B) is in.

What (A) buys: `BuildFallbackConfig` returns a `Pane` through a signature that promises nothing
about whether the caller will render it. The fourth call site someone adds — plausibly for a new
failure mode, plausibly without noticing the reason-must-be-non-null coupling — gets zero rows and
no diagnostic, exactly as `:896` does today.

**Revision 2 sharpens what "buys" means.** The thing worth removing is not the empty `Items` list;
it is the **unenforced coupling** between "you called `BuildFallbackConfig`" and "you must also
return a non-null reason." That coupling lives in a reviewer's head. §4.3 moves it into the type
system, which is both cheaper and stronger than what Revision 1 proposed.

### 4.2 E1's answer, and what it rules out

**E1: there is no literal-text `PaneItem`, and there is no way to make one.**

- The only production construction of `PaneItem` is `Config.cs:830-846`, built straight from parsed
  JSON. Nothing anywhere constructs a literal-only item.
- The inferred carrier does not work. `LeafItems.ResolveDisplay` (`:55-64`) and `ApplyFormat`
  (`:178-179`): with `Item` null, `value` is null, and
  `if (item.Format is not null) return value is null ? null : ...` returns **null**. So
  `Format`-with-null-`Item` renders **nothing** — the precise opposite of what Revision 1's §4.3
  required, and exactly the "renders as empty" failure that section warned the guess could produce.

E1's stated escape hatch fired as written: *"If no literal-only construction exists anywhere →
report that, because it means a pane item may always require a resolvable `Item`, and (A) needs a
different shape."* It does, and this is that shape.

**Withdrawn: the diagnostic `PaneItem`.** Not deferred, not gated on further evidence — withdrawn,
because the two ways to build it are both worse than the problem:

1. **A new `PaneItem` member** (`Text`/`Literal`). `PaneItem` is a *config-shaped* record
   deserialized from user JSON at `Config.cs:830-846`. A new member is a new config key by
   construction — it needs a `--check` disposition, a `--schema` entry, a README row, and an
   `unknown-key` interaction. **That is a permanent config-surface expansion to serve a provably
   unreachable path.** The cost is not the field; it is that the field is now part of the public
   config contract forever.
2. **A new registry item kind** whose value is a fixed diagnostic string. No config-surface *shape*
   change, but strictly worse in one respect: registry ids are globally nameable, so the id becomes
   something a user can write in their own config, and it would resolve. A diagnostic that renders
   in a healthy config is a worse defect than the one being guarded against.

Both trade a real, permanent surface for insurance on a path §4.1 already establishes is
unreachable. That is the wrong side of the trade.

### 4.3 What replaces it: make the diagnostic non-optional at the type level

**Ruling: the fallback pane keeps `Array.Empty<PaneItem>()` and changes not at all. Instead, make
it impossible to obtain a fallback pane without also producing a reason.**

Introduce one helper that returns the whole 5-tuple, and make `BuildFallbackConfig` private to it:

```csharp
static (ResolvedConfig TopLevel, Pane RootPane, string? ConfigPath,
        string? UnreadableReason, int UnreadableReasonProtectedLength)
FallbackResult(string? configPath, string reason, int protectedLength)
```

- `reason` is **non-nullable and has no default.** This is the whole mechanism.
- The three existing sites become `return FallbackResult(configPath, <their reason>, <their length>)`
  — `:867-869`, `:876-877`, and (B)'s `:896-897`.
- `BuildFallbackConfig` is called only from inside `FallbackResult`. The point is that no new code
  can reach the pane without going through the reason parameter.

**Revision 4:** `FallbackResult` and `BuildFallbackConfig` both live in the new `ConfigResolution`
class (§5.0.2). `BuildFallbackConfig` becomes `private static` there — under Revision 3's
local-function arrangement its inaccessibility was incidental; now it is declared. That is a small
genuine gain, and it is the only thing §5.0's refactor changes about §4.

**Why this is the right form.** The defect at `:896` was never "the pane is empty" — it was "a
caller produced a fallback and forgot the reason." An empty pane is the *symptom*; the missing
reason is the *cause*, and it is the missing reason that suppresses the diagnostic. A required
parameter makes the cause unrepresentable. The fourth call site cannot compile without deciding
what to say, which is precisely the decision `:896` failed to make.

This is the same lesson as SPEC-87 §12.6.4 and SPEC-88's §2.4 finding, in a third setting:
**a default value converts a compile error into a silent behavioural gap**, and the whole value of
(A) is refusing to let a future author skip a decision. Note the direct consequence for Revision 1's
§4.2, which proposed `BuildFallbackConfig(string? message = null)` — that signature has exactly the
optional-with-a-default shape this rules against, and would have left the fourth call site free to
omit the message, guarding nothing.

### 4.4 What this deliberately does not do

- **It does not make the fallback pane render anything.** It cannot; E1 established there is no
  mechanism. If a future change makes the fallback pane reachable *with* a reason — which would
  itself be a §7 violation of `Program.cs:40-45`'s short-circuit contract — the pane still renders
  zero rows. **That is accepted**, because the reason is printed on the row above and the pane's
  content would be a second copy of it.
- **It does not add a `PaneItem`, a config key, a registry id, or a schema entry.** §7 pins this.
- **It does not change `BuildFallbackConfig`'s output.** The `ResolvedConfig` at `:923-929` and the
  `Pane` at `:930-938` are byte-identical to `main`.

If someone later has an independent reason to introduce literal pane content — a real feature, with
its own config-surface justification — then §4.2's option 1 becomes available and (A) could be
revisited. **Do not introduce it for #47's sake.**

---

## 5. Verification

### 5.0 The testability ruling

The Implementor reported that **most of Revision 2's §5 cannot be written.** Tests 1/2/3/4/6 all
require forcing `ResolveTopLevel`/`ResolveRootPane` to throw, and:

- §1.3 already established **no real `UserConfig` value can trigger that throw**;
- there is **no seam to fake it**. `LoadRenderConfig`, `ComposeResolutionFailureReason`,
  `ComposeUnreadableReason`, and `BuildFallbackConfig` are **local functions inside `Program.cs`'s
  top-level-statements file** — implicitly private, and `InternalsVisibleTo` cannot reach them;
- the only existing coverage of this area (`PreviewCliTests.cs`) **shells out to the built CLI as a
  subprocess**, which has exactly the same problem from the outside.

That is a correct and well-scoped finding, and the Implementor was right to stop rather than pick.
Three options were offered.

#### 5.0.0 Option 1 — fault-injection hook in `ResolveTopLevel`/`ResolveRootPane`: REJECTED

Not weighed against the others; rejected outright.

It is production code whose only purpose is to make production code misbehave, sited in the
config-resolution path — **the exact path whose reliability is this ticket's subject.** And it is
self-defeating whichever way it is built:

- Guarded by `#if DEBUG` or a test-only flag → the tests no longer exercise the shipped artifact,
  which was the entire justification for preferring an end-to-end test over a unit test.
- Present in release → **#47's own fix has added the first thing under those two methods that can
  throw.** §1.3's invariant ("nothing in this call graph throws") is what makes the bug dormant;
  a spec that destroys that invariant in the course of testing the consequence of losing it has
  argued itself in a circle.

Do not implement this, and do not implement a narrower variant of it (an injectable delegate, a
virtual method, a static hook). **The objection is to the category, not to the visibility.**

> **Revision 4 — this is now MORE tempting, and the answer is unchanged.** With `ConfigResolution`
> internal (§5.0.2), a resolver-injection parameter on `LoadRenderConfig` looks harmless: "it is
> internal already, so an internal seam costs nothing." It is not harmless and it is not a
> different proposal — threading an injectable resolver into `LoadRenderConfig` so a test can make
> it throw **is** option 1, and it destroys §1.3's invariant in exactly the same way. Visibility
> was never the objection. Anyone reaching for this after the refactor should read this note as
> addressed to them specifically.

#### 5.0.1 Option 2 — extract into an internal class: **ADOPTED (Revision 4), but it does not do what Revision 3 said it would**

Revision 3 deferred this on two grounds and set a re-dispatch trigger. E4 measured it (§6), and the
result requires splitting the verdict, because **the trigger I wrote bundled two claims that do not
travel together.**

**Ground 1 — cost — is withdrawn.** E4 measured the move as mechanical: zero outer-scope capture in
any of the four functions, exactly **one** call site touched (`Program.cs:35`), the three
inter-function calls moving together untouched, no generics, no `ref`/`out`, no signature changes.
That is not "refactoring the program entry point"; it is a relocation with a namespace prefix. The
blast-radius objection does not survive the measurement.

**Ground 2 — that moving the code would undermine test 5's byte-identical comparison against
`main` — was simply wrong, and is withdrawn as an error rather than as a reconsideration.** Test 5
compares **rendered output**, captured by shelling out to the built CLI. Where the functions live is
invisible to it. Revision 3 conflated "the code under test moved" with "the assertion's basis
moved"; the basis is bytes, and bytes do not care about file layout. Recorded rather than deleted,
because the confusion is an easy one to repeat.

**But the trigger's second clause does not follow, and this is the substantive Revision 4 finding.**
Revision 3 §9 said: *"if the move is small and mechanical, then it is worth doing now **and §5.2's
struck tests come back**."* The first half is now established. **The second half is false.**

Option 2 makes `LoadRenderConfig` **reachable** from a test. Tests 1 and 2 require
`ResolveTopLevel`/`ResolveRootPane` to **throw**. Those are two different axes:

- **Reachability** — can a test call the function under test? Option 2 fixes this.
- **Controllability** — can a test drive its dependency into the state under test? **Option 2 does
  not touch this at all.**

`LoadRenderConfig` calls `Config.ResolveTopLevel` directly. Moving the caller into an internal
class does nothing whatsoever to make the callee throw, and §1.3's finding — that no reachable
`UserConfig` value triggers one — is untouched by where the caller is declared. The only thing that
would close the controllability gap is injecting the resolver, **which is option 1**, rejected in
§5.0.0 and re-rejected in its Revision 4 note precisely because this refactor makes it look
cheaper.

**So the ruling splits: adopt option 2 on its own structural merits; §5.2 is unchanged.**

#### 5.0.2 What to implement

Move all four functions —

```
LoadRenderConfig(string?)                      -> the 5-tuple
FallbackResult(string?, string, int)           -> the 5-tuple             (§4.3, new)
BuildFallbackConfig()                          -> (ResolvedConfig, Pane)  [private]
ComposeUnreadableReason(ConfigReadResult)      -> (string, int)
ComposeResolutionFailureReason(Exception)      -> string                  (§3.1, new)
```

— out of `Program.cs`'s top-level statements into `internal static class ConfigResolution`,
reachable from tests via `InternalsVisibleTo`. `Program.cs:35` becomes
`ConfigResolution.LoadRenderConfig(explicitConfigPath)`. **No signature changes to any of them.**

**The refactor and the behavioural change should be separable in review.** The behavioural change
is four lines; the relocation is mechanical but large in diff. Land them as two commits if the
branch history allows it — relocation first, behaviour second — so the Reviewer can confirm the
relocation is a pure move by inspection rather than reading it interleaved with §3 and §4. If the
branch is already past that point, say so in the report and do not rewrite history for it.

**Do not let the scope creep.** Four functions plus §4.3's new helper, named above. Nothing else
moves out of `Program.cs` under this ticket, and no signature acquires a parameter it did not have.
§7 pins both.

#### 5.0.3 Option 3 — narrower coverage: superseded, with one correction preserved

Option 3 is superseded by the adoption of option 2, which gives everything option 3 gave and more.
One finding from Revision 3's analysis is preserved, because it was a real error in the option as
offered and it would have surfaced mid-implementation:

**Option 3 as stated did not work.** It proposed unit-testing `ComposeResolutionFailureReason` in
isolation — but under Revision 3's arrangement that was a local function in top-level statements,
unreachable for exactly the reason everything else was. Taken literally it delivered only the
`Format` tests and silently dropped test 3, the newline case, which guards the one defect here that
a real exception message can cause today. Moot now, and worth remembering: an option that names a
function as its unit-under-test has to be checked against whether that function is reachable.

### 5.1 The reachable tier — write all of these

3. **One row, never more.** `ComposeResolutionFailureReason` given an exception whose `Message`
   contains `\n` produces a reason with no newline in it. **Direct unit test** on
   `ConfigResolution.ComposeResolutionFailureReason`. This is the highest-value test in the spec:
   it guards a defect a real exception message can cause. **If it fails, the fix is to strip or
   replace newlines in `ComposeResolutionFailureReason`, not to change the output channel.**
4. **Null path enters rung 3 (§3.3).** `ConfigUnreadableMessage.Format(null, reason, width, 0)`
   returns the path-less rendering, throws nothing, and contains no placeholder where a path would
   be. Unit test on `Format`. This is where §3.3's hazard actually lives — the
   `NullReferenceException` was a property of `Format`'s contract, not of the catch block — so
   testing it here is the right level, not a downgrade.
5. **The two existing paths are byte-identical.** For a parse error (`:867`) and an asserted
   missing file (`:876`), assert the rendered row is byte-for-byte identical to `main`'s.
   **This test stays a subprocess test in `PreviewCliTests.cs` — do not convert it to a unit test
   now that `LoadRenderConfig` is reachable.** Its entire purpose is to pin end-to-end rendered
   output across the refactor; calling the extracted function directly would test the relocated
   code instead of the shipped pipeline, and would silently drop exactly the regression class the
   relocation could introduce. §9.2.2's line-number protection is shipped behaviour and this spec
   must not perturb it.
6. **Width degradation for the new reason.** Feed a resolution-failure-shaped reason string to
   `Format` at the widths exercising each of the five rungs. Assert it degrades rather than
   crashing or emitting an empty row, and that at the tightest width it still emits the bare
   `claude-tui-line` form. Unit test on `Format`; the reason string is an input, so no throw needs
   staging.
7. **The two reachable failure paths produce a non-null reason (§4.3).** Now writable as a direct
   unit test on `ConfigResolution.LoadRenderConfig` rather than only through the CLI: assert a
   non-null `UnreadableReason` and the expected protected length for a parse error and for an
   asserted missing file. **The third (resolution throw) remains in §5.2** — reachability changed,
   controllability did not.
8. **`BuildFallbackConfig`'s output is unchanged (§4.4).** Now a real test rather than a
   code-review item: assert via `FallbackResult` that the returned `ResolvedConfig` and `Pane`
   match `main`'s, including `Items.Count == 0`. **This is the one test Revision 3 struck that the
   refactor genuinely restores.**
9. `tools/check-all.sh` passes.

### 5.2 The unreachable tier — UNCHANGED BY REVISION 4

Tests 1 and 2 from Revision 2 remain **struck**. §5.0.1 is the argument: option 2 bought
reachability, and these two need controllability.

- ~~1. Force the throw, assert zero rows on `main` then one row on the fix.~~
- ~~2. Exit code `0` on the same scenario.~~

**Why the gap is acceptable — this is the load-bearing argument, and the refactor does not affect
it.** The behaviour that cannot be tested is exactly the behaviour that cannot occur: both are
blocked by the same fact, that nothing under those two methods throws. An end-to-end test here
would exercise a path production cannot reach either. And the two re-open **together** — the first
person to add a `throw` makes the path live and stageable in the same commit. The coverage gap does
not lead the risk window.

That only holds if someone notices at that moment, so **three requirements replace the struck
tests, and the third is the one that matters:**

1. **The implementation report must state the gap explicitly** — that the catch-all is verified by
   inspection only, and name the four lines at their new home in `ConfigResolution`. A spec whose
   test list quietly shrinks reads afterwards as if it were fully covered. **Do not let the adopted
   refactor blur this**: a report that leads with "extracted for testability" and omits the gap
   will read as though the seam problem was solved outright, and it was solved only halfway.
2. **The catch block gets a comment citing this section** — not change narration, but the thing a
   comment is legitimately for: a non-obvious constraint a future reader cannot infer. Wording
   along the lines of *"unreachable today (SPEC-47 §1.3); if anything below `ResolveTopLevel`/
   `ResolveRootPane` gains a `throw`, SPEC-47 §5.2 requires the end-to-end test that becomes
   writable at that point."*
3. **The gap is self-closing, and the comment is the mechanism.** The first change that adds a
   `throw` under those two methods simultaneously makes the path live and makes it stageable. That
   commit — not this one — is when tests 1 and 2 get written, and the comment is what tells its
   author so. **This is the entire reason the gap is acceptable**; without the comment it is just
   missing coverage with a rationalisation attached.

**The structural half of (A) is still not testable and is still a code-review item.** "No caller can
obtain a fallback pane without supplying a reason" is a property of the signature, and a test
asserting it would have to fail to compile. Call it out explicitly in the implementation report so
the Reviewer checks the signature. A reflection-based test asserting `reason` is non-nullable would
be theatre — it would pass on a signature with a default value, which is exactly the failure mode
§4.3 exists to prevent.

---

## 6. NEEDS-EVIDENCE

- **E1 — how is a literal-text `PaneItem` constructed?** ✅ **ANSWERED, and it invalidated §4's
  original shape.** There is no literal-only construction anywhere; the only production
  construction is `Config.cs:830-846` from parsed JSON; and the inferred `Format`-with-null-`Item`
  carrier renders **nothing** (`LeafItems.cs:55-64`, `:178-179` — `Item` null → `value` null →
  `ApplyFormat` returns null). §4.2 records this and §4.3 is the replacement design. **The
  Medium-confidence guess was wrong, and the gate is why it cost nothing.**

- **E2 — can `ConfigUnreadableMessage.Format` take a null path, entering at the path-less rung?**
  ✅ **ANSWERED, and it resolved on the clean branch.** `Format`'s path parameter is now `string?`
  and enters rung 3 directly when null; the `!` at `Program.cs:43` is removed. §3.3's ruling was
  implementable as specified, and rung 3 was cleanly reachable as an entry point rather than only
  as a width-driven fallthrough — the good branch of E2's two. **Test 4 (§5.1) is the proof and
  must be reported as passing explicitly**; a clean build does not demonstrate it, because the
  hazard was a runtime dereference rather than a compile error.

- **E3 — is there a test seam for `Program.cs`'s local functions?** ✅ **ANSWERED and ruled on.**
  There is none: top-level-statement local functions are implicitly private and
  `InternalsVisibleTo` cannot reach them, and the existing CLI coverage shells out to a subprocess.
  Recorded as a NEEDS-EVIDENCE item retroactively because it is a fact about the codebase the next
  spec touching `Program.cs` will need, and it deserves to be findable.

- **E4 — what does the option-2 extraction actually cost?** ✅ **ANSWERED, and it changed the
  ruling.** Zero outer-scope capture in all four functions (each touches only its own
  params/locals and imported statics — `ConfigLoader.*`, `ColorResolution.*`, `BoxBorder.*`); one
  call site (`Program.cs:35`); the three inter-function calls move together untouched; no generics,
  no `ref`/`out`, no signature changes. §5.0.1 adopts option 2 on this basis — **and separately
  records that the measurement does *not* restore §5.2's struck tests, contrary to what the
  question's own re-dispatch trigger asserted.**

---

## 7. What must NOT change

- **`Program.cs:40-45`'s short-circuit contract** — one row, `return 0`, nothing below it runs.
  §9.2.1's *"the render path's only output channel is this one row"* (`:37-39`) is the rule this
  spec extends a third producer onto, not one it relaxes.
- **`ComposeUnreadableReason`'s output** — its reason text, its `"line N"` protected length, and
  §9.2.2's rationale at `:901-908`. §5.0.2 **moves** this function; it must not change what it
  returns for any input. Test 5 pins this from the outside.
- **The five-rung degradation ladder** in `ConfigUnreadableMessage`, and its behaviour for the two
  existing reason producers. E2's null-path **entry point** is added; no rung is reordered,
  removed, or reworded. Test 5 pins this.
- **`PaneCollapse.Collapse`'s empty-pane rule** (`PaneCollapse.cs:21-62`). Correct and general —
  §2 rejects carving an exemption into it.
- **Exit code `0`** on every fallback path.
- **`BuildFallbackConfig`'s output.** The `ResolvedConfig` at `:923-929` and the `Pane` at
  `:930-938` are unchanged — §4.4 and test 8. Revision 1 permitted a message-carrying variant;
  Revision 2 withdraws that permission.
- **`PaneItem` (`Pane.cs:230`).** **No new member.** §4.2 rules out `Text`/`Literal`, and adding one
  for #47's sake is a config-surface expansion this spec explicitly rejects. Likewise no new
  `ItemRegistry` id.
- **`ResolveTopLevel` and `ResolveRootPane` (`Config.cs:579-590`, `:641-657`).** **No fault-injection
  hook, no injectable delegate, no virtual method, no test-only flag** — §5.0.0. These two methods
  not throwing is the invariant that makes this bug dormant, and **§5.0.1's adoption of the
  extraction does not soften this by one inch**: an injectable resolver on the newly-internal
  `LoadRenderConfig` is the same rejected proposal wearing the refactor as cover.
- **The scope of §5.0.2's extraction.** Exactly the functions named there move, with **no signature
  changes**. Nothing else leaves `Program.cs` under this ticket. A pure move is reviewable by
  inspection; a move that quietly adds a parameter is not.
- **Test 5 stays a subprocess test.** §5.1 item 5. Converting it to a unit test now that
  `LoadRenderConfig` is reachable would drop the exact regression class the relocation could
  introduce.
- **The `:876` `"no such file"` reason and its protected length `0`.** Untouched.
- **`SPEC-V2-FRAMEWORK.md` §2.11.3's replacement wording is not free-form.** It must describe
  **both** paths — short-circuited-before-collapse and, after (B), also-short-circuited — and must
  **not** state or imply that any single mechanism universally protects the fallback pane. Naming
  one mechanism as universal is precisely the staleness #47 was filed about; replacing it with a
  differently-scoped universal claim reproduces the defect. If the wording cannot be made accurate
  without enumerating the call sites, enumerate them.
  The wording must also **not** claim the fallback pane renders a diagnostic. It does not and
  cannot (§4.4). It is protected by never being reached, which is a different claim and the only
  true one.

---

## 8. Incidental findings — NOT part of this task

**(1) ~~`Program.cs`'s top-level statements are a testability dead zone.~~ RESOLVED WITHIN THIS
TICKET (Revision 4).** E4's measurement made the extraction cheap enough to adopt in scope; §5.0.2
specifies it. **Resolved only for the config-loading path, and only for reachability** — anything
else still living in `Program.cs`'s top-level statements has the same problem, and §5.0.1's
reachability/controllability distinction is the thing to carry forward when the next spec hits this
wall.

**(2) The bare `catch` at `Program.cs:894` may be swallowing types it should not** —
`OutOfMemoryException`, `StackOverflowException`, cancellation. A real question about the handler
this spec edits, but a separate change with a separate blast radius, and #47 is deliberately not
widened to include it. Worth its own ticket.

---

## 9. Confidence, and what I did not verify

**High** on §1's diagnosis. The three call sites, the null-reason third row, and the collapse
consequence are read directly from `Program.cs:856-940` and confirmed against the independent
`PaneCollapse` trace.

**High** on §3, now implemented including §3.3. E2 came back on its good branch.

**High** on §4.3. It rests on a signature property rather than on any inference about rendering, so
E1's class of error cannot recur here. The residual risk is that someone later adds a default value
to `reason` for convenience, which §7 and §5.2's code-review note are written to prevent.

**High** on §5.0.0's rejection of option 1, which is a categorical argument rather than a judgment
call: the hook either does not test the shipped artifact or destroys §1.3's invariant, and there is
no third configuration. Revision 4 raises rather than lowers the confidence here, because the
adopted refactor makes the tempting variant more visible and it still fails the same way.

**High** on §5.0.1's adoption of option 2 given E4's numbers. One call site and zero capture is
about as close to free as a structural change gets.

**High** on §5.0.1's finding that option 2 does not restore tests 1 and 2. This is a
reachability-versus-controllability distinction, not a judgment call: `LoadRenderConfig` calls
`Config.ResolveTopLevel` directly, and no change to the caller's declaration site affects whether
the callee throws.

**What I got wrong in Revision 3, recorded rather than quietly amended.** Two things. First, the
claim that moving the code would undermine test 5 — false; test 5 compares rendered bytes and does
not care where the code lives. Second, and worse, the re-dispatch trigger in Revision 3 §9 bundled
"the move is cheap" with "the struck tests come back" as though the second followed from the first.
It does not. A trigger that bundles an empirical question with a conclusion invites exactly what
happened here: the measurement came back and both halves were read as settled. **The lesson is
about how to write a NEEDS-EVIDENCE item** — state what each possible result decides, and do not
smuggle a second inference into the decision clause.

**Revision 1's §4.3 was Medium-confidence on an unverified inference, and it was wrong.** Recorded
rather than deleted, because the gate is the reusable lesson: the guess was flagged as a guess,
marked NEEDS-EVIDENCE, and the Implementor stopped at it instead of building on it. That is the
mechanism working, and the cost of the error was one dispatch.

**Low stakes throughout.** Every path here is already a failure path; the worst outcome of a wrong
call is a differently-worded diagnostic on a path that today prints nothing.

**No Ultra-Advisor escalation recommended.** No security, concurrency, migration, or
public-interface surface. Two reversibility concerns, both declined: §3.4's rename, and §4.2's new
`PaneItem` member — the latter being the one genuinely hard-to-reverse thing #47 could have
produced. The adopted extraction is internal and trivially reversible.

**One thing I did not verify:** whether `ex.Message` can contain a newline in practice. Test 3
covers it either way.
