using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Common;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using static Marrow.App.Tests.Views.Sidebar.SidebarViewTests;

namespace Marrow.App.Tests.Views.Sidebar;

// Source: DroppyCode/App/Views/Sidebar/SidebarView.swift:2408-2427 ActivityStatus: `:2414 approvals || questions` -> the hand,
// `:2418 isRunning || isHydraMerging || hasWorkingHelpers` -> the spinner. The earlier tests drove the merge branch and the
// approval branch only (UI-SIDEBAR-024); each remaining term of the two conditions is proven here on its own.
[Collection("Headless")]
public sealed class SidebarActivityStatusTests(HeadlessSession headless)
{
    private static QuestionRequest Question() =>
        new("q1", new ValueList<QuestionRequest.Question>([new("a", "Header", "Which one?", ValueList<QuestionRequest.Choice>.Empty, false, false, false)]));

    [Theory]
    [Trait("Req", "UI-SIDEBAR-024")] // :2418 isRunning; :2418 hasWorkingHelpers; :2414 questions with no approval
    [InlineData("running")]
    [InlineData("working helper")]
    [InlineData("pending question")]
    public void The_trailing_status_shows_the_spinner_for_a_running_turn_or_a_working_helper_and_the_hand_for_a_question_alone(string state) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var helper = NewThread(project, "Helper", 1) with { ParentThreadID = thread.Id };
        var model = Model(harness, [project], [thread, helper]);
        harness.Main.OnMain(() =>
        {
            Control Status() => Hosted(model, (Control)Activator.CreateInstance(Private("ActivityStatus"), thread)!);
            Assert.Empty(LogicalQuery.SelfAndDescendants<Marrow.UI.Port.Nth.NthSpinner>(Status())); // idle: nothing, to tell each state from the quiet row
            switch (state)
            {
                case "running":
                    typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Phase))!.SetValue(model.Runtime(thread.Id), RuntimePhase.Running);
                    Assert.False(model.Runtime(thread.Id).IsHydraMerging); // the merge flag is not what shows it
                    break;
                case "working helper":
                    typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Phase))!.SetValue(model.Runtime(helper.Id), RuntimePhase.Running);
                    Assert.False(model.Runtime(thread.Id).IsRunning); // the thread itself is idle, only its helper works
                    Assert.True(model.Runtime(thread.Id).HasWorkingHelpers);
                    break;
                default:
                    typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Questions))!.SetValue(model.Runtime(thread.Id), new ValueList<QuestionRequest>([Question()]));
                    Assert.Empty(model.Runtime(thread.Id).Approvals); // a question, not an approval
                    break;
            }
            var status = Status();
            if (state == "pending question")
            {
                Assert.Empty(LogicalQuery.SelfAndDescendants<Marrow.UI.Port.Nth.NthSpinner>(status));
                var hand = LogicalQuery.SelfAndDescendants<SwiftImage>(status).Single();
                Assert.Equal("hand.raised.fill", hand.SymbolName); // :2415
                Assert.Equal(Chrome.Warning.Resolve(hand.ActualThemeVariant), ((ISolidColorBrush)hand.Foreground!).Color); // :2417
            }
            else
            {
                Assert.Single(LogicalQuery.SelfAndDescendants<Marrow.UI.Port.Nth.NthSpinner>(status)); // :2419
                Assert.Empty(LogicalQuery.SelfAndDescendants<SwiftImage>(status));
            }
        });
    });
}
