using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.App.Runtime;
using Marrow.App.Views.Composer;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Views.Chat;

// Source: DroppyCode/App/Views/Chat/HydraPanel.swift (B2 1fbf81e), :1-553.

/// <summary>:4-201 <c>struct HydraPanel: View</c>: the lead's team as a floating glass panel.</summary>
public sealed class HydraPanel : Decorator
{
    public const double CornerRadius = 22; // :33

    // :37 static let stripHeight: CGFloat = Chrome.chromeTopPadding + Chrome.capsuleHeight + 6
    public static double StripHeight => Chrome.ChromeTopPadding + Chrome.CapsuleHeight + 6;

    // :41 static let finishHold: TimeInterval = 2.0
    public const double FinishHold = 2.0;

    // :18-31, :46-122
    public HydraPanel(
        ThreadRuntime runtime,
        IReadOnlyList<ChatThread> heads,
        Size size,
        string? workingDirectory,
        string? projectName,
        Action<Size> onDrag,
        Action onDragEnd,
        Action dismiss,
        bool isPoppedOut = false,
        Action<Guid>? popOut = null)
    {
        // :50-56 The head on stage: the one picked, else the newest still working, else the newest. A
        // popped-out panel holds one head, and that one is on stage.
        ChatThread? selected = null;
        if (isPoppedOut)
        {
            if (heads.Count > 0) selected = heads[0];
        }
        else
        {
            foreach (var head in heads)
            {
                if (head.Id == runtime.HydraSelectedHeadID)
                {
                    selected = head;
                    break;
                }
            }

            if (selected is null)
            {
                for (var index = heads.Count - 1; index >= 0; index--)
                {
                    if (heads[index].Hydra?.HydraHeadInfoStatus == HydraHeadInfo.Status.Running)
                    {
                        selected = heads[index];
                        break;
                    }
                }
            }

            if (selected is null && heads.Count > 0) selected = heads[heads.Count - 1];
        }

        var isDark = ActualThemeVariant == ThemeVariant.Dark;
        // :99-108 One glass surface washed with Chrome.glassTint at 0.22 in dark and 0.16 in light
        // (UI-HYDRA-073).
        var shape = new Border
        {
            CornerRadius = new CornerRadius(CornerRadius),
            ClipToBounds = true,
            Background = Chrome.GlassTint.Opacity(isDark ? 0.22 : 0.16).Brush(this),
            Child = selected is null
                ? new Grid()
                : new HydraHeadTranscript(selected, size.Height, workingDirectory, projectName)
        };

        // :113-121 The scrim carries the panel's shadow: black at 0.3 in dark and white at 0.34 in light,
        // with a black shadow at 1.0 in dark and 0.65 in light, radius 28, y offset 10. It is a plain
        // filled shape, so its shadow is drawn once and kept.
        var scrim = new Border
        {
            CornerRadius = new CornerRadius(CornerRadius),
            Background = new SolidColorBrush(Color.FromArgb(
                isDark ? (byte)77 : (byte)87,
                isDark ? (byte)0 : (byte)255,
                isDark ? (byte)0 : (byte)255,
                isDark ? (byte)0 : (byte)255)),
            BoxShadow = new BoxShadows(new BoxShadow
            {
                Color = Color.FromArgb(isDark ? (byte)255 : (byte)166, 0, 0, 0),
                Blur = 28,
                OffsetY = 10
            })
        };

        // :92-98, :110-121 Keep the shadow-bearing scrim outside the clipped panel. The panel clips
        // its surface and strip to the continuous rounded shape; the scrim's shadow remains visible.
        var panelContents = new Grid();
        panelContents.Children.Add(shape);
        panelContents.Children.Add(Strip(selected, runtime, heads, isPoppedOut, popOut, dismiss, onDrag, onDragEnd));

        var panel = new Border
        {
            CornerRadius = new CornerRadius(CornerRadius),
            ClipToBounds = true,
            Child = panelContents
        };

        var floatingPanel = new Grid();
        floatingPanel.Children.Add(scrim);
        floatingPanel.Children.Add(panel);
        Child = floatingPanel;
        ClipToBounds = false;
        Width = size.Width;
        Height = size.Height;
    }

    // :124-200 The handle across the top: the count and the head on stage at the left, the pop-out and
    // close buttons at the right, and the room between them drags the panel.
    private Control Strip(
        ChatThread? selected,
        ThreadRuntime runtime,
        IReadOnlyList<ChatThread> heads,
        bool isPoppedOut,
        Action<Guid>? popOut,
        Action dismiss,
        Action<Size> onDrag,
        Action onDragEnd)
    {
        // :128-131
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (!isPoppedOut) left.Children.Add(new HydraHeadsButton(runtime, heads, dismiss));
        if (selected?.Hydra is { } info)
        {
            // :132-135 The head's stored record, not its live one: the strip sits over a streaming chat
            // and shows only the name and the state.
            var capsule = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center
            };
            capsule.Children.Add(new HydraGlyph(info.Persona, 16, info.HydraHeadInfoStatus == HydraHeadInfo.Status.Running, info.HydraHeadInfoStatus));
            capsule.Children.Add(new HydraNameText(info.Persona, 12.5, SwiftFont.Weight.Medium, Chrome.PrimaryText.Opacity(0.92)));
            // :141-145 The provider's mark, so the strip says whose model is at work on stage.
            var providerIcon = new ProviderIcon { Provider = selected.Provider, Size = 12 };
            ToolTip.SetTip(providerIcon,
                WindowManager.Shared.Model.Providers.Model(selected.Model, selected.Provider)?.ShortName
                ?? selected.Provider.DisplayName);
            capsule.Children.Add(providerIcon);
            // :146-149 Text(verbatim: HydraStatusText.short(info)).font(.system(.callout)); ED-059: callout
            // is 12 points (B1:DroppyCode/UI/Common/ChatZoom.swift:52), in Inter (OD-0009).
            var status = new TextBlock
            {
                Text = HydraStatusText.Short(info),
                Foreground = Chrome.SecondaryText.Brush(this),
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            SwiftFont.System(SwiftFont.TextStyle.Callout).Apply(status);
            capsule.Children.Add(status);
            // :150-152 The head's own clock, so the strip itself is still drawn once.
            capsule.Children.Add(new HydraElapsedTime(info.StartedAt, info.FinishedAt, info.HydraHeadInfoStatus == HydraHeadInfo.Status.Running));

            // :154-158 horizontal 12, content height 28, then vertical 2, fixed size, chromeGlassCapsule.
            var capsuleBorder = new Border
            {
                Child = capsule,
                Padding = new Thickness(Chrome.CapsuleHorizontalPadding, Chrome.CapsuleVerticalPadding),
                Height = Chrome.CapsuleHeight,
                CornerRadius = new CornerRadius(Chrome.CapsuleHeight / 2),
                Background = Chrome.GlassTint.Opacity(0.22).Brush(this)
            };

            // :159-163 The name is a label, not a control, so it drags the panel too: it is where the hand
            // goes when there is little room beside it.
            var stage = new Grid();
            stage.Children.Add(capsuleBorder);
            var stageDrag = new Border { Background = Brushes.Transparent };
            WireDragHandle(stageDrag, onDrag, onDragEnd);
            stage.Children.Add(stageDrag);
            left.Children.Add(stage);
        }

        // :169-175 The handle. Invisible, like a window's title bar with no title; the cursor says what it
        // does.
        var drag = new Border
        {
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height = StripHeight
        };
        WireDragHandle(drag, onDrag, onDragEnd);
        ToolTip.SetTip(drag, "Drag to move");
        AutomationProperties.SetName(drag, "Drag to move");

        // :176-194
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (isPoppedOut)
        {
            // :177-180
            right.Children.Add(new ChromeCircleButton(
                "arrow.down.left",
                $"Put {selected?.Hydra?.DisplayName ?? "the head"} back in the team panel",
                dismiss));
        }
        else
        {
            // :182-188 The head on stage pops out into a second panel, as long as another head stays
            // behind to keep this one.
            if (selected is not null && popOut is not null && heads.Count > 1)
                right.Children.Add(new ChromeCircleButton(
                    "arrow.up.right",
                    $"Pop {selected.Hydra?.DisplayName ?? "this head"} out into its own panel",
                    () => popOut(selected.Id)));
            // :190-192
            right.Children.Add(new ChromeCircleButton("xmark", "Dismiss the panel; heads keep working", dismiss));
        }

        // :127, :167-168, :195-196 The strip's ends, padded 12 at the top and 14 at the outer edge.
        var row = new Grid { Height = StripHeight };
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        var leftHolder = new Border
        {
            Child = left,
            Padding = new Thickness(Chrome.ChromeHorizontalPadding, Chrome.ChromeTopPadding, 0, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        var rightHolder = new Border
        {
            Child = right,
            Padding = new Thickness(0, Chrome.ChromeTopPadding, Chrome.ChromeHorizontalPadding, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetColumn(leftHolder, 0);
        Grid.SetColumn(drag, 1);
        Grid.SetColumn(rightHolder, 2);
        row.Children.Add(leftHolder);
        row.Children.Add(drag);
        row.Children.Add(rightHolder);
        return row;
    }

    /// <summary>
    /// :169-175, :640-738. The strip's invisible handle. In the source the middle of the strip is
    /// <c>PanelDragHandle</c>, an NSViewRepresentable over SubagentPanel.swift's <c>PanelDragHandleView</c>
    /// (:677-738): a press is tracked, the pointer's travel is reported through <c>onDrag</c> once it has
    /// passed the source's 2-point slop (:685), and the release is reported through <c>onDragEnd</c>. That
    /// pair belongs to the SubagentPanel twin, whose bodies are not ported yet, so the strip wires the same
    /// pointer behaviour here.
    /// </summary>
    private static void WireDragHandle(Border handle, Action<Size> onDrag, Action onDragEnd)
    {
        Point? last = null;
        var moved = false;

        handle.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
            last = e.GetPosition(handle);
            moved = false;
            e.Pointer.Capture(handle);
            e.Handled = true;
        };
        handle.PointerMoved += (_, e) =>
        {
            if (last is not { } previous) return;
            var point = e.GetPosition(handle);
            var step = new Size(point.X - previous.X, point.Y - previous.Y);
            if (!moved)
            {
                // :685 private let slop: CGFloat = 2
                if (Math.Abs(step.Width) + Math.Abs(step.Height) < 2) return;
                moved = true;
            }
            last = point;
            onDrag(step);
        };
        handle.PointerReleased += (_, e) =>
        {
            if (last is null) return;
            last = null;
            e.Pointer.Capture(null);
            onDragEnd();
        };
        handle.PointerCaptureLost += (_, _) =>
        {
            if (last is null) return;
            last = null;
            onDragEnd();
        };
    }
}

/// <summary>:203-208 <c>private struct StageHold: Hashable</c>.</summary>
internal sealed record StageHold
{
    public required Guid Id { get; init; } // :206
    public required DateTimeOffset StartedAt { get; init; } // :207
}

/// <summary>:210-214 <c>private struct StageMark: Equatable</c>.</summary>
internal sealed record StageMark
{
    public Guid? Id { get; init; } // :212
    public HydraHeadInfo.Status? Status { get; init; } // :213
}

/// <summary>:216-288 <c>private struct HydraHeadsButton: View</c>.</summary>
internal sealed class HydraHeadsButton : Decorator
{
    private readonly Border popover = null!;

    // :223-225, :230-287
    public HydraHeadsButton(ThreadRuntime runtime, IReadOnlyList<ChatThread> heads, Action dismiss)
    {
        // :231 let running = heads.count { $0.hydra?.status == .running }
        var running = 0;
        foreach (var head in heads)
            if (head.Hydra?.HydraHeadInfoStatus == HydraHeadInfo.Status.Running) running++;

        // :232-242
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new HydraSpokesMark());
        row.Children.Add(new TextBlock
        {
            Text = heads.Count.ToString(CultureInfo.InvariantCulture),
            FontSize = 12.5,
            FontWeight = FontWeight.SemiBold,
            FontFamily = new FontFamily("fonts:Inter#Inter")
        });
        if (running > 0) row.Children.Add(new MiniSpinner());
        row.Children.Add(SwiftImage.SystemName("chevron.down").Font(Chrome.ChevronFont));

        // :243-249, :254-257
        var label = heads.Count == 1 ? "1 head" : $"{heads.Count.ToString(CultureInfo.InvariantCulture)} heads";
        var capsule = new Border
        {
            Child = row,
            Padding = new Thickness(Chrome.CapsuleHorizontalPadding, Chrome.CapsuleVerticalPadding),
            Height = Chrome.CapsuleHeight,
            CornerRadius = new CornerRadius(Chrome.CapsuleHeight / 2),
            Background = Chrome.GlassTint.Opacity(0.22).Brush(this)
        };
        ToolTip.SetTip(capsule, label);
        AutomationProperties.SetName(capsule, label);
        // :250 The tap rides along with the glass's own tracking, so the whole capsule presses the native
        // way.
        capsule.PointerPressed += (_, args) =>
        {
            if (args.GetCurrentPoint(capsule).Properties.IsLeftButtonPressed)
            {
                popover.IsVisible = !popover.IsVisible;
                args.Handled = true;
            }
        };

        // :258-286
        var menu = new StackPanel { Spacing = 4 };
        menu.Children.Add(new PopoverSectionHeader(Header(running, runtime.HydraWaitingCount)));
        foreach (var head in heads)
        {
            var rowControl = new HydraHeadRow(head, head.Id == runtime.HydraSelectedHeadID, () =>
            {
                runtime.HydraSelectedHeadID = head.Id;
                popover.IsVisible = false;
            });
            menu.Children.Add(rowControl);
        }

        var finished = false;
        foreach (var head in heads)
            if (head.Hydra?.IsFinished == true) finished = true;

        if (running > 1 || finished) menu.Children.Add(new PopoverDivider());
        if (running > 1)
        {
            menu.Children.Add(new PopoverItem("Stop all heads", () =>
            {
                InvokeModelAction(WindowManager.Shared.Model, "StopAllHydraHeads", runtime.ThreadID);
                popover.IsVisible = false;
            }, "stop.circle"));
        }
        if (finished)
        {
            menu.Children.Add(new PopoverItem("Clear finished heads", () =>
                InvokeModelAction(WindowManager.Shared.Model, "ClearFinishedHydraHeads", runtime.ThreadID), "checkmark.circle"));
        }

        popover = new Border
        {
            Child = menu,
            IsVisible = false,
            ZIndex = 100,
            Background = Chrome.GlassTint.Opacity(0.22).Brush(this),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8)
        };

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        content.Children.Add(capsule);
        content.Children.Add(popover);
        Child = content;
    }

    // :274, :280-281. The Swift methods are on AppModel; resolve their C# names at the call boundary.
    private static void InvokeModelAction(AppModel model, string methodName, Guid threadID)
    {
        var method = typeof(AppModel).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public, [typeof(Guid)]);
        if (method is null) throw new MissingMethodException(typeof(AppModel).FullName, methodName);
        method.Invoke(model, [threadID]);
    }

    // :259-262
    private static string Header(int running, long waiting)
    {
        var header = running == 0 ? "Heads" : running == 1 ? "1 head working" : $"{running.ToString(CultureInfo.InvariantCulture)} heads working";
        return waiting > 0 ? header + (waiting == 1 ? ", 1 waiting" : $", {waiting.ToString(CultureInfo.InvariantCulture)} waiting") : header;
    }
}

/// <summary>:290-297 <c>private struct HydraSpokesMark: View</c>.</summary>
internal sealed class HydraSpokesMark : Decorator
{
    // :292-296
    public HydraSpokesMark()
    {
        Child = new HydraMarkImage
        {
            Width = 15,
            Height = 15,
            Opacity = 0.9
        };
    }
}

/// <summary>:299-363 <c>private struct HydraHeadRow: View</c>.</summary>
internal sealed class HydraHeadRow : Decorator
{
    // :304-306, :310-362
    public HydraHeadRow(ChatThread head, bool isOnStage, Action select)
    {
        if (head.Hydra is not { } stored) return;

        // :313-314 A running head's note and counts live on its runtime, off the thread record.
        var info = HydraLiveInfo.Shown(stored, WindowManager.Shared.Model.ExistingRuntime(head.Id));

        // :320-336
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        label.Children.Add(new HydraNameText(info.Persona, 13, SwiftFont.Weight.Medium, Chrome.PrimaryText));
        var providerIcon = new ProviderIcon { Provider = head.Provider, Size = 11 };
        ToolTip.SetTip(providerIcon,
            WindowManager.Shared.Model.Providers.Model(head.Model, head.Provider)?.ShortName
            ?? head.Provider.DisplayName);
        label.Children.Add(providerIcon);
        // :327-330 .font(.system(.subheadline)); ED-059: subheadline is 11 points
        // (B1:DroppyCode/UI/Common/ChatZoom.swift:53).
        var status = new TextBlock
        {
            Text = HydraStatusText.Long(info),
            Foreground = (info.HydraHeadInfoStatus == HydraHeadInfo.Status.Failed ? Chrome.Danger : Chrome.SecondaryText).Brush(this),
            TextWrapping = TextWrapping.NoWrap
        };
        SwiftFont.System(SwiftFont.TextStyle.Subheadline).Apply(status);
        label.Children.Add(status);

        var details = new StackPanel { Spacing = 2 };
        details.Children.Add(label);
        // :332-336 Text(verbatim: info.task.isEmpty ? "Waiting for a task" : info.task).font(.system(.callout))
        var task = new TextBlock
        {
            Text = info.Task.Length == 0 ? "Waiting for a task" : info.Task,
            Foreground = Chrome.SecondaryText.Brush(this),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };
        SwiftFont.System(SwiftFont.TextStyle.Callout).Apply(task);
        details.Children.Add(task);

        // :315-347
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var selectButton = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children =
                {
                    new HydraGlyph(info.Persona, 22, info.HydraHeadInfoStatus == HydraHeadInfo.Status.Running, info.HydraHeadInfoStatus),
                    details,
                    // :338 Spacer(minLength: 8)
                    new Border { Width = 8 }
                }
            },
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        selectButton.Click += (_, _) => select();
        row.Children.Add(selectButton);
        if (info.HydraHeadInfoStatus == HydraHeadInfo.Status.Running && info.CanStop)
            row.Children.Add(new HydraStopButton(info.DisplayName, () => InvokeModelAction(WindowManager.Shared.Model, "StopHydraHead", head.Id)));

        // :349-360
        var background = new Border
        {
            Child = row,
            Padding = new Thickness(8, 5),
            CornerRadius = new CornerRadius(6),
            Background = isOnStage ? Chrome.Overlay(0.14).Brush(this) : Brushes.Transparent
        };
        AutomationProperties.SetName(background,
            $"{info.DisplayName}, {HydraStatusText.Long(info)}, {info.Task}{(isOnStage ? ", on stage" : "")}");
        Child = background;
    }

    private static void InvokeModelAction(AppModel model, string methodName, Guid threadID)
    {
        var method = typeof(AppModel).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public, [typeof(Guid)]);
        if (method is null) throw new MissingMethodException(typeof(AppModel).FullName, methodName);
        method.Invoke(model, [threadID]);
    }
}

/// <summary>:365-383 <c>private struct HydraStopButton: View</c>.</summary>
internal sealed class HydraStopButton : Decorator
{
    // :368-369, :371-382
    public HydraStopButton(string name, Action action)
    {
        var button = new Button
        {
            Content = SwiftImage.SystemName("stop.fill")
                .Font(Chrome.IconFont)
                .ForegroundStyle(SwiftColor.White),
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Background = Chrome.Accent.Brush(this),
            CornerRadius = new CornerRadius(14)
        };
        button.Click += (_, _) => action();
        ToolTip.SetTip(button, $"Stop {name}");
        AutomationProperties.SetName(button, $"Stop {name}");
        Child = button;
    }
}

/// <summary>:385-437 <c>private struct HydraHeadTranscript: View</c>.</summary>
internal sealed class HydraHeadTranscript : Decorator
{
    // :388-391, :396-436
    public HydraHeadTranscript(ChatThread head, double height, string? workingDirectory, string? projectName)
    {
        var model = WindowManager.Shared.Model;
        var runtime = model.Runtime(head.Id);
        var scrollChrome = new ChromeScrollModel();
        var scrollState = new TimelineScrollState();
        Control body;
        if (model.Settings.HydraShowsHeadDetails)
        {
            // :400-411 A panel's transcript needs no rail.
            body = new ThreadTimeline(
                runtime,
                scrollChrome,
                scrollState,
                projectName,
                workingDirectory,
                false,
                height,
                false);
        }
        else
        {
            // :413-417 The task and the progress bar stand in for the steps.
            body = new HydraHeadProgress(head, runtime);
        }

        // :419-432
        var content = new DockPanel();
        content.Children.Add(body);
        if (head.Hydra?.HydraHeadInfoKind == HydraHeadInfo.Kind.Droppy)
        {
            // :421-425 A child Droppy Code runs itself can be steered and answered like any chat.
            var composer = new Grid();
            composer.Children.Add(new ComposerArea(runtime, workingDirectory, compactModelChip: true, takesFocusOnAppear: false, showsChanges: false));
            composer.Children.Add(new JumpToLatestButton(scrollState));
            DockPanel.SetDock(composer, Dock.Bottom);
            content.Children.Add(composer);
        }
        else if (head.Hydra is { } info)
        {
            var footer = new Grid();
            footer.Children.Add(new HydraHeadFooter(HydraLiveInfo.Shown(info, runtime)));
            footer.Children.Add(new JumpToLatestButton(scrollState));
            DockPanel.SetDock(footer, Dock.Bottom);
            content.Children.Add(footer);
        }

        // :433-435
        var transcript = new Grid();
        transcript.Children.Add(content);
        transcript.Children.Add(new PaneTopVeil(scrollChrome));
        Child = transcript;
    }
}

/// <summary>:439-473 <c>private struct HydraHeadFooter: View</c>.</summary>
internal sealed class HydraHeadFooter : Decorator
{
    // :443, :445-472
    public HydraHeadFooter(HydraHeadInfo info)
    {
        var isDark = ActualThemeVariant == ThemeVariant.Dark;
        var row = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };

        // :447-453
        Control leading;
        if (info.HydraHeadInfoStatus == HydraHeadInfo.Status.Running)
        {
            leading = new MiniSpinner();
        }
        else
        {
            var symbol = info.HydraHeadInfoStatus == HydraHeadInfo.Status.Completed
                ? "checkmark.circle.fill"
                : info.HydraHeadInfoStatus == HydraHeadInfo.Status.Failed
                    ? "xmark.circle.fill"
                    : "stop.circle.fill";
            var color = info.HydraHeadInfoStatus == HydraHeadInfo.Status.Completed
                ? Chrome.Success
                : info.HydraHeadInfoStatus == HydraHeadInfo.Status.Failed
                    ? Chrome.Danger
                    : Chrome.SecondaryText;
            leading = SwiftImage.SystemName(symbol).Font(SwiftFont.System(SwiftFont.TextStyle.Callout)).ForegroundStyle(color);
        }

        leading.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(leading);

        // :454-458 Text(verbatim: HydraStatusText.footer(info)).font(.system(.callout))
        var status = new TextBlock
        {
            Text = HydraStatusText.Footer(info),
            Foreground = Chrome.SecondaryText.Brush(this),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        SwiftFont.System(SwiftFont.TextStyle.Callout).Apply(status);
        Grid.SetColumn(status, 1);
        row.Children.Add(status);

        // :460-464
        if (info.ToolCalls > 0)
        {
            var steps = new TextBlock
            {
                Text = info.ToolCalls == 1 ? "1 step" : $"{info.ToolCalls.ToString(CultureInfo.InvariantCulture)} steps",
                Foreground = Chrome.SecondaryText.Brush(this),
                VerticalAlignment = VerticalAlignment.Center
            };
            SwiftFont.System(SwiftFont.TextStyle.Subheadline).MonospacedDigit().Apply(steps);
            Grid.SetColumn(steps, 2);
            row.Children.Add(steps);
        }

        // :469 Chrome.panelControlFill(isDark:): Chrome.glassTint mixed toward white in dark by 0.12 or
        // toward black in light by 0.04, at 0.55 opacity in dark and 0.42 in light.
        var fill = Chrome.GlassTint
            .Mix(isDark ? SwiftColor.White : SwiftColor.Black, isDark ? 0.12 : 0.04)
            .Opacity(isDark ? 0.55 : 0.42);

        // :466-471 padded 14 horizontally and 10 vertically, backed by a 16-point continuous rounded
        // rectangle, then padded 20 horizontally and 14 at the bottom.
        Child = new Border
        {
            Child = row,
            Padding = new Thickness(14, 10),
            CornerRadius = new CornerRadius(16),
            Background = fill.Brush(this),
            Margin = new Thickness(20, 0, 20, 14)
        };
    }
}

/// <summary>:475-489 <c>@MainActor enum HydraLiveInfo</c>, a caseless enum used as a namespace.</summary>
public static class HydraLiveInfo
{
    // :482-488
    public static HydraHeadInfo Shown(HydraHeadInfo stored, ThreadRuntime? runtime)
    {
        if (stored.HydraHeadInfoStatus != HydraHeadInfo.Status.Running || runtime is null) return stored;
        var info = stored;
        if (runtime.HydraActivity is { } activity) info = info with { Activity = activity };
        info = info with { ToolCalls = Math.Max(info.ToolCalls, runtime.HydraToolCalls) };
        return info;
    }
}

/// <summary>:491-553 <c>enum HydraStatusText</c>, a caseless enum used as a namespace: the words for a head's state.</summary>
public static class HydraStatusText
{
    // :493-500
    public static string Short(HydraHeadInfo info) => info.HydraHeadInfoStatus switch
    {
        HydraHeadInfo.Status.Running => "working",
        HydraHeadInfo.Status.Completed => "done",
        HydraHeadInfo.Status.Failed => "failed",
        HydraHeadInfo.Status.Stopped => "stopped",
        _ => throw new System.Diagnostics.UnreachableException()
    };

    // :502-513
    public static string Long(HydraHeadInfo info) => info.HydraHeadInfoStatus switch
    {
        HydraHeadInfo.Status.Running => info.Activity is { } activity
            ? "\U000000B7 " + SingleLine(activity, 40) // U+00B7 MIDDLE DOT
            : "\U000000B7 working", // U+00B7 MIDDLE DOT
        HydraHeadInfo.Status.Completed =>
            "\U000000B7 done" + Elapsed(info) + Steps(info) + (Landed(info) is { } landed ? " \U000000B7 " + landed : ""), // U+00B7 MIDDLE DOT
        HydraHeadInfo.Status.Failed => "\U000000B7 failed", // U+00B7 MIDDLE DOT
        HydraHeadInfo.Status.Stopped => "\U000000B7 stopped", // U+00B7 MIDDLE DOT
        _ => throw new System.Diagnostics.UnreachableException()
    };

    // :515-526
    public static string Footer(HydraHeadInfo info) => info.HydraHeadInfoStatus switch
    {
        HydraHeadInfo.Status.Running => info.Activity is { } activity
            ? SingleLine(activity, 80)
            : "Working",
        HydraHeadInfo.Status.Completed =>
            "Done" + Elapsed(info) + Steps(info) + (Landed(info) is { } landed ? " \U000000B7 " + landed : "") + // U+00B7 MIDDLE DOT
            (info.Summary is { } summary ? " \U000000B7 " + SingleLine(summary, 80) : ""), // U+00B7 MIDDLE DOT
        HydraHeadInfo.Status.Failed =>
            "Failed" + (info.Summary is { } summary ? " \U000000B7 " + SingleLine(summary, 80) : ""), // U+00B7 MIDDLE DOT
        HydraHeadInfo.Status.Stopped => "Stopped",
        _ => throw new System.Diagnostics.UnreachableException()
    };

    // :528-540 Where a Droppy-run head's work went, in a few words; nil for a head with no copy of its own.
    public static string? Landed(HydraHeadInfo info)
    {
        if (info.Landing is not { } landing) return null;
        if (landing.IsEmpty) return "changed nothing";
        var files = landing.Files.Count == 1 ? "1 file" : $"{landing.Files.Count.ToString(CultureInfo.InvariantCulture)} files";
        if (landing.PatchPath is not null) return "kept as a patch";
        if (landing.Error is not null) return "did not land";
        if (landing.Conflicts.Count != 0)
            return "landed " + files + ", " + (landing.Conflicts.Count == 1
                ? "1 conflict"
                : $"{landing.Conflicts.Count.ToString(CultureInfo.InvariantCulture)} conflicts");
        return "landed " + files;
    }

    // :542-545
    private static string Steps(HydraHeadInfo info)
    {
        if (info.ToolCalls <= 0) return "";
        return info.ToolCalls == 1
            ? " \U000000B7 1 step" // U+00B7 MIDDLE DOT
            : $" \U000000B7 {info.ToolCalls.ToString(CultureInfo.InvariantCulture)} steps"; // U+00B7 MIDDLE DOT
    }

    // :547-552
    private static string Elapsed(HydraHeadInfo info)
    {
        if (info.FinishedAt is not { } finished) return "";
        var seconds = SwiftNumber.TruncatingToInt(SwiftDate.TimeIntervalSince(finished, info.StartedAt));
        if (seconds <= 0) return "";
        return seconds < 60
            ? $" in {seconds.ToString(CultureInfo.InvariantCulture)}s"
            : $" in {(seconds / 60).ToString(CultureInfo.InvariantCulture)}m {(seconds % 60).ToString(CultureInfo.InvariantCulture)}s";
    }

    // Text.swift:63-73 TextCleanup.singleLine, at this file's own call sites (:505, :518, :520, :522).
    private static string SingleLine(string text, int limit) => TextCleanup.SingleLine(text, limit);
}
