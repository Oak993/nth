using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.Testing;
using Marrow.UI.Markdown;

namespace Marrow.UI.Tests.Fixtures;

/// <summary>
/// The host path of the real rows (AssistantMessageRow, the narration, the thinking steps) for the stream veil tests (UI-THEME-047): a Decorator
/// that replaces its MarkdownView with a new one for each flush of a streaming reply and hands it the MarkdownVeils it keeps, as a row does.
/// </summary>
public sealed class StreamVeilFlowHost
{
    public readonly Decorator Decorator = new();
    public readonly MarkdownVeils Veils = new();
    public Window Window = null!;

    public StreamVeilFlowHost(TestRuntime runtime, string first, bool dimmed = false)
    {
        runtime.OnMain(() =>
        {
            Decorator.Child = View(first, dimmed);
            Window = Show(Decorator);
        });
    }

    public MarkdownView View(string text, bool dimmed)
    {
        var view = new MarkdownView(text, isStreaming: true, Veils.Root);
        if (dimmed) MarkdownView.SetMarkdownDimmed(view, true);
        return view;
    }

    /// <summary>What a row does when a flush changes its text: a new MarkdownView replaces the old one.</summary>
    public void Flush(TestRuntime runtime, string text, bool dimmed = false) =>
        runtime.OnMain(() =>
        {
            Decorator.Child = View(text, dimmed);
            Pump();
        });

    public static Window Show(Control content)
    {
        var window = new Window { Width = 400, Height = 600, Content = new StackPanel { Children = { content } } };
        window.Show();
        Pump();
        return window;
    }

    public static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
