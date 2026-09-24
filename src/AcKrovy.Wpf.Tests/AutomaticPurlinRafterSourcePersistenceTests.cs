using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Wpf.Tests;

[Collection(WpfUiSerialCollection.CollectionName)]
public sealed class AutomaticPurlinRafterSourcePersistenceTests
{
    [Fact]
    public void PersistedManual_NonePresent_WithoutActual_RestoresExactProfile()
    {
        AppLanguageService.Apply("en");
        var layout = RoofAutomaticPurlinLayout.Empty with
        {
            RidgeEnabled = true,
            ManualRafterWidthMm = 90d,
            ManualRafterHeightMm = 200d,
            RafterSourcePolicy = RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            AcknowledgedActualKind = RoofAutomaticPurlinAcknowledgedActualKind.NonePresent,
        };
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed,
            layout,
            layoutExists: true);

        Assert.Equal(90d, viewModel.RafterWidthMm);
        Assert.Equal(200d, viewModel.RafterHeightMm);
        Assert.Equal(AutomaticPurlinRafterDimensionSource.ExplicitManual, viewModel.RafterDimensionSource);
        Assert.False(viewModel.ShowRafterSourceConflictWarning);
        Assert.True(viewModel.CanApply);
        Assert.True(viewModel.TryCreateDraft(out var draft, out _));
        Assert.Equal(RoofAutomaticPurlinAcknowledgedActualKind.NonePresent, draft!.AcknowledgedActualKind);
    }

    [Fact]
    public void PreferManual_AcknowledgedActualUnchanged_ReopensWithoutConflict()
    {
        var layout = RoofAutomaticPurlinLayout.Empty with
        {
            RidgeEnabled = true,
            ManualRafterWidthMm = 90d,
            ManualRafterHeightMm = 200d,
            RafterSourcePolicy = RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            AcknowledgedActualKind = RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
            AcknowledgedActualWidthMm = 80d,
            AcknowledgedActualHeightMm = 125d,
        };
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.RecoveredRoofRecipe,
            layout,
            layoutExists: true,
            rafterWidthMm: 80d,
            rafterHeightMm: 125d);

        Assert.False(viewModel.ShowRafterSourceConflictWarning);
        Assert.Equal(90d, viewModel.RafterWidthMm);
        Assert.Equal(200d, viewModel.RafterHeightMm);
        Assert.True(viewModel.CanApply);
    }

    [Fact]
    public void PreferManual_AcknowledgedActualChanged_ShowsNewConflict()
    {
        var layout = RoofAutomaticPurlinLayout.Empty with
        {
            RidgeEnabled = true,
            ManualRafterWidthMm = 90d,
            ManualRafterHeightMm = 200d,
            RafterSourcePolicy = RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            AcknowledgedActualKind = RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
            AcknowledgedActualWidthMm = 80d,
            AcknowledgedActualHeightMm = 125d,
        };
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.RecoveredRoofRecipe,
            layout,
            layoutExists: true,
            rafterWidthMm: 100d,
            rafterHeightMm: 180d);

        Assert.True(viewModel.ShowRafterSourceConflictWarning);
        Assert.False(viewModel.CanApply);
        Assert.Equal(100d, viewModel.ConflictActualRafterWidthMm);
        Assert.Equal(180d, viewModel.ConflictActualRafterHeightMm);
    }

    [Fact]
    public void PreferManual_NonePresent_ThenActualAppears_ShowsConflict()
    {
        var layout = RoofPurlinLayoutPersistenceRules.CreateNewDraftDefaults() with
        {
            ManualRafterWidthMm = 100d,
            ManualRafterHeightMm = 125d,
            RafterSourcePolicy = RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            AcknowledgedActualKind = RoofAutomaticPurlinAcknowledgedActualKind.NonePresent,
        };
        var persisted = RoofPurlinLayoutPersistenceRules.ValidateForWrite(layout);
        Assert.True(persisted.IsValid, persisted.Error.ToString());

        // Completely NEW ViewModel after persistence, with FRESH recovered actual 120×160.
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.RecoveredRoofRecipe,
            persisted.Layout,
            layoutExists: true,
            rafterWidthMm: 120d,
            rafterHeightMm: 160d);

        Assert.True(viewModel.ShowRafterSourceConflictWarning);
        Assert.False(viewModel.CanApply);
        Assert.Equal(100d, viewModel.RecoverableManualRafterWidthMm);
        Assert.Equal(125d, viewModel.RecoverableManualRafterHeightMm);
        Assert.Equal(100d, viewModel.RafterWidthMm);
        Assert.Equal(125d, viewModel.RafterHeightMm);
        Assert.Equal(120d, viewModel.ConflictActualRafterWidthMm);
        Assert.Equal(160d, viewModel.ConflictActualRafterHeightMm);
        Assert.Equal(
            RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            viewModel.DraftRafterSourcePolicy);
        Assert.Equal(
            RoofAutomaticPurlinAcknowledgedActualKind.NonePresent,
            viewModel.DraftAcknowledgedActualKind);
    }

    [Fact]
    public void PreferManual_NonePresent_MatchingActualStillShowsFirstAppearanceConflict()
    {
        var layout = RoofPurlinLayoutPersistenceRules.CreateNewDraftDefaults() with
        {
            ManualRafterWidthMm = 100d,
            ManualRafterHeightMm = 125d,
            RafterSourcePolicy = RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            AcknowledgedActualKind = RoofAutomaticPurlinAcknowledgedActualKind.NonePresent,
        };
        var persisted = RoofPurlinLayoutPersistenceRules.ValidateForWrite(layout).Layout!;

        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.RecoveredRoofRecipe,
            persisted,
            layoutExists: true,
            rafterWidthMm: 100d,
            rafterHeightMm: 125d);

        Assert.True(viewModel.ShowRafterSourceConflictWarning);
        Assert.False(viewModel.CanApply);
    }

    [Fact]
    public void PreferManual_KeepManualAck_SameActual_NoRepeatedConflict()
    {
        var layout = RoofPurlinLayoutPersistenceRules.CreateNewDraftDefaults() with
        {
            ManualRafterWidthMm = 100d,
            ManualRafterHeightMm = 125d,
            RafterSourcePolicy = RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            AcknowledgedActualKind = RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
            AcknowledgedActualWidthMm = 120d,
            AcknowledgedActualHeightMm = 160d,
        };
        var persisted = RoofPurlinLayoutPersistenceRules.ValidateForWrite(layout).Layout!;

        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.RecoveredRoofRecipe,
            persisted,
            layoutExists: true,
            rafterWidthMm: 120d,
            rafterHeightMm: 160d);

        Assert.False(viewModel.ShowRafterSourceConflictWarning);
        Assert.True(viewModel.CanApply);
        Assert.Equal(100d, viewModel.RafterWidthMm);
        Assert.Equal(125d, viewModel.RafterHeightMm);
    }

    [Fact]
    public void PreferManual_KeepManualAck_ActualChanged_ShowsNewConflict()
    {
        var layout = RoofPurlinLayoutPersistenceRules.CreateNewDraftDefaults() with
        {
            ManualRafterWidthMm = 100d,
            ManualRafterHeightMm = 125d,
            RafterSourcePolicy = RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            AcknowledgedActualKind = RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
            AcknowledgedActualWidthMm = 120d,
            AcknowledgedActualHeightMm = 160d,
        };
        var persisted = RoofPurlinLayoutPersistenceRules.ValidateForWrite(layout).Layout!;

        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.RecoveredRoofRecipe,
            persisted,
            layoutExists: true,
            rafterWidthMm: 120d,
            rafterHeightMm: 180d);

        Assert.True(viewModel.ShowRafterSourceConflictWarning);
        Assert.False(viewModel.CanApply);
        Assert.Equal(120d, viewModel.ConflictActualRafterWidthMm);
        Assert.Equal(180d, viewModel.ConflictActualRafterHeightMm);
        Assert.Equal(100d, viewModel.RecoverableManualRafterWidthMm);
    }

    [Fact]
    public void PreferActual_ConfirmManualNonePresent_ThenActualReappears_ShowsConflict()
    {
        // After missing-actual confirm + Apply: PreferManual + NonePresent.
        var afterConfirm = RoofPurlinLayoutPersistenceRules.CreateNewDraftDefaults() with
        {
            ManualRafterWidthMm = 100d,
            ManualRafterHeightMm = 125d,
            RafterSourcePolicy = RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            AcknowledgedActualKind = RoofAutomaticPurlinAcknowledgedActualKind.NonePresent,
        };
        var persisted = RoofPurlinLayoutPersistenceRules.ValidateForWrite(afterConfirm).Layout!;

        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.RecoveredRoofRecipe,
            persisted,
            layoutExists: true,
            rafterWidthMm: 120d,
            rafterHeightMm: 160d);

        Assert.True(viewModel.ShowRafterSourceConflictWarning);
        Assert.False(viewModel.CanApply);
        Assert.Equal(
            RoofAutomaticPurlinAcknowledgedActualKind.NonePresent,
            viewModel.DraftAcknowledgedActualKind);
    }

    [Fact]
    public void PreferManual_NonePresent_WithoutRecoveredActual_DoesNotInventConflict()
    {
        var layout = RoofPurlinLayoutPersistenceRules.CreateNewDraftDefaults() with
        {
            ManualRafterWidthMm = 100d,
            ManualRafterHeightMm = 125d,
            RafterSourcePolicy = RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            AcknowledgedActualKind = RoofAutomaticPurlinAcknowledgedActualKind.NonePresent,
        };
        var persisted = RoofPurlinLayoutPersistenceRules.ValidateForWrite(layout).Layout!;

        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed,
            persisted,
            layoutExists: true);

        Assert.False(viewModel.ShowRafterSourceConflictWarning);
        Assert.Equal(AutomaticPurlinRafterDimensionSource.ExplicitManual, viewModel.RafterDimensionSource);
        Assert.True(viewModel.CanApply);
    }

    [Fact]
    public void LegacySchemaTwoUnspecified_RequiresConfirmationWhenDiffer()
    {
        var layout = RoofAutomaticPurlinLayout.Empty with
        {
            RidgeEnabled = true,
            ManualRafterWidthMm = 90d,
            ManualRafterHeightMm = 200d,
            RafterSourcePolicy = RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            AcknowledgedActualKind = RoofAutomaticPurlinAcknowledgedActualKind.Unspecified,
        };
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.RecoveredRoofRecipe,
            layout,
            layoutExists: true,
            rafterWidthMm: 80d,
            rafterHeightMm: 125d);

        Assert.True(viewModel.ShowRafterSourceConflictWarning);
        Assert.False(viewModel.CanApply);
    }

    [Fact]
    public void ConflictChooseManual_UpdatesAcknowledgementOnDraft_CancelDoesNot()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("en");
            var layout = RoofAutomaticPurlinLayout.Empty with
            {
                RidgeEnabled = true,
                ManualRafterWidthMm = 90d,
                ManualRafterHeightMm = 200d,
                RafterSourcePolicy = RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
                AcknowledgedActualKind = RoofAutomaticPurlinAcknowledgedActualKind.NonePresent,
            };
            var viewModel = CreateViewModel(
                AutomaticPurlinDialogMode.ProductionEdit,
                AutomaticPurlinRafterDimensionSource.RecoveredRoofRecipe,
                layout,
                layoutExists: true,
                rafterWidthMm: 80d,
                rafterHeightMm: 125d);
            Assert.True(viewModel.ShowRafterSourceConflictWarning);

            var window = CreateOffscreenWindow(viewModel);
            window.Show();
            window.UpdateLayout();
            var dialog = new AutomaticPurlinRafterSourceConflictDialogWindow(
                90d,
                200d,
                80d,
                125d,
                CultureInfo.GetCultureInfo("en"),
                SettingsTheme.Light)
            {
                Owner = window,
            };
            dialog.Show();
            dialog.UpdateLayout();
            dialog.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(
                RoofAutomaticPurlinAcknowledgedActualKind.NonePresent,
                viewModel.DraftAcknowledgedActualKind);
            Assert.True(viewModel.ShowRafterSourceConflictWarning);

            Assert.True(viewModel.TryResolveRafterSourceConflictKeepManual());
            Assert.Equal(
                RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
                viewModel.DraftAcknowledgedActualKind);
            Assert.Equal(80d, viewModel.DraftAcknowledgedActualWidthMm);
            Assert.Equal(125d, viewModel.DraftAcknowledgedActualHeightMm);
            Assert.True(viewModel.TryCreateDraft(out var draft, out _));
            Assert.Equal(
                RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
                draft!.AcknowledgedActualKind);
            Assert.Equal(80d, draft.AcknowledgedActualWidthMm);
            window.Close();
        });
    }

    [Fact]
    public void ConflictChooseManual_ThenReopenWithSameActual_NoConflict()
    {
        var layout = RoofAutomaticPurlinLayout.Empty with
        {
            RidgeEnabled = true,
            ManualRafterWidthMm = 90d,
            ManualRafterHeightMm = 200d,
            RafterSourcePolicy = RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            AcknowledgedActualKind = RoofAutomaticPurlinAcknowledgedActualKind.NonePresent,
        };
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.RecoveredRoofRecipe,
            layout,
            layoutExists: true,
            rafterWidthMm: 80d,
            rafterHeightMm: 125d);
        Assert.True(viewModel.TryResolveRafterSourceConflictKeepManual());
        Assert.True(viewModel.TryCreateDraft(out var draft, out _));

        var reopened = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.RecoveredRoofRecipe,
            draft,
            layoutExists: true,
            rafterWidthMm: 80d,
            rafterHeightMm: 125d);
        Assert.False(reopened.ShowRafterSourceConflictWarning);
        Assert.Equal(90d, reopened.RafterWidthMm);
        Assert.True(reopened.CanApply);
    }

    [Fact]
    public void PreferActual_WhenActualRemoved_ShowsUnresolvedMissingActualTransition()
    {
        var layout = RoofPurlinLayoutPersistenceRules.CreateNewDraftDefaults() with
        {
            ManualRafterWidthMm = 100d,
            ManualRafterHeightMm = 125d,
            RafterSourcePolicy = RoofAutomaticPurlinRafterSourcePolicy.PreferActual,
            AcknowledgedActualKind = RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
            AcknowledgedActualWidthMm = 120d,
            AcknowledgedActualHeightMm = 160d,
        };
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed,
            layout,
            layoutExists: true);

        Assert.True(viewModel.ShowMissingActualTransitionWarning);
        Assert.True(viewModel.ShowProvisionalManualRafterFooterInfo);
        Assert.False(viewModel.ShowManualRafterFooterInfo);
        Assert.False(viewModel.HasAuthoritativeRafterProfile);
        Assert.False(viewModel.CanApply);
        Assert.Equal(100d, viewModel.RafterWidthMm);
        Assert.Equal(125d, viewModel.RafterHeightMm);
        Assert.Equal(
            RoofAutomaticPurlinRafterSourcePolicy.PreferActual,
            viewModel.DraftRafterSourcePolicy);
        Assert.True(viewModel.TryCreateDraft(out var draft, out _));
        Assert.Equal(RoofAutomaticPurlinRafterSourcePolicy.PreferActual, draft!.RafterSourcePolicy);
        Assert.Equal(
            RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
            draft.AcknowledgedActualKind);
    }

    [Fact]
    public void PreferActual_MissingActual_ConfirmStoredManual_EnablesApplyWithoutPersistingYet()
    {
        var layout = RoofPurlinLayoutPersistenceRules.CreateNewDraftDefaults() with
        {
            ManualRafterWidthMm = 100d,
            ManualRafterHeightMm = 125d,
            RafterSourcePolicy = RoofAutomaticPurlinRafterSourcePolicy.PreferActual,
            AcknowledgedActualKind = RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
            AcknowledgedActualWidthMm = 120d,
            AcknowledgedActualHeightMm = 160d,
        };
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed,
            layout,
            layoutExists: true);

        Assert.True(viewModel.TryConfirmStoredManualAfterMissingActual());
        Assert.False(viewModel.ShowMissingActualTransitionWarning);
        Assert.True(viewModel.HasExplicitManualRafterProfile);
        Assert.True(viewModel.CanApply);
        Assert.Equal(
            RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            viewModel.DraftRafterSourcePolicy);
        Assert.Equal(
            RoofAutomaticPurlinAcknowledgedActualKind.NonePresent,
            viewModel.DraftAcknowledgedActualKind);
        Assert.Equal(100d, viewModel.RafterWidthMm);
        Assert.Equal(125d, viewModel.RafterHeightMm);
    }

    [Fact]
    public void PreferActual_MissingActual_CancelConfirm_LeavesTransitionUnresolved()
    {
        var layout = RoofPurlinLayoutPersistenceRules.CreateNewDraftDefaults() with
        {
            ManualRafterWidthMm = 100d,
            ManualRafterHeightMm = 125d,
            RafterSourcePolicy = RoofAutomaticPurlinRafterSourcePolicy.PreferActual,
            AcknowledgedActualKind = RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
            AcknowledgedActualWidthMm = 120d,
            AcknowledgedActualHeightMm = 160d,
        };
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed,
            layout,
            layoutExists: true);

        // No confirmation invoked — transition must remain unresolved (Cancel semantics).
        Assert.True(viewModel.ShowMissingActualTransitionWarning);
        Assert.Equal(
            RoofAutomaticPurlinRafterSourcePolicy.PreferActual,
            viewModel.DraftRafterSourcePolicy);
        Assert.False(viewModel.CanApply);
    }

    [Fact]
    public void PreferActual_MissingActual_EnterDifferentManual_ResolvesOnlyAfterConfirm()
    {
        var layout = RoofPurlinLayoutPersistenceRules.CreateNewDraftDefaults() with
        {
            ManualRafterWidthMm = 100d,
            ManualRafterHeightMm = 125d,
            RafterSourcePolicy = RoofAutomaticPurlinRafterSourcePolicy.PreferActual,
            AcknowledgedActualKind = RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
            AcknowledgedActualWidthMm = 120d,
            AcknowledgedActualHeightMm = 160d,
        };
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed,
            layout,
            layoutExists: true);

        Assert.True(viewModel.ShowMissingActualTransitionWarning);
        Assert.True(viewModel.TryApplyManualRafterDimensions(110d, 140d));
        Assert.False(viewModel.ShowMissingActualTransitionWarning);
        Assert.Equal(110d, viewModel.RafterWidthMm);
        Assert.Equal(140d, viewModel.RafterHeightMm);
        Assert.Equal(
            RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            viewModel.DraftRafterSourcePolicy);
        Assert.Equal(
            RoofAutomaticPurlinAcknowledgedActualKind.NonePresent,
            viewModel.DraftAcknowledgedActualKind);
        Assert.True(viewModel.CanApply);
    }

    [Fact]
    public void PreferActual_MissingActual_ActualReappearsBeforeConfirm_UsesActualOrConflict()
    {
        var layout = RoofPurlinLayoutPersistenceRules.CreateNewDraftDefaults() with
        {
            ManualRafterWidthMm = 100d,
            ManualRafterHeightMm = 125d,
            RafterSourcePolicy = RoofAutomaticPurlinRafterSourcePolicy.PreferActual,
            AcknowledgedActualKind = RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
            AcknowledgedActualWidthMm = 120d,
            AcknowledgedActualHeightMm = 160d,
        };

        var matching = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.RecoveredRoofRecipe,
            layout,
            layoutExists: true,
            rafterWidthMm: 120d,
            rafterHeightMm: 160d);
        Assert.False(matching.ShowMissingActualTransitionWarning);
        Assert.False(matching.ShowRafterSourceConflictWarning);
        Assert.True(matching.CanApply);
        Assert.Equal(120d, matching.RafterWidthMm);

        var changed = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.RecoveredRoofRecipe,
            layout,
            layoutExists: true,
            rafterWidthMm: 130d,
            rafterHeightMm: 180d);
        Assert.False(changed.ShowMissingActualTransitionWarning);
        Assert.True(changed.ShowRafterSourceConflictWarning);
        Assert.False(changed.CanApply);
    }

    [Fact]
    public void PreferActual_MissingActual_AfterConfirm_ReopenWithoutActual_UsesManual()
    {
        var confirmed = RoofPurlinLayoutPersistenceRules.CreateNewDraftDefaults() with
        {
            ManualRafterWidthMm = 100d,
            ManualRafterHeightMm = 125d,
            RafterSourcePolicy = RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            AcknowledgedActualKind = RoofAutomaticPurlinAcknowledgedActualKind.NonePresent,
        };
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed,
            confirmed,
            layoutExists: true);

        Assert.False(viewModel.ShowMissingActualTransitionWarning);
        Assert.Equal(AutomaticPurlinRafterDimensionSource.ExplicitManual, viewModel.RafterDimensionSource);
        Assert.Equal(100d, viewModel.RafterWidthMm);
        Assert.True(viewModel.CanApply);
    }

    [Fact]
    public void PreferActual_WhenActualRemoved_KeepsPreferActualOnDraft()
    {
        var layout = RoofPurlinLayoutPersistenceRules.CreateNewDraftDefaults() with
        {
            ManualRafterWidthMm = 95d,
            ManualRafterHeightMm = 155d,
            RafterSourcePolicy = RoofAutomaticPurlinRafterSourcePolicy.PreferActual,
            AcknowledgedActualKind = RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
            AcknowledgedActualWidthMm = 80d,
            AcknowledgedActualHeightMm = 125d,
        };
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed,
            layout,
            layoutExists: true);

        Assert.True(viewModel.ShowMissingActualTransitionWarning);
        Assert.Equal(
            AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed,
            viewModel.RafterDimensionSource);
        Assert.Equal(95d, viewModel.RafterWidthMm);
        Assert.Equal(RoofAutomaticPurlinRafterSourcePolicy.PreferActual, viewModel.DraftRafterSourcePolicy);
        Assert.False(viewModel.CanApply);
        Assert.True(viewModel.TryCreateDraft(out var draft, out _));
        Assert.Equal(RoofAutomaticPurlinRafterSourcePolicy.PreferActual, draft!.RafterSourcePolicy);
        Assert.Equal(
            RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
            draft.AcknowledgedActualKind);
    }

    [Fact]
    public void ManualEntry_WithoutActual_PersistsNonePresentAcknowledgement()
    {
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed);
        viewModel.RidgeEnabled = true;
        Assert.True(viewModel.TryApplyManualRafterDimensions(100d, 180d));
        Assert.True(viewModel.TryCreateDraft(out var draft, out _));
        Assert.Equal(RoofAutomaticPurlinRafterSourcePolicy.PreferManual, draft!.RafterSourcePolicy);
        Assert.Equal(RoofAutomaticPurlinAcknowledgedActualKind.NonePresent, draft.AcknowledgedActualKind);
        Assert.Null(draft.AcknowledgedActualWidthMm);
    }

    [Fact]
    public void Identical125MmProfile_KeepsAcceptedTechnicalInvariants()
    {
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ReadOnlyPreview,
            AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed);
        while (viewModel.Rows.Count > 0)
        {
            viewModel.RemoveRow(viewModel.Rows[0]);
        }

        Assert.True(viewModel.TryApplyManualRafterDimensions(80d, 125d));
        viewModel.WallPlateEnabled = true;
        viewModel.RidgeEnabled = false;
        viewModel.WallPlateRow.PlacementMode = RoofAutomaticPurlinPlacementMode.PlanDistanceFromEave;
        viewModel.WallPlateRow.PlacementValueText = "0";
        viewModel.WallPlateRow.SelectedSeatingMode =
            RoofAutomaticPurlinSeatingDepthMode.PercentOfRafterHeight;
        viewModel.WallPlateRow.SeatingDepthValueText = "25";

        Assert.True(viewModel.TryGetPreviewPlan(out var plan) && plan is not null);
        Assert.Contains("31", viewModel.WallPlateRow.SeatingDepthDerived, StringComparison.Ordinal);
        Assert.Equal(125d, viewModel.RafterHeightMm);
    }

    private static AutomaticPurlinDialogViewModel CreateViewModel(
        AutomaticPurlinDialogMode mode,
        AutomaticPurlinRafterDimensionSource source,
        RoofAutomaticPurlinLayout? layout = null,
        bool layoutExists = false,
        double? rafterWidthMm = null,
        double? rafterHeightMm = null)
    {
        var solved = Solve(RectanglePoints());
        var rafterDefaults = TimberElementDefaults.For(TimberElementType.Rafter);
        if (rafterWidthMm is { } width)
        {
            rafterDefaults = rafterDefaults with { WidthMm = width };
        }

        if (rafterHeightMm is { } height)
        {
            rafterDefaults = rafterDefaults with { HeightMm = height };
        }

        return new AutomaticPurlinDialogViewModel(
            solved.Geometry,
            solved.Provenance,
            layout,
            layoutExists,
            null,
            false,
            TimberElementDefaults.For(TimberElementType.Purlin),
            TimberElementDefaults.For(TimberElementType.WallPlate),
            rafterDefaults,
            AppLanguageService.CurrentUiCulture,
            mode,
            existingAutomaticPurlinCount: 0,
            storedDatumLoadError: null,
            rafterDimensionSource: source);
    }

    private static AutomaticPurlinDialogWindow CreateOffscreenWindow(
        AutomaticPurlinDialogViewModel viewModel) =>
        new(viewModel, SettingsTheme.Light)
        {
            Left = -30000,
            Top = -30000,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
        };

    private static SolvedFixture Solve(RoofPoint2D[] points)
    {
        var input = new RoofFootprintInput(points, true);
        var normalized = RoofFootprintValidator.ValidateWithProvenance(input);
        Assert.True(normalized.Validation.IsValid, normalized.Validation.Error.ToString());
        var identity = RoofBoundaryIdentityRules.Validate(
            RoofBoundaryIdentitySchema.CurrentVersion,
            normalized.EdgeProvenance.Count,
            RoofBoundaryIdentityRules.FormatWinding(normalized.Validation.SourceOrientation),
            Enumerable.Range(1, normalized.EdgeProvenance.Count).ToArray()).Identity!;
        var provenance = RoofBoundaryIdentityProvenanceResolver.Resolve(input, identity);
        Assert.True(provenance.IsValid);
        var geometry = HipRoofGeometrySolver.Solve(new RoofDefinition(
            normalized.Validation.Footprint!,
            new RoofParameters(30d),
            RoofKind.Hip));
        Assert.True(geometry.IsValid, geometry.Error.ToString());
        return new SolvedFixture(Assert.IsType<HipRoofGeometry>(geometry.Geometry), provenance);
    }

    private static RoofPoint2D[] RectanglePoints() =>
        [new(0, 0), new(10000, 0), new(10000, 6000), new(0, 6000)];

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "Rafter source conflict test timed out.");
        Assert.Null(failure);
    }

    private sealed record SolvedFixture(
        HipRoofGeometry Geometry,
        RoofBoundaryIdentityProvenanceResult Provenance);
}
