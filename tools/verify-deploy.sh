#!/usr/bin/env bash
# SPEC-V2-FRAMEWORK.md §14.2.2: AssemblyVersionInfoTests.cs's drift test ties the .csproj to
# plugin.json, both files in the source tree read by the same test run — it proves the tree is
# internally consistent, and it cannot observe publish/ (or a real user's install) at all. This is
# the other half: run the deployed binary and ask it, rather than measuring it. It never builds
# anything and never writes into the binary's directory — a check that needs a working toolchain
# to run cannot verify a deploy on a machine where the toolchain is exactly what's broken.
#
# Exit codes are three, not two, because folding "could not tell" into either "match" or
# "mismatch" is itself a silent failure:
#   0 - the deployed binary's --version agrees with plugin.json.
#   1 - they disagree. Both versions are printed, labelled, since "mismatch" alone doesn't say
#       which side is stale.
#   2 - the question couldn't be answered at all: no binary at the path, it won't run, or
#       plugin.json is unreadable or has no "version" field.
#
# Deliberately not in check-docs.sh, for the same reason check-examples.sh isn't: this needs a
# binary, and folding it in would mean check-docs.sh either fails on a machine with no build yet
# or, worse, learns to skip itself when it can't find one.

set -uo pipefail

cd "$(dirname "$0")/.." || exit 2

binary="${1:-publish/claude-tui-line}"
plugin_json=".claude-plugin/plugin.json"

if [[ ! -x "$binary" ]]; then
    echo "verify-deploy: no executable binary at '$binary'" >&2
    exit 2
fi

if [[ ! -r "$plugin_json" ]]; then
    echo "verify-deploy: cannot read '$plugin_json'" >&2
    exit 2
fi

tree_version=$(grep -o '"version"[[:space:]]*:[[:space:]]*"[^"]*"' "$plugin_json" | head -1 | grep -o '"[^"]*"$' | tr -d '"')
if [[ -z "$tree_version" ]]; then
    echo "verify-deploy: '$plugin_json' has no readable \"version\" field" >&2
    exit 2
fi

if ! deployed_version=$("$binary" --version 2>/dev/null); then
    echo "verify-deploy: '$binary --version' failed to run" >&2
    exit 2
fi

if [[ -z "$deployed_version" ]]; then
    echo "verify-deploy: '$binary --version' produced no output" >&2
    exit 2
fi

if [[ "$deployed_version" == "$tree_version" ]]; then
    echo "verify-deploy: '$binary' matches the tree — $tree_version"
    exit 0
fi

echo "verify-deploy: version mismatch" >&2
echo "  tree declares:   $tree_version   ($plugin_json)" >&2
echo "  binary reports:  $deployed_version   ($binary --version)" >&2
exit 1
