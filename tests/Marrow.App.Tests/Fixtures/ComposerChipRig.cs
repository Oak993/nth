using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.App.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// The real composer (ComposerView over a resolved thread) with its model chip, for the tests that need the chip where the
/// product puts it: every write to the thread record reaches the composer, which hands the chip its new inputs, and the
/// chip's popover must stay open through it (ComposerView.swift:647,688 keep the chip's @State through a re-render). The popover
/// content is hosted in a second window, as the other chip tests do, because the recording presenter does not open a Popup.
/// </summary>
public sealed class ComposerChipRig : IDisposable
{
    private readonly List<IDisposable> owned = [];
    public required ThreadRuntimePart12Harness Harness;
    public required ManualFrameSource Frames;
    public ManualTimeProvider Time => Harness.Main.Time; // the harness's clock is SwiftClock's source
    public required RecordingPresenter Presenter;
    public required ComposerView View;
    public required Avalonia.Controls.Window Window;
    public Avalonia.Controls.Window? CardWindow;
    public AppModel App => Harness.App;
    public Guid ThreadID => Harness.Thread.Id;
    public ChatThread Thread => OnMain(() => App.Thread(ThreadID)!);

    /// <summary>Runs on the main loop, or directly when the loop is already running (the observed reads need it).</summary>
    public T OnMain<T>(Func<T> body) => Harness.Main.Loop.CheckAccess() ? body() : Harness.Main.OnMain(body);
    public void OnMain(Action body) { if (Harness.Main.Loop.CheckAccess()) body(); else Harness.Main.OnMain(body); }

    /// <summary>The Codex model the tests give the thread: efforts low to xhigh, a fast tier.</summary>
    public const string ModelID = "c1";

    public static ComposerChipRig Start(Action<AppModel>? configure = null, string effort = "low", bool fast = false, bool hydra = false, ProviderKind provider = ProviderKind.Codex)
    {
        Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        var harness = new ThreadRuntimePart12Harness(provider); // the thread's service (MAUI-002 under RR-49: a verified opinion head)
        harness.Main.Loop.Run(() => harness.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        var frames = new ManualFrameSource();
        var presenter = new RecordingPresenter();
        ComposerChipRig rig = null!;
        harness.Main.OnMain(() =>
        {
            HydraCookbookResolveFixture.SetModels(harness.App.Providers, ProviderKind.Codex,
                new ModelOption("c1", "Codex one", efforts: new ValueList<string>(["low", "medium", "high", "xhigh"]), defaultEffort: "medium", fastTier: "priority"));
            configure?.Invoke(harness.App);
            harness.App.UpdateThread(harness.Thread.Id, current => current with { Model = ModelID, Effort = effort, FastMode = fast, HydraEnabled = hydra });
        });
        var view = harness.Main.OnMain(() => new ComposerView(harness.Runtime, workingDirectory: null));
        Avalonia.Controls.Window window = null!;
        harness.Main.OnMain(() =>
        {
            var host = new Border { Child = view, Width = 600 };
            AppEnvironment.SetAppModel(host, harness.App);
            window = new Avalonia.Controls.Window { Content = host, Width = 900, Height = 600 };
            window.Show();
        });
        rig = new ComposerChipRig { Harness = harness, Frames = frames, Presenter = presenter, View = view, Window = window };
        rig.owned.Add(FrameClock.Source.Override(frames));
        rig.owned.Add(Presentation.Presenter.Override(presenter));
        rig.owned.Add(harness);
        rig.Pump();
        return rig;
    }

    public void Dispose()
    {
        Harness.Main.OnMain(() => { CardWindow?.Close(); Window.Close(); });
        for (var i = owned.Count - 1; i >= 0; i--) owned[i].Dispose();
    }

    public void Pump()
    {
        for (var round = 0; round < 4; round++)
            OnMain(() => { Window.UpdateLayout(); CardWindow?.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
    }

    /// <summary>Time and frames move together, as a display's frames do.</summary>
    public void Step(int milliseconds)
    {
        OnMain(() =>
        {
            Time.Advance(TimeSpan.FromMilliseconds(milliseconds));
            Frames.Advance(TimeSpan.FromMilliseconds(milliseconds));
        });
        Pump();
    }

    public ModelEffortButton Chip => OnMain(() => Assert.Single(View.GetVisualDescendants().OfType<ModelEffortButton>()));
    public Button ChipButton => OnMain(() => Assert.Single(Chip.GetVisualDescendants().OfType<Button>()));
    public double ChipWidth => OnMain(() => ChipButton.Bounds.Width);
    public bool Presented => Presenter.Calls.Count > 0 && Presenter.Calls[^1].Presented;
    public int Dismissals => Presenter.Calls.Count(call => !call.Presented);

    /// <summary>A click on the chip, which toggles its popover.</summary>
    public void ClickChip()
    {
        OnMain(() => ChipButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        Pump();
    }

    /// <summary>Opens the popover and puts its content on screen in a window of its own.</summary>
    public Control OpenCard()
    {
        ClickChip();
        Assert.True(Presented, "the chip's popover opened");
        var popup = Presenter.Calls[^1].Popup;
        var content = Assert.IsAssignableFrom<Control>(popup.Child);
        OnMain(() =>
        {
            popup.Child = null; // the recording presenter does not host the panel: it moves to a window
            var host = new Border { Child = content, Width = 330, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
            AppEnvironment.SetAppModel(host, App);
            CardWindow = new Avalonia.Controls.Window { Content = host, Width = 330, Height = 700 };
            CardWindow.Show();
        });
        Pump();
        return content;
    }

    public EffortSliderCard Card(Control content) => OnMain(() => Assert.Single(content.GetVisualDescendants().OfType<EffortSliderCard>()));

    public void Write(Func<ChatThread, ChatThread> change)
    {
        OnMain(() => App.UpdateThread(ThreadID, change));
        Pump();
    }

    public void Click(Control control)
    {
        OnMain(() => Assert.Single(control.GetVisualDescendants().OfType<Button>()).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        Pump();
    }

    /// <summary>What a caption shows of its soft appear: the opacity down to the card and the vertical offset.</summary>
    public static (double Opacity, double OffsetY) Shown(Control caption, Control card)
    {
        double opacity = caption.Opacity, offset = 0;
        for (var node = caption.GetVisualParent(); node is not null && !ReferenceEquals(node, card); node = node.GetVisualParent())
        {
            if (node is Visual visual) opacity *= visual.Opacity;
            if (node is Visual { RenderTransform: Avalonia.Media.TranslateTransform translate }) offset += translate.Y;
            if (node is Visual { RenderTransform: Avalonia.Media.TransformGroup group }) offset += group.Children.OfType<Avalonia.Media.TranslateTransform>().Sum(t => t.Y);
        }
        return (opacity, offset);
    }
}
