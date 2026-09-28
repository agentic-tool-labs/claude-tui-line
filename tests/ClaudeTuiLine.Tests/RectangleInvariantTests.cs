using ClaudeTuiLine;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §10.3: for any pane tree, every composed ROOT row has the same
/// (ANSI-stripped) width — the surface renders as an actual rectangle. This is rules 1 and 2 of
/// §2.4 (pad every row to its pane's width; pad siblings to a common height with full-width blank
/// rows), independent of rule 4's trailing-whitespace trim. Trim is a deliberate, spec-sanctioned
/// exception for the no-background case — it is what preserves Phase 1 byte-parity for the single,
/// backgroundless root pane (see GoldenParityTests) — not a violation of the rectangle shape, so
/// these tests use HasBackground: true throughout to isolate the padding rules from the trim rule.
/// </summary>
public class RectangleInvariantTests
{
    private static int MeasuredWidth(string composedRow) => DisplayWidth.Measure(composedRow);

    private static int MeasuredWidth(PaneRow composedRow) => MeasuredWidth(composedRow.Markup);

    [Fact]
    public void SinglePane_RaggedNaturalRowWidths_AllComposedRowsEqualPaneWidth()
    {
        var buffer = new PaneBuffer(new List<PaneRow>
        {
            new("a", 1),
            new("abcdefgh", 8),
            new("abc", 3),
        });
        var contribution = new Compositor.PaneContribution(buffer, Width: 12, HasBackground: true);

        var composed = Compositor.ComposeRoot(new[] { contribution });

        Assert.All(composed, row =>
        {
            Assert.Equal(12, row.Width);
            Assert.Equal(row.Width, MeasuredWidth(row));
        });
    }

    [Fact]
    public void TwoSiblings_DifferentHeightsAndRowWidths_ComposedRootIsARectangle()
    {
        var left = new Compositor.PaneContribution(
            new PaneBuffer(new List<PaneRow> { new("x", 1), new("yy", 2), new("zzz", 3) }),
            Width: 6, HasBackground: true);
        var right = new Compositor.PaneContribution(
            new PaneBuffer(new List<PaneRow> { new("q", 1) }),
            Width: 4, HasBackground: true);

        var composed = Compositor.ComposeRoot(new[] { left, right });

        Assert.Equal(3, composed.Count); // padded to the taller sibling's height
        Assert.All(composed, row =>
        {
            Assert.Equal(10, row.Width); // 6 + 4
            Assert.Equal(row.Width, MeasuredWidth(row));
        });
    }

    [Fact]
    public void DeliberatelyBrokenCompositor_SkipsRowPadding_FailsTheInvariant()
    {
        // A deliberately wrong compositor: joins raw rows without rule 1's per-row width padding.
        // Proves the invariant assertion above is capable of catching a real defect, not just
        // trivially true for any implementation.
        var buffer = new PaneBuffer(new List<PaneRow> { new("a", 1), new("abcdefgh", 8), new("abc", 3) });

        static IReadOnlyList<string> BrokenCompose(PaneBuffer b) => b.Rows.Select(r => r.Markup).ToList();

        var brokenOutput = BrokenCompose(buffer);
        var widths = brokenOutput.Select(MeasuredWidth).Distinct().ToList();

        Assert.True(widths.Count > 1, "expected the broken compositor to produce ragged row widths");
    }

    [Fact]
    public void DeliberatelyBrokenCompositor_SkipsHeightPadding_FailsTheInvariant()
    {
        // A deliberately wrong compositor: joins siblings row-by-row without rule 2's
        // common-height padding, so a shorter sibling simply contributes nothing past its own
        // last row instead of a full-width blank.
        var left = new PaneBuffer(new List<PaneRow> { new("x", 1), new("yy", 2), new("zzz", 3) });
        var right = new PaneBuffer(new List<PaneRow> { new("q", 1) });

        static IReadOnlyList<string> BrokenCompose(PaneBuffer l, PaneBuffer r)
        {
            var height = Math.Max(l.Rows.Count, r.Rows.Count);
            var rows = new List<string>();
            for (var i = 0; i < height; i++)
            {
                var leftText = i < l.Rows.Count ? l.Rows[i].Markup : string.Empty;
                var rightText = i < r.Rows.Count ? r.Rows[i].Markup : string.Empty; // no blank-row padding
                rows.Add(leftText + rightText);
            }
            return rows;
        }

        var brokenOutput = BrokenCompose(left, right);
        var widths = brokenOutput.Select(MeasuredWidth).Distinct().ToList();

        Assert.True(widths.Count > 1, "expected the broken compositor to produce ragged row widths when a shorter sibling isn't height-padded");
    }
}
