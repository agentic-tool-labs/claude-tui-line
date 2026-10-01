# SPEC-101 live-verification findings (E1/E4 + your two follow-up checks)

## E1 (§9) — verify-row rendering — PASS, but surfaced a UX defect

Jim ran `--calibrate` live. Digit=5 at his real columns → candidate reserve=4
(matches the §3.3 worked example exactly). At the verify step, Row A rendered
clean and Row B truncated — the textbook "confirm" signal. Jim read the two
rows differing in raw width (86 vs 87 chars, by construction) as suspicious
and hit `--reject` instead of `--confirm`, landing on reserve=5 (functional,
but 1 more than necessary) instead of the correct 4.

**Root cause**: the `--saw`/verify prompt text doesn't explain *why* row A
and row B are different lengths (the two-probe bracket: A = COLUMNS-reserve,
B = COLUMNS-reserve+1 by design). A user who doesn't already know the
bracket design reads unequal lengths as "these aren't comparable" rather
than "this is the point." Recommend clarifying the on-screen `--confirm`/
`--reject` prompt (CalibrateCommand.cs's RunConfirm/RunReject-adjacent
messaging) to state explicitly that A and B are intentionally different
widths. This is a spec/UX-defect, not an impl-defect — the code did exactly
what §3.4 specifies; the specified prompt wording is what let a correct
result get rejected.

Jim manually corrected to reserve=4 via `--calibrate --set 4` after we
diagnosed it together. No code change needed for the calibration math.

## E4 (§12.10) — version field presence — PASS

`calibratedVersion` in the record populated correctly from Jim's real live
payload during calibration ("2.1.237" at the time). Top-level `version` key
exists and is read correctly. This NEEDS-EVIDENCE item is closed.

## Your Point A (record-write-frequency / rule 8 guard) — code confirmed correct, but real-environment finding

Isolated repro (temp CLAUDE_TUI_LINE_CONFIG, fixed version across 3
consecutive renders): **zero** additional writes after the first — the
"only write when PromptedForVersion changes" guard behaves exactly as
specified. Second isolated repro (version changes once, then holds): exactly
one write on the change, zero afterward. The rule 8 write-guard logic itself
is correct.

However, watching Jim's *real* record file live, its mtime kept advancing on
every redraw, and `promptedForVersion` was observed cycling through THREE
different values across a few minutes (2.1.233, then 2.1.234, then 2.1.238)
while `calibratedVersion` stayed fixed at "2.1.237". Cause: Jim has 16
concurrent Claude Code processes running (this session plus several peer/
subagent panes), and — apparently due to an in-progress client auto-update —
different panes are running genuinely different CC client versions
concurrently. Each pane's statusline hook redraws independently, but *all
panes share one global calibration-record.json*. Since each pane's own
version differs from the calibrated version, rule 8 correctly fires
per-pane, and different panes race to overwrite `promptedForVersion` with
whatever version *they* individually see — causing continuous churn and the
nudge appearing on some panes but not others, seemingly at random.

This is a **spec gap, not an impl-defect**: the §12.3 trigger table
implicitly assumes one Claude Code version is active system-wide at a time.
It has no provision for multiple concurrently-running client versions
sharing one record file. Two possible directions, your call:
1. Accept the churn as harmless/self-resolving (once all panes converge on
   one version, writes stop) and document the assumption explicitly.
2. Loosen rule 5 to compare only major.minor (or some tolerance) so a patch
   bump across concurrent panes doesn't repeatedly invalidate the
   calibration — reduces churn and re-nudge frequency for exactly this kind
   of multi-pane environment.

No code changes made for this — reporting for your adjudication per the
NEEDS-EVIDENCE protocol.

## Status

- No commit made. Awaiting Jim's explicit go-ahead per repo convention.
- Config now correctly has `layout.chromeReserve: 4` (Jim's real, confirmed
  value) after the manual correction above.
