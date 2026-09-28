#!/usr/bin/env bash
# Run every check in this directory, including the one that needs a .NET toolchain.
#
# This exists because check-examples.sh had nowhere to run. It lived only in the GitHub
# Actions `build` job, on the reasoning that CI is where a binary already exists — and
# GitHub Actions was never billed on this repo, so that job has never executed. The check
# was written, reviewed, committed, and has compared nothing. Worse than absent: a `ci.yml`
# in the tree reads as coverage, and the red ✗ GitHub showed against every commit meant
# "never ran" rather than "failing" — a signal that cannot distinguish those two is one
# people stop reading, which is the only signal that would have said so.
#
# The workflow is gone now. Clone-and-build-locally is the supported story, so the checks
# have to be runnable that way too.
#
# This does NOT fold check-examples.sh into check-docs.sh. That file's header already
# rules on the question and the ruling stands: check-docs.sh must run on a machine with no
# toolchain, and must never learn to skip a check it cannot perform. Composing the two
# keeps that guarantee intact and puts the toolchain requirement here, where it is the
# whole point rather than a caveat.
#
# There is deliberately no build step. check-examples.sh already builds one via `dotnet
# run` when CLAUDE_TUI_LINE_BIN is unset, and dies rather than reporting clean when it
# can neither find nor build a binary. Adding a build here would be a second copy of that
# decision, and a second place for it to drift.
#
# Set CLAUDE_TUI_LINE_BIN to an already-built binary to skip that build — it is inherited
# from this shell.
#
# One caveat with no clean fix: the build shares src/ClaudeTuiLine/obj/ with any other
# build in this working tree. Do not run this while something else is compiling here.

set -uo pipefail

cd "$(dirname "$0")/.." || exit 2

status=0

# Both run even when the first fails, so one pass reports every disagreement rather than
# the first — same reason check-docs.sh loops instead of chaining with &&. Documentation
# findings fixed one round trip at a time are how a five-minute cleanup becomes an
# afternoon.
./tools/check-docs.sh || status=1
./tools/check-examples.sh || status=1
./tools/check-doc-tokens.sh || status=1

if [[ $status -ne 0 ]]; then
    echo >&2
    echo "check-all: at least one check above failed." >&2
fi
exit $status
