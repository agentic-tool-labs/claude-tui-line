---
description: Build claude-tui-line and wire it up as this machine's statusline, backing up whatever is there now
---

# Set up claude-tui-line as the statusline

Work through these steps in order. **Stop and report** at the first one that fails rather than
carrying on — a half-installed statusline is worse than none, because Claude Code will run a
broken command once a second.

## 1. Run install.sh

```bash
"${CLAUDE_PLUGIN_ROOT}/install.sh"
```

This is the one implementation of the install — toolchain check, both builds, the backup ledger
entry, the `settings.json` write, and MCP/plugin registration all live there, not here. Run it and
watch its own prompts: it asks per action class (build, deploy, statusline rewrite, registration)
before writing anything, and it never edits `settings.json` without appending a ledger entry first.

**This requires a locally-sourced plugin — a git checkout, not a marketplace-synced snapshot.**
`${CLAUDE_PLUGIN_ROOT}` points at a snapshot when the plugin came from a marketplace, and
`install.sh` refuses to run against one (it would register the snapshot itself, which the next sync
overwrites). If it refuses for this reason, **show the user its refusal message verbatim** rather
than reinterpreting it — it names the actual checkout to run `install.sh` from instead.

If it exits non-zero, show the user its output and stop — do not retry pieces of it by hand or
re-derive what it does in prose.

If it succeeds, relay what it printed:

- which directory the binaries went to
- the ledger entry kind it appended (`origin` or `checkpoint`) and the backup path — and if it was
  a `checkpoint` rather than an `origin`, say so explicitly (see step 3 below for why that matters)
- whether it registered the MCP server and the plugin, or left either alone

Read `statusLine.command` back out of `~/.claude/settings.json` for use in step 2 — do not assume
it matches what `install.sh` printed; step 2 exists precisely because printed intent and written
fact can differ.

## 2. Show the user what they will get — by running what was actually written

Read the `statusLine.command` value out of `~/.claude/settings.json` and run **that string,
verbatim**:

```bash
<the exact binary path from settings.json> --fixture \
  | COLUMNS=80 <the exact command string now in settings.json>
```

80 is written out rather than measured, and must stay that way (§12.1.1): you have no terminal, so
`tput cols` returns terminfo's static 80 while looking like it adapted. Report "at 80 columns".

**This previews at §9.3.1's complete fixture, not a placeholder.** §12.7.1 rules that setup should
show the user the whole statusline rather than a `cwd` and a model name — the payload above is
`--fixture`'s emission of §9.3.1's fixture with `cwd` set to the real working directory (§12.7.2),
so every item that depends on workspace, session, usage, or editor-state fields renders instead of
sitting blank. Do not hand-roll a payload here — §9.3 requires exactly one synthetic payload, and a
second standing fixture in a command file is the defect §12.7.1 names, written once more.

**Not `${CLAUDE_PLUGIN_DATA}/bin/claude-tui-line`.** That path was already proven in step 1 and is
not what is in doubt. The one untested thing after step 1 is *the expansion* — whether the absolute
path written into settings.json is the path that exists. Testing the variable instead of the value
verifies the half that was never at risk: if the expansion is wrong, or a literal `${...}` was
written through unexpanded, this preview still renders perfectly, setup still reports success, and
the user gets a blank statusline with nothing anywhere pointing at why. Reading the value back out
costs one step and is the only version of this check that can fail when the install is broken.

`/claude-tui-line:revert` already verifies this way, printing and running the command it restored.
Two commands answering the same question differently is worse than either being wrong alone, and
this is the one that runs on every install.

Show the output, and **split the two failures the way `/claude-tui-line:revert` does** — same
observation, same conclusion, because this is the paragraph above's rule applied to itself:

- **A nonzero exit, or anything on stderr** → a real finding. Say it plainly and now.
- **Empty stdout, exit 0** → *inconclusive*, not a symptom. This can still render at 80 columns
  rather than the user's width, and the fixture's `cwd` is the real directory but its `session_id`,
  `workspace.repo`, and similar fields are fixed placeholder values (§9.3.1) — an item keyed to a
  real session or a real PR number still renders absent here. Say it produced no output, say why,
  and hand them the one-liner to run in their own terminal rather than chasing it here.

**This does not weaken what step 2 is for.** The failure it exists to catch — an unexpanded
variable or a wrong absolute path written into settings.json — cannot present as empty stdout,
because a command that does not exist does not run: the shell exits nonzero and puts "command not
found" on stderr, landing in the first bucket. The bucket that got softer is the one that never
held this check's quarry.

Then say that the payload is **synthetic**: `--fixture`'s output is invented data with the real
working directory substituted in, not the user's actual session, PR, or usage state, so the render
shows what the statusline looks like rather than what it says right now. Otherwise a correct install
reads as reporting live data, and the user's first act is questioning numbers that were never real —
which is the same reason empty stdout is inconclusive above, stated for the populated case instead
of the blank one.

## 3. Tell them what happens next

Report, briefly:

- where the binary lives
- where the backup went, and that `/claude-tui-line:revert` restores it — it reads the ledger at
  `~/.claude/claude-tui-line/backups/ledger.jsonl`, and targets the `origin` entry by default no
  matter how many changes come after.

  **If step 1 reported a `checkpoint` rather than an `origin`**, say so and give its timestamp. Bare
  `/claude-tui-line:revert` then does *not* restore what was just backed up — it restores the
  older `origin`, correctly and by design, and the user who read "your statusline is backed up"
  will not expect that. Tell them the argument to pass to get this state back. The default is
  right; leaving the difference unsaid is what is wrong
- that config goes in `~/.claude/settings.json`'s sibling, `~/.claude/claude-tui-line.json`, and
  that with no config file the built-in defaults apply
- that `$CLAUDE_TUI_LINE_CONFIG` overrides that path if they want to keep configs elsewhere

- that `/claude-tui-line:edit` changes the statusline in plain English, so they do not have to
  hand-write JSON to try something
- that the MCP tools (`get_config_schema` and the rest) need a **session restart or
  `/reload-plugins`** before they appear, since a newly added MCP server is picked up at plugin load
  rather than immediately

Do not invent configuration examples in your summary. Point them at the project README, which
documents every pane key, all eighteen built-in items, custom `command` items, derived items,
colours, and hyperlinks.
