using Avalonia;
using Avalonia.Controls;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts.Pills;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Capsule;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.App.Tests.Views.Composer.MultiAccount;

// MA-9 (step 3a, OD-0115): the composer's slot for the capsule: shown only with a second account of the service or a pill relay, in the style Customization picked, and it follows that pick live.
[Collection("Headless")]
public sealed class ComposerCapsuleSlotTests(HeadlessSession headless)
{
    private void Run(Action<MultiAccountHarness, CapsuleSlotScene> check, Action<CapsuleSlotScene>? setup = null, bool reduceMotion = false, bool motion = false)
    {
        var rig = new AccountsRig();
        var scene = new CapsuleSlotScene { Rig = rig, Style = new CapsuleStyleSetting(rig.Settings) };
        setup?.Invoke(scene);
        MultiAccountHarness.Run(headless, rig, () =>
        {
            var model = new ComposerCapsuleModel(rig.Accounts, rig.Pills, rig.IsConnected, id => rig.Usage.GetValueOrDefault(id));
            scene.Slot = new ComposerCapsuleSlot(model, scene.Style, () => scene.Subject, rig.Pills) { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
            return scene.Slot;
        }, h => check(h, scene), motion, reduceMotion);
    }

    private static CapsuleSubject Subject(Marrow.Core.Port.Accounts.Account account, Marrow.Core.Accounts.Pills.PillThreadState? relay = null) =>
        new(account.Service, account.Id, relay, false, null);

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void With_one_account_and_no_pill_the_slot_shows_nothing_and_takes_no_room() => Run((h, scene) =>
    {
        var only = scene.Rig.Connect(ProviderKind.Claude, "Max");
        scene.Subject = Subject(only);
        scene.Slot!.Refresh();
        h.Settle();
        Assert.False(scene.Slot.Shown);
        Assert.Null(scene.Slot.Capsule);
        Assert.Equal(0, scene.Slot.Bounds.Width);
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void A_second_connected_account_shows_the_plain_account_capsule() => Run((h, scene) =>
    {
        var first = scene.Rig.Connect(ProviderKind.Claude, "Max");
        scene.Rig.ConnectAnother(ProviderKind.Claude, "Max");
        scene.Subject = Subject(first);
        scene.Slot!.Refresh();
        h.Settle();
        Assert.True(scene.Slot.Shown);
        Assert.Equal("plain", scene.Slot.Capsule!.Style);
        Assert.Equal("M1", scene.Slot.Capsule.Mono);
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void A_pill_relay_shows_the_pill_capsule_in_merged_capsule_until_another_style_is_picked() => Run((h, scene) =>
    {
        var claude = scene.Rig.Connect(ProviderKind.Claude, "Max");
        var codex = scene.Rig.Connect(ProviderKind.Codex);
        var pill = scene.Rig.MakePill("Deep work", claude, codex);
        scene.Subject = Subject(claude, new PillThreadState(pill.Id, 0, claude.Id.ToString("D")));
        scene.Slot!.Refresh();
        h.Settle();
        Assert.Equal("1", scene.Slot.Capsule!.Style);
        Assert.Equal(2, scene.Slot.Capsule.Marks.Count);
        scene.Style.Value = "9";
        h.Settle();
        Assert.Equal("9", scene.Slot.Capsule!.Style);
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void A_style_picked_before_the_slot_exists_is_the_one_it_shows() => Run((h, scene) =>
    {
        var claude = scene.Rig.Connect(ProviderKind.Claude, "Max");
        var codex = scene.Rig.Connect(ProviderKind.Codex);
        var pill = scene.Rig.MakePill("Deep work", claude, codex);
        scene.Subject = Subject(claude, new PillThreadState(pill.Id, 1, codex.Id.ToString("D")));
        scene.Slot!.Refresh();
        h.Settle();
        Assert.Equal("5", scene.Slot.Capsule!.Style);
        Assert.Equal(1, scene.Slot.Capsule.Marks.ToList().FindIndex(m => m.Role == StepRole.Cur));
    }, setup: scene => scene.Style.Value = "5");

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void A_new_style_replaces_the_capsule_by_a_fade_and_never_cuts() => Run((h, scene) =>
    {
        var claude = scene.Rig.Connect(ProviderKind.Claude, "Max");
        var codex = scene.Rig.Connect(ProviderKind.Codex);
        var pill = scene.Rig.MakePill("Deep work", claude, codex);
        scene.Subject = Subject(claude, new PillThreadState(pill.Id, 0, claude.Id.ToString("D")));
        scene.Slot!.Refresh();
        h.Settle();
        var before = scene.Slot.Capsule!;
        scene.Style.Value = "3";
        h.Step(60);
        var during = scene.Slot.Capsule!;
        Assert.NotSame(before, during);
        Assert.InRange(during.Opacity, 0.01, 0.99);
        h.Step(400);
        Assert.Equal(1, during.Opacity, 3);
    }, motion: true);

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void Under_Reduce_Motion_a_new_style_still_crossfades_in_place() => Run((h, scene) =>
    {
        var claude = scene.Rig.Connect(ProviderKind.Claude, "Max");
        var codex = scene.Rig.Connect(ProviderKind.Codex);
        var pill = scene.Rig.MakePill("Deep work", claude, codex);
        scene.Subject = Subject(claude, new PillThreadState(pill.Id, 0, claude.Id.ToString("D")));
        scene.Slot!.Refresh();
        h.Settle();
        scene.Style.Value = "3";
        h.Step(60);
        Assert.Equal("3", scene.Slot.Capsule!.Style);
        h.Step(400);
        Assert.Equal(1, scene.Slot.Capsule.Opacity, 3);
    }, reduceMotion: true, motion: true);

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void The_slot_passes_the_capsules_click_on_and_does_nothing_with_it() => Run((h, scene) =>
    {
        var first = scene.Rig.Connect(ProviderKind.Claude, "Max");
        scene.Rig.ConnectAnother(ProviderKind.Claude, "Max");
        scene.Subject = Subject(first);
        scene.Slot!.Refresh();
        h.Settle();
        var clicks = 0;
        scene.Slot.Clicked += () => clicks++;
        scene.Slot.Capsule!.Face.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, clicks);
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void The_slot_follows_the_stores_when_asked_to_refresh_a_second_account_appears_and_goes() => Run((h, scene) =>
    {
        var first = scene.Rig.Connect(ProviderKind.Claude, "Max");
        scene.Subject = Subject(first);
        scene.Slot!.Refresh();
        h.Settle();
        Assert.False(scene.Slot.Shown);
        var second = scene.Rig.ConnectAnother(ProviderKind.Claude, "Max");
        scene.Slot.Refresh();
        h.Settle();
        Assert.True(scene.Slot.Shown);
        scene.Rig.Accounts.SetEnabled(second.Id, false);
        scene.Slot.Refresh();
        h.Settle();
        Assert.False(scene.Slot.Shown);
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void A_pill_that_changes_in_the_store_redraws_the_capsule_by_itself() => Run((h, scene) =>
    {
        var claude = scene.Rig.Connect(ProviderKind.Claude, "Max");
        var codex = scene.Rig.Connect(ProviderKind.Codex);
        var pill = scene.Rig.MakePill("Deep work", claude, codex);
        scene.Subject = Subject(claude, new PillThreadState(pill.Id, 0, claude.Id.ToString("D")));
        scene.Slot!.Refresh();
        h.Settle();
        Assert.Equal(2, scene.Slot.Capsule!.Marks.Count);
        scene.Rig.Pills.AddStep(pill.Id, new Marrow.Core.Accounts.PillStep(claude.Id.ToString("D")));
        h.Settle();
        Assert.Equal(3, scene.Slot.Capsule.Marks.Count);
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void The_handshake_flag_and_stand_by_account_set_on_the_slot_reach_the_capsule() => Run((h, scene) =>
    {
        var claude = scene.Rig.Connect(ProviderKind.Claude, "Max");
        var codex = scene.Rig.Connect(ProviderKind.Codex);
        var pill = scene.Rig.MakePill("Deep work", claude, codex);
        scene.Subject = Subject(claude, new PillThreadState(pill.Id, 0, claude.Id.ToString("D")));
        scene.Slot!.Refresh();
        h.Settle();
        scene.Slot.SetHandshake(true, codex.Id);
        h.Settle();
        Assert.True(scene.Slot.Capsule!.Marks[1].Stand);
    });
}
