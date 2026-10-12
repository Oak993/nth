using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Port;

[Collection("Headless")]
public sealed class C7InstallerClosingNoticeTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_shell_notice_observes_the_hold_even_when_there_is_no_active_chat()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var source = new ManualFrameSource();
            using var frames = FrameClock.Source.Override(source);
            InstallerClosingNotice notice = null!;
            Control? host = null;
            var hold = new object();
            try
            {
                harness.Main.OnMain(() =>
                {
                    Assert.Empty(model.Threads);
                    Assert.Null(model.SelectedThreadID);
                    notice = new InstallerClosingNotice();
                    host = RootViewRig.Show(model, notice, 900, 600);
                    Assert.True(notice.IsAttachedToVisualTree());
                    Assert.Same(model, AppEnvironment.GetAppModel(notice));
                    Assert.Null(notice.VisibleMessage);
                    model.HoldInstallerTurns(hold);
                    Assert.True(model.InstallerHoldsNewTurns);
                }); // drains the posted Tracked.Update before advancing the animation clock
                harness.Main.OnMain(() =>
                {
                    harness.Main.Time.Advance(TimeSpan.FromMilliseconds(200));
                    source.Advance(TimeSpan.FromMilliseconds(200));
                    RootViewRig.Layout(host!);
                    Assert.Equal("nth is closing to update", notice.VisibleMessage);
                    Assert.True(notice.IsEffectivelyVisible);
                    Assert.True(notice.Opacity > 0);
                    Assert.True(notice.Bounds.Width > 0);
                    Assert.True(notice.Bounds.Height > 0);
                    model.ReleaseInstallerTurns(hold);
                }); // drains the posted fade-out update while FrameClock remains bound
                harness.Main.OnMain(() =>
                {
                    harness.Main.Time.Advance(TimeSpan.FromMilliseconds(200));
                    source.Advance(TimeSpan.FromMilliseconds(200));
                    RootViewRig.Layout(host!);
                    Assert.Null(notice.VisibleMessage);
                    Assert.False(notice.IsVisible);
                });
            }
            finally { if (host is not null) harness.Main.OnMain(() => RootViewRig.Close(host)); }
        });
    }
}
