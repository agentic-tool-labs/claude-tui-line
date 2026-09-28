#!/usr/bin/env bash
# A sentence that announces how many items follow must agree with the list that follows it.
#
# Found twice by hand: §8's segment count read as a superset of what it listed, and §12.2
# said "Three rules, none optional" above four rules. The second is the instructive one —
# the extra rule was the one whose failure mode is explicitly silent, and a reader
# reconciling the two documents would have concluded it was the editing error and dropped
# it. Both survived many readings, for the same reason dangling citations do: the sentence
# reads correctly either way, and nobody counts.
#
# Scope, deliberately narrow. A line qualifies only when it
#   - ends in a colon, outside a fenced code block,
#   - names a count as a word or digit, and
#   - is followed by an actual list.
# The count taken is the LAST one before the colon, because that is the one qualifying the
# noun the list enumerates. Lines with no list under them are skipped rather than guessed
# at, which is what keeps arithmetic prose ("four values of T, two panes:") out of the
# report. Hedged counts ("at least three:") are excluded — a lower bound is not a count.
#
# The bar this has to clear is not "finds things" but "is worth reading every time". A
# check that cries wolf gets ignored, and an ignored check is worse than none: it occupies
# the slot a real one would have. If this starts reporting prose, tighten it or delete it.
#
# Exit 0 clean, 1 with mismatches listed, 2 if nothing could be scanned.

set -uo pipefail

cd "$(dirname "$0")/.." || exit 2

FILES=("$@")
if [[ ${#FILES[@]} -eq 0 ]]; then
    # shellcheck disable=SC2207
    FILES=($(git ls-files '*.md' 2>/dev/null))
fi

if [[ ${#FILES[@]} -eq 0 ]]; then
    echo "check-counts: no markdown files to scan — refusing to report clean." >&2
    exit 2
fi

awk '
function num(w) {
    if (w ~ /^[0-9]+$/) return w + 0
    if (w == "two")   return 2
    if (w == "three") return 3
    if (w == "four")  return 4
    if (w == "five")  return 5
    if (w == "six")   return 6
    if (w == "seven") return 7
    if (w == "eight") return 8
    if (w == "nine")  return 9
    if (w == "ten")   return 10
    return 0
}
# The marker class of a list line, or "" if it is not one. Ordered and unordered are
# different classes so a bulleted aside nested under a numbered list cannot inflate a count.
function marker(s,   t) {
    t = s
    sub(/^[ \t]+/, "", t)
    if (t ~ /^[0-9]+\.[ \t]/) return "ol"
    if (t ~ /^[-*+][ \t]/)    return "ul"
    return ""
}
function indent(s,   t) {
    t = s
    if (match(t, /^[ \t]*/)) return RLENGTH
    return 0
}
function flush(   ) {
    if (pending && found > 0 && found != want)
        printf "%s:%d: says %d, lists %d — %s\n", file, lead_line, want, found, lead_text
    pending = 0
    para = ""       # the consumed list is not part of whatever paragraph comes next
}
BEGIN { fence = 0; pending = 0; para = "" }
FNR == 1 { flush(); fence = 0; para = ""; file = FILENAME }
{
    if ($0 ~ /^[ \t]*(```|~~~)/) { fence = !fence; if (pending && found > 0) flush(); next }
    if (fence) next

    m = marker($0)

    if (pending) {
        if ($0 ~ /^[ \t]*$/) {
            blanks++
            if (blanks >= 2 && found > 0) flush()
        } else if (m != "" && indent($0) == item_indent && m == item_marker) {
            found++; blanks = 0
        } else if (found == 0 && m != "") {
            item_indent = indent($0); item_marker = m; found = 1; blanks = 0
        } else if (found > 0 && indent($0) > item_indent) {
            blanks = 0                      # continuation of the current item
        } else {
            flush()
        }
    }

    if (pending) next

    # The paragraph so far. A count and the colon introducing its list are one *sentence* and
    # only accidentally one line, so a line-only lead-in test is armed or disarmed by where the
    # text happens to wrap — "There are two candidate rules, and a key is suggested when it
    # satisfies either:" was unguarded purely because "two" landed on the line above the colon.
    # A check that a reflow can silently switch off is the decay class this file exists to catch.
    if ($0 ~ /^[ \t]*$/ || $0 ~ /^[ \t]*(#|>)/) { para = ""; next }
    if (m != "") para = ""                  # a list item begins its own paragraph
    para = (para == "" ? $0 : para " " $0)

    # A lead-in: ends in a colon, names a count, and is not hedged into a bound.
    if ($0 ~ /:[ \t]*$/) {
        line = tolower(para)

        # Everything below removes numbers that are names rather than counts. Without this
        # the check reports "SHA-256 of each captured artifact:" as promising 256 items,
        # which is the noise that would get it ignored.
        gsub(/`[^`]*`/, " ", line)          # inline code
        gsub(/§[0-9]+(\.[0-9]+)*/, " ", line)   # section references
        gsub(/[0-9]+(\.[0-9]+)+/, " ", line)    # dotted numbers generally
        gsub(/[a-z]-[0-9]+/, " ", line)         # sha-256, utf-8
        gsub(/[0-9]+[-–—][0-9]+/, " ", line)    # ranges: "defects 3-6"
        sub(/^[ \t]*([0-9]+\.|[-*+])[ \t]+/, "", line)  # the lead-in is itself a list item

        # Only the final sentence of the paragraph introduces the list; earlier sentences are
        # prose that happens to share the block. This runs after the substitutions above so a
        # section reference is already gone and cannot split a sentence at its dots.
        sub(/^.*[.!?][ \t]+/, "", line)

        if (line ~ /(at least|at most|up to|more than|fewer than|no more than|roughly|about|around|some)/) next

        # The LAST count qualifying a plural noun is the one the list answers to, because it is
        # the one nearest the colon. Taking the first was survivable while a lead-in was a single
        # line and is not now: "it applies at two levels ... so there are three cases:" states
        # both, and only the second is a promise about the list. Requiring a following word also
        # drops "…Segment 13." — a numeral ending a clause counts nothing.
        # A numeral followed by a function word is not counting what the list enumerates — it is
        # referring to some other set, as in a lead-in reading "Four of the ten in §12.6 are
        # conditions …:", where the promise is four and the ten belongs to a list in another
        # section. Requiring a content word after the numeral separates the two, and when nothing
        # qualifies the lead-in is skipped rather than guessed at.
        want = 0
        rest = line
        while (match(rest, /(^|[ \t(])(two|three|four|five|six|seven|eight|nine|ten|[0-9]+)[ \t]+[a-z]+/)) {
            seg = substr(rest, RSTART, RLENGTH)
            rest = substr(rest, RSTART + RLENGTH)
            sub(/^[ \t(]/, "", seg)
            tok = seg; sub(/[ \t]+[a-z]+$/, "", tok)
            nxt = seg; sub(/^[^ \t]+[ \t]+/, "", nxt)
            if (num(tok) > 0 && nxt !~ /^(are|is|was|were|be|of|in|on|to|and|or|for|that|which|the|a|an|but|as|by|with|from|it|they|have|has|had)$/)
                want = num(tok)
        }
        if (want > 0) {
            pending = 1; found = 0; blanks = 0; item_marker = ""; item_indent = 0
            lead_line = FNR
            # Echo the sentence the count came from, not the line the colon landed on. Those are
            # the same thing only when the lead-in did not wrap, and a report that says "says 3"
            # above a quoted line containing no 3 reads as a broken check.
            lead_text = para
            gsub(/§[0-9]+(\.[0-9]+)*/, "§", lead_text)  # so a section number cannot end a sentence
            sub(/^.*[.!?][ \t]+/, "", lead_text)
            sub(/^[ \t]+/, "", lead_text)
            if (length(lead_text) > 78) lead_text = substr(lead_text, 1, 75) "..."
        }
    }
}
END { flush() }
' "${FILES[@]}" > /tmp/check-counts.$$ 2>/dev/null

status=0
if [[ -s /tmp/check-counts.$$ ]]; then
    echo "check-counts: a stated count disagrees with the list beneath it:" >&2
    sed 's/^/  /' /tmp/check-counts.$$ >&2
    echo >&2
    echo "Fix the number, or the list. Which one is wrong is a judgement — but they cannot" >&2
    echo "both stand, and the reader who notices will not know which to trust." >&2
    status=1
else
    echo "check-counts: every stated count matches its list (${#FILES[@]} files)"
fi

rm -f /tmp/check-counts.$$
exit $status
