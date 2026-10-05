#!/bin/sh
# SessionStart notice: tells the user when the deployed binaries were built from
# a different plugin version than the one now running. Reads two files; never
# builds, writes, prompts or fails. Every path out is a silent exit 0.
#
# The stamp path is literal on purpose. CLAUDE_PLUGIN_DATA is set in a hook's
# environment, but snapshot installs always deploy to the pinned home directory,
# so reading through that variable would look where nothing is ever written.
exec 2>/dev/null
command -v jq >/dev/null || exit 0

root=${CLAUDE_PLUGIN_ROOT:-}
[ -n "$root" ] || root=$(cd "$(dirname "$0")/.." && pwd) || exit 0

stamp=$(tr -d '[:space:]' < "${HOME:-}/.claude/claude-tui-line/bin/.plugin-version") || exit 0
running=$(jq -r '.version // empty' "$root/.claude-plugin/plugin.json" | tr -d '[:space:]') || exit 0
[ -n "$stamp" ] && [ -n "$running" ] && [ "$stamp" != "$running" ] || exit 0

jq -nc --arg m "claude-tui-line: plugin is $running but installed binaries are $stamp — run /claude-tui-line:setup to rebuild" '{systemMessage: $m}' || exit 0
exit 0
