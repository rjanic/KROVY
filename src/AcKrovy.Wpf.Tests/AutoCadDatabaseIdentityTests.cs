using AcKrovy.AutoCAD.Infrastructure;
using Xunit;

namespace AcKrovy.Wpf.Tests;

public sealed class AutoCadDatabaseIdentityTests
{
    [Fact]
    public void SameNativeToken_WithDifferentManagedWrappers_IsAccepted()
    {
        var result = AutoCadDatabaseIdentityPolicy.Compare(
            Token(0x1234),
            Token(0x1234),
            managedReferenceEquals: false);

        Assert.True(result.IsSameDatabase);
        Assert.False(result.ManagedReferenceEquals);
        Assert.Equal(
            "Native database identities match.",
            result.Reason);
    }

    [Fact]
    public void DifferentNativeTokens_AreRejectedEvenIfManagedReferenceMatches()
    {
        var result = AutoCadDatabaseIdentityPolicy.Compare(
            Token(0x1234),
            Token(0x5678),
            managedReferenceEquals: true);

        Assert.False(result.IsSameDatabase);
        Assert.True(result.ManagedReferenceEquals);
        Assert.Equal(
            "Native database identities differ.",
            result.Reason);
    }

    [Theory]
    [InlineData(null, 0x1234L)]
    [InlineData(0L, 0x1234L)]
    [InlineData(0x1234L, null)]
    [InlineData(0x1234L, 0L)]
    public void NullOrInvalidToken_IsRejected(long? expected, long? actual)
    {
        var result = AutoCadDatabaseIdentityPolicy.Compare(
            ToToken(expected),
            ToToken(actual),
            managedReferenceEquals: false);

        Assert.False(result.IsSameDatabase);
        Assert.Contains(
            "null, disposed, or invalid",
            result.Reason,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Diagnostics_DistinguishManagedAndNativeIdentity()
    {
        var result = AutoCadDatabaseIdentityPolicy.Compare(
            Token(0x1234),
            Token(0x1234),
            managedReferenceEquals: false);

        Assert.Equal("0x0000000000001234", result.ExpectedDiagnosticToken);
        Assert.Equal("0x0000000000001234", result.ActualDiagnosticToken);
        Assert.False(result.ManagedReferenceEquals);
        Assert.True(result.IsSameDatabase);
    }

    private static AutoCadDatabaseIdentityToken Token(long value) => new(value);

    private static AutoCadDatabaseIdentityToken? ToToken(long? value) =>
        value.HasValue
            ? Token(value.Value)
            : null;
}
