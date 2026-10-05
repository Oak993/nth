using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window.Native;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// PR-TERM-074 (DroppyCode/App/Views/Chat/ChatView.swift:1456-1463: `.presentationBackground { WindowBackdrop(opacity: model.settings.backdropOpacity) }`;
// DroppyCode/UI/Chrome/Chrome.swift:251-259, with no wallpaper the backdrop is `shape.fill(.clear).glassEffect(in: shape)` under the scrim). The sheet's glass
// lies over the app's own window, not the desktop (the record's windows_path: under OD-0005 "the source-defined material reproduced by the port's shader,
// not Acrylic"). The sheet opens in its window (WindowSheetLayer, so the material can read what the window drew under it) and its backdrop's
// glass is the app's material: fine black and white stripes (six pixels, the GlassWindowNativeTests pattern) in the window, under where the sheet opens,
// are frosted along a line inside the sheet above its title once the scene settles; with that glass taken off the same line shows them through the scrim
// alone, so the frost is the material's and not an opaque fill.
[Collection("Headless")]
public sealed class ChatViewScriptsSheetGlassTests(HeadlessSession headless)
{
    private static double Contrast(AvaloniaWindow window, int y, int from, int to) =>
        FramePixels.Levels(window, Enumerable.Range(from, to - from).Select(x => (x, y)).ToArray()) is var levels ? levels.Max() - levels.Min() : 0;

    [Fact]
    [Trait("Req", "PR-TERM-074")] // App/Views/Chat/ChatView.swift:1456-1463; UI/Chrome/Chrome.swift:251-259; OD-0005 (the material over app content)
    public void The_scripts_sheet_opens_in_its_window_and_its_glass_frosts_the_stripes_under_it_and_without_the_glass_the_scrim_alone_shows_them()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            void Settle()
            {
                for (var pass = 0; pass < 30; pass++)
                {
                    h.Main.Advance(TimeSpan.FromMilliseconds(16));
                    frames.Advance(TimeSpan.FromMilliseconds(16));
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Dispatcher.UIThread.RunJobs();
                }
            }
            AvaloniaWindow window = null!;
            h.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", h.NewFolder("project")).Url);
                var anchor = new Border(); // where the chat column stands; the sheet centres on it
                window = new AvaloniaWindow
                {
                    // a panel of content, as the window factory gives every window (WindowManager.MakeWindow's container)
                    Content = new Panel { Children = { new Stripes(), anchor } }, Width = 900, Height = 700,
                };
                ChatHeadlessFixtures.Track(window);
                window.Show();
                ChatGitFixtures.InvokeStatic(typeof(ScriptsMenu), "PresentScriptsEditor", model, project.Id, anchor);
            });
            h.Main.RunUntilIdle();
            Settle();
            var (sheet, backdrop, line) = h.Main.OnMain(() =>
            {
                var found = window.GetVisualDescendants().OfType<WindowBackdrop>().SingleOrDefault();
                Assert.True(found is not null, "the scripts sheet is not drawn in its window, so its glass cannot read what the window drew under it");
                var sheet = found!.GetVisualAncestors().OfType<Border>().First(border => border.ClipToBounds && border.CornerRadius == new CornerRadius(Chrome.WindowCornerRadius));
                var at = sheet.TranslatePoint(default, window)!.Value;
                Assert.Equal(new Size(580, 460), sheet.Bounds.Size); // :1721-1722, laid out whole (the appearance has finished)
                // a line 6 points below the sheet's top edge (inside its 20 point padding, above the title), clear of its 26 point corners
                var line = ((int)Math.Ceiling(at.Y + 6), (int)Math.Ceiling(at.X + 30), (int)Math.Floor(at.X + sheet.Bounds.Width - 30));
                Assert.True(Contrast(window, (int)at.Y - 20, line.Item2, line.Item3) > 0.9, "the bare stripes above the sheet are not sharp");
                var frosted = Contrast(window, line.Item1, line.Item2, line.Item3);
                Assert.True(frosted < 0.25, $"the stripes behind the scripts sheet keep {frosted:F2} of their contrast");
                Assert.Contains(sheet.GetVisualAncestors(), ancestor => ancestor is Marrow.App.Port.WindowSheetLayer); // on its window's sheet layer
                return (sheet, found, line);
            });
            h.Main.OnMain(() =>
            {
                foreach (var glass in backdrop.GetVisualDescendants().OfType<Control>().Where(control => SwiftGlass.GetEffect(control) is not null).ToArray())
                    SwiftGlass.SetEffect(glass, null);
            });
            Settle();
            h.Main.OnMain(() =>
            {
                var scrimmed = Contrast(window, line.Item1, line.Item2, line.Item3);
                Assert.True(scrimmed > 0.5, $"without the glass the sheet's line keeps only {scrimmed:F2}, so the line does not measure the glass");
                GC.KeepAlive(sheet);
            });
        });
    }
}
