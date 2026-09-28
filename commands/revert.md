---
description: Restore the statusline from the backup ledger — the original by default, or a named checkpoint
argument-hint: "[optional: a checkpoint timestamp; omit to restore the original]"
---

# Revert to a backed-up statusline

This is the escape hatch. It should work when the user is annoyed and does not want a conversation.
Requested checkpoint, if any: **$ARGUMENTS**

## 1. Read the ledger

Read `${CLAUDE_PLUGIN_ROOT}/docs/backup-ledger.md` for the format, then read
`~/.claude/claude-tui-line/backups/ledger.jsonl` — one entry per line, so parse it line by line.

**Missing, empty, or unreadable** → say so plainly and stop. Do not reconstruct a statusline from
memory, from this repo, or from anything in the conversation — a fabricated statusline the user
believes is theirs is worse than none. Tell them where the ledger would have been, and offer to
remove the `statusLine` key entirely so Claude Code falls back to no statusline. That is a clean,
honest state.

## 2. Choose the entry

- **No argument** → the `origin` entry. This is the default *because* it is the state before
  claude-tui-line ever touched the machine, and it stays reachable no matter how many migrates and
  edits have happened since. Do not substitute "the most recent checkpoint" as a convenience; the
  newest backup is frequently claude-tui-line's own command, and restoring that is not a revert.
- **An argument** → the `checkpoint` whose timestamp matches. Match liberally: the full ISO
  timestamp (`2026-08-13T04:12:07Z`), the compact form the artifact filenames use
  (`20260813-041207`), or an unambiguous prefix of either. The two forms differ, and the compact
  one is what the previous command printed in its report, so it is what the user will paste. If
  none matches, or a prefix matches more than one, list the available entries with their timestamps
  and their recorded `statusLine.command`, and ask. Do not guess at the nearest one.
- **No `origin` entry exists** (possible if the ledger was created by an older version) → say so,
  list every `checkpoint` with its timestamp and command, and let the user choose. Flag which ones
  point at a `claude-tui-line` binary, so they are choosing between recognisable things rather than
  between timestamps.

Show the user which entry you are about to restore, and its recorded command, before restoring it.

## 3. Verify the backup — before writing anything

Re-hash the **backup copies** in the backup directory and compare against the entry's recorded
SHA-256. This asks one question: is our own store intact? A mismatch means the backup is damaged.
**Stop, report both hashes, and do not restore from it.**

Do **not** hash the live `settings.json` against the entry. It is *supposed* to differ —
claude-tui-line is installed now and was not when the backup was taken — so that check fails every
single time and would turn the escape hatch into a command that never runs. See the table in
`docs/backup-ledger.md`.

This step comes before step 4 deliberately. Everything that can abort happens before anything that
writes, so an aborted revert leaves no trace in an append-only ledger that forbids removing one.

## 4. Checkpoint the current state

Yes, even now — and this is required, not a courtesy. Reverting is itself a change: follow the
ledger procedure and append a **`checkpoint`** for the state you are about to replace, so reverting
a revert is possible. The user may revert, dislike the old one more, and want today's config back.

This never consumes or removes the `origin`. The `origin` entry survives every operation.

If the checkpoint cannot be written, ask the user whether to proceed anyway rather than deciding
for them.

## 5. Restore

Restore the entry's `statusLine` value verbatim into `~/.claude/settings.json`, per **"Writing
`settings.json`"** in `${CLAUDE_PLUGIN_ROOT}/docs/backup-ledger.md`. Rule 1 *of that section* — the
file has a second numbered list, "The four rules", and this is not it — is the one that bites
here: the temptation is to copy the backed-up settings file wholesale, since a whole file is
right there and the user asked to go back. It reverts every unrelated setting they have changed
since, silently, while the report says one key was restored.

**A restored `statusLine` points at a path, not at contents.** So check the script the entry
recorded, at its original path, and handle three cases rather than one:

- **Missing** → restore the copy there as well, and say that you did. A restored command pointing
  at a file that no longer exists leaves the user with no statusline and no obvious cause.
- **Present, hash matches `scriptSha256`** → nothing to do. This is the ordinary case.
- **Present, hash differs** → the user has edited that script since the backup. Do not overwrite
  it and do not proceed silently. Say the script has changed, and ask whether they want the live
  version or the backed-up copy restored alongside the command.

The third case is the one worth spelling out, because it is the only one with no symptom: the
revert succeeds, the statusline renders, and it is simply not the statusline that was backed up.
Nothing about the result says so.

If the entry's `statusLine` was `null` — there genuinely was no statusline before — remove the key.
That is the correct restoration of that state.

## 6. Leave claude-tui-line's own things alone

Do **not** delete `~/.claude/claude-tui-line.json`. It is the user's work, it costs nothing to
keep, and it is what makes coming back cheap.

Ledger entries now carry a copy of that config as well, and **revert deliberately does not restore
it.** The two artifacts move independently: this command answers "put my old statusline back", not
"undo my layout work", and rolling their configuration back as a side effect of unpointing
`statusLine` would destroy work they never asked you to touch. `/claude-tui-line:edit` owns config
rollback. Say the copy exists, so they know the option is there. Do not delete the built binary either. Tell them both
are still there and that re-pointing `statusLine.command` at the binary brings it all back.

Do not delete anything in the backup directory, ever.

## 7. Confirm concretely

Print the restored command verbatim — a user reaching for revert is already having a bad time and
deserves to see exactly what they got back — then render it:

```bash
<the restored binary path> --fixture \
  | COLUMNS=80 <the restored command>
```

80 is written out rather than measured, and must stay that way (§12.1.1): you have no terminal, so
`tput cols` returns terminfo's static 80 while looking like it adapted to the user's window.

The payload is `--fixture`'s emission of §9.3.1's fixture with `cwd` set to the real working
directory (§12.7.2) — the same one `commands/migrate.md` and `commands/setup.md` use. §12.7.1 rules
this for consistency across all three commands, not because revert's own case was broken: an empty
render here already demonstrates the command ran, but three copies of a hand-rolled literal is how
a fourth gets written, so revert uses the flag too.

**Errors and empty output are not the same finding, and this is where that matters most.**

- **A nonzero exit, or anything on stderr** → a real finding about the backup. Say it plainly and
  now, not next session.
- **Empty stdout, exit 0** → *inconclusive*, and reporting it as damage is its own harm. This can
  still render at 80 columns rather than the user's width, and the fixture's `session_id`,
  `workspace.repo`, and similar fields are fixed placeholder values (§9.3.1), so a script keyed to
  the user's real session or PR still renders absent here.

  Say it produced no output, say why, and hand them the check rather than performing it: the
  one-liner above, run in their own terminal, where the real width and real session state exist. Do
  not chase it.

Either way, do not report a revert as successful on the strength of having written the file.

Then, briefly: what was restored, which ledger entry it came from, where the new checkpoint went,
and that `claude-tui-line.json` is untouched.
