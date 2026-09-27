using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Usage;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Usage;

[Collection("Headless")]
public sealed class UsageViewsReviewTests(HeadlessSession headless)
{
    private static PlanLimits.ResetCredits Credits(long count=1) => new() {AvailableCount=count,Credits=new ValueList<PlanLimits.ResetCredit>([new() {Id="credit",Title="A very long banked reset title",Detail="A very long banked reset detail"}])};
    private static PlanLimits.Window Meter(double percent=50,bool reset=true) => new() {Id="window",Title="A very long window title",Detail="A very long window detail",Percent=percent,ResetsAt=reset ? SwiftDate.Now.AddHours(1) : null};
    private static void Publish(ProviderRegistry registry,PlanLimits value) => typeof(ProviderRegistry).GetProperty("PlanLimits")!.SetValue(registry,ValueMap<ProviderKind,PlanLimits>.Empty.Mutating(d=>d[ProviderKind.Zai]=value));
    private static void Phase(BankedResetsView view,BankedResetsView.Phase phase) => typeof(BankedResetsView).GetMethod("SetPhase",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(view,[phase]);
    private static string[] Text(Control view) => LogicalQuery.SelfAndDescendants<TextBlock>(view).Select(t=>t.Text!).ToArray();
    private static void Drain() => Dispatcher.UIThread.RunJobs();

    private void WithLimits(Action<AppModel,PlanLimitsView> body) => headless.OnUiThread(()=> {
        using var harness=new AppModelHarness(); using var actor=MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        using var frames=FrameClock.Source.Override(new ManualFrameSource());
        var model=new AppModel(); Publish(model.Providers,new PlanLimits {Windows=new ValueList<PlanLimits.Window>([Meter()]),PlanLimitsResetCredits=Credits(),Problem="Long API key problem text"});
        var view=new PlanLimitsView(ProviderKind.Zai); var host=new Border {Child=view}; AppEnvironment.SetAppModel(host,model);
        var window=new Avalonia.Controls.Window {Width=400,Height=600,Content=host};
        try {window.Show();Drain();body(model,view);} finally {window.Close();Drain();}
    });

    [Fact]
    [Trait("Req", "PR-USAGE-041")] // App/Views/Usage/UsageViews.swift:32-39,217-218,293-307
    public void Registry_refresh_keeps_the_confirming_reset_view_and_latest_credits() => WithLimits((model,view)=> {
        var resets=LogicalQuery.SelfAndDescendants<BankedResetsView>(view).Single();
        LogicalQuery.SelfAndDescendants<Button>(resets).Single(b=>b.Content as string=="Use reset").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Drain();
        Assert.Contains("Reset now",LogicalQuery.SelfAndDescendants<Button>(resets).Select(b=>b.Content as string));
        Publish(model.Providers,new PlanLimits {Windows=new ValueList<PlanLimits.Window>([Meter(10)]),PlanLimitsResetCredits=Credits(2)});Drain();
        Assert.Same(resets,LogicalQuery.SelfAndDescendants<BankedResetsView>(view).Single());
        Assert.Contains("· 2 available",Text(resets)); Assert.Contains("Reset now",LogicalQuery.SelfAndDescendants<Button>(resets).Select(b=>b.Content as string));
    });

    [Fact]
    [Trait("Req", "PR-USAGE-042")] // App/Views/Usage/UsageViews.swift:217-218,257,293-307
    [Trait("Req", "PR-USAGE-043")] // App/Views/Usage/UsageViews.swift:260-273
    public void Pending_spend_survives_limits_publish_and_renders_its_outcome() => WithLimits((model,view)=> {
        var resets=LogicalQuery.SelfAndDescendants<BankedResetsView>(view).Single();
        Phase(resets,new BankedResetsView.Phase.Applying("credit"));
        var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var published=false;
        // Stand-in provider completion at the view's phase/task boundary; no HTTP or real CLI is launched.
        var task=MainActor.Spawn(async()=> {await completion.Task;Phase(resets,new BankedResetsView.Phase.Done(PlanLimits.ResetOutcome.Reset));published=true;await SwiftTask.TrySleep(TimeSpan.FromSeconds(4));});
        typeof(BankedResetsView).GetField("settle",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(resets,task); Drain();
        Publish(model.Providers,new PlanLimits {Windows=new ValueList<PlanLimits.Window>([Meter(0)]),PlanLimitsResetCredits=Credits(0)});Drain();
        Assert.Same(resets,LogicalQuery.SelfAndDescendants<BankedResetsView>(view).Single()); Assert.False(task.IsCancelled);
        completion.SetResult();var deadline=DateTime.UtcNow.AddSeconds(5);
        while(!published) {Assert.True(DateTime.UtcNow<deadline);Drain();Thread.Sleep(1);}
        Assert.Contains("Usage was reset.",Text(view));
    });

    [Fact]
    [Trait("Req", "PR-USAGE-026")] // App/Views/Usage/UsageViews.swift:53,150,156,167,328,332
    [Trait("Req", "PR-USAGE-037")] // App/Views/Usage/UsageViews.swift:328
    [Trait("Req", "PR-USAGE-038")] // App/Views/Usage/UsageViews.swift:332
    public void Limited_window_problem_and_banked_reset_text_uses_tail_ellipsis() => WithLimits((_,view)=> {
        var texts=LogicalQuery.SelfAndDescendants<TextBlock>(view).Where(t=>t.MaxLines is 1 or 2).ToArray();
        Assert.Equal(6,texts.Length); Assert.All(texts,t=>Assert.Equal(TextTrimming.CharacterEllipsis,t.TextTrimming));
    });

    [Fact]
    [Trait("Req", "PR-USAGE-054")] // App/Views/Usage/UsageViews.swift:111-116
    public void Credits_amount_has_one_line_tail_ellipsis() => headless.OnUiThread(()=> {
        using var harness=new AppModelHarness();using var actor=MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var model=new AppModel();typeof(ProviderRegistry).GetProperty("Credits")!.SetValue(model.Providers,ValueMap<ProviderKind,ProviderCredits>.Empty.Mutating(d=>d[ProviderKind.Deepseek]=new ProviderCredits {Total=123456789,Currency="USD",IsUsable=true}));
        var view=new CreditsView(ProviderKind.Deepseek);var host=new Border {Child=view};AppEnvironment.SetAppModel(host,model);var window=new Avalonia.Controls.Window {Content=host};
        try {window.Show();Drain();var amount=LogicalQuery.SelfAndDescendants<TextBlock>(view).Single(t=>t.MaxLines==1);Assert.Equal(TextTrimming.CharacterEllipsis,amount.TextTrimming);}finally {window.Close();Drain();}
    });

    [Theory]
    [InlineData(74.9,"accent")] [InlineData(75,"warning")] [InlineData(89.9,"warning")] [InlineData(90,"danger")]
    [Trait("Req", "PR-USAGE-029")] // App/Views/Usage/UsageViews.swift:173-177
    public void Tint_thresholds_are_inclusive(double percent,string color) => headless.OnUiThread(()=> {
        var row=new LimitRow(Meter(percent));var bar=LogicalQuery.SelfAndDescendants<UsageBar>(row).Single();var fill=(Border)((Grid)bar.Child!).Children[1];
        var expected=color switch {"danger"=>Chrome.Danger,"warning"=>Chrome.Warning,_=>Chrome.Accent};
        Assert.Equal(expected.Resolve(ThemeVariant.Light),Assert.IsType<SolidColorBrush>(fill.Background).Color);
    });

    [Theory]
    [InlineData(0,"· None available yet")] [InlineData(1,"· 1 available")] [InlineData(3,"· 3 available")]
    [Trait("Req", "PR-USAGE-035")] // App/Views/Usage/UsageViews.swift:285-291
    public void Reset_count_text_obeys_the_three_source_cases(long count,string expected) => headless.OnUiThread(()=>Assert.Contains(expected,Text(new BankedResetsView(ProviderKind.Zai,Credits(count)))));

    [Theory]
    [InlineData(PlanLimits.ResetOutcome.Reset,"checkmark.circle.fill",true)]
    [InlineData(PlanLimits.ResetOutcome.NothingToReset,"info.circle",false)]
    [InlineData(PlanLimits.ResetOutcome.NoCredit,"info.circle",false)]
    [InlineData(PlanLimits.ResetOutcome.AlreadyRedeemed,"info.circle",false)]
    [Trait("Req", "PR-USAGE-043")] // App/Views/Usage/UsageViews.swift:260-273
    public void Reset_outcome_selects_the_source_symbol_and_color(PlanLimits.ResetOutcome outcome,string symbol,bool success) => headless.OnUiThread(()=> {
        var view=new BankedResetsView(ProviderKind.Zai,Credits());Phase(view,new BankedResetsView.Phase.Done(outcome));
        var glyph=LogicalQuery.SelfAndDescendants<SwiftImage>(view).Single(i=>i.SymbolName==symbol);
        Assert.Equal((success ? Chrome.Success : Chrome.SecondaryText).Resolve(ThemeVariant.Light),Assert.IsType<SolidColorBrush>(glyph.Foreground).Color);
    });

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [Trait("Req", "PR-USAGE-053")] // App/Views/Usage/UsageViews.swift:130-138
    public void Credits_note_follows_the_four_state_ladder(int scenario) => headless.OnUiThread(()=> {
        ProviderCredits? balance=scenario<2 ? null : new ProviderCredits {Total=scenario==2 ? 0:2,Currency="USD",IsUsable=true,ToppedUp=scenario==3 ? 2:null};
        var actual=typeof(CreditsView).GetMethod("Note",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(new CreditsView(ProviderKind.Deepseek),[balance,scenario==0]);
        var expected=scenario switch {0=>null,1=>"DeepSeek did not report a balance.",2=>"DeepSeek declines turns until the balance is topped up.",3=>"Topped up $2.00",_=>null};Assert.Equal(expected,actual);
    });

    [Fact]
    [Trait("Req", "PR-USAGE-026")] // App/Views/Usage/UsageViews.swift:146-162
    public void Header_without_reset_date_keeps_both_twelve_point_spacer_gaps() => headless.OnUiThread(()=> {
        var row=new LimitRow(Meter(reset:false));var heading=(Grid)((StackPanel)row.Child!).Children[0];heading.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
        var title=(TextBlock)heading.Children[0];var percent=(TextBlock)heading.Children[1];
        var plain=new TextBlock {Text=title.Text};SwiftFont.Body.Apply(plain);plain.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
        Assert.Equal(plain.DesiredSize.Width+32+percent.DesiredSize.Width,heading.DesiredSize.Width,1);
    });
}
