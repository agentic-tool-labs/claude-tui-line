---
description: Adopt an existing statusline — map each of its elements onto claude-tui-line, backing the original up first
---

# Migrate an existing statusline to claude-tui-line

The user already has a statusline that works. Your job is to reproduce it, not to improve it —
they can ask for improvements afterwards, and they cannot ask for their old one back if you lose
it.

**Stop and report** at the first step that fails. A half-migrated statusline runs once a second.

## 0. Confirm the binary can answer questions

This command is a prompt; the binary is the oracle. Before anything else:

```bash
<claude-tui-line binary> --items --json
```

If that fails or is unrecognised, **stop**. Report that migration needs a build of the CLI
(SPEC-V2-FRAMEWORK.md §9) and that this version does not have one. Do not fall back to a list of
items written from memory or copied out of the README — an item list in a prompt file is a second
registry, it goes stale on the next item added, and a remembered id that no longer exists resolves
to nothing and is *silently suppressed*. You would produce a config that looks right and renders
short, with no error anywhere.

Keep the `--items` output. It is the only authority on what exists. Read **both** of its sections:
`items` tells you what each builtin reports, what it looks like rendered, and whether it is in the
default set or opt-in; `kinds` gives the keys for each way an item can be written, including the
one for authoring an item that has no row yet. Tier 2 below depends on that second section — you
cannot write a `command` item from the `items` list alone.

`default: false` on an item deserves attention. Those render **only** where you place them, so
mapping an element onto one and then leaving it out of a pane produces a config that is valid,
passes `--check`, and renders short.

Also run `--colors --json` and keep that; it is the same rule for the palette.

## 1. Find what they have now

Read `~/.claude/settings.json` and look for `statusLine`.

- **No `statusLine` key** → nothing to migrate. Point them at `/claude-tui-line:setup`. Stop.
- **Already points at a `claude-tui-line` binary** → they are migrated. Say so and stop; do not
  re-migrate over a config they may have edited since.
- Otherwise note the command and continue.

## 2. Take the backup

Read and follow `${CLAUDE_PLUGIN_ROOT}/docs/backup-ledger.md` in full, now, before reading the
user's script and long before writing anything.

If no `origin` entry exists yet, this is it — the state before claude-tui-line ever touched this
machine, written exactly once ever. Getting this right here is what makes revert work three
migrations from now.

If the backup cannot be written, **stop**. Do not continue on the theory that you can reconstruct
the original later.

## 3. Inventory what the script emits

Read the script. Do not run it yet.

List every distinct thing it puts on the line, in order — each field, separator, prefix, colour,
and any hyperlink. Be exhaustive. This inventory is the contract for the rest of the migration: an
element not on this list is an element you will silently lose.

For each element record:

- what it displays
- where the value comes from (the stdin JSON, a shell command, an environment variable, a file)
- how it is formatted — prefix/suffix text, colour, truncation, **and any condition on it being
  shown at all**

That last one matters more than it looks. Many statuslines only show a field when it is non-empty,
or only inside a git repo. That is behaviour, not decoration, and dropping it produces a line that
looks wrong precisely when the user notices.

## 4. Map every element into exactly one of three tiers

1. **A built-in item**, when `--items` offers an equivalent. Match on what the value *is*, not what
   the script calls it — a `$branch` variable is `git-branch` regardless of naming. If a built-in
   has the right value in the wrong shape, this tier still applies via a derived item: `from` names
   the source, `extract` is a regex over the raw value, `case` folds it, `format` wraps it, applied
   in that order.

2. **A `command` item wrapping the original logic**, when no built-in fits. This tier is why
   migration can be lossless: worst case every element shells out to a snippet of the user's own
   script, and they still gain panes, borders, sizing, and colour rules over logic that already
   worked. Prefer an argv array to `"shell": true`; reach for `shell` only when pipes or expansion
   are genuinely needed, and say so in your report when you do. Set `ttlSeconds` and `timeoutMs` on every
   command item **deliberately**, and report what you chose. Not because they are unset otherwise
   — they default to 30 s and 150 ms — but because 150 ms is tight for a real script, and a
   migrated command that quietly exceeds it is killed and renders as nothing (§7). The original
   script ran under no such budget, so this is the one place a faithful port can silently lose an
   element that otherwise mapped cleanly.

3. **Unmappable** — reported to the user, never silently dropped.

**Tier 3 existing is what makes tiers 1 and 2 trustworthy.** A migration that cannot say what it
failed to carry across will quietly lose an element, and the user will not find out until the day
they needed it. Do not stretch tier 2 to avoid an empty tier-3 list.

Preserve colours by name, from `--colors`'s `recommended` list, where the original used one of the
standard ANSI colours — those are theme-mapped, so they keep following the user's terminal theme
exactly as the original did. Where the original used a *specific* shade (a 256-palette index or a
truecolor escape), reproducing it by name is a downgrade: use the 256 name or `#rrggbb` hex, which
parse everywhere a name does, and say in your report that this also needs `"colorSystem"` set
(§6.2 — the default profile approximates them to the nearest of the sixteen). `--colors`'s
`recommended` list is a recommendation, not the accepted set; do not refuse a colour for being
absent from it. If the original varied
a colour by value — red above a threshold, a different colour per model — that is a colour rule
with `match` or `thresholds`, not a fixed colour. An OSC 8 escape becomes a `link` on the item.

## 5. Draft the config and check it — still without writing

Build the config in memory or in a temp file. One pane, items in their original order: a single
pane reproduces a one-line statusline most faithfully, and that is the goal. Do not split panes,
add borders, or reorder to look nicer. This is a port, and a port that also redesigns is a port
nobody can verify.

Then validate it:

```bash
<binary> --check --json --config <temp path>
```

`--check` names the offending key by JSON Pointer. Fix and re-check until clean. Do not skip this
because the config looks obviously fine — a bad config is *silent* (§7), so this is the only thing
standing between a typo and a statusline that renders short forever.

## 6. Check fidelity against the original — content, not bytes

Run both against the same stdin payload — §9.3.1's fixture, with `cwd` replaced by the real working
directory, emitted by the binary itself rather than hand-rolled here:

```bash
payload="$(<binary> --fixture)"
echo "$payload" | COLUMNS=80 <original command>
echo "$payload" | COLUMNS=80 <binary> --preview --columns 80 --config <temp path> 2>/tmp/preview-notes
cat /tmp/preview-notes
```

**80 is written out, not measured, and you must not replace it with `tput cols`** (§12.1.1). You
have no terminal — `tty` says "not a tty" and `COLUMNS` is `0` — so `tput cols` returns terminfo's
static default, which is 80 anyway. The difference is only that the command would *look* like it
adapted. Say "at 80 columns" in your report, never "at your terminal width".

**Read that stderr file. It is not noise, and it is the half of the answer stdout cannot give.**
Render notes go to stderr in the human form (§9.8.1) precisely so stdout stays byte-comparable for
the diff you are about to do — which means a command that captures stdout alone throws away every
explanation of what it just did. Do not merge the streams with `2>&1`: that corrupts the very
comparability the split exists to protect.

A note tells you *why* the render is short, and that changes the fix completely:

- `pane N dropped: no width remained at C columns` — the mapping is fine; the layout does not fit
  at that width. Report it to the user, do not re-map anything.
- `item 'X' emitted N lines; M kept (maxLines)` — a tier-2 command item wrapping their script is
  being cut by a `maxLines` cap (§4.0.1). There is no default cap, so this note always names a
  number that is written in the config **you generated** — the original script ran under none.
  Raise it or drop it, and say so in your report.

Without the notes, both of these reach you as nothing but a token missing from the diff, and the
obvious response — re-map the element — is wrong for each. This is the same class as the timeout in
step 4: a faithful port losing an element to a budget the original never ran under, with no symptom
that names the budget.

If you want them structured instead of read by eye, `--preview --json` carries the same notes in
`notes[]`. Use whichever you will actually check.

This is **not** a byte-parity check — the layout differs by design, that is the point. It is a
*content* check: strip escape sequences from both, and every visible token the original produced
must either appear in the new render or be on the tier-3 list. Anything else is a silent drop
wearing a success message.

Also preview at `--columns 80` and `--columns 60`. Most layout mistakes only appear when something
has to wrap. Capture stderr on those runs too — they are the widths where `pane N dropped` actually
fires, and a narrow preview that silently renders short is the failure this whole step exists to
catch.

If the original script errors on this synthetic payload, say so rather than treating its empty
output as a match. Two blank lines are not parity.

**The fixture carries every `StatusInput` field, so this check no longer passes on the empty
set.** §12.3.1 found the old hand-rolled two-field payload (`cwd` and `model`, nothing else) made
the check hold vacuously for any element reading `session_id`, `context_window`, `rate_limits`,
`pr`, `vim`, `agent`, `effort`, `thinking`, `output_style`, `worktree`, or `workspace`: nothing on
either side, and a silent drop reported as a match. `--fixture`'s payload is §9.3.1's fixture, whose
first rule is exactly "every field is populated, including the ones real payloads usually omit" —
so every element you mapped is actually exercised here, not skipped past.

**One payload now covers both halves.** §12.3.1 originally required a second run at the real `$PWD`
because the fixture's own `cwd` (`/home/you/code/acme-web`) is deliberately fake, so filesystem-
derived elements would resolve blank under it — the same vacuous pass one layer down. `--fixture`
already substitutes the real working directory for `cwd` (§12.7.2), so the one payload above
exercises both the stdin-derived and the filesystem-derived halves at once; a second run is not
needed.

**A disagreement in a machine-probed value (git branch, remote URL) is still not a finding.** The
fixture payload is piped through stdin, so `--preview` takes its real-probe branch rather than the
canned one `--items`' `example` field uses — it probes the same repository the original script
does, and on a real `cwd` the two should agree by construction. If they don't, that is worth noting,
but it is a different kind of finding than a token silently dropped from the render.

## 7. Show the user, and wait

**Nothing is written until the user says yes.** Show them:

1. the proposed config
2. the side-by-side render from step 6
3. **every render note step 6 produced**, at each width you previewed — these are the differences
   the side-by-side cannot show, because a dropped pane looks identical to an element you never
   mapped
4. the tier-3 list — and if it is empty, say "everything mapped" rather than omitting the section;
   an absent section reads as an oversight
5. **What is at the config path now** — the path §5's search order resolves to, and whether a file
   already exists there. If one does, say plainly that saying yes replaces it, and that the copy
   taken at step 2 is the only way back. If none does, say the path is currently empty rather than
   omitting the item.

Then ask. If they say no, you have written nothing outside the backup directory and there is
nothing to undo.

## 8. Write, then report

Write the config to the path §5's search order resolves to — `$CLAUDE_TUI_LINE_CONFIG` if it is
set, otherwise `~/.claude/claude-tui-line.json`. Do not assume the default. If that variable points
somewhere else and you write the default anyway, the renderer reads the file you did not write:
nothing errors, the statusline does not change, and every step above still reports success. Say
which path you wrote.

Write the config **before** repointing `statusLine`. The moment `statusLine.command` changes, the
binary starts running once a second — with whatever config is on disk at that instant. In the other
order that is a second of built-in defaults, which is a statusline the user did not ask for and did
not approve at step 7.

Then point `statusLine.command` at the binary, using the ledger's *writing* rules — only the
`statusLine` key, atomically, every other key and the file's formatting preserved.

**Do not run the ledger procedure again here** — not as an exception to it, but because it says so:
one entry per invocation, taken before the first write, covering every artifact. The backup was
taken at step 2, deliberately, before this command read anything, and it already holds both files.
Re-running it now would append a second entry whose config capture is the config *you just wrote* —
a restore point for a state that existed for one instant and that nobody would ever want back:
migrated config, original `statusLine`. Permanently, since rule 1 forbids removing it.

Report, in this order:

1. **The backup path and which ledger entry kind you appended** — first, before anything else. Say
   that `/claude-tui-line:revert` restores the `statusLine` entry in `settings.json`; it does
   **not** restore the config file. If step 7's item 5 found an existing config, name the
   `configCopy` path recorded in this entry and say restoring it is a manual copy — the ledger
   keeps it forever (§12.2 rule 1), but no command performs that copy for you.
2. **The mapping table** — one row per inventoried element: what it was, what it became, which
   tier.
3. **The tier-3 list**, as its own section.
4. **What you were unsure about** — any element where you guessed at intent, any `shell: true`, any
   `ttlSeconds` you invented, any colour you approximated. This is the section that earns the
   user's trust in the other three.

Do not describe the migration as verified beyond what step 6 actually showed.
