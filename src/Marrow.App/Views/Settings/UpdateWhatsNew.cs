using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Views.Settings;

/// <summary>OD-0111 / UE-6: the offered Marrow version's parity cards on the Updates page and sidebar offer.</summary>
internal sealed class UpdateWhatsNew : Decorator
{
    private readonly StackPanel content = new() { Orientation = Orientation.Vertical, Spacing = 8 };
    private readonly CzFoldRow fold;
    private AvailableUpdate? shown;

    public UpdateWhatsNew()
    {
        var checker = UpdateChecker.Shared;
        var initial = checker.UpdateAvailable ? checker.Update : null;
        Apply(initial);
        fold = new CzFoldRow(new Border { Margin = new Thickness(0, 0, 0, 12), Child = content }, initiallyOpen: shown is not null);
        Child = fold;
        // Track reads the whole offer (including Notes), so a corrected release refreshes its cards too.
        this.Track(() => checker.UpdateAvailable ? checker.Update : null, update =>
        {
            Apply(update);
            fold.Open = shown is not null;
        });
    }

    private void Apply(AvailableUpdate? update)
    {
        if (Equals(update, shown)) return;
        shown = update;
        content.Children.Clear();
        if (update is null) return;
        var digest = UpdateReleaseNotesDigest.Parse(update.Notes);
        if (digest.IsEmpty) return;
        content.Children.Add(new TextBlock
        {
            Text = $"What's new in Marrow {update.Version}", TextWrapping = TextWrapping.Wrap,
        }.Font(SwiftFont.Callout));
        content.Children.Add(new UpdateReleaseSectionCards(digest, update.Version));
    }
}
