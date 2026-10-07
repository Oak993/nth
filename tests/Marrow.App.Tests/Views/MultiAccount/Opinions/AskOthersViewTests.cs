using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Marrow.App.Port.MultiAccount.Opinions;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.MultiAccount.Opinions;
using Marrow.Core.Models;
namespace Marrow.App.Tests.Views.MultiAccount.Opinions;
[Collection("Headless")]
public sealed class AskOthersViewTests(HeadlessSession headless)
{
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Open_list_updates_visible_account_name_and_model_after_the_port_changes()
    {
        var rig = new OpinionViewRig(); using var model = rig.Model(); AskOthersView? view = null;
        MultiAccountHarness.Run(headless, rig.Accounts, () => view = new(model), h =>
        {
            SettingsProviderPopupLayers.Install(h.Window); h.Settle(); h.Press("ask-others-button");
            rig.Accounts.Accounts.Rename(rig.Other, "Checker");
            rig.Accounts.Accounts.SetDefaults(rig.Other, new() { Model = "Sonnet 5.5" }); rig.Current.Refresh(); h.Settle();
            Assert.Contains(h.Words(view!.List), t => t.Contains("Checker", StringComparison.Ordinal));
            Assert.Contains("Sonnet 5.5", h.Words(view.List));
            Assert.DoesNotContain("Opus 5.5", h.Words(view.List));
        });
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Chips_render_each_selected_monogram_as_its_own_mark_with_the_page_separator()
    {
        var rig = new OpinionViewRig(); using var model = rig.Model(); AskOthersView? view = null;
        MultiAccountHarness.Run(headless, rig.Accounts, () => view = new(model), h =>
        {
            model.Toggle(rig.Other); model.Toggle(rig.Cross); h.Settle();
            Assert.Equal(new[] { "Ask also: ", "M2", "·", "C1" }, h.Words(view!.Chips));
            Assert.Equal("Ask also: M2·C1", view.Chips.TextShown);
        });
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Composer_seam_mounts_button_and_chips_in_the_actual_compose_box()
    {
        headless.OnUiThread(() =>
        {
            var accounts = new OpinionViewRig(); using var model = accounts.Model();
            using var rig = new VoiceComposerRig();
            using var offer = AskOthersComposerSeams.Offer.Override(id => id == rig.Runtime.ThreadID ? model : null);
            var (composer, window) = rig.Open();
            try
            {
                rig.Main.OnMain(() => model.Toggle(accounts.Other)); rig.PumpWindow(window);
                rig.Main.OnMain(() =>
                {
                    var mounted = Assert.Single(composer.GetVisualDescendants().OfType<AskOthersView>());
                    Assert.Equal("Ask also: M2", mounted.Chips.TextShown);
                    Assert.Contains(mounted.Chips, composer.GetVisualDescendants());
                    Assert.True(mounted.Button.Bounds.Width > 0);
                });
            }
            finally { rig.Close(window); }
        });
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Button_opens_ordered_glass_list_and_all_updates_chips_without_typing_at()
    {
        var rig = new OpinionViewRig(); using var model = rig.Model(); AskOthersView? view = null;
        MultiAccountHarness.Run(headless, rig.Accounts, () => view = new(model), h =>
        {
            SettingsProviderPopupLayers.Install(h.Window); h.Settle();
            Assert.Equal("Ask others for this message", AutomationProperties.GetName(view!.Button));
            Assert.Equal("Ask others for this message", ToolTip.GetTip(view.Button));
            Assert.True(h.Press("ask-others-button"));
            Assert.True(view.ListPopup.IsOpen); Assert.Contains("Also ask, for this message only", h.Words());
            Assert.Equal(new[] { rig.Other, rig.Cross }, view.List!.AccountIds);
            Assert.Contains("Claude Max 2 / Opus 5.5", view.List.RowLabels);
            Assert.Contains("@all", h.Words()); Assert.Contains("Everyone with room", h.Words());
            h.Press("ask-others-all"); Assert.Equal("Ask also: M2·C1", view.Chips.TextShown);
            h.Press("ask-others-all"); Assert.Equal("", view.Chips.TextShown);
            view.List.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape }); h.Settle();
            Assert.False(view.ListPopup.IsOpen);
        });
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Sending_same_service_records_ids_clears_chips_and_posts_note()
    {
        var rig = new OpinionViewRig(); using var model = rig.Model(); AskOthersView? view = null;
        MultiAccountHarness.Run(headless, rig.Accounts, () => view = new(model), h =>
        {
            model.Toggle(rig.Other); h.Settle();
            Assert.Equal(AskSendResult.Sent, view!.SendAsync("Review").GetAwaiter().GetResult()); h.Settle();
            Assert.Equal(new[] { rig.Other }, Assert.Single(rig.Port.Asked).Accounts);
            Assert.Equal("", view.Chips.TextShown); Assert.Equal("Also asked: M2", view.LastAskedNote);
        });
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Other_service_confirmation_defaults_unchecked_and_cancel_never_sends()
    {
        var rig = new OpinionViewRig(); using var model = rig.Model(); AskOthersView? view = null;
        MultiAccountHarness.Run(headless, rig.Accounts, () => view = new(model), h =>
        {
            SettingsProviderPopupLayers.Install(h.Window); model.Toggle(rig.Cross); h.Settle();
            Assert.Equal(AskSendResult.NeedsConfirmation, view!.SendAsync("Review").GetAwaiter().GetResult()); h.Settle();
            Assert.Empty(rig.Port.Asked); Assert.True(view.ConfirmPopup.IsOpen);
            Assert.False(view.Confirmation!.DontAskAgain.IsChecked ?? false);
            Assert.Contains("Codex 1 is another service: it will see this conversation.", h.Words());
            h.Press("other-service-cancel"); Assert.Empty(rig.Port.Asked); Assert.False(view.ConfirmPopup.IsOpen);
            Assert.False(rig.Consent.Permits(ConsentPurpose.Opinions, ProviderKind.Codex));
        });
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Checkbox_keyboard_and_accept_send_once_and_remember_only_explicit_choice()
    {
        var rig = new OpinionViewRig(); using var model = rig.Model(); AskOthersView? view = null;
        MultiAccountHarness.Run(headless, rig.Accounts, () => view = new(model), h =>
        {
            SettingsProviderPopupLayers.Install(h.Window); model.Toggle(rig.Cross);
            view!.SendAsync("Review").GetAwaiter().GetResult(); h.Settle();
            view.Confirmation!.DontAskAgain.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space }); h.Settle();
            Assert.True(view.Confirmation.DontAskAgain.IsChecked); h.Press("other-service-accept"); h.Settle();
            Assert.Single(rig.Port.Asked); Assert.True(rig.Consent.Permits(ConsentPurpose.Opinions, ProviderKind.Codex));
            Assert.Null(view.Confirmation); Assert.Equal("Also asked: C1", view.LastAskedNote);
        });
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void One_account_hides_button_and_out_row_stays_disabled()
    {
        var rig = new OpinionViewRig(); rig.Accounts.Usage[rig.Other] = new(1, null, null, null, false, false);
        rig.Accounts.Accounts.SetEnabled(rig.Cross, false); using var model = rig.Model(); AskOthersView? view = null;
        MultiAccountHarness.Run(headless, rig.Accounts, () => view = new(model), h =>
        {
            SettingsProviderPopupLayers.Install(h.Window); h.Settle(); h.Press("ask-others-button");
            Assert.False(h.Find<Button>("ask-others-" + rig.Other.ToString("N")).IsEnabled);
            rig.Accounts.Accounts.SetEnabled(rig.Other, false); rig.Current.Refresh(); h.Settle();
            Assert.False(view!.Button.IsEffectivelyVisible); Assert.False(view.ListPopup.IsOpen);
        }, reduceMotion: true);
    }
}
