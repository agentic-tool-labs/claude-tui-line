using System.Text.Json.Serialization;

namespace ClaudeTuiLine;

public sealed class StatusInput
{
    [JsonPropertyName("cwd")]
    public string? Cwd { get; set; }

    [JsonPropertyName("workspace")]
    public WorkspaceInfo? Workspace { get; set; }

    [JsonPropertyName("worktree")]
    public WorktreeInfo? Worktree { get; set; }

    [JsonPropertyName("pr")]
    public PrInfo? Pr { get; set; }

    [JsonPropertyName("model")]
    public ModelInfo? Model { get; set; }

    [JsonPropertyName("effort")]
    public EffortInfo? Effort { get; set; }

    [JsonPropertyName("thinking")]
    public ThinkingInfo? Thinking { get; set; }

    [JsonPropertyName("output_style")]
    public OutputStyleInfo? OutputStyle { get; set; }

    [JsonPropertyName("context_window")]
    public ContextWindowInfo? ContextWindow { get; set; }

    [JsonPropertyName("rate_limits")]
    public RateLimitsInfo? RateLimits { get; set; }

    [JsonPropertyName("agent")]
    public AgentInfo? Agent { get; set; }

    [JsonPropertyName("vim")]
    public VimInfo? Vim { get; set; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }

    // SPEC-101-calibrate-chrome-reserve.md §12.1: absence of a field from this model is evidence
    // about this tool, not about the payload — "version" is a real top-level key Claude Code sends
    // (tests/ClaudeTuiLine.Tests/fixtures/real_captured_workspace.json:13) that was simply never
    // modelled before. Nullable; null means UNKNOWN, never a value, never equal to a recorded
    // version.
    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

public sealed class WorkspaceInfo
{
    [JsonPropertyName("repo")]
    public RepoInfo? Repo { get; set; }
}

public sealed class RepoInfo
{
    [JsonPropertyName("host")]
    public string? Host { get; set; }

    [JsonPropertyName("owner")]
    public string? Owner { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class WorktreeInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("branch")]
    public string? Branch { get; set; }
}

public sealed class PrInfo
{
    [JsonPropertyName("number")]
    public long? Number { get; set; }

    [JsonPropertyName("review_state")]
    public string? ReviewState { get; set; }
}

public sealed class ModelInfo
{
    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }
}

public sealed class EffortInfo
{
    [JsonPropertyName("level")]
    public string? Level { get; set; }
}

public sealed class ThinkingInfo
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }
}

public sealed class OutputStyleInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class ContextWindowInfo
{
    [JsonPropertyName("used_percentage")]
    public double? UsedPercentage { get; set; }

    [JsonPropertyName("total_input_tokens")]
    public long? TotalInputTokens { get; set; }

    [JsonPropertyName("context_window_size")]
    public long? ContextWindowSize { get; set; }
}

public sealed class RateLimitsInfo
{
    [JsonPropertyName("five_hour")]
    public RateWindowInfo? FiveHour { get; set; }

    [JsonPropertyName("seven_day")]
    public RateWindowInfo? SevenDay { get; set; }
}

public sealed class RateWindowInfo
{
    [JsonPropertyName("used_percentage")]
    public double? UsedPercentage { get; set; }
}

public sealed class AgentInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class VimInfo
{
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(StatusInput))]
public partial class StatusInputJsonContext : JsonSerializerContext
{
}
