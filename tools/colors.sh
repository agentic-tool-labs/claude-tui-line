#!/usr/bin/env bash
# Print every colour name claude-tui-line accepts, rendered in that colour.
#
# Interim stand-in for `claude-tui-line --colors` (SPEC-V2-FRAMEWORK.md §9), which will read the
# palette from the binary rather than from this list. Until that ships, the names here were
# verified by rendering each one through the real binary and reading the SGR it emitted.
#
# The palette is theme-mapped: the terminal decides what "blue" looks like. That is the whole
# reason to print it here rather than put a swatch in the README -- a swatch shows the author's
# terminal, this shows yours.

set -euo pipefail

printf '\n  claude-tui-line colour names\n\n'
# printf pads by bytes, and a box-drawing character is 3 of them -- a U+2500 rule here would
# sit 12 columns left of where it looks like it should. ASCII dashes measure honestly.
printf '  %-22s   %-22s\n' 'normal' 'bright'
printf '  %-22s   %-22s\n' '------' '------'

names_normal=(black maroon green olive navy purple teal silver)
codes_normal=(30 31 32 33 34 35 36 37)
names_bright=(grey red lime yellow blue fuchsia aqua white)
codes_bright=(90 91 92 93 94 95 96 97)

for i in "${!names_normal[@]}"; do
    printf '  \033[%sm%-10s\033[0m %-11s   \033[%sm%-10s\033[0m %-11s\n' \
        "${codes_normal[$i]}" "${names_normal[$i]}" "SGR ${codes_normal[$i]}" \
        "${codes_bright[$i]}" "${names_bright[$i]}" "SGR ${codes_bright[$i]}"
done

printf '\n  attributes\n'
printf '  \033[0mdefault\033[0m      SGR 0     resets to the terminal default\n'
printf '  \033[2mdim\033[0m          SGR 2\n'
printf '  \033[1mbold\033[0m         SGR 1\n'

printf '\n  Each row is a pair -- navy/blue, maroon/red, green/lime, olive/yellow,\n'
printf '  purple/fuchsia, teal/aqua, silver/white -- normal and bright of one hue.\n'

printf '\n  Use any of them anywhere a colour is accepted:\n\n'
printf '    { "item": "model", "color": "yellow" }\n'
printf '    "border": { "enabled": true, "color": "grey" }\n'
printf '    "colors": { "my-accent": { "default": "fuchsia" } }\n\n'
