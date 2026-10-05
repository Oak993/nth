// PORT-ONLY: first-release feature unit SK-14 (DL-18). A real offer card in a headless window above a composer-like text box, over a scripted offer: the catalogue is Marrow's embedded one, nothing downloads and no runtime is made.
// The window has the panel the app's windows have; time and frames can be taken by hand for the transition tests.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port.Skills;
using Marrow.Core.Port;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.Detect;
using Marrow.Core.Port.Skills.State;
using Marrow.Testing;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Tests.Fixtures;

internal sealed class OfferCardHarness : IDisposable
{
    private readonly List<IDisposable> scopes = [];
    private readonly bool runOut;

    private OfferCardHarness(ScriptedOfferSource source, AddonsState initial, bool motion, bool reduceMotion, double width, SkillsCatalogue? catalogue)
    {
        Source = source;
        State = initial;
        Catalogue = catalogue ?? SkillsCatalogueLoader.LoadEmbedded();
        Time = new ManualTimeProvider();
        scopes.Add(SwiftClock.Provider.Override(Time));
        scopes.Add(ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = false }));
        runOut = !motion;
        Frames = new ManualFrameSource();
        scopes.Add(FrameClock.Source.Override(Frames));
        Card = new OfferCardControl(source, Catalogue, () => State);
        Caption = new OfferCaptionControl(source, Catalogue, Card);
        Composer = new TextBox { Text = "a draft the card must never cover", Width = width };
        Column = new StackPanel { Width = width, Spacing = 8, Children = { Card, Caption, Composer } };
        Window = new Avalonia.Controls.Window { Content = new Panel { Children = { new Border { Padding = new Thickness(24), Child = Column } } }, Width = width + 48, Height = 480 };
        if (reduceMotion) EnvironmentValues.SetAccessibilityReduceMotion(Window, true);
        Window.Show();
        Settle();
    }

    /// <summary>The pipeline's state the card reads: a test changes it the way an acceptance would.</summary>
    public AddonsState State { get; set; }

    public ScriptedOfferSource Source { get; }

    public SkillsCatalogue Catalogue { get; }

    public OfferCardControl Card { get; }

    public OfferCaptionControl Caption { get; }

    public TextBox Composer { get; }

    public StackPanel Column { get; }

    public Avalonia.Controls.Window Window { get; }

    public ManualTimeProvider Time { get; }

    public ManualFrameSource Frames { get; }

    /// <summary>The offer for the LibreOffice tool (T3), asked.</summary>
    public static ScriptedOfferSource Asked(params string[] items) => new() { Card = new OfferCard(items.Length == 0 ? ["T3"] : items.TakeLast(1).ToArray(), items.Length == 0 ? ["T3"] : items, null) };

    public static void Run(HeadlessSession headless, ScriptedOfferSource source, Action<OfferCardHarness> check, bool motion = false, bool reduceMotion = false, AddonsState? state = null, double width = 420, SkillsCatalogue? catalogue = null)
    {
        void Body()
        {
            var catalogueSize = SkillsCatalogueLoader.LoadEmbedded();
            if (source.Card is { } card) source.Card = card with { ApproxSizeMb = card.Items.Sum(id => catalogueSize.Items.Single(i => i.Id == id).ApproxSizeMb) };
            using var harness = new OfferCardHarness(source, state ?? AddonsState.Empty, motion, reduceMotion, width, catalogue);
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

    public void Step(double milliseconds)
    {
        var by = TimeSpan.FromMilliseconds(milliseconds);
        Time.Advance(by);
        Frames.Advance(by);
        Window.UpdateLayout();
    }

    public void Dispose()
    {
        Window.Close();
        foreach (var scope in scopes) scope.Dispose();
    }
}
