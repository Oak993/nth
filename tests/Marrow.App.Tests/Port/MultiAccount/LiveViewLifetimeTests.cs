using Avalonia.Controls;
using Marrow.App.Runtime;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Port.MultiAccount.Link;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount.Link;
using Marrow.Services.Providers;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port.MultiAccount;

[Collection("Headless")]
[Trait("Rule", "R-TEST-4")]
public sealed class LiveViewLifetimeTests(HeadlessSession headless)
{
    [Fact]
    public void The_production_link_host_mounts_and_suspends_its_real_model_on_detach_and_shutdown()
    {
        headless.OnUiThread(() =>
        {
            using var reader = MultiAccountRouting.LimitsReader.Override((_, _, _) => Task.FromResult<PlanLimits?>(null));
            using var rig = new ThreadRuntimeTurnRig();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            var source = rig.App.Settings.Accounts.ForService(ProviderKind.Claude)[0].Id;
            rig.App.Settings.Accounts.MarkSignedIn(source);
            var other = rig.App.Settings.Accounts.Add(ProviderKind.Claude).Id;
            rig.App.Settings.Accounts.MarkSignedIn(other);
            using var binding = new LiveViewBindingScope(rig.App);
            var factory = LinkComposerSeams.Offer.Current;
            LinkUpModel model = null!;
            using var capture = LinkComposerSeams.Offer.Override(thread => model = factory(thread)!);
            var control = LinkComposerHost.With(ComposerCapsuleSeams.For(rig.Runtime)!, rig.Runtime);
            Assert.IsType<LinkedAccountCapsule>(control);
            var changes = 0; model.Changed += () => changes++;
            var window = new Avalonia.Controls.Window { Content = control };
            try
            {
                window.Show();
                rig.App.Settings.Accounts.Rename(other, "While attached");
                Assert.True(changes > 0);
                window.Content = null;
                var detached = changes;
                rig.App.Settings.Accounts.Rename(other, "While detached");
                Assert.Equal(detached, changes);
                window.Content = control;
                rig.App.Settings.Accounts.Rename(other, "Attached again");
                Assert.True(changes > detached);
                rig.App.DisposeThreadPorts();
                var stopped = changes;
                rig.App.Settings.Accounts.Rename(other, "After shutdown");
                Assert.Equal(stopped, changes);
                Assert.Empty(rig.Session.Inputs);
            }
            finally { window.Close(); }
        });
    }
}
