using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AcKrovy.AutoCAD.Infrastructure;
using AcKrovy.AutoCAD.UI;
using AcKrovy.Core.Models;
using AcKrovy.Core.Models.Roofs;
using AcKrovy.Core.Services;
using AcKrovy.Core.Services.Roofs;
using AcKrovy.Localization;
using Xunit;

namespace AcKrovy.Wpf.Tests;

[Collection(WpfUiSerialCollection.CollectionName)]
public sealed class AutomaticPurlinManualRafterDimensionTests
{
    [Fact]
    public void UnresolvedProfileSeed_ShowsFooterWarning_AndBlocksApply()
    {
        AppLanguageService.Apply("sk");
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed);
        viewModel.RidgeEnabled = true;

        Assert.True(viewModel.RequiresManualRafterInput);
        Assert.False(viewModel.HasAuthoritativeRafterProfile);
        Assert.True(viewModel.ShowNoRafterFooterWarning);
        Assert.False(viewModel.ShowManualRafterFooterInfo);
        Assert.Equal("Strecha zatiaľ neobsahuje žiadnu krokvu.", viewModel.NoRafterWarningTitle);
        Assert.False(viewModel.CanApply);
        Assert.True(viewModel.CanPreview);
    }

    [Fact]
    public void ManualConfirm_UpdatesDimensionsSourceSchematicAndEnablesApply()
    {
        AppLanguageService.Apply("en");
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed);
        viewModel.RidgeEnabled = true;
        var beforeW = viewModel.RafterWidthMm;
        var beforeH = viewModel.RafterHeightMm;
        var beforeSeating = viewModel.Rows[0].SeatingDepthDerived;

        Assert.True(viewModel.TryApplyManualRafterDimensions(90d, 200d));

        Assert.Equal(90d, viewModel.RafterWidthMm);
        Assert.Equal(200d, viewModel.RafterHeightMm);
        Assert.Equal(AutomaticPurlinRafterDimensionSource.ExplicitManual, viewModel.RafterDimensionSource);
        Assert.True(viewModel.HasExplicitManualRafterProfile);
        Assert.False(viewModel.RequiresManualRafterInput);
        Assert.True(viewModel.CanApply);
        Assert.Contains("90", viewModel.ManualRafterFooterInfoText, StringComparison.Ordinal);
        Assert.Contains("200", viewModel.ManualRafterFooterInfoText, StringComparison.Ordinal);
        Assert.NotEqual(beforeSeating, viewModel.Rows[0].SeatingDepthDerived);
        Assert.Equal(200d, viewModel.SectionPresentation.RafterHeightMm);
        Assert.Equal(90d, viewModel.SectionPresentation.RafterWidthMm);
        Assert.NotEqual(beforeW, viewModel.RafterWidthMm);
        Assert.NotEqual(beforeH, viewModel.RafterHeightMm);
    }

    [Fact]
    public void ManualDialog_SelectFromDrawingButton_ExistsAndLocalizes()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("sk");
            var dialog = new AutomaticPurlinManualRafterDialogWindow(
                100d,
                125d,
                CultureInfo.GetCultureInfo("sk"),
                SettingsTheme.Light);
            dialog.Show();
            dialog.UpdateLayout();
            Assert.True(dialog.SelectFromDrawingButton.IsVisible);
            Assert.Equal("Vybrať z výkresu", dialog.SelectFromDrawingButton.Content);
            dialog.Close();
        });
    }

    [Fact]
    public void ManualDialog_ApplyPickedDimensions_PopulatesBothEditableFields()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("en");
            var dialog = new AutomaticPurlinManualRafterDialogWindow(
                80d,
                160d,
                CultureInfo.GetCultureInfo("en"),
                SettingsTheme.Light);
            dialog.Show();
            dialog.UpdateLayout();
            dialog.ApplyPickedDimensions(120d, 180d);
            Assert.Equal("120", dialog.WidthTextBox.Text);
            Assert.Equal("180", dialog.HeightTextBox.Text);
            dialog.WidthTextBox.Text = "130";
            dialog.HeightTextBox.Text = "190";
            Assert.Equal("130", dialog.WidthTextBox.Text);
            Assert.Equal("190", dialog.HeightTextBox.Text);
            Assert.False(dialog.IsSuspendedForCadPick);
            dialog.Close();
        });
    }

    [Fact]
    public void ManualDialog_SelectFromDrawing_SuspendsWithoutConfirmingDraft()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("en");
            var viewModel = CreateViewModel(
                AutomaticPurlinDialogMode.ProductionEdit,
                AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed);
            var widthBefore = viewModel.RafterWidthMm;
            var heightBefore = viewModel.RafterHeightMm;
            var sourceBefore = viewModel.RafterDimensionSource;

            var dialog = new AutomaticPurlinManualRafterDialogWindow(
                100d,
                125d,
                CultureInfo.GetCultureInfo("en"),
                SettingsTheme.Light);
            dialog.Show();
            dialog.UpdateLayout();
            dialog.WidthTextBox.Text = "100";
            dialog.HeightTextBox.Text = "125";
            dialog.SelectFromDrawingButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.True(dialog.IsSuspendedForCadPick);
            Assert.Equal(100d, dialog.PreserveWidthMm);
            Assert.Equal(125d, dialog.PreserveHeightMm);
            Assert.Equal(widthBefore, viewModel.RafterWidthMm);
            Assert.Equal(heightBefore, viewModel.RafterHeightMm);
            Assert.Equal(sourceBefore, viewModel.RafterDimensionSource);
        });
    }

    [Fact]
    public void ManualDialogPick_CurrentRoofUneditedConfirm_AdoptsSelectedRafter()
    {
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed);
        viewModel.BeginManualDialogCadPick(100d, 125d);
        viewModel.CompleteManualDialogCadPick(
            success: true,
            pickedWidthMm: 120d,
            pickedHeightMm: 160d,
            isCurrentRoofGeneratedRafter: true);

        Assert.True(viewModel.TryConsumeManualDialogReopen(out var reopen));
        Assert.Equal(120d, reopen.SeedWidthMm);
        Assert.Equal(160d, reopen.SeedHeightMm);
        Assert.True(viewModel.ShouldAdoptPickedRafterAsSelected(120d, 160d));
        Assert.True(viewModel.TryApplySelectedRafterDimensions(120d, 160d));
        Assert.Equal(AutomaticPurlinRafterDimensionSource.SelectedRafter, viewModel.RafterDimensionSource);
        viewModel.ClearManualDialogSession();
        Assert.False(viewModel.ShouldAdoptPickedRafterAsSelected(120d, 160d));
    }

    [Fact]
    public void ManualDialogPick_ExternalOrEdited_StaysExplicitManual()
    {
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed);
        viewModel.BeginManualDialogCadPick(100d, 125d);
        viewModel.CompleteManualDialogCadPick(
            success: true,
            pickedWidthMm: 120d,
            pickedHeightMm: 160d,
            isCurrentRoofGeneratedRafter: false);

        Assert.False(viewModel.ShouldAdoptPickedRafterAsSelected(120d, 160d));
        Assert.True(viewModel.TryApplyManualRafterDimensions(120d, 160d));
        Assert.Equal(AutomaticPurlinRafterDimensionSource.ExplicitManual, viewModel.RafterDimensionSource);

        viewModel.BeginManualDialogCadPick(100d, 125d);
        viewModel.CompleteManualDialogCadPick(
            success: true,
            pickedWidthMm: 120d,
            pickedHeightMm: 160d,
            isCurrentRoofGeneratedRafter: true);
        Assert.False(viewModel.ShouldAdoptPickedRafterAsSelected(130d, 160d));
        Assert.True(viewModel.TryApplyManualRafterDimensions(130d, 160d));
        Assert.Equal(AutomaticPurlinRafterDimensionSource.ExplicitManual, viewModel.RafterDimensionSource);
        Assert.Equal(130d, viewModel.RafterWidthMm);
    }

    [Fact]
    public void ManualDialogPick_EscapeOrInvalid_PreservesPreviousSeeds()
    {
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed);
        var sourceBefore = viewModel.RafterDimensionSource;
        viewModel.BeginManualDialogCadPick(100d, 125d);
        viewModel.CompleteManualDialogCadPick(
            success: false,
            pickedWidthMm: null,
            pickedHeightMm: null,
            isCurrentRoofGeneratedRafter: false);

        Assert.True(viewModel.TryConsumeManualDialogReopen(out var reopen));
        Assert.Equal(100d, reopen.SeedWidthMm);
        Assert.Equal(125d, reopen.SeedHeightMm);
        Assert.False(reopen.AdoptAsSelectedIfUnedited);
        Assert.Equal(sourceBefore, viewModel.RafterDimensionSource);
    }

    [Fact]
    public void ManualDialogPick_DoesNotPersistUntilMainApplyPath()
    {
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed);
        viewModel.BeginManualDialogCadPick(90d, 200d);
        viewModel.CompleteManualDialogCadPick(
            success: true,
            pickedWidthMm: 120d,
            pickedHeightMm: 160d,
            isCurrentRoofGeneratedRafter: false);
        Assert.True(viewModel.TryConsumeManualDialogReopen(out _));
        // Draft unchanged until Confirm applies dimensions.
        Assert.Equal(AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed, viewModel.RafterDimensionSource);
        Assert.True(viewModel.TryApplyManualRafterDimensions(120d, 160d));
        Assert.True(viewModel.TryCreateDraft(out var draft, out _));
        Assert.Equal(120d, draft!.ManualRafterWidthMm);
        Assert.Equal(RoofAutomaticPurlinRafterSourcePolicy.PreferManual, draft.RafterSourcePolicy);
    }

    [Fact]
    public void SameSession_SelectedThenExternalManualConfirm_ShowsImmediateConflict()
    {
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed);
        viewModel.RidgeEnabled = true;
        Assert.True(viewModel.TryApplySelectedRafterDimensions(120d, 160d));
        Assert.Equal(AutomaticPurlinRafterDimensionSource.SelectedRafter, viewModel.RafterDimensionSource);

        // External-roof pick: populate candidate only, then Confirm as ExplicitManual.
        viewModel.BeginManualDialogCadPick(120d, 160d);
        viewModel.CompleteManualDialogCadPick(
            success: true,
            pickedWidthMm: 100d,
            pickedHeightMm: 125d,
            isCurrentRoofGeneratedRafter: false);
        Assert.False(viewModel.ShouldAdoptPickedRafterAsSelected(100d, 125d));
        Assert.True(viewModel.TryApplyManualRafterDimensions(100d, 125d));

        Assert.True(viewModel.ShowRafterSourceConflictWarning);
        Assert.False(viewModel.CanApply);
        Assert.Equal(AutomaticPurlinRafterDimensionSource.ExplicitManual, viewModel.RafterDimensionSource);
        Assert.True(viewModel.HasExplicitManualRafterProfile);
        Assert.True(viewModel.ShowManualRafterFooterInfo);
        Assert.False(viewModel.PrefersCadRafterPick);
        Assert.Equal(100d, viewModel.RecoverableManualRafterWidthMm);
        Assert.Equal(125d, viewModel.RecoverableManualRafterHeightMm);
        Assert.Equal(100d, viewModel.RafterWidthMm);
        Assert.Equal(125d, viewModel.RafterHeightMm);
        Assert.Equal(120d, viewModel.ConflictActualRafterWidthMm);
        Assert.Equal(160d, viewModel.ConflictActualRafterHeightMm);
        Assert.Contains("100", viewModel.ManualRafterFooterInfoText, StringComparison.Ordinal);
        Assert.Equal(
            RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
            viewModel.DraftRafterSourcePolicy);
    }

    /// <summary>
    /// HOST two-pick workflow in ONE main-dialog session (exact reproduction numbers):
    /// Roof A generated ordinary = 100×100 → Rozmery → Vybrať → Roof B = 100×125 Confirm.
    /// Manual candidate and current-roof actual must remain independent; conflict footer
    /// must appear without reopening the ViewModel.
    /// </summary>
    [Fact]
    public void SameSession_TwoCadPicks_RoofAThenRoofB_IndependentProfilesAndConflictFooter()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("sk");
            var viewModel = CreateViewModel(
                AutomaticPurlinDialogMode.ProductionEdit,
                AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed);
            viewModel.RidgeEnabled = true;

            // --- Pick 1: Roof A current-roof generated ordinary 100×100 ---
            Assert.True(viewModel.TryApplySelectedRafterDimensions(100d, 100d));
            Assert.True(viewModel.HostActualAvailable);
            Assert.Equal(100d, viewModel.HostActualWidthMm);
            Assert.Equal(100d, viewModel.HostActualHeightMm);
            Assert.Equal(AutomaticPurlinRafterDimensionSource.SelectedRafter, viewModel.RafterDimensionSource);
            Assert.Equal(RoofAutomaticPurlinRafterSourcePolicy.PreferActual, viewModel.DraftRafterSourcePolicy);
            Assert.Equal(
                RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
                viewModel.DraftAcknowledgedActualKind);
            Assert.Equal(100d, viewModel.DraftAcknowledgedActualWidthMm);
            Assert.Equal(100d, viewModel.DraftAcknowledgedActualHeightMm);
            Assert.False(viewModel.ShowRafterSourceConflictWarning);
            Assert.False(viewModel.ShowManualRafterFooterInfo);

            var window = CreateOffscreenWindow(viewModel);
            window.Show();
            window.UpdateLayout();
            Assert.True(viewModel.PrefersCadRafterPick);
            // PreferCad must not leave a raw schematic CAD-suspend latch armed.
            Assert.False(window.IsSuspendedForRafterPick);
            Assert.False(window.IsSuspendedForManualDialogRafterPick);

            // --- Pick 2: Roof B external generated 100×125 via Vybrať production path ---
            viewModel.BeginManualDialogCadPick(100d, 100d);
            viewModel.CompleteManualDialogCadPick(
                success: true,
                pickedWidthMm: 100d,
                pickedHeightMm: 125d,
                isCurrentRoofGeneratedRafter: false);
            Assert.True(viewModel.TryConsumeManualDialogReopen(out var reopen));
            Assert.Equal(100d, reopen.SeedWidthMm);
            Assert.Equal(125d, reopen.SeedHeightMm);
            Assert.False(viewModel.ShouldAdoptPickedRafterAsSelected(100d, 125d));

            // Host actual must still be Roof A after external pick seeds (before Confirm).
            Assert.True(viewModel.HostActualAvailable);
            Assert.Equal(100d, viewModel.HostActualWidthMm);
            Assert.Equal(100d, viewModel.HostActualHeightMm);

            // Same Confirm branch as OpenManualRafterDimensionsDialog — one ViewModel session.
            Assert.True(viewModel.TryApplyManualRafterDimensions(100d, 125d));
            viewModel.ClearManualDialogSession();
            window.UpdateLayout();

            Assert.Same(viewModel, window.ViewModel);

            // Independent profiles: Roof A actual retained, Roof B is manual candidate only.
            Assert.True(viewModel.HostActualAvailable);
            Assert.Equal(100d, viewModel.HostActualWidthMm);
            Assert.Equal(100d, viewModel.HostActualHeightMm);
            Assert.Equal(100d, viewModel.RafterWidthMm);
            Assert.Equal(125d, viewModel.RafterHeightMm);
            Assert.Equal(AutomaticPurlinRafterDimensionSource.ExplicitManual, viewModel.RafterDimensionSource);
            Assert.Equal(
                RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
                viewModel.DraftRafterSourcePolicy);
            Assert.Equal(
                RoofAutomaticPurlinAcknowledgedActualKind.Unspecified,
                viewModel.DraftAcknowledgedActualKind);

            Assert.True(viewModel.ShowRafterSourceConflictWarning);
            Assert.True(viewModel.ShowManualRafterFooterInfo);
            Assert.True(window.RafterSourceConflictPanel.IsVisible);
            Assert.True(window.ResolveRafterSourceConflictButton.IsVisible);
            Assert.True(window.ManualRafterInfoText.IsVisible);
            Assert.False(viewModel.CanApply);
            Assert.Equal(100d, viewModel.ConflictActualRafterWidthMm);
            Assert.Equal(100d, viewModel.ConflictActualRafterHeightMm);
            Assert.Equal(100d, viewModel.RecoverableManualRafterWidthMm);
            Assert.Equal(125d, viewModel.RecoverableManualRafterHeightMm);

            // Apply-only persistence: conflict blocks Apply; no XDATA write without Apply.
            Assert.False(viewModel.CanApply);
            Assert.False(viewModel.TryBeginApply(out _, out _, out _));

            window.Close();
        });
    }

    [Fact]
    public void SameSession_KeepManualThenReconfirm_NoRepeatedConflict_AndDraftPersistsAck()
    {
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed);
        viewModel.RidgeEnabled = true;
        Assert.True(viewModel.TryApplySelectedRafterDimensions(100d, 100d));
        Assert.True(viewModel.TryApplyManualRafterDimensions(100d, 125d));
        Assert.True(viewModel.ShowRafterSourceConflictWarning);
        Assert.True(viewModel.TryResolveRafterSourceConflictKeepManual());
        Assert.False(viewModel.ShowRafterSourceConflictWarning);
        Assert.True(viewModel.CanApply);
        Assert.True(viewModel.TryCreateDraft(out var draft, out _));
        Assert.Equal(RoofAutomaticPurlinRafterSourcePolicy.PreferManual, draft!.RafterSourcePolicy);
        Assert.Equal(
            RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
            draft.AcknowledgedActualKind);
        Assert.Equal(100d, draft.AcknowledgedActualWidthMm);
        Assert.Equal(100d, draft.AcknowledgedActualHeightMm);

        // Same session reconfirm of the acknowledged difference must not re-warn.
        Assert.True(viewModel.TryApplyManualRafterDimensions(100d, 125d));
        Assert.False(viewModel.ShowRafterSourceConflictWarning);
        Assert.True(viewModel.CanApply);
        Assert.True(viewModel.HostActualAvailable);
        Assert.Equal(100d, viewModel.HostActualWidthMm);
        Assert.Equal(100d, viewModel.HostActualHeightMm);
    }

    [Fact]
    public void SameSession_BugPath_ExternalViaSelectedApply_WouldOverwriteHostActual()
    {
        // Documents the PreferCad schematic defect: treating Roof B as SelectedRafter
        // overwrites Roof A host actual and suppresses conflict. Production must use
        // ownership-aware manual apply instead (covered by TwoCadPicks test).
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed);
        viewModel.RidgeEnabled = true;
        Assert.True(viewModel.TryApplySelectedRafterDimensions(100d, 100d));
        Assert.Equal(100d, viewModel.HostActualHeightMm);

        Assert.True(viewModel.TryApplySelectedRafterDimensions(100d, 125d));
        Assert.Equal(125d, viewModel.HostActualHeightMm);
        Assert.False(viewModel.ShowRafterSourceConflictWarning);
        Assert.Equal(AutomaticPurlinRafterDimensionSource.SelectedRafter, viewModel.RafterDimensionSource);
    }

    [Fact]
    public void SameSession_ExternalConflict_KeepManual_EnablesApply_AndPersistsAckOnDraft()
    {
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.RecoveredRoofRecipe,
            rafterWidthMm: 120d,
            rafterHeightMm: 160d);
        viewModel.RidgeEnabled = true;
        Assert.True(viewModel.TryApplyManualRafterDimensions(100d, 125d));
        Assert.True(viewModel.ShowRafterSourceConflictWarning);

        Assert.True(viewModel.TryResolveRafterSourceConflictKeepManual());
        Assert.False(viewModel.ShowRafterSourceConflictWarning);
        Assert.True(viewModel.CanApply);
        Assert.Equal(100d, viewModel.RafterWidthMm);
        Assert.Equal(
            RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
            viewModel.DraftAcknowledgedActualKind);
        Assert.Equal(120d, viewModel.DraftAcknowledgedActualWidthMm);
        Assert.True(viewModel.TryCreateDraft(out var draft, out _));
        Assert.Equal(RoofAutomaticPurlinRafterSourcePolicy.PreferManual, draft!.RafterSourcePolicy);
        Assert.Equal(100d, draft.ManualRafterWidthMm);
        Assert.Equal(
            RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
            draft.AcknowledgedActualKind);
        Assert.Equal(120d, draft.AcknowledgedActualWidthMm);
    }

    [Fact]
    public void SameSession_ExternalPick_EditThenConfirm_UsesEditedManualCandidate()
    {
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.RecoveredRoofRecipe,
            rafterWidthMm: 120d,
            rafterHeightMm: 160d);
        viewModel.RidgeEnabled = true;
        viewModel.BeginManualDialogCadPick(120d, 160d);
        viewModel.CompleteManualDialogCadPick(
            success: true,
            pickedWidthMm: 100d,
            pickedHeightMm: 125d,
            isCurrentRoofGeneratedRafter: false);
        Assert.True(viewModel.TryApplyManualRafterDimensions(110d, 140d));

        Assert.True(viewModel.ShowRafterSourceConflictWarning);
        Assert.Equal(110d, viewModel.RecoverableManualRafterWidthMm);
        Assert.Equal(140d, viewModel.RecoverableManualRafterHeightMm);
        Assert.Equal(120d, viewModel.ConflictActualRafterWidthMm);
    }

    [Fact]
    public void SameSession_ExternalPickEscape_PreservesPriorSelectedSource()
    {
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed);
        Assert.True(viewModel.TryApplySelectedRafterDimensions(120d, 160d));
        var sourceBefore = viewModel.RafterDimensionSource;
        viewModel.BeginManualDialogCadPick(120d, 160d);
        viewModel.CompleteManualDialogCadPick(
            success: false,
            pickedWidthMm: null,
            pickedHeightMm: null,
            isCurrentRoofGeneratedRafter: false);
        Assert.True(viewModel.TryConsumeManualDialogReopen(out var reopen));
        Assert.Equal(120d, reopen.SeedWidthMm);
        Assert.Equal(160d, reopen.SeedHeightMm);
        Assert.Equal(sourceBefore, viewModel.RafterDimensionSource);
        Assert.Equal(120d, viewModel.RafterWidthMm);
        Assert.False(viewModel.ShowRafterSourceConflictWarning);
        viewModel.ClearManualDialogSession();
    }

    [Fact]
    public void SameSession_ExternalPickMatchingHostActual_BecomesExplicitManualNotSelected()
    {
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed);
        viewModel.RidgeEnabled = true;
        Assert.True(viewModel.TryApplySelectedRafterDimensions(100d, 125d));
        Assert.Equal(AutomaticPurlinRafterDimensionSource.SelectedRafter, viewModel.RafterDimensionSource);

        // Matching W×H must still reclassify to ExplicitManual (copy, not current-roof actual).
        viewModel.BeginManualDialogCadPick(100d, 125d);
        viewModel.CompleteManualDialogCadPick(
            success: true,
            pickedWidthMm: 100d,
            pickedHeightMm: 125d,
            isCurrentRoofGeneratedRafter: false);
        Assert.False(viewModel.ShouldAdoptPickedRafterAsSelected(100d, 125d));
        Assert.True(viewModel.TryApplyManualRafterDimensions(100d, 125d));

        Assert.Equal(AutomaticPurlinRafterDimensionSource.ExplicitManual, viewModel.RafterDimensionSource);
        Assert.True(viewModel.ShowManualRafterFooterInfo);
        Assert.False(viewModel.PrefersCadRafterPick);
        Assert.False(viewModel.ShowRafterSourceConflictWarning);
        Assert.True(viewModel.CanApply);
        Assert.Equal(100d, viewModel.RafterWidthMm);
        Assert.Equal(125d, viewModel.RafterHeightMm);
    }

    [Fact]
    public void SameSession_ProductionPath_ExternalConfirm_UpdatesSourceWithoutNewViewModel()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("sk");
            var viewModel = CreateViewModel(
                AutomaticPurlinDialogMode.ProductionEdit,
                AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed);
            viewModel.RidgeEnabled = true;
            Assert.True(viewModel.TryApplySelectedRafterDimensions(100d, 125d));

            var window = CreateOffscreenWindow(viewModel);
            window.Show();
            window.UpdateLayout();
            Assert.True(viewModel.PrefersCadRafterPick);
            Assert.False(window.ManualRafterInfoText.IsVisible);

            // Production Confirm path after other-roof pick (not adoptable).
            viewModel.BeginManualDialogCadPick(100d, 125d);
            viewModel.CompleteManualDialogCadPick(
                success: true,
                pickedWidthMm: 120d,
                pickedHeightMm: 160d,
                isCurrentRoofGeneratedRafter: false);
            Assert.True(viewModel.TryConsumeManualDialogReopen(out var reopen));
            Assert.Equal(120d, reopen.SeedWidthMm);

            // Same Confirm branch as AutomaticPurlinDialogWindow.OpenManualRafterDimensionsDialog.
            Assert.False(viewModel.ShouldAdoptPickedRafterAsSelected(120d, 160d));
            Assert.True(viewModel.TryApplyManualRafterDimensions(120d, 160d));
            viewModel.ClearManualDialogSession();
            window.UpdateLayout();

            // ONE session — no new ViewModel.
            Assert.Same(viewModel, window.ViewModel);
            Assert.Equal(AutomaticPurlinRafterDimensionSource.ExplicitManual, viewModel.RafterDimensionSource);
            Assert.Equal(120d, viewModel.RafterWidthMm);
            Assert.Equal(160d, viewModel.RafterHeightMm);
            Assert.True(viewModel.ShowManualRafterFooterInfo);
            Assert.True(window.ManualRafterInfoText.IsVisible);
            Assert.Contains("120", viewModel.ManualRafterFooterInfoText, StringComparison.Ordinal);
            Assert.Contains("160", viewModel.ManualRafterFooterInfoText, StringComparison.Ordinal);
            Assert.True(viewModel.ShowRafterSourceConflictWarning);
            Assert.False(viewModel.CanApply);
            Assert.Equal(100d, viewModel.ConflictActualRafterWidthMm);
            Assert.Equal(125d, viewModel.ConflictActualRafterHeightMm);
            Assert.Equal(
                RoofAutomaticPurlinRafterSourcePolicy.PreferManual,
                viewModel.DraftRafterSourcePolicy);

            window.Close();
        });
    }

    [Fact]
    public void SameSession_CurrentRoofPickConfirm_RemainsSelectedRafter()
    {
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed);
        viewModel.BeginManualDialogCadPick(80d, 160d);
        viewModel.CompleteManualDialogCadPick(
            success: true,
            pickedWidthMm: 100d,
            pickedHeightMm: 125d,
            isCurrentRoofGeneratedRafter: true);
        Assert.True(viewModel.ShouldAdoptPickedRafterAsSelected(100d, 125d));
        Assert.True(viewModel.TryApplySelectedRafterDimensions(100d, 125d));
        Assert.Equal(AutomaticPurlinRafterDimensionSource.SelectedRafter, viewModel.RafterDimensionSource);
        Assert.False(viewModel.ShowManualRafterFooterInfo);
        Assert.True(viewModel.PrefersCadRafterPick);
    }

    [Fact]
    public void SameSession_ExternalPickMatchingHostActual_NoConflictWarning()
    {
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.RecoveredRoofRecipe,
            rafterWidthMm: 120d,
            rafterHeightMm: 160d);
        viewModel.RidgeEnabled = true;
        Assert.True(viewModel.TryApplyManualRafterDimensions(120d, 160d));
        Assert.False(viewModel.ShowRafterSourceConflictWarning);
        Assert.True(viewModel.CanApply);
        Assert.Equal(AutomaticPurlinRafterDimensionSource.ExplicitManual, viewModel.RafterDimensionSource);
        Assert.Equal(
            RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
            viewModel.DraftAcknowledgedActualKind);
    }

    [Fact]
    public void SameSession_AcknowledgedDifference_ReconfirmSameManual_NoRepeatedConflict()
    {
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.RecoveredRoofRecipe,
            rafterWidthMm: 120d,
            rafterHeightMm: 160d);
        viewModel.RidgeEnabled = true;
        Assert.True(viewModel.TryApplyManualRafterDimensions(100d, 125d));
        Assert.True(viewModel.TryResolveRafterSourceConflictKeepManual());
        Assert.False(viewModel.ShowRafterSourceConflictWarning);

        Assert.True(viewModel.TryApplyManualRafterDimensions(100d, 125d));
        Assert.False(viewModel.ShowRafterSourceConflictWarning);
        Assert.True(viewModel.CanApply);
        Assert.Equal(
            RoofAutomaticPurlinAcknowledgedActualKind.ExplicitProfile,
            viewModel.DraftAcknowledgedActualKind);
    }

    [Fact]
    public void SameSession_AcknowledgedDifference_HostActualChanges_ShowsNewConflict()
    {
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.RecoveredRoofRecipe,
            rafterWidthMm: 120d,
            rafterHeightMm: 160d);
        viewModel.RidgeEnabled = true;
        Assert.True(viewModel.TryApplyManualRafterDimensions(100d, 125d));
        Assert.True(viewModel.TryResolveRafterSourceConflictKeepManual());

        // Fresh current-roof actual discovery (not SelectedRafter adoption).
        viewModel.RefreshHostActualRafterDimensions(120d, 180d);
        Assert.True(viewModel.TryApplyManualRafterDimensions(100d, 125d));
        Assert.True(viewModel.ShowRafterSourceConflictWarning);
        Assert.Equal(180d, viewModel.ConflictActualRafterHeightMm);
        Assert.False(viewModel.CanApply);
    }

    [Fact]
    public void ManualCancel_LeavesDraftUnchanged()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("en");
            var viewModel = CreateViewModel(
                AutomaticPurlinDialogMode.ProductionEdit,
                AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed);
            var window = CreateOffscreenWindow(viewModel);
            window.Show();
            window.UpdateLayout();

            var widthBefore = viewModel.RafterWidthMm;
            var heightBefore = viewModel.RafterHeightMm;
            var sourceBefore = viewModel.RafterDimensionSource;

            var dialog = new AutomaticPurlinManualRafterDialogWindow(
                widthBefore,
                heightBefore,
                CultureInfo.GetCultureInfo("en"),
                SettingsTheme.Light)
            {
                Owner = window,
            };
            dialog.Show();
            dialog.UpdateLayout();
            dialog.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(widthBefore, viewModel.RafterWidthMm);
            Assert.Equal(heightBefore, viewModel.RafterHeightMm);
            Assert.Equal(sourceBefore, viewModel.RafterDimensionSource);
            Assert.True(viewModel.RequiresManualRafterInput);
            window.Close();
        });
    }

    [Fact]
    public void ManualDialog_RejectsInvalidDimensions()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("en");
            var dialog = new AutomaticPurlinManualRafterDialogWindow(
                80d,
                160d,
                CultureInfo.GetCultureInfo("en"),
                SettingsTheme.Light);
            dialog.Show();
            dialog.UpdateLayout();
            dialog.WidthTextBox.Text = "0";
            dialog.HeightTextBox.Text = "-10";
            dialog.ConfirmButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(dialog.ValidationText.IsVisible);
            Assert.False(dialog.DialogResult == true);
            dialog.Close();
        });
    }

    [Fact]
    public void FooterAndSchematic_OpenSameManualWorkflow_WhenNoActualRafters()
    {
        RunSta(() =>
        {
            AppLanguageService.Apply("sk");
            var viewModel = CreateViewModel(
                AutomaticPurlinDialogMode.ProductionEdit,
                AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed);
            var window = CreateOffscreenWindow(viewModel);
            window.Show();
            window.UpdateLayout();

            Assert.False(viewModel.PrefersCadRafterPick);
            Assert.True(window.NoRafterWarningPanel.IsVisible);
            Assert.True(window.EnterRafterDimensionsButton.IsVisible);

            Assert.True(viewModel.TryApplyManualRafterDimensions(100d, 180d));
            window.UpdateLayout();
            Assert.False(window.NoRafterWarningPanel.IsVisible);
            Assert.True(window.ManualRafterInfoText.IsVisible);
            Assert.False(viewModel.PrefersCadRafterPick);

            // Re-edit via same apply path (schematic uses same method when PrefersCadRafterPick is false).
            Assert.True(viewModel.TryApplyManualRafterDimensions(110d, 190d));
            Assert.Equal(110d, viewModel.RafterWidthMm);
            Assert.Equal(190d, viewModel.RafterHeightMm);
            window.Close();
        });
    }

    [Fact]
    public void RecoveredOrSelectedRafter_KeepsCadPickPreference_AndNoFooterWarning()
    {
        var recovered = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.RecoveredRoofRecipe);
        Assert.True(recovered.PrefersCadRafterPick);
        Assert.False(recovered.ShowNoRafterFooterWarning);
        Assert.True(recovered.HasAuthoritativeRafterProfile);

        var unresolved = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed);
        Assert.True(unresolved.TryApplySelectedRafterDimensions(80d, 125d));
        Assert.True(unresolved.PrefersCadRafterPick);
        Assert.Equal(AutomaticPurlinRafterDimensionSource.SelectedRafter, unresolved.RafterDimensionSource);
        Assert.False(unresolved.ShowNoRafterFooterWarning);
    }

    [Fact]
    public void ManualThenSelected_DoesNotSilentlyDropManualUntilExplicitPick()
    {
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ProductionEdit,
            AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed);
        Assert.True(viewModel.TryApplyManualRafterDimensions(95d, 155d));
        Assert.Equal(AutomaticPurlinRafterDimensionSource.ExplicitManual, viewModel.RafterDimensionSource);

        // Deterministic transition: only an explicit CAD pick replaces manual in-session.
        Assert.True(viewModel.TryApplySelectedRafterDimensions(80d, 125d));
        Assert.Equal(AutomaticPurlinRafterDimensionSource.SelectedRafter, viewModel.RafterDimensionSource);
        Assert.Equal(80d, viewModel.RafterWidthMm);
        Assert.Equal(125d, viewModel.RafterHeightMm);
    }

    [Fact]
    public void IdenticalResolved125MmProfile_KeepsAcceptedTechnicalInvariants()
    {
        var viewModel = CreateViewModel(
            AutomaticPurlinDialogMode.ReadOnlyPreview,
            AutomaticPurlinRafterDimensionSource.UnresolvedProfileSeed);
        ClearIntermediateRows(viewModel);
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
            null,
            false,
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

    private static void ClearIntermediateRows(AutomaticPurlinDialogViewModel viewModel)
    {
        while (viewModel.Rows.Count > 0)
        {
            viewModel.RemoveRow(viewModel.Rows[0]);
        }
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "Manual rafter dialog test timed out.");
        Assert.Null(failure);
    }

    private sealed record SolvedFixture(
        HipRoofGeometry Geometry,
        RoofBoundaryIdentityProvenanceResult Provenance);
}
