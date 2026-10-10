// PORT-ONLY, first-release UE-4 (OD-0100 place A, OD-0076). A row's status: a mark and a word, right-aligned in its column. When the status changes the old face fades and sinks 4 pt while the new one
// rises and fades in after a short wait (the design's .st: opacity .22 s ease, translate .3 s ez, .05 s delay); a change of the word alone ("Downloading 41%" to "42%") is written in place.
// The motion runs on the frame clock (CzMotion) and lands at once under Reduce Motion.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

internal sealed class UpdatesStatusCell : Panel
{
    private const double Sink = 4;

    private readonly CzMotion motion;
    private Face current;
    private Face? previous;

    public UpdatesStatusCell(RowStatus status, string text)
    {
        motion = new CzMotion(this);
        ClipToBounds = false;
        current = Add(new Face(this, status, text), shown: true);
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
    }

    /// <summary>The status the cell shows (the incoming face).</summary>
    public RowStatus Status => current.Status;

    /// <summary>The word the cell shows.</summary>
    public string Text => current.Text.Text ?? "";

    public double IncomingOpacity => current.Opacity;

    public double OutgoingOpacity => previous?.Opacity ?? 0;

    /// <summary>The natural width of the face now shown: what the column must hold so the word is not cut.</summary>
    public double FaceDesiredWidth
    {
        get
        {
            current.Measure(Size.Infinity);
            return current.DesiredSize.Width;
        }
    }

    public void SetStatus(RowStatus status, string text)
    {
        var oldText = Text;
        if (status == current.Status)
        {
            if (oldText == text) return;
            current.Text.Text = text;
            Announce(oldText, text);
            return;
        }
        foreach (var face in Children.OfType<Face>().Where(f => !ReferenceEquals(f, current)).ToArray()) Remove(face);
        var outgoing = current;
        current = Add(new Face(this, status, text), shown: false);
        previous = outgoing;
        outgoing.Hide();
        current.Show();
        Announce(oldText, text);
    }

    private void Announce(string oldText, string text)
    {
        if (oldText != text)
            ControlAutomationPeer.CreatePeerForElement(this)?.RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, oldText, text);
    }

    // A stable peer reads only the incoming face; outgoing animation text is never the announcement.
    protected override AutomationPeer OnCreateAutomationPeer() => new StatusPeer(this);

    private sealed class StatusPeer(UpdatesStatusCell owner) : ControlAutomationPeer(owner)
    {
        protected override string GetNameCore() => owner.Text;
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
    }

    private Face Add(Face face, bool shown)
    {
        Children.Add(face);
        face.Bind(motion, shown);
        return face;
    }

    private void Remove(Face face)
    {
        Children.Remove(face);
        if (ReferenceEquals(previous, face)) previous = null;
    }

    private void Gone(Face face)
    {
        if (!ReferenceEquals(face, current)) Remove(face);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var face in Children.OfType<Face>()) face.Measure(availableSize);
        return new Size(current.DesiredSize.Width, current.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var face in Children.OfType<Face>())
        {
            var width = Math.Min(face.DesiredWidthFor(finalSize.Width), finalSize.Width);
            face.Arrange(new Rect(finalSize.Width - width, Math.Max(0, (finalSize.Height - face.DesiredSize.Height) / 2), width, face.DesiredSize.Height));
        }
        return finalSize;
    }

    // One mark and one word.
    private sealed class Face : Grid
    {
        private readonly UpdatesStatusCell owner;
        private CzNumber? fade, lift;

        public Face(UpdatesStatusCell owner, RowStatus status, string text)
        {
            this.owner = owner;
            Status = status;
            ColumnDefinitions = new ColumnDefinitions("Auto,*");
            Text = new TextBlock { Text = text, MaxLines = 1, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var medium = status is RowStatus.Available or RowStatus.RestartToFinish or RowStatus.DidNotUpdate;
            SwiftFont.System(12, medium ? SwiftFont.Weight.Medium : SwiftFont.Weight.Regular).Apply(Text);
            Text.Foreground = Ink(status).Brush(Text);
            var mark = Mark(status);
            if (mark is not null)
            {
                mark.Margin = new Thickness(0, 0, 6, 0);
                mark.VerticalAlignment = VerticalAlignment.Center;
                Children.Add(mark);
            }
            SetColumn(Text, 1);
            Children.Add(Text);
            IsHitTestVisible = false;
            RenderTransform = new TranslateTransform(0, 0);
        }

        public RowStatus Status { get; }

        public TextBlock Text { get; }

        public double DesiredWidthFor(double available)
        {
            Measure(new Size(available, double.PositiveInfinity));
            return DesiredSize.Width;
        }

        public void Bind(CzMotion motion, bool shown)
        {
            fade = new CzNumber(motion, shown ? 1 : 0, v => Opacity = v);
            lift = new CzNumber(motion, shown ? 0 : Sink, v => RenderTransform = new TranslateTransform(0, v));
            Opacity = shown ? 1 : 0;
            RenderTransform = new TranslateTransform(0, shown ? 0 : Sink);
        }

        public void Show()
        {
            fade!.To(1, CzMotion.Ease(.22), .05);
            lift!.To(0, CzMotion.Ez(.3), .05);
        }

        public void Hide()
        {
            hidden = true; // taken out of the tree once it is invisible (OnPropertyChanged)
            fade!.Jump(Opacity);
            fade.To(0, CzMotion.Ease(.22));
            lift!.To(Sink, CzMotion.Ez(.3));
            if (Opacity <= 0) owner.Gone(this); // it was never shown: no fade to wait for
        }

        private bool hidden;

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == OpacityProperty && hidden && Opacity <= 0) owner.Gone(this);
        }

        private static SwiftColor Ink(RowStatus status) => status switch
        {
            RowStatus.Available or RowStatus.RestartToFinish => Chrome.Accent,
            RowStatus.DidNotUpdate => Chrome.Warning,
            RowStatus.Updating or RowStatus.Downloading or RowStatus.Updated => Chrome.PrimaryText,
            _ => Chrome.SecondaryText,
        };

        private static Control? Mark(RowStatus status) => status switch
        {
            RowStatus.Current or RowStatus.Updated => new UpdatesGlyph(UpdatesGlyphKind.Check),
            RowStatus.Available => new UpdatesGlyph(UpdatesGlyphKind.AccentDot),
            RowStatus.ChecksWhenUpdated or RowStatus.Waiting => new UpdatesGlyph(UpdatesGlyphKind.QuietDot),
            RowStatus.RestartToFinish => new UpdatesGlyph(UpdatesGlyphKind.Restart),
            RowStatus.DidNotUpdate => new UpdatesGlyph(UpdatesGlyphKind.Warning),
            RowStatus.Checking or RowStatus.Updating or RowStatus.Downloading => new NthSpinner { Width = UpdatesGlyph.Size, Height = UpdatesGlyph.Size },
            _ => null,
        };
    }
}
