using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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
        Guid? headId = null;
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
            headId = head.Id;
            store.WriteLibrary([project], [thread, head]);
        }
        else
        {
            store.WriteLibrary([project], [thread]);
        }
        AppModelHarness.WriteDocument(thread.Id);
        var app = store.MakeModel();
        var runtime = store.Main.OnMain(() => new ThreadRuntime(thread.Id, app));
        if (headId is { } running)
            store.Main.OnMain(() => app.UpdateThread(running, current => current with
            {
                Hydra = current.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running },
            }));
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
    public static Grid RowLine(FollowUpRow row) =>
        row.GetVisualDescendants().OfType<Grid>().First(grid => grid.ColumnDefinitions.Count == 6);

    /// <summary>The first cell of the line: the number, the unlink button or the plain link glyph.</summary>
    public static Control Leading(FollowUpRow row) => RowLine(row).Children.First(child => Grid.GetColumn(child) == 0);

    public static SwiftImage Grip(FollowUpRow row) =>
        row.GetVisualDescendants().OfType<SwiftImage>().First(icon => icon.SymbolName == "line.3.horizontal");

    /// <summary>The prompt text, or the attachment summary, as the row shows it.</summary>
    public static TextBlock ProseOf(FollowUpRow row) =>
        (TextBlock)RowLine(row).Children.First(child => Grid.GetColumn(child) == 3);

    /// <summary>The rule along the bottom of the row.</summary>
    public static Border RuleOf(FollowUpRow row) =>
        row.GetVisualDescendants().OfType<Border>().First(border => border.Height == 1);

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

    public static void RunTab(HeadlessSession headless, FollowUpPrompt[] prompts, Action<FollowUpTabRun> body,
        double width = 600, bool nativeHead = false)
    {
        headless.OnUiThread(() =>
        {
            var (app, runtime, store) = Make(nativeHead);
            using var storeDisposable = store;
            using var frames = FrameClock.Source.Override(new ManualFrameSource()); // thumbnails fade in on frames
            // The window of the current event is what anchors a popover hung from a rect (WindowRectAnchor.Target); the
            // watcher subscribes on first use, and a headless session has no active-window fallback, so subscribe first.
            _ = AppCurrentEvent.CurrentWindow;
            var run = new FollowUpTabRun(app, runtime, store);
            run.Step(() =>
            {
                Seed(runtime, prompts);
                run.Tab = new FollowUpQueueTab(runtime);
                run.Window = Show(run.Tab, width);
            });
            body(run);
            run.Step(() => run.Window.Close());
        });
    }

    /// <summary>A row on its own in a window, with no tab around it.</summary>
    public static FollowUpRow RowOf(FollowUpPrompt prompt, ThreadRuntime runtime, int position = 1, bool showsRule = false,
        bool bundledWithPrevious = false, bool bundledWithNext = false) => new(
        position, prompt, runtime, isDragged: false, showsRule: showsRule, isBundledWithPrevious: bundledWithPrevious,
        isBundledWithNext: bundledWithNext, isPairTarget: false, isPairing: false, onDragChanged: _ => { }, onDragEnded: () => { });

    /// <summary>A real, valid one-pixel PNG on disk, so an image attachment can be decoded and previewed.</summary>
    public static Attachment RealImage(string name)
    {
        var folder = Path.Combine(Path.GetTempPath(), "followup-rig-images");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        System.IO.File.WriteAllBytes(path, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="));
        return new Attachment { Name = name, Path = path, MimeType = "image/png" };
    }
}

/// <summary>
/// A tab shown in a window over a real offline runtime. The main loop runs posted observation work (Tracked,
/// .onChange) only when a store.Main.OnMain body returns, so a test is a series of <see cref="Step"/>s: the writes of
/// one step have been observed by the views when the next one starts.
/// </summary>
public sealed class FollowUpTabRun(AppModel app, ThreadRuntime runtime, AppModelHarness store)
{
    public AppModel App { get; } = app;
    public ThreadRuntime Runtime { get; } = runtime;
    public FollowUpQueueTab Tab { get; set; } = null!;
    public AvaloniaWindow Window { get; set; } = null!;

    public void Step(Action action) => store.Main.OnMain(action);

    /// <summary>A real left click at a point in the window, then everything the click made the views do.</summary>
    public void Click(Point point) => Step(() =>
    {
        Window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        Window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
    });

    public void Click(Visual control) => Click(FollowUpQueueTabRig.Center(control, Window));

    public void Settle() => Step(() => FollowUpQueueTabRig.Settle(Window));

    public List<FollowUpRow> Rows => FollowUpQueueTabRig.Rows(Tab);
}
