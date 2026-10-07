// PORT-ONLY: MA-3b-7 :4857-4871. IOpinionsPort/E1 posts the actual synthesized timeline message/byline; this row never invents an answer.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Port.MultiAccount.Cards;
using Marrow.App.Port.Skills.Pages;
using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.UI.Port;
namespace Marrow.App.Port.MultiAccount.Opinions;
internal sealed class OpinionRoundView : StackPanel
{
    private readonly Guid thread;
    private readonly IOpinionsPort port;
    private readonly Func<OpinionContinuation> continuation;
    private readonly Func<Guid, string?> headError;
    private readonly OpinionCardGrid grid = new();
    private readonly List<OpinionCardView> cards = [];
    private readonly TextBlock failure = LibraryUi.Text("", 12, secondary: true, wrap: true);
    private readonly OpinionArrival arrival;
    private readonly Decorator combineHost = new();
    private readonly OpinionArrival combineArrival;
    private bool used, completed;
    internal Guid RoundId { get; }
    internal IReadOnlyList<OpinionCardView> Cards => cards;
    internal Button Combine { get; }
    internal Control? ClosingNote { get; private set; }
    internal ThreadAccount? SourceAccount { get; }
    internal string CombinedByline => cards.FirstOrDefault()?.AccountName + " · combined " + cards.Count.ToString(CultureInfo.InvariantCulture) + " answers";
    internal OpinionRoundView(OpinionRound round, IOpinionsPort port, IReadOnlyList<ThreadAccount> accounts, Func<OpinionContinuation> continuation, Func<Guid, string?>? headError = null)
    {
        thread = round.Thread; RoundId = round.Id; this.port = port; this.continuation = continuation; Spacing = 12;
        this.headError = headError ?? (_ => null); SourceAccount = accounts.FirstOrDefault(a => a.Id == round.Answers.FirstOrDefault()?.AccountId);
        this.Named("Answers for " + round.Message, "opinion-round-" + RoundId.ToString("N"));
        foreach (var answer in round.Answers)
        {
            var card = new OpinionCardView(answer, accounts.FirstOrDefault(a => a.Id == answer.AccountId), cards.Count == 0); cards.Add(card); grid.Children.Add(card);
        }
        Children.Add(grid);
        var words = LibraryUi.Text("Combine", 12, weight: SwiftFont.Weight.Medium);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new CombineGlyph(), words } };
        Combine = PlainControls.Button(content, StartCombine); Combine.Height = 26; Combine.Padding = new Thickness(11, 0); Combine.CornerRadius = new CornerRadius(13); Combine.Focusable = true;
        Combine.HorizontalAlignment = HorizontalAlignment.Center; Combine.Named("Combine", "opinion-combine-" + RoundId.ToString("N"));
        OpinionCombineChrome.Install(Combine);
        combineHost.Child = Combine; combineHost.IsVisible = false; Children.Add(combineHost); combineArrival = new(combineHost);
        failure.IsVisible = false; Children.Add(failure); arrival = new(this);
        AttachedToVisualTree += (_, _) => Paint(); ActualThemeVariantChanged += (_, _) => Paint();
        Apply(round);
        void Paint()
        {
            var dark = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
            words.Foreground = new SolidColorBrush(CapsuleTokens.For(dark).Tx);
        }
    }
    internal void Enter() => arrival.Enter();
    internal void Apply(OpinionRound round)
    {
        foreach (var card in cards) if (round.Answers.FirstOrDefault(a => a.AccountId == card.AccountId) is { } answer) { card.Apply(answer); card.ApplyError(headError(card.AccountId)); }
        var ready = round.Answers.Count > 0 && round.Answers.All(a => a.Done && !string.IsNullOrWhiteSpace(a.Text) && headError(a.AccountId) is null);
        var appeared = !combineHost.IsVisible && (ready || used || round.Combined);
        combineHost.IsVisible = Combine.IsVisible = ready || used || round.Combined;
        Combine.IsEnabled = ready && !used && !round.Combined;
        Combine.Opacity = used || round.Combined ? .5 : 1;
        if (appeared && this.IsAttachedToVisualTree()) combineArrival.Enter();
        if (round.Combined) Complete();
    }
    private async void StartCombine()
    {
        if (used || !Combine.IsEnabled || !Combine.IsVisible) return;
        used = true; Combine.IsEnabled = false; Combine.Opacity = .5;
        try { await port.CombineAsync(thread, RoundId); Complete(); }
        catch (Exception error) { failure.Text = error.Message; failure.IsVisible = true; }
        // A failed post may already have committed in E1: the same control never submits again.
    }
    private void Complete()
    {
        if (completed) return; completed = true; used = true; Combine.IsEnabled = false; Combine.Opacity = .5;
        var current = continuation();
        var text = new CardTitleBlock { Text = current.Note, FontSize = 11.5, LineHeight = 16, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
        SwiftFont.System(11.5).Apply(text); CardTitle.Emphasize(text, current.Pill is { } pill ? [pill] : []);
        var icon = LibraryUi.Glyph("checkmark", 11, Marrow.UI.Chrome.Chrome.SecondaryText);
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("11,*"), ColumnSpacing = 7 }; content.Children.Add(icon); Grid.SetColumn(text, 1); content.Children.Add(text);
        var note = new OpinionSurface(OpinionSurfaceKind.Note, 999) { Padding = new Thickness(11, 4), Child = content, HorizontalAlignment = HorizontalAlignment.Center };
        note.Named(current.Note, "opinion-closing-" + RoundId.ToString("N"));
        note.AttachedToVisualTree += (_, _) => Paint(); note.ActualThemeVariantChanged += (_, _) => Paint();
        ClosingNote = note; Paint(); Children.Add(note); new OpinionArrival(note).Enter();
        void Paint()
        {
            var tokens = CapsuleTokens.For(note.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark);
            text.Foreground = new SolidColorBrush(tokens.Tx2);
            foreach (var run in text.Inlines!.OfType<Run>()) run.Foreground = new SolidColorBrush(run.FontWeight == FontWeight.SemiBold ? tokens.Tx : tokens.Tx2);
            note.InvalidateVisual();
        }
    }
    private sealed class CombineGlyph : Control
    {
        internal CombineGlyph() { Width = Height = 11; IsHitTestVisible = false; }
        public override void Render(DrawingContext context)
        {
            const double ratio = 11.0 / 16;
            var pen = new Pen(new SolidColorBrush(CapsuleTokens.For(ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark).Tx), 1.3 * ratio);
            context.DrawEllipse(null, pen, new Point(6 * ratio, 8 * ratio), 3.6 * ratio, 3.6 * ratio);
            context.DrawEllipse(null, pen, new Point(10.5 * ratio, 8 * ratio), 3.6 * ratio, 3.6 * ratio);
        }
    }
}
