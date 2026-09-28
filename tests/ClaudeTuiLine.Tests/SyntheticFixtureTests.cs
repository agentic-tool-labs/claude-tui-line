using ClaudeTuiLine;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §9.3.1: "The type permits any combination, so the agreement is asserted by
/// test, not by this paragraph and not by the comment in SyntheticFixture.cs." Three redundant-field
/// pairs in the fixture must independently agree with each other.
/// </summary>
public class SyntheticFixtureTests
{
    [Fact]
    public void UsedPercentage_AgreesWith_TotalInputTokensOverContextWindowSize()
    {
        var contextWindow = SyntheticFixture.Input.ContextWindow!;
        var expected = 100.0 * contextWindow.TotalInputTokens!.Value / contextWindow.ContextWindowSize!.Value;

        Assert.Equal(expected, contextWindow.UsedPercentage!.Value);
    }

    [Fact]
    public void CannedGitBranch_AgreesWith_WorktreeBranch()
    {
        var ctx = SyntheticFixture.CreateItemContext();

        Assert.Equal(SyntheticFixture.Input.Worktree!.Branch, ctx.GitBranch);
    }

    [Fact]
    public void CannedRemoteUrl_AgreesWith_WorkspaceRepoAndWorktreeName()
    {
        var ctx = SyntheticFixture.CreateItemContext();
        var repo = SyntheticFixture.Input.Workspace!.Repo!;

        Assert.Equal($"https://{repo.Host}/{repo.Owner}/{repo.Name}", ctx.RemoteUrl);
        Assert.Equal(repo.Name, SyntheticFixture.Input.Worktree!.Name);
    }

    // §12.7.2: "Pin the relationship with a test, because this is precisely the assumption a later
    // reader will make wrongly: the emitted payload equals SyntheticFixture.Input in every field
    // except cwd, which equals the process working directory."
    [Fact]
    public void WithRealCwd_MatchesInputInEveryFieldExceptCwd()
    {
        var withRealCwd = SyntheticFixture.WithRealCwd("/some/real/cwd");

        Assert.Equal("/some/real/cwd", withRealCwd.Cwd);
        Assert.NotEqual(SyntheticFixture.Input.Cwd, withRealCwd.Cwd);
        Assert.Same(SyntheticFixture.Input.Workspace, withRealCwd.Workspace);
        Assert.Same(SyntheticFixture.Input.Worktree, withRealCwd.Worktree);
        Assert.Same(SyntheticFixture.Input.Pr, withRealCwd.Pr);
        Assert.Same(SyntheticFixture.Input.Model, withRealCwd.Model);
        Assert.Same(SyntheticFixture.Input.Effort, withRealCwd.Effort);
        Assert.Same(SyntheticFixture.Input.Thinking, withRealCwd.Thinking);
        Assert.Same(SyntheticFixture.Input.OutputStyle, withRealCwd.OutputStyle);
        Assert.Same(SyntheticFixture.Input.ContextWindow, withRealCwd.ContextWindow);
        Assert.Same(SyntheticFixture.Input.RateLimits, withRealCwd.RateLimits);
        Assert.Same(SyntheticFixture.Input.Agent, withRealCwd.Agent);
        Assert.Same(SyntheticFixture.Input.Vim, withRealCwd.Vim);
        Assert.Equal(SyntheticFixture.Input.SessionId, withRealCwd.SessionId);
    }
}
