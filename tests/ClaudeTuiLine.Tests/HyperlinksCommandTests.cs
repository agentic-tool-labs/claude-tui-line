using Xunit;

namespace ClaudeTuiLine.Tests;

public sealed class HyperlinksCommandTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("hyperlinks-cmd").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private string Profile => Path.Combine(_dir, ".zshenv");

    [Fact]
    public void Enable_CreatesProfileWithGuardedExport()
    {
        HyperlinksCommand.Enable(Profile, false, Now);

        var text = File.ReadAllText(Profile);
        Assert.Contains(HyperlinksCommand.ExportLine, text);
        Assert.Contains(HyperlinksCommand.Marker, text);
    }

    [Fact]
    public void Enable_IsIdempotent()
    {
        HyperlinksCommand.Enable(Profile, false, Now);
        var once = File.ReadAllText(Profile);

        var report = HyperlinksCommand.Enable(Profile, false, Now);

        Assert.Equal(once, File.ReadAllText(Profile));
        Assert.StartsWith("already enabled", report);
    }

    [Fact]
    public void Enable_KeepsExistingContentAndAddsMissingNewline()
    {
        File.WriteAllText(Profile, "export A=1");

        HyperlinksCommand.Enable(Profile, false, Now);

        var lines = File.ReadAllLines(Profile);
        Assert.Equal("export A=1", lines[0]);
        Assert.Equal(HyperlinksCommand.Marker, lines[1]);
        Assert.Equal(HyperlinksCommand.ExportLine, lines[2]);
    }

    [Fact]
    public void Disable_RemovesOnlyItsOwnLines()
    {
        File.WriteAllText(Profile, "export A=1\n");
        HyperlinksCommand.Enable(Profile, false, Now);
        File.AppendAllText(Profile, "export B=2\n");

        HyperlinksCommand.Disable(Profile, false, Now);

        Assert.Equal(new[] { "export A=1", "export B=2" }, File.ReadAllLines(Profile));
    }

    [Fact]
    public void Disable_WithoutProfile_ReportsNothingToRemove()
    {
        Assert.StartsWith("nothing to remove", HyperlinksCommand.Disable(Profile, false, Now));
        Assert.False(File.Exists(Profile));
    }

    [Fact]
    public void Enable_BacksUpExistingProfileBeforeWriting()
    {
        File.WriteAllText(Profile, "export A=1\n");

        HyperlinksCommand.Enable(Profile, false, Now);

        var backup = Assert.Single(Directory.GetFiles(_dir, "*.bak-claude-tui-line-*"));
        Assert.Equal("export A=1\n", File.ReadAllText(backup));
        Assert.EndsWith("20261015T120000Z", backup);
    }

    [Fact]
    public void RepeatedRunsInOneSecond_KeepEveryBackup()
    {
        File.WriteAllText(Profile, "export A=1\n");
        HyperlinksCommand.Enable(Profile, false, Now);
        HyperlinksCommand.Disable(Profile, false, Now);

        Assert.Equal(2, Directory.GetFiles(_dir, "*.bak-claude-tui-line-*").Length);
    }

    [Fact]
    public void Enable_DryRun_WritesNothingAndReportsTheChange()
    {
        File.WriteAllText(Profile, "export A=1\n");

        var report = HyperlinksCommand.Enable(Profile, true, Now);

        Assert.Equal("export A=1\n", File.ReadAllText(Profile));
        Assert.Empty(Directory.GetFiles(_dir, "*.bak-*"));
        Assert.StartsWith("dry run", report);
        Assert.Contains(HyperlinksCommand.ExportLine, report);
    }

    [Fact]
    public void Disable_DryRun_WritesNothing()
    {
        HyperlinksCommand.Enable(Profile, false, Now);
        var before = File.ReadAllText(Profile);

        var report = HyperlinksCommand.Disable(Profile, true, Now.AddMinutes(1));

        Assert.Equal(before, File.ReadAllText(Profile));
        Assert.Empty(Directory.GetFiles(_dir, "*.bak-*"));
        Assert.StartsWith("dry run", report);
    }

    [Fact]
    public void Disable_BacksUpBeforeRemoving()
    {
        HyperlinksCommand.Enable(Profile, false, Now);
        var enabled = File.ReadAllText(Profile);

        HyperlinksCommand.Disable(Profile, false, Now.AddMinutes(1));

        var backup = Directory.GetFiles(_dir, "*20261015T120100Z").Single();
        Assert.Equal(enabled, File.ReadAllText(backup));
    }

    [Fact]
    public void Enable_ThroughSymlink_EditsTheRealFileAndKeepsTheLink()
    {
        var real = Path.Combine(_dir, "dotfiles-zshenv");
        File.WriteAllText(real, "export A=1\n");
        File.CreateSymbolicLink(Profile, real);

        HyperlinksCommand.Enable(Profile, false, Now);

        Assert.NotNull(new FileInfo(Profile).LinkTarget);
        Assert.Contains(HyperlinksCommand.ExportLine, File.ReadAllText(real));
    }

    [Fact]
    public void Enable_KeepsFileMode()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.WriteAllText(Profile, "export A=1\n");
        File.SetUnixFileMode(Profile, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        HyperlinksCommand.Enable(Profile, false, Now);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Profile));
    }

    [Theory]
    [InlineData("/bin/zsh", ".zshenv")]
    [InlineData("/usr/bin/bash", ".bashrc")]
    public void ProfilePath_PicksByShell(string shell, string expected)
    {
        Assert.Equal(Path.Combine("/h", expected), HyperlinksCommand.ProfilePath(shell, "/h"));
    }

    [Theory]
    [InlineData("/usr/bin/fish")]
    [InlineData(null)]
    public void ProfilePath_OtherShell_IsNull(string? shell)
    {
        Assert.Null(HyperlinksCommand.ProfilePath(shell, "/h"));
    }
}
