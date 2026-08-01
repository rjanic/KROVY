using AcKrovy.AutoCAD.Infrastructure;
using Xunit;

namespace AcKrovy.Wpf.Tests;

public sealed class AutoCadTextStyleResolverTests
{
    [Fact]
    public void ResolveExplicit_ExactMatchUsesRequestedStyle()
    {
        var resolver = Resolver(Style("Krovy"), Style("Standard"));

        var result = resolver.ResolveExplicit("Krovy");

        Assert.Equal(AutoCadTextStyleResolutionKind.Requested, result.ResolutionKind);
        Assert.Equal(AutoCadTextStyleRequestStatus.Compatible, result.RequestStatus);
        Assert.Equal("Krovy", result.RequestedTextStyleName);
        Assert.Equal("Krovy", result.ResolvedTextStyleName);
        Assert.False(result.IsFallback);
        Assert.True(result.HasCompatibleStyle);
    }

    [Fact]
    public void ResolveExplicit_CaseInsensitiveMatchReturnsCanonicalName()
    {
        var resolver = Resolver(Style("AcKrovy Text"));

        var result = resolver.ResolveExplicit("ackrovy text");

        Assert.Equal(AutoCadTextStyleResolutionKind.Requested, result.ResolutionKind);
        Assert.Equal("ackrovy text", result.RequestedTextStyleName);
        Assert.Equal("AcKrovy Text", result.ResolvedTextStyleName);
    }

    [Fact]
    public void ResolveExplicit_MissingRequestedUsesStandardWithoutChangingRequest()
    {
        var resolver = Resolver(Style("Standard"), Style("Zeta", isCurrent: true));

        var result = resolver.ResolveExplicit("Missing Style");

        Assert.Equal(AutoCadTextStyleResolutionKind.StandardFallback, result.ResolutionKind);
        Assert.Equal(AutoCadTextStyleRequestStatus.Missing, result.RequestStatus);
        Assert.Equal("Missing Style", result.RequestedTextStyleName);
        Assert.Equal("Standard", result.ResolvedTextStyleName);
        Assert.True(result.IsFallback);
    }

    [Fact]
    public void ResolveExplicit_StandardLookupIsCaseInsensitiveAndReturnsCanonicalName()
    {
        var resolver = Resolver(Style("sTaNdArD"));

        var result = resolver.ResolveExplicit("Missing");

        Assert.Equal(AutoCadTextStyleResolutionKind.StandardFallback, result.ResolutionKind);
        Assert.Equal(AutoCadTextStyleRequestStatus.Missing, result.RequestStatus);
        Assert.Equal("sTaNdArD", result.ResolvedTextStyleName);
    }

    [Theory]
    [InlineData(2.5d, false)]
    [InlineData(-1d, false)]
    [InlineData(0d, true)]
    public void ResolveExplicit_IncompatibleRequestedUsesStandard(
        double textSize,
        bool isAnnotative)
    {
        var resolver = Resolver(
            Style("Bad", textSize: textSize, isAnnotative: isAnnotative),
            Style("Standard"));

        var result = resolver.ResolveExplicit("Bad");

        Assert.Equal(AutoCadTextStyleRequestStatus.Incompatible, result.RequestStatus);
        Assert.Equal(AutoCadTextStyleResolutionKind.StandardFallback, result.ResolutionKind);
        Assert.Equal("Bad", result.RequestedTextStyleName);
        Assert.Equal("Standard", result.ResolvedTextStyleName);
    }

    [Fact]
    public void ResolveExplicit_UsesCurrentWhenStandardIsUnavailable()
    {
        var resolver = Resolver(
            Style("Standard", textSize: 2.5d),
            Style("Current", isCurrent: true),
            Style("Alpha"));

        var result = resolver.ResolveExplicit("Missing");

        Assert.Equal(AutoCadTextStyleResolutionKind.CurrentFallback, result.ResolutionKind);
        Assert.Equal(AutoCadTextStyleRequestStatus.Missing, result.RequestStatus);
        Assert.Equal("Current", result.ResolvedTextStyleName);
    }

    [Fact]
    public void ResolveExplicit_UsesDeterministicFirstWhenStandardAndCurrentUnavailable()
    {
        var resolver = Resolver(Style("Zulu"), Style("alpha"), Style("Beta"));

        var result = resolver.ResolveExplicit("Missing");

        Assert.Equal(
            AutoCadTextStyleResolutionKind.FirstCompatibleFallback,
            result.ResolutionKind);
        Assert.Equal(AutoCadTextStyleRequestStatus.Missing, result.RequestStatus);
        Assert.Equal("alpha", result.ResolvedTextStyleName);
    }

    [Fact]
    public void ResolveLegacy_UsesCurrentBeforeStandardAndFirst()
    {
        var resolver = Resolver(
            Style("Alpha"),
            Style("Standard"),
            Style("Current", isCurrent: true));

        var result = resolver.ResolveLegacy();

        Assert.Equal(AutoCadTextStyleResolutionKind.CurrentFallback, result.ResolutionKind);
        Assert.Equal(AutoCadTextStyleRequestStatus.NotRequested, result.RequestStatus);
        Assert.Null(result.RequestedTextStyleName);
        Assert.Equal("Current", result.ResolvedTextStyleName);
    }

    [Fact]
    public void ResolveLegacy_UsesStandardWhenCurrentIsIncompatible()
    {
        var resolver = Resolver(
            Style("Current", textSize: 1d, isCurrent: true),
            Style("Standard"),
            Style("Alpha"));

        var result = resolver.ResolveLegacy();

        Assert.Equal(AutoCadTextStyleResolutionKind.StandardFallback, result.ResolutionKind);
        Assert.Equal("Standard", result.ResolvedTextStyleName);
    }

    [Fact]
    public void ResolveLegacy_UsesFirstWhenCurrentAndStandardUnavailable()
    {
        var resolver = Resolver(Style("Zulu"), Style("Alpha"));

        var result = resolver.ResolveLegacy();

        Assert.Equal(
            AutoCadTextStyleResolutionKind.FirstCompatibleFallback,
            result.ResolutionKind);
        Assert.Equal("Alpha", result.ResolvedTextStyleName);
    }

    [Fact]
    public void StandardAndCurrentSameStyle_HasUnambiguousPriority()
    {
        var resolver = Resolver(Style("Standard", isCurrent: true));

        var explicitResult = resolver.ResolveExplicit("Missing");
        var legacyResult = resolver.ResolveLegacy();

        Assert.Equal(
            AutoCadTextStyleResolutionKind.StandardFallback,
            explicitResult.ResolutionKind);
        Assert.Equal(
            AutoCadTextStyleResolutionKind.CurrentFallback,
            legacyResult.ResolutionKind);
        Assert.Equal("Standard", explicitResult.ResolvedTextStyleName);
        Assert.Equal("Standard", legacyResult.ResolvedTextStyleName);
    }

    [Fact]
    public void Resolve_EmptyCatalogReturnsStructuredFailure()
    {
        var resolver = Resolver();

        var explicitResult = resolver.ResolveExplicit("Missing");
        var legacyResult = resolver.ResolveLegacy();

        Assert.Equal(
            AutoCadTextStyleResolutionKind.NoCompatibleStyle,
            explicitResult.ResolutionKind);
        Assert.Equal(AutoCadTextStyleRequestStatus.Missing, explicitResult.RequestStatus);
        Assert.Equal("Missing", explicitResult.RequestedTextStyleName);
        Assert.False(explicitResult.HasCompatibleStyle);
        Assert.Null(explicitResult.ResolvedTextStyleName);
        Assert.Equal(
            AutoCadTextStyleResolutionKind.NoCompatibleStyle,
            legacyResult.ResolutionKind);
        Assert.Equal(
            AutoCadTextStyleRequestStatus.NotRequested,
            legacyResult.RequestStatus);
    }

    [Theory]
    [InlineData(2.5d, false)]
    [InlineData(-1d, false)]
    [InlineData(0d, true)]
    public void Resolve_OnlyIncompatibleStylesReturnsNoCompatibleStyle(
        double textSize,
        bool isAnnotative)
    {
        var resolver = Resolver(
            Style(
                "Standard",
                textSize: textSize,
                isAnnotative: isAnnotative,
                isCurrent: true));

        var result = resolver.ResolveLegacy();

        Assert.Equal(
            AutoCadTextStyleResolutionKind.NoCompatibleStyle,
            result.ResolutionKind);
        Assert.False(result.HasCompatibleStyle);
    }

    [Fact]
    public void Catalog_IgnoresErasedInvalidAndDuplicateNames()
    {
        var catalog = Catalog(
            Style("Good"),
            Style("GOOD"),
            Style("Erased", isErased: true),
            Style("Invalid", isValid: false),
            Style("Fixed", textSize: 2d),
            Style("Annotative", isAnnotative: true));

        var entry = Assert.Single(catalog.CompatibleStyles);
        Assert.Equal("GOOD", entry.CanonicalName, ignoreCase: true);
        Assert.False(catalog.TryFindCompatible("Erased", out _));
        Assert.False(catalog.TryFindCompatible("Invalid", out _));
        Assert.True(catalog.ContainsKnownName("Fixed"));
        Assert.True(catalog.ContainsKnownName("Annotative"));
    }

    [Fact]
    public void Catalog_DuplicateNamePreservesCurrentStyleIdentity()
    {
        var catalog = Catalog(
            Style("duplicate"),
            Style("DUPLICATE", isCurrent: true),
            Style("Other"));

        Assert.Equal(2, catalog.CompatibleStyles.Count);
        Assert.Equal("DUPLICATE", catalog.CurrentCompatibleStyle!.CanonicalName);
    }

    [Fact]
    public void Catalog_SortsNamesDeterministicallyWithOrdinalIgnoreCase()
    {
        var catalog = Catalog(Style("zeta"), Style("Beta"), Style("alpha"));

        Assert.Equal(
            ["alpha", "Beta", "zeta"],
            catalog.CompatibleStyles.Select(entry => entry.CanonicalName));
    }

    [Fact]
    public void Selection_RejectsContradictoryResolvedStates()
    {
        var entry = new AutoCadTextStylePolicyEntry("Style", false);

        Assert.Throws<ArgumentException>(() =>
            AutoCadTextStyleSelection.Resolved(
                "Missing",
                entry,
                AutoCadTextStyleResolutionKind.Requested,
                AutoCadTextStyleRequestStatus.Missing));
        Assert.Throws<ArgumentException>(() =>
            AutoCadTextStyleSelection.Resolved(
                "Style",
                entry,
                AutoCadTextStyleResolutionKind.Requested,
                AutoCadTextStyleRequestStatus.Incompatible));
        Assert.Throws<ArgumentException>(() =>
            AutoCadTextStyleSelection.Resolved(
                "Missing",
                entry,
                AutoCadTextStyleResolutionKind.StandardFallback,
                AutoCadTextStyleRequestStatus.Compatible));
        Assert.Throws<ArgumentException>(() =>
            AutoCadTextStyleSelection.Resolved(
                "Missing",
                entry,
                AutoCadTextStyleResolutionKind.StandardFallback,
                AutoCadTextStyleRequestStatus.Missing));
        Assert.Throws<ArgumentException>(() =>
            AutoCadTextStyleSelection.Resolved(
                "Missing",
                entry,
                AutoCadTextStyleResolutionKind.CurrentFallback,
                AutoCadTextStyleRequestStatus.Missing));
        Assert.Throws<ArgumentException>(() =>
            AutoCadTextStyleSelection.Resolved(
                "Missing",
                new AutoCadTextStylePolicyEntry("Current", true),
                AutoCadTextStyleResolutionKind.FirstCompatibleFallback,
                AutoCadTextStyleRequestStatus.Missing));
    }

    [Fact]
    public void Selection_RejectsContradictoryNoCompatibleStates()
    {
        Assert.Throws<ArgumentException>(() =>
            AutoCadTextStyleSelection.NoCompatibleStyle(
                "Style",
                AutoCadTextStyleRequestStatus.Compatible));
        Assert.Throws<ArgumentException>(() =>
            AutoCadTextStyleSelection.NoCompatibleStyle(
                null,
                AutoCadTextStyleRequestStatus.Missing));
    }

    [Fact]
    public void Selection_HasNoPublicConstructorOrSettableState()
    {
        Assert.Empty(typeof(AutoCadTextStyleSelection).GetConstructors());
        Assert.All(
            typeof(AutoCadTextStyleSelection).GetProperties(),
            property => Assert.Null(property.SetMethod));
    }

    private static AutoCadTextStyleSelectionPolicy Resolver(
        params AutoCadTextStylePolicyDescriptor[] descriptors) =>
        new(Catalog(descriptors));

    private static AutoCadTextStylePolicyCatalog Catalog(
        params AutoCadTextStylePolicyDescriptor[] descriptors) =>
        new(descriptors);

    private static AutoCadTextStylePolicyDescriptor Style(
        string name,
        bool isValid = true,
        bool isErased = false,
        double textSize = 0d,
        bool isAnnotative = false,
        bool isCurrent = false) =>
        new(
            name,
            isValid,
            isErased,
            textSize,
            isAnnotative,
            isCurrent);
}
