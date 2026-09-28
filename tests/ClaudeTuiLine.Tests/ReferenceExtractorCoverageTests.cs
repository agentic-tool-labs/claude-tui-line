using System.Reflection;

namespace ClaudeTuiLine.Tests;

/// <summary>
/// SPEC-V2-FRAMEWORK.md §9.5.1: a fail-closed coverage test over every reference-carrying member
/// reachable from the reflection roots — <see cref="Pane"/>, <see cref="PaneBorder"/>,
/// <see cref="PaneItem"/>, and every <see cref="ColorResolution"/> reference/colour-rule type. A
/// member whose type is a ClaudeTuiLine-declared record (or a collection of one) is recursed into
/// rather than itself being a candidate; a member whose type is declared outside the ClaudeTuiLine
/// assembly is not recursed and must be exempted by name instead, since we cannot verify a foreign
/// type is string-free without recursing into it; otherwise a member is a candidate iff its type
/// transitively contains string (a string, a collection of string, a dictionary keyed or valued by
/// string, a tuple containing one); everything else (bool, int?, every enum) is out of scope.
/// Every candidate must appear in a <see cref="ItemValueResolver.ReferenceExtractor"/>/
/// <see cref="ItemValueResolver.ColorTokenExtractor"/> row's Members, or in <see cref="Exemptions"/>
/// with a reason — so a new reference-carrying member added anywhere in this reachable graph fails
/// the build until someone classifies it, rather than silently sitting outside every id/colour-token
/// scan.
/// </summary>
public class ReferenceExtractorCoverageTests
{
    private enum ExemptionKind
    {
        // Can never name another item or colour token, by the shape of the value.
        NeverAReference,

        // Will become a reference form under a spec section not yet implemented; carries that section.
        PendingForm,
    }

    private static readonly IReadOnlyDictionary<MemberInfo, (ExemptionKind Kind, string Reason)> Exemptions =
        new Dictionary<MemberInfo, (ExemptionKind, string)>
        {
            [Member<PaneItem>(nameof(PaneItem.Format))] = (ExemptionKind.NeverAReference, "literal {}-substitution only (LeafItems.cs), not a reference form"),
            [Member<PaneItem>(nameof(PaneItem.Extract))] = (ExemptionKind.NeverAReference, "a regex applied to the item's own resolved value"),
            [Member<PaneItem>(nameof(PaneItem.Case))] = (ExemptionKind.NeverAReference, "a closed case-transform token set (\"upper\"/\"lower\")"),
            [Member<ColorResolution.ColorExpr.Literal>(nameof(ColorResolution.ColorExpr.Literal.Spec))] = (ExemptionKind.NeverAReference, "a literal colour spec (ColorResolution.cs), never re-parsed"),
            [Member<ColorResolution.ColorValue.Literal>(nameof(ColorResolution.ColorValue.Literal.Spec))] = (ExemptionKind.NeverAReference, "a literal colour spec; anything @-prefixed became a ColorValue.TokenRef at parse time (§4.1) and is therefore covered"),
            [Member<ColorResolution.MatchRule>(nameof(ColorResolution.MatchRule.Contains))] = (ExemptionKind.NeverAReference, "a predicate over the item's own value, not an id"),
            [Member<ColorResolution.MatchRule>(nameof(ColorResolution.MatchRule.EqualsValue))] = (ExemptionKind.NeverAReference, "a predicate over the item's own value, not an id"),
            [Member<Pane>(nameof(Pane.Id))] = (ExemptionKind.NeverAReference, "pane-id-title-align §2.3: an addressing/diagnostics handle in a separate namespace from item ids, never itself a reference"),
            [Member<Pane>(nameof(Pane.Size))] = (ExemptionKind.NeverAReference, "a size form per §4.1 (integer/percentage/content/fill/auto), never an id"),
            [Member<Pane>(nameof(Pane.Ellipsis))] = (ExemptionKind.NeverAReference, "literal display text"),
            [Member<PaneBorder>(nameof(PaneBorder.Style))] = (ExemptionKind.NeverAReference, "Spectre.Console's BoxBorder — a foreign type, not recursed into"),
            [Member<PaneItemPart>(nameof(PaneItemPart.Text))] = (ExemptionKind.NeverAReference, "a compound part's literal text, never a reference"),
            [Member<PaneItemPart>(nameof(PaneItemPart.Extract))] = (ExemptionKind.NeverAReference, "a regex applied to the part's own resolved value"),
            [Member<PaneItemPart>(nameof(PaneItemPart.Case))] = (ExemptionKind.NeverAReference, "a closed case-transform token set (\"upper\"/\"lower\")"),
            [Member<PaneItemPart>(nameof(PaneItemPart.Format))] = (ExemptionKind.NeverAReference, "literal {}-substitution only (LeafItems.cs), not a reference form"),
        };

    [Fact]
    public void EveryReachableStringMemberIsCoveredOrExempt()
    {
        var candidates = CollectCandidates();

        var covered = new HashSet<MemberInfo>(
            ItemValueResolver.ReferenceExtractors.SelectMany(e => e.Members)
                .Concat(ItemValueResolver.ColorTokenExtractors.SelectMany(e => e.Members)));

        var unclassified = candidates
            .Where(c => !covered.Contains(c) && !Exemptions.ContainsKey(c))
            .Select(Describe)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unclassified.Count == 0,
            "Unclassified reference-carrying member(s) — add a ReferenceExtractors/ColorTokenExtractors "
                + "row or an Exemptions entry for: " + string.Join(", ", unclassified));
    }

    [Fact]
    public void CandidateCountMatchesTheMeasuredFilter()
    {
        var candidates = CollectCandidates();

        Assert.True(
            candidates.Count == 25,
            $"Expected 25 candidates from the 8 reflection roots, found {candidates.Count}: "
                + string.Join(", ", candidates.Select(Describe).OrderBy(s => s, StringComparer.Ordinal)));
    }

    private static List<MemberInfo> CollectCandidates()
    {
        var ownAssembly = typeof(ItemValueResolver).Assembly;
        var roots = new[]
        {
            typeof(Pane),
            typeof(PaneBorder),
            typeof(PaneItem),
            typeof(ColorResolution.ColorExpr),
            typeof(ColorResolution.ColorValue),
            typeof(ColorResolution.ColorRule),
            typeof(ColorResolution.ThresholdRule),
            typeof(ColorResolution.MatchRule),
        };

        var candidates = new List<MemberInfo>();
        var visited = new HashSet<Type>();

        void Visit(Type type)
        {
            if (!visited.Add(type))
            {
                return;
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                Classify(property);
            }

            if (type.IsAbstract || type.IsInterface)
            {
                foreach (var subtype in ownAssembly.GetTypes().Where(t => t != type && !t.IsAbstract && !t.IsInterface && type.IsAssignableFrom(t)))
                {
                    Visit(subtype);
                }
            }
        }

        void Classify(PropertyInfo property)
        {
            var recursionTarget = FullyUnwrapSingle(property.PropertyType);
            if (recursionTarget != typeof(string) && recursionTarget.Assembly == ownAssembly && !recursionTarget.IsEnum)
            {
                Visit(recursionTarget);
                return;
            }

            if (ContainsString(property.PropertyType))
            {
                candidates.Add(property);
                return;
            }

            var scalarProbe = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (IsScalarOutOfScope(scalarProbe))
            {
                return;
            }

            candidates.Add(property);
        }

        foreach (var root in roots)
        {
            Visit(root);
        }

        return candidates;
    }

    // Repeatedly unwraps a nullable value type, an array's element type, or a single-type-argument
    // generic's argument, stopping at the first type that is none of those — the single inner type
    // rule 1 asks "is this a ClaudeTuiLine record?" about.
    private static Type FullyUnwrapSingle(Type type)
    {
        var probe = type;
        while (true)
        {
            var next = Nullable.GetUnderlyingType(probe)
                ?? (probe.IsArray ? probe.GetElementType() : null)
                ?? (probe.IsGenericType && probe.GetGenericArguments().Length == 1 ? probe.GetGenericArguments()[0] : null);
            if (next is null)
            {
                return probe;
            }

            probe = next;
        }
    }

    // Rule 3, literally: string itself, string?, a collection of string, a dictionary keyed or
    // valued by string, or a tuple containing one — checked by unwrapping nullable/array layers and
    // testing every generic type argument, not just one, so a future Dictionary<string, X> or a
    // tuple is caught regardless of which argument position carries the string.
    private static bool ContainsString(Type type)
    {
        if (type == typeof(string))
        {
            return true;
        }

        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null)
        {
            return ContainsString(nullable);
        }

        if (type.IsArray)
        {
            var element = type.GetElementType();
            return element is not null && ContainsString(element);
        }

        return type.IsGenericType && type.GetGenericArguments().Any(ContainsString);
    }

    private static bool IsScalarOutOfScope(Type type) =>
        type.IsEnum || type.IsPrimitive || type == typeof(decimal) || type == typeof(DateTime) || type == typeof(Guid);

    private static MemberInfo Member<T>(string name) => typeof(T).GetProperty(name)!;

    private static string Describe(MemberInfo member) => $"{member.DeclaringType!.Name}.{member.Name}";
}
