using ClaudeTuiLine;

namespace ClaudeTuiLine.Tests;

public class SurfaceLayoutTests
{
    [Fact]
    public void NullColumnsEnv_ReturnsNull()
    {
        Assert.Null(SurfaceLayout.ComputeWidth(null, chromeReserve: 3));
    }

    [Fact]
    public void UnparsableColumnsEnv_ReturnsNull()
    {
        Assert.Null(SurfaceLayout.ComputeWidth("not-a-number", chromeReserve: 3));
    }

    [Fact]
    public void EmptyColumnsEnv_ReturnsNull()
    {
        Assert.Null(SurfaceLayout.ComputeWidth("", chromeReserve: 3));
    }

    [Fact]
    public void SubtractsChromeReserveExactlyOnce()
    {
        Assert.Equal(97, SurfaceLayout.ComputeWidth("100", chromeReserve: 3));
        Assert.Equal(99, SurfaceLayout.ComputeWidth("100", chromeReserve: 1));
    }

    [Fact]
    public void ChromeReserveExceedsColumns_ClampsToZero()
    {
        Assert.Equal(0, SurfaceLayout.ComputeWidth("2", chromeReserve: 3));
    }

    [Fact]
    public void MeasuredCase_Columns112_MatchesTheLiveSessionResult()
    {
        // SPEC.md §6 "MEASURED": in a live Claude Code session with COLUMNS=112, widths 112, 111
        // and 110 were truncated; 109 was the widest to survive intact.
        Assert.Equal(109, SurfaceLayout.ComputeWidth("112", chromeReserve: 3));
    }
}
