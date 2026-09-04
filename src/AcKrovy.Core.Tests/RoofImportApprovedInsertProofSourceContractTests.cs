using Xunit;

namespace AcKrovy.Core.Tests;

public sealed class RoofImportApprovedInsertProofSourceContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string Command = ReadAutoCad(
        "Commands",
        "AutoCadRoofImportApprovedInsertProofCommand.cs");
    private static readonly string Session = ReadAutoCad(
        "Infrastructure",
        "RoofImportApprovedInsertProofSession.cs");
    private static readonly string Probe = ReadAutoCad(
        "Infrastructure",
        "RoofImportDocumentLockVetoProbe.cs");
    private static readonly string Diagnostics = ReadAutoCad(
        "Infrastructure",
        "RoofImportRuntimeDiscoveryDiagnostics.cs");
    private static readonly string ProductionVeto = ReadAutoCad(
        "Infrastructure",
        "RoofImportCommandEntryProtection.cs");

    [Fact]
    public void ProofCommandAndSession_AreStrictlyDebugOnly()
    {
        AssertDebugOnly(Command);
        AssertDebugOnly(Session);
        AssertDebugOnly(Probe);
        AssertDebugOnly(Diagnostics);
        Assert.Contains("AK_DEBUG_APPROVED_INSERT_PROOF", Command);
        Assert.DoesNotContain("AK_DEBUG_APPROVED_INSERT_PROOF", ReadAutoCad("PluginEntry.cs"));
    }

    [Fact]
    public void SourceConsistency_UsesOneImmutableSnapshotForPreflightAndInsert()
    {
        Assert.Contains("File.Copy(selectedSource, snapshotPath, overwrite: false);", Command);
        Assert.Contains("FileAccess.Read", Command);
        Assert.Contains("FileShare.Read", Command);
        Assert.Contains("SHA256.HashData(snapshotLease)", Command);
        Assert.Contains("var preflight = PreflightSnapshot(snapshotPath);", Command);
        Assert.Contains("$\"{destinationBlockName}={snapshotPath}\"", Command);
        Assert.Contains("snapshotLease?.Dispose();", Command);
        Assert.Contains("DeleteSnapshotDirectory(snapshotDirectory, editor);", Command);
        Assert.DoesNotContain("PreflightSnapshot(selectedSource)", Command);
    }

    [Fact]
    public void PreflightAndNewDestinationCheck_PrecedeApproval()
    {
        var preflight = Command.IndexOf("PreflightSnapshot(snapshotPath)", StringComparison.Ordinal);
        var destinationCheck = Command.IndexOf(
            "EnsureDestinationDoesNotExist(",
            StringComparison.Ordinal);
        var markOk = Command.IndexOf(
            "RoofImportApprovedInsertProofSession.MarkPreflightOk",
            StringComparison.Ordinal);
        var approve = Command.IndexOf(
            "RoofImportApprovedInsertProofSession.Approve",
            StringComparison.Ordinal);

        Assert.True(preflight >= 0 && preflight < destinationCheck);
        Assert.True(destinationCheck < markOk && markOk < approve);
        Assert.Contains("The proof source contains a KROVY RegApp marker.", Command);
        Assert.Contains("The proof source contains KROVY XData.", Command);
        Assert.Contains("The proof source contains a KROVY dictionary marker.", Command);
        Assert.Contains("targetBtrDelta=", Command);
        Assert.Contains("result.TargetBlockCount - targetBlockCountBefore != 1", Command);
        Assert.Contains("result.TopLevelReferenceCount != 1", Command);
        Assert.Contains("OpenMode.ForRead", Command);
        Assert.DoesNotContain("OpenMode.ForWrite", Command);
    }

    [Fact]
    public void Approval_DefaultsAbsentAndIsExactDocumentCommandAndPhaseScoped()
    {
        Assert.Contains("private static SessionState? _active;", Session);
        Assert.DoesNotContain("ApprovalAvailable { get; set; } = true", Session);
        Assert.Contains("state.Phase != SessionPhase.Approved", Session);
        Assert.Contains("!ReferenceEquals(document, state.Document)", Session);
        Assert.Contains("ExpectedGlobalCommandName = \"-INSERT\"", Session);
        Assert.Contains("StringComparison.Ordinal", Session);
        Assert.DoesNotContain("OrdinalIgnoreCase", Session);
        Assert.DoesNotContain("NormalizeCommandName", Session);
        Assert.DoesNotContain("TrimStart", Session);
    }

    [Fact]
    public void ApprovalToken_IsConsumedExactlyOnceBeforeAllow()
    {
        var availableFalse = Session.IndexOf(
            "state.ApprovalAvailable = false;",
            Session.IndexOf("public static bool TryConsume(", StringComparison.Ordinal),
            StringComparison.Ordinal);
        var consumedTrue = Session.IndexOf(
            "state.TokenConsumed = true;",
            availableFalse,
            StringComparison.Ordinal);
        var success = Session.IndexOf("return true;", consumedTrue, StringComparison.Ordinal);

        Assert.True(availableFalse >= 0 && availableFalse < consumedTrue);
        Assert.True(consumedTrue < success);
        Assert.Contains("state.TokenConsumed", Session);
        Assert.Contains("!state.ApprovalAvailable", Session);

        var consumeCall = Probe.IndexOf(
            "RoofImportApprovedInsertProofSession.TryConsume(",
            StringComparison.Ordinal);
        var allowLog = Probe.IndexOf(
            "ROOF_IMPORT_APPROVED_INSERT session=",
            consumeCall,
            StringComparison.Ordinal);
        var allowReturn = Probe.IndexOf("return;", allowLog, StringComparison.Ordinal);
        var veto = Probe.IndexOf("e.Veto();", allowReturn, StringComparison.Ordinal);
        Assert.True(consumeCall >= 0 && consumeCall < allowLog);
        Assert.True(allowLog < allowReturn && allowReturn < veto);
    }

    [Fact]
    public void RawDashInsert_RemainsOnTheExistingOneShotVetoPath()
    {
        Assert.Equal(1, CountOccurrences(Probe, "e.Veto();"));
        Assert.Contains("_isArmed = false;", Probe);
        Assert.Contains("ROOF_IMPORT_LOCK_VETO command=", Probe);
        Assert.Contains("ROOF_IMPORT_LOCK_VETOED command=", Probe);
        Assert.DoesNotContain("_isArmed = false;\n                Write(\n                    e.Document,\n                    $\"ROOF_IMPORT_APPROVED_INSERT", Normalize(Probe));
    }

    [Fact]
    public void WrongDocumentOrCommand_CannotConsumeApproval()
    {
        var consume = ExtractMethod(Session, "public static bool TryConsume(");
        Assert.Contains("!ReferenceEquals(document, state.Document)", consume);
        Assert.Contains("globalCommandName,", consume);
        Assert.Contains("ExpectedGlobalCommandName,", consume);
        Assert.Contains("StringComparison.Ordinal", consume);
        Assert.Contains("return false;", consume);
    }

    [Fact]
    public void SynchronousInsert_UsesEditorCommandWithDeterministicNeutralArguments()
    {
        Assert.Contains("editor.Command(", Command);
        Assert.Contains("\"._-INSERT\"", Command);
        Assert.Contains("Point3d.Origin", Command);
        Assert.Contains("\"_XYZ\"", Command);
        Assert.Contains("1.0,\n                1.0,\n                1.0,\n                0.0", Normalize(Command));
        Assert.DoesNotContain("SendStringToExecute", Command);
        Assert.DoesNotContain("StartUndoMark", Command);
        Assert.DoesNotContain("EndUndoMark", Command);
        Assert.DoesNotContain("CommandFlags.NoUndoMarker", Command);
    }

    [Fact]
    public void LifecycleBinding_BeginsOnlyAfterTokenConsumption()
    {
        Assert.Contains("state.TokenConsumed", Session);
        Assert.Contains("SessionPhase.InsertStarted", Session);
        Assert.Contains("SessionPhase.MappingSeen", Session);
        Assert.Contains("ROOF_IMPORT_APPROVED_INSERT_SESSION", Session);
        Assert.Contains("mapping-seen", Session);

        foreach (var callback in new[]
                 {
                     "CommandWillStart",
                     "BeginInsert",
                     "InsertMappingAvailable",
                     "BeginDeepCloneTranslation",
                     "ObjectAppended",
                     "DeepCloneEnded",
                     "InsertEnded",
                     "CommandEnded",
                 })
        {
            Assert.Contains(callback, Diagnostics);
        }

        Assert.Contains(
            "RoofImportApprovedInsertProofSession.ObserveMapping(",
            Diagnostics);
        Assert.Contains(
            "RoofImportApprovedInsertProofSession.ObserveObjectAppended(",
            Diagnostics);
        Assert.Contains(
            "RoofImportApprovedInsertProofSession.ObserveCommandTerminal(",
            Diagnostics);
    }

    [Fact]
    public void SuccessCancelFailureDocumentDestructionAndShutdown_ClearAuthorization()
    {
        Assert.Contains("state.ApprovalAvailable = false;", Session);
        Assert.Contains("SessionPhase.Completed", Session);
        Assert.Contains("SessionPhase.Cancelled", Session);
        Assert.Contains("SessionPhase.Failed", Session);
        Assert.Contains("RoofImportApprovedInsertProofSession.Clear(", Command);
        Assert.Contains("RoofImportApprovedInsertProofSession.ClearDocument(", Probe);
        Assert.Contains("RoofImportApprovedInsertProofSession.ClearAll(\"plugin-stop\")", Probe);
        Assert.Contains("DocumentToBeDestroyed += DocumentToBeDestroyed;", Probe);
        Assert.Contains("DocumentToBeDestroyed -= DocumentToBeDestroyed;", Probe);
        Assert.Contains("approvedInsertCompleted ? \"completed\" : \"failed-or-cancelled\"", Command);
    }

    [Fact]
    public void DocumentLockCallback_RemainsNoDatabaseWriteNoThrowAndVetoIsSoleHostAction()
    {
        var callback = ExtractMethod(Probe, "private static void DocumentLockModeChanged(");
        Assert.Contains("catch (System.Exception exception)", callback);
        Assert.Equal(1, CountOccurrences(callback, "e.Veto();"));
        foreach (var forbidden in new[]
                 {
                     "StartTransaction",
                     "StartOpenCloseTransaction",
                     "OpenMode.ForWrite",
                     "UpgradeOpen",
                     ".Erase(",
                     "SetXData",
                     "TransactionManager",
                     ".LockDocument(",
                     "Idle",
                     "Timer",
                     "UserBreak",
                     "throw ",
                     "editor.Command",
                     "SendStringToExecute",
                 })
        {
            Assert.DoesNotContain(forbidden, callback);
        }
    }

    [Fact]
    public void ProductionVeto_RecognizesTheExactApprovedOneShotSessionBeforeVetoing()
    {
        var callback = ExtractMethod(
            ProductionVeto,
            "private static void DocumentLockModeChanged(");

        var blockedGuard = callback.IndexOf(
            "!BlockedCommands.Contains(command)",
            StringComparison.Ordinal);
        var recognize = callback.IndexOf(
            "RoofImportApprovedInsertProofSession.IsApprovedNativeInsert(e.Document, command)",
            StringComparison.Ordinal);
        var allowLog = callback.IndexOf(
            "action=allow-approved-session",
            StringComparison.Ordinal);
        var allowReturn = callback.IndexOf("return;", allowLog, StringComparison.Ordinal);
        var veto = callback.IndexOf("e.Veto();", StringComparison.Ordinal);

        // Recognition happens only for already-blocked commands, before the veto, and the
        // approved session short-circuits with an explicit return ahead of e.Veto().
        Assert.True(blockedGuard >= 0 && blockedGuard < recognize);
        Assert.True(recognize >= 0 && recognize < allowLog);
        Assert.True(allowLog < allowReturn && allowReturn < veto);
    }

    [Fact]
    public void ProductionVeto_StillBlocksUnapprovedInsertAndPreservesExactBlockedCommands()
    {
        // The single unconditional brake remains; no command is added or removed and no
        // command family (e.g. COPY) is collaterally blocked.
        Assert.Equal(1, CountOccurrences(ProductionVeto, "e.Veto();"));
        Assert.Contains("\"-INSERT\"", ProductionVeto);
        Assert.Contains("\"INSERT\"", ProductionVeto);
        Assert.Contains("\"CLASSICINSERT\"", ProductionVeto);
        Assert.DoesNotContain("COPY", ProductionVeto);
        Assert.DoesNotContain("Clear(", ProductionVeto);
    }

    [Fact]
    public void ProductionVeto_HasNoGlobalOrAllDebugBypass()
    {
        // The allowance is bound to the exact approved-session recognition; there is no
        // blanket DEBUG allow, no unconditional early return, and no command broadening.
        Assert.DoesNotContain("return; // DEBUG", ProductionVeto);
        Assert.DoesNotContain("#if DEBUG\n            return;", Normalize(ProductionVeto));
        Assert.DoesNotContain("BlockedCommands.Clear", ProductionVeto);
        Assert.DoesNotContain("BlockedCommands.Remove", ProductionVeto);
        Assert.Equal(1, CountOccurrences(ProductionVeto, "IsApprovedNativeInsert"));
    }

    [Fact]
    public void ProductionVeto_ApprovalRecognitionIsStrippedFromReleaseButVetoRemains()
    {
        var release = StripDebugBlocks(ProductionVeto);
        Assert.DoesNotContain("IsApprovedNativeInsert", release);
        Assert.DoesNotContain("action=allow-approved-session", release);
        Assert.DoesNotContain("RoofImportApprovedInsertProofSession", release);
        // Release keeps an unconditional brake for every blocked native INSERT entry.
        Assert.Contains("e.Veto();", release);
        Assert.Contains("BlockedCommands.Contains(command)", release);
    }

    [Fact]
    public void ApprovedSessionRecognition_IsReadOnlyExactBoundAndOneShotScoped()
    {
        var recognitionStart = Session.IndexOf(
            "public static bool IsApprovedNativeInsert(",
            StringComparison.Ordinal);
        Assert.True(recognitionStart >= 0);
        var recognitionEnd = Session.IndexOf(
            "public static void ObserveCommandWillStart(",
            recognitionStart,
            StringComparison.Ordinal);
        Assert.True(recognitionEnd > recognitionStart);
        var recognition = Session[recognitionStart..recognitionEnd];

        // Exact document + command binding, ordinal comparison, and only the live approved
        // or already-consumed phases are recognized (terminal phases fall through to veto).
        Assert.Contains("ReferenceEquals(document, state.Document)", recognition);
        Assert.Contains("ExpectedGlobalCommandName", recognition);
        Assert.Contains("StringComparison.Ordinal", recognition);
        Assert.Contains("SessionPhase.Approved or SessionPhase.TokenConsumed", recognition);
        Assert.DoesNotContain("SessionPhase.Completed", recognition);
        Assert.DoesNotContain("SessionPhase.Cancelled", recognition);
        Assert.DoesNotContain("SessionPhase.Failed", recognition);

        // The recognition never consumes the token or mutates any session state.
        Assert.DoesNotContain("ApprovalAvailable = false", recognition);
        Assert.DoesNotContain("ApprovalAvailable = true", recognition);
        Assert.DoesNotContain("TokenConsumed = true", recognition);
        Assert.DoesNotContain("state.Phase =", recognition);
        Assert.DoesNotContain("_active =", recognition);
        Assert.DoesNotContain("OpenMode", recognition);
        Assert.DoesNotContain("StartTransaction", recognition);
    }

    private static string StripDebugBlocks(string source)
    {
        var lines = new List<string>();
        var depth = 0;
        foreach (var line in Normalize(source).Split('\n'))
        {
            var directive = line.Trim();
            if (directive == "#if DEBUG")
            {
                depth++;
                continue;
            }

            if (directive == "#endif")
            {
                Assert.True(depth > 0);
                depth--;
                continue;
            }

            if (depth == 0)
            {
                lines.Add(line);
            }
        }

        Assert.Equal(0, depth);
        return string.Join('\n', lines);
    }

    private static void AssertDebugOnly(string source)
    {
        var trimmed = source.Trim();
        Assert.StartsWith("#if DEBUG", trimmed, StringComparison.Ordinal);
        Assert.EndsWith("#endif", trimmed, StringComparison.Ordinal);
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method signature not found: {signature}");
        var next = source.IndexOf("\n    private static ", start + signature.Length, StringComparison.Ordinal);
        if (next < 0)
        {
            next = source.IndexOf("\n    public static ", start + signature.Length, StringComparison.Ordinal);
        }

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

    private static string Normalize(string source) =>
        source.Replace("\r\n", "\n", StringComparison.Ordinal);

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
