using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Port.MultiAccount.Handshake;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Accounts.Pills;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Capsule;
using Marrow.Core.Port.MultiAccount.Handshake;
using Marrow.Testing.MultiAccountSettings;
using SkiaSharp;

namespace Marrow.App.Tests.Views.MultiAccount.Handshake;

// MAUI-049 owner ruling: HTML 3295-3303/3346-3348 composes the shared capsule outside the painter.
[Collection("Headless")]
public sealed class HandshakeComposedGhostReviewProofTests(HeadlessSession headless)
{
    public static IEnumerable<object[]> Looks =>
        from style in new[] { "plain" }.Concat(CapsuleStyles.Ids)
        from dark in new[] { false, true }
        from reduced in new[] { false, true }
        select new object[] { style, dark, reduced };

    [Theory, MemberData(nameof(Looks)), Trait("Rule", "R-TEST-4")]
    public void Composed_ghost_has_the_shared_capsules_exact_face_and_the_pages_placement(string style, bool dark, bool reduced)
    {
        var rig = new AccountsRig(); var a = rig.Connect(ProviderKind.Claude, "Max"); var b = rig.ConnectAnother(ProviderKind.Claude, "Max");
        var pill = rig.MakePill("Work", a, b); var rows = new FakeThreadAccountPort(rig, ProviderKind.Claude); var cards = new FakeCardPort();
        var model = new ComposerCapsuleModel(rig.Accounts, rig.Pills, rig.IsConnected, _ => new(.3, null, .2, null, false, false));
        var outgoing = new CapsuleSubject(ProviderKind.Claude, a.Id, style == "plain" ? null : new PillThreadState(pill.Id, 0, a.Id.ToString("D")), false, null);
        var incoming = new CapsuleSubject(ProviderKind.Claude, b.Id, style == "plain" ? null : new PillThreadState(pill.Id, 1, b.Id.ToString("D")), false, null);
        var content = model.For(incoming)!; ComposerCapsuleSlot? slot = null; HandshakeCapsuleHost? view = null;
        MultiAccountHarness.Run(headless, rig, () =>
        {
            var setting = new CapsuleStyleSetting(rig.Settings); if (style != "plain") setting.Value = style;
            slot = new ComposerCapsuleSlot(model, setting, () => outgoing);
            return view = new HandshakeCapsuleHost(slot, slot, rows.ThreadId, cards, () => rows.Accounts, _ => content);
        }, h =>
        {
            h.Window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light; h.Settle();
            var expected = new AccountCapsule(style) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            h.Root.Children.Add(expected); expected.Apply(content, animate: false); h.Settle();
            cards.Raise(new(rows.ThreadId, HandshakeEventKind.PhaseChanged, HandshakePhase.Idle, HandshakePhase.Standby, a.Id.ToString("D"), b.Id.ToString("D"), HandshakeReason.NearLimit, h.Time.GetUtcNow()));
            h.Step(600);
            var ghost = Assert.IsType<AccountCapsule>(view!.Ghost); var plan = view.Plan!;
            Assert.Same(view, ghost.GetVisualParent()); Assert.NotSame(slot!.Capsule, ghost);
            Assert.Equal(style, ghost.Style); Assert.Equal(content.Mono, ghost.Mono); Assert.Equal(expected.NameBelow, ghost.NameBelow);
            Assert.True(ghost.IsEffectivelyVisible); Assert.False(ghost.IsHitTestVisible); Assert.False(ghost.Face.Focusable);
            var geometry = HandshakeGeometry.Draw(230, 128, plan.VirtualMilliseconds, dark, reduced).Ghost;
            Assert.Equal(geometry.Scale, view.GhostScale, 9);
            Assert.Equal(geometry.Alpha * (reduced ? 1 : .8 + .2 * Math.Sin(plan.VirtualMilliseconds / 260)), ghost.Opacity, 9);
            var center = ghost.RenderTransform!.Value.Transform(new Point(style == "7" ? 17 : ghost.FaceSize.Width / 2, 11 + (style == "1" ? 1.5 : 0)));
            Assert.Equal((style == "7" ? 17 : view.Bounds.Width / 2) - 184 + geometry.X, center.X, 9);
            Assert.Equal(view.Bounds.Height / 2 + (style == "1" ? 1.5 : 0) - 102 + geometry.Y, center.Y, 9);
            var width = (int)Math.Ceiling(ghost.FaceSize.Width);
            using var actualPixels = Draw(ghost.Face, width); using var expectedPixels = Draw(expected.Face, width);
            Assert.Equal(expectedPixels.Bytes, actualPixels.Bytes);
            Assert.Contains(actualPixels.Pixels, pixel => pixel.Alpha > 0);
            h.Root.Children.Remove(expected);
        }, motion: true, reduceMotion: reduced);
    }

    private static SKBitmap Draw(Control face, int width)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, 22)); bitmap.Render(face);
        using var stream = new MemoryStream(); bitmap.Save(stream, new PngBitmapEncoderOptions()); stream.Position = 0;
        return SKBitmap.Decode(stream);
    }
}
