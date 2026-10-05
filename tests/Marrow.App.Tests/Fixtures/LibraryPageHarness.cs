// PORT-ONLY: SK-15 (DL-17). A real Plugins or Skills page in a headless window over a fake backend and a scripted install seam: the catalogue is Marrow-made, nothing downloads, no data root or network is touched. The window's content is the plain
// panel the app's windows have (the in-window sheet layer needs it), the page sits in the 24 point margin the Settings window gives it, and time and frames can be taken by hand for the transition tests.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port.Settings;
using Marrow.App.Port.Skills.Pages;
using Marrow.Core.Port;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Services.Port.Skills.Pages;
using Marrow.Testing;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Tests.Fixtures;

internal sealed class LibraryPageHarness : IDisposable
{
    private readonly List<IDisposable> scopes = [];

    private LibraryPageHarness(HeadlessSession headless, LibrarySection section, SkillsCatalogue catalogue, IFirstInstall? install, bool motion, bool reduceMotion)
    {
        Headless = headless;
        Section = section;
        Install = install as ScriptedInstall ?? new ScriptedInstall();
        Backend = new FakeLibraryBackend(catalogue, install ?? Install);
        Controller = new LibraryController(section, Backend);
        Time = new ManualTimeProvider();
        scopes.Add(SwiftClock.Provider.Override(Time));
        scopes.Add(ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = false })); // the machine's own setting never decides a test
        if (motion)
        {
            Frames = new ManualFrameSource();
            scopes.Add(FrameClock.Source.Override(Frames));
        }
        scopes.Add(SkillsPageSeams.Controllers.Override(new LibraryControllers(_ => Controller)));
        Page = section == LibrarySection.Plugins ? new PluginsSettingsPage() : new SkillsSettingsPage();
        var margin = new Border { Padding = new Thickness(24), Child = Page };
        Root = new Panel { Children = { margin } };
        Window = new Avalonia.Controls.Window { Content = Root, Width = 568, Height = 640 };
        if (reduceMotion) EnvironmentValues.SetAccessibilityReduceMotion(Window, true);
        Window.Show();
        Settle();
    }

    public HeadlessSession Headless { get; }

    public LibrarySection Section { get; }

    public ScriptedInstall Install { get; }

    public FakeLibraryBackend Backend { get; }

    public LibraryController Controller { get; }

    public LibraryPage Page { get; }

    public Panel Root { get; }

    public Avalonia.Controls.Window Window { get; }

    public ManualTimeProvider Time { get; }

    public ManualFrameSource? Frames { get; }

    public static void Run(HeadlessSession headless, LibrarySection section, Action<LibraryPageHarness> check, bool motion = false, bool reduceMotion = false, SkillsCatalogue? catalogue = null, IFirstInstall? install = null)
    {
        void Body()
        {
            using var harness = new LibraryPageHarness(headless, section, catalogue ?? LibraryFixtures.Catalogue(), install, motion, reduceMotion);
            check(harness);
            harness.Settle();
        }
        // a check may open a second page (the other section) inside the first: a test body is already on the UI thread, where a second dispatch would wait for itself
        if (HeadlessSession.IsInBody) Body();
        else headless.OnUiThread(Body);
    }

    public void Settle()
    {
        for (var i = 0; i < 4; i++)
        {
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Waits for work on another thread while the UI thread's dispatcher keeps running, then lets what it posted land.</summary>
    public void Wait(Task task)
    {
        HeadlessSession.RunDispatcherUntil(task, TimeSpan.FromSeconds(10));
        Settle();
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
    }

    /// <summary>Moves the manual clock and delivers one frame, the way a display would.</summary>
    public void Step(double milliseconds)
    {
        var by = TimeSpan.FromMilliseconds(milliseconds);
        Time.Advance(by);
        Frames!.Advance(by);
        Window.UpdateLayout();
    }

    public IEnumerable<Control> All() => Window.GetVisualDescendants().OfType<Control>();

    public Control? TryFind(string id) => All().FirstOrDefault(c => AutomationProperties.GetAutomationId(c) == id && c.IsEffectivelyVisible);

    public T Find<T>(string id) where T : Control => (T?)TryFind(id) as T ?? throw new InvalidOperationException("No visible control with the id '" + id + "'.");

    public IEnumerable<Control> Find(Func<string, bool> id) =>
        All().Where(c => AutomationProperties.GetAutomationId(c) is { Length: > 0 } found && id(found) && c.IsEffectivelyVisible);

    /// <summary>Presses a button the way a click does; false when it is disabled or hidden (a real click then reaches nothing).</summary>
    public bool Press(string id)
    {
        if (TryFind(id) is not Button { IsEffectivelyEnabled: true } button) return false;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle();
        return true;
    }

    /// <summary>The words of every visible text block under <paramref name="root"/> (the whole window when null).</summary>
    public IReadOnlyList<string> Words(Control? root = null) =>
        (root ?? Window).GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)).Select(t => t.Text!).ToList();

    public void Dispose()
    {
        Controller.Dispose();
        Window.Close();
        Dispatcher.UIThread.RunJobs();
        for (var i = scopes.Count - 1; i >= 0; i--) scopes[i].Dispose();
    }
}
