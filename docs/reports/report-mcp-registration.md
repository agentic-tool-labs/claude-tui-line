# #90 MCP server registration — implementation report

Status: built and locally verified where safely testable; E1/E2/E4 could not be
live-verified without a disruptive, unauthorized action (see §"What I could not
verify" below) — reporting per the spec's own instruction rather than guessing.

## What I built (per SPEC-90-mcp-server-registration.md, files-to-touch table)

1. **`bin/claude-tui-line-mcp`** — new, committed-shape (staged, not committed —
   see "commit" note below), mode `100755`, content byte-identical to spec §4.1.
   `BIN_DIR` line is byte-identical to `commands/setup.md`'s step-2 line, by
   requirement.
2. **`.mcp.json`** (repo root) — new, wrapped `mcpServers` shape per spec §4.2,
   registers the server via the launcher, not the raw binary. Valid JSON
   (checked).
3. **`commands/setup.md`** — new step 2b inserted after step 2 / before step 3
   (publishes `ClaudeTuiLineMcp.csproj` framework-dependent into the same
   `$BIN_DIR`, per spec §6.1 verbatim); step 6 gained the restart/`/reload-plugins`
   line per spec §6.2. Steps 1/3/4/5 untouched.
4. **`migrate.md` / `revert.md`** — not touched, per spec §6.4 (revert must not
   unregister the MCP server).
5. **`.gitignore`** — `publish/` folded into `publish*/` (now also matches
   `publish-mcp/`, closing the live hazard in spec §5.1) — **plus one fix the
   spec did not anticipate**, see "Spec gap found" below.
6. **`README.md`** — new `## MCP tools` section between `### CLI` and
   `## Layout, briefly`, documenting `get_config_schema` and its `sections`
   filter (verified param name/values against `ConfigTools.cs:152-164` directly,
   not assumed), and the restart/`/reload-plugins` requirement.

`plugin.json` (`.claude-plugin/plugin.json`) — **not touched**, per E5's
resolution below (not forced).

## Spec gap found: `.gitignore`'s `bin/` rule blocks the very thing §4.1 asks to commit

`.gitignore` already had two overlapping rules — `bin/` (line 2, "Build output")
and `[Bb]in/` (line 22, .NET section) — both unanchored, so both match a
repo-root `bin/` directory, not just nested build output. Confirmed empirically:
before any fix, `git check-ignore -v bin/claude-tui-line-mcp` matched
`.gitignore:22:[Bb]in/`. The spec's own file-to-touch table for `.gitignore`
only mentions adding `publish-mcp/`/`publish*/` (§9); it does not mention this
conflict, and §7 "what must not change" lists `.gitignore`'s *publish*
prohibition, not this.

This is a real blocker, not a style nit — spec §4.1 explicitly requires the
launcher be "committed, mode 755," which is impossible while `bin/` is
unconditionally ignored. I made the minimal fix rather than stopping the whole
task on it: added `!/bin/` after the `.NET` section's `[Bb]in/` rule, which
un-ignores the repo-root `bin/` directory specifically without affecting nested
build output. Verified both directions:
- `git check-ignore -v bin/claude-tui-line-mcp` → **not ignored** (correct).
- `git check-ignore -v src/ClaudeTuiLine/bin/Debug/foo.dll` (synthetic nested
  build-output file) → **still ignored** via the same `[Bb]in/` rule (correct,
  unaffected).

Flagging this explicitly since it's a deviation beyond the spec's literal file
list — reviewer/architect should confirm the negation placement is the intended
fix rather than, say, scoping the .NET section's rule down instead.

## E1/E3/E5 — what I verified and how

Per the peer dispatch (which narrowed the blocking set to E1/E3/E5, not the
full E1-E5 the spec text names):

- **E3 — executable bit through install: RESOLVED, bit is preserved.**
  Gathered evidence from every other locally installed plugin with a shell
  script (not claude-tui-line, since its own cache install predates this
  change): `hy3d-mcp`'s three cached versions each carry `install.sh` with the
  executable bit intact after install (`file` reports "Bourne-Again shell
  script text executable" for the *cached copy*, matching the source). No
  evidence of the bit being stripped during install for any script-type
  artifact found on this machine. Per spec's own conditional in E3 ("if the bit
  is lost → interpreter form"), the bit is *not* lost, so the primary
  direct-command design in §4.2 stands — I did not switch to the interpreter
  form (`"command": "sh", "args": [...]`).

- **E5 — install sync scope: RESOLVED, not manifest-allow-listed; `plugin.json`
  change not forced.** Compared the actual installed cache copy
  (`~/.claude/plugins/cache/claude-tui-line/claude-tui-line/0.1.0/`) against
  both `git ls-files`'s tracked top-level entries and the raw repo-root working
  tree. The cache's top-level listing is **exactly** the git-tracked top-level
  entries (`bench, CAPTURE.md, commands, docs, LICENSE, README.md`, every
  tracked `SPEC-*.md`, `SPEC.md`, `src, STATUS.md, tests, tools, .claude-plugin,
  .gitignore`) plus one added `.in_use` marker — nothing more, nothing
  allow-listed, and no items absent that were present and tracked as of the
  cache's install/sync ref. (The cache is missing SPEC-84/85/85-ADDENDUM/88/90,
  but those are tracked at current HEAD — meaning the cache is simply *stale*,
  synced from an earlier commit, not filtered by a manifest key.) This shows
  the sync mechanism is "whatever's tracked in git at the ref used," a
  plain snapshot, not an allow-list — so a newly committed `.mcp.json` and
  `bin/claude-tui-line-mcp` will sync on the next install/update with **no**
  `plugin.json` change needed, confirming spec §9's "not modified unless E5
  forces it" — it does not force it.

## What I could not verify: E1 (and by extension E2/E4)

**E1 — does `.mcp.json` expand `${CLAUDE_PLUGIN_ROOT}` in `command`?** Still
genuinely unknown. Re-confirmed the spec's own finding with fresh evidence:
searched every `command` key across every locally installed plugin's
`plugin.json`/`.mcp.json` (not just claude-tui-line) — zero use
`${CLAUDE_PLUGIN_ROOT}` or any `${...}` variable in `command`; all resolve via
`PATH` (`mempalace-mcp`, etc.). No local corroboration exists for either
answer.

Resolving E1 for real requires Claude Code itself to load `.mcp.json` as part
of *this* plugin's registration and observe whether it substitutes the
variable before exec — that requires (a) committing these files, (b) forcing
the installed plugin cache to resync from the new commit, and (c) restarting or
`/reload-plugins`-ing a live session to pick the registration up. I did **not**
do this: committing is outside what this dispatch authorized ("do not commit
the compiled binaries... only the launcher/.mcp.json/doc edits should be
committed" implies committing happens, but doesn't say by me, and my own
contract is not to commit unless explicitly told), and resyncing +
reloading/restarting my own live interactive session is a disruptive action on
shared global state I'm not confident is mine to take unilaterally mid-task.

Per the spec's own instruction ("report back... rather than silently
improvising"), I'm reporting rather than guessing. **Recommendation**: once
these files are committed and the plugin cache resyncs, the *next fresh
session* that loads this plugin (e.g. cdtui-worker's own session start) is
itself the live E1/E2/E4 test — a fresh process naturally reloads plugin MCP
registrations, so no separate live-test step needs scheduling. If
`get_config_schema` is unreachable from that fresh session, that's E1 resolving
to "no" and the fallback in spec §8 (absolute path written by setup.md, in
exact analogy to step 4) is needed instead of the current `.mcp.json`.

## Verification run against spec §10

1. `git ls-files -s bin/claude-tui-line-mcp` → `100755 ...` ✅ (staged to check;
   not committed).
2. `git check-ignore -v publish-mcp` → matches `.gitignore:8:publish*/` ✅.
3. `git status --porcelain` → no `?? publish-mcp/` ✅.
4. Launcher with server absent (`CLAUDE_PLUGIN_DATA` pointed at an empty temp
   dir) → exits 1, message names the exact searched path ✅ (ran directly).
5. Launcher with server present (published a real `ClaudeTuiLineMcp.csproj`
   build into a temp `$BIN_DIR`, ran the launcher against it) → execs the real
   server; observed the MCP stdio transport start and shut down cleanly on
   stdin EOF (`Server (stream) (claude-tui-line-mcp) transport reading
   messages` / `Application started`) ✅.
6. Not verified — this is exactly E1/E4's live-session question above.
7. `tools/check-all.sh` — pending (dispatched, see below).
8. Not run — would require a from-scratch install, out of scope for this pass;
   step 2b's own commands were smoke-tested individually (item 5 above covers
   the MCP half; step 2's CLI publish already had standing coverage from #83).

- `dotnet test tests/ClaudeTuiLine.Tests` — **1444/1444 passed**, exit 0.
- `dotnet test tests/ClaudeTuiLineMcp.Tests` — **22/22 passed**, exit 0.
- `tools/check-all.sh` — **fails**, exit 1, but on the same two pre-existing
  categories reported in the #84/#85 reports, neither touched by this change:
  - `check-citations.sh`: ~28 dangling `§N.N` citations across SPEC files —
    same doc-registration-scope gap as before.
  - `check-doc-tokens.sh`: `README.md:162` — `border` accepted-token gap,
    unrelated to MCP registration.
  Not fixed — out of #90's file set.

## Scope

Touched exactly the files SPEC-90 §9 lists, plus the one `.gitignore` line
beyond its literal ask (flagged above). Did not touch `migrate.md`,
`revert.md`, `plugin.json`, or any file belonging to other in-flight/merged
tasks (SPEC-88, the untracked report files from prior rounds). Did not commit
anything — `bin/claude-tui-line-mcp` is `git add`-staged only, to let
`git ls-files -s` confirm its mode; everything else is an unstaged working-tree
edit or new file.
