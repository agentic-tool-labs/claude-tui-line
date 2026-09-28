namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.3: resolves a pane tree's sizes. Vertical splits divide width and run
/// the full bounded fixpoint (intrinsic measurement, the six-step single-pass allocation, the
/// over-constrained drop loop, monotone-clamped re-measurement capped at three passes) — this is
/// the axis both §2.9 acceptance cases and every §10 fixpoint test exercise. Horizontal
/// splits divide nothing along height in this phase: every child simply inherits the split's full
/// width and renders to its own natural row count, because §2.8 (the row budget a real height
/// division would need to divide) is out of scope for Phase 3 — see the phase-3 report for this
/// as a disclosed scope boundary, not an oversight.
/// </summary>
public static class SizeResolver
{
    private const int MaxPasses = 3;

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §2.5.1: leaf rendering is a pure function of (items, innerWidth) — no
    /// surface-derived value may reach it through the <see cref="Pane"/> record itself, since a
    /// <c>Pane</c> is shared across terminal widths and heights. <see cref="ClipRows"/> and
    /// <see cref="ItemsEmptied"/> are §2.8.1 degrade-ladder output for one render attempt against
    /// one surface budget, so they live here instead, alongside the rest of that attempt's layout —
    /// <see cref="HeightLadder"/> annotates a freshly resolved tree with them, never a shared
    /// <c>Pane</c>.
    /// </summary>
    /// <summary>
    /// SPEC-88 §1.3/§4.5: <see cref="EffectiveSplit"/> is the render-time orientation this node
    /// resolved to — never <see cref="PaneSplit.Flex"/>, regardless of <see cref="Pane.Split"/> on
    /// <see cref="Source"/>. A leaf carries <see cref="PaneSplit.None"/>. This is the carrier a
    /// Class-B consumer (one that needs the effective orientation rather than the declared one)
    /// reads instead of <c>Source.Split</c>.
    /// </summary>
    public sealed record ResolvedPane(Pane Source, int OuterWidth, IReadOnlyList<ResolvedPane> Children, int? ClipRows = null, bool ItemsEmptied = false, PaneSplit EffectiveSplit = PaneSplit.None);

    public static ResolvedPane Resolve(Pane root, int outerWidth, ItemContext ctx, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, Segment> compounds, RenderNoteCollector notes, bool collapse = false) =>
        ResolveNode(root, outerWidth, ctx, values, compounds, measureOverride: null, rowCountOverride: null, notes, collapse);

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §10.6's three fixpoint tests need a "content" pane whose reported
    /// request is independent of real segment measurement — a stub that requests more
    /// width when granted less (the monotone-clamp test), or that changes its request every pass
    /// (the pass-cap test). <paramref name="measureOverride"/>, when supplied, replaces
    /// <see cref="MeasureRequest"/> for every content-kind pane on the greedy path; production
    /// callers never pass it, so real rendering is unaffected.
    ///
    /// On a <c>distribute: min-rows</c> subtree the override reaches exactly one site — the
    /// content candidate's natural-width cap inside the surplus water-fill — because that is the
    /// only <see cref="MeasureRequest"/> call min-rows makes. The row-count search itself
    /// (<see cref="RowCountAt"/>) never calls <see cref="MeasureRequest"/> and is unaffected by
    /// this override; the 7-arg overload below is the seam into that search instead.
    /// </summary>
    public static ResolvedPane Resolve(Pane root, int outerWidth, ItemContext ctx, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, Segment> compounds, Func<Pane, int?, int> measureOverride, RenderNoteCollector notes) =>
        ResolveNode(root, outerWidth, ctx, values, compounds, measureOverride, rowCountOverride: null, notes, collapse: false);

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §2.3.1: min-rows' own tests need a seam into <see cref="RowCountAt"/>
    /// — the packer-backed rows_i(w) the binary search in <see cref="MinWidthForRowCount"/>
    /// assumes is non-increasing in w. <paramref name="rowCountOverride"/>, when supplied,
    /// replaces <see cref="RowCountAt"/>'s real packer call for every candidate; production
    /// callers never pass it, so real rendering is unaffected. Still increments
    /// <see cref="MinRowsPackerInvocationCount"/> so cost assertions keep working under the
    /// override.
    /// </summary>
    public static ResolvedPane Resolve(Pane root, int outerWidth, ItemContext ctx, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, Segment> compounds, Func<Pane, int?, int>? measureOverride, Func<Pane, int, int>? rowCountOverride, RenderNoteCollector notes) =>
        ResolveNode(root, outerWidth, ctx, values, compounds, measureOverride, rowCountOverride, notes, collapse: false);

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §2.3 / SPEC-2.3-suppression-predicate.md §4: tests the pane's own
    /// PRE-suppression inner width — <see cref="RowLayout.MinUsableWidth"/> governs viability for
    /// <c>fill</c> and percent panes only — the same panes <see cref="Floor"/> treats as having a
    /// real minimum. A <c>content</c> or fixed-size pane got exactly the width it asked for, so it
    /// is never "squeezed" and never suppresses its border no matter how narrow that width is —
    /// see <see cref="PaneBorderRenderer"/>'s <c>suppressed</c> parameter. Non-circular: suppression
    /// only ever removes reserve, so post-suppression inner width can only be greater than what is
    /// tested here, never re-triggering the condition. Callers supply the inner width rather than
    /// this method deriving it, since only they hold the collapse-aware excludes needed for it.
    /// </summary>
    public static bool ShouldSuppressBorder(Pane pane, int innerWidth)
    {
        if (pane.Border.Style is null || innerWidth >= RowLayout.MinUsableWidth)
        {
            return false;
        }

        var kind = ClassifySize(pane.Size).Kind;
        return kind is SizeKind.Fill or SizeKind.Percent;
    }

    /// <summary>SPEC-V2-FRAMEWORK.md §4: whether a pane is "pane-width eligible" for a command item's cache stamp — a <c>content</c>-sized pane never is, since its width is a function of its own content rather than an independent layout grant.</summary>
    public static bool IsContentSized(Pane pane) => ClassifySize(pane.Size).Kind == SizeKind.Content;

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §9.4: whether a pane's size classifies as <see cref="SizeKind.Fill"/> —
    /// the catch-all for <c>"fill"</c>, <c>"auto"</c>, and anything unrecognized. <c>pane-no-items</c>
    /// needs this alongside <see cref="IsContentSized"/>: a <c>fixed</c>/<c>percent</c> pane with no
    /// items is a deliberate spacer (§2.11) and keeps its declared extent, but a <c>content</c>/
    /// <c>fill</c> pane with no items collapses to zero, so its declaration did nothing.
    /// </summary>
    internal static bool IsFillSized(Pane pane) => ClassifySize(pane.Size).Kind == SizeKind.Fill;

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §2.10: <c>reserve(p)</c> — the width a pane itself consumes for its own
    /// border, charged wherever that pane is measured (its own <see cref="Floor"/>/request, or a
    /// split subtracting its own border before dividing width among children). Padding (2 columns)
    /// is unconditional whenever the pane draws a border at all; each active vertical edge
    /// (<see cref="PaneBorderEdges.Left"/>/<see cref="PaneBorderEdges.Right"/>) adds one more
    /// column. Named so every call site shares one definition instead of repeating the arithmetic.
    /// </summary>
    internal static int OwnBorderReserve(Pane pane) => OwnBorderReserve(pane.Border);

    internal static int OwnBorderReserve(PaneBorder border) => border.Style is null
        ? 0
        : 2 + (border.Edges.Left ? 1 : 0) + (border.Edges.Right ? 1 : 0);

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §2.10.2: <c>reserve(p)</c> for a vertical-split child under
    /// <c>collapse:true</c> — an edge facing an interior (shared) boundary is not this pane's own
    /// column to charge; the boundary itself already reserves exactly one column for it (see
    /// <see cref="BoundaryCost"/>). <paramref name="excludeLeft"/>/<paramref name="excludeRight"/>
    /// are true only for a child whose Left/Right respectively faces such a boundary — never for an
    /// outer edge (the split's first child's Left, or its last child's Right).
    /// </summary>
    internal static int OwnBorderReserve(Pane pane, bool excludeLeft, bool excludeRight) =>
        OwnBorderReserve(pane.Border, excludeLeft, excludeRight);

    internal static int OwnBorderReserve(PaneBorder border, bool excludeLeft, bool excludeRight)
    {
        if (border.Style is null)
        {
            return 0;
        }

        var left = !excludeLeft && border.Edges.Left;
        var right = !excludeRight && border.Edges.Right;
        return 2 + (left ? 1 : 0) + (right ? 1 : 0);
    }

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §2.10.1 rule 5: <c>rowReserve(p)</c> — the row-count counterpart to
    /// <see cref="OwnBorderReserve(Pane)"/>, charged wherever a pane's row budget is computed. Only
    /// active horizontal edges (<see cref="PaneBorderEdges.Top"/>/<see cref="PaneBorderEdges.Bottom"/>)
    /// count — there is no unconditional row the way padding is unconditional on the width side.
    /// </summary>
    internal static int OwnRowReserve(Pane pane) => pane.Border.Style is null
        ? 0
        : (pane.Border.Edges.Top ? 1 : 0) + (pane.Border.Edges.Bottom ? 1 : 0);

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §2.10: what a vertical split reserves for itself before dividing width
    /// among its <paramref name="childCount"/> children — its own <see cref="OwnBorderReserve"/>
    /// plus the gutters between children (<c>collapse: false</c>'s arithmetic, the only kind
    /// implemented today; each child's own edges are its own <see cref="OwnBorderReserve"/>,
    /// already folded into that child's <see cref="Floor"/>/request, not charged again here).
    /// Named per §9.5's rule applied to arithmetic instead of references: §9.8's "can this ever
    /// fit" check calls this exact function rather than holding a second copy that can drift from
    /// what the allocator below actually runs.
    /// </summary>
    internal static int BoundaryCost(Pane split, int childCount) => BoundaryCost(split, childCount, collapse: false);

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §2.10.2: under <c>collapse:true</c>, every interior boundary is exactly
    /// one shared column — <c>avail = splitInnerWidth − (N−1)</c> — regardless of the pane's own
    /// declared <see cref="Pane.Gutter"/> value; <c>collapse:false</c>'s <c>gutter × (N−1)</c> is
    /// unchanged.
    /// </summary>
    internal static int BoundaryCost(Pane split, int childCount, bool collapse) =>
        OwnBorderReserve(split) + (collapse ? Math.Max(0, childCount - 1) : split.Gutter * Math.Max(0, childCount - 1));

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §9.8: a pane's own declared fixed-cell size, when its <see cref="Pane.Size"/>
    /// classifies as <see cref="SizeKind.Fixed"/> — the structural checks need this exact
    /// classification, the same one <see cref="ClassifySize"/> already runs for the allocator, not a
    /// second parse of the same string.
    /// </summary>
    internal static int? FixedSize(Pane pane) => ClassifySize(pane.Size) is { Kind: SizeKind.Fixed } spec ? spec.FixedValue : null;

    private static ResolvedPane ResolveNode(Pane pane, int outerWidth, ItemContext ctx, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, Segment> compounds, Func<Pane, int?, int>? measureOverride, Func<Pane, int, int>? rowCountOverride, RenderNoteCollector notes, bool collapse)
    {
        if (pane.Split == PaneSplit.None || pane.Children.Count == 0)
        {
            return new ResolvedPane(pane, outerWidth, Array.Empty<ResolvedPane>(), EffectiveSplit: PaneSplit.None);
        }

        // SPEC-88 §4.1: Flex resolves to a concrete orientation here, strictly above both the
        // Horizontal branch and the vertical entry points below, so distribute:"min-rows" (which
        // dispatches inside the vertical branch) sees the same decision a greedy split would.
        // ResolveFlexOrientation never returns Flex, so effectiveSplit — and every EffectiveSplit
        // this method returns below — can never carry it either.
        var orientation = pane.Split == PaneSplit.Flex
            ? ResolveFlexOrientation(pane, outerWidth, ctx, values, compounds, measureOverride, rowCountOverride, collapse, notes)
            : new FlexOrientationResult(pane.Split, CachedMinRowsFirstPass: null);
        var effectiveSplit = orientation.Split;

        if (effectiveSplit == PaneSplit.Horizontal)
        {
            // §2.3.5: stacked children share the split's inner width instead of dividing it, so
            // they are charged no gutters — but the split's OWN border reserve is not theirs to
            // spend, exactly as BoundaryCost charges it on the vertical side. Granting outerWidth
            // here made every stacked child overflow its parent's content box by reserve(p) at once
            // (SPEC-96). Each child is then sized independently against that ceiling (SPEC-97).
            var stackedAvail = Math.Max(0, outerWidth - OwnBorderReserve(pane));
            var horizontalChildren = pane.Children
                .Select((c, i) => ResolveNode(c, StackedWidth(c, stackedAvail, ctx, values, compounds, measureOverride, i + 1, notes), ctx, values, compounds, measureOverride, rowCountOverride, notes, collapse))
                .ToList();
            return new ResolvedPane(pane, outerWidth, horizontalChildren, EffectiveSplit: PaneSplit.Horizontal);
        }

        var alloc = pane.Distribute switch
        {
            PaneDistribute.MinRows => ResolveVerticalMinRows(pane, outerWidth, ctx, values, compounds, measureOverride, rowCountOverride, notes, collapse, orientation.CachedMinRowsFirstPass),
            PaneDistribute.Even => ResolveVerticalEven(pane, outerWidth, notes, collapse),
            _ => ResolveVertical(pane, outerWidth, ctx, values, compounds, measureOverride, notes, collapse),
        };
        var resolvedChildren = new List<ResolvedPane>(alloc.Children.Count);
        for (var i = 0; i < alloc.Children.Count; i++)
        {
            resolvedChildren.Add(ResolveNode(alloc.Children[i], alloc.Grants[i], ctx, values, compounds, measureOverride, rowCountOverride, notes, collapse));
        }

        return new ResolvedPane(pane, outerWidth, resolvedChildren, EffectiveSplit: PaneSplit.Vertical);
    }

    // SPEC-95-flex-side-by-side-wrapped.md §5.4: the orientation decision, plus — only on the
    // minRowsFeasible branch below — the AllocateMinRowsOnePass result that decision computed,
    // handed to ResolveVerticalMinRows so it is not recomputed (§5.4(a)).
    private readonly record struct FlexOrientationResult(PaneSplit Split, AllocResult? CachedMinRowsFirstPass);

    // SPEC-88 §3.1 / SPEC-88-AMENDMENT-flex-content-orientation.md / SPEC-95-flex-side-by-side-wrapped.md
    // §5.2: side by side when it fits, stacked when it does not and stacking would help, side by
    // side (the drop ladder's better-understood over-constrained answer) when neither does. Uses
    // SideBySideNeed/StackedFloor directly rather than Floor(pane, ...) itself, because §3.1's
    // predicate is defined over the branch VALUES, not Floor()'s minSize short-circuit (:331-334) —
    // that short-circuit is §3.4's contract for Floor(flexPane) as seen by an ANCESTOR, a different
    // question from what orientation this pane itself should adopt.
    //
    // SPEC-95 §5.2: under distribute:"min-rows" only, a second side-by-side test is inserted between
    // the fast (natural-width) path and the stacked branch — minRowsFeasible(p, W), i.e. whether the
    // SAME AllocateMinRowsOnePass call ResolveVerticalMinRows is about to run actually fits W. Greedy
    // and even cannot shrink a child (§5.2), so they keep exactly the #94 predicate, unchanged.
    private static FlexOrientationResult ResolveFlexOrientation(Pane pane, int outerWidth, ItemContext ctx, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, Segment> compounds, Func<Pane, int?, int>? measureOverride, Func<Pane, int, int>? rowCountOverride, bool collapse, RenderNoteCollector notes)
    {
        var sideBySideNeed = SideBySideNeed(pane, ctx, values, compounds, measureOverride, collapse);
        if (sideBySideNeed <= outerWidth)
        {
            return new FlexOrientationResult(PaneSplit.Vertical, CachedMinRowsFirstPass: null);
        }

        if (pane.Distribute == PaneDistribute.MinRows)
        {
            var firstPass = AllocateMinRowsOnePass(pane, pane.Children, outerWidth, ctx, values, compounds, measureOverride, rowCountOverride, collapse);
            if (firstPass.Feasible)
            {
                return new FlexOrientationResult(PaneSplit.Vertical, firstPass.Alloc);
            }
        }

        var stackedFloor = StackedFloor(pane, collapse);
        if (stackedFloor <= outerWidth)
        {
            notes.Add($"pane {pane.Children.Count}: flex split stacked; children need {sideBySideNeed} columns at {outerWidth} columns");
            return new FlexOrientationResult(PaneSplit.Horizontal, CachedMinRowsFirstPass: null);
        }

        return new FlexOrientationResult(PaneSplit.Vertical, CachedMinRowsFirstPass: null);
    }

    // SPEC-88-AMENDMENT-flex-content-orientation.md §2/§3.3: the side-by-side quantity the flex
    // orientation predicate itself compares against W — natural (unwrapped) width for a
    // content-sized LEAF child (no split of its own), Floor() for every other child, including a
    // content-sized child that has its own split (§3.2: CandidateSegments would return the default
    // builtin segment list for a childless-items container, which has no relationship to what that
    // pane actually renders). SideBySideFloor's own contract and callers (§3.4's Floor(flexPane))
    // are unchanged; this is an additional quantity, not a replacement.
    private static int SideBySideNeed(Pane p, ItemContext ctx, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, Segment> compounds, Func<Pane, int?, int>? measureOverride, bool collapse)
    {
        var n = p.Children.Count;
        var boundary = collapse ? Math.Max(0, n - 1) : p.Gutter * Math.Max(0, n - 1);
        var sum = 0;
        for (var i = 0; i < n; i++)
        {
            var child = p.Children[i];
            var excludeLeft = collapse && i > 0;
            var excludeRight = collapse && i < n - 1;
            var isContentLeaf = IsContentSized(child) && (child.Split == PaneSplit.None || child.Children.Count == 0);
            sum += isContentLeaf
                ? (measureOverride is not null ? measureOverride(child, null) : MeasureRequest(child, null, ctx, values, compounds, excludeLeft, excludeRight))
                : Floor(child, collapse, excludeLeft, excludeRight);
        }

        return sum + boundary;
    }

    // ---- vertical axis: the graded fixpoint ----

    private static AllocResult ResolveVertical(Pane split, int splitOuterWidth, ItemContext ctx, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, Segment> compounds, Func<Pane, int?, int>? measureOverride, RenderNoteCollector notes, bool collapse)
    {
        // §2.10.2: a test-supplied measureOverride is position-blind by design (§10.6's fixpoint
        // stubs); collapse's edge exclusion only applies to the real measurement path.
        int Measure(Pane c, int? w, int index, int count) => measureOverride is not null
            ? measureOverride(c, w)
            : MeasureRequest(c, w, ctx, values, compounds, excludeLeft: collapse && index > 0, excludeRight: collapse && index < count - 1);

        var initialChildren = split.Children;
        var requests = initialChildren
            .Select((c, i) => Measure(c, null, i, initialChildren.Count))
            .ToArray();

        var result = AllocateWithDrop(split, initialChildren, splitOuterWidth, requests, notes, collapse);
        requests = requests.Take(result.Children.Count).ToArray();

        for (var pass = 1; pass < MaxPasses; pass++)
        {
            var nextRequests = new int[result.Children.Count];
            var changed = false;

            for (var i = 0; i < result.Children.Count; i++)
            {
                var child = result.Children[i];
                if (ClassifySize(child.Size).Kind != SizeKind.Content)
                {
                    nextRequests[i] = requests[i];
                    continue;
                }

                var remeasured = Measure(child, result.Grants[i], i, result.Children.Count);
                var clamped = Math.Min(remeasured, requests[i]); // monotone: a request may never grow between passes.
                nextRequests[i] = clamped;
                changed |= clamped != requests[i];
            }

            if (!changed)
            {
                break;
            }

            requests = nextRequests;
            result = AllocateWithDrop(split, result.Children, splitOuterWidth, requests, notes, collapse);
            requests = requests.Take(result.Children.Count).ToArray();
        }

        return result;
    }

    private enum SizeKind { Fixed, Percent, Content, Fill }

    private readonly record struct SizeSpec(SizeKind Kind, int FixedValue, double Pct);

    private readonly record struct AllocResult(IReadOnlyList<Pane> Children, IReadOnlyList<int> Grants);

    private static SizeSpec ClassifySize(string? size)
    {
        var trimmed = (size ?? "auto").Trim();

        if (int.TryParse(trimmed, out var fixedVal))
        {
            return new SizeSpec(SizeKind.Fixed, fixedVal, 0);
        }

        if (trimmed.EndsWith('%') && double.TryParse(trimmed[..^1], out var pct))
        {
            return new SizeSpec(SizeKind.Percent, 0, pct / 100.0);
        }

        if (string.Equals(trimmed, "content", StringComparison.OrdinalIgnoreCase))
        {
            return new SizeSpec(SizeKind.Content, 0, 0);
        }

        // "fill", "auto", and anything unrecognized.
        return new SizeSpec(SizeKind.Fill, 0, 0);
    }

    /// <summary>
    /// True when <paramref name="size"/> was present but matched none of §2.3's recognized forms
    /// (an integer, a percent, <c>"content"</c>, <c>"fill"</c>, or <c>"auto"</c>) — distinct from an
    /// absent field, both of which <see cref="ClassifySize"/> silently falls back to
    /// <see cref="SizeKind.Fill"/>. §9.4's config diagnostics need this distinction; the renderer's
    /// fallback does not.
    /// </summary>
    internal static bool IsUnrecognizedSize(string? size)
    {
        if (string.IsNullOrWhiteSpace(size))
        {
            return false;
        }

        var trimmed = size.Trim();
        if (int.TryParse(trimmed, out _))
        {
            return false;
        }

        if (trimmed.EndsWith('%') && double.TryParse(trimmed[..^1], out _))
        {
            return false;
        }

        return !string.Equals(trimmed, "content", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(trimmed, "fill", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(trimmed, "auto", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// §2.3/§9.4: true when <paramref name="size"/> is the <c>"auto"</c> synonym for <c>"fill"</c> —
    /// legal and unchanged in behavior, but its plain-English reading suggests sizing to content
    /// (what <c>"content"</c> actually does), the opposite of what it resolves to.
    /// </summary>
    internal static bool IsDeprecatedSizeAlias(string? size) =>
        !string.IsNullOrWhiteSpace(size) && string.Equals(size.Trim(), "auto", StringComparison.OrdinalIgnoreCase);

    // §2.3 floor(p): the minimum outer width a pane can be reduced to before it is dropped.
    // A vertical split's children divide the available width, so its floor is Σ floor(children) +
    // gutters. A horizontal split's children stack and each takes the full width, so its floor is
    // max(floor(children)) — untested in Phase 3 since no acceptance or required test nests a
    // horizontal split inside a vertical one.
    private static int Floor(Pane p) => Floor(p, collapse: false, excludeLeft: false, excludeRight: false);

    // §2.10.2: collapse threads through so a nested vertical split's own boundary cost switches to
    // the one-column-per-boundary formula, and excludeLeft/excludeRight (set by the caller only for
    // a vertical split's own immediate children, never below) drop a shared edge's column from this
    // pane's own floor — the boundary itself already reserves it.
    private static int Floor(Pane p, bool collapse, bool excludeLeft, bool excludeRight)
    {
        if (p.MinSize is int min)
        {
            return min;
        }

        if (p.Split != PaneSplit.None && p.Children.Count > 0)
        {
            if (p.Split == PaneSplit.Horizontal)
            {
                return StackedFloor(p, collapse);
            }

            // SPEC-88 §3.4: a Flex pane can render in either orientation, so its floor is the
            // minimum over both — evaluated via the same branch expressions this method uses for a
            // declared Vertical/Horizontal pane, with the same collapse/exclude threading, never a
            // restated formula (§3.4.2: the two branches are not interchangeable under collapse).
            if (p.Split == PaneSplit.Flex)
            {
                return Math.Min(SideBySideFloor(p, collapse), StackedFloor(p, collapse));
            }

            return SideBySideFloor(p, collapse);
        }

        var spec = ClassifySize(p.Size);
        return spec.Kind switch
        {
            SizeKind.Fixed => spec.FixedValue,
            SizeKind.Content => 0,
            _ => RowLayout.MinUsableWidth + OwnBorderReserve(p, excludeLeft, excludeRight),
        };
    }

    // §2.3: the value Floor()'s Horizontal branch computes for a split with children — its
    // children stack, each spanning the full width, so the floor is the tallest child's floor.
    // Named and extracted (not inlined in Floor()) because SPEC-88 §3.1's flex-orientation
    // predicate and §3.4's Flex floor both need exactly this value, and §1.1 requires one
    // statement of the rule rather than two that can drift.
    private static int StackedFloor(Pane p, bool collapse) =>
        p.Children.Max(c => Floor(c, collapse, excludeLeft: false, excludeRight: false));

    // §2.3.5: a stacked child is sized independently against the split's inner width — never a
    // share of it, because stacked children occupy different rows and cannot contend for a
    // column. No allocation pass, no contention bookkeeping, and no drop: dropping a stacked
    // child frees width for nobody. The ceiling always wins over a declared size or minSize, and
    // the clamp is reported.
    private static int StackedWidth(Pane child, int avail, ItemContext ctx, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, Segment> compounds, Func<Pane, int?, int>? measureOverride, int position, RenderNoteCollector notes)
    {
        var spec = ClassifySize(child.Size);
        var raw = spec.Kind switch
        {
            SizeKind.Fixed => spec.FixedValue,
            SizeKind.Percent => (int)Math.Round(spec.Pct * avail, MidpointRounding.AwayFromZero),
            SizeKind.Content => measureOverride?.Invoke(child, avail) ?? MeasureRequest(child, avail, ctx, values, compounds),
            _ => avail, // Fill, and the unspecified default — the pre-§2.3.5 behaviour of every stacked child.
        };

        var lo = child.MinSize ?? 0;
        var hi = child.MaxSize ?? int.MaxValue;
        var bounded = Math.Clamp(raw, lo, hi);
        var granted = Math.Clamp(bounded, 0, avail);
        if (granted < bounded)
        {
            notes.Add($"pane {position}: {bounded} columns requested, clamped to {avail} at {avail} columns");
        }

        return granted;
    }

    // §2.3/§2.10.2: the value Floor()'s vertical (non-Horizontal) branch computes for a split with
    // children — its children divide the width, so the floor is their sum plus boundary cost, each
    // child's own floor threaded with the collapse-aware edge exclusions its position implies.
    // Extracted for the same reason as StackedFloor above.
    private static int SideBySideFloor(Pane p, bool collapse)
    {
        var n = p.Children.Count;
        var boundary = collapse ? Math.Max(0, n - 1) : p.Gutter * Math.Max(0, n - 1);
        var sum = 0;
        for (var i = 0; i < n; i++)
        {
            sum += Floor(p.Children[i], collapse, excludeLeft: collapse && i > 0, excludeRight: collapse && i < n - 1);
        }

        return sum + boundary;
    }

    // SPEC-2.3-drop-predicate.md §3 / SPEC-2.3-suppression-predicate.md §4: the drop-retry loops'
    // per-pane test — never below 1 cell (§3(a): Floor returns 0 for content panes, so a bare
    // grant-vs-Floor test would never fire for them), and suppression-aware for fill/percent
    // (§3(b)): border suppression is tested on the pane's own PRE-suppression inner width (grant
    // minus its own border reserve, using the same excludes this call already received), and when
    // it applies the floor drops to MinUsableWidth alone, since a suppressed border no longer needs
    // OwnBorderReserve. Reuses ShouldSuppressBorder as-is rather than restating its condition, so
    // the two checks cannot drift apart.
    private static int DropFloor(Pane pane, int grant, bool collapse, bool excludeLeft, bool excludeRight)
    {
        var preSuppressionInnerWidth = grant - OwnBorderReserve(pane, excludeLeft, excludeRight);
        var floor = ShouldSuppressBorder(pane, preSuppressionInnerWidth)
            ? RowLayout.MinUsableWidth
            : Floor(pane, collapse, excludeLeft, excludeRight);
        return Math.Max(1, floor);
    }

    // One run of the six-step allocation (§2.3), operating on whatever child list/request set the
    // caller currently has — a single pass, no fixpoint, no dropping.
    private static AllocResult AllocateOnePass(Pane split, IReadOnlyList<Pane> children, int splitOuterWidth, IReadOnlyList<int> requests, bool collapse)
    {
        var avail = Math.Max(0, splitOuterWidth - BoundaryCost(split, children.Count, collapse));

        var kinds = children.Select(c => ClassifySize(c.Size)).ToArray();
        var grants = new int[children.Count];
        var rem = avail;

        // Step 2: fixed.
        for (var i = 0; i < children.Count; i++)
        {
            if (kinds[i].Kind == SizeKind.Fixed)
            {
                grants[i] = kinds[i].FixedValue;
                rem -= grants[i];
            }
        }

        // Step 3: reserve.
        var reserve = 0;
        for (var i = 0; i < children.Count; i++)
        {
            if (kinds[i].Kind is SizeKind.Percent or SizeKind.Fill)
            {
                reserve += Floor(children[i], collapse, excludeLeft: collapse && i > 0, excludeRight: collapse && i < children.Count - 1);
            }
        }

        // Step 4: content, declaration order.
        var contentIndices = new List<int>();
        for (var i = 0; i < children.Count; i++)
        {
            if (kinds[i].Kind == SizeKind.Content)
            {
                contentIndices.Add(i);
            }
        }

        for (var ci = 0; ci < contentIndices.Count; ci++)
        {
            var i = contentIndices[ci];
            var laterMinSum = 0;
            for (var cj = ci + 1; cj < contentIndices.Count; cj++)
            {
                laterMinSum += children[contentIndices[cj]].MinSize ?? 0;
            }

            var cap = rem - reserve - laterMinSum;
            var minSize = children[i].MinSize ?? 0;
            var maxSize = children[i].MaxSize ?? int.MaxValue;
            var upperBound = Math.Max(minSize, Math.Min(maxSize, cap));
            var grant = Math.Clamp(requests[i], minSize, upperBound);
            grants[i] = grant;
            rem -= grant;
        }

        // Step 5: percent.
        for (var i = 0; i < children.Count; i++)
        {
            if (kinds[i].Kind == SizeKind.Percent)
            {
                var raw = (int)Math.Round(kinds[i].Pct * avail, MidpointRounding.AwayFromZero);
                var grant = Math.Clamp(raw, 0, Math.Max(0, rem));
                grants[i] = grant;
                rem -= grant;
            }
        }

        // Step 6: fill, leftover to the leftmost.
        var fillIndices = new List<int>();
        for (var i = 0; i < children.Count; i++)
        {
            if (kinds[i].Kind == SizeKind.Fill)
            {
                fillIndices.Add(i);
            }
        }

        if (fillIndices.Count > 0)
        {
            var remClamped = Math.Max(0, rem);
            var each = remClamped / fillIndices.Count;
            var leftover = remClamped - each * fillIndices.Count;
            for (var fi = 0; fi < fillIndices.Count; fi++)
            {
                grants[fillIndices[fi]] = each + (fi == 0 ? leftover : 0);
            }
        }

        return new AllocResult(children, grants);
    }

    // §2.3's over-constrained handling: a non-fixed pane granted under 1 cell means the split
    // cannot honor everyone at once — drop the last child and recompute from step 1. Bounded: each
    // iteration strictly shrinks the child list, so this always terminates.
    private static AllocResult AllocateWithDrop(Pane split, IReadOnlyList<Pane> children, int splitOuterWidth, IReadOnlyList<int> requests, RenderNoteCollector notes, bool collapse)
    {
        var current = children;
        var currentRequests = requests;

        while (true)
        {
            var result = AllocateOnePass(split, current, splitOuterWidth, currentRequests, collapse);

            var tooSmall = false;
            for (var i = 0; i < result.Grants.Count; i++)
            {
                if (ClassifySize(current[i].Size).Kind == SizeKind.Fixed)
                {
                    continue;
                }

                var floor = DropFloor(current[i], result.Grants[i], collapse, excludeLeft: collapse && i > 0, excludeRight: collapse && i < current.Count - 1);
                if (result.Grants[i] < floor)
                {
                    tooSmall = true;
                    break;
                }
            }

            // §67a: fixed-size panes are exempt from the per-pane tooSmall check above (their
            // grant is their declared size, never shrunk), so two fixed panes whose declared sizes
            // alone exceed the split's budget would otherwise sail through with neither pane
            // reporting too-small. Recomputed per iteration, not hoisted out of the loop — the
            // gutter count (and therefore avail) falls with the child count on every drop.
            var avail = Math.Max(0, splitOuterWidth - BoundaryCost(split, current.Count, collapse));
            var overAllocated = result.Grants.Sum() > avail;

            if ((!tooSmall && !overAllocated) || current.Count <= 1)
            {
                return ClampToAvail(result, avail, splitOuterWidth, notes);
            }

            // §9.8.2: the dropped pane is always the current list's last child, whose 1-based
            // position in it equals current.Count before this truncation — stable across repeated
            // drops because only the tail is ever removed. SPEC-2.3-drop-predicate.md §4: an
            // over-allocated split wins the tie over a below-floor one when both hold on the same
            // iteration, since the sum violation applies to every remaining pane at once.
            if (overAllocated)
            {
                notes.Add($"pane {current.Count} dropped: children need {result.Grants.Sum()} columns at {splitOuterWidth} columns");
            }
            else
            {
                var lastIndex = current.Count - 1;
                var lastFloor = DropFloor(current[lastIndex], result.Grants[lastIndex], collapse, excludeLeft: collapse && lastIndex > 0, excludeRight: false);
                notes.Add($"pane {current.Count} dropped: {result.Grants[lastIndex]} columns is under its {lastFloor}-column floor at {splitOuterWidth} columns");
            }

            current = current.Take(current.Count - 1).ToList();
            currentRequests = currentRequests.Take(current.Count).ToList();
        }
    }

    // SPEC-2.3-residual-pane-overwidth.md §4.1/§4.2: both drop loops above exit their
    // `current.Count <= 1` branch unconditionally, even when the sole surviving child's grant
    // still exceeds what the split actually has — the per-pane tooSmall check never fires for a
    // single fixed-size child, and overAllocated stops being consulted once no further pane is
    // left to drop. Called at both loop exits, strictly after the drop decision, so it only ever
    // tightens a result the drop loop already settled on, never substitutes for it.
    private static AllocResult ClampToAvail(AllocResult result, int avail, int splitOuterWidth, RenderNoteCollector notes)
    {
        if (result.Grants.Count == 0)
        {
            return result;
        }

        int[]? grants = null;
        for (var i = 0; i < result.Grants.Count; i++)
        {
            if (result.Grants[i] <= avail)
            {
                continue;
            }

            grants ??= result.Grants.ToArray();
            var requested = grants[i];
            grants[i] = avail;
            notes.Add($"pane {i + 1}: {requested} columns requested, clamped to {avail} at {splitOuterWidth} columns");
        }

        return grants is null ? result : new AllocResult(result.Children, grants);
    }

    // ---- vertical axis: min-rows distribution (§2.3.1) ----

    /// <summary>
    /// Test-only diagnostic: counts calls to <see cref="RowCountAt"/> — one packer invocation
    /// each — so a test can assert the actual cost of a min-rows resolve instead of reasoning
    /// about it. Production callers never read this.
    ///
    /// <see cref="ThreadStaticAttribute"/> is sound here specifically because §5 resolves once
    /// per render, synchronously: nothing on the min-rows path (<see cref="SolveMinRows"/>
    /// included) ever awaits, so the thread that zeroes this counter is guaranteed to be the same
    /// thread that later reads it, even though xUnit runs other test classes concurrently on
    /// other threads. If sizing ever gains an <c>await</c>, that guarantee breaks and this
    /// attribute becomes wrong rather than merely redundant — re-check this reasoning before
    /// introducing one on the min-rows path.
    /// </summary>
    [ThreadStatic]
    internal static int MinRowsPackerInvocationCount;

    // Structurally mirrors AllocateWithDrop's drop-retry loop rather than sharing it: the two
    // allocate differently enough (AllocateWithDrop threads a per-child request array that
    // min-rows has no equivalent of) that forcing a shared implementation would risk the
    // unchanged greedy path for a small amount of duplication.
    private static AllocResult ResolveVerticalMinRows(Pane split, int splitOuterWidth, ItemContext ctx, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, Segment> compounds, Func<Pane, int?, int>? measureOverride, Func<Pane, int, int>? rowCountOverride, RenderNoteCollector notes, bool collapse, AllocResult? cachedFirstPass = null)
    {
        var current = split.Children;
        // SPEC-95-flex-side-by-side-wrapped.md §5.4(a): ResolveFlexOrientation, on the path that
        // decides side-by-side via minRowsFeasible, already ran this exact first pass (same split,
        // same width, same collapse) to answer the predicate — reusing it here rather than
        // recomputing avoids doubling MinRowsPackerInvocationCount. Consumed once: any subsequent
        // drop-retry iteration recomputes normally, unchanged from before.
        var pendingFirstPass = cachedFirstPass;

        while (true)
        {
            var result = pendingFirstPass ?? AllocateMinRowsOnePass(split, current, splitOuterWidth, ctx, values, compounds, measureOverride, rowCountOverride, collapse).Alloc;
            pendingFirstPass = null;

            var tooSmall = false;
            for (var i = 0; i < result.Grants.Count; i++)
            {
                if (ClassifySize(current[i].Size).Kind == SizeKind.Fixed)
                {
                    continue;
                }

                // SPEC-2.3-drop-predicate.md §8: SolveMinRows's own candidate floors come from the
                // 1-arg Floor overload (collapse-blind), so this drop test matches that — collapse:
                // false, no excluded edges — rather than the split's actual collapse flag. Fixing
                // min-rows' own collapse-blindness is a separate, larger change and out of scope
                // here; this keeps the drop test consistent with the allocation it is checking.
                var floor = DropFloor(current[i], result.Grants[i], collapse: false, excludeLeft: false, excludeRight: false);
                if (result.Grants[i] < floor)
                {
                    tooSmall = true;
                    break;
                }
            }

            // §67: SolveMinRows's over-constrained fallback (`return lo`) hands back each
            // candidate's floor with no reference to `r`, so the sum can exceed what the split
            // actually has — recomputed per iteration since the gutter count (and so `avail`)
            // falls with each drop.
            var avail = Math.Max(0, splitOuterWidth - BoundaryCost(split, current.Count, collapse));
            var overAllocated = result.Grants.Sum() > avail;

            if ((!tooSmall && !overAllocated) || current.Count <= 1)
            {
                return ClampToAvail(result, avail, splitOuterWidth, notes);
            }

            // §9.8.2: the dropped pane is always the current list's last child, whose 1-based
            // position in it equals current.Count before this truncation — stable across repeated
            // drops because only the tail is ever removed. §2.3.3:1220-1222 makes this note the
            // stated observable consequence of min-rows' own over-constrained fallback.
            // SPEC-2.3-drop-predicate.md §4: over-allocated wins the tie over below-floor.
            if (overAllocated)
            {
                notes.Add($"pane {current.Count} dropped: children need {result.Grants.Sum()} columns at {splitOuterWidth} columns");
            }
            else
            {
                var lastIndex = current.Count - 1;
                var lastFloor = DropFloor(current[lastIndex], result.Grants[lastIndex], collapse: false, excludeLeft: false, excludeRight: false);
                notes.Add($"pane {current.Count} dropped: {result.Grants[lastIndex]} columns is under its {lastFloor}-column floor at {splitOuterWidth} columns");
            }

            current = current.Take(current.Count - 1).ToList();
        }
    }

    // SPEC-95-flex-side-by-side-wrapped.md §5.4: Feasible mirrors MinRowsSolveResult.Feasible for
    // the whole pass — true when there were no content/fill candidates to search at all (nothing to
    // fail), so a caller with zero candidates never misreads "vacuously fine" as "over-constrained".
    // ResolveFlexOrientation's minRowsFeasible(p, W) reads exactly this from the SAME pass it hands
    // to ResolveVerticalMinRows (§5.4(a)), rather than recomputing it.
    private readonly record struct MinRowsPassResult(AllocResult Alloc, bool Feasible);

    // One run of the min-rows allocation (§2.3.1): fixed and percent panes take their width
    // first, mirroring AllocateOnePass's own step order and matching R's own prose definition —
    // "the extent remaining after fixed and percent panes and gutters have taken theirs" — then
    // every content/fill pane is a candidate for the row-count search.
    private static MinRowsPassResult AllocateMinRowsOnePass(Pane split, IReadOnlyList<Pane> children, int splitOuterWidth, ItemContext ctx, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, Segment> compounds, Func<Pane, int?, int>? measureOverride, Func<Pane, int, int>? rowCountOverride, bool collapse)
    {
        var avail = Math.Max(0, splitOuterWidth - BoundaryCost(split, children.Count, collapse));

        var kinds = children.Select(c => ClassifySize(c.Size)).ToArray();
        var grants = new int[children.Count];
        var rem = avail;

        for (var i = 0; i < children.Count; i++)
        {
            if (kinds[i].Kind == SizeKind.Fixed)
            {
                grants[i] = kinds[i].FixedValue;
                rem -= grants[i];
            }
        }

        for (var i = 0; i < children.Count; i++)
        {
            if (kinds[i].Kind == SizeKind.Percent)
            {
                var raw = (int)Math.Round(kinds[i].Pct * avail, MidpointRounding.AwayFromZero);
                var grant = Math.Clamp(raw, 0, Math.Max(0, rem));
                grants[i] = grant;
                rem -= grant;
            }
        }

        var candidateIndices = new List<int>();
        for (var i = 0; i < children.Count; i++)
        {
            if (kinds[i].Kind is SizeKind.Content or SizeKind.Fill)
            {
                candidateIndices.Add(i);
            }
        }

        var feasible = true;
        if (candidateIndices.Count > 0)
        {
            var r = Math.Max(0, rem);
            var solved = SolveMinRows(children, candidateIndices, r, ctx, values, compounds, measureOverride, rowCountOverride);
            feasible = solved.Feasible;
            for (var ci = 0; ci < candidateIndices.Count; ci++)
            {
                grants[candidateIndices[ci]] = solved.Widths[ci];
            }
        }

        return new MinRowsPassResult(new AllocResult(children, grants), feasible);
    }

    // SPEC-95-flex-side-by-side-wrapped.md §5.1: SolveMinRows' own search floor. Floor() itself
    // returns 0 for a content-sized leaf (SizeKind.Content => 0, unchanged per
    // SPEC-88-AMENDMENT-flex-content-orientation.md §5) — using it as SolveMinRows' lower bound
    // sends RowCountAt(candidate, 0) into the search (§3.1), which degenerates maxT the same way
    // the flex orientation predicate degenerated before #94. This is a solver-local floor with its
    // own name, not a redefinition of Floor(): RowLayout.MinUsableWidth + OwnBorderReserve(c) is
    // already what Floor() gives fill/percent panes at its `_` branch (:427), so a content leaf
    // gets the same treatment inside this solver only. Uses the same content-sized-leaf test
    // SPEC-88-AMENDMENT §3.3 fixes, so the two cannot drift.
    //
    // §5.1.1: the maxSize clause is tested FIRST and wins. :870 derives hi from
    // Math.Max(Math.Min(MaxSize ?? r, r), lo) — raising lo above a declared MaxSize would raise hi
    // to match, silently discarding the author's explicit ceiling in favour of a default they never
    // wrote. Where MaxSize is set and sits below the broadened floor, this returns the unmodified
    // Floor() (pre-#95 behaviour) so :870's hi stays capped at MaxSize.
    private static int SearchFloor(Pane c)
    {
        var floor = Floor(c, collapse: false, excludeLeft: false, excludeRight: false);
        var broadenedFloor = RowLayout.MinUsableWidth + OwnBorderReserve(c);
        if (c.MaxSize is int maxSize && maxSize < broadenedFloor)
        {
            return floor;
        }

        var isContentLeaf = IsContentSized(c) && (c.Split == PaneSplit.None || c.Children.Count == 0);
        return isContentLeaf ? Math.Max(floor, broadenedFloor) : floor;
    }

    // SPEC-95-flex-side-by-side-wrapped.md §5.2: whether SolveMinRows reached its feasible (:840)
    // branch, as opposed to falling through to the over-constrained `lo` fallback — the orientation
    // predicate's minRowsFeasible(p, W) reads exactly this, from the same computation the allocator
    // is about to run (§5.3), rather than a second model of it.
    private readonly record struct MinRowsSolveResult(int[] Widths, bool Feasible);

    // SPEC-V2-FRAMEWORK.md §2.3.1: T is the achievable row count, searched from 1 upward — the
    // first T for which every candidate's minWidth(i, T) fits within r wins, since feasible(T) is
    // monotone (a larger T only relaxes each candidate's minWidth). Bounded by the largest rows_i
    // any candidate reports at its own floor (§2.3.3) — never by item count, which undercounts
    // whenever an item wraps across more than one row. Falls back to every candidate at its own
    // floor when no T up to that bound is feasible (the split as a whole is over-constrained), the
    // same outcome AllocateOnePass's own step 4 falls back to; the outer drop-retry loop in
    // ResolveVerticalMinRows exists for exactly this case.
    private static MinRowsSolveResult SolveMinRows(IReadOnlyList<Pane> children, IReadOnlyList<int> candidateIndices, int r, ItemContext ctx, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, Segment> compounds, Func<Pane, int?, int>? measureOverride, Func<Pane, int, int>? rowCountOverride)
    {
        var n = candidateIndices.Count;
        var candidates = candidateIndices.Select(i => children[i]).ToList();
        var lo = candidates.Select(SearchFloor).ToArray();
        var hi = new int[n];
        for (var ci = 0; ci < n; ci++)
        {
            hi[ci] = Math.Max(Math.Min(candidates[ci].MaxSize ?? r, r), lo[ci]);
        }

        var maxT = 1;
        for (var ci = 0; ci < n; ci++)
        {
            maxT = Math.Max(maxT, RowCountAt(candidates[ci], lo[ci], ctx, values, compounds, rowCountOverride));
        }

        for (var t = 1; t <= maxT; t++)
        {
            var minWidths = new int[n];
            var feasible = true;
            var sum = 0;

            for (var ci = 0; ci < n; ci++)
            {
                var w = MinWidthForRowCount(candidates[ci], lo[ci], hi[ci], t, ctx, values, compounds, rowCountOverride);
                if (w is not int width)
                {
                    feasible = false;
                    break;
                }

                minWidths[ci] = width;
                sum += width;
            }

            if (feasible && sum <= r)
            {
                var surplus = r - sum;
                return new MinRowsSolveResult(WaterFillSurplus(candidates, minWidths, hi, surplus, ctx, values, compounds, measureOverride), Feasible: true);
            }
        }

        return new MinRowsSolveResult(lo, Feasible: false);
    }

    // minWidth(i, T): the narrowest outer width at which candidate i's real rendering — the same
    // packer every other render path calls — achieves T rows or fewer. rows_i(w) is non-increasing
    // in w (more width never adds rows), so binary search over [lo, hi] is valid; returns null when
    // even hi cannot reach T rows.
    private static int? MinWidthForRowCount(Pane candidate, int lo, int hi, int t, ItemContext ctx, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, Segment> compounds, Func<Pane, int, int>? rowCountOverride)
    {
        if (RowCountAt(candidate, hi, ctx, values, compounds, rowCountOverride) > t)
        {
            return null;
        }

        var low = lo;
        var high = hi;
        while (low < high)
        {
            var mid = low + (high - low) / 2;
            if (RowCountAt(candidate, mid, ctx, values, compounds, rowCountOverride) <= t)
            {
                high = mid;
            }
            else
            {
                low = mid + 1;
            }
        }

        return low;
    }

    // rows_i(w): candidate i's real row count at outer width w, via the same packer every leaf
    // actually renders through (PaneAssembler.RenderLeafRows's own path) — §2.3.1 requires "the
    // existing packer, called unchanged", not a re-derived width twin. rowCountOverride, when
    // supplied, replaces the real packer call for min-rows' own tests (§2.3.1); production callers
    // never pass it.
    private static int RowCountAt(Pane candidate, int outerWidth, ItemContext ctx, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, Segment> compounds, Func<Pane, int, int>? rowCountOverride)
    {
        MinRowsPackerInvocationCount++;

        if (rowCountOverride is not null)
        {
            return rowCountOverride(candidate, outerWidth);
        }

        var innerWidth = Math.Max(0, outerWidth - OwnBorderReserve(candidate));
        var segments = CandidateSegments(candidate, ctx, values, compounds);
        var overflow = PaneAssembler.ResolveOverflow(candidate);
        // This is a probe of a candidate width during the row-count search, not a rendered row of
        // the final surface — its own truncations are not the render's, so they get a throwaway
        // collector rather than the one flowing to the caller's real result.
        var buffer = PaneRenderer.RenderLeaf(segments, innerWidth, overflow, candidate.Ellipsis, new RenderNoteCollector(), allowFallback: false);
        return buffer.Rows.Count;
    }

    // SPEC-V2-FRAMEWORK.md §2.3.1: the winning T's leftover width is spent purely on evenness —
    // repeatedly handing one cell to the currently-narrowest eligible candidate — since giving any
    // candidate more width than its own minWidth(i, T) can never raise its row count above T. A
    // content candidate is capped at its own natural (unwrapped) width, matching the existing
    // MeasureRequest call greedy itself uses: extra columns past that point are not a benefit to
    // it, so surplus flows on to whichever candidates can still use it. If every candidate is
    // capped and surplus remains, it is left unspent, exactly as greedy leaves an unconsumed
    // remainder.
    private static int[] WaterFillSurplus(IReadOnlyList<Pane> candidates, int[] minWidths, int[] hi, int surplus, ItemContext ctx, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, Segment> compounds, Func<Pane, int?, int>? measureOverride)
    {
        var n = candidates.Count;
        var widths = (int[])minWidths.Clone();
        var cap = new int[n];

        for (var ci = 0; ci < n; ci++)
        {
            cap[ci] = ClassifySize(candidates[ci].Size).Kind == SizeKind.Content
                ? Math.Min(hi[ci], measureOverride?.Invoke(candidates[ci], null) ?? MeasureRequest(candidates[ci], null, ctx, values, compounds))
                : hi[ci];
        }

        var remaining = surplus;
        while (remaining > 0)
        {
            var narrowest = -1;
            for (var ci = 0; ci < n; ci++)
            {
                if (widths[ci] >= cap[ci])
                {
                    continue;
                }

                if (narrowest == -1 || widths[ci] < widths[narrowest])
                {
                    narrowest = ci;
                }
            }

            if (narrowest == -1)
            {
                break;
            }

            widths[narrowest]++;
            remaining--;
        }

        return widths;
    }

    // ---- vertical axis: even distribution (§2.3) ----

    // Structurally mirrors ResolveVerticalMinRows's drop-retry loop for the same reason: §2.3's
    // over-constrained handling ("no child may resolve below 1 cell ... dropped entirely") applies
    // regardless of which policy divides the remaining extent.
    private static AllocResult ResolveVerticalEven(Pane split, int splitOuterWidth, RenderNoteCollector notes, bool collapse)
    {
        var current = split.Children;

        while (true)
        {
            var result = AllocateEvenOnePass(split, current, splitOuterWidth, collapse);

            var tooSmall = false;
            for (var i = 0; i < result.Grants.Count; i++)
            {
                if (ClassifySize(current[i].Size).Kind == SizeKind.Fixed)
                {
                    continue;
                }

                var floor = DropFloor(current[i], result.Grants[i], collapse, excludeLeft: collapse && i > 0, excludeRight: collapse && i < current.Count - 1);
                if (result.Grants[i] < floor)
                {
                    tooSmall = true;
                    break;
                }
            }

            // §67a: fixed-size panes are exempt from the per-pane tooSmall check above (their
            // grant is their declared size, never shrunk), so two fixed panes whose declared sizes
            // alone exceed the split's budget would otherwise sail through with neither pane
            // reporting too-small. Recomputed per iteration, not hoisted out of the loop — the
            // gutter count (and therefore avail) falls with the child count on every drop.
            var avail = Math.Max(0, splitOuterWidth - BoundaryCost(split, current.Count, collapse));
            var overAllocated = result.Grants.Sum() > avail;

            if ((!tooSmall && !overAllocated) || current.Count <= 1)
            {
                return ClampToAvail(result, avail, splitOuterWidth, notes);
            }

            // §9.8.2: the dropped pane is always the current list's last child, whose 1-based
            // position in it equals current.Count before this truncation — stable across repeated
            // drops because only the tail is ever removed. SPEC-2.3-drop-predicate.md §4: an
            // over-allocated split wins the tie over a below-floor one when both hold on the same
            // iteration, since the sum violation applies to every remaining pane at once.
            if (overAllocated)
            {
                notes.Add($"pane {current.Count} dropped: children need {result.Grants.Sum()} columns at {splitOuterWidth} columns");
            }
            else
            {
                var lastIndex = current.Count - 1;
                var lastFloor = DropFloor(current[lastIndex], result.Grants[lastIndex], collapse, excludeLeft: collapse && lastIndex > 0, excludeRight: false);
                notes.Add($"pane {current.Count} dropped: {result.Grants[lastIndex]} columns is under its {lastFloor}-column floor at {splitOuterWidth} columns");
            }

            current = current.Take(current.Count - 1).ToList();
        }
    }

    // One run of the even allocation (§2.3): fixed and percent panes take their width first,
    // mirroring AllocateOnePass's/AllocateMinRowsOnePass's own step order, then every content/fill
    // pane splits what remains equally, leftover to the leftmost (matching AllocateOnePass's step
    // 6) — ignoring intrinsic measurement and the content/fill distinction entirely, which is the
    // property that keeps the layout from moving as content changes. A content candidate still
    // degrades under its own §2.6 rules at whatever width it lands on; this only decides the width,
    // not what happens to it afterward.
    private static AllocResult AllocateEvenOnePass(Pane split, IReadOnlyList<Pane> children, int splitOuterWidth, bool collapse)
    {
        var avail = Math.Max(0, splitOuterWidth - BoundaryCost(split, children.Count, collapse));

        var kinds = children.Select(c => ClassifySize(c.Size)).ToArray();
        var grants = new int[children.Count];
        var rem = avail;

        for (var i = 0; i < children.Count; i++)
        {
            if (kinds[i].Kind == SizeKind.Fixed)
            {
                grants[i] = kinds[i].FixedValue;
                rem -= grants[i];
            }
        }

        for (var i = 0; i < children.Count; i++)
        {
            if (kinds[i].Kind == SizeKind.Percent)
            {
                var raw = (int)Math.Round(kinds[i].Pct * avail, MidpointRounding.AwayFromZero);
                var grant = Math.Clamp(raw, 0, Math.Max(0, rem));
                grants[i] = grant;
                rem -= grant;
            }
        }

        var candidateIndices = new List<int>();
        for (var i = 0; i < children.Count; i++)
        {
            if (kinds[i].Kind is SizeKind.Content or SizeKind.Fill)
            {
                candidateIndices.Add(i);
            }
        }

        if (candidateIndices.Count > 0)
        {
            var remClamped = Math.Max(0, rem);
            var each = remClamped / candidateIndices.Count;
            var leftover = remClamped - each * candidateIndices.Count;
            for (var ci = 0; ci < candidateIndices.Count; ci++)
            {
                grants[candidateIndices[ci]] = each + (ci == 0 ? leftover : 0);
            }
        }

        return new AllocResult(children, grants);
    }

    // ---- intrinsic measurement: the same fits-or-degrade decision the renderer uses (LeafContent) ----

    private static int MeasureRequest(Pane pane, int? grantedOuterWidth, ItemContext ctx, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, Segment> compounds, bool excludeLeft = false, bool excludeRight = false)
    {
        var borderReserve = OwnBorderReserve(pane, excludeLeft, excludeRight);
        var innerCap = grantedOuterWidth is int g ? Math.Max(0, g - borderReserve) : (int?)null;
        var contentWidth = MeasureInnerContentWidth(pane, innerCap, ctx, values, compounds);

        // SPEC pane-id-title-align §3.7: a content-sized leaf pane's intrinsic (uncapped) request
        // never falls under T+2, so its caption always has room to draw without narrowing the pane
        // further at render time. Only the uncapped request — a narrower re-measurement under an
        // already-granted width reports what fits there, per the comment on
        // MeasureInnerContentWidth/LongestWrappedRowWidth below, and the caption itself just
        // truncates under that grant instead of forcing more width.
        var titleFloor = innerCap is null && pane.Children.Count == 0 && pane.Title is { } title
            ? (TitleCaptionResolver.ResolveWidth(title, ctx, values, compounds) is int t ? t + 2 : 0)
            : 0;

        return Math.Max(contentWidth, titleFloor) + borderReserve;
    }

    private static int MeasureInnerContentWidth(Pane pane, int? innerCap, ItemContext ctx, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, Segment> compounds)
    {
        var segments = CandidateSegments(pane, ctx, values, compounds);
        return innerCap is int cap ? LongestWrappedRowWidth(segments, cap) : UnwrappedWidth(segments);
    }

    // The segment list a pane actually renders — the default 14 builtins when no items are
    // configured, or its resolved items otherwise — shared by the existing content-width
    // measurement above and the min-rows row-count search below so the two never drift apart.
    // Color is never resolved here: RowLayout.Wrap's row-break decision only ever inspects
    // Segment.Plain.Length.
    private static IReadOnlyList<Segment> CandidateSegments(Pane pane, ItemContext ctx, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, Segment> compounds)
    {
        if (pane.Items.Count == 0)
        {
            return SegmentBuilder.Build(ctx);
        }

        var packedGroup = new List<Segment>();

        foreach (var resolved in LeafItems.Resolve(pane.Items, values, ctx, compounds))
        {
            if (resolved.Value is null)
            {
                continue;
            }

            var decision = LeafContent.Decide(resolved, values, compounds);
            packedGroup.Add(SegmentBuilder.BuildItemSegment(decision.Text, null));
        }

        return packedGroup;
    }

    // The same unwrapped-single-row arithmetic RowLayout.Wrap's packing loop produces, so a
    // content pane's intrinsic request equals what it would actually render at that width.
    private static int UnwrappedWidth(IReadOnlyList<Segment> segments)
    {
        if (segments.Count == 0)
        {
            return 0;
        }

        return segments.Sum(s => s.Plain.Length) + RowLayout.SeparatorWidth * (segments.Count - 1);
    }

    // SPEC-V2-FRAMEWORK.md §2.3/§2.9: a content pane re-measured under a narrower grant reports
    // the width of its longest wrapped row at that width, not the grant itself — freed columns
    // must reach the sibling rather than sit unused inside the anchor. Reproduces RowLayout.Wrap's
    // exact row-break decision (rowWidth + SeparatorWidth + segWidth <= cap) rather than calling
    // it directly, because this only needs each row's width, not its rendered markup.
    private static int LongestWrappedRowWidth(IReadOnlyList<Segment> segments, int cap)
    {
        if (segments.Count == 0)
        {
            return 0;
        }

        var maxWidth = 0;
        var rowWidth = 0;
        var rowStarted = false;

        foreach (var seg in segments)
        {
            var segWidth = seg.Plain.Length;

            if (!rowStarted)
            {
                rowWidth = segWidth;
                rowStarted = true;
            }
            else if (rowWidth + RowLayout.SeparatorWidth + segWidth <= cap)
            {
                rowWidth += RowLayout.SeparatorWidth + segWidth;
            }
            else
            {
                maxWidth = Math.Max(maxWidth, rowWidth);
                rowWidth = segWidth;
            }
        }

        return Math.Max(maxWidth, rowWidth);
    }
}
