# Report: README compound-items (`parts`) example

Branch `task-compound-items-readme`, commit `61209b0`, worktree
`/Users/jimcline/git/repos/claude-tui-line-task-compound-items-readme`,
based on local `main`@`2b9a925` (matches dispatch's stated HEAD). Only
`README.md` touched — 1 file changed, 15 insertions(+).

## What I added

New `### Compound items` section, README.md:309-323, inserted between the
existing `### Derived items` (ends :307) and `### Colours` (:309→now :325),
matching that section's style (prose intro, fenced ```json example, one
prose sentence after).

Example used verbatim from SPEC-V2-FRAMEWORK.md §3.3 (confirmed by direct
read, not paraphrase — matches the dispatch's suggested snippet exactly,
including the multi-line brace layout):

```json
{ "id": "agent-badge", "parts": [
    { "text": "agent:", "color": "grey" },
    { "from": "agent", "extract": "[^:]+$", "case": "upper", "color": "aqua" }
] }
```

Prose: "An item can declare `parts` instead of a value, rendering several
sources concatenated with no separator between them — each part gets its
own colour," plus a closing sentence on what it's for: one item from
several differently-coloured sources with nothing forced between them,
vs. two separate derived items which would always apply a separator (or
none) uniformly.

## Verification (against the built binary, not just read)

- Rebuilt Release (`dotnet build src/ClaudeTuiLine -c Release`) — succeeded.
- Spliced the exact item above into a real config
  (`/tmp/compound-item-verify.json`, under `surface.pane.items`) and ran:
  - `--check --json --config /tmp/compound-item-verify.json` →
    `{"ok":true,"diagnostics":[]}`.
  - `--preview --config /tmp/compound-item-verify.json` → rendered
    `agent:` in grey directly concatenated with `ACME-REVIEWER` in aqua,
    no separator — confirms both the parts-concatenation behavior and
    that `extract`/`case` ran correctly on the synthetic `agent` payload.

## Ready for cdtui-worker pre-merge verify.
