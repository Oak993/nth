using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

[Collection("Headless")]
public sealed class HydraSettingsPageReviewTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-SETTINGS-146")] // App/Views/Settings/HydraSettingsPage.swift:8-9,38-46,164-166
    [Trait("Req", "PR-SETTINGS-150")] // App/Views/Settings/HydraSettingsPage.swift:50-59
    [Trait("Req", "PR-SETTINGS-157")] // App/Views/Settings/HydraSettingsPage.swift:96,194-200
    public void Page_reacts_to_enabled_checkout_cap_and_blocked_row_changes() => WithView(_=>new HydraSettingsPage(),(view,m)=>
    {
        m.Settings.HydraEnabled=false; Sync();Assert.Contains("Switch Hydra on to set up its heads.",Text(view),StringComparison.Ordinal);
        m.Settings.HydraEnabled=true;m.Settings.HydraIsolateHeads=false;m.Settings.HydraMaxHeads=2;m.Settings.HydraAlwaysHeads=true;Sync();
        Assert.DoesNotContain("Switch Hydra on to set up its heads.",Text(view),StringComparison.Ordinal);
        Assert.Contains("Heads work in the chat's checkout itself.",Text(view),StringComparison.Ordinal);
        Assert.Contains("At most 2 heads in parallel.",Text(view),StringComparison.Ordinal);
        var row=Rows(view).Single(r=>Text(r).Contains("Queued follow-ups start on heads",StringComparison.Ordinal));
        Assert.False(row.IsEnabled);Assert.Equal(.5,row.Opacity);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-161")] // App/Views/Settings/HydraSettingsPage.swift:246-247,299-317
    public void Existing_pair_row_uses_the_updated_pair_without_reattaching() => WithPair("HydraPairRow",(view,m,pair)=>
    {
        m.Settings.UpdateHydraPair(pair.Id,p=>p with {Name="New name",MaxHeads=2});Sync();
        Assert.Contains("New name",Text(view),StringComparison.Ordinal);
        Assert.Contains("Up to 2 heads",Text(view),StringComparison.Ordinal);
        m.Settings.UpdateHydraPair(pair.Id,p=>p with {Name=null,OrchestratorModel="catalog-lead"});Sync();
        m.Providers.UpdateCatalog(new ValueList<ModelOption>([new ModelOption("catalog-lead","Catalog Leader")]),ProviderKind.Claude);Sync();
        Assert.Contains("Catalog Leader",Text(view),StringComparison.Ordinal);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-166")] // App/Views/Settings/HydraSettingsPage.swift:345-358,371-373,496-505
    public void Existing_editor_uses_the_updated_pair_and_disappears_when_removed() => WithPair("HydraPairEditor",(view,m,pair)=>
    {
        m.Settings.UpdateHydraPair(pair.Id,p=>p with {Name="Updated editor"});Sync();
        Assert.Equal("Updated editor",LogicalQuery.SelfAndDescendants<TextBox>(view).Single().Text);
        typeof(ProviderRegistry).GetProperty("Catalogs")!.SetValue(m.Providers,m.Providers.Catalogs.Mutating(map=>map[ProviderKind.Claude]=ValueList<ModelOption>.Empty));Sync();
        typeof(ProviderRegistry).GetProperty("LoadingCatalogs")!.SetValue(m.Providers,m.Providers.LoadingCatalogs.Mutating(set=>set.Add(ProviderKind.Claude)));Sync();
        Assert.Contains("Loading Claude's models…",Text(view),StringComparison.Ordinal);
        m.Settings.RemoveHydraPair(pair.Id);Sync();Assert.Empty(LogicalQuery.SelfAndDescendants<TextBox>(view));
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-162")] // App/Views/Settings/HydraSettingsPage.swift:227-230
    public void All_taken_installed_providers_fall_back_to_the_first_installed_not_enum_zero() => WithView(_=>new HydraSettingsPage(),(view,m)=>
    {
        foreach(var provider in SwiftEnum<ProviderKind>.AllCases)
            typeof(ProviderRegistry).GetProperty("Statuses")!.SetValue(m.Providers,m.Providers.Statuses.Mutating(map=>map[provider]=new ProviderStatus()));
        foreach(var provider in new[]{ProviderKind.Claude,ProviderKind.Cursor})
        {
            typeof(ProviderRegistry).GetProperty("Statuses")!.SetValue(m.Providers,m.Providers.Statuses.Mutating(map=>map[provider]=new ProviderStatus{Executable=SwiftURL.FileURLWithPath("/fixture/never-launch")}));
            m.Settings.AddHydraPair(new HydraPair(provider));
        }
        Sync();Click(Add(view));Assert.Equal(ProviderKind.Claude,m.Settings.HydraPairs[^1].Provider);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-162")] // App/Views/Settings/HydraSettingsPage.swift:230
    [Trait("Req", "PR-SETTINGS-164")] // App/Views/Settings/HydraSettingsPage.swift:277-288
    public void Add_and_remove_pair_mutations_carry_panel_slide_and_update_the_attached_list() => WithView(_=>new HydraSettingsPage(),(view,m)=>
    {
        Animation? observed=null;
        var (_,tracker)=ObservationTracking.Track(()=>m.Settings.HydraPairs,()=>observed=Transaction.Current);
        Click(Add(view)); Assert.Equal(Chrome.PanelSlide,observed);tracker.Cancel();Sync();
        Assert.DoesNotContain("No pairs yet",Text(view),StringComparison.Ordinal);
        observed=null;var (_,removeTracker)=ObservationTracking.Track(()=>m.Settings.HydraPairs,()=>observed=Transaction.Current);
        var remove=LogicalQuery.SelfAndDescendants<Button>(view).Single(b=>AutomationProperties.GetName(b)=="Remove this pair");
        Click(remove);Assert.Equal(Chrome.PanelSlide,observed);removeTracker.Cancel();Sync();
        Assert.Empty(m.Settings.HydraPairs);Assert.Contains("No pairs yet",Text(view),StringComparison.Ordinal);
    });

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-001")] // App/Views/Settings/HydraSettingsPage.swift:101-106
    [Trait("Req", "UI-HYDRAMERGE-001")] // App/Views/Settings/HydraSettingsPage.swift:101-106
    public void Merge_automatically_row_has_exact_text_default_off_and_a_working_switch() => WithView(_=>new HydraSettingsPage(),(view,m)=>
    {
        var row=Rows(view).Single(r=>Text(r).Contains("Merge automatically",StringComparison.Ordinal));
        Assert.Contains("The team's changes go out as a merge request and are merged.",Text(row),StringComparison.Ordinal);
        Assert.False(m.Settings.HydraAutoMerge);
        var switchControl=LogicalQuery.SelfAndDescendants<SettingsSwitch>(row).Single().Child!;
        var peer=Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(switchControl));
        Assert.Equal(ToggleState.Off,peer.ToggleState);peer.Toggle();Assert.True(m.Settings.HydraAutoMerge);
        Sync();row=Rows(view).Single(r=>Text(r).Contains("Merge automatically",StringComparison.Ordinal));
        switchControl=LogicalQuery.SelfAndDescendants<SettingsSwitch>(row).Single().Child!;
        peer=Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(switchControl));
        Assert.Equal(ToggleState.On,peer.ToggleState);peer.Toggle();Assert.False(m.Settings.HydraAutoMerge);
    });

    private void WithPair(string type,Action<Control,AppModel,HydraPair> check)
    {
        var pair=new HydraPair(ProviderKind.Claude);
        WithView(m=>
        {
            m.Settings.AddHydraPair(pair);
            var unit=typeof(HydraSettingsPage).Assembly.GetType("Marrow.App.Views.Settings."+type)!;
            return (Control)Activator.CreateInstance(unit,type=="HydraPairRow"?new object[]{pair}:new object[]{pair.Id})!;
        },(view,m)=>check(view,m,pair));
    }
    private void WithView(Func<AppModel,Control> create,Action<Control,AppModel> check)=>headless.OnUiThread(()=>
    {
        using var h=new AppModelHarness();var model=h.MakeModel();
        using var actor=MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        using var frames=FrameClock.Source.Override(new ManualFrameSource());
        // Catalog tasks stay on already-attempted stand-in data: never execute a provider CLI.
        var attempted=(HashSet<ProviderKind>)typeof(ProviderRegistry).GetField("attemptedCatalogs",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(model.Providers)!;
        foreach(var provider in SwiftEnum<ProviderKind>.AllCases)attempted.Add(provider);
        var view=create(model);var host=new Border{Child=view};AppEnvironment.SetAppModel(host,model);
        var w=new Avalonia.Controls.Window{Width=900,Height=1800,Content=host};
        try{w.Show();Sync();check(view,model);}
        finally{w.Close();Sync();}
    });
    private static IEnumerable<ChromeRow> Rows(Control c)=>LogicalQuery.SelfAndDescendants<ChromeRow>(c);
    private static string Text(Control c)=>string.Join("\n",LogicalQuery.SelfAndDescendants<TextBlock>(c).Select(t=>t.Text??""));
    private static Button Add(Control c)=>LogicalQuery.SelfAndDescendants<Button>(c).First(b=>Text(b).Contains("Add pair",StringComparison.Ordinal));
    private static void Click(Button b)=>b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Sync()=>Dispatcher.UIThread.RunJobs();
}
