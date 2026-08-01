using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using AcadException = Autodesk.AutoCAD.Runtime.Exception;

namespace AcKrovy.AutoCAD.Infrastructure;

internal readonly record struct AutoCadDatabaseIdentityToken(long Value)
{
    public bool IsValid => Value != 0L;

    public string ToDiagnosticHex() =>
        IsValid
            ? $"0x{unchecked((ulong)Value):X16}"
            : "<invalid>";
}

internal sealed record AutoCadDatabaseIdentityComparison(
    AutoCadDatabaseIdentityToken? ExpectedToken,
    AutoCadDatabaseIdentityToken? ActualToken,
    bool ManagedReferenceEquals,
    bool IsSameDatabase,
    string Reason)
{
    public string ExpectedDiagnosticToken =>
        ExpectedToken is { } expected
            ? expected.ToDiagnosticHex()
            : "<null/disposed>";

    public string ActualDiagnosticToken =>
        ActualToken is { } actual
            ? actual.ToDiagnosticHex()
            : "<null/disposed>";
}

internal static class AutoCadDatabaseIdentityPolicy
{
    public static AutoCadDatabaseIdentityComparison Compare(
        AutoCadDatabaseIdentityToken? expected,
        AutoCadDatabaseIdentityToken? actual,
        bool managedReferenceEquals)
    {
        if (expected is not { IsValid: true })
        {
            return new AutoCadDatabaseIdentityComparison(
                expected,
                actual,
                managedReferenceEquals,
                false,
                "Expected database is null, disposed, or invalid.");
        }
        if (actual is not { IsValid: true })
        {
            return new AutoCadDatabaseIdentityComparison(
                expected,
                actual,
                managedReferenceEquals,
                false,
                "Actual database is null, disposed, or invalid.");
        }

        var isSame = expected.Value.Value == actual.Value.Value;
        return new AutoCadDatabaseIdentityComparison(
            expected,
            actual,
            managedReferenceEquals,
            isSame,
            isSame
                ? "Native database identities match."
                : "Native database identities differ.");
    }
}

internal static class AutoCadDatabaseIdentity
{
    public static bool IsSame(Database? expected, Database? actual) =>
        Compare(expected, actual).IsSameDatabase;

    public static bool IsSame(Database? expected, ObjectId actualObjectId) =>
        Compare(expected, actualObjectId).IsSameDatabase;

    public static AutoCadDatabaseIdentityComparison Compare(
        Database? expected,
        Database? actual) =>
        AutoCadDatabaseIdentityPolicy.Compare(
            TryGetToken(expected),
            TryGetToken(actual),
            ReferenceEquals(expected, actual));

    public static AutoCadDatabaseIdentityComparison Compare(
        Database? expected,
        ObjectId actualObjectId)
    {
        Database? actual = null;
        try
        {
            if (!actualObjectId.IsNull)
            {
                actual = actualObjectId.Database;
            }
        }
        catch (AcadException)
        {
            // The comparison result records an unavailable actual identity.
        }
        catch (ObjectDisposedException)
        {
            // The comparison result records an unavailable actual identity.
        }

        return Compare(expected, actual);
    }

    private static AutoCadDatabaseIdentityToken? TryGetToken(Database? database)
    {
        if (database is null)
        {
            return null;
        }

        try
        {
            if (database.IsDisposed || database.UnmanagedObject == IntPtr.Zero)
            {
                return null;
            }

            return new AutoCadDatabaseIdentityToken(
                database.UnmanagedObject.ToInt64());
        }
        catch (AcadException)
        {
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }
}
