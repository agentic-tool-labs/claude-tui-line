# Statusline capture — baseline contract for the rebuild

Captured 2026-08-12 from `~/.claude/statusline-command.sh` (bash, 395 lines), wired in
`~/.claude/settings.json` as:

```json
"statusLine": {
  "type": "command",
  "command": "bash /Users/example/.claude/statusline-command.sh",
  "refreshInterval": 1
}
```

Any rebuild must reproduce the behavior below unless a deviation is explicitly agreed.

## Execution model

- Claude Code spawns the command, pipes session JSON on **stdin**, and renders each line of
  stdout as one status row. ANSI escapes are honored.
- Re-runs on: session start/resume, each new assistant message, /compact finishing,
  permission-mode change, vim-mode toggle, and every `refreshInterval` (1s here). Debounced at
  300ms; an in-flight process is killed when a new trigger fires.
- **Startup cost is render cost**: with a 1s refresh, the process is spawned every second.
  The bash version's whole render is ~44ms p50, and its comments record that a 17-fork jq
  version at 87ms was deemed too slow. Budget: match or beat ~44ms end-to-end.
- Claude Code (v2.1.153+) exports `COLUMNS` before spawning. `ENGRAM_HOME` may override the
  Engram home directory (default `~/.engram`).

## Input fields consumed (jq paths)

```
.cwd
.workspace.repo.owner + .workspace.repo.name
.worktree.name        .worktree.branch
.pr.number            .pr.review_state
.model.display_name
.effort.level
.thinking.enabled
.output_style.name
.context_window.used_percentage
.context_window.total_input_tokens
.context_window.context_window_size
.rate_limits.five_hour.used_percentage
.rate_limits.seven_day.used_percentage
.agent.name
.vim.mode
.session_id            (used only to filter Engram telemetry)
```

All fields are optional; a missing field suppresses its segment (never renders "null").

## Segments, in order

Threshold coloring used by segments 10–11: `< 50` green, `50–79` yellow, `>= 80` red
(percentage rounded to nearest integer first).

| # | Segment | Condition | Format | Color |
|---|---------|-----------|--------|-------|
| 1 | Directory | `.cwd` present | `basename(cwd)` | cyan |
| 2 | Git branch | `git --no-optional-locks -C <cwd> branch --show-current` non-empty (live subprocess, not from stdin JSON) | branch name | green |
| 3 | GitHub repo | workspace repo present | `owner/name` | dim |
| 4 | Worktree | worktree name present | `worktree:NAME` or `worktree:NAME(BRANCH)` | magenta |
| 5 | PR | pr number present | `PR #N` + optional ` [approved]` / ` [changes]` (from `changes_requested`) / ` [draft]` / ` [<other state>]` | yellow |
| 6 | Model | display_name present | as-is | blue |
| 7 | Effort | effort level present | `effort:LEVEL` | dim |
| 8 | Thinking | `.thinking.enabled == true` | `thinking` | magenta |
| 9 | Output style | present and not `default`/`Default` | `style:NAME` | dim |
| 10 | Context | used_percentage present | `ctx:NN%` (threshold color) + ` (USEDk/SIZEk)` in dim when both token counts present; bare `ctx:NN%` otherwise. Token counts are integer-divided by 1000. | mixed |
| 11 | Rate limits | either window present | `5h:NN%` and/or `7d:NN%`, joined with dim ` / ` when both | threshold colors |
| 12 | Agent | agent name present | `agent:NAME` | magenta |
| 13 | Engram | see below | `engram:COUNT` (dim) + activity verb (magenta), either alone is valid | mixed |
| 14 | Vim mode | vim mode present | `[MODE]` | yellow |

Separator between segments: ` | ` with the pipe dimmed (3 visible columns).

## Segment 13: Engram telemetry

Source: `${ENGRAM_HOME:-$HOME/.engram}/telemetry.jsonl`, append-only JSONL shared by every
session on the machine. Only the **last 64KB** is read (`tail -c 65536`) so cost is flat as
the log grows.

**Session filtering.** Hook-written records carry Claude Code's `session_id`; MCP-written
records (recall/remember/browse/expand) carry a different transport id that cannot be mapped
back. So a record is eligible if it carries this session's id, OR its `kind` is one of the
unattributable-by-nature kinds:
`recall|remember|browse|expand|digest|revise|session-open|index|embedding|server-start|server-stop`.
When `session_id` is absent from stdin, a placeholder that matches no record is used — the
segment degrades to shared kinds only, never to "everyone's edits".

**Fact count.** `long_term_fact_count` appears with a number only on
`session-start`/`subagent-start` (primer) records — on `recall` records the same key means
something else (facts returned by that call) and must NOT be used. Take the newest primer
record in the window that carries a numeric value → `engram:COUNT`.

**Instant events** (rendered only if the newest eligible record is ≤ 10s old — depends on the
1s refresh to clear):

| kind | verb |
|------|------|
| file-touched | `✎ <basename of path>`, or `✎ edit` when the record has no path |
| user-prompt | `✱ captured` |
| remember | `✱ saved` |
| recall / browse / expand | `◉ recalled` |
| digest / revise | `◈ digested` |
| session-start / subagent-start | `▸ primed` |
| server-start | `● up` |
| server-stop | `○ down` |

**Ongoing work** (`index`, `embedding`): these kinds write phase records; show
`✎ indexing` / `∿ embedding` while the newest record of that kind has `phase:"started"`
(a finished/failed record clears it), bounded at 900s so a killed process cannot pin the
display forever. Both can show at once, and ongoing verbs render before the instant verb.

Timestamps are ISO-8601 UTC, some `Z`-suffixed and some `+00:00` — parse by truncating at the
first `.` and reading as UTC.

## Wrapping

Claude Code truncates an over-wide status line but renders one row per stdout line, so the
script wraps itself:

- Available width = `COLUMNS - 1` (the last cell is left unwritten because terminals disagree
  on when writing it wraps).
- Greedy packing: segments flow into a row while `row + separator + segment` fits; a segment
  is never split across rows; an oversized segment gets its own row.
- Width is measured on the ANSI-stripped text.
- If `COLUMNS` is unset/unparsable, or the usable width (`COLUMNS - 1`) is less than 20
  (bash-exact: `avail < 20`): emit everything as one unwrapped line (old behavior).

## Performance notes from the bash implementation

These are constraints on the *old* implementation, recorded so the rebuild knows what the
44ms is made of — not requirements to copy the mechanism:

- One jq invocation for all 17 fields (17 separate calls doubled the render: 87ms vs 44ms).
- Engram record fields parsed with parameter expansion, not jq (+19ms per extra jq call).
- `grep` over the 64KB tail beat pure-bash pattern matching (+14ms vs +38ms).
- Any perf comparison was only trusted after calibrating the harness against itself
  (same-script-vs-itself gap must read 0ms).

The one live subprocess besides jq is the `git branch --show-current` call (segment 2).
