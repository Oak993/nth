using Avalonia.VisualTree;
using Avalonia.Controls;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Port;
using Marrow.App.Views;
using Marrow.Core.Support;
using Marrow.Core.Port;
using Marrow.UI.Theme;
using Marrow.UI.Theme.Wallpapers;
using Marrow.UI.Port;
using Marrow.Testing;
using Xunit;

namespace Marrow.App.Tests.Port;

[Collection("Headless")]
public sealed class C7InstallerRootNoticeReviewTests(HeadlessSession headless)
{
    // ED-NEW-inst-c7-5: verify the actual root hosts the message, including the common empty-chat page.
    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_idle_quit_is_visible_in_the_actual_root_even_without_an_active_chat()
    {
        headless.OnUiThread(() =>
        {
            using var rig = new NthControlRig();
            using var wallpaper = WallpaperStore.Shared.Override((WallpaperStore)Activator.CreateInstance(typeof(WallpaperStore), nonPublic: true)!);
            using var wallpaperLibrary = WallpaperLibraryStore.Shared.Override(new WallpaperLibraryStore(UserDefaults.Standard.Current));
            var source = new ManualFrameSource();
            using var frames = FrameClock.Source.Override(source);
            RootView root = null!;
            Control? host = null;
            try
            {
                rig.H.Main.OnMain(() =>
                {
                    Assert.Empty(rig.Model.Threads);
                    Assert.Null(rig.Model.SelectedThreadID);
                    root = new RootView();
                    host = RootViewRig.Show(rig.Model, root, 900, 600);
                    var notice = Assert.Single(RootViewRig.All<InstallerClosingNotice>(root));
                    Assert.True(notice.IsAttachedToVisualTree());
                    Assert.Same(rig.Model, AppEnvironment.GetAppModel(notice));
                    var answer = rig.Host.Answer(NthControlRequest.QuitIfIdle with { Id = Guid.NewGuid().ToString("N") });
                    Assert.Equal(NthControlReply.Ok, answer.Response.Reply);
                    Assert.True(rig.Model.InstallerHoldsNewTurns);
                }); // drains the posted notice observation before advancing the animation clock
                rig.H.Main.OnMain(() =>
                {
                    rig.H.Main.Time.Advance(TimeSpan.FromMilliseconds(200));
                    source.Advance(TimeSpan.FromMilliseconds(200));
                    RootViewRig.Layout(host!);
                    var notice = Assert.Single(RootViewRig.All<TextBlock>(root), block =>
                        block.Text == "nth is closing to update" && block.IsEffectivelyVisible);
                    Assert.True(notice.Bounds.Width > 0);
                    Assert.True(RootViewRig.All<InstallerClosingNotice>(root).Single().Opacity > 0);
                    Assert.True(notice.Bounds.Height > 0);
                    Assert.Empty(rig.Recorder.Calls);
                });
            }
            finally { if (host is not null) rig.H.Main.OnMain(() => RootViewRig.Close(host)); }
        });
    }
}
