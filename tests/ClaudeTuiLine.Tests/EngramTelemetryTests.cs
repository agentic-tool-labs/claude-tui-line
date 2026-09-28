using ClaudeTuiLine;

namespace ClaudeTuiLine.Tests;

public class EngramTelemetryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", "telemetry", name);

    [Fact]
    public void OwnSessionRecord_TakesPrecedenceOverForeignNonSharedKindRecord()
    {
        var result = EngramTelemetry.Build("session-own", Now, Fixture("own_vs_foreign.jsonl"));

        Assert.NotNull(result);
        Assert.Equal("✎ Foo.cs", result!.Verb); // must not leak the foreign session's "Bar.cs"
    }

    [Fact]
    public void SharedKind_EligibleDespiteSessionMismatch()
    {
        var result = EngramTelemetry.Build("session-own", Now, Fixture("shared_kind.jsonl"));

        Assert.NotNull(result);
        Assert.Equal("✱ saved", result!.Verb);
    }

    [Fact]
    public void MissingSessionId_UsesPlaceholder_OnlySharedKindsEligible()
    {
        var result = EngramTelemetry.Build(sessionId: null, Now, Fixture("placeholder_session.jsonl"));

        Assert.NotNull(result);
        Assert.Equal("✱ saved", result!.Verb); // the real-session file-touched record must not leak through
    }

    [Fact]
    public void PrimerFactCount_IgnoresRecallRecordsOwnCountField()
    {
        var result = EngramTelemetry.Build("session-own", Now, Fixture("primer_fact_count.jsonl"));

        Assert.NotNull(result);
        Assert.Equal((long?)42, result!.Facts); // from session-start, not the recall record's 999
        Assert.Equal("◉ recalled", result.Verb);
    }

    [Fact]
    public void InstantEvent_AtExactly10Seconds_StillFresh()
    {
        var result = EngramTelemetry.Build("session-own", Now, Fixture("fresh_at_boundary.jsonl"));

        Assert.NotNull(result);
        Assert.Equal("✱ captured", result!.Verb);
    }

    [Fact]
    public void InstantEvent_At11Seconds_IsStale_ResultIsNull()
    {
        var result = EngramTelemetry.Build("session-own", Now, Fixture("stale_after_boundary.jsonl"));

        Assert.Null(result);
    }

    [Fact]
    public void OngoingWork_BothIndexingAndEmbedding_ShowTogether()
    {
        var result = EngramTelemetry.Build("session-x", Now, Fixture("ongoing_active.jsonl"));

        Assert.NotNull(result);
        Assert.Equal("✎ indexing ∿ embedding", result!.Verb);
    }

    [Fact]
    public void OngoingWork_FinishedRecordClearsTheVerb()
    {
        var result = EngramTelemetry.Build("session-x", Now, Fixture("ongoing_cleared.jsonl"));

        Assert.Null(result);
    }

    [Fact]
    public void OngoingWork_Beyond900Seconds_DoesNotShow()
    {
        var result = EngramTelemetry.Build("session-x", Now, Fixture("ongoing_expired.jsonl"));

        Assert.Null(result);
    }

    [Fact]
    public void OngoingVerb_RendersBeforeInstantVerb()
    {
        var result = EngramTelemetry.Build("session-own", Now, Fixture("both_verbs_ordering.jsonl"));

        Assert.NotNull(result);
        Assert.Equal("✎ indexing ✱ captured", result!.Verb);
    }

    [Fact]
    public void MissingFile_ReturnsNull_NoThrow()
    {
        var result = EngramTelemetry.Build("session-own", Now, "/nonexistent/path/telemetry.jsonl");
        Assert.Null(result);
    }

    [Fact]
    public void TornFirstLine_OfLargeTailWindow_IsDroppedNotMisparsed()
    {
        // Pad past the 64KB tail window with a torn (truncated-by-seek) first line, then a
        // clean, fresh, eligible record as the last line.
        var tempFile = Path.GetTempFileName();
        try
        {
            var padding = new string('x', 70000); // forces the 64KB seek to land mid-line
            var goodLine = "{\"session_id\":\"session-own\",\"kind\":\"user-prompt\",\"timestamp\":\"2026-08-12T11:59:55Z\"}";
            File.WriteAllText(tempFile, padding + "\n" + goodLine + "\n");

            var result = EngramTelemetry.Build("session-own", Now, tempFile);

            Assert.NotNull(result);
            Assert.Equal("✱ captured", result!.Verb);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
