# claude-tui-line

A statusline framework for [Claude Code](https://claude.com/claude-code). You compose your
statusline out of panes and items — built-in ones, or your own shell and Python scripts — and it
renders inside a bordered TUI surface instead of a single line of concatenated text.

Written in C# on .NET 10 with [Spectre.Console](https://spectreconsole.net/), published as a
Native AOT binary so it starts fast enough to run on every render.

> **Status: pre-1.0 and moving.** The rendering engine is built and tested; the CLI, the authoring
> commands, and the MCP tools are not. See [STATUS.md](docs/STATUS.md) for what is done and what is
> left, and [SPEC-V2-FRAMEWORK.md](docs/specs/SPEC-V2-FRAMEWORK.md) for the architecture.

## Why a framework

Claude Code's `statusLine` hook hands your program a JSON blob on stdin and prints whatever you
write to stdout. That is enough to build anything, which in practice means everyone rewrites the
same string-slicing and ANSI-escaping by hand, and it stops being maintainable the moment you want
two columns.

This takes over the layout. You describe *what* you want on the statusline; the framework decides
how many rows it needs, how wide each pane is, where to wrap, where to truncate, and what to drop
when the terminal gets narrow.

## Install

Either way, you need the [.NET 10 SDK](https://dotnet.microsoft.com/download) — this compiles
from source rather than shipping a binary.

### As a plugin

```
/plugin marketplace add agentic-tool-labs/claude-tui-line
/plugin install claude-tui-line@claude-tui-line
/claude-tui-line:setup
```

`setup` checks your toolchain, builds into the plugin's data directory, **backs up whatever
statusline you already have** before touching anything, writes the new `statusLine` setting, and
shows you a rendered preview.

If you're working from a local checkout instead of installing from GitHub — e.g. you cloned the
repo to contribute, or you want the plugin's commands to track your working tree instead of a
synced snapshot — register it as a local marketplace and plugin instead of the two `/plugin`
commands above:

```
claude plugin marketplace add /path/to/claude-tui-line
claude plugin install claude-tui-line@claude-tui-line -s user
```

`install.sh` (below) does this for you as part of a full install.

Three more commands cover the rest of the lifecycle — adopt an existing statusline, change it in
conversation, and go back:

| command | what it does |
|---|---|
| `/claude-tui-line:migrate` | Reads your current statusline script and maps each element onto an item — built-in, a `command` item wrapping your own logic, or reported as unmappable. Shows you the result before writing anything. |
| `/claude-tui-line:edit` | "Add git diff stat at the end of the first pane, adds green and deletes red." Edits the config, validates it, and shows you before-and-after renders. |
| `/claude-tui-line:revert` | Restores the statusline you had before any of this. |

All four share one backup ledger at `~/.claude/claude-tui-line/backups/`, which records the state
from *before claude-tui-line was ever installed* separately from every state since. That
distinction is the point: migrate, edit, migrate again, and a naive "back up whatever is there now"
would capture claude-tui-line's own command as the thing to restore. `revert` targets the original
by default, so the escape hatch survives any number of changes. See
[docs/backup-ledger.md](docs/backup-ledger.md).

> These three need the CLI (`--items`, `--check`, `--preview`), which is now built and tested —
> see [CLI](#cli) below. `setup` works today.

### By hand

```bash
git clone https://github.com/agentic-tool-labs/claude-tui-line.git
cd claude-tui-line
./install.sh
```

`install.sh` builds both the CLI and the MCP server into gitignored `publish/`/`publish-mcp/`
staging directories, then — with separate consent, since this replaces a binary Claude Code may be
exec'ing once a second — deploys them into the shared `$BIN_DIR` the compiled code already expects
(`${CLAUDE_PLUGIN_DATA:-$HOME/.claude/claude-tui-line}/bin`). It registers the MCP server and the
plugin as pointing at this checkout, and — with your consent, and only after taking a backup —
points `settings.json`'s `statusLine` at the deployed binary. It reports what it changed and how to
undo each part; see [docs/backup-ledger.md](docs/backup-ledger.md) for the statusline/config backup
mechanism specifically. Safe to rerun any time you pull new commits — an already-correct install
reports so and writes nothing.

`refreshInterval: 1` in the `statusLine` it writes means the binary runs once per second, so
**startup cost is render cost** — which is why this is AOT-compiled rather than a script.

If you already have a statusline script, `install.sh` backs it up before replacing it — see
[docs/backup-ledger.md](docs/backup-ledger.md). Installing as a plugin instead also gets you
`/claude-tui-line:migrate`, which does the same backup and maps your script's elements onto items.

## Configuration

The config is a single JSON file, looked up in this order:

1. `$CLAUDE_TUI_LINE_CONFIG` — an explicit path, if set
2. `$HOME/.claude/claude-tui-line.json`
3. no config — built-in defaults

A minimal one:

```json
{
  "surface": {
    "pane": {
      "border": { "enabled": true, "style": "rounded", "color": "grey" },
      "items": [
        { "item": "directory" },
        { "item": "git-branch" },
        { "item": "model" },
        { "item": "context" }
      ]
    }
  }
}
```

### Panes

A pane either holds items or splits into child panes. Splits nest, so you can build columns of
rows of columns.

```json
{
  "surface": {
    "pane": {
      "split": "vertical",
      "gutter": 1,
      "children": [
        { "size": "fill",    "items": [ { "item": "directory" }, { "item": "git-branch" } ] },
        { "size": "content", "items": [ { "item": "model" }, { "item": "context" } ] }
      ]
    }
  }
}
```

Pane keys:

<!-- pane-token-table: quoted literals checked against `--accepted --json` by tools/check-doc-tokens.sh -->

| key | notes |
|---|---|
| `split` | `"vertical"`, `"horizontal"`, `"flex"` — makes this a container for `children` |
| `children` | child panes |
| `size` | a bare column count (`"24"`), a percentage (`"40%"`), `"content"`, or `"fill"` (also the default). `"auto"` is a deprecated alias for `"fill"` — note that it does **not** mean `"content"` |
| `minSize` / `maxSize` | integers — clamps on the resolved width |
| `distribute` | `"greedy"` (default) — each sibling claims what it wants, in order; `"min-rows"` — size siblings to minimise total rows instead; `"even"` — divide the remaining width equally among siblings, ignoring their content/fill sizing |
| `gutter` | integer, default `0` — columns between children on a **vertical** split; has no effect on a horizontal split |
| `align` | `"left"` (default), `"center"`, `"right"` — aligns this pane's **content** within its own box |
| `valign` | `"top"` (default), `"middle"`, `"bottom"` |
| `selfAlign` | `"left"` (default), `"center"`, `"right"` — aligns the **pane itself** within the leftover space of its parent's layout, distinct from `align`/`valign` |
| `overflow` | `"wrap"`, `"truncate"`, `"overflow"` |
| `ellipsis` | the marker used when truncating |
| `height` | `"content"` or `"fill"` (default) — sizes the pane to its own content or lets it fill, same vocabulary as `size` on the width axis |
| `maxRows` | integer — cap on rows this pane may occupy |
| `border` | `enabled`, `style`, `color`, `edges` — or, in place of the object, a bare shorthand string: `"all"`, `"outline"`, `"inside"`, `"none"` |
| `border.style` | `"rounded"` (default), `"square"`, `"heavy"`, `"double"`, `"ascii"`, or `"none"` |
| `border.edges` | `top` / `right` / `bottom` / `left`, each boolean, defaulting to `true` |
| `items` | the items to render, for a leaf pane |
| `id` | an optional identifier for the pane, in its own namespace — never shadows or is shadowed by an item `id` |
| `title` | an item, authored exactly like any entry in `items`, rendered as a caption spliced into the pane's top border line — requires `border.edges.top` |
| `titleAlign` | `"left"` (default), `"center"`, `"right"` — position of `title` along the border run; has no effect without `title` |

`split` names the dividing line, not the arrangement of children along it. `"vertical"` panes are
divided by a vertical line — children sit side by side, first child leftmost — the shape in the
example above. `"horizontal"` panes are divided by a horizontal line — children stack top to
bottom, first child topmost:

```json
{
  "split": "horizontal",
  "gutter": 1,
  "children": [
    { "size": "content", "items": [ { "item": "model" } ] },
    { "size": "fill",    "items": [ { "item": "directory" } ] }
  ]
}
```

`"split": "flex"` picks between those two arrangements automatically: side by side when the
children fit the available width, stacked when they do not. It is opt-in — `"vertical"` and
`"horizontal"` keep their exact current meaning — and, once chosen, the arrangement is not
revisited if the stacked result runs long on rows; a `pane {N}: flex split stacked` note reports
when it happens. `flex` is one letter from `size`/`height`'s `"fill"`, but the two mean different
things on different keys: `flex` picks a *direction* for `split`, `fill` picks an *extent* for
`size`/`height`.

`size: "content"` measures the pane's own text and asks for exactly that much — its **entire**
unwrapped width, before any cap. A pane holding a long list of items will ask for far more than any
terminal has; `content` is for anchors whose natural size is small and meaningful (a single item or
two), `fill` is for everything else. `distribute:
"min-rows"` is the interesting one: rather than letting each pane grab what it wants in order, it
searches for the width split that makes the *whole statusline* as short as possible.

> Unrecognised values are accepted silently at render time rather than rejected — an unknown
> `size` falls back to `"fill"`, an unknown `style` to `"rounded"`. Run `--check` against your
> config first: it reports every one of these instead of swallowing them. For any key above,
> `--check` reports the complete accepted set the moment it flags a bad value — this table is a
> guide to that set, not the definitive list of it.

The surface as a whole also takes its own `maxRows`, separate from any single pane's: `{ "surface":
{ "maxRows": 8 } }` bounds the total rows the whole statusline may occupy, however many panes want
more. `8` is the default.

A pane can carry its own `id`, a `title` caption, and a `selfAlign`:

```json
{
  "split": "vertical",
  "children": [
    { "id": "left", "size": "fill", "items": [ { "item": "directory" } ] },
    {
      "id": "right",
      "size": "content",
      "selfAlign": "right",
      "title": { "item": "model" },
      "titleAlign": "center",
      "items": [ { "item": "context" } ]
    }
  ]
}
```

`selfAlign` moves the whole `right` pane to the right edge of the leftover width in its parent's
row, while `align`/`valign` (unset here) still govern how `context`'s own content sits inside that
pane's box — the two never conflict because they act on different things. `title` is authored the
same way any item is — here it renders the `model` item — but is flagged as the pane's title and
drawn into the top border line itself rather than as a content row, so it never consumes a row or
counts against `maxRows`. It requires a top border edge, drops silently (with a `--check` note)
under `surface.border.collapse: true`, and `titleAlign` places it left, center, or right along
that border run.

### Borders

A leaf pane defaults to bordered; a split container defaults to borderless, so adding a split to a
config never silently adds chrome. Set `border.enabled` explicitly to override either default.

Instead of the `enabled`/`style`/`color`/`edges` object, `border` also accepts a bare shorthand
string:

| shorthand | effect |
|---|---|
| `"all"` | every edge, on just this pane |
| `"none"` | no edges, on just this pane |
| `"outline"` | every edge on this pane, and this instruction keeps propagating to every descendant that doesn't declare its own `border` |
| `"inside"` | no outer edges; each direct child instead draws only the edge(s) it shares with a neighbour along the split axis — the instruction does not propagate past those direct children |

A pane's own explicit `border` declaration — shorthand, `edges` object, or a plain
`enabled`/`color`/`style` object — always wins over whatever an ancestor's `"outline"` or `"inside"`
is propagating.

`border` is either one of the shorthand strings above, or an object — never both on the same pane.
The object form's `edges` key turns individual sides on or off directly:

```json
{ "border": { "edges": { "left": false, "right": false } } }
```

This pane keeps its top and bottom edges and drops left/right — handy for two side-by-side panes
that shouldn't each draw their own copy of the edge between them. Any edge you omit from `edges`
defaults to `true`, the same as omitting `edges` entirely defaults all four to `true` — `edges` is
for overriding specific sides, not for restating the ones you're keeping. `border.enabled` is
independent of `edges`: `enabled: false` (or `style: "none"`) suppresses the border outright,
regardless of what `edges` says; `edges` only has anything to draw once the border is enabled. And
if every edge ends up `false` — by explicit `edges`, or because an ancestor's `"outline"` forced
this pane's edges off — `style` is dropped too, rather than leaving a styled-but-invisible border on
record.

By default, adjacent panes draw as two separate boxes with the gutter between them. Set

```json
{ "surface": { "border": { "collapse": true } } }
```

to make shared edges resolve to a single line both panes touch instead — this is legal **only** at
`surface.border.collapse`; the same key anywhere else (the top-level `border`, or any pane's own
`border`) is rejected with `collapse-not-surface-level`. Under `collapse: true`, the divider always
occupies exactly one column, regardless of what `gutter` is set to — `gutter`'s value is ignored in
that mode, not enforced to be `>= 1`.

### Items

These ship built in. The ones marked *(opt-in)* render only where you place them yourself;
everything else is in the **default set** — the list you get when a pane omits `items`:

<!-- items-table: checked against `--items --json` by tools/check-examples.sh (rule C) -->

| | |
|---|---|
| `directory` | the current working directory |
| `git-branch` | current branch |
| `repo` | the workspace repo, as `owner/name` |
| `worktree` | worktree name, plus branch when `showBranch` is set |
| `pr` | pull request number and review state |
| `model` | model display name |
| `model-short` | abbreviated model name *(opt-in)* |
| `effort` | reasoning effort level |
| `thinking` | whether extended thinking is on |
| `output-style` | active output style |
| `context` | context window usage; renders `0%` when no usage has been reported |
| `rate-limits` | five-hour and seven-day usage |
| `agent` | active agent name |
| `engram` | Engram memory activity |
| `vim` | vim mode, when enabled |
| `remote-url` | the git remote URL *(opt-in)* |
| `repo-host` | the host the workspace repo lives on, from the session payload *(opt-in)* |
| `linear` | the Linear ticket id extracted from the current git branch, uppercased *(opt-in)* |

`model-short`, `remote-url`, `repo-host`, and `linear` are opt-in rather than default. `remote-url`
is opt-in because resolving it shells out to git, which you should only pay for if you asked for
it. `repo-host` costs nothing to resolve — it's excluded because a bare hostname is noise in a
rendered statusline; its purpose is to be referenced from a `link` template, not displayed on its
own. `linear` is excluded because most branches carry no ticket id, so a default placement would
render nothing on the majority of renders.

Each item entry accepts:

```json
{ "item": "context", "format": "ctx {}", "color": "aqua", "overflow": "truncate" }
```

`format`'s `{}` is the item's value. `maxLines` caps how many lines an item's own output may
produce — opt-in, no cap unless set.

### Item settings

A handful of builtins take their own settings, keyed by item id under a top-level
`itemSettings`:

```json
{
  "itemSettings": {
    "directory": { "depth": 2 },
    "context": { "showDetail": false },
    "rateLimits": { "windows": "5h" },
    "pr": { "reviewStateLabels": { "approved": "✓" } },
    "linear": { "workspace": "acme-corp" },
    "worktree": { "showBranch": true }
  }
}
```

`directory.depth` shows that many trailing path segments instead of just the basename.
`context.showDetail` toggles the token-count parenthetical. `worktree.showBranch` adds the
`(branch)` suffix after the worktree name; it is off by default. `rateLimits.windows` narrows
which of the five-hour/seven-day windows render — `"5h"`, `"7d"`, or `"both"` (the default).
`pr.reviewStateLabels` overrides the built-in review-state labels, keyed by the lowercase review
state token (`"approved"`, `"changes_requested"`, `"draft"`). `linear.workspace`
is covered below, under [Linking a branch's ticket id](#linking-a-branchs-ticket-id).

These settings are per item id, not per placement: if you place the same builtin twice in a
render, both placements see the same settings.

### Custom items

Anything you can run from a shell can be an item. Give it an `id`, a `command`, and how long to
cache it:

```json
{
  "id": "diffstat",
  "command": ["git", "--no-pager", "diff", "--shortstat"],
  "ttlSeconds": 5,
  "timeoutMs": 200,
  "format": "± {}",
  "color": "olive"
}
```

`command` is an argv array, passed to the process verbatim — no shell, so no quoting hazards and
no command injection. Set `"shell": true` only if you genuinely need shell features, and
understand what you're opting into. `ttlSeconds` caches the result so a slow command doesn't run
on every render; `timeoutMs` bounds how long a render will wait for it.

Python, a script in your repo, `curl` — anything that writes a line to stdout works.

### Derived items

An item can take another item's value and reshape it, without that source item being displayed:

```json
{
  "id": "agent-short",
  "from": "agent",
  "extract": "[^:]+$",
  "case": "upper",
  "color": "aqua"
}
```

The pipeline runs `from` → `extract` → `case` → `format`, in that order. `extract` is a regex
applied to the raw provider value, so it sees the underlying data rather than the rendered text.

### Compound items

An item can declare `parts` instead of a value, rendering several sources concatenated with no
separator between them — each part gets its own colour:

```json
{ "id": "agent-badge", "parts": [
    { "text": "agent:", "color": "grey" },
    { "from": "agent", "extract": "[^:]+$", "case": "upper", "color": "aqua" }
] }
```

This is for one item built from several differently-coloured sources with nothing forced between
them — two separate derived items would always have a separator (or none) applied uniformly.

### Colours

Sixteen named colours, plus `default`, `dim`, and `bold`:

```
black   maroon  green   olive   navy    purple  teal    silver
grey    red     lime    yellow  blue    fuchsia aqua    white
```

These are theme-mapped — your terminal decides what `blue` actually looks like. `tools/colors.sh`
prints them all rendered in your own terminal.

Anywhere a colour is named you can also use a **256-palette name** (`deepskyblue1`, `orange3`), a
**bare palette index** (`"207"`, `"141"` — the number as a string, with no `color` prefix), or a
**hex literal** (`"#ff8800"`). How faithfully they render depends on the colour profile, which
defaults to the conservative one:

```json
{ "colorSystem": "truecolor" }
```

`standard` (the default) renders everything through the 16 theme colours, so a hex literal is
approximated by the nearest of them — it works, it just won't be the exact shade you asked for.
`256` and `truecolor` widen that. The default is deliberate: the 16 are the only colours that
follow the user's terminal theme, so a statusline built from them stays readable when the theme
changes, and one built from hex does not.

Colour can also be computed from a value. Define a named rule and reference it:

```json
{
  "colors": {
    "model-tone": {
      "from": "model",
      "match": [
        { "contains": "Sonnet", "color": "blue" },
        { "contains": "Opus",   "color": "yellow" },
        { "contains": "Fable",  "color": "fuchsia" }
      ],
      "default": "grey"
    }
  }
}
```

`thresholds` does the same for numbers — `{ "min": 80, "color": "red" }` — which is how `context`
and `rate-limits` shade themselves as they fill up. A pane border and the text inside it can
reference the same rule, so they change colour together.

### Hyperlinks

Items can carry an OSC 8 hyperlink, which terminals that support it render as clickable text:

```json
{ "item": "git-branch", "link": "{remote-url}/tree/{}" }
```

`{}` is this item's own value; `{other-id}` is another item's — and the referenced item does not
need to be displayed anywhere. Terminals without OSC 8 support just show the text.

`git-branch` and `directory` already link by default, with no config needed: `git-branch` infers
its remote's `/tree/<branch>` page from `git remote get-url origin` (the same probe `remote-url`
uses, bounded by its 30-second cache), and `directory` links to a `file://` URI for the real
working directory, independent of any `depth` setting. Because `git-branch` is part of the default
pipeline, this means the default statusline now probes the git remote on every render — bounded to
roughly one subprocess every 30 seconds by that same cache, local only, no network. Your own `link`
config on either item still overrides its default.

`directory`'s default link target is configurable via `itemSettings.directory.openWith`: `"files"`
(the default) keeps the `file://` link above, and `"vscode"` links to `vscode://file/<path>`
instead, opening the directory in VS Code:

```json
{ "itemSettings": { "directory": { "openWith": "vscode" } } }
```

Whether either link actually does anything depends on your terminal: only some terminals forward
a clicked OSC 8 link's URI to the OS at all, and among those, some only do so for `http(s)` links.
A terminal that ignores custom schemes will make `openWith: "vscode"` look inert — that's the
terminal's OSC-8 handling, not a bug in this project.

To make your repo item clickable, `repo-host` is a cheaper link base than `{remote-url}` above:
it reads the host straight from the session payload instead of shelling out to git, and it composes
with `repo`'s own `owner/name` rather than duplicating it:

```json
{ "item": "repo", "color": "blue", "link": "https://{repo-host}/{}" }
```

`{}` here is `repo`'s own value (`owner/name`); `repo-host` supplies just the host, so the two
compose into a full URL with no subprocess involved. `repo-host` isn't itself displayed anywhere.

### Linking a branch's ticket id

For Linear specifically, use the `linear` builtin — it extracts and uppercases the ticket id
from the current branch for you, and links to the issue once you set your workspace slug at the
top level:

```json
{
  "itemSettings": { "linear": { "workspace": "acme-corp" } },
  "surface": { "pane": { "items": [{ "item": "linear" }] } }
}
```

Drop `workspace` for plain text with no hyperlink. A branch with no matching ticket id (e.g.
`main`) renders nothing at all — not an empty segment.

For any other tracker (Jira, Shortcut, ...), derived items plus a hyperlink do the same job —
see [SPEC-V2-FRAMEWORK.md §3.2](docs/specs/SPEC-V2-FRAMEWORK.md) for the underlying mechanism this recipe
is built from:

```json
{ "id": "ticket", "from": "git-branch", "extract": "[A-Za-z]{2,}-[0-9]+",
  "case": "upper", "link": "https://your-tracker.example/issue/{}" }
```

The same regex matches Jira, Shortcut, and most other trackers: it's the `link` template that
decides where it points. Drop `link` for plain text with no hyperlink.

### CLI

`--check` validates a config without rendering it, and exits 0 either way — problems are reported
as `diagnostics`, not failures:

```bash
claude-tui-line --check --config path/to/config.json
```

```
warning /surface/pane/distribute: "distribute" has no effect on a horizontal split; it divides extent among side-by-side children [key-not-applicable]
```

Add `--json` for a machine-readable shape:

```json
{"ok":true,"diagnostics":[{"path":"/surface/pane/distribute","severity":"warning","code":"key-not-applicable","message":"\"distribute\" has no effect on a horizontal split; it divides extent among side-by-side children"}]}
```

`--accepted --json` reports the complete accepted-value registry for every closed-set key — the
same registry `--check` diagnoses against, and the same source [tools/check-doc-tokens.sh](tools/check-doc-tokens.sh)
checks this README's own pane-keys table against, so that table can't quietly drift from what the
binary actually accepts:

```json
{"version":"0.1.0","keys":[{"key":"split","accepted":["none","horizontal","vertical"],"alsoAccepted":null}, ...]}
```

A key with no closed set — `size` is the current example — reports `"accepted":null` and its
possible forms in `alsoAccepted` instead.

`--schema --json` aggregates `--items --json`, `--colors --json` and `--accepted --json` into one
envelope, plus a `structures` table describing the shape of a config document (root, pane, item,
colour rule, ...) and a `kindSupport` table noting which item kinds an MCP-side editor can safely
construct today.

`--items` and `--preview` are also part of the CLI; see the blockquote near the top of this README,
under Install, for their current status.

## MCP tools

The plugin also registers a stdio MCP server, built by `/claude-tui-line:setup` alongside the CLI.
It exposes `get_config_schema`, which returns the same envelope as `--schema --json` (above) so an
MCP-aware editor can introspect the config format — every pane/item/colour-rule shape, the accepted
values for every closed-set key, and which item kinds it can safely construct — without shelling out
to the CLI itself. An optional `sections` argument narrows the response to just the parts needed
(`items`, `colors`, `accepted`, `structures`, `kindSupport`).

A newly installed or updated plugin's MCP registration is picked up at the next session restart or
`/reload-plugins` — it does not appear mid-session automatically.

## Layout, briefly

Width is the hard constraint. The usable surface is `COLUMNS` minus a small reserve for Claude
Code's own chrome — 4 columns by default (2 for its left indent, 2 for the right margin it
truncates at), adjustable with a top-level `"layout": { "chromeReserve": 4 }` if your terminal or
Claude Code version reserves a different amount. Every sizing decision follows from that number,
so raising it shrinks everything uniformly rather than clipping one pane. Panes are measured, not guessed:
an item's *plain* text determines its width, and colour markup never does — so adding colour can
never change the layout.

Don't guess `chromeReserve` — measure it with `claude-tui-line --calibrate`. It walks you through a
short digit-ruler check inside a live Claude Code session and writes the measured value straight to
your config:

```bash
claude-tui-line --calibrate            # start: renders a ruler row on the next statusline redraw
claude-tui-line --calibrate --saw 5    # report the last digit you could read before it was cut off
claude-tui-line --calibrate --confirm  # after checking the verify rows, writes chromeReserve
```

`--calibrate --status` shows an in-progress calibration; `--calibrate --cancel` abandons it without
writing anything. claude-tui-line also appends a one-line nudge to the statusline itself the first
time it runs on a machine, and again after a Claude Code version change, unless you set
`"layout": { "calibrationPrompt": false }`.

When content genuinely doesn't fit, it degrades in a defined order rather than overflowing: wrap,
then truncate, then drop.

## Contributing

The architecture lives in [SPEC-V2-FRAMEWORK.md](docs/specs/SPEC-V2-FRAMEWORK.md) and is the source of truth
— it is written to be argued with, and sections are cited by number in commit messages and code
comments. [STATUS.md](docs/STATUS.md) tracks what is built.

Two older documents are still in the repo and still matter. [CAPTURE.md](docs/CAPTURE.md) is the
behavioural capture of the original bash statusline, and it is normative for parity questions.
[SPEC.md](docs/SPEC.md) is v1 — superseded on architecture, but v2 cites it by number in four places
and those rulings stand. Its own header says which. New rules go in SPEC-V2-FRAMEWORK.md.

**Before you build, read [SPEC-V2-FRAMEWORK.md §14](docs/specs/SPEC-V2-FRAMEWORK.md).** `publish/` is what a
user's live statusline executes, so writing there replaces a running program and is a deploy
rather than a build; development and verification build to the SDK-default output instead.

```bash
dotnet build src/ClaudeTuiLine/ClaudeTuiLine.csproj
dotnet test  tests/ClaudeTuiLine.Tests/ClaudeTuiLine.Tests.csproj
./tools/check-citations.sh
./tools/check-counts.sh
```

There is no solution file, so build and test commands name their project explicitly.

Both `tools/` scripts check the docs for things careful reading does not catch, and both exist
because the defect they look for was found by hand more than once.

`check-citations.sh` verifies that every `§N.M` the spec cites resolves to a heading in the spec.
Four references were cited and undefined — one of them 27 times — and every one had survived many
careful readings, because prose citing a missing section reads perfectly well. The sentence
carries the meaning; the number is decoration until someone tries to follow it.

`check-counts.sh` verifies that a sentence promising *n* items is followed by *n* items. One rule
list announced three and had four, and the extra one was the rule whose failure mode is silent —
so a reader reconciling it against the other copy would have dropped exactly the wrong one.

Run `./tools/check-docs.sh` after editing any document — it runs those two plus `check-notes.sh`,
needs no toolchain, and reports every disagreement in one pass rather than the first one.

`./tools/check-all.sh` runs that and then `check-examples.sh`, which needs a .NET SDK because it
compares every documented example against what the binary actually emits. Run it before sending a
change that touches an example. There is no CI: this project is cloned and built locally, so these
scripts are the whole gate.

## Licence

MIT — see [LICENSE](LICENSE).
