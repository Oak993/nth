using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Tests.Port;

// OD-0088 (ED-250): the Look card at rest against the locked board's settled stills (SPEC 8: canvas-frames/<appearance>/
// rest-base.png and rest-named.png, copied to tests/fixtures/accent/), dark and light. Each still is the 782 x 581 Settings
// window at 1:1 with the card at (237, 89). The port's card is drawn at the same place over the still's own backdrop
// (rebuilt row by row from the window colour either side of the card, since the board's glass stands in for the port's,
// SPEC 3.3), and compared pixel by pixel. The type differs by ruling (the port draws Inter, OD-0009; the board Segoe UI
// Variable), so the text-free parts are held to the board exactly and the whole card is measured and reported.
// LEAD RULING (rec-linebox, 2026-10-04, ED-316 (6)): the card's two rows are ChromeRows, 58 tall as the Mac draws them
// (ED-290 (1), S 23.5 and S 194.0), where the board drew them 54; the board's other geometry is kept. So the card is cropped at its own
// height (245 shut, 281 open) and compared with the still row by row: the mini window, each row and the fold are each read from their own
// top in the port and in the board, so a row's content is held at the board's offset from that row's top and only the 4 extra points at
// each row's foot are not compared.
[Collection("Headless")]
public sealed class LookCardRenderTests(HeadlessSession headless) : LookCardFixture(headless)
{
    private const int CardX = 237, CardY = 89, CardWidth = 520;

    private static string Fixture(string name) => StrikeFixtures.FixturePath("accent", name);

    private static (byte[] Pixels, int Width, int Height) Read(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        var stride = size.Width * 4;
        var bytes = new byte[stride * size.Height];
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(size), pin.AddrOfPinnedObject(), bytes.Length, stride); }
        finally { pin.Free(); }
        if (bitmap.Format == PixelFormat.Bgra8888)
            for (var i = 0; i < bytes.Length; i += 4) (bytes[i], bytes[i + 2]) = (bytes[i + 2], bytes[i]);
        return (bytes, size.Width, size.Height);
    }

    private static (byte R, byte G, byte B) At((byte[] Pixels, int Width, int Height) image, int x, int y)
    {
        var i = (y * image.Width + x) * 4;
        return (image.Pixels[i], image.Pixels[i + 1], image.Pixels[i + 2]);
    }

    // The still's window backdrop under the card: each row's colour at x 230 (left of the card) and x 770 (right of it),
    // interpolated across.
    private static WriteableBitmap Backdrop((byte[] Pixels, int Width, int Height) still)
    {
        var bitmap = new WriteableBitmap(new PixelSize(still.Width, still.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Premul);
        using var buffer = bitmap.Lock();
        var row = new byte[buffer.RowBytes];
        for (var y = 0; y < still.Height; y++)
        {
            var (l, r) = (At(still, 230, y), At(still, 770, y));
            for (var x = 0; x < still.Width; x++)
            {
                var t = Math.Clamp((x - 230) / 540.0, 0, 1);
                row[x * 4] = (byte)Math.Round(l.R + (r.R - l.R) * t);
                row[x * 4 + 1] = (byte)Math.Round(l.G + (r.G - l.G) * t);
                row[x * 4 + 2] = (byte)Math.Round(l.B + (r.B - l.B) * t);
                row[x * 4 + 3] = 255;
            }
            Marshal.Copy(row, 0, buffer.Address + y * buffer.RowBytes, buffer.RowBytes);
        }
        return bitmap;
    }

    private void Compare(string appearance, string still, AppTheme theme, bool open)
    {
        var dark = appearance == "dark";
        (byte[] Pixels, int Width, int Height) reference = default;
        AppSettings? settings = null;
        LookCard? card = null;
        IDisposable? accent = null;
        With(() =>
        {
            reference = Read(new Bitmap(Fixture($"{appearance}-{still}.png")));
            accent = SwiftColor.AccentSource.Override(() => Color.Parse("#0067C0")); // the board's preview of the Windows accent
            card = new LookCard(settings!);
            var scope = new ThemeVariantScope { RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light, Width = CardWidth, Child = card };
            Canvas.SetLeft(scope, CardX);
            Canvas.SetTop(scope, CardY);
            return new Canvas { Width = reference.Width, Height = reference.Height, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Background = new ImageBrush(Backdrop(reference)), Children = { scope } };
        }, (view, model, h, w) =>
        {
            try
            {
                if (open) { card!.ToggleFold(); }
                Settle(h, w);
                using var frame = w.CaptureRenderedFrame()!;
                var port = Read(frame);
                var origin = view.TranslatePoint(default, w)!.Value; // SettingsRestFixture's host padding, 40
                Assert.Equal(new Point(40, 40), origin);
                const int Origin = 40;
                var height = (int)Math.Round(card!.Bounds.Height);
                (int X, int Y) P(int x, int y) => (Origin + x, Origin + y);
                // Regions in window coordinates (the board's), the card's parts found in the port's own layout.
                var mini = In(card.Mini, card);
                var line = In(card.Classic, card);
                // (name, x, the board's y, the port's y, width, height): the board's rows are 54, the port's 58 (LEAD RULING above).
                const int BoardRow = 54;
                var themeTop = (int)Math.Round(In(card.ThemeRow, card).Y); var accentTop = (int)Math.Round(In(card.AccentRow, card).Y);
                Assert.Equal((int)Math.Round(128 + 2 * RowHeight(card!) + 1 + (open ? 36 : 0)), height); // the card follows its content: 128 + row + 1 + row (+ 36); 245 and 281 with the 58-point rows, taller at the Settings sizes (ED-NEW-textsize-2)
                var regions = new List<(string Name, int X, int BoardY, int PortY, int W, int H)>
                {
                    ("mini window", CardX + 12, CardY, CardY, 496, 128),
                    ("theme row", CardX, CardY + 128, CardY + themeTop, CardWidth, BoardRow),
                    ("accent row", CardX, CardY + 128 + BoardRow + 1, CardY + accentTop, CardWidth, BoardRow),
                };
                if (open) regions.Add(("fold", CardX, CardY + 128 + 2 * BoardRow + 1, CardY + accentTop + (int)Math.Round(card.AccentRow.Bounds.Height), CardWidth, 36));
                var metrics = new Dictionary<string, object>();
                var means = new Dictionary<string, double>();
                foreach (var region in regions)
                {
                    double sum = 0; var within8 = 0; var n = 0;
                    for (var dy = 0; dy < region.H; dy++)
                        for (var x = region.X; x < region.X + region.W; x++)
                        {
                            var a = At(reference, x, region.BoardY + dy);
                            var (px, py) = P(x, region.PortY + dy);
                            var b = At(port, px, py);
                            var d = (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B)) / 3.0;
                            sum += d; n++; if (d <= 8) within8++;
                        }
                    means[region.Name] = sum / n;
                    metrics[region.Name] = new { meanAbs = Math.Round(sum / n, 2), within8 = Math.Round(100.0 * within8 / n, 2) };
                }
                // The swatch dots: the colour a little left of each centre (clear of the Windows dot), the board's centre at
                // x 436 + 26 i + 12 and y 287 + 12 (Signature 326 + 12), the port's from its own layout.
                var dots = new List<object>();
                var worst = 0;
                void Dot(SwatchLine swatches, int i, int boardX, int boardY)
                {
                    var s = swatches.Swatches[i];
                    var c = s.TranslatePoint(new Point(12, 12), card)!.Value;
                    var (px, py) = P(CardX + (int)Math.Floor(c.X) - 5, CardY + (int)Math.Floor(c.Y));
                    var b = At(port, px, py);
                    var a = At(reference, boardX - 5, boardY);
                    var d = Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
                    worst = Math.Max(worst, d);
                    dots.Add(new { i = s.Index, board = $"#{a.R:X2}{a.G:X2}{a.B:X2}", port = $"#{b.R:X2}{b.G:X2}{b.B:X2}", d, boardCentre = new[] { boardX, boardY }, portCentre = new[] { CardX + c.X, CardY + c.Y } });
                }
                for (var i = 0; i < 10; i++) Dot(card.Classic, i, 436 + 26 * i + 12, 287 + 12);
                if (open) for (var j = 0; j < 12; j++) Dot(card.Signature, j, 436 + 26 * j + 12, 326 + 12);
                metrics["dots"] = dots;
                metrics["card"] = new { meanAbs = Math.Round(regions.Sum(r => means[r.Name] * r.W * r.H) / regions.Sum(r => (double)r.W * r.H), 2) }; // the rows together
                metrics["worstDot"] = worst;
                metrics["geometry"] = new
                {
                    card = new[] { CardX, CardY, CardWidth, height },
                    mini = new[] { CardX + mini.X, CardY + mini.Y, mini.Width, mini.Height },
                    classicLine = new[] { CardX + line.X, CardY + line.Y, line.Width, line.Height },
                    segments = new[] { CardX + In(card.Segments, card).X, CardY + In(card.Segments, card).Y, card.Segments.Bounds.Width, card.Segments.Bounds.Height },
                    more = new[] { CardX + In(card.More, card).X, CardY + In(card.More, card).Y, card.More.Bounds.Width, card.More.Bounds.Height },
                };
                var folder = Path.Combine(Path.GetTempPath(), "marrow-accent-render");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, $"{appearance}-{still}.json"), JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true }));
                using (var stream = File.Create(Path.Combine(folder, $"{appearance}-{still}-port.png"))) frame.Save(stream, new PngBitmapEncoderOptions());
                var summary = JsonSerializer.Serialize(metrics);

                // The mini window is the board's (no text in it): every element, colour and edge.
                Assert.Equal(new Rect(12, 0, 496, 128), mini);
                Assert.True(means["mini window"] <= 2.5, $"{appearance} {still}: mini window differs: {summary}");
                // Its landmarks, pixel for pixel (mini window coordinates, SPEC 2.2): the accent bar, the bubble, the tube's fill,
                // the send button, the current sidebar item, the composer's placeholder bar.
                var landmarks = new (string Name, int X, int Y)[] { ("accent bar", 126 + 125 + 39, 56), ("bubble", 405, 21), ("tube fill", 400, 101), ("send", 460, 95), ("current item", 100, 39), ("placeholder", 160, 100) };
                var landmarkWorst = 0;
                var marks = new List<object>();
                foreach (var (name, x, y) in landmarks)
                {
                    var a = At(reference, CardX + 12 + x, CardY + y);
                    var (px, py) = P(CardX + 12 + x, CardY + y);
                    var b = At(port, px, py);
                    var d = Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
                    landmarkWorst = Math.Max(landmarkWorst, d);
                    marks.Add(new { name, board = $"#{a.R:X2}{a.G:X2}{a.B:X2}", port = $"#{b.R:X2}{b.G:X2}{b.B:X2}", d });
                }
                metrics["landmarks"] = marks;
                summary = JsonSerializer.Serialize(metrics);
                File.WriteAllText(Path.Combine(folder, $"{appearance}-{still}.json"), JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true }));
                Assert.True(landmarkWorst <= 3, $"{appearance} {still}: a mini window landmark differs: {summary}");
                // Every swatch dot is the board's colour.
                Assert.True(worst <= 3, $"{appearance} {still}: a swatch dot differs: {summary}");
                // Each row's content at the board's offset from its own row top (SPEC 2.3 the segments 13, SPEC 2.5 the Classic line 15), the
                // Signature line at the fold's top (SPEC 2.6), not moved by the taller rows.
                Assert.Equal(13, In(card.Segments, card).Y - themeTop);
                Assert.Equal(15, line.Y - accentTop);
                if (open) Assert.Equal(accentTop + card.AccentRow.Bounds.Height, In(card.Signature, card).Y);
                // The swatch line: 310 x 24, its right edge at the card's right less the port's row padding (10).
                Assert.Equal(new Rect(CardWidth - 10 - 310, line.Y, 310, 24), line);
            }
            finally { accent?.Dispose(); }
        }, seed: model =>
        {
            settings = model.Settings;
            model.Settings.Theme = theme;
            model.Settings.Accent = "windows";
        }, height: 700);
    }

    [Theory]
    [InlineData("dark")]
    [InlineData("light")]
    [Trait("Decision", "ED-250")]
    public void The_card_at_rest_on_the_base_theme_matches_the_board(string appearance) =>
        Compare(appearance, "rest-base", appearance == "dark" ? AppTheme.Dark : AppTheme.Light, open: false);

    [Theory]
    [InlineData("dark")]
    [InlineData("light")]
    [Trait("Decision", "ED-250")]
    public void The_card_at_rest_on_a_named_theme_with_the_line_open_matches_the_board(string appearance) =>
        Compare(appearance, "rest-named", appearance == "dark" ? AppTheme.Dracula : AppTheme.CatppuccinLatte, open: true);
}
