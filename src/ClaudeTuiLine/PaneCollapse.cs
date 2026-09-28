namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.4/§2.11/§2.11.1/§2.11.2: the collapse pre-pass, run once per render
/// inside the split-tree pipeline, after §5's value resolution and before §2.3's sizing. A
/// <c>content</c>/<c>fill</c> leaf pane with no declared <see cref="Pane.MinSize"/> collapses —
/// occupies no width, draws no border, and its parent drops the corresponding gutter — when every
/// one of its items is structurally empty: <see cref="Pane.Items"/> is empty, or every placed item
/// resolved to no value. An explicit <see cref="Pane.MinSize"/> always suppresses collapse
/// (§2.11.1 — the author named a floor, the same as <c>fixed</c>/<c>percent</c> naming an extent),
/// and so does <c>fixed</c>/<c>percent</c> sizing itself (§2.4 — the author named a number). An
/// item whose value is null because it did not answer in time (§7), rather than because it
/// legitimately has nothing to say, also suppresses collapse for the pane holding it (§2.11.2) —
/// otherwise a flaky command's pane would flicker in and out of existence as its timeout drifts. A
/// split whose every child collapses collapses itself, resolved bottom-up in one pass since
/// emptiness here only ever propagates upward, never down. Collapsing the root returns null; the
/// caller must treat that the same as an empty-segments surface — zero rows, not an empty box.
/// </summary>
public static class PaneCollapse
{
    public static Pane? Collapse(
        Pane pane,
        IReadOnlyDictionary<string, string?> values,
        ItemContext ctx,
        IReadOnlyDictionary<string, Segment> compounds,
        IReadOnlyCollection<string> unavailableIds)
    {
        if (pane.Split != PaneSplit.None && pane.Children.Count > 0)
        {
            var kept = new List<Pane>(pane.Children.Count);
            var changed = false;
            foreach (var child in pane.Children)
            {
                var collapsedChild = Collapse(child, values, ctx, compounds, unavailableIds);
                if (collapsedChild is null)
                {
                    changed = true;
                    continue;
                }

                if (!ReferenceEquals(collapsedChild, child))
                {
                    changed = true;
                }

                kept.Add(collapsedChild);
            }

            if (kept.Count == 0)
            {
                return null;
            }

            return changed ? pane with { Children = kept } : pane;
        }

        if (pane.MinSize is not null || !(SizeResolver.IsContentSized(pane) || SizeResolver.IsFillSized(pane)))
        {
            return pane;
        }

        return IsStructurallyEmpty(pane, values, ctx, compounds, unavailableIds) ? null : pane;
    }

    private static bool IsStructurallyEmpty(
        Pane pane,
        IReadOnlyDictionary<string, string?> values,
        ItemContext ctx,
        IReadOnlyDictionary<string, Segment> compounds,
        IReadOnlyCollection<string> unavailableIds)
    {
        if (pane.Items.Count == 0)
        {
            return true;
        }

        foreach (var resolved in LeafItems.Resolve(pane.Items, values, ctx, compounds))
        {
            if (resolved.Value is not null)
            {
                return false;
            }

            if (resolved.Config.Id is { Length: > 0 } id && unavailableIds.Contains(id))
            {
                return false;
            }
        }

        return true;
    }
}
