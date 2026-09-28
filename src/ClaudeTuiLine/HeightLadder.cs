namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.8.1: enforces <c>surface.maxRows</c> and every pane's own
/// <c>maxRows</c> via the deterministic degrade ladder — there is no height fixpoint (unlike
/// width, wrapping can trade width for height and a vertical split's height is
/// <c>max(height(children))</c>, so naive iteration is not guaranteed to converge). The ladder
/// instead applies four strictly row-reducing rungs, in order, re-measuring after each edit and
/// stopping the instant the budget is met: (1) measure — stop if already in budget; (2) demote
/// <c>wrap</c> to <c>truncate</c>, one pane at a time, in reverse tree-declaration order; (3) drop
/// trailing items from the tallest pane, one at a time — a pane emptied this way renders zero
/// content rows, never the "declared nothing" default-segments fallback; (4) clip the tallest
/// pane's row footprint, one row at a time, closing a bordered pane's box on its last surviving row
/// and suppressing the border entirely once its budget falls under 3 (§2.8.2). "Tallest pane" ties
/// break by reverse declaration order at every rung, mirroring rung 2's own iteration order.
/// "Declaration order" is surface-wide reverse pre-order DFS over the whole pane tree, distinct
/// from §2.3 step 4's sibling-scoped use of the same term.
///
/// §2.5.1/§2.8.1 addendum: per-attempt state (a leaf's clip budget, whether rung 3 emptied it) is
/// surface-derived and never belongs on the shared, per-config <see cref="Pane"/> record — it is
/// tracked here as path-keyed side tables and annotated onto a freshly resolved
/// <see cref="SizeResolver.ResolvedPane"/> tree before every measurement and before the final
/// render. Paths (child-index sequences from the root), not <see cref="Pane"/> references, key
/// these tables because rungs 2/3 rebuild ancestors (and the target itself) via <c>with</c>-
/// expressions, so a pane's own reference does not survive across the ladder's rebuilds — its
/// structural position does.
/// </summary>
public static class HeightLadder
{
    public static (SizeResolver.ResolvedPane Resolved, Compositor.PaneContribution Contribution) Resolve(
        Pane root,
        int outerWidth,
        int surfaceMaxRows,
        ItemContext ctx,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyDictionary<string, ColorResolution.ColorRule> tokens,
        IReadOnlyDictionary<string, Segment> compounds,
        RenderNoteCollector notes,
        bool collapse = false)
    {
        var current = root;
        var clipBudgets = new Dictionary<string, int>();
        var emptiedPaths = new HashSet<string>();
        var (rootRows, rowCounts, _, _) = Measure(current, outerWidth, ctx, values, tokens, compounds, clipBudgets, emptiedPaths, collapse);

        if (!InBudget(current, rootRows, rowCounts, surfaceMaxRows))
        {
            (current, rootRows, rowCounts) = DemoteWrapToTruncate(current, rootRows, rowCounts, outerWidth, surfaceMaxRows, ctx, values, tokens, compounds, clipBudgets, emptiedPaths, collapse);
        }

        if (!InBudget(current, rootRows, rowCounts, surfaceMaxRows))
        {
            (current, rootRows, rowCounts) = DropTrailingItems(current, rootRows, rowCounts, outerWidth, surfaceMaxRows, ctx, values, tokens, compounds, clipBudgets, emptiedPaths, collapse);
        }

        if (!InBudget(current, rootRows, rowCounts, surfaceMaxRows))
        {
            (current, rootRows, rowCounts) = ClipTallest(current, rootRows, rowCounts, outerWidth, surfaceMaxRows, ctx, values, tokens, compounds, clipBudgets, emptiedPaths, collapse);
        }

        // Final render against the caller's real notes collector — every rung above measures
        // through a throwaway collector so a rejected/superseded attempt's notes never reach the
        // caller.
        var resolved = Annotate(SizeResolver.Resolve(current, outerWidth, ctx, values, compounds, notes, collapse), Array.Empty<int>(), clipBudgets, emptiedPaths);
        var grid = collapse ? BorderGrid.Build(resolved, rowCounts, values, tokens) : null;
        var contribution = PaneTreeRenderer.Render(resolved, ctx, values, tokens, compounds, notes, collapse: collapse, grid: grid);
        return (resolved, contribution);
    }

    // §2.8.1 rung 2: iterates the (structurally stable) reverse-declaration-order pane list once,
    // demoting each explicitly-"wrap" pane to "truncate" and re-measuring, stopping as soon as the
    // budget is met. Panes without an explicit "wrap" (null, already "truncate", or the split-
    // coerced "overflow") are left untouched — PaneAssembler.ResolveOverflow already handles those.
    private static (Pane Root, int RootRows, IReadOnlyDictionary<Pane, int> RowCounts) DemoteWrapToTruncate(
        Pane root, int rootRows, IReadOnlyDictionary<Pane, int> rowCounts, int outerWidth, int surfaceMaxRows,
        ItemContext ctx, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, ColorResolution.ColorRule> tokens,
        IReadOnlyDictionary<string, Segment> compounds, Dictionary<string, int> clipBudgets, HashSet<string> emptiedPaths, bool collapse)
    {
        var flat = Flatten(root);
        for (var i = flat.Count - 1; i >= 0; i--)
        {
            var path = flat[i].Path;
            var pane = GetAt(root, path);
            if (pane.Overflow != OverflowMode.Wrap)
            {
                continue;
            }

            root = ReplaceAt(root, path, 0, p => p with { Overflow = OverflowMode.Truncate });
            (rootRows, rowCounts, _, _) = Measure(root, outerWidth, ctx, values, tokens, compounds, clipBudgets, emptiedPaths, collapse);
            if (InBudget(root, rootRows, rowCounts, surfaceMaxRows))
            {
                break;
            }
        }

        return (root, rootRows, rowCounts);
    }

    // §2.8.1 rung 3: repeatedly drops the last item from the tallest eligible leaf (a leaf with at
    // least one item left to drop), re-measuring after each drop, until in budget or no eligible
    // leaf remains. A drop that empties a leaf's item list records that leaf's path in
    // emptiedPaths, so the very next Measure (and every one after) renders it as zero content rows
    // instead of falling through to the default-segments fallback — the row-reducing invariant a
    // drop must have holds because of that, not despite it.
    private static (Pane Root, int RootRows, IReadOnlyDictionary<Pane, int> RowCounts) DropTrailingItems(
        Pane root, int rootRows, IReadOnlyDictionary<Pane, int> rowCounts, int outerWidth, int surfaceMaxRows,
        ItemContext ctx, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, ColorResolution.ColorRule> tokens,
        IReadOnlyDictionary<string, Segment> compounds, Dictionary<string, int> clipBudgets, HashSet<string> emptiedPaths, bool collapse)
    {
        while (!InBudget(root, rootRows, rowCounts, surfaceMaxRows))
        {
            var target = TallestEligible(root, rowCounts, t => t.Pane.Children.Count == 0 && t.Pane.Items.Count > 0);
            if (target is null)
            {
                break; // rung 3 exhausted: no leaf has a trailing item left to drop.
            }

            var (pane, path) = target.Value;
            var newItems = pane.Items.Take(pane.Items.Count - 1).ToList();
            if (newItems.Count == 0)
            {
                emptiedPaths.Add(PathKey(path));
            }

            root = ReplaceAt(root, path, 0, p => p with { Items = newItems });
            (rootRows, rowCounts, _, _) = Measure(root, outerWidth, ctx, values, tokens, compounds, clipBudgets, emptiedPaths, collapse);
        }

        return (root, rootRows, rowCounts);
    }

    // §2.8.1 rung 4 / §2.8.2: repeatedly clips one more row off the tallest eligible leaf's total
    // row footprint (content + border), re-measuring after each clip, until in budget or every
    // leaf has hit its 0-row floor. clipBudgets carries each clipped leaf's own final row
    // footprint, keyed by path — PaneTreeRenderer/PaneAssembler read it (via the annotated
    // ResolvedPane tree) to cap content and (below 3 rows) suppress the border. The Pane tree
    // itself is never rebuilt here: unlike rungs 2/3, a clip budget is render-attempt state, not a
    // change to what the pane declares.
    private static (Pane Root, int RootRows, IReadOnlyDictionary<Pane, int> RowCounts) ClipTallest(
        Pane root, int rootRows, IReadOnlyDictionary<Pane, int> rowCounts, int outerWidth, int surfaceMaxRows,
        ItemContext ctx, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, ColorResolution.ColorRule> tokens,
        IReadOnlyDictionary<string, Segment> compounds, Dictionary<string, int> clipBudgets, HashSet<string> emptiedPaths, bool collapse)
    {
        while (!InBudget(root, rootRows, rowCounts, surfaceMaxRows))
        {
            var target = TallestEligible(root, rowCounts, t => t.Pane.Children.Count == 0 && ClipBudgetRemaining(clipBudgets, t.Path) > 0);
            if (target is null)
            {
                break; // rung 4 exhausted: every leaf is already at a 0-row floor.
            }

            var (pane, path) = target.Value;
            var key = PathKey(path);
            var floor = clipBudgets.TryGetValue(key, out var budget) ? budget : (rowCounts.TryGetValue(pane, out var count) ? count : 0);
            clipBudgets[key] = Math.Max(0, floor - 1);
            (rootRows, rowCounts, _, _) = Measure(root, outerWidth, ctx, values, tokens, compounds, clipBudgets, emptiedPaths, collapse);
        }

        return (root, rootRows, rowCounts);
    }

    private static int ClipBudgetRemaining(IReadOnlyDictionary<string, int> clipBudgets, int[] path) =>
        clipBudgets.TryGetValue(PathKey(path), out var budget) ? budget : int.MaxValue;

    // The shared "tallest pane, ties break by reverse declaration order" target selection rungs
    // 3 and 4 both use: among panes matching eligible, picks the one with the greatest measured
    // row count, and among ties the one that appears LAST in tree declaration (pre-order) —
    // i.e. the later-declared pane degrades first, the same tie-break rung 2 iterates by.
    private static (Pane Pane, int[] Path)? TallestEligible(
        Pane root, IReadOnlyDictionary<Pane, int> rowCounts, Func<(Pane Pane, int[] Path), bool> eligible)
    {
        var candidates = Flatten(root)
            .Select((entry, index) => (entry.Pane, entry.Path, Index: index))
            .Where(t => eligible((t.Pane, t.Path)))
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var best = candidates
            .OrderByDescending(t => rowCounts.TryGetValue(t.Pane, out var count) ? count : 0)
            .ThenByDescending(t => t.Index)
            .First();

        return (best.Pane, best.Path);
    }

    private static (int RootRows, IReadOnlyDictionary<Pane, int> RowCounts, SizeResolver.ResolvedPane Resolved, Compositor.PaneContribution Contribution) Measure(
        Pane root, int outerWidth, ItemContext ctx, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, ColorResolution.ColorRule> tokens,
        IReadOnlyDictionary<string, Segment> compounds, IReadOnlyDictionary<string, int> clipBudgets, IReadOnlySet<string> emptiedPaths, bool collapse)
    {
        var scratchNotes = new RenderNoteCollector();
        var resolved = Annotate(SizeResolver.Resolve(root, outerWidth, ctx, values, compounds, scratchNotes, collapse), Array.Empty<int>(), clipBudgets, emptiedPaths);
        var rowCounts = new Dictionary<Pane, int>(ReferenceEqualityComparer.Instance);
        var contribution = PaneTreeRenderer.Render(resolved, ctx, values, tokens, compounds, scratchNotes, rowCounts: rowCounts);
        return (contribution.Buffer.Rows.Count, rowCounts, resolved, contribution);
    }

    // Overlays this attempt's clipBudgets/emptiedPaths onto a freshly resolved (annotation-free)
    // ResolvedPane tree, by path. SizeResolver.Resolve stays a pure function of (Pane, width) —
    // this is the one place the ladder's own per-attempt search state joins that tree.
    private static SizeResolver.ResolvedPane Annotate(
        SizeResolver.ResolvedPane node, int[] path, IReadOnlyDictionary<string, int> clipBudgets, IReadOnlySet<string> emptiedPaths)
    {
        var key = PathKey(path);
        var children = node.Children.Count == 0
            ? node.Children
            : node.Children.Select((c, i) => Annotate(c, path.Append(i).ToArray(), clipBudgets, emptiedPaths)).ToList();

        return node with
        {
            Children = children,
            ClipRows = clipBudgets.TryGetValue(key, out var budget) ? budget : null,
            ItemsEmptied = emptiedPaths.Contains(key),
        };
    }

    private static string PathKey(int[] path) => string.Join('.', path);

    // §2.6/§2.8.1: the whole surface never exceeds surface.maxRows, and any pane that declared
    // its own maxRows never exceeds that either — the ladder's only stopping condition, checked
    // against BOTH budgets together.
    private static bool InBudget(Pane root, int rootRows, IReadOnlyDictionary<Pane, int> rowCounts, int surfaceMaxRows)
    {
        if (rootRows > surfaceMaxRows)
        {
            return false;
        }

        foreach (var (pane, _) in Flatten(root))
        {
            if (pane.MaxRows is int max && rowCounts.TryGetValue(pane, out var count) && count > max)
            {
                return false;
            }
        }

        return true;
    }

    // Pre-order DFS flatten — SPEC-V2-FRAMEWORK.md's own "tree declaration order" term (the same
    // ordering §2.3 step 4 uses at sibling scope, extended here to the whole tree). Path is the
    // child-index sequence from the root, used (rather than Pane reference identity) to relocate a
    // node after a functional tree rebuild changes every ancestor's reference.
    private static List<(Pane Pane, int[] Path)> Flatten(Pane root)
    {
        var result = new List<(Pane Pane, int[] Path)>();

        void Walk(Pane node, int[] path)
        {
            result.Add((node, path));
            for (var i = 0; i < node.Children.Count; i++)
            {
                Walk(node.Children[i], path.Append(i).ToArray());
            }
        }

        Walk(root, Array.Empty<int>());
        return result;
    }

    private static Pane GetAt(Pane root, int[] path)
    {
        var node = root;
        foreach (var index in path)
        {
            node = node.Children[index];
        }

        return node;
    }

    // Rebuilds root with the pane at path replaced by transform(pane at path) — every ancestor
    // along the path gets a new record (Pane is immutable), every sibling subtree keeps its
    // original reference untouched.
    private static Pane ReplaceAt(Pane root, int[] path, int depth, Func<Pane, Pane> transform)
    {
        if (depth == path.Length)
        {
            return transform(root);
        }

        var childIndex = path[depth];
        var newChildren = root.Children.ToList();
        newChildren[childIndex] = ReplaceAt(root.Children[childIndex], path, depth + 1, transform);
        return root with { Children = newChildren };
    }
}
