namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §10.1: "a test that passes on a blank surface is not testing content."
/// Every layout test asserting a width invariant (§10 bullets 2/3/4/6) over item-driven content
/// must also assert that invariant on a blank-surface control — the same tree, every item forced
/// to resolve empty — and that the two renders are content-distinguishable. A broken compositor is
/// a control for the padding a width assertion measures; a blank surface is a control for the
/// content it does not.
/// </summary>
internal static class BlankSurfaceControl
{
    /// <summary>
    /// An ItemContext equivalent to <paramref name="ctx"/> but with every source field nulled out.
    /// Built-in items resolve their display text via <c>ItemRegistry.Find(id).BuildDefaultSegment(ctx)</c>
    /// — driven by <c>ctx</c>, not by the resolved-values dictionary — so blanking the values dict alone
    /// does not force built-in items to render empty; the context itself must be blanked.
    /// </summary>
    public static ItemContext Blank(ItemContext ctx) =>
        new(new StatusInput(), gitBranch: null, engram: null, remoteUrlProbe: () => null);

    /// <summary>Every resolved item value forced to resolve empty, for value-driven (non-registry-default) items.</summary>
    public static IReadOnlyDictionary<string, string?> BlankValues(IReadOnlyDictionary<string, string?> values) =>
        values.ToDictionary(kv => kv.Key, _ => (string?)"");

    /// <summary>Minimum bar per §10.1: the populated and blank-surface renders must be distinguishable by content.</summary>
    public static void AssertContentDiffers(string populatedMarkup, string blankMarkup) =>
        Assert.NotEqual(DisplayWidth.Strip(populatedMarkup), DisplayWidth.Strip(blankMarkup));
}
