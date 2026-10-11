// PORT-ONLY: MA-8 (T11 step 2). A part of Settings > Providers (the accounts, OD-0139) or Settings > Pills in a headless window over the foundations' real stores (in memory) and a recording sign-in: no CLI, network or model. The window's content is the plain panel the app's
// windows have; time and frames can be taken by hand for the transition tests (the pattern of LibraryPageHarness).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port.MultiAccount.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.Testing.MultiAccountSettings;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Tests.Fixtures;

internal sealed class MultiAccountHarness : IDisposable
{
    private readonly List<IDisposable> scopes = [];
    private readonly bool runOut;

    private MultiAccountHarness(HeadlessSession headless, AccountsRig rig, Func<Control> make, bool motion, bool reduceMotion, Func<ProviderKind, IReadOnlyList<ModelOption>>? models)
    {
        Headless = headless;
        Rig = rig;
        Time = new ManualTimeProvider();
        scopes.Add(SwiftClock.Provider.Override(Time));
        scopes.Add(ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = false }));
        runOut = !motion;
        Frames = new ManualFrameSource();
        scopes.Add(FrameClock.Source.Override(Frames));
        var context = new MultiAccountContext(rig.Model, rig.PillsModel, rig.Editor, models ?? (_ => []), () => OpenedProviders++);
        scopes.Add(MultiAccountSettingsSeams.Context.Override(new MultiAccountOffer(_ => context)));
        Page = make();
        var margin = new Border { Padding = new Thickness(24), Child = Page };
        Root = new Panel { Children = { margin } };
        Window = new Avalonia.Controls.Window { Content = Root, Width = 620, Height = 760 };
        if (reduceMotion) EnvironmentValues.SetAccessibilityReduceMotion(Window, true);
        Window.Show();
        Settle();
    }

    public HeadlessSession Headless { get; }
    public AccountsRig Rig { get; }
    public ManualTimeProvider Time { get; }
    public ManualFrameSource Frames { get; }
    public Control Page { get; }
    public Panel Root { get; }
    public Avalonia.Controls.Window Window { get; }
    public int OpenedProviders { get; private set; }

    public static void Run(HeadlessSession headless, AccountsRig rig, Func<Control> make, Action<MultiAccountHarness> check, bool motion = false, bool reduceMotion = false, Func<ProviderKind, IReadOnlyList<ModelOption>>? models = null)
    {
        void Body()
        {
            using var harness = new MultiAccountHarness(headless, rig, make, motion, reduceMotion, models);
            check(harness);
            harness.Settle();
        }
        if (HeadlessSession.IsInBody) Body();
        else headless.OnUiThread(Body);
    }

    public void Settle()
    {
        for (var i = 0; i < 4; i++)
        {
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            if (!runOut) continue;
            var second = TimeSpan.FromSeconds(1);
            Time.Advance(second);
            Frames.Advance(second);
        }
    }

    /// <summary>Waits until <paramref name="condition"/> holds, pumping the dispatcher.</summary>
    public void Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The condition never held.");
            Thread.Sleep(5);
            Settle();
        }
        Settle();
    }

    public void Step(double milliseconds)
    {
        var by = TimeSpan.FromMilliseconds(milliseconds);
        Time.Advance(by);
        Frames.Advance(by);
        Window.UpdateLayout();
    }

    public IEnumerable<Control> All() => Window.GetVisualDescendants().OfType<Control>();

    // ED-502: use only when asserting a retained control is hidden. Find stays visible-only.
    public T FindIncludingHidden<T>(string id) where T : Control =>
        All().OfType<T>().Single(c => AutomationProperties.GetAutomationId(c) == id);

    public Control? TryFind(string id) => All().FirstOrDefault(c => AutomationProperties.GetAutomationId(c) == id && c.IsEffectivelyVisible);

    public T Find<T>(string id) where T : Control => (T?)TryFind(id) as T ?? throw new InvalidOperationException("No visible control with the id '" + id + "'.");

    public IEnumerable<Control> Find(Func<string, bool> id) =>
        All().Where(c => AutomationProperties.GetAutomationId(c) is { Length: > 0 } found && id(found) && c.IsEffectivelyVisible);

    /// <summary>Presses a button the way a click does; false when it is disabled or hidden.</summary>
    public bool Press(string id)
    {
        if (TryFind(id) is not Button { IsEffectivelyEnabled: true } button) return false;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle();
        return true;
    }

    /// <summary>The words a person can see: visible text blocks with some opacity (a crossfade keeps its outgoing text at 0 until the next change).</summary>
    public IReadOnlyList<string> Words(Control? root = null) =>
        (root ?? Window).GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text) && t.GetSelfAndVisualAncestors().OfType<Visual>().Aggregate(1.0, (a, v) => a * v.Opacity) > 0.01)
            .Select(t => t.Text!).ToList();

    public void Dispose()
    {
        Window.Close();
        Dispatcher.UIThread.RunJobs();
        for (var i = scopes.Count - 1; i >= 0; i--) scopes[i].Dispose();
    }
}
