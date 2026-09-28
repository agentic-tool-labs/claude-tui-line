---
description: Change the statusline in plain English — add, remove, recolour, or rearrange items and panes
argument-hint: "[what you want changed, e.g. add git diff stat at the end of the first pane, adds green and deletes red]"
---

# Edit the statusline

The user's request: **$ARGUMENTS**

If that is empty, ask what they want changed and stop. Do not guess at an improvement — an
unrequested redesign of something they look at all day is not a favour.

The loop is fixed and it is the same every time: **query → edit → check → preview → show the
user.** Writing config from memory and declaring success is the failure mode this structure exists
to prevent, because a bad config is *silent* (§7): an unverified edit produces a wrong statusline
with no error anywhere.

## 1. Query — ask the binary, do not rely on recall

```bash
<claude-tui-line binary> --items --json
<claude-tui-line binary> --colors --json
```

If those are unrecognised, **stop** and report that this build has no CLI (SPEC-V2-FRAMEWORK.md
§9). Do not substitute a list of items from memory or from the README. Item ids and accepted keys
change between versions, and a remembered id that no longer exists **resolves to nothing and is
silently suppressed** — you would produce a config that looks right, renders short, and reports no
error.

Re-run these every time. Not once per session, not "I checked earlier."

## 2. Find the live config

In order: `$CLAUDE_TUI_LINE_CONFIG`, then `~/.claude/claude-tui-line.json`, then built-in defaults.
Do not guess which file is live — a config edited at the wrong path is an edit the user will report
as "nothing happened."

If there is no config file, say so and offer to create one seeded with the current defaults. Get
agreement first: a user who did not know they had no config may want `/claude-tui-line:migrate`
instead.

**If they agree, take step 4's checkpoint before creating the file, not after.** Seeding a config
is a write, and the ledger's rule is that every step which can abort precedes every step which
writes. Creating it here and checkpointing at step 4 records the seeded file as the state to roll
back to — so a failed edit rolls back to a config the user did not have when this command started,
and "no config, defaults apply" stops being reachable. The entry taken first records
`configCopy: null`, which is the ledger's way of saying there was nothing here, and a rollback
against it deletes the file rather than restoring one.

Read the whole file. You need the tree in front of you to navigate to "the first pane" —
`surface.pane.children[0].items` is only locatable if you can see the structure.

## 3. Capture the "before"

```bash
<binary> --preview --columns 80 2>/tmp/edit-before-notes
<binary> --preview --columns 60 2>>/tmp/edit-before-notes
cat /tmp/edit-before-notes
```

Both widths, and both written out rather than measured (§12.1.1) — you have no terminal, so
`tput cols` would return terminfo's static 80 while looking like it adapted. The before and after
captures must cover the *same* widths or the diff in step 7 compares different things.

Keep **both** streams. The render is half of what you show the user at the end; the notes on stderr
are how you tell an effect of your change from one that was already there. A pane that was being
dropped before you touched anything will still be dropped after, and without a "before" note to
compare against you will report it as something you did.

If it already errors or prints nothing, say so **now**, before editing. Otherwise your change gets
blamed for a fault that predates it.

Report those two as the different things they are, the way `/claude-tui-line:setup` and
`/claude-tui-line:revert` do. A nonzero exit or output on stderr is a real fault that predates you.
Empty stdout at exit 0 is **not evidence** — of a fault or of health — because this renders at a
synthetic width with a minimal payload. Recording it as a broken baseline is how a working
statusline gets reported as fixed later, when the only thing that changed was the payload.

## 4. Checkpoint

Read and follow `${CLAUDE_PLUGIN_ROOT}/docs/backup-ledger.md`, appending a **`checkpoint`** before
the first edit of a session. Not an `origin` — `origin` is written exactly once ever and this is
not it.

**Confirm the entry you appended actually carries the config fields** — `configOriginalPath`
present, and `configCopy` either naming a copy or explicitly `null`. A `null` is a legitimate
answer here and not a failure: it is the ledger recording that no config existed, which is the
normal case when step 2 was about to seed one. What must not pass is the fields being *missing*,
which means the procedure did not look. This command
changes `claude-tui-line.json` and nothing else, so an entry holding only `settings.json` is a
backup of the one file `/edit` cannot break — and step 7's rollback would then restore a
`statusLine` key nobody touched, leave the broken config exactly where it is, and report success.
If the ledger procedure did not capture the config, **stop**: an unrecoverable edit is not worth
one round trip.

Once per session, not once per edit, is enough. The point is that undoing one bad idea should not
require going all the way back to `origin`.

If the checkpoint cannot be written, stop and report.

## 5. Edit — and change only what was asked

**Re-read the config file immediately before you edit it**, rather than editing the copy you read
at step 2. Several steps have happened since, and an MCP call, another session, or the user in an
editor can have written it in between (§12.1.2). You have no `baseRevision` to refuse a stale write
with, so this is your only chance to notice — that, and step 4's checkpoint, which makes a clobber
recoverable rather than preventable.

**Then compare it against what you read at step 2, and branch (§12.6.11).** Re-reading alone makes
the *content* current and leaves your *intent* stale: "remove the second item" is a position you
resolved against the older copy, and applying it to a reordered newer one removes the wrong thing —
correctly, silently, and past every check below, because the config you write is valid and the
config you show the user is the one you wrote.

- **Identical** — the ordinary case. Carry on.
- **Different** — someone is editing this file right now. Do **not** apply the edit you already
  worked out. Re-derive it against the new content, and **tell the user the file changed underneath
  you and what moved.** They are one of the three writers this guards against, and they are the
  only one who can say whether their change and the requested change belong together.

**Never widen the request.** Do not reformat the JSON, reorder untouched keys, add borders, or
"tidy" adjacent items while you are in there. Reformatting the whole config while adding one item
makes the diff unreviewable and buries an unintended change where nobody will look for it.

Translations that come up often:

- *"at the end of the first pane"* → append to that pane's `items`. `children` is in visual order;
  the first child of a `"split": "vertical"` is the leftmost.
- *"make adds green and deletes red"* → one value, two colours means this is **not** one item.
  Either two derived items, each with its own `extract` regex over the same `from`, or one item
  with per-part colours. For a string like `3 files changed, 40 insertions(+), 12 deletions(-)`,
  two derived items is the honest shape.
- *"something not built in"* → a `command` item, using the §4.1 schema that `--items` returned.
  Prefer an argv array to `"shell": true`. Always set `ttlSeconds` and `timeoutMs`: this runs once
  a second, and an unbounded command in that loop presents as a frozen statusline.
- *"colour it by value"* → a colour rule with `match` (string `contains`) or `thresholds` (numeric
  `min`), not a fixed colour. A pane border and the text inside it can name the same rule, so they
  change together.
- *"make it fit" / "it's getting cut off"* → a sizing question, not an item question. Look at
  `size`, `overflow` and `distribute` before deleting anything the user asked for.

If the request is ambiguous in a way that changes the result, ask. If it is ambiguous in a way that
does not, take the obvious reading, do it, and say which reading you took.

## 6. Check

```bash
<binary> --check --json --config <the config path>
```

`--check` names the offending key by JSON Pointer. Fix and re-check until clean. Do not skip this
because the change looks obviously fine — that is exactly when a silent config error survives.

## 7. Preview, at both widths

```bash
<binary> --preview --columns 80 2>/tmp/edit-after-notes
<binary> --preview --columns 60 2>>/tmp/edit-after-notes
cat /tmp/edit-after-notes
```

`--check` passing is not evidence the result looks right. Most layout mistakes only appear when
something has to wrap, which is why the narrow width is not optional.

**Diff these notes against the ones from step 3.** That comparison is the only thing that separates
"my edit dropped a pane" from "a pane was already being dropped at 60 columns and still is" — and
those call for opposite responses. A note present in both is context for the user; a note that is
new is your change, and `--check` will never tell you about either, because neither is a config
error (§9.8.1).

The 60-column run appends rather than overwrites, so one file holds both. Read it knowing the two
kinds of note behave differently in it: a width-drop note carries its own width in its message, so
it stays unambiguous, while a `maxLines` cap note does not — the cap is width-independent, so it
fires identically at both widths and appears twice. Two copies of a cap note is one finding, not
two.

Then verify three things and report each honestly:

- **Did the intended change appear?** An item that resolves to empty renders invisible, and
  invisible is easy to mistake for absent.
- **Did anything else move?** Adding an item can rewrap or resize its neighbours. That is the
  layout working correctly, but the user should hear it from you rather than notice it later.
- **Does it still degrade rather than break** at 80 and 60?

If the binary now errors, or the change plainly did not work, roll back and report the failure. Do
not leave a broken statusline in place while you explain what went wrong — it will run once a
second the entire time.

**Rolling back means copying step 4's `configCopy` back over the config file** — the artifact this
command modified. It does *not* mean restoring that entry's `statusLine`, which was never changed
and whose restoration would fix nothing while looking exactly like a fix. If `configCopy` is
`null`, there was no config before this command ran, and rolling back means **deleting** the file
you created — that is the restoration of that state, the same way an entry with `"statusLine":
null` restores by removing the key. Then re-run
`--preview` and confirm the rollback actually took, for the same reason step 7 exists at all: a
recovery reported on the strength of having written a file is not a recovery.

## 8. Show

Before and after, labelled, one above the other. That comparison is the deliverable; a description
of what you changed is not.

Then, short:

- what changed, in a line or two
- **the config file you wrote, by full path** — not "your config". `$CLAUDE_TUI_LINE_CONFIG` set in
  the user's interactive shell need not be visible to yours, so §5's search order can resolve to a
  different file for each of you, and nothing errors (§12.1.2). The user reads the path and sees
  immediately that you edited the wrong one; without it they see a successful report and no change,
  which is the one failure with no symptom pointing at its cause.
- which ledger checkpoint it can be undone to
- anything you chose for them — a reading you picked, a `ttlSeconds` you invented, a colour you
  approximated
- anything you noticed but did not touch, if it matters
