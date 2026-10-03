using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using static Marrow.App.Tests.Views.Composer.EffortSliderModelListTests;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/EffortSlider.swift (B3 52bc792), :166-179 (a pair is chosen), :237-258 (a model is chosen).
// The scenarios drive the real panel: the title is tapped, the row is pressed with the pointer, and what the app then holds is read back.
[Collection("Headless")]
public sealed class EffortSliderModelChoiceTests(HeadlessSession headless)
{
    private static ChatThread Chat(ProviderKind provider, string model, string? effort, bool fast = false, Guid? pair = null, string? session = null, string? resume = null, bool hydra = false) =>
        new(Guid.NewGuid(), provider, model, effort, RuntimeMode.Supervised, fast)
        {
            HydraEnabled = hydra || pair is not null, HydraPairID = pair, ProviderSessionID = session, ProviderResumeAt = resume,
        };

    private static void Pump(Env env) { env.Step(10); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }

    /// <summary>Opens the list in the panel and presses the row by the name it shows; the 0.12 s cross-fade then runs and the panel is back on its slider.</summary>
    private static void ChooseRow(Env env, ModelEffortPanel panel, Avalonia.Controls.Window window, string name)
    {
        if (!panel.GetVisualDescendants().OfType<ModelList>().Any()) OpenList(env, panel);
        Click(window, ModelRow(panel, name));
        env.Step(130);
    }

    private static ThreadRuntime RunningTurn(Env env)
    {
        AppModelHarness.WriteDocument(env.Thread.Id);
        var runtime = env.Model.Runtime(env.Thread.Id);
        var load = runtime.EnsureLoaded(); // a turn rehearsed before the document has loaded is marked interrupted by the load
        for (var i = 0; i < 2000 && !load.IsCompleted; i++) { Dispatcher.UIThread.RunJobs(); System.Threading.Thread.Sleep(5); }
        Assert.True(load.IsCompleted);
        runtime.RehearseTurn("a turn that is running");
        Assert.True(runtime.IsRunning);
        return runtime;
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-149")] // App/Views/Composer/EffortSlider.swift:242-257
    public void Choosing_a_model_on_another_provider_clears_the_session_leaves_the_pair_stops_the_turn_and_carries_the_stored_preference() => headless.OnUiThread(() =>
    {
        var pair = new HydraPair(ProviderKind.Claude) { OrchestratorModel = "opus", Name = "Led" };
        using var env = Make(() => Chat(ProviderKind.Claude, "opus", "high", pair: pair.Id, session: "session-1", resume: "resume-1"), model =>
        {
            model.Settings.HydraEnabled = true; model.Settings.AddHydraPair(pair);
            model.Settings.AddToModelList(Pin(ProviderKind.Claude, "opus")); model.Settings.AddToModelList(Pin(ProviderKind.Codex, "cx"));
            // the stored preference of the model that will be chosen: an effort it offers and the fast flag
            model.Settings.SetPreference(new ModelPreference { Effort = "high", FastMode = true }, ProviderKind.Codex, "cx");
        }, (ProviderKind.Codex, [Option("cx", "Cx", fast: true)]));
        var runtime = RunningTurn(env);
        // every change the app and its settings announce, each with what the chat holds at that moment: the order the source writes them in
        var seen = new List<string>();
        void Note(string what) => seen.Add($"{what}:{env.Model.Thread(env.Thread.Id)!.Model}/{env.Model.Thread(env.Thread.Id)!.HydraPairID is null}/{env.Model.Settings.DefaultProvider.Id}");
        env.Model.PropertyChanged += (_, change) => Note("model." + change.PropertyName);
        env.Model.Settings.PropertyChanged += (_, change) => Note("settings." + change.PropertyName);

        var panel = new ModelEffortPanel(env.Thread.Id, false);
        var window = env.Show(panel);
        Assert.Single(panel.GetVisualDescendants().OfType<EffortSliderCard>());
        seen.Clear();
        ChooseRow(env, panel, window, "Cx");
        Pump(env);

        var after = env.Model.Thread(env.Thread.Id)!;
        Assert.Equal(ProviderKind.Codex, after.Provider); Assert.Equal("cx", after.Model);
        Assert.Equal("high", after.Effort); Assert.True(after.FastMode); // the stored preference, as the model offers it
        Assert.Null(after.ProviderSessionID); Assert.Null(after.ProviderResumeAt); // a provider change drops the session id and the resume point
        Assert.Null(after.HydraPairID); // a model row is the choice of a model alone: the chat leaves the pair
        Assert.Equal("cx", env.Model.Settings.LastModel(ProviderKind.Codex)); Assert.Equal("high", env.Model.Settings.LastEffort(ProviderKind.Codex));
        Assert.Equal(ProviderKind.Codex, env.Model.Settings.DefaultProvider);
        Assert.False(runtime.IsRunning); // the running session was stopped: its turn ended
        Assert.Equal(TurnStatus.Interrupted, Assert.Single(runtime.Turns).Status);
        // the order: the thread takes the model first (the pair still led), then leaves the pair, and only then are the model remembered and the default set
        var model = seen.FindIndex(note => note.StartsWith("model.", StringComparison.Ordinal) && note.Contains(":cx/False/", StringComparison.Ordinal));
        var left = seen.FindIndex(note => note.StartsWith("model.", StringComparison.Ordinal) && note.Contains(":cx/True/", StringComparison.Ordinal));
        var remembered = seen.FindIndex(note => note.StartsWith("settings.", StringComparison.Ordinal) && note.Contains(":cx/True/", StringComparison.Ordinal));
        Assert.True(model >= 0 && left > model, $"the model is set, then the pair left: {string.Join(" | ", seen)}");
        Assert.True(remembered > left, $"remembered after the pair is left: {string.Join(" | ", seen)}");
        // the list gave way to the slider again
        Assert.Empty(panel.GetVisualDescendants().OfType<ModelList>());
        Assert.Single(panel.GetVisualDescendants().OfType<EffortSliderCard>());
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-149")] // App/Views/Composer/EffortSlider.swift:243,248-256: a same-provider choice keeps the session, the stored effort is dropped when not offered, fast needs the tier and the preference
    public void Choosing_a_model_on_the_same_provider_keeps_the_session_and_the_turn_and_drops_what_the_model_does_not_offer() => headless.OnUiThread(() =>
    {
        using var env = Make(() => Chat(ProviderKind.Codex, "cx1", "low", session: "session-1", resume: "resume-1"), model =>
        {
            foreach (var id in new[] { "cx1", "cx2", "cx3", "cx4" }) model.Settings.AddToModelList(Pin(ProviderKind.Codex, id));
            model.Settings.SetPreference(new ModelPreference { Effort = "max", FastMode = true }, ProviderKind.Codex, "cx2");  // an effort the model does not offer; fast the model has no tier for
            model.Settings.SetPreference(new ModelPreference { Effort = "low", FastMode = false }, ProviderKind.Codex, "cx3"); // valid, fast off although the model has the tier
            model.Settings.SetPreference(new ModelPreference { Effort = "high", FastMode = true }, ProviderKind.Codex, "cx4"); // valid, fast on and the tier
        }, (ProviderKind.Codex, [Option("cx1", "Cx1"), Option("cx2", "Cx2", efforts: true), Option("cx3", "Cx3", fast: true), Option("cx4", "Cx4", fast: true)]));
        var runtime = RunningTurn(env);
        var panel = new ModelEffortPanel(env.Thread.Id, false);
        var window = env.Show(panel);
        ChooseRow(env, panel, window, "Cx2");
        var chat = env.Model.Thread(env.Thread.Id)!;
        Assert.Equal("cx2", chat.Model); Assert.Null(chat.Effort); Assert.False(chat.FastMode); // "max" is not among its efforts; no fast tier
        Assert.Equal("session-1", chat.ProviderSessionID); Assert.Equal("resume-1", chat.ProviderResumeAt); // the provider did not change
        Assert.True(runtime.IsRunning); Assert.Equal(TurnStatus.Running, runtime.Turns[0].Status); // and the running session was left alone
        Assert.Null(env.Model.Settings.LastEffort(ProviderKind.Codex)); Assert.Equal("cx2", env.Model.Settings.LastModel(ProviderKind.Codex));
        ChooseRow(env, panel, window, "Cx3");
        chat = env.Model.Thread(env.Thread.Id)!;
        Assert.Equal("cx3", chat.Model); Assert.Equal("low", chat.Effort); Assert.False(chat.FastMode); // fast is the preference's, and the preference says off
        ChooseRow(env, panel, window, "Cx4");
        chat = env.Model.Thread(env.Thread.Id)!;
        Assert.Equal("cx4", chat.Model); Assert.Equal("high", chat.Effort); Assert.True(chat.FastMode); // a valid effort and the fast flag, carried
        Assert.Equal("high", env.Model.Settings.LastEffort(ProviderKind.Codex)); Assert.Equal(ProviderKind.Codex, env.Model.Settings.DefaultProvider);
        Assert.True(runtime.IsRunning);
    });

    private static HydraPair LeadPair(string name = "Lead") =>
        new(ProviderKind.Codex) { OrchestratorModel = "lead", OrchestratorEffort = "high", Name = name };

    private static Env PairEnv(Func<ChatThread> thread, HydraPair pair) =>
        Make(thread, model =>
        {
            model.Settings.HydraEnabled = true; model.Settings.AddHydraPair(pair);
            model.Settings.AddToModelList(Pin(ProviderKind.Codex, "lead")); model.Settings.AddToModelList(Pin(ProviderKind.Codex, "other"));
        }, (ProviderKind.Codex, [Option("lead", "Lead model"), Option("other", "Other model")]));

    [Fact]
    [Trait("Req", "PR-COMPOSER-150")] // App/Views/Composer/EffortSlider.swift:166-179; App/AppModel+HydraPairs.swift:29-52
    public void Choosing_a_pair_enters_it_twice_the_same_and_closes_the_whole_popover_a_locked_pair_does_nothing_and_the_chip_dismisses() => headless.OnUiThread(() =>
    {
        var pair = LeadPair();
        using (var env = PairEnv(() => Chat(ProviderKind.Codex, "other", "low", hydra: true), pair))
        {
            var dismissed = 0;
            var panel = OpenPanelList(env, false, out var window, () => dismissed++);
            Click(window, Pairs(panel).Single());
            env.Step(130);
            // entering the pair settles the model, the effort and Hydra in one go; the popover is dismissed, not returned to the slider
            var entered = env.Model.Thread(env.Thread.Id)!;
            Assert.Equal(pair.Id, entered.HydraPairID); Assert.Equal("lead", entered.Model); Assert.Equal("high", entered.Effort); Assert.True(entered.HydraEnabled);
            Assert.Equal(1, dismissed);
            Assert.Empty(panel.GetVisualDescendants().OfType<ModelList>());
            // the slider or an edit moves the chat off the lead; tapping the pair the chat already leads settles them again, and dismisses again
            env.Model.UpdateThread(env.Thread.Id, chat => chat with { Model = "other", Effort = "low" });
            Dispatcher.UIThread.RunJobs();
            OpenList(env, panel);
            Assert.Equal(1, Tick(Pairs(panel).Single())); // the pair is the one led
            Click(window, Pairs(panel).Single());
            env.Step(130);
            var again = env.Model.Thread(env.Thread.Id)!;
            Assert.Equal(pair.Id, again.HydraPairID); Assert.Equal("lead", again.Model); Assert.Equal("high", again.Effort);
            Assert.Equal(2, dismissed);
        }

        // a pair on another provider than a chat with history: its row is locked, a press does nothing and the popover stays
        var foreign = new HydraPair(ProviderKind.Claude) { OrchestratorModel = "opus", Name = "Foreign" };
        using (var env = PairEnv(() => Chat(ProviderKind.Codex, "other", "low", hydra: true), foreign))
        {
            var dismissed = 0;
            var panel = OpenPanelList(env, true, out var window, () => dismissed++);
            Assert.False(RowButton(Pairs(panel).Single()).IsEnabled);
            Click(window, Pairs(panel).Single());
            env.Step(130);
            Assert.Null(env.Model.Thread(env.Thread.Id)!.HydraPairID); Assert.Equal("other", env.Model.Thread(env.Thread.Id)!.Model);
            Assert.Equal(0, dismissed);
            Assert.Single(panel.GetVisualDescendants().OfType<ModelList>());
        }

        // end to end through the real chip: its popover is open, the title is tapped, the pair is chosen, and the chip's popover is closed
        using (var env = PairEnv(() => Chat(ProviderKind.Codex, "other", "low", hydra: true), pair))
        {
            var presenter = new RecordingPresenter(); using var presentation = Presentation.Presenter.Override(presenter);
            var chip = new ModelEffortButton(env.Thread, false);
            var window = env.Show(chip);
            var button = Assert.Single(chip.GetVisualDescendants().OfType<Button>());
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.True(presenter.Calls[^1].Presented);
            var popup = presenter.Calls[^1].Popup;
            var content = Assert.IsAssignableFrom<Control>(popup.Child);
            popup.Child = null; // the headless presenter shows no popup: the content is hosted in the window in its place
            var host = new Border { Child = content };
            AppEnvironment.SetAppModel(host, env.Model);
            window.Content = host; Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var panel = Assert.Single(content.GetVisualDescendants().OfType<ModelEffortPanel>());
            OpenList(env, panel);
            var closes = presenter.Calls.Count(call => !call.Presented);
            Click(window, Pairs(panel).Single());
            env.Step(130);
            Assert.False(presenter.Calls[^1].Presented);
            Assert.Same(popup, presenter.Calls[^1].Popup);
            Assert.Equal(closes + 1, presenter.Calls.Count(call => !call.Presented)); // dismissed once
            Assert.Equal(pair.Id, env.Model.Thread(env.Thread.Id)!.HydraPairID);
        }
    });
}
