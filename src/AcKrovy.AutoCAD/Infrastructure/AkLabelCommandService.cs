using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using AcKrovy.AutoCAD.Settings;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models;
using AcKrovy.Core.Services;
using AcKrovy.Localization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Central AK_LABEL / AK_LABELS execution path.
/// Annotation-only: timber sources are opened ForRead; metadata is never rewritten
/// by SynchronizeElementIds / InitializeLocalCopies on this path.
/// </summary>
internal static class AkLabelCommandService
{
    private const int ResetAllProgressThreshold = 5;
    private const int EtaMinSamples = 3;

    public static ElementLabelUpdateResult Run(
        Database database,
        Editor editor,
        AkLabelIntention intention,
        IReadOnlyList<ObjectId>? selectedIds = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(editor);

        return intention switch
        {
            AkLabelIntention.MissingOnly => Execute(
                database,
                editor,
                AkLabelIntention.MissingOnly,
                targetTimberIds: null),
            AkLabelIntention.ResetSelected => ExecuteResetSelected(
                database,
                editor,
                selectedIds ?? Array.Empty<ObjectId>()),
            AkLabelIntention.ResetAll => Execute(
                database,
                editor,
                AkLabelIntention.ResetAll,
                targetTimberIds: null),
            _ => new ElementLabelUpdateResult(0, 0, 0),
        };
    }

    public static bool ConfirmResetAll()
    {
        try
        {
            var uiCulture = AppLanguageService.CurrentUiCulture;
            var title = UiStrings.GetString("Command_Labels_ResetAllTitle", uiCulture);
            var warning = UiStrings.GetString("Command_Labels_ResetAllWarning", uiCulture);
            var cancel = UiStrings.GetString("Common_Cancel", uiCulture);
            var confirm = UiStrings.GetString("AkLabelResetAllConfirm_Confirm", uiCulture);
            if (string.IsNullOrWhiteSpace(title) ||
                string.IsNullOrWhiteSpace(warning) ||
                string.IsNullOrWhiteSpace(cancel) ||
                string.IsNullOrWhiteSpace(confirm) ||
                title == "Command_Labels_ResetAllTitle" ||
                warning == "Command_Labels_ResetAllWarning" ||
                cancel == "Common_Cancel" ||
                confirm == "AkLabelResetAllConfirm_Confirm")
            {
                throw new InvalidOperationException(
                    "ResetAll confirmation resources are missing or unresolved.");
            }

            var theme = SettingsUiPreferencesStore.Load().Theme;
            var dialog = new AkLabelResetAllConfirmWindow(theme)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
            };
            var ownerHandle = TryGetAutoCadMainWindowHandle();
            SettingsWindowOwner.TryAssign(dialog, ownerHandle);

            // Drain leftover keyboard input from GetKeywords (e.g. Enter used to
            // accept Z/ResetAll) before the modal with Cancel-as-default opens.
            DrainDispatcherInputQueue();

            var result = AcApp.ShowModalWindow(dialog);
            return result == true;
        }
        catch (Exception exception)
        {
            Diagnostics.AcKrovyDiagnostics.Error(
                "AkLabelResetAll",
                $"ResetAll confirmation failed: {exception.GetType().FullName}: {exception.Message}",
                "AK_LABEL",
                exception);
            throw;
        }
    }

    /// <summary>
    /// Keyword prompt structured for a future KrovyDynamicPromptScope.
    /// Does not mutate global DYNMODE/DYNPROMPT.
    /// Enter / Missing → MissingOnly.
    /// Registers genuine AutoCAD global/local/display keywords so localized
    /// Dynamic Input and first-letter shortcuts (C/V/O, …) are valid inputs.
    /// Local matching tokens must have unique first letters in every UI language.
    /// </summary>
    public static bool TryPromptIntention(
        Editor editor,
        out AkLabelIntention intention,
        out bool cancelled)
    {
        ArgumentNullException.ThrowIfNull(editor);
        intention = AkLabelIntention.MissingOnly;
        cancelled = false;

        var setup = CreateKeywordPromptSetup();
        var response = editor.GetKeywords(setup.Options);
        if (response.Status == PromptStatus.Cancel ||
            response.Status == PromptStatus.Error)
        {
            cancelled = true;
            return false;
        }

        var isNoneOrEmpty =
            response.Status == PromptStatus.None ||
            string.IsNullOrWhiteSpace(response.StringResult);
        intention = AkLabelIntentionPromptRules.Parse(
            response.StringResult,
            isNoneOrEmpty,
            setup.MissingLocal,
            setup.SelectLocal,
            setup.AllLocal,
            setup.MissingDisplay,
            setup.SelectDisplay,
            setup.AllDisplay);
        return true;
    }

    private static void DrainDispatcherInputQueue()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        dispatcher.Invoke(DispatcherPriority.ApplicationIdle, static () => { });
        dispatcher.Invoke(DispatcherPriority.Input, static () => { });
        dispatcher.Invoke(DispatcherPriority.Background, static () => { });
    }

    private readonly struct KeywordPromptSetup
    {
        public KeywordPromptSetup(
            PromptKeywordOptions options,
            string missingLocal,
            string selectLocal,
            string allLocal,
            string missingDisplay,
            string selectDisplay,
            string allDisplay,
            string registeredAllGlobal)
        {
            Options = options;
            MissingLocal = missingLocal;
            SelectLocal = selectLocal;
            AllLocal = allLocal;
            MissingDisplay = missingDisplay;
            SelectDisplay = selectDisplay;
            AllDisplay = allDisplay;
            RegisteredAllGlobal = registeredAllGlobal;
        }

        public PromptKeywordOptions Options { get; }
        public string MissingLocal { get; }
        public string SelectLocal { get; }
        public string AllLocal { get; }
        public string MissingDisplay { get; }
        public string SelectDisplay { get; }
        public string AllDisplay { get; }
        public string RegisteredAllGlobal { get; }
    }

    /// <summary>
    /// Shared PromptKeywordOptions construction for interactive AK_LABEL / AK_LABELS.
    /// </summary>
    private static KeywordPromptSetup CreateKeywordPromptSetup()
    {
        var uiCulture = AppLanguageService.CurrentUiCulture;
        var missingLocal = UiStrings.GetString("Command_Labels_KeywordMissingLocal", uiCulture);
        var selectLocal = UiStrings.GetString("Command_Labels_KeywordSelectLocal", uiCulture);
        var allLocal = UiStrings.GetString("Command_Labels_KeywordAllLocal", uiCulture);
        var missingDisplay = UiStrings.GetString("Command_Labels_KeywordMissing", uiCulture);
        var selectDisplay = UiStrings.GetString("Command_Labels_KeywordSelect", uiCulture);
        var allDisplay = UiStrings.GetString("Command_Labels_KeywordAll", uiCulture);
        var registeredAllGlobal = AkLabelIntentionPromptRules.ResolveRegisteredAllGlobal(selectLocal);

        // Prompt already lists localized display keywords and the named default.
        var options = new PromptKeywordOptions(
            UiStrings.GetString("Command_Labels_IntentionPrompt", uiCulture))
        {
            AllowNone = true,
            AppendKeywordsToMessage = false,
        };
        options.Keywords.Add(
            AkLabelIntentionPromptRules.GlobalMissing,
            missingLocal,
            missingDisplay);
        options.Keywords.Add(
            AkLabelIntentionPromptRules.GlobalSelect,
            selectLocal,
            selectDisplay);
        // AutoCAD uniqueness spans global+local initials. German Auswählen (A)
        // collides with global All (A); register disambiguated global then.
        options.Keywords.Add(
            registeredAllGlobal,
            allLocal,
            allDisplay);
        options.Keywords.Default = AkLabelIntentionPromptRules.GlobalMissing;

        return new KeywordPromptSetup(
            options,
            missingLocal,
            selectLocal,
            allLocal,
            missingDisplay,
            selectDisplay,
            allDisplay,
            registeredAllGlobal);
    }

    private static ElementLabelUpdateResult ExecuteResetSelected(
        Database database,
        Editor editor,
        IReadOnlyList<ObjectId> selectedIds)
    {
        if (selectedIds.Count == 0)
        {
            return new ElementLabelUpdateResult(0, 0, 0);
        }

        var resolved = ResolveSelectedTimberSources(database, selectedIds);
        if (resolved.AcceptedIds.Count == 0)
        {
            return new ElementLabelUpdateResult(0, 0, resolved.Skipped);
        }

        var result = Execute(
            database,
            editor,
            AkLabelIntention.ResetSelected,
            resolved.AcceptedIds);
        return new ElementLabelUpdateResult(
            result.Created,
            result.Updated,
            result.Skipped + resolved.Skipped);
    }

    private static SelectedTimberSourceResolution ResolveSelectedTimberSources(
        Database database,
        IReadOnlyList<ObjectId> selectedIds)
    {
        using var transaction = database.TransactionManager.StartOpenCloseTransaction();
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var acceptedIds = new List<ObjectId>();
        var seenIds = new HashSet<ObjectId>();
        var skipped = 0;

        foreach (var id in selectedIds)
        {
            // Prompt selection already disallows duplicates, but keep direct and
            // pickfirst callers deterministic without inflating skipped counts.
            if (!seenIds.Add(id))
            {
                continue;
            }

            if (id.IsNull ||
                !AutoCadObjectIdAccess.TryGetObject<Entity>(
                    transaction,
                    id,
                    OpenMode.ForRead,
                    out var entity,
                    database) ||
                entity is null ||
                entity.IsErased ||
                !AutoCadEntityHelpers.IsSupportedTimberGeometry(entity) ||
                !metadataStore.TryRead(entity, out var data) ||
                data is null)
            {
                skipped++;
                continue;
            }

            acceptedIds.Add(id);
        }

        transaction.Commit();
        return new SelectedTimberSourceResolution(acceptedIds, skipped);
    }

    private static ElementLabelUpdateResult Execute(
        Database database,
        Editor editor,
        AkLabelIntention intention,
        IReadOnlyList<ObjectId>? targetTimberIds)
    {
        using var transaction = database.TransactionManager.StartTransaction();
        var metadataStore = new AutoCadTimberElementMetadataStore(transaction);
        var timberIds = targetTimberIds is null
            ? DrawingScanner.FindAllTimberElements(database, transaction, metadataStore)
            : targetTimberIds.Distinct().ToList();

        // Read-only ownership index — no SynchronizeElementIds / copy init
        // (those can write timber metadata and must stay off AK_LABEL).
        var existingMainSourceHandles = ElementLabelService.ReadLabelCandidates(database, transaction)
            .Select(candidate => candidate.SourceHandle)
            .Where(handle => !string.IsNullOrWhiteSpace(handle))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var created = 0;
        var updated = 0;
        var skipped = 0;
        var defaultProfile = TimberElementDefaultProfileStore.Load();
        var roundingStepMm = defaultProfile.GetCuttingLengthRoundingStepMm();
        var presentationBatchContext = AutoCadAnnotationPresentationBatchContext.Create(
            database,
            transaction,
            defaultProfile);

        using var progress = TryOpenResetAllProgress(intention, timberIds.Count);
        var stopwatch = Stopwatch.StartNew();
        var processed = 0;

        foreach (var id in timberIds)
        {
            try
            {
                if (!AutoCadObjectIdAccess.TryGetObject<Entity>(
                        transaction,
                        id,
                        OpenMode.ForRead,
                        out var entity,
                        database) ||
                    entity is null ||
                    !AutoCadEntityHelpers.IsSupportedTimberGeometry(entity) ||
                    !metadataStore.TryRead(entity, out var data) ||
                    data is null)
                {
                    skipped++;
                    continue;
                }

                var sourceHandle = entity.Handle.ToString();
                var hasExisting = AkLabelCommandRules.HasExistingMainAnnotationForSource(
                    sourceHandle,
                    existingMainSourceHandles);
                var action = AkLabelCommandRules.Decide(
                    intention,
                    data.AnnotationMode,
                    hasExisting);

                if (action == AkLabelSourceAction.NoOp)
                {
                    skipped++;
                    continue;
                }

                if (action == AkLabelSourceAction.ForceCanonicalRecreate)
                {
                    DeleteOwnedAnnotationsForSource(database, transaction, sourceHandle);
                }

                var ensured = TimberAnnotationService.EnsureForElement(
                    database,
                    transaction,
                    entity,
                    data,
                    presentationBatchContext,
                    previousElementId: null,
                    roundingStepMm,
                    copySourcePreservation: false);

                if (ensured)
                {
                    created++;
                }
                else
                {
                    updated++;
                }
            }
            catch (System.Exception ex)
            {
                skipped++;
                editor.WriteMessage(UiStrings.Format(
                    UiStrings.CommandLabelsRefreshFailedFormat,
                    id,
                    ex.Message));
            }
            finally
            {
                processed++;
                progress?.Report(processed, timberIds.Count, stopwatch.Elapsed);
            }
        }

        if (intention is AkLabelIntention.ResetSelected or AkLabelIntention.ResetAll ||
            created > 0)
        {
            TimberAnnotationService.DeleteDuplicatesForExistingSourceHandles(
                database,
                transaction);
        }

        transaction.Commit();
        return new ElementLabelUpdateResult(created, updated, skipped);
    }

    private static ResetAllProgressSession? TryOpenResetAllProgress(
        AkLabelIntention intention,
        int total)
    {
        if (intention != AkLabelIntention.ResetAll || total < ResetAllProgressThreshold)
        {
            return null;
        }

        try
        {
            var theme = SettingsUiPreferencesStore.Load().Theme;
            var window = new AkLabelResetAllProgressWindow(total, theme)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
            };
            SettingsWindowOwner.TryAssign(window, TryGetAutoCadMainWindowHandle());
            window.Show();
            window.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Render, static () => { });
            return new ResetAllProgressSession(window, total);
        }
        catch
        {
            // Progress UI is optional — never block ResetAll writes.
            return null;
        }
    }

    private static IntPtr TryGetAutoCadMainWindowHandle()
    {
        try
        {
            return AcApp.MainWindow?.Handle ?? IntPtr.Zero;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static void DeleteOwnedAnnotationsForSource(
        Database database,
        Transaction transaction,
        string sourceHandle)
    {
        ElementLabelService.DeleteForSourceHandle(database, transaction, sourceHandle);
        SlopeAnnotationService.DeleteForSourceHandle(database, transaction, sourceHandle);
        PostFootprintPerpendicularAnnotationService.DeleteForSourceHandle(
            database,
            transaction,
            sourceHandle);
    }

    private sealed class ResetAllProgressSession : IDisposable
    {
        private readonly AkLabelResetAllProgressWindow _window;
        private bool _disposed;

        public ResetAllProgressSession(AkLabelResetAllProgressWindow window, int total)
        {
            _window = window;
        }

        public void Report(int processed, int total, TimeSpan elapsed)
        {
            if (_disposed)
            {
                return;
            }

            TimeSpan? eta = null;
            if (processed >= EtaMinSamples &&
                processed < total &&
                elapsed.TotalSeconds > 0.25)
            {
                var averageTicks = elapsed.Ticks / processed;
                var remaining = total - processed;
                eta = TimeSpan.FromTicks(averageTicks * remaining);
            }

            _window.Report(processed, elapsed, eta);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                _window.Close();
            }
            catch
            {
                // Ignore close races after AutoCAD focus changes.
            }
        }
    }

    private sealed record SelectedTimberSourceResolution(
        IReadOnlyList<ObjectId> AcceptedIds,
        int Skipped);
}
