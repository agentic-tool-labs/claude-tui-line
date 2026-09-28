#!/usr/bin/env bash
# Run every documentation check that needs no toolchain, and fail if any of them does.
#
# This exists because of a specific near-miss rather than for tidiness. The habit while
# working is to run the checks and skim the last line — `./tools/check-counts.sh | tail -2
# && ./tools/check-citations.sh | tail -2 && git commit ...` — and in bash the exit status
# of a pipeline is the exit status of its LAST command. `tail` always succeeds. So the
# `&&` gate read tail's status, a failing count check reported a real disagreement, and
# the commit went out anyway. That is precisely the failure check-examples.sh's own header
# calls out: a check reporting clean because nothing read its answer.
#
# The fix is not to remember `set -o pipefail`. It is to have one thing to run whose whole
# output is short enough that there is no reason to pipe it anywhere.
#
# check-examples.sh is deliberately NOT here. It needs a binary, and folding it in would
# mean this script either fails on a machine mid-build or — far worse — learns to skip
# itself when it cannot find one. A check that can silently downgrade to a pass is the
# thing all three of these guard against.
#
# Run ./tools/check-all.sh to get this plus check-examples.sh, or run that one on its own
# with CLAUDE_TUI_LINE_BIN pointing at a real build. It used to live in CI's `build` job
# instead, which is why it had no local runner — and why, GitHub Actions never having been
# billed on this repo, it went from written to committed without ever once executing.

set -uo pipefail

cd "$(dirname "$0")/.." || exit 2

status=0
# check-notes.sh reads C# sources, which makes it look like it belongs with check-examples.sh
# below. It does not: it reads them as *text*, needs no build, and runs anywhere the repo is
# checked out. The distinction that matters here is toolchain, not subject matter.
for check in check-citations check-counts check-notes; do
    if ! "./tools/$check.sh"; then
        status=1
    fi
done

# Every check runs even after one fails, so a single pass reports every disagreement rather
# than the first one. Fixing documentation findings one round trip at a time is how a
# five-minute cleanup becomes an afternoon.
if [[ $status -ne 0 ]]; then
    echo >&2
    echo "check-docs: at least one check above failed. Nothing was committed." >&2
fi
exit $status
