using Spectre.Console;
using Spectre.Console.Rendering;

namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §2.10.2: builds the single compositor-owned border grid under
/// collapse:true. The grid is a per-cell 4-bit NESW mask — not per-pane, per-edge, or per-column-
/// run — because a shared column's glyph can depend on up to two panes' edges plus whatever
/// crosses it. Named to keep bare `Collapse` reserved for <see cref="PaneCollapse"/> (§2.4/§2.11
/// pane pruning, an unrelated pre-pass).
/// </summary>
public static class BorderGrid
{
    public const int N = 0b1000;
    public const int E = 0b0100;
    public const int S = 0b0010;
    public const int W = 0b0001;

    public readonly record struct Cell(int Mask, string ColorMarkup, BoxBorder Style);

    /// <summary>Sparse per-cell mask/colour/style grid, keyed by absolute (row, column).</summary>
    public sealed class Grid
    {
        private readonly Dictionary<(int Row, int Col), Cell> _cells = new();

        // §2.10.1 rule 4 / §2.10.2 §4: the first contribution to land on a cell fixes its colour
        // and style; later contributions still OR their bits into the mask. Callers must therefore
        // visit contributors in tree declaration order for the tie-break to mean anything.
        internal void Or(int row, int col, int bits, string colorMarkup, BoxBorder style)
        {
            if (bits == 0)
            {
                return;
            }

            if (_cells.TryGetValue((row, col), out var existing))
            {
                _cells[(row, col)] = existing with { Mask = existing.Mask | bits };
            }
            else
            {
                _cells[(row, col)] = new Cell(bits, colorMarkup, style);
            }
        }

        public bool TryGet(int row, int col, out Cell cell) => _cells.TryGetValue((row, col), out cell);
    }

    private sealed record Box(int Row0, int Col0, int Row1, int Col1, PaneBorderEdges Edges, string ColorMarkup, BoxBorder Style);

    /// <summary>
    /// SPEC-V2-FRAMEWORK.md §2.10.2 point 2: walks the resolved pane tree once, in tree
    /// declaration order, computing each bordered pane's absolute box rectangle and OR-ing its
    /// edge runs into the grid. Vertical-split interior boundaries (§2.10.2 point 3) are resolved
    /// as a convex hull of their contributors rather than direct per-box accumulation, which is
    /// what fills a gap between two differently-sized `height: "content"` neighbours without
    /// extending past either end.
    /// </summary>
    public static Grid Build(
        SizeResolver.ResolvedPane root,
        IReadOnlyDictionary<Pane, int> rowCounts,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyDictionary<string, ColorResolution.ColorRule> tokens)
    {
        var grid = new Grid();
        var rootHeight = rowCounts.TryGetValue(root.Source, out var rh) ? rh : 0;
        Walk(root, 0, 0, rootHeight, excludeLeft: false, excludeRight: false, rowCounts, values, tokens, grid);
        return grid;
    }

    private static string ResolveColor(Pane pane, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, ColorResolution.ColorRule> tokens) =>
        ColorResolution.Resolve(pane.Border.Color, values, tokens) ?? "grey";

    private static void Walk(
        SizeResolver.ResolvedPane node, int rowStart, int colStart, int bandHeight,
        bool excludeLeft, bool excludeRight,
        IReadOnlyDictionary<Pane, int> rowCounts,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyDictionary<string, ColorResolution.ColorRule> tokens,
        Grid grid)
    {
        var pane = node.Source;
        var hasBorder = pane.Border.Style is not null;
        var edges = pane.Border.Edges;
        var row0 = rowStart;
        var row1 = rowStart + Math.Max(1, bandHeight) - 1;

        if (hasBorder)
        {
            // The box's own top/bottom runs still span into a column excluded from ITS OWN
            // reserve when that edge is declared — the column belongs to a shared boundary, but
            // the corner/junction glyph a horizontal run produces there is this box's
            // contribution to that boundary's cell, computed here and consumed later by whatever
            // owns that column (§2.10.2 point 2's corner derivation: "a box's top-left cell
            // receives S from its left edge's run... and E from its top edge's run").
            var col0 = colStart - (excludeLeft && edges.Left ? 1 : 0);
            var col1 = colStart + node.OuterWidth - 1 + (excludeRight && edges.Right ? 1 : 0);
            var box = new Box(row0, col0, row1, col1, edges, ResolveColor(pane, values, tokens), pane.Border.Style!);

            if (edges.Top)
            {
                AddHorizontalRun(grid, box.Row0, box.Col0, box.Col1, box.ColorMarkup, box.Style);
            }

            if (edges.Bottom)
            {
                AddHorizontalRun(grid, box.Row1, box.Col0, box.Col1, box.ColorMarkup, box.Style);
            }

            // Non-shared verticals accumulate directly. Shared (excluded) verticals are handled
            // exclusively by the boundary hull step below/in the vertical-split loop, never here —
            // accumulating both would double-draw and, for content-height neighbours, would skip
            // the gap-filling hull entirely.
            if (edges.Left && !excludeLeft)
            {
                AddVerticalRun(grid, box.Col0, box.Row0, box.Row1, box.ColorMarkup, box.Style);
            }

            if (edges.Right && !excludeRight)
            {
                AddVerticalRun(grid, box.Col1, box.Row0, box.Row1, box.ColorMarkup, box.Style);
            }
        }

        if (node.Children.Count == 0)
        {
            return;
        }

        var innerRow0 = row0 + (hasBorder && edges.Top ? 1 : 0);
        var innerBand = Math.Max(0, bandHeight - (hasBorder ? SizeResolver.OwnRowReserve(pane) : 0));

        if (node.EffectiveSplit == PaneSplit.Vertical)
        {
            var cursorCol = colStart + (hasBorder && edges.Left && !excludeLeft ? 1 : 0);

            // Every vertical edge in this split — the first child's own left, every interior
            // boundary between siblings, and the last child's own right — is resolved uniformly
            // as a boundary with 0-2 contributors, per §2.10.2 point 3's convex-hull rule. A
            // boundary with exactly one contributor degenerates to that contributor's own span,
            // which is exactly a plain uncontested edge.
            (Pane Pane, PaneBorderEdges Edges, bool IsRight, int Row0, int Row1, string ColorMarkup, BoxBorder Style)? prev = null;

            for (var i = 0; i < node.Children.Count; i++)
            {
                var child = node.Children[i];
                var childExcludeLeft = i > 0;
                var childExcludeRight = i < node.Children.Count - 1;
                var childIsContentHeight = child.Source.Height == PaneHeight.Content;
                var childNaturalRows = rowCounts.TryGetValue(child.Source, out var cr) ? cr : innerBand;

                int childRowStart;
                int childRows;
                if (childIsContentHeight && childNaturalRows < innerBand)
                {
                    var deficit = innerBand - childNaturalRows;
                    var before = child.Source.Valign switch
                    {
                        PaneValign.Middle => deficit / 2,
                        PaneValign.Bottom => deficit,
                        _ => 0,
                    };
                    childRowStart = innerRow0 + before;
                    childRows = childNaturalRows;
                }
                else
                {
                    childRowStart = innerRow0;
                    childRows = innerBand;
                }

                var boundaryCol = cursorCol - (childExcludeLeft && child.Source.Border.Style is not null && child.Source.Border.Edges.Left ? 1 : 0);
                var childEdges = child.Source.Border.Edges;
                var childHasBorder = child.Source.Border.Style is not null;
                var childRow0 = childRowStart;
                var childRow1 = childRowStart + Math.Max(1, childRows) - 1;
                var childColor = childHasBorder ? ResolveColor(child.Source, values, tokens) : "";
                var childStyle = childHasBorder ? child.Source.Border.Style! : BoxBorder.Square;

                var leftContributor = childHasBorder && childEdges.Left
                    ? (child.Source, childEdges, false, childRow0, childRow1, childColor, childStyle)
                    : ((Pane, PaneBorderEdges, bool, int, int, string, BoxBorder)?)null;

                ResolveBoundary(grid, boundaryCol, prev, leftContributor);

                Walk(child, childRowStart, cursorCol, childRows, childExcludeLeft, childExcludeRight, rowCounts, values, tokens, grid);

                prev = childHasBorder && childEdges.Right
                    ? (child.Source, childEdges, IsRight: true, childRow0, childRow1, childColor, childStyle)
                    : null;

                cursorCol += child.OuterWidth + 1;
            }

            // The last child's own right edge, uncontested (a one-sided "boundary").
            ResolveBoundary(grid, cursorCol - 1, prev, null);
        }
        else
        {
            var innerCol0 = colStart + (hasBorder && edges.Left && !excludeLeft ? 1 : 0);
            var cursorRow = innerRow0;
            foreach (var child in node.Children)
            {
                var childRows = rowCounts.TryGetValue(child.Source, out var cr) ? cr : 0;
                Walk(child, cursorRow, innerCol0, childRows, excludeLeft: false, excludeRight: false, rowCounts, values, tokens, grid);
                cursorRow += childRows;
            }
        }
    }

    // §2.10.2 point 3: the run in a shared column is the convex hull of its contributors' row
    // extents, coloured/styled once from the first requester in tree order — never per cell. With
    // zero contributors nothing is drawn; with one, the hull degenerates to that contributor's own
    // span (an ordinary uncontested edge drawn via the boundary mechanism instead of directly, so
    // outermost edges and shared ones share one code path).
    private static void ResolveBoundary(
        Grid grid, int col,
        (Pane Pane, PaneBorderEdges Edges, bool IsRight, int Row0, int Row1, string ColorMarkup, BoxBorder Style)? a,
        (Pane Pane, PaneBorderEdges Edges, bool IsRight, int Row0, int Row1, string ColorMarkup, BoxBorder Style)? b)
    {
        if (a is null && b is null)
        {
            return;
        }

        // `a` (the earlier sibling's right edge, if any) is always earlier in tree declaration
        // order than `b` (the later sibling's left edge), so `a` wins ties per §2.10.1 rule 4.
        var winner = a ?? b!.Value;
        var row0 = a is null ? b!.Value.Row0 : b is null ? a.Value.Row0 : Math.Min(a.Value.Row0, b.Value.Row0);
        var row1 = a is null ? b!.Value.Row1 : b is null ? a.Value.Row1 : Math.Max(a.Value.Row1, b.Value.Row1);

        AddVerticalRun(grid, col, row0, row1, winner.ColorMarkup, winner.Style);
    }

    private static void AddHorizontalRun(Grid grid, int row, int col0, int col1, string colorMarkup, BoxBorder style)
    {
        for (var c = col0; c <= col1; c++)
        {
            var bits = (c < col1 ? E : 0) | (c > col0 ? W : 0);
            grid.Or(row, c, bits, colorMarkup, style);
        }
    }

    private static void AddVerticalRun(Grid grid, int col, int row0, int row1, string colorMarkup, BoxBorder style)
    {
        for (var r = row0; r <= row1; r++)
        {
            var bits = (r < row1 ? S : 0) | (r > row0 ? N : 0);
            grid.Or(r, col, bits, colorMarkup, style);
        }
    }

    // §2.10: "Implement it as one 16-entry table per style, keyed by the NESW neighbour mask."
    // Spectre's BoxBorderPart has only the eight corner/edge members (§2.10.2 point 5) — corners,
    // plain horizontal/vertical runs, and single-arm stubs are derived from it; the five tee/cross
    // glyphs have no BoxBorderPart source and are hand-authored per style below.
    public static string Glyph(BoxBorder style, int mask)
    {
        if (mask == 0)
        {
            return " ";
        }

        var n = (mask & N) != 0;
        var e = (mask & E) != 0;
        var s = (mask & S) != 0;
        var w = (mask & W) != 0;

        // Corners: exactly the two adjacent arms that meet at that box corner.
        if (s && e && !n && !w) return style.GetPart(BoxBorderPart.TopLeft);
        if (s && w && !n && !e) return style.GetPart(BoxBorderPart.TopRight);
        if (n && e && !s && !w) return style.GetPart(BoxBorderPart.BottomLeft);
        if (n && w && !s && !e) return style.GetPart(BoxBorderPart.BottomRight);

        // Cross and tees: not representable via BoxBorderPart, hand-authored per style.
        if (n && e && s && w) return Tee(style, Junction.Cross);
        if (n && e && s && !w) return Tee(style, Junction.TeeRight);   // ├
        if (n && w && s && !e) return Tee(style, Junction.TeeLeft);    // ┤
        if (e && w && s && !n) return Tee(style, Junction.TeeDown);    // ┬
        if (e && w && n && !s) return Tee(style, Junction.TeeUp);      // ┴

        // Plain runs and single-arm stubs: both ends of an axis render as that axis's line.
        if (n || s) return style.GetPart(BoxBorderPart.Left);
        return style.GetPart(BoxBorderPart.Top);
    }

    private enum Junction { TeeRight, TeeLeft, TeeDown, TeeUp, Cross }

    private static string Tee(BoxBorder style, Junction junction)
    {
        // Reference equality against Spectre's built-in statics — the same five styles
        // Config.cs:862-869 registers (`rounded`, `square`, `heavy`, `double`, `ascii`). Rounded
        // has no rounded tee/cross in Unicode, so it shares square's set; only corners differ,
        // and those come from BoxBorder.GetPart above, never from this table.
        if (ReferenceEquals(style, BoxBorder.Heavy))
        {
            return junction switch
            {
                Junction.TeeRight => "┣",
                Junction.TeeLeft => "┫",
                Junction.TeeDown => "┳",
                Junction.TeeUp => "┻",
                _ => "╋",
            };
        }

        if (ReferenceEquals(style, BoxBorder.Double))
        {
            return junction switch
            {
                Junction.TeeRight => "╠",
                Junction.TeeLeft => "╣",
                Junction.TeeDown => "╦",
                Junction.TeeUp => "╩",
                _ => "╬",
            };
        }

        if (ReferenceEquals(style, BoxBorder.Ascii))
        {
            return "+";
        }

        // Rounded and Square (and any other/unrecognised style) share the light-line tee/cross set.
        return junction switch
        {
            Junction.TeeRight => "├",
            Junction.TeeLeft => "┤",
            Junction.TeeDown => "┬",
            Junction.TeeUp => "┴",
            _ => "┼",
        };
    }
}
