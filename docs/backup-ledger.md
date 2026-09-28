# The backup ledger

**Every command that writes `settings.json` *or* `claude-tui-line.json` follows this. No
exceptions, no abbreviations.**

**Once per invocation, before the first write — not once per file.** A command that writes both
files takes *one* entry, and takes it before it writes either. Running the procedure a second time
mid-command is not extra safety: the second entry captures what the command has already written, so
it is a restore point for a half-finished state that existed for an instant and that nobody would
ever want back. See rule 4 — one entry, everything in it, taken before anything moves.

> That scope line used to read "writes to `settings.json`", and the narrower version hid a real
> defect for as long as it stood. `/claude-tui-line:edit` never touches `settings.json` — it edits
> `claude-tui-line.json` — yet it was instructed to checkpoint through this procedure, whose entry
> captured only the file `/edit` does not modify. So `/edit`'s "restore the checkpoint and report
> the failure" recovery path restored a `statusLine` key nobody had changed, left the broken config
> exactly where it was, and reported success. See "The config file is an artifact too" below.

This is SPEC-V2-FRAMEWORK.md §12.2, restated as a procedure. It lives in one file because four
commands need it and four copies would drift.

## Why not just timestamped backups

The obvious design is "copy `settings.json` to `settings.json.backup-<timestamp>` before writing."
It is wrong, and it fails on the second use rather than the first.

Migrate. Then edit. Then migrate again. A naive "back up whatever is there now" captures
**claude-tui-line's own command** as the thing to restore. Revert then cheerfully restores the tool
the user is trying to escape, and the further they get from their original statusline the harder it
becomes to reach. The escape hatch quietly closes exactly as it becomes needed.

So the ledger distinguishes the state that existed *before this tool ever touched the machine* from
every state since. That distinction is the entire reason this is a ledger and not a file-copy.

## Where it lives

```
~/.claude/claude-tui-line/backups/
  ledger.jsonl
  <timestamp>-settings.json
  <timestamp>-<original-script-basename>
```

Under the user's Claude directory, **not** under `${CLAUDE_PLUGIN_DATA}`. A backup that a plugin
reinstall can delete is not a backup.

Create the directory if it does not exist. If you cannot create or write to it, **stop the command
you are running** and report why. Never proceed with a write on the theory that the backup can be
taken afterwards.

## `ledger.jsonl`

**JSON Lines: one entry per line, and adding an entry appends that one line.** Never edit or remove
an existing entry — and never rewrite the file in order to add one.

```json
{"kind":"origin","timestamp":"2026-08-13T04:12:07Z","statusLine":{"type":"command","command":"/Users/someone/.claude/statusline.sh","refreshInterval":1},"settingsCopy":"20260813-041207-settings.json","settingsSha256":"9f2b…","scriptOriginalPath":"/Users/someone/.claude/statusline.sh","scriptCopy":"20260813-041207-statusline.sh","scriptSha256":"4c81…","note":"state before claude-tui-line was first installed"}
```

One line, no wrapping — a pretty-printed entry spans lines, and then the format's one guarantee is
gone. Write it with `>>`, never with a whole-file write tool:

```bash
printf '%s\n' "$entry_json" >> ~/.claude/claude-tui-line/backups/ledger.jsonl
```

**Guard against a torn final line before appending, not just when reading.** If a previous writer's
`>>` was interrupted mid-line, the file may not end in a newline. Appending straight onto that torn
line would merge two entries onto one line and corrupt both. Before the `printf` above, check
whether the file is non-empty and its last byte is not `\n`; if so, write a `\n` first. This is the
write-side counterpart to "Reading it" above — `src/ClaudeTuiLineMcp/BackupLedger.cs` guards the
same condition on the compiled path.

This was a JSON array until SPEC §12.2.1, and the reason for the change is worth carrying at the
call site. An array cannot be appended to — its closing bracket has to move — so adding an entry
meant reading the whole file and writing it back. **You** are the thing that does that: no compiled
code writes this file, so "write the array back" meant re-emitting every prior entry, each holding
opaque SHA-256 digests, unrecognised `statusLine` keys, and absolute paths, from context. Dropping
one is undetectable afterwards, because a lost entry and an entry never written leave the same file.
The entry most at risk is the oldest, which is `origin`, which is the one that can never be
recreated: once a claude-tui-line binary is installed, the test in step 7 can never again come out
`origin`, so the loss is permanent and nothing downstream reports it.

**Reading it:** parse each non-empty line as its own object. A missing file means no entries. If the
*final* line is incomplete, discard that line and use the rest — a torn append costs the newest
entry and leaves every earlier one byte-identical, which is the whole reason for the format. That is
not the "unreadable ledger" that stops `revert`; every complete line before it is still the ledger.

`statusLine` holds the previous value **verbatim**, including keys you do not recognise. If there
was no `statusLine` key at all, record `"statusLine": null` — that is a real, restorable state, and
it is different from not knowing.

Omit the three `script*` fields when the previous command was not a script on disk.

### The config file is an artifact too

An entry also carries the claude-tui-line config, under the same three-field shape:

```json
"configOriginalPath": "/Users/someone/.claude/claude-tui-line.json",
"configCopy":         "20260813-041207-claude-tui-line.json",
"configSha256":       "b7e0…"
```

Record these **whenever a config file exists** at the path §5's search order resolves to
(`$CLAUDE_TUI_LINE_CONFIG` first, then `~/.claude/claude-tui-line.json`) — not only when the
command is about to change it.

**When no config file exists, record the absence rather than omitting the fields:**

```json
"configOriginalPath": "/Users/someone/.claude/claude-tui-line.json",
"configCopy":         null
```

`configOriginalPath` is the path the search order resolved to — where a config *would* have been —
and a null `configCopy` says we looked there and found nothing. `configSha256` is omitted, since
there is nothing to hash.

This mirrors `"statusLine": null` exactly, which is the point: an earlier version of this section
invoked that precedent and then did something weaker, telling authors to omit all three fields and
mention it in free-text `note`. Those are not the same. Three fields missing is indistinguishable
from an entry written before configs were captured at all, and prose in `note` is not something a
rollback can branch on. So a command asking "was there a config here?" gets *no config was here*
and *this ledger cannot say* as the same answer, and the two call for opposite actions — delete the
file, or leave it alone. This is the third place in this project where absence needed a
distinguished value rather than a missing key (see also SPEC §12.6.9's `revision: "absent"`); it is
worth assuming it will be the shape of the fourth.

**This is what makes `/claude-tui-line:edit` recoverable at all.** `/edit` changes the config and
nothing else. An entry holding only `settings.json` is a backup of the one file that command
cannot break, and restoring it is a no-op that looks like a recovery — the config stays broken,
the report says restored, and the pre-edit config is gone. A backup that does not contain the
thing the command modifies is not a backup, however carefully the rest of the procedure is
followed.

The two artifacts are restored independently. Reverting the statusline does not touch the config
(see `revert`, step 6), and rolling back an edit does not touch `settings.json`.

Timestamps are UTC, ISO 8601. Compute hashes with `shasum -a 256 <file>` and store the bare hex.

Artifact filenames are second-resolution, so two writes in the same second collide. If the name
you are about to write already exists, append a counter (`-2`, `-3`) rather than writing over it —
rule 1 below is absolute, and a naming scheme that silently overwrites would breach it by accident
rather than by decision.

## The two kinds

- **`origin`** — the state before claude-tui-line ever touched this machine. **Written exactly once,
  ever.** Before writing one, read the ledger; if an `origin` entry already exists, you must not
  write another, no matter how long ago it was or how wrong it looks. Append a `checkpoint`
  instead.
- **`checkpoint`** — any state captured since. Written freely, as often as anything writes.

**An `origin` must never record a `statusLine` that already points at a claude-tui-line binary.**
Check before writing one. A user can arrive at that state without this tool ever having run — by
hand-editing `settings.json` and only later invoking `/claude-tui-line:setup` — and the naive
"no `origin` exists, so this is the origin" rule would then record claude-tui-line's own command
as the state to escape *to*. Because `origin` is written exactly once ever, that is unfixable
afterwards: the escape hatch is poisoned at the moment it is created, which is worse than the
second-use failure this whole design exists to prevent.

When the live `statusLine` already points at a claude-tui-line binary and no `origin` exists,
append a **`checkpoint`** and leave `origin` unwritten. A missing `origin` is an honest state and
the commands already handle it — `revert` lists the checkpoints, flags which ones point at a
claude-tui-line binary, and lets the user choose. A *false* `origin` gets no such handling,
because nothing downstream has any reason to doubt it.

Reverting is itself a change: it appends a `checkpoint` for the state it replaced, and it does
**not** consume or remove the `origin`. Reverting a revert has to be possible.

## The four rules

1. **Nothing in the backup directory is ever overwritten or deleted by any command.** Not stale
   entries, not superseded copies, not the directory itself. Pruning is the user's to do.

2. **The user's original script is copied, never moved and never modified.** Take the copy even
   though installing does not touch the script — restoring a command that points at a file the user
   has since deleted is a broken revert with no obvious cause.

3. **Only the `statusLine` key of `settings.json` is read or written.** Preserve every other key and
   the file's existing formatting. Write atomically: temp file in the same directory, then rename,
   so an interrupted write cannot leave a truncated `settings.json` — which would break far more
   than the statusline.

4. **An entry captures every artifact, not the one its command intends to change.** `/edit` writes
   only `claude-tui-line.json`; `setup` writes only `settings.json`; both take the same complete
   entry. An entry scoped to its caller is a backup of the wrong file for whichever command later
   needs it, and it fails silently — the rollback runs, restores something real, reports success,
   and leaves the damaged file untouched. That is the defect described at the top of this file, and
   it survived because this was a step in a procedure rather than a rule. Steps 4–6 below are how
   it is carried out.

   The question at each call site, and the one nothing inside the procedure can answer, is **does
   what this saves include what this command is about to change?**

## Checking a hash before restoring

The hashes answer two different questions about two different files, and an earlier version of
this section collapsed them into one instruction — "re-hash the live file and compare against the
ledger entry" — which is wrong in a way that disables the escape hatch.

**Applied to `settings.json`, that check fails every time by construction.** At revert time the
live `settings.json` is *supposed* to differ from the backed-up copy: claude-tui-line is installed
now and was not then. A revert following it literally would report "the user hand-edited it" on
every run and stop, which is the escape hatch refusing precisely when it is reached for.

So, explicitly:

| file | checked? | what a mismatch means |
|---|---|---|
| the **backup copies** in the backup directory | **yes**, before restoring | the backup store is damaged — stop, do not restore from it |
| the **live `settings.json`** | **no** | nothing; it differs by design |
| the **live `claude-tui-line.json`** | **no**, at revert time | nothing; revert does not touch it |
| the user's **original script**, at its original path | **yes**, when the entry records one | the user edited it since the backup — restoring the command restores a pointer to different code |

That last row is where the original instruction's rationale actually belongs, and it is the case
nobody was checking. A `statusLine` restored verbatim points at a path, not at contents; if the
script at that path has changed, the revert succeeds, the statusline runs, and it is not the
statusline that was backed up. Nothing about the outcome says so.

**Report the mismatch and let the user decide.** Do not overwrite their edit to make the numbers
agree — the whole point of recording the hash is to notice this, and silently proceeding discards
the only information the check produced. For a modified script, the choice is between the live
version and the backed-up copy, and they need to see that it is a choice.

## Writing `settings.json`

Every command here eventually writes that file, and they must all write it the same way. This is
the one definition; the commands cite it rather than restating it.

1. **Write only the `statusLine` key.** Never copy a backed-up `settings.json` wholesale over the
   live one. The user may have changed unrelated settings since the backup was taken, and a
   wholesale copy reverts those too — silently, and with no mention in any report, because the
   command believes it restored one key.
2. **Atomically** — temp file in the same directory, then rename. A statusline command runs once a
   second, so a torn write is read almost immediately.
3. **Preserve every other key and match the file's formatting.** Edit it; do not regenerate it. This
   means key/value fidelity for everything but `statusLine`, *and* matching the file's existing
   indentation style (space count, or tabs) — not byte-exact reproduction of the whole file, which no
   general-purpose JSON writer guarantees, but close enough that a diff of the write shows only the
   intended change. A reformatted settings.json makes the real change unreviewable and buries
   anything unintended.
4. **`"statusLine": null` restores by removing the key.** There genuinely was no statusline, and
   the absence of the key is the faithful reproduction of that state — not an empty object, and
   not a no-op.

## Restoring

The capture side above has no restore counterpart documented anywhere, even though the hash rules
in "Checking a hash before restoring" already anticipate one. Any command that restores from the
ledger — `revert` today, and anything else built against it later — follows this. The
`ledger.jsonl` schema does not change for this; restoring only reads entries already defined above.

1. **Select the entry.** A full revert to pre-install state selects the `origin` entry. Any other
   restore selects a `checkpoint` — by default the newest one, or a specific one the user names.
2. **Verify the backup copies before touching anything live.** Hash the `settingsCopy` (and
   `scriptCopy`, when present) files in the backup directory and compare against `settingsSha256` /
   `scriptSha256` in the entry. A mismatch means the backup store itself is damaged: **stop, restore
   nothing**, and report which file failed to verify. This is the "backup copies" row of "Checking a
   hash before restoring" — it is the only file whose hash is expected to still match.
3. **Verify the user's original script, if the entry records one**, by hashing the live file at
   `scriptOriginalPath` and comparing against `scriptSha256`. A mismatch means the user has edited
   that file since the backup was taken — restoring the `statusLine` command would point at
   different code than what was backed up. Report the mismatch and let the user choose between the
   live version and the backed-up copy; do not silently restore over their edit. Do **not** perform
   this check against the live `settings.json` or the live `claude-tui-line.json` — per "Checking a
   hash before restoring" above, those are expected to differ from their backups by design, and
   hash-checking them would make every restore fail.
4. **Append a `checkpoint` recording the state a restore is about to replace**, following "The
   procedure, in order" below, before either write in steps 5-6 happens. A restore is itself a
   state-changing write, and rule 4 above admits no exception for it: the entry captures the live
   (pre-restore) `settings.json` and `claude-tui-line.json` exactly as any other checkpoint would, so
   a restore can itself be undone. Capturing after the write instead would record the state the
   restore just produced, not the state it replaced — the same ordering the "once per invocation,
   before the first write" and "never proceed with a write on the theory the backup can be taken
   afterwards" rules require everywhere else in this document.
5. **Restore the `statusLine` key**, and only that key, following "Writing `settings.json`" above:
   atomically, preserving every other key and the file's formatting. An entry recording
   `"statusLine": null` restores by **removing the key entirely** — not by writing an empty object —
   because that is what a faithful reproduction of "no statusline configured" looks like.
6. **Restore `claude-tui-line.json` from `configCopy`.** When `configCopy` is a filename, copy it
   back to the path recorded in `configOriginalPath`. When `configCopy` is `null`, the entry recorded
   that no config file existed at capture time, so the correct restore is the **absence of the
   file** — delete it if a config file exists now, rather than writing an empty one.

## The procedure, in order

Any command that is about to write `settings.json` or `claude-tui-line.json`:

1. Ensure `~/.claude/claude-tui-line/backups/` exists and is writable. Stop if not.
2. Read `ledger.jsonl` (a missing file means no entries; a torn final line is discarded, not fatal).
   You are reading it to *decide* step 7, and for nothing else — do not hold it to write it back.
3. Read the live `settings.json` and its current `statusLine` value.
4. Copy `settings.json` into the backup directory with a timestamped name; hash it.
5. If `statusLine.command` names a script on disk, copy that too; hash it.
6. **Resolve the config path (§5's search order) and, if a file is there, copy and hash it too.**
   Do this whichever file you came here to write — rule 4. **If no file is there, still record
   `configOriginalPath` with `configCopy: null`** — the resolved path plus an explicit "nothing was
   here", never three omitted fields.
7. Append **one** entry — `origin` if and only if no `origin` entry exists **and** the current
   `statusLine` does not already point at a claude-tui-line binary; otherwise `checkpoint`.
8. Append it as **one line** to `ledger.jsonl`, with `>>`. Existing lines are not read here, not
   re-emitted, and not touched.
9. **Only now** write: the new `statusLine` into `settings.json` atomically, preserving other keys,
   and/or the new `claude-tui-line.json`.
10. Report the backup path and the kind of entry you appended.

**Every step that can abort comes before every step that writes**, and that ordering is the point
rather than a convenience. A command that mutates the ledger and then discovers it must stop
leaves a permanent entry for a change that never happened — permanent because rule 1 forbids
removing it.
