using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

/// <summary>The check row inside a card at the settings window's default width (a helper, not a test class): the card is 520 pt wide, where the resting sentence takes two lines and the asking sentence one.</summary>
internal sealed class AboutCheckRowRig : IDisposable
{
    private readonly TestRuntime runtime = new();
    private readonly ManualFrameSource frames = new();
    private readonly IDisposable main, frameScope, info, locale, shared;
    public readonly UpdateChecker Checker;
    public readonly AboutUpdateCheckRow Row;
    public readonly Marrow.UI.Chrome.ChromeCard Card;
    public readonly Avalonia.Controls.Window Window;

    public AboutCheckRowRig(bool reduced = false)
    {
        main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        frameScope = FrameClock.Source.Override(frames);
        info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0")); locale = SwiftFormatters.CurrentLocale.Override("en_US");
        Checker = runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], ""));
        shared = UpdateChecker.OverrideShared(Checker);
        Row = new AboutUpdateCheckRow(); EnvironmentValues.SetAccessibilityReduceMotion(Row, reduced);
        Card = new Marrow.UI.Chrome.ChromeCard(() => new StackPanel { Children = { Row } });
        Window = new Avalonia.Controls.Window
        {
            Width = 568, Height = 300, UseLayoutRounding = false,
            Content = new Border { Margin = new Thickness(24, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Child = Card },
        };
        Window.Show(); Dispatcher.UIThread.RunJobs(); Window.UpdateLayout();
    }

    public double Height => Card.Bounds.Height;
    public TextBlock Sentence => Row.GetVisualDescendants().OfType<SentenceHeightGlide>().Single().Children.OfType<TextBlock>().First();
    public SentenceHeightGlide Box => Row.GetVisualDescendants().OfType<SentenceHeightGlide>().Single();

    public void Step(int milliseconds)
    {
        runtime.Time.Advance(TimeSpan.FromMilliseconds(milliseconds)); frames.Advance(TimeSpan.FromMilliseconds(milliseconds)); Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
    }

    public void Change(string property, object? value) { Checker.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public)!.SetValue(Checker, value); Dispatcher.UIThread.RunJobs(); Window.UpdateLayout(); }

    public void Dispose() { Window.Close(); shared.Dispose(); locale.Dispose(); info.Dispose(); frameScope.Dispose(); main.Dispose(); runtime.Dispose(); }
}
