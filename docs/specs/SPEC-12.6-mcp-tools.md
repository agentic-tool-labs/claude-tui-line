# §12.6 — MCP tools: transport shape, the first slice, and the checkpoint write

Task #10 (Phase 7). Written against `/Users/jimcline/git/repos/claude-tui-line`,
from §12.6.5, §12.6.6, §12.6.11, §12.6.12, §12.2, §12.2.1 and `docs/backup-ledger.md` verbatim.

*(Baseline: the shas pinned in earlier revisions of this file record what I read, not a required
checkout. Do not check anything out on their account.)*

## Amendment history

- **A1 — THE FIRST SLICE WAS NARROWED; `set_config` REMOVED.** Superseded by A2.
- **A2 — JIM'S RULING: `set_config` IS BACK IN SCOPE.** `dispatch-10-ruling.md` overrules A1's scope
  cut. **Section 7.2's ruling is superseded by §9**, which specifies the checkpoint mechanism concretely.
  **§8's Ultra-Advisor recommendation is WITHDRAWN — do not route this to Ultra-Advisor**, per the
  ruling's *"it should not gate #10 on an Ultra-Advisor pass."* A1's §7.1 (library and project
  layout) is **unchanged and still in force**. Section 7.3's "get_config alone" is dead except for one
  sentence promoted into §2.2. **Section 7.4's V1-V5 are superseded by §10**, which is now the single
  verification list.

  A2 also records the finding that **dissolves A1's second objection outright**:
  **`docs/backup-ledger.md` exists, is 262 lines, and specifies the ledger format completely** — entry
  shape, field names, both `kind` values, hashing, filenames, the 10-step procedure and the four
  rules. **#10 does not choose a format; it implements one that was written down before this task
  existed.** A1's *"decide once, free exactly once"* concern was mine to raise and is now answered by
  evidence rather than by ruling.

---

Two forks were put to me. Both are ruled below. §12.6.6:29-31 is explicit that the first one is
**open in the spec** — *"Whether the server spawns the CLI or links the core is left open by
§12.6"* — so §1 is my ruling, not a reading of an existing one, and it is labelled accordingly.

---

## 1. Fork 1 — the server SPAWNS the CLI. It does not link the core as a library.

**Ruled: spawn.** Three arguments, in ascending order of how much I weight them. **Unaffected by A2.**

### 1.1 One implementation per behaviour

The MCP server is a **harness**. A harness owns its scenario — which tool is exposed, how arguments
map, how errors are shaped — and never a second copy of the behaviour it drives. Linking the core
gives the server its own path into config validation and rendering, and the moment two entry points
exist they drift. This is the weakest of the three arguments because discipline could hold the line,
but it is the one that generalises.

### 1.2 The concurrency contract is inherently cross-process, so linking buys nothing where it counts

§12.6.5's opening sentence names the three writers: *"an MCP call, a slash command, and a hand edit
in an editor."* Two of those three are other processes. In-process locking or an in-process
compare-and-swap is therefore a **false guarantee** — it serialises the one writer that was never
the problem and is blind to the two that were.

Linking's headline advantage is shared in-process state. That advantage is worthless for the single
hardest part of this feature. CAS has to be built on the file's bytes (§12.6.5: *"a hash of the
file's bytes as read"*) precisely because the bytes are the only thing all three writers share.

### 1.3 The decisive one — linking puts a forbidden capability within reach, and breaks a fail-safe

**(a) §12.6.12 rule 3 forbids the server merging, and spawn makes it structurally impossible.**

> **The server must not attempt the merge itself.** It is the actor without the intent … That is
> §12.6.11's failure with an extra step and better paperwork.

A linked server *can* see both configs, diff them, and infer intent. It is specified never to — but
the capability sits there, one helpful refactor away, and §12.6.12 explains why exercising it is
worse than the bug it appears to fix (*"silent and attested"*). A spawning server is architecturally
the thing that cannot merge: it hands bytes to a subprocess and hands the result back. **Prefer the
shape that makes the forbidden thing impossible over the shape that merely prohibits it.**

**(b) Linking lets every MCP tool succeed on a machine where the statusline cannot render.**

The statusline is produced by the CLI. §12.6.6 says a linked server makes `cli-not-found` unable to
arise and *"the tool simply works."* That is true and it is the problem: `set_config` would validate,
write, and report success on a machine with no CLI installed — and the user would get a blank
statusline with every tool reporting green. The check that would have caught it cannot fire.

This is a **fail-open seam**, the same defect class as a cap on a quantity that cannot exceed its
bound. §12.6.6's own instruction — *"A model receiving `cli-not-found` should point the user at
`/claude-tui-line:setup`"* — only ever executes under spawn.

**Under spawn, a successful MCP call is evidence the statusline actually works.** Under linking it is
evidence of nothing. That is a correctness property, not a style preference, and it decides the fork.

### 1.4 The line between "harness" and "behaviour" — read this before implementing

Spawn does **not** mean shelling out for everything.

- **The server does its own file I/O.** Reading the config file and hashing its bytes for `revision`
  is not behaviour — there is nothing to duplicate and no second implementation to drift. §12.6.5
  defines `revision` as a hash of the file's bytes, which the server can compute directly.
  **A2 extends this to the checkpoint: the artifact copies and their SHA-256 hashes (§9) are also the
  server's own file I/O.** They are not CLI behaviour and there is nothing to shell out to.
- **The server spawns the CLI for validation and preview.** `--check` and the render path are
  behaviour, they live in one place, and the server must not reimplement or link them.

Getting this line wrong in either direction is costly: shelling out to read a file is pointless
ceremony, and validating in-process is the duplication §1.1 forbids.

---

## 2. Fork 2 — the CAS contract, and what ships in the first slice

**A2 amends this section's scope line.** The CAS contract below is unchanged. What A1 changed —
deferring `set_config` — is reverted: **the first slice is `get_config` AND `set_config`**, per
`dispatch-10-ruling.md`. §9 supplies the checkpoint mechanism that A1 said was missing.

### 2.1 Why the CAS contract cannot be cut smaller

The obvious decomposition — ship `set_config` now, add the rich refusal later — **creates the exact
defect §12.6.12 exists to prevent, during the window between the two.** §12.6.12:92-96:

> A bare `{ "code": "stale-revision" }` leaves the model one affordance: call `get_config`, take the
> new revision, resend the config it already built. That is not a caller behaving badly — **it is the
> only move the response makes available**, and it is precisely the clobber §12.6.11 names.

A refusal without a payload does not merely fail to help; it *manufactures* the naive retry. And per
§12.6.11:69-74, that retry **succeeds**, so the mechanism logs a clean compare-and-swap over a write
that destroyed someone's work — *"silent and attested."* An interim version is worse than no version,
because it produces evidence of its own correctness.

**A1's correction, retained.** I originally wrote that `get_config` and `set_config` were "one
indivisible slice." Too strong. What the argument establishes is that **`set_config` must not ship
with a partial CAS contract** — nothing about `get_config`. Both ship together under A2, so the point
is moot in practice, but the correction stands: the indivisible unit is `set_config` + the complete
CAS contract.

### 2.2 The contract

**`get_config`** returns the config and a `revision` — a hash of the file's bytes as read. When no
config file exists it returns `revision: "absent"` (§12.6.9), **not** an error and not a missing
field. §12.6.5:16-19 is explicit that the fresh-machine case is *"the moment two agents are most
likely to act on the same file"*, and it previously had no compare-and-swap at all. `"absent"` gives
a first write a value to send.

**`set_config`** takes an optional `baseRevision`. Supplied and no longer matching ⇒ refuse with
`code: "stale-revision"`. Optional, not required, so a first write needs no ceremony — but note
`"absent"` means "optional" now covers far less ground than it used to.

**The `stale-revision` payload carries the current config AND the current revision** (§12.6.12
rule 1). Not a code and a sentence. This is the clause that turns the refusal into the second read:

> The refusal stops being a wall and becomes the second read.

**The tool description states the compare-and-branch rule** (§12.6.12 rule 2). This is not
belt-and-braces with the payload — they do different jobs, and §12.6.12:107-109 says so directly:
*"the description is what makes the first call careful, the payload is what makes the retry correct,
and neither substitutes for the other."* Nothing loads `commands/edit.md` when a model calls
`set_config`, so on this path **the description is the procedure**.

**The server never merges** (§12.6.12 rule 3). It hands back enough for the actor holding the intent
to redo the work, and stops.

**Every tool fails with `cli-not-found` when the CLI is absent, naming the paths searched**
(§12.6.6). It **never** falls back to a remembered item list — §12.1's rule does not relax by
changing transport.

**This includes `get_config`, and that is not optional** (promoted from A1's section 7.3, which is otherwise
dead). A read-only tool has no strict need to spawn anything — but if `get_config` skips the CLI
check, the first machine without a CLI gets a happily-served config and a blank statusline, which is
section 1.3(b)'s fail-open seam arriving through the back door.

### 2.3 The asymmetry that must not be flattened

§12.6.12:117-121 is the sentence most likely to be lost in implementation:

> In `/edit` the model must not proceed **when the two reads differ**. Over MCP it must not proceed
> **when the write is refused** — it has no two reads to compare, and the refusal is its only signal
> that there was anything to compare.

So **§12.6.11's rule 2 ("say so" — tell the user a concurrent write happened) does NOT transfer to
this slice.** That rule belongs to `/claude-tui-line:edit`, where one actor holds both copies. At the
MCP layer nobody holds both halves (§12.6.12:87-88), and the model has no second read to compare
against. Implementing "say so" here would require the server to diff — which is rule 3's prohibition
arriving by the back door.

---

## 3. An erratum this task must apply

§12.6.11 rule 3 amends §12.6.5's text: the sentence telling a refused caller to **"re-read"** must
say **"re-derive"**.

> A caller that answers `stale-revision` by fetching a fresh `revision` and resubmitting its original
> `config` has written a retry loop that is not a retry.

§12.6.5:9-11 already carries the forward-reference. **Make the edit in `SPEC-V2-FRAMEWORK.md` as part
of this task**, so the normative sentence stops saying the wrong thing on its own. Leaving a
superseded clause in place and relying on the reader to follow a cross-reference is the staleness
failure this repo has hit repeatedly: implementors read the sentence, not the pointer.

---

## 4. What must not change

1. **§12.1's no-remembered-items rule.** §12.6.6 is explicit it does not relax by changing transport.
2. **The `--check` and render behaviour.** The server drives them; it does not own them.
3. **`revision` is a hash of the file's bytes**, not of the parsed config. Two byte-different files
   that parse identically must produce different revisions — otherwise a formatting-only hand edit is
   invisible to CAS.
4. **§12.2's checkpoint stays the backstop.** §12.6.11:52 is careful that it makes a clobber
   *"recoverable rather than preventable"* — do not let this slice's CAS be described as making the
   checkpoint unnecessary. **A2: and it is now implemented by this task — see §9.** A1's warning
   ("do not describe it as implemented") is retired; it was true of A1's scope and is false of A2's.

---

## 5. NEEDS-EVIDENCE from §1-§2 — both ANSWERED

**N1 — does the CLI expose what the server needs to spawn? ANSWERED: yes.** Both `--check` and
`--preview` accept `--config <path>` for an arbitrary file. Exit codes: parse error → 3; explicit
path not found → 3; check diagnostics with errors → 1; clean → 0.

**N2 — can the CLI validate a candidate config at an arbitrary path? ANSWERED: yes.**
`explicitConfigPath ?? ConfigLoader.ResolveConfigPath()` at both `RunCheck` and `RunPreview`.

**Consequences, now settled:** validate-then-write is the right shape, and the write-then-validate-
then-roll-back contingency is dead. **A2 sharpens the ordering: validate-then-checkpoint-then-write.**
The checkpoint goes after validation (a config that fails `--check` is never written, so it needs no
checkpoint) and before the write (§9.2). See §9.6 on why nothing may be reordered past the write.

---

## 6. Confidence (§1 and §2)

**High on §1 (spawn).** Section 1.3(b) is the argument I would defend hardest: linking makes every MCP tool
succeed on a machine where the statusline cannot render, which converts §12.6.6's `cli-not-found`
path from a safeguard into dead code. Section 1.3(a) is nearly as strong and comes straight from §12.6.12
rule 3. Note the spec explicitly leaves this open, so this is a ruling that closes an open question
rather than an interpretation of a settled one — **if anyone disagrees, this is a legitimate thing to
disagree with**, and section 1.3(b) is the claim to attack.

**High on §2.1's core claim** (`set_config` must not ship with a partial CAS contract). It rests on
§12.6.12's own text rather than on my judgment. **But I overstated its scope** in the original,
bundling `get_config` into an indivisibility the argument never supported. Corrected above.

**High on §2.3.** §12.6.12:117-121 states the asymmetry in as many words; my contribution is only
noticing that "say so" would smuggle rule 3's prohibition back in.

**Medium on section 1.4's harness/behaviour line**, constrained by §7.1's allow-list.

---

## 7. A1 — the server project

### 7.1 The MCP library and the project layout — UNCHANGED BY A2, still in force

**Use the official `ModelContextProtocol` NuGet package. Do not hand-roll JSON-RPC.**

MCP is a **public interface** whose wire format is maintained by someone else and will move. A
hand-rolled implementation is a second copy of a spec this project does not own — the same
one-implementation argument as §1.1, pointed outward. Protocol conformance is not where this
project's correctness budget should go; `SizeResolver` is.

**Layout: a new `src/ClaudeTuiLineMcp/` sibling project.** Separate assembly, separate entry point.

**On sharing code with the core — SPEC-83 replaced the project reference with a shared
library.** impl3 originally proposed referencing the main project "only for the server's own file I/O,"
and the tension was real: a project reference makes the whole core reachable, including the merge
capability section 1.3(a) is built to keep out of reach.

I considered forbidding the reference and duplicating config-path resolution in the server. **Ruled
against it**, because the failure it invites is worse: if the server and the CLI ever disagree about
*which file is the config*, the server validates one file and writes another, and every tool reports
success. A silent path divergence is harder to detect than a rule violation and has no natural
tripwire.

The original resolution was to allow a `ProjectReference` to `ClaudeTuiLine` under a
one-member allow-list. **SPEC-83 superseded that**, for an unrelated reason:
`ClaudeTuiLine.csproj` sets `PublishAot`, making it self-contained, and the SDK refuses
to publish a framework-dependent exe that references one (NETSDK1151). The shared
function now lives in `src/ClaudeTuiLineShared`, a plain dependency-free library, as
`ClaudeTuiLineShared.ConfigPath.ResolveConfigPath()`. Both the CLI and the server
reference *that*; **the server has no reference to `ClaudeTuiLine` at all.**

The allow-list survives this intact, and is strictly stronger for it. It was previously
"one permitted member out of an entire reachable core," enforced by a grep. It is now
"the core is not reachable," enforced by the absence of the reference itself — V4b
asserts the `ProjectReference` is gone, and V4 continues to assert no `ClaudeTuiLine.*`
member has crept back in by any other route. If a second shared member is genuinely
needed, moving it into `ClaudeTuiLineShared` is a spec amendment, not an implementor's
call.

### 7.2 — SUPERSEDED BY A2. See §9.

A1 ruled that #10 must not originate the §12.2 ledger writer, that `set_config` must defer, and that
the first slice was `get_config` alone. **Jim overruled it.** `dispatch-10-ruling.md`:

> **Keep `set_config` in #10's scope.** … the two-writer race that motivated the concern is
> unrealistic in practice — a human is driving one LLM session at a time when they ask for config
> changes … Don't hold the feature back for a theoretical race that doesn't match how this gets used.

> … using whatever checkpoint-write mechanism the architect judges appropriate for a
> single-writer-in-practice assumption — architect's call on the concrete implementation.

**Accepted without reservation.** §9 is that concrete mechanism. What A1 got right and wrong, kept
visible rather than edited away, because two of the three grounds it gave were bad:

- **Wrong on framing the append as unfixable.** A1 observed that .NET's `FileMode.Append` is
  seek-then-write rather than POSIX `O_APPEND` — true of that one API — and then let it stand as
  though the platform had no answer. It has several. §9.4 uses one.
- **Wrong on the format-freeze argument, and this one should never have been made.**
  `docs/backup-ledger.md` specifies the entry format completely and predates this task. **#10 is
  implementing a format, not choosing one**, so *"decide once, free exactly once"* does not apply and
  neither does "choosing for four unwritten consumers." A1 should have looked for that file before
  building an argument on its absence; the retrieval that answered it cost one dispatch.
- **Right on rejecting option (b), the config-only checkpoint** — and now confirmed by the project's
  own documentation rather than by my reasoning. `docs/backup-ledger.md` opens with exactly that bug:

  > `/claude-tui-line:edit` never touches `settings.json` — it edits `claude-tui-line.json` — yet it
  > was instructed to checkpoint through this procedure, whose entry captured only the file `/edit`
  > does not modify. So `/edit`'s "restore the checkpoint and report the failure" recovery path
  > restored a `statusLine` key nobody had changed, left the broken config exactly where it was, and
  > reported success.

  That is the origin story of §12.2 rule 4, and it is the same failure shape A1 argued from
  independently. **A config-only checkpoint is a documented, already-paid-for bug. §9.2 is not
  negotiable.**

### 7.3 — DEAD. The "get_config alone" scope reduction is withdrawn.

One sentence survives it, promoted into §2.2: **`get_config` must still perform the CLI presence
check** and fail `cli-not-found`.

### 7.4 — SUPERSEDED BY §10, which is now the single verification list.

---

## 8. — ESCALATION WITHDRAWN

A1 recommended Ultra-Advisor for the ledger writer. **The ruling forbids gating #10 on that, and I
withdraw the recommendation.** Disposition of the four questions A1 could not settle:

1. **`O_APPEND` vs. a serialising lock** — §9.4 rules: a lock, with a mandatory bounded retry.
2. **Can a compiled writer and the model-written path coexist?** Not fully resolvable, and **Jim has
   ruled it out of scope on likelihood grounds**. §9.5 records it as a live assumption rather than
   pretending the guard covers it.
3. **Does the once-ever `origin` rule survive an unattended writer?** §9.3 sidesteps this rather than
   answering it: the MCP server never writes `origin` at all, so the question does not arise for #10.
4. **Should `set_config`'s checkpoint capture the `statusLine` and referenced script even though it
   never touches `settings.json`? — ANSWERED: YES**, and not by me. §12.2 rule 4 and
   `docs/backup-ledger.md`'s opening bug (quoted in section 7.2) settle it. I had leaned yes and said I was
   not confident enough to rule it; the document rules it.

§9.5 documents one residual assumption. **That is documentation, not a reopened objection, and
nothing in §9 gates #10.**

---

## 9. A2 — the checkpoint write

**Authority: `docs/backup-ledger.md` is the procedure; §12.2/§12.2.1 is the design.** Where this
section and that file disagree, **that file wins and this section is the defect.** Do not implement
from this summary — read `docs/backup-ledger.md` in full first. It is 262 lines and it is normative.

### 9.1 What exists today

- **`docs/backup-ledger.md` — complete.** Entry shape, field names, both `kind` values, hashing,
  filename format, the 10-step procedure, the four rules.
- **The store**: `~/.claude/claude-tui-line/backups/`, holding `ledger.jsonl`,
  `<timestamp>-settings.json`, and `<timestamp>-<original-script-basename>`.
- **Zero compiled implementation.** `grep -rn 'Checkpoint\|checkpoint' src/ --include=*.cs` → **NO
  MATCHES.** #10 writes the first one. §12.2 also notes the store *"does not exist on any machine yet
  — no ledger has ever been written, so there is nothing to migrate and no `origin` at risk."*

### 9.2 `set_config` takes a FULL entry, not a config-only one

Per §12.2 rule 4 and the procedure's steps 3-6: **one entry per invocation, written before the first
write, capturing every artifact regardless of which one the command intends to change.** Ordered:

1. **Ensure the backup directory exists and is writable. If not, STOP** — fail the MCP call and write
   nothing (§9.6). This is the procedure's step 1 and it is an abort, not a warning.
2. **Read `ledger.jsonl`** — only to satisfy §9.6's readability check. Under §9.3 its contents no
   longer decide anything. Never hold it open to write it back (§12.2.1 rule 2).
3. **Read the live `settings.json` `statusLine`** and record it **verbatim, including keys we do not
   recognise**. If there is no `statusLine` key at all, record **`"statusLine": null`** — a real,
   restorable state, and distinct from not knowing.
4. **Copy `settings.json`** into the backup dir under a timestamped name; SHA-256 it →
   `settingsCopy`, `settingsSha256`.
5. **If `statusLine.command` names a script on disk**, copy and hash it →
   `scriptOriginalPath`, `scriptCopy`, `scriptSha256`. Omit those three fields only when the previous
   command was not a script on disk. **Copy, never move, and never modify the user's script** (rule 2).
6. **Resolve the config path via the same `ConfigLoader.ResolveConfigPath()` the write will use**
   (§7.1), and if a file is there, copy and hash it → `configOriginalPath`, `configCopy`,
   `configSha256`. **If no config file exists, record `configOriginalPath` with `configCopy: null`
   and omit `configSha256`** — never all three omitted. This is the same present-and-null pattern as
   `"statusLine": null` above and §12.6.9's `revision: "absent"`; recording an absence explicitly
   keeps "nothing was here" distinguishable from "this record cannot say," which call for opposite
   recovery actions.
7. **Append exactly one line** (§9.4). **Then** write the config.

**Yes, `set_config` copies `settings.json` even though it never modifies it.** That is rule 4, and
Section 7.2 quotes the bug that produced the rule. An implementor who "optimises" this away reintroduces it.

### 9.3 The most dangerous decision in this task — and how to delete it

The procedure's step 7 writes `origin` iff no `origin` entry exists **and** the current `statusLine`
does not already point at a claude-tui-line binary. §12.2 on getting that wrong:

> the once-ever rule makes the resulting false `origin` permanent. That is strictly worse than the
> second-use failure above: there, the escape hatch degrades; here, it is poisoned at creation and
> nothing downstream has cause to doubt it.

Implementing *"points at a claude-tui-line binary"* in compiled code means a path/name heuristic, and
a wrong answer is **permanently unfixable**: rule 1 forbids removing the entry, and once the tool is
installed the condition can never come out `origin` again.

**RULED: the MCP server NEVER writes an `origin` entry. `set_config` always appends `kind:
"checkpoint"`.** Three reasons, and I hold this at high confidence:

- **It removes the risk class rather than mitigating it.** The failure above requires writing
  `origin`; a writer that cannot write one cannot poison anything. Same argument shape as section 1.3(a) —
  prefer the structure that makes the bad outcome impossible over the rule that forbids it.
- **A missing `origin` is explicitly an honest, handled state.** §12.2: *"A missing `origin` is honest
  and already handled — §12.5 lists the checkpoints and flags which point at a claude-tui-line
  binary."*
- **`origin` belongs to `setup`.** `set_config` runs only where the CLI is already installed — §2.2's
  `cli-not-found` check guarantees it — which makes it close to the worst-placed writer in the system
  to judge what "before this tool existed" looked like.

The cost is that a user who only ever uses MCP never gets an `origin`. That is the honest state, and
it is the state they are actually in.

### 9.4 The append

**Per §12.2.1 rule 2: a real append of one line. Never a whole-file write.**
`docs/backup-ledger.md` gives the model-facing form (`>>`, *"never with a whole-file write tool"*);
the compiled equivalent must have the same effect, not the same syntax. Four requirements:

1. **One line.** Minified JSON, no interior newlines, terminated by exactly one `\n`. A
   pretty-printed entry spans lines and the format's one guarantee is gone.
2. **One write call.** Build the complete line in memory and issue a single write — never
   header-then-body. This is the **crash-atomicity** guard and it is orthogonal to any race: a lone
   writer that dies mid-write still tears the file.
3. **`FileShare.None` on the append, with a bounded retry and backoff.** **The retry is mandatory,
   not decorative.** §12.2.1 rule 4 states the property the lock must satisfy:

   > the correct outcome for two concurrent ledger writes is that **both entries land**, not that the
   > second one wins.

   A lock that fails or skips the second writer instead of retrying it **violates that rule**. On
   exhausting the retries, **fail the MCP call** (§9.6) — never skip the checkpoint and proceed.
   Retry count and backoff are the implementor's to pick (modest, and a named constant, not a
   literal); I have no measurement to spec them from.
4. **Never point a whole-file write API at `ledger.jsonl`** (§12.2.1 rule 2). Note this is the
   opposite of the config write, which §12.2 rule 3 requires to be atomic temp-file-then-rename. Two
   files, two write disciplines; do not unify them.

**Not a named `Mutex`.** .NET's system-wide named mutexes have macOS/Unix semantics I have not
verified and will not spec on assumption. `FileShare.None` needs no such verification. If someone
confirms the cross-process behaviour on this platform, a Mutex becomes an acceptable substitute — but
it is not the default and must not be adopted on a guess.

**Two timestamp formats coexist in one entry and must NOT be unified.** The `timestamp` field is UTC
ISO 8601 (`2026-08-13T04:12:07Z`); artifact **filenames** use the compact form
(`20260813-041207-settings.json`). Normalising either to the other breaks cross-referencing between
the entry and the files it names.

**Hashes**: `System.Security.Cryptography.SHA256`, rendered as **bare lowercase hex with no prefix and
no filename**, byte-identical to what `shasum -a 256` produces for the same file. A model reading the
ledger cannot tell which writer produced an entry and must not have to.

**Filename collisions**: timestamps are second-resolution and two artifacts can collide. If the name
you are about to write already exists, **append a counter (`-2`, `-3`) — never overwrite** (rule 1).

### 9.5 What the guard covers and what it does not — RECORD THIS, do not re-argue it

Jim ruled the binary-vs-model race unlikely in practice and #10 proceeds on that basis. §9.4(3) is
worth having regardless, but its coverage must be stated, because **a guard that buys less than it
appears to is worse than no guard — the assumption stops being visible.**

- ✅ **A second *compiled* writer.** §12.3, §12.4, §12.5 and §12.7 are all specced to touch this
  ledger. The moment any of them lands, two binaries append and this guard is exactly right.
- ❌ **The binary-vs-model race — NOT covered.** The other writer is a language model with a file-edit
  tool. It will never open the file through our `FileStream` and cannot be enrolled in any locking
  protocol. **A lock only excludes writers participating in the same locking protocol.** Recording
  this guard as though it addressed that race would hide a live assumption behind it.
- ✅ **A torn append is already survivable by design**, and this is the reassuring part. §12.2.1
  rule 3 and `docs/backup-ledger.md` both require a reader to *"discard that line and use the rest"* —
  *"a torn append costs the newest entry and leaves every earlier one byte-identical."* The format was
  chosen for this. **Under the single-writer assumption the durability story is complete**, and
  §9.4(2) narrows the window further.

### 9.6 Hard error paths — `set_config` FAILS rather than proceeding

`docs/backup-ledger.md`: *"Every step that can abort comes before every step that writes,"* and
*"Never proceed with a write on the theory that the backup can be taken afterwards."* The ordering
exists because rule 1 forbids removing an entry, so a permanent record of a change that never
happened cannot be cleaned up.

`set_config` therefore fails, having written nothing, when:

- the backup directory cannot be created or written (step 1);
- any artifact copy or hash fails (steps 4-6);
- the ledger append cannot be completed, **including retry exhaustion** (§9.4(3));
- the ledger exists but **cannot be read at all**. Note the distinction §12.2.1 rule 3 draws: a
  **torn final line is NOT this case** and must be tolerated silently. Only a genuinely unreadable
  ledger aborts. This is the condition most likely to be implemented as a fatal error by mistake —
  see V11.

**This needs its own error code, distinct from `stale-revision` and `cli-not-found`.** Call it
`checkpoint-failed`; the payload carries the failing path and the underlying error. The two existing
codes both mean "retry differently"; this one means "your backup store is broken, tell the user." A
model that cannot distinguish them will retry into the same failure indefinitely.

**The tool description must state that a failed checkpoint means nothing was written** — otherwise a
model assumes a partial write and goes looking for damage that does not exist.

### 9.7 THE BACKUP ROOT MUST BE INJECTABLE — a test-safety requirement, not a style preference

Three facts combine into a hazard that is easy to miss:

1. The ledger lives under the user's real `~/.claude/`.
2. **Rule 1 forbids ever deleting or overwriting anything in the backup directory.**
3. There is a standing constraint in this project that the implementor does not touch `~/.claude`.

So **a test that exercises the checkpoint path against the default root writes permanent,
undeletable entries into Jim's actual backup store** — and rule 1 makes that pollution unremovable
*by design*. There is no cleanup step available; the format's own integrity rule prevents one.

**RULED: the backup root is a constructor/parameter input, defaulting to
`~/.claude/claude-tui-line/backups/`. Tests always inject a temp directory. No test may run against
the default root.** V6 enforces it structurally, because a convention that lives only in a review
comment will not survive the fourth test someone adds.

This is the same defect class as the `maxLines` failure diagnosed in
`SPEC-diag-73b-31-maxlines.md` — a test pointed at a shared, run-persistent location — except that
here the shared location is the user's real recovery tree and the writes cannot be undone. That bug
cost a false regression hunt. This one would cost user data.

---

## 10. Verification (supersedes section 7.4)

- **V1** — `get_config` returns the parsed config and a `revision` that changes when a byte changes
  and not otherwise. **Include a formatting-only edit** (whitespace, key order) and assert the
  revision **does** change — §4 item 3 is the rule this catches.
- **V2** — `get_config` with no config file returns `revision: "absent"`: not an error, not `null`,
  not a missing field.
- **V3** — with no CLI on the search path, **both** tools fail `cli-not-found` and **name the paths
  searched**. Assert the paths are in the payload, not just that the code fires.
- **V4** — structural: `grep` `src/ClaudeTuiLineMcp/` for core types; assert only the §7.1 allow-list
  appears. Without this, §7.1 is a comment rather than a rule. **V4b** additionally asserts the MCP
  csproj carries no `ProjectReference` to `ClaudeTuiLine.csproj`.
- **V5** — `set_config` with a stale `baseRevision` refuses with `stale-revision` **and a payload
  carrying the current config and the current revision** (§2.2). **Assert the payload, not just the
  code** — the payload is the half that prevents the clobber, and a code-only assertion passes
  against the exact defect §2.1 describes.
- **V6** — structural: assert no test constructs the checkpoint writer without injecting a backup
  root (§9.7). This is the one protecting real user data; it should fail loudly and cite §9.7.
- **V7** — a `set_config` entry contains `settings.json`'s copy and hash **and** the config's copy and
  hash, in **one** entry, written **before** the config is modified (§9.2, rule 4).
- **V8** — with no config file present, the entry carries `configOriginalPath` and
  `configCopy: null` and **omits** `configSha256`. Assert present-and-null, not absent (§9.2 step 6).
- **V9** — `set_config` **never** writes `kind: "origin"`, even with an empty ledger and a
  `statusLine` pointing at a non-claude-tui-line script (§9.3). Assert the safety property directly.
- **V10** — an unwritable backup directory makes `set_config` fail `checkpoint-failed` **and leave the
  config byte-identical** (§9.6). Assert both halves.
- **V11** — a ledger whose final line is truncated is **tolerated**: `set_config` succeeds, appends
  after it, and leaves every earlier line byte-identical (§12.2.1 rule 3).
- **V12** — an artifact filename collision within the same second yields a `-2` suffix and **does not
  overwrite** the existing file (§9.4).
- **V13** — a hash the implementation produces for a known file equals `shasum -a 256`'s output for
  that file, bare lowercase hex (§9.4).

**V6, V9 and V11 catch the expensive mistakes.** V6 protects user data, V9 protects the recovery
tree's root permanently, V11 is the one a reasonable implementor gets wrong by being careful.

---

## 11. Confidence on §9

**High on §9.2, §9.4, §9.6 and §9.7's mechanics.** These are transcriptions of
`docs/backup-ledger.md` and §12.2.1 rather than my judgment, and where I interpreted, I cited.

**High on §9.3 (never write `origin`)** — but flagging it as the one place in A2 where I made a real
behavioural choice rather than a transcription. It trades a state the spec explicitly calls *"honest
and already handled"* for the elimination of a permanently-unfixable failure. I do not think it
warrants delaying #10, and the ruling forbids gating on an Ultra-Advisor pass — **but if anything in
A2 deserves a second opinion later, it is this**, and it is cheap to revisit because no `origin` entry
is ever created by the choice.

**Medium-high on §9.4(3)'s retry.** That `FileShare.None` *without* retry would violate §12.2.1
rule 4 follows directly from that rule's text. The retry count and backoff I have deliberately not
specified, for lack of any measurement to base them on.

**E1 — NEEDS-EVIDENCE, non-blocking.** Does the core already expose (a) a SHA-256 helper and (b) an
atomic temp-file-then-rename writer? §7.1's conditional allow-list extension turns on this. If both
are absent the server implements them locally and V4 asserts a one-member list; if present, V4
asserts two or three. **Either way #10 proceeds** — this only decides which line V4 asserts, so do
not hold implementation on it. Report the finding back and amend §7.1 in place.

---

## 12.6 `get_config_schema` (task #84)

A third tool alongside `get_config`/`set_config`, added by SPEC-84-mcp-schema-explorer.md. Read-only,
takes no config path — it describes the config format itself, not any particular file's contents.

**Signature.** `get_config_schema(sections?: string[])`. `sections` is an optional allow-list filter
over `items`, `colors`, `accepted`, `structures`, `kindSupport`; omitted or empty returns the full
envelope. An unrecognized name in `sections` fails the call outright (code `unknown-section`) rather
than silently dropping it — see SPEC-84 §5.1/§6.3.

**Mechanism.** Spawns the CLI via `CliRunner.RunSchemaAsync()` (`ClaudeTuiLineMcp/CliRunner.cs`),
which runs `<cli> --schema --json` with no other arguments — the same spawn shape as
`RunCheckAsync`, reusing the `CliCheckResult` record. This follows §1's rule that the server spawns
the CLI rather than linking the core in-process; a successful spawn is itself evidence the installed
binary produces a schema, which an in-process link could not demonstrate.

**Response shape.** On success, returns the CLI's `--schema --json` envelope verbatim (or, when
`sections` is given, `{ version, <each requested section> }`) — see SPEC-84 §5 for the envelope's
full shape (`version`, `items`, `colors`, `accepted`, `kindSupport`, `structures`).

**Failure modes**, mirroring `get_config`'s pattern:
- `cli-not-found` — `CliLocator` could not locate the binary (same shape as the other two tools;
  `McpResults.CliNotFound`).
- `schema-unavailable` — the CLI was found but exited non-zero or its stdout did not parse as JSON.
- `unknown-section` — `sections` named something outside the five valid names; the error message
  lists the valid set.

No config file is read, no config path is resolved, and no checkpoint/write path is touched — this
tool cannot fail for any reason `get_config`/`set_config` can.
