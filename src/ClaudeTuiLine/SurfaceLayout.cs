namespace ClaudeTuiLine;

/// <summary>
/// The single point where the terminal's chrome reserve is applied to <c>COLUMNS</c>. Computes
/// the width of the pane that fills the statusline surface. Everything below this — RowLayout,
/// border/padding arithmetic, Panel/Profile.Width — takes that width as a plain parameter and
/// has no knowledge of COLUMNS or chromeReserve. See SPEC.md §6 "MEASURED: the usable statusline
/// width is COLUMNS - 3, not COLUMNS - 1".
/// </summary>
public static class SurfaceLayout
{
    public static int? ComputeWidth(string? columnsEnv, int chromeReserve)
    {
        if (string.IsNullOrEmpty(columnsEnv) || !int.TryParse(columnsEnv, out var columns))
        {
            return null;
        }

        return Math.Max(0, columns - chromeReserve);
    }
}
