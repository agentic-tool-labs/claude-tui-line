# SPEC-105 — default OSC-8 hyperlink for the `pr` item

- **Status:** rev 3 — IMPLEMENTED AND SHIPPED (rev 1). Rev 2 amended three
  spec-defects found in review; **one of them (§C.4b) is code-affecting and is
  still outstanding.** Rev 3 is a spec-text-only follow-up to rev 2. See §K.
- **Author:** Architect
- **Request:** `20260825-154703-37z6` (pr-item-hyperlink); amended per
  `20260825-161525-d5t2` (pr-hyperlink-host-spoof) and `20260825-162443-eduf`
  (pr-hyperlink-c4a-nit)
- **Prerequisite:** none. SPEC-100..104 are independent of this change; no shared
  class or method.

---

## §A — goal, and the one correction to the brief

### A.1 Goal

The `pr` item (`ItemRegistry.cs:69`) renders `PR #128` / `PR #128 [draft]` as plain
coloured text with no hyperlink, for every review state. Give it a default OSC-8
hyperlink to the pull request's page on the repo host, requiring no configuration.

### A.2 The link must NOT be wrapped inside `SegmentBuilder`

The dispatching brief's §4 suggested wrapping `OscHyperlink.Wrap` around
`ResolvePullRequest`/`BuildPullRequest` in `SegmentBuilder.cs`. **Do not do this.**
It is the wrong layer, for a reason this codebase has already settled:

- `SegmentBuilder` resolves an item by id and never sees the item's *placement*
  config, so it cannot tell whether the user has also set a `link` on that
  placement. It would double-wrap, and undetectably — `LeafContent.Decide`
  (`LeafContent.cs:56-61`) has no pre-check for an already-present hyperlink and
  would wrap the wrapped markup a second time.
- There is exactly **one** OSC-8 wrap site for item links: `LeafContent.cs:60`.
  Precedence between a user `link` and a registry default is decided exactly once,
  at `LeafContent.cs:52-54`. Adding a second wrap path forfeits that.

The correct mechanism already exists and already has three users — see §B.

### A.3 Alternatives considered and rejected (do not re-litigate)

1. **A generic per-item `link` template in the user's config.** Cannot work.
   `{}` resolves to the item's own resolved value, which for `pr` is
   `#128 [draft]` — a leading `#` plus a bracketed suffix. GitHub needs a bare
   digit path segment. Confirmed by the requester and by
   `SegmentBuilder.ResolvePullRequest`.
2. **A `DefaultLinkTemplate` string referencing other items,** e.g.
   `https://{repo-host}/{repo}/pull/{}`. Cannot work, and fails *silently*:
   `LeafContent.TryBuildLink` resolves `{other-id}` against a map built from
   already-rendered items, finalized before default templates are invoked, so
   `{repo-host}` misses, `missing` is set, and the link is suppressed with no
   diagnostic. §B.2's finished-URL form exists precisely to avoid this.
3. **A new `itemSettings.pr.*` knob to enable or template the link.** Rejected —
   see §D.

---

## §B — mechanism: `ItemDefinition.DefaultLinkTemplate`

### B.1 What it is

`DefaultLinkTemplate` is an optional `Func<ItemContext, string?>` on
`ItemDefinition`. `LeafContent.Decide` picks the template as:

```csharp
var template = resolved.Config.Link is { Length: > 0 } configured
    ? configured
    : resolved.DefaultLink;
```

so a **user-configured `link` on the placement always wins**, and the default is
the fallback. This is the whole reason no opt-out setting is needed (§D.2).

### B.2 It may return a finished URL, not only a template

Two shapes are already in use, and both are supported by the same layer:

| item | form returned | site |
|---|---|---|
| `directory` | **finished URL**, no placeholders (`file:///…`, `vscode://file/…`) | `ItemRegistry.cs:59`, via `DirectoryLink.Build` |
| `git-branch` | **finished URL**, no placeholders | `ItemRegistry.cs:102-110`, `GitBranchDefaultLink` |
| `linear` | **template** containing `{}` | `ItemRegistry.cs:~93`, `LinearDefaultLink` |

`TryBuildLink` tokenizes whatever it is given; a string containing no placeholder
tokens accumulates as literal text, `missing` stays `false`, and the string is
returned verbatim. `AnsiStrip.Strip` is applied only to *substituted* values, never
to literal spans.

**`pr` uses the finished-URL form.** It must not contain `{}` — `{}` is
`#128 [draft]`, which is the bug.

---

## §C — the design

### C.1 URL shape

```
https://<host>/<owner>/<name>/pull/<number>
```

- `<host>` — `ctx.Input.Workspace?.Repo?.Host`
- `<owner>` — `ctx.Input.Workspace?.Repo?.Owner`
- `<name>` — `ctx.Input.Workspace?.Repo?.Name`
- `<number>` — `ctx.Input.Pr?.Number`, formatted with
  `CultureInfo.InvariantCulture`, exactly as `ResolvePullRequest` already does.

**The number is taken from `PrInfo.Number` directly.** It is never derived from the
item's rendered value, so `review_state` and any
`itemSettings.pr.reviewStateLabels` override cannot leak into the URL. This is a
hard requirement — see the acceptance criterion §G.3.

The scheme is the literal `https://`. It is never taken from input.

### C.2 Where the code goes

Add a private static method to **`src/ClaudeTuiLine/ItemRegistry.cs`**, alongside
`GitBranchDefaultLink` (`:102`) and `LinearDefaultLink`:

```csharp
private static string? PrDefaultLink(ItemContext ctx)
{
    // ... per §C.3 / §C.4
}
```

and wire it on the `pr` item definition at `ItemRegistry.cs:69` by adding the
trailing `DefaultLinkTemplate:` argument, exactly as `git-branch` and `linear` do.

**Do not create a new file.** `DirectoryLink.cs` is a separate file because it
carries two target dialects plus percent-encoding rules shared with `FileUri`.
`pr` is a single expression; the `linear`/`git-branch` precedent applies.

`SegmentBuilder.cs` is **not** touched by this spec. `PrItemSettings` is **not**
touched. `Config.cs`, `SchemaCommand.cs`, and `ConfigCheck.cs` are **not** touched
(no new config surface — §D).

### C.3 Null-safety — the exact fallback

Return `null` (⇒ no link, item still renders as plain coloured text, no crash) if
**any** of the following is true. Each is independently nullable on `StatusInput`:

1. `ctx.Input.Pr` is null, or `Pr.Number` is null.
2. `ctx.Input.Workspace` is null.
3. `ctx.Input.Workspace.Repo` is null.
4. `Repo.Host` is null or empty.
5. `Repo.Owner` is null or empty.
6. `Repo.Name` is null or empty.
7. Any of the host/owner/name rejections in §C.4.

Use `{ Length: > 0 }` patterns, matching `GitBranchDefaultLink`'s existing style.
There is no partial URL and no default host: a missing piece means no link. This
matches the "link is best-effort" contract `LeafContent` already documents.

Note that cases 2–7 leave the item itself rendering normally; only case 1 also
removes the item, and it does so already via `ResolvePullRequest` returning null.

### C.4 Input handling at the trust boundary — REQUIRED, do not simplify away

`Host`, `Owner`, and `Name` come from the harness's JSON payload. They are
interpolated into a URL that a terminal will open on click.

**Order matters: validate (b) first, then escape (a).** A component that fails
validation returns `null` immediately and is never escaped or interpolated.

**(a) Template-literal escaping — `Owner` and `Name` only.**

The returned string is fed to `PlaceholderTemplate.Tokenize`. A brace in the data
would otherwise be parsed as a placeholder and either substituted or suppress the
link. Pass `Owner` and `Name` through the **same `EscapeTemplateLiteral` helper
`GitBranchDefaultLink` already uses** (`ItemRegistry.cs:109`). Do not write a
second escaper. The number is digits-only and needs no escaping.

> **AMENDED rev 3** — review nit (`20260825-162443-eduf`). Rev 2 listed `Host`
> here as well. That was left over from rev 1, when all three components shared
> one blacklist. It is now **inert**: §C.4b.1's whitelist forbids braces in
> `Host` by construction, so escaping it can never change anything. Escaping a
> value that provably cannot contain the escaped character is dead code, and
> this repo treats accepted-but-inert surface as a defect (SPEC-104 §L), so
> `Host` is dropped from the escape path rather than kept as belt-and-braces.
>
> **The whitelist is load-bearing for that.** If §C.4b.1's `Host` rule is ever
> relaxed to admit characters outside `[A-Za-z0-9.:-]`, `Host` must rejoin the
> escape path in the same change. Whoever relaxes it owns that.

**(b) Reject structurally invalid components.**

> **AMENDED rev 2** — review finding 1 (`20260825-161525-d5t2` §[1].1). Rev 1
> specified a single character **blacklist** applied to all three components, and
> it omitted `@` and `:`. That let
> `workspace.repo.host = "github.com@evil.test"` produce
> `https://github.com@evil.test/acme/acme-web/pull/128` — a link whose real
> authority is `evil.test`, while the statusline shows an ordinary `PR #128`.
> **The fix is not to append the two missing characters.** A blacklist that has
> already been found short by two is the wrong shape for the authority position;
> the next gap would be found the same way. `Host` flips to a **whitelist** below.
> `Owner`/`Name` keep the rev-1 blacklist, deliberately — see (b.2).

**(b.1) `Host` — whitelist. Reject anything that does not match, in full:**

- one or more of `[A-Za-z0-9.-]`, **optionally** followed by
- a single `:` and 1–5 digits forming a port in `1..65535`.

Anything else ⇒ return `null`, no link. `@` is rejected by construction, as are
control characters, whitespace, `/`, `?`, `#`, `\`, `%`, `[`, `]`, braces, and
every character the rev-1 list would have missed. A real GHE host
(`github.mycompany.com`) still resolves — see §G.8. An IPv6 literal (`[::1]`) is
rejected; that is accepted, as it is not a case this feature needs to serve.

**The `:` ruling — allowed, but only as a trailing numeric port.** Stated
explicitly because the request asked for it:

- `@` has **no** legitimate use in this position and is the actual spoof vector,
  so it is rejected outright.
- A colon does have one legitimate use — self-hosted GHE on a non-standard port
  (`github.corp.example:8443`) — so rejecting `:` outright would break a real
  deployment for no security gain.
- A *non-numeric* colon (`github.com:evil.test`) is not a spoof: a non-numeric
  port is a URL parse error, so the URL fails to navigate rather than navigating
  somewhere else. Requiring digits therefore costs nothing and keeps the rule
  simple enough to read in one line.

**(b.2) `Owner` and `Name` — unchanged from rev 1.** Reject if either contains a
control character (`< 0x20` or `0x7F`), whitespace, `/`, `?`, `#`, or `\`.

They are **not** flipped to a whitelist, for two reasons: neither can reach the
authority position (a `/` is already rejected, so both stay in the path), and a
whitelist there would make `EscapeTemplateLiteral` unreachable for them, retiring
the (a) escape path that §G.6 exists to cover. Keeping the blacklist keeps that
branch live and tested.

Rejecting is preferred over percent-encoding throughout, because these fields have
a narrow legitimate charset (`[A-Za-z0-9._-]` for GitHub owner/name; a hostname for
host), so anything outside it signals a payload we do not understand, and emitting
no link is the honest response. Do not add percent-encoding —
`EscapeBranchForPath` exists for branch names, which genuinely do contain `/`; PR
components do not.

---

## §D — design fork #1: no new settings knob. RULING: none.

### D.1 The question

`directory` has `itemSettings.directory.openWith` choosing between `files` and
`vscode`. Should `pr` get an analogous knob?

### D.2 Ruling: no

- `openWith` exists because a directory has **two genuinely different targets**
  (an OS file browser vs. an editor). A pull request has **one canonical page**.
  A knob with one meaningful value is an abstraction with one implementation.
- The escape hatch already exists and is already generic: setting `link` on the
  `pr` placement overrides the default (§B.1). Nothing new is needed to opt out or
  to point somewhere else.
- Adding a key means touching `Config.cs`, `SchemaCommand.cs` (hand-written — it
  does not surface keys automatically) and `ConfigCheck.cs`, and every one of those
  is an opportunity for the accepted-but-inert key defect class that SPEC-104 §L
  had to close. The cheapest way not to ship that defect is not to add the key.

**Caveat that would reverse this:** if §E.1's evidence shows non-GitHub hosts
reach the `pr` item in practice, an `itemSettings.pr` template key becomes the
right fix — see §E.1. That is a follow-up spec, not this one.

---

## §E — design fork #2: host scope. RULING: host-derived domain, GitHub-shaped path.

### E.1 Ruling

Take the **domain** from `RepoInfo.Host` — never hardcode `github.com`. Hardcode
the **path shape** `/<owner>/<name>/pull/<number>`.

Consequences, stated plainly:

- `github.com` works.
- **GitHub Enterprise on an arbitrary domain works** — this is the realistic
  non-`github.com` case, and it is the reason not to hardcode the domain.
- A GitLab host would receive a wrong-but-well-formed URL (GitLab uses
  `/-/merge_requests/<n>`). This is the cost of the ruling.

### E.2 Why not a host→path-shape table

- **Nothing in `src/` does host-specific URL construction today.** `ResolveRepoHost`
  (`SegmentBuilder.cs:194-195`) returns the host verbatim; the only `github.com`
  literals in `src/` are in `SyntheticFixture.cs` fixture data. A dispatch table
  would be the first of its kind, built for a user we have no evidence exists.
- `PrInfo.ReviewState`'s own vocabulary (`approved`, `changes_requested`, `draft`
  — `SegmentBuilder.cs:~252`) is the GitHub `gh pr` vocabulary. The input field
  this feature reads is already GitHub-shaped upstream of us.

### E.3 NEEDS-EVIDENCE — E1: does a non-GitHub host ever reach this item?

This is the one fact that would reverse §E.1, and I could not settle it statically.

- **What to check:** Claude Code's own statusline payload — does it populate
  `pr` (and with what `review_state` tokens) when the workspace's remote is
  GitLab or Bitbucket, and what does it put in `workspace.repo.host` in that case?
  Documentation or a payload capture from a non-GitHub repo both answer it.
- **If `pr` is only ever populated for GitHub remotes:** §E.1 stands unchanged and
  §E.4 can be dropped.
- **If `pr` is populated for other forges:** §E.1 is shipping a wrong clickable
  link, which is worse than no link. The fix is *not* a host table; it is to
  suppress the link for unrecognised hosts and add an
  `itemSettings.pr.urlTemplate` key (reversing §D.2) so those users have a hatch.

**This does not block implementation.** Ship §E.1 either way; E1 decides only
whether a follow-up spec is needed.

### E.4 Known gap, recorded deliberately

A GitLab user today has **no working escape hatch**: overriding via `link` gives
them `{}` = `#128 [draft]`, which is exactly the broken-URL problem in §A.3.1. So
under §E.1 they get a wrong link and cannot fix it from config. I am accepting
that only because E1 most likely shows the case does not arise. If E1 says
otherwise, this is a defect, not a limitation.

---

## §F — fixture movement

> **AMENDED rev 2 — this section is a RETRACTION.** Review finding 2
> (`20260825-161525-d5t2` §[1].2). Rev 1 asserted the golden baseline "WILL
> change, and must", and pre-authorised regenerating it under a bounded diff.
> **That premise was wrong**, and the pre-authorisation should never have been
> written. The rev-1 text is replaced by §F.1–§F.3 below.
>
> My error: I inferred the golden path's *code path* from its *fixture data* —
> `SyntheticFixture` populates `workspace.repo` and `pr`, and `pr` is in
> `DefaultIds`, so I concluded the baseline must render the item through the
> registry. It does not. Do not infer which layers a test exercises from what its
> fixture contains; read the test's own wiring.

### F.1 The golden baseline does NOT move, and cannot

`GoldenParityTests` feeds fixture segments **straight to
`PaneRenderer`/`Compositor`**. It never goes through `ItemRegistry` or
`LeafContent.Decide`, which is the only place a `DefaultLinkTemplate` is ever
consulted (§B.1). No default link can reach that baseline, by construction.

### F.2 The standing rule applies, unmodified

`fixtures/golden-phase1-baseline.json` **must not move.** Rev 1's bounded
pre-authorisation to regenerate it is withdrawn in full. If the baseline moves,
that is a regression — **stop and report it; do not regenerate over it.** This is
the repo's standing tripwire rule, and SPEC-105 does not override it.

`Segment.Plain` for the `pr` item must in any case remain byte-identical
(`PR #128 [approved]`): OSC-8 bytes belong to markup only and must not reach the
width-measurement path.

### F.3 Other suites

`ItemFormatParityTests`, `SegmentTruncationSpansTests`, `PaneAssemblerSpansTests`,
and `RenderInvariantTests` all already assert on OSC-8-bearing segments and should
not move — they construct their own segments rather than going through
`ItemRegistry`. If one of them moves, that is a real finding: report it.

`SegmentBuilderTests.cs:321-325` and `:852` assert on
`ResolvePullRequest`/`BuildPullRequest` output. Because §C.2 does not touch
`SegmentBuilder`, **these must not move.** If they do, the link was added at the
wrong layer — see §A.2.

`ItemsCommandTests` **may** move, if it asserts on the `pr` item's description
string (§G.10). That one is expected.

---

## §G — acceptance criteria

Tests belong in `tests/ClaudeTuiLine.Tests/HyperlinkTests.cs`, following its
existing convention (build the input, render, then
`OscHyperlink.TryUnwrap(markup, out var url, out _)` and assert on `url`).

1. **Draft PR renders a working hyperlink.** `Pr = { Number = 128, ReviewState =
   "draft" }` with `Repo = { github.com, acme, acme-web }` ⇒ the `pr` item's markup
   unwraps to `https://github.com/acme/acme-web/pull/128`, and the visible text is
   still `PR #128 [draft]`. *(This is the reported bug.)*
2. **Non-draft PR renders one too.** Same input with `ReviewState = null` ⇒ URL
   `…/pull/128`, text `PR #128`. *(Proves it was not review-state-specific.)*
3. **The suffix never leaks into the URL.** With
   `itemSettings.pr.reviewStateLabels = { "draft": " ~WIP~" }` and
   `ReviewState = "draft"` ⇒ the URL is still `…/pull/128` while the text is
   `PR #128 ~WIP~`. *(Locks §C.1's "number from `PrInfo.Number`, not from the
   rendered value" requirement.)*
4. **Every missing piece yields plain text, no link, no crash.** One case each for:
   `Workspace` null; `Workspace.Repo` null; `Host` null; `Host` empty; `Owner`
   null; `Name` null. In all six the item renders `PR #128…` with
   `OscHyperlink.TryUnwrap` returning false.
5. **Rejected components yield no link.** `Owner = "ac/me"` and, separately,
   `Name = "acme#1"` ⇒ no link, item still renders.
   > **AMENDED rev 2** — review finding 3. Rev 1's second example read
   > `Name = "acmeweb"`, which contains no invalid character and so was not
   > testable as written; the `#` was dropped when the spec text was authored.
   > The shipped test already uses `"acme#1"`, which is what this criterion
   > always meant.
6. **A brace in a component does not corrupt the link.** `Name = "acme{web}"`
   must survive `EscapeTemplateLiteral` and appear literally in the URL. The
   forbidden outcome is a *substituted or silently suppressed* link. *(§C.4a. This
   case stays live because §C.4b.2 keeps `Owner`/`Name` on the blacklist rather
   than a whitelist — see the reasoning there. It is also now the **only**
   coverage of the escape path, since rev 3 drops `Host` from it.)*
7. **A user `link` on the placement wins.** With `link: "https://example.test/x"`
   on the `pr` placement ⇒ URL is `https://example.test/x`, and the markup contains
   exactly **one** OSC-8 open sequence. *(Locks §A.2's no-double-wrap property.)*
8. **A non-github host is used verbatim.** `Host = "ghe.corp.example"` ⇒
   `https://ghe.corp.example/acme/acme-web/pull/128`. *(Locks §E.1 — no hardcoded
   domain.)*
9. **No unrelated regression.** Full suite green; the golden baseline unmoved
   (§F.2); `SegmentBuilderTests` unmoved (§F.3).
10. **`--items` description updated.** The `pr` item's description string in
    `ItemRegistry.cs:69` mentions the link, e.g. appending *"; links to the pull
    request on the repo host"*. Update `ItemsCommandTests` if it asserts on that
    string — that movement is expected.

**Added rev 2, for §C.4b.1:**

11. **Userinfo spoof is rejected.** `Host = "github.com@evil.test"` ⇒ no link, item
    still renders `PR #128`. *(This is the reported vulnerability.)*
12. **A legitimate port survives.** `Host = "github.corp.example:8443"` ⇒
    `https://github.corp.example:8443/acme/acme-web/pull/128`.
13. **A non-numeric or malformed port is rejected.** `Host = "github.com:evil"`
    and, separately, `Host = "github.com:99999"` ⇒ no link.
14. **A brace in `Host` is rejected, not escaped.** `Host = "git{hub}.com"` ⇒ no
    link. *(Added rev 3. Locks §C.4a's claim that dropping `Host` from the escape
    path is safe — if this ever renders a link, the whitelist has been relaxed and
    `Host` must rejoin the escape path.)*

No test may reach the network or invoke `gh`/`git`. All inputs are constructed
`StatusInput` objects.

---

## §H — what must NOT change

- `SegmentBuilder.ResolvePullRequest` / `BuildPullRequest` / `ReviewStateSuffix` —
  no edits. Their outputs are asserted directly by `SegmentBuilderTests`.
- `PrItemSettings`, and the JSON config schema generally. No new keys (§D.2).
- `OscHyperlink.cs`, `LeafContent.cs`, `PlaceholderTemplate` — reuse only, no
  changes. If the implementation appears to need a change in `LeafContent`, that is
  a signal the link is being added at the wrong layer; stop and report.
- `Segment.Plain` for the `pr` item, and therefore every width and truncation
  calculation.
- The `pr` item's colour (`olive` via `SingleColor`) and its
  `ItemColorKind.Decorative` classification.
- **(rev 2)** The three test cases the reviewer confirmed green —
  `Pr_NonGitHubHost_UsedVerbatimNoHardcodedDomain`,
  `Pr_ReviewStateLabelSuffix_DoesNotLeakIntoUrl`, and §G.6's escape branch — must
  all still pass after the §C.4b.1 change. §C.4b.2 exists to guarantee the third.

---

## §I — risks for the Implementor

1. **`EscapeTemplateLiteral` is easy to miss.** It is the difference between a
   finished URL and one that gets re-tokenized. `GitBranchDefaultLink` uses it;
   copy that. Do not write a second escaper (one implementation per behaviour).
2. **`DefaultLinkTemplate` is a trailing named argument** on the `ItemDefinition`
   record. The `pr` definition at `:69` is currently a single long line; adding the
   argument is the whole wiring change.
3. **Do not add `{}` to the returned string.** It will resolve to `#128 [draft]`.
4. **(rev 2)** `Host` and `Owner`/`Name` now use **different** validation shapes
   (whitelist vs. blacklist), and **(rev 3)** only `Owner`/`Name` are escaped.
   Both asymmetries are deliberate and reasoned in §C.4 — do not "tidy" either
   into one shared helper without re-reading it, because unifying the validation
   retires §G.6's coverage and re-adding `Host` to the escape path re-introduces
   dead code.

---

## §J — confidence and escalation

High confidence on §A–§D and §G–§H: the mechanism is existing, three-times-used
machinery, and every claim is grounded in a read of the current source.

Lower confidence on §E (host scope) — it rests on E1, which is empirical and
unresolved. It is a best-effort cosmetic link, not a security or data-integrity
surface, so it does not warrant Ultra-Advisor escalation; it warrants the E1 check
and, if that check goes the other way, a follow-up spec.

§C.4b is the one place I have deliberately been non-lazy: it is a trust boundary
where malformed input reaches a terminal escape sequence and a clickable
authority. Do not trim it.

---

## §K — closeout

### K.1 OUTSTANDING — code change required

**§C.4b.1** (`Host` whitelist) is not yet implemented. The shipped rev-1 code has
`IsSafeUrlComponent` at `ItemRegistry.cs:118-129` applying one blacklist to all
three components. The change is: split `Host` onto its own whitelist validator per
§C.4b.1, leave `Owner`/`Name` on the existing blacklist per §C.4b.2, drop `Host`
from the `EscapeTemplateLiteral` call per §C.4a, and add acceptance criteria
§G.11–§G.14.

Severity is low, not zero: `workspace.repo.host` comes from the user's own git
remote via Claude Code's payload, so it is not attacker-controlled under the normal
threat model. It is a real spoof primitive with an unusually quiet failure mode
(the statusline shows an ordinary `PR #128` while the link's authority is
something else), which is why it is being closed rather than accepted.

**§C.4a's `Host` drop is part of this same change, not a separate one.** Dropping
it before the whitelist lands would remove a protection that is still load-bearing.
Order: whitelist first, then drop the escape, in one diff.

### K.2 CLOSED rev 2 — text-only, no code impact

- **§F** — retracted and rewritten. Rev 1's premise that the golden baseline must
  move was wrong; the standing do-not-move rule applies unmodified. No code or
  fixture consequence, because nothing was ever regenerated on the strength of it.
- **§G.5** — example string corrected to `"acme#1"`, matching the shipped test.

### K.3 CLOSED rev 3 — spec text, with one line of code folded into §K.1

- **§C.4a** — `Host` dropped from the escape path, because §C.4b.1's whitelist
  makes escaping it provably inert. Reviewer's nit
  (`20260825-162443-eduf`) was correct. §G.14 added to lock the assumption, and
  §C.4a records that the whitelist is load-bearing for it: relax the whitelist and
  `Host` must rejoin the escape path in the same change.
- **§C.4's ordering** — made explicit (validate, then escape). It was implied by
  rev 2's structure but never stated, and the two steps are only safe in that
  order.
