# SPEC-100: agent/agent.name absent from real stdin payloads — null path unchecked

Status: OPEN (stub — filed, not yet designed or implemented)
Filed: 2026-08-20, spun off from SPEC-98 implementation work, deliberately not bundled into that fix.

## Symptom

Observed empirically: `agent` / `agent.name` is absent from roughly 7 of 9
sampled real Claude Code stdin statusline payloads. The code path that reads
`agent.name` has not been confirmed to handle that absence safely — needs a
targeted look at whatever item/resolver reads it, to confirm it degrades
(e.g. to empty/hidden) rather than throwing or rendering "null"/garbage.

## Scope

Design and fix belong to the Architect: whether the resolver should treat a
missing `agent` block as "no agent item configured" (hide it) vs. some other
fallback. Needs a spec pass to pick the right default, not a guess.

## Repro condition

A stdin payload with the `agent` key entirely absent, or present but missing
`name`. Sample real payloads to confirm the exact shape before designing the
fix.
