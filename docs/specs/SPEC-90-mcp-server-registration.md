# SPEC-90 — registering the MCP server so a Claude Code session can actually reach it

## 0. Goal

`src/ClaudeTuiLineMcp/` builds a working stdio MCP server exposing #84's
`get_config_schema` and the rest of §12.6's tool surface. **Nothing registers it**,
so no Claude Code session can call any of it. This spec defines the registration
and install story.

Design only. Nothing here is implemented.

---

## 1. A correction to the dispatch, up front

The dispatch states the precedent as:

> the CLI binary (claude-tui-line) is distributed via commands/setup.md, which
> builds it and symlinks ~/.local/bin/claude-tui-line to the repo's
> publish/claude-tui-line output.

**That is not what setup.md does, and no symlink is involved.** `commands/setup.md:34-36`:

```bash
BIN_DIR="${CLAUDE_PLUGIN_DATA:-$HOME/.claude/claude-tui-line}/bin"
dotnet publish "${CLAUDE_PLUGIN_ROOT}/src/ClaudeTuiLine/ClaudeTuiLine.csproj" \
  -c Release -o "$BIN_DIR"
```

The binary is **built at setup time into the plugin's data directory**. It is never
symlinked, never installed to `~/.local/bin`, and the repo's `publish/` output is
explicitly *not* the install target.

Three independent sources agree, which is why this correction is safe to act on:

1. `commands/setup.md:34-36` (above).
2. `.gitignore:5-7` — *"The install flow builds into `${CLAUDE_PLUGIN_DATA}`; this
   directory is a local development target only and must never be committed -- it
   is ~21MB of platform-specific binary that would be wrong on every machine but
   the one that built it."*
3. `src/ClaudeTuiLineMcp/CliLocator.cs:6-9` — *"The candidates mirror
   `commands/setup.md`'s own install-location convention
   (`${CLAUDE_PLUGIN_DATA:-$HOME/.claude/claude-tui-line}/bin/claude-tui-line`),
   since that is the one place `/claude-tui-line:setup` actually puts the binary."*

**This correction decides question 2 before it is asked**: there is an established,
documented, code-depended-upon install convention, and the MCP binary joins it
rather than inventing a second one.

---

## 2. The hazard this design is shaped around

`commands/setup.md:88-90`, on writing the CLI path into `settings.json`:

> Expand it to a real absolute path — **settings.json does not interpolate shell or
> plugin variables**, so a literal `${CLAUDE_PLUGIN_DATA}` or `$BIN_DIR` is written
> through verbatim and Claude Code runs a command that does not exist.

setup.md's entire step 5 exists to catch that one failure. Lines 116-121 describe
why it is so dangerous: *"An unset variable in a path expansion does not announce
itself; it quietly names a different, real, usually-worse directory."*

`.mcp.json` invites the identical failure. And the evidence about whether it
expands variables in `command` is weaker than it looks:

- The Claude Code docs state `${CLAUDE_PLUGIN_ROOT}` is supported in `command`.
- **Of the 22 `.mcp.json` files present on this machine, not one uses
  `${CLAUDE_PLUGIN_ROOT}` in `command`.** All four occurrences
  (`discord`, `fakechat`, `telegram`, `imessage`) put it in `args`, as the value
  after `--cwd`. Every stdio example resolves its `command` via `PATH`
  (`npx`, `bun`, `php`, `uvx`, `docker`, `mempalace-mcp`).

So the documented behaviour has zero local corroboration, and this repo has already
been burned once by assuming variable expansion in a config file.

**Design consequence: put the variable expansion in a shell script, where the
semantics are certain, rather than betting the install on a config-file expansion
we cannot yet confirm.** See §4.

---

## 3. Question 1 — the registration mechanism

**Use a `.mcp.json` at the repo root**, not an inline `mcpServers` key in
`plugin.json`.

Both work. `plugin.json` inline is real — `~/.claude/plugins/cache/mempalace/mempalace/3.6.0/plugin.json`
carries a working `"mcpServers"` block. `.mcp.json` is the canonical form per the
plugin reference and is what 22 of 23 local examples use. Prefer it because it keeps
the server declaration out of the plugin manifest, which this repo otherwise keeps
minimal (`plugin.json` currently declares only `commands`).

### 3.1 The top-level shape — a real ambiguity, resolved

Two incompatible shapes are both in production **in the official marketplace**:

```json
// bare map — terraform, playwright, github, linear, serena, firebase, example-plugin
{ "playwright": { "command": "npx", "args": ["@playwright/mcp@latest"] } }
```
```json
// wrapped — engram, discord, fakechat, telegram, imessage, github-pr-toolkit
{ "mcpServers": { "engram": { "type": "http", "url": "http://127.0.0.1:7433/" } } }
```

**Use the wrapped form.** It matches the documented shape, matches the project-level
`.mcp.json` convention, and matches the key name used for the `plugin.json` inline
variant, so the same block can move between the two files unchanged. The bare form
appears to be tolerated rather than specified.

E2 (§8) verifies the wrapped form actually loads before anything else is built on it.

---

## 4. The design

### 4.1 A committed launcher script, not a committed binary

Add **`bin/claude-tui-line-mcp`** to the repo — a small POSIX shell script,
committed with the executable bit set.

```sh
#!/bin/sh
# Resolves the install location written by /claude-tui-line:setup step 2. The
# expansion lives here rather than in .mcp.json because a config file that does not
# interpolate variables fails silently and unrecoverably -- the failure
# commands/setup.md:88-90 and step 5 exist to catch.
set -eu

BIN_DIR="${CLAUDE_PLUGIN_DATA:-$HOME/.claude/claude-tui-line}/bin"
SERVER="$BIN_DIR/claude-tui-line-mcp"

if [ ! -x "$SERVER" ]; then
  echo "claude-tui-line: MCP server not found at $SERVER" >&2
  echo "Run /claude-tui-line:setup to build it." >&2
  exit 1
fi

# CliLocator.cs:18-22 prefers $CLAUDE_PLUGIN_DATA/bin/claude-tui-line. Export the
# directory this script just resolved so the server's CLI lookup agrees with the
# launcher's, rather than depending on the server inheriting the same environment.
CLAUDE_PLUGIN_DATA="${CLAUDE_PLUGIN_DATA:-$HOME/.claude/claude-tui-line}"
export CLAUDE_PLUGIN_DATA

exec "$SERVER" "$@"
```

`BIN_DIR`'s definition is **byte-identical to `commands/setup.md:34`** by
requirement, not coincidence. If one changes, both must.

Why a script rather than pointing `.mcp.json` straight at the binary:

- It needs only `${CLAUDE_PLUGIN_ROOT}`, the *documented* variable, and only to
  locate a file that is committed and therefore certain to exist in the synced
  plugin copy.
- A missing binary produces a **named error on stderr**, not a silent no-tools
  session. Per the plugin docs, a failing stdio command surfaces its error.
- It is platform-independent text, so committing it violates nothing in
  `.gitignore:5-7`.
- It closes the environment gap in §4.3.

### 4.2 `.mcp.json` at the repo root

```json
{
  "mcpServers": {
    "claude-tui-line": {
      "command": "${CLAUDE_PLUGIN_ROOT}/bin/claude-tui-line-mcp"
    }
  }
}
```

No `args`, no `env`. The server takes no arguments (§12.6.3: `preview` takes its
width and never infers one, so there is nothing terminal-shaped to pass).

### 4.3 Why the launcher exports `CLAUDE_PLUGIN_DATA`

`CliLocator.cs:14-39` searches, in order:

1. `$CLAUDE_PLUGIN_DATA/bin/claude-tui-line`
2. `$HOME/.claude/claude-tui-line/bin/claude-tui-line`

There is a real gap: if `CLAUDE_PLUGIN_DATA` was **set** when setup ran but is
**unset** in the environment Claude Code spawns the MCP server with, candidate 1 is
skipped and candidate 2 points at a directory setup never wrote to. The CLI is
present on disk and the server reports `cli-not-found` anyway.

This is §12.6.2's rule — *"The server's environment is not the user's shell"* —
biting on the install path. The launcher resolving and exporting the directory makes
the server's lookup agree with the launcher's by construction instead of by
inheritance.

**This does not fully close it**: if `CLAUDE_PLUGIN_DATA` was set at setup time and
is unset at launch, the launcher computes the fallback path, finds no server binary
there, and exits with the §4.1 message. That is the correct outcome — a named,
actionable error rather than a silent misfire.

### 4.4 Question 4 — Fork 1 does not constrain the layout

It does not. `CliLocator` resolves the CLI by **absolute path from an environment
variable**, never relative to the MCP binary's own location. The two binaries are
therefore free to live anywhere independently.

They nonetheless end up **in the same `$BIN_DIR`** under this design, because both
follow the one install convention. That is a convenience, not a requirement, and no
code may start assuming co-location — doing so would silently re-introduce the
coupling Fork 1 avoided.

§12.6.6's `cli-not-found`, which names every path searched, remains the failure
mode and is unchanged by this spec.

---

## 5. Question 2 — do not commit the binary

**Do not commit `publish-mcp/`.** `.gitignore:5-7` already rules that publish output
is *"~21MB of platform-specific binary that would be wrong on every machine but the
one that built it."* The MCP output is the same kind of artifact — 37 files, ~7MB,
a macOS-arm64 apphost plus managed DLLs. Committing it would ship a binary that is
wrong for every Linux and Windows user of the marketplace.

Instead, **setup.md builds it**, exactly as it builds the CLI (§6).

### 5.1 A live hazard, independent of this spec

`publish-mcp/` is **not gitignored** — `git check-ignore` matches only `publish/`,
and `git status --porcelain --ignored` reports `?? publish-mcp/` (untracked), not
`!!` (ignored).

So ~7MB of platform-specific binaries is one `git add -A` away from being committed,
which is precisely what `.gitignore:5-7` forbids. **Add `publish-mcp/` to
`.gitignore` as part of this task**, regardless of the rest.

Better: replace the two entries with a single `publish*/` rule, so the next publish
target invented does not repeat this. Keep the existing explanatory comment.

---

## 6. Question 3 — what setup.md needs

### 6.1 A new step 2b, after step 2

Insert after `setup.md:50`, before step 3's backup:

> ## 2b. Build the MCP server
>
> The same `$BIN_DIR` from step 2 — one install location, so the launcher script and
> `CliLocator` agree about where things are.
>
> ```bash
> dotnet publish "${CLAUDE_PLUGIN_ROOT}/src/ClaudeTuiLineMcp/ClaudeTuiLineMcp.csproj" \
>   -c Release -o "$BIN_DIR"
> ```
>
> The result is `$BIN_DIR/claude-tui-line-mcp`. Confirm it exists and is executable.
> Report the exit code; if nonzero, show the error lines and stop.
>
> This publish is framework-dependent and must stay that way — see #83. Do not add
> `--self-contained` or a `-r` flag to make it match step 2's output.

**Placement is deliberate.** It must come after step 2 (so `$BIN_DIR` is defined and
proven) and before step 3 (whose "stop and report" contract means a later failure
leaves the statusline half-configured). Building both binaries before anything is
written to `settings.json` keeps the existing all-or-nothing property.

### 6.2 Step 6 gains a line

Step 6 (`setup.md:150-173`) reports what happens next. Add:

> - that the MCP tools (`get_config_schema` and the rest) need a **session restart or
>   `/reload-plugins`** before they appear, since a newly added MCP server is picked
>   up at plugin load rather than immediately

This is the answer to the second half of question 3: registration is **not** fully
automatic for an already-installed plugin. Per the plugin docs, servers start when
the plugin is enabled and are read at session startup; `/reload-plugins` picks up
changed configuration mid-session. A user who has claude-tui-line installed today
will not see the tools until one of those happens. E4 (§8) confirms which.

### 6.3 No verification step, deliberately

setup.md's step 5 verifies the statusline by running what it wrote. **Do not add the
analogous step for the MCP server**, because the command file cannot do it: the
session running `/claude-tui-line:setup` has already loaded its MCP servers, so the
newly registered one is not callable from inside that session (§6.2). A step that
appears to verify but cannot fail is worse than no step — the vacuous-test hazard,
and the same reasoning that put a real V4b in #83's spec.

The `$BIN_DIR/claude-tui-line-mcp` existence-and-executable check in step 2b is the
honest half, and it is what step 2b asserts.

### 6.4 `migrate.md` and `revert.md` — no change

Both operate on the statusline: `settings.json`'s `statusLine` key and the backup
ledger. MCP registration touches neither. `revert.md:48`'s only install-shaped line
concerns whether the CLI is installed, which this does not alter.

**`/claude-tui-line:revert` must NOT remove the MCP registration.** Revert restores
the user's *statusline*; silently unregistering a set of tools they did not ask to
lose would exceed what the command promises.

---

## 7. What must NOT change

- **`.gitignore`'s prohibition on committing publish output** (`:5-7`). This spec
  strengthens it (§5.1); it does not carve an exception.
- **`CliLocator.cs`'s search order.** Not modified. §4.3 makes the environment agree
  with it rather than editing it.
- **`src/ClaudeTuiLineMcp/`'s framework-dependent publish** (#83). Adding
  `--self-contained` would re-trip NETSDK1151.
- **setup.md steps 1, 3, 4, 5** — untouched. Step 2b is inserted between 2 and 3.
- **`plugin.json`'s existing `commands` key** and the rest of the manifest.
- **§12.6.6's `cli-not-found` contract.**
- **setup.md's stop-at-first-failure contract** (`:7-9`).

---

## 8. NEEDS-EVIDENCE — implementor-tier, all blocking

Each states what to run and what each outcome decides. **E1 and E2 gate the whole
design** and should be run first, before any file is written.

- **E1 — does `.mcp.json` expand `${CLAUDE_PLUGIN_ROOT}` in `command`?**
  The docs say yes; zero of 22 local `.mcp.json` files corroborate it (§2).
  *Method*: create the `.mcp.json` of §4.2 plus the launcher of §4.1, reload, and
  check whether the server starts.
  *If it expands* → design stands as written.
  *If it does not* → the fallback is an **absolute path with no variables**, written
  by setup.md into a user-level `.mcp.json` or via `claude mcp add`, in exact analogy
  to how setup.md step 4 writes an expanded absolute path into `settings.json`. This
  is a materially different install story — **report back before building it**.

- **E2 — which top-level shape does this Claude Code version accept?**
  Both bare and `mcpServers`-wrapped forms ship in the official marketplace (§3.1).
  *Method*: try the wrapped form of §4.2 first.
  *If it loads* → done. *If not* → use the bare form and report, since it means the
  documented shape is not the accepted one.

- **E3 — the executable bit.** Undocumented whether Claude Code requires it.
  *Method*: confirm the committed launcher has git mode `100755`
  (`git ls-files -s bin/claude-tui-line-mcp`), and that a fresh plugin install
  preserves it.
  *If the bit is lost through install* → the launcher must be invoked via an
  interpreter (`"command": "sh"`, `"args": ["${CLAUDE_PLUGIN_ROOT}/bin/..."]`),
  which also sidesteps E1's `command` question — report if so, as it may be the
  better primary design.

- **E4 — pickup for an already-installed plugin.** Does `/reload-plugins` pick up a
  newly added `.mcp.json`, or is a reinstall/restart required?
  *Method*: with the plugin installed, add `.mcp.json`, run `/reload-plugins`, check
  whether the tools appear.
  *Outcome decides the exact wording of §6.2*, which currently hedges across both.

- **E5 — does the plugin install sync include new top-level files?** The installed
  copy lives at `~/.claude/plugins/cache/claude-tui-line/claude-tui-line/0.1.0/`.
  *Method*: confirm `.mcp.json` and `bin/` appear there after install/update.
  *If the sync is allow-listed by manifest keys* → `plugin.json` may need to declare
  the paths, and the inline-`mcpServers` variant (§3) becomes preferable. **Report
  before working around it.**

---

## 9. Files to touch

| File | Change |
|---|---|
| `.mcp.json` | **new**, repo root, §4.2 |
| `bin/claude-tui-line-mcp` | **new**, committed, mode 755, §4.1 |
| `.gitignore` | add `publish-mcp/` (or fold both into `publish*/`), §5.1 |
| `commands/setup.md` | insert step 2b after `:50`; add the reload line to step 6 |
| `README.md` | a short note that the plugin ships MCP tools and that setup builds them |

`plugin.json` is **not** modified unless E5 forces it.

---

## 10. Verification

1. `git ls-files -s bin/claude-tui-line-mcp` reports mode `100755`.
2. `git check-ignore -v publish-mcp` now matches a rule.
3. `git status --porcelain` shows no `?? publish-mcp/`.
4. Launcher with the server absent → exits nonzero, message names the searched path.
5. Launcher with the server present → server starts and speaks MCP on stdio.
6. After install + reload, `get_config_schema` is callable **from a session**, and
   returns the same payload as `claude-tui-line --schema --json`. This is the bar the
   task exists to meet — a server that starts but whose tools are unreachable is the
   current bug, not a fix for it.
7. `tools/check-all.sh` passes.
8. A fresh `/claude-tui-line:setup` on a machine with no prior install produces both
   binaries in `$BIN_DIR`.

---

## 11. Confidence, escalation, and what I did not decide

**Confidence: high** on not committing the binary (§5, three concordant sources),
on the install location (§1), and on Fork 1 not constraining layout (§4.4).
**Medium** on the launcher-script mechanism — it is the right shape given §2's
uncertainty, but E1/E3 could redirect it toward the interpreter form, and E5 could
force the inline-`plugin.json` variant.

**I did not run anything**, so every claim about what Claude Code *does at runtime*
is documentation or observed-example evidence, not tested behaviour. That is exactly
what E1-E5 exist to convert.

**No Ultra-Advisor escalation recommended.** The blast radius is two new files, one
`.gitignore` line, and an additive setup step; nothing changes the render path or
existing install behaviour, and the failure mode is a server that does not start.

**Left to Jim, not decided here:**

1. **Should `/claude-tui-line:setup` be the thing that installs MCP tools at all?**
   A user who wants the statusline now also gets a build of a second binary and a set
   of ambient tools. The alternative is a separate opt-in command. I kept it in setup
   because the tools are useless without the CLI setup installs, and a second command
   is a second thing to forget — but that is a product call about default surface
   area, not a technical one.

2. **Whether the MCP server should be announced in step 5's user-facing summary**
   beyond the one line in §6.2. It is currently near-invisible to a user who does not
   read release notes.

---

## 12. Incidental — not part of this task

`commands/setup.md:9` (the doc-comment line numbering in `CliLocator.cs`) shows a
duplicated line number `9` in the file as read, suggesting the source has an
irregularity around `CliLocator.cs:9-10`. Cosmetic, unverified, and out of scope —
noted only so it is not mistaken for something this change introduced.
