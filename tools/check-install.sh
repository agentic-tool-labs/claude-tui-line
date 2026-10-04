#!/usr/bin/env bash
# Exercise install.sh and the SessionStart hook end to end in a sandbox HOME, with `dotnet`
# and `claude` replaced by stubs, so it needs no .NET toolchain and never touches the real
# ~/.claude. jq and git are real.
#
# What it pins: snapshot mode (install from a plugin-cache copy), the handoff to a local
# clone, --dry-run, the version stamp, the clone path's local-marketplace recognition under
# both the `Folder` and `Directory` labels, and the notify-only hook.

set -uo pipefail

REPO="$(cd "$(dirname "$0")/.." && pwd)"
T0=$(mktemp -d)
[ -n "$T0" ] || exit 2
T=$(cd "$T0" && pwd -P)
[ -n "$T" ] && [ "$T" != / ] || exit 2
trap 'rm -rf "$T"' EXIT

BASE_PATH="$PATH"
STUBS="$T/stubs"
mkdir -p "$STUBS"

cat > "$STUBS/dotnet" <<'EOF'
#!/bin/sh
[ -z "${STUB_NO_DOTNET:-}" ] || exit 127
if [ "$1" = "--version" ]; then echo 10.0.100; exit 0; fi
if [ "$1" = "publish" ]; then
  csproj=$2; out=
  while [ $# -gt 0 ]; do [ "$1" = "-o" ] && out=$2; shift; done
  mkdir -p "$out"
  case "$csproj" in
    *Mcp*) printf '#!/bin/sh\nexit 0\n' > "$out/claude-tui-line-mcp"; chmod +x "$out/claude-tui-line-mcp"; : > "$out/claude-tui-line-mcp.dll" ;;
    *) printf '#!/bin/sh\nexit 0\n' > "$out/claude-tui-line"; chmod +x "$out/claude-tui-line" ;;
  esac
  exit 0
fi
exit 1
EOF

cat > "$STUBS/claude" <<'EOF'
#!/bin/sh
echo "$*" >> "$CLAUDE_LOG"
mkdir -p "$STUB_STATE"
case "$1:$2:$3" in
  mcp:list:*)
    [ -f "$STUB_STATE/mcp" ] || exit 0
    c=$(cat "$STUB_STATE/mcp")
    if [ -x "$c" ]; then echo "claude-tui-line: $c - ✓ Connected"; else echo "claude-tui-line: $c - ✗ Failed to connect"; fi ;;
  mcp:add:*) printf '%s' "$6" > "$STUB_STATE/mcp" ;;
  mcp:remove:*) rm -f "$STUB_STATE/mcp" ;;
  plugin:marketplace:list)
    echo "Configured marketplaces:"; echo
    if [ -n "${STUB_DECOY:-}" ]; then echo "  ❯ my-claude-tui-line"; echo "    Source: Folder ($STUB_DECOY)"; echo; fi
    echo "  ❯ ponytail"; echo "    Source: GitHub (DietrichGebert/ponytail)"; echo
    if [ -f "$STUB_STATE/mkt" ]; then
      l=$(cut -d'|' -f1 "$STUB_STATE/mkt"); a=$(cut -d'|' -f2- "$STUB_STATE/mkt")
      echo "  ❯ claude-tui-line"; echo "    Source: $l ($a)"
    fi ;;
  plugin:marketplace:add) printf '%s|%s' "${STUB_LOCAL_LABEL:-Folder}" "$4" > "$STUB_STATE/mkt" ;;
  plugin:marketplace:remove) rm -f "$STUB_STATE/mkt" ;;
esac
exit 0
EOF
chmod +x "$STUBS/dotnet" "$STUBS/claude"

PASS=0; FAILN=0
ok()  { PASS=$((PASS + 1)); }
bad() { FAILN=$((FAILN + 1)); printf 'FAIL %s: %s\n' "$CASE" "$1" >&2; [ -z "${2:-}" ] || sed 's/^/    | /' "$2" | tail -n 25 >&2; }
expect() { # expect <description> <command...>
  local d=$1; shift
  if "$@" >/dev/null 2>&1; then ok; else bad "$d" "${OUT:-}"; fi
}
has()  { grep -qF -- "$2" "$1"; }
lacks() { ! grep -qF -- "$2" "$1"; }

mkfix() { # mkfix <dir> — the files install.sh and the hook need; dotnet is stubbed
  mkdir -p "$1/.claude-plugin" "$1/bin"
  cp "$REPO/install.sh" "$1/install.sh"; chmod +x "$1/install.sh"
  jq '.version = "0.4.0"' "$REPO/.claude-plugin/plugin.json" > "$1/.claude-plugin/plugin.json"
  cp "$REPO/bin/claude-tui-line-mcp" "$1/bin/claude-tui-line-mcp"; chmod -x "$1/bin/claude-tui-line-mcp"
  cp -R "$REPO/hooks" "$1/hooks"
}

N=0
fresh() { # fresh sandbox: HOME, stub state, snapshot fixture, clone fixture, no-.git fixture
  N=$((N + 1)); H="$T/s$N"
  export HOME="$H/home" STUB_STATE="$H/state" CLAUDE_LOG="$H/claude.log"
  export PATH="$STUBS:$BASE_PATH"
  unset CLAUDE_PLUGIN_DATA CLAUDE_PLUGIN_ROOT STUB_NO_DOTNET STUB_LOCAL_LABEL
  mkdir -p "$HOME" "$STUB_STATE"; : > "$CLAUDE_LOG"
  SNAP="$HOME/.claude/plugins/cache/claude-tui-line/claude-tui-line/0.4.0"
  CLONE="$H/clone"; NOGIT="$H/nogit"
  mkfix "$SNAP"; mkfix "$CLONE"; mkfix "$NOGIT"
  git -C "$CLONE" init -q
  git -C "$CLONE" add -A
  git -C "$CLONE" -c user.name=t -c user.email=t@t commit -q -m fixture
  BIN="$HOME/.claude/claude-tui-line/bin"
  OUT="$H/out"
}
github_mkt() { printf 'GitHub|agentic-tool-labs/claude-tui-line' > "$STUB_STATE/mkt"; }
run() { "$@" > "$OUT" 2>&1 < /dev/null; RC=$?; }
mcp_adds() { grep -c '^mcp add ' "$CLAUDE_LOG"; }

# ---- snapshot mode ---------------------------------------------------------

CASE=G.1; fresh; github_mkt
run "$SNAP/install.sh" --dry-run
expect "exit 0" test "$RC" = 0
expect "plan header" has "$OUT" "this run would have:"
expect "statusLine line" has "$OUT" "rewritten settings.json statusLine"
expect "MCP line names the pinned apphost" has "$OUT" "at $BIN/claude-tui-line-mcp"
expect "no plugin-registration line" lacks "$OUT" "registered the plugin"
expect "no stray shell error" lacks "$OUT" "No such file"
expect "no chmod line" lacks "$OUT" "chmod +x"

CASE=G.3
before=$(cd "$HOME" && find . -not -path './.claude/claude-tui-line/backups*' | sort)
run "$SNAP/install.sh" --dry-run
after=$(cd "$HOME" && find . -not -path './.claude/claude-tui-line/backups*' | sort)
expect "dry-run leaves HOME unchanged" test "$before" = "$after"
expect "dry-run makes only list calls" test "$(grep -vc ' list$' "$CLAUDE_LOG")" = 0

CASE=G.14; fresh; github_mkt
mode_before=$(ls -l "$SNAP/bin/claude-tui-line-mcp" | cut -c1-10)

CASE=G.2
export CLAUDE_PLUGIN_DATA="$T/pd"
run "$SNAP/install.sh" --non-interactive
unset CLAUDE_PLUGIN_DATA
expect "exit 0" test "$RC" = 0
expect "cli deployed" test -x "$BIN/claude-tui-line"
expect "mcp deployed with its dll" test -f "$BIN/claude-tui-line-mcp.dll"
expect "CLAUDE_PLUGIN_DATA ignored" test ! -e "$T/pd"
expect "stamp = 0.4.0" test "$(cat "$BIN/.plugin-version" 2>/dev/null)" = "0.4.0"
expect "statusLine pinned" test "$(jq -r .statusLine.command "$HOME/.claude/settings.json")" = "$BIN/claude-tui-line"
expect "mcp add targets pinned apphost" has "$CLAUDE_LOG" "mcp add -s user claude-tui-line $BIN/claude-tui-line-mcp"
expect "no marketplace add/remove" test "$(grep -cE '^plugin marketplace (add|remove)' "$CLAUDE_LOG")" = 0
expect "no plugin install" lacks "$CLAUDE_LOG" "plugin install"
expect "exactly one origin" test "$(grep -c '"kind":"origin"' "$HOME/.claude/claude-tui-line/backups/ledger.jsonl")" = 1

CASE=G.14b
expect "snapshot wrapper mode unchanged" test "$(ls -l "$SNAP/bin/claude-tui-line-mcp" | cut -c1-10)" = "$mode_before"

CASE=G.4
run "$SNAP/install.sh" --dry-run
expect "exit 0" test "$RC" = 0
expect "already installed" has "$OUT" "already installed"

CASE=G.5
expect "settings.json free of snapshot path" lacks "$HOME/.claude/settings.json" "$HOME/.claude/plugins/"
expect "ledger free of snapshot path" lacks "$HOME/.claude/claude-tui-line/backups/ledger.jsonl" "$HOME/.claude/plugins/"
expect "mcp add argv free of snapshot path" test "$(grep '^mcp add ' "$CLAUDE_LOG" | grep -cF "$HOME/.claude/plugins/")" = 0

CASE=G.6
jq '.version = "0.4.1"' "$SNAP/.claude-plugin/plugin.json" > "$H/pj" && mv "$H/pj" "$SNAP/.claude-plugin/plugin.json"
run "$SNAP/install.sh" --dry-run
expect "stale stamp plans a rebuild" has "$OUT" "built and deployed"
jq '.version = "0.4.0"' "$SNAP/.claude-plugin/plugin.json" > "$H/pj" && mv "$H/pj" "$SNAP/.claude-plugin/plugin.json"

CASE=G.7
rm -rf "$SNAP"
expect "statusLine target survives snapshot removal" test -x "$(jq -r .statusLine.command "$HOME/.claude/settings.json")"
expect "mcp target survives snapshot removal" test -x "$(cat "$STUB_STATE/mcp")"
claude mcp list > "$H/mcp.out" 2>&1
expect "mcp still Connected" has "$H/mcp.out" "Connected"

CASE=G.16
run "$CLONE/install.sh" --non-interactive --allow-marketplace-replace
expect "exit 0" test "$RC" = 0
expect "clone sees snapshot-built binaries as stale" has "$OUT" "built from a plugin snapshot"
expect "stamp removed" test ! -e "$BIN/.plugin-version"

CASE=G.8; fresh; printf 'Folder|%s' "$CLONE" > "$STUB_STATE/mkt"
run "$SNAP/install.sh" --dry-run
expect "exit 0" test "$RC" = 0
expect "handoff line" has "$OUT" "handing off to $CLONE/install.sh"
expect "clone ran" has "$OUT" "running from a git checkout at $CLONE"

CASE=G.9; fresh; printf 'Folder|%s' "$NOGIT" > "$STUB_STATE/mkt"
run "$SNAP/install.sh" --dry-run
expect "exit 0" test "$RC" = 0
expect "no handoff" lacks "$OUT" "handing off"
expect "info line" has "$OUT" "not a usable checkout"
expect "snapshot mode" has "$OUT" "running from a plugin snapshot"

CASE=G.13; fresh; github_mkt
STUB_NO_DOTNET=1 run "$SNAP/install.sh" --dry-run
expect "exit 1" test "$RC" = 1
expect "dotnet not found" has "$OUT" "dotnet not found"
expect "nothing written" test ! -e "$HOME/.claude/claude-tui-line"

CASE=G.24; fresh; github_mkt
DECOY="$H/decoy"; mkdir -p "$DECOY/.git"
printf '#!/bin/sh\ntouch "%s"\n' "$H/decoy-ran" > "$DECOY/install.sh"; chmod +x "$DECOY/install.sh"
STUB_DECOY="$DECOY" run "$SNAP/install.sh" --dry-run
expect "exit 0" test "$RC" = 0
expect "snapshot mode" has "$OUT" "running from a plugin snapshot"
expect "no handoff" lacks "$OUT" "handing off"
expect "decoy script never ran" test ! -e "$H/decoy-ran"

# ---- clone mode ------------------------------------------------------------

for label in Folder Directory; do
  case $label in Folder) CASE=G.10 ;; *) CASE=G.17 ;; esac
  fresh
  STUB_LOCAL_LABEL=$label run "$CLONE/install.sh" --non-interactive --allow-marketplace-replace
  expect "exit 0" test "$RC" = 0
  expect "mcp add targets the clone wrapper" has "$CLAUDE_LOG" "mcp add -s user claude-tui-line $CLONE/bin/claude-tui-line-mcp"
  expect "marketplace add once" test "$(grep -c "^plugin marketplace add $CLONE\$" "$CLAUDE_LOG")" = 1
  expect "no marketplace remove" test "$(grep -c '^plugin marketplace remove' "$CLAUDE_LOG")" = 0
  expect "plugin install" has "$CLAUDE_LOG" "plugin install claude-tui-line@claude-tui-line -s user -y"
  expect "wrapper chmod'd" test -x "$CLONE/bin/claude-tui-line-mcp"
  expect "no stamp" test ! -e "$BIN/.plugin-version"
  expect "verify passed" has "$OUT" "done"
done

CASE=G.15; fresh
run "$CLONE/install.sh" --non-interactive --allow-marketplace-replace
run "$CLONE/install.sh" --dry-run
expect "exit 0" test "$RC" = 0
expect "already installed" has "$OUT" "already installed"

CASE=G.11; fresh
run "$CLONE/install.sh"
expect "exit 1" test "$RC" = 1
expect "refuses" has "$OUT" "refusing to write anything"
expect "plan list" has "$OUT" "this run would have:"
expect "no binaries" test ! -e "$BIN"
expect "no settings" test ! -e "$HOME/.claude/settings.json"
expect "only list calls" test "$(grep -vc ' list$' "$CLAUDE_LOG")" = 0

CASE=G.12; fresh
run "$NOGIT/install.sh" --dry-run
expect "exit 1" test "$RC" = 1
expect "no .git message" has "$OUT" "has no .git"

# ---- hook ------------------------------------------------------------------

hook() { # hook <plugin root or "-"> — stdout in $H/hout, stderr in $H/herr
  local root=$1; shift
  if [ "$root" = - ]; then unset CLAUDE_PLUGIN_ROOT; else export CLAUDE_PLUGIN_ROOT="$root"; fi
  /bin/sh "$FIXHOOK" > "$H/hout" 2> "$H/herr"; RC=$?
}
silent() { [ "$RC" = 0 ] && [ ! -s "$H/hout" ] && [ ! -s "$H/herr" ]; }
stamp() { mkdir -p "$BIN"; printf '%s\n' "$1" > "$BIN/.plugin-version"; }

fresh; FIXHOOK="$SNAP/hooks/stamp-check.sh"
mkfix "$H/v041"; jq '.version = "0.4.1"' "$H/v041/.claude-plugin/plugin.json" > "$H/pj" && mv "$H/pj" "$H/v041/.claude-plugin/plugin.json"

CASE=G.18; stamp 0.4.0; hook "$SNAP"
expect "matching stamp is silent" silent

CASE=G.19; hook "$H/v041"
expect "exit 0" test "$RC" = 0
expect "stderr empty" test ! -s "$H/herr"
expect "one JSON object naming both versions and the command" \
  jq -es 'length == 1 and (.[0].systemMessage | contains("0.4.1") and contains("0.4.0") and contains("/claude-tui-line:setup"))' "$H/hout"

CASE=G.20
rm -f "$BIN/.plugin-version"; hook "$SNAP"; expect "no stamp" silent
: > "$BIN/.plugin-version"; hook "$SNAP"; expect "empty stamp" silent
stamp 0.4.0; mkdir -p "$H/empty"; hook "$H/empty"; expect "missing plugin.json" silent
mkdir -p "$H/nojq"; for t in tr dirname; do ln -sf "$(command -v $t)" "$H/nojq/$t"; done
( export PATH="$H/nojq"; hook "$H/v041"; silent ) && ok || bad "jq absent from PATH"

CASE=G.21
stamp 0.4.0; mkdir -p "$T/pd/bin"; printf '9.9.9\n' > "$T/pd/bin/.plugin-version"
CLAUDE_PLUGIN_DATA="$T/pd" hook "$SNAP"
expect "CLAUDE_PLUGIN_DATA ignored" silent

CASE=G.22
mkdir -p "$H/elsewhere"
( cd "$H/elsewhere" && hook "$SNAP" && silent ) && ok || bad "matching stamp from another cwd"
( cd "$H/elsewhere" && hook "$H/v041" ) ; expect "mismatch from another cwd" has "$H/hout" "0.4.1"
FIXHOOK="$H/v041/hooks/stamp-check.sh"; ( cd "$H/elsewhere" && hook - ); expect "root found from the script location" has "$H/hout" "0.4.1"

CASE=G.23
HJ="$REPO/hooks/hooks.json"
expect "one SessionStart entry, startup|resume, timeout <= 10" \
  jq -e '(.hooks.SessionStart | length) == 1 and .hooks.SessionStart[0].matcher == "startup|resume" and (.hooks.SessionStart[0].hooks[0].timeout | type == "number" and . <= 10)' "$HJ"
script=$(jq -r '.hooks.SessionStart[0].hooks[0].command' "$HJ" | grep -o 'hooks/[A-Za-z0-9._-]*')
expect "hook script exists and is executable" test -x "$REPO/$script"

echo
if [ "$FAILN" -ne 0 ]; then
  echo "check-install: $FAILN of $((PASS + FAILN)) assertions failed." >&2
  exit 1
fi
echo "check-install: $PASS assertions passed."
