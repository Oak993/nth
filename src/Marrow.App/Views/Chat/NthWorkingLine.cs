// PORT-ONLY: OD-0136 amendment 2, step 2 option B ("Thinking line", ED-NEW-nth-motion). The main working line under a turn
// (ThreadTimeline's WorkingIndicator): the three-arc glyph (14 px), the line's word in breathing letters (a new word arrives letter
// by letter) and the time since the turn started in rolling digits (mono, tabular, right-aligned, quieter than the word). The
// words, the summaries and the time's text are the line's own; only the drawing changes. The Hydra head panels keep
// HydraWorkingTitle, HydraElapsedTime and the activity chart as they are.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;

namespace Marrow.App.Views.Chat;

/// <summary>A header child that knows where its text's first baseline is (the working line's header aligns its children on it).</summary>
internal interface IBaselineProvider
{
    double Baseline { get; }
}

/// <summary>The type and ink of an <see cref="NthLetters"/> in the working line.</summary>
internal static class NthLineType
{
    /// <summary>Gives the letters the font (the letters read it as a TextBlock would, from the TextElement properties).</summary>
    public static void Apply(NthLetters letters, SwiftFont font)
    {
        var resolved = font.Resolve();
        letters.SetValue(TextElement.FontFamilyProperty, resolved.FontFamily);
        letters.SetValue(TextElement.FontSizeProperty, resolved.FontSize);
        letters.SetValue(TextElement.FontWeightProperty, resolved.FontWeight);
        letters.SetValue(TextElement.FontStyleProperty, resolved.FontStyle);
        letters.SetValue(TextElement.FontFeaturesProperty, resolved.FontFeatures);
    }

    /// <summary>Where the first baseline of a line set in <paramref name="font"/> lies, from the top of the line.</summary>
    public static double Baseline(SwiftFont font)
    {
        var resolved = font.Resolve();
        using var layout = new TextLayout("Ag", new Typeface(resolved.FontFamily, resolved.FontStyle, resolved.FontWeight), resolved.FontSize, Brushes.Black, fontFeatures: resolved.FontFeatures);
        return layout.TextLines.Count == 0 ? layout.Height : layout.TextLines[0].Baseline;
    }

    /// <summary>Keeps the letters' ink in step with the theme of <paramref name="owner"/>.</summary>
    public static void Follow(Control owner, NthLetters letters, SwiftColor color)
    {
        void Paint() => letters.Foreground = new ImmutableSolidColorBrush(color.Resolve(owner.ActualThemeVariant));
        Paint();
        owner.ActualThemeVariantChanged += (_, _) => Paint();
        owner.AttachedToVisualTree += (_, _) => Paint();
    }
}

/// <summary>The running working line's title: the thinking glyph and the line's word in breathing letters.</summary>
internal sealed class NthWorkingTitle : Panel, IBaselineProvider
{
    private const double GlyphSize = 14, Gap = 8;
    private static readonly SwiftFont Font = SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Medium); // :2123 the title's type, kept

    private readonly NthThinkingGlyph glyph = new();
    private readonly NthLetters letters = new(NthLettersMode.Breathe);

    public NthWorkingTitle(string word)
    {
        ClipToBounds = true; // the word is clipped to its box, as the page's .wd is, so a long summary never leaves the pill
        NthLineType.Apply(letters, Font);
        NthLineType.Follow(this, letters, Chrome.SecondaryText);
        letters.SetTextInstantly(word);
        Children.Add(glyph); Children.Add(letters);
    }

    /// <summary>The glyph (tests).</summary>
    public NthThinkingGlyph Glyph => glyph;

    /// <summary>The word's letters (tests).</summary>
    public NthLetters Letters => letters;

    /// <summary>The line's word; a new one arrives letter by letter.</summary>
    public string Word { get => letters.Text; set => letters.Text = value; }

    public double Baseline => (DesiredSize.Height - letters.DesiredSize.Height) / 2 + NthLineType.Baseline(Font);

    protected override Size MeasureOverride(Size availableSize)
    {
        glyph.Measure(new Size(GlyphSize, GlyphSize));
        letters.Measure(Size.Infinity);
        var width = GlyphSize + Gap + letters.DesiredSize.Width;
        return new Size(Math.Min(width, availableSize.Width), Math.Max(GlyphSize, letters.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        glyph.Arrange(new Rect(0, (finalSize.Height - GlyphSize) / 2, GlyphSize, GlyphSize));
        letters.Arrange(new Rect(GlyphSize + Gap, (finalSize.Height - letters.DesiredSize.Height) / 2, letters.DesiredSize.Width, letters.DesiredSize.Height));
        return finalSize;
    }
}

/// <summary>The working line's clock (HydraProgressBar.swift:541-562 as the line used it): the same text, ticking once a second while the turn runs, but drawn as rolling digits.</summary>
internal sealed class NthElapsedTime : Decorator, IBaselineProvider
{
    private static readonly SwiftFont Font = SwiftFont.System(SwiftFont.TextStyle.Subheadline, SwiftFont.Design.Monospaced, SwiftFont.Weight.Medium); // mono, tabular (the page: 500 11.5 mono)

    private readonly NthLetters letters = new(NthLettersMode.Roll) { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
    private DateTimeOffset startedAt;
    private DateTimeOffset? finishedAt;
    private bool isRunning;
    private ITimer? nextTick;
    private bool attached;
    private int ticking; // the generation of the tick in force: a tick of an earlier one does nothing
    private WindowPresence? presence; // ED-NEW-lean-6: no tick while the window cannot be seen

    public NthElapsedTime(DateTimeOffset startedAt, DateTimeOffset? finishedAt, bool isRunning)
    {
        NthLineType.Apply(letters, Font);
        NthLineType.Follow(this, letters, Chrome.PrimaryText.Opacity(0.58)); // --text3: quieter than the word
        Child = letters;
        this.startedAt = startedAt; this.finishedAt = finishedAt; this.isRunning = isRunning;
        if (!isRunning) letters.SetTextInstantly(Finished());
        letters.AttachedToVisualTree += (_, _) => { attached = true; Watch(); if (this.isRunning) Tick(++ticking); };
        letters.DetachedFromVisualTree += (_, _) => { attached = false; Unwatch(); ticking++; nextTick?.Dispose(); nextTick = null; };
    }

    /// <summary>The time's digits (tests).</summary>
    public NthLetters Letters => letters;

    /// <summary>The time shown.</summary>
    public string Text => letters.Text;

    public double Baseline => (DesiredSize.Height - letters.DesiredSize.Height) / 2 + NthLineType.Baseline(Font);

    private string Finished() => RelativeTime.Duration((finishedAt ?? SwiftDate.Now) - startedAt);

    private void Tick(int generation)
    {
        nextTick?.Dispose(); nextTick = null;
        if (!attached || !isRunning || generation != ticking) return;
        var now = SwiftDate.Now;
        var n = Math.Floor((now - startedAt).TotalSeconds);
        var gridDate = startedAt.AddSeconds(n);
        letters.Text = RelativeTime.Duration(gridDate - startedAt);
        if (presence is { Seen: false }) return; // ED-NEW-lean-6: unseen, the next tick waits for the window to be seen
        nextTick = SwiftClock.Provider.Current.CreateTimer(_ => MainActor.Post(() => Tick(generation)), null,
            startedAt.AddSeconds(n + 1) - now, Timeout.InfiniteTimeSpan);
    }

    // ED-NEW-lean-6: while the window cannot be seen the clock does not tick (nothing would see the digits roll, and each
    // tick wakes main); seen again, it shows the current time at once and ticks on the same one-second grid.
    private void Watch()
    {
        Unwatch();
        presence = WindowPresence.For(this);
        if (presence is not null) presence.Changed += PresenceChanged;
    }

    private void Unwatch()
    {
        if (presence is null) return;
        presence.Changed -= PresenceChanged;
        presence = null;
    }

    private void PresenceChanged()
    {
        var generation = ++ticking; nextTick?.Dispose(); nextTick = null;
        if (!attached || !isRunning || presence is not { Seen: true }) return;
        var shown = RelativeTime.Duration(startedAt.AddSeconds(Math.Floor((SwiftDate.Now - startedAt).TotalSeconds)) - startedAt);
        if (shown != letters.Text) letters.SetTextInstantly(shown);
        Tick(generation);
    }

    /// <summary>The clock handed its inputs again: the same text, now counting from <paramref name="nextStartedAt"/>, ticking only while it runs.
    /// When the words change the digits that differ roll (the letters' own motion).</summary>
    internal void Update(DateTimeOffset nextStartedAt, DateTimeOffset? nextFinishedAt, bool nextIsRunning)
    {
        if (nextStartedAt == startedAt && nextFinishedAt == finishedAt && nextIsRunning == isRunning) return;
        startedAt = nextStartedAt; finishedAt = nextFinishedAt; isRunning = nextIsRunning;
        var generation = ++ticking; nextTick?.Dispose(); nextTick = null;
        if (isRunning) letters.Text = RelativeTime.Duration(startedAt.AddSeconds(Math.Floor((SwiftDate.Now - startedAt).TotalSeconds)) - startedAt);
        else letters.Text = Finished();
        if (isRunning) Tick(generation);
    }
}
