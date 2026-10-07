using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Port.MultiAccount.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.App.Tests.Views.Settings.MultiAccount;

// Supplemental proof for MAUI-046/047, HTML 2383/4462-4466. The existing tests remain unchanged.
[Collection("Headless")]
public sealed class PillDefaultsPopupReviewProofTests(HeadlessSession headless)
{
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Floating_defaults_keep_the_row_closed_and_the_small_switch_resets_only_this_step()
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude, "Max"); var b = rig.ConnectAnother(ProviderKind.Claude, "Max");
        rig.Accounts.SetDefaults(a.Id, rig.Accounts.Get(a.Id).Defaults with { Model = "opus", Effort = "high", FastMode = true });
        var pill = rig.MakePill("Work", a, b);
        MultiAccountHarness.Run(headless, rig, () => new MultiAccountHost<PillEditorView>(c => new PillEditorView(c, c.Editor(pill.Id), () => { })), h =>
        {
            SettingsProviderPopupLayers.Install(h.Window); h.Settle();
            var view = ((MultiAccountHost<PillEditorView>)h.Page).View!; var row = view.StepList[0];
            Assert.True(h.Press("step-config-" + a.Id.ToString("D")));
            var panel = Assert.Single(h.All().OfType<StepSettingsPanel>());
            Assert.False(row.SettingsOpen); Assert.DoesNotContain(row, panel.GetVisualAncestors());
            Assert.Equal(330, panel.Width); Assert.NotNull(panel.Card);
            var toggle = h.Find<SettingsSwitch>("step-same-account-0");
            Assert.IsType<SmallSwitch>(toggle.Child); Assert.True(view.Model.Steps[0].SameAsAccount);
            toggle.Child!.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space }); h.Settle();
            Assert.False(view.Model.Steps[0].SameAsAccount);
            view.Model.SetStepSettings(0, new Marrow.Core.Accounts.StepSettings("opus", "low", false)); h.Settle();
            toggle.Child.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space }); h.Settle();
            Assert.True(view.Model.Steps[0].SameAsAccount); Assert.Equal("high", view.Model.Steps[0].Effort); Assert.True(view.Model.Steps[0].Fast);
            Assert.True(view.Model.Steps[1].SameAsAccount); Assert.Null(rig.Pills.Find(pill.Id)!.Steps[0].Effort);
            panel.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape }); h.Settle();
            Assert.Empty(h.All().OfType<StepSettingsPanel>());
        }, models: _ => [new ModelOption("opus", "Opus 5.5", efforts: new ValueList<string>(["low", "medium", "high"]), defaultEffort: "medium", fastTier: "fast")]);
    }

    [Theory, Trait("Rule", "R-TEST-4")]
    [InlineData(false)] [InlineData(true)]
    public void Two_step_refusal_finishes_after_300_ms_and_reduced_motion_only_fades(bool reduced)
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude); var b = rig.ConnectAnother(ProviderKind.Claude); var pill = rig.MakePill("Work", a, b);
        MultiAccountHarness.Run(headless, rig, () => new MultiAccountHost<PillEditorView>(c => new PillEditorView(c, c.Editor(pill.Id), () => { })), h =>
        {
            var row = ((MultiAccountHost<PillEditorView>)h.Page).View!.StepList[0];
            Assert.True(h.Press("step-remove-" + a.Id.ToString("D"))); h.Step(100);
            Assert.Equal(2, rig.Pills.Find(pill.Id)!.Steps.Count);
            if (reduced) { Assert.Null(row.RenderTransform); Assert.Equal(.6 + .4 / 3, row.Opacity, 6); }
            else Assert.Equal(-4, Assert.IsType<TranslateTransform>(row.RenderTransform).X, 6);
            h.Step(200); Assert.Null(row.RenderTransform); Assert.Equal(1, row.Opacity);
        }, motion: true, reduceMotion: reduced);
    }
}
