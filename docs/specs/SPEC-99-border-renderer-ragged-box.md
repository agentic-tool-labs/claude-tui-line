# SPEC-99: PaneBorderRenderer unclamped content row → ragged box on narrow panes

Status: OPEN (stub — filed, not yet designed or implemented)
Filed: 2026-08-20, spun off from SPEC-98 implementation work, deliberately not bundled into that fix.

## Symptom

`PaneBorderRenderer.cs:102-104` does not clamp a content row to the pane's
inner width before drawing the border around it. When a content pane's
`innerWidth` drops below `RowLayout.MinUsableWidth` (20) — reachable via
`RowLayout.cs:57-60`'s degrade path on a narrow bordered pane — the emitted
row can be wider than the border box drawn around it, producing a visibly
ragged/misaligned box (border corners not lining up with the content edge).

## Scope

Design and fix belong to the Architect. Likely a clamp/truncate at
`PaneBorderRenderer.cs:102-104` matching `innerWidth`, but the exact
correct behavior (truncate vs. re-wrap vs. suppress border) needs a spec
pass, not a guess.

## Repro condition

A content (non-fill) pane, bordered, whose `innerWidth` computes below 20
columns — narrow terminal or many siblings.
