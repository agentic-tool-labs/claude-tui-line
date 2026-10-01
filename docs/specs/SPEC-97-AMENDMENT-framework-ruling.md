# SPEC-97 AMENDMENT — Ultra-Advisor ruling on the section 3 framework amendment

STATUS: **RULED. SPEC-97 §3 (A1–A4) APPROVED WITH CORRECTIONS — two additional
amendment sites (A5, A6) are REQUIRED, plus one wording fix to A1 (C1) and one
non-blocking gap to close in A2 (C2). No further round with the Architect is
needed: the corrections are fully specified below and same-shape with A1.**

Ruled by Ultra-Advisor per SPEC-97 §11's escalation. Written against `main` @
`1f938b5`, SPEC-96 and SPEC-97 both unmerged. File follows the
`SPEC-88-AMENDMENT-*` convention for escalation rulings.

> Citations anchored by commit, quoted by content. Do not target edits by line
> number alone — match the quoted text.

---

## 1. The three questions, answered

### 1.1 Is A1 correctly scoped — premise replaced, conclusion preserved?

**Yes.** The conclusion "distribute/gutter stay inert on a horizontal split"
follows from the NEW premise as validly as from the old one. `distribute`
divides a shared extent; under §2.3.5 there is still no shared extent — each
stacked child is sized independently against the same ceiling, and nothing one
child takes is denied a sibling. `gutter` is cells between side-by-side
siblings on a column boundary; stacked siblings still share no column boundary.
The load-bearing property was never "children span the full width" — it was
"width is not divided among them," and both the old and new premises entail it.

Checked against the full text of §2.3.2, not just the quoted sentence: the
`key-not-applicable` ruling, its message wording ("it divides extent among
side-by-side children"), and both census bullets remain true verbatim under
§2.3.5. Nothing else in §2.3.2 leans on full-width spanning.

### 1.2 Does §2.3.4's floor justification survive — is A3 prose repair or a weakening?

**Prose repair, and in fact a sharpening.** Two separate observations, both
verified against the framework text:

- The floor derivation itself (§2.3.4: *"A `flex` pane's width floor is the
  lesser of its two orientations' floors, because it can render in either"*)
  **never uses the "can each take all of it" phrase at all.** The phrase
  appears only in the AND-semantics structural-check paragraph.
- Where it does appear, the property the math actually uses is
  `StackedFloor = max Floor(child) ≤ W` — i.e. *each child's need fits within
  the width*. "Can each take all of it" was a sufficient condition stated
  loosely; "can each fit within it" is the exact condition the floor
  comparison tests. A3 replaces an over-strong informal claim with the precise
  one. Nothing the invariant's proof relies on is weakened.

Concretely, at `W = stackedFloor` under §2.3.5: a fixed child F has
`Floor = F ≤ maxFloor`, so it is granted F un-clamped; a `minSize` child
likewise; a content child measures capped at the ceiling; percent and fill are
ceiling-bounded by construction. Every grant ≤ ceiling, so the stacked
arrangement survives at the advertised floor — which is all the advertisement
ever promised. Children taking *less* than full width cannot break a claim of
the form "each need fits."

Also checked the rest of §2.3.4 for hidden reliance on full-width stacking:
"stacking can therefore cost rows" (still true — a narrower sized child wraps
more, same direction; NE-3 already covers it), "the size resolver cannot
distinguish a stacked flex pane from a declared horizontal one" (preserved —
§4.3's call site keys on `effectiveSplit == Horizontal`), and the
gutter/distribute sentence (preserved by A1's conclusion). Nothing else is
disturbed.

### 1.3 Does "the floor survives by construction" hold against source?

**Yes — verified in `SizeResolver.cs`, not taken from the spec.**

- `Floor(p, ...)` opens `if (p.MinSize is int min) { return min; }` and its
  leaf switch has `SizeKind.Fixed => spec.FixedValue` — exactly as SPEC-97 §5
  claims.
- `StackedFloor(p, collapse) => p.Children.Max(c => Floor(c, ...))` — so a
  stacked child's declared `size`/`minSize` already reaches
  `ResolveFlexOrientation`'s stacked test today.
- `ResolveFlexOrientation` reads only `SideBySideNeed` and `StackedFloor`.
  SPEC-97's diff adds `StackedWidth` and calls it only from `ResolveNode`'s
  stacked grant path; no function in the floor/orientation chain calls it or
  is edited. The `sideBySideFloor ≥ stackedFloor` invariant is therefore
  untouched whatever its proof status — B changes grants, never needs.

One nuance, no action needed: `Floor` of a content-sized stacked leaf is `0`
(the SPEC-88-AMENDMENT degeneracy). Harmless on the stacked side — a stacked
child imposes no width on siblings — and B does not change it.

---

## 2. REQUIRED corrections — the amendment set is incomplete

A grep for the old premise across `SPEC-V2-FRAMEWORK.md` finds it stated
normatively in **two places A1–A4 do not touch**. Leaving them lands the exact
failure SPEC-92 names — a framework internally inconsistent, with §2.3.2/§2.3.5
saying "sized independently" while two other sections still say "each span the
full width." `check-citations.sh` cannot catch this; both sentences resolve
fine. This section's history (SPEC-88 Rev 2, Rev 4) is a history of amendments
that were right where they looked and wrong about where else to look.

### A5 — the split-definition bullet (§2.2 region)

Replace, in the `horizontal` definition bullet:

> - **`horizontal`** splits top to bottom — children divide the parent's
>   **height**, each spanning the parent's full width. This is nearly free,
>   because rows are already the output unit.

with:

> - **`horizontal`** splits top to bottom — children divide the parent's
>   **height**, each sized independently within the parent's width (§2.3.5).
>   This is nearly free, because rows are already the output unit.

### A6 — the split-floor rationale (the "divides sums, shares maxes" paragraph)

Replace, in the paragraph beginning "A split's floor follows its orientation":

> a horizontal split's children each span the full width, so its width floor is
> the largest of theirs.

with:

> a horizontal split's children are each sized independently against the full
> width (§2.3.5) and impose nothing on one another, so its width floor is the
> largest of theirs.

**The conclusion — floor is the max, not the sum — is unchanged and correct
after B**, for the same reason as §1.2 above: needs are independent, so the
binding need is the largest one. Only the premise wording moves, same shape as
A1. The paragraph's warning about over-stating a nested horizontal split's
floor stands unchanged.

### C1 — A1's replacement text must not say "the full width"

A1's proposed text reads "sized independently against the full width (§2.3.5)"
while §2.3.5 defines the ceiling as the *inner* width, `outer − reserve(p)`.
"Full width" is precisely the looseness that concealed SPEC-96's bug for the
code's whole life; do not re-write it into the repaired sentence. Amend A1's
replacement to:

> its children stack downward and are each sized independently within the
> split's inner width (§2.3.5)

(The second sentence of A1's replacement — independent sizing is not division —
is approved verbatim; "the whole width" inside it may stay, since it is
describing the no-contention property, not defining the ceiling.)

### C2 — non-blocking gap in A2: grants below `RowLayout.MinUsableWidth`

A2 rules what happens when a declared size exceeds the ceiling, but is silent
on the other end: a `percent` or `maxSize` child can resolve *below*
`RowLayout.MinUsableWidth` (e.g. `size:"10%"` at a narrow ceiling), a quantity
the framework elsewhere treats as the usability floor of a fill leaf. There is
no drop and there should be none (dropping frees nothing); ruled here so A2 is
not silent: **the grant stands as declared and the renderer's existing
narrow-width degradation governs, with no note** — a note reports the
framework overriding the author, and here the author got what they asked for.
Add one sentence to A2 saying so. Non-blocking; if ct-arch prefers a different
disposition for the note, that is fine — what is not fine is A2 saying nothing.

---

## 3. Ruling summary

- **A1**: approved with C1's wording fix. Premise/conclusion split is sound.
- **A2**: approved; add C2's one sentence.
- **A3**: approved verbatim — prose repair confirmed; the floor invariant's
  justification is sharpened, not weakened.
- **A4**: approved (note-wording reuse; nothing to adjudicate).
- **A5, A6**: required additions — same-shape premise repairs at the two
  normative sites the grep found and A1–A4 missed.
- **"Floor survives by construction"**: verified against
  `SizeResolver.cs` source. True.

**Clean to implement** with A5/A6/C1/C2 folded in — all four are fully
specified above and none reopens a design question, so no further Architect
round is required. SPEC-96 still merges first, per SPEC-97 §0.

**Confidence: high.** Strongest rejected alternative: reject A1 on the grounds
that "the premise changed so the conclusion needs re-derivation" (this
section's historical failure mode). Rejected because the conclusion was
re-derived here from the new premise directly (§1.1) rather than assumed to
carry over — the two prior SPEC-88 failures were claims *not* re-checked
against source; every claim in this ruling was.

**What would overturn it:** a normative use of the full-width premise outside
the sites swept (grep covered "full width", "whole width", "span the full",
"each span" over the framework; remaining hits are the vertical-split
definition and test-sweep phrasing, both inert here), or evidence that some
consumer reads a stacked sibling's width to position another sibling — none
exists in the resolver or renderer paths cited by SPEC-96/97.

**Out of scope, noted without redirecting the ruling:** `BorderGrid` branches
on `EffectiveSplit == Vertical` with stacked as fallthrough (SPEC-96 §4.2);
variable-width bordered stacked children are a new visual case for it. SPEC-97
S8/S13 partially cover this at render level — worth the Implementor eyeballing
a bordered narrow stacked child's junctions once, but it is code-design
territory ct-arch declared high-confidence and it does not bear on A1's scope.
