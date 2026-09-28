namespace ClaudeTuiLine.Tests;

public class KeySuggestionTests
{
    [Fact]
    public void EditDistance_EmptyAgainstNonEmpty_IsFullLength()
    {
        Assert.Equal(3, KeySuggestion.EditDistance("", "abc"));
    }

    [Fact]
    public void EditDistance_IdenticalStrings_IsZero()
    {
        Assert.Equal(0, KeySuggestion.EditDistance("abc", "abc"));
    }

    [Fact]
    public void EditDistance_ColorColour_IsOne()
    {
        Assert.Equal(1, KeySuggestion.EditDistance("color", "colour"));
    }

    [Fact]
    public void EditDistance_ColorColro_IsTwo()
    {
        Assert.Equal(2, KeySuggestion.EditDistance("color", "colro"));
    }

    [Fact]
    public void EditDistance_CaseOnlyMismatch_IsOne()
    {
        Assert.Equal(1, KeySuggestion.EditDistance("Color", "color"));
    }

    [Fact]
    public void Suggest_NoCandidateWithinThreshold_ReturnsEmpty()
    {
        Assert.Empty(KeySuggestion.Suggest("zzzzzz", new[] { "color", "format" }));
    }

    [Fact]
    public void Suggest_HalfLengthBound_TooShortUnknown_ReturnsEmpty()
    {
        Assert.Empty(KeySuggestion.Suggest("ab", new[] { "color" }));
    }

    [Fact]
    public void Suggest_HalfLengthBound_ExactlyAtBoundary_ReturnsEmpty()
    {
        // distance 2 against a 4-char unknown: 2*2 < 4 is false, so this must not qualify.
        Assert.Empty(KeySuggestion.Suggest("abcd", new[] { "abxy" }));
    }

    [Fact]
    public void Suggest_PrefixCandidateLongerThanUnknown_Qualifies()
    {
        Assert.Equal(new[] { "ttlSeconds" }, KeySuggestion.Suggest("ttl", new[] { "ttlSeconds" }));
    }

    [Fact]
    public void Suggest_PrefixUnknownLongerThanCandidate_Qualifies()
    {
        Assert.Equal(new[] { "colorSystem" }, KeySuggestion.Suggest("colorSystemX", new[] { "colorSystem" }));
    }

    [Fact]
    public void Suggest_PrefixFloor_ShorterStringBelowThreeChars_DoesNotQualify()
    {
        // min("c", "case") == 1 < 3: the prefix relation holds but the floor blocks it, and
        // "c" is nowhere near any of these candidates by distance either.
        Assert.Empty(KeySuggestion.Suggest("c", new[] { "case", "color", "colors", "colorSystem", "children" }));
    }

    [Fact]
    public void Suggest_PrefixFloor_ShorterStringExactlyTwoChars_DoesNotQualify()
    {
        // min("tt", "ttSeconds") == 2 < 3: below the floor, and distance 7 doesn't qualify either.
        Assert.Empty(KeySuggestion.Suggest("tt", new[] { "ttSeconds" }));
    }

    [Fact]
    public void Suggest_PrefixFloor_ShorterStringExactlyThreeChars_Qualifies()
    {
        // min("ttl", "ttlSeconds") == 3: the floor is inclusive ("at least three"), so this
        // must still qualify — the motivating example for the floor.
        Assert.Equal(new[] { "ttlSeconds" }, KeySuggestion.Suggest("ttl", new[] { "ttlSeconds" }));
    }

    [Fact]
    public void Suggest_PrefixMatch_OutranksCloserDistanceMatch()
    {
        // "ttx" is distance 1 from "ttl" (half-length bound: 1*2 < 3 holds), so it qualifies by
        // distance — but "ttlSeconds" qualifies by prefix, and prefix must win regardless of
        // how much closer the distance match is.
        Assert.Equal(new[] { "ttlSeconds" }, KeySuggestion.Suggest("ttl", new[] { "ttlSeconds", "ttx" }));
    }

    [Fact]
    public void Suggest_WithinPrefixMatches_ShortestCandidateWins()
    {
        Assert.Equal(new[] { "ttlSeconds" }, KeySuggestion.Suggest("ttl", new[] { "ttlSeconds", "ttlSecondsExtra" }));
    }

    [Fact]
    public void Suggest_GenuineTie_NamesAllCandidatesOrdinallySortedRegardlessOfInputOrder()
    {
        // "abce" and "abcf" are both distance 1 from "abcd" and neither is a prefix of the
        // other or of "abcd" — a genuine tie, which must name both rather than pick one.
        var forward = KeySuggestion.Suggest("abcd", new[] { "abce", "abcf" });
        var reversed = KeySuggestion.Suggest("abcd", new[] { "abcf", "abce" });

        Assert.Equal(new[] { "abce", "abcf" }, forward);
        Assert.Equal(forward, reversed);
    }

    [Fact]
    public void Suggest_EmptyUnknown_ReturnsEmpty()
    {
        Assert.Empty(KeySuggestion.Suggest("", new[] { "color" }));
    }
}
