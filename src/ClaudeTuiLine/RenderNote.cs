namespace ClaudeTuiLine;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §9.8.1/§9.8.2: one render-time note — a pane dropped for want of width, a
/// segment truncated to fit. The single object rendered three ways: <c>--preview --json</c>'s
/// <c>notes[]</c>, the bare human form's stderr lines (§9.3.3), and §12.6.10's per-render
/// <c>notes[]</c> one layer up.
/// </summary>
public sealed record RenderNote(string Message);

/// <summary>
/// SPEC-V2-FRAMEWORK.md §9.8.2: the sink <see cref="SizeResolver"/> and <see cref="PaneRenderer"/>
/// append to wherever they would otherwise silently drop a pane or truncate a segment. Never
/// nullable — the render path constructs one and discards it, <c>--preview</c> constructs one and
/// serializes it, so drawing code stays on the one path regardless of who is listening.
/// </summary>
public sealed class RenderNoteCollector
{
    private readonly List<RenderNote> _notes = new();

    public IReadOnlyList<RenderNote> Notes => _notes;

    public void Add(string message) => _notes.Add(new RenderNote(message));
}
