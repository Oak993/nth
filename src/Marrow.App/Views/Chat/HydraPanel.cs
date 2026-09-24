using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
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

    // :37 static let stripHeight: CGFloat
    public static double StripHeight => Chrome.ChromeTopPadding + Chrome.CapsuleHeight + 6;

    public const double FinishHold = 2.0; // :41

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
        // :50-56
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

        var shape = new Border
        {
            CornerRadius = new CornerRadius(CornerRadius),
            ClipToBounds = true,
            Background = Chrome.GlassTint.Opacity(0.22).Brush(this),
            Child = selected is null
                ? new Grid()
                : new HydraHeadTranscript(selected, size.Height, workingDirectory, projectName)
        };

        var scrim = new Border
        {
            CornerRadius = new CornerRadius(CornerRadius),
            Background = new SolidColorBrush(Color.FromArgb(77, 0, 0, 0)),
            BoxShadow = new BoxShadows(new BoxShadow
            {
                Color = Color.FromArgb(255, 0, 0, 0),
                Blur = 28,
                OffsetY = 10
            })
        };

        var content = new Grid();
        content.Children.Add(scrim);
        content.Children.Add(shape);
        content.Children.Add(Strip(selected, runtime, heads, isPoppedOut, popOut, dismiss, onDrag, onDragEnd));
        Child = content;
        Width = size.Width;
        Height = size.Height;
    }

    // :126-200
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
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (!isPoppedOut) left.Children.Add(new HydraHeadsButton(runtime, heads, dismiss));
        if (selected?.Hydra is { } info)
        {
            var capsule = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center
            };
            capsule.Children.Add(new HydraGlyph(info.Persona, 16, info.HydraHeadInfoStatus == HydraHeadInfo.Status.Running, info.HydraHeadInfoStatus));
            capsule.Children.Add(new HydraNameText(info.Persona, 12.5, SwiftFont.Weight.Medium, Chrome.PrimaryText.Opacity(0.92)));
            var providerIcon = new ProviderIcon { Provider = selected.Provider, Size = 12 };
            ToolTip.SetTip(providerIcon,
                WindowManager.Shared.Model.Providers.Model(selected.Model, selected.Provider)?.ShortName
                ?? selected.Provider.DisplayName);
            capsule.Children.Add(providerIcon);
            capsule.Children.Add(new TextBlock
            {
                Text = HydraStatusText.Short(info),
                FontSize = 12,
                FontFamily = new FontFamily("fonts:Inter#Inter"),
                Foreground = Chrome.SecondaryText.Brush(this),
                TextWrapping = TextWrapping.NoWrap
            });
            capsule.Children.Add(new HydraElapsedTime(info.StartedAt, info.FinishedAt, info.HydraHeadInfoStatus == HydraHeadInfo.Status.Running));
            left.Children.Add(new Border
            {
                Child = capsule,
                Padding = new Thickness(12, Chrome.CapsuleVerticalPadding),
                Height = Chrome.CapsuleHeight,
                CornerRadius = new CornerRadius(Chrome.CapsuleHeight / 2),
                Background = Chrome.GlassTint.Opacity(0.22).Brush(this)
            });
            var stageCapsule = left.Children[left.Children.Count - 1];
            stageCapsule.PointerPressed += (_, args) =>
            {
                if (args.GetCurrentPoint(stageCapsule).Properties.IsLeftButtonPressed)
                {
                    args.Handled = true;
                }
            };
            var dragHandle = new PanelDragHandle(onDrag, onDragEnd);
            stageCapsule.RenderTransform = null;
            stageCapsule.PointerPressed += (_, _) => { };
            capsule.Children.Add(dragHandle);
        }

        var drag = new PanelDragHandle(onDrag, onDragEnd)
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height = StripHeight
        };
        ToolTip.SetTip(drag, "Drag to move");
        AutomationProperties.SetName(drag, "Drag to move");

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (isPoppedOut)
        {
            right.Children.Add(new ChromeCircleButton(
                "arrow.down.left",
                $"Put {selected?.Hydra?.DisplayName ?? "the head"} back in the team panel",
                dismiss));
        }
        else
        {
            if (selected is not null && popOut is not null && heads.Count > 1)
                right.Children.Add(new ChromeCircleButton(
                    "arrow.up.right",
                    $"Pop {selected.Hydra?.DisplayName ?? "this head"} out into its own panel",
                    () => popOut(selected.Id)));
            right.Children.Add(new ChromeCircleButton("xmark", "Dismiss the panel; heads keep working", dismiss));
        }

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
        var running = 0;
        foreach (var head in heads)
            if (head.Hydra?.HydraHeadInfoStatus == HydraHeadInfo.Status.Running) running++;

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
        capsule.PointerPressed += (_, args) =>
        {
            if (args.GetCurrentPoint(capsule).Properties.IsLeftButtonPressed)
            {
                popover.IsVisible = !popover.IsVisible;
                args.Handled = true;
            }
        };

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

    // :261-262
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

        var info = HydraLiveInfo.Shown(stored, WindowManager.Shared.Model.ExistingRuntime(head.Id));
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        label.Children.Add(new HydraNameText(info.Persona, 13, SwiftFont.Weight.Medium, Chrome.PrimaryText));
        var providerIcon = new ProviderIcon { Provider = head.Provider, Size = 11 };
        ToolTip.SetTip(providerIcon,
            WindowManager.Shared.Model.Providers.Model(head.Model, head.Provider)?.ShortName
            ?? head.Provider.DisplayName);
        label.Children.Add(providerIcon);
        label.Children.Add(new TextBlock
        {
            Text = HydraStatusText.Long(info),
            FontSize = 11,
            FontFamily = new FontFamily("fonts:Inter#Inter"),
            Foreground = (info.HydraHeadInfoStatus == HydraHeadInfo.Status.Failed ? Chrome.Danger : Chrome.SecondaryText).Brush(this),
            TextWrapping = TextWrapping.NoWrap
        });

        var details = new StackPanel { Spacing = 2 };
        details.Children.Add(label);
        details.Children.Add(new TextBlock
        {
            Text = info.Task.Length == 0 ? "Waiting for a task" : info.Task,
            FontSize = 12,
            FontFamily = new FontFamily("fonts:Inter#Inter"),
            Foreground = Chrome.SecondaryText.Brush(this),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        });

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
        Control body;
        if (model.Settings.HydraShowsHeadDetails)
        {
            body = new ThreadTimeline(
                runtime,
                new ChromeScrollModel(),
                new TimelineScrollState(),
                projectName,
                workingDirectory,
                false,
                height,
                false);
        }
        else
        {
            body = new HydraHeadProgress(head, runtime);
        }

        if (head.Hydra?.HydraHeadInfoKind == HydraHeadInfo.Kind.Droppy)
        {
            var content = new DockPanel();
            content.Children.Add(body);
            var composer = new ComposerArea(runtime, workingDirectory, compactModelChip: true, takesFocusOnAppear: false, showsChanges: false);
            DockPanel.SetDock(composer, Dock.Bottom);
            content.Children.Add(composer);
            Child = content;
        }
        else if (head.Hydra is { } info)
        {
            var content = new DockPanel();
            content.Children.Add(body);
            var footer = new HydraHeadFooter(HydraLiveInfo.Shown(info, runtime));
            DockPanel.SetDock(footer, Dock.Bottom);
            content.Children.Add(footer);
            Child = content;
        }
        else
        {
            Child = body;
        }
    }
}

/// <summary>:439-473 <c>private struct HydraHeadFooter: View</c>.</summary>
internal sealed class HydraHeadFooter : Decorator
{
    // :443, :445-472
    public HydraHeadFooter(HydraHeadInfo info)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (info.HydraHeadInfoStatus == HydraHeadInfo.Status.Running)
        {
            row.Children.Add(new MiniSpinner());
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
            row.Children.Add(SwiftImage.SystemName(symbol).Font(SwiftFont.Callout).ForegroundStyle(color));
        }

        row.Children.Add(new TextBlock
        {
            Text = HydraStatusText.Footer(info),
            FontSize = 12,
            FontFamily = new FontFamily("fonts:Inter#Inter"),
            Foreground = Chrome.SecondaryText.Brush(this),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        });
        row.Children.Add(new Border { Width = 8 });
        if (info.ToolCalls > 0)
        {
            row.Children.Add(new TextBlock
            {
                Text = info.ToolCalls == 1 ? "1 step" : $"{info.ToolCalls.ToString(CultureInfo.InvariantCulture)} steps",
                FontSize = 11,
                FontFamily = new FontFamily("fonts:Cascadia Mono#Cascadia Mono"),
                Foreground = Chrome.SecondaryText.Brush(this)
            });
        }

        Child = new Border
        {
            Child = row,
            Padding = new Thickness(14, 10),
            CornerRadius = new CornerRadius(16),
            Background = Chrome.GlassTint.Opacity(0.12).Brush(this),
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
            "Done" + Elapsed(info) + Steps(info) + (Landed(info) is { } landed ? " \U000000B7 " + landed : "") +
            (info.Summary is { } summary ? " \U000000B7 " + SingleLine(summary, 80) : ""), // U+00B7 MIDDLE DOT
        HydraHeadInfo.Status.Failed =>
            "Failed" + (info.Summary is { } summary ? " \U000000B7 " + SingleLine(summary, 80) : ""), // U+00B7 MIDDLE DOT
        HydraHeadInfo.Status.Stopped => "Stopped",
        _ => throw new System.Diagnostics.UnreachableException()
    };

    // :530-540
    public static string? Landed(HydraHeadInfo info)
    {
        if (info.Landing is not { } landing) return null;
        if (landing.IsEmpty) return "changed nothing";
        var files = landing.Files.Count == 1 ? "1 file" : $"{landing.Files.Count.ToString(CultureInfo.InvariantCulture)} files";
        if (landing.PatchPath is not null) return "kept as a patch";
        if (landing.Error is not null) return "did not land";
        if (landing.Conflicts.Count > 0)
            return "landed " + files + ", " + (landing.Conflicts.Count == 1 ? "1 conflict" : $"{landing.Conflicts.Count.ToString(CultureInfo.InvariantCulture)} conflicts");
        return "landed " + files;
    }

    // :505, :518, :520-522. PR-HYDRA-180: the running activity is cut to 40 characters in `long` and to 80 in
    // `footer`, and a finished head's summary to 80. The shared helper supplies the same first-line trim; the
    // panel's limit is the plain character cut the record states.
    private static string SingleLine(string text, int limit)
    {
        var line = TextCleanup.SingleLine(text, int.MaxValue);
        return SwiftString.Count(line) <= limit ? line : SwiftString.Prefix(line, limit);
    }

    // :542-545. Swift Int values are rendered invariantly.
    private static string Steps(HydraHeadInfo info) =>
        info.ToolCalls <= 0 ? "" : info.ToolCalls == 1 ? " \U000000B7 1 step" : $" \U000000B7 {info.ToolCalls.ToString(CultureInfo.InvariantCulture)} steps"; // U+00B7 MIDDLE DOT

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
}
