# Snapshot install — install.sh and setup for marketplace snapshots as well as git clones

Status: rev 3 READY-FOR-IMPL (2026-10-02). Brief: `.claude/hierarchy/msgs/20261002-104537-gwp5--architect--snapshot-install--request.md`; rev 2 brief: `…/20261002-124021-nsxu--architect--snapshot-install-rev2--request.md`
Implementer: implementor
Reviewer: reviewer
Branch base: main 7c48b95 (v0.4.0)

**Rev 2 changes** (each is also marked `rev 2` where it lands):
- User decisions: U1 = (b) a notify-only SessionStart hook (new §D.11); U2 = automatic handoff; U3 = one "apply this plan" approval (§I).
- H.1 PASS: snapshot mode is viable as specced.
- H.2: the CLI labels a local marketplace `Source: Folder (<path>)`, not `Directory`. The three existing checks therefore never match, so **today's clone path always ends `install incomplete`, exit 1**. New §D.10 fixes it, as a deliberate clone-path delta.
- The stamp semantics change (§D.5): the stamp now means "these binaries were built from a snapshot". Clone mode removes it rather than writing it. This keeps the hook silent for clone users, whose plugin cache can lag their checkout.
- H.6 is new: the hook's visibility.

**Rev 3 changes** (reviewer findings 6–8, `…/20261002-125116-1yqn--orchestrator--snapshot-install-review--response.md`):
- 6: §D.10 now requires `marketplace_block` to match the exact marketplace name. **This is an implementation change**, one awk pattern, pinned by the new G.24.
- 7: G.13 is reworded to use a stub knob.
- 8: new §D.12, the release version bump. U5 (§I) holds the number, which is the user's call.

---

## A. Goal and headline findings

Goal: `/plugin marketplace add` → `/plugin install` → `/claude-tui-line:setup` completes end-to-end from a
marketplace snapshot, while `./install.sh` from a git clone keeps today's outcome.

Headline findings that shape the design:

1. **Setup has never completed from inside Claude Code, in either mode.**
   - setup.md:14 runs `"${CLAUDE_PLUGIN_ROOT}/install.sh"` bare.
   - `${CLAUDE_PLUGIN_ROOT}` is *always* a cache copy under `~/.claude/plugins/cache/…`, even for a
     local-directory marketplace (F2), so install.sh:231-236 refuses it.
   - Even past that, the Bash tool has no TTY, so install.sh:426-438 refuses every write unless
     `--non-interactive` is given.
   - So this spec fixes two gaps: the snapshot refusal, and a consent flow that works without a TTY.
2. **Jim's own machine is the dangerous case.**
   - His marketplace is `directory: /Users/jimcline/git/repos/claude-tui-line`, but the installed plugin
     runs from `cache/claude-tui-line/claude-tui-line/0.3.0`, while the clone is at 0.4.0 (F2).
   - A naive snapshot install would build the stale 0.3.0 cache and overwrite the 0.4.0 binaries in
     `$BIN_DIR`.
   - So a snapshot whose marketplace source is a live git clone **hands off** to that clone's `install.sh` (§D.3).
3. **MCP stays a user-scope `claude mcp add`; no `.mcp.json` and no plugin `mcpServers`.**
   - In snapshot mode the registration targets the deployed apphost in `$BIN_DIR`, never a path inside
     the snapshot.
   - This departs from the brief's suggestion. Why is in §J.1.
4. **In snapshot mode, `$BIN_DIR` is pinned to `$HOME/.claude/claude-tui-line/bin`** by removing
   `CLAUDE_PLUGIN_DATA` from install.sh's environment.
   - Every consumer resolves that path when `CLAUDE_PLUGIN_DATA` is unset.
   - `CLAUDE_PLUGIN_DATA` is deleted on plugin uninstall, which would silently break the statusLine (F4, §J.2).
5. **(rev 2) The clone path is broken today on the current CLI.**
   - install.sh recognises its own marketplace by `Source: Directory (<path>)` at 377, 528 and 604.
   - The current CLI prints `Source: Folder (<path>)` (F18).
   - So c6_plugin never passes, and every clone run re-plans plugin registration.
   - In an interactive run, the post-add check (528) misreads the just-added entry as a foreign collision and offers to remove and re-add it.
   - Verify (604) then fails, so the run ends `install incomplete` with exit 1.
   - `already installed` is unreachable.
   - §D.10 accepts both labels. This is the one clone-path behaviour delta, and it is deliberate.

## B. Facts (cited)

| # | Fact | Source |
|---|---|---|
| F1 | install.sh refuses any `REPO_ROOT` under `$HOME/.claude/plugins/` (231-236), then any `REPO_ROOT` without `.git` (238-242). It refuses to write without a TTY unless `--non-interactive` is given (426-438). Its freshness check uses `git log` (297-310). MCP is registered via `claude mcp add -s user claude-tui-line $REPO_ROOT/bin/claude-tui-line-mcp` (22, 501-514). The marketplace is registered as `Source: Directory ($REPO_ROOT)` (516-576), and verify re-checks that (603-609). | install.sh |
| F2 | Local state: marketplace `claude-tui-line` is `directory:/Users/jimcline/git/repos/claude-tui-line`. The plugin installPath is `~/.claude/plugins/cache/claude-tui-line/claude-tui-line/0.3.0` (no `.git`), while repo plugin.json is 0.4.0. The ponytail cache has no `.git`; the skillsmith cache *has* `.git`. GitHub marketplace dirs under `marketplaces/` have `.git`, but `claude-plugins-official` does not. | `~/.claude/plugins/{known_marketplaces,installed_plugins}.json`, `ls` (2026-10-02) |
| F3 | A local-directory marketplace "has no copy" under `marketplaces/`, but installed plugins live at `cache/<marketplace>/<plugin>/<version>/`. The version comes from the manifest `version`, then the marketplace entry, then the source. On update, the old version dir gets `.orphaned_at` and is removed 14 days later. | code.claude.com/docs/en/plugins/loading.md ("Find plugins on disk", "Versions and updates") |
| F4 | `CLAUDE_PLUGIN_DATA` = `~/.claude/plugins/data/<id>/`. It is kept across updates but **deleted on uninstall by default**. It is exported to hooks, MCP stdio servers and LSP servers. Export to the Bash tool during a command is **not documented**. | docs plugins/manifest-reference.md ("Environment variables") |
| F5 | Plugin MCP tools are named `mcp__plugin_<plugin>_<server>__<tool>`. User-scope `claude mcp add` tools are `mcp__<server>__<tool>`; today's are `mcp__claude-tui-line__get_config` etc. | docs mcp.md |
| F6 | `${CLAUDE_PLUGIN_ROOT}` in a plugin MCP `command` registered only 4/6 times, failing silently; it worked 9/9 in `args`. With `mcpServers` in plugin.json only, the server did not register until marketplace.json mirrored it. | agent-tools/agent-hierarchy/docs/specs/0013-agent-hierarchy-mcp-server.md:285-298, 891-908 |
| F7 | A repo-root `.mcp.json` is also the project `.mcp.json` when the clone is opened as a project. There the variable is unset and passed through literally, and install.sh:316-323 fails on a `claude-tui-line` entry in it. | Engram f20778; install.sh:313-326 |
| F8 | A plugin cannot ship `statusLine`: plugin default settings honour only `agent` and `subagentStatusLine`. | docs plugins/components.md ("Default settings") |
| F9 | SessionStart hooks: Claude's **first response waits** for them, the default timeout is 600 s, and stdout goes to Claude's context. There is no install/update lifecycle hook. | docs hooks.md (SessionStart) |
| F10 | `bin/claude-tui-line-mcp` resolves `${CLAUDE_PLUGIN_DATA:-$HOME/.claude/claude-tui-line}/bin/claude-tui-line-mcp`, exports `CLAUDE_PLUGIN_DATA` with the same fallback, and execs. It sets no runtime vars (`DOTNET_ROOT` etc.). | bin/claude-tui-line-mcp:8-23 |
| F11 | `CliLocator` (the MCP server's CLI lookup) tries `$CLAUDE_PLUGIN_DATA/bin/claude-tui-line`, then `$HOME/.claude/claude-tui-line/bin/claude-tui-line`. | src/ClaudeTuiLineMcp/CliLocator.cs:14-39 |
| F12 | Nothing tests install.sh. The repo's shell checks are `tools/check-*.sh`; C# tests live in `tests/`. | grep (smart-gopher), `ls tools/` |
| F13 | The backup ledger lives at `~/.claude/claude-tui-line/backups/`, deliberately outside `CLAUDE_PLUGIN_DATA`. | docs/backup-ledger.md:37-49 |
| F14 | No command .md references an MCP tool by its full `mcp__…` name. | grep commands/ |
| F15 | All three product csproj set `Version` 0.4.0 and `IncludeSourceRevisionInInformationalVersion` false. There is no Directory.Build.\*, global.json or nuget.config, and no visible build-time git dependency. The CLI supports `--version` (Program.cs:535). | src/*/*.csproj; grep |
| F16 | SPEC-90 §4.3 records the gap this design closes: when `CLAUDE_PLUGIN_DATA` is set at setup but unset at MCP launch, `CliLocator` looks where setup never wrote. | docs/specs/SPEC-90-mcp-server-registration.md:178-198 |
| F17 | `tools/check-all.sh` runs an explicit list of checks (`check-docs`, `check-examples`, `check-doc-tokens`) and runs all of them even if one fails. | tools/check-all.sh |
| F18 | (rev 2) Real `claude plugin marketplace list` blocks are `❯ claude-tui-line` followed by `Source: Folder (/Users/jimcline/git/repos/claude-tui-line)`, and `❯ ponytail` followed by `Source: GitHub (DietrichGebert/ponytail)`. The label is **`Folder`**. `marketplace_block` (install.sh:64-66) still isolates the block, because its awk keys on a line ending `claude-tui-line`. The three exact-match sites, 377, 528 and 604, do not match. | H.2 (task-gopher run, 2026-10-02) |
| F19 | (rev 2) Both csproj (`ClaudeTuiLine`, `ClaudeTuiLineMcp`) `dotnet publish` with exit 0 from a `.git`-less `git archive` export. No git or sourcelink lines appear in the logs. | H.1 (task-gopher run, 2026-10-02) |
| F20 | (rev 2) Plugin hooks: `hooks/hooks.json` at the plugin root is the default location. SessionStart matchers are `startup`, `resume`, `clear` and `compact`. A hook entry takes a `timeout` in seconds. On exit 0, JSON stdout is parsed, and two fields matter here: `systemMessage`, documented as a message shown to the user, and `hookSpecificOutput.additionalContext`, which is added to Claude's context. SessionStart cannot block. | docs hooks.md / plugins reference (from the rev 1 reading; not re-fetched). Visibility is unobserved, hence H.6 |
| F21 | (rev 2) `${CLAUDE_PLUGIN_ROOT}` in a plugin **hook** `command` expands reliably on this machine. Engram's `"${CLAUDE_PLUGIN_ROOT}/hooks/engram-exec.sh"` and compaction-guard's `node "${CLAUDE_PLUGIN_ROOT}/scripts/…"` hooks both ran successfully this session. The F6 flakiness is specific to MCP `command`. | this session's PreCompact/PostCompact hook output |

## C. Design overview

install.sh gains a **mode**, decided once, before anything else, from `REPO_ROOT` alone:

| `REPO_ROOT` | Mode | Behaviour |
|---|---|---|
| under `$HOME/.claude/plugins/` (same prefix as install.sh:232) | **snapshot** | §D.2, or the §D.3 handoff to a clone |
| elsewhere, `.git` present | **clone** | today's behaviour, with two exceptions: the §D.10 source-label fix, and the §D.5 stamp removal, which only matters after a snapshot install (rev 2) |
| elsewhere, no `.git` | refuse | install.sh:238-242 text and exit code, unchanged |

`.git` presence plays no part in recognising a snapshot. It varies by source type (F2), and a snapshot
that happens to carry `.git` is still overwritten on update.

Setup moves to a TTY-free flow: **plan → user approves → apply** (§D.7), built on a new `--dry-run` flag
plus the existing `--non-interactive`.

(rev 2) The plugin also ships a **notify-only SessionStart hook** (§D.11):

- It compares the snapshot stamp in the pinned `BIN_DIR` with the running plugin's version.
- When they differ, it tells the user to re-run setup.
- It never builds, writes, prompts or blocks.

## D. Requirements by file

### D.1 install.sh: mode detection

- Detect the mode immediately after `REPO_ROOT` is resolved. Detection must not depend on any later
  variable, and must come before `BIN_DIR` is resolved (D.2.1 needs that ordering).
- Step 1 ("Checkout sanity") prints the mode:
  - snapshot: `running from a plugin snapshot at <REPO_ROOT>`, plus a note that nothing persisted will
    reference this directory
  - clone: today's pass line, verbatim
- The refusal at install.sh:231-236 is removed. Its prefix test becomes the snapshot test.

### D.2 install.sh: snapshot-mode deltas (each applies ONLY in snapshot mode)

1. **`BIN_DIR` pin.**
   - Remove `CLAUDE_PLUGIN_DATA` from the script's environment (unset it) before `BIN_DIR` is resolved.
     The existing line-15 expression then yields `$HOME/.claude/claude-tui-line/bin`.
   - The §D.3 handoff inherits the unset environment.
   - The /bin-collapse guard (272-281) stays and runs in both modes.
2. **Freshness (c7).**
   - Replace the `git log` comparison with: the version stamp in `$BIN_DIR` (§D.5) equals `.version` of
     `$REPO_ROOT/.claude-plugin/plugin.json` (read with jq).
   - Stale in each of these cases, with a `warn` naming which:
     - the stamp is missing or unreadable
     - the plugin.json version is null or empty
     - the two differ
   - Stale means a rebuild is planned.
   - No `git` invocation of any kind in snapshot mode.
3. **Wrapper (c4).** Treated as OK and not reported as a write: snapshot mode never uses
   `$REPO_ROOT/bin/claude-tui-line-mcp` and never `chmod`s anything inside the snapshot.
4. **MCP registration target** = `$BIN_DIR/claude-tui-line-mcp`, the deployed framework-dependent apphost.
   - Its DLLs sit beside it (install.sh:110-133).
   - The wrapper adds nothing it needs (F10).
   - Under a user-scope server with `CLAUDE_PLUGIN_DATA` unset, `CliLocator` resolves the pinned `BIN_DIR` (F11).
   - The detection logic (exactly one `claude-tui-line:` line, `Connected`) is unchanged.
   - The confirm text and the plan line name this target instead of `$MCP_WRAPPER`.
   - An existing *connected* registration pointing elsewhere (e.g. a clone's wrapper) is left alone, as
     today. The wrapper resolves the same pinned `BIN_DIR` when `CLAUDE_PLUGIN_DATA` is unset (F10).
5. **Plugin/marketplace registration (c6_plugin).**
   - Treated as OK. Running from the snapshot is proof the plugin is installed.
   - No `claude plugin marketplace add/remove` and no `claude plugin install` in snapshot mode, in either
     phase 2-4 or the plan list.
   - Phase 5 verify skips the marketplace-source check (603-609) and prints a pass line naming the snapshot.
   - `--allow-marketplace-replace` is accepted and ignored.
6. **Staging stays at `$REPO_ROOT/publish*`**, i.e. inside the snapshot.
   - `dotnet publish` writes `obj/` and `bin/` under `$REPO_ROOT/src/*` regardless.
   - The version dir is orphan-cleaned 14 days after an update (F3).
   - Nothing persisted references it (§D.6).
   - See §J.4 for why it is not relocated.
7. **Everything else is identical to clone mode:**
   - toolchain checks
   - the `.mcp.json` report (c3)
   - the statusLine check and write (target = pinned `$cli_bin`)
   - ledger logic
   - the no-TTY refusal
   - build and deploy
   - exit codes

### D.3 install.sh: handoff to a clone (snapshot mode only)

**Trigger.** All of the following must hold:

- snapshot mode
- `claude` on PATH
- the §D.10 local-source recognition reports the `claude-tui-line` marketplace as a local-folder source
  at `<P>`, i.e. `Source: Folder (<P>)` or `Source: Directory (<P>)` (rev 2: the CLI prints `Folder`, F18)
- `<P>` is absolute
- `<P>` is not under `$HOME/.claude/plugins/`
- `<P>/.git` exists
- `<P>/install.sh` is executable

**Action:**

- Print one line: `marketplace source is your checkout <P> — handing off to <P>/install.sh`.
- Replace the process with `<P>/install.sh` and the **same arguments**, with `CLAUDE_PLUGIN_DATA` unset (§D.2.1).
- The exit status is the clone's.

**Placement:** after the mode is known, before any write and before step 4. If `claude` is absent, skip
the lookup; step 2 then fails as today.

**Not triggered.** If any condition fails:

- continue in snapshot mode
- if a local-folder source was found but `<P>` failed a condition, add one `info` line saying which

**Loop safety:** `<P>` is not under the plugins dir, so the handed-off run is clone mode and cannot hand off again.

### D.4 install.sh: `--dry-run`

**Interface.**

- New flag `--dry-run`. Add it to the `-h` usage line.
- Combined with `--non-interactive`, `--dry-run` wins.

**Behaviour.**

- Runs phase 1 (all checks, including mode detection and the §D.3 handoff, which passes `--dry-run` along).
- Then:
  - If `all_ok`: prints the existing `already installed` line (install.sh:415) and its two info lines,
    then exits 0. No "Rebuild anyway?" prompt.
  - Otherwise: prints the plan and exits 0.
- A phase-1 failure exits exactly as today (non-zero).

**Plan text.**

- The plan is the **same list** the no-TTY refusal prints (install.sh:429-435): header line
  `this run would have:` and one `  - …` line per pending action.
- It is made mode-aware per §D.2 (snapshot: no wrapper or plugin lines; the MCP line names `$BIN_DIR/claude-tui-line-mcp`).
- Both callers print it from one implementation, not two copies.

**Writes.**

- Writes nothing except what phase 1 already writes today (the `mkdir -p "$LEDGER_DIR"` at 389). Pinned by test G.3.
- Never prompts, TTY or not.

**Strings.** These are contract strings that setup.md keys off; G pins them:

- `already installed`
- `this run would have:`

### D.5 install.sh: snapshot stamp

> rev 2: rev 1 wrote the stamp in both modes. Clone mode now *removes* it instead.
>
> Why: the §D.11 hook compares the stamp with the version of the plugin that is *running*. For a clone
> user, the running plugin is a cache copy of the checkout, which can lag it. On Jim's machine the cache
> is 0.3.0 and the clone is 0.4.0 (F2). A clone-written stamp would therefore nag every session, and
> setup would hand off to the clone, which reports `already installed`, so the nag would never clear.
>
> Under the new rule the stamp means exactly one thing, "the binaries in `$BIN_DIR` were built from a
> plugin snapshot", so clone users never see the notice.

**Invariant.** `$BIN_DIR/.plugin-version` exists if and only if the binaries in `$BIN_DIR` came from a
snapshot-mode deploy. Its content is that snapshot's plugin.json `.version` plus a newline.

**Snapshot mode, deploy:**

- `do_deploy` writes the stamp with `.version` from `$REPO_ROOT/.claude-plugin/plugin.json`.
- **When:** after the final apphost rename (install.sh:132), using the same temp-then-rename.
  - The stamp never claims a deploy that did not finish.
  - A failed deploy leaves the old stamp, so the next run sees a mismatch and rebuilds.
- If the plugin.json version is null or empty, write no stamp, delete any existing one, and warn. The
  next snapshot run then rebuilds.

**Clone mode, deploy:**

- After the final apphost rename, remove `$BIN_DIR/.plugin-version` if it is present.
- Nothing is printed when it is absent, which is always the case on a machine that never ran a snapshot
  install.

**Clone mode, freshness (c7):**

- A present stamp means the binaries were not built from this checkout. Treat them as stale, with a `warn`
  that they were built from a plugin snapshot. A rebuild is then planned, and that deploy removes the stamp.
- With the stamp absent, the existing `git log` check runs unchanged.
- Net effect: on any machine that never ran a snapshot install, clone mode behaves byte-identically to
  today, apart from §D.10.

**Snapshot mode, freshness:** unchanged from §D.2.2.

### D.6 Invariant: nothing persisted references the snapshot

In snapshot mode, after a successful run, no path under `$HOME/.claude/plugins/` appears in:

- `settings.json` `statusLine`
- the MCP registration command
- any ledger entry field written by this run

`$BIN_DIR` and the ledger are both outside it. G.5 asserts this.

### D.7 commands/setup.md

Replace step 1's flow and remove the snapshot-refusal paragraph (22-26). Required behaviour:

1. **Plan.**
   - Run `"${CLAUDE_PLUGIN_ROOT}/install.sh" --dry-run`.
   - Non-zero exit: show its output verbatim and stop. Toolchain-missing lands here, and the user sees
     install.sh's own message and URL.
2. **Already installed.** If the output contains `already installed`, say nothing was changed and go to the preview step.
3. **Approve.**
   - Otherwise show the plan lines verbatim and ask with **AskUserQuestion** whether to apply exactly that
     plan. Options: Apply / Cancel.
   - If the plan contains the statusLine rewrite line, the question text must quote it. This is the
     user-confirmed statusLine step the brief requires.
   - Cancel: stop, nothing written.
4. **Apply.** Run `"${CLAUDE_PLUGIN_ROOT}/install.sh" --non-interactive`. Non-zero exit: show output, stop. Do not retry pieces by hand (keep setup.md:28-29).
5. **Preview and report.** The existing steps 2 and 3 are unchanged, plus three additions to the report:
   - which mode ran: snapshot, clone, or handoff to `<P>`, taken from install.sh's output
   - snapshot mode: binaries live in `~/.claude/claude-tui-line/bin`, outside the plugin, so they survive
     updates; **after a plugin update, run `/claude-tui-line:setup` again to rebuild**. (rev 2) A startup notice (§D.11) reminds the user when that is due.
   - an older clone whose install.sh lacks `--dry-run` fails at step 1 with `unrecognized argument`; say to update the checkout

The text stays relay-only. setup.md must not re-derive install.sh logic, which is the existing setup.md:17-20 principle.

### D.8 README.md

- **`### As a plugin` (30-71).**
  - State the prerequisites: .NET 10 SDK, `jq`, and the `claude` CLI on PATH.
  - The three-command flow now completes. Setup shows a plan and asks before changing anything.
  - Binaries go to `~/.claude/claude-tui-line/bin`, not the plugin dir.
  - Re-run setup after `/plugin update`. (rev 2) A one-line notice at session start says when the
    installed binaries are older than the plugin. It never builds anything itself.
  - A local-clone marketplace hands off to the clone's install.sh.
- **81-92 (`$BIN_DIR`):** note that a snapshot install always uses `~/.claude/claude-tui-line/bin`.
- **New short "Uninstall" subsection** (none exists today), in this order:
  1. `/claude-tui-line:revert`
  2. `claude mcp remove -s user claude-tui-line`
  3. `claude plugin uninstall claude-tui-line@claude-tui-line`
  4. optionally delete `~/.claude/claude-tui-line`. Warn that this deletes the backups, so only after revert.
- Plugin uninstall alone leaves the statusLine and the MCP server working from `~/.claude/claude-tui-line/bin`. This is deliberate (§E.9).

### D.9 Files that must NOT change

- `.claude-plugin/plugin.json` and `marketplace.json`:
  - no `mcpServers`
  - (rev 2) no `hooks` key, unless the H.6 fallback in §D.11 requires one
  - (rev 3) the only other change allowed is the release version bump in §D.12
- No `.mcp.json` is added. (rev 2) `hooks/` **is** added, per §D.11.
- `bin/claude-tui-line-mcp` and all of `src/`.
- `docs/backup-ledger.md`.
- commands `edit.md`, `migrate.md` and `revert.md`.

### D.10 install.sh: recognise the local marketplace source as `Folder` or `Directory` (rev 2, both modes)

**Problem.** Today three sites test for the literal `Source: Directory ($REPO_ROOT)`:

- 377: the c6_plugin check
- 528: the post-add collision check
- 604: verify

The current CLI prints `Source: Folder (<path>)` (F18), so all three misfire on every run (§A.5).

**Requirements.**

- **One implementation** decides whether the `marketplace_block` output is a local-folder source, and
  extracts its path. Both labels count: `Folder` (current CLI) and `Directory` (older CLIs that install.sh
  was written against).
- **Uses.** Sites 377, 528 and 604, and the §D.3 handoff, all use it. No site keeps its own literal.
  A fifth copy would drift, as the original literal did.
- **Matching.**
  - The path is the text between `Source: <label> (` and the **last** `)` on that line, so a path
    containing `)` still works.
  - Comparison with `$REPO_ROOT` stays exact string equality, as today. No normalisation is added.
- **Output.** The existing pass, fail and warn texts are unchanged. The fail and warn paths keep echoing
  the raw `Source:` line, so the user sees whatever label the CLI used.
- **Other labels** (e.g. `GitHub (…)`, `Git (…)`, `URL (…)`) are a non-local source, exactly as today.
- **(rev 3, review finding 6) `marketplace_block` must anchor on the exact marketplace name.**
  - **Problem.** Its awk start pattern today is `/claude-tui-line$/`, which also matches any marketplace whose
    name merely *ends* in `claude-tui-line` (e.g. `❯ my-claude-tui-line`). If such an entry is listed first,
    its block is taken.
  - Before §D.3 that only caused a misreport. Now that the handoff `exec`s `<P>/install.sh` from that
    block, it could run another checkout's script.
  - **Requirement.** The block starts only at a line that is exactly the name `claude-tui-line`, after
    stripping leading whitespace and an optional `❯` marker plus the whitespace after it. Real lines are
    `  ❯ <name>` (F18, confirmed by the reviewer).
  - The optional marker keeps older CLI output, which may lack `❯`, working.
  - The block's end rule (the first `Source:` line) is unchanged.
  - **This is an implementation change** to `marketplace_block` in install.sh, one pattern. Every consumer
    (377, 528, 604 and the handoff) inherits the fix, and no consumer changes. G.24 pins it.

**Effect on the clone path.** This is a deliberate delta, and the only clone-mode behaviour change besides
the §D.5 stamp removal:

- On the current CLI, a registered clone now passes c6_plugin.
- The interactive remove-and-re-add churn at 528-555 stops.
- Verify passes.
- `already installed` becomes reachable.

On an older CLI that prints `Directory`, behaviour is byte-identical to today (H.4).

### D.11 Notify-only SessionStart hook (rev 2, user decision U1 = b)

**Goal.** After `/plugin update`, tell the user once per session start that the deployed binaries are
older than the plugin, and that `/claude-tui-line:setup` rebuilds them. It never builds, writes, prompts or
blocks.

**Files (new):**

- `hooks/hooks.json` at the repo (plugin) root, in the default location (F20).
  - One `SessionStart` entry with matcher `startup|resume`. Those are the two fresh-process starts; `clear`
    and `compact` would re-nag mid-session.
  - The command runs the hook script via `${CLAUDE_PLUGIN_ROOT}`. This is reliable for hook commands
    (F21), unlike MCP `command` (F6).
  - An explicit `timeout` of 10 s, so a hung script can never hold Claude's first response the way F9
    describes.
- One POSIX `sh` script under `hooks/`, executable. The Implementor names it.
- No plugin.json or marketplace.json change on the first attempt (see the H.6 fallback below).

**Inputs.**

- **Running version:** `.version` of the plugin.json beside the script. Locate it from
  `$CLAUDE_PLUGIN_ROOT` or from the script's own location, never from the working directory. The hook's
  cwd is the user's project.
- **Stamp:** `$HOME/.claude/claude-tui-line/bin/.plugin-version`. This path is **literal**.
  - `CLAUDE_PLUGIN_DATA` **is** set in a hook's environment (F4) and must be ignored. Snapshot mode
    always pins `BIN_DIR` there (§D.2.1).
  - Reading the stamp through that variable would look in `~/.claude/plugins/data/…`, where nothing is
    ever written.

**Behaviour.** Exit 0 always. Nothing on stderr in any case.

| Condition | Output |
|---|---|
| stamp absent (never set up, clone install, or pre-stamp install) | nothing |
| stamp unreadable or empty | nothing |
| plugin.json missing or unreadable, `.version` null or empty, or `jq` not on PATH | nothing |
| stamp == running version (whitespace-trimmed) | nothing |
| stamp ≠ running version | one JSON object on stdout (below) |

- A difference in either direction notifies. A downgrade to an older plugin is still a mismatch, and setup
  handles both directions the same way.
- No semver comparison is needed.

**Notice content.** One line naming both versions and the command, e.g. (illustrative):
`claude-tui-line: plugin is 0.5.0 but installed binaries are 0.4.0 — run /claude-tui-line:setup to rebuild`.

**Channel, decided by H.6:**

- **Primary:** the JSON top-level `systemMessage` carries the notice, and nothing else is emitted.
  - It is user-visible by documentation (F20), costs zero context tokens, and does not steer Claude's
    first reply.
  - Ship this if H.6b shows the line in the TUI.
- **Fallback 1:** if `systemMessage` is not shown at SessionStart, emit
  `hookSpecificOutput.additionalContext` instead.
  - Wording: a fact plus one instruction, e.g. "tell the user once, briefly, at the start of your first
    reply".
  - It reaches the user only through Claude, but it is proven to reach Claude: this session receives
    SessionStart additionalContext from other plugins.
- **Fallback 2:** if H.6a shows the hook never runs from an installed plugin, add
  `"hooks": "./hooks/hooks.json"` to plugin.json (and to the marketplace.json entry, per the F6 pattern)
  and re-run H.6a.
  - If it still never fires, do **not** ship the hook. Delete `hooks/`, and option (a) applies: the README
    and setup's report already tell the user to re-run setup after an update.
  - Report this as NEEDS-ARCHITECT so the spec records it. Do not improvise a third channel.
- **Never both channels.** A doubled notice is noise.

**Scope guards.**

- The hook does not detect "plugin installed but setup never run" (stamp absent). That case is silent by
  design: an absent stamp is ambiguous (clone install, older install.sh, custom `BIN_DIR`), and the README
  flow already ends with setup.
- If wanted later, that is a separate decision. It is out of scope here.
- No `claude` CLI call, no `dotnet`, no `git`, no network, and no writes, ever. The script reads two files.

### D.12 Release: bump the plugin version when shipping (rev 3, review finding 8)

**Why.** Claude Code keys the plugin cache and `/plugin update` on the manifest `version` (F3). If this
change ships at 0.4.0, existing 0.4.0 installs never receive the new setup.md, install.sh or `hooks/`.
The fix would reach only fresh installs.

**Requirements.**

- The release that ships this change sets a new `version` in `.claude-plugin/plugin.json`. The number is
  user decision U5 (§I).
- If `.claude-plugin/marketplace.json` carries a version for the `claude-tui-line` entry, set it to the
  same value. Otherwise leave it untouched.
- Set the three product csproj `Version` properties (F15) to the same value. This follows the 0.4.0
  precedent, where all four were in step.
  - Keeping them equal keeps `claude-tui-line --version` truthful.
  - The csproj version is not what the stamp or hook compare (§J.5); this is only the repo's release
    convention.
- **When.** At release time, as a separate commit on the same branch, once the user has chosen the number.
  The implementation diff itself does not need it.
- Tests are version-agnostic. G.2's `0.4.0` and G.6's `0.4.1` come from the snapshot **fixture's**
  plugin.json, not the repo's. If the Implementor built the fixture by copying the repo's plugin.json, the
  assertions must read the version from the fixture rather than hard-coding it. Check this when the bump
  lands.
- **Effect on the real machine (Folder marketplace).** After the bump, a `/plugin update` refreshes the
  cache to the new version. Setup still hands off to the clone (§D.3), and clone users get no hook notice
  (no stamp, E.14).

## E. Edge cases

| # | Case | Required behaviour |
|---|---|---|
| E.1 | Snapshot, the marketplace is GitHub-sourced, nothing installed yet | snapshot mode, full plan, ledger `origin` |
| E.2 | Snapshot, marketplace `Folder(<clone with .git>)` (Jim's machine) | handoff (§D.3); the clone runs as today plus §D.10 |
| E.3 | Snapshot, marketplace `Folder(<dir without .git>)` | snapshot mode plus an info line |
| E.4 | Snapshot after a plugin update (new version dir, stamp = old version) | c7 stale, so a rebuild is planned; the old version dir is never referenced |
| E.5 | Old snapshot dir deleted by orphan cleanup | statusLine and MCP keep working (pinned `$BIN_DIR`) |
| E.6 | `CLAUDE_PLUGIN_DATA` set in the env during a snapshot run | ignored; nothing is written under it |
| E.7 | A connected user-scope MCP registration already points at a clone wrapper | left alone (it resolves the same `$BIN_DIR`) |
| E.8 | `dotnet` absent | step 2 fails as today; setup relays it and stops; nothing is written |
| E.9 | `claude plugin uninstall` without revert | the statusLine and MCP keep working (binaries are outside `CLAUDE_PLUGIN_DATA`); documented in README |
| E.10 | Two setups run concurrently | not handled (existing race on staging); out of scope |
| E.11 | `CLAUDE_CONFIG_DIR` relocates `~/.claude` | not supported: no snapshot detection, so the no-`.git` refusal applies (existing limitation; install.sh hard-codes `$HOME/.claude` throughout) |
| E.12 | The plan goes stale between dry-run and apply | apply does what its own checks find and prints it; setup relays the apply output |
| E.13 | (rev 2) Snapshot install, then the user switches to a clone; the clone's git-log check would call the snapshot-built binaries fresh | the present stamp makes clone c7 stale (§D.5), so the clone rebuilds and removes the stamp; the hook goes silent |
| E.14 | (rev 2) Clone user whose plugin cache lags the checkout (Jim: cache 0.3.0, clone 0.4.0) | no stamp, so the hook is silent; no loop between the notice and an `already installed` setup |
| E.15 | (rev 2) An older CLI prints `Source: Directory (…)` | accepted (§D.10); byte-identical to today |
| E.16 | (rev 2) Hook runs with `CLAUDE_PLUGIN_DATA` pointing at a dir holding a different `.plugin-version` | ignored; only the literal pinned path is read |
| E.17 | (rev 2) Clone install with a custom `CLAUDE_PLUGIN_DATA` after an earlier snapshot install | the stamp left at the pinned path is not removed (clone `BIN_DIR` differs), so the hook may notify. Accepted: this needs a deliberate env override on top of a mode switch; deleting the file clears it |
| E.18 | (rev 2) Plugin downgraded below the stamp | the hook notifies (any mismatch); setup rebuilds to the running version |

## F. What must not change (clone path)

(rev 2) "Identical" is measured against base 7c48b95 **on a CLI that prints `Source: Directory`** (H.4). On
the current CLI's `Folder` label, base is broken (§A.5), and the §D.10 fix is the intended difference. The
§D.5 stamp removal is a no-op unless a snapshot install happened first.

From a clone, with no `--dry-run`, these stay identical:

- the output
- the prompts
- the exit codes
- the writes:
  - `settings.json`
  - the ledger
  - `claude mcp add` target `$REPO_ROOT/bin/claude-tui-line-mcp`
  - `marketplace add $REPO_ROOT`
  - `plugin install -s user [-y]`
  - the `--allow-marketplace-replace` rules (install.sh:516-576)
  - the `chmod +x` on the wrapper

Also unchanged:

- The no-TTY refusal path, including its exit 1.
- Freshness via `git log` when no stamp is present.
- `BIN_DIR` honours `CLAUDE_PLUGIN_DATA` as today.

## G. Test plan: new `tools/check-install.sh`

There are no install.sh tests today (F12). Add one standalone shell check. It follows `tools/check-*.sh`
conventions and is added to `tools/check-all.sh`'s explicit list with the same `|| status=1` pattern (F17).
It needs no .NET toolchain because dotnet is stubbed.

**Sandbox rules.**

- `T=$(mktemp -d)`, then abort if empty.
- `HOME="$T/home"`. install.sh is entirely `$HOME`-relative.
- PATH gets `$T/stubs` first.
- Every git write is `git -C "$T/…"`.
- Trap-remove `$T`.

**Stubs.**

- **`dotnet`**
  - `--version` returns `10.0.100`.
  - `publish <csproj> … -o DIR` creates executable `DIR/claude-tui-line` (CLI csproj), or executable
    `DIR/claude-tui-line-mcp` plus `DIR/claude-tui-line-mcp.dll` (MCP csproj).
- **`claude`**
  - Keeps state files under `$T/state`.
  - Appends every argv to `$T/claude.log`.
  - Emulates `mcp list`: prints `claude-tui-line: <cmd> - ✓ Connected` iff `<cmd>` is executable,
    otherwise a failed line.
  - Emulates `mcp add/remove`, `plugin marketplace list/add/remove` and `plugin install`.
  - (rev 2) The marketplace-list block format follows F18:
    - a `❯ <name>` line, then a `Source: <Label> (<arg>)` line
    - a local add prints label `Folder` by default
    - a stub knob (e.g. an env var read by the stub) switches it to `Directory`, for G.17 and H.4
    - a GitHub entry prints `Source: GitHub (<owner>/<repo>)`
- `jq` and `git` are real.

**Fixtures.**

- Snapshot: `$HOME/.claude/plugins/cache/claude-tui-line/claude-tui-line/0.4.0/`, with no `.git`. It holds
  the working-tree `install.sh`, `.claude-plugin/plugin.json`, `bin/claude-tui-line-mcp` and (rev 2)
  `hooks/`. That is sufficient because dotnet is stubbed.
- Clone: `$T/clone`, holding the same files plus `git -C "$T/clone" init` and one commit.

**Cases.** Each asserts its exit code and the listed state.

| # | Setup | Assert |
|---|---|---|
| G.1 | snapshot, GitHub marketplace, `--dry-run` | exit 0; output has `this run would have:`; there is a statusLine line and an MCP line naming `$HOME/.claude/claude-tui-line/bin/claude-tui-line-mcp`; there is NO plugin-registration line and NO chmod line |
| G.2 | G.1 then `--non-interactive`, with `CLAUDE_PLUGIN_DATA=$T/pd` exported | exit 0; binaries are in `$HOME/.claude/claude-tui-line/bin`; `$T/pd` does not exist; `.plugin-version` = `0.4.0`; statusLine.command = pinned CLI path; claude.log has `mcp add -s user claude-tui-line <pinned mcp path>` and no `plugin marketplace add/remove` or `plugin install`; the ledger has exactly one `origin` |
| G.3 | snapshot `--dry-run` writes nothing | before/after listing of `$HOME` (excluding `backups/` dir creation) is identical; claude.log has only `list` calls |
| G.4 | G.2 then `--dry-run` again | exit 0, `already installed` |
| G.5 | after G.2 | the snapshot path string is absent from settings.json, ledger.jsonl and the `mcp add` argv (§D.6) |
| G.6 | after G.2, bump the snapshot plugin.json to 0.4.1, `--dry-run` | plan includes build/deploy (stale stamp) |
| G.7 | after G.2, `rm -rf` the snapshot dir | the statusLine command and the MCP target are still executable; stub `mcp list` says Connected |
| G.8 | snapshot, marketplace `Folder($T/clone)`, `--dry-run` | output has the handoff line and the clone's `running from a git checkout at $T/clone`; exit 0 |
| G.9 | snapshot, marketplace `Folder($T/nogit)` (exists, no .git) | no handoff; snapshot mode; info line |
| G.10 | clone, fresh sandbox, `--non-interactive --allow-marketplace-replace` (stub label `Folder`) | exit 0; `mcp add` target = `$T/clone/bin/claude-tui-line-mcp`; `marketplace add $T/clone` exactly once and **no** `marketplace remove` (§D.10); `plugin install … -s user -y`; wrapper chmod'd; **no** `.plugin-version` in `$BIN_DIR` (rev 2); verify passes |
| G.11 | clone, no TTY, no flags (stdin `</dev/null`) | exit 1; `refusing to write anything` plus the plan list; nothing written |
| G.12 | dir outside plugins without `.git` | exit 1; today's no-`.git` message |
| G.13 | snapshot, with the dotnet stub's no-dotnet knob set (e.g. `STUB_NO_DOTNET=1`), so the stub exits 127 on every call, including `--version`. (rev 3, review finding 7: removing the stub from PATH cannot be done when a real `dotnet` sits later on PATH; the knob drives the same `dotnet --version` failure branch.) | exit 1; install.sh's existing dotnet-missing failure line; nothing written |
| G.14 | snapshot: record the permission bits of the snapshot's `bin/claude-tui-line-mcp` (fixture created non-executable) before G.2 | bits unchanged after G.2 (no chmod into the snapshot) |
| G.15 | (rev 2) G.10 then `--dry-run` from the clone | exit 0; `already installed`. On base this was unreachable with label `Folder` |
| G.16 | (rev 2) after G.2 (stamp present), run the clone with `--non-interactive --allow-marketplace-replace` | the clone's c7 reports stale (snapshot-built), rebuilds, and afterwards `.plugin-version` is absent (§D.5, E.13) |
| G.17 | (rev 2) G.10 with stub label `Directory` | same assertions as G.10 (§D.10, both labels) |
| G.18 | (rev 2) hook: stamp `0.4.0`, `CLAUDE_PLUGIN_ROOT` = snapshot fixture at 0.4.0 | exit 0; stdout and stderr empty |
| G.19 | (rev 2) hook: stamp `0.4.0`, fixture plugin.json `0.4.1` | exit 0; stdout is exactly one valid JSON object (checked with `jq -e`) whose notice field (per the H.6 outcome) contains `0.4.1`, `0.4.0` and `/claude-tui-line:setup`; stderr empty |
| G.20 | (rev 2) hook: no stamp; then an empty stamp; then a missing plugin.json; then `jq` absent from PATH | each: exit 0, stdout and stderr empty |
| G.21 | (rev 2) hook: pinned stamp = running version, `CLAUDE_PLUGIN_DATA=$T/pd` with `$T/pd/bin/.plugin-version` = `9.9.9` | silent (E.16) |
| G.22 | (rev 2) hook: run with cwd = an unrelated temp dir | same results as G.18 and G.19 (no cwd dependence) |
| G.24 | (rev 3) the stub lists `❯ my-claude-tui-line` / `Source: Folder ($T/decoy)` **before** the real entry. `$T/decoy` has `.git` and an executable `install.sh` that only touches `$T/decoy-ran`. The real `claude-tui-line` entry is GitHub-sourced. Run a snapshot `--dry-run` | exit 0; snapshot mode; no handoff line; `$T/decoy-ran` absent |
| G.23 | (rev 2) `hooks/hooks.json` | valid JSON (`jq -e`); has exactly one SessionStart entry with matcher `startup\|resume` and a numeric `timeout` ≤ 10; the script it names exists in the repo and is executable |

**No-regression for clone mode.** One-time reviewer evidence (H.4), not a standing test: run base
7c48b95's install.sh and the new one through G.10's sandbox **with stub label `Directory`**, then diff the
end state. (rev 2) Base cannot pass with `Folder`, so that label is not a parity baseline.

The existing C# suites (`tests/`) are unaffected: no `src/` change.

## H. NEEDS-EVIDENCE

These items go to the Implementor. All are sandboxed per the rules in §G.

> rev 2 status:
> - H.1: **PASS** (F19)
> - H.2: **DONE** (F18), and it found the §A.5 bug
> - H.3, H.4, H.5 and the new H.6: still open; none of them gates the start of implementation
> - H.6 decides the §D.11 channel
> - H.3 and H.6 are phase acceptance
> - H.4 is reviewer evidence
> - H.5 is user acceptance

- **H.1: does a build from a `.git`-less export succeed?** (rev 2: PASS.) This gates the design.

  ```sh
  T=$(mktemp -d); [ -n "$T" ] || exit 1
  git -C /Users/jimcline/git/repos/claude-tui-line archive HEAD | tar -x -C "$T"
  dotnet publish "$T/src/ClaudeTuiLine/ClaudeTuiLine.csproj" -c Release -o "$T/o1" > "$T/cli.log" 2>&1; echo cli=$?
  dotnet publish "$T/src/ClaudeTuiLineMcp/ClaudeTuiLineMcp.csproj" -c Release -o "$T/o2" > "$T/mcp.log" 2>&1; echo mcp=$?
  grep -niE 'git|sourcelink|error' "$T"/*.log | head -20
  ```

  - Both 0: snapshot mode is viable as specced.
  - Either fails on a git dependency: back to the Architect.
  - Predicted to pass: F15 shows no build-time git use. The item still gates the work, because AOT/ILC
    behaviour outside a git tree is unobserved.
- **H.2: real `claude plugin marketplace list` output.** Read-only. (rev 2: done. The label is `Folder`, and the 377 check is broken; see F18 and §D.10.)
  - Paste the `claude-tui-line` block (Directory source) and the `ponytail` block (GitHub source) verbatim.
  - This decides the §D.3 path extraction and the G stub format.
  - If the Directory block is not literally `Source: Directory (<path>)`, the existing install.sh:377
    check is already broken. Report that too.
- **H.3: end-to-end snapshot run with the real `claude` and real `dotnet`, under a sandbox HOME.** Phase acceptance.
  - Steps:

    ```sh
    T=$(mktemp -d); [ -n "$T" ] || exit 1; mkdir -p "$T/home" "$T/export"
    git -C /Users/jimcline/git/repos/claude-tui-line archive HEAD | tar -x -C "$T/export"
    cp <branch working tree>/install.sh "$T/export/install.sh"
    export NUGET_PACKAGES="$HOME/.nuget/packages"   # reuse cache; set BEFORE HOME changes
    HOME="$T/home" claude plugin marketplace add "$T/export"   # Directory source WITHOUT .git → no handoff
    HOME="$T/home" claude plugin install claude-tui-line@claude-tui-line -s user
    R=$(ls -d "$T"/home/.claude/plugins/cache/claude-tui-line/claude-tui-line/*/); echo "$R"
    HOME="$T/home" "$R/install.sh" --dry-run;          echo dry=$?
    HOME="$T/home" "$R/install.sh" --non-interactive;  echo apply=$?
    HOME="$T/home" claude mcp list | grep claude-tui-line
    jq .statusLine "$T/home/.claude/settings.json"
    ```

  - Then delete `$R` and re-run `claude mcp list`. The server must still be Connected.
  - Report: the exit codes, whether `$R` is under `$T/home/.claude/plugins/cache`, and whether the build
    could write inside `$R`.
  - If `claude` refuses to run under a substituted HOME, report that. The user then runs the slash-command
    flow (H.5).
  - This never touches the real `~/.claude`.
- **H.4: clone-path parity (reviewer evidence).**
  - Run G.10's sandbox twice, both with the stub's marketplace label set to **`Directory`** (rev 2; base
    is broken under `Folder`, §A.5):
    - once with `git -C <repo> show 7c48b95:install.sh`
    - once with the new install.sh
  - Diff these, ignoring timestamps (rev 2: no `.plugin-version` exclusion is needed, since clone mode
    never writes one):
    - stdout
    - `claude.log`
    - settings.json
    - the ledger
    - the `$BIN_DIR` listing
  - The diff must be empty.
- **H.5: slash-command acceptance by the user, on the real machine.**
  - Run `/claude-tui-line:setup`.
  - Expect the handoff to the clone (E.2), then the plan, AskUserQuestion, apply and the preview.
  - This exercises the setup.md flow and the handoff. The pure-snapshot slash flow is covered by H.3, plus
    H.5 on a GitHub-sourced machine if one is available.
  - (rev 2) With §D.10, the handed-off clone run on the real machine now passes c6_plugin and should
    verify clean. Expect `already installed` if the binaries are fresh by git log.
- **H.6 (rev 2): does the §D.11 hook fire from an installed plugin, and is its notice visible?** Run this on
  the implementation branch, after G.18–G.23 pass. It never touches the real `~/.claude`.

  ```sh
  T=$(mktemp -d); [ -n "$T" ] || exit 1; mkdir -p "$T/home/.claude/claude-tui-line/bin" "$T/export"
  git -C /Users/jimcline/git/repos/claude-tui-line archive <impl-branch> | tar -x -C "$T/export"
  printf '0.0.1\n' > "$T/home/.claude/claude-tui-line/bin/.plugin-version"     # deliberately stale
  HOME="$T/home" claude plugin marketplace add "$T/export"
  HOME="$T/home" claude plugin install claude-tui-line@claude-tui-line -s user
  # H.6a — does it fire (non-interactive)?
  HOME="$T/home" claude -p "reply ok" --output-format stream-json --verbose > "$T/s.jsonl" 2>&1; echo rc=$?
  grep -n 'claude-tui-line' "$T/s.jsonl" | head -20
  HOME="$T/home" claude -p "reply ok" --debug > "$T/d.log" 2>&1; grep -niE 'SessionStart|hook' "$T/d.log" | head -20
  ```

  - **H.6a.** Report whether the hook ran and whether its notice JSON was accepted (no hook-error line).
    - Ran and accepted: proceed to H.6b.
    - Never ran: apply Fallback 2 in §D.11, then re-run H.6a.
    - Still never runs: do not ship the hook.
  - **H.6b (visibility, interactive).**
    - Start `HOME="$T/home" claude` inside a detached tmux session, wait about 8 s, then
      `tmux capture-pane -p`.
    - Then kill the tmux session and confirm no stray `claude` process is left for that HOME.
    - Report whether the notice text appears in the pane.
    - If first-run onboarding in the fresh HOME blocks the view, hand the same command to the user to run
      by eye.
    - Decides the channel:
      - Visible: keep `systemMessage` (Primary).
      - Not visible: switch to `additionalContext` (Fallback 1), re-run H.6b, and report whether Claude's
        first reply mentions it.
  - **H.6c.** `rm "$T/home/.claude/claude-tui-line/bin/.plugin-version"`, re-run H.6a. No notice and no
    hook error.
  - If `claude` will not run under a substituted HOME (also the H.3 risk), report that. The fallback is the
    user's eye check: on the real machine, after a snapshot install, bump the version.

## I. Decisions for the user

> rev 2: all three are decided by the user (via AskUserQuestion, relayed in the rev 2 brief):
> - U1 = **(b) a notify-only hook**. This is not the architect's recommendation, (a); it is specced in §D.11, with fallback to (a) if H.6 cannot prove a channel.
> - U2 = **automatic handoff** (§D.3).
> - U3 = **one "apply this plan" approval** (§D.7).
>
> The original options are kept below for the record.
>
> **rev 3 — U5 (open, needed only at release, §D.12): the version number to ship.**
> - **0.5.0 [recommended]:** this adds a feature (snapshot install, startup notice) and changes the setup
>   flow, which in 0.x is a minor bump.
> - **0.4.1:** treats it as a fix release, mainly the §D.10 clone-path fix.
>
> Either value triggers the update (F3). The choice is about what the number communicates.

1. **Auto-rebuild after a plugin update.**
   - Options:
     - (a) No hook: the README says to re-run setup after `/plugin update`. **[recommended]**
     - (b) A notify-only SessionStart hook: compares `.plugin-version` with plugin.json (two file reads, no dotnet) and tells the user to run setup. Hook stdout reaches Claude, not the user (F9), so its user-visible form needs evidence.
     - (c) An auto-build SessionStart hook. **Not recommended:**
       - Claude's first response waits on a multi-minute AOT build (F9).
       - It deploys without consent.
       - It fails on every session without dotnet.
       - Concurrent sessions race on staging.
       - The hook env carries `CLAUDE_PLUGIN_DATA` (F4).
   - The brief asked whether a hook is worth it. Verdict: not as auto-build; notify-only is a cheap follow-up if wanted.
2. **Snapshot over a clone marketplace.**
   - Options:
     - Automatic handoff (§D.3). **[recommended]**
     - Refuse and name the clone, which means setup stops and the user runs it by hand.
3. **Approval granularity in setup.**
   - Options:
     - One "apply this plan" approval. **[recommended]**
     - Per-action approval. This needs new skip flags, e.g. install binaries but leave the statusLine alone.
4. **(Future, out of scope) prebuilt release binaries.** These would remove the .NET SDK prerequisite for
   marketplace users. Flagged only.

## J. Rejected alternatives

1. **Plugin-declared MCP (`.mcp.json` or plugin.json `mcpServers` with `${CLAUDE_PLUGIN_ROOT}`).**
   - (a) Clone mode installs the plugin too, so the plugin server would run *alongside* the user-scope one.
     That means duplicate tools, unless clone mode drops `claude mcp add`, which breaks "clone path unchanged".
   - (b) Tool names would change to `mcp__plugin_claude-tui-line_claude-tui-line__*` (F5), breaking existing
     permission allowlists.
   - (c) A root `.mcp.json` doubles as the clone's project `.mcp.json`, where the variable is unset (F7).
   - (d) `command` expansion fails silently about 1 time in 3 (F6).
   - (e) The plugin server's env carries `CLAUDE_PLUGIN_DATA`, so the wrapper and `CliLocator` would resolve
     `~/.claude/plugins/data/…/bin`. That path is deleted on uninstall (F4).
   - A user-scope registration at a stable absolute path has none of these problems.
2. **`CLAUDE_PLUGIN_DATA` as the snapshot `BIN_DIR`.**
   - It is deleted on uninstall (F4), which would leave settings.json pointing at nothing.
   - The user-scope MCP server and `CliLocator` never see that variable (F11).
   - It is undocumented whether the Bash tool sees it (F4).
3. **Detect a snapshot by `.git` absence.** Wrong both ways (F2): skillsmith's cache has `.git`, and
   `claude-plugins-official` has none.
4. **Relocate staging to `mktemp` or `$BIN_DIR/..`.**
   - `dotnet publish` still writes `obj/` and `bin/` into `$REPO_ROOT/src/*`, so it buys nothing.
   - Redirecting those needs `--artifacts-path` or MSBuild props, an untested toolchain change.
   - Revisit if H.3 shows the snapshot is not writable.
5. **Version detection via `claude-tui-line --version`.**
   - The flag exists (F15), but it reports the csproj `Version`. That is a second version source, kept in
     step with plugin.json only by hand.
   - Claude Code keys updates on the plugin.json version (F3). The stamp records exactly that key at build
     time, and costs one file write.

## K. Risks for the Implementor

- **Statement ordering.** `BIN_DIR`, `cli_bin`, `mcp_bin` and `target_status_line` are computed at the top
  (install.sh:15-30). Mode detection and the `CLAUDE_PLUGIN_DATA` unset must precede them. Moving those lines
  must not change clone-mode values (H.4 catches this).
- **One plan listing.** `--dry-run` and the no-TTY refusal must print the plan from one place. A second copy
  will drift. This is rule 1 of the repo's DRY practice.
- **`marketplace_block` parsing is a dependency on CLI output format.** It already exists at install.sh:377.
  The handoff adds a second consumer, so validate it against H.2 before coding.
- **Do not let `set -e` abort on a missing `claude` during the handoff lookup.** Guard it.
- **The stamp write must use the same temp-then-rename as the binaries.** A torn stamp would read as a
  version mismatch, which is safe but causes a needless rebuild.
- (rev 2) **One local-source recogniser (§D.10).**
  - Sites 377, 528, 604 and the handoff must all route through it.
  - Grep the finished install.sh for `Source: Directory` and `Source: Folder`: neither literal may appear
    outside that one implementation.
- (rev 2) **528 lives inside the S7 replace logic.** Swap only the predicate. The surrounding prompt and
  opt-in structure (S7: `--non-interactive` alone is not consent for a marketplace replace) must not
  change. H.4 is run under `Directory`, so it shows any accidental change there.
- (rev 2) **Hook script hygiene.**
  - POSIX `sh`.
  - No `set -e` exit paths that could print to stderr.
  - Every failure branch ends in a silent exit 0.
  - Build the JSON with `jq` (already required, and checked present before use), so a version string can
    never break the quoting.
- (rev 2) **Do not read `CLAUDE_PLUGIN_DATA` in the hook.** It is set there (F4), and using it is the
  obvious-looking mistake.
