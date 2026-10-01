# #84 re-sync report (SchemaCommand.cs vs. merged Config.cs / #85)

Status: done, ready for cdtui-worker independent verification.

## What I checked against the real, merged `Config.cs`

Read `Config.cs:190-330` directly (not assumed): `PaneItemJsonConfig`'s 15
`[JsonPropertyName]`-carrying properties are `item, format, color, overflow,
id, command, shell, ttlSeconds, timeoutMs, link, from, extract, case,
maxLines, parts`. `PaneItemPartJsonConfig` (new type, #85) carries 9:
`text, item, from, extract, case, format, color, parts, link`.

## Peer's five steps

1. **`ModelItemKeys` includes `"parts"`.** Already true — `SchemaCommand.cs:64`
   lists all 15 keys above, matching `PaneItemJsonConfig` exactly (verified,
   not assumed; matches
   `ModelItemKeys_MatchesPaneItemJsonConfigsRealJsonPropertyNamesExactly`).
   No edit needed here; this had already been done in an earlier round.

2. **`structures` table's `compoundPart` entry vs. real `PaneItemPartJsonConfig`
   shape — edited.** The entry still had `record: null`, empty
   required/optional/fields, and a description saying "Not yet implemented
   … until task #85 lands" (SPEC-84 §5.3's F9b reasoning, which no longer
   applies now that the type exists). Changed it to `record:
   "PaneItemPartJsonConfig"`, `optional` = the real 9 wire keys in
   declaration order, one `Field(...)` per key, and an updated description.
   `parts` and `link` are listed as real (forbidden-by-diagnostic, not
   unknown-key) wire keys per the code comment on
   `PaneItemPartJsonConfig.Parts`/`.Link` and SPEC-84 §5.3's "wire keys only"
   rule — notes explain both are declared solely so a violation surfaces as
   `part-forbidden-key` rather than `unknown-key`.
   File: `src/ClaudeTuiLine/SchemaCommand.cs:313-327` (old) →
   `src/ClaudeTuiLine/SchemaCommand.cs:313-333` (new).

   Companion edit, same reasoning, in #84's own test file: `compoundPart` was
   in `RecordCheckExemptEntries` "because record is null (task #85 hasn't
   landed the type yet)" — that condition is gone, so I removed it from the
   exempt set (kept `colorExpr`, still a converter-driven union with no
   attributed properties). This makes V4's reflection check actually validate
   `compoundPart` against `PaneItemPartJsonConfig` going forward, same as
   every other entry — SPEC-84 §10 anticipated exactly this: "If #85 lands
   mid-implementation, `ModelItemKeys`, V3, and `compoundPart`'s entry will
   need updating together — that is V3/V4 doing their job, not a conflict."
   File: `tests/ClaudeTuiLine.Tests/SchemaCommandTests.cs:9-15`.

3. **`kindSupport.compound.supported` now `true`.** Confirmed via test run,
   not assumed — `KindSupport_CompoundReflectsWhetherItsKeysAreCurrentlyModeled`
   (V6, asserts the *computed* relationship, not a hardcoded bool) passed.
   Since `ModelItemKeys` already included `parts` (step 1) and #85's
   `ItemsCommand.Build().Kinds.Compound` already advertises its real
   required/optional set, `ComputeKindSupport` self-corrected with no
   `SchemaCommand.cs` logic change needed — exactly D4's designed property.

4. **Targeted tests re-run and pass:**
   `ModelItemKeys_MatchesPaneItemJsonConfigsRealJsonPropertyNamesExactly` and
   `BuildStructures_EveryNonExemptEntrysRecordTypeMatchesItsRequiredAndOptionalKeysExactly`
   both pass against the merged shape (part of the full-suite run below —
   the latter now genuinely exercises `compoundPart` since it's no longer
   exempt).

5. **Full suite + check-all.sh:**
   - `dotnet test tests/ClaudeTuiLine.Tests` — **1444/1444 passed**, exit 0.
   - `dotnet test tests/ClaudeTuiLineMcp.Tests` — **22/22 passed**, exit 0.
   - `tools/check-all.sh` — **fails**, but on the same two pre-existing
     categories as before, neither caused by this change:
     - `check-citations.sh`: ~13 dangling `§N.N` citations across SPEC files
       and `STATUS.md` — the same doc-registration-scope gap noted in the
       prior (#85) report: citations into genuinely-existing headings in
       specs the checker's fixed file list hasn't been updated to scan.
     - `check-doc-tokens.sh`: `README.md:162` — `border` quoted as an
       accepted token but not reported by `--accepted --json`. Pre-existing,
       unrelated to `parts`/`compound`/schema-explorer output.
     Neither failure touches `--schema --json`'s own output, `kindSupport`,
     or `structures`. Not fixed — `check-citations.sh`/`check-doc-tokens.sh`
     internals are out of #84's file set.

## Scope respected

Only edited `src/ClaudeTuiLine/SchemaCommand.cs` and
`tests/ClaudeTuiLine.Tests/SchemaCommandTests.cs`, both in #84's own file
set. Touched nothing else — no #85 files (merged/closed), no
`check-citations.sh`/`check-doc-tokens.sh`.

## Bottom line

Re-sync complete. `--schema --json` now correctly reports `compound` as a
real, supported kind, and `compoundPart` is a real, reflection-checked
structure entry instead of a placeholder. 1444+22 tests green. Ready for
`cdtui-worker`.
