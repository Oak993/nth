using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views;
using Marrow.App.Views.Chat;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views;

// Source: DroppyCode/App/Views/RootView.swift:64-68 (the backdrop is clipped to the window's 26 pt shape), UI/Chrome/Chrome.swift:8, :211-224;
// OD-0075 (owner, 2026-10-02: "the wedge is filled with the window's scrim"; settles OD-0072); ED-236 UNRESOLVED (a).
//
// The real window is a rectangle of system Acrylic (DWM rounds it by 8 px), the content a 26 pt shape: between them the blur showed without
// the scrim (luma 126 beside 93). OD-0075 fills that wedge with the window's scrim. The window here is a headless one with a known grey
// background, so a corner's colour is the scrim over the grey; what the real Acrylic shows through it is a native check.
[Collection("Headless")]
public sealed class RootViewCornerWedgeTests(HeadlessSession headless)
{
    private const int Width = 900, Height = 600;
    private static readonly Color Grey = Color.FromRgb(128, 128, 128);

    private static (AvaloniaWindow Window, RootView Root) Mount(AppModel model, ThemeVariant theme)
    {
        var host = new Border();
        AppEnvironment.SetAppModel(host, model);
        var root = new RootView();
        host.Child = root;
        var window = new AvaloniaWindow { Width = Width, Height = Height, RequestedThemeVariant = theme, Content = host };
        window.Show();
        Settle(window);
        // The window's chrome has made it clear (WindowChrome.Configure: the system's Acrylic is behind it): the known grey stands in for that blur.
        window.Background = new SolidColorBrush(Grey);
        Settle(window);
        return (window, root);
    }

    private static void Settle(AvaloniaWindow window)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }

    // Vertical stripes 8 px wide, pale then deep: a backdrop that is busy across the window and the same down every column, so a wedge pixel and the
    // body pixel under it in the same column see the same backdrop.
    internal static IBrush Stripes() => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
        EndPoint = new RelativePoint(8, 0, RelativeUnit.Absolute),
        SpreadMethod = GradientSpreadMethod.Repeat,
        GradientStops = [new GradientStop(Color.FromRgb(240, 200, 40), 0), new GradientStop(Color.FromRgb(240, 200, 40), 0.5), new GradientStop(Color.FromRgb(30, 60, 220), 0.5), new GradientStop(Color.FromRgb(30, 60, 220), 1)],
    };

    // Each wedge pixel along the top and bottom rows and on the diagonal, with the body pixel under it in the shape (OD-0075, ED-338).
    internal static CornerSeam.Pair[] Pairs() =>
        CornerSeam.Pairs(Width, Height, Chrome.WindowCornerRadius, [2, 3, 4, 5, 6, 8, 10], edgeInset: 1)
            .Concat(CornerSeam.Pairs(Width, Height, Chrome.WindowCornerRadius, [3, 4, 5, 6, 7], edgeInset: 3)).ToArray();

    // ED-NEW-gatefix4-1 / ED-NEW-gatefix4-2: OD-0147 / ED-NEW-glasslook-4 approve the foreground capsule's shadow.
    // ED-NEW-sidebarmodes-9 puts that button at (14,12), so its shadow reaches the old body samples.
    // Keep every original ED-338 seam pair and bound on the backdrop, AND pin the visible foreground.
    private static void AssertBackdropAndToolbar(AvaloniaWindow window, CornerSeam.Pair[] pairs, string when,
        (int R, int G, int B)? bareBackdrop = null)
    {
        Assert.Equal(40, pairs.Length);
        var toolbar = ((Control)window.Content!).GetVisualDescendants().OfType<EmptyPageSidebarButton>().Single();
        var wasVisible = toolbar.IsVisible;
        Assert.True(wasVisible);
        Assert.Equal(new Point(14, 12), toolbar.TranslatePoint(new Point(), window)!.Value);
        Assert.Equal(new Size(32, 32), toolbar.Bounds.Size);
        var points = pairs.SelectMany(pair => new[] { pair.Wedge, pair.Body }).ToArray();
        byte[] Read() => CornerSeam.Rgb(window, points)
            .SelectMany(colour => new[] { (byte)colour.R, (byte)colour.G, (byte)colour.B }).ToArray();
        var visible = Read();
        Assert.Equal(ApprovedToolbarPixels(when), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(visible)));
        try
        {
            toolbar.IsVisible = false;
            Settle(window);
            CornerSeam.AssertSeamless(window, pairs, when, bareBackdrop);
        }
        finally
        {
            toolbar.IsVisible = wasVisible;
            Settle(window);
        }
        Assert.Equal(visible, Read());
    }

    // Independently recorded at the approved glass-look landing 51fb3dde8f9f7726e9e0bee229c0fedee0e4dd49.
    // SHA-256 of all 40 pairs, in Pairs() order, wedge then body, RGB bytes (240 bytes per scenario).
    private static string ApprovedToolbarPixels(string when) => when switch
    {
        "light, grey backdrop" => "DD9EB5E2BBDAB15D80C10777EF9BA56CBE1EE481BF2042D0E86D062C09E5013C",
        "light, striped backdrop" => "81EEACCA5C2AE4FF095160DE1B4217F3BAD5D595CA5436DCC69FCE2035B8E219",
        "dark, grey backdrop" => "B6827186FB55C42303080CA417A183082EFB11CABC504E1DF8F9356627298026",
        "dark, striped backdrop" => "4236D6B2E6F8F01D1A83091FF2BD28DA3440CDFC37779BC84E0DC8106D625D55",
        "at 0.25 dark" => "EC1E66C082AE312B5303B5C4669D741F4FCDA2FC3E62DB187665B48D7A6FE6F2",
        "at 0.75 dark" => "A6871AE47BC4C7D17DC1A0BFF74FC0149369E8409B5DE08138126D2820BF53F6",
        "at 0.75 light" => "9572521677B1C45229AF73A29BD90BDF8900D489D5566DEE5C40E75A91998E32",
        "solid light: the window is white, corners too" => "FD93CBA7C09EA4565DB402A82ED9C29115B5C52870F6AFB8614556CC70095088",
        "solid dark: the window is black, corners too" => "36E5861CA19FC7F8AD213836A76F6C2A730D3CBCC9C71BE8E10CDC8ECDF754DB",
        _ => throw new ArgumentOutOfRangeException(nameof(when), when, "No approved corner pixels for this scenario"),
    };

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [Trait("Decision", "ED-338")] // App/Views/RootView.swift:64-68; UI/Chrome/Chrome.swift:8, :211-215; App/Window/WindowManager.swift:62-69 the main window's surface; OD-0075
    public void The_main_windows_four_corners_outside_the_26_point_curve_are_the_body_just_inside_it(bool dark, bool busy)
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                model.Settings.BackdropOpacity = 0.5;
                var (window, _) = Mount(model, dark ? ThemeVariant.Dark : ThemeVariant.Light);
                try
                {
                    if (busy) { window.Background = Stripes(); Settle(window); }
                    AssertBackdropAndToolbar(window, Pairs(), $"{(dark ? "dark" : "light")}, {(busy ? "striped" : "grey")} backdrop", busy ? null : (Grey.R, Grey.G, Grey.B));
                }
                finally { window.Close(); }
            });
        });
    }

    [Fact]
    [Trait("Decision", "ED-338")] // UI/Chrome/Chrome.swift:211-224; OD-0075
    public void The_corners_are_the_body_at_every_backdrop_opacity_in_both_themes_and_the_solid_window()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            AvaloniaWindow window = null!;
            // The model's changes reach the view on the next turn of the main actor: each step is its own turn.
            void Step(Action change, string when)
            {
                harness.Main.OnMain(change);
                harness.Main.OnMain(() => { Settle(window); AssertBackdropAndToolbar(window, Pairs(), when); });
            }
            harness.Main.OnMain(() =>
            {
                model.Settings.BackdropOpacity = 0.25;
                (window, _) = Mount(model, ThemeVariant.Dark);
            });
            try
            {
                harness.Main.OnMain(() => AssertBackdropAndToolbar(window, Pairs(), "at 0.25 dark"));
                Step(() => model.Settings.BackdropOpacity = 0.75, "at 0.75 dark");
                Step(() => window.RequestedThemeVariant = ThemeVariant.Light, "at 0.75 light");
                Step(() => model.Settings.BackdropOpacity = 1, "solid light: the window is white, corners too");
                Step(() => window.RequestedThemeVariant = ThemeVariant.Dark, "solid dark: the window is black, corners too");
            }
            finally { harness.Main.OnMain(window.Close); }
        });
    }
}
