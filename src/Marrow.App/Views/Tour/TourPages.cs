using System.Globalization;
using Marrow.Core.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Views.Tour;

// Source: DroppyCode/App/Views/Tour/TourPages.swift (B3 52bc792), :1-29.
// The Windows welcome title and descriptions follow OD-0012.
public static class Tour
{
    // TourPages.swift:4-13
    private static readonly ValueList<TourPage> pages = new(new[]
    {
        // DEVIATION OD-0012: the Windows welcome title and description are truthful to the port.
        new TourPage("tour-welcome", "Welcome to Nth", "One native Windows window for every coding agent you use. Claude, Codex, Gemini, Copilot and more, each in its own thread, on the same glass."),
        new TourPage("tour-hydra", "Meet Hydra", "One chat, many heads. The lead writes the briefs, sends helper agents out in parallel, each in a copy of your project, and their work lands back in your checkout."),
        new TourPage("tour-pairs", "Pair a strong lead with quick heads", "Put a Claude lead over Gemini or OpenCode heads. Pick the pair from the model picker, cap how many heads run at once, and give each side its own effort."),
        new TourPage("tour-slider", "Effort, on a slider", "Drag from low to max on any model. In a pair the track blends the lead's colour into the heads', fast mode streaks toward the knob, and the title opens the model picker."),
        new TourPage("tour-threads", "Threads, when you want them", "Hide the sidebar and the chat has the whole window. The Threads button brings the list back as a floating panel: the same search, the same rows, and it goes away as soon as you pick one."),
        new TourPage("tour-recipes", "Recipes for a team", "Hydra's cookbook holds ready-made pairs: a lead, its heads and the effort each side gets, from the providers you have signed in. Pick one in Settings and every new chat starts on it."),
        new TourPage("tour-panels", "Panels that float", "Changes, the terminal and the team's panel dock to any corner and can pop out on their own. The conversation makes room, and everything stays in one window."),
        // DEVIATION OD-0012: the Windows themes description avoids unsupported platform claims.
        new TourPage("tour-themes", "Make it yours", ThemeCount + " themes, from Catppuccin to Kanagawa, in real glass. Built in C#, so it scrolls smoothly and starts in a blink."),
    });

    // TourPages.swift:4-13
    public static ValueList<TourPage> Pages => pages;

    // TourPages.swift:18-24
    private static string ThemeCount
    {
        get
        {
            var count = SwiftEnum<AppTheme>.AllCases.Count;
            var spelled = SwiftFormatters.SpellOut(count, "en_US") ?? count.ToString(CultureInfo.InvariantCulture);
            return SwiftString.Uppercased(SwiftString.Prefix(spelled, 1)) + SwiftString.DropFirst(spelled, 1);
        }
    }

    // TourPages.swift:26-28
    public static void Present(AppModel model)
    {
        var seen = false;
        void MarkSeen()
        {
            if (seen) return;
            seen = true;
            model.Settings.HasSeenTour = true;
        }

        TourWindowController.Shared.Present(
            pages: Pages,
            onFinish: MarkSeen,
            onClose: MarkSeen);

    }
}
