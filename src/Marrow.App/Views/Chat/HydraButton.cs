using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Views.Chat;

// Source: DroppyCode/App/Views/Chat/HydraButton.swift (B2 1fbf81e), :1-711.

/// <summary>:4-198 <c>struct HydraButton: View</c>: the chat's Hydra mark, first in the chrome row.</summary>
public sealed class HydraButton : Decorator
{
    private static readonly RecentCache<string, bool> DelegationChecks = new(32);
    private static readonly Dictionary<string, (int Scanned, bool Found)> StreamingScans = new(StringComparer.Ordinal);

    private readonly ChatThread thread;
    private readonly ThreadRuntime runtime;
    private readonly Button button;
    private bool isHovering;
    private bool isExplaining;
    private bool isSending;
    private bool isHydraSwitchShown;
    private bool isIntroShown;
    private long lastRunning;
    private TaskHandle? sendingPulse;

    // :12-13, :26-101
    public HydraButton(ChatThread thread, ThreadRuntime runtime)
    {
        this.thread = thread;
        this.runtime = runtime;
        lastRunning = RunningCount();

        button = new Button
        {
            Width = Chrome.CapsuleHeight,
            Height = Chrome.CapsuleHeight,
            Padding = new Thickness(0),
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Background = Chrome.GlassTint.Brush(this),
            Content = new HydraMarkImage(),
        };
        button.Click += OnClick;
        button.PointerEntered += (_, _) =>
        {
            isHovering = true;
            UpdateMark();
        };
        button.PointerExited += (_, _) =>
        {
            isHovering = false;
            UpdateMark();
        };
        button.ContextRequested += (_, e) =>
        {
            e.Handled = true;
            ShowSwitchPopover();
        };

        runtime.PropertyChanged += (_, _) => UpdateMark();

        Child = button;
        UpdateMark();
    }

    // :36-37
    private void OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!HasSeenIntro())
        {
            ShowIntroPopover();
            return;
        }

        if (Heads().Count == 0)
        {
            ShowIdlePopover();
            return;
        }

        TogglePanel();
    }

    // :27-35, :39-100
    private void UpdateMark()
    {
        var running = RunningCount();
        if (running > lastRunning && !isSending)
        {
            isSending = true;
            sendingPulse?.Cancel();
            sendingPulse = MainActor.Spawn(async () =>
            {
                await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(900));
                if (SwiftTask.IsCancelled) return;
                isSending = false;
                UpdateMark();
            });
        }

        lastRunning = running;

        var isOn = IsOn();
        var heads = Heads();
        var delegating = isOn && running == 0 && runtime.IsRunning && IsLeadDelegating(runtime.Entries);

        button.Content = new Grid
        {
            Width = Chrome.CapsuleHeight,
            Height = Chrome.CapsuleHeight,
            Children =
            {
                isOn ? new HydraCharge(running > 0) : new Grid(),
                new HydraMarkView(isOn, isHovering),
                MakeBadge(running, delegating),
            },
        };

        ToolTip.SetTip(button, Help(isOn, running, heads.Count > 0));
        AutomationProperties.SetName(button, PanelHelp(running, heads.Count > 0));
        AutomationProperties.SetHelpText(button, isOn ? "On" : "Off");
    }

    // :52-57, :620-645
    private Control MakeBadge(long running, bool delegating)
    {
        if (running == 0 && !isSending && !delegating) return new Grid();

        return new HydraSendWorkingBadge(running, isSending || delegating)
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Thickness(3, -3, 0, 0),
        };
    }

    // :31
    private long RunningCount() =>
        Heads().LongCount(head => head.Hydra?.HydraHeadInfoStatus == HydraHeadInfo.Status.Running);

    // :30
    private IReadOnlyList<ChatThread> Heads()
    {
        var model = FindAppModel();
        if (model is null) return [];

        var method = model.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(candidate => candidate.Name == "PanelHeads" &&
                candidate.GetParameters() is [{ ParameterType: var parameterType }] &&
                parameterType == typeof(Guid));

        return method?.Invoke(model, [thread.Id]) is System.Collections.IEnumerable values
            ? values.Cast<object>().OfType<ChatThread>().ToArray()
            : [];
    }

    // :27
    private bool IsOn()
    {
        var model = FindAppModel();
        if (model is not null)
        {
            var method = model.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(candidate => candidate.Name == "HydraIsOn" &&
                    candidate.GetParameters() is [{ ParameterType: var parameterType }] &&
                    parameterType == typeof(ChatThread));
            if (method?.Invoke(model, [thread]) is bool isOn) return isOn;
        }

        return thread.HydraEnabled;
    }

    // :23-24, :90
    private bool HasSeenIntro()
    {
        var model = FindAppModel();
        if (model is null) return false;
        return ReadProperty(model, "Settings") is { } settings &&
            ReadProperty(settings, "HasSeenHydraIntro") is true;
    }

    // :90
    private void SetSetting(string name, object value)
    {
        var model = FindAppModel();
        if (model is null) return;

        var settings = ReadProperty(model, "Settings");
        settings?.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.SetValue(settings, value);
    }

    // :98-100, :176-197
    private string Help(bool isOn, long running, bool hasHeads)
    {
        if (!isOn) return "Hydra is on in Settings";

        var parts = new List<string> { "Hydra is on" };
        if (TryGetPair() is { } pair)
            parts.Add(HydraPairSummary.Workers(pair, GetRegistry()));
        else
            parts.Add("Heads run on this chat's model");

        if (running > 0)
            parts.Add(running == 1 ? "1 head working" : $"{running.ToString(CultureInfo.InvariantCulture)} heads working");

        if (!hasHeads)
        {
            parts.Add("No heads out yet");
            return string.Join(" \U000000B7 ", parts);
        }

        parts.Add(runtime.IsHydraPanelHidden ? "Show the panel" : "Hide the panel");
        return string.Join(" \U000000B7 ", parts);
    }

    // :99, :176-180
    private static string PanelHelp(long running, bool hasHeads)
    {
        if (!hasHeads) return "Hydra is on, no heads out yet";
        if (running == 0) return "Hydra is on, show the panel";

        return running == 1
            ? "Hydra is on, 1 head working, show or hide the panel"
            : $"Hydra is on, {running.ToString(CultureInfo.InvariantCulture)} heads working, show or hide the panel";
    }

    // :185-186
    private HydraPair? TryGetPair()
    {
        var model = FindAppModel();
        if (model is null) return null;

        var method = model.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(candidate =>
                (candidate.Name == "HydraPairFor" || candidate.Name == "HydraPair") &&
                candidate.GetParameters() is [{ ParameterType: var parameterType }] &&
                parameterType == typeof(ChatThread));

        return method?.Invoke(model, [thread]) as HydraPair;
    }

    // :185-186
    private ProviderRegistry GetRegistry()
    {
        var model = FindAppModel();
        return model?.GetType().GetProperty("Providers")?.GetValue(model) as ProviderRegistry
            ?? throw new InvalidOperationException("The Hydra button requires its application provider registry.");
    }

    // :10
    private AppModel? FindAppModel()
    {
        foreach (var field in typeof(ThreadRuntime).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            if (typeof(AppModel).IsAssignableFrom(field.FieldType) && field.GetValue(runtime) is AppModel model)
                return model;

        return null;
    }

    // :106-108
    private void TogglePanel() =>
        Transaction.WithAnimation(Chrome.PanelSlide, () => runtime.IsHydraPanelHidden = !runtime.IsHydraPanelHidden);

    // :113-136
    private static bool IsLeadDelegating(IReadOnlyList<TimelineEntry> entries)
    {
        if (entries.Count == 0) return false;

        var lastTurnID = entries[^1].TurnID;
        for (var index = entries.Count - 1; index >= 0; index--)
        {
            var entry = entries[index];
            if (entry.TurnID != lastTurnID) break;

            var content = ReadProperty(entry.Item, "Content");
            if (content is null) continue;

            var caseName = content.GetType().Name;
            if (caseName.Contains("Tool", StringComparison.Ordinal))
            {
                var call = ReadProperty(content, "Call") ?? content;
                var kind = ReadProperty(call, "Kind");
                var status = ReadProperty(call, "Status");
                if (kind is not null && status is not null &&
                    kind.ToString()?.Contains("Agent", StringComparison.OrdinalIgnoreCase) == true &&
                    status.ToString()?.Contains("Running", StringComparison.OrdinalIgnoreCase) == true)
                    return true;
            }
            else if (caseName.Contains("Assistant", StringComparison.Ordinal))
            {
                var message = ReadProperty(content, "Message") ?? content;
                var text = ReadProperty(message, "Text") as string ?? "";
                var streaming = ReadProperty(message, "IsStreaming") is true;
                var found = streaming
                    ? HasDelegationBlockWhileStreaming(text, entry.Id)
                    : HasDelegationBlock(text, entry.Id);
                if (found) return true;
            }
        }

        return false;
    }

    // :147-152
    private static bool HasDelegationBlock(string text, string rowID)
    {
        if (DelegationChecks.TryGetValue(rowID, out var known)) return known;

        var found = HydraPrompts.HasDelegationBlock(text);
        DelegationChecks.Insert(found, rowID);
        return found;
    }

    // :162-174
    private static bool HasDelegationBlockWhileStreaming(string text, string rowID)
    {
        if (StreamingScans.TryGetValue(rowID, out var known) && known.Found) return true;

        var bytes = Encoding.UTF8.GetBytes(text);
        var scanned = StreamingScans.TryGetValue(rowID, out known) ? known.Scanned : 0;
        if (bytes.Length == scanned) return false;

        var from = bytes.Length < scanned ? 0 : Math.Max(0, scanned - 32);
        var tail = Encoding.UTF8.GetString(bytes, from, bytes.Length - from);
        var found = HydraPrompts.HasDelegationOpener(tail);

        if (StreamingScans.Count > 64) StreamingScans.Clear();
        StreamingScans[rowID] = (bytes.Length, found);
        return found;
    }

    // :121-129
    private static object? ReadProperty(object? instance, string name) =>
        instance?.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(instance);

    // :83-85
    private void ShowIdlePopover()
    {
        if (isExplaining)
        {
            isExplaining = false;
            Child = button;
            return;
        }

        isExplaining = true;
        var pair = TryGetPair();
        Child = new HydraIdlePopover(thread, pair, pair is null ? null : GetRegistry());
    }

    // :86-88
    private void ShowSwitchPopover()
    {
        if (isHydraSwitchShown)
        {
            isHydraSwitchShown = false;
            if (Child is HydraSwitchPopover active)
                Popovers.GetClosePopover(active)?.Invoke();
            Child = button;
            return;
        }

        isHydraSwitchShown = true;

        var model = FindAppModel();
        var settings = model is null ? null : ReadProperty(model, "Settings");
        var settingsEnabled = ReadProperty(settings, "HydraEnabled") is bool enabled && enabled;
        var popover = new HydraSwitchPopover(thread, IsOn(), Binding<bool>.Constant(true));
        popover.Configure(settingsEnabled, IsOn(), enabled =>
        {
            SetHydra(enabled, !settingsEnabled);
            isHydraSwitchShown = false;
            Child = button;
        });
        Child = popover;
    }

    // :89-93
    private void ShowIntroPopover()
    {
        isIntroShown = true;
        Child = new HydraIntroPopover(() =>
        {
            SetSetting("HasSeenHydraIntro", true);
            isIntroShown = false;
            Child = button;
        });
    }

    // :93
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (IsOn() && !HasSeenIntro() && !isIntroShown)
            ShowIntroPopover();
    }

    // :163
    private void SetHydra(bool enabled, bool alsoEnableSettings)
    {
        var model = FindAppModel();
        if (model is null) return;

        if (alsoEnableSettings)
        {
            var settings = ReadProperty(model, "Settings");
            settings?.GetType().GetProperty("HydraEnabled", BindingFlags.Instance | BindingFlags.Public)
                ?.SetValue(settings, true);
        }

        var method = model.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(candidate => candidate.Name == "SetHydra" &&
                candidate.GetParameters() is [{ ParameterType: var firstType }, { ParameterType: var secondType }] &&
                firstType == typeof(bool) && secondType == typeof(Guid));
        method?.Invoke(model, [enabled, thread.Id]);
    }
}

/// <summary>:200-230 <c>private struct HydraSwitchPopover: View</c>.</summary>
internal sealed class HydraSwitchPopover : Decorator
{
    private bool settingsEnabled = true;
    private bool isOn;
    private Action<bool>? setHydra;

    // :204-206, :208-229
    public HydraSwitchPopover(ChatThread thread, bool isOn, Binding<bool> isHydraSwitchShown)
    {
        this.isOn = isOn;
        Build();
    }

    // :208-229
    internal void Configure(bool appEnabled, bool isOn, Action<bool> setHydraAction)
    {
        settingsEnabled = appEnabled;
        this.isOn = isOn;
        setHydra = setHydraAction;
        Build();
    }

    // :208-229
    private void Build()
    {
        var content = new StackPanel { Spacing = 4 };

        content.Children.Add(new PopoverSectionHeader(settingsEnabled
            ? isOn ? "Hydra is on for this chat" : "Hydra is off for this chat"
            : "Hydra is off in Settings"));

        if (!settingsEnabled)
        {
            content.Children.Add(new PopoverItem("Turn Hydra on", () =>
            {
                setHydra?.Invoke(true);
                Close();
            }));
        }
        else
        {
            content.Children.Add(new PopoverItem("Turn Hydra on for this chat", () =>
            {
                setHydra?.Invoke(true);
                Close();
            }, isChecked: isOn));
            content.Children.Add(new PopoverItem("Turn Hydra off for this chat", () =>
            {
                setHydra?.Invoke(false);
                Close();
            }, isChecked: !isOn));
        }

        Child = new PopoverMenu(() => content);

        void Close()
        {
            var close = Popovers.GetClosePopover(this);
            close?.Invoke();
        }
    }
}

/// <summary>:232-250 <c>private struct HydraRightTap: NSViewRepresentable</c>.</summary>
internal sealed class HydraRightTap : Decorator
{
    // :241, :243-249
    public HydraRightTap(Action action)
    {
        Child = new HydraRightTapHost(action);
    }
}

/// <summary>:252-294 <c>private final class HydraRightTapHost: NSView</c>.</summary>
internal sealed class HydraRightTapHost : Control
{
    // :253
    public Action Action { get; set; }

    // :256-259
    public HydraRightTapHost(Action action)
    {
        Action = action;
        IsHitTestVisible = false;
    }
}

/// <summary>:296-319 <c>private struct HydraIdlePopover: View</c>.</summary>
internal sealed class HydraIdlePopover : Decorator
{
    // :301, :303-318
    public HydraIdlePopover(ChatThread thread) : this(thread, null, null)
    {
    }

    // :301, :303-318
    public HydraIdlePopover(ChatThread thread, HydraPair? pair, ProviderRegistry? registry)
    {
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new PopoverSectionHeader("Hydra is on"));

        if (pair is not null && registry is not null)
        {
            content.Children.Add(new PopoverNote(HydraPairSummary.Title(pair, registry)));
            content.Children.Add(new PopoverNote(HydraPairSummary.Workers(pair, registry)));
        }
        else
        {
            content.Children.Add(new PopoverNote("Heads run on this chat's model and effort."));
        }

        content.Children.Add(new PopoverDivider());
        content.Children.Add(new PopoverNote("No heads are out yet. Ask for something big enough to split up and this chat sends them out, each with a panel of its own."));
        content.Children.Add(new PopoverDivider());
        content.Children.Add(new PopoverNote("Right-click the mark to switch Hydra off for this chat."));

        Child = new PopoverMenu(() => content);
        Width = 290;
    }
}

/// <summary>:321-331 <c>private struct HydraMarkView: View</c>.</summary>
internal sealed class HydraMarkView : Decorator
{
    // :323-324, :326-330
    public HydraMarkView(bool isOn, bool isHovering)
    {
        Child = new HydraMarkImage
        {
            Width = 18,
            Height = 18,
            Opacity = isOn || isHovering ? 1 : 0.6,
        };
    }
}

/// <summary>:333-387 <c>struct HydraCharge: View</c>.</summary>
public sealed class HydraCharge : Decorator
{
    // :344
    internal static ValueList<SwiftColor> Colors => new(HydraRoster.Personas.Take(6).Select(persona =>
        new SwiftColor(persona.Color.Red, persona.Color.Green, persona.Color.Blue, persona.Color.Alpha)));

    // :346
    internal static ValueList<SwiftColor> LayerColors => Colors;

    // :342, :348-358
    public HydraCharge(bool isWorking = true)
    {
        var reduceMotion = EnvironmentValues.GetAccessibilityReduceMotion(this);
        Child = isWorking && !reduceMotion
            ? new HydraChargeMotion()
            : new Grid
            {
                IsHitTestVisible = false,
                Children =
                {
                    Glow(false),
                    Ring(false),
                },
            };
        IsHitTestVisible = false;
    }

    // :360-372
    public static Control Glow(bool breathing) =>
        new Ellipse
        {
            Margin = new Thickness(2),
            Opacity = breathing ? 1 : 0.6,
            Fill = new RadialGradientBrush
            {
                GradientStops =
                [
                    new GradientStop(WithAlpha(Chrome.Accent.Resolve(ThemeVariant.Default), 0.46), 0),
                    new GradientStop(WithAlpha(Chrome.Accent.Resolve(ThemeVariant.Default), 0), 1),
                ],
            },
        };

    // :374-386
    public static Control Ring(bool turned, bool bright = false)
    {
        var ring = new Ellipse
        {
            Margin = new Thickness(1),
            StrokeThickness = 2,
            Opacity = bright ? 1 : 0.85,
            Stroke = RingBrush(),
        };
        if (turned) ring.RenderTransform = new RotateTransform(360);
        return ring;
    }

    // :364-368, :376-381
    private static Color WithAlpha(Color color, double opacity) =>
        Color.FromArgb((byte)Math.Round(color.A * opacity), color.R, color.G, color.B);

    // :377-381
    private static LinearGradientBrush RingBrush()
    {
        var colors = Colors;
        var stops = new GradientStops();
        for (var index = 0; index < colors.Count; index++)
            stops.Add(new GradientStop(colors[index].Resolve(ThemeVariant.Default), index / 6.0));
        if (colors.Count > 0)
            stops.Add(new GradientStop(colors[0].Resolve(ThemeVariant.Default), 1));

        return new LinearGradientBrush { GradientStops = stops };
    }
}

/// <summary>:389-411 <c>private struct HydraChargeMotion: NSViewRepresentable</c>.</summary>
internal sealed class HydraChargeMotion : Decorator
{
    // :397-410
    public HydraChargeMotion()
    {
        var view = new HydraChargeLayerView();
        view.Configure(HydraCharge.LayerColors, Chrome.Accent);
        Child = view;
    }
}

/// <summary>:413-613 <c>final class HydraChargeLayerView: NSView</c>.</summary>
public sealed class HydraChargeLayerView : Control
{
    private const string TurnKey = "turn"; // :428
    private const string BreathKey = "breathe"; // :429
    private const float RingRestOpacity = 0.85f; // :431
    private const float GlowRestOpacity = 0.6f; // :432
    private const double TurnDuration = 4; // :433
    private const double BreathDuration = 1.4; // :434
    private const double LineWidth = 2; // :435
    private const double GlowStartRadius = 2; // :437
    private const double GlowEndRadius = 15; // :438

    private IReadOnlyList<SwiftColor> colors = Array.Empty<SwiftColor>();
    private SwiftColor accent = SwiftColor.ControlAccentColor;

    // :440-480
    public HydraChargeLayerView()
    {
        IsHitTestVisible = false;
    }

    // :493-501
    public void Configure(IReadOnlyList<SwiftColor> colors, SwiftColor accent)
    {
        var changed = !ColorsEqual(this.colors, colors) || !ColorEqual(this.accent, accent);
        this.colors = colors.ToArray();
        this.accent = accent;
        if (changed) InvalidateVisual();
    }

    // :535-562, :568-579
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = Bounds;
        if (bounds.Width <= 4 || bounds.Height <= 4) return;

        var reduceMotion = EnvironmentValues.GetAccessibilityReduceMotion(this);
        var glowOpacity = reduceMotion ? GlowRestOpacity : 1;
        var ringOpacity = reduceMotion ? RingRestOpacity : 1;

        var glowRect = new Rect(2, 2, bounds.Width - 4, bounds.Height - 4);
        var centre = glowRect.Center;
        var accentColor = accent.Resolve(ThemeVariant.Default);
        var stops = new GradientStops
        {
            new GradientStop(WithAlpha(accentColor, 0.46), 0),
            new GradientStop(WithAlpha(accentColor, 0), 1),
        };
        var brush = new RadialGradientBrush
        {
            GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(GlowEndRadius / glowRect.Width, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(GlowEndRadius / glowRect.Height, RelativeUnit.Relative),
            GradientStops = stops,
        };

        using (context.PushOpacity(glowOpacity))
            context.DrawEllipse(brush, null, centre, glowRect.Width / 2, glowRect.Height / 2);

        var radius = Math.Min(bounds.Width, bounds.Height) / 2 - 1;
        var count = Math.Min(6, colors.Count);
        if (count == 0) return;

        using (context.PushOpacity(ringOpacity))
        {
            const int segments = 72;
            for (var i = 0; i < segments; i++)
            {
                var a = i * 2 * Math.PI / segments;
                var b = (i + 1) * 2 * Math.PI / segments;
                var x1 = bounds.Center.X + radius * Math.Cos(a);
                var y1 = bounds.Center.Y + radius * Math.Sin(a);
                var x2 = bounds.Center.X + radius * Math.Cos(b);
                var y2 = bounds.Center.Y + radius * Math.Sin(b);
                var colorIndex = i * count / segments;
                var color = colors[colorIndex].Resolve(ThemeVariant.Default);
                context.DrawLine(new Pen(new SolidColorBrush(color), LineWidth), new Point(x1, y1), new Point(x2, y2));
            }
        }
    }

    // :493-497
    private static bool ColorsEqual(IReadOnlyList<SwiftColor> first, IReadOnlyList<SwiftColor> second)
    {
        if (first.Count != second.Count) return false;
        for (var index = 0; index < first.Count; index++)
            if (first[index].Resolve(ThemeVariant.Default) != second[index].Resolve(ThemeVariant.Default))
                return false;
        return true;
    }

    // :493-497
    private static bool ColorEqual(SwiftColor first, SwiftColor second) =>
        first.Resolve(ThemeVariant.Default) == second.Resolve(ThemeVariant.Default);

    // :576-577
    private static Color WithAlpha(Color color, double opacity) =>
        Color.FromArgb((byte)Math.Round(color.A * opacity), color.R, color.G, color.B);
}

/// <summary>:615-646 <c>private struct HydraSendWorkingBadge: View</c>.</summary>
internal sealed class HydraSendWorkingBadge : Decorator
{
    // :621-622, :624-645
    public HydraSendWorkingBadge(long running, bool isSending)
    {
        Control content = isSending
            ? new MiniSpinner(cellSize: 2.4)
            : new TextBlock
            {
                Text = running.ToString(CultureInfo.InvariantCulture),
                FontSize = 10,
                FontWeight = FontWeight.Bold,
                FontFamily = new FontFamily("fonts:Inter#Inter"),
                Foreground = Brushes.White,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            };

        Child = new Border
        {
            MinWidth = 14,
            MinHeight = 14,
            Padding = new Thickness(4, 1),
            CornerRadius = new CornerRadius(999),
            Background = (isSending ? Chrome.GlassTint : Chrome.Accent).Brush(this),
            Child = content,
        };
    }
}

/// <summary>:648-711 <c>enum HydraPairSummary</c>: one-line summaries of a pair.</summary>
public static class HydraPairSummary
{
    // :652-657
    public static string? WorkerModel(HydraPair pair, ProviderRegistry registry)
    {
        if (pair.WorkerModel is { } model)
            return registry.Model(model, pair.HeadsProvider)?.ShortName ?? model;

        if (!pair.SendsHeadsElsewhere) return null;
        return registry.DefaultModel(pair.HeadsProvider)?.ShortName;
    }

    // :661-673
    public static string Workers(HydraPair pair, ProviderRegistry registry)
    {
        var parts = new List<string>();
        if (WorkerModel(pair, registry) is { } model)
            parts.Add("Heads on " + model + (pair.SendsHeadsElsewhere ? $" ({pair.HeadsProvider.DisplayName})" : ""));
        else if (pair.SendsHeadsElsewhere)
            parts.Add($"Heads on {pair.HeadsProvider.DisplayName}");
        else
            parts.Add("Heads on the chat's model");

        if (pair.WorkerEffort is { } effort)
            parts.Add(ModelOption.EffortTitle(effort) + " effort");

        return string.Join(" \U000000B7 ", parts);
    }

    // :676-678
    public static string Providers(HydraPair pair) =>
        pair.SendsHeadsElsewhere
            ? $"{pair.Provider.DisplayName} lead, {pair.HeadsProvider.DisplayName} heads"
            : pair.Provider.DisplayName;

    // :683-693
    public static string? Efforts(HydraPair pair)
    {
        var lead = pair.OrchestratorEffort is { } leadEffort
            ? SwiftString.Lowercased(ModelOption.EffortTitle(leadEffort))
            : null;
        var heads = pair.WorkerEffort is { } headsEffort
            ? SwiftString.Lowercased(ModelOption.EffortTitle(headsEffort))
            : null;

        if (lead is not null && heads is not null)
        {
            if (SwiftString.Equal(lead, heads))
                // :687 lead.capitalized: each word of the lowercased effort title (including "extra high").
                // UNVERIFIED O-07: invariant TextInfo word boundaries outside the pinned effort titles may
                // differ from NSString.capitalized; all catalog titles, including "extra high", agree.
                return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(lead) + " effort";
            return $"Lead {lead}, heads {heads} effort";
        }

        if (lead is not null) return $"Lead {lead} effort";
        if (heads is not null) return $"Heads {heads} effort";
        return null;
    }

    // :697-704
    public static string ModelsTitle(HydraPair pair, ProviderRegistry registry)
    {
        var lead = pair.OrchestratorModel is { } model
            ? registry.Model(model, pair.Provider)?.ShortName ?? model
            : $"Any {pair.Provider.DisplayName} model";

        var worker = WorkerModel(pair, registry) ?? (pair.SendsHeadsElsewhere ? pair.HeadsProvider.DisplayName : null);
        return worker is null ? $"{lead} + itself" : $"{lead} + {worker}";
    }

    // :707-710
    public static string Title(HydraPair pair, ProviderRegistry registry) =>
        pair.CustomName ?? ModelsTitle(pair, registry);
}
