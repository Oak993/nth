using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Chat;

[Collection("Headless")]
public sealed class HydraPanelTests
{
    private readonly HeadlessSession session;

    public HydraPanelTests(HeadlessSession session) => this.session = session;

    [Fact]
    [Trait("Req", "UI-HYDRA-022")] // App/Views/Chat/HydraPanel.swift:190-192
    public void Dismiss_button_uses_the_xmark_help_and_runs_its_action()
    {
        session.OnUiThread(() =>
        {
            var calls = 0;
            var button = new ChromeCircleButton("xmark", "Dismiss the panel; heads keep working", () => calls++);
            var controls = LogicalQuery.SelfAndDescendants<Control>(button).ToArray();

            Assert.Contains(controls, control => ToolTip.GetTip(control) as string == "Dismiss the panel; heads keep working");
            Assert.Contains(LogicalQuery.SelfAndDescendants<SwiftImage>(button),
                image => image.SymbolName == "xmark");

            var clickTarget = Assert.Single(controls.OfType<Button>());
            clickTarget.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(1, calls);
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-023")] // App/Views/Chat/HydraPanel.swift:177-180
    public void Popped_out_panel_put_back_button_uses_the_fallback_name_and_dismisses()
    {
        session.OnUiThread(() =>
        {
            var calls = 0;
            var panel = new HydraPanel(
                runtime: null!,
                heads: Array.Empty<ChatThread>(),
                size: new Avalonia.Size(400, 280),
                workingDirectory: null,
                projectName: null,
                onDrag: _ => { },
                onDragEnd: () => { },
                dismiss: () => calls++,
                isPoppedOut: true);

            var controls = LogicalQuery.SelfAndDescendants<Control>(panel).ToArray();
            Assert.Contains(controls, control =>
                ToolTip.GetTip(control) as string == "Put the head back in the team panel");
            Assert.Contains(LogicalQuery.SelfAndDescendants<SwiftImage>(panel),
                image => image.SymbolName == "arrow.down.left");

            var clickTarget = Assert.Single(controls.OfType<Button>());
            clickTarget.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(1, calls);
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-024")] // App/Views/Chat/HydraPanel.swift:441-473
    public void Footer_uses_the_upstream_text_for_each_status_and_omits_zero_steps()
    {
        using var runtime = new TestRuntime();

        var runningWithoutActivity = new HydraHeadInfo(0, "", default, default);
        var runningWithActivity = runningWithoutActivity with { Activity = "Searching files" };
        var completedWithoutSteps = runningWithoutActivity with
        {
            HydraHeadInfoStatus = HydraHeadInfo.Status.Completed
        };
        var completedWithStepsAndEmptyLanding = completedWithoutSteps with
        {
            ToolCalls = 1,
            Landing = new HydraLanding()
        };
        var failedWithSummary = runningWithoutActivity with
        {
            HydraHeadInfoStatus = HydraHeadInfo.Status.Failed,
            Summary = "Could not finish"
        };
        var stopped = runningWithoutActivity with
        {
            HydraHeadInfoStatus = HydraHeadInfo.Status.Stopped
        };

        Assert.Equal("Working", HydraStatusText.Footer(runningWithoutActivity));
        Assert.Equal("Searching files", HydraStatusText.Footer(runningWithActivity));
        Assert.Equal("Done", HydraStatusText.Footer(completedWithoutSteps));
        Assert.Equal("Done \U000000B7 1 step \U000000B7 changed nothing", HydraStatusText.Footer(completedWithStepsAndEmptyLanding));
        Assert.Equal("Failed \U000000B7 Could not finish", HydraStatusText.Footer(failedWithSummary));
        Assert.Equal("Stopped", HydraStatusText.Footer(stopped));
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-024")] // App/Views/Chat/HydraPanel.swift:515-552
    public void Footer_includes_elapsed_steps_landing_and_summary_in_upstream_order()
    {
        using var runtime = new TestRuntime();

        var startedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var info = new HydraHeadInfo(0, "", default, default)
        {
            HydraHeadInfoStatus = HydraHeadInfo.Status.Completed,
            StartedAt = startedAt,
            FinishedAt = startedAt.AddSeconds(61),
            ToolCalls = 2,
            Landing = new HydraLanding
            {
                Files = new[]
                {
                    new HydraLanding.File { Path = "one.txt", Additions = 1, Deletions = 0 },
                    new HydraLanding.File { Path = "two.txt", Additions = 0, Deletions = 1 }
                },
                Conflicts = new[] { "one.txt" }
            },
            Summary = "Finished the task"
        };

        Assert.Equal(
            "Done in 1m 1s \U000000B7 2 steps \U000000B7 landed 2 files, 1 conflict \U000000B7 Finished the task",
            HydraStatusText.Footer(info));
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-025")] // App/Views/Chat/HydraPanel.swift:419-425
    public void Composer_area_exposes_the_source_options_for_a_droppy_head()
    {
        var constructor = typeof(Marrow.App.Views.Composer.ComposerArea)
            .GetConstructors()
            .Single();
        var parameters = constructor.GetParameters();

        Assert.Equal(
            new[] { "runtime", "workingDirectory", "compactModelChip", "takesFocusOnAppear", "showsChanges" },
            parameters.Select(parameter => parameter.Name).ToArray());
        Assert.Equal(typeof(bool), parameters[2].ParameterType);
        Assert.Equal(typeof(bool), parameters[3].ParameterType);
        Assert.Equal(typeof(bool), parameters[4].ParameterType);
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-026")] // App/Views/Chat/HydraPanel.swift:38-41, :72-91
    public void Finish_hold_is_two_seconds()
    {
        Assert.Equal(2.0, HydraPanel.FinishHold);
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-073")] // App/Views/Chat/HydraPanel.swift:99-121
    public void Panel_corner_radius_is_twenty_two_points()
    {
        Assert.Equal(22.0, HydraPanel.CornerRadius);
    }
}
