using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Port.Settings;
using Marrow.Core.Port.Updates;
using Marrow.UI.Chrome;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Views.Settings;

/// <summary>The same cards or plain markdown on About and every install offer.</summary>
internal static class UpdateReleaseNotesContent
{
    public static Control? Create(UpdateReleaseNotesDigest digest, string version, string? notes)
    {
        if (!digest.IsEmpty) return new UpdateReleaseSectionCards(digest, version);
        if (string.IsNullOrWhiteSpace(notes)) return null;
        var markdown = new MarkdownView(notes);
        MarkdownView.SetMarkdownPointSize(markdown, 12.5);
        markdown.Margin = new Thickness(16);
        markdown.HorizontalAlignment = HorizontalAlignment.Stretch;
        var scroll = new ScrollViewer
        {
            Content = markdown,
            Template = AboutUpdateScroll.Template,
            MaxHeight = 240,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        return new ChromeCard(() => scroll);
    }
}

/// <summary>OD-0111 / UE-6: notes from the Marrow item actually offered by this runner, before install.</summary>
internal sealed class UpdateWhatsNew : Decorator
{
    private readonly IUpdatesRunner runner;
    private readonly StackPanel content = new() { Orientation = Orientation.Vertical, Spacing = 8 };
    private readonly CzFoldRow fold;
    private (string? Version, AvailableUpdate? Release) shown;

    public UpdateWhatsNew(IUpdatesRunner runner)
    {
        this.runner = runner;
        Apply();
        fold = new CzFoldRow(new Border { Margin = new Thickness(0, 0, 0, 12), Child = content }, initiallyOpen: shown.Version is not null);
        Child = fold;
        AttachedToVisualTree += (_, _) => { runner.Changed += OnChanged; OnChanged(); };
        DetachedFromVisualTree += (_, _) => runner.Changed -= OnChanged;
    }

    private void OnChanged()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(OnChanged); return; }
        Apply();
        fold.Open = shown.Version is not null;
    }

    private void Apply()
    {
        var row = runner.Rows.FirstOrDefault(row => row.Kind == UpdateItemKind.Marrow && row.Installed is not null &&
            (row.RestartRequired || row.State is not (UpdateRowState.Unchecked or UpdateRowState.NotInstalled or UpdateRowState.UpToDate)));
        var version = row?.RestartRequired == true ? row.Installed : row?.Latest;
        var release = runner.MarrowOffer;
        // A metadata source must describe the exact displayed target, including a downloaded version awaiting restart.
        if (!string.Equals(version, release?.Version, StringComparison.Ordinal)) release = null;
        var snapshot = (version, release);
        if (snapshot == shown) return;
        shown = snapshot;
        content.Children.Clear();
        if (version is null) return;
        content.Children.Add(new TextBlock
        {
            Text = $"What's new in nth {version}", TextWrapping = TextWrapping.Wrap,
        }.Font(SwiftFont.Callout));
        content.Children.Add(UpdateReleaseNotesContent.Create(UpdateReleaseNotesDigest.Parse(release?.Notes), version, release?.Notes) ??
            new TextBlock { Text = $"Release notes are unavailable for nth {version}.", TextWrapping = TextWrapping.Wrap }.Font(SwiftFont.Callout));
    }
}
