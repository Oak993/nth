using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Capsule;
using Marrow.Testing.MultiAccountSettings;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer.MultiAccount;

// MA-9 (step 3a, OD-0115): the capsule's place in the composer's control row.
[Collection("Headless")]
public sealed class ComposerCapsuleRowTests(HeadlessSession headless)
{
    private static ThreadRuntimePart12Harness Harness()
    {
        Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        var harness = new ThreadRuntimePart12Harness();
        harness.Main.Loop.Run(() => harness.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        return harness;
    }

    private static Avalonia.Controls.Window Show(Control view, AppModel model)
    {
        var host = new Border { Child = view };
        AppEnvironment.SetAppModel(host, model);
        var window = new Avalonia.Controls.Window { Content = host, Width = 550, Height = 250 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void The_composer_row_has_no_capsule_slot_until_the_app_binds_one() => headless.OnUiThread(() =>
    {
        using var harness = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        harness.Main.OnMain(() =>
        {
            var controls = new ComposerControls(harness.Runtime, harness.Thread, false, false, false, null, () => { });
            var window = Show(controls, harness.App);
            try { Assert.Empty(controls.GetVisualDescendants().OfType<ComposerCapsuleSlot>()); }
            finally { window.Close(); }
        });
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void When_a_slot_is_offered_it_sits_just_before_the_model_chip() => headless.OnUiThread(() =>
    {
        using var harness = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var rig = new AccountsRig();
        var model = new ComposerCapsuleModel(rig.Accounts, rig.Pills, rig.IsConnected, _ => null);
        using var offer = ComposerCapsuleSeams.Offer.Override(_ => new ComposerCapsuleSlot(model, new CapsuleStyleSetting(rig.Settings), () => null));
        harness.Main.OnMain(() =>
        {
            var controls = new ComposerControls(harness.Runtime, harness.Thread, false, false, false, null, () => { });
            var window = Show(controls, harness.App);
            try
            {
                var row = (StackPanel)controls.Child!;
                var slot = Assert.Single(row.Children.OfType<ComposerCapsuleSlot>());
                Assert.Equal(row.Children.IndexOf(slot) + 1, row.Children.IndexOf(Assert.Single(row.Children.OfType<ModelEffortSlot>())));
            }
            finally { window.Close(); }
        });
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void The_apps_own_slot_stays_hidden_for_a_thread_with_one_account() => headless.OnUiThread(() =>
    {
        using var harness = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        harness.Main.OnMain(() =>
        {
            var slot = ComposerCapsuleSeams.For(harness.Runtime);
            Assert.NotNull(slot);
            var window = Show(slot!, harness.App);
            try
            {
                slot!.Refresh();
                Assert.False(slot.Shown);
            }
            finally { window.Close(); }
        });
    });
}
