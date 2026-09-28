namespace ClaudeTuiLine;

/// <summary>SPEC-V2-FRAMEWORK.md §3.3: one fragment of a compound <see cref="Segment"/>'s decomposition.</summary>
/// <param name="Plain">The span's contribution to the segment's Plain, in order.</param>
/// <param name="Markup">The span's own markup — a colour wrap, or a builtin's composite markup.</param>
public readonly record struct StyledSpan(string Plain, string Markup);

/// <summary>
/// One statusline segment. <see cref="Plain"/> is the visible text (used to measure width
/// for wrapping); <see cref="Markup"/> is the Spectre markup used to render it.
/// <see cref="Spans"/> is a compound item's (SPEC-V2-FRAMEWORK.md §3.3) per-part decomposition —
/// null for every other segment. When non-null, concatenating every span's <see cref="StyledSpan.Plain"/>
/// yields <see cref="Plain"/> and concatenating every span's <see cref="StyledSpan.Markup"/> yields
/// <see cref="Markup"/>; nothing downstream may use it to compute width — <see cref="Plain"/>'s
/// length is the sole width metric, unconditionally.
/// </summary>
public sealed record Segment(string Markup, string Plain, IReadOnlyList<StyledSpan>? Spans = null);
