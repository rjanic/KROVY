using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofImportNormalTransactionAbortProofSourceContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string Proof = ReadAutoCad(
        "Commands",
        "AutoCadRoofImportNormalTransactionAbortProofCommand.cs");

    [Fact]
    public void CommandAndObserver_AreStrictlyDebugOnly()
    {
        var trimmed = Proof.Trim();
        Assert.StartsWith("#if DEBUG", trimmed, StringComparison.Ordinal);
        Assert.EndsWith("#endif", trimmed, StringComparison.Ordinal);
        Assert.Contains("AK_DEBUG_C1_NORMAL_TX_ABORT", Proof);
        Assert.Contains("ROOF_IMPORT_C1_NORMAL_TX_ABORT", Proof);
        Assert.Contains("ROOF_IMPORT_C1_NORMAL_TX_ABORT_RESULT", Proof);
        Assert.DoesNotContain(
            "AK_DEBUG_C1_NORMAL_TX_ABORT",
            ReadAutoCad("PluginEntry.cs"));
    }

    [Fact]
    public void WriteOperation_UsesExactlyOneNormalTransactionAndAddsOneBtr()
    {
        var write = ExtractMethod(
            Proof,
            "private static void AddTemporaryBtrAndAbort(");

        Assert.Equal(1, CountOccurrences(Proof, "StartTransaction()"));
        Assert.Equal(1, CountOccurrences(write, "StartTransaction()"));
        Assert.Contains("OpenMode.ForWrite", write);
        Assert.Contains("new BlockTableRecord", write);
        Assert.Equal(1, CountOccurrences(write, "blockTable.Add(temporaryBtr)"));
        Assert.Equal(
            1,
            CountOccurrences(
                write,
                "transaction.AddNewlyCreatedDBObject(temporaryBtr, add: true)"));
        Assert.Contains("blockTable.Has(temporaryBtrName)", write);
        Assert.DoesNotContain(".Commit(", write);
        Assert.Contains("Guid.NewGuid():N", Proof);
    }

    [Fact]
    public void RollbackAudit_IsSeparateReadOnlyExactBlockTableLookup()
    {
        var audit = ExtractMethod(
            Proof,
            "private static bool AuditTemporaryBtrAbsent(");

        Assert.Contains("StartOpenCloseTransaction()", audit);
        Assert.Contains("OpenMode.ForRead", audit);
        Assert.Contains("return !blockTable.Has(temporaryBtrName);", audit);
        Assert.DoesNotContain("OpenMode.ForWrite", audit);
        Assert.DoesNotContain("AddNewlyCreatedDBObject", audit);
        Assert.DoesNotContain(".Add(", audit);
    }

    [Fact]
    public void DbmodTimeline_UsesExactNumericCheckpoints()
    {
        Assert.Equal(
            1,
            CountOccurrences(
                Proof,
                "AcApplication.GetSystemVariable(\"DBMOD\")"));
        Assert.Contains("Convert.ToInt32(", Proof);

        foreach (var phase in new[]
                 {
                     "before-target-transaction",
                     "after-btr-add",
                     "after-target-transaction-dispose",
                     "after-readonly-audit-dispose",
                     "command-ended",
                 })
        {
            Assert.Contains($"\"{phase}\"", Proof);
        }

        Assert.Contains("temporaryBtrAbsent={Bool(_temporaryBtrAbsent)}", Proof);
        Assert.Contains("dbmodBefore={_dbmodBefore}", Proof);
        Assert.Contains("dbmodAfterDispose={_dbmodAfterDispose}", Proof);
        Assert.Contains("dbmodCommandEnded={dbmodCommandEnded}", Proof);
        Assert.Contains("dbmodCommandEnded == _dbmodBefore", Proof);
    }

    [Fact]
    public void CommandEndedObserver_IsExactDocumentScopedAndOneShot()
    {
        Assert.Contains("private readonly Document _document;", Proof);
        Assert.Contains("document.CommandEnded += CommandEnded;", Proof);
        Assert.Contains("document.CommandCancelled += CommandCancelled;", Proof);
        Assert.Contains("document.CommandFailed += CommandFailed;", Proof);

        var matches = ExtractMethod(Proof, "private bool Matches(");
        Assert.Contains("_isArmed", matches);
        Assert.Contains("ReferenceEquals(sender, _document)", matches);
        Assert.Contains("e.GlobalCommandName ?? string.Empty", matches);
        Assert.Contains("CommandName", matches);
        Assert.Contains("StringComparison.Ordinal", matches);

        var ended = ExtractMethod(Proof, "private void CommandEnded(");
        var disarm = ended.IndexOf("Disarm();", StringComparison.Ordinal);
        var measurement = ended.IndexOf(
            "var dbmodCommandEnded = ReadDbmod();",
            StringComparison.Ordinal);
        Assert.True(disarm >= 0 && disarm < measurement);
        Assert.Contains("catch (System.Exception exception)", ended);
        Assert.DoesNotContain("throw ", ended);

        var cleanup = ExtractMethod(
            Proof,
            "private void CompleteWithoutMeasurement(");
        Assert.Contains("Disarm();", cleanup);
        Assert.Contains("catch", cleanup);
        Assert.DoesNotContain("throw ", cleanup);

        var disarmMethod = ExtractMethod(Proof, "private void Disarm()");
        Assert.Contains("_isArmed = false;", disarmMethod);
        Assert.Contains("_document.CommandEnded -= CommandEnded;", disarmMethod);
        Assert.Contains(
            "_document.CommandCancelled -= CommandCancelled;",
            disarmMethod);
        Assert.Contains("_document.CommandFailed -= CommandFailed;", disarmMethod);
    }

    [Fact]
    public void ProofContainsNoForbiddenHostActions()
    {
        foreach (var forbidden in new[]
                 {
                     "Database.Insert",
                     "WblockCloneObjects",
                     "Editor.Command",
                     "SendStringToExecute",
                     "StartUndoMark",
                     "EndUndoMark",
                     "SetSystemVariable",
                     ".Erase(",
                     "UserBreak",
                     "AbortDeepClone",
                     "Task.Delay",
                     "Dispatcher",
                     "Timer",
                     "Idle",
                 })
        {
            Assert.DoesNotContain(forbidden, Proof);
        }

        Assert.DoesNotContain(".Commit(", Proof);
        Assert.DoesNotContain("CommandFlags.NoUndoMarker", Proof);
    }

    [Fact]
    public void ReleaseCompilationContract_HasNoUnconditionalCommandOrMarker()
    {
        var trimmed = Proof.Trim();
        Assert.StartsWith("#if DEBUG", trimmed, StringComparison.Ordinal);
        Assert.EndsWith("#endif", trimmed, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(Proof, "[CommandMethod(CommandName, CommandFlags.Modal)]"));
        Assert.DoesNotContain("AutoCadRoofImportNormalTransactionAbortProofCommand", ReadAutoCad("PluginEntry.cs"));
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method signature not found: {signature}");
        var nextPrivate = source.IndexOf(
            "\n    private ",
            start + signature.Length,
            StringComparison.Ordinal);
        var nextInternal = source.IndexOf(
            "\n        internal ",
            start + signature.Length,
            StringComparison.Ordinal);
        var next = new[] { nextPrivate, nextInternal }
            .Where(index => index >= 0)
            .DefaultIfEmpty(-1)
            .Min();
        return next < 0 ? source[start..] : source[start..next];
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static string ReadAutoCad(params string[] path) =>
        File.ReadAllText(Path.Combine(
            new[] { RepositoryRoot, "src", "AcKrovy.AutoCAD" }
                .Concat(path)
                .ToArray()));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AcKrovy.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
