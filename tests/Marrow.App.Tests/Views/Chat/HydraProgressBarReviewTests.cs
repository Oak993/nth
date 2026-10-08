using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Chrome = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Tests.Views.Chat;

[Collection("Headless")]
public sealed class HydraProgressBarReviewTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "UI-HYDRA-028")] // App/Views/Chat/HydraProgressBar.swift:213-243,567-579
    [Trait("Req", "PR-HYDRA-186")] // App/Views/Chat/HydraProgressBar.swift:177,213-243
    public void Hover_card_uses_glass_or_panel_fill_and_fades_in_and_out_over_015() => headless.OnUiThread(() =>
    {
        using var rt=new TestRuntime(); using var main=MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var frames=new ManualFrameSource(); using var clock=FrameClock.Source.Override(frames);
        var b=Bar([Reply("Hello",SwiftDate.Now.AddSeconds(-1))],HydraHeadInfo.Status.Completed);
        var w=Show(b); var card=Field<Border>(b,"card");
        Assert.NotNull(SwiftGlass.GetEffect(card));
        RenderBudget.SetIsOnGlassPanel(b,true); Dispatcher.UIThread.RunJobs();
        Assert.Null(SwiftGlass.GetEffect(card));
        Assert.Equal(Chrome.PanelControlFill(false).Resolve(card.ActualThemeVariant),Assert.IsAssignableFrom<ISolidColorBrush>(card.Background).Color);
        RenderBudget.SetIsOnGlassPanel(b,false); Dispatcher.UIThread.RunJobs();
        Assert.NotNull(SwiftGlass.GetEffect(card));
        w.MouseMove(new Point(1,14)); Dispatcher.UIThread.RunJobs(); rt.RunUntilIdle();
        Assert.True(card.IsVisible); Assert.Equal(0,card.Opacity);
        Tick(rt,frames,.075); Assert.InRange(card.Opacity,.01,.99);
        Tick(rt,frames,.075); Assert.Equal(Animation.Smooth(.15).Progress(.15),card.Opacity,6);
        Tick(rt,frames,Animation.Smooth(.15).Duration-.15); Assert.Equal(1,card.Opacity,6); // ED-092 perceptual duration vs settle
        Assert.Equal("Replied: Hello",Field<TextBlock>(b,"cardLabel").Text);
        Assert.Equal(260,card.Width); Assert.Equal(new Thickness(12,10),card.Padding);
        Assert.False(card.IsHitTestVisible); Assert.Equal(0,card.Margin.Left);
        w.MouseMove(new Point(299,90)); Dispatcher.UIThread.RunJobs();
        Assert.True(card.IsVisible); Assert.Equal(1,card.Opacity,6);
        Tick(rt,frames,.075); Assert.InRange(card.Opacity,.01,.99);
        Tick(rt,frames,.075); Assert.Equal(1-Animation.Smooth(.15).Progress(.15),card.Opacity,6);
        Tick(rt,frames,Animation.Smooth(.15).Duration-.15); Assert.False(card.IsVisible);
        w.Close();
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-192")] // App/Views/Chat/HydraProgressBar.swift:548-554
    [Trait("Req", "UI-HYDRA-031")] // App/Views/Chat/HydraProgressBar.swift:548-561
    public void Running_elapsed_time_ticks_on_the_started_at_grid_without_display_frames() => headless.OnUiThread(() =>
    {
        using var rt=new TestRuntime(); using var main=MainActor.Executor.Override(AvaloniaMainExecutor.Instance); var frames=new ManualFrameSource(); using var clock=FrameClock.Source.Override(frames);
        var start=SwiftDate.Now; rt.Advance(TimeSpan.FromMilliseconds(300));
        var view=new HydraElapsedTime(start,null,true); var w=Show(view);
        var label=Assert.IsType<TextBlock>(view.Child);
        Assert.Equal("0s",label.Text);
        rt.Advance(TimeSpan.FromMilliseconds(690)); Dispatcher.UIThread.RunJobs(); Assert.Equal("0s",label.Text);
        rt.Advance(TimeSpan.FromMilliseconds(10)); Dispatcher.UIThread.RunJobs(); Assert.Equal("1s",label.Text);
        Assert.Equal(0,frames.Pending);
        rt.Advance(TimeSpan.FromSeconds(1)); Dispatcher.UIThread.RunJobs(); Assert.Equal("2s",label.Text);
        w.Close(); Assert.Equal(0,rt.Time.ActiveTimers);
    });

    [Fact]
    [Trait("Req", "UI-HYDRA-031")] // App/Views/Chat/HydraPanel.swift:137 HStack(spacing: 6): the clock beside a 16-point glyph is centred on it
    public void Elapsed_time_is_centred_at_its_own_height_in_a_taller_slot() => headless.OnUiThread(() =>
    {
        var view = new HydraElapsedTime(SwiftDate.Now.AddSeconds(-3), SwiftDate.Now, false);
        var slot = new Grid { Height = 16, Children = { view } };
        var w = Show(slot);
        var label = Assert.IsType<TextBlock>(view.Child);
        // The 14-point line is shorter than the 16-point slot (the strip's glyph); an HStack centres it there, at its own height.
        Assert.Equal(label.DesiredSize.Height, label.Bounds.Height, 0.5);
        Assert.Equal(8, label.TranslatePoint(new Avalonia.Point(0, label.Bounds.Height / 2), slot)!.Value.Y, 0.5);
        w.Close();
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-184")] // App/Views/Chat/HydraProgressBar.swift:186-198,369-388
    [Trait("Req", "PR-HYDRA-185")] // App/Views/Chat/HydraProgressBar.swift:133-144,199-206
    public void Fourth_running_bar_releases_its_frame_request_after_the_055_rise_task() => headless.OnUiThread(() =>
    {
        using var rt=new TestRuntime(); using var main=MainActor.Executor.Override(AvaloniaMainExecutor.Instance); var frames=new ManualFrameSource(); using var clock=FrameClock.Source.Override(frames);
        var blockers=new[]{new object(),new object(),new object()};
        foreach(var id in blockers) SweepBudget.Shared.Join(id);
        var b=Bar([Reply("new",SwiftDate.Now)],HydraHeadInfo.Status.Running); var w=Show(b);
        try
        {
            Assert.True(frames.Pending>0);
            Tick(rt,frames,.225);
            var drawing=Draw(Field<Control>(b,"bar"));
            var stave=drawing.Single(d=>d.Geometry!.Bounds.Width==3);
            Assert.Equal(28*.875,stave.Geometry!.Bounds.Height,5); // cubic ease-out at .5 = .875
            Tick(rt,frames,.325); rt.RunUntilIdle(); Dispatcher.UIThread.RunJobs();
            frames.Advance(TimeSpan.Zero); // ED-117 (6): drain the single stale request of a paused schedule.
            Assert.Equal(0,frames.Pending);
            Assert.Equal(28,Draw(Field<Control>(b,"bar")).Single(d=>d.Geometry!.Bounds.Width==3).Geometry!.Bounds.Height,5);
        }
        finally {w.Close();foreach(var id in blockers) SweepBudget.Shared.Leave(id);rt.RunUntilIdle();}
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-183")] // App/Views/Chat/HydraProgressBar.swift:123-126,340-344
    [Trait("Req", "PR-HYDRA-189")] // App/Views/Chat/HydraProgressBar.swift:393-399
    public void Capacity_keeps_latest_staves_and_only_the_final_failed_stave_is_danger() => headless.OnUiThread(() =>
    {
        var entries=Enumerable.Range(0,5).Select(i=>Reply(i.ToString(System.Globalization.CultureInfo.InvariantCulture),SwiftDate.Now.AddSeconds(-2))).ToArray();
        var b=Bar(entries,HydraHeadInfo.Status.Failed);
        b.Measure(new Size(15,100));b.Arrange(new Rect(0,0,15,28));
        var result=typeof(HydraProgressBar).GetMethod("Resolve",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(b,null)!;
        var staves=(IEnumerable)result.GetType().GetField("Item2")!.GetValue(result)!;
        Assert.Equal(new[]{"Replied: 2","Replied: 3","Replied: 4"},staves.Cast<object>().Select(s=>(string)s.GetType().GetProperty("Step")!.GetValue(s)!));
        var draws=Draw(Field<Control>(b,"bar"));
        var danger=draws.Single(d=>Assert.IsAssignableFrom<ISolidColorBrush>(d.Brush).Color==Chrome.Danger.Resolve(b.ActualThemeVariant));
        Assert.Equal(12,danger.Geometry!.Bounds.Left); Assert.Equal(3,danger.Geometry!.Bounds.Width);
        var stopped=Bar(entries,HydraHeadInfo.Status.Stopped); stopped.Measure(new Size(15,100));stopped.Arrange(new Rect(0,0,15,28));
        Assert.Contains(Draw(Field<Control>(stopped,"bar")),d=>Assert.IsAssignableFrom<ISolidColorBrush>(d.Brush).Color==Chrome.SecondaryText.Resolve(stopped.ActualThemeVariant)&&d.Geometry!.Bounds.Left==12);
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-188")] // App/Views/Chat/HydraProgressBar.swift:61-120
    public void Cache_hits_identity_and_misses_tool_title_status_and_capacity() => headless.OnUiThread(() =>
    {
        var type=typeof(HydraProgressBar).GetNestedType("StaveCache",BindingFlags.NonPublic)!;
        var cache=Activator.CreateInstance(type,true)!; var resolve=type.GetMethod("Resolve")!;
        var call=new ToolCall{ToolCallKind=ToolCall.Kind.Edit,Title="Edit one",ToolCallStatus=ToolCall.Status.Running};
        var entry=new TimelineEntry(new TimelineItem(null,new TimelineItem.Content.Tool(call)));
        object Events(long cap)=>resolve.Invoke(cache,new object[]{new[]{entry},cap})!.GetType().GetField("Item1")!.GetValue(resolve.Invoke(cache,new object[]{new[]{entry},cap}))!;
        var first=Events(4); Assert.Same(first,Events(4));
        entry.Item=entry.Item with {TimelineItemContent=new TimelineItem.Content.Tool(call with {Title="Edit two"})};
        var title=Events(4);Assert.NotSame(first,title);
        entry.Item=entry.Item with {TimelineItemContent=new TimelineItem.Content.Tool(call with {Title="Edit two",ToolCallStatus=ToolCall.Status.Completed})};
        var status=Events(4);Assert.NotSame(title,status);Assert.NotSame(status,Events(3));
    });

    [Fact]
    [Trait("Req", "UI-HYDRA-030")] // App/Views/Chat/HydraProgressBar.swift:516-528: the title is Chrome.primaryText in the current appearance
    public void A_finished_working_title_takes_the_new_appearance_primary_text_when_the_theme_changes() => headless.OnUiThread(() =>
    {
        // Lead (native gallery, 2026-10-08): a stopped title does not tick, so after Dark -> Light it kept white text on the light panel.
        using var rt=new TestRuntime(); using var main=MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var frames=new ManualFrameSource();using var clock=FrameClock.Source.Override(frames);
        var view=new HydraWorkingTitle("Task",false);
        var label=LogicalQuery.SelfAndDescendants<TextBlock>(view).Single();
        Assert.IsType<Grid>(view.Child).Children.Clear();
        var w=Show(view);
        w.RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Dark; Dispatcher.UIThread.RunJobs();
        Assert.Equal(Chrome.PrimaryText.Resolve(Avalonia.Styling.ThemeVariant.Dark),Assert.IsType<LinearGradientBrush>(label.Foreground).GradientStops[0].Color);
        w.RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Light; Dispatcher.UIThread.RunJobs();
        var light=Assert.IsType<LinearGradientBrush>(label.Foreground);
        Assert.All(light.GradientStops,stop=>Assert.Equal(Chrome.PrimaryText.Resolve(Avalonia.Styling.ThemeVariant.Light),stop.Color));
        w.Close();
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-191")] // App/Views/Chat/HydraProgressBar.swift:491-534
    [Trait("Req", "UI-HYDRA-030")] // App/Views/Chat/HydraProgressBar.swift:500-534
    public void Working_title_breathes_and_moves_the_darker_gradient_band() => headless.OnUiThread(() =>
    {
        using var rt=new TestRuntime(); using var main=MainActor.Executor.Override(AvaloniaMainExecutor.Instance); var time=new ManualTimeProvider(new DateTimeOffset(2001,1,1,0,0,0,TimeSpan.Zero));
        using var date=SwiftClock.Provider.Override(time);
        var frames=new ManualFrameSource();using var clock=FrameClock.Source.Override(frames);
        var view=new HydraWorkingTitle("Task",true);
        var label=LogicalQuery.SelfAndDescendants<TextBlock>(view).Single();
        Assert.Equal(FontWeight.Medium,label.FontWeight);
        // Attach the timeline host without glyph layout; this headless backend cannot resolve Inter Medium.
        // The source callback still styles its original label, whose font is asserted above.
        Assert.IsType<Grid>(view.Child).Children.Clear();
        var w=Show(view);
        Assert.Equal(.93,label.Opacity,6);
        time.Advance(TimeSpan.FromSeconds(.8));frames.Advance(TimeSpan.FromSeconds(.8));
        Assert.Equal(1,label.Opacity,6);
        var gradient=Assert.IsType<LinearGradientBrush>(label.Foreground);
        Assert.Equal(.1,gradient.GradientStops[2].Offset,6); // -.3 + .25*1.6
        Assert.Equal((byte)(Chrome.PrimaryText.Resolve(label.ActualThemeVariant).A*.6),gradient.GradientStops[2].Color.A);
        w.Close();Assert.Equal(0,frames.Pending);
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-184")] // App/Views/Chat/HydraProgressBar.swift:423-445
    public void Sweep_crosses_the_filled_width_at_13_seconds_then_rests_from_26_to_6() => headless.OnUiThread(() =>
    {
        using var rt=new TestRuntime();using var main=MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var time=new ManualTimeProvider(new DateTimeOffset(2001,1,1,0,0,0,TimeSpan.Zero));using var date=SwiftClock.Provider.Override(time);
        var frames=new ManualFrameSource();using var clock=FrameClock.Source.Override(frames);
        var b=Bar(Enumerable.Range(0,5).Select(_=>Reply("reply",SwiftDate.Now.AddSeconds(-2))).ToArray(),HydraHeadInfo.Status.Running);
        var w=Show(b);
        try
        {
            time.Advance(TimeSpan.FromMilliseconds(1300));Dispatcher.UIThread.RunJobs();frames.Advance(TimeSpan.FromMilliseconds(1300));
            var sweep=Draw(Field<Control>(b,"bar")).Single(d=>d.Brush is LinearGradientBrush);
            Assert.Equal(new Rect(1.5,0,24,28),sweep.Geometry!.Bounds); // filled width 5*6-3=27, band24, midpoint1.5
            var stops=Assert.IsType<LinearGradientBrush>(sweep.Brush).GradientStops;
            Assert.Equal(new[]{0d,.5,1},stops.Select(s=>s.Offset));
            Assert.Equal((byte)140,stops[1].Color.A); // Swift white.opacity(.55), rounded byte
            time.Advance(TimeSpan.FromMilliseconds(1300));Dispatcher.UIThread.RunJobs();frames.Advance(TimeSpan.FromMilliseconds(1300));
            Assert.DoesNotContain(Draw(Field<Control>(b,"bar")),d=>d.Brush is LinearGradientBrush);
        }
        finally {w.Close();}
    });

    private static TimelineEntry Reply(string text,DateTimeOffset date)=>new(new TimelineItem(null,new TimelineItem.Content.Assistant(new AssistantMessage{Text=text}),date:date));
    private static HydraProgressBar Bar(IReadOnlyList<TimelineEntry> entries,HydraHeadInfo.Status status)
    {
        var runtime=(ThreadRuntime)RuntimeHelpers.GetUninitializedObject(typeof(ThreadRuntime));
        typeof(ThreadRuntime).GetField("entries",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(runtime,ValueList<TimelineEntry>.Empty);
        return new(runtime,SwiftDate.Now,null,status,new SwiftColor(1,0,0),entries);
    }
    private static T Field<T>(object obj,string name)=>(T)obj.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(obj)!;
    private static Avalonia.Controls.Window Show(Control c){var w=new Avalonia.Controls.Window{Width=300,Height=100,Content=c};w.Show();Dispatcher.UIThread.RunJobs();return w;}
    private static void Tick(TestRuntime rt,ManualFrameSource frames,double seconds){var span=TimeSpan.FromTicks((long)Math.Round(seconds*TimeSpan.TicksPerSecond));rt.Advance(span);frames.Advance(span);rt.RunUntilIdle();Dispatcher.UIThread.RunJobs();}
    private static GeometryDrawing[] Draw(Control c){var group=new DrawingGroup();using(var context=group.Open())c.Render(context);return Flatten(group).ToArray();}
    private static IEnumerable<GeometryDrawing> Flatten(Drawing d){if(d is GeometryDrawing g)yield return g;if(d is DrawingGroup group)foreach(var child in group.Children)foreach(var item in Flatten(child))yield return item;}
}
