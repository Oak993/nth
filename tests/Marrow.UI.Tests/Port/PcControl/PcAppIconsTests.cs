// PORT-ONLY: Tactus, the apps' own icons (ED-NEW-pcctl-46; DESIGN.md 4.5 "one row per app, with icon", 10.4 the approval
// body's "App icon, name and path"; canvas `tile(k,20)` and `.kap`). An app whose key gives an icon shows it at the place's
// size, drawn smoothly; any other app shows the canvas's lettered tile (its first letter on a colour its name picks, the
// corners 24 % of the side), and an app with no key or no name still gets one. The pill's rows show each app's icon at
// 20 DIP and make it again only when the app's name changes; the approval card shows it at 34 DIP. Decorative only: the
// app's name is always written next to it. An icon source on fakes.
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.PcControl;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port.PcControl;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-pcctl-46")]
public sealed class PcAppIconsTests(HeadlessSession headless)
{
    private const string ExcelKey = "exe:c:\\office\\excel.exe";

    private static Border Lettered(Control tile, double size, string letter)
    {
        var border = Assert.IsType<Border>(tile);
        Assert.Equal(size, border.Width);
        Assert.Equal(size, border.Height);
        Assert.Equal(size * 0.24, border.CornerRadius.TopLeft, 6);
        Assert.Equal(letter, Assert.IsType<TextBlock>(border.Child).Text);
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(border));
        return border;
    }

    private static Image Drawn(Control tile, double size, IImage icon)
    {
        var image = Assert.IsType<Image>(tile);
        Assert.Same(icon, image.Source);
        Assert.Equal(size, image.Width);
        Assert.Equal(size, image.Height);
        Assert.Equal(Stretch.Uniform, image.Stretch);
        Assert.Equal(BitmapInterpolationMode.HighQuality, RenderOptions.GetBitmapInterpolationMode(image));
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(image));
        return image;
    }

    [Fact]
    public void An_app_with_an_icon_shows_it_at_the_place_s_size_and_any_other_its_lettered_tile() => headless.OnUiThread(() =>
    {
        var excel = new PcFakeIcon("excel");
        var source = new PcFakeAppIcons().With(ExcelKey, excel);
        using var scope = PcAppIcons.Source.Override(source);

        Assert.Equal(ExcelKey, PcAppIcons.KeyOf("C:\\Office\\EXCEL.EXE"));
        Drawn(PcAppIcons.Tile("Excel", ExcelKey, 34), 34, excel);
        Drawn(PcAppIcons.Tile("Excel", ExcelKey, 16), 16, excel);

        // no icon for the key, no key, no name: the lettered tile, never nothing
        var outlook = Lettered(PcAppIcons.Tile("outlook", "exe:c:\\office\\outlook.exe", 20), 20, "O");
        Assert.Equal(((ISolidColorBrush)PcAppIcons.Brush("Outlook")).Color, ((ISolidColorBrush)outlook.Background!).Color);
        Assert.Equal(CardLook.TileRim, outlook.BoxShadow);
        Lettered(PcAppIcons.Tile("Paint", null, 32), 32, "P");
        Lettered(PcAppIcons.Tile("", "", 20), 20, "?");
        Assert.Equal([ExcelKey, ExcelKey, "exe:c:\\office\\outlook.exe"], source.Asked);

        // the colour is the name's own: the same for any spelling of its case, and the six colours are all used
        Assert.Equal(((ISolidColorBrush)PcAppIcons.Brush("Excel")).Color, ((ISolidColorBrush)PcAppIcons.Brush("EXCEL")).Color);
        var colours = new[] { "a", "b", "c", "d", "e", "f" }.Select(n => ((ISolidColorBrush)PcAppIcons.Brush(n)).Color).Distinct();
        Assert.Equal(6, colours.Count());
    });

    [Fact]
    public void The_pill_s_rows_show_each_app_s_icon_and_make_it_again_only_when_its_name_changes() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var excel = new PcFakeIcon("excel");
        var source = new PcFakeAppIcons().With("excel", excel);
        using var scope = PcAppIcons.Source.Override(source);
        var pill = new ControlPill(PillDriverKind.RenderThread, new RecordingPillAnimator(), new ManualTimeProvider());
        var window = new Window { Width = 900, Height = 400, Content = pill };
        window.Show();
        try
        {
            PillApp[] apps = [new("excel", "Excel", "Filling row 12 of 40"), new("outlook", "Outlook", "Writing the reply")];
            pill.Show(PillStates.Compute(new PillFacts { Apps = apps }, 10));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, pill.RowIcons.Count);
            var drawn = Drawn(pill.RowIcons[0]!, 20, excel);
            var outlook = Lettered(pill.RowIcons[1]!, 20, "O");
            Assert.Equal(2, source.Asked.Count);

            // the next step: the same icons, not made again
            pill.Show(PillStates.Compute(new PillFacts { Apps = [apps[0] with { Step = "Filling row 13 of 40" }, apps[1]] }, 11));
            Assert.Same(drawn, pill.RowIcons[0]);
            Assert.Same(outlook, pill.RowIcons[1]);
            Assert.Equal(2, source.Asked.Count);

            // the app's name is learnt later: its tile follows
            pill.Show(PillStates.Compute(new PillFacts { Apps = [apps[0], apps[1] with { Name = "Mail" }] }, 12));
            Assert.Same(drawn, pill.RowIcons[0]);
            Lettered(pill.RowIcons[1]!, 20, "M");
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void The_approval_card_shows_the_app_s_icon_at_34_DIP_next_to_its_name() => headless.OnUiThread(() =>
    {
        var outlook = new PcFakeIcon("outlook");
        const string key = "exe:c:\\program files\\microsoft office\\root\\office16\\outlook.exe";
        using var scope = PcAppIcons.Source.Override(new PcFakeAppIcons().With(key, outlook));
        var approval = new PillApprovalBody("a1", 0, "Outlook", "C:\\Program Files\\Microsoft Office\\root\\Office16\\OUTLOOK.EXE", Risky: false, UseAsked: true, AllowAlways: true);

        var shown = new ApprovalCardBody(approval with { AppKey = key });
        var image = Drawn(Assert.Single(shown.GetLogicalDescendants().OfType<Image>()), 34, outlook);
        var row = Assert.IsType<StackPanel>(image.Parent);
        Assert.Contains(row.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Outlook");

        // no key (or one with no icon): the lettered tile in its place
        var lettered = new ApprovalCardBody(approval);
        Assert.DoesNotContain(lettered.GetLogicalDescendants(), c => c is Image);
        Assert.Contains(lettered.GetLogicalDescendants().OfType<Border>(), b => b.Width == 34 && b.Child is TextBlock { Text: "O" });
    });
}
