# claude-tui-line 0.4.0 — first release

claude-tui-line is a statusline framework for Claude Code. You compose a statusline from panes
and items (built-in, or your own shell and Python scripts) and it renders inside a bordered TUI
surface. It is written in C# on .NET 10 with Spectre.Console and builds as a Native AOT binary.

This is the project's first release. It ships the rendering engine, a CLI, a stdio MCP server,
and a Claude Code plugin with `setup`, `migrate`, `edit` and `revert` commands.

## Highlights

- Pane layout engine: vertical, horizontal and `flex` splits; `fixed`, `content`, `percent` and
  `fill` sizing; per-pane borders; overflow `wrap` / `truncate` / `overflow`.
- Built-in item registry, plus `command` items, derived items and compound items (`parts`).
- A CLI for validating, inspecting and previewing configs, and for calibrating the chrome reserve.
- An MCP server (`get_config`, `set_config`, `get_config_schema`) so an editor can read, change
  and introspect the config.
- Plugin commands to adopt, edit and revert a statusline, backed by a shared backup ledger.

## Features

**Layout**
- `split: "flex"` picks side-by-side or stacked automatically; a `pane {N}: flex split stacked`
  note is emitted when it stacks.
- `distribute: "min-rows"` sizes panes by searching for the achievable row count.
- Width is `COLUMNS` minus a configurable `layout.chromeReserve` (see Behaviour changes).

**Items**
- Built-ins: `directory`, `git-branch`, `repo`, `worktree`, `pr`, `model`, `effort`, `thinking`,
  `output-style`, `context`, `rate-limits`, `agent`, `engram`, `vim`, plus opt-in `model-short`,
  `remote-url`, `repo-host` and `linear`.
- `autocompact` item and a shared `showLabel` item setting.
- `engram` item reads values from the Engram CLI and colours by state.
- `pr` item carries a default OSC 8 hyperlink.
- `linear` extracts the ticket id from the branch and links to it once `workspace` is set.
- Compound items: one item built from several differently-coloured parts.
- Per-item `format`, `color`, `overflow`, `maxLines`, and OSC 8 `link` templates.

**Colour**
- Named tokens (e.g. `@model-accent`), threshold rules, and literals.
- 256-palette names (`deepskyblue1`, `orange3`) are accepted; `--colors` reports the full palette.

**CLI** (`claude-tui-line ...`)
- `--check [--json]` validates a config and reports diagnostics by JSON Pointer; it exits 0 on
  config problems, which are reported rather than signalled.
- `--accepted --json` lists accepted values for every closed-set key; `--schema --json`
  aggregates items, colours, accepted values and config structures.
- `--items`, `--colors`, `--preview`, `--version`.
- `--calibrate` measures the real chrome reserve with a digit ruler in a live session and writes
  `chromeReserve` to your config (`--saw`, `--confirm`, `--status`, `--cancel`).

**MCP server**
- `get_config`, `set_config` and `get_config_schema`. `get_config_schema` returns the same
  envelope as `--schema --json`, optionally narrowed with `sections`. Registration is picked up
  after a session restart or `/reload-plugins`.

**Plugin**
- `/claude-tui-line:setup`, `:migrate`, `:edit`, `:revert`, sharing one backup ledger that keeps
  the pre-install state separate from later checkpoints, so `revert` always has the original.

## Fixes

- Stacked splits no longer grant every child its own outer width, so children no longer overflow
  the parent's border box.
- Stacked `fill` panes no longer overflow the terminal at narrow widths (see below).
- Flex orientation for content-sized children and the responsive split fallback were corrected.
- Wrap-aware re-measurement: a narrower grant now returns the longest wrapped row, not the grant.
- Configured item colours replace internal decorative colours but never override a value-derived
  threshold colour.
- Reference extraction now sees every id a config references, including those inside `link`
  templates, so `--check` and the resolver agree on what an item depends on.
- Backup ledger: `/edit`'s checkpoint now contains the file `/edit` modifies, so its rollback
  path restores something real.

## Behaviour changes

**`chromeReserve` default 3 -> 4.** Usable width is now `COLUMNS - 4` (2 columns for Claude
Code's left indent, 2 for the right margin it truncates at). Stacked `fill` panes previously
overflowed by one column at the old value and no longer do. If you set `layout.chromeReserve`
explicitly it is unchanged; if you relied on the default, every pane is one column narrower than
before. Measure your own value with `claude-tui-line --calibrate`.

Other behaviour to be aware of:
- A `link` naming `{remote-url}` now genuinely resolves `remote-url`, which shells out to git.
  That makes an opt-in item reachable without you placing it.
- A branch with no ticket id renders nothing for `linear`, not an empty segment.

## Docs / repo layout

This release also reorganises the repository documentation:
- Specs moved to `docs/specs/`; reports to `docs/reports/`; top-level docs (`SPEC.md`,
  `STATUS.md`, `CAPTURE.md`, `backup-ledger.md`) now live in `docs/`.
- Tests were recalibrated for `DefaultChromeReserve = 4`, and an invariant was added that
  stacked fill panes never exceed the usable width.
- `tools/` holds consistency checks (`check-all.sh`, `check-docs.sh`, `check-examples.sh`,
  `check-doc-tokens.sh`, `check-counts.sh`, `check-notes.sh`, `check-citations.sh`).

## Known issues

- `tools/check-notes.sh` and `tools/check-citations.sh` exit 1 on content findings. Whether
  these findings pre-date the docs reorganisation has not been verified.
- `chromeReserve = 4` was verified only by a human looking at a real Claude Code pane, not by an
  automated test against Claude Code's actual chrome.
- Build is from source and needs the .NET 10 SDK; no prebuilt binary is shipped.
- Some docs disagree about the project's state. The README status banner still says the CLI,
  authoring commands and MCP tools "are not" built, but the CLI, MCP server and `--calibrate`
  exist in the source and the README documents them. `docs/STATUS.md` was last updated
  2026-08-15 and describes Phase 5 (CLI) and Phase 6 (`migrate` / `edit` / `revert`) as in
  flight, with Phase 6 unverified end to end. Treat `setup` and the CLI as the exercised paths
  and `migrate` / `edit` / `revert` as not yet proven against a real ledger.
- The `--accepted --json` example in the README shows `"version":"0.1.0"`, while the project
  version is 0.4.0.
- SPEC-99 (border renderer can emit a ragged box on very narrow panes) and SPEC-100
  (`agent` / `agent.name` absent from real stdin payloads) are filed as open stubs, not fixed.
