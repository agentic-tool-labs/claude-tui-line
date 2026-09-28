#!/usr/bin/env bash
# Every render note the code can emit must appear in §9.8.1's pinned list.
#
# §9.3.4 ruled that a note is an interface the moment a prompt tells anyone to read it, and that
# every note therefore has its text pinned in §9.8.1 rather than living only at the call site.
# That ruling was written and then not discharged: §9.8.1 had no list at all for several commits,
# and the only reason anyone noticed is that the implementor went and read HEAD instead of taking
# the claim on trust. "Adding a producer means adding a line here" is an instruction to a future
# editor, and every instruction of that shape in this project has decayed at least once — the
# editor adding the producer has no reason to look upstream, and the list has no way to notice.
#
# So it is checked. One direction only: every emitted note must be listed. The reverse — a listed
# note with no producer — is deliberately not an error, because §4.0.1's maxLines note is pinned
# and cannot fire until maxLines exists, and encoding "not built yet" into the list would make the
# block something other than the exact strings it is supposed to be.
#
# Known blind spot, stated rather than papered over: only single-line literals passed directly to
# Add() are seen. A note assembled into a variable first is invisible here. That is a real gap and
# the reason the spec sentence stays next to the check rather than being replaced by it.
#
# Exit 0 clean, 1 with unpinned notes listed, 2 if either side could not be read.

set -uo pipefail

cd "$(dirname "$0")/.." || exit 2

SPEC="SPEC-V2-FRAMEWORK.md"
MARKER="pinned-notes"

[[ -f "$SPEC" ]] || { echo "check-notes: no such file: $SPEC" >&2; exit 2; }

# Placeholders differ between the two sides by design — the spec names them for a reader
# ({columns}) and the code interpolates an expression ({splitOuterWidth}) — so both collapse to
# {} before comparison. Comparing the names would be comparing two things nobody promised match.
normalize() { sed -E 's/\{[^{}]*\}/{}/g'; }

# The fenced block after the marker line. The marker must be the whole line: §9.8.1 discusses the
# check in prose, and a substring test would match the sentence describing it — which is how rule
# C in check-examples.sh reported the spec as omitting every item it documents.
pinned=$(awk -v marker="$MARKER" '
    BEGIN { markerline = "^[ \t]*<!--.*" marker ".*-->[ \t]*$"; armed = 0; inblock = 0 }
    !inblock && !armed && $0 ~ markerline { armed = 1; next }
    armed && $0 ~ /^[ \t]*```/ { armed = 0; inblock = 1; next }
    inblock && $0 ~ /^[ \t]*```/ { inblock = 0; exit }
    inblock && $0 !~ /^[ \t]*$/ { print }
' "$SPEC" | normalize | sort -u)

if [[ -z "$pinned" ]]; then
    echo "check-notes: found no pinned list under the '$MARKER' marker in $SPEC." >&2
    echo "The extraction is broken, or the marker was removed. Refusing to report clean." >&2
    exit 2
fi

# shellcheck disable=SC2207
SOURCES=($(git ls-files 'src/**/*.cs' 'src/*.cs' 2>/dev/null))
if [[ ${#SOURCES[@]} -eq 0 ]]; then
    echo "check-notes: no C# sources found — refusing to report clean." >&2
    exit 2
fi

# file:line then a tab then the text, so a finding can say where to go. The receiver must be a
# notes collector: a bare `.Add("…")` matches every argv builder in the tree, and a check that
# reports `--show-current` as an unpinned render note is one nobody reads twice.
#
# awk rather than sed for the splitting, and not by preference: BSD sed does not read \t as a tab
# in a bracket expression or a replacement, so the obvious `sed 's/…/\t/; s/^[^\t]*\t//'` silently
# splits on a literal backslash-t that never appears, and every match falls through unstripped.
occurrences=$(awk '
    match($0, /[A-Za-z_]*[Nn]otes\.Add\(\$?"[^"]*"/) {
        m = substr($0, RSTART, RLENGTH)
        sub(/^[^"]*"/, "", m)
        sub(/"$/, "", m)
        if (m != "") printf "%s:%d\t%s\n", FILENAME, FNR, m
    }
' "${SOURCES[@]}")

emitted=$(printf '%s\n' "$occurrences" | cut -f2- | normalize | sort -u)

# An empty emitted set is almost certainly the extraction failing rather than the collector having
# no callers — the spec documents two live producers — so it is loud rather than green.
if [[ -z "$emitted" ]]; then
    echo "check-notes: found no Add(\"…\") call sites at all — the extraction is broken," >&2
    echo "not the code. Refusing to report every pinned note as unused." >&2
    exit 2
fi

unpinned=$(comm -23 <(echo "$emitted") <(echo "$pinned"))

if [[ -z "$unpinned" ]]; then
    count=$(echo "$emitted" | wc -l | tr -d ' ')
    echo "check-notes: every emitted note text is pinned in §9.8.1 ($count distinct)"
    exit 0
fi

echo "check-notes: emitted but not pinned in §9.8.1:" >&2
while read -r note; do
    [[ -z "$note" ]] && continue
    echo "  $note" >&2
    printf '%s\n' "$occurrences" | while IFS=$'\t' read -r where text; do
        [[ "$(printf '%s' "$text" | normalize)" == "$note" ]] && echo "      $where" >&2
    done
done <<< "$unpinned"
echo >&2
echo "Add the text to §9.8.1's pinned block, or change the call site to use one that is" >&2
echo "already there. A note nothing documents is a string prompts will quote and then drift" >&2
echo "from, with nothing failing." >&2
exit 1
