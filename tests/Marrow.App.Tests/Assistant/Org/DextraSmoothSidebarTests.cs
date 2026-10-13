using Avalonia.Automation;
using Avalonia.VisualTree;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port.Armilla;

namespace Marrow.App.Tests.Assistant.Org;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-dextra-smooth-6")]
public sealed class DextraSmoothSidebarTests(HeadlessSession headless)
{
    [Fact]
    public void Live_sidebar_updates_keep_rows_attached_and_reorder_only_when_needed() => headless.OnUiThread(() =>
    {
        using var still = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
        var group = new MissionChatsGroup(collapsed: () => false);
        var first = new TeamChat(Guid.NewGuid(), Guid.NewGuid(), "Mission A", MissionState.Working, "One");
        var second = new TeamChat(Guid.NewGuid(), Guid.NewGuid(), "Mission B", MissionState.Working, "Two");
        group.Show([first, second]);
        using var stage = new HomeStage(group.Root);
        var buttons = group.Buttons;
        var detached = 0;
        foreach (var button in buttons) button.DetachedFromVisualTree += (_, _) => detached++;
        for (var i = 0; i < 20; i++) group.Show([first with { Hover = "Progress " + i, Title = "Mission A " + i }, second]);
        Assert.True(detached == 0, $"unchanged-order row detachments={detached} over 20 updates");
        Assert.Equal((first with { Title = "Mission A 19" }).Reading, AutomationProperties.GetName(buttons[0]));
        Assert.Equal("Progress 19", buttons[0].GetVisualDescendants().OfType<ArmillaMark>().Single().Activity);
        group.Show([second, first]);
        Assert.Equal([buttons[1], buttons[0]], group.Buttons);
        group.Show([first]);
        Assert.Single(group.Buttons);
        Assert.Same(buttons[0], group.Buttons[0]);
    });
}
