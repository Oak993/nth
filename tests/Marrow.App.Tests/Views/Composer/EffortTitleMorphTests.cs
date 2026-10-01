using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// EffortSlider.swift:573-574 Text(verbatim: title).contentTransition(.numericText()); :631 the slider writes the effort
// inside withAnimation(.snappy(duration: 0.2)). The morph itself is MorphingTextBlockTests'.
[Collection("Headless")]
public sealed class EffortTitleMorphTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-COMPOSER-111")]
    [Trait("Req", "UI-COMPOSER-031")]
    public void The_title_morphs_when_the_effort_changes_inside_an_animation_and_changes_at_once_outside_one() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var effort = new ObservableStringBox { Value = "high" };
        var card = new EffortSliderCard("Sonnet", ProviderKind.Claude, new ValueList<string>(["low", "medium", "high", "max"]), "medium", false,
            effort.AsBinding(), Binding<bool>.Constant(false), () => { });
        var window = new Avalonia.Controls.Window { Width = 360, Height = 200, Content = card };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var title = Assert.Single(card.GetVisualDescendants().OfType<MorphingTextBlock>());
            Assert.Equal("High", title.Text);

            Transaction.WithAnimation(Animation.Snappy(duration: 0.2), () => effort.Value = "max");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Max", title.Text);
            Assert.True(title.IsMorphing); // the change came inside the slider's animation

            effort.Value = "low";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Low", title.Text);
            Assert.False(title.IsMorphing); // no transaction: the text simply changes
        }
        finally { window.Close(); }
    });
}
