using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.Core.NthUi;
using Marrow.Core.Tests.Fixtures;
using Marrow.UI.NthUi;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.NthUi;

// DESIGN.md 7.2 actions and follow-ups (web surfaces W16a; ED-NEW-web-w16a-1): copy, open, export, send, context and call
// raise their effect for the host; a follow-up chip sends its words as the user's turn; a form's submit sends the form; a
// copy button says "Copied" for a moment; set and toggle change state with no effect; a hidden action does nothing.
[Collection("Headless")]
public sealed class NthUiActionsTests(HeadlessSession headless)
{
    private static List<NthUiEffect> Collect(NthUiView view)
    {
        var effects = new List<NthUiEffect>();
        view.EffectRequested += (_, e) =>
        {
            effects.Add(e.Effect);
            e.Handled = true;
        };
        return effects;
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void Copy_as_table_raises_the_table_text_and_the_button_says_copied_for_a_moment() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.CanvasHosting, culture: NthUiRig.English));
        var effects = Collect(rig.View);
        var copy = NthUiRig.Named<Button>(rig.View, "Copy as table");
        rig.Click(copy);
        var effect = Assert.Single(effects);
        Assert.Equal(NthUiEffectKind.Copy, effect.Kind);
        Assert.Contains("Total per month", effect.Text, StringComparison.Ordinal);
        Assert.Contains("$134", effect.Text, StringComparison.Ordinal);
        Assert.Contains("Copied", rig.Texts());
        rig.Run(1.3);
        Assert.Contains("Copied", rig.Texts());
        rig.Run(.2);
        Assert.DoesNotContain("Copied", rig.Texts());
        Assert.Contains("Copy as table", rig.Texts());
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void Send_and_follow_up_chips_post_their_words_as_the_users_turn() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.CanvasHosting, culture: NthUiRig.English));
        var effects = Collect(rig.View);
        rig.Click(NthUiRig.Named<Button>(rig.View, "Plan for twice the traffic"));
        rig.Click(NthUiRig.Named<Button>(rig.View, "Add a CDN"));
        Assert.Equal([(NthUiEffectKind.Send, "Plan for twice the traffic"), (NthUiEffectKind.Send, "Add a CDN")], effects.Select(e => (e.Kind, e.Text)));
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void Open_export_context_and_call_raise_their_effects_and_a_hidden_button_does_nothing() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Actions, culture: NthUiRig.English));
        var effects = Collect(rig.View);
        var promote = NthUiRig.Named<Button>(rig.View, "Promote");
        Assert.False(rig.View.SlotFor(rig.Node("button").Key)!.IsShown);
        rig.Click(promote);
        Assert.Empty(effects);
        rig.Click(NthUiRig.Named<Button>(rig.View, "Release notes"));
        rig.Click(NthUiRig.Named<Button>(rig.View, "Download the table"));
        rig.Click(NthUiRig.Named<Button>(rig.View, "release"));
        Assert.Equal([NthUiEffectKind.Open, NthUiEffectKind.Export, NthUiEffectKind.Context], effects.Select(e => e.Kind));
        Assert.Equal("https://example.com/releases/r42", effects[0].Text);
        Assert.Contains("Waiting", effects[1].Text, StringComparison.Ordinal);
        effects.Clear();
        rig.Click(rig.ViewOf<NthUiToggleView>().Switch);
        rig.Settle();
        Assert.True(rig.View.SlotFor(rig.Node("button").Key)!.IsShown);
        Assert.Contains("Approved", rig.TextsOf("table"));
        rig.Click(promote);
        var call = Assert.Single(effects);
        Assert.Equal((NthUiEffectKind.Call, "shipyard", "promote"), (call.Kind, call.Server, call.Tool));
        Assert.False(rig.View.Runtime.Get("approved").IsTruthy);
        Assert.Contains("Waiting", rig.TextsOf("table"));
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void A_forms_submit_sends_the_filled_form() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.Form, culture: NthUiRig.English));
        var effects = Collect(rig.View);
        rig.ViewOf<NthUiTextFieldView>().Box.Text = "Ada";
        rig.Click(NthUiRig.Named<Button>(rig.View, "Request booking"));
        var send = Assert.Single(effects);
        Assert.Equal(NthUiEffectKind.Send, send.Kind);
        Assert.Contains("Ada", send.Text, StringComparison.Ordinal);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void The_copy_feedback_shows_in_a_view_with_no_frame_clock() => headless.OnUiThread(() =>
    {
        var view = NthUiView.Show("copy \"Copy the code\" value=\"npm run dev\"", culture: NthUiRig.English);
        var window = new Window { Width = 300, Height = 200, Content = view };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var effects = Collect(view);
            var copy = NthUiRig.Named<Button>(view, "Copy the code");
            copy.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("npm run dev", Assert.Single(effects).Text);
            Assert.Contains(copy.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Copied");
        }
        finally
        {
            window.Close();
        }
    });
}
