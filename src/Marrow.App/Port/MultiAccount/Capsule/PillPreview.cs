// PORT-ONLY: multi-account step 3a (OD-0115 composer=A). The live preview in a Customization card (multi-account.html .czgl and .cpv): a 58-high stage with a small composer on it at half size, the real capsule in it.
// The preview takes no pointer. It shows the page's own demo pill (Deep work, on its second step), so the cards compare like with like.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port.MultiAccount.Capsule;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.MultiAccount.Capsule;

internal sealed class PillPreview : Border
{
    /// <summary>The demo pill of the page (CAT m1, m2, ds, c1, l1; PILLS "deep"), its second step current, the thread's use 12 percent.</summary>
    internal static CapsuleContent Demo(double use = .12)
    {
        CapsuleStep[] steps = [new("M1", 0xD97757, false), new("M2", 0x8E7CFF, false), new("DS", 0x4D6BFE, false), new("C1", 0x10A37F, false), new("L1", 0x8A94A6, true)];
        var view = new CapsuleView(steps, 1, null, null, false, use, .1, "Deep work");
        return new CapsuleContent("M2", "Claude Max 2", use, false, "", view);
    }

    public PillPreview(string style)
    {
        Capsule = new AccountCapsule(style) { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
        Capsule.Apply(Demo(), animate: false);
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        var placeholder = new TextBlock { Text = "Ask Claude", FontSize = 14, Opacity = .6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };
        var send = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(6, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = .55 };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Height = 46 };
        Grid.SetColumn(Capsule, 1);
        Grid.SetColumn(send, 2);
        row.Children.Add(placeholder);
        row.Children.Add(Capsule);
        row.Children.Add(send);
        composer = new Border { Width = 320, Height = 46, CornerRadius = new CornerRadius(23), Child = row };
        var scaled = new LayoutTransformControl { LayoutTransform = new ScaleTransform(.5, .5), Child = composer, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Child = scaled;
        Height = 58;
        CornerRadius = new CornerRadius(8);
        ClipToBounds = true;
        IsHitTestVisible = false;
        stage = this;
        send.Background = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128));
        ActualThemeVariantChanged += (_, _) => Recolour();
        AttachedToVisualTree += (_, _) => Recolour();
        Recolour();
    }

    private readonly Border composer;
    private readonly Border stage;

    internal AccountCapsule Capsule { get; }

    // the page's --stage (two soft washes over a base) and the composer's glass pill, per theme
    private void Recolour()
    {
        var dark = CzTheme.Dark(this);
        var baseColour = dark ? Color.Parse("#141a22") : Color.Parse("#e4e8ee");
        var washA = dark ? Color.Parse("#24435a") : Color.Parse("#cfe0f0");
        var washB = dark ? Color.Parse("#4a3524") : Color.Parse("#efdcc8");
        stage.Background = new DrawingBrush
        {
            Drawing = new DrawingGroup
            {
                Children =
                {
                    new GeometryDrawing { Brush = new SolidColorBrush(baseColour), Geometry = new RectangleGeometry(new Rect(0, 0, 100, 100)) },
                    new GeometryDrawing { Brush = Wash(washA, .1, 0), Geometry = new RectangleGeometry(new Rect(0, 0, 100, 100)) },
                    new GeometryDrawing { Brush = Wash(washB, 1, 1), Geometry = new RectangleGeometry(new Rect(0, 0, 100, 100)) },
                },
            },
            Stretch = Stretch.Fill,
        };
        composer.Background = new SolidColorBrush(CzTheme.Token(this, "pill"));
        composer.BorderBrush = new SolidColorBrush(CzTheme.Token(this, "rim"));
        composer.BorderThickness = new Thickness(.5);
        if (composer.Child is Grid { Children: [TextBlock text, ..] }) text.Foreground = CzTheme.Text(this, true);
    }

    private static IBrush Wash(Color colour, double x, double y) => new RadialGradientBrush
    {
        Center = new RelativePoint(x, y, RelativeUnit.Relative),
        GradientOrigin = new RelativePoint(x, y, RelativeUnit.Relative),
        RadiusX = RelativeScalar.Parse("70%"),
        RadiusY = RelativeScalar.Parse("70%"),
        GradientStops = [new GradientStop(colour, 0), new GradientStop(Color.FromArgb(0, colour.R, colour.G, colour.B), 1)],
    };
}
