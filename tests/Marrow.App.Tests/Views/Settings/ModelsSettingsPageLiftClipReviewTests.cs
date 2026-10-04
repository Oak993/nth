using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using static Marrow.App.Tests.Fixtures.ModelsSettingsSectionRig;

namespace Marrow.App.Tests.Views.Settings;

// REVIEW (unit rec-cornerclip). ChromeCard is a fill, not a clip (Chrome.swift:1012 "The card uses a filled shape instead of a clip", :1021-1024
// `.background(RoundedRectangle(cornerRadius: Chrome.cardCornerRadius, style: .continuous).fill(...))`), and the held pinned-model row draws past the card's sides:
// ModelsSettingsPage.swift:255-262 its lift fill with `.shadow(color: .black.opacity(0.28), radius: 10, y: 4)` and `.scaleEffect(isDragged ? 1.02 : 1)` on a row as wide
// as the card. The base clip (every such Border marked ContentBeyondSides) left that drawn past the sides between the corners' run-ins; the exact outline cuts it.
[Collection("Headless")]
public sealed class ModelsSettingsPageLiftClipReviewTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-SETTINGS-187")] // ModelsSettingsPage.swift:255-262 the lifted row's fill, shadow and 1.02 scale; Chrome.swift:1012-1026 a card that does not clip
    public void REVIEW_the_held_rows_lift_and_shadow_are_drawn_past_the_cards_sides_as_with_the_base_clip() => headless.OnUiThread(() =>
    {
        var report = new List<string>();
        var failures = new List<string>();
        foreach (var scale in new[] { 1.0, 1.5 })
        {
            using var rig = new ModelsSettingsSectionRig([], model => { foreach (var id in new[] { "alpha", "beta", "gamma" }) model.Settings.AddToModelList(new ModelPin(ProviderKind.Claude, id)); });
            rig.Window.SetRenderScaling(scale);
            rig.Sync();
            var rows = Of<PinnedModelRow>(rig.Page).ToArray();
            var grip = Of<Border>(rows[1]).First(border => AutomationProperties.GetName(border) == "Drag to reorder");
            var start = grip.TranslatePoint(new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2), rig.Window)!.Value;
            rig.Window.MouseMove(start);
            rig.Window.MouseDown(start, MouseButton.Left);
            rig.Sync();
            rig.Window.MouseMove(new Point(start.X, start.Y + 4));
            rig.Sync();
            rig.Advance(1);
            rig.Sync();
            var line = ContentClipBaseDiff.Read(rig.Window, rig.Page, scale, rig.Sync, out var outside, out _);
            report.Add(line);
            if (outside > 0) failures.Add(line);
            rig.Window.MouseUp(new Point(start.X, start.Y + 4), MouseButton.Left);
            rig.Sync();
        }
        ContentClipBaseDiff.Report(report);
        Assert.True(failures.Count == 0, string.Join(" || ", failures));
    });

}
