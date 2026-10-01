# claude-tui-line — status

Running progress against `SPEC-V2-FRAMEWORK.md`. Updated as work lands.

**Last updated:** 2026-08-15

A line is only **Done** here if it was verified independently of the report claiming it —
rebuilt from source with a matching SHA-256, or checked against rendered bytes. "Tests pass" is
not by itself enough; this project has twice had a green suite over a broken instrument.

---

## Done and verified

| Area | What | Evidence |
|---|---|---|
| Phase 1 | `chromeReserve` width fix — usable width is `COLUMNS - 3` | live |
| Phase 2 | Pane surface, root leaf pane, overflow `wrap` / `truncate` / `overflow` | acceptance §2.7 |
| Phase 3 | Splits: sizing (`fixed` / `content` / `percent` / `fill`), gutters, per-pane borders, `valign` | §2.9 eyeballed live |
| Phase 4 | Item registry, `command` providers, cache, TTL, timeouts | — |
| Colour | Named tokens (`@model-accent`), threshold rules, literals | §6 |
| Colour | Decorative vs semantic item colour — a configured colour replaces internal decorative colour, but never overrides a value-derived threshold | `verify_itemcolor.py`, innermost-SGR span read |
| Colour | Model name follows the model, no `"format": "{}"` workaround needed | build `1012fa45` |
| Cleanup | Banner glyph renderer removed | build `8ab7ffce`, 228 renders byte-identical across 4 configs × widths 20–240 |
| Sizing | Wrap-aware re-measurement — a narrower grant returns the longest wrapped row, not the grant | build `697e9629` |
| Cleanup | `capWidth` parameter removed from `LeafContent.Decide` | 1044/1044 tests |
| Sizing | **`distribute: "min-rows"`** (§2.3 / §2.3.1) — optimal pane sizing by searching the achievable row count `T`, not the allocation | Independent build+test: exit 0, 0 warnings, 1067/1067. Carries a brute-force optimality oracle at `COLUMNS=112` and `60`, so the green suite is a behavioural check on the allocator. p90 `0.073ms` across widths 100–240 vs a 12.6ms budget; 45 packer calls on the live config |

## In flight

- **Phase 5 CLI** (§9) — with the implementor, and now the **critical path**: §12.1 makes the
  binary the oracle for every authoring command, so Phase 6 below is blocked on it entirely.
  Ordered `--check` → `--items` → `--preview` → `--colors`, and the ordering is reasoned:
  `--check` first because §7 makes a bad config *silent*, so it is the only one that prevents
  damage rather than adding convenience. It reports by **JSON Pointer** and must **reuse
  `ReferenceExtractors`** rather than growing a second config walk — a checker that passes while
  the resolver drops an id is defect 11 re-opened somewhere new and equally silent. `--check`
  is also where the silent-acceptance defects 3–6 become visible without changing runtime
  behaviour. `--preview` needs `--config <path>` so a candidate can be seen **without being
  installed**, which is what lets migrate show a result before writing. **`--version` was added
  to this scope (§9.7)** after a cross-reference check found §12.6 surfacing a `cliVersion` that
  §9 had never defined — the implementor had no way to know the field was expected. It brings a
  test with it: the version lives in both the `.csproj` and the hand-written `plugin.json`, which
  cannot be generated, so an assertion that the two match is the only thing standing between that
  drift and a user reporting a version corresponding to nothing. **§9.8 settles what "fixed sizes
  cannot fit the parent" means**, raised by the implementor rather than found by me: `--check`
  consults no width at all, because §12.6's `validate` calls it from an MCP server with no
  terminal, and because degrading at a narrow width is what the §2 ladder is *for*. The invariant
  is a contradiction no width can resolve — fixed children against a bounded parent, `minSize`
  above `maxSize` — and where the parent is `fill`/`content` it says nothing. The width-dependent
  finding is real but belongs to `--preview`, as a note that never touches the exit code.
- **Phase 6 authoring commands** (§12.3–12.5) — `migrate`, `edit`, `revert` **written and
  committed, blocked on §9**. Each opens by asking the binary for `--items`, and **stops** if the
  CLI is absent rather than falling back to a list written from memory: §12.1 forbids that
  because a prompt-file item list is a second registry, and a remembered id that no longer exists
  resolves to nothing and is silently suppressed. The README says plainly that these three do not
  work yet. **Not verified end-to-end** — nothing can be until §9 exists. **Walked over cases on
  14 Aug and six defects fixed**, one of them severe: `/edit`'s checkpoint did not contain the file
  `/edit` modifies, so its rollback path was a no-op that reported success. See the session-log
  entry below; the prose is fixed, still unexercised.
- **The backup ledger** (§12.2) — `docs/backup-ledger.md`, one definition shared by all four
  commands. A first draft of these commands used timestamped `settings.json.backup-*` copies and
  had exactly the bug §12.2 exists to prevent: migrate → edit → migrate again captures
  *claude-tui-line's own* command as the thing to restore, so revert restores the tool the user is
  escaping, and the escape hatch closes as it becomes needed. Replaced with the `origin` /
  `checkpoint` distinction, `origin` written exactly once ever, SHA-256 recorded per artifact and
  a mismatch **reported rather than overwritten**. Also unexercised: no command has run against a
  real ledger yet. **Amended 14 Aug:** an entry now carries `claude-tui-line.json` as a third
  artifact alongside `settings.json` and the user's script, recorded whenever a config exists
  rather than when the running command intends to change it — without it `/edit` had no
  recoverable backup at all. The hash-check rule was also wrong in a way that would have made
  `revert` refuse on every run; both are in the session log.

- ~~**Defects 11 and 13**~~ — **both fixed, committed as `5ed9ccb` and pushed.** Verified
  independently of the report claiming it: clean Release build, three consecutive full-suite runs
  at **1069/1069**, and both fixes confirmed present in the tree (`[ThreadStatic]` at
  `SizeResolver.cs:332`, the non-parallel collection at `MinRowsDistributeTests.cs:12`) rather
  than merely reported. Isolated, the true packer count is **14** and stable across 8 runs — so
  the 45 this file recorded as a baseline was itself contaminated, just less than the 314 was.
  Defect 11's fix: `CollectIds` restructured into a
  `ReferenceExtractors` array with one lambda per §5 reference form, so adding §4.2's argv
  placeholder or §3.3's compound parts is an append rather than an edit — that array is now the
  single definition of what ids a config references, and **`--check` must reuse it rather than
  grow a second config walk**. Colour-token `from` was checked and is *not* affected, closing the
  question STATUS previously left open at defect 11: the link resolver was the only broken form.
  One consequence of defect 11 still wants a spec decision: a `link` naming `{remote-url}` now
  genuinely resolves `remote-url`, which shells out to git. That is the fix working as intended,
  but it makes an item that is opt-in *for cost reasons* reachable without the user placing it.
  §3.2 may need to say so.

Notes carried forward from `min-rows`, now in **Done and verified**: implementation is
`PaneDistribute` → `ResolveVerticalMinRows` → `SolveMinRows` → `MinWidthForRowCount` →
`WaterFillSurplus`, and `RowCountAt` calls the real packer rather than a re-derived twin — the
property that makes the search trustworthy. Its latency test measures the allocator in-process
rather than through `bench.sh`, deliberately: §2.3.1 condition 2 measures the allocator, §10
item 10 measures the binary, and routing through `bench.sh` would need a published artifact.
**`R = 108`, not the 109 the worked example claimed** — implemented against the formula, prose
flagged rather than silently reconciled; §2.3.1 is now corrected.

Recently landed, pending the last of its per-defect checks:

- **MCP schema explorer** (task #84) — new `get_config_schema` MCP tool, backed by a new public
  CLI subcommand `--schema --json` (spawned by MCP via the existing `CliRunner`/`CliLocator`
  pattern — MCP still has zero `ProjectReference` to the core `ClaudeTuiLine` project, per #83's
  allow-list). The envelope embeds `--items`/`--colors`/`--accepted --json` verbatim plus new
  `structures` (12 hand-authored records, one per config shape — `config`, `border`,
  `borderEdges`, `layout`, `surface`, `pane`, `item`, `colorRule`, `threshold`, `match`,
  `colorExpr`, `compoundPart`) and `kindSupport` (per-kind `supported: bool` +
  `unsupportedKeys: string[]`, computed by reflection diff against `PaneItemJsonConfig`'s real
  properties, so it self-corrected the moment #85 landed `Parts` — `compound` now reports
  `supported: true` with no code change to the diff logic itself). `--schema` reclaims a CLI name
  previously rejected for `--accepted` in §1.1.3 (that rejection was scoped to `--accepted`'s flat
  payload overpromising structure; `--schema` now genuinely delivers it). `--schema --json` is a
  public, frozen surface — its key names and `structures` shape are a compatibility commitment.
  1444/1444 CLI tests, 22/22 MCP tests. AOT publish needs no new `[JsonSerializable]` registration;
  MCP publish shows no NETSDK1151 regression.
- **Compound items** (§3.3, task #85) — `parts`, so one item can hold several sources with a
  colour each and no separator between them (a dim `agent:` label against an aqua value).
  `Segment.Spans` decomposition (SPEC-85 §5.1/§5.2) is now actually wired into the production
  render path — `ItemDecision` → `LeafContent.Decide` → `PaneAssembler` → the 4-arg
  `SegmentBuilder.BuildItemSegment` — not just present in unit tests
  (SPEC-85-ADDENDUM-spans-threading.md §12, closing that gap). The item-level colour wrap is
  skipped for a compound (already consumed per-part) and a `link` wraps the whole compound from
  outside the decomposition. `TruncateSpans`/`RestyleSlice` both close a colour span cleanly at a
  cut, append the truncation ellipsis as its own unstyled span, and keep the ellipsis outside any
  link so it is never clickable. `from` naming a compound item's own id (item-level or
  part-level) is now `from-compound-source`, error severity. 1443/1443 tests. Known accepted
  limitation: a compound spanning more than one line loses per-part colour (block layout renders
  each line as one plain `Segment`).
- **OSC 8 hyperlinks** (§3.2, §3.2.1) — committed as `1028f5d` and pushed to `main`, remote ref
  confirmed. Brings `remote-url`, derived items (`from` / `extract` / `case`), the `ItemContext`
  refactor (§3), one shared `AnsiStrip`, and `OscHyperlink.EscapeForRender`. 1063/1063 tests;
  Native AOT publish independently rebuilt to a matching SHA-256
  `4860066b…54e4a4`. **That verifies the artifact matches the source, not that each defect's fix
  behaves** — defects 1, 7, 8, 9 and 10 stay open below until checked against rendered bytes
  from the built binary.

## Queued, in order

1. **Phase 5 CLI** (§9) — `--check` (with `--json`), `--preview`, `--items`, `--colors`,
   `--version`.
2. **Phase 6 authoring surface** (§12) — backup ledger **first**, then `migrate`, `revert`,
   `edit`.
3. **Config diagnostics** — see the open defects below. Defect 11's fix is §5's resolution set,
   which item 4 depends on.
4. **`{item-id}` placeholders in a `command` item's argv** (§4.2) — hand a framework-resolved
   value to a user's own script without re-deriving it. Reuses §3.2's link-template resolver
   rather than adding a syntax. **argv-only expansion**; under `shell: true` the values go to
   the environment as `CLAUDE_TUI_LINE_VAL_<ID>` instead, because substituting into a `sh -c`
   string is command injection and a branch name is attacker-influenceable. **Ordered after
   defect 11 deliberately** — both are the same root cause (§5 enumerating displayed items
   rather than referenced ones), and building this first would reproduce defect 11 in a second
   place with the same silence.
5. **`maxRows` degrade ladder** (§2.8).
6. **Phase 7 MCP server** (§12.6) — ambient access, so "make the border green" works mid-
   conversation without the user knowing a slash command exists. Seven tools, **read/write**:
   `list_items`, `list_colors`, `get_config`, `set_config`, `validate`, `preview`, `revert` —
   enough for the model to carry a request from words to a rendered statusline unaided.
   `set_config` validates before it commits and never writes a config that fails `--check`;
   `preview` returns rendered rows so the model checks its work by looking rather than by
   asserting. **Deliberately last**: it wraps the CLI, so it cannot be designed before the CLI
   exists. Stateless, and the renderer stays a one-shot AOT binary regardless.
   **Spec now carries the wire contract** (§12.6.1–12.6.8): per-tool arguments and returns;
   failures as readable results rather than JSON-RPC protocol errors, with `--check` diagnostics
   passed through unflattened; the ruling that the server's environment is *not* the user's shell,
   so §5's search order can silently resolve a different file — hence `configPath`/`source` on
   every read and an explicit `configPath` override on every tool; `preview` never inferring a
   width; unconfirmed `revert` returning the ledger instead of acting; compare-and-swap on
   `set_config` so an interleaved hand edit is refused rather than clobbered; the three files a
   tool may write and no others. Nothing here is implemented — it is unblocked design ahead of
   the CLI landing.

## Open defects

| # | Defect | Impact | Status |
|---|---|---|---|
| 0 | **Width is derived by parsing markup, not from `Plain`** — `Markup.Remove(...).Length` at three sites | Violates the invariant the layout rests on. `Markup.Remove` strips Spectre tags, not ANSI, so any row carrying raw escapes measures long and the border lands early — silently. Genuinely pre-existing | **Fixed and verified.** `PaneRow(Markup, Width)` threads measured width through the pipeline; the dead `MeasureRow`/`FromMarkupRows` deleted. Confirmed by independent rebuild, SHA `55baa073…59ac9` |
| 1 | OSC 8 hyperlinks are counted as visible text | Border lands ~50 columns early; row goes ragged. Reproduced: `sgr-stripped=77, sgr+osc-stripped=27, budget=77, RAGGED` at `COLUMNS=80` | **Fixed and verified.** A linked row at `COLUMNS=112` pads out to the right border exactly as an unlinked one does — the OSC bytes contribute no width. Read from rendered bytes of the built binary, two configs (`git-branch`, `directory`) |
| 2 | `surface.maxRows` is entirely unenforced | 8 rows emitted at `COLUMNS=112` and 14 at `COLUMNS=60` against a configured 6 | Queued (5) |
| 3 | `ConfigLoader.TryReadConfig` swallows a malformed config | Exit 0, zero bytes on stderr, a completely different statusline renders. A JSON typo gives the user nothing to debug against | Queued (4) |
| 4 | `"auto"` and any unrecognized `size` silently resolve to `fill` | Same silent-acceptance class as #3 | Queued (4) |
| 5 | Unrecognized `case` value passes through unchanged | Same class; deliberately deferred to (4) rather than special-cased | Queued (4) |
| 6 | **An unrecognized colour name silently renders uncoloured** — `"color": "orange"` gives exit 0, empty stderr, and no SGR at all | Same silent-acceptance class as #3–#5, and the one most likely to be hit by a model authoring config (§4.1), which will reach for plausible names like `orange`. Verified through the built binary: `cyan`→96 and `magenta`→95 are accepted as aliases of `aqua`/`fuchsia`, but `orange` emits nothing | Queued (4) |
| 7 | **The test suite measures width with the wrong stripper** — `Markup.Remove(...).Length` at `RectangleInvariantTests.cs:16` and `SplitAcceptanceTests.cs:89`/`:102`/`:193` | Defect 0 was removed from production and left in the instrument that certifies production. Also: asserting `surfaceWidth == r.Width` is circular — both sides come from the same sum — so each site needs a second assertion measuring the *rendered bytes* independently | Reported fixed, **not yet independently verified**. Shared `DisplayWidth` helper (`AnsiStrip.Strip` → `Markup.Remove`, order load-bearing), two-assertion pattern at each site |
| 8 | **A configured link crashes the render** — `console.MarkupLine` throws `Encountered unescaped ']' token` on any row containing OSC 8, because Spectre's tokenizer reads the `]` in `ESC]8;;` as markup | Reproduced twice: isolated probe, and the full config→resolve→render→`MarkupLine` pipeline. **Spectre 0.57.2 has no native `[link]` support** — no `]8;;` literal anywhere in the assembly (UTF-16 scan, extraction proven against real literals), so we must emit OSC 8 ourselves and keep it away from the tokenizer. Any statusline with a working link currently goes silent | **Fixed and verified.** `EscapeForRender` at the three output sites. A configured link now renders `ESC]8;;<url> ESC\ <styled text> ESC]8;; ESC\` — correct URL, correct ST terminator, correct close — with exit 0 and empty stderr. Read from rendered bytes of the built binary |
| 9 | **`RemoteUrl.Normalize` cannot signal "not a recognized remote"** — non-nullable return, local paths pass through unchanged | A local-path remote yields `link: "/Users/x/repos/foo/tree/main"` — a link to nowhere. §3.2.1's drop-the-link ruling has no way to fire while the return type is non-nullable | Reported fixed, **not yet independently verified**. Returns `string?`; `ssh://git@host:2222/...` drops the port per ruling 8; `http://` restored per ruling 12 |
| 10 | **`Program.RunAsync` wraps everything in `catch { return 0; }`** | Any render exception becomes an empty statusline, clean exit, silent stderr — indistinguishable from "nothing configured". This is why #8 survived 1059 passing tests, and why three link-configured fixtures read as "no link" rather than "the renderer is throwing". Catching is right at `refreshInterval: 1`; exiting 0 with zero bytes is not | Ruled (11) — visible marker on stdout, one-line detail on stderr, keep exit 0 |

| 11 | **`{other-id}` in a `link` template resolves only when that item is also placed in a pane** | §3.2's own worked example, `{ "item": "git-branch", "link": "{remote-url}/tree/{}" }`, produces **no link at all**. The spec says the registry resolves these and forbids "a second lookup mechanism"; the code reads a map of already-rendered items, which is that mechanism. Lands on the primary use case — `remote-url` is referenced precisely so it need *not* be displayed. Fails silently and identically to a typo'd id, so the two are indistinguishable | Verified through the built binary, 5 discriminating configs: `{}` works alone (`.../x/main`, `.../d/claude-tui-line`); `{remote-url}` fails unplaced, **succeeds when placed** (`https://github.com/JimCline/claude-tui-line/tree/main`); `{nosuchitem}` fails identically. Queued behind `min-rows`. **Scope narrowed:** a derived item's
`from` is *not* affected — `{"id":"agent-short","from":"agent",...}` renders `CDTUI` with `agent`
nowhere in the config (fixture `{"agent":{"name":"cdtui-implementor"}}`, COLUMNS=100). So the
defect is the link resolver specifically, and whatever mechanism makes derived `from` work
unplaced is a model to copy rather than replace. Colour-token `from` untested — check before
assuming which side it lands on |
| 12 | **An empty pane still renders its borders** | `{"items":[{"item":"repo"}]}` in this repo emits 674 bytes — top and bottom border, no content row. Collides with SPEC.md:353 *"no segments ⇒ zero output even with border enabled."* Separately, `repo` yielding nothing here may be correct de-duplication (repo name and directory name are both `claude-tui-line`) — that part is unconfirmed **Ruled** — §2.4 now carries it. SPEC.md:353 survives, applied at two levels: an empty *surface* emits zero bytes; an empty `content`/`fill` pane collapses with its gutter; an empty `fixed`/`percent` pane keeps extent and border, because the user named a number and §2.3's principle of not overruling explicit sizing applies here too. Queued behind defect 11. Whether `repo` yielding nothing here is correct de-duplication is still unconfirmed and tracked separately |
| 13 | **`Columns112_LiveConfig_PackerInvocationCountStaysBounded` reads a racy process-global counter** — it reported 314 packer invocations against a `1..300` bound where a clean tree reported 45 | **Diagnosed: a test defect, not a sizer regression — there was never a 45 → 314 jump to explain.** `MinRowsPackerInvocationCount` (`SizeResolver.cs:324`) is a plain `static int`, incremented un-interlocked at `:501` by *every* min-rows render in the assembly; the test zeroes it at `MinRowsDistributeTests.cs:153` and reads it at `:155`. The repo has no `xunit.runner.json`, no `[CollectionDefinition]` and no `DisableTestParallelization`, so xUnit's default holds and test classes run **in parallel** — the test measures its own packer calls *plus whatever else was in flight*. 45 was a quiet scheduling window, 314 a busy one; defect 11's two new end-to-end `HyperlinkTests` perturbed the schedule, which is why it surfaced then. Being un-interlocked it can under-count as well as over-count | Diagnosed here, fix with the implementor: mark the counter `[ThreadStatic]` — sound *because* §5 resolve-once-per-render makes `SolveMinRows` synchronous, so the thread that zeroed the counter is the thread that reads it, and that precondition goes in a comment — plus a non-parallel `[CollectionDefinition]` for the instrumented class as belt-and-braces |
| 14 | **`shell: true` with a multi-element argv silently drops every argument after the first** | `CommandProvider.RunAsync:74` passes `command[0]` and nothing else to `sh -c`, so `{"command":["kubectl","config","current-context"],"shell":true}` runs `sh -c "kubectl"` and renders bare kubectl's usage text. **The only defect found so far that renders *wrong output* rather than nothing** — §7's contract is "bad config yields nothing at render, `--check` says why", and this escapes that pair entirely: the user gets no signal, not even an absence to notice. Cargo-culting `shell: true` onto a working argv array is an easy way to reach it | Ruled, both halves specified in §4.1. Render: suppress the item instead of spawning. Check: `error`, code `command-shell-argv`, fires only at `count > 1` (a single-element array is what the string form normalizes to, so it is correct and must not be reported). Found while ruling on the implementor's `command-shape` question — not by looking for it |
| 15 | **Border colour resolves two different ways, and the two do not accept the same language** | `PaneTreeRenderer.cs:76` calls `ColorResolution.Resolve` and gets a **spec string** used as a markup tag (fallback `"grey"`); `Program.cs:145` calls `ResolveBorderColor` → `ResolveLiteral` and gets a Spectre **`Color`** via `Style.TryParse(...).Foreground` (fallback `Color.Grey`). Both are live; which runs depends on the shape of the user's config. A markup tag carries decorations and a `Color` cannot, so `dim`/`bold` — documented in §6.1, accepted by `Style.TryParse` — are **predicted** to render on the tree path and vanish on the single-pane path, with no diagnostic, because both paths *succeed*. Item colour has no such split (`PaneAssembler.cs:66` uses `Resolve`), which is why it stayed invisible. Directly contradicts §6.5's own closing line, "one resolution point beats two" | Ruled in §6.6, task #15. `Resolve` becomes the sole border resolver; `ResolveBorderColor` survives as a thin adapter but returns a **`Style`**, not a `Color` — `ResolveLiteral`'s return type is the actual bug. **Step 1 is verifying the symptom**, not fixing: that `Style.TryParse("dim")` yields `Foreground == Color.Default` is inferred from the signature, never observed, and must not enter a test as an assumption. Found by asking what `--colors` may print, then listing `ResolveLiteral`'s callers — nobody was auditing borders |

**The OSC-8-in-the-sizer hypothesis was wrong and is closed.** It predicted the sizing path counted escape bytes as width, which would have made the *layout* wrong rather than merely slow. Tested directly: two configs identical except one item carrying `link`, rendered through the built binary at COLUMNS 112, 90 and 70, compared byte-for-byte after stripping OSC 8 then CSI (that order — stripping CSI first leaves the URI payload looking like text). **Identical at all three widths**: same row counts, same pane widths, same bytes. Defect 1's fix holds in the sizing path as well as the render path. Recorded because the reasoning was sound and the conclusion still false — `RowCountAt` → `PaneRenderer.RenderLeaf` genuinely is a caller defect 1 was never checked against; that just wasn't where this went wrong.

Noted for later, filed rather than done: `SolveMinRows` scans `t` linearly from 1 to `maxT` while its own comment states `feasible(T)` is monotone — the property that licenses bisection. And `RowCountAt` rebuilds `CandidateSegments` on every probe, though §5 guarantees values are constant for the render, so `rows_i(w)` is pure within a render and every repeat probe is provably redundant. Both were embargoed while defect 13 looked like a regression they could mask; with no regression to mask **the embargo is lifted**. They remain optimizations rather than fixes, so they land as their own change with their own before/after numbers — not folded into defect 11.

## Not started

- **Per-edge borders** (§2.10) — Excel-style per-edge selection, the 16-entry junction table, and
  the `reserve(p)` decomposition. Spec'd, unbuilt. Sits in the compositor border path.
- **`border: { "collapse": ... }`** (§2.10) — both visual languages, not one. `false` (**default**,
  and what ships today) is separate boxes; `true` collapses adjacent edges to one shared line.
  The payoff is width: a separate boundary spends `gutter + 2` columns, a collapsed one spends 1,
  so every interior boundary hands back `gutter + 1` columns. Default stays `false` because
  changing an existing config's visual language on upgrade is not a framework's call, and because
  `true` is the mode that needs the colour/style tie-break rule.
- **`height: "content"`** (§2.8) — a pane's border box closes under its last content row instead
  of filling its band, so a 2-row pane beside a 3-row one stops drawing a blank row inside its
  border. `valign` gains a second subject (it places the box in the band rather than the content
  in the box); no new knob. **Ships independently against the default `collapse: false`** — with
  separate boxes a short box introduces no new glyph case, since the neighbour's edges were never
  shared. Only its collapsed-mode junctions need the border grid. Does *not* reduce total rows;
  that is `distribute: "min-rows"`, already shipped.
- **Plugin packaging** — `.claude-plugin/plugin.json` and a `/claude-tui-line:setup` command
  that checks for the .NET SDK, builds into `${CLAUDE_PLUGIN_DATA}`, backs up any existing
  `statusLine.command`, writes the new one, and renders a preview. Designed, not built.

## Repository

**Backed up.** `github.com/JimCline/claude-tui-line` — **private**, 73 files, initial commit
`60eeb34` pushed to `main` and verified against the remote ref. `main` is protected against
force-push and deletion with admin bypass on, so the backup cannot be rewritten away but the
owner is never blocked.

Scanned before pushing: no credentials, no company data, no build artifacts.

Still needed **before making it public**:

- ~~README, LICENSE~~ — **done.** `LICENSE` is MIT. `README.md` documents install, the config
  file and its lookup order, panes and every pane key, all sixteen built-in items, custom
  `command` items, derived items, colours and colour rules, and hyperlinks. Every string value in
  it was read out of the parsers rather than assumed — `align`/`valign`, `size` forms, `overflow`,
  border `style`, the env var, and the published binary name (`claude-tui-line`, from
  `AssemblyName`). It documents only what exists: no CLI flags are described, because `Program.cs`
  has none yet. The silent-acceptance defects (3–6) are disclosed in a note rather than papered
  over.
- ~~Plugin scaffolding~~ — **done, but unexercised.** `.claude-plugin/plugin.json` and
  `.claude-plugin/marketplace.json` (the plugin *is* the repo root, so its marketplace entry uses
  `"source": "./"`), plus `commands/setup.md` at the plugin root — **not** inside
  `.claude-plugin/`, which holds only the two manifests. Both schemas were confirmed against the
  Claude Code docs rather than guessed. `/claude-tui-line:setup` checks for .NET 10, publishes
  into `${CLAUDE_PLUGIN_DATA}/bin`, **backs up `settings.json` and any script it points at before
  writing anything and stops if the backup fails**, edits `settings.json` in place preserving
  other keys, then renders a preview. **Structurally verified, not installed end-to-end.** Both
  manifests parse as JSON; `marketplace.json` carries the required `name`, `owner.name`, and
  `plugins[]` with `source: "./"`; `commands/` sits at the plugin root with all four command
  files, and `.claude-plugin/` holds only the two manifests. Every relative path the README
  points at exists, `tools/colors.sh` and `docs/backup-ledger.md` included. What remains
  unverified is the part only a real install exercises — `/plugin marketplace add` against this
  repo cannot be tried until it is public.
- ~~Genericize the hardcoded home paths~~ — **done.** The fixture cwd is now
  `/Users/example/git/repos/claude-tui-line` across `bench/fixture.json:2`,
  `tests/.../fixtures/full.json:2`, and three test files; `CAPTURE.md:9` likewise. The
  substitution preserved both the path depth and the final segment, because the rendered
  `directory` item derives from that path and a shorter path would have moved the golden-parity
  baseline. Verified rather than assumed: full suite 1069/1069, exit 0, parity gate included.

## Session log — overnight, 13→14 Aug 2026

All design, no implementation: the implementor holds `src/` for §9, so I stayed out of it. Twelve
commits, all pushed.

- **Genericized the hardcoded home path** (`6dcb68f`) — the last pre-public item. Preserved path
  depth and final segment so the golden-parity baseline could not move; suite 1069/1069.
- **Specified the MCP wire contract**, §12.6.1–12.6.8 (`d18b9b8`). Phase 7 was the last surface
  not specified to §9's depth. Four hazards ruled on, the sharpest being that an MCP stdio server
  does not inherit the user's shell environment — so §5's search order resolves a *different file*
  in each place, and nothing errors.
- **Added `--version`, §9.7** (`f14cd4b`). Found by checking cross-references: §12.6 surfaced a
  `cliVersion` that §9 never defined. Brings a test, because the number lives in both the `.csproj`
  and a hand-written `plugin.json` that cannot be generated.
- **Ruled `--check` width-independent, §9.8** (`dff1fdc`) — raised by the implementor, who was
  right that no existing invariant matched. Decisive argument: §12.6's `validate` calls `--check`
  from a process with no terminal.
- **Resolved a §9.4/§9.8 contradiction** (`eb42fe8`) — also the implementor's catch. `minSize >
  maxSize` is an `error`. The severity discriminator had been left implicit and was therefore
  re-derivable to opposite conclusions; it is now stated: **satisfiable vs unsatisfiable, not
  "does the renderer cope."** Coping cannot be the test, because §7 makes the renderer cope with
  everything.
- **Fixed a poisoned-`origin` defect in the backup ledger** (`cb9f2bb`) — the most serious find of
  the night, in the one component whose entire purpose is being trustworthy. Writing an `origin`
  because none exists yet is insufficient; a user who hand-wires the binary and *then* runs setup
  gets the tool recorded as its own escape hatch, permanently. Only `setup.md` was exposed —
  `migrate.md` already guarded the case for an unrelated reason, which is why it hid.
- **Fixed `migrate.md` ignoring `$CLAUDE_TUI_LINE_CONFIG`** (`dff1fdc`) and a stale backup-naming
  instruction in `setup.md` (`6dcb68f`), both found by re-reading committed prompts rather than
  trusting them.
- **Verified the plugin scaffolding structurally**; STATUS now says what was actually checked
  rather than "parses by inspection."

- **Ran a consistency pass over every unbuilt section**, prompted by the finding that §9 was the
  deepest section in the spec and still contradicted itself twice. §2.10 and §3.3 first
  (`7e157c0`, `1a59cbe`), then §4.2 (`1c89d8c`), which was the last one unchecked. §4.2 turned up
  three things: reject conditions with no diagnostic codes, the same omission §3.3 had; an
  apparent §3.2.1 contradiction that resolved into a real distinction — a dangling `{other-id}` is
  a *warning* in a `link` and an *error* in a `command` item's argv, because §3.2.1 defines what a
  dropped link does and nothing defines what an unexpanded argv entry becomes; and a new
  `placeholder-env-collision`, since `CLAUDE_TUI_LINE_VAL_<ID>` mangling is many-to-one and
  `agent-short` and `agent.short` both land on `AGENT_SHORT`. The same commit fixed §9.8's third
  bullet, which still charged gutters and border reserve as independent addends — the double-count
  `7e157c0` had already corrected in its first bullet.

**Every section of the spec has now been consistency-checked.** A grep sweep of every flag and
diagnostic code found no dangling references, but grep was never going to be enough: none of the
inconsistencies it missed were dangling identifiers. They were two sections defining one condition
with different severities, and one formula corrected at one of its two use sites. Two came from the
implementor building two sections at once, which is the argument for a separate pair of eyes on the
spec rather than just a reader.

### Second batch — the consistency pass turning up real defects

The pass stopped being bookkeeping and started finding things.

- **§5.1, `remote-url` caching** (`8ee5744`) — closes the last unspecified queued item. It reuses
  `ItemCache` rather than getting its own store. The ruling that bites: a probe that finds nothing
  is a *cached result*, not a cache miss, or git gets re-spawned every render in exactly the case
  where the spawn can never return anything.
- **§2.11, empty-pane collapse** (`d506075`, corrected in `28c1bd2`) — defect 12. "Empty" has two
  causes and only one is safe to act on. Collapsing a pane that the *sizing loop itself* emptied
  frees width, which un-drops the items, which un-empties the pane, which reclaims the width — a
  cycle with no fixpoint, breaking §2.3's convergence argument. I also had to retract my own
  fixed-size bullet: §2.4 already ruled the opposite and its reasoning was better than mine.
- **§9.4.1, the severity test** (`ac1c959`, then `fdad2ac`) — and this one is the cautionary tale
  of the night. The first version contradicted the paragraph three lines below it: applied
  strictly, "delivered vs deleted" demotes every typo to a warning, because an unknown `size`
  falls back to `fill` and renders fine. Two tiers now — *not in the language* is always an error,
  and only *in the language with a missing referent* gets judged by consequence. **A section can
  contradict itself within its own body**, which is a shorter distance than any of the other
  inconsistencies found this session had to travel.
- **§2.10's `collapse: false` formula was arithmetically wrong** (`28c1bd2`), and this is the one
  that would have shipped. It charged `(N−1)×(g+2)`, but `N` separate boxes own `2N` edge columns
  and that expression covers `2(N−1)` — it drops the outermost pair and under-reserves by 2 at
  every `N`. Found because the implementor asked which of two arithmetics to build `--check`
  against instead of picking one. The durable fix is that **nothing restates the arithmetic,
  including this document**: one named function, called by both the renderer and the checker.
- **One condition, four positions.** A dangling `link` was an error in §9.4, a graceful drop in
  §3.2.1, "rejected" in §3.2, and a warning in the §4.2 text I wrote myself. All four now say
  warning. A third severity — "notice" — was also in §6.2 by accident, in a system §9.4 defines as
  having exactly two.

### Third batch — a stale defect, and the verification rule catching a *retrieval*

- **§2.8's "the second pass currently frees nothing, and that is a live defect" was stale.** The
  fix landed and the prose never caught up. `ResolveVertical` re-measures *with* the grant
  (`SizeResolver.cs:129`), `MeasureRequest` turns it into an inner cap and returns
  `LongestWrappedRowWidth` rather than the cap, `:130` clamps monotonically against the previous
  request, and `:141` reallocates from the reduced set, bounded at `MaxPasses = 3`. So the loop
  can once again produce a shrinking re-measurement — the thing the banner-to-text deletion had
  removed the only source of. §2.8 now says so.
  - **Still owed, and now recorded rather than assumed:** §10 requirement 6's fixpoint tests drive the loop
    through `measureOverride`, so they certify the monotone clamp and the pass cap **against a
    stub**. Nothing yet asserts that the *real* measurement frees six columns at `COLUMNS=112`.
    A defect can be fixed and its test still be measuring something else.

- **A retrieval subagent fabricated a spec section, and the fabrication was caught by content
  rather than by suspicion.** Asked to print §9.6 verbatim, it returned ~15 lines of fluent,
  correctly-formatted prose describing a "handshake", a `tools` table, and the root output of a
  `_parse_file` call — none of which exist anywhere in this project. The real §9.6 is 26 lines
  about `--json` shapes. The tell was not that the output looked wrong; it read perfectly well.
  The tell was that it described a protocol this tool does not have.

  This extends the rule at the top of this file. "Verified independently of the report claiming
  it" was written for *implementation* reports — a claim that code works. It turns out to be
  needed just as much for *retrieval* reports, where the failure is quieter: a fabricated file
  quote has no build to fail and no test to go red, and it would have been laundered into the
  spec as a real inconsistency to go and fix. **Prose that will be quoted or edited gets read
  directly; delegated retrieval is for locating and counting, not for transcribing.**

  Worth noting what did *not* catch it: two runners independently agreeing on line numbers in
  `SizeResolver.cs` is real corroboration, and that report held up. The difference is that code
  could be cross-checked against a second runner's grep of the same symbols. A single verbatim
  print of prose has nothing to cross-check against except the file itself.

- **Six diagnostic codes did not exist, including the two biggest.** §9.6 declares `code` values a
  permanent compatibility surface, but nothing gathered them, so they lived scattered across six
  sections and nobody could see the holes. Writing the registry (new §9.6.1) found that **every
  unknown enum value and every unknown colour name** — between them the entire reason §9.4 exists,
  and the exact diagnostics being implemented this week — had **no code string anywhere**, while
  §3.3 already cited "the existing `unknown-item-id` and unknown-colour codes" as though one had
  been defined. All three §9.4.1 warnings were also unnamed.

  Named now, before anyone had to invent one mid-build: `unknown-enum-value`, `unknown-color`,
  `overflow-forbidden-position`, `unknown-link-target`, `unknown-color-source`,
  `unknown-color-token`. Rulings that came with them —
  - **One code for all six enum keys, not one per key.** The JSON Pointer already names the key
    and the repair is identical in every case. §4.1 splits `command-shape` from
    `command-shell-argv` under the *same* rule because there the repairs differ. One rule, opposite
    answers, which is how you can tell it is doing work rather than dressing up a decision.
  - **None of the three item-id warnings reuses `unknown-item-id`.** §9.5 ruled that sharing the
    reference walk is not sharing the verdict; the corollary is that the code must carry the
    verdict, or a consumer branching on it can't tell error from warning. One code with two
    severities is a code whose meaning is not fixed.
  - **`unknown-color` is spelled the American way on purpose** — codes match the config key
    (`"color"`), this document says colour, so the mismatch looks like a typo and will tempt a
    tidy-up that silently breaks a shipped surface. Written down so it survives.
  - **Tool-protocol codes are a separate table.** `stale-revision` and `cli-not-found` describe a
    failed *invocation* and have no `path`; they must never be confused with a `diagnostics` entry
    pointing into the user's config.

- **The registry immediately found two more, and both turned out to be warnings that should not
  exist.** §9.4's prose named two warnings no code covered, so the first act of the new registry
  was to catch its own omissions. Checking them against the code rather than adding rows:
  - *"A `command` item with no `timeoutMs`"* — **removed.** The premise was an unbounded
    subprocess in a once-a-second loop. There is no unbounded subprocess:
    `CommandProvider.DefaultTimeoutMs = 150`, whole process tree killed. The warning would have
    sent every author to set a key already set for them. `commands/migrate.md` rested on the same
    false premise and has been corrected — the advice to choose the values deliberately survives,
    since 150 ms is genuinely tight for a migrated script, but the *reason* is now the true one.
  - *"A pane with no items"* — **narrowed** to `content`/`fill` only, code `pane-no-items`. §2.11's
    own division does the work: those collapse, so the declaration achieved nothing; an empty
    `fixed`/`percent` pane keeps its extent and is a legitimate **spacer**. Unqualified, this
    warning fired on working intent.

  The shared lesson, now in §9.4: **a diagnostic's premise is a claim about the implementation and
  goes stale like any other.** Both were true when written; one stopped being true when a default
  landed, the other when §2.11 was ruled. Neither was reachable by grep — nothing dangled, and both
  read as sensible prose right up until they were checked against the thing they asserted.

### Fourth batch — `--items`, and the §1 failure inside the paragraph warning about it

Ran the registry technique on the *item* ids next, to see whether the list was duplicated the way
the diagnostic codes were. It is not: `ItemRegistry.cs` is a genuine single enumeration point, the
only `.cs` file that names all sixteen, and its `DefaultIds` derives the opt-in set rather than
restating it. That check came back clean.

What it did surface was §9's `--items` bullet, which specified five fields against a record that
carries two. Checking each against `ItemRegistry.ItemDefinition` — now §9.6.2:

- **"which config keys it accepts" — wrong shape.** Keys do not vary by item id; every builtin
  takes `format`/`color`/`overflow`/`link` and nothing else. What varies is the *kind* — §4.1's
  `command`, §4.3's `from`/`extract`/`case`, §3.3's `parts`. A per-row key column would store one
  per-kind fact sixteen times. That is the §1 duplication failure, specified by the sentence
  immediately above the paragraph that warns about it — the shortest distance an inconsistency has
  travelled in this document yet. `--items --json` now emits two sections, `items` and `kinds`.
  §12.6's `list_items` row had it right all along ("**and the schema for defining a new one**"),
  which is what confirmed the ruling: a per-row column cannot describe a row that does not exist.
- **"its default format" — unimplementable as written.** There is no format string. Rendering is a
  builder function per row, so there is no `"⎇ {}"` anywhere to print, and satisfying the field
  would mean adding a template layer beneath every builder so a CLI flag has something to name.
  Replaced with `example`, rendered through the same `BuildDefaultSegment` the renderer calls,
  against one canned synthetic context. It answers the authoring question better anyway: *does
  this item already carry its own decoration, or do I need a `format`?*
- **Two fields were missing.** `reports` (the description) and `default` (in the default pipeline
  vs opt-in). `reports` goes on `ItemDefinition` as a **required** positional field, so adding a
  row without describing it fails to compile. `default` matters to §12.2: mapping an element onto
  `remote-url` or `model-short` and not placing it yields a config that passes `--check` and
  renders short.

`commands/migrate.md` step 0 claimed `--items` was "the only authority on what exists and what keys
each item takes" — the authority claim survives, the per-item half does not, and it now tells the
migrator to read both sections and to treat `default: false` as a placement obligation.

**The technique generalised.** Gathering identifiers found missing codes; comparing a spec's field
list against the record that has to supply it found a field that cannot exist and two that were
never asked for. Same move both times: put the scattered thing next to the single thing that owns
it, and the holes stop being invisible.

### Fifth batch — `--colors`, and a registry that should not be built (§9.6.3)

Ran the same check on `--items`' sibling flag, which was specified to "print every accepted colour
name". It cannot, and this one is structural rather than an oversight.

**There is no colour registry in `src/`, and there should not be one.**
`ColorResolution.ResolveLiteral` is two lines — it hands the string to `Spectre.Console.Style.TryParse`
and takes the `Foreground`. So the accepted set is Spectre's whole table plus `#rrggbb` hex, which
is infinite. The sixteen are named only in prose; no `.cs` file lists them. Both ways of giving
`--colors` something to enumerate are wrong: hardcoding the accepted names is a second registry of
a table we do not own, drifting against a library upgrade with no care on our side able to prevent
it; reflecting over Spectre's `Color` statics has no drift but means reflection over a library type
in an AOT binary, to print 256 swatches nobody wants to read.

**Ruling: `--colors` prints a curated recommendation** — the sixteen plus `default`/`dim`/`bold` —
and that list is a genuine new artefact, since it exists nowhere in code today. A curated list can
rot in a way a derived one cannot, so every entry is asserted to round-trip through `ResolveLiteral`
non-null. **The list is allowed to be hand-written precisely because that test refuses to let it be
wrong** — without it, a renamed colour would go on being recommended and fail as a silently
uncoloured item under §7.

**The failure that made this urgent is the `kinds` failure again in different clothes.** §12's
tools read `--colors` as authority, and §12.2 told the migrator to preserve colours "by name, from
`--colors`". A bare list of nineteen names reads as exhaustive — so a model asked for `#ff8800`,
which parses fine and always has, would consult the list, not find it, and refuse or silently
substitute. **An authority list that omits a valid form causes the tool to refuse valid work.**
`--colors --json` now carries `recommended` and `alsoAccepted` as separate keys, and each
recommended entry carries `themeMapped: true` — the reason to prefer it, stated, so a model can
weigh it against a user who explicitly wants a specific shade instead of merely obeying it.

`commands/migrate.md` corrected to match: standard ANSI in the original maps to a recommended name
(theme-mapped, same behaviour as before), but a *specific* shade in the original maps to a 256 name
or hex, with `colorSystem` flagged in the report — reproducing a truecolor escape by nearest name
is a downgrade, and the old instruction mandated it.

**And tracing `ResolveLiteral`'s callers to write that section found defect 15** — see below. The
`--colors` question had nothing to do with borders.

### Defect 15 — two border-colour resolvers, found sideways (§6.6, task #15)

§6.5 closes with "one resolution point beats two." Border colour has two, one layer below where
that sentence was looking:

- `PaneTreeRenderer.cs:76` → `ColorResolution.Resolve` → a **spec string** used as a markup tag,
  falling back to `"grey"`.
- `Program.cs:145` → `ResolveBorderColor` → `ResolveLiteral` → a Spectre **`Color`** via
  `Style.TryParse(...).Foreground`, falling back to `Color.Grey`.

Both live in production; which runs depends on the shape of the user's config. Item colour has no
such split — `PaneAssembler.cs:66` calls `Resolve` like the tree path does — and that is exactly
why this stayed invisible, since the divergence exists on one key only.

**It is a capability gap, not just duplication.** A markup tag carries decorations; a `Color`
cannot. `ResolveLiteral` takes `.Foreground` and discards the rest, so `dim` and `bold` — both
documented in §6.1, both accepted by `Style.TryParse` — are predicted to render on one path and
vanish on the other, with no diagnostic, because both paths *succeeded*. The two fallbacks agree
in appearance today by coincidence, not construction.

**Since verified, and it is wider than predicted.** The implementor built a throwaway console app
against the pinned Spectre 0.57.2 for §9.6.3.1's round-trip assertion — a different section — and
in the process settled this: `TryParse("dim")`, `("bold")` and `("default")` all return true with
`Foreground == Color.Default`, while `("olive")` returns a real colour. Then every remaining
decoration keyword behaved the same way: `italic`, `underline`, `invert`, `conceal`,
`strikethrough`, and both blinks. So the rule is not three special cases but one — **a
decoration-only spec has no colour component to contribute** — and `ResolveLiteral` drops the whole
class, not just the two §6.1 happens to document. `border.color: "italic"` fails identically.

That also fixes the shape of the test: driving this with a *colour* passes on both paths, because a
colour is the one input where the two resolvers agree. It has to be driven with a decoration, and
it has to assert the decoration survives — `Color.Default` is what the broken path returns
*successfully*, so "it resolved" passes against the bug (§10.1, arriving through a second door).

**Ruled, and the dispatch narrows it to one pane shape (§6.6.1).** "Which runs depends on the
shape of the user's config" is true and too generous. `Program.cs:94` sends every pane with items
or children to the tree path, so the `Panel` branch is reachable only by a pane with a border, no
items and no children — an *empty bordered pane*, including `SafeLoadAll`'s config-error fallback
pane (`Program.cs:496`), which is built with no items and no children by construction. Narrower in
reach, worse in stakes: on the only path where the narrow resolver runs, the border is the entire
visible output, with no content inside it to carry styling instead.

**Defect 12 narrows this branch, it does not empty it — and reading §2.11 rather than task #4's
title is what showed that.** "Empty pane still draws its border" as a title says the branch's only
live case is about to vanish. §2.11 rules something narrower: collapse reaches only a structurally
empty `content`/`fill` pane with no `minSize`. Empty `fixed` and `percent` panes keep extent and
border on purpose (§2.4), as do `minSize` panes (§2.11.1), and all of them still have no items and
no children — so they still route to the `Panel` branch. The adapter is needed either way and
deleting the branch is off the table. My first draft of §6.6.1 said the opposite, from the task
title alone; the correction is the same wrong-citation habit as always, one door further along —
a *task title* is not the ruling either.

**`--check` is an aggravator, not a third instance of the defect.** `ConfigCheck.cs:194` also calls
`ResolveLiteral`, and the reflex is to file it as more of the same. Its comment already reasons
correctly — a spec resolving to `Color.Default` has no palette dependency — so `--check` accepts
`border.color: "dim"` and is *right to*. Acceptance there is `Style.TryParse`, the same question
the markup path asks. Which makes it worse: the user is told the spec is valid by the tool whose
job is to say so, and then one renderer honours it and the other does not.

So `ResolveLiteral` survives the fix rather than being deleted — §6.2.1's palette ranking genuinely
needs a `Color`, and discarding decorations is correct for that question. It is a **palette query,
not a resolver**, callable from §6.2.1's check and `--colors` and from no render path, and that has
to be written at its definition: a `Color`-returning function is the convenient reach when a
Spectre API wants a colour, and the call site type-checks. That is how this got written the first
time.

**The reusable shape:** a function that parses a rich value and returns one field of it is a lossy
narrowing wearing a parser's name. `ResolveLiteral` parses a whole `Style` and returns
`.Foreground` on the next line — one property access wide — and nothing in the type system, the
tests, or `--check` was ever going to point at it. My own working hypothesis going in was that
Spectre's API *forced* the `Color`; it does not. `Panel.BorderStyle` takes a `Style`, and
`Program.cs:149` already builds one — from the narrowed colour. The constraint was self-inflicted,
which is the answer worth having, because a forced narrowing would have needed a different fix.

The original entry below is kept as written, because how the gap was recorded is the reusable part.

**Recorded as unverified where it was unverified.** That `Style.TryParse("dim")` yields
`Foreground == Color.Default` was inferred from the signature, not observed, and §6.6 said so in a
block quote rather than burying it. The defect held either way — two resolvers and two fallbacks
for one key is the defect — but the symptom was a prediction about a third-party library, and
§9.4's lesson (a claim about the implementation goes stale, or was never checked) applies to spec
prose exactly as it applies to diagnostics. Task #15 makes verifying it step 1, ahead of the fix,
so it cannot enter a test as an assumption.

**The fix is the project's usual shape:** `Resolve` becomes the sole border-colour resolver;
`ResolveBorderColor` survives as a thin adapter but returns a **`Style`**, not a `Color`, so
decorations reach `Panel.BorderStyle`. `ResolveLiteral`'s return type is the actual bug.

**How it was found is the transferable part.** Nobody audited borders. The question was "what is
`--colors` allowed to print?", which led to `ResolveLiteral`, which led to listing its callers —
and the answer was one caller, while the *sibling* function had another, taking a different type.
§9.8's rule was written about a checker transcribing the renderer's arithmetic; this is the same
drift with both copies inside the renderer, where no checker/renderer boundary suggests looking.

### The audit caught one of mine (§9.3 / §9.6.2)

Moving on to `--preview` and reading §9.3 turned up a duplication **I had introduced two commits
earlier**. §9.6.2 said `--items`' `example` renders "against one canned synthetic `ItemContext`
fixture" — and §9.3 already specifies a fixed, non-randomised synthetic payload for `--preview`.
Two synthetic payloads, for the same purpose, written into the same document within an hour, one
of them inside the section correcting a duplication.

Fixed in both places: **one `StatusInput` constant**, which `--items` wraps with canned
`GitBranch`/`Engram`/`RemoteUrl` — the only fields `ItemContext` adds beyond the payload, and they
need constants because they come from probing the machine, which `--items` must not do.

Worth recording plainly rather than quietly repairing. The consequence was not hypothetical: an
`example` built from a different payload would show an item one way under `--items` and another
under `--preview`, and `/migrate` reads both as authority in the same session with no way to
notice they came from different inputs. **Two authorities disagreeing is worse than either being
wrong alone**, because a single wrong answer is at least consistently wrong.

The transferable bit is that knowing the failure mode by name did not prevent committing it. What
caught it was the mechanical habit — read the neighbouring section before specifying against it —
and not the understanding.

### Auditing the unimplemented specs instead of hunting more defects

Five specs went to the implementor tonight and it is still on §9; finding a sixth thing for the
queue is worth less than making sure what is already queued is right. So the next pass was over my
own pending specs, where a gap costs nothing now and a round trip later. Two results from §3.3
(compound items, task #6):

**Clean:** `part-source-count` and `part-forbidden-key` are both in §9.6.1's registry, at the rows
for §3.3. The registry did not lose them when it was built.

**Not clean — §3.3's literal-binding rule had a hole in the exact failure it claims to close.**
It read: when a value part is empty, drop "the literal part next to it — the one after it if there
is one, otherwise the one before." Walk that over a value wrapped in a literal pair:

```json
[ {"text":"("}, {"from":"pr"}, {"text":")"} ]
```

An absent PR drops `)` and keeps `(`. The statusline renders a bare `(`. **That is the
render-wrong class — the second instance after defect 14**, and the one §7's contract does not
cover: not an absence the user might notice, but visible output that is simply incorrect, with no
diagnostic anywhere because nothing failed.

Now: **a literal is dropped when any adjacent value part is empty**, evaluated against original
array positions. It handles the bracket pair, still handles `agent:` and ` | ` separators
identically, and is the simpler implementation — it never needs to know whether a *following*
literal exists in order to decide about a preceding one. Evaluating against original positions
rather than the partially-mutated array is what keeps it order-independent, which is the same
objection §3.3 already raises against nested parts.

**Caught by walking the rule over cases rather than reading it.** The prose is persuasive; the
counter-example takes three parts and appears the moment you try one. Worth remembering that a
rule stated as closing a failure is a claim to test, not a claim to accept — the same posture
§9.4's stale warnings needed, applied to a rule that was never true rather than one that stopped
being true.

### §4.2 made the right argument and applied it to only half the cases (task #5)

Same pass over argv placeholders. `--check`'s three codes and §5's six-construct enumeration are
both complete and consistent — that part is clean. The gap is elsewhere, and it is a nice example
of a section containing its own missing ruling.

§4.2 justifies making an **unknown** placeholder id an error with this: "The literal
`{gitbranch}`? An empty string? A dropped argv entry? Each is a different command line, the script
sees a different `$1`, and the spec picks none of them." That argument is exactly as true of an id
that is **known and simply empty at render time** — `{git-branch}` outside a repo. And there,
unlike the unknown case, `--check` cannot help, because emptiness is a runtime condition. The
section made the argument and then applied it only where a checker could act, leaving the harder
half unspecified.

**Ruled: substitute the empty string; the argv entry survives.** Arity is the thing that must not
change. Dropping the entry shifts every positional after it, so
`mytool --branch {git-branch} --format json` outside a repo becomes `mytool --branch --format json`
and the tool binds `--format` as the *value* of `--branch` — **a third render-wrong case**, and the
nastiest of the three, because the command runs, exits 0, and reports something the user never
asked for. An empty argument can be wrong; it cannot re-bind a flag to the wrong value.

Under `shell: true` the variable is exported **empty rather than unset**, for a parallel reason:
unset lets the script's own `${VAR:-default}` fire, silently converting "no branch this render"
into "whatever the script's default is", which is indistinguishable downstream.

**And the item is not suppressed** — the opposite call from defect 14, deliberately. There the
config has no defined meaning; here it has one and the value merely happens to be empty.
Suppressing would delete behaviour the author chose.

The pattern across §3.3 and §4.2 is the same: both sections argue well about the case a checker
can catch, and go quiet on the case only the renderer sees. That is where the render-wrong class
keeps coming from — and none of it is reachable by `--check`. Generalised into §7.1 next.

### The class had four members and no home in the spec (§7.1)

Four separate rulings tonight all turned out to be the same failure wearing different clothes:
defect 14's `sh -c "kubectl"`, defect 15's two border resolvers, §3.3's orphaned `(`, and §4.2's
re-bound flag. Each got fixed where it was found. None of them was *named* anywhere.

That matters because **§7 only describes two outcomes** — the config is fine, or it renders less
than intended and `--check` explains why. Every one of those four produces a third: output that is
present, plausible, and wrong. The user cannot notice it (nothing is missing), `--check` cannot
report it (nothing failed), and the render path cannot degrade around it (from the inside it looks
like success). A spec whose failure-semantics section does not admit the category will keep
producing members of it one ruling at a time, which is precisely what happened.

So §7.1 now names it, tables the four instances, and states the rule they produced: **when a
config has a defined meaning and only the value is absent, prefer the option that cannot silently
produce different-but-plausible output** — preserve arity over dropping an entry, export empty
over leaving unset, drop a bound literal over emitting it alone. When the config itself has no
defined meaning the call inverts to suppression (defect 14), which puts the fault back where
`--check` can speak.

The part worth keeping is the question it hands to every future rule, because three of the four
were found by asking it rather than by anything failing: not "is this right?" but **"what does
this do when the value is missing at render time, and who would ever find out?"** If the answer
to the second half is "nobody", the rule is not finished.

Two pending specs remained un-walked at that point — §2.10 per-edge borders (task #8) and §2.11
empty-pane collapse (task #4). Both got the same treatment next.

### §2.10 and §2.11 walked — seven gaps, and one section contradicting another (§2.10.1, §2.11.1, §2.11.2)

These two were the last unimplemented specs nobody had walked over cases. Seven findings, five of
them §7.1-class.

**§2.10, five gaps (§2.10.1):**

1. **`outline` and `inside` cannot be what the section says they are.** It calls all four
   shorthands expansions of the declaring pane's four booleans. True of `all` and `none`; false
   of the other two, because "no interior dividers" describes a split's *descendants* and no
   booleans on the split itself can say it. Read literally, `"border": "outline"` renders the
   outer box **plus** every interior line — the exact opposite — silently. Ruled: on a split they
   are subtree instructions; on a leaf `outline` ≡ `all` and `inside` ≡ `none`; a descendant's own
   explicit `border` overrides an ancestor's shorthand, or `outline` is a mute nobody can escape.
2. **`collapse` never had a stated home.** Ruled surface-level only: the compositor resolves
   **one** grid, and the boundary between a collapsed subtree and an uncollapsed sibling has two
   different widths with no way to ask which. Accepting it on a pane and ignoring it is the §7.1
   failure exactly, so it is an error (`collapse-not-surface-level`), not a silent ignore.
3. **The degrade rule frees nothing under `collapse: true`.** "A pane drops its vertical edges
   first" assumes the edge is that pane's column; collapsed, the line is drawn if *any* neighbour
   wants it, so the pane drops its edge, the line stays, and the sizing model credits a column it
   never got back — a ragged row (§2.4) arriving from the degrade step. Ruled: under collapsing,
   degrade operates on **boundaries**, dropping a divider entire for its 1 column.
4. **A junction can have arms in three styles.** "One table per style" presumes one style at the
   position, but the tie-break resolves style *per edge*. Ruled: the junction takes the first
   requester in tree order among its arms — the same tie-break, one rule instead of two.
5. **`reserve(p)` is width-only and horizontal edges cost rows.** The decomposition splits
   `borderReserve` into edges + padding on the width axis and says nothing about rows, while §2.8
   reasons throughout about a box closing under its last content row — written when both
   horizontals always existed. Ruled: `rowReserve(p)`, same one-function-no-transcription regime.

**§2.11, two gaps — and the first is two sections disagreeing (§2.11.1, §2.11.2):**

6. **§2.3's floor table and §2.11 give different answers for the same pane.** The table reads
   `p.minSize set -> p.minSize (author said so; always wins)`; §2.11 says a collapsed pane
   "occupies no width". A `content` pane with `minSize: 12` holding nothing is twelve columns by
   one and zero by the other, and a reader consulting either leaves confident. Same shape as
   defect 15 and worse than either being wrong alone. Ruled **`minSize` wins**: it is §2.4's own
   declared-versus-inferred test, it is the only way to say "size to content but hold this much",
   and it inverts safely — omit `minSize` to get collapse, so nobody is surprised into a pane
   that vanishes.
   - **Follow-on false positive:** §9.4's `pane-no-items` justifies itself with "it collapses, so
     the declaration did nothing" — which stops being true for those panes. Registry row amended.
     A diagnostic whose reason has lapsed is how authors learn to ignore a checker.
7. **A timed-out command item collapses the pane, once a second.** §5 hands the pre-pass "value or
   no value", but a `command` item reaches no-value two ways: it returned nothing, or it never
   answered inside the 150 ms budget. Collapsing on the second means a pane that vanishes and
   returns as a script drifts across its timeout — the line jumping once a second, which is the
   *precise* failure §2.11 names two paragraphs earlier as why `fixed` panes don't collapse. It
   came back through the door the section had just shut. Ruled: **absent** (resolved, no value) is
   distinguished from **unavailable** (did not answer), and a pane holding an unavailable item
   keeps its extent for that render.

Four new registry codes in §9.6.1: `collapse-not-surface-level`, `border-inside-on-leaf`,
`color-down-converted` (§6.2's approximation warning, which the implementor correctly flagged as
missing), and the amended `pane-no-items`.

**What the pass is worth, stated plainly.** Seven gaps in two sections that both read as finished
prose, found by asking §7.1's question of each rule rather than by anything failing. None would
have surfaced in a test, because in every case the code would have done something defensible. The
technique is now cheap enough to be routine: walk the rule over concrete cases, and ask who finds
out when it is wrong.

### Phase 6 walked — the backup that did not contain the file it was backing up

With every pending spec walked, the last unaudited surface was Phase 6: the four prompt files that
write `settings.json` and restore backups. Highest stakes in the project — a gap here loses the
user's statusline — and unexercised, because they all gate on a CLI that does not exist yet. Six
findings, one of them the worst thing found so far.

**1. `/edit`'s checkpoint did not contain the file `/edit` modifies, and its recovery path was a
no-op that reported success.** `/edit` changes `claude-tui-line.json` and nothing else. The ledger
entry captures `settings.json` and, optionally, the user's original script — never the config. So
step 7's "restore the checkpoint from step 4 and report the failure" restored a `statusLine` key
nobody had touched, left the broken config exactly where it was, said it had recovered, and the
pre-edit config was gone. Every individual instruction was followed correctly.

What hid it was a scope line: the ledger opened "**Every command that writes to `settings.json`
follows this**", and `/edit` does not write `settings.json`. By the ledger's own scope, `/edit`
should never have been pointed at it — and the contradiction between that line and `/edit`'s step 4
is exactly where the missing artifact lived. Fixed by widening the scope to both files and giving
the entry a second artifact class (`configOriginalPath`/`configCopy`/`configSha256`), recorded
whenever a config exists rather than only when the running command intends to change it — because
the command that needs a backup is not the one that took it.

**2. The hash check would have disabled the escape hatch permanently.** The ledger said "re-hash
the **live file** and compare against the ledger entry", and `revert.md` said "re-hash the
**artifacts the ledger entry points at**" — two documents, two different files, one shared
rationale. Two authorities disagreeing again, the defect-15 shape.

And the ledger's reading is not merely different, it is fatal: at revert time the live
`settings.json` is *supposed* to differ from the backup, because claude-tui-line is installed now
and was not then. A revert following it literally reports "the user hand-edited it" on every run
and stops. The escape hatch refuses precisely when it is reached for. Replaced with a four-row
table naming which files are checked and what a mismatch means for each.

**3. The rationale in that instruction was true — about a file nobody was checking.** "A mismatch
means the user hand-edited it since the backup" describes the user's **original script**, at its
original path. `revert` restores a `statusLine` verbatim, which points at a *path*, not at
contents. `revert.md` handled the script being **missing** and said nothing about it being
**modified** — and modified is the silent one: the revert succeeds, the statusline renders, and it
is simply not the statusline that was backed up. Now three cases, with the third asking the user.

**4. `revert` checkpointed before it verified.** Step 3 appended a ledger entry (mutating); step 4
verified hashes and could stop. In an append-only ledger that forbids removing an entry, an aborted
revert left a permanent record of a change that never happened. Swapped, and the general form is
now stated in the ledger procedure: every step that can abort comes before every step that writes.

**5. Timestamp arguments would not have matched.** Ledger entries key on full ISO
(`2026-08-13T04:12:07Z`); artifact filenames use the compact form (`20260813-041207`) — and the
compact form is what the previous command *printed in its report*, so it is what the user pastes.
Match liberally now: either form, or an unambiguous prefix.

**6. `revert` and the config file needed an explicit non-interaction.** Now that entries carry a
config copy, "restore everything" is a tempting reading. Ruled explicitly: revert does not restore
the config. It answers "put my old statusline back", not "undo my layout work", and rolling
someone's layout back as a side effect of unpointing `statusLine` destroys work they never asked
about. `/edit` owns config rollback.

**The generalisation, which is not about backups.** A procedure can be correct at every step and
still not do its job, when the artifact it operates on is not the artifact the caller modifies. The
check that catches it is one question asked of the call site rather than of the procedure: *does
what this saves include what this command is about to change?* Nothing inside the ledger could have
answered that, which is why it survived a careful reading of the ledger.

### §12.6 walked — and one fix from earlier tonight had already propagated (§12.6.7, §12.6.9)

Last specified-but-unwalked surface: the MCP wire contract. Seven tools, two of which write the
user's files. Five findings.

**1. "Three files, and no others" was missing one the same section requires.** §12.6.7 enumerates
what an MCP tool may write, and `revert` restoring the copied script to `scriptOriginalPath` is
not among them — while §12.5 and §12.6 both require exactly that write, because a restored command
pointing at nothing leaves the user with no statusline and no obvious cause. An implementor
obeying the list literally would skip it and produce the failure the restore exists to prevent.
Added as a fourth entry, and scoped hard: only from `revert`, only from the entry being restored,
only when no file is there, never overwriting. "Restore the script" is otherwise an arbitrary-path
write driven by the contents of a data file, which is a much larger permission than the other
three and should not arrive by implication.

**2. Only `get_config` could say which file it touched.** §12.6.2 spends a section on the hazard
— the server's environment is not the user's shell, so §5's search order can resolve a different
file with nothing erroring — and then requires "the model is expected to state the path when it
reports what it changed". But `configPath` appeared on exactly one tool's return. A model calling
`set_config` without a prior `get_config` had *written somewhere it could not name*; `preview`
could render a file that was not the user's and hand back rows with nothing to compare them to.
Now: every tool that resolves a path returns `configPath` and `source`. Resolving a path and
reporting which one you resolved are one obligation.

**3. `confirm: true` with no `target` had two readings.** The table marks `target` optional; the
text says restoring takes an explicit one. Ruled an error (`target-required`), returning the
listing — and **deliberately diverging from `/claude-tui-line:revert`, which defaults to
`origin`**. A human typed the slash command and read its name; an ambient tool call was not asked
for, and one boolean should not roll a config back to before this tool existed. The asymmetry is
the argument, so the cheap call stays the one that only looks.

**4. Compare-and-swap had a hole exactly where two agents collide.** `baseRevision` is optional so
a first write needs no ceremony — which covers the caller omitting it and leaves the fresh-machine
case with no CAS at all: two sessions both find no config, both write, second discards first,
silently. Ruled `revision: "absent"` for a missing file, with `set_config` refusing if one now
exists. Create becomes atomic inside the mechanism already there.

**5. `preview` could show a plausible render of a config `set_config` would reject.** §7 makes a
bad config degrade silently rather than fail, and §12.6 tells the model that looking at rows is
how it checks its work — so "the preview looked right" was evidence for an invalid candidate. It
now returns `diagnostics[]` for an inline config. The check has already run; withholding it made
preview quietly weaker than the loop it anchors.

**And one thing that fixed itself.** `set_config` checkpoints before mutating, through §12.2 — so
before tonight it carried `/edit`'s defect exactly: a checkpoint that did not contain the config
it was about to overwrite. Amending §12.2 closed it here without a second edit and without anyone
noticing it was open. That is the one-definition rule paying out rather than merely being
asserted, and it is worth recording as evidence that the rule is load-bearing: the alternative
design, where each command describes its own backup, would have needed this found twice.

### The implementor's scoping question found the gap in my own section (§6.2.1)

`color-down-converted` landed, and the implementor flagged what it had *not* covered: Spectre's
numeric `colorNNN` form, deliberately left out because my example was hex-only and it did not
want to invent the interaction. Exactly the right instinct — and following it up found that the
narrow implementation matched §6.2's prose while the §9.6.1 registry row already said something
broader ("a `colorSystem` that **cannot render it**", a tier comparison). Two authorities, and
this time the *implementation* had been written against the weaker one.

The real gap is that every paragraph of §6.2 discusses `standard`. So the case nobody had
written down is **hex under `256`** — an author who widened the palette one step, which is the
one situation where you would expect the warning to have been thought about. §6.2.1 now states
the rule as a minimum-system table with the comparison made explicit: fires when the literal's
minimum exceeds the resolved system, which is three cases rather than one.

Two smaller rulings inside it:

- The row's own rationale — "approximated to the nearest of the sixteen" — is **false** in the
  new case, where it is approximated to the nearest of 256. That is the digest rule about a
  diagnostic carrying its stated reason as an implicit condition, hit again: broadening a check
  without broadening its message ships a diagnostic that lies to a third of the authors who
  trigger it. The message now names which palette.
- A hex literal landing exactly on a 256 entry still fires under `256`, ruled deliberately.
  Suppressing it means shipping 256 RGB triples nothing here would ever verify, and the warning
  is *correct* regardless: someone who means entry 207 should write `color207`, which says so
  and survives a colour-system change. The diagnostic points at a real improvement rather than a
  phantom defect, which is the only reason a technically-avoidable warning is allowed to stay.

### `setup` walked — the only command that runs today, and it verified the wrong half

Task #12 was marked complete, which is not the same as audited. `setup` is the one command that
works without the CLI, it is the first thing a new user runs, and it is the one that performs the
backup the whole project's safety story rests on. Five findings.

**The one that matters is step 5.** It rendered a preview by running
`${CLAUDE_PLUGIN_DATA}/bin/claude-tui-line` — the variable. But step 4 writes settings.json, which
does *not* interpolate plugin variables, so it must write an **expanded absolute path**, and that
expansion is the only thing in the whole procedure still untested at step 5. The binary was proven
in step 2. So a wrong expansion — a typo, or the literal `${CLAUDE_PLUGIN_DATA}` written through
unexpanded — produces a perfect preview, a success report, and a blank statusline, with nothing
anywhere pointing at the cause. §7.1's class exactly, in the install path: present, plausible,
wrong. Step 5 now reads `statusLine.command` back out of the file and runs *that string*.

`/revert` step 7 already got this right — it prints and runs the command it restored. Two commands
answering the same question different ways, and the weaker one ran on every install.

**The other four:**

- Step 6 told the user their statusline was backed up and that `revert` restores it. True when
  step 3 wrote an `origin`; **false when it wrote a `checkpoint`**, which is the documented case
  where setup runs on a machine that already had one. Bare `revert` then correctly restores the
  older `origin` — not what setup just saved. The default is right; saying nothing about it is
  not. Step 6 now reports the kind and hands over the timestamp.
- Step 1 ran `dotnet --version` in the user's cwd while step 2 builds elsewhere. `global.json`
  makes that two different SDKs, so the check can pass for a toolchain the build never uses and
  the failure then presents as a build error. Now runs in the project directory.
- The sample payload carries `cwd` and `model` only, so items depending on workspace/session
  fields render absent. A correct install reads as a broken one and the user's first act is to
  debug something that works. Now said out loud.
- Ordering (checkpoint before any write) was already correct. Recorded as a clean result.

**And the config-absence marker, found by walking setup into the ledger.** The ledger's config
section said to omit all three fields when no config exists and "note it in `note`" — while citing
`"statusLine": null` as its precedent. It is not that precedent. Three missing fields are
indistinguishable from an entry written before configs were captured at all, and free text is not
something a rollback can branch on: *no config was here* and *this ledger cannot say* arrive as the
same answer and call for opposite actions. Now `configOriginalPath` plus an explicit
`configCopy: null`. Third time in this project that absence needed a distinguished value rather
than a missing key, after §12.6.9's `revision: "absent"` — assume the fourth has the same shape.

That marker then propagated into `/edit` twice, and the second was a defect the first created:

1. Step 2 offers to seed a config when none exists, then step 4 checkpoints. So the checkpoint
   records the *seeded* file, and a failed edit rolls back to a config the user did not have when
   the command started — "no config, defaults apply" becomes unreachable by rollback. Same
   ordering defect I fixed in `/revert` by swapping steps 3 and 4: a write ahead of the
   checkpoint. Now the checkpoint is taken first, and step 7's rollback deletes rather than
   restores when `configCopy` is null.
2. Step 4's guard — "stop if the entry has no `configCopy`" — would then fire on exactly that
   legitimate null. Adding a distinguished value turned a correct check into a false positive one
   edit later, which is the same trap as tonight's `color-down-converted` message: broaden the
   data and the things reading it silently stop meaning what they said. Now it distinguishes
   `null` (looked, found nothing — fine) from missing (never looked — stop).

### `migrate` walked — one defect, one clean result, and a rule written out three times

The last command. It held up better than the others, which is worth saying plainly.

**The defect is step 8.** After writing the config it said to point `statusLine.command` at the
binary "following the ledger procedure". The backup was already taken at step 2 — deliberately,
before this command reads anything — so running the procedure again appends a second entry whose
config capture is *the config you just wrote*. That is a restore point for a state that existed
for one instant and that nobody would ever want back: migrated config, original statusline. The
phrase had two readings and the wrong one produces a plausible, permanent, useless ledger entry.
Now it says the backup is done and cites the write rules instead. Also spelled out that config
must be written before `statusLine` is repointed, since the other order gives the user a second of
built-in defaults they never approved at step 7.

**The clean result is step 6.** It pipes one payload into both the original script and
`--preview`, which would be worthless if `--preview` ignored stdin — two renders compared across
two different inputs, differences and matches both meaningless. §9.3 already rules it: stdin has
data, use it; stdin empty or a TTY, use the fixed synthetic payload **and say so on stderr**. It
even names `/migrate` as the reason. Checked and correct.

**And the third copy.** Chasing my own dangling reference to "the ledger's writing rules" found
that there is no such section — the rules were written out inline in `setup`, `revert` and
`migrate`, three times, in three wordings. Three copies is how two of them drift. `backup-ledger.md`
now has one **Writing `settings.json`** section with the four rules, and the commands cite it
while keeping only the emphasis specific to them: `setup` on expansion, `revert` on the
wholesale-copy temptation that is strongest exactly when a user asks to go back.

That is every command and every doc walked.

### The manifests, and an unset variable that names `/bin`

`plugin.json` and `marketplace.json` had never been walked, and a broken manifest means the plugin
does not install at all — the earliest possible failure. Both are sound: names match across the
two, `source: "./"` is right for a marketplace living in the repo it ships, and the `commands/`
pointer resolves.

The walk did settle one thing worth verifying rather than assuming: **`CLAUDE_PLUGIN_DATA` is
real.** `setup` leans on it entirely, and the whole "works today" claim rests on it existing.
Confirmed from an official Anthropic plugin that both uses it and documents it as the plugin's
persistent data directory, surviving plugin updates. It is much newer than `CLAUDE_PLUGIN_ROOT`
(two files against 112 across the installed marketplaces), which is why it was worth checking.

**And checking it found the sharper problem.** Unset, `"${CLAUDE_PLUGIN_DATA}/bin"` expands to
**`/bin`**. So on any Claude Code that does not set it, step 2 was a release build published into
the system binary directory — failing on permissions, or succeeding under a privileged shell —
after which step 4 writes `"command": "/bin/claude-tui-line"` and step 5 cheerfully confirms it
renders. An unset variable inside a path expansion does not announce itself; it silently names a
different, real, usually worse directory. Now guarded with `:-$HOME/.claude/claude-tui-line`,
resolved once into `$BIN_DIR`, and the fallback is reported to the user rather than used quietly.

Same family as the expansion defect in step 5, and it is the reason that one was worth fixing:
both are the install writing a path nothing downstream ever re-checks.

### §8 walked — a count that reads as a superset, and `auto` ruled two ways

First of the early sections, written before most of what now cites them. Four findings, one live
on the implementor's critical path tonight.

**`size: "auto"` had two contradictory rulings.** §2.2 lists it as a documented synonym for
`fill`; §3.2.1 cited "`auto` resolving to `fill`" as an example of the *silent-acceptance flaw*
the diagnostics work exists to fix. `--check` cannot satisfy both — warn or don't — and it is
being written right now, so this was one edit away from being decided by whichever line the
implementor read first.

Ruled: **`auto` stays legal, as a deprecated alias, and warns** (`deprecated-size-alias`). Not
because deprecation is tidy, but because of what makes this value specifically dangerous — it is
the only one in the vocabulary whose plain-English reading names *a different value that also
exists*. "Auto" sounds like "size to its content", which is `content`, the exact opposite of
taking an equal share of the leftovers. So the author who most needs telling is the one who wrote
`auto` meaning `content`, and their only symptom is a layout slightly off at some widths. The
registry row therefore requires the message to name `content` as the other candidate: told merely
"deprecated", that author re-spells it `fill` and keeps the layout they did not want.

§3.2.1's aside is corrected in place — `case: "title"` is the honest example of a value with no
meaning, and §7.1 rules the two in opposite directions, so citing one as an instance of the other
pointed the fix backwards.

**The count.** Three sites said "all 14 builtins" for the default list. There are **sixteen**
builtins; fourteen are in the default set. Read literally — and "all" invites exactly that — the
default set gains `model-short` and `remote-url`, and `remote-url` shells out to git on every
render, breaking the promise the README makes in as many words. §9.6.2's `default` flag is the
one definition; all three sites now defer to it rather than restating a number.

**Two smaller ones.** §8 said an unknown builtin id "is suppressed silently" and stopped there —
true, and half the rule: `unknown-item-id` is an error at `--check`, which is the half that makes
the silence recoverable rather than permanent. And `layout.chromeReserve` appears in the very
first example config in the spec while the README documents no such key; now documented, with
what raising it actually does.

**One clean result:** the `(SPEC.md §6b)` cross-reference resolves — `SPEC.md` is still in the
repo. Checked because a dangling pointer in the sentence defining per-render config reload would
be worth knowing about.

### §5 walked — the cache had a genuine multi-terminal defect (§5.0.1)

"The hard part", and the first walk tonight to find something that would misbehave at runtime
rather than mislead a reader.

**`paneWidth` was stored in the value cache entry.** The value is keyed by `id` + argv + `cwd`
and deliberately shared by every session on the machine — that is the whole design. The pane
width is a property of one terminal. So two sessions in the same repo at different terminal
widths collide on a record where last-write-wins is *not* correct: each overwrites the other's
width every second, and each spawn is handed the other terminal's width. A `command` item that
adapts its output to the space available then formats for a pane it is not in — present,
plausible, wrong, with nothing in the render suggesting it. Two terminals open on one repo is not
an exotic setup.

What makes it a good find is that **§5 already contains the argument that forbids it.** The
"one file per cache key, not one shared `items.json`" bullet reasons exactly this way about two
sessions read-modify-writing one map. The same reasoning one level down says the value and the
width cannot share a record either, and the section did not follow its own rule to the next
granularity. Generalised in §5.0.1: **data with different sharing scopes must not share a
last-write-wins record.**

Fix: a separate `widths/` store keyed by the cache key **plus `COLUMNS`** — two sessions at the
same terminal width agree, so between those two last-write-wins is correct, which is the test §5
already applies to values. And **written only when the width changes**, which incidentally
repairs a claim §5 made and then contradicted four bullets later: "the steady-state cost of a
custom item is a map lookup, not a fork" was false while every command item rewrote a file every
second. Steady state is now zero writes.

**Two interlocks that were missing.**

- §5's stale-on-failure said a command with no cached value "is suppressed", flatly. §2.11.2 —
  written last night — needs that suppression *marked unavailable*, because a pane holding an
  unavailable item must not collapse. Left flat, the two states merge one layer down and a
  command that is 200 ms slow on one tick silently restructures the statusline: pane collapses,
  neighbours resize, everything reflows, on a timing accident, with no diagnostic. The marker is
  what stops a transient failure being read as a layout instruction.
- "Exit code is always 0 and stdout is always valid" was written absolutely, and §9.4 defines
  exit codes 0/1/2/3. Not actually a contradiction — one is the render path, the other the CLI —
  but nothing said so, and §9.4 is being implemented right now. Scoped explicitly: same binary,
  two callers, and the contract belongs to the caller. §9.1's "the render path is untouched" is
  this same boundary drawn from the other side.

### §4 walked — the section forbidding second registries kept one, and `--check` had no boundary

Four findings, and the first two are both cases of §4 breaking its own rule one paragraph after
stating it.

- **§4 enumerated the builtins in prose, immediately after declaring the registry the only place
  they are enumerated** — and the prose list was wrong: it omitted `remote-url`, which §5.1
  specifies and §9.3 names as one of the canned `ItemContext` fields. It also opened with "the 14
  captured segments", a number that collides by coincidence with §8's default-set size. Rewritten:
  the list stays, demoted explicitly to orientation, with `--items` named as the authority and the
  instruction that if the two disagree the prose is the one that is wrong. Membership in the
  default set is now stated as a **flag on the row, never a count**, and the coincidence of the two
  sizes is called out as a fact about today's rows that nothing may be derived from.

  The same count was restated in three more places. §8's definition, §9.6.2's `default` field, and
  the README all now state the predicate instead. The README's version was the worst of them: it
  said "sixteen ship built in, fourteen in the default set" directly above a table that marks each
  opt-in row — so the sentence added nothing except a second thing to keep true.

- **§4 still recorded `paneWidth` in the cache entry**, which is the design §5.0.1 removed two
  hours earlier. Two sections describing one mechanism, disagreeing about which file it lives in,
  with the implementation not yet written — the reader who gets there first decides. Rewritten to
  cite the widths store, and the new keying turned out to *improve* §4's own claim: the old text
  admitted the value is "correct on all but the first tick after a resize", i.e. wrong for one
  tick. Keying by `COLUMNS` makes a resize a key miss instead, so that tick reports **absent**
  rather than a confidently stale number — and absent is a state a script can branch on.

- **"Nonzero exit ⇒ treated as empty" was the exact behaviour §2.11.2 exists to forbid**, written
  into the section that produces the value. §2.11.2's whole argument is that an item that did not
  answer is not an empty item, because collapsing on a 150 ms timeout makes the line jump once a
  second. Fixed to `absent` vs `unavailable`, with the old wording quoted in place — it is a
  reading someone will arrive at again from §7, which genuinely does treat both as "renders
  nothing". §7 governs what the user sees; §2.11.2 governs what the layout is told.

- **`--check` was said to report a runtime fact, and that exposed a missing ruling.** The `maxLines`
  bullet ended "excess lines are dropped and `--check` reports the cap was hit" — a diagnostic with
  no code in §9.6.1, which per that registry means it does not exist, and which `--check` could
  only produce by executing the user's commands. Nothing in the spec said whether it does.

  Ruled in a new **§9.1.1**: `--check` never executes a `command` item; `--preview` always does.
  Three reasons for the first half, and the first is the one that matters — `/edit`, `/migrate`,
  and §12.6's tools all run `--check` on a config a model has just written, so "validate this
  file" must not mean "run the commands in this file". The other two: `--check`'s answer must be a
  function of the config alone (§9.8 already argues this for width; machine state and wall-clock
  are the same argument, and its exit code is the gate a write is accepted on), and a validation
  must not cost what a render costs. The consequence is a boundary worth having in writing:
  **`--check` cannot report anything about a command's output**, which is why every `command`
  diagnostic in §9.6.1 is a statement about the declaration.

  `--preview` is the deliberate opposite, since §12.3 and §12.4 both put a preview in front of the
  user as the evidence for accepting a write, and evidence assembled by skipping the interesting
  half is not evidence. The `maxLines` notice moved there, on **stderr** — the channel §9.3 already
  uses for "this payload is synthetic", and not stdout, which stays byte-comparable so `/migrate`
  can diff a preview against the original script.

  That ruling then landed on §12.6, which had just made `preview` accept an **inline** config: a
  caller can hand the server a config that was never written to disk and have its commands spawned.
  Added as §12.6.9's fourth ruling — it still runs them, for the same evidence argument, but the
  ordering is now stated because the tool surface invites the opposite guess. `check` is the
  cautious call; `preview` is not, and a caller entitled to preview a config is a caller entitled
  to write it. §12.6.4's look-before-you-leap default only works if the model knows which call is
  which.

### §9.4 walked, and four rulings the implementor was blocked on

**§9.4's own findings — two, both about lists that had gone quietly short.**

- **Tier 1's enum list named six keys and missed three.** `size`, `style`, `align`, `valign`,
  `overflow`, `case` were listed; `split`, `distribute`, and `colorSystem` were not, and all three
  have closed value sets whose failures are silent and consequential. A misspelled `split` turns a
  container into a non-container, so its `children` become a key nothing reads and half the
  statusline disappears. A misspelled `distribute` reverts to greedy sizing — the exact layout the
  author wrote the key to avoid, differing by a row count rather than by an absence.
  `"colorSystem": "24bit"` is the cruellest: it falls back to `standard` and then produces
  `color-down-converted` warnings on the literals the author widened the profile *for*, so the
  diagnostic they receive is correct, unexplainable from where they are standing, and points away
  from the typo.

  The list was also restated in four places — §9.4's severity bullet, §9.4.1's tier 1, the
  "one code across all six" paragraph, and the §9.6.1 registry row. Now stated as a **predicate**:
  any key whose accepted values are a closed set. Same treatment as §4 got an hour earlier, and
  the same failure it prevents — a key added with a closed value set and nobody thinking to touch
  §9.4. The "one code, not one per key" ruling survives and is now argued rather than assumed: a
  per-key code would make adding a key a change to the §9.6 compatibility surface. Added: the
  message must name the accepted set, because a code and a pointer localise the fault and only
  the accepted set repairs it.

- **`--config` and the render path were two rules pointing in opposite directions**, meeting on one
  real command line: a `statusLine.command` of `claude-tui-line --config ~/my.json` whose file is
  later deleted or saved with a trailing comma. §9.2 says never silently fall back; §5 and §9.1 say
  the render path exits 0 and never blocks, because Claude Code runs it once a second and has
  nowhere to show a failure. Both cannot be followed literally and neither may simply lose.

  Falling back to defaults is the false resolution — it satisfies §5's letter and produces §7.1's
  outcome in its purest form. New **§9.2.1**: exit 0, and **render the reason** as one plain row,
  because the statusline is the only output channel that path has. stderr is discarded, the exit
  code is not displayed, and a log file nobody knows about is not a channel.

  The rule that took the most thought is that the test is **whether a config was asserted, not
  whether one was found**. A file at the searched path that exists but does not parse gets the same
  treatment as a named one: writing it asserts a config exactly as naming it does. Defaults are the
  right answer only to the *absence* of an assertion. That row is probably a defect in the render
  path today. Tracked as task #17, deliberately including the `--config` wiring, because half of
  this ships a defect: `--config` parsed on the render path without §9.2.1 means a deleted config
  renders defaults once a second forever.

**Four rulings the implementor held on rather than guessed — every one of them right to hold.**

- **`colorNNN` does not parse, and §6.2.1 was recommending it.** They reflected over Spectre
  0.57.2 rather than trusting a docs search that came back inconclusive: `Style.TryParse("color207")`
  **fails**; `Style.TryParse("207")` resolves to palette entry 207. My table was not using shorthand
  — I believed the prefixed spelling was accepted. The failure mode is the bad one: `color207` does
  not error, it fails to parse into a colour and the item renders uncoloured. So the diagnostic
  whose entire job is to warn about colour going wrong was, in its repair advice, telling authors
  to write a value that silently produces no colour. Table corrected to bare palette indices, the
  finding recorded in place, and README gained the form — it documented names and hex only, so the
  escape hatch the diagnostic points at was undocumented.

- **Named-token tiering: build the sixteen-name constant.** Spectre exposes `R`/`G`/`B` and nothing
  else — no palette index, no name readback — confirmed by reflection over the public surface. So
  there is no computed discriminator between `red` and `grey37`, and the choice was a name list or
  abandoning the row. Signed off, and my earlier ruling was narrower than it read: I forbade
  enumerating the *extended* palette, which is Spectre's, ~240 entries, changes with the library.
  The sixteen are closed by the ANSI standard. What decides it is that **the constant has to exist
  anyway** — §9.6.3 requires `--colors` to print exactly these sixteen and notes that no file in
  `src/` enumerates them today. One constant, two consumers; two lists would let `--colors` print a
  palette the tier check disagrees about.

- **`--check --json` emits JSON at exit 3.** Their working assumption — nothing could be checked,
  so nothing to serialize, so prose on stderr — is right about the diagnostics and wrong about the
  contract. `/edit` and §12.6 call this on configs a model just wrote, which makes unparseable a
  *likely* outcome rather than a remote one, and that is where prose on stdout costs the most. A
  flag that guarantees a format except when something goes wrong does not guarantee a format. Ruled
  into §9.6 with the `{ ok: false, code, path, message }` envelope §9.6.1's second table already
  defines, `config-unreadable` and `usage` added as rows. **`diagnostics` is absent, not `[]`** —
  `[]` means "checked, found nothing", so a consumer testing `length === 0` would call a broken
  config clean. Third instance tonight of absence needing a distinguished value.

- **`--config` on the render path: defer both halves together** — see §9.2.1 above.

Their correction to my characterization is accepted: the code already used `!= TrueColor` rather
than `== Standard`, so hex-under-256 was firing before this round. That was me being wrong about
the code, not the spec being wrong. Suite at 1110/1110.

*Historical note:* two references to `color207` survive earlier in this log, in the §6.2.1 entry.
They are left as written — the log records what was believed at the time — but the advice in them
is wrong and §6.2.1 is the authority.

### §9.7 and §9.8 walked — a source of truth that does not contain the value

§9.8 itself is sound; the two findings are in what it hands off and in what §9.7 assumes.

- **§9.7 names the `.csproj` as the source of truth for a version the `.csproj` does not declare.**
  There is no `<Version>` element in it. MSBuild then supplies `1.0.0` — silently, with no warning,
  indistinguishable from a deliberate choice — while `plugin.json` says `0.1.0`. So `--version`
  as specified would ship reporting a number that is not this project's, which is verbatim the
  outcome §9.7's two-homes ruling exists to prevent. A defaulted version is worse than a stale
  one: stale is a real number from a real release; `1.0.0` here means nothing at all. §9.7 now
  requires the element, and notes that its own drift test fails on this first — which is the test
  working, before there is a release to be confused by.

  Also checked and recorded as a clean result: `marketplace.json` carries no version and must not
  gain one. Duplicating it there would add a copy the drift test does not cover, which is the
  two-registry problem returning under a third name.

- **§9.8's parting instruction had nowhere to land.** It argues that width-dependent facts belong
  to `--preview` and to nothing else, and says to report them "as a note alongside the rows" — but
  §9.6's `--preview --json` shape is `columns`, `usableColumns`, `rows`, with no field for one.
  So the human form of `--preview` would report what it dropped and the JSON form would not, and
  the JSON form is the one a model reads. The information §9.8 had just finished arguing for would
  reach only the caller with no programmatic need for it.

  New **§9.8.1**: render notes become one named channel with two renderings — stderr in the human
  form, `notes[]` in JSON. §4's `maxLines` notice, which I had assigned to stderr by name two hours
  earlier, is folded into it rather than keeping a private arrangement. Three rulings on the shape:
  a note carries **no `code`** (§9.6.1's codes exist so a consumer can branch on a fault, and there
  is no branch to take on "truncated at 109 columns" other than showing a person; codes would grow
  a permanent compatibility surface for no consumer); a note **never** appears in `diagnostics` and
  vice versa (they answer different questions, and merging them makes a config working exactly as
  §2's ladder specifies read as broken); and notes never affect the exit code, restated so the JSON
  form cannot quietly acquire a different rule from the human one.

  This is the fourth time tonight that a command's human form and its `--json` form were specified
  to report different things, and the fourth time the JSON form was the poorer one. Worth naming as
  a pattern rather than fixing case by case: the prose gets written for the human reader, the JSON
  shape gets written as an afterthought two sections away, and the consumer who cannot fall back to
  reading English is the one who loses.

### Open, and honest about it

- **The colour system has tests for none of what makes it a colour system.** Narrowed from
  "a parser and no tests" by reading the source. Two halves, and only one is still open:
  - *Resolved by reading.* §6.1's three literal forms all parse: `ColorResolution.ResolveLiteral`
    delegates to Spectre's `Style.TryParse`, which accepts standard-16 names, 256-palette names,
    and `#rrggbb` alike — parsing was **never** limited to 16, and §6's "widen the palette" was
    always a rendering-profile change, not a parsing one. `Config.cs:384` confirms the profile
    side: `256` → `EightBit`, `truecolor` → `TrueColor`, everything else → `Standard`, wired into
    the console at `Program.cs:58-61`. README widened accordingly.
  - *Still open.* §6.2's **down-conversion** — what a hex literal actually looks like under the
    default `standard` profile — remains unverified. Every one of the 11 `ColorSystemSupport`
    references in the test suite pins `Standard`, and not one of those tests uses a hex or
    256-palette literal, so the down-conversion path is exercised by nothing. Handed to the
    implementor alongside `--colors` (§9), which is the natural place for it: the command has to
    render each colour under the active profile anyway.

  The general point, worth keeping: **a parser delegating to a library may already accept more
  than the project's own documentation claims.** Here the code was ahead of the docs, and reading
  it turned a feature-gap into a documentation-gap.

### §10 walked — the suite's strongest assertions all pass on a blank surface

§10 was eleven bullets written before the CLI existed, so the first job was the six missing
bullets (12–17): `--check` spawns no process while `--preview` does, the four config-assertion
cases on both paths, `--json` parsed at all four exit codes, every recommended colour literal
proven to parse, the version drift test, and notes staying out of both `diagnostics` and the exit
code. Each is one of tonight's rulings given something that can fail.

The find is **§10.1**, and it is about the eleven bullets that were already there. Bullets 2, 3, 4
and 6 are all width assertions, and **every one of them is satisfied by a surface on which every
item resolved to nothing.** Rows still equal in width, still under the cap, still
position-independent; the `fill` sibling still gets the exact remainder; the anchor's measured
content width plus chrome still equals its resolved width, because zero plus chrome is a perfectly
consistent answer. A pane tree rendering entirely empty passes the rectangle invariant flawlessly
— and the rectangle invariant is described in this document as the one assertion that catches
ragged padding, height mismatch and overflow together.

That is §7.1's render-wrong class arriving in the test suite rather than in the output, which
makes it worse than the instances of it fixed earlier tonight: the suite is the instrument that is
supposed to detect the class. The failures this project is most exposed to — a provider returning
nothing because the payload changed shape, an item id quietly unresolved, a cache handing back an
empty string — all move the surface from correct to blank **without moving a single width**.

So every layout test now carries a blank-surface control: same tree, every item forced empty,
asserting the invariants still hold *and* that the two runs are distinguishable. Bullet 3 already
required the rectangle invariant be shown to fail against a deliberately broken compositor, which
is the right instinct pointed at the other half of the problem — **a broken compositor is a
control for the padding the assertion measures; a blank surface is a control for the content the
assertion does not measure.** Passing one says nothing about the other, and the document had only
ever asked for one.

Two smaller repairs, both to bullets that existed:

- **Bullet 8** tested command providers against five real scripts but asserted nothing about the
  resulting state. Empty output, nonzero exit and timeout all render as no text, so an assertion
  phrased against the output cannot tell §4's `absent` from its `unavailable` — while §2.11.2's
  collapse rule reads exactly that distinction. The suite would have scored the
  collapse-on-timeout defect as passing.
- **Bullet 9** covered the value cache and knew nothing about §5.0.1's separate widths store. Added
  the three-render sequence, with the assertion made against the *child's environment* rather than
  the rendered row, because the failure being guarded against is `CLAUDE_TUI_LINE_PANE_WIDTH` being
  present and wrong — which nothing about the row can see.

### §11 walked — the phase list stopped being updated and nothing noticed

Started as two one-line fixes, turned into the section's own walk. **Phase 5 enumerated the CLI
surface as three flags** where §9 specifies five, and the two it omitted (`--colors`, `--version`)
are the two §12's commands need in order to offer the user a choice rather than guess at one.
**Phase 7 was missing entirely**, so the phasing section and the task list disagreed about how many
phases this project has.

Pulling on that produced §11.1. Phases 2, 3 and 4 each enumerated their features and **every one
of those enumerations is stale**: §2.8's `height: "content"`, §2.10's per-edge borders and
`border.collapse`, §2.11's collapse rule, §3.2's hyperlinks, §3.3's compound items, §4.2's argv
placeholders, §5.0.1's widths store and §5.1's probe caching are all specified and **no phase
mentions any of them**. Read literally, Phase 4 is "item registry + command providers, cache, TTL,
timeouts" and is therefore done, while three of those eight sections are unbuilt work sitting
inside its boundary.

The proof it is not load-bearing: **§3.2 shipped anyway.** Hyperlinks were specified, built, tested
and merged with no phase ever assigned, and nothing anywhere noticed. That answers the question of
whether this list is what sequences the work.

So the rule this document already applies to the item registry and to the CLI surface, now applied
to itself — **a phase names a capability boundary and cites the sections inside it; it does not
enumerate features.** Phases 2, 3 and 4 rewritten accordingly: 2 is the single-pane surface
(§2.1–§2.8), 3 is splits and everything that exists only because there is more than one pane
(§2.9's re-measurement, §2.10's border grid, §2.11's collapse), 4 is the whole item layer (§3, §4,
§5 — "how a value gets from a provider to a pane"). The dependency claims underneath were correct
and are untouched; that question — what must exist before what, and why — is the reason the
section is worth keeping at all. What is *outstanding* is the tracker's job, because the tracker is
what work is dispatched from and therefore the only one of the two anybody has a reason to update.

Two smaller things fell out:

- **The eyeball step is §10.1's control run by hand.** "Each phase is wired into the live session
  and eyeballed" has been described here as the only step that caught the last defect; §10.1 now
  says why it keeps being that step. A person notices a blank statusline. A suite whose strongest
  assertions are about width does not.
- **Phase 7 cannot use that acceptance** — an MCP tool emits JSON consumed by a model, and there is
  nothing to look at. Its acceptance is a round trip: a model given only the tool descriptions
  produces a config that `--check` passes and that renders what was asked for. That is the property
  §12.6 needs and the one no unit test states.

### §13 walked — a four-bullet section hiding a live defect

Two bullets were phrased in ways that read as licence rather than as boundaries:

- **"no resize event"** does not mean the terminal is never resized. The process exits and is
  re-run, and a new width arrives as a different `COLUMNS` on the next tick — a case §5.0.1 and §4
  handle carefully and at length. A sentence under a heading reading *out of scope* must not be
  readable as permission to skip work the document does elsewhere.
- **"no input"** does not mean nothing we emit can be clicked; §3.2 emits OSC 8 links exactly so
  the terminal can make them clickable. The real boundary is that the interaction is entirely the
  terminal's — we write a string and never learn that anyone clicked it. Left unsaid, §13 and §3.2
  contradicted each other.

Also added §9.1.1's boundary here, since this is where boundaries belong: no diagnostic can ever
be about a `command` item's *output*, because `--check` never executes one.

**§13.1** is the real work. `Plain.Length` staying the width metric is still right — a wcwidth
table is a real dependency, §2.7's parity baseline is stated in terms of it, and what this thing
renders is overwhelmingly ASCII. What was missing is the consequence, and the consequence is bad:
**a pane containing wide characters draws wider than the compositor believes, and the rectangle
invariant does not notice**, because §10 bullet 3 measures with the same metric the renderer sizes
with. The assertion this document calls the one that catches ragged padding, height mismatch and
overflow together reports success on a row that visibly overruns its border. That is §10 bullet
7's own warning — *both sides can share a wrong constant* — landing in the measuring instrument
rather than in the bash comparison it was written about, and §10.1's shape a second time: the
suite is not wrong about what it measures, it is silent about what it does not.

The limitation is now recorded as a **test asserting the known-wrong behaviour**, citing §13.1. A
stated limitation only survives a refactor if breaking it breaks something: anyone introducing a
width-aware measurer now fails that test and has to come and decide, rather than finding out later
that the parity baseline moved.

**§13.2 — defect 16, found while verifying §13.1 against the source.** `Plain.Length`
approximating width is accepted; `Plain` being *cut* at code-unit boundaries was never decided.
`PaneRenderer.WrapSegment` cuts with `Plain.Substring(i, innerWidth)` and `TruncateSegment` with
`Plain[..contentBudget]`, and neither checks whether the cut lands between a high and a low
surrogate. A non-BMP character straddling the boundary is split into two lone surrogates —
**invalid UTF-16 on its way to stdout**, not a clipped glyph.

The two are independent, and the emoji case proves it: `🎉` is 2 code units and 2 columns, so the
width metric is accidentally correct there and the slice is broken anyway. §2.6's trap list
requires a hard break never land inside an escape sequence; the equivalent sentence was never
written about a character, so the guard exists for one and not the other and nobody ever asked for
the test. Filed as task #20, both paths, since both cut and only the wrap path is usually
remembered.

### §4.2 walked — a security fix that quietly emptied the cache key

Walked ahead of building it (task #5), which is the right order: everything below would otherwise
have been decided by whoever wrote the code first. Four rulings.

**§4.2.3 is the one that matters.** §5 keys the value cache on `id` + hash of the **resolved argv**
+ `cwd`. That is complete for the argv path, because placeholder values are *in* the argv. For
`shell: true` §4.2 deliberately substitutes nothing — the argv is the identical `sh -c '…'` string
every render and the values arrive through the environment — so **the key no longer covers them.**
A script reading `$CLAUDE_TUI_LINE_VAL_MODEL` is cached under a key that ignores the model: switch
models and it reports the old one for up to `ttlSeconds`, silently.

The security fix caused it, and correctly. Moving an input from argv into the environment was
necessary, and it removed that input from a key defined by *which channel* an input travels in
rather than by *what the child can see*. `CLAUDE_TUI_LINE_PANE_WIDTH` was always in the same
position — exported to every `command` item, absent from the key — so a script formatting itself to
the pane width caches one answer and reuses it at every other width, defeating the exact feature the
variable exists to provide.

Ruled: **the key covers every input the child process can see** — resolved argv, `cwd`, every
`CLAUDE_TUI_LINE_VAL_*`, and the pane width — stated as a property so the next input added is
covered by the rule rather than by someone remembering. §5's bullet now says that and cites §4.2.3
rather than restating it. The cost is named too: a resize becomes a cache miss for every `command`
item, which is correct, and if it ever hurts, the lever is exporting the width only to items that
ask for it — **not** dropping it from the key. Naming the right lever matters, because the wrong one
is the one that looks like a performance fix.

Three more:

- **§4.2.1, bare `{}`.** "The same `{}` / `{other-id}` vocabulary §3.2 defines" carries across one
  member that does not survive the trip: `{}` means *this item's own value*, and a `command` item's
  own value is what it prints, which does not exist yet. New error `placeholder-self-reference`.
- **§4.2.1, literal braces.** `jq '{name: .name}'` is an ordinary argv entry that a naive reading
  turns into a placeholder naming nothing — which this section makes an *error*, so a working
  command becomes a config the framework refuses. Guessing is unavailable, since `jq '{a}'` is
  valid jq shorthand *and* a well-formed reference. Grammar ruled: `{{` and `}}` are literal
  braces; `{…}` is a placeholder only if empty or matching the id charset, so the common case
  needs no escaping and the author never has to learn any of this; everything else passes
  through. `'{a}'` errors, correctly — genuinely ambiguous, caught by `--check` before it ships,
  and the diagnostic can name the repair.
- **§4.2.2, an unavailable source is not an empty one.** The empty-placeholder rule covers an item
  that answered with nothing, not one that *did not answer*. Substituting empty for an
  `unavailable` source collapses §4's distinction one section after it was made, and does it at the
  worst point — handing it to a process that cannot tell "no branch" from "git didn't finish in
  150ms" and will act on the first reading. A `command` item whose placeholder names an
  `unavailable` source is itself `unavailable` and is not spawned.

### §3 walked — and the biggest find is a diagnostic that never existed

Five things in §3/§3.1, and one of them generalised into §9.

**§9.4.2 — every key-name typo in every config object is silent.** §9.4.1 covers unknown *values*
of known keys; nothing anywhere covered an unknown *key*, and the deserializer ignores one by
default. `{"item": "context", "colour": "aqua"}` parses cleanly, renders uncoloured, and is
reported by nothing. So do `"ttl"` for `ttlSeconds` and `"maxLines"` for `maxRows`.

Worse than the unknown-value case in one specific way: an unknown value has a known key to attach
a message to, whereas an unknown key makes the *absence of an effect* the only symptom — and
absence is what a user attributes to having misunderstood the feature. And it lands where §12 is
most exposed: `/edit` and the §12.6 tools have a model write JSON and gate the write on `--check`,
and **a plausible-but-wrong key is the likeliest thing a model gets wrong** — likelier than an
unknown enum value, because the enum sets are short and printed while the key vocabulary is long
and adjacent to every other JSON schema the model has seen. The gate was blind to exactly that.

Ruled: `unknown-key`, **warning** (§9.4.1's test — the rest of the config does mean what it says,
and only this key is dead), message naming the nearest known key, because this is the diagnostic
where the gap between identifying a fault and repairing it is widest. Two riders: the known-key
set is **derived from the config types, never listed** — a hand-maintained list fails toward
reporting valid configs as unknown, and a warning that fires on correct input is one users learn
to ignore — and **§12's gate must surface warnings, not only errors**, since a model-written
config never trips this on purpose.

The rest of §3:

- **Zero rows conflated `absent` with `unavailable` at the type level.** "An item resolves to a
  block: zero or more rows; zero rows means suppressed" gives an item that answered with nothing
  and an item that *did not answer* the identical representation — while §4 distinguishes them
  and §2.11.2's collapse rule reads that distinction. The item model erased it before the
  compositor could honour it, which is the worst place for it, since nothing downstream can
  recover what the type does not carry. A resolved item now carries `present` | `absent` |
  `unavailable` in §4's own vocabulary, with `present` + zero rows ruled not constructible.
- **Two struct fields that do not exist and should not.** `Align` was listed as per-item "within
  the pane": `PaneItemJsonConfig` has no `align`, `PaneAssembler` aligns whole rows by the
  *pane's*, so the document advertised a capability a config cannot express — `color207` again,
  where what is recommended silently does nothing. It is also incoherent, since items pack
  several to a row and three items sharing a row cannot each align within the pane. `Enabled` was
  a second mechanism for "don't render this" where "don't place it" already exists, which is what
  §1 forbids. Both removed rather than left; a struct in a spec reads as the set of things that
  work.
- **Packing runs before wrapping, and it was undefined.** "Packing operates on single-row items"
  plus "a multi-row block occupies its own rows" leaves open which an item is when it is
  single-row *until the pane wraps it*. The readings differ visibly: pack-then-wrap flows a full
  row onto continuation rows; wrap-then-pack promotes any long item to a block, so items stop
  sharing a row and the pane's whole shape changes because one value grew. Ruled pack-first — an
  item's block count is a property of what the provider returned, never of the width it was later
  granted — which is also what §2.6's traps already assume in requiring continuation rows to
  carry their style.
- **"Memoized for the process" vs §5.1's cross-render cache** were two lifetimes for one
  mechanism. Now "for this render", with §5.1 named as the design and per-process memoization as
  the floor.

### §2.3 walked — one defect wearing three keys, and a total function that eats diagnostics

§2.3 and §2.3.1 were the last big unwalked sections. Four findings, and the fourth explains the
other three.

- **`distribute` has three values and two of them do not exist.** §2.3 declares
  `greedy | min-rows | even`; §9.4.1's closed-set list said `distribute ("min-rows")`;
  `PaneDistributeParsing.Parse` maps `"min-rows"` and sends everything else to greedy. Three
  authorities, three answers. The sharp end is §2.4, which recommends `distribute: "even"` *by
  name* to a user who wants a layout that holds still — and what they get for writing it is
  greedy, the reflowing layout that sentence exists to steer them away from. The recommendation
  and the failure are the same act. Once `unknown-enum-value` lands as specced, `even` also
  becomes a `--check` **error on a value this document recommends**. Ruled all three into the
  language; `even` divides the remaining extent equally and ignores intrinsic measurement
  entirely, which is the point rather than a shortcut — content-independent widths are what makes
  a layout stop moving. Tasks #22/#23.
- **`min-rows` replaces the fixpoint; it does not run on top of it** — and the spec never said so,
  presenting the fixpoint as unconditional and then presenting min-rows as though it slotted in
  beside it. The code branches correctly on a fact this document did not contain. It has to be a
  branch: `rows_i(w)` is already wrap-aware, so composing the fixpoint afterwards re-measures every
  `content` pane at the width min-rows deliberately granted it, shrinks it to its longest wrapped
  row, and the monotone clamp makes that permanent — **the surface comes out taller than the `T`
  the search proved achievable**, with every pane still individually legal. Stated as the property:
  exactly one width-resolution policy runs per split. Also found the seam gap this implies — §10 requirement 6's
  three fixpoint tests reach the resolver through `measureOverride`, which the min-rows path does
  not take, so none of them can run against it. Task #25, with `minWidth`'s missing upper bound
  (it is `R`, never intrinsic — a `content` pane narrower than its content is exactly what the
  search needs to consider).
- **§2.3.2, keys that are valid, spelled right, and meaningless where they are written.**
  `{"split": "horizontal", "distribute": "min-rows"}` is a legal value of a legal key that nothing
  reads; the resolver returns before the branch. Same for `gutter` on a horizontal split. §9.4.1
  can't see it (key known, value legal) and §9.4.2 can't either (key not unknown) — a third
  silence, and the one with the best alibi, because the config reviews clean. Ruled
  `key-not-applicable` (warning), message names where the key *does* apply, because an author who
  wrote it there has the axis convention backwards. Declined the symmetric fix of giving `gutter`
  a row meaning on horizontal splits: a gutter row is a permanent terminal row spent on nothing at
  `refreshInterval: 1`. Task #24.

- **§9.4.3 — why none of §9.4.1 is implementable as the code is shaped.** The best find of the
  walk, and it came out of asking why a section that has been in the document for weeks reports
  nothing. `PaneAlign.Parse`, `PaneValign.Parse` and `PaneDistributeParsing.Parse` are **total
  functions into their enums**: a `_ =>` arm answers every input, so by the time any caller holds
  the result, the fact that the input was not in the language has been *consumed* — by the one
  function positioned to notice it. `--check` cannot report what it cannot be told, and no care
  inside `--check` recovers it, because the information is gone before `--check` runs.

  `OverflowMode.Parse` in the same codebase returns `OverflowMode?` and answers `null`. Both
  shapes are already here, and **the three keys §9.4.1 singles out as failing silently are exactly
  the three with the total shape.** That is the mechanism, not a coincidence — one defect with
  three instances rather than three defects. Ruled: a closed-set parse reports the unrecognised
  case and the *caller* chooses; the renderer's caller substitutes the default so §7 is untouched
  and no output changes, `--check`'s caller reports the diagnostic. Explicitly rejected the
  inverse — a total parse plus a validator that re-reads the raw strings — as the two-authorities
  defect in its worst form, where you discover the drift via a `--check` that passes a config the
  renderer ignores. Task #22, and it blocks #23: adding `even` to a switch fixes one key on one
  day; changing the shape is what stops the next key arriving pre-broken.

### §2.4–§2.9 walked — the spec walk is complete, and it ended by auditing itself

Last unwalked stretch. Every section of the spec has now been walked.

- **§2.5.1 — three rulings intersect on `CLAUDE_TUI_LINE_PANE_WIDTH` and nobody looked at the
  intersection.** §2.5 exports the pane's inner width to `command` providers; §4.2.3 (mine, two
  sessions ago) put every input the child can see into the cache key; §2.3 resolves sizes by a
  fixpoint of up to three passes whose entire purpose is changing those widths. Compose them and a
  `command` item is **a cache miss on every pass** — the process spawns up to three times per
  render at `refreshInterval: 1`, with a correct statusline and no symptom. It is also genuinely
  circular in a `content` pane, whose width is *defined as* the measurement of its content; the
  monotone clamp does not save that, because it constrains the pane's request and not the script's
  output. Ruled: one spawn per render at the first-pass grant, and a `content` pane's items are
  measured with the variable **unset**. The rule underneath, for whatever pane kind comes next —
  *a pane exports its width only if its width does not depend on what the export returns.* Task #26.
- **§2.4.1 — "the rightmost contributing pane" assumes a surface has one.** A root horizontal split
  does not: its children stack, so each row's rightmost cells come from a different child with its
  own background. Decided once, the backgrounded pane's colour band ends short on exactly the rows
  that carry it. Per-row now. Same edit records that the trim is not a layout step — it removes
  padding from some rows and not others, so §10's rectangle invariant is a property of the composed
  buffer asserted *before* the trim, and a test measuring the emitted line passes only on a surface
  where no row had trailing space. §10.1's problem, one layer down. Task #27.
- **§2.6's vertical marker was never budgeted.** The horizontal case explicitly reserves the
  marker's width; the vertical case said "ends with the marker", and the last surviving row is
  routinely full — it is the row that forced the wrap being truncated. Appending puts it over the
  pane width, which §2.4 rule 1 names as the ugliest failure available. One rule now, applied twice;
  stating it once per axis is how the two got to disagree.
- **§2.8 — §2.6 and the degrade ladder were two authorities on `maxRows`.** §2.6 reads as though
  pane-level `maxRows` clips during layout, which would mean a pane exceeding *its own* budget gets
  rung 4 — the harshest — while a surface exceeding its budget gets rungs 2 and 3 first. Same key,
  same meaning one level up, opposite severity. The ladder owns both budgets; §2.6 describes what
  rung 4 does, not when it fires. Also broke rung 3's tie (equal heights are the *normal* state in a
  vertical split, since §2.4 pads siblings to a common height — leaving it unbroken makes a ladder
  justified by determinism depend on enumeration order), and closed the gap in the subsection
  titled **"Clipping must close the border"**: under three rows a bordered box *cannot* close, so
  the rung written to prevent the "crashed" render produces it. Border suppresses under 3 rows —
  the height-axis twin of `MinUsableWidth` — unless the author declared that `maxRows` themselves.
  Task #29.

- **§13.3 — a section number is a reference, and four of them resolved to nothing.** The find of
  the walk, and it came out of noticing that **§2.9 is cited nine times and does not exist**. That
  prompted checking every `§N.M` against the document's own headings:

  | cited | times | |
  |---|---|---|
  | `§7` | 27 | failure behaviour — `§7.1` was a subsection of nothing |
  | `§2.9` | 9 | the two-pane worked example, unheaded at the tail of §2.8 |
  | `§10.6` | 3 | bullet 6 of §10's list, cited in subsection form |
  | `§4.3` | 1 | derived items |

  This is §9.6.1's own registry rule — "a code that is not in it does not exist" — which the
  document applies to diagnostic codes and had never applied to itself. Not hypothetical: §11
  defines a phase's acceptance as **"Acceptance is §2.9"**, and §9.4's whole severity argument
  turns on "§7 makes the renderer cope."

  The distribution is the part worth keeping. **The most-cited reference in the document is the
  most thoroughly missing**, and that is causal rather than ironic — a reference used constantly is
  one every reader already knows the meaning of, so nobody follows it, so nobody learns it goes
  nowhere. Frequency of citation is *negatively* correlated with the chance anyone checks. The
  single-citation dangle is the one a reader would most likely have caught.

  §7 and §2.9 fixed in place. `§10.6` and §4.3 plus a three-line CI check are task #28 — and the
  check is the point, because all four survived many careful readings: prose citing a section reads
  correctly whether or not the section is there.

- Also corrected: §2.3 restated §2.9's measured anchor width as **43** where §2.9 measures **66**,
  for the same pane in the same config at the same width. §2.9 says outright that its integers are
  *measured, not asserted*. A second copy of a measured output is a hand-maintained duplicate, and
  the copy that goes stale is always the one no test reads.

### §9.4.3 resolved, and the round-trip that resolved it is the finding

`--check`/`--check --json` landed (Program.cs, ConfigCheck.cs; build clean, 1122/1122). Task #22 is
**done** — the implementor had already reshaped all three parsers to `ParseCore → T?` with a public
`IsUnrecognized`, independently arriving at exactly what §9.4.3 rules for, with
`ConfigCheck.CheckPaneEnums` as the second caller. #23 (`even`) is unblocked.

But we spent a full round-trip disagreeing about what the code said, and **both readings were of
real files**: §9.4.3 quotes `Pane.cs` at `8306620`, and the fix lives in an uncommitted working
tree. This document's own two-authorities defect, with the second authority being **time** — and
it is the variant with no possible symptom, because nobody is wrong. The two readings simply do not
refer to the same thing, and neither side can tell from its own evidence. §9.4.3 now pins the
revision it quotes and is kept in past tense rather than deleted; the rule it states governs closed
sets not yet written.

Two rulings the implementor asked for, both now in §9.6:

- **`path` is absent for `code: "usage"`**, not `""`. Same reasoning as `diagnostics` being absent
  rather than `[]` one field over — `""` is not "no path", it is the claim that the path is the
  empty string, and it survives a null check and concatenates into `could not read `. Getting the
  two fields different would be worse than getting either wrong, since a consumer that learned the
  `diagnostics` rule would assume it generalises.
- **No catch-all around `--check` is correct** — an internal exception reported as a clean config is
  §7.1's render-wrong class in the one command that exists to find defects. But the consequence
  needed writing down: the alternative to swallowing is a runtime exit code the registry does not
  define. So **{0,1,2,3} is the contract, not the range**; anything else is a crash in this tool
  rather than a verdict about the config. A caller testing `exit == 0` is right by accident; one
  switching on the four falls through the bottom exactly when it should be loudest.

### §9.3.1 and §9.6.2.1 — two gaps that were blocking `--items`, both mine to fill

The implementor stopped and reported rather than freehanding, which was right: both were missing
spec content that ships to users as documentation, not code questions.

- **§9.3 mandated a fixed synthetic `StatusInput` and never said what is in it.** "A fixed
  non-randomised payload" is not a value; two people implementing that sentence produce two
  fixtures, and this one is shared with `--preview` precisely so the two can't disagree. §9.3.1 now
  gives it as a literal, with four rules that each rule out a fixture someone would otherwise write:
  **every field populated** (real payloads omit `pr` and `vim`, and an item whose example renders
  empty reads as an item that produces nothing — completeness beats realism here); **redundant
  fields must agree** (34% *is* 68k of 200k; nothing enforces it, and an inconsistent fixture
  depicts a state Claude Code cannot produce, which `--preview` then renders faithfully);
  **deliberately away from thresholds** — the implementor's instinct was to sit `context` at ~82% so
  the example exercises the colour ladder, and I went the other way, because this is the resting
  baseline users compare against and `--colors`/`--preview` are where the ladder belongs; and
  **visibly synthetic**, since §9.3's "admit it is invented" goes to stderr, the stream most likely
  to be discarded.
- **`reports` was required on all sixteen items and demonstrated on one.** §9.6.2.1 writes all
  sixteen. The three `Semantic` ones carry a second sentence naming what drives the colour, because
  for those three the colour *is* information: `rate-limits` follows the **higher** of its two
  windows, `engram` is a state rather than a magnitude. Both change what a sensible `thresholds`
  override looks like and were otherwise only learnable by reading the implementation.
- Recorded alongside it: **`example` is rendered, never stored** — the `"⎇ main"` in §9.6.2's shape
  is `BuildDefaultSegment`'s output, not a table entry, so that field needed zero hand-authored
  values. And **a builder disagreeing with a `reports` string is a finding, not a string to
  reword** — rewording converts behavioural drift into documentation.

### §13.3 closed — four dangling references, three different fixes, and a CI check

Task #28 done. `tools/check-citations.sh` compares every cited `§N.M` against the document's own
headings; **68 of 68 resolve**, and it was proven able to fail by injecting a `§99.9` into a copy
before being trusted. Wired into a new `.github/workflows/ci.yml` (the repo had no CI at all),
running ahead of build and test so a broken reference is still reported when the build is red.

Three things worth keeping from closing the four:

- **The remedy was not uniform, which "four dangling references" conceals.** §7 and §2.9 had
  content and no heading → promoted in place. §4.3 had content under the *wrong* heading — derived
  items were introduced inside §3.2's hyperlink example, reading as part of linking rather than as
  one of §9.6.2's four item kinds → promoted to a real §4.3, which is what the single citation was
  already describing.
- **`§10.6` reversed my own earlier ruling.** The first draft of §13.3 said to promote §10's bullets
  to subsections rather than rewrite the citations, on the principle that bullets renumber silently
  while headings are visible when they move. The principle holds; the application was wrong —
  **§10.1 already exists as a heading and is not bullet 1**, so promoting would have given `§10.1`
  two meanings in one document. That converts a dangling reference into an *ambiguous* one, which
  is strictly worse: a dangling reference at least fails when followed. Citations now read "§10
  requirement 6", §10 says so explicitly, and the general rule keeps a precondition — promote when
  the number is free.
- The script itself shipped a bug worth noting: `sed 's/x\+//'` is a GNU extension that BSD sed
  accepts and silently does not apply, so the first run reported all 69 citations dangling. Loud,
  and therefore harmless. The same mistake in the other direction reports clean forever, which is
  why the script now refuses to run if it extracts zero headings.

### ~~Open, and needs Jim~~ RESOLVED: GitHub Actions is billing-blocked

**Resolved by deleting the workflow, not by paying the bill** — see "GitHub Actions removed" near the
end of this document. The reasoning below is preserved as written and the last paragraph of it turned
out to be wrong, which is why it is worth leaving in place: *"deleting it would discard correct work
over a one-minute fix"* weighed the workflow's correctness and not its runtime, and a correct
workflow that never runs is not correct work — it is a claim of coverage. The one thing that reasoning
got exactly right is the sentence before it, about a signal that cannot mean what it appears to mean;
it simply did not notice that the ✗ was that signal.

The CI workflow is correct and has never run. Both jobs on `088a759` were refused before starting:

> The job was not started because recent account payments have failed or your spending limit needs
> to be increased.

So `main` now shows a red ✗ that says nothing about the code — which is the failure class this
document spends its time on, a signal that cannot mean what it appears to mean. It resolves in the
Billing & plans settings, and the workflow will work unchanged once it does; nothing about it is
worth rewriting in the meantime, and deleting it would discard correct work over a one-minute fix.

Branch protection does **not** require status checks, so this does not block pushes — verified by
the fact that the push carrying it succeeded.

In the meantime `./tools/check-citations.sh` runs locally in under a second and is now documented
in the README's contributing section, so the check has value today rather than only after billing
is sorted.

### Audits run tonight that found nothing

Both recorded because a clean result is only worth having if it is written down — otherwise the
next person re-runs it, or worse, assumes it was never checked.

- **Every CLI flag the plugin commands and README invoke is defined in §9.** `--check`, `--json`,
  `--items`, `--colors`, `--preview`, `--columns`, `--config`, `--version` — all present. The only
  other flags in the docs are git's own, inside a config example.
- **§12.6 is fully specified, not merely mandated.** Checked specifically for the gap that blocked
  `--items` twice tonight — a section that requires something to exist without saying what it is.
  §12.6 has a wire contract, an environment ruling, per-tool rows, CAS for concurrent writes, an
  explicit allowlist of files a tool may write, and version reporting. It is implementable as
  written. Worth knowing given it is cited 26 times, which by §13.3's own finding makes it the
  least likely thing in the document to have been checked.

### The two-spec audit — and it was not the defect I went looking for (v2 §14)

`SPEC.md` (v1, 24KB) sits at the repo root beside `SPEC-V2-FRAMEWORK.md`, and `README.md` pointed
contributors only at the latter. That is the shape of every two-authorities defect found tonight,
so I checked whether v1 was stale enough to mislead someone.

**It is not stale.** v2 cites it as live authority in four places — §6b (config re-read every
render), §6 (`Plain.Length` is the width metric), `SPEC.md:353` (an empty surface emits zero
bytes), and Phase 1 as a prerequisite. Archiving it would have broken four working citations.

**The actual finding was the opposite shape.** `SPEC.md` §10 requirement 2 carried the entire
build-and-deploy discipline — one command produces the artifact, identity by hash rather than
mtime, and *writing into `publish/` is a deploy that replaces a program the user is running, so it
requires approval*. `SPEC-V2-FRAMEWORK.md` contained no occurrence of the string `publish/` or
`deploy` at all. A contributor following the README's single pointer would never encounter the
rule, and the reason it has held anyway is that it gets restated by hand in session messages —
which reads like the rule working and is in fact the rule being carried by something that does not
persist.

Relocated to **v2 §14**, moved rather than copied, with §14.4 recording the generalisation:

- *"Is this document stale?"* is the wrong audit question — it only finds authorities that
  disagree. Ask *what does this document say that no reader of the current one will ever see?* A
  superseded rule surfaces the moment two readers compare notes; an unreachable one has no symptom
  at all.
- A rule enforced by repetition is not enforced. The test is whether a competent stranger reading
  only what the README points at arrives at the same behaviour.
- `check-citations.sh` cannot catch this class. It is a closed-world check on references *within*
  one document; this defect is about what is absent from that world entirely.

Also fixed on the way through: `SPEC.md` now carries a status header saying which of its rulings
still stand and that new rules do not go there; the README's Contributing section names all three
documents and their standing; and STATUS.md's own citation of `SPEC.md §10.2` is gone — v1's §10 is
a bare numbered list with no subsections, so `§10.2` was the same §10.N ambiguity §13.3 ruled on,
one document over.

### §12.2 promised three rules and had four — and the fourth is the silent one

Found while applying §14.4's question to the rest of the repo. `SPEC-V2-FRAMEWORK.md` §12.2 read
**"Three rules, none optional:"** above a list of four. The fourth is *"an entry captures every
artifact, not the one its command intends to change"* — the rule whose violation is explicitly
silent: the rollback runs, restores something real, reports success, and leaves the damaged file
untouched. A reader reconciling §12.2 against `docs/backup-ledger.md`, which had a section headed
"The three rules", would have concluded the fourth was an editing error and dropped precisely the
one worth keeping.

No behavioural drift had happened yet — the doc carried the same requirement as procedure step 6.
The two documents were describing the same thing at different ranks, which is how they would have
drifted. Both now state four rules; step 6 cites rule 4 instead of re-arguing it, so the rationale
has one home.

The doc/spec split here is deliberate and stated, unlike §14's: `docs/backup-ledger.md` says in its
own header that it is §12.2 restated as a procedure, and it exists in one file because four command
prompts read it at runtime and four copies would drift. That is the right shape. The count was the
defect, not the split.

### `tools/check-counts.sh` — the second mechanical doc check

Two instances of "a stated count disagrees with the list under it" found by hand (§8's segment
count, then §12.2's) is the threshold at which it stops being a reading problem. The script finds
lead-in sentences that name a count and compares it to the list beneath.

It found two more, and both were real:

- **`SPEC-V2-FRAMEWORK.md` §2.4** said emptiness "applies at two levels, differently" above three
  bullets. Defensible — there really are two levels — but the pane level splits on whether the user
  named a size, and a reader counting bullets against the sentence has no way to see that. Now says
  two levels, three cases, and names the split.
- **`STATUS.md`** said "Two things worth keeping from closing the four" above three bullets, in the
  section documenting `check-citations.sh`. A third bullet was appended and the number was not.

**The tuning was most of the work, and it is the part worth recording.** The first version reported
twelve sites, ten of them noise: it took the *last* numeral before the colon, so `SHA-256` promised
256 items, `§9.6` promised six, and `defects 3–6` promised six. A check with that hit rate gets
ignored, and an ignored check is worse than no check — it occupies the slot a real one would have.
The rules that fixed it: strip inline code, section references, dotted numbers, hyphenated names and
ranges; take the **first** count rather than the last, since later numerals in a sentence are
incidental; require the count to be followed by a word, which drops numerals that end a clause; skip
any lead-in with no list under it at all, which is what keeps arithmetic prose out. Twelve reports
became two, and both were defects.

Proven able to fail before being trusted, the same as `check-citations.sh` — run against a
reconstruction of the §12.2 defect, where it reports `says 3, lists 4` and correctly stays silent
about a neighbouring "Two severities:" list that is right. Wired into CI beside the citation check.

### §12.5 carried the one instruction the procedure doc says is wrong — and Phase 6 is live

The same audit, applied to the command prompts. `commands/revert.md` is 121 lines of rulings;
§12.5 was two paragraphs. That gap is not itself a defect — the spec should hold decisions and the
prompt the procedure — but four decisions were missing from it, and one of them was actively
harmful.

**§12.5 said "it verifies the SHA-256 of what it restores against the ledger."** That is the
collapsed instruction `docs/backup-ledger.md` calls out by name as wrong, in a section written
specifically to correct it: it does not say *which* file, and the obvious reading takes the live
`settings.json` — which is **supposed** to differ at revert time, because claude-tui-line is
installed now and was not when the backup was taken. An implementor building revert from §12.5
alone produces a command that reports "you hand-edited this" and stops **on every single run**.
The escape hatch refusing exactly when it is reached for, and the report would read like the check
working.

The doc had been fixed. The spec section had not, and the spec is what §12.6's MCP `revert` tool
and the CLI would be built from. Task #9 (Phase 6) is in progress right now, which is the only
reason this was worth interrupting the audit for.

Three more decisions promoted into §12.5, all of which existed only inside the command prompt:

- **The restored script has three cases, not one.** §12.5 had "missing → restore the copy". The
  third case — *present, but the hash differs* — is the one with no symptom: the revert succeeds,
  the statusline renders, and it is not the statusline that was backed up. Report and let the user
  choose; never overwrite their edit.
- **An unreadable ledger stops the command.** No reconstructing a statusline from the repo or the
  conversation — a fabricated statusline the user believes is theirs is worse than none. Offer to
  remove the `statusLine` key, which is an honest state.
- **Revert deliberately does not restore `claude-tui-line.json`.** This one was a contradiction
  waiting to happen: §12.2 rule 4 makes every entry capture the config, so restoring it is
  *available*, and a spec-only reader would plausibly do it — destroying layout work the user never
  asked to touch. `/edit` owns config rollback; revert answers "put my old statusline back".

Also tightened: `commands/revert.md` cited "Rule 1" of a file that now has two numbered lists both
starting at 1. It meant the one in "Writing `settings.json`" and now says so. Same ambiguity class
as §10.N, one document over — and unlike section numbers, no script catches "Rule N".

### §12.7 — `setup` had no spec section at all

§12 documented `migrate`, `edit`, `revert` and the MCP server. It did not document `setup` — the
command the README sends every new user to, and the only one that can create the `origin` entry.
Not a thin section: **no section**. Five decisions lived exclusively in `commands/setup.md`, which
the CLI and the MCP server do not read:

- **The SDK check must run in the project directory.** `dotnet --version` reports the SDK selected
  for the *current* directory, and a `global.json` above it can pin another. Check in one place,
  build in another, and a passing toolchain check is for an SDK the build never uses — after which
  the failure gets reported as a build error rather than a toolchain one.
- **An unset path variable names a real directory, and a worse one.** Unset,
  `"${CLAUDE_PLUGIN_DATA}/bin"` expands to `/bin`, making the build a release publish into the
  system binary directory. `settings.json` then points at `/bin/claude-tui-line`, and the preview
  confirms it renders. The `:-` fallback is load-bearing, not padding.
- **Verify by running the value, not the variable.** Read `statusLine.command` back out of
  `settings.json` and run that string verbatim. The build already proved the path; the untested
  thing is *the expansion*, because `settings.json` does not interpolate variables. Test the
  variable and the preview renders perfectly while the install is broken — and §12.5's revert
  already verifies the other way, which is two commands answering one question differently.
- **Say the preview payload is minimal**, or a correct install reads as half-broken and the user's
  first act is debugging something that works.
- **If the backup was a `checkpoint`, say so and give the timestamp.** Bare revert restores the
  older `origin` — correct by design, and not what someone told "your statusline is backed up"
  expects.

Numbered `12.7` and therefore printed after the MCP server despite running first, because §12.6
already owns nine subsections and renumbering to satisfy reading order would break every existing
citation. The section says so in its first line rather than leaving the reader to wonder.

It also states how `setup`'s build destination relates to §14: a third location, deliberately.
§14 governs `publish/`, the deploy target a developer's own live statusline runs, which is why
writing there needs approval; `setup` writes into the plugin's data directory, which belongs to the
installed plugin and cannot collide with a working tree.

### The sweep finished on `migrate` and `edit` — and the ledger doc contradicted a command it governs

Applying §14.4's question to the last two command prompts. Both were thin sections over long
prompts, which is the shape that produced §12.5 and §12.7.

**The contradiction.** `docs/backup-ledger.md` opened with *"Every command that writes
`settings.json` **or** `claude-tui-line.json` follows this. No exceptions, no abbreviations."*
`commands/migrate.md` step 8 said *"Do not run the ledger procedure again here."* Both documents
are handed to the same model in the same invocation, and step 2 tells it to read the ledger doc in
full. Migrate writes two files; read literally, the doc demands a second entry, and that entry
captures the config migrate has *just written* — a permanent restore point (rule 1 forbids removing
it) for a state that existed for one instant and that nobody would ever want back: migrated config,
original `statusLine`.

Migrate's reasoning was right and its framing was wrong. It read as an exception claimed by a
caller against a document that says it has none, which is the weakest possible position for a rule
to be in. The fix was to find the principle that makes it not an exception: **one entry per
invocation, taken before the first write — not one per file.** That is the same ruling as rule 4
("an entry captures every artifact") seen from the other end. An entry holds everything, so nothing
needs a second entry. Now in the doc's scope line, in §12.2 rule 4, and cited rather than argued at
migrate's own call site.

**Four more decisions promoted into §12.3.** The sharpest is the timeout: `ttlSeconds` and
`timeoutMs` default to 30 s and 150 ms, and the user's original statusline was a program that could
take as long as it liked. Every fragment lifted out of it becomes a `command` provider under a
budget it never ran under, and over-budget means killed and rendered as nothing (§7) — a loss that
looks identical to never having migrated the element. Also: colour preserved *by kind* (ANSI names
stay theme-mapped, specific shades become 256/hex and need `colorSystem`); write to the path §5's
search order resolves to, never the default by assumption, or the renderer reads the file you did
not write while every step reports success; and write the config *before* repointing `statusLine`,
since the binary starts running once a second the instant that key changes.

**§12.4 said nothing about what recovery is.** `commands/edit.md` carries the whole of it. Promoted:
a rollback restores the entry's `configCopy` over the config, **not** its `statusLine` — restoring
a key `/edit` never touched changes nothing, leaves the broken config in place, and looks exactly
like a fix. A `configCopy` of `null` rolls back by *deleting* the file. Rule 4 is verified at the
call site and the command stops if the fields are missing, because nothing inside the procedure can
answer whether what it saved covers what its caller is about to change. Seeding a config is a write,
so the checkpoint precedes it — otherwise the rollback target is the seeded file and "no config,
defaults apply" stops being reachable. And an edit is verified by previewing at 80 and 60 columns
as well as the terminal's width, because a passing `--check` proves nothing about layout.

§12.2 also gained the ledger's encoding of absence — `configOriginalPath` present with
`configCopy: null` — which lived only in the procedure doc. *Nothing was here* and *this ledger
cannot say* are opposite facts calling for opposite recoveries (delete the file, or leave it
alone), and three omitted fields cannot distinguish them from an entry written before configs were
captured at all. Third instance in this project of absence needing a distinguished value rather
than a missing key, after `"statusLine": null` and §12.6.9's `revision: "absent"`. Assume the
fourth.

That closes the sweep: every document in the repo has now been read against the question *what does
this say that no reader of the current one will ever see?*

### `--items` came back with four blockers, and the spec was wrong on two of them

The implementor built `--items --json` (1129/1129 green) and stopped on four things rather than
guess. Two were spec defects, one was a genuine gap, one was an unconfirmed assumption. Ruling on
all four is what unblocked Phase 5's remaining flags.

**The glyph that was never there.** §9.6.2's shape showed `"example": "⎇ main"` for `git-branch`,
framed as real `BuildDefaultSegment` output. `BuildGitBranch` is `SingleColor("green", branch)` and
always has been. **CAPTURE.md line 60 settles which side was wrong** — the bash statusline renders
segment 2 as a bare branch name in green — so the code was right and the glyph existed only in this
document.

It existed in four places by then: §4.3's extract-ordering example, §9.6.2's shape, §9.6.2.1's
description of that shape, and §9.6.2.1's argument for why `example` replaced "default format",
which used the glyph as its *proof* that a builder emits decoration no format string could express.
That last one is the finding. **An illustration invented to explain one rule was later cited as
evidence for a different rule**, and nothing by then marked it as invented. The argument survives —
`worktree` really does emit `worktree:NAME(BRANCH)` — but it survived by luck, resting on a false
instance. Neither mechanical check can catch this: both compare documents against themselves, and
this was a claim about the code. An example naming a specific behaviour is an assertion about the
implementation and ages exactly like one.

Worth noting §9.6.2.1's own rule is what produced the find: *"if the implementation disagrees with
one of these strings, that is a finding, not a string to quietly correct."* It paid for itself
inside one session, and it paid out against the spec rather than the code.

**A fixture that guaranteed one item could never render.** §9.3.1's literal sets
`output_style.name: "default"`, and `BuildOutputStyle` deliberately suppresses any style name equal
to `"default"` — correctly, since it is noise on a statusline. Together they meant `--items` would
report `output-style` with an empty example permanently, and a user would correctly conclude the
item produces nothing. That directly contradicts the fixture's own rule 1: *whatever an item needs
in order to render, the fixture has.*

`"default"` is the **realistic** value, which is exactly how it got written — "what would a real
payload hold here?" is the right question fifteen times and the wrong one once. The rule is not
populate every field; it is **populate every field with a value that survives the renderer**, and
the only fields where those differ are the ones with suppression logic behind them, which is
invisible from the payload side. Now `"Explanatory"`, with a paragraph forbidding the revert.

The same audit caught a milder version two lines down: the Engram canned value read "whatever shape
makes `engram` render *present*" — a constraint stated as an outcome, leaving the value to whoever
implements it. Pinned to the implementor's own choice (3 memories, `◉ recalled`), which converts an
accident into a decision.

**Bare `--items` had no specified form.** §9.6 promises every one of the four flags a non-JSON
default and demonstrates one only for `--preview`. The implementor left the CLI unwired rather than
invent a layout — right call. New §9.6.2.2 rules it: a three-column table that is a *view of*
`ItemsCommand.Build()`'s result rather than a second registry walk; groups labelled by what
`default` means rather than by its name, since the person reading plain text is the one who does
not know yet; no key lists, because a truncated §4.1 schema reads as complete; `Plain` only, never
ANSI, even on a TTY, because colour-when-interactive is two surfaces that drift. And the ruling
that keeps it from calcifying: **the plain form is a convenience view and the JSON is the
contract** — columns may come and go without that being a compatibility break. Every
human-readable output nobody labels that way eventually becomes a second frozen surface.

**`version` confirmed** as the same string `--version` prints, through the one
`AssemblyVersionInfo` accessor the implementor extracted. A consumer comparing the two must never
see two answers — §9.7's drift test premise, one surface lower.

### Then the same defect happened twice more, inside the fix for it

Having written that an invented example ages like an implementation assertion, I extracted every
string literal `SegmentBuilder` actually emits and swept the spec against it. Two of the
disagreements it found were **mine, from the previous twenty minutes**:

- §9.3.1's newly-pinned Engram value said it renders as `◉ recalled`. `BuildEngram` renders
  `engram:3 ◉ recalled` — `◉ recalled` is the `Verb` going *in*, not the segment coming out.
- §9.6.2.2's brand-new `--items` table showed `directory` as `~/code/acme-web`. `BuildDirectory`
  is `Basename(cwd)`, so it renders `acme-web`.

Everything older was clean — `ctx:62% (125k/200k)`, `5h:30% / 7d:85%`, `effort:high`, `PR #42`,
`worktree:NAME(BRANCH)` all match the builders exactly. So this is not a decay problem, and it is
not carelessness either. **Writing an illustrative value is frictionless; verifying one means
reading a builder.** Every example therefore defaults to invented unless something forces the
check, which is why the failure reproduced immediately in the hands of the person who had just
finished describing it.

§9.3.1 now separates the two registers explicitly: `Facts` and `Verb` are the fixture and are
normative; `engram:3 ◉ recalled` is a description of what the builder does with them, and **if it
ever disagrees, the builder is the fact and the clause is the finding.**

**The mitigation is mechanical and now specified.** `--items`' `example` field is
`BuildDefaultSegment` run against §9.3.1's fixture — so once the flag is wired, a check that runs
`--items --json` and compares this document's example values against it is a document-versus-*code*
check. That is precisely the class `check-citations.sh` and `check-counts.sh` cannot reach, both
being closed-world. Third mechanical check, real target, and the case for it is three failures in
one session rather than a hypothetical.

### The third check exists, and its first run was a false positive

`tools/check-examples.sh` is built and wired into CI. It is the first check here that runs the
code: `--items --json`, then two exact rules over tracked markdown — a fenced `"example": "…"` must
be a value some item renders, and a row inside a block that reproduces `--items` plain output must
carry that item's live example. The block has to identify itself by its own `Item kinds:` pointer
line rather than by resembling a table, because elsewhere the document legitimately shows items with
a `format` or `extract` applied and those are *not* the default render. §4.3's
`worktree:api(feature/ABC-123)` is the case that would otherwise have been reported as a defect on
day one.

Proven to fail before being trusted: against a fixture carrying a stale `⎇ main`, a stale
`~/code/acme-web`, and an Engram string missing its verb glyph, it reports all three and exits 1.
With no binary, or with an empty item list, it exits 2 — never a clean report it did not earn.

**Reviewing it caught a bug in it.** `dotnet run` writes restore and build chatter to *stdout*, not
stderr, so the fallback path would have handed `jq` MSBuild output with the JSON glued to it. Local
testing used a stub binary and never went near that path. Fixed by quieting the build and keeping
everything from the first line that opens the object — deliberately not `tail -1`, which works only
while the serializer emits one line and would break silently-to-the-reader the day anyone sets
`WriteIndented`.

**Still unverified: the `dotnet run` path has never actually run.** No binary has been built in this
tree (the implementor holds uncommitted work in `src/` and building would race it), and CI has never
executed at all — Actions billing is blocked, which is why `main` shows a meaningless red ✗. So the
extraction logic is tested against both single-line and pretty-printed JSON with chatter prepended,
but the real invocation is not. It fails loudly (exit 2) rather than passing vacuously if it is
wrong, which is the property worth having until someone can run it.

**Its first run against the real tree flagged this file, and the flag was wrong.** The passage above
recording that `"example": "⎇ main"` had been removed is a sentence *about* a retired value, not a
claim that it renders. Correct by the letter, wrong by the point — and a check that forbids a
retrospective from quoting the value it is retiring makes the project unable to describe its own
defects, which is how a check gets switched off. The distinction that survived is that **a fenced
block asserts and prose discusses**, so rule 1 is fenced-only. The original defect lived in a fenced
shape, so the narrowing gave up nothing.

That the first execution produced a false positive rather than a finding is the more useful outcome.
The three real instances had already been fixed by hand; what was untested was whether the guard
could sit in CI without crying wolf. It could not, until it was narrowed. **The bar for a check is
not "finds things" but "is worth reading every time"** — `check-counts.sh` says so in its own header,
and this is the second check to have to earn it.

Closes task #30. All three doc checks are now in CI, and the closed-world gap named in §13.3 is
closed for the sixteen builtins' default renders — and only for those. Every other worked example in
the specification remains an unverified assertion about the implementation.

### Getting ahead of `--colors` instead of reviewing it afterwards

`--items` came back with four blockers, two of which were spec defects. Rather than wait for the
same thing on `--colors`, §9.6.3 got the audit first — and it had the identical hole §9.6.2.2 was
written to fill, plus one consequence sharper than that case. §9.6.3.1 is the result.

**Bare `--colors` prints ANSI, and it is the deliberate exception to §9.6.2.2's plain-only rule.**
§2 requires each swatch rendered in its own colour; §9.6.2.2 says bare `--items` is never styled,
even on a TTY. Both correct, and they collide on the same implementer within days — with the
absolute-sounding rule being the one most recently implemented. The principle that makes both
follow: *is the styling the value, or a coat of paint on the value?* An item's example survives
stripping; a swatch stripped of its colour is nineteen rows reading `olive`, `teal`, `fuchsia`,
which is the guessing the command exists to end.

Two more found by reading the code against the section:

- **The curated list already exists** as `ColorResolution.StandardColorNames`, serving §6.2.1.
  §9.6.3 said it "exists nowhere today" — stale. `--colors` is its second consumer, not its author.
- **The round-trip test proves less than the section claims**, exactly on the three entries that
  are not colours. `ResolveLiteral` returns `style.Foreground`, non-nullable, so "non-null result"
  silently degrades to "did the parse succeed" — right for the sixteen, vacuous for `default`,
  `dim` and `bold`, which parse as decorations yielding `Color.Default`. Assertion split, and the
  count of the three pinned so a real colour cannot join them via a rename.

### The renderer writes to stderr for two commands that never read it

§7: render notes go to stderr in the human form so "stdout stays byte-comparable either way, **so
`/migrate` can diff a preview against the original script's output**." That split was designed for
`/migrate` and `/edit`. Neither had ever been told. Both captured stdout alone.

The cost is not cosmetic. A dropped pane and a `maxLines` cap each arrive as nothing but a token
missing from the diff, and the obvious response — re-map the element — is wrong for both. One needs
the width reported to the user, the other needs the cap raised. The note is the only thing that says
which. `/edit` now captures on both the "before" and "after" previews and diffs them, because that
comparison is the sole difference between "my edit dropped a pane" and "a pane was already being
dropped at 60 columns" — and `--check` reports neither, since neither is a config error.

Same class as everything else this week: **what does this say that no reader of the current document
will ever see?** The answer was in §7, one line, describing a benefit to a file that never got the
memo.

### A resolving citation can still point at the wrong section

§9.6.3 cited §12.2 for the colour ruling. §12.2 is the ledger; the ruling is §12.3's.
`check-citations.sh` passed the whole time — it proves a section exists, never that it says what the
citing sentence claims.

Measured before proposing a fourth check: only **15 of 630 citations** carry a quoted phrase that
could be verified mechanically, and several of those quotes are deliberate paraphrase a checker
would flag wrongly. A check covering a fortieth of the surface while crying wolf on part of it is
below the bar `check-counts.sh` sets in its own header. Left manual, and written into §13.3 so the
next person knows it was a decision rather than an oversight.

**Then it happened again, minutes later, in my own new text.** §9.6.3.1 got a sentence citing
"§13.1's rule" about hard-coded counts; §13.1 is *What `Plain.Length` costs, stated* and has no such
rule. Same session, immediately after writing the warning above, while looking for exactly this.
So the mitigation is not care — it is a habit: **`grep` the cited heading and read its title.**
Five seconds, no judgement, and it catches the common form, which is not a subtly misread section
but a wholly unrelated one whose number was close to hand. The fix was to *delete* the citation
rather than repoint it: no section states that rule, and the pointer was there for borrowed
authority rather than for anything a reader would follow.

### The `--colors` round-trip assertion was scoped wrong

§9.6.3.1 said `default`, `dim` and `bold` are "exactly the three entries that resolve to
`Color.Default`". True of the nineteen rows `--colors` prints; false of Spectre's parser, which
accepts every decoration keyword it has (`italic`, `underline`, `invert`, `conceal`,
`strikethrough`, the blinks) and should resolve each to the same value for the same reason.

An implementer testing "everything that parses" rather than iterating the nineteen rows gets a
failure that looks like a defect in the ruling and is not. Sent to the implementor before they
pinned the count, along with the confirmation that `StandardColorNames` is exactly the sixteen ANSI
names — so nineteen is `Count + 3` and not a literal, in the test or in the prose.

The implementor had already gone further than asked on the half of this that *was* right: rather
than trust §9.6.3.1's reasoning about `Style.Foreground` being non-nullable — which the section
itself flagged as read off a cast rather than off the library — they built a throwaway console app
against the pinned Spectre 0.57.2 and ran it. `Style` is a value type, `Foreground` reflects as a
non-nullable `Color`, and `{default, dim, bold}` empirically resolve to `Color.Default` while all
sixteen names do not. The section's "confirm this before relying on it" line is now discharged, and
by evidence rather than by agreement.

### The bare form of a command is unspecified until someone rules on it — three for three

`--items`, `--colors`, and `--preview` each specified a JSON envelope and left the bare form to
whoever implemented it first. Three sections now exist because of that (§9.6.2.2, §9.6.3.1,
§9.3.2), and the third was caught only because the second had just been written.

`--preview` was the sharp one, and I created its risk myself. Having ruled that bare `--colors`
auto-detects and loses its colour under a pipe, I sent that to the implementor as a ruling rather
than as the output of a test — and they were an hour from applying it, correctly and by analogy, to
`--preview`, where it is wrong.

The test §9.6.3.1 applies without naming: **a form may degrade only while some other form still
carries the whole payload.**

- `--colors`: `--json` carries the names, which are the whole payload. Degrade.
- `--preview`: §9.8.1 pins `rows[]` as `{text, width}` — plain, deliberately, because `text` is the
  diffable form. Degrade the bare form too and *no* form carries styling to a non-terminal caller.
  Which is every caller that matters: `/edit`, `/migrate`, and the MCP tools all capture through a
  harness. "Colour it by value" is among the most common `/edit` requests.

Ruled: `--preview` uses **the render path's console configuration itself**, not a matching one —
§1's rule, and it disposes of `NO_COLOR` and everything like it without the section needing to know
what any of them do. `migrate.md` step 6 had been assuming escapes since it was written, which is
the tell that the behaviour needed a ruling.

Then the same test, applied a third time, said *unstyled* for the MCP `preview` tool (§12.6.10) —
its consumer relays to a chat surface where escapes are noise in the middle of a sentence, so
nothing is lost that the channel could have carried. One principle, three commands, three different
answers, no exceptions claimed.

### `preview` over MCP could not report a dropped pane at all

§12.6.9 argues for four paragraphs that the tool must return `diagnostics[]`, because §7 makes a
bad config render silently degraded and "the preview looked right" is otherwise evidence for a
config `set_config` will reject. Every word applies to render notes; none of it was applied to
them. §9.8.1's separation rule — a note never appears in `diagnostics` — meant the information was
not degraded but absent.

`notes[]` added, **per render rather than per response**: §12.6.3 renders at 80 and 60 by default,
so a response-level array cannot say which width dropped the pane, which is the note's whole
content. Diagnostics are width-independent and stay at the response level. A `maxLines` cap is
width-independent and therefore appears in every render's list — the same three copies the CLI
already prints across three widths, and one finding rather than three.

### The wrong-citation habit paid on first use

§13.3 now prescribes reading the *title* of a cited heading, not just confirming the number
resolves. Applied once, it found two wrong citations in §9.6.3.1 — both `§2`, both for things §9's
own bullet list says outright, neither newly written, both passing `check-citations.sh` the whole
time. Cheapness is the property: it costs nothing, so it gets done on citations nobody suspects,
and an unsuspected citation is the only kind this hides in.

### The README's item table was the one item list nothing checked

§9 forbids an item list embedded in a skill or command's prose, and the audit that looked for
violations found the command prompts clean — 1–3 mentions each, all translation examples, and both
`migrate.md` and `edit.md` say in as many words not to copy a list out of the README. What the
audit found instead was the README's own table: sixteen ids with descriptions, asserting both the
membership of the set and which two of them are opt-in.

**It is not the forbidden second registry, and deleting it would be the wrong fix.** Nothing
automated reads it; it is what a person reads before deciding to build, and a README that cannot
say what ships is worse than one that can. But "not forbidden" is not "checked" — the table made
two claims the binary knows the answer to, and nothing compared them. Rule C in
`check-examples.sh` now does: id-set equality in both directions, plus each row's `(opt-in)` marker
against `default: false`. The table is marked with an HTML comment because a prose table has no
in-band string to anchor on the way `--items` plain output does, and the README's other tables must
stay unscanned.

This costs a README row per new item. That is not §1's zero-edit promise breaking — §1 exempts
whatever is genuinely unique about the new thing, and one line saying what it reports is exactly
that. The check does not write the row; it refuses to let anyone not notice.

**The negative test caught a live bug in the check itself.** The first draft read the flag with
`jq '.default // true'`. jq's `//` fires on `false` as well as `null`, so every opt-in item read
back as a default one and the opt-in half of the rule was dead — passing on a correct README and
passing exactly as hard on a wrong one. It surfaced only because the drift cases were driven in all
four directions, and two of them failed to fail. This is §10.1's rule again: a check is not
verified by watching it pass.

### `check-examples.sh`'s `dotnet run` path had never once executed

Reported by the implementor, who ran it with `CLAUDE_TUI_LINE_BIN` unset for the first time. SDK
10.0.301's `dotnet run` does not claim `--nologo`, so it was forwarded past `--` into the app's own
argv at every position tried. The app answered `unrecognized argument: '--nologo'` — as a valid
JSON error object, which is why this presented as `--items --json was not the expected shape`
rather than as anything resembling a build failure. `-v quiet` alone suppresses the banner; the
flag is gone. Every previous run of this check had gone through the stub.

### The note channel had no producer, and the feature its example described was never built

The implementor went to reuse the mechanism §9.8.1 points at — §4's `maxLines` truncation notice —
and found there is no mechanism, because there is no `maxLines`. `CommandProvider` takes
`stdout.Split('\n', 2)[0]`, the first line unconditionally, and no such key exists in any config
type. §4 has described multi-line command output with a cap, in the present tense, for as long as
it has existed. Nothing caught it because the document was entirely self-consistent about it: §4
described the feature, §9.8.1 cited §4, §12.4 told a reader what to do about the note, and every
citation resolved.

The other case is worse in a quieter way. Pane-dropping and segment truncation *do* happen —
`SizeResolver.AllocateWithDrop` and `PaneRenderer.TruncateSegment` — and both are silent. So the
`"pane 2 dropped: no width remained at 109 columns"` in §9.6's own JSON example had no code that
could emit it either.

**The ruling that matters is that `--preview` may not ship with `notes[]` stubbed empty** (§9.8.2).
An empty array is not a partial implementation of this channel; it is the channel's failure mode
with a success message on it. `notes: []` reads as "nothing was dropped", §12.3 and §12.4 both tell
a reader to treat that as load-bearing, and empty-because-unbuilt is indistinguishable at the
consumer from empty-because-nothing-happened — with the second being far more often true, so the
wrong reading is the one that always wins. Drop and truncation instrumentation is therefore in
scope for `--preview` itself (#32); `maxLines` arrives with its own feature (#31).

The signal leaves through a collector passed into `SizeResolver` and `PaneRenderer`, **never
nullable**. A sink the render path passes `null` to is two paths again — instrumented for
`--preview`, uninstrumented for what actually ships — which is Defect 15's shape a second time, in
a second place, six sections apart. A real collector thrown away costs one allocation per render
and buys one path.

### Is `maxLines` a pattern or a singleton? Audited: singleton

`maxLines` was found by accident — the implementor went to reuse a mechanism and there wasn't one.
Accident is not a strategy, so the mechanical form of the same question got run: **every identifier
the spec names should exist in the code.**

Two passes. Keys appearing as `"key":` inside fenced JSON blocks — 94 of them, three absent
(`notes`, `usableColumns`, `removed`). Then backticked lower-camel identifiers in *prose*, which is
the form `maxLines` actually took and which the first pass cannot see — 177 of them, 32 absent.

Every absence triages to one of four legitimate kinds:

- **A vocabulary we correctly do not own** — `italic`, `underline`, `conceal`, `invert`,
  `strikethrough` are Spectre's decoration keywords, and `gold1`, `grey37`, `hotpink`, `orange3`,
  `steelblue1`, `color141`, `colorNNN` are its palette. §9.6.3 already rules that the accepted
  colour set is not ours and is not finite, so finding these missing is that ruling holding.
- **Prompt-owned artifacts** — `checkpoint`, `revision`, `baseRevision`, `configCopy`,
  `configOriginalPath`, `scriptOriginalPath`, `restored` are ledger fields written by the command
  prompts, and `migrate`, `revert`, `setup` are the commands. No C# should know any of them.
- **Tracked pending features** — `outline` is §2.10's per-edge borders (#8); `cliVersion` and
  `unavailable` are §12.6's MCP surface (Phase 7, #10); `notes` and `usableColumns` are
  `--preview`'s own shape (#32).
- **Not a framework key at all** — `removed` is a key in a *user's* colour map in an example
  config.

So `maxLines` (#31) is the only untracked one, and this class is a singleton rather than a pattern.
That is the useful half of the result: without it, the next person to notice a stale-looking
sentence has to re-run this to find out whether they are pulling one thread or unravelling a sweater.

**Deliberately not made a CI check.** The prose pass needs roughly thirty allowlist entries to read
clean, and a check carrying an allowlist that size is one nobody maintains and everybody switches
off — the failure mode `check-examples.sh`'s own header argues against, where drawing a class one
notch too wide costs the check itself. The fenced-key pass is nearly exact and worth revisiting
**once Phase 5 and Phase 7 land**, because at that point its expected output is genuinely empty and
an allowlist stops being needed. Recorded as the condition rather than the intention.

### The citation check had been reading one file and reporting as though it read the project

`check-citations.sh` printed "all 81 cited sections resolve" on every run since #28 closed. That
sentence sounds like a claim about the documentation; it was a claim about
`SPEC-V2-FRAMEWORK.md`, which was the only file it ever opened. Same shape as the two other
findings this session and as defect 15: **an authority that does not cover what everyone assumes
it covers.** Nobody misread the code — the report was read instead of the code, and the report was
worded for the wider claim.

Widened to every tracked markdown file. Headings still come from the spec alone, because the spec
is the only place a section is *defined*; what changed is who may cite one. It now reports
`all 89 cited sections resolve (9 files)` — the file count is in the line precisely so the
next version of this failure is visible from the output.

The unread files were the expensive ones. `commands/*.md` are followed by an LLM at runtime, so
`§9.8.1` written where `§9.8` was meant sends it to read the wrong rule during somebody's real
migration, not during a review where a wrong number is cheap. Those came back clean; STATUS.md
did not.

**Five dangling references, all in STATUS.md, all `§10.6` or `§10.2`.** §10 has exactly one
subsection heading. Task #28 closed `§10.6` by *rewriting the spec's citations* to "§10
requirement 6" rather than by promoting §10's bullets to subsections — and STATUS.md's own copies
of the number were never rewritten with them, because nothing was reading STATUS.md. Two
(`:338`, `:1514`) were live references pointing at a section deliberately never created, and are
now "§10 requirement 6". Three (`:1609`, `:1689`, `:1768`) are this document discussing the
dangling numbers themselves, and are now backticked — the accommodation the script's own header
already describes for §13.3, and the same one `check-examples.sh` rule A makes for STATUS.md
quoting a retired value.

`SPEC.md` stays excluded, deliberately: it is superseded v1 under a different numbering scheme, so
checking it would report dozens of references that are correct in their own document, and a
permanently red check is a check nobody runs.

### `tools/check-docs.sh` — one thing to run, short enough not to pipe

The habit while working was `./tools/check-counts.sh | tail -2 && git commit …`. A bash pipeline's
exit status is its **last** command's, `tail` always succeeds, and so commit `576675b` went out
over a real failure ("STATUS.md:2292: says 35, lists 4"). That is exactly the defect
`check-examples.sh`'s header warns about — a check reporting clean because nothing read its answer
— committed by the person who wrote the warning.

The durable fix is not remembering `set -o pipefail`. It is having one command whose whole output
is two lines, so there is no reason to pipe it anywhere. `check-docs.sh` runs check-citations and
check-counts, runs **all** of them even after one fails so a single pass reports every
disagreement, and exits nonzero if any did.

`check-examples.sh` is deliberately *not* in it: that one needs a built binary and lives in CI's
`build` job after the tests for that reason. Folding it in would mean this script either fails on a
machine mid-build or — far worse — learns to skip itself when it cannot find a binary. A check that
can silently downgrade to a pass is the thing all three of these exist to prevent.

CI was left alone. `.github/workflows/ci.yml` invokes the three checks as separate steps and
already checks each exit status correctly; the defect was in the local invocation habit, not in the
pipeline.

### `maxLines` ruled (§4.0.1): a fairness bound, opt-in, default no cap

#31 was written down as "build the cap §4 describes". Reading §4 to build it, the stated rationale
— "so a runaway script cannot flood the surface" — turns out to be a job the cap cannot do and
that something else already does.

**The surface is bounded by the pane, not by the item.** A `fill`, percent, or fixed pane resolves
its rows without reference to its content, so a script emitting ten thousand lines costs exactly
the rows that pane was going to spend. §2.6 already owns what happens at the boundary and calls
itself authoritative. Adding a per-item truncation on the same axis is **defect 15's shape** — two
mechanisms removing rows for unrelated reasons with nothing reconciling them — which is the defect
class this project keeps paying for.

The single case where the flooding argument holds is `height: "content"` (§2.8), whose height *is*
its content, so the bound that would contain a runaway is derived from the runaway. A per-item cap
does not close that either: three items capped at 4 still grow the pane to 12. It is a **pane**
maximum and belongs to #7/#29.

**What survives is a different feature with the same name.** Items share a pane's rows (§3.1), so
a forty-line item does not overflow the surface — it evicts its siblings. Real, per-item, and
unaddressed anywhere else. That is now `maxLines`'s stated job.

**The default `4` is overturned.** It was written into prose and never measured, and worse, a
default cap is silent truncation on the render path — the render path has no note channel at all,
since §9.8.1's notes belong to `--preview`. A user with a legitimate five-line item sees four rows
forever, and the only explanation is reachable exclusively by someone who already suspected it.
That is §9.8.2's defect from the other side: **there the channel had no producer; here the producer
would have had no channel.** With no default, every `maxLines` note names a number the user typed,
which is the only reason the note is actionable.

**Found in the runtime prompt, which is where it cost most.** `commands/migrate.md` told the LLM a
tier-2 item is "being cut to 4 lines by default (§7)" and to advise the user to raise `maxLines` —
during a real migration, about a key that does not exist, with §9.4.2's unknown-key diagnostic
(#21) not built to catch the resulting config. It also cited **§7**, which is absent/unavailable
items and has never said anything about caps. `check-citations.sh` could not have caught that: §7
resolves. Only reading the cited heading's title does — the same habit that caught §12.4/§12.5
earlier this session. Rewritten to §4.0.1 with the no-default rule stated.

`commands/edit.md`'s mention needed no change: it says a cap note is width-independent and appears
at all three widths, which stays true whenever a cap is configured and claims no default.

### `tput cols` is a constant in every environment these commands run in (§12.1.1)

Came out of auditing the prompts' spec citations by heading title — the habit that caught §12.4/§12.5
earlier and §7 an hour ago. The citations were all fine. The **commands around them** were not.

`COLUMNS=$(tput cols)` appeared in migrate, revert, setup, and edit. Measured in the environment an
LLM actually executes these in:

```
tty                     → not a tty
stty size < /dev/tty    → device not configured
COLUMNS                 → 0
tput cols               → 80
tput -T xterm cols      → 80      # identical: the static terminfo capability
```

There is no controlling terminal, so there is no window to ask, and `tput` falls back to terminfo's
`cols`. **It is the literal constant 80 wearing the costume of an adaptive width** — which is worse
than having written `80`, because a reviewer reading the prompt sees a command that adapts and no
output ever contradicts them.

§12.6.3 had already ruled exactly this one layer down — *"the server has no terminal… a preview at
an inferred width is a faithful preview of a layout the user will never see, which is worse than no
preview, because it will be believed."* The slash commands share the condition and never got the
rule. Same shape as §12.6.10 (`preview` has no `notes`, "§9.8.1's defect one layer up"): **this
project's recurring failure is a rule made in one layer and not propagated to the layer above it.**
Worth naming, because that is now three.

**The duplicate it had already caused.** §12.4 asked for the terminal's width *and* 80 *and* 60, so
`edit` ran three previews at two widths: 80, 80, 60. §12.6.3 said "pair" the whole time. The waste
was not the point — the prompt *reasoned from the count*, telling its reader a width-independent
`maxLines` note "fires identically at all three widths and appears three times." It appears twice,
so a reader following that rule concludes the note is width-dependent, the exact inverse of the
lesson. And step 3's "before" capture used the single inferred width, so the before/after note diff
the method rests on compared one width against three runs.

**The escape hatch had a false-alarm instruction.** `revert` step 7 said output of nothing is "a
real finding about the backup" — at a width that is not the user's, with a deliberately minimal
payload. `setup` step 5 already says the opposite about the same evidence, warning that "a correct
install reads as a half-broken one" and the user's first act is debugging something that works.
Two commands drawing opposite conclusions from one observation is what `setup` itself calls worse
than either being wrong alone. Split: **nonzero exit or anything on stderr is a real finding; empty
stdout alone is inconclusive** — name both ordinary causes and hand the user the one-liner for their
own terminal, which is the only place the real width exists.

Fixed in all four prompts and in §12.1 and §12.4, which both instructed the impossible width. No
`$(tput` remains anywhere in `commands/` or `docs/`.

### §12.6 was where the shared authoring rules got written, and it is the wrong place (§12.1.2)

Naming the §12.1.1 pattern made it searchable, so I ran it against all ten of §12.6's rules asking
one question: *is this caused by the transport, or by a condition the slash commands also meet?*
**Four are conditions.** §12.6 was written last and most carefully, so it became where cross-cutting
rules landed, each scoped to the server because that was what was on the page at the time.

The tell is now written down: a section opening **"same root cause as §…"** marks a rule whose scope
was drawn at the layer it was *noticed* in rather than the layer it *holds* at.

- **§12.6.2** — the environment is not the user's shell. §12.3 already said "do not assume the
  default" and "say which path you wrote". §12.4 did not: its step 8 report listed what changed, the
  checkpoint, and every choice made on the user's behalf, and **never named the file it wrote**. The
  failure it leaves is the one with no symptom — the user's shell resolves one config, the command
  resolves another, nothing errors, the report is honest, and nothing changes.
- **§12.6.3** — no terminal. Hoisted an hour ago as §12.1.1.
- **§12.6.5** — concurrent writes. Its own opening sentence is *"an MCP call, **a slash command**,
  and a hand edit in an editor can now interleave"*. It names the command layer in the premise and
  hands the mechanism to the server alone. `edit` read the config at step 2 and wrote it at step 5
  with four steps in between; it now re-reads immediately before writing, which plus §12.2's
  checkpoint is the whole of that layer's protection.
- **§12.6.7** — the complete write list. An explicit boundary existed for the *ambient* layer and
  not for the one a user invokes deliberately, which is backwards.

**Hoisting verbatim would have been wrong, and that is the useful part.** The command layer's write
list is genuinely different in three ways: §12.6.7 says `settings.json` may be written "only from
`revert`", true only because there is no `setup` *tool* — copied across, it forbids §12.7 from doing
the one thing it exists for. The command layer needs a fifth entry for build outputs, since no MCP
tool compiles anything. And §12.6.7's ban on temp files outside the target directory is a constraint
on the file being atomically renamed, not on scratch files — §12.3 and §12.4 both draft configs and
capture stderr under `/tmp` and are right to.

So §12.1.2 states the rule (transport → §12.6; condition → §12.1) and writes the command layer's own
list out rather than cross-referencing one that is subtly false here.

## Fixing Defect 12 makes a broken config render nothing (§2.11.3, tasks #4 and #17)

Found while checking whether Defect 12 would strand §6.6's `Panel` branch. It does not — but the
same question turned up something worse three subsections deeper.

`SafeLoadAll` (`Program.cs:490`) builds the pane used when the config cannot be loaded, and it
satisfies every qualifier §2.11 spent three subsections narrowing: `Array.Empty<PaneItem>()` so it
is structurally empty rather than degradation-emptied (§2.11) or holding an unavailable item
(§2.11.2); size `"auto"`, which §2.4 makes a deprecated alias for `fill` and therefore
collapse-eligible; no `minSize` to hold it open (§2.11.1); and `PaneSplit.None` with no children,
so it is the root. §2.11's last bullet: *"If the root collapses, the surface emits nothing — zero
rows."*

So the moment Defect 12 is implemented, an unreadable config renders **nothing at all**. Today it
renders a bordered empty box — which is Defect 12's own complaint, and genuinely wrong, and also
the only evidence the user gets that anything happened. Zero rows is indistinguishable from a
working statusline with nothing to say, from claude-tui-line not being installed, and from the
`statusLine` key having been deleted. §7.1's third outcome is output that is wrong rather than
absent; this is absent *and* silent, on exactly the input where a reason is most needed.

**The fix is §9.2.1, not an exemption.** Special-casing the fallback pane in the collapse pre-pass
would work, and it puts a special case in the one function whose correctness argument (§2.11's
convergence reasoning) rests on having none. §9.2.1 already requires the render path to draw the
*reason* a config could not be read — and a pane carrying that reason is not structurally empty, so
it never qualifies for collapse and no exemption exists to write. The bug and the feature are one
edit.

**Hard ordering constraint: #17 lands with or before #4.** If #4 goes first it must ship a
temporary guard holding the fallback pane open, deleted when #17 arrives, because the window
between them is one where every config error is silent — and that window is precisely when a user
has just edited their config.

The reusable part is the shape of the question. "Does fixing A break B?" was asked about a border
drawer and answered no; the same walk found that fixing A deletes the surface C needs. **A
reachability question asked about one consumer is worth re-asking about every consumer of the same
condition** — the empty-root condition had two, and only one of them was on the list.

## Empty stdout is not evidence — one rule, four commands

`setup.md` line 117 states the principle: *"Two commands answering the same question differently is
worse than either being wrong alone."* Three lines later it broke it. Step 5 read "if it is empty,
that is a symptom worth chasing", while `revert.md` step 7 ruled empty stdout **inconclusive** and
said chasing it is its own harm — the same observation, from the same synthetic render, with
opposite conclusions.

It also contradicted *itself* within the step. Two paragraphs after "chase it", setup.md says the
minimal payload means dependent items render absent, "otherwise a correct install reads as a
half-broken one, and the user's first act is to debug something that is working." That is the
argument against the sentence above it.

**The unified rule, now in all four commands:**

- **Nonzero exit, or anything on stderr** → a real finding, reported now.
- **Empty stdout at exit 0** → **not evidence, in either direction.** The render used a synthetic
  width and a minimal payload, so it is not evidence of damage (`revert`), not evidence of a match
  (`migrate` — "two blank lines are not parity"), and not evidence of a broken baseline (`edit`).
  Name both causes and hand the user the one-liner for their own terminal.

`migrate.md` already had the other half and was never in conflict: `revert` says empty ≠ damage,
`migrate` says empty ≠ success. Stating it as one rule is what makes them obviously the same rule
rather than two that happen to coexist.

**Setup's softening does not blunt its check**, and the reason is worth keeping: the fault step 5
exists to catch — an unexpanded `${CLAUDE_PLUGIN_DATA}` or a wrong absolute path in settings.json —
*cannot* present as empty stdout, because a command that does not exist does not run. The shell
exits nonzero with "command not found" and lands in the first bucket. The bucket that got softer
never held the quarry.

**The reusable part:** a document that states a consistency principle is the highest-yield place to
look for a violation of it, because the principle was written by someone thinking about a
neighbouring case rather than auditing their own file. §12.1.1's "same root cause as §…" tell has a
sibling here — *a sentence explaining why two things must agree* is a search key for the two things
not agreeing.

## §9's opening was a second, stale authority on how many CLI modes exist

Found by the same search key that produced the §11 discharge: present-tense critiques whose fix
already landed. §9's lead paragraph read *"The binary currently does exactly one thing. v2 needs
three more"* — stale twice over, and the second way was the interesting one.

- **Stale in tense.** `--check`, `--items`, `--colors` and `--version` have all shipped. A reader
  acting on the sentence would go build them again.
- **Stale as a count, in a way nothing checked.** It said "three more" above a list of four, and the
  list itself omitted `--version` — which §9.7 specifies and §9.4 counts as a mode. So the numeral,
  the list, and the real mode set were three different answers. `check-counts` never fired: the
  numeral and the list are separated by "none of which may interfere…", so it did not read as a
  count-above-a-list at all. The checker guards a *shape*, and this claim had drifted out of it.

The fix is the §11 shape — past tense plus an explicit discharge — with one addition: **§9.4 is
named as the authority on the mode set**, and this list is demoted to defining what each mode
*does*. §9.4 already states the rule in a form that survives the sixth command being added, so the
opening had nothing to be right about; it only had something to disagree with. `--version` was also
added as a bullet, since a list that omits a mode is a trap even after it stops carrying a count.

General shape, worth keeping: **an enumeration written before a later section added a member is a
count that decays without anybody editing it.** The section that adds the member has no reason to
look upstream, and the upstream list has no way to notice. Wherever a spec states a set and a later
subsection extends it, the earlier statement is a defect waiting for a reader who trusts it.

## check-examples.sh: a fourth rule, and two defects found by running it at all

Acting on the finding above — a count decays with nobody editing anything — the durable fix was to
make the claim checkable rather than to fix the one sentence. `check-examples.sh` already has the
binary and treats it as the oracle, so **rule D** went there: a prose count of the builtins must be
the live count. Verified three ways: clean at the true count, and firing at both one below and one
above, because a checker only proven on the passing case is a checker proven to print a string.

The anchor is `builtin(s)` or `built-in item(s)` immediately after the numeral, and two near-misses
set that boundary. "the nearest of the **sixteen**" is the ANSI palette — closed by the standard,
not by this registry, and it will be wrong the day the two counts differ. SPEC.md's banner says v1
had "14 **built-in segments**" — true, past tense, about a design v2 replaced. Requiring the noun
`item` is not a carve-out for that line; it is the check asking about the *registry* rather than
about anything that happens to be built in. STATUS.md is exempt outright: it is append-only, and a
check that demands a retrospective be rewritten to stay green teaches people to rewrite
retrospectives.

**Running it turned up two defects that had nothing to do with the new rule.**

1. **Rule 3 tripped on its own documentation.** Its marker test was a substring test — line contains
   `<!--` and contains `items-table` — and §9.6.2.2's bullet *documenting rule 3* quotes the marker
   inside a sentence. The check read its own spec as a marker, opened a table scan, found no table,
   and reported SPEC-V2-FRAMEWORK.md as omitting all sixteen items. **A check that cannot survive
   being described is a check nobody can write documentation for.** Fixed with rule 1's own
   distinction in the shape a prose marker needs: the marker counts only when the *whole line is the
   comment*. Not an exact match on the marker — README.md's carries a trailing note inside it.

2. **Nothing had ever run this check.** It is deliberately outside `check-docs.sh` (needs a binary)
   and runs only in CI's `build` job — and CI has never executed here, billing being blocked. So
   between the day rule 3 was documented and today, the only check that compares documentation
   against *code* was failing, and the failure had no reader. The exclusion from `check-docs.sh` is
   still correct. The conclusion is narrower and worse: **a check whose only runner is an unproven
   runner is not in service yet.** Task #30 is marked complete and the check was red the whole time.

Until CI is real, run it by hand — no build needed, any existing artifact will do:

```
CLAUDE_TUI_LINE_BIN=./src/ClaudeTuiLine/bin/Release/net10.0/claude-tui-line ./tools/check-examples.sh
```

## check-citations named two innocent lines, and the reason was eight unescaped dots

Writing the section above produced a citation to `§9.6.2.3`, which has no heading — a real finding,
correctly caught. But the report named **three** locations for it, and two were lines about `§2.3`
that have never mentioned it: `SPEC-V2-FRAMEWORK.md:906` and `:916`, both innocent, alongside the
one real citation in this file.

(Every number in this section is written backticked, deliberately. check-citations treats a bare
`§N.M` as a citation and a backticked one as a mention, which is the same accommodation Rule A of
check-examples makes for fenced-versus-prose — and it is the only reason a retrospective about a
dangling reference can name the reference. Without it this paragraph would trip the check it
describes, which is how the check would come to be switched off.)

The resolve step is exact — `comm` over two sorted sets — so the *finding* was right. The
**reporting** step then goes back to the occurrence list with `grep -E ":${ref}$"`, and in an ERE
every `.` is a wildcard. The occurrence line for line 906 is `SPEC-V2-FRAMEWORK.md:906:2.3`, whose
last eight characters are `:906:2.3`, and the pattern `:9.6.2.3$` is eight characters that match it
one for one. Escaping the dots is the whole fix.

Three things worth keeping from it:

- **A correct finding pointing at wrong lines is worse than a wrong finding.** It sends the reader to
  edit prose that is fine, and the conclusion they reach is that the checker is broken — which, on
  the evidence in front of them, it is.
- It cost real time here: the misattribution made the dangling citation look **pre-existing**, since
  both bogus locations sat in a region nothing had touched. The next twenty minutes went to "how was
  this ever green?" rather than to the one line that actually needed changing.
- The exact-vs-fuzzy split is the tell. A checker that computes its verdict one way and its
  explanation another has two implementations of "which citation is this", and only the first is
  covered by the green run. **Wherever a check reports a location, the location is a second answer
  and nothing tests it.**

The citation itself was fixed by pointing at §9.6.2.2 — the `--items` section that already contains
the `check-examples.sh` ruling — rather than by adding a `#### 9.6.2.3` heading, which would have
split the `version` ruling out of the section whose title promises it.

## `--preview` landed in the implementor's tree; three rulings, one of them a change (§9.3.4)

`--preview` (bare and `--json`) plus `notes[]` are implemented and green — build 0/0, suite
1143/1143 — but **uncommitted**, and one ruling changes the diff. Task #33 was closed from the other
direction: the implementor hit §9.3.1 while building the synthetic-input path, found nothing
anywhere referenced `SyntheticFixture`, and added the three assertions. #32's collector was already
closed. Both marked done.

The three questions came back as "the spec pins the content but not this", which is the shape a real
gap arrives in. Ruled in **§9.3.4** rather than in the reply, because a ruling that lives in a
conversation is a ruling nobody can cite.

1. **`rows[]` is a line of the rendered surface, borders included** — changed from the pre-Panel
   content rows it was built with. The decider was not fidelity but *agreement*: a bordered pane
   writes three lines and would report one, so bare `--preview` and `--preview --json` would
   disagree about how many rows exist, while `migrate.md` promises an LLM they are one render seen
   two ways. Border-Rounded is the default, so that is the common case.
2. **Preview must never write the `paneWidth` stamp** — confirmed, for a bigger reason than the
   caution it was done out of. The stamp goes to the cache **on disk**, which the live statusline
   reads next tick, so `--preview --columns 60` would hand `60` to the user's real `command` items
   at their actual width. A read-only preview reaching the render path through a shared store is
   §9.1 violated one indirection out. #16's description now carries the fix: key the widths store by
   resolved surface width, so the boolean disappears instead of becoming permanent.
3. **Note text is pinned the moment anything is told to read it.** `migrate.md` already teaches an
   LLM to quote two note strings verbatim. The rule is not "quoted notes are pinned" — it is that an
   unpinned note *will* be quoted by whoever writes the next prompt, and will then drift with
   nothing failing. All collector note texts go in §9.8.1's list. The §9.3 preamble lines stay
   content-pinned only; they address a person reading a terminal.

## `9.2.1` was ruled but not specified; the gap was the word "truncated" (`9.2.2`)

Assigned #17 to the implementor and then read `9.2.1` to check it was implementable before they
got there. It rules the hard part correctly — the render path exits 0 and draws the reason rather
than falling back to defaults — and then hands the easy-looking part over in four words: "truncated
to the usable width."

Four words, four unanswered questions. Is the prefix the program name or `argv[0]`? Which path is
named in the row where the user never typed one? What gets dropped first when a home-directory path
plus a parse error does not fit in 60 columns? And whose ellipsis — the built-in one, or the
`ellipsis` setting that lives in the file that could not be read?

The third is the one with a wrong answer that looks right. Truncating right-to-left is what any
implementation does by default, and it keeps the path while throwing away the reason — leaving a row
that says a file is broken without saying what is wrong with it, which is the one substring of the
message with no value on its own, since the user can already see the file. So `9.2.2` rules the
degradation as an explicit ladder of five rungs rather than a sentence about truncation. A ladder is
five tests; a sentence is zero.

The general shape, which is the third time it has come up this week: **a section that rules the
contested half of a decision will hand the uncontested half over in a phrase, and the phrase is
where the defect lives.** Nobody argues about truncation, so nobody writes it down, so it gets
implemented by whoever is typing at the time.

Verified the new count is actually checked rather than assumed: mutated "five rungs" to "six" and
confirmed `check-counts` goes red naming `SPEC-V2-FRAMEWORK.md:3007`, then restored. Green again.

## `9.4.2`: the derivation bullet had a wrong answer that passes every test

Applied the same lens to `9.4.2` (unknown-key diagnostic, task #21) and it has the same shape as
`9.2.1` — the contested half ruled well, the mechanism left to whoever implements it. Two of the
three additions are worth stating outside the spec.

The first is a live trap. `9.4.2` says the known-key set is "derived from the config types, not
listed", and the obvious reading is reflection: `typeof(UserConfig).GetProperties()`. This binary is
`PublishAot`, every config type binds through a source-generated `JsonSerializerContext`, and the
test host is not AOT. So reflection compiles, passes the suite, and is exactly what the trimmer may
drop from the published binary — the known-key set comes back short, and every valid config starts
emitting unknown-key warnings, **for users only and never for us**. Ruled: the set comes from
`ConfigJsonContext.Default.<Type>.Properties`, which is not merely AOT-safe but is the same metadata
the deserializer binds with, so it cannot disagree with what actually parses.

The second is that `[JsonExtensionData]` beats walking the JSON against a mirror of the config
shape, because the mirror is the hand-maintained second registry the bullet forbids, reintroduced
one level up. Letting the deserializer route unknown keys makes the per-object scoping fall out of
binding instead of being reimplemented next to it.

Case turned out to be already decided rather than open: every context sets
`PropertyNameCaseInsensitive = false`, so `"Color"` genuinely does not bind and reporting it is
correct. That also makes it the most valuable suggestion this diagnostic can produce.

## `check-counts` was armed or disarmed by where a paragraph happened to wrap

Wrote "There are two candidate rules, and a key is suggested when it satisfies either:" into
`9.4.2`, then mutated the two to three to confirm the checker guarded it. It did not fire. The
lead-in test was line-based, and "two" had landed on the line above the colon — so the count was
unguarded for no reason other than the wrap point. **A check a reflow can silently switch off is the
decay class the file exists to catch**, which makes this a defect in the checker rather than in the
prose. Widened it to test the paragraph, trimmed to its final sentence.

Widening it immediately surfaced three things the line-based version could not see, and only one of
them was a real finding:

- `SPEC:502` — "it applies at two levels ... so there are three cases:" above three items. A false
  positive caused by taking the *first* count in the sentence. The file header always said the last
  one was correct; the code had taken the first since it was written, and on a single line the two
  almost never differed. Switched the code to agree with its own header.
- `SPEC:4751` — "Four of §12.6's ten are conditions ...:" above four items. The `ten` belongs to a
  list in another section. Fixed by requiring a content word after the numeral: a numeral followed
  by `are`/`of`/`is` is referring to some other set, not promising this one. That lead-in is now
  skipped rather than guessed at, which is the failure direction this checker is tuned for.
- `SPEC:4771` — a genuine bug. `para` was never cleared when a pending list was flushed, so a
  paragraph after a list inherited the text before it. Cleared it in `flush()`.

Last fix was to the report itself, and it is the same lesson as the `check-citations` dot-escaping:
it echoed the *line* the colon landed on, so the wrapped case printed "says 3" above a quoted
sentence containing no 3, which reads as a broken check. It now echoes the sentence the count came
from.

Proved both directions: mutating the wrapped lead-in fires (`SPEC:3572`), mutating a single-line one
still fires (`SPEC:3007`), and the unmutated tree is clean across all ten files.

## `13.2`: the fix sentence was wrong in every load-bearing part (`13.2.1`)

Third section through the lens, and the worst of the three. `13.2` files defect 16 correctly — wrap
and truncate cut `Plain` at code-unit boundaries and can emit lone surrogates — and then prescribes
the fix in one sentence: "a boundary check at the cut — advance by one unit when the index falls
between surrogates — in **both** paths." Read against the code, that sentence is wrong four ways,
and a test asserting "the output contains no lone surrogate" passes the wrong fix.

- **Three cut sites, not two.** `PaneRenderer:61` (the too-narrow-for-the-ellipsis prefix) is inside
  the truncate path but is not the cut `13.2` was looking at, so it goes unlisted. `9.2.2` has since
  added a fourth. Ruled: one helper computes every cut and no site indexes `Plain` directly —
  a per-site check is correct exactly until someone adds a site, and they have no reason to look
  here.
- **The cut must round down; "advance" reads as up.** Because `Plain.Length` *is* the width metric,
  taking the extra code unit puts the row a real column over budget, which pushes a border or wraps
  the terminal row. Rounding down leaves a blank column nobody can see.
- **Rounding down alone does not terminate.** At `innerWidth == 1` against a non-BMP character the
  cut is zero, the chunk is empty, the carried index never moves, and the wrap loop spins — in a
  process Claude Code runs once a second, forever, because someone put an emoji in a narrow pane.
  That single case is the one place forward is right, and it is right because the alternative is a
  hang rather than a wide row.
- **The wrap path needs a carried index, not a check.** `WrapSegment` advances by a fixed stride, so
  trimming the end of row N leaves the orphaned low surrogate sitting at the start of row N+1. The
  bolted-on fix turns the truncate path green and leaves the defect in the path `13.2` was actually
  about.

The distinguishing test is therefore not "no lone surrogates" but: wrap non-BMP text at a width that
guarantees a mid-pair cut, then assert every row is independently valid UTF-16 **and** that
concatenating the rows reproduces the input exactly.

Three sections in, the pattern is consistent enough to state as a rule: **when a section files a
defect and prescribes the fix in a sentence, the sentence is the least-examined text in the
document.** It was written after the hard thinking was done, by someone who already knew the answer,
and it is the part an implementer follows literally.

## Stating a count twice in one lead-in leaves one of them unguarded

Wrote "**There are three cut sites, not two.** `PaneRenderer` cuts `Plain` in three places:", then
mutated three to four to confirm `check-counts` guarded it. It stayed green. The lead-in names the
count twice, the checker takes the last one, and the last one was still correct — so the sentence
could contradict itself and nothing would notice.

Not a checker bug, and deliberately not fixed there: requiring every count in a lead-in to agree
would re-break `SPEC:502` ("it applies at two levels ... so there are three cases:"), where two
different counts in one sentence are both right. Fixed in the prose instead — "in each of these
places:" — and it now fires at `SPEC:5461`. Worth knowing while writing: a lead-in that states its
count twice has one guarded copy and one that can drift.

## `check-notes.sh`: the note-pinning rule, mechanised — and red on purpose

§9.3.4 ruled that a render note is an interface and that every note's text is pinned in §9.8.1.
That ruling sat undischarged for several commits — §9.8.1 had no list at all — and the only reason
it surfaced is that the implementor read HEAD instead of taking my claim on trust. The ruling is
now discharged (`23ebe22`) and, more usefully, checked: `tools/check-notes.sh` extracts every
`*Notes.Add($"…")` literal under `src/`, collapses `{placeholder}` to `{}` on both sides, and fails
on anything the pinned block does not list.

One direction only. A pinned note with no producer is not an error, because §4.0.1's `maxLines`
note is pinned and cannot fire until `maxLines` exists; encoding "not built yet" into the block
would make it something other than the exact strings it is supposed to be.

Wired into `check-docs.sh`, not `check-examples.sh`. It reads C# — but as *text*, with no build —
and `check-examples.sh` sat unrun for its entire existence because it needs a binary. The
distinction that matters for where a check lives is toolchain, not subject matter.

**It is red right now, and that is the point.** `PaneRenderer.cs:33` says `segment truncated: no
width remained at {width} columns` where width plainly remained — the pane note keeps that phrase
because for that pane none did. §9.8.1 pins the corrected `segment truncated to fit {columns}
columns`, and the one-line fix is with the implementor. Both directions were proved before
committing: a throwaway worktree carrying only that one-line change ran `check-docs` to `exit=0`,
so the check is known to go green rather than merely known to complain.

A red gate with a named owner and a known-green proof is a gate. A red gate nobody is fixing is
noise, and the next person silently stops reading the whole runner.

## `2.3.1` specified min-rows only for the case where an answer exists (`2.3.3`)

Audited the §2 layout cluster — the largest unimplemented body in the spec and the one nobody had
read with the "ruled the contested half, phrased the rest" lens. §2.3.1 holds up unusually well:
the inversion, the monotonicity argument, the O(N) claim and the cost analysis are all correct. The
defects are all in the same place, and it is the place that shape predicts — the sentences written
after the thinking was done.

`minWidth(i, T) = min { w : rows_i(w) <= T }` is a minimum over a set that can be empty, and the
section's own worked example is the first thing to walk into it: at `T = 1` it says "`minWidth(left,
1)` alone exceeds `R`", when by the definition three paragraphs above it does not exist. An
implementer writing `feasible` as a sum will make it return a number, and the specific way that
fails is `int.MaxValue`: two candidates that cannot fit sum to −2, −2 is `≤ R`, and the search
certifies the narrowest `T` in the scan. The surface then renders taller than the `T` just proved
achievable, every pane individually legal, nothing to report — §2.3.1's own stated failure class,
produced by the line it did not write.

The scan's ceiling is wrong too. "Bounded by the largest item count, a row holds at least one item"
is false under §2.6, where an item wider than the pane wraps: three items with one wrapping across
four rows is six rows. The bound is too low exactly when the terminal is narrow, which is when
anyone wants min-rows at all. And when no `T` is feasible at any height, the section has no answer
— ruled as a fallback to `greedy`, whose over-constrained behaviour is already defined and already
observable through §9.8.1's `pane {n} dropped` note.

Two suspicions checked and dropped rather than written up. Whether the tens of packer calls meant
tens of shell spawns per render: they do not — the packer takes pre-resolved `Segment`s and touches
nothing that resolves a value. And whether "the existing packer, called unchanged" was false because
the packer returns a longest-row *width* rather than a row count: `SizeResolver.RowCountAt`
(`SizeResolver.cs:583`) is exactly `rows_i(w)`, so the phrase is accurate. Recording both, because an
audit that only ever finds things is not an audit — and because in each case the write-up would have
been confident, plausible, and wrong.

The second check did pin down §2.3.1's last ruling, though. `RowCountAt(Pane, int, ItemContext,
IReadOnlyDictionary)` has no `measureOverride` parameter, while `ResolveVertical` threads one — so
the min-rows path really does bypass the seam, exactly as §2.3.1 says, and task #25 now has a line
number instead of a paragraph.

## §2.8's rulings were real and uncitable

Three `####` headings under §2.8 carried no numbers, so the ladder's ownership of both row budgets,
the border-closing rule, and shrink-wrap could not be cited by anything — including tasks #7 and #29,
which are the work that implements them. Numbered §2.8.1–§2.8.3. Purely additive: nothing cited
`§2.8.x` before, so no citation could break, and `check-citations` confirms 97 still resolve.

Cheap, but the shape is the same one this session keeps finding. An unnumbered heading is a ruling
the document cannot refer to, which over time becomes a ruling the document does not have.

## A purity test stated over two inputs when the function takes five (`2.5.2`)

§2.5.1 closes by handing §10 a test: the same leaf pane at the same inner width renders identically
as the root of an 80-column terminal and as the third child of a split in a 200-column one. The
intent is exactly right and is the section's whole point. The sentence is false, and §2.6 is what
falsifies it — below `MinUsableWidth` the root pane takes the single-line fallback and the child in
a split does not, and the `overflow` default differs by position too. The two positions named as an
identity test are the two positions §2.6 exists to distinguish.

`PaneRenderer.RenderLeaf` already takes `(items, innerWidth, overflow, ellipsis, notes,
allowFallback)`. The code was right and the summary of it was written by someone thinking about
`COLUMNS`. Ruled over the full tuple, which is a property that can actually hold.

The part worth enforcing rather than recording: `allowFallback` is a surface fact, computed at the
root and passed down. A leaf that derives it has to ask a question about the surface, which is the
defect §2.5.1's enforcement paragraph forbids in different clothes. `COLUMNS` is the obvious way to
reach around the compositor; this is the quiet one.

And the reason it survived: the false version passes. Every fixture at 20 columns or more with
`overflow` given explicitly agrees in both positions, so a suite that never sizes a leaf below
`MinUsableWidth` and never leans on the default confirms a property the spec does not have.

## The surface's pane count was a render-time quantity (`2.6.1`)

Chasing whether §2.6's "single root pane" and "surface has exactly one pane" could disagree on a
one-child split turned up something better. Nothing validates a one-child split, and §2.2 says a
collapsing split "reduces to a single child" — so a two-pane surface whose second pane goes empty
satisfies "exactly one pane" by the only reading §2.6 offers.

Take the count at render time and the survivor's `overflow` default flips from `truncate` to
`overflow`, and its fallback eligibility flips on, because a sibling had nothing to say. At
`refreshInterval: 1` that is a flicker, not a corner case: a neighbour holding a git branch outside
a repo, or a command that returns empty on a cache miss, switches the surviving pane's overflow
behaviour once a second. The user sees a row that alternates between tidily truncated and running
past the surface, with no config change and nothing to report.

Ruled to the configured count, fixed at config load, collapse never changing it. Both rules keep
their own reason: parity belongs to a config that asks for one pane, and "never corrupt a neighbour"
belongs to a config that has neighbours, whether or not they have anything to say this second. It
also makes `allowFallback` a config-load fact, which is what §2.5.2 needs — a flag recomputed per
render from a tree that changes per render is the leaf asking about its position with an extra step.

## The red gate closed itself

`check-notes` went red on `PaneRenderer.cs:33`, the finding named the file, the line and the
required text, and the implementor's next commit made it green. No round trip, no restating the
rule, no one taking my word for what §9.8.1 says. That is the whole argument for mechanising a
rule instead of writing it down harder — this same rule had already decayed once, and the way it
decayed was me asserting it had been discharged when it had not.

`check-docs` is green on all three checks.

## §2.11.3 and §9.2.2 agree, and now say so

§2.11.3 argues that Defect 12 is safe to implement once §9.2.1 gives the config-error fallback pane
content, because a pane carrying a reason is not structurally empty and so never collapses. That
argument rests on the reason pane always having something in it — which §9.2.2, written later this
session, is what actually guarantees: its ladder bottoms out at "as much of `claude-tui-line` as
fits" rather than at nothing.

The two were written two sections and several commits apart and happened to compose. Made the
dependency an explicit citation rather than a coincidence, so `check-citations` holds it. Task #4
now records §2.11.3's ordering constraint as a real `blockedBy` on #17 instead of a sentence in a
document nobody re-reads before starting work.

## The ledger is JSON Lines now (§12.2.1)

§12.2 said "`ledger.json` is append-only" and `docs/backup-ledger.md` expanded that into "A JSON
array, append-only. Read it, append one entry, write the whole array back." A JSON array cannot be
appended to — the closing bracket has to move — so the procedure's own next sentence was *rewrite
every prior byte*. "Append-only" was the semantic; the operation was the opposite of it.

What made that fatal rather than untidy: **no `.cs` file writes the ledger, and none is planned
to.** Checked — zero hits for `FileMode.Append`, `AppendAllText`, `AppendAllLines`, and no source
file mentions the ledger at all. Per §12.1 the commands are prompts, so the writer is always an LLM
with a whole-file write tool, and "write the array back" meant re-emitting every prior entry from
context: SHA-256 digests, `statusLine` keys it is explicitly told it will not recognise, absolute
paths, growing every invocation. Rule 1 forbids removing an entry, but under a whole-file rewrite
that rule is not just unenforced — it is uncheckable, because a dropped entry and an entry never
written leave the same file.

The loss lands on the oldest entry, which is `origin`, which is the one that can never be
recreated — rule 4 writes `origin` only when the live `statusLine` does not already point at a
claude-tui-line binary, and after install that is permanently false. So a dropped `origin` produces
no error and no gap: every later entry is a correctly-written `checkpoint`, forever. §12.5 rules a
damaged ledger stops `revert` and forbids reconstructing one, so there is no recovery. §12.2's
opening argument is that the naive design lets the escape hatch close quietly exactly as it becomes
needed; it then chose a write path that reintroduced that failure one layer down.

Ruled in §12.2.1: JSON Lines, file renamed `ledger.jsonl`, appended with `>>` and never with a
whole-file write; the ledger is read to *decide*, never to write back; a reader discards a torn
final line instead of treating it as the unreadable-ledger case, since every complete line before
the tear is still the ledger; and if this ever moves into the binary, .NET's `FileMode.Append` is
seek-then-write rather than POSIX `O_APPEND` and is not sufficient. §12.6.5's compare-and-swap
deliberately does not reach here — for two concurrent ledger writes the correct outcome is that
*both* entries land.

Timing was the deciding factor. `~/.claude/claude-tui-line/backups/` does not exist on this machine;
no ledger has ever been written, so there is nothing to migrate and no `origin` at risk. Converting
later would mean rewriting the file whole — the exact operation the section forbids — on the one
file whose oldest entry cannot be regenerated. Landed the doc and command changes in the same commit
rather than leaving `docs/backup-ledger.md` describing an array the spec had already replaced.

## Migrate's fidelity check passed on the empty set (§12.3.1)

§12.3 rules the migration tiers, the colour mapping, the timeout budget, the write order and the
ledger interaction — at length, each with its failure mode named. Then it hands the verification
over in a phrase: "run the original script and `--preview` against the same stdin payload." *Same*
is stated; *what* is not. `commands/migrate.md:138` filled that in with a payload it wrote itself:
`{"cwd":"$PWD","model":{"display_name":"Claude Opus 5"}}` — two of `StatusInput`'s thirteen fields.

Every element of the user's script reading `session_id`, `context_window`, `rate_limits`, `pr`,
`vim`, `agent`, `effort`, `thinking`, `output_style`, `worktree` or `workspace` produces nothing
under that, and produces nothing *without erroring* — so step 6's existing guard ("if the original
script errors on this synthetic payload, say so rather than treating its empty output as a match")
never fires. The check is "every visible token the original produced must appear in the new
render," which on an element that produced no token holds vacuously. Both sides silent, comparison
passes, tier-3 list empty, success reported. The elements hardest to migrate are exactly the ones
the payload makes invisible, since the ones that render from a bare `cwd` are the easy ones.

§9.3.1's very first rule is this same finding for a different consumer — "a fixture built to look
like a real payload omits them," and then `--items` shows an empty `example`. Migrate is the worse
case: there the consequence is visible, here it is a pass.

Not carelessness — a missing seam. §9.3's branches are *stdin has data → use it*, annotated "this
is what `/migrate` uses", and *stdin empty → the built-in fixture*. §9.3 named migrate the consumer
of the branch with no fixture, and the fixture is reachable only from inside the process: the flag
list is `--check --version --items --colors --preview --json --config --columns`, and none emits
it. So migrate.md had nothing to hand the user's script and invented something.

Ruled in §12.3.1: the payload must populate every field; §9.3.1's fixture is that payload and the
binary must emit it (which is what makes §9.3's "the only synthetic payload" true once a consumer
lives outside the process); the check runs at two payloads because the fixture's `cwd` is
deliberately not a real path and every filesystem-derived element goes vacuous under it; and a
machine-probed disagreement under the fixture is expected, not a finding.

Filed as **#36** (the flag and the migrate.md rewrite must land together). Landed an interim fix in
`commands/migrate.md` now rather than waiting on the flag: an element whose field the payload does
not carry goes on the tier-3 list as **`unverified`**, not silently counted as matched. Deliberately
did *not* write a full thirteen-field payload into migrate.md — that would be the second standing
fixture §9.3 exists to forbid.

**Discarded, checked first:** that the two sides could not be fed the same stdin at all —
`--preview` reads it (`Program.cs:253`) and `CommandProvider.cs:114` pipes it to command items, so
the mechanism is sound. And that stripping escapes leaves colour unverified with nobody saying so —
technically true, but `migrate.md:139-140` puts both renders side by side in front of the user and
nothing is written before they approve, so the human eye is a real check, not an absent one.

## One literal, three commands (§12.7.1)

`{"cwd":"$PWD","model":{"display_name":"Claude Opus 5"}}` is character-for-character identical in
`commands/setup.md`, `commands/migrate.md` and `commands/revert.md`. §12.3.1 read that as migrate's
problem; it is not. §9.3 claims the fixture is "the only synthetic payload **in the binary**" —
true, and the boundary is wrong, because the duplication is in the layer the user sees output from.
§9.3's own case against a second constant applies verbatim to the command layer and was never made
there.

Ruled: setup previews at §9.3.1's fixture, and §12.7's "say the payload is minimal" is demoted from
remedy to mitigation. It tells the user the tool is incomplete; §9.3's stderr admission tells them
the data is invented. Only the second is true.

The argument that makes task #36 load-bearing rather than a convenience: setup must run
`statusLine.command` verbatim (§12.7, and correct — the expansion is the untested thing), which
forecloses `--preview`'s empty-stdin fallback, because the verbatim command *is* the render path and
the render path needs stdin. Verbatim-command and complete-payload are jointly satisfiable only once
the binary can pipe its fixture out. #36 now covers three commands.

Interim fix in `commands/setup.md` is a note pinning the literal in place — the tempting wrong fix
is hand-rolling a fuller payload there, which is the same defect written once more.

Resolved opposite to migrate deliberately: the fixture's `cwd` is not a real path, so
filesystem-derived items blank out under it. Migrate needs verification coverage and so needs both
payloads; setup needs one honest render and needs only this one.

## Swept the commands for other hardcoded enumerations — mostly a discard

Having found the payload duplicated three times, checked whether the command layer states any other
list the binary owns. Four candidates, three discarded:

- `migrate.md:82` — the three mapping tiers. Defined by the spec, not by the binary. Not a registry.
- `migrate.md:84` — `from`/`extract`/`case`/`format`, the derived-item keys. States four keys inline
  while line 29 already sends the reader to `--items`'s `kinds` section for them. Real but mild: a
  stale key list here costs a capability the migration doesn't reach for, not a broken statusline,
  and the sentence carries ordering information (`applied in that order`) that `--items` may not.
- `edit.md:129` — `size`/`overflow`/`distribute`, presented as examples rather than a closed set.

The live one was **`migrate.md:186`, written this session** in the §12.3.1 interim fix: eleven
`StatusInput` field names spelled out in a prompt file. Exactly the §1 rule this session has been
enforcing on other people's prose. Fixed now rather than deferred — the enumeration was never
load-bearing, since the instruction is "any top-level key the payload does not carry," and the
payload is right there. Deriving it is strictly more correct than a snapshot that goes stale on the
next field added.

No new spec section: §1 already rules this, and `SPEC:164` is the catch-all for prose that duplicates
an output. Eleven existing applications of it — generalizing it a twelfth time would be the
duplication it forbids.

## §9.5 asserts an arrangement instead of specifying one (§9.5.1)

Picked §9.5 by structure, not by suspicion: seventeen lines, no subsections, the shortest section in
§9 — and its title is a declarative implementation claim, which is the "uncontested half handed over
in a phrase" shape in its purest form. All three claims fail against the code.

Its central argument is also inverted. Two walks that drift *disagree*, and a disagreement is
findable; one shared walk that falls behind the config surface makes the resolver and the checker
wrong in the same direction and in agreement. `--check` passes, the id resolves to nothing, §7
renders it absent. Sharing is still right — it is just not the safety property §9.5 claims it is,
and the concentrated risk needs a guarantee the section never gives.

Confirmed against `ItemValueResolver.cs` before writing (three code facts, no speculation):

- **`ReferenceExtractors` is `private static`** (`:138`), only caller in the same file. `--check`
  literally cannot reuse it. The heading states as fact what the access modifier forbids — and this
  is the dangerous kind of gap, because an implementer finds the shared table unreachable and the
  second walk trivial exactly when the choice is being made.
- **Type is `Func<ScanContext, IEnumerable<string>>`** — bare ids. §9.5's own next paragraph promises
  a warning in a `link` and an error in an argv, and §9.4 names the offending key by JSON Pointer.
  Neither survives a `string`. "The extractor answers which ids this config names; nothing more" was
  written to keep *verdicts* out and took *provenance* out with them.
- **No coverage test.** The invariant is the sentence "adding a reference form must remain a single
  append", addressed to a person — and Defect 11 is proof that instruction has already been missed
  once, which is why the section exists at all.

Ruled: widen the member deliberately; yield a record of (id, construct, JSON Pointer) so the
resolver selects the id and discards the rest; and replace the sentence with a **fail-closed** test —
walk the config types, require every id-naming member to be either covered or explicitly exempted,
so a new field fails the build rather than going silently unchecked. Same move as §9.4.2's
`[JsonExtensionData]`: make the type system enumerate instead of a person remembering.

## Two rulings driven by the implementor's findings (§9.2.2, §12.7.2)

**§9.2.2 — the reason string is composed, not passed through.** They fed a real trailing-comma
config through `--check` and got 190 characters of `System.Text.Json`, opening with "Change the
reader options" and carrying `LineNumber` at the far end. §9.2.1's fenced sample reads `unexpected
',' at line 12`. Rung 4 truncates from the right, so the real row keeps advice actionable by nobody
and drops the only part not recoverable by opening the file. Ruled: lead with position, read from
`JsonException`'s properties rather than scraped from text, message appended raw as the part rung 4
may eat. General rule the ladder was missing — **truncation must degrade toward what the user cannot
otherwise obtain.** §9.2.1's fence marked illustrative in place.

**§12.7.2 — the emitted payload carries the real `cwd`.** Answering my open question, they found
`--preview` is two paths: empty stdin uses the canned `CreateItemContext()`, non-empty stdin parses
the payload but still probes the real machine for git/Engram/remote-url. So a fixture arriving
through a pipe takes the probing branch, and emitting it verbatim would pair an invented `cwd` with
real machine state — **incoherent**, which is worse than the minimal render §12.7.1 replaces.

Ruled: emit the fixture with `cwd` replaced by the process working directory, nothing else changed.
Not a second fixture in §9.3's sense — that forbids a second *authored* constant, and one field
derived from the environment is not that. §9.3.1's constant stays visibly synthetic where
determinism is actually needed.

This made #36 **smaller**. A real `cwd` resolves the filesystem-derived items coherently, so one
payload exercises both halves and §12.3.1's mandatory second run is retired. The honest limit, now
stated in the spec: coherent and complete, **not deterministic** — it varies by machine, which is
exactly what setup and revert want, and which migrate is immune to since it compares two renders of
one payload in one session.

Worth recording as process: this is the third time this session that checking the code before
finalizing a ruling changed the ruling. Here it changed it in the cheaper direction.

## §1 was cited nine times for a rule it does not state (§1.1)

Best finding of the session, and found purely by structure: §1 is ten lines with no subsections, and
eleven sections cite it. Highest-leverage target in the document — a gap there is inherited
everywhere.

**Its worked example was stale, in the way it exists to forbid.** `SegmentBuilder.Build` is now
fourteen lines with exactly one `if` (a null check) wrapping
`foreach (var id in ItemRegistry.DefaultIds)`. The collapse §1 demands **has already landed**. The
spec still said *"current 14 hand-written `if` blocks"* — present tense, a number in prose about live
code, which is verbatim the defect §4 names ("a number written into prose is a second registry that
goes stale"). §1 demonstrated the failure it defines, in its only example. `14` survives as
coincidence: it is now the method's line count.

Repaired as *history*, not as a fresh count — writing a new number re-commits the defect. The
before-state is real and worth keeping; what must not persist is prose asserting what the code looks
like today.

**The bigger half: §1 is about cost, and every citation of it is about drift.** Read literally, §1
says adding an item costs one registry row and zero edits elsewhere — a claim about *expensive
extension*, satisfied completely by a registry that is trivial to append to and silently missing half
its entries. Now read what §1 actually gets invoked for: Defect 11's resolution set behind the config
surface, §9.5.1's extractor table behind it, §9.4.2's unknown keys, §9.6.2.2's version drift,
§12.7.1's payload copied into three commands. Not one is "adding a thing was expensive." Every one is
a registry that fell behind the surface it mirrors with nothing to notice. The document has been
borrowing §1's spirit and getting the right answer anyway.

Ruled as the rule's second half: **a registry must be mechanically tied to the kind it enumerates.**
One registry is necessary, not sufficient — the cheap-to-extend registry and the silently-incomplete
one are the same object, and only a check tells them apart. "Mechanically" excludes a sentence
addressed to whoever comes next, because Defect 11 and §9.5.1 are each that sentence, already ignored
once.

**And §1's own test was a milestone, not a check.** *"If adding an item means touching
`SegmentBuilder`'s control flow, the abstraction has not landed"* cannot fail again — the landing
happened and nothing runs it. That is how a registry starts falling behind: not by anyone deciding to
duplicate it, but by the check that would have caught them being retired as passed.

## §14.2 rejects mtime correctly, then substitutes something that fails the same test (§14.2.1)

§14.2's case against mtime: *"the whole failure mode is a file that was written from stale input"* —
mtime says **when**, not **what from**. Correct. But SHA-256 of the deployed binary says **which**,
and equally not what from. Both answer a question about the output; neither reaches the input, which
is where the failure lives. A stale artifact hashes perfectly consistently — consistency is exactly
what a stale file has.

What the hash genuinely buys is auditability: two people, or one person across two sessions, can
establish they mean the same binary, and "shipped and verified" stops being a memory. That is a
reporting discipline, and §14.2 should be read as one. It is not detection, and it was being counted
as detection.

Ruled: provenance requires the artifact to **carry** its source identity, not to be measured after
the fact. The mechanism already exists and §14 had never cited it — §9.7's `<Version>` and
`--version`. Comparing `publish/claude-tui-line --version` against the source tree answers the
question §14 is actually asking, by asking the artifact, which is the only party that knows. Hash it
to name it; ask it to date it.

**This reclassifies task #18.** §9.7's drift test was scoped as internal consistency between the
assembly version and `plugin.json`. It is also the missing half of §14. Two sections solving
complementary halves of one problem without referencing each other is the condition under which both
get called complete — so §14 now depends on §9.7, and changing how the version is stamped changes
whether deploys can be verified at all.

Weighed and kept minor: `-o publish` is a relative path (the §12.7 unset-variable shape), but the
`.csproj` argument is relative too, so a wrong directory fails to find the project instead of
publishing somewhere unexpected. The unguarded case is a **second clone or worktree** — both are real
repo roots, both satisfy the `.csproj` path, and only one holds the `publish/` the live statusline
runs. That is §14.1's original drift with the directories renamed, and it is another argument for
§9.7: a version answers *which tree*, a hash only *which file*.

## Compare-and-swap notifies; it does not protect (§12.6.11)

Audited §12.6.5. **Three hypotheses discarded first**, which is worth recording because two of them
were already answered better elsewhere in the document:

- *§2.7's byte-parity gate is a milestone that stops being enforced once it passes* — **false.**
  `tests/ClaudeTuiLine.Tests/GoldenParityTests.cs` and `fixtures/golden-phase1-baseline.json`
  captured the pre-pane output once, before Phase 2 touched any rendering code. The gate is a file,
  not a memory. This is the §1.1 pattern done right, and it was done right before I looked.
- *`baseRevision` being optional voids the protection on a fresh machine* — **already ruled**, at
  §12.6.9, and more precisely than I had it: optionality handles a caller *omitting* the field;
  the fresh-machine hole is separate, and `revision: "absent"` closes it by giving a first write a
  value to send.
- *The mechanism is scoped to the MCP server while the hazard names three writers* — **already
  ruled**, at §12.1.2, and `commands/edit.md` already says so in its own step 5.

What survived is in the sentence both layers share. §12.6.5 refuses a stale write and says the
model "re-reads instead of clobbering"; `edit.md` step 5, having no `baseRevision` to be refused
by, independently arrives at "re-read immediately before you edit." Re-reading makes the **content**
current and leaves the **intent** stale — a position resolved against the older copy, applied
unchanged to a reordered newer one, removing the wrong item validly and silently.

Credit where it is due: neither section oversells its backstop. `edit.md` explicitly calls the
checkpoint "recoverable rather than preventable," which is the honest claim. The one word that
overreached was calling the re-read *the whole of your protection*.

**Ruled (§12.6.11).** Compare the re-read against the first read and branch: identical → proceed;
different → re-derive the edit, and tell the user the file moved under them and what moved. Same
for the server: `stale-revision` means re-derive, not re-read.

**The point worth keeping.** A caller that answers `stale-revision` by fetching a fresh `revision`
and resubmitting its original `config` *succeeds*, and the ledger then records a clean
compare-and-swap over a write that discarded someone's work. Without CAS the clobber is silent;
with CAS and a naive retry it is silent **and attested**. The refusal was never the protection — it
was the notification, and what the caller does next is the protection. That half was left to the
caller's judgment by a section that reads as though it had specified it.

Touched: new §12.6.11; §12.6.5's two superseded clauses marked in place (forward to §12.6.11 and
§12.6.9); `commands/edit.md` step 5 rewritten with the compare-and-branch. check-docs green at 108
cited sections.

## §3.2.2 discharged, and three rulings back to the implementor (§9.2.2, §9.4.4)

**§3.2.2 — nothing to rule.** It was on the shortlist as a short leaf section making a concrete
code claim; the code already satisfies every part of it. `PaneRenderer.RestyleSimple` unwraps the
OSC 8 link, restyles the inner markup, and re-wraps (`:112`–`:119`); `TruncateSegment` closes the
link before appending the ellipsis (`:73`–`:79`), so clicking `…` cannot navigate; and
`HyperlinkTests.cs:76 WrapOfLinkedColoredSegment_EveryContinuationRow_KeepsBothColorAndLink` is
exactly the test §3.2.2 said was the one that matters — asserting the link *survives* a wrap of a
coloured segment, rather than asserting a row is the right width. Fourth discard of this pass.

**Three flags from the implementor's `eca7477`, all three answered, two of them corrections to me.**

1. **`JsonException.LineNumber` is 0-indexed; the row must show `+ 1`.** They found this by crafting
   a real malformed config rather than by reading the API docs. Accepted and pinned into §9.2.2.
   Worth stating why it is not a nitpick: §9.2.2 justifies the entire composition by calling the
   message "the part a user can reconstruct by opening the file at the line we just named." That is
   true only if the number names the line they land on. An off-by-one does not degrade the row, it
   inverts the argument that produced it.

2. **A real JSON Pointer costs ~34 columns, so protecting the whole prefix makes rung 5 the common
   case.** Also theirs, also measured. My ruling said protect the position and truncate the message;
   at 45 columns that cannot fit the protected region at all and drops to the bare tool name —
   correct behaviour of the rule as written, wrong outcome, trading a row that still says *where*
   for one that says nothing, at exactly the widths the ladder exists for. **Corrected: the
   protected region is `line <n>` alone.** The Pointer and the message are one truncatable tail.
   The irreplaceability rule was right; I had applied it one level too coarsely — the line number
   cannot be recovered by any means, the Pointer is what you see when you open the file at that
   line. Also ruled: when the tail is cut entirely the separator goes with it, no dangling `: `.

3. **§9.4.4's `--config` sentence excluded the render path, and they were right not to follow it.**
   It derived "which modes read `--config`" from "which modes can reach exit 3", giving `--check`
   and `--preview`. Render reads config and exits **0** with a diagnostic row (§9.2.1), so exit-3
   reachability was never the property in question — *loading config* is. Sentence corrected in
   place with the reasoning kept, because the general form is worth having: **a derived list is only
   as good as the property it is derived from, and a derivation that happens to produce the right
   answer today is not thereby correct.** §9.4.4's own thesis is that derived beats enumerated. It
   does — and this is the price: an enumeration is wrong visibly, a bad derivation is wrong with a
   reason attached. Building the table from the corrected rule surfaced a live regression
   (`--config` silently accepted alongside `--version`/`--items`/`--colors` since `b182025`) that
   the old wording would have ratified.

Note on their `--check --columns` message rewording: no wording was ever spec'd for those two, and
naming the mode that was actually selected is better than naming the one that wasn't. No objection.

check-docs green at 108 cited sections.

### The deploy check §14 needs does not exist, and #18 is not it (§14.2.2)

§14.2.1 named `--version` as the provenance mechanism and I treated task #18 as discharging it. It
does not. §9.7's drift test compares the assembly version against `plugin.json` — two files in the
source tree, read by the same test run. It proves internal consistency and cannot observe
`publish/`.

The missing check is a separate step: run the deployed binary, compare what it answers to what the
tree declares. It cannot be a unit test, because a test interrogates what the test run just built
and the artifact in question was deployed earlier. Beside §14.2's hash, doing the half the hash
cannot — the hash names the file, `--version` dates it.

The scenario, with every current check green: bump, rebuild, deploy the previous binary. Drift test
passes, hash is self-consistent, §14.3's single-command rule unviolated, statusline is last week's.

Sent to the implementor as an insert after #18, so it lands next to the same material. Sequencing is
now **#18 → §14.2.2 deploy check → §9.2.2 protected-region fix → #37 → #21 → #36.**

**Landed as `tools/verify-deploy.sh` (`d1227bc`).** My own first statement of the contract had the
defect it was written to prevent: I named `publish/claude-tui-line` as though there were one
deployed artifact. There are two — `publish/`, where this repo builds, and
`${CLAUDE_PLUGIN_DATA:-$HOME/.claude/claude-tui-line}/bin`, where `commands/setup.md` step 2 puts a
real user's copy. A check hardcoded to the first is green on my machine while a user runs last
week's binary, which is §14.1's failure relocated from *which version* to *whose copy*. So the path
is `$1`, defaulting to `publish/claude-tui-line` for convenience only. Two further rulings went in
with it: the tree side reads `plugin.json` rather than building, because deploy verification's
premise is a machine that may be broken; and "cannot tell" exits distinctly from "mismatch", because
folding it into failure teaches people to ignore red and folding it into success is a silent pass.
All three exit paths were exercised, not just read. Deliberately not added to `check-all.sh` —
everything there asks about the working tree, this asks about a particular installed copy.

**~~Open, delegated~~ Measured: AOT publish is deterministic across `-o` destinations.** §14.3's
"reproduces a hash" needed two AOT publishes to different scratch directories compared
byte-for-byte, neither of them `publish/`. The sha256s matched, so §14.3's verification story stands
and §14.2's hash is a check any reviewer can run rather than one only the deployer can. The result
is also what keeps the hash and `--version` from collapsing into each other: determinism is exactly
what makes the hash answer *which source* precisely — and exactly why it can never answer *when*,
since output from the same commit is identical whether it was published this morning or last month.

### GitHub Actions removed; `tools/check-all.sh` is the gate

**User ruling: CI is deleted, clone-and-build-locally is the supported story.** `.github/workflows/ci.yml`
is gone and `.github/` went with it, having held nothing else. Branch protection is unaffected —
`required_status_checks` was `null`, so the workflow was never a merge gate and removing it cannot
block a push.

What prompted it: the workflow had never run. Actions was never billed on this repo, so `main` carried
a red ✗ meaning "never ran" rather than "failing" — a permanently-red signal, which is the same defect
§1.1.1 describes in another register. A check that cannot fail is not a check; one that always fails
is the same check.

**The concrete loss, and the reason this is not just tidying: `check-examples.sh` has never executed
anywhere.** It needs a built binary, so it lived only in the Actions `build` job. Written, reviewed,
committed, and it has compared nothing. **Task #30 is marked complete and the gate behind it has never
fired.** Expect its first real run to find things; those findings are latent, not new.

**It has now run, against a real binary, and came back clean:** 16 items across 10 files, everything
matching, `check-all.sh` exit 0. Recording that flat rather than as vindication. The prediction above
was that a never-run check would find latent defects and it found none — which says the examples were
right, and says nothing at all about whether they would have stayed right for another month
unchecked. The finding that mattered was never "these examples are wrong"; it was that nobody could
have known either way. That is now false, which is the entire change.

`tools/check-all.sh` is `check-docs.sh` then `check-examples.sh`, both run even if the first fails. No
build step — `check-examples.sh` already builds via `dotnet run` when `CLAUDE_TUI_LINE_BIN` is unset and
dies rather than reporting clean when it cannot. Deliberately **not** folded into `check-docs.sh`, whose
header already rules that it must run with no toolchain and must never learn to skip a check it cannot
perform.

Handed to the implementor to run, since it shells out to `dotnet run` and shares `obj/` with their
build.

### §1.1 discharged: nine accepted-value lists, none tied to its parser (§1.1.1)

`ConfigCheck.cs:289–297`. Every one currently agrees, which is the point. `BorderStyleParsing` was
extracted specifically to avoid "a second copy of the token list" and `ConfigCheck.cs:289` is one,
twenty lines away — because **a switch is not enumerable**, so a shared parser can make two acceptance
behaviours agree and can never make the documented set agree. Fix is to make the set data; `size` is
exempt, its list mixes literals with descriptions of a form.

~~Open: whether the README and spec carry a third and fourth copy.~~ Answered — see §1.1.2 below.

### §1.1.2 landed: the docs are a third and fourth copy, two already drifted — and the fix is not to generate them

First real task for `cdtui-architect`, the new architecture peer. Investigated read-only, wrote a
self-contained fragment to its own path (no Edit access, so no full-file rewrite of the living spec
to land one section), handed back the splice instructions; I did the insert and STATUS write.

**Finding.** Yes, six sites, two already drifted. `split` is written `"vertical"`/`"horizontal"` at
both `README.md:138` and `SPEC-V2-FRAMEWORK.md:3694`, omitting `none`; `distribute` is written
`"min-rows"` alone at `README.md:142`, omitting `greedy`. Sharpest instance:
`SPEC-V2-FRAMEWORK.md:3693–3696` states the cite-don't-copy discipline for `distribute` in one
sentence and copies `split`'s members wrong, and `colorSystem`'s in full, in the same sentence.

**A second finding arrived first and changed the shape of this one: §1.1.1 was never implemented.**
`1c10684` touched only `SPEC-V2-FRAMEWORK.md` (+68, zero code); `ConfigCheck.cs` has exactly one
commit in its history, predating §1.1.1. The nine arrays at `ConfigCheck.cs:289–297` are untouched;
acceptance was unified (`BorderStyleParsing.TryParse`) but the value-set duplication was not. **I had
marked #38 completed on the strength of the spec commit alone and never checked the code — wrong.**
Caught by the architect via `git show --stat` and `git log -- ConfigCheck.cs` before it built a
docs-ruling on top of a registry that doesn't exist. #38 reopened, sequenced behind #37 (shares
`ItemValueResolver.cs`). §1.1.2 rules on the docs question anyway, explicitly depending on a registry
that isn't built yet rather than pretending otherwise.

**Ruling: docs ⊆ registry, never docs = registry.** Every literal token the docs quote as an accepted
value must be one the registry accepts; the docs are not required to name every token the registry
holds. `--check`'s existing diagnostic (`ConfigCheck.cs:401–410`) is the authority on the full set —
it already delivers the complete list at the moment someone needs it, which a table read weeks
earlier cannot match. `README.md:136`'s `accepted values` column heading gets retitled, which turns
both current drifts into intentional brevity rather than defects, and they get left alone — not
"finished" by adding the missing tokens after the completeness claim is gone. Spec prose cites rather
than copies; the `split` fix at `:3694` is a citation, not the missing `none`, or fixing it creates a
fifth copy while fixing the fourth. The check cannot live in `check-docs.sh` — needs a compiled
registry, and that script's own guarantee (`check-all.sh:15–19`) is that it never learns to skip what
it can't perform — so it belongs beside `check-examples.sh`. Nothing buildable yet: #37 → #38 → an
external door onto the registry (needed because a registry no outside consumer can read is "a
`switch` with extra steps", the exact thing §1.1.1 exists to end) → the subset check itself, in that
order.

**The load-bearing argument, and why generation was rejected outright rather than deferred:**
`README.md:140` on `size` says `"auto"` is a deprecated alias for `"fill"` and warns it does *not*
mean `"content"` — three facts `SizeValues` doesn't carry at all. Generating that row from the
registry would delete information to gain a consistency the row never lacked. That's §1.1.1's `size`
exemption ("three literals and two descriptions of a form") arriving a second time in a second
medium: a registry can hold a set; documentation's job is to describe a form; where they diverge,
generation destroys the part worth writing. Three kinds follow from that seam — enumerable
(checkable), form-with-special-cases (`size` alone, uncheckable), illustrative mention (every example
config, must **never** be checked, since the only way to satisfy such a check is to make examples
exhaustive and thereby worse to read).

Fold-in of my one live call in this thread: `README.md:159–161`'s note that `--check` "is the next
thing being built" was stale (it shipped in Phase 5, `1c90e0c`) — fixed directly (`b018b7b`), kept
the render-time-fallback half of the sentence since nothing in `ConfigCheck.cs`'s history confirmed
that changed too, rather than guessing.

### #37 scoped up: `ScanReferences` is a second walk, and the record shape found a broken diagnostic

The implementor stopped before building and asked, which was right. §9.5.1 asks for three things —
widen `ReferenceExtractors` to `internal`, give it a record-shaped yield with provenance, add a
fail-closed coverage test — and doing exactly those three would have closed nothing. `--check`'s
diagnostics never call `ReferenceExtractors`. They come from `ScanReferences`, an independent
hand-rolled walk of five `foreach` blocks re-deriving the same reference forms. Not "the table has
no consumer yet" — a second implementation that agrees with the first by accident.

**Ruled: #37 includes retiring that walk.** The three asks are means; the end is one implementation
of "where can an id appear", and shipping the means without the end ships the appearance of it.
Fourth time this shape has come up (Defects 11 and 15, §6.6, §1.1.1).

**But the walk does not get deleted until a test proves the two agree.** "Agrees by coincidence" is
itself an assertion nobody has checked — neither implementation has ever been run against the other.
Equivalence test first, over the §9.3.1 fixture plus `--check`'s malformed cases, asserting identical
diagnostic sets. Green, delete and reuse the corpus; red, that is a live `--check` defect worth more
than the refactor. Deleting first and eyeballing the diff is how a silent behaviour change ships.

Three shape rulings:

- **One stream, one required `Kind` discriminator** — not `ReferenceForm?` with null meaning
  "declares". Both consumers filter the same stream, which is what makes sharing load-bearing, but a
  nullable field that changes what the record *is* keeps tempting callers to skip the branch and an
  exhaustive test cannot enumerate null as a case. Invariant enforced in the constructor, not
  documented. Rejected widening `ReferenceForm` with a `Declaration` member: every existing switch
  grows a case, and any default arm silently absorbs it — §9.4.3 biting from the other side. This
  also makes explicit that `entry.Item.Id ?? entry.Item.Item` was **already wrong for one of its two
  consumers** (`Id` declares, `Item` references); it never showed because only one consumer existed.
- **Colour tokens get their own table**, not a fold-in. `@name` resolves in a different namespace, so
  folding it in makes the `Id` field mean two things by `Kind` — the conflation just ruled against.
  Two enumerable kinds, two registries.
- **Fix `ScanContext.Tokens`, don't weaken the record.** Bucket 5 has only `.Values` in scope, so
  `/colors/{name}/from` is underivable — a diagnostic that cannot name the token it is complaining
  about. Expose pairs. A nullable pointer would let every future extractor opt out of the provenance
  that was the point of the change. Best find in the round trip: the record shape surfaced it.

**The coverage test's mechanism was the real question, and the obvious answer is wrong.** A
hand-authored `(MemberInfo, extractor)` map is a second registry — the test would prove the map
matches the members, not that the extractors do. Each row carries its own member instead, authored
once as a member expression that yields both accessor and `MemberInfo`. Walked type set derived by
reachability from the config root rather than listed, so it grows on its own; candidates are string
and string-collection members; each must be covered or exempted **with a printed reason**. One
measurement gate before building: report the candidate count. Tens is the honest price of failing
closed; two hundred means the filter is wrong.

### #37 step 5: promotion landed clean; the coverage test's own scope had a gap in exactly the corner it exists to guard

Step 4 (the equivalence test, both walks over a 10-member corpus harvested from `ConfigCheckTests.cs`)
came back green, 1164/1164 — no live `--check` defect, the two implementations already agreed.
Step 5a promoted `ScanReferencesViaExtractors`'s body into `ScanReferences`, deleted the now-redundant
second implementation and its equivalence test, held at 1154/1154 — the step-3 baseline count, proof the
promotion changed no behaviour.

Step 5b — the fail-closed coverage test itself — is where the implementor's own measurement pass
surfaced a real classification error, caught by `cdtui-architect` on review rather than by the
implementor: the proposed reflection roots were `PaneItem`, `ColorExpr`, `ColorRule` — the three types
`ScanContext` exposes — on the reasoning that `Pane` is "pure tree-navigation." False: `Pane.cs:149`
gives every pane a non-nullable `PaneBorder Border`, and `PaneBorder.Color` is a full `ColorExpr` — a
reference-carrying member of a type the root set excluded. `Walk` already scans it correctly
(`ItemValueResolver.cs:110`); the bug was never live. But the coverage test, as scoped, could never
catch a reference field added later to `Pane` or `PaneBorder` — precisely where §3.3's compound items
will land. It would have stayed green across the one class of change it exists to guard against.

Second finding: `PaneItem.Command` was proposed exempt as "a literal/config value." `ItemValueResolver.cs:138`'s
own comment says otherwise — "Adding a form (§4.2's argv placeholders...) means appending here."
`Command` **is** argv; it just has no reference form *yet*. Filing it next to `PaneItem.Case` in an
undifferentiated exempt bucket means nothing prompts reclassification when §4.2 ships. **Ruled:
exemptions come in two kinds** — `NeverAReference` and `PendingForm` (the latter carrying the spec
section that will make it live) — not one undifferentiated bucket. Same shape as the `ReferenceForm?`
nullable rejected earlier this section: a category that silently absorbs a case it should force a
decision on.

Also ruled: this test is **fail-closed over members, fail-open over sites** — reflection proves every
reference-carrying member is handled, never that `Walk` visits every place those types live. It would
stay exactly as green today with `ItemValueResolver.cs:110` deleted outright. Paired with a cheap fixture
assertion (step 5c: one config exercising all four `ColorExpr` sources, asserting the resulting path set
literally) rather than fixed by widening reflection further, because that is a different question
reflection cannot see. The registry form of `Walk` itself (walk sites as data) was recorded as the
eventual durable answer and deliberately not mandated now — three sites, twenty lines, not worth it
until §3.3 adds a fourth.

Landed as a new subsection of §9.5.1 rather than a chat-log ruling, same reasoning as §1.1.2: "what does
covered mean" is exactly the kind of definition that rots silently if it only ever existed in a message.
Fragment delivered by `cdtui-architect` (no Edit access, by design), verified against source before I
spliced it, `check-docs.sh` green afterward (112 cited sections, unchanged). Two NEEDS-EVIDENCE items
left for the implementor to resolve while building: whether a `PendingForm` row can detect its own spec
section has landed, and whether the stated candidate filter actually reproduces 16 (now 19, with the two
new `Pane` exemptions and the `PaneBorder.Style` foreign-type row) when run against the real code.

One aside surfaced and deliberately left unruled: a colour token (`@name`) can only appear in the
`ColorExpr` position, never inside a `MatchRule`/`ThresholdRule` branch, because `@`-prefix resolution
happens once at parse time (`Config.cs:537`, the only such site in the codebase). Unclear whether that's
a deliberate product boundary or an accident of how rules were built. Not blocking #37; flagged for a
future task if it turns out to matter.

### #38: the nine hand-copied enum-value arrays became eight per-kind registries, one exemption

§1.1.1's own inventory (`ConfigCheck.cs:289–297`) was the next instance of the pattern #37 had just
been fixed for elsewhere: nine hand-written token lists, each a second copy of a set a `switch`
already encoded, agreeing today only because nothing forced them to keep agreeing. `cdtui-implementor`
rebuilt eight of the nine (`border.style`, `valign`, `align`, `overflow`, `case`, `split`,
`distribute`, `colorSystem`) into a `static readonly Accepted` collection colocated with each kind's
own parser — `Config.cs`, `Pane.cs`, `OverflowMode.cs`, `ItemValueResolver.cs` — so the parser becomes
a lookup against the same object the diagnostic reads, closing the drift both directions §1.1.1 warns
about by construction rather than discipline. `size` stays hand-maintained and exempt, per §1.1.1's own
ruling: its list mixes literals with described forms (`an integer`, `a percentage`) and has no
closed-set parser to unify with. `ConfigCheck.cs`'s ten `UnknownEnumValue` call sites now read the
cross-class `AcceptedTokens` properties; only `SizeValues` remains local.

Verified independently before approving: `dotnet build` clean, 1158/1158 tests, `tools/check-all.sh`
exit 0, `check-citations` 112 sections resolving.

One design choice the implementor disclosed rather than assumed: registries colocated per-kind with
each parser's own file, not consolidated into one shared registry file. Approved as-is — this is the
same shape as `ItemValueResolver.cs:177-180`'s existing "two enumerable kinds, two registries" rule
(colour-token references and item-id references are kept apart because a shared `Kind` field would
mean two different things depending on context). One registry per enumerable kind is the standing
convention here; consolidating eight kinds into one file would be that anti-pattern in reverse.

Two prose staleness items the implementor correctly flagged rather than fixed (their instruction was
scoped to the two citation tables, not narrative prose): §1.1.1's intro sentence still claimed nine
*live* hand-written lists, and the border-row paragraph cited `Config.cs:625` (now `:652`) and quoted
a source comment ("through the exact same switch the loader uses") that #38's own refactor reworded to
"accepted-token lookup." Fixed directly rather than routed anywhere — past-tensed both passages, scoped
the "all nine agree" line down to the one kind (`size`) it still describes, and credited #38 by number.
`check-docs.sh` green afterward, same 112 sections, no drift from the prose-only edit.

Landed as two commits: `7a30bfb` (the implementor's registry rebuild, five source files) and `30dd9c1`
(the prose fix, spec-only), both pushed.

### #42: `--accepted --json`, the external door §1.1.3 specified

New `AcceptedCommand.cs` mirrors `ItemsCommand`/`ColorsCommand`'s shape: 9 rows (8 kinds read their
parser's `AcceptedTokens` directly, no copy; `size` gets `accepted: null` plus `alsoAccepted` from
`ConfigChecker.FormatAccepted(SizeValues)`). `Program.cs` gets the mode, mutual-exclusion entry, and
the bare-`--accepted`-is-an-error usage check per §1's `--json`-required ruling. `SizeValues` and
`FormatAccepted` widened `private`→`internal` to allow the shared call (NEEDS-EVIDENCE (b), chosen
over the fallback duplicate-sentence test).

Verified independently before approving: `dotnet build` clean, 1166/1166 tests (+8 new, 0
regressions), live-binary checks for all 9 keys, the mutual-exclusion and bare-flag errors, the
fail-closed invariant, `size`'s byte-identical `alsoAccepted`, and a `grep` proving zero token
literals are copied into `AcceptedCommand.cs`. `check-all.sh` unaffected (112 citations, exit 0).

Three spec corrections from the implementor's report, all fixed directly: (1) verification item 8
assumed the `check-all.sh` subset-check itself was #42's job — it's #43's (§5); item 8 now says so
and #42 is not blocked on it. (2) NEEDS-EVIDENCE (c)'s premise that `--colors` is JSON-only was
wrong — it has its own bare-mode markup render, a documented exception to §9.6.2.2 — corrected in
place; doesn't change #42, since §1's `--json`-required ruling was already stated as deliberate
regardless of precedent. (3) a spec citation said `ConfigCheck.CheckEnums`; the class is
`ConfigChecker` (the file is `ConfigCheck.cs`) — fixed.

Approved, implementor instructed to commit + push. #45 (unify `CheckEnums`'s 9-kind list with
`AcceptedCommand`'s key table) and #46 (`size` decomposition) tracked separately, not folded in.
#43 stays blocked on the still-open "is `--accepted` public or internal" question, held for Jim.

### #15: one border-colour resolver — `ResolveBorderColor` returns a `Style`

First worktree-parallel task landed. Verified `Style.TryParse("dim"/"bold")` directly (throwaway probe
against Spectre.Console 0.57.2) before touching production code: `Foreground == Color.Default`,
decoration set correctly — confirming, not assuming, §6.6's premise. `ColorResolution.Resolve` is now
the sole border-colour resolution point; `ResolveBorderColor` (`ColorResolution.cs:66-76`) returns
`Style` instead of `Color` and no longer calls `ResolveLiteral`. `Program.cs`'s `ComputeRows`/`DrawRows`
threaded the type change through; `ResolveLiteral` itself untouched (`ConfigCheck.cs:196` still needs a
bare `Color` for `--check`'s colour-system ranking, per §6.6.1). New `BorderColorResolutionTests.cs`
proves decoration survives and both paths now agree on the same spec.

Built and merged from an isolated worktree (`worktree-agent-a0e9bbcdf33bedc6a`) — first use of the new
parallel-dispatch pattern. Full suite run (implementor's call, not just smoke — the return-type change
touched three call sites): 1171/1171, 0 failures (1166 pre-existing + 5 new). Fast-forward merge to
main, no conflicts. Landed as `18d6322`, pushed.

### #14: `shell:true` multi-element argv — was already fixed, coverage gap closed

Second worktree-parallel task landed, different finding than expected: the render suppression
(`CommandProvider.cs:57-64`) and the `command-shell-argv` check diagnostic (`ConfigCheck.cs:271-277`)
were already correctly implemented, from the original Phase 5 CLI commit — STATUS.md's "Ruled, not yet
built" framing had gone stale. Confirmed against §4.1 directly rather than trusting the dispatch's
premise.

What was actually missing: no test called `CommandProvider.ResolveAsync` at all — the three existing
`ConfigCheckTests.cs` cases covered only the check path, not render suppression itself. New
`CommandProviderTests.cs` (3 tests) closes that, including two that spawn a real `sh`/`echo` process —
the first tests in this suite to do so, mirroring `EndToEndItemValuesTests.cs`'s existing use of
`Path.GetTempPath()` as `cacheDir`.

Merged (non-fast-forward, main had moved) after a clean build. No conflicts — only the new test file.
Landed as `041770b`, pushed.

### #4: Defect 12, empty pane still drew its border (§2.4/§2.11/§2.11.1/§2.11.2)

New `PaneCollapse.cs`: a pre-pass, run before `SizeResolver.Resolve`, that collapses a leaf pane
(no border/gutter/space) when it's content/fill-sized, has no `minSize`, and every item resolved to
no value — excluding items whose command genuinely failed or was unavailable (§2.11.2's carve-out),
so a real failure still shows rather than silently vanishing. A split pane collapses bottom-up when
every child collapses. A pane with an explicit `minSize` keeps its border even when empty (§2.11.1).

Distinguishing "resolved to nothing" from "resolution failed" required widening two return types:
`CommandProvider.ResolveAsync` now returns `CommandResolution(string? Value, bool Unavailable)`,
`ItemValueResolver.ResolveAsync` now returns `Resolution(Values, UnavailableIds)` — both were bare
value/dictionary before. `Program.cs` threads `unavailableIds` into `ComputeRows`, and the collapse
pass only runs on the split-pipeline branch; the legacy no-surface leaf path (no items configured,
fall back to v1 defaults) is a different semantic and was left alone.

Two things flagged, neither treated as a defect:
- A split-child pane with `items:[]` (or omitted) now collapses instead of falling through to
  `PaneAssembler`'s default 14-builtin-segment fallback. Checked: nothing in the suite relies on
  that fallback for a split child specifically.
- §2.11.3's ruling to exempt "the SafeLoadAll fallback Pane" from collapse doesn't apply — that
  architecture is gone (zero `SafeLoadAll` hits). The unreadable-config case already short-circuits
  via `ConfigUnreadableMessage.Format` before `ComputeRows`/`DrawRows` run at all, which independently
  satisfies §9.2.1's intent. Spec section is stale, not wrong-and-acted-on; tracked as #47 rather than
  edited now.

Full suite run (justified — public return-type contract change with call sites outside the feature):
1174/1174, plus 3 targeted `--preview` repros matching the literal defect-12 shape and §2.11.1's
minSize-keeps-border case. Test fixups: `CommandProviderTests.cs` (3 call sites, `.Value` added),
`EndToEndItemValuesTests.cs` (1 call site, `.Values` added). Committed directly to main (in-tree work,
not a worktree task) as `62a5741`, fast-forward, pushed.

### #35: `--preview --json` `rows[]` shape test coverage

First worktree task from `cdtui-implementor2` (worktree `../claude-tui-line-wt-35`, branch `task-35`).
New `PreviewJsonRowsTests.cs` (3 tests): every row's `width` equals `text.Length` after ANSI-strip;
`contentWidth` is present on content rows and absent (not null) on border rows; JSON property-name
shape matches `ItemsCommandTests`' existing style. The actual `rows[]` rules live in §9.3.4 (plus
§9.3.2/§9.3.3 for bare-vs-json, §9.6 for the base shape) — corrected in this entry since the original
dispatch had mis-cited §9.6.2.2, which covers `--items --json`, not `--preview`.

`RunPreview` has no public `Build()`-style entry point the way `ItemsCommand`/`ColorsCommand` do (it's
a top-level local function in `Program.cs`), so it isn't reachable via `InternalsVisibleTo`. Tests
exercise the built CLI as a subprocess instead — the same mechanism `tools/check-examples.sh` already
uses (`CLAUDE_TUI_LINE_BIN` honored, falls back to `dotnet run -c Release`). New pattern for the test
suite; backlogged as #48 rather than refactoring `RunPreview` now.

One divergence flagged, not fixed: `rows[].width` is always `text.Length` (UTF-16 code units), while
§9.3.4's prose says it's "computed by the same function the layout used" — only `contentWidth` actually
reuses the layout's measured value. Didn't affect the ASCII fixtures either way. Backlogged as #49,
linked to #20 (surrogate-pair slicing) as the likely shared root cause rather than treated separately.

Merged (non-fast-forward, main had moved) after a clean build and a targeted test run (3/3 passed).
Landed as `78467b0`, pushed.

### #7: `height: "content"` pane shrink-wrap (§2.8.3)

A pane may declare `height: "content"` (new `PaneHeight` enum, default stays `"fill"`) so its border box
closes immediately under its last content row instead of padding to the band height; `valign` gains a
second meaning under `content` — positioning the box within the band, rather than positioning content
within the box as it does under `fill`. New `PaneConfig.Height`/`"height"` JSON key wired through
`ResolvePane`, plus a `ConfigCheck.cs` diagnostic for an unrecognized token, mirroring the existing
`distribute` pattern.

The fix reused rather than duplicated an existing mechanism: `Compositor.PaneContribution.Valign`/
`PadRows` already did the right thing, but was unreachable for vertical-split children because
`PaneTreeRenderer.cs`'s split branch force-padded every child to `childHeight` via `PadHeight` before
that logic ever ran. One line now skips that pad step when the child's `Height` is `Content`, letting
the pre-existing padding-outside-the-border logic take over — no new compositing path.

Confirmed out of scope and untouched: §2.10 border-collapse/per-edge glyphs (doesn't exist in `src/`
yet — grepped, doc-comments only; §2.8.3 is spec-coupled to #8 there but #8 hasn't started) and §2.8.1/
§2.8.2 (degrade ladder, border-suppression-under-3-rows — task #29, also unstarted). Neither total
surface row count nor the degrade ladder is touched by this change, per the spec's explicit non-goals.

5 new tests (`PaneHeightContentTests.cs`) computed against the code rather than hardcoded, covering:
total row count unchanged, right pane's border spans its natural height not the band, band remainder is
blank background under default `valign: top`, `fill` sibling still spans the full band, `valign: bottom`
moves the box to the band's bottom. Plus 2 `ConfigCheckTests.cs` cases for the new `height` token.

Fast-forward merge (main hadn't moved), clean build, full suite escalated (justified — touches the shared
split-rendering path): 1184/1184 passed. Landed as `2f69047`, pushed.

### #5: §4.2 argv placeholders for custom command items

`{item-id}` placeholders in a `command` item's `argv` reuse §3.2's `{}`/`{other-id}` vocabulary via a new
shared `PlaceholderTemplate.cs` tokenizer (id charset `^[A-Za-z0-9_.\-]*$`, `{{`/`}}` escaping), retrofitted
under `LeafContent.cs`'s existing link-template regex too rather than living beside it. New
`ArgvPlaceholders.cs` does the actual expansion: non-shell mode substitutes resolved values directly into
`ProcessStartInfo.ArgumentList` (no shell, no injection risk); `shell:true` mode substitutes nothing into
the command string and instead exports only the referenced values as `CLAUDE_TUI_LINE_VAL_<ID>` env vars —
the security boundary against command injection the spec calls for.

`--check` now errors (not warns) on five new diagnostics: unknown-item-id, placeholder-derived-source,
placeholder-command-source, bare-`{}` self-reference, and placeholder-env-collision. The value-cache key
(`ItemCache.cs`) was widened to a 5-argument form covering resolved argv, cwd, pane width, and exported env
together — closing the gap flagged in dispatch (§5's up-front resolution set needed the same treatment as
defect 11, and `CLAUDE_TUI_LINE_PANE_WIDTH` needed to be in the key, not just argv). `PaneItem.Command` is
now a real covered row in §9.5.1's `ReferenceExtractors` table, not a `PendingForm` exemption.

Three implementor judgment calls, none blocking: (1) the cache-key split (3-arg width-tracking vs. new
5-arg value key) is plumbing not dictated by spec text — chosen to hit the coverage requirement with zero
`Program.cs` changes; (2) argv-substituted/env-exported values are not ANSI-stripped, unlike link templates
— reasoned correct since these go to subprocess argv/env, not rendered terminal text, but the spec doesn't
explicitly rule on it; (3) a *named* self-reference (an item's own id used as `{own-id}`, vs. bare `{}`)
falls through to the generic placeholder-command-source diagnostic rather than the self-reference one —
functionally still an error, just a different code. Not fixed now; flagged as backlog #50 in case the
diagnostic code matters to a future `--check --json` consumer.

Non-fast-forward merge (main had moved with #7 in the meantime; auto-merged cleanly, no conflicts). Full
suite: 1216/1216 passed. Landed as merge commit `f555b57` (branch tip `723e203`), pushed.

### #8a: border reserve decomposition (§2.10 / §2.10.1 rule 5 prereq)

Replaced the flat `PaneBorderRenderer.BorderReserve = 4` constant with named
`SizeResolver.OwnBorderReserve(p)` / `OwnRowReserve(p)` functions, backed by new
`BorderWidthReserve = 4` / `BorderRowReserve = 2` consts in `SizeResolver.cs`. No new config, no
behavior change — this is pure decomposition ahead of §2.10's per-edge config (#8b) and border
grid (#8c). Five call sites updated (`PaneBorderRenderer.cs:16`, `PaneTreeRenderer.cs:24`,
`Program.cs:199,759`, plus `SizeResolver.cs` itself); `ConfigCheck.cs:549`'s `BoundaryCost` call
site was left untouched as required — it picks up the new arithmetic transitively rather than
gaining a second copy.

One implementor judgment call, accepted: `PaneTreeRenderer.cs:79` had its own inline row-border
arithmetic (`targetHeight - (pane.Border.Style is not null ? 2 : 0)`), not in the original 5-site
list but squarely §2.10.1 rule 5's "no transcription" target — folded into `OwnRowReserve(pane)`,
same value, no behavior change.

Golden parity gate did not move (this was the acceptance bar, per §2.10's "a move here is a defect
in the decomposition, not an expected consequence" — confirmed unmoved, so no escalation needed).
Independently re-verified via task-gopher off the worktree at the implementor's commit before
merging: build exit 0, full suite **1216/1216 passed**, 0 failed.

Non-fast-forward merge (main had moved with the §2.10.2 amendment/fix spec commits in the
meantime; auto-merged cleanly, no conflicts — spec-only vs. src/tests-only diffs). Landed as merge
commit `421f6bc` (branch tip `d19e7fa`), pushed. Unblocks #8b (task #52).

### #29: §2.8 height ladder owns both row budgets, border suppressed under 3 rows

Implemented the §2.8.1 deterministic degrade ladder (measure → demote wrap→truncate in reverse
declaration order → drop trailing items → clip) and folded §2.8.2's under-3-rows border suppression
into the same pass. `ClipRows`/`ItemsEmptied` moved off `Pane` onto a new `ResolvedPane`/per-render
side structure, keyed by tree path rather than by `Pane` reference — required by §2.5.1 (leaf
rendering must stay a pure function of `(items, innerWidth)`, so a render-time clip budget can't
reach it through the `Pane` config record) and §5.0.1 (data with different lifetimes must not share
a record; a `Dictionary<Pane,_>` would also collide on record value-equality between two
structurally-identical panes, e.g. two empty bordered spacers).

Architect review (second look after "tests pass," per established pattern) caught two real defects
before merge, not just spec-silence: a tie-break direction risk and a termination-invariant break
from the default-segments fallback (rung 4 could clip a higher rung's growth, making the proof's
"every rung strictly reducing" argument fail silently). Both fixed. Spec gained a new §2.8.1 rule:
a pane whose last item rung 3 dropped renders **zero content rows**, not `RenderDefaultRows`'s
"author declared nothing" fallback — declared-empty and emptied-by-degradation share a
representation but must not share a rendering outcome. Border suppression stays keyed on row
budget only, never on emptiness — an emptied pane keeps its box unless §2.8.2 independently
suppresses it. "Declaration order" was also spec-disambiguated at §2.8.1: the ladder compares
panes with no common parent, so it means reverse pre-order document order over the whole tree, not
§2.3 step 4's sibling-scoped sense of the same term.

Merge hit the same class of issue as #8a warned about: `task-29` branched before #8a merged and
still referenced the old `PaneBorderRenderer.BorderReserve` constant #8a had replaced with
`SizeResolver.OwnBorderReserve`/`OwnRowReserve`. git's line-merge auto-resolved with no reported
conflict twice in a row — first leaving a stale `BorderReserve` reference in
`PaneBorderRenderer.cs` plus (after the implementor's own pre-empt fix) a duplicate
`OwnBorderReserve(PaneBorder)` overload in `SizeResolver.cs` colliding with main's #8a version —
and a post-merge build was the only thing that caught either one. Fixed directly as
merge-integration cleanup (implementor fixed `PaneBorderRenderer.cs` plus their own overload;
orchestrator removed the duplicate overload and a stale `HeightLadderTests.cs` reference to the
removed `BorderReserve` constant). Full suite green at **1235/1235** after cleanup. Landed as merge
commit `e8c9afd` plus fixup commit `5d1ee89`, pushed.

### #16: §5.0.1 paneWidth split into a width-partitioned widths/ store

`ItemCache.StampPaneWidth` used to write paneWidth into the same on-disk cache the live statusline
reads next tick, so a `--preview` at a synthetic width could hand that width to the user's real
command items at their real width, invisibly. §9.3.4 requires the store to be keyed by resolved
surface width so a preview at 60 and a live render at 120 read/write disjoint entries.

Replaced `CacheEntry.PaneWidth` with a sibling `widths/` directory (mirrors `ItemCache`'s existing
per-item-file layout) keyed by `WidthKeyFor(id, argv, cwd, surfaceWidth)`. `StampPaneWidth` (the old
stamp-only-if-exists write) is gone, replaced by unconditional `TryReadWidth`/`WriteWidth` — no
reason left to gate on pre-existence once the key is already surface-width-partitioned. The interim
`stampWidths: false` escape hatch `--preview` used is removed entirely; the write is now always
safe because the store can't cross-contaminate. `CommandProvider.ResolveAsync` and
`ItemValueResolver.ResolveAsync` thread `widthsDir`/`surfaceWidth` through; `Program.cs` computes
`widthsDir` alongside `cacheDir` in both `RunAsync` and `RunPreview`.

New coverage: a test writing two widths for the same id/command/cwd at surface widths 60 and 120,
asserting each resolves independently. Per the session's new no-per-task-falsification-tests
directive, this is normal coverage, not a proof-of-failure test. Full suite: 1236/1236 passed
(independently re-verified via task-gopher, both pre-merge on the worktree and post-merge on
`main`). Landed as merge commit `d95f6ca` (branch tip `4ed325a`), pushed.

### #20: defect 16 — wrap/truncate slicing through UTF-16 surrogate pairs

`PaneRenderer.WrapSegment`/`TruncateSegment` cut `segment.Plain` by raw UTF-16 index with no check
for whether the cut landed between a high and low surrogate, so a non-BMP character (most emoji)
straddling a wrap or truncate boundary could be split into two lone surrogates — invalid UTF-16
written to stdout, not merely a clipped glyph. Independent of the existing `Plain.Length`-as-column
approximation, which is already correct for a 2-unit/2-column emoji.

Fix: new `SafeCutIndex(plain, index)` helper advances the index by one when it falls between a high
surrogate at `index-1` and a low surrogate at `index`; both `TruncateSegment` slice sites and
`WrapSegment`'s chunk-end route through it. `WrapSegment`'s loop changed from a fixed `i += innerWidth`
stride to a `while` loop tracking running position (`i = end` after each chunk), since adjusting one
chunk's end shifts where the next chunk must start — `SafeCutIndex`'s output is always itself
surrogate-safe so no re-check is needed at the top of the next iteration.

New coverage in `OverflowModeTests.cs` (established home for wrap/truncate edge cases): two normal
tests (not falsification-style, per the session's standing directive) constructing an emoji
straddling the exact cut index for both the wrap and truncate paths, plus a shared
`AssertNoLoneSurrogates` helper checked against every row. This closes a real gap — §2.6's trap list
covered escape sequences but never got the equivalent sentence for surrogate-pair characters.

Full suite: 1238/1238 passed (2 new tests over the #16 baseline), independently re-verified via
task-gopher both pre-merge on the worktree and post-merge on `main`. Landed as merge commit
`19dc964` (branch tip `2803c81`), pushed.

### #8b: per-edge border config, collapse:false (§2.10/§2.10.1)

Problem: `PaneBorder` only carried a single on/off `Style`, so a split's inner shared edge
between siblings couldn't be selectively suppressed — every pane in a split either drew its
own full box or none at all, with no way to express "no divider between these two children."

Fix: `PaneBorder` gains a `PaneBorderEdges(Top, Right, Bottom, Left)` record; `ResolveBorderPropagation`
computes which edges each split's children keep — for a horizontal split each child keeps its
outer edges but drops the shared inner vertical (`Left`/`Right` between neighbours), and
symmetrically for a vertical split's `Top`/`Bottom` — recursing into nested splits via an
`InheritedBorderDirective` so a grandchild inherits its ancestor's suppression instead of only
its immediate parent's. `PaneBorderRenderer.Wrap` draws each glyph/corner conditionally on its
own edge, with a horizontal run that always spans the full padding+content width regardless of
which corners are present, so `outerWidth` comes out right in every edge-on/off combination
without a junction table. `PaneBorderEdges.All` is the default for a non-split, non-nested pane.

Judgment calls (implementor-flagged, not spec gaps I'm reporting upward): (1) the `inside`
border shorthand config key propagates edge suppression only one level of nesting, not
recursively through arbitrary split depth — narrower than the general `InheritedBorderDirective`
mechanism, called out as a deliberate scope-limit rather than an oversight; (2) §2.10's
"Degrade squeeze" paragraph (what happens when a suppressed-edge pane is *also* squeezed below
`MinUsableWidth`) was left untouched — out of scope for this task, open as a question for #8c
or a follow-up.

Merge-conflict resolution: this branch predated #16/#20 and diverged further, so `main`'s
`git merge --no-ff task-8b` reported genuine conflicts (unlike #16/#20's silent auto-merges) in
two places: `Config.cs`'s `ConfigLoader.ResolveTopLevel` factory call, where `main` (#29) and
`task-8b` had each added a different new positional argument to the same `ResolvedConfig(...)`
constructor call (`surfaceMaxRows` trailing vs. `resolvedBorder.Edges` mid-list) — resolved by
combining both into the record's already-merged declared order; and `PaneBorderRenderer.Wrap`,
where `main`'s `omitEdges` (#29's height-budget top/bottom suppression under a 3-row budget) and
`task-8b`'s per-edge `Top`/`Right`/`Bottom`/`Left` conditionals both rewrote the same row-building
block — resolved by composing them: a top/bottom row draws only when its edge is both configured
on *and* not forced off by `omitEdges`. A stale `HeightLadderTests.cs` call to `PaneBorder`'s
old 2-arg constructor (missing the now-required `Edges` param) surfaced as a compile error after
the conflict markers were resolved; fixed to pass `PaneBorderEdges.All`, matching the convention
already used in every other test file `task-8b` touched.

Full suite: 1252/1252 passed, independently verified via task-gopher after conflict resolution
and again after the `HeightLadderTests.cs` fix. Landed as merge commit `5663b04` (branch tip
`e616781`), pushed.

### #23: distribute "even", accept "greedy" explicitly (§2.3)

Problem: §2.3 declares three `distribute` values (`greedy | min-rows | even`), but the parser
only recognized `min-rows` — every other token, including the spec's own recommended-by-name
`even` (§2.4: the layout that "holds still"), silently fell through to the `greedy` default.
`even` was therefore unimplemented in practice: it produced the same reflowing behavior it
exists to avoid.

Fix: `PaneDistribute` gains an `Even` case; `PaneDistributeParsing.Accepted` gains both
`("greedy", Greedy)` and `("even", Even)` as real tokens instead of `greedy` being an unlisted
fallthrough default. `SizeResolver` gets a genuine third arm — `ResolveVerticalEven`/
`AllocateEvenOnePass` — dispatched in `ResolveNode`'s switch alongside greedy/min-rows, not
bolted on: fixed/percent panes take their width first (same step order as the other two
policies), then all content/fill candidates split what remains equally, ignoring intrinsic
measurement and the content/fill distinction entirely — that's the point, not a simplification,
since content-independent widths are what keeps the layout from moving. A content pane still
degrades under §2.6 at whatever width it lands on.

Judgment call (implementor-flagged): leftover remainder cells go to the leftmost candidate,
matching the spec's own six-step algorithm and the existing `AllocateOnePass` step-6
fill-distribution convention, rather than an apparently-superseded earlier §2.3 prose sentence
that says the last child absorbs it. `minSize`/`maxSize` are deliberately not read by
`AllocateEvenOnePass` at all — "even fixes the extent, not the content." The over-constrained
drop-loop (no non-fixed child may resolve below 1 cell → drop last child, retry) applies to
`even` too, mirroring `ResolveVerticalMinRows`'s structure, treated as split-wide per §2.3
rather than gated to one distribute policy.

Merged cleanly (git auto-merged `Pane.cs`/`SizeResolver.cs`, no conflicts), independently
verified via task-gopher both pre-merge on the worktree and post-merge on `main`. Full suite:
1256/1256 passed. Landed as merge commit `4e09333` (branch tip `894e4ea`), pushed.

### #49: `--preview --json` `rows[].width` reuses `PaneRow.Width` (§9.3.4)

Problem: `--preview --json`'s non-panel rowsJson branch computed `rows[].width` independently as
`text.Length` on the final rendered line, while `rows[].contentWidth` reused the layout's own
`PaneRow.Width` — two computations of the same metric on the same row that could drift apart.

Scope correction: the task was originally issued as "fix `rows[].width` to use display width
instead of UTF-16 code-unit length," which the implementor caught and stopped on before writing
any code — SPEC-V2-FRAMEWORK.md §2.4 rule 3 and §13/§13.1 deliberately keep wcwidth measurement
out of scope for v2 and mandate `Plain.Length` as the width metric everywhere, including for CJK,
ZWJ sequences, and combining marks; reversing that would touch §2.7's parity baseline and §10
bullet 3, not just this call site. Redirected to the actual defect within scope: `rows[].width`
and `rows[].contentWidth` should agree, both as `Plain.Length`, since they're describing the same
row.

Fix: `rows[].width` now reuses `row.Width` (`new PreviewRowJson(text, row.Width, row.Width)`)
instead of recomputing `text.Length`. The three Panel-branch `text.Length` sites
(`jsonRenderingPanel == true`) are untouched — no `PaneRow` exists for Spectre's Panel-drawn
border decoration there.

Side finding, not fixed here: while investigating, `--preview`'s rendered pane structure turned
out to be unaffected by the config's `pane` (split/children/items) section — split config, a
minimal single-item config, and `"{}"` all render the identical single-bordered panel, differing
only in item values. Logged as new backlog item #54; not part of this fix.

Merged cleanly (git auto-merged `Program.cs`, no conflicts), independently verified via
task-gopher post-merge on `main`. Full suite: 1256/1256 passed. Landed as merge commit `3d1f8c3`
(branch tip `ff93889`), pushed.

### #54: `--preview` split-pane config — test-fixture bug, not a rendering defect

Problem (as logged from #49): `--preview` appeared to ignore the config's `pane`
(split/children/items) section entirely — split config, a minimal single-item config, and plain
`"{}"` all seemed to render the identical single-bordered panel.

Root cause: not a `--preview`/`ResolveRootPane` defect at all. `UserConfig` has no top-level
`Pane` property — the documented schema is always `{"surface": {"pane": {...}}}` (confirmed
against SPEC-V2-FRAMEWORK.md §2.2 and other spec examples). `PreviewJsonRowsTests.cs`'s
split-pane fixture used a bare top-level `{"pane": {...}}` instead of nesting it under
`"surface"` — `System.Text.Json` silently ignores unrecognized properties, so `config.Surface`
stayed null and `ResolveRootPane` always fell back to its default single-leaf-pane branch. The
CLI itself was never broken: a correctly-nested config renders a genuine multi-pane split (verified
directly — a real 2-column side-by-side render with matching corner glyphs on one row).

Fix: corrected the fixture's nesting in
`Preview_SplitPaneConfig_EveryRowCarriesItsOwnWidthMatchingItsText` so it actually exercises the
split pipeline, and added `Preview_SplitPaneConfig_WidthAndContentWidthAgreeSinceBothComeFromTheSameLayoutValue`
against the corrected fixture — real regression coverage for #49 that didn't exist before, since
the original fixture's assertions were content-agnostic. A repo-wide scan for the same
bare-top-level-`"pane"` mistake found no other occurrences.

Judgment call (implementor-flagged, not acted on): no JSON schema strictness was added to reject
unrecognized top-level keys (which would have caught this fixture bug at parse time) — a real
hardening idea, but out of scope for this fix; left as a suggestion rather than implemented.

Merged cleanly (git auto-merged `PreviewJsonRowsTests.cs`, no conflicts), independently verified
via task-gopher pre-merge on the worktree and post-merge on `main`. Full suite: 1257/1257 passed.
Landed as merge commit `c115b03` (branch tip `01611d4`), pushed.

### #50: named self-reference now classifies as `placeholder-self-reference`, not the generic fallback

Problem: two independent code paths detect argv-placeholder issues. `CheckArgvPlaceholders`
(`ConfigCheck.cs`) already caught *bare* self-references (`{}`) correctly via
`ArgvPlaceholders.HasSelfReference`. But a *named* self-reference — an item's own id used as its
own placeholder, e.g. item `"cmd"` referencing `{cmd}` — went through `CheckReferences`'s
id-reference pipeline instead, which had no way to tell "references itself" from "references a
different command item": it only saw the referenced id was in `CommandItemIds` (trivially true,
since the item includes itself) and emitted the generic `placeholder-command-source` diagnostic.

Fix: added a nullable `OwnerId` field to `IdCandidate`/`IdReference` (`ItemValueResolver.cs`),
populated only by the argv-placeholder extractor with the referencing item's own id — every other
extractor (from/link/color-from/etc.) leaves it null, so no other diagnostic path is affected.
`CheckReferences` now branches before the `CommandItemIds` check: when the reference is an argv
placeholder and its id equals its own `OwnerId`, it emits `placeholder-self-reference` instead of
the generic code. Bare-`{}` handling is untouched and doesn't double-fire, since `ReferencedIds`
already excludes bare `{}`.

Judgment call: `OwnerId` was added as a generic field on the shared `IdCandidate`/`IdReference`
types rather than a self-reference-specific side channel, since those types are already shared
across all reference extractors — the smaller, single-source-of-truth fix over duplicating
`CommandItemIds` lookup logic inside the argv-placeholder check itself.

New regression test: `ArgvPlaceholderNamedSelfReference_ReportsPlaceholderSelfReferenceNotCommandSource`
(item `"cmd"` with command `{cmd}`, asserts `placeholder-self-reference` fires and
`placeholder-command-source` does not); confirmed the existing bare-`{}` and legitimate
cross-reference tests still pass unchanged.

Merged cleanly (git auto-merged `ConfigCheck.cs`/`ItemValueResolver.cs`/`ConfigCheckTests.cs`, no
conflicts), independently verified via task-gopher pre-merge on the worktree and post-merge on
`main`. Full suite: 1258/1258 passed. Landed as merge commit `a2b3f01` (branch tip `7a628ba`),
pushed.

### #45: `AcceptedCommand` gains `height`, closing the drift from `CheckEnums` (§1.1.3 follow-up)

Problem: `ConfigCheck.cs`'s `CheckEnums` (via `CheckPaneEnums`/`CheckItemEnums`) validates
`height` as an enum kind, but #42's `AcceptedCommand` key table (backing `--accepted --json`) had
9 rows and never included it — added after `height` was already a checked enum, so the two lists
drifted out of lockstep.

Fix: added `new("height", PaneHeightParsing.AcceptedTokens, null)` to `AcceptedCommand.Build()`,
reusing the parser's own `AcceptedTokens` (`["content", "fill"]`) directly, matching the
zero-copy convention already used for the other 8 rows — `size` remains the sole exception
(`accepted: null` + `alsoAccepted`). The doc comment's stale "eight parser-colocated registries"
count corrected to nine.

Verified against the live built binary (no committed `--accepted` subprocess test existed to
extend, unlike #42's assumption): `--accepted --json` now emits the `height` row
(`{"key":"height","accepted":["content","fill"],"alsoAccepted":null}`) alongside the other 9,
confirmed byte-for-byte. `AcceptedCommandTests.cs` updated: the exact-key-count test now expects
10 keys for 9 enumerable kinds plus `size`, and the same-object-as-parser's-`AcceptedTokens`
assertion gained a `height` case.

Judgment call (implementor-flagged, not acted on): a structural drift-prevention fix — a test
asserting `AcceptedCommand`'s key set matches whatever `CheckEnums` actually validates — was
considered and declined as out of scope. `CheckEnums`'s validated keys are inline string literals
scattered across three methods with no existing registry to introspect; building one is a real
design decision (reflection over diagnostic codes? an explicit shared registry?), left as a
suggestion for the architect rather than built here.

Merged cleanly (git auto-merged `AcceptedCommand.cs`/`AcceptedCommandTests.cs`, no conflicts),
independently verified via task-gopher pre-merge on the worktree and post-merge on `main`. Full
suite: 1258/1258 passed. Landed as merge commit `dc25b42` (branch tip `b739fad`), pushed.

### #8c: collapse:true compositor border grid (§2.10/§2.10.2)

Problem: with `collapse:true`, adjacent panes sharing a boundary must draw one shared border —
tee/cross junctions where three or four panes meet, not each pane independently drawing its own
overlapping box (the `collapse:false` behaviour already shipped in #8b).

Fix: new `BorderGrid.cs` (335 lines) builds a per-cell 4-bit NESW mask grid once per render via
its own top-down tree walk, derives the correct junction glyph per border style from a 16-entry
table keyed by the mask, and resolves each interior boundary as a 0-2-contributor convex hull of
row-spans — ties broken by first-requester-in-tree-declaration-order. `PaneTreeRenderer.cs`
changed so every pane draws its own uncontested edges through the ordinary `PaneBorderRenderer.Wrap`
path, using an "effective" border with excludeLeft/excludeRight falsified for whichever side is
charged to a shared boundary instead; every shared vertical-split boundary becomes a synthetic
1-column contribution spliced into the split's children list via absolute (row,col) grid lookups,
with `rowStart`/`colStart` threaded top-down through the walk. `HeightLadder.cs`/`Program.cs`:
`collapse` is threaded through only to the final render call, not through the ladder's row-count
measurement passes (those don't care about junction glyphs). `Program.cs`'s new parameter is named
`collapseBorders` specifically to avoid colliding with the pre-existing `PaneCollapse.Collapse`
name (§2.11 empty-pane pruning) — same word, unrelated concept.

`SizeResolver.cs`/`Config.cs`/`ConfigCheck.cs` needed no *new* changes for #8c itself: their
collapse-aware `BoundaryCost`/`Floor` overloads, `BorderConfig.Collapse`/`ResolvedConfig.Collapse`,
and collapsed-edge-conflict diagnostics were written earlier for #8a/#8b but were sitting
uncommitted in the worktree. Since collapse cannot function without them and they were squarely
in scope, the implementor committed them alongside #8c rather than leaving them stranded.

Disclosed scope cuts (implementor-flagged, none silently dropped): only vertical-split interior
boundaries are collapsed — horizontal-split/row boundaries and the outer surface edge are out of
scope for this pass; `gutter>1` centering and boundary-level degrade under collapse are not
implemented; when sibling panes have different resolved heights (e.g. one `fill`, one shorter
`content`-sized), their borders don't land on the same rows so the bottom boundary isn't a clean
tee in that row — known gap, not fixed, not tested, and deliberately avoided in the implementor's
own test config (which uses two `fill` children to sidestep it).

Merge note: `task-8c` had branched before #49/#54/#50/#45 landed (12 commits behind
`origin/main` at merge time), yet `git merge --no-ff` auto-merged cleanly — `ConfigCheck.cs` and
`Program.cs` were touched by both sides but with no overlapping hunks, no conflict markers.
Independently verified via task-gopher both pre-merge on the worktree (1263/1263, against its
own older base) and post-merge on `main` (1265/1265, build 0 warnings/0 errors). Landed as merge
commit `070cc7c` (branch tip `ac4b634`), pushed.

### #43: §1.1.2 docs⊆registry subset check (`--accepted --json`)

Problem: README and SPEC-V2-FRAMEWORK.md quote literal accepted-value tokens (`"rounded"`,
`"vertical"`, etc.) in prose and tables with nothing tying them back to the registry `--accepted
--json` (#42) exposes — a doc can drift to naming a token the parser no longer accepts, or lose a
key's row entirely, with nothing to catch it. §1.1.2 itself left the extraction mechanism as
NEEDS-EVIDENCE: a naive doc-wide scan for backtick-quoted literals sweeps JSON example fences
(`"overflow": "wrap"` appears dozens of times as illustrative example, never as an assertion) with
no way to tell the two apart.

Design (cdtui-architect, `SPEC-1.1.2-resolve-extraction-rule.md`): mark the README's pane-keys
table with an in-band `<!-- pane-token-table: ... -->` comment, the same self-describing-anchor
pattern `check-examples.sh` rule C already uses on README.md:168 — scan only marked tables, never
prose, never fenced blocks. Within a marked table: a backtick-fenced token is a key name if bare,
a checkable value if additionally double-quoted — a convention the table's rows already followed
unprompted, so extraction needed zero heuristics and zero hand-listing. A row's key column may
name more than one key (`minSize`/`maxSize`); every quoted value in that row must be accepted by
every key it names. The checkable set per key is `accepted` only, not `accepted ∪ alsoAccepted` —
`alsoAccepted` is a prose description (`AcceptedCommand.cs:8`, rendered via `FormatAccepted`), not
a token list, so unioning it in would mean comparing tokens against an English sentence. Keys with
`accepted: null` (currently only `size`) are skipped and named as skipped, not silently passed.

Fix: new `tools/check-doc-tokens.sh` (189 lines) implements the above, wired into `check-all.sh`
alongside `check-docs.sh`/`check-examples.sh`. README changes: added the marker; retitled the
table's "accepted values" column heading to "notes" (that heading was an equality claim the table
never made — only some rows enumerate exhaustively); moved `border.style`'s six literals out of a
now-deleted prose sentence and into the table as their own row, so they're checked for free;
added a blockquote sentence pointing at `--check` as the actual completeness authority (the
existing blockquote there only promised to report *unrecognised* values, a narrower claim than the
retitled heading needs). SPEC-V2-FRAMEWORK.md: replaced the two closed NEEDS-EVIDENCE items with
a section stating the resolved extraction rule, added verification items 7-11, and converted the
`split`/`colorSystem` member lists that were hand-copied in prose into `§2.3`/`§6.2` citations
(matching `distribute`'s existing citation) — removing the redundant copies is load-bearing for
the design, not cosmetic: the checkable-table approach only stays honest once the SPEC stops
carrying its own separately-driftable copy of the same literals.

Deliberately left uncovered, named in the spec rather than silently skipped: the overflow ASCII
sketch (`Overflow   wrap | truncate | overflow`) isn't a table and isn't marked — no mechanism
scans it. `height` (added since #45) has no README row at all yet; subset semantics permits the
omission so the check must not (and does not) fail on it, but it's a real doc gap — tracked
separately as task #55, not folded into #43.

Judgment call (implementor-flagged, not acted on): the original dispatch assumed README already
documented `--accepted --json` following an existing pattern used for `--check`/`--items`/
`--preview`/`--colors`. No such pattern exists anywhere in README — the only occurrence of those
four flags is a stale "not built yet" blockquote at README.md:58. That premise was wrong going in;
adding `--accepted`'s own README documentation is left as a follow-up rather than invented here
without direction.

Verified red-path as well as green: injecting an unaccepted literal into a table row flipped the
check to exit 1 with the correct file:line; injecting a real-but-omitted-by-brevity token kept it
at exit 0 with the count rising by one, confirming subset (not equality) semantics; removing the
marker flipped it to exit 1 rather than silently reporting clean having compared nothing.

Merged cleanly, independently verified via task-gopher both pre-merge on the worktree (1258/1258)
and post-merge on `main` (1265/1265, build 0 warnings/0 errors, `check-all.sh` all four checks
green including `check-doc-tokens`: 18 tokens checked, 0 disagree). Landed as merge commit
`9be1e23` (branch tip `947c2d0`), pushed.

### #21: §9.4.2 unknown-key diagnostic, derived from the config types

Problem: a config key that doesn't exist in the schema at all (a typo, a stale key from an older
version) was silently ignored — System.Text.Json's default binding drops unrecognized JSON
properties on the floor with no diagnostic, unlike the tier-1 closed-value-set checks (§9.4/§9.4.1,
#45) which fire when a *known* key gets an invalid value.

Fix: `[JsonExtensionData] Dictionary<string, JsonElement>? Extra` added to all 10 object-bound
config classes in `Config.cs` (all 10 now explicitly `[JsonSerializable]` on `ConfigJsonContext`,
up from 3), capturing whatever the schema doesn't bind. New `ConfigChecker.CheckUnknownKeys`
(`ConfigCheck.cs`) walks the *raw* `UserConfig` graph rather than the resolved tree — extension
data only survives pre-resolution — and derives each type's known-key set from
`ConfigJsonContext.Default.<Type>.Properties`, never reflection or a hand-maintained list, matching
the "derive it mechanically" principle §38/#45 established. Emits `Warning`/`unknown-key`
diagnostics, wired in last in the existing diagnostic sequence so nothing already emitted shifts.
`ColorExprJsonConfig` excluded — it's never object-bound directly, so it has no extension-data slot
to walk. New `KeySuggestion.cs`: Levenshtein distance plus §9.4.2's own suggestion rule (distance
≤2 AND under half the key's length, OR a prefix relation; smallest distance wins; nothing suggested
if no candidate qualifies).

Confirmed §9.4.2's "strip extension data before re-emitting config" concern is N/A here — grepped
`JsonSerializer.Serialize` + `ConfigJsonContext` across `src/`+`tests/` and found no config
re-emission path exists anywhere in the codebase. §12's requirement that its own gate surface these
warnings is explicitly a requirement on §12, not on this diagnostic — left untouched, flagged as a
possible follow-up if §12 needs it tracked separately.

Real finding, not spec-related: `BorderConfig.Shorthand` (populated only by
`BorderConfigConverter`, never bound from real JSON) does **not** get excluded from
`JsonTypeInfo.Properties` by `[JsonIgnore]` on the .NET 10 runtime in use here, contrary to
expectation — confirmed empirically, not assumed. Worked around with an explicit name filter in
the known-keys derivation, pinned by a new guard test
(`BorderConfig_KnownKeySet_ExcludesShorthand`) so a future runtime/SDK change fails loudly instead
of silently reintroducing a false "unknown key" warning on `shorthand`.

Spec-erratum finding, non-blocking (implementor-flagged, routed to architect separately): two of
§9.4.2's own worked examples don't actually satisfy its own algorithm pseudocode, which the spec
itself designates authoritative over the examples — an `"aa"` vs `["ab","ac"]` tie-break example
that doesn't clear the suggestion threshold, and a `maxLines`→`maxRows` suggestion example at edit
distance 4 (over the ≤2 threshold). Implemented per the algorithm as written; tests use examples
that actually exercise the intended behavior, with inline comments documenting the discrepancy for
whoever fixes the spec prose.

No scope ambiguity — §9.4.2 read directly rather than from paraphrase, per standing instruction,
and was unambiguous as written.

Merged cleanly, independently verified via task-gopher both pre-merge on the worktree (1291/1291)
and post-merge on `main` (1291/1291, build 0 warnings/0 errors, `check-all.sh` all four checks
green). Landed as merge commit `dfa6b4e` (branch tip `b0a0c72`), pushed.

### #57: §9.4.2 suggestion-ranking fixes — tie-break (D1), prefix-vs-distance (D2), prefix length floor (D3)

Problem: an architect review of #21's shipped `KeySuggestion.cs` (triggered by a since-retracted,
false spec-erratum report — see #21's entry above) surfaced three real defects in the
edit-distance suggestion algorithm, none of which the original §9.4.2 implementation task had
specified against:

- **D1 — tie-break undefined.** `distance < bestDistance` picked the first candidate strictly
  less than the running best, so a genuine tie fell to whichever key came first in
  `JsonTypeInfo`'s source-gen property order — an order with no defined stability guarantee.
  Confirmed live, not theoretical: an exhaustive edit-distance-1 mutation search over every real
  config type's known-key set found 54 real ties on `PaneConfig` alone (e.g. `"xalign"` ties
  `align`/`valign`).
- **D2 — prefix rule defeated by distance ranking.** §9.4.2's prefix relation is supposed to catch
  truncations like `ttl`→`ttlSeconds` (edit distance 7, far outside the ≤2 threshold) as a second,
  independent qualifying rule — but `Suggest` ran both rules into the same "smaller distance wins"
  comparison, so a distance-≤2 candidate could beat a genuine prefix match and undermine the whole
  reason the prefix rule exists.
- **D3 — prefix rule had no length floor.** With no minimum, `{"c": 1}` prefix-matched
  `case`/`color`/`colors`/`colorSystem`/`children` off a single character. Confirmed reachable:
  `{"c":1}` does reach `CheckUnknownKeys` via `UserConfig.Extra`/`[JsonExtensionData]`, now pinned
  by a test.

Rulings — D1 and D3 routed to the user (product-judgment, not mechanical), D2 resolved by the
architect alone (a direct logical inconsistency in the spec's own stated principle, no ambiguity):

- **D1**: on a genuine tie, name *all* tied candidates, no cap, joined via the existing
  `ConfigCheck.FormatAccepted` (already used for `unknown-enum-value`), sorted with
  `StringComparer.Ordinal` specifically — not culture-sensitive default string ordering, which
  would reintroduce build-to-build nondeterminism.
- **D2**: a prefix match always outranks a distance match, unconditionally. Within prefix matches,
  the shortest candidate wins; within distance matches, the smallest edit distance wins.
- **D3**: the prefix rule now requires `Math.Min(unknown.Length, candidate.Length) >= 3`. Not
  `> 3` — `"ttl"` is exactly 3 characters and is the motivating example; an off-by-one here would
  have silently broken it again.

Fix: `KeySuggestion.Suggest` now returns `IReadOnlyList<string>` instead of a single `string?`;
`ConfigCheck.cs`'s call site reuses `FormatAccepted` to join multi-candidate results into the
diagnostic message. §9.6's diagnostic JSON shape needs no change — the suggestion is interpolated
into the message string only (`ConfigCheck.cs` ~line 781), never a structured field, and the spec
now explicitly prohibits adding one.

Also corrected: the misleading #21-era test comments alleging a spec-example-vs-algorithm
discrepancy. The architect confirmed §9.4.2 contains no worked examples of the suggestion
algorithm at all — four sentences of prose, nothing more — so the prior report was investigating a
document that doesn't exist; the comments asserting otherwise were fixed rather than left as
durable misinformation.

Merged cleanly, independently verified via task-gopher both pre-merge on the worktree and
post-merge on `main` (1298/1298, build 0 warnings/0 errors, `check-all.sh` all five checks green:
check-citations 115/9 files, check-counts 10 files, check-notes 2 distinct, check-examples
16 items/10 files, check-doc-tokens 18 tokens/0 disagree). Landed as merge commit `209ace6`
(branch tip `7da31bb`), pushed.

### #24: §2.3.2 key-not-applicable diagnostic

Problem: §2.3.2 defines `key-not-applicable` as a predicate — *is this key read on this node,
given its resolved shape?* — not an enumerated list of key/context pairs. Three cases read
directly off the assembled `Pane` tree: `distribute` on a horizontal split, `gutter` on a
horizontal split, and `items` alongside a non-empty `children`. A fourth case, "children on a
leaf," was flagged mid-implementation as a genuine scope ambiguity rather than guessed at: §2.3.2's
prose mentions it, but it doesn't parse cleanly against the leaf/children invariant at
`Config.cs:788` (non-empty `children` implies non-leaf) — so on the assembled tree, a leaf never
carries a non-empty `children` to begin with. Routed to the architect rather than resolved by
assumption.

Ruling (architect): the fourth case is real, but not for the reason first guessed. `Config.cs:795`
(`NormalizeSplit`) sends any pane with non-empty `children` and no `split` key to `Vertical` — so a
pane carrying `children` is a leaf only when that list is explicitly *empty* (`children: []`). The
trigger is `pane.Children is { Count: 0 }` (matches non-null-and-empty only; an absent key doesn't
match) — the same leaf definition already used at `ConfigCheck.cs:486`/`:517`, not a new one. The
load-bearing wrinkle: `PaneConfig.Children` is `List<PaneConfig>?`, but `ResolvePane`
(`Config.cs:642`) collapses null and empty to the same `Array.Empty` on the assembled tree — so
whether the author wrote an explicit empty `children: []` key only survives in the *raw* `UserConfig`
DTO. Unlike the other three cases, this one has no assembled-tree representation to read at all.

Fix: new `ConfigChecker.CheckKeyNotApplicable` in `ConfigCheck.cs`, wired in after
`CheckLeafOnlyKeysOnSplits`, covering all four cases per §9.6.1's registry and the rest of §2.3.2 —
the first three read the assembled `Pane` tree (mirroring the existing pattern), the fourth reads
the raw `UserConfig` via `WalkRawPanes` in the same method, since it has no assembled-tree source.
`Config.cs:764`'s `ParseSplitCore` widened `private`→`internal` for reuse. Guards against
double-flagging with `unknown-enum-value` per §9.4.1's "one condition, one code" principle — a
misspelled `split` (e.g. `"vertikal"`) with non-empty `children` parses to `None`, normalizes to
`Vertical`, stays a container, so `key-not-applicable` is correctly unreachable there; only
`unknown-enum-value` fires.

Three follow-ups the architect flagged during the ruling, explicitly non-blocking for this merge,
tracked separately (not filed as numbered backlog items yet — see next orchestration pass):
diagnostic-code coherence (`leaf-only-key-on-split`, `border-inside-on-leaf`, and now
`key-not-applicable` are three separate codes for what §2.3.2 frames as one predicate, worth
checking against §9.6.1's registry before deciding whether to unify); a fifth case falls out of the
same mechanism (`{"split": "vertical", "items": [...]}` with no `children` silently drops back to a
leaf, `items` does nothing, currently unflagged); and a low-priority §9.4.2 prose erratum (a
misspelled `split` does not actually collapse the statusline as the spec's example claims — the
pane stays a container, just on the wrong axis).

Merged cleanly, independently verified via task-gopher both pre-merge on the worktree (1304/1304)
and post-merge on `main` (1311/1311, build 0 warnings/0 errors, `check-all.sh` all five checks
green). Landed as merge commit `25ff0ab` (branch tip `898acc3`), pushed.

### #55/#56: document `height` pane key and `--check`/`--accepted --json` CLI usage in README

Problem: two documentation gaps, both discovered as byproducts of earlier tasks rather than
planned work. #55 — the `height` pane key (real, implemented, covered by #7's shrink-wrap work)
was never added to README's pane-keys table; #43 spun it out as a known gap rather than scope-creep
into it while doing the docs⊆registry subset check. #56 — `--accepted --json` (added in #42, used
internally by `check-doc-tokens.sh` since #43) was never itself documented as a public CLI command.

Fix: #55 added a `height` row to the pane-keys table (`"content"`/`"fill"` (default), values taken
directly from `PaneHeightParsing.Accepted`), placed next to `maxRows` as the height-axis sizing key,
mirroring how `size` sits next to `minSize`/`maxSize`. Verified against the built binary via
`check-doc-tokens.sh` (18→20 tokens checked, 0 disagree).

#56 surfaced a real structural gap while in progress, correctly not guessed at: README has no
CLI-flag documentation section at all — every existing mention of `--check`/`--items`/`--preview`/
`--accepted` was either the stale "not built yet" blockquote near line 58, one passing sentence, or
a non-reader-facing HTML tooling marker. Decided directly (a doc-shape call, not a product/
architecture one — no escalation needed): added a new `### CLI` section documenting `--check`
(plain and `--json`) and `--accepted --json`, all examples copy-pasted from the built binary's real
output rather than hand-typed. `--items`/`--preview` get a one-line pointer at the existing
blockquote rather than full documentation — kept out of scope. The blockquote's staleness for
`--check` (confirmed working since #21/#43/#24) was flagged but deliberately left untouched, filed
separately as task #61.

Merged cleanly, independently verified via task-gopher both pre-merge on the worktree and
post-merge on `main` (1311/1311, build 0 warnings/0 errors, `check-all.sh` all five checks green,
20 doc tokens checked). Landed as merge commit `74a3b38` (branch tip `dc7c168`), pushed.

### #59: §2.3.2 fifth key-not-applicable case — childless explicit split

Problem: the architect's #24 ruling flagged a fifth case falling out of the same predicate as a
non-blocking follow-up: `Config.cs:794` (`NormalizeSplit`) drops an explicit `split` key with no
(or empty) `children` back to a leaf. The task was originally briefed as "an inert `items` key
alongside a childless `split`" — that framing was wrong and corrected before implementation
started.

Correction (architect, traced `Config.cs:794-795` directly): when `childCount == 0`, it's `split`
itself that gets normalized to `PaneSplit.None` and dropped — `items` is untouched and renders
normally on the resulting leaf exactly as intended, so flagging `items` would have been a false
positive. `split` is the actually-inert key. Consequently the trigger widens: since nothing about
`items` being present is relevant, the diagnostic fires on *any* childless explicit `split`
(`{"split":"vertical"}` alone, no `items` at all, is equally inert), not just ones that also carry
`items`. None of the four existing key-not-applicable cases gate on an unrelated second key being
present either, so this keeps the same shape.

Fix: `ConfigChecker.CheckKeyNotApplicable` (`ConfigCheck.cs`) gained a fifth case, appended last:
predicate `ParseSplitCore(pane.Split) is PaneSplit.Vertical or PaneSplit.Horizontal &&
pane.Children is not { Count: > 0 }`, warning `key-not-applicable` at `{path}/split`. Reuses #24's
existing `ParseSplitCore` helper, which gives the double-flagging guard against `unknown-enum-value`
for free — a misspelled split value parses to `null`, not `Vertical`/`Horizontal`, so it's
correctly excluded and `unknown-enum-value` keeps sole ownership of that case.

Product-judgment call, routed to the user rather than guessed: `{"split":"vertical","children":[]}`
now trips two independent warnings — `/children` (from #24's case) and `/split` (from this case) —
since both keys are independently inert from the same underlying mistake. **Ruling: emit both**,
no suppression logic; consistent with #24's "one condition, one code" precedent, and each warning
is independently true and actionable at its own path.

Also updated: the three previously path-unqualified `DoesNotContain(key-not-applicable)` assertions
in `ConfigCheckTests.cs` (~1052, 1128, 1172) were widened by #59's new case, so they're now scoped
to their specific paths (`/surface/pane/distribute`, `/gutter`, `/items`) rather than asserting
absence of the code anywhere in the diagnostic list.

Flagged, not acted on: §2.3.2's prose ends "...and whatever the next one is" with no per-case
enumeration to extend, and a fifth case now genuinely exists — worth tightening once #58 (the
diagnostic-code coherence follow-up, which now also covers this prose question) is resolved.

Merged cleanly, independently verified via task-gopher both pre-merge on the worktree (1319/1319)
and post-merge on `main` (1319/1319, build 0 warnings/0 errors, `check-all.sh` all five checks
green, 20 doc tokens checked). Landed as merge commit `d18226c` (branch tip `f361860`), pushed.

### #60/#61: §9.4.1 misspelled-split erratum location, README `--check` blockquote staleness

**#60** was briefed against §9.4.2, but the erratum text it was meant to fix ("misspelled `split`
turns a container into something that is not a container... half the statusline disappears") is
actually in **§9.4.1**, lines 4551-4552 — a bullet in the "unknown keys" silent-failure example
list. Fixed at its real location, not the briefed one. New phrasing: "A misspelled `split` reverts
to the default vertical axis; the pane stays a container with all its children intact, and the
difference is a silently wrong axis rather than a vanished one" — matches the sibling bullets'
"reverts to X... difference is A rather than B" convention. Added a pinning test,
`PaneTests.cs::UnrecognizedSplitWithChildren_StaysAContainerOnTheDefaultAxis` (split:"diagonal" +
2 children → asserts `Split == PaneSplit.Vertical`, `Children.Count == 2`), since nothing existing
covered this exact resolved-axis behavior.

**#61** fixed the stale README.md:58 blockquote claiming the CLI flags were "not built yet... will
tell you so and stop." Independently verified (not assumed) that `--items` and `--preview`, not
just `--check`, are also fully implemented and tested (Program.cs handlers, `CommandProviderTests.cs`,
`PreviewJsonRowsTests.cs`) — the stale premise applied to all three flags. Updated to point at the
new [CLI](#cli) section (added in #56) instead. Whether the `/migrate`/`/edit`/`/revert` slash
commands are themselves fully wired to that CLI was correctly left out of scope — the blockquote's
"which" grammatically refers to the CLI flags, not the slash commands.

Merged cleanly, independently verified via task-gopher both pre-merge on the worktree (1312/1312)
and post-merge on `main` (1320/1320, build 0 warnings/0 errors, `check-all.sh` all five checks
green, 20 doc tokens checked, 0 disagree). Landed as merge commit `faec247`, pushed.

### #48: RunPreview non-happy-path test coverage

#35 established a subprocess-CLI test pattern for `--preview --json` but only exercised the happy
path. Added `tests/ClaudeTuiLine.Tests/PreviewCliTests.cs` (same `RunCli`/`WriteTempConfig` pattern
as `PreviewJsonRowsTests.cs`), closing 7 previously-untested `RunPreview` branches (`Program.cs:269-478`):
config parse-error → exit 3 (both `--json` and bare stderr), `--config` pointing at a missing file →
exit 3 (both forms), bare non-JSON output shape (rendered rows to stdout, the "preview at N columns"
+ synthetic-stdin note to stderr), and the `--columns`-omitted fallback chain (`COLUMNS` env var,
then default 100 when that's also unset — `RunCli` gained an optional env-override dict to make this
deterministic against the host's own `COLUMNS`). No `RunPreview` behavior changed — tests only.

Flagged, not chased: `RunPreview`'s real (non-synthetic) stdin path (`Program.cs:296-346`,
`ParseInput`/`StatusInput`) is still untested — `RunCli` always closes stdin immediately, so covering
it needs a new subprocess helper that writes to the child's stdin plus `StatusInput`'s JSON shape.
Filed separately as #63 rather than folding into #48's scope.

Merged cleanly, independently verified via task-gopher both pre-merge on the worktree (1327/1327)
and post-merge on `main` (1327/1327, build 0 warnings/0 errors, `check-all.sh` all five checks
green, 20 doc tokens checked, 0 disagree). Landed as merge commit `54d20a4`, pushed.

### #62: §2.3.3 verification — SolveMinRows's floor-fallback vs. AllocateOnePass step 4 (no code change)

Investigation only, no code changes. Confirmed equivalence: for a content-kind pane with no
`MinSize` set, `Floor(p)` (`SizeResolver.cs:307-339`) resolves to `p.MinSize ?? 0`, exactly the
`minSize` `AllocateOnePass` step 4 (`:381-397`) clamps against. In the fully over-constrained case
(`Σ minSize_i > budget`), the per-pane cap telescopes below its own floor for *every* candidate
simultaneously (not just some), which is algebraically identical to `SolveMinRows:625`'s `return lo`
(every candidate at `Floor(candidates[ci])`). The existing `SizeResolver.cs:574-581` comment already
states this correctly, citing `AllocateOnePass`'s step 4 by name — no tightening needed.

One adjacent, unconfirmed wire flagged in passing (not verified as a defect, filed separately —
see below): §2.3.3 describes the over-constrained fallback as surfacing the `pane {n} dropped`
note (§9.8.1), but neither `AllocateOnePass` nor `SolveMinRows` itself emits it — the drop-retry
loop lives in the outer caller `ResolveVerticalMinRows` (`:493-518`), which takes no
`RenderNoteCollector` parameter at all. Whether that path actually surfaces the note was outside
what #62 asked; filed as #64.

### #63: RunPreview real-stdin path test coverage

Traced `ParseInput`/`StatusInput` (`Program.cs:783-798`): malformed or unparseable stdin JSON
returns an all-null `StatusInput`, silently, no exception. `usedSynthetic` is decided purely by
whether raw stdin was blank (`Program.cs:308`) — not by whether it parsed — so malformed-but-nonempty
stdin is a third branch distinct from both "no stdin" and "valid stdin", previously untested by #48.

Added `RunCliWithStdin(string stdin, params string[] cliArgs)` to `PreviewCliTests.cs` (same
process-launch structure as the existing `RunCli`, but writes to `StandardInput` before closing
instead of closing immediately — `RunCli` can never exercise this path since closed/empty stdin is
indistinguishable from "no stdin given" to `RunPreview`). Two tests: real JSON stdin drives
rendering and is not treated as synthetic; malformed-but-nonempty stdin parses to an empty
`StatusInput` rather than falling back to the synthetic fixture.

Merged cleanly, independently verified via task-gopher both pre-merge on the worktree (1329/1329)
and post-merge on `main` (1329/1329, build 0 warnings/0 errors, `check-all.sh` all five checks
green, 20 doc tokens checked, 0 disagree — one intermittent flake seen on the first post-merge run,
`Preview_SplitPaneConfig_EveryRowCarriesItsOwnWidthMatchingItsText` failed on a JSON parse error,
did not reproduce across 3 immediate reruns; filed as #65). Landed as merge commit `b793b7b`, pushed.

### #58: §2.3.2 diagnostic-code coherence — census, not unification

Investigated whether `leaf-only-key-on-split` / `border-inside-on-leaf` / `key-not-applicable` are
really one predicate wearing three codes, per the follow-up filed during #24. Verdict: **do not
unify.**

- `border-inside-on-leaf` isn't the same predicate at all — `"border":"inside"` on a leaf pane IS
  read (it silences the border); the repair is "pick a different value," not "delete the key." A
  different repair means a different code, per the project's own established split rule.
- `leaf-only-key-on-split` genuinely is the same predicate as `key-not-applicable` (both are
  inert-key-delete-it cases) — the one real collapse candidate — but ruled against anyway on a
  compatibility basis: §9.6 makes a shipped diagnostic code's meaning permanent, so retiring it
  would change what code an existing config emits under `--check --json` today. It also carries
  message specificity (naming `overflow`/`ellipsis` non-inheritance) a generic code would blur.

What landed instead (doc-only, `SPEC-V2-FRAMEWORK.md`, no `.cs` changed): §2.3.2's "...and whatever
the next one is" replaced with an explicit five-case census (distribute-on-horizontal,
gutter-on-horizontal, items-with-nonempty-children, empty-children, childless-split — keeping the
"predicate, not a closed list" framing per §9.4.1), plus prose explaining why the other two codes
stay separate. §9.6.1's `key-not-applicable` table row updated to name all five cases (was stale at
three since #24/#59 landed without updating it). Census-to-code mapping verified 1:1 against
`ConfigCheck.cs`'s actual emissions.

Flagged, not chased: the childless-split normalization behavior (`Config.cs:795`) has no section
number citing it normatively in the spec — filed separately as #66 rather than inventing one.

Merged cleanly, independently verified via task-gopher both pre-merge on the worktree (1327/1327)
and post-merge on `main` (1329/1329, build 0 warnings/0 errors, `check-all.sh` all five checks
green, 20 doc tokens checked, 0 disagree). Landed as merge commit `0c51fce`, pushed.

### #66: childless-split normalization was already normatively specified (§2.2)

The gap flagged during #58 — no section citing `Config.cs:795`'s childless-split
normalization (a pane with `split` set and no children drops the stray `split`
and is treated as a leaf) — turned out not to be a gap. §2.2 ("The pane tree",
`SPEC-V2-FRAMEWORK.md:778-781`) already states it normatively, word for word:
"Conversely a pane with `split` set and no children is a leaf, and the stray
`split` is dropped." §9.4.1's misspelled-split bullet already cited it
correctly; the actual hole was in §2.3.2's five-case census (added in #58),
whose two childless/empty-children bullets cited nothing. Added `(§2.2)` to
both. Doc-only, no code changed.

Merged cleanly as the first of three sequential merges onto `main` this round
— see the #25 entry below for the combined merge commit and verification
numbers (#66, #64, and #25 landed together as `Merge #66` → `Merge #64` →
`Merge #25`).

### #64: min-rows drop-retry now emits the `pane {n} dropped` note

`ResolveVerticalMinRows`'s drop-retry loop silently truncated panes via
`.Take(Count-1)` with no `RenderNoteCollector` at all — unlike greedy's
`AllocateWithDrop`, which emits a `pane {n} dropped: no width remained at {w}
columns` note per §9.8.2. §2.3.3:1220-1222 says the note should fire for
min-rows too, so this was a real gap (flagged during #62), not just untested.
Fixed: threaded `RenderNoteCollector notes` through `ResolveVerticalMinRows`
and its call site, added the note-emission with the same message/position
convention as `AllocateWithDrop`. New test `MinRowsDropNoteTests.cs` pins it,
regression-proved (stashed the fix, confirmed the test fails without it).

### #25: fixed `SolveMinRows`'s `maxT` bound; threaded both test seams; emitted the min-rows drop note

Per the Architect's ruling (spec: `SPEC-2.3.1-min-rows-seam-and-bound.md`):

- **Real spec-defect fixed**: `maxT` was `CandidateSegments(...).Count` (raw
  segment count), which is wrong per §2.3.3:1209-1215 — an item wider than the
  pane wraps, so 3 items can need 6 rows. Too-low `maxT` made the binary/linear
  T-search exit early and fall to the `return lo` floor-fallback, silently
  dropping every candidate to its floor on exactly the narrow terminals
  min-rows exists for. Now `maxT = max over candidates of
  RowCountAt(candidate, Floor(candidate))`. **User-visible**: this changes
  rendered output for real min-rows configs on narrow terminals.
- Binary-search optimization **not adopted** — N1 evidence (p90 latency 0.065ms
  vs a 12.6ms budget across widths 100-240, ~190x margin) shows the corrected
  linear scan already meets budget comfortably, so the added complexity/risk
  wasn't justified.
- Two seams threaded to their correct single sites: `measureOverride` reaches
  only `WaterFillSurplus`'s content-candidate surplus cap (the `Resolve`
  overload's doc comment, which falsely claimed broader reach, is now
  accurate); a new `rowCountOverride` (`Func<Pane,int,int>?`) reaches
  `RowCountAt` for min-rows's own monotonicity test.
- Min-rows gets its own test set in `MinRowsDistributeTests.cs` (4 new tests:
  maxT regression against brute force with wrapping content, silent-drop note
  assertion, measureOverride seam-reach, rowCountOverride monotonicity) rather
  than reusing the three greedy fixpoint tests, which §2.3.1:1135-1136 says
  don't apply — min-rows deliberately has no fixpoint. Packer-invocation-count
  bound re-derived from `1..300` to `1..100` (measured 16 post-fix).
- SPEC-V2-FRAMEWORK.md spliced in two places: §2.3.1's T-bound paragraph
  (correcting the §2.3.3 contradiction) and §10 req 6 (amending for min-rows's
  own test set).
- **New finding, flagged not fixed, filed as #67**: `SolveMinRows`'s
  over-constrained fallback (`return lo`) doesn't check `Σlo <= r` before
  accepting — 2+ fill/content candidates whose floors sum past
  `splitOuterWidth` get over-allocated instead of triggering a drop. Reproduced
  directly (two fill panes, gutter 1, r=46, both floor 24 → both granted 24,
  sum 48 > 46). Whether greedy's `AllocateOnePass` has the same gap is
  unconfirmed — routed to the Architect.

**Merge**: #66, #64, and #25 landed as three sequential `--no-ff` merges onto
`main` (`faec247`... `→` `Merge #64` `→` `Merge #25` at `0266d03`). #25 and #64
both threaded a note-emission fix into the same function
(`ResolveVerticalMinRows`) independently, producing a real merge conflict —
resolved by hand in favor of #25's superset signature (which also carries the
`measureOverride`/`rowCountOverride` params), keeping #25's more detailed
§9.8.2/§2.3.3 comment. Post-merge full-suite run at `0266d03` hit one
`PreviewJsonRowsTests` failure with the same signature as the #63-era flake
(`JsonReaderException`, `'/' is an invalid start of a value`,
`PreviewJsonRowsTests.cs:77`) — unrelated to #25's SizeResolver-only change
surface. 3 immediate reruns all clean (1334/1334 each), confirming it's the
known #65 flake recurring rather than a regression — logged as a data point
for #65, not re-filed. `check-all.sh`: 115 citations resolve, all counts
match, notes pinned, 20 doc tokens checked, 0 disagree. Pushed —
`main` is now at `0266d03`.

### #26: §2.5.1 (one spawn per render; content panes export no width) — both rules already held

Verified rather than fixed: both halves of §2.5.1 already hold structurally.
Spawning happens exactly once per render — `Program.cs`'s `ItemValueResolver.
ResolveAsync` runs before `SizeResolver`'s multi-pass fixpoint even starts, so
there's no code path that could re-spawn across passes; a fill/fixed pane's
width comes from the *previous* render's stamped value via `ItemCache`, the
spec's sanctioned stale-but-safe caching. Content-sized panes never get
`PANE_WIDTH`: `ItemValueResolver.cs:141`'s eligibility check
(`!SizeResolver.IsContentSized(pane)`) short-circuits before the widths cache
is even consulted. Closed two real gaps: no test pinned that an ineligible
pane's env var stays unset even with a stale width sitting in the cache under
the same key (added); §2.5.1 was cited three times elsewhere but never at the
actual decision point (`ItemValueResolver.cs:141`) — added the citation there.

Merged cleanly (no conflicts). Verified via `cdtui-worker` pre-merge (1330/1330)
and post-merge on `main` (1335/1335, `check-all.sh` clean). Landed as merge
commit `0d6cf62` (pushed).

### #65: fixed the flaky PreviewCliTests/PreviewJsonRowsTests interference

Root-caused the intermittent `JsonReaderException` that hit #63's and the
#66/#64/#25 post-merge verifies. Both test classes fell back to `dotnet run
--project ... -c Release -v quiet` whenever `CLAUDE_TUI_LINE_BIN` was unset;
xUnit runs the two classes in parallel by default, so concurrent `dotnet run`
invocations raced on the shared `obj`/`bin` tree, and a losing MSBuild
diagnostic (MSB3030, leading with an absolute path) could leak onto the
child's stdout ahead of the JSON payload. Reproduced 2 of 3 tries when forcing
concurrent cold builds.

Fix: the test project's `ProjectReference` to `src/ClaudeTuiLine` already
copies a runnable pre-built apphost (binary name `claude-tui-line`, lowercase)
into the test output directory — no build step needed at all. Consolidated
four near-identical CLI-runner helpers into one `PreviewCliRunner` that
resolves that binary once and execs it directly. Folded in a second fix:
concurrent `ReadToEndAsync()` draining of stdout/stderr before `WaitForExit()`
(replacing sequential draining, which risked a pipe deadlock on high-stderr
failures). Added `[CollectionDefinition("PreviewCli", DisableParallelization =
true)]` on both test classes, matching the existing `MinRowsDistributeTests`
pattern, as defense in depth.

One spec deviation, flagged not silently resolved: the original spec asked
the JSON-parse helper to also fail on non-zero exit code, but that would break
two legitimately-passing tests that expect exit 3 *with* valid JSON on stdout
— a genuine spec self-contradiction against its own "no test assertions
change" rule. Implemented parse-failure-only diagnostics; exit-code checking
was not added.

Two related risks noted, not touched here — filed as #68 (`ConfigTests.cs`
sets process-wide env vars via `Environment.SetEnvironmentVariable`, a similar
leak risk in a different file) and #69 (`ItemCache.cs` shares fixed temp paths
across concurrent CLI subprocesses, incidentally mitigated as a side effect of
this fix but not directly addressed).

Merged cleanly (no conflicts). Verified via `cdtui-worker`: pre-merge 3x full
runs clean (1329/1329 each), post-merge 2x full runs clean (1335/1335 each),
`check-all.sh` clean. Landed as merge commit `4d35daf` (pushed).

### #67 / #70 (#67a): SolveMinRows and AllocateWithDrop both had unbounded
over-allocation

§2.3.1 (min-rows) and §2 (greedy) each had a fallback path that granted panes
more total width than was available, with no check catching it: `SolveMinRows`'s
over-constrained fallback returned `lo` without verifying Σgrants stayed within
`avail`, and `AllocateWithDrop`'s `tooSmall` check explicitly exempted
`SizeKind.Fixed`, so two `size:30` panes in a 46-column split both got granted
30 with no drop and no note.

Fixed in `ResolveVerticalMinRows`'s drop-retry loop (#67, `cdtui-impl3`) and in
`AllocateWithDrop` (#70/#67a, `cdtui-impl4`) with the same shape: recompute
`avail` via `BoundaryCost` fresh each iteration (gutter count falls with each
drop), add an `overAllocated = grants.Sum() > avail` check alongside the
existing `tooSmall` check, and keep dropping until neither fires. #67 also
caught a real regression in its own pre-existing #25 test — two unclamped
`size:500` fixed panes now correctly get dropped instead of silently
overflowing, since the new Σgrants≤avail guard fires on the min-rows side too.

New tests: `MinRowsDistributeTests.cs` (5 cases per the architect's
SPEC-2.3.1-min-rows-floor-sum.md §7, including a byte-identical feasible-path
check and a per-iteration-BoundaryCost boundary case) and
`FixedSizeOverAllocationDropTests.cs` (greedy repro: two `size:30` panes in 46
columns, gutter 0).

Not in scope here, filed separately: #67b (drop predicate `grants[i] < 1` vs
a floor-based threshold — product call, Jim approved changing to floor-based,
architect wrote SPEC-2.3-drop-predicate.md, dispatch pending) and N1/#72
(compositor behavior on an over-width single-pane residual — unobserved,
needs a render-level check).

Merged cleanly (#70's `SizeResolver.cs` edit auto-merged alongside #67's).
Verified via `cdtui-worker`: pre-merge full suites clean for both (1340/1340
and 1336/1336), post-merge full suite clean (1341/1341), `check-all.sh` clean.
Landed as `Merge #67: 8c55dee` → `Merge #70/#67a: 473b01b` (pushed).

### #68: ConfigTests.cs env var leak risk — investigated, not real today

Checked whether `ConfigTests.cs`'s process-wide `HOME`/`CLAUDE_TUI_LINE_CONFIG`
mutation (one test method, `ConfigPathOverride_Unset_FallsBackToHomePath`) could
race with another concurrently-running xUnit test class, the same shape as #65's
flake. Confirmed no code change was needed: the mutating method restores both
vars in a `finally` and is the only ambient-env-reading call site in the entire
suite — every other test across 10+ files that touches config/cache/telemetry
resolution passes an explicit path/override instead of relying on ambient
`HOME`. `ConfigTests.cs` also carries no `[Collection]` attribute, so its own
methods already run sequentially. No merge — working tree left clean, no diff.

### #69: ItemCache.cs temp-path collision risk — investigated, not real today

Checked whether `ItemCache.cs` sharing fixed temp paths across concurrent CLI
subprocesses could corrupt or lose writes, the same shape as #68. Confirmed no
code change needed: `Write`/`WriteWidth` already stage to a GUID-suffixed temp
file in the cache dir, then `File.Move(..., overwrite: true)` — an atomic
rename since .NET 6, not an in-place write — and `TryRead`/`TryReadWidth`
treat any failure as a cache miss rather than an error, per §7. Cache
filenames are per-key (hash of id+argv+cwd+width/env), so concurrent writers
to different keys never collide on a target file, and concurrent writers to
the same key each stage independently and race only on the final rename —
documented-intentional last-write-wins at the granularity the value actually
has.

New test: `ItemCacheConcurrencyTests.cs` (32 concurrent writers to the same
key never produce a torn/mixed read; 32 concurrent first-writers to distinct
keys under a not-yet-existing cache dir all succeed) — passes against
unmodified `ItemCache.cs`. `ItemCache.cs` itself left byte-for-byte unchanged.

Merged cleanly alongside #27 (see below). Verified via `cdtui-worker`:
pre-merge full suite clean (1337/1337), post-merge full suite clean covering
both #69 and #27 together (1343/1343), `check-all.sh` clean. Landed as merge
commit `0309fea` (pushed as part of `9f68ea9`).

### #27: §2.4.1/§2.6 — ellipsis marker now honors the pane's own inner width

§2.4.1 (per-row trim condition for a stacked composite sibling's per-row
background) needed no fix: `PaneContribution.HasBackground` is hardcoded
`false` at every construction site in the codebase, and no config/item type
anywhere exposes a background-color property, so the failure mode §2.4.1
describes has no live caller today. Flagged as forward-looking only — will
need revisiting if/when background-color config lands.

§2.6 (vertical ellipsis budget) had a real gap: `PaneAssembler.ClipRows` had
no `innerWidth` parameter, so a narrow pane's marker-only row could exceed
the pane's own inner width — contradicting the rule that the marker is
budgeted against inner width on both axes, mirroring
`PaneRenderer.TruncateSegment` on the horizontal axis. Fixed by threading
`innerWidth` through; when the ellipsis string is empty or `>=` innerWidth,
the marker is dropped entirely and every `cap` row keeps real content instead
of wasting the last row on an unbudgeted marker.

Open question routed to `cdtui-architect`, not resolved here: whether
`ClipRows`'s full-row-replacement approach (dropping the last surviving row's
own content entirely rather than splicing the marker into its trailing
cells) satisfies §2.6's "ends with the marker" language, or whether a true
splice is required — blocked on `PaneRow` carrying no plain-text field to
splice against today (only opaque `Markup`). Not blocking this merge; a
follow-up task if the architect rules a splice is required.

New tests in `HeightLadderTests.cs` (marker-too-narrow-for-innerWidth,
empty-ellipsis), plus the existing ellipsis test confirmed still green (no
regression on the common case). Merged cleanly on top of #69's merge (only
`PaneAssembler.cs` + `HeightLadderTests.cs` touched, no conflict). Verified
via `cdtui-worker`: pre-merge full suite clean (1337/1337), post-merge
covered together with #69 above. Landed as merge commit `9f68ea9` (pushed).

**Architect ruling on the open question above**: full-row-replacement is not
spec-compliant. §2.6's "ends with the marker" means the marker replaces the
row's last *cells*, never the whole row — impl2's diff is correct and stays
(it fixed a real §2.4 shearing bug independent of this), but the splice
itself is a follow-up, deliberately sequenced after the §3.1 block-model work
below rather than built against the current row-production shape twice.

### #13: remote-url probe now cached under a TTL

`RemoteUrl.Probe` reuses `ItemCache`'s existing atomic-write machinery
(`ItemCache.KeyFor`, `TryRead`/`Write`, the same `CacheEntry` record) rather
than inventing a second caching mechanism, with a 30s TTL matching
`CommandProvider.DefaultTtlSeconds`'s precedent. Cache key folds in `cwd`
alongside the id. Touched `RemoteUrl.cs`, `Program.cs`,
`RemoteUrlTests.cs` — 91 insertions, 5 deletions. Verified via `cdtui-worker`:
pre-merge and post-merge build+full suite (1349/1349)+`check-all.sh` all
clean. Landed as merge commit `24357b4` (pushed).

### #39: §12.6.11 compare-and-branch — investigated, no code change

Rule has three parts: (1) compare a re-read config against the first read and
branch, (2) tell the user what moved, (3) apply the same discipline to the
MCP server's stale-revision refusal (§12.6.5) so a naive retry can't defeat
it. Parts 1+2 are already fully implemented, verbatim, in
`commands/edit.md` step 5 (the client/prompt layer `/claude-tui-line:edit`
follows). Part 3's target — an MCP server implementing `set_config` and
§12.6.5's baseRevision check — does not exist anywhere in this repo; the
C# codebase is CLI-only (render/validate/preview), and every
`set_config`/`stale-revision` hit is spec/doc text, not implementation. This
is Phase-7 MCP-tools territory (task #10) — holding #39 until that server
exists to implement rule 3 against. No falsification-style test written
against a server that isn't built.

### #47: §2.11.3/§6.6.1 citations confirmed stale — routed to architect

Not a line-number fix: both sections' *mechanism* claims are wrong about
current code, not just their line refs. `SafeLoadAll` no longer exists (split
into `LoadRenderConfig` + `BuildFallbackConfig`, Program.cs:804/869). §9.2.1
bypasses the Pane/collapse/render pipeline entirely for all 3
"config unreadable" rows — `RunAsync` prints a raw diagnostic and returns
before any Pane reaches the renderer, contradicting §2.11.3's "non-empty
reason-pane survives collapse" mechanism and §6.6.1's "always routes through
Panel" claim. One narrow exception found and flagged, not treated as a live
bug: the catch-all `try/catch` around `ResolveTopLevel`/`ResolveRootPane`
still renders a collapse-eligible fallback pane through the ordinary
pipeline, but no user-reachable config value was found that triggers that
catch. Both sections cross-reference each other's now-wrong claims, so
rewriting coherently is a spec-authoring call — routed to `cdtui-architect`
rather than guessed at. No code or spec changes made.

### #76: `split` key's horizontal/vertical convention documented

`"split": "horizontal"` is genuinely ambiguous by naming convention (could
read as "panes arranged horizontally," tmux `-h`/CSS `row` style, or
"divided by a horizontal rule," which is what this codebase actually does —
children stack top to bottom). Architect ruling: not a bug, not to be
changed — changing it now would silently invert every existing config.
Documented explicitly in README.md's pane-keys section instead, with a
worked example of each value. Doc-only, no behaviour touched. Verified via
`cdtui-worker`: build+full suite (1349/1349)+`check-all.sh` clean pre- and
post-merge. Landed as merge commit `7b55390` (pushed).

### #71/#67b: drop predicate is now floor-based, not `< 1`

Both drop-retry loops (`AllocateWithDrop`, `ResolveVerticalMinRows`) now test
`grant < DropFloor(pane, grant, ...)` instead of `grant < 1`, per
SPEC-2.3-drop-predicate.md. `DropFloor` returns `MinUsableWidth` (20) when
the pane would suppress its border, else its ordinary `Floor`. Drop notes
split into two reason-selected messages — below-floor keeps the existing
floor wording, over-allocation gets a new "children need {sum} columns at
{w} columns" message; when both conditions hold in one iteration,
over-allocation wins as the stated cause (§4). Both new note-text forms
pinned in SPEC-V2-FRAMEWORK.md §9.8.1. New
`tests/ClaudeTuiLine.Tests/DropFloorPredicateTests.cs`, including an item-2
invariant test (architect-revised): on the min-rows path every surviving
grant is `>= Floor(i)`, pinning the fact that `SolveMinRows`'s infeasible
fallback structurally cannot produce a below-floor grant.

Item 4 (§3(b) suppress-then-test boundary) was explicitly skipped per prior
agreement — turned out to matter: the architect found `ShouldSuppressBorder`
testing outer width instead of the spec's inner width is a live regression
this change introduced (suppression on the drop path currently never changes
any outcome). Follow-up fix tracked as #73, dispatched immediately.

Verified via `cdtui-worker`: pre-merge failed check-all.sh once (new note
text unpinned), fixed and re-verified clean — build+full suite
(1356/1356)+`check-all.sh` clean pre- and post-merge. Landed as merge commit
`41abc03` (pushed).

### #46: cite §1.1.3 instead of reopening size's undecomposed exemption

Investigation found SPEC-V2-FRAMEWORK.md §1.1.3 had already ruled, deliberately,
that `size`'s accepted-values exemption stays undecomposed. Architect confirmed:
don't reopen without a genuinely new fact — reopening would require amending
§1.1.1/§1.1.2/§1.1.3 together and flipping `AcceptedCommandTests.cs`'s
byte-identical-string test. Fix is a one-line comment in
`src/ClaudeTuiLine/AcceptedCommand.cs` citing §1.1.3 so the next reader doesn't
re-litigate it. Verified via `cdtui-worker`: build+full suite (1349/1349)
+`check-all.sh` clean pre- and post-merge. Landed as merge commit `972ae80`
(pushed).

### #36: emit the §9.3.1 synthetic fixture from the binary (§12.3.1/§12.7.1)

New `--fixture` CLI mode (mutually exclusive with the other six modes) and
`SyntheticFixture.WithRealCwd(string cwd)`, so `migrate.md` step 6, `setup.md`
step 5, and `revert.md` step 7 now consume `<binary> --fixture` instead of a
literal two-field payload — keeping the docs' fixture in lockstep with the
binary's own definition. New pin test
`WithRealCwd_MatchesInputInEveryFieldExceptCwd`. Flag name (`--fixture`) chosen
by the implementor without explicit spec text authorizing it (spec only ever
said "a flag") — noted for review, not blocking. Verified via `cdtui-worker`:
build+full suite (1350/1350, then 1357/1357 combined with #46)+`check-all.sh`
clean pre- and post-merge. Landed as merge commit (pushed alongside #46).

### #77: clean up 3 stale "no width remained" note-text occurrences

Three note-text references outside §9.8.1's pinned set were stale from #71's
drop-predicate change: §9.3.4's `migrate.md` quote example (now shows both
current drop-note forms), §9.6's JSON example (`notes[].message` updated to
current wording), and §9.8.2's self-referential claim, which needed a rewording
(not a pure substitution) since the sentence asserted no code could emit
§9.6's own JSON-example message — a claim that would go live-false once §9.6's
example was corrected. Reworded to explicitly past tense ("at the time this
section was written..."). Worktree was rebased onto post-#71/#76 main before
editing so the branch read current wording, not stale history. Verified via
`cdtui-worker`: build+full suite (1356/1356, then 1357/1357 post-merge)
+`check-notes.sh`+`check-doc-tokens.sh`+`check-all.sh` clean pre- and
post-merge. Landed as merge commit `8f4f14e` (pushed).

### #75: SPEC-3.1 multi-row command block model

`PaneAssembler.cs`'s `RenderItemRows` now classifies each resolved item as a
block (multi-line, per the D2 split rule) or packed (single line), per SPEC-3.1
D1-D8. A block flushes the current packed group, renders each of its own lines
independently through the unmodified `RowLayout.Wrap`, and is appended with no
separator/blank row on either edge (D4/D6, no separator confirmed by Jim).
Wrapping a long single-line item never promotes it to a block, and a block's
own line still wraps if too wide (D5). New `SplitBlockLines(string)` helper
implements D2 exactly: strip one trailing `\n` (+ a `\r` immediately before
it), split, `TrimEnd('\r')` each line — `"foo\n"` is 1 line (not a block),
`"foo\n\n"` is 2 lines (a block, with a real blank row preserved). Surfaced and
fixed a pre-existing leak along the way: a lone trailing-`\n` single line was
pushing raw untrimmed text into the packed segment, leaking a literal `\n`
into row markup. `RowLayout.Wrap` itself, `RenderLeaf`'s §2.5.1 cache-key
tuple, and valign — all untouched, as required. §6's single-concatenation-site
obligation (for #27's future splice) is `RenderItemRows`'s `rows` list, fed
from exactly two call sites — confirmed PaneRow-typed, not Segment-backed;
architect confirmed this is sufficient for #27, spec's "Segment-backed"
phrasing wasn't load-bearing. 10 new tests in `PaneAssemblerBlockTests.cs`
covering SPEC-3.1 §8's verification items 1-9. Verified via `cdtui-worker`:
build+full suite (1359/1359, then 1367/1367 post-merge)+`check-all.sh` clean
pre- and post-merge. Landed as merge commit `c4b93d2` (pushed).


### #73: border-suppression predicate fix (§1-§4)

Merged to main (commit 2a7def0). Fixes SizeResolver.ShouldSuppressBorder to guard on innerWidth>=MinUsableWidth instead of the prior outer-width-based check; DropFloor now computes preSuppressionInnerWidth using its own excludes. PaneTreeRenderer.cs:43 now passes innerWidth instead of node.OuterWidth. New tests/ClaudeTuiLine.Tests/BorderSuppressionPredicateTests.cs covers SPEC-2.3-suppression-predicate.md §8 items 1,2,3,5,6. Full suite 1372/1372 green, verified independently by cdtui-worker pre- and post-merge.

Item 7 (§8) — whether a suppressed border's content reclaims the reserved column space, or the current outer-minus-reserve design stands as documented — is still open, routed to the architect alongside #74 (same PaneBorderRenderer.Wrap exit sites). Task #73 stays in_progress until that's ruled.

### #19: §10.1 blank-surface controls for layout tests

Added blank-surface controls (BlankSurfaceControl.cs helper: Blank(ItemContext) and BlankValues(dict)) to 17 qualifying tests across 7 files (SplitAcceptanceTests, HeightLadderTests, NarrowSplitPaneTests, EvenDistributeTests, BorderGridTests, MinRowsDistributeTests) to assert layout invariants hold independent of item content, per §10.1's distinguishability floor. Many candidate tests were vetted and excluded as vacuous (no items, content-irrelevant assertions, or structurally incompatible with blanking). Surfaced a real product bug in the process, filed separately as #80 (bordered pane with 0 content rows renders zero rows instead of an empty box).

### #31: §4.0.1 maxLines opt-in per-item cap

Removed the prior unconditional 1-line stdout cap for command items; maxLines is now a pure opt-in ceiling with no default, per §4.0.1's explicit "Default: no cap" and the §3 erratum retiring the single-line stdout reading. Implementation reuses #75's SplitBlockLines (D2-compliant trailing-newline strip, general multi-line splitting) for both the block-model path and the newly-uncapped output, confirmed safe against embedded-newline and trailing-newline edge cases by direct empirical test through the production render path. New CheckMaxLines diagnostic rejects maxLines <= 0. Render note on truncation: `item '{id}' emitted {n} lines; {kept} kept (maxLines)`.

### #27: §2.4.1 + §2.6 per-row trim condition and vertical ellipsis budget

RowLayout.Wrap rewritten with a one-row lookahead (PackRow/Compose helpers) per SPEC-3.1-block-model.md Amendment A3: on the row that would exceed the cap, the unit re-packs and splices a truncation via SegmentTruncation.Truncate when content overflows or a marker is required. PaneAssembler gained a RenderUnit(Segments, Rows) record and ApplyRowBudget (replacing ClipRows): finds the smallest unit where cumulative rows meet the cap, keeps everything before it, re-invokes that unit with the remaining budget and markerRequired:true, drops everything after. PaneRenderer.cs is now a thin passthrough; SegmentTruncation.cs (new) extracts the truncation/wrap helpers per §9.4. Merged as 25facff.

### Test-isolation fix: CommandProviderTests fixed-tempdir cache collision

CommandProviderTests.MaxLinesSet_OutputExceedsCap_TruncatesAndEmitsPinnedNote passed on a cold cache and failed deterministically on every subsequent run against the same commit — not a #65-style concurrency flake, but a #69-style fixed-temp-path defect: the test passed `cacheDir: Path.GetTempPath()` with a fixed id, so run 1 executed live (truncated, emitted the note, wrote the cache), and every run after served from cache (correct value, no note, because the note-emission code path never re-ran). Fix: added an `IsolatedTempDir([CallerMemberName] testName)` helper and applied it to this test's cacheDir/widthsDir (the only test in the file that asserts on notes, so the only one that noticed the shared-tempdir defect). CommandProvider.cs untouched — test-only change. Test renamed to `...TruncatesAndEmitsMaxLinesNote` (nothing in src/ emits a "pinned" note). Merged as c304a86.

### #74: residual over-width pane clamp

Added `ClampToAvail(AllocResult, avail, splitOuterWidth, RenderNoteCollector)` at SizeResolver.cs's two drop-loop exits (`AllocateWithDrop`, `ResolveVerticalMinRows`), applied strictly after the drop decision, fixing the over-width residual-pane render overflow from N1/#72. `ResolveVerticalEven` deliberately left untouched (tracked separately as #78). Render note: `"pane {n}: {requested} columns requested, clamped to {avail} at {splitOuterWidth} columns"`, pinned in SPEC-V2-FRAMEWORK.md §9.8.1. Merged as 44a3955.

### #73 (defect B): border-suppression content-width reclaim

Per SPEC-2.3-suppression-predicate.md's ruling on item 7 (§8, N1): a suppressed border's content reclaims the reserved column space rather than the prior outer-minus-reserve design standing undisturbed. PaneBorderRenderer.cs and PaneTreeRenderer.cs updated accordingly. Initially merged, then reverted after a false #31×#73b interaction suspicion (later diagnosed as an unrelated CommandProviderTests fixed-tempdir defect, see above) — re-verified clean against current main and re-merged as c0ef530.

### #78: `ResolveVerticalEven` parity fix

Per SPEC-2.3-even-split-parity.md, `ResolveVerticalEven` was the one of the three width-allocation-with-drop loops (`AllocateWithDrop`, `ResolveVerticalMinRows`, `ResolveVerticalEven`) never brought up to parity across #71/#67b's `DropFloor` predicate, #67a's over-allocation check, and #74's `ClampToAvail`. Ported all five: threaded `RenderNoteCollector`, replaced the hardcoded `Grants[i] < 1` check with `DropFloor` using `AllocateWithDrop`'s collapse-aware form (confirmed correct: `AllocateEvenOnePass` does consult `collapse`, via `BoundaryCost`), added the over-allocation check with drop-note wording byte-identical to the siblings, and wrapped the exit in `ClampToAvail`. `AllocateEvenOnePass` itself untouched. Also added a structural tripwire test asserting all three loops emit drop notes with the shared wording, so a future fix landing in two of three loops (as happened here) fails a test instead of shipping silently. 1409/1409 tests pass. Merged as 6ddd558.

### #80: bordered pane with 0 content rows renders an empty box, not nothing

Per SPEC-2.8.2-height-suppression-empty-content.md: `PaneTreeRenderer.cs`'s height-suppression `Wrap` call site now derives `omitEdges` from `heightSuppressed && (contentRows.Count > 0 || !budgetFitsBox)` instead of gating both edge rows on `heightSuppressed` alone — a bordered pane with zero content rows and room for the 2-row box now renders the empty box rather than vanishing (there's nothing to reclaim the edge rows for). `ClipRows: 1` and `ClipRows: 0` budgets are unchanged (the box genuinely doesn't fit). `heightSuppressed` itself and the natural-height/`ClipRows` predicates it's derived from are untouched — `contentRows` is read strictly downstream, no feedback into the suppression decision itself. This also fixes a `minSize`-protected pane vanishing through this path despite `ConfigCheck.cs:784`'s "author said so, always wins" rule, since height suppression previously consulted no collapse-protection state at all. Merged as 4fb14ad.

### #96: stacked split's missing border-reserve subtraction

Per SPEC-96-stacked-child-width-reserve.md: `SizeResolver.ResolveNode`'s stacked (`PaneSplit.Horizontal`) branch granted every child the parent's full `outerWidth`, never subtracting the parent's own border reserve — unlike its vertical sibling, which does via `BoundaryCost`. Not a missing clamp (the three drop-loop sites — `AllocateWithDrop`, `ResolveVerticalMinRows`, `ResolveVerticalEven` — were audited and found already correct, clamping via `ClampToAvail` per #74/#78); the stacked branch performs no allocation at all, so it had no exit for a clamp to sit at. Fix: `stackedAvail = Math.Max(0, outerWidth - OwnBorderReserve(pane))`, passed down to every stacked child. Root cause of the reported "statusline's right side clips/overlaps" symptom — reachable for any bordered stacked split since #88 (`split:"flex"`), triggered visibly when a longer model+effort string (e.g. "Opus 5 : high") pushed `sideBySideNeed` past the width and flex flipped to stacked, activating the latent defect on every child of that split at once. Merged together with #97 below (SPEC-97 §0: #97 subsumes #96's arithmetic rather than replacing it).

### #97: a stacked child honours its own `size`/`minSize`/`maxSize`

Per SPEC-97-stacked-child-size.md and the Ultra-Advisor ruling in SPEC-97-AMENDMENT-framework-ruling.md (approved with corrections). Prior to this, a stacked child's declared size was silently ignored — every child got the same full-width grant. `SPEC-V2-FRAMEWORK.md` §2.3.2 stated this normatively ("its children each span the full width and stack downward"); the code was faithful to the framework, not lagging it, so per SPEC-92's precedent the framework was amended first: §2.3.2's premise replaced (independent sizing within the inner width, not full-width spanning) while its conclusion (`distribute`/`gutter` stay inert on a horizontal split) is preserved and re-derived from the new premise; new §2.3.5 states the rule; §2.3.4's flex-floor rationale prose sharpened ("can each fit within it", not "take all of it" — the floor invariant is unchanged, since `Floor`/`StackedFloor` already honour `size`/`minSize` and neither is touched by this change); the split-definition bullet and the split-floor "divides sums, shares maxes" rationale paragraph got the same-shape premise repair (two additional sites the Ultra-Advisor's grep found that the initial amendment missed). New `SizeResolver.StackedWidth(child, avail, ...)`: a pure per-child function — fixed/percent/content/fill resolved against the ceiling (never a shared budget; stacked children occupy different rows and never contend for a column, so there is no allocation pass, no drop loop, and `Σ grants > avail` is not a defect). Declared bounds (`minSize`/`maxSize`) clamp before the ceiling, not after — reversing the order would let an over-ceiling `minSize` re-inflate the grant and reopen #96's overflow. `ConfigCheck.cs`'s `CheckHorizontalSplitChildren` had a stale comment citing "§2.8" for horizontal width allocation as unimplemented; §2.8 is Height, not width — no such section existed to land, and the comment (which nobody caught since `check-citations.sh` can't see a wrong citation to a section that resolves fine) is corrected. Behaviour change, not purely additive: `size` on a stacked child was never documented as inert, so a config that set it now renders differently (a config with no stacked `size`, or `size:"fill"`, is byte-identical — the `_ => avail` fill arm is exactly the pre-existing grant). Blast-radius sweep (NE-2) found one such config: Jim's own `~/.claude/claude-tui-line.json` sets `size:"content"` on the flex pane's first child — this pane now renders at its measured content width when stacked, instead of the parent's full (bugged) width. 1627/1627 tests pass (12 new: SPEC-96 T1/T2/T4/T6, SPEC-97 S1/S2/S3/S5/S6/S7/S9/S14 in `SplitFlexTests.cs`; one pre-existing test, `V7_ContentLeaves_StackWhenCombinedNaturalWidthExceedsW_JimsShape`, updated — it had pinned the pre-#96 buggy grant of 97 for a content leaf; corrected to 60, the measured-and-capped value).

### #10: Phase 7 MCP tools (§12.6, Amendment A2)

New `src/ClaudeTuiLineMcp/` project (official `ModelContextProtocol` SDK, spawn-not-link — never links core in-process, spawns the CLI subprocess for `--check`/`--items`). `get_config`/`set_config` tools with CAS via `revision`/`baseRevision`; stale-write refusal returns the current config+revision rather than a bare error code (§12.6.11's re-derive correction). Full §9 checkpoint mechanism (`BackupLedger`/`BackupLedgerFactory`): validate-then-checkpoint-then-write ordering, every artifact captured regardless of trigger, collision-safe naming. Found and fixed a real atomicity bug via TDD: `BackupLedger.AppendLine` used `FileMode.Append`, which in .NET is seek-then-write rather than POSIX `O_APPEND` — a torn final ledger line would get a new entry glued onto its tail with no separator, corrupting both; fixed by repairing the missing newline in-memory before the single write call, preserving one-write-call crash-atomicity while making the torn-line-tolerance guarantee actually hold. §7.1's core-helper allow-list stays at one member (`ConfigLoader.ResolveConfigPath`) — no reusable SHA-256/atomic-writer helper existed in core to add a second. 15/15 new tests pass, 1417/1417 core suite, no regressions. Merged as 63b82b8.

### #44: colour tokens (`@name`) legal inside `MatchRule`/`ThresholdRule` branches

Per SPEC-44 (Ultra-Advisor ruling: constant-tokens-only, one hop, no chase past a second `@token`). `ColorResolution.cs` gains a `ColorValue` type (`Literal`/`TokenRef`, no `Inline`); `ColorRule.Default`/`ThresholdRule.Color`/`MatchRule.Color` retyped to it; new one-hop `ResolveColorValue` helper. `ItemValueResolver.cs`'s `Walk` extended with a `CollectRuleBranches` traversal-site (new class per §4.3) so branch-position tokens get scanned like top-position ones. New `ConfigCheck.cs` Warning diagnostic `non-constant-color-token`, fired when a branch token resolves to a non-constant rule — a dynamic token in a branch is genuinely ambiguous since the branch has already dispatched by the time it'd apply, unlike top position which still has a live value driving the rule. All 9 SPEC-44 §7 doc-amendment items applied to `SPEC-V2-FRAMEWORK.md` §9.5.1 (citation re-pointing, exempt/covered table updates, candidate count 19→18). 163 targeted tests pass, 1406/1406 full suite, check-all.sh green including check-citations. Merged as 621b54b's parent merge commit (task-44, HEAD a94ad0e).

### Spec drafts landed: SPEC-2.6 Amendment A3, SPEC-3.1 Amendments A1-A4, plus 11 unrelated drafts

Two files (`SPEC-2.6-vertical-marker-splice.md`, `SPEC-3.1-block-model.md`) sat uncommitted in the shared working tree with disputed authorship — cdtui-arch2 and cdtui-architect each initially disclaimed and then contested the other's claim. Preserved to branch `preserve-spec26-31-unattributed` pending resolution. cdtui-architect ultimately re-reviewed the actual content (not recollection) line-by-line against SPEC-3.1's A4 claims — `RenderUnit(Segments, Rows)` struct, `units` as the single concatenation site, provenance retention, `PaneRow` not extended, `Wrap`'s three new optional params — all confirmed against the cited line numbers. SPEC-2.6 turned out to be authored by a different architect than either currently-live peer; SPEC-3.1 is cdtui-architect's own. Two non-blocking citation defects flagged in SPEC-3.1's A4 block for a follow-up fix (six methods move to `SegmentTruncation.cs`, not five; "single concatenation site" claim is at §9.2.1, not §9.2.2). Landed as 621b54b, along with 11 other unrelated uncommitted spec drafts swept up in the same preservation pass (SPEC-1.1.2, SPEC-12.6-mcp-tools erratum, four SPEC-2.3.x files, SPEC-2.3.1/2.3.2, SPEC-2.8.2, SPEC-9.4.2, SPEC-diag-73b).

**Two open items surfaced during this review, not yet actioned:**
- SPEC-2.6 §9.8's E2 (highest severity in #27): does a `RenderLeaf` result cache exist in code today, and if so does its key include both new members? Under §9.2.2 unit `k` is rendered twice per pane render — once `(null, false)`, once `(budget, true)` — a cache keyed on the old signature would silently return the first result for the second call, dropping every marker. Needs a grep before #27 implementation starts.
- SPEC-3.1 section 7.3's N1 (open, cdtui-architect's own): what does `RowLayout.Wrap` do with an embedded newline once the cap is lifted? Should be answered before #31's merge decision — the only remaining item that could change it.

### #39: §12.6.11 compare-and-branch on the re-read (test-only)

Per cdtui-arch2's scoping ruling: §12.6.11 applies only to commands that already hold two reads to compare "for free" — `edit.md` and MCP `set_config` (#10). `migrate.md`/`revert.md` never read the live config before writing, so mandating a comparison there would need a *new* read purely to enable it, contradicting the rule's own cost justification; out of scope (see #81, opened separately). Both layers were already implemented correctly by #10; #39 became regression-test-only, no production code. New tests: `EditCommandCompareAndBranchTests.cs` (content-presence guard on `edit.md`'s §12.6.11 rule 1+2 text — a model procedure, not compiled code, mirroring `AllowListTests.V4`'s scan-not-execute pattern) and two new `ConfigToolsTests.cs` cases (tool description reflects §12.6.12 rule 2; full round-trip proving stale-refusal → re-derive against current config → resubmit → succeeds with the intervening write preserved). 1425/1425 core, 17/17 MCP, no regressions. Confirmed as a side effect: `revert.md` deliberately does not restore the config file (documented at `commands/revert.md` §6 and `docs/backup-ledger.md`:130-131 — "put my old statusline back" is a different question from "undo my layout work"), not a gap. Merged as 883275d.

### #81: migrate.md consent-disclosure fix for existing config overwrite

Per cdtui-arch2's ruling (SPEC-81-migrate-overwrite.md): reframed from a missing-guard problem to a consent-integrity defect — `migrate.md`'s existing step-7 consent gate never disclosed that an existing config would be overwritten, even though step 2's backup-ledger procedure already determines config-existence for free (to populate `configCopy`/`null`). Added a 5th disclosure item (existence + resolved path only, never contents) to both branches of step 7, using the same resolved-path phrasing as step 8's write. Separately fixed: step 8's report previously over-promised that `/claude-tui-line:revert` restores the config file — it deliberately does not (only the statusLine entry); corrected to name the backup ledger's `configCopy` path as the only recovery route. `revert.md` untouched. SPEC-V2-FRAMEWORK.md §12.3 gained the corresponding amendment; the existing §10 migrate/edit/migrate-again scenario (bullet 11) was confirmed against its actual text before being extended, rather than assuming the cited line numbers. 1425/1425 tests pass, docs-only change. Merged as 7cac40a.

### #82: `--colors` renders at the terminal's real palette, not resolved truecolor

Per cdtui-arch2's ruling (SPEC-colors-terminal-fidelity.md, Amendment A1): `--colors`'s swatch preview rendered through the global auto-detecting `AnsiConsole`, which on a truecolor-capable terminal resolves named colors to 24-bit RGB escapes that bypass terminal ANSI-palette customization (e.g. iTerm2 profile remapping). The statusline render path itself was unaffected — it already pins `ColorSystem` to `Standard`. New `ColorsConsole.Create(TextWriter, AnsiSupport)` factory (`src/ClaudeTuiLine/ColorsConsole.cs`, a new `internal static class` matching the codebase's existing one-concern-one-file pattern) pins `ColorSystem` to `Standard` while leaving `Ansi` auto-detected, so piped output still degrades to bare names as before. Amendment A1 relocated the fix out of `Program.cs` after a CS0106 build blocker: `Program.cs` uses top-level statements with no explicit `Program` class, so a new method there is necessarily a `private` local function unreachable from tests via `InternalsVisibleTo` — the original spec's `internal static` signature was unimplementable as written. `CreateRenderConsole` (Program.cs:135) deliberately left untouched; the two consoles' differing `ColorSystem`/`Ansi` settings must never be harmonized. 1426/1426 core tests pass (re-verified independently post-merge, since the merge auto-combined SPEC-V2-FRAMEWORK.md edits with #81's). Merged as 2cedc89.

### §4.1/§4.2(a): citation-namespace desigil (partial — SPEC-2.6 held)

Per SPEC-tools-citation-namespace.md (cdtui-architect, Ultra-Advisor-reviewed): `check-citations.sh` took heading definitions from `SPEC-V2-FRAMEWORK.md` alone but collected citations from every tracked `.md` file, so a per-task spec's own numbered heading with the same `§N.M` sigil as a framework section number was ambiguous — the actual 8 check-all.sh failures were all framework-undefined refs (false positives, not silent mis-resolutions). Ruling: `§` means only a framework section; local/sibling refs drop the sigil and read "section N.M." §4.1 desigiled the 25-of-27 mechanically-identifiable sites (all framework-undefined) across SPEC-12.6-mcp-tools.md, SPEC-2.8.2-height-suppression-empty-content.md, SPEC-3.1-block-model.md, SPEC-2.3-residual-pane-overwidth.md, SPEC-2.3-even-split-parity.md, and STATUS.md (this file's own site landed via the #81 commit above). §4.2(a) separately fixed SPEC-2.3-drop-predicate.md:172's stated-count-vs-list mismatch. The 2 remaining sites (SPEC-2.6-vertical-marker-splice.md) are held pending ownership confirmation — dangling refs dropped from 8/27 to exactly 1/2, matching expectation. A larger, separate 166-site ambiguous-citation class (locally-defined numbers that also collide with genuine framework numbers, silently mis-resolving rather than merely failing) was found by the Ultra-Advisor review and deliberately deferred — SPEC-tools-citation-namespace.md §4.3 is marked DEFERRED, not ready to implement. Implemented by cdtui-impl4, verified by cdtui-worker. Merged as d0534f0.

### `tools/install.sh`: one-shot dev build+confidence script

Jim's actual blocker on the fill/content/gutter report turned out not to be that report at all — he said outright he didn't know if it was a bug, and that the real problem was having no installer script to be confident the code he's running is actually built and current. (Root cause behind that: his dev binary had been running a day-plus stale against `main` more than once this session, with no way to tell from the running binary alone.) New `tools/install.sh` mechanizes README's existing "By hand" steps rather than replacing them: toolchain check, repo state (dirty-tree warning, non-fatal), CLI publish (`ClaudeTuiLine.csproj`), MCP publish, a freshness proof (binary mtime vs. HEAD's commit timestamp, so staleness is impossible to miss), a `~/.claude/settings.json` wire-up block with the real resolved path substituted in (printed only — the script never touches the file), and a live `--preview` render against the just-built binary. README's manual steps stay documented in full alongside it.

MCP publish (step 4) surfaced a genuine, pre-existing defect never previously exercised: `ClaudeTuiLineMcp.csproj` references `ClaudeTuiLine.csproj` per SPEC-12.6 §7.1's allow-list (`ConfigLoader.ResolveConfigPath()` only), but `ClaudeTuiLine.csproj`'s `PublishAot=true` makes it self-contained, and the .NET SDK refuses to publish a non-self-contained exe (MCP) that references a self-contained one (NETSDK1151). Neither csproj was touched by this change. Ruled (by me, directly — a fast, low-stakes scope call, not routed to architect) to make step 4 non-fatal (warn and continue) rather than block the installer on an unrelated conflict; the conflict itself is tracked separately as **#83**, candidate fixes `-p:SelfContained=false` on the MCP publish invocation or splitting `ConfigLoader.ResolveConfigPath()` into a small shared library, needs an architect ruling before either is picked.

Implemented by cdtui-impl2, verified by cdtui-worker (all 7 steps pass, idempotent across two runs, guardrails held — no network calls, no writes outside the repo, neither csproj modified, plugin/setup path untouched). Re-ran on `main` post-merge myself: exit 0, binary confirmed newer than HEAD's commit. Merged as c437973/merge-commit (this entry).

**Follow-up (same session):** Jim asked for two more things — a symlink into the user's bin path so `settings.json` doesn't need the full repo path, and an interactive prompt to write the `statusLine` change directly instead of just printing it. New step picks `$HOME/.local/bin` then `$HOME/bin` (whichever exists and is on `$PATH`; never creates a directory or mutates `$PATH`) and symlinks the built binary there, refusing to clobber a real non-symlink file. The wire-up step now prompts on a real tty (default No); on accept it backs up the existing `settings.json` with a timestamp first, then edits only the `statusLine` key via `jq`, leaving every other key untouched — `jq` absent falls back to the original print-only behavior. Non-interactive runs are unchanged. Verified independently by cdtui-worker via a real pty (`expect`): both bin-dir-found and no-bin-dir paths, real-file-collision refusal, accept/decline/jq-absent scenarios, real `~/.claude/settings.json` checksum unchanged throughout, idempotent on rerun. Re-ran on `main` post-merge myself (non-interactive): symlink at `~/.local/bin/claude-tui-line` picked up automatically, printed snippet now shows the short path. Merged as df71ef1/merge-commit.

### README: config-documentation audit

Full sweep of README's config docs against `Config.cs`/`Pane.cs`/SPEC-V2-FRAMEWORK.md, not just the 3 known gaps carried over from cdtui-architect's fill-content-gutter investigation. Fixed: `gutter`'s default (`0`, no floor) and vertical-split-only restriction (confirmed real and enforced — `ConfigCheck.cs`'s `key-not-applicable` diagnostic rejects it on horizontal splits); `border.collapse` (previously undocumented entirely — surface-level-only restriction, matching `collapse-not-surface-level`'s actual reach on both the top-level and pane-level forms); `size: "content"`'s unwrapped-width behavior. Audit also found and fixed: `border.edges`, border shorthand strings (`all`/`outline`/`inside`/`none`) and their propagation rules, the leaf-vs-split `border.enabled` default asymmetry ("adding a split never silently adds chrome"), `distribute`'s full token set (`greedy`/`min-rows`/`even`), `items[].maxLines`, `surface.maxRows`.

Deliberately omitted: SPEC-V2-FRAMEWORK.md's "gutter>1 centres with blanks under collapse" claim — confirmed genuinely unimplemented by both the implementor and cdtui-worker independently (no width-dependent centering logic anywhere in the collapsed-boundary code path); documenting it would have promised behavior that doesn't exist. The spec text itself is stale there and at :1905 (a separate "collapse is unshipped" line cdtui-architect flagged earlier) — not fixed here, README-only scope; still needs its own pass.

cdtui-worker's verification caught one real inaccuracy before merge: the original wording ("gutter must be `>= 1` and defaults to `1` under collapse") was copied from the spec but doesn't match the code — `SizeResolver.cs`/`PaneTreeRenderer.cs` hardcode the collapsed boundary cost to exactly 1 column regardless of what `gutter` is set to, with no `ConfigCheck` diagnostic enforcing a floor. Corrected directly (by me) before merging: "the divider always occupies exactly one column, regardless of what `gutter` is set to — `gutter`'s value is ignored in that mode." check-examples.sh passes clean (16 items/24 files); check-docs.sh's only failure is the pre-existing, already-tracked SPEC-2.6 held-citation issue (confirmed identical on main pre- and post-merge); 1425/1425 tests green. Implemented by cdtui-impl3, verified by cdtui-worker. Merged as d391f45 (README fix commit b0d0f1a).

**Left open, not decided this pass**: whether the top-level `border`/`items`-without-`surface` config fallback (a real, live single-root-leaf-pane shortcut per `UserConfig`'s direct fields, confirmed not deprecated) should be promoted into README's minimal example, or stay implicit. Flagged by both the implementor and cdtui-worker as genuinely ambiguous — a "what should README teach" call, not a correctness one. Deferred, low priority.

### #83: MCP publish fails (NETSDK1151) — fixed via shared ConfigLoader library

`src/ClaudeTuiLineMcp/ClaudeTuiLineMcp.csproj` referenced `src/ClaudeTuiLine/ClaudeTuiLine.csproj` per SPEC-12.6 §7.1's allow-list (`ConfigLoader.ResolveConfigPath()` only), but `ClaudeTuiLine.csproj` has `PublishAot=true`, making it self-contained — the SDK refuses to publish a non-self-contained exe (MCP) that references a self-contained one. Surfaced by `tools/install.sh` actually running the MCP publish standalone for the first time; nothing else in the repo had exercised that path.

Jim chose the shared-library split over the `-p:SelfContained=false` flag. cdtui-architect confirmed `ResolveConfigPath()` (both overloads, `Config.cs:508-527`) is BCL-pure — no transitive dependencies beyond `string`/`Path`/`Environment` calls — and designed a new plain-library project, `src/ClaudeTuiLineShared/` (namespace `ClaudeTuiLineShared`, class `ConfigPath`, method name `ResolveConfigPath` kept to avoid a doc-citation sweep across SPEC-12.6/SPEC.md). MCP's `ProjectReference` to `ClaudeTuiLine.csproj` was removed entirely, not redirected — both CLI and MCP now reference `ClaudeTuiLineShared.csproj` instead. `CliLocator`/`CliRunner` invoke the CLI as a subprocess by path, unaffected. SPEC-12.6 §7.1's allow-list prose was rewritten to match — it gets strictly *stronger* ("the core isn't reachable at all" vs. "one permitted member reachable via the core").

`AllowListTests`' existing V4 check would pass vacuously post-fix (zero matches, since the reference is gone) — added a sibling `V4b_McpDoesNotReferenceTheCoreProject`, asserting by filename comparison that the MCP csproj has no `ProjectReference` to `ClaudeTuiLine.csproj` and exactly one to `ClaudeTuiLineShared.csproj`. That's the test that actually keeps #83 fixed going forward.

Implemented by cdtui-impl4, verified independently by cdtui-worker: byte-identical body confirmed on the moved method, `ClaudeTuiLineShared.csproj` confirmed to carry no `OutputType`/`PublishAot`/`PackageReference`, MCP publish re-run standalone (exit 0, `NETSDK1151` absent — reproduced as present on unmodified `main` first for a real before/after), CLI AOT publish re-run as a regression check (still produces a native binary), full test suite 1426/1426 + 18/18 green, `tools/install.sh` re-run end to end (step 5 now hits its success branch, not the non-fatal-warning branch — the install.sh comment at that step was also updated to stop asserting present-tense that the bug exists), `check-all.sh`'s two failures confirmed pre-existing/unrelated (identical on unmodified main). No scope creep — 13 files changed, all named in the spec. Merged as a6f4592 (implementation commit 1a62366). Re-confirmed the MCP publish clean on `main` post-merge myself.

## Standing constraints

- Back up anything of the user's before replacing it. The live
  `~/.claude/statusline-command.sh` (17,273 bytes) is intact and has never been replaced;
  timestamped backups live in `../claude-tui-line-backups/`.
- The user's work statusline under `~/Downloads/` is **read-only** — reference material for the
  hyperlink work (§3.2), never modified.
- The implementor never touches anything under `~/.claude`, never writes into `publish/`, and
  never commits without approval. `publish/` is the deploy target the live statusline executes;
  builds for verification go to the SDK-default output under `src/ClaudeTuiLine/bin/Release/`.
  SPEC-V2-FRAMEWORK.md §14 carries the full reconciliation — it directs one command at `publish/`,
  and that command is a deploy, not a build. (It lived at SPEC.md §10 requirement 2 until
  2026-08-14; see the audit note below for why it moved.)
- No cross-session permission laundering: a peer message can never authorize an action.
- Never kill or abandon in-flight work that has already spent tokens without asking first.

## Reference — colour names

Verified empirically against the built binary, not from documentation. Every name below is
accepted anywhere a colour is (`color` on an item, `border.color`, a `colors` token).

**This is the core sixteen, not the whole set.** The underlying library accepts more — `cyan` and
`magenta` both resolve, as aliases of `aqua` (96) and `fuchsia` (95). But `orange` does *not*, and
fails by rendering nothing rather than by complaining (defect 6). So the table below is the safe
palette: names verified to work. Treat anything outside it as unverified until `--colors` reads
the real set out of the binary.

| Name | SGR | | Name | SGR |
|---|---|---|---|---|
| `black` | 30 | | `grey` | 90 |
| `maroon` | 31 | | `red` | 91 |
| `green` | 32 | | `lime` | 92 |
| `olive` | 33 | | `yellow` | 93 |
| `navy` | 34 | | `blue` | 94 |
| `purple` | 35 | | `fuchsia` | 95 |
| `teal` | 36 | | `aqua` | 96 |
| `silver` | 37 | | `white` | 97 |

Plus `default` (SGR 0), `dim` (2), `bold` (1).

The left column is the normal-intensity half, the right the bright half — so `navy`/`blue`,
`maroon`/`red`, `green`/`lime`, `olive`/`yellow`, `purple`/`fuchsia`, `teal`/`aqua`,
`silver`/`white` are seven pairs, not fourteen unrelated names. All sixteen are theme-mapped:
the terminal decides what `blue` actually looks like, which is why the framework defaults to
this palette rather than truecolor (§6.2).

To see them in your own terminal with your own theme:

```sh
for i in 30 31 32 33 34 35 36 37 90 91 92 93 94 95 96 97; do printf '\033[%sm  %s  \033[0m' "$i" "$i"; done; echo
```
