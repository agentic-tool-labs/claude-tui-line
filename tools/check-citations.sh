#!/usr/bin/env bash
# Every §N.M cited in the documentation must resolve to a heading in the spec.
#
# §13.3 found four that did not, including §7 cited 27 times, and all four had
# survived many careful readings — prose citing a missing section reads
# correctly, because the sentence carries the meaning and the number is
# decoration until someone tries to follow it. So this is checked mechanically
# or it is not checked.
#
# It read the spec and nothing else for as long as it existed, and reported that
# as "all N cited sections resolve" — which is what the project's citations
# resolving would sound like, and is not what it meant. The files it was not
# reading are the ones where a bad number costs the most: commands/*.md are
# followed by an LLM at runtime, so §9.8.1 written where §9.8 was meant sends it
# to read the wrong rule during somebody's real migration. Headings still come
# from the spec alone, because the spec is the only place a section is defined;
# what widened is who may cite one.
#
# SPEC.md is excluded, and deliberately: it is v1, superseded, and its numbers
# belong to a scheme this document no longer uses. Checking it would report
# dozens of dangling references that are all correct in their own document, and
# a permanently red check is a check nobody runs.
#
# Exit 0 clean, 1 with dangling references listed, 2 if the spec is unreadable.
#
# -E everywhere on purpose: `sed 's/x\+//'` is a GNU extension that BSD sed
# accepts and silently does not apply, so the first version of this script
# stripped nothing, matched nothing, and reported all 69 cited sections as
# dangling. A check whose failure mode is "everything is broken" is at least
# loud; the same bug in the other direction reports clean forever.

set -uo pipefail

SPEC="${1:-$(dirname "$0")/../SPEC-V2-FRAMEWORK.md}"

if [[ ! -f "$SPEC" ]]; then
    echo "check-citations: no such file: $SPEC" >&2
    exit 2
fi

# Headings define; every other occurrence of §N.M cites. The two sets cannot be
# confused here because headings are written "### 9.4.1" with no § — if that
# ever changes, exclude heading lines from the citation side, or every section
# becomes self-defining and this check goes vacuously green.
# Backticked references are *mentioned*, not cited. §13.3 discusses broken
# references by number and would otherwise trip this check on the very numbers
# it exists to have found — a checker that cannot describe its own findings.
# The document already draws this line: real citations are bare (§9.4), and
# §13.3's table of dangling numbers is written `§10.6`. The cost is a false
# negative if someone backticks a genuine citation; that is the safe direction,
# since the alternative is a permanently red check that gets ignored.
CITERS=("${@:2}")
if [[ ${#CITERS[@]} -eq 0 ]]; then
    # shellcheck disable=SC2207
    CITERS=($(cd "$(dirname "$SPEC")" && git ls-files '*.md' 2>/dev/null | grep -v '^SPEC\.md$'))
    if [[ ${#CITERS[@]} -eq 0 ]]; then
        echo "check-citations: no markdown files to scan — refusing to report clean." >&2
        exit 2
    fi
    cd "$(dirname "$SPEC")" || exit 2
    SPEC="$(basename "$SPEC")"
fi

# One pass produces file:line:ref, so a finding can name where to go. The
# citation set for the resolve step is derived from it rather than gathered
# separately — two extractions of the same thing is how they come to disagree.
occurrences=$(for f in "${CITERS[@]}"; do
    sed -E 's/`[^`]*`//g' "$f" \
      | grep -noE '§[0-9]+(\.[0-9]+)*' \
      | grep -vE ':§[0-9]+$' \
      | sed -E "s|^([0-9]+):§|$f:\1:|"
done)

cited=$(printf '%s\n' "$occurrences" | sed -E 's/^.*:([0-9]+(\.[0-9]+)*)$/\1/' | sort -u)

defined=$(grep -oE '^#+ [0-9]+(\.[0-9]+)*' "$SPEC" \
          | sed -E 's/^#+ //' | sort -u)

if [[ -z "$defined" ]]; then
    echo "check-citations: found no numbered headings at all — the extraction is broken," >&2
    echo "not the document. Refusing to report every citation as dangling." >&2
    exit 2
fi

dangling=$(comm -23 <(echo "$cited") <(echo "$defined"))

if [[ -z "$dangling" ]]; then
    echo "check-citations: all $(echo "$cited" | wc -l | tr -d ' ') cited sections resolve" \
         "(${#CITERS[@]} files)"
    exit 0
fi

echo "check-citations: cited but never defined as a heading:" >&2
while read -r ref; do
    [[ -z "$ref" ]] && continue
    # The dots have to be escaped, and this is not pedantry — unescaped they are ERE
    # wildcards, so `§9.6.2.3` matched the occurrence line `…md:906:2.3`, character for
    # character, and the report named two innocent lines about §2.3 as citing a section
    # they have never heard of. The resolve step above is exact-string (comm over sorted
    # sets), so only the *reporting* was ever wrong — which is the worse half to get wrong:
    # a correct finding pointing at the wrong lines sends the reader to edit working prose,
    # and the first thing they conclude is that the check is broken.
    ref_re=${ref//./\\.}
    where=$(printf '%s\n' "$occurrences" | grep -E ":${ref_re}$" | sed -E 's/:[0-9.]+$//')
    echo "  §${ref} — cited $(printf '%s\n' "$where" | wc -l | tr -d ' ')×:" >&2
    printf '%s\n' "$where" | sed 's/^/      /' >&2
done <<< "$dangling"
echo >&2
echo "Fix by giving the content a heading (§13.3's default), or — when that number" >&2
echo "is already taken on a different axis — by rewriting the citations." >&2
exit 1
