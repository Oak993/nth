using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Testing;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// Test-owned helpers; no edits to the other TimelineRows job's tests.
internal static class TimelineRowsPrSupport
{
    internal const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static object? Invoke(object target, string member, params object?[] args) =>
        target.GetType().GetMethod(member, Hidden)!.Invoke(target, args);
    internal static object? Field(object target, string name) => target.GetType().GetField(name, Hidden)!.GetValue(target);
    internal static T Property<T>(object target, string name) => (T)target.GetType().GetProperty(name)!.GetValue(target)!;
    internal static ThreadRuntimePart12Harness Harness()
    {
        Task.Run(() => LoginEnvironment.Load()).GetAwaiter().GetResult();
        return new ThreadRuntimePart12Harness();
    }
    internal static AvaloniaWindow Show(Control content)
    {
        var window = new AvaloniaWindow { Width = 900, Height = 700,
            Content = Layers(content) };
        window.Show();
        Settle(window);
        window.KeyPress(Key.F13, RawInputModifiers.None, PhysicalKey.F13, null);
        return window;
    }
    private static Control Layers(Control content)
    {
        var layers = new VisualLayerManager { EnableOverlayLayer = true, Child = content };
        // Avalonia 12.1.2 exposes its separate popup layer internally, without an XML-doc entry.
        typeof(VisualLayerManager).GetProperty("EnablePopupOverlayLayer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(layers, true);
        return layers;
    }
    internal static void Settle(AvaloniaWindow window)
    {
        for (var i = 0; i < 4; i++) { window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
    }
    internal static void Close(AvaloniaWindow window)
    {
        window.Content = null; window.Close(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }
    internal static void Click(AvaloniaWindow window, Control target)
    {
        Settle(window);
        var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("Target is not mounted");
        window.MouseMove(point, RawInputModifiers.None);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Settle(window);
    }
    internal static void Hover(AvaloniaWindow window, Control target)
    {
        var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("Target is not mounted");
        window.MouseMove(point, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }
    internal static TimelineEntry Reply(string text) => new(new TimelineItem(null,
        new TimelineItem.Content.Assistant(new AssistantMessage { Text = text })));
    internal static TimelineEntry Tool(ToolCall.Kind kind = ToolCall.Kind.Command, ToolCall.Status status = ToolCall.Status.Completed,
        ValueList<FileEdit>? edits = null) => new(new TimelineItem(null, new TimelineItem.Content.Tool(
            new ToolCall { ToolCallKind = kind, ToolCallStatus = status, Title = "Read file", Edits = edits ?? ValueList<FileEdit>.Empty })));
    internal static ToolCall Call(ToolCall.Kind kind, string title, ToolCall.Status status = ToolCall.Status.Completed) =>
        new() { ToolCallKind = kind, Title = title, ToolCallStatus = status };
    internal static TimelineEntry ToolEntry(ToolCall call, Guid? turn = null) =>
        new(new TimelineItem(turn, new TimelineItem.Content.Tool(call)));
    internal static TimelineEntry Plan() => new(new TimelineItem(null, new TimelineItem.Content.Plan(new ProposedPlan
        { Markdown = "plan", ProposedPlanState = ProposedPlan.State.Accepted })));
    internal static TimelineEntry MakeNotice(Notice.Level level) => new(new TimelineItem(null,
        new TimelineItem.Content.Notice(new Notice { Message = level.ToString(), NoticeLevel = level })));
    internal static TimelineEntry NoticeEntry(Notice.Level level, string message) => new(new TimelineItem(null,
        new TimelineItem.Content.Notice(new Notice { Message = message, NoticeLevel = level })));
    internal static TurnSummary Summary(Guid turn) => new() { TurnID = turn, Status = TurnStatus.Completed, Duration = 5, FilesChanged = 0, Additions = 0, Deletions = 0 };
    internal static TimelineEntry UserEntry(string text, Guid? turn, ValueList<Attachment>? attachments = null) =>
        new(new TimelineItem(turn, new TimelineItem.Content.User(new UserMessage { Text = text, Attachments = attachments ?? ValueList<Attachment>.Empty })));
    // The point sizes a selectable text is drawn at: its own size and every run's, its monospaced code runs aside (inline code reads
    // in its own face and size: StreamVeil.swift:309-338 .body monospaced in a Text; LinkParagraphView.swift:117 pointSize - 1).
    internal static double[] DrawnProseSizes(SelectableTextBlock text)
    {
        var mono = SwiftFont.System(SwiftFont.TextStyle.Body, design: SwiftFont.Design.Monospaced).Resolve().FontFamily;
        return (text.Inlines ?? []).OfType<Avalonia.Controls.Documents.Run>()
            .Where(run => !Equals(run.FontFamily, mono) && run.Text is { Length: > 0 })
            .Select(run => run.FontSize).Append(text.FontSize).Distinct().ToArray();
    }
    // SwiftUI's easeOut is the cubic Bezier (0, 0, 0.58, 1): the progress at a fraction of the duration, found independently of the port's
    // own timing code (x(t) solved by bisection, then y(t)). Chrome.hover is `.easeOut(duration: 0.1)` (Chrome.swift).
    internal static double HoverEaseOut(double fraction)
    {
        static double Bezier(double t, double p1, double p2) => 3 * (1 - t) * (1 - t) * t * p1 + 3 * (1 - t) * t * t * p2 + t * t * t;
        double low = 0, high = 1;
        for (var step = 0; step < 60; step++)
        {
            var mid = (low + high) / 2;
            if (Bezier(mid, 0, 0.58) < fraction) low = mid; else high = mid;
        }
        return Bezier((low + high) / 2, 0, 1);
    }
    // How far along the straight line from <paramref name="from"/> to <paramref name="to"/> a colour has got (0 at from, 1 at to), all four channels.
    internal static double Along(Color from, Color to, Color now)
    {
        double[] a = [from.A, from.R, from.G, from.B], b = [to.A, to.R, to.G, to.B], c = [now.A, now.R, now.G, now.B];
        double dot = 0, length = 0;
        for (var i = 0; i < 4; i++) { dot += (c[i] - a[i]) * (b[i] - a[i]); length += (b[i] - a[i]) * (b[i] - a[i]); }
        return dot / length;
    }
    internal static Color Resolved(SwiftColor color, Control on) => color.Resolve(on.ActualThemeVariant);
    internal static Color BrushColor(IBrush? brush) => (brush as ISolidColorBrush)!.Color;
    internal static string[] Texts(Control control) =>
        control.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text ?? "").ToArray();
    internal static TextBlock TextNamed(Control root, string text) =>
        root.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == text);
    internal static TimelineItem.Content.Todos Steps(params (string Text, TodoStep.Status Status)[] steps) =>
        new(new ValueList<TodoStep>(steps.Select(step => new TodoStep { Text = step.Text, TodoStepStatus = step.Status })));
    internal static TimelineEntry PlanEntry(ProposedPlan.State state, string markdown = "```\nstep one\n```", Guid? turn = null) =>
        new(new TimelineItem(turn, new TimelineItem.Content.Plan(new ProposedPlan { Markdown = markdown, ProposedPlanState = state })));
}

// Real overlay popups keep hit testing and pointer input in the headless window.
internal sealed class TimelineRowsPrPopupPresenter : IPopoverPresenter, IDisposable
{
    internal List<(Popup Popup, bool Presented)> Calls { get; } = [];
    public void SetPresented(Popup popup, bool presented)
    {
        Calls.Add((popup, presented));
        popup.ShouldUseOverlayLayer = true;
        popup.IsOpen = presented;
    }
    public void Dispose()
    {
        foreach (var popup in Calls.Select(call => call.Popup).Distinct()) popup.IsOpen = false;
    }
}

// Counts the real Derived constructor's walks without adding counters or callbacks to production.
internal sealed class TimelineRowsPrCountedEntries : IList<TimelineEntry>
{
    private readonly List<TimelineEntry> entries;
    internal int Derivations { get; private set; }
    internal TimelineRowsPrCountedEntries(IEnumerable<TimelineEntry> entries) => this.entries = entries.ToList();
    internal ValueList<TimelineEntry> AsValueList()
    {
        var result = new ValueList<TimelineEntry>(entries);
        typeof(ReadOnlyCollection<TimelineEntry>).GetField("list", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(result, this);
        return result;
    }
    public IEnumerator<TimelineEntry> GetEnumerator()
    {
        var caller = new StackTrace().GetFrames().Select(frame => frame.GetMethod())
            .FirstOrDefault(method => method?.DeclaringType?.Assembly == typeof(Marrow.App.Views.Chat.TurnFinishedBlock).Assembly);
        if (caller?.DeclaringType?.Name == "Derived" && caller.Name == ".ctor") Derivations++;
        return entries.GetEnumerator();
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public TimelineEntry this[int index] { get => entries[index]; set => entries[index] = value; }
    public int Count => entries.Count;
    public bool IsReadOnly => false;
    public void Add(TimelineEntry item) => entries.Add(item);
    public void Clear() => entries.Clear();
    public bool Contains(TimelineEntry item) => entries.Contains(item);
    public void CopyTo(TimelineEntry[] array, int index) => entries.CopyTo(array, index);
    public int IndexOf(TimelineEntry item) => entries.IndexOf(item);
    public void Insert(int index, TimelineEntry item) => entries.Insert(index, item);
    public bool Remove(TimelineEntry item) => entries.Remove(item);
    public void RemoveAt(int index) => entries.RemoveAt(index);
}
