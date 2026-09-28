#!/usr/bin/env bash
# Takes a fresh checkout to a working local install. Build and deploy are
# separate phases (SPEC.md §14): build publishes to gitignored ./publish and
# ./publish-mcp; deploy copies those into $BIN_DIR — the one location
# src/ClaudeTuiLineMcp's CliLocator and bin/claude-tui-line-mcp already
# hard-code — via temp-then-rename so a statusline exec'ing once a second
# never observes a partial binary. Registers the MCP server and the plugin at
# user scope so both point at this checkout, and backs up whatever statusline
# already exists via docs/backup-ledger.md's ledger before touching it.
# Idempotent and reversible — see docs/backup-ledger.md for restore,
# `claude mcp remove -s user` / `claude plugin uninstall` for registration.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")" && pwd)"
BIN_DIR="${CLAUDE_PLUGIN_DATA:-$HOME/.claude/claude-tui-line}/bin"
STAGE_DIR="$REPO_ROOT/publish"
STAGE_MCP_DIR="$REPO_ROOT/publish-mcp"
LEDGER_DIR="$HOME/.claude/claude-tui-line/backups"
LEDGER="$LEDGER_DIR/ledger.jsonl"
SETTINGS="$HOME/.claude/settings.json"
CONFIG="${CLAUDE_TUI_LINE_CONFIG:-$HOME/.claude/claude-tui-line.json}"
MCP_WRAPPER="$REPO_ROOT/bin/claude-tui-line-mcp"
MCP_SCOPE="user"
PLUGIN_SCOPE="user"

stage_cli="$STAGE_DIR/claude-tui-line"
stage_mcp="$STAGE_MCP_DIR/claude-tui-line-mcp"
cli_bin="$BIN_DIR/claude-tui-line"
mcp_bin="$BIN_DIR/claude-tui-line-mcp"
target_status_line="$cli_bin"

NON_INTERACTIVE=0
ALLOW_MARKETPLACE_REPLACE=0
for arg in "$@"; do
  case "$arg" in
    --non-interactive) NON_INTERACTIVE=1 ;;
    --allow-marketplace-replace) ALLOW_MARKETPLACE_REPLACE=1 ;;
    -h|--help) echo "Usage: $0 [--non-interactive] [--allow-marketplace-replace]"; exit 0 ;;
    *) echo "install.sh: unrecognized argument: $arg" >&2; exit 2 ;;
  esac
done

pass() { printf '  \033[32m\xe2\x9c\x93\033[0m %s\n' "$1"; }
fail() { printf '  \033[31m\xe2\x9c\x97\033[0m %s\n' "$1"; }
warn() { printf '  \033[33m!\033[0m %s\n' "$1"; }
info() { printf '  %s\n' "$1"; }

mtime_epoch() { stat -f %m "$1" 2>/dev/null || stat -c %Y "$1"; }

confirm() {
  # $1 = prompt text. Yes only on an explicit interactive y/Y, or
  # automatically under --non-interactive (which is itself the consent).
  local prompt="$1"
  if [[ "$NON_INTERACTIVE" == "1" ]]; then
    info "$prompt -> yes (--non-interactive)"
    return 0
  fi
  printf '  %s [y/N] ' "$prompt"
  local reply=""
  read -r reply || reply=""
  [[ "$reply" =~ ^[Yy]$ ]]
}

marketplace_block() {
  claude plugin marketplace list 2>&1 | awk '/claude-tui-line$/{f=1} f{print} f&&/Source:/{exit}' || true
}

do_build() {
  echo "Build (staging)"
  mkdir -p "$STAGE_DIR" "$STAGE_MCP_DIR"
  local log_dir
  log_dir=$(mktemp -d)
  if dotnet publish "$REPO_ROOT/src/ClaudeTuiLine/ClaudeTuiLine.csproj" -c Release -o "$STAGE_DIR" > "$log_dir/cli.log" 2>&1; then
    pass "claude-tui-line staged -> $stage_cli"
  else
    fail "dotnet publish (ClaudeTuiLine) failed:"
    tail -n 30 "$log_dir/cli.log" | sed 's/^/    /'
    exit 1
  fi
  if dotnet publish "$REPO_ROOT/src/ClaudeTuiLineMcp/ClaudeTuiLineMcp.csproj" -c Release -o "$STAGE_MCP_DIR" > "$log_dir/mcp.log" 2>&1; then
    pass "claude-tui-line-mcp staged -> $stage_mcp"
  else
    fail "dotnet publish (ClaudeTuiLineMcp) failed:"
    tail -n 30 "$log_dir/mcp.log" | sed 's/^/    /'
    exit 1
  fi
  if [[ ! -x "$stage_cli" || ! -x "$stage_mcp" ]]; then
    fail "publish reported success but the staged binaries are missing or not executable"
    exit 1
  fi
  echo
}

do_deploy() {
  # $BIN_DIR may hold a binary Claude Code is exec'ing once a second
  # (refreshInterval: 1) — copy-to-temp-then-rename, same filesystem, so a
  # concurrent reader always sees a complete old or new file, never a partial
  # one (§3.3).
  echo "Deploy ($BIN_DIR)"
  mkdir -p "$BIN_DIR"

  # claude-tui-line (CLI) is AOT/self-contained: one file.
  local tmp
  tmp="$BIN_DIR/.claude-tui-line.tmp.$$"
  cp "$stage_cli" "$tmp"
  chmod +x "$tmp"
  mv -f "$tmp" "$cli_bin"
  pass "deployed claude-tui-line -> $cli_bin"

  # claude-tui-line-mcp is framework-dependent (SPEC-83/SPEC-12.6): the
  # apphost needs its .dll, .deps.json, .runtimeconfig.json, and every
  # dependency DLL alongside it, not just the entrypoint. Deploy every staged
  # file flat into $BIN_DIR (the wrapper at bin/claude-tui-line-mcp expects
  # $BIN_DIR/claude-tui-line-mcp directly), dependencies first and the
  # entrypoint last, so a client spawning the apphost mid-deploy never
  # observes a new apphost next to old-or-missing dependencies.
  local f rel dest
  while IFS= read -r -d '' f; do
    rel="${f#"$STAGE_MCP_DIR"/}"
    [[ "$rel" == "claude-tui-line-mcp" ]] && continue
    dest="$BIN_DIR/$rel"
    mkdir -p "$(dirname "$dest")"
    tmp="$(dirname "$dest")/.$(basename "$rel").tmp.$$"
    cp "$f" "$tmp"
    mv -f "$tmp" "$dest"
  done < <(find "$STAGE_MCP_DIR" -type f -print0)
  pass "deployed claude-tui-line-mcp's dependencies -> $BIN_DIR"

  tmp="$BIN_DIR/.claude-tui-line-mcp.tmp.$$"
  cp "$stage_mcp" "$tmp"
  chmod +x "$tmp"
  mv -f "$tmp" "$mcp_bin"
  pass "deployed claude-tui-line-mcp -> $mcp_bin"

  if [[ ! -x "$cli_bin" || ! -x "$mcp_bin" ]]; then
    fail "deploy reported success but $BIN_DIR binaries are missing or not executable"
    exit 1
  fi
  echo
}

ledger_captured=0

capture_ledger_once() {
  [[ "$ledger_captured" == "1" ]] && return 0

  # Torn-final-line guard (S3): append-only per docs/backup-ledger.md, but a
  # previous writer's interrupted `>>` can leave the file without a trailing
  # newline — start our append on its own line rather than concatenating onto
  # a torn one. Mirrors src/ClaudeTuiLineMcp/BackupLedger.cs's intent without
  # copying its C#-specific seek mechanics.
  if [[ -s "$LEDGER" ]] && [[ "$(tail -c1 "$LEDGER" 2>/dev/null)" != "" ]]; then
    printf '\n' >> "$LEDGER"
  fi

  local ts ts_compact
  ts=$(date -u +%Y-%m-%dT%H:%M:%SZ)
  ts_compact="${ts//[:]/}"

  local settings_copy_name=null settings_sha=null status_line_json=null
  if [[ -f "$SETTINGS" ]]; then
    settings_copy_name="${ts_compact}-settings.json"
    while [[ -e "$LEDGER_DIR/$settings_copy_name" ]]; do settings_copy_name="${settings_copy_name%.json}-2.json"; done
    cp "$SETTINGS" "$LEDGER_DIR/$settings_copy_name"
    settings_sha=$(shasum -a 256 "$SETTINGS" | awk '{print $1}')
    status_line_json=$(jq -c '.statusLine // null' "$SETTINGS")
  fi

  local script_original=null script_copy=null script_sha=null
  if [[ -n "$current_status_line" && -f "$current_status_line" ]]; then
    script_original="$current_status_line"
    script_copy="${ts_compact}-$(basename "$current_status_line")"
    while [[ -e "$LEDGER_DIR/$script_copy" ]]; do script_copy="${script_copy}-2"; done
    cp "$current_status_line" "$LEDGER_DIR/$script_copy"
    script_sha=$(shasum -a 256 "$current_status_line" | awk '{print $1}')
  fi

  local config_copy=null config_sha=null
  if [[ -f "$CONFIG" ]]; then
    config_copy="${ts_compact}-$(basename "$CONFIG")"
    while [[ -e "$LEDGER_DIR/$config_copy" ]]; do config_copy="${config_copy}-2"; done
    cp "$CONFIG" "$LEDGER_DIR/$config_copy"
    config_sha=$(shasum -a 256 "$CONFIG" | awk '{print $1}')
  fi

  local note
  if [[ "$ledger_kind" == "origin" ]]; then
    note="state before claude-tui-line ever touched this machine"
  elif [[ "$points_at_claude_tui_line" == "1" && "$has_origin" != "1" ]]; then
    note="checkpoint before install.sh — no origin recorded because statusLine already pointed at a claude-tui-line binary (pre-install state is unrecoverable)"
  else
    note="checkpoint before install.sh"
  fi

  jq -nc \
    --arg kind "$ledger_kind" \
    --arg timestamp "$ts" \
    --argjson statusLine "$status_line_json" \
    --arg settingsCopy "$settings_copy_name" \
    --arg settingsSha256 "$settings_sha" \
    --arg configOriginalPath "$CONFIG" \
    --arg configCopy "$config_copy" \
    --arg configSha256 "$config_sha" \
    --arg scriptOriginalPath "$script_original" \
    --arg scriptCopy "$script_copy" \
    --arg scriptSha256 "$script_sha" \
    --arg note "$note" \
    '
    def nn($v): if $v == "null" or $v == "" then null else $v end;
    {kind:$kind, timestamp:$timestamp, statusLine:$statusLine,
     settingsCopy: nn($settingsCopy), settingsSha256: nn($settingsSha256),
     configOriginalPath: $configOriginalPath, configCopy: nn($configCopy), configSha256: nn($configSha256)}
    + (if nn($scriptOriginalPath) then {scriptOriginalPath:$scriptOriginalPath, scriptCopy:$scriptCopy, scriptSha256:$scriptSha256} else {} end)
    + {note:$note}
    ' >> "$LEDGER"

  ledger_captured=1
  info "ledger: appended a $ledger_kind entry"
}

echo
echo "claude-tui-line — install"
echo

# ---------------------------------------------------------------------------
# Phase 1: validate — read-only, may abort freely. §4.4's snapshot refusal
# comes first, before any git or claude call.
# ---------------------------------------------------------------------------

echo "1. Checkout sanity"
case "$REPO_ROOT" in
  "$HOME/.claude/plugins/"*)
    fail "install.sh is running from a synced plugin snapshot ($REPO_ROOT), not a git checkout"
    info "a snapshot is overwritten by the next marketplace sync — run ./install.sh from your actual git clone instead"
    exit 1
    ;;
esac
if [[ ! -e "$REPO_ROOT/.git" ]]; then
  fail "$REPO_ROOT has no .git — this does not look like a git checkout"
  info "run ./install.sh from your actual git clone instead"
  exit 1
fi
pass "running from a git checkout at $REPO_ROOT"
echo

echo "2. Toolchain"
if ! dotnet_version=$(dotnet --version 2>/dev/null); then
  fail "dotnet not found on PATH"
  info "Install the .NET 10 SDK: https://dotnet.microsoft.com/download"
  exit 1
fi
dotnet_major="${dotnet_version%%.*}"
if [[ ! "$dotnet_major" =~ ^[0-9]+$ ]] || (( dotnet_major < 10 )); then
  fail "dotnet $dotnet_version found, but .NET 10+ is required"
  exit 1
fi
pass "dotnet $dotnet_version"

if ! command -v jq >/dev/null 2>&1; then
  fail "jq not found on PATH — required to write settings.json's statusLine key without disturbing the rest of the file"
  exit 1
fi
pass "jq present"

if ! command -v claude >/dev/null 2>&1; then
  fail "claude CLI not found on PATH — required for MCP and plugin registration"
  exit 1
fi
pass "claude CLI present"
echo

echo "3. \$BIN_DIR"
case "$BIN_DIR" in
  /*) ;;
  *) fail "\$BIN_DIR resolved to a non-absolute path: $BIN_DIR"; exit 1 ;;
esac
if [[ "$BIN_DIR" == "/bin" || "$BIN_DIR" == "/bin/"* ]]; then
  fail "\$BIN_DIR resolved to $BIN_DIR — CLAUDE_PLUGIN_DATA makes this collapse onto the system binary directory; refusing to publish there"
  exit 1
fi
pass "\$BIN_DIR = $BIN_DIR"
echo

echo "4. Binaries (deployed)"
c1_ok=0; c2_ok=0; c4_ok=0; c7_ok=1
if [[ -x "$cli_bin" ]]; then c1_ok=1; pass "claude-tui-line -> $cli_bin"; else fail "claude-tui-line missing or not executable at $cli_bin"; fi
if [[ -x "$mcp_bin" && -f "${mcp_bin}.dll" ]]; then
  c2_ok=1
  pass "claude-tui-line-mcp -> $mcp_bin"
elif [[ -x "$mcp_bin" ]]; then
  fail "claude-tui-line-mcp is present at $mcp_bin but its .dll is missing — framework-dependent deploy is incomplete (partial/pre-fix deploy)"
else
  fail "claude-tui-line-mcp missing or not executable at $mcp_bin"
fi
if [[ -x "$MCP_WRAPPER" ]]; then c4_ok=1; pass "$MCP_WRAPPER is executable"; else warn "$MCP_WRAPPER is not executable (fresh clones land it this way)"; fi

if [[ "$c1_ok" == "1" ]]; then
  commit_epoch=$(git -C "$REPO_ROOT" log -1 --format=%ct 2>/dev/null) || commit_epoch=""
  bin_epoch=$(mtime_epoch "$cli_bin" 2>/dev/null) || bin_epoch=""
  if [[ -n "$commit_epoch" && -n "$bin_epoch" ]]; then
    if (( bin_epoch < commit_epoch )); then
      c7_ok=0
      warn "claude-tui-line predates HEAD's commit time — rebuild recommended"
    else
      pass "claude-tui-line is newer than HEAD's commit"
    fi
  else
    warn "could not compare binary mtime against HEAD's commit time — skipping freshness check"
  fi
fi
echo

echo "5. .mcp.json (report only — this repo does not ship one; §4.3)"
mcp_json="$REPO_ROOT/.mcp.json"
c3_ok=1
if [[ -f "$mcp_json" ]]; then
  has_entry=$(jq -e '.mcpServers["claude-tui-line"]' "$mcp_json" >/dev/null 2>&1 && echo 1 || echo 0)
  if [[ "$has_entry" == "1" ]]; then
    c3_ok=0
    fail ".mcp.json has a claude-tui-line mcpServers entry — this is always broken (project-scope variables are never set) and install.sh will not regenerate it. Remove it by hand."
  else
    info ".mcp.json exists but has no claude-tui-line entry"
  fi
else
  info "no .mcp.json at $mcp_json (expected — MCP is registered via claude mcp add instead)"
fi
echo

echo "6. settings.json statusLine"
current_status_line=""
if [[ -f "$SETTINGS" ]]; then
  current_status_line=$(jq -r '.statusLine.command // ""' "$SETTINGS" 2>/dev/null || echo "")
fi
c5_ok=0
if [[ -z "$current_status_line" ]]; then
  info "no statusLine currently configured"
elif [[ "$current_status_line" == "$target_status_line" ]]; then
  c5_ok=1
  pass "statusLine already points at $target_status_line"
elif [[ "$current_status_line" == *'${'* ]]; then
  fail "statusLine is an unexpanded variable: $current_status_line"
elif [[ "$current_status_line" == *"/publish/"* || "$current_status_line" == *"/publish-mcp/"* ]]; then
  fail "statusLine points into build staging, not the deployed binary: $current_status_line"
else
  warn "statusLine points at $current_status_line, not $target_status_line"
fi
echo

# S4: match on whether the command string CONTAINS a claude-tui-line binary
# path, not basename equality — statusLine.command routinely carries
# arguments or an interpreter prefix ("/path/claude-tui-line --foo"), and
# basename equality misses those, which can misfire the origin/checkpoint
# choice below in the direction docs/backup-ledger.md:167 makes unrecoverable.
points_at_claude_tui_line=0
if [[ "$current_status_line" == *"claude-tui-line"* ]]; then
  points_at_claude_tui_line=1
fi

echo "7. Registration"
mcp_matches=$(claude mcp list 2>&1 | grep -E '^claude-tui-line:' || true)
mcp_match_count=0
[[ -n "$mcp_matches" ]] && mcp_match_count=$(printf '%s\n' "$mcp_matches" | grep -c '^claude-tui-line:' || true)
c6_mcp_ok=0
if [[ "$mcp_match_count" == "1" ]] && [[ "$mcp_matches" == *"Connected"* ]]; then
  c6_mcp_ok=1
  pass "MCP: $mcp_matches"
elif [[ "$mcp_match_count" == "0" ]]; then
  fail "MCP: claude-tui-line not registered"
else
  fail "MCP: expected exactly one claude-tui-line registration, found $mcp_match_count:"
  printf '%s\n' "$mcp_matches" | while IFS= read -r line; do info "  $line"; done
fi

block=$(marketplace_block)
c6_plugin_ok=0
if [[ -n "$block" ]]; then
  if echo "$block" | grep -Fq "Source: Directory ($REPO_ROOT)"; then
    c6_plugin_ok=1
    pass "plugin marketplace: local checkout ($REPO_ROOT)"
  else
    fail "plugin marketplace: $(echo "$block" | grep 'Source:' | sed 's/^ *//')"
  fi
else
  fail "plugin marketplace: claude-tui-line not registered"
fi
echo

echo "8. Ledger"
mkdir -p "$LEDGER_DIR" 2>/dev/null || true
if [[ ! -d "$LEDGER_DIR" || ! -w "$LEDGER_DIR" ]]; then
  fail "cannot create or write $LEDGER_DIR"
  exit 1
fi
pass "$LEDGER_DIR is writable"

has_origin=0
if [[ -f "$LEDGER" ]] && grep -q '"kind"[[:space:]]*:[[:space:]]*"origin"' "$LEDGER" 2>/dev/null; then
  has_origin=1
fi
if [[ "$has_origin" == "1" ]]; then
  info "ledger already has an origin entry — any write below appends a checkpoint"
  ledger_kind="checkpoint"
elif [[ "$points_at_claude_tui_line" == "1" ]]; then
  info "statusLine already points at a claude-tui-line binary and no origin exists — this writes a checkpoint, not an origin (pre-install state is unrecoverable)"
  ledger_kind="checkpoint"
else
  info "no origin exists and statusLine does not point at claude-tui-line — a write below would be a genuine first install and writes an origin"
  ledger_kind="origin"
fi
echo

all_ok=$(( c1_ok && c2_ok && c3_ok && c4_ok && c7_ok && c5_ok && c6_mcp_ok && c6_plugin_ok ))

if [[ "$all_ok" == "1" ]]; then
  pass "already installed"
  info "claude-tui-line: $cli_bin"
  info "claude-tui-line-mcp: $mcp_bin"
  echo
  if confirm "Rebuild and redeploy anyway?"; then
    do_build
    do_deploy
  fi
  exit 0
fi

if [[ ! -t 0 && "$NON_INTERACTIVE" != "1" ]]; then
  echo
  fail "no TTY and --non-interactive not given — refusing to write anything"
  info "this run would have:"
  [[ "$c1_ok" != "1" || "$c2_ok" != "1" || "$c7_ok" != "1" ]] && info "  - built and deployed claude-tui-line and claude-tui-line-mcp into $BIN_DIR"
  [[ "$c4_ok" != "1" ]] && info "  - chmod +x $MCP_WRAPPER"
  [[ "$c5_ok" != "1" ]] && info "  - rewritten settings.json statusLine from '${current_status_line:-<none>}' to $target_status_line"
  [[ "$c6_mcp_ok" != "1" ]] && info "  - registered the MCP server (claude mcp add -s $MCP_SCOPE)"
  [[ "$c6_plugin_ok" != "1" ]] && info "  - registered the plugin as a local checkout ($REPO_ROOT)"
  [[ "$c3_ok" != "1" ]] && info "  - (and .mcp.json needs a by-hand fix — install.sh will not touch it)"
  info "re-run with a TTY, or pass --non-interactive to proceed unattended"
  exit 1
fi

# ---------------------------------------------------------------------------
# Phase 2-4: prompt, capture, write. Order: deploy binaries, then settings,
# then registration (§8). Ledger capture happens once, before the first write
# that touches settings.json or claude-tui-line.json. write_failed
# accumulates across this phase so a partial failure still reaches phase 5
# and exits non-zero (B4) instead of aborting silently between two
# already-committed writes (B3).
# ---------------------------------------------------------------------------

write_failed=0

if [[ "$c1_ok" != "1" || "$c2_ok" != "1" || "$c7_ok" != "1" ]]; then
  if confirm "Build claude-tui-line and claude-tui-line-mcp (staging)?"; then
    do_build
    if confirm "Deploy the staged binaries into $BIN_DIR (replaces the binary your statusline is currently running, once per second)?"; then
      do_deploy
    else
      warn "deploy declined — binaries are staged in $STAGE_DIR / $STAGE_MCP_DIR but not live"
      write_failed=1
    fi
  else
    fail "build declined — nothing else can proceed without the binaries"
    exit 1
  fi
fi

if [[ "$c4_ok" != "1" ]]; then
  chmod +x "$MCP_WRAPPER"
  pass "chmod +x $MCP_WRAPPER"
fi

if [[ "$c5_ok" != "1" ]]; then
  if confirm "Update $SETTINGS statusLine from '${current_status_line:-<none>}' to $target_status_line?"; then
    capture_ledger_once
    # docs/backup-ledger.md:231 — preserve every other key and the file's
    # formatting; match its existing indent rather than jq's default so an
    # untouched key round-trips byte-identical, not just value-identical.
    indent_flag=(--indent 2)
    if [[ -f "$SETTINGS" ]]; then
      first_indent=$(awk 'NR==2{match($0,/^[ \t]*/); print substr($0,1,RLENGTH); exit}' "$SETTINGS")
      if [[ "$first_indent" == *$'\t'* ]]; then
        indent_flag=(--tab)
      elif [[ -n "$first_indent" ]]; then
        indent_flag=(--indent "${#first_indent}")
      fi
    fi
    tmp_settings=$(mktemp "$(dirname "$SETTINGS")/.settings.json.XXXXXX")
    if [[ -f "$SETTINGS" ]]; then
      jq "${indent_flag[@]}" --arg cmd "$target_status_line" '.statusLine = {"type":"command","command":$cmd,"refreshInterval":1}' "$SETTINGS" > "$tmp_settings"
    else
      mkdir -p "$(dirname "$SETTINGS")"
      jq -n "${indent_flag[@]}" --arg cmd "$target_status_line" '{"statusLine":{"type":"command","command":$cmd,"refreshInterval":1}}' > "$tmp_settings"
    fi
    mv "$tmp_settings" "$SETTINGS"
    pass "statusLine -> $target_status_line"
  else
    warn "statusLine left unchanged"
    write_failed=1
  fi
fi

if [[ "$c6_mcp_ok" != "1" ]]; then
  if confirm "Register the claude-tui-line MCP server (claude mcp add -s $MCP_SCOPE, at $MCP_WRAPPER)?"; then
    claude mcp remove -s "$MCP_SCOPE" claude-tui-line >/dev/null 2>&1 || true
    if claude mcp add -s "$MCP_SCOPE" claude-tui-line "$MCP_WRAPPER"; then
      pass "MCP server registered ($MCP_SCOPE scope)"
    else
      fail "claude mcp add failed"
      write_failed=1
    fi
  else
    warn "MCP registration skipped"
    write_failed=1
  fi
fi

if [[ "$c6_plugin_ok" != "1" ]]; then
  # S7: --non-interactive alone is not consent for a marketplace add/replace
  # — neither `marketplace add` nor a same-name collision has a `-y` this
  # script can pass, so under --non-interactive this whole action requires
  # the separate --allow-marketplace-replace opt-in, or fails closed.
  if [[ "$NON_INTERACTIVE" == "1" && "$ALLOW_MARKETPLACE_REPLACE" != "1" ]]; then
    warn "plugin registration needs --allow-marketplace-replace under --non-interactive (marketplace add has no -y of its own) — skipped"
    write_failed=1
  elif confirm "Register the claude-tui-line plugin as this local checkout ($REPO_ROOT)?"; then
    do_install=1
    if claude plugin marketplace add "$REPO_ROOT"; then
      post_add_block=$(marketplace_block)
      if ! echo "$post_add_block" | grep -Fq "Source: Directory ($REPO_ROOT)"; then
        # Re-adding did not cleanly replace an existing (e.g. GitHub-sourced)
        # marketplace entry of the same name. Removal is destructive to
        # registration state, so it gets its own named prompt/opt-in — never
        # bundled into the one above.
        existing=$(echo "$post_add_block" | grep 'Source:' | sed 's/^ *//')
        if [[ "$NON_INTERACTIVE" == "1" ]]; then
          if [[ "$ALLOW_MARKETPLACE_REPLACE" == "1" ]]; then
            claude plugin marketplace remove claude-tui-line || true
            claude plugin marketplace add "$REPO_ROOT" || { fail "claude plugin marketplace add failed after removal"; do_install=0; write_failed=1; }
          else
            warn "existing claude-tui-line marketplace entry ($existing) needs --allow-marketplace-replace to remove under --non-interactive — skipped"
            do_install=0
            write_failed=1
          fi
        elif confirm "Remove the existing claude-tui-line marketplace entry ($existing) so the local checkout can replace it?"; then
          claude plugin marketplace remove claude-tui-line || true
          if ! claude plugin marketplace add "$REPO_ROOT"; then
            fail "claude plugin marketplace add failed after removal"
            do_install=0
            write_failed=1
          fi
        else
          warn "plugin marketplace left unchanged — plugin registration skipped"
          do_install=0
          write_failed=1
        fi
      fi
    else
      fail "claude plugin marketplace add failed"
      do_install=0
      write_failed=1
    fi

    if [[ "$do_install" == "1" ]]; then
      plugin_install_args=(claude-tui-line@claude-tui-line -s "$PLUGIN_SCOPE")
      [[ "$NON_INTERACTIVE" == "1" ]] && plugin_install_args+=(-y)
      if claude plugin install "${plugin_install_args[@]}"; then
        pass "plugin registered as local checkout ($PLUGIN_SCOPE scope)"
      else
        fail "claude plugin install failed"
        write_failed=1
      fi
    fi
  else
    warn "plugin registration skipped"
    write_failed=1
  fi
fi

# ---------------------------------------------------------------------------
# Phase 5: verify and report. A warning here is a failure, not a note (B4) —
# exit non-zero if anything is still unmet, rather than reporting success
# over a half-install.
# ---------------------------------------------------------------------------

echo
echo "9. Verify"
verify_failed=0
if [[ -x "$cli_bin" ]]; then pass "claude-tui-line: $cli_bin"; else fail "claude-tui-line still missing"; verify_failed=1; fi
if [[ -x "$mcp_bin" ]]; then pass "claude-tui-line-mcp: $mcp_bin"; else fail "claude-tui-line-mcp still missing"; verify_failed=1; fi

final_status=$(jq -r '.statusLine.command // "(none)"' "$SETTINGS" 2>/dev/null || echo "(none)")
if [[ "$final_status" == "$target_status_line" ]]; then pass "statusLine: $final_status"; else warn "statusLine: $final_status"; verify_failed=1; fi

final_matches=$(claude mcp list 2>&1 | grep -E '^claude-tui-line:' || true)
final_match_count=0
[[ -n "$final_matches" ]] && final_match_count=$(printf '%s\n' "$final_matches" | grep -c '^claude-tui-line:' || true)
if [[ "$final_match_count" == "1" ]] && [[ "$final_matches" == *"Connected"* ]]; then
  pass "MCP: connected"
else
  warn "MCP: $final_matches"
  verify_failed=1
fi

final_block=$(marketplace_block)
if echo "$final_block" | grep -Fq "Source: Directory ($REPO_ROOT)"; then
  pass "plugin: local checkout ($REPO_ROOT)"
else
  warn "plugin: $(echo "$final_block" | grep 'Source:' | sed 's/^ *//')"
  verify_failed=1
fi
echo

if [[ "$write_failed" == "1" || "$verify_failed" == "1" ]]; then
  fail "install incomplete — see warnings above"
  exit 1
fi

pass "done"
