using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Terminal;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-review-8")]
public sealed class WebReviewTerminalTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Restoring_or_resetting_during_collapse_keeps_the_terminal_open(bool reset)
    {
        TerminalPanelRig.Run(headless, rig =>
        {
            var frames = new QuietFrameSource();
            var time = new ManualTimeProvider();
            using var source = FrameClock.Source.Override(frames);
            using var clock = SwiftClock.Provider.Override(time);
            rig.Step(() => rig.Runtime.IsTerminalVisible = true);
            var (_, panel, _) = rig.Show(TerminalPanelRig.Folder("review interrupted collapse"));
            var target = panel.ResizeSplitter.Target;
            rig.Step(() => target.Collapse(true));
            Assert.True(rig.Runtime.IsTerminalVisible);
            rig.Step(() => { if (reset) target.Reset(); else target.Collapse(false); });
            for (var i = 0; i < 200; i++)
            {
                time.Advance(TimeSpan.FromSeconds(1.0 / 60));
                frames.Advance(TimeSpan.FromSeconds(1.0 / 60));
                Dispatcher.UIThread.RunJobs();
            }
            Assert.True(rig.Runtime.IsTerminalVisible);
            Assert.Equal(260, rig.App.Settings.TerminalHeight);
        });
    }
}
