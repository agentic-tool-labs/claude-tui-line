#!/usr/bin/env bash
# Every enumerable-key literal the docs quote must be a value the registry actually accepts.
#
# This is the same shape as check-examples.sh applied to a different kind (SPEC-V2-FRAMEWORK.md
# §1.1.2): that script proved a rendered example against the binary; this one proves a quoted
# accepted-value literal against `--accepted --json`. It is a subset assertion, never an equality
# one — the docs may omit a token the registry accepts (README.md's `split` row omits `none` and
# its `distribute` row omits `greedy`, both on purpose), but every token the docs DO quote as an
# accepted value must be one the registry actually accepts.
#
# Only marked tables are scanned — no prose, no fenced block, no unmarked table, in either file.
# A doc-wide backtick scan would sweep up illustrative example configs (kind 3 in §1.1.2's terms,
# e.g. `"overflow": "wrap"` inside a sample JSON block) as if they were claims of completeness,
# which they are not. The marker is the same in-band-HTML-comment convention check-examples.sh's
# rule C already uses on README.md's items-table — carried over rather than reinvented.
#
# Within a marked table's rows: column 1 names one or more keys, each a bare backtick-fenced
# token (`` `minSize` / `maxSize` `` is a real two-key row). A checkable token is a backtick-fenced,
# DOUBLE-QUOTED literal anywhere in the row (`` `"vertical"` ``) — bare backtick tokens are key
# names and are never checked. Every checkable token in a row must be accepted by every key the
# row's column 1 names.
#
# Exit 0 clean, 1 with mismatches listed, 2 if the binary, jq, or the marked table itself could not
# be obtained — never a silent pass, matching check-examples.sh's own discipline.

set -uo pipefail

cd "$(dirname "$0")/.." || exit 2

MARKER='pane-token-table'

die() { echo "check-doc-tokens: $*" >&2; exit 2; }

command -v jq >/dev/null 2>&1 || die "jq is required to read --accepted --json, and is not installed.
  Refusing to report clean without having compared anything. Install jq, or set
  CLAUDE_TUI_LINE_BIN to a built binary on a machine that has it."

# Same oracle discipline as check-examples.sh: the binary is the source of truth, never a
# reimplementation of it. CLAUDE_TUI_LINE_BIN lets a caller reuse an artifact it already built.
if [[ -n "${CLAUDE_TUI_LINE_BIN:-}" ]]; then
    [[ -x "$CLAUDE_TUI_LINE_BIN" ]] || die "CLAUDE_TUI_LINE_BIN is set to '$CLAUDE_TUI_LINE_BIN', which is not executable."
    accepted_json=$("$CLAUDE_TUI_LINE_BIN" --accepted --json 2>/dev/null)
elif command -v dotnet >/dev/null 2>&1; then
    # See check-examples.sh for why build chatter is dropped by seeking the first `{` line rather
    # than by `tail -1` or stderr redirection alone.
    accepted_json=$(dotnet run --project src/ClaudeTuiLine/ClaudeTuiLine.csproj -c Release \
        -v quiet -- --accepted --json 2>/dev/null | sed -n '/^[[:space:]]*{/,$p')
else
    die "no binary. Set CLAUDE_TUI_LINE_BIN, or install the .NET SDK so this can build one."
fi

[[ -n "$accepted_json" ]] || die "--accepted --json produced no output."

# One row per key with a closed set: key <TAB> its accepted tokens, joined on \x01 (a byte that
# cannot appear in a token). Keys with accepted: null (currently only `size`) are deliberately
# excluded here — alsoAccepted is a prose description (AcceptedCommand.cs), not a second token
# list, and comparing quoted literals against an English sentence would be a category error.
checkable_pairs=$(printf '%s' "$accepted_json" \
    | jq -r '.keys[] | select(.accepted != null) | [.key, (.accepted | join("\u0001"))] | @tsv' 2>/dev/null) \
    || die "--accepted --json was not the expected shape (no .keys[] with .key and .accepted)."

[[ -n "$checkable_pairs" ]] || die "--accepted --json reported no key with a closed accepted set — refusing to report clean."

# The keys reported with accepted: null. Named on every run rather than silently swallowed: a skip
# set that grows past `size` unnoticed is how an exemption becomes a hole.
null_keys=$(printf '%s' "$accepted_json" | jq -r '.keys[] | select(.accepted == null) | .key' 2>/dev/null)

FILES=("$@")
if [[ ${#FILES[@]} -eq 0 ]]; then
    # shellcheck disable=SC2207
    FILES=($(git ls-files '*.md' 2>/dev/null))
fi
[[ ${#FILES[@]} -gt 0 ]] || die "no markdown files to scan — refusing to report clean."

{
    printf '%s\n' "$checkable_pairs" | awk -F'\t' 'NF>=2 { print "ACCEPTED\t" $1 "\t" $2 }'
    printf '%s\n' "$null_keys" | awk 'NF { print "NULLKEY\t" $0 }'
} > "/tmp/check-doc-tokens-registry.$$"

awk -v marker="$MARKER" '
# Anchored to the whole line, the same defense check-examples.sh rule C uses: a substring match
# would self-trigger on this very sentence, or on SPEC-V2-FRAMEWORK.md quoting the marker inside a
# fenced code block to document it. The `!fence` guard below is what keeps a quoted marker inert.
BEGIN { markerline = "^[ \t]*<!--.*" marker ".*-->[ \t]*$" }

# The registry file (first argument): one row per accepted-set key, one row per null-accepted key.
# FNR==NR is true only while reading this first file, which is what lets a single pass tell it
# apart from every markdown file that follows.
FNR == NR {
    if ($1 == "ACCEPTED") {
        n = split($3, toks, "\001")
        for (i = 1; i <= n; i++) accepted[$2, toks[i]] = 1
        hasclosedset[$2] = 1
    } else if ($1 == "NULLKEY") {
        isnullkey[$2] = 1
    }
    next
}

FNR == 1 { file = FILENAME; fence = 0; intable = 0 }

{
    if ($0 ~ /^[ \t]*(```|~~~)/) { fence = !fence; next }

    if (!fence && !intable && $0 ~ markerline) { intable = 1; tablerows = 0; tablegap = 0; next }

    if (intable) {
        if ($0 !~ /^[ \t]*\|/) {
            # A blank line between the marker and the header row is normal markdown (most
            # renderers require one); allow up to two before giving up on ever finding a row.
            if (tablerows == 0 && !fence && ++tablegap <= 2) next
            intable = 0
            next
        }

        tablerows++
        row = $0
        sub(/^[ \t]*\|/, "", row); sub(/\|[ \t]*$/, "", row)
        n = split(row, col, /\|/)
        if (n < 1) next

        # Column 1: every bare backtick-fenced token is a key name.
        delete keys; nkeys = 0
        c1 = col[1]
        while (match(c1, /`[^`]+`/)) {
            tok = substr(c1, RSTART + 1, RLENGTH - 2)
            c1 = substr(c1, RSTART + RLENGTH)
            if (tok !~ /"/) { nkeys++; keys[nkeys] = tok }
        }
        if (nkeys == 0) next  # header row, separator row, or a row whose first column names nothing

        # Anywhere in the row: every backtick-fenced, double-quoted token is a checkable value.
        delete values; nvalues = 0
        line = $0
        while (match(line, /`"[^"]*"`/)) {
            val = substr(line, RSTART + 2, RLENGTH - 4)
            line = substr(line, RSTART + RLENGTH)
            nvalues++; values[nvalues] = val
        }

        for (k = 1; k <= nkeys; k++) {
            key = keys[k]
            if (hasclosedset[key]) {
                for (v = 1; v <= nvalues; v++) {
                    checked++
                    if (!((key, values[v]) in accepted))
                        printf "%s:%d: `%s` row quotes \"%s\", which --accepted --json does not accept for `%s`\n", file, FNR, key, values[v], key
                }
            } else if (isnullkey[key]) {
                skipped[key] = 1
            } else if (nvalues > 0) {
                printf "%s:%d: `%s` row quotes a literal, but --accepted --json does not report `%s` at all\n", file, FNR, key, key
            }
        }
    }
}

END {
    for (k in skipped) print "SKIPPED\t" k
    print "CHECKED\t" (checked + 0)
}
' "/tmp/check-doc-tokens-registry.$$" "${FILES[@]}" \
    > "/tmp/check-doc-tokens.$$" 2>/dev/null

checked_count=$(grep '^CHECKED' "/tmp/check-doc-tokens.$$" | cut -f2)
skipped_keys=$(grep '^SKIPPED' "/tmp/check-doc-tokens.$$" | cut -f2 | sort -u | paste -sd, -)
grep -v '^CHECKED\|^SKIPPED' "/tmp/check-doc-tokens.$$" > "/tmp/check-doc-tokens-findings.$$"

status=0
if [[ -s "/tmp/check-doc-tokens-findings.$$" ]]; then
    echo "check-doc-tokens: the documentation quotes a value the registry does not accept:" >&2
    sed 's/^/  /' "/tmp/check-doc-tokens-findings.$$" >&2
    echo >&2
    echo "The registry (--accepted --json) is the fact; the docs are the finding. Docs may omit a" >&2
    echo "token the registry accepts (that is allowed brevity), but must never quote one it does not." >&2
    status=1
elif [[ -z "$checked_count" || "$checked_count" -eq 0 ]]; then
    # A marked table that yields zero checks is the same failure mode as no marker at all: a green
    # exit that compared nothing. See check-all.sh:4-10 on why that must never happen silently.
    echo "check-doc-tokens: no pane-token-table marker was found, or the marked table had no" >&2
    echo "checkable tokens — refusing to report clean without having compared anything." >&2
    status=1
else
    echo "check-doc-tokens: ${checked_count} quoted token(s) checked against the registry, 0 disagree" \
        "(keys skipped for having no closed set: ${skipped_keys:-none})"
fi

rm -f "/tmp/check-doc-tokens.$$" "/tmp/check-doc-tokens-findings.$$" "/tmp/check-doc-tokens-registry.$$"
exit $status
