using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Views.Composer;
using Marrow.UI.Port;
using Avalonia.Threading;
using Marrow.App;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// Shared setup for the FollowUpQueueTab coverage tests: a real offline ThreadRuntime (same shape as
/// FollowUpQueueTabTests.MakeRuntimeFixture), a way to put prompts straight into its queue (EnqueueFollowUp would
/// start a turn on an idle runtime), and a window to show the tab in. Test-only.
/// </summary>
public static class FollowUpQueueTabRig
{
    /// <param name="nativeHead">Adds a running native Hydra head to the thread, so HasWorkingNativeHeads is true.</param>
    public static (AppModel App, ThreadRuntime Runtime, AppModelHarness Store) Make(bool nativeHead = false)
    {
        var store = new AppModelHarness();
        var project = AppModelSamples.Project("Follow-ups", store.NewFolder("follow-ups"));
        var thread = AppModelSamples.Thread(project);
        if (nativeHead)
        {
            var head = new ChatThread(project.Id, ProviderKind.Codex, null, null, RuntimeMode.Supervised)
            {
                ParentThreadID = thread.Id,
                IsInPanel = true,
                Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Running) with
                {
                    HydraHeadInfoKind = HydraHeadInfo.Kind.Native,
                    HydraHeadInfoStatus = HydraHeadInfo.Status.Running,
                },
            };
            store.WriteLibrary([project], [thread, head]);
        }
        else
        {
            store.WriteLibrary([project], [thread]);
        }
        AppModelHarness.WriteDocument(thread.Id);
        var app = store.MakeModel();
        var runtime = store.Main.OnMain(() => new ThreadRuntime(thread.Id, app));
        return (app, runtime, store);
    }

    /// <summary>Replaces the queue with exactly these prompts (the setter is private; the public mutators start turns).</summary>
    public static void Seed(ThreadRuntime runtime, params FollowUpPrompt[] prompts) =>
        typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.FollowUps))!
            .SetValue(runtime, new ValueList<FollowUpPrompt>(prompts));

    /// <summary>Phase has a private setter; Starting and Running both make IsRunning true.</summary>
    public static void SetPhase(ThreadRuntime runtime, RuntimePhase phase) =>
        typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Phase))!.SetValue(runtime, phase);

    public static FollowUpPrompt Prompt(string text, Guid? bundle = null, params Attachment[] attachments) => new()
    {
        Text = text,
        BundleID = bundle,
        Attachments = new ValueList<Attachment>(attachments),
    };

    public static Attachment Image(string name) => new() { Name = name, Path = Path.Combine(Path.GetTempPath(), "followup-rig-missing", name), MimeType = "image/png" };

    public static Attachment File(string name) => new() { Name = name, Path = Path.Combine(Path.GetTempPath(), "followup-rig-missing", name), MimeType = "text/plain" };

    public static AvaloniaWindow Show(Control content, double width = 600, double height = 500)
    {
        var window = new AvaloniaWindow { Content = content, Width = width, Height = height };
        window.Show();
        Settle(window);
        return window;
    }

    public static void Settle(TopLevel window)
    {
        for (var pass = 0; pass < 6; pass++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>The prompt rows in the order the tab shows them.</summary>
    public static List<FollowUpRow> Rows(Visual tab) => tab.GetVisualDescendants().OfType<FollowUpRow>().ToList();

    /// <summary>The title line: the first button in the tab.</summary>
    public static Button TitleButton(Visual tab) => tab.GetVisualDescendants().OfType<Button>().First();

    /// <summary>The row's one horizontal line: number or link, grip, thumbnails, text, spacer, buttons.</summary>
    public static StackPanel RowLine(FollowUpRow row) => row.GetVisualDescendants().OfType<StackPanel>().First();

    public static SwiftImage Grip(FollowUpRow row) =>
        row.GetVisualDescendants().OfType<SwiftImage>().First(icon => icon.SymbolName == "line.3.horizontal");

    /// <summary>The prompt text, or the attachment summary, as the row shows it.</summary>
    public static TextBlock ProseOf(FollowUpRow row) =>
        row.GetVisualDescendants().OfType<TextBlock>().Last();

    public static List<QueueIconButton> Buttons(FollowUpRow row) =>
        row.GetVisualDescendants().OfType<QueueIconButton>().ToList();

    public static Point Center(Visual control, Visual ancestor) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ancestor)!.Value;

    /// <summary>The control's rectangle in the window, render transforms included.</summary>
    public static Rect RectIn(Visual control, Visual ancestor) => new(
        control.TranslatePoint(new Point(0, 0), ancestor)!.Value,
        control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), ancestor)!.Value);

    /// <summary>A brush's colour with the brush's own opacity folded into the alpha.</summary>
    public static Color Effective(IBrush? brush)
    {
        if (brush is null) return Color.FromArgb(0, 0, 0, 0);
        var solid = (ISolidColorBrush)brush;
        return Color.FromArgb((byte)Math.Round(solid.Color.A * solid.Opacity), solid.Color.R, solid.Color.G, solid.Color.B);
    }

    public static string[] Texts(ThreadRuntime runtime) => runtime.FollowUps.Select(prompt => prompt.Text).ToArray();
}
