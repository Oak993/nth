// PORT-ONLY: OD-0049/0075/0076/0099 Q-PICKER A. Mirrored openrouter-picker/Main.dc.html v4/v5;
// ED-616. Strike's ModelList constructor seam is supplied separately; no EffortSlider edits.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Port.ModelsPalette;

public sealed record PickerModel(string Id, string Name, string Maker, string Color, bool Enabled = true,string? Detail=null);

/// <summary>The composer's approved searchable maker-grouped model list. All folds remain mounted.</summary>
public sealed class OpenRouterPicker : Decorator
{
    public const double ChipClearance = 10;
    public const double MaximumListHeight = 440;
    private static readonly Animation Ease = Animation.TimingCurve(.25, .1, .25, 1, .34);
    private static readonly Animation Ez = Animation.TimingCurve(.32, .72, 0, 1, .24);
    private readonly List<PickerRow> rows = [];
    private readonly List<(string Maker, PickerFold Fold)> groups = [];
    private readonly Dictionary<string,Marrow.UI.Port.Settings.CzColor> makerDots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PickerCount> counts = new(StringComparer.Ordinal);
    // ED-NEW-stutterfix-2: a long list's count labels are made as each count is first shown, in the order of their counts
    // (the stack the cross-fade draws in); the widest, the full count, is made with the picker, so the column keeps its width.
    private const int LazyCountsFrom = 100;
    private readonly Grid countGrid;
    private readonly List<int> countOrder = [];
    private readonly DeferredRows? fill;
    private readonly List<PickerFold> items = []; // the list's groups and rows, in list order, with their heights
    private readonly List<double> heights = [];
    private PickerHeldSpacer? spacer;
    private readonly StackPanel list;
    private readonly ScrollViewer scroll;
    private readonly AnimatedScalar scrollOffset;
    private readonly PickerFold empty;
    private readonly TextBlock emptyLabel;
    private readonly Button clear;
    private readonly AnimatedScalar clearOpacity, clearScale, height;
    private AnimatedScalar? topFade,bottomFade;
    private double contentHeight;
    private readonly Action<string> choose;
    private int active;
    private string selected;
    private string shownCount;
    private bool changing;
    public TextBox Search { get; }
    public IReadOnlyList<PickerRow> Rows => rows;
    public string CountText => shownCount;
    public string EmptyText => emptyLabel.Text ?? "";
    public string? ActiveId => Keyed ? Visible.ElementAtOrDefault(active)?.Model.Id : null;
    public double ListHeight => scroll.Height;
    public double ScrollOffset=>scroll.Offset.Y;
    public double TargetScrollOffset {get;private set;}
    public double TargetListHeight { get; private set; }
    public double SearchFieldHeight => 28;
    public double CountOpacity(string text) => counts.TryGetValue(text, out var c) ? c.Opacity : 0;
    private bool Keyed => Words.Length != 0;
    private string[] Words => (Search.Text ?? "").Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    private IEnumerable<PickerRow> Visible => rows.Where(r => r.Shown);

    public OpenRouterPicker(IReadOnlyList<PickerModel> models, string selectedId, Action<string> choose, Action back)
    {
        using var probe = InteractionProbe.Begin("openrouter.build"); // ED-NEW-stutterfix-1
        this.choose = choose; selected = selectedId; shownCount = Count(models.Count);
        Width = 330; AutomationProperties.SetName(this, "Select model");
        var stack = new StackPanel();
        // The host owns the material; these are the design's 6px inner padding and content geometry.
        Child = new Border { Padding = new Thickness(6), Child = stack };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*,Auto"), Margin = new Thickness(8,8,8,4) };
        var backButton = Button(new PickerGlyph("back"), back); backButton.Width = backButton.Height = 20;
        AutomationProperties.SetName(backButton, "Back"); header.Children.Add(backButton);
        var caption = Label("Select model", 13, .6); caption.Margin = new Thickness(6,0,0,0); Grid.SetColumn(caption,1); header.Children.Add(caption);
        countGrid = new Grid(); Grid.SetColumn(countGrid,2); header.Children.Add(countGrid);
        if (models.Count < LazyCountsFrom) for (var n=0; n<=models.Count; n++) CountLabel(n, n==models.Count);
        else CountLabel(models.Count, true);
        stack.Children.Add(header);
        Search = new TextBox { PlaceholderText = "Search models", FontSize = 12.5, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Padding = new Thickness(0), MinHeight = 0, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Foreground = Brush(.96,.965,.984,1) };
        PlainControls.PlainTextField(Search);
        Search.PlaceholderForeground=Brush(.957,.965,.984,.45);
        AutomationProperties.SetName(Search, "Search models");
        var fieldGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("13,*,16"), ColumnSpacing = 6 };
        fieldGrid.Children.Add(new PickerGlyph("search")); Grid.SetColumn(Search,1); fieldGrid.Children.Add(Search);
        clear = Button(new PickerGlyph("clear"), ClearSearch); clear.Width = clear.Height = 16; Grid.SetColumn(clear,2); fieldGrid.Children.Add(clear);
        AutomationProperties.SetName(clear,"Clear search");
        clearOpacity = new AnimatedScalar(clear,0,v=>clear.Opacity=v);
        clearScale = new AnimatedScalar(clear,.6,v=>clear.RenderTransform=new ScaleTransform(v,v));
        var field = new Border { Height = 28, Padding = new Thickness(10,0), CornerRadius = new CornerRadius(999),
            Background = Brush(1,1,1,.08), BorderBrush = Brush(1,1,1,.12), BorderThickness = new Thickness(1), Child=fieldGrid,
            Margin=new Thickness(4,4,4,8) };
        Search.GotFocus += (_,_)=>field.BorderBrush=Brush(1,1,1,.22);
        Search.LostFocus += (_,_)=>field.BorderBrush=Brush(1,1,1,.12);
        stack.Children.Add(field);
        list = new StackPanel { Margin=new Thickness(0,0,0,2) }; // ED-NEW-stutterfix-2: the list's items, put in by DeferredRows past its first screens
        foreach (var group in models.GroupBy(m=>m.Maker,StringComparer.Ordinal))
        {
            var section = new StackPanel { Orientation=Avalonia.Layout.Orientation.Horizontal, Spacing=7, Height=24, Margin=new Thickness(10,0) };
            var makerDot=(Border)Dot(group.First().Color,7);section.Children.Add(makerDot);section.Children.Add(Label(group.Key,11,.55));
            var makerBrush=(SolidColorBrush)makerDot.Background!;makerDots.Add(group.Key,new(new Marrow.UI.Port.Settings.CzMotion(makerDot),makerBrush.Color,c=>makerBrush.Color=c));
            var fold = new PickerFold(section,true); groups.Add((group.Key,fold)); items.Add(fold); heights.Add(24);
            foreach (var model in group)
            {
                var row = new PickerRow(model, model.Id==selectedId,()=>Pick(model.Id));
                row.Button.PointerEntered += (_,_)=> { if (Keyed && row.Shown) { active=Visible.ToList().IndexOf(row); UpdateActive(); } };
                rows.Add(row); items.Add(row); heights.Add(47);
            }
        }
        var eager = DeferredRows.EagerCount(heights, 0, 0);
        for (var i=0; i<eager; i++) list.Children.Add(items[i]);
        emptyLabel = Label("",13,.6); emptyLabel.TextAlignment=TextAlignment.Center; emptyLabel.Margin=new Thickness(12,18,12,22);
        empty = new PickerFold(emptyLabel,false);
        scroll = new ScrollViewer { Content=list, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled, VerticalScrollBarVisibility=ScrollBarVisibility.Hidden };
        if (eager < items.Count) fill = new DeferredRows(list, scroll, eager, heights, 0, i => items[i]);
        list.Children.Add(empty);
        PickerScroll.Apply(scroll);
        stack.Children.Add(scroll);
        scrollOffset=new(scroll,0,v=>scroll.Offset=new Vector(0,v));
        contentHeight=models.Count*47+groups.Count*24+2;TargetListHeight = Math.Min(440,contentHeight);
        height = new AnimatedScalar(scroll,TargetListHeight,v=>{scroll.Height=v;UpdateMask();});
        topFade=new(scroll,0,_=>UpdateMask());bottomFade=new(scroll,contentHeight>440?22:0,_=>UpdateMask());
        scroll.ScrollChanged += (_,e)=> { if (spacer is { IsHolding: true } && e.OffsetDelta.Y!=0 && scroll.Offset.Y>0) spacer.ReleaseAll(); }; // ED-NEW-stutterfix-8
        scroll.ScrollChanged += (_,_)=>Edges();
        Search.TextChanged += (_,_)=> { if (!changing) QueryChanged(); };
        Search.KeyDown += OnSearchKey;
        Edges();
    }

    private PickerCount CountLabel(int n, bool shown=false)
    {
        if (counts.TryGetValue(Count(n), out var made)) return made;
        var label = new PickerCount(Count(n), shown); counts.Add(Count(n), label);
        var at = countOrder.BinarySearch(n); at = at < 0 ? ~at : at;
        countOrder.Insert(at, n); countGrid.Children.Insert(at, label);
        return label;
    }

    public void ClearSearch() { changing=true; Search.Text=""; changing=false; QueryChanged(); Search.Focus(); }
    private void Pick(string id)
    {
        var row=rows.First(r=>r.Model.Id==id); if (!row.Model.Enabled) return;
        selected=id; foreach (var r in rows) r.SetSelected(r.Model.Id==id); choose(id);
    }
    private void QueryChanged()
    {
        fill?.RealiseAll(); // ED-NEW-stutterfix-2: a search folds every row in the list
        spacer?.ReleaseAll(); // ED-NEW-stutterfix-8: items held by the last search take the values they have reached, and fold on from there
        active=0; var words=Words;
        var shown=new Dictionary<PickerFold,bool>(items.Count);
        foreach (var row in rows)
        {
            var haystack=$"{row.Model.Name} {row.Model.Id} {row.Model.Maker}".ToLowerInvariant();
            shown[row]=words.All(w=>haystack.Contains(w,StringComparison.Ordinal));
        }
        foreach (var g in groups) shown[g.Fold]=rows.Any(r=>r.Model.Maker==g.Maker && shown[r]);
        var held=Unseen(shown);
        foreach (var row in rows) row.Show(shown[row],Hold(held,row));
        foreach (var g in groups) g.Fold.Show(shown[g.Fold],Hold(held,g.Fold));
        var count=Visible.Count(); var next=Count(count);
        if (shownCount!=next) { counts[shownCount].Set(false); CountLabel(count).Set(true); shownCount=next; }
        emptyLabel.Text=$"No models match “{Search.Text}”"; empty.Show(Keyed && count==0);
        clear.IsHitTestVisible=!string.IsNullOrEmpty(Search.Text);
        clearOpacity.Set(clear.IsHitTestVisible?1:0,Animation.TimingCurve(.25,.1,.25,1,.16));
        clearScale.Set(clear.IsHitTestVisible?1:.6,Animation.TimingCurve(.32,.72,0,1,.22));
        contentHeight=count*47+groups.Count(g=>rows.Any(r=>r.Shown && r.Model.Maker==g.Maker))*24+(Keyed&&count==0?58:0)+2;TargetListHeight=Math.Min(440,contentHeight);
        height.Set(TargetListHeight,Ease);TargetScrollOffset=0;scrollOffset.Set(0,null); UpdateActive(); Edges();
    }
    // ED-NEW-stutterfix-8: a search folds the items the viewport can show while the fold runs, as before. The folds of one search share one
    // curve and one start, so an item's top at any frame lies between its tops before and after the fold (each item laid out at least as tall
    // as its folded height): an item whose lesser top is past the tallest viewport and two rows is never seen. It folds on, hidden, and one
    // spacer before the empty note stands for its height as layout rounds it, so the extent and the edges are those of the full fold; it shows
    // again when its fold ends, or with the values reached when a key is pressed, the list is scrolled or the search changes.
    internal static bool HoldUnseenRows = true; // tests compare with the plain fold
    private HashSet<PickerFold>? Unseen(Dictionary<PickerFold,bool> shown)
    {
        if (!HoldUnseenRows) return null;
        HashSet<PickerFold>? held=null; double before=0,after=0;
        for (var k=0; k<items.Count; k++)
        {
            var item=items[k]; var from=item.FoldAmount; var to=shown[item]?1d:0d;
            if (from!=to && Math.Min(before,after)>=MaximumListHeight+2*47) (held??=[]).Add(item);
            before+=heights[k]*from; after+=heights[k]*to;
        }
        return held;
    }
    private PickerHeldSpacer? Hold(HashSet<PickerFold>? held,PickerFold item)
    {
        if (held is null || !held.Contains(item)) return null;
        spacer??=new PickerHeldSpacer();
        if (spacer.Parent is null) list.Children.Insert(list.Children.IndexOf(empty),spacer);
        return spacer;
    }
    private void OnSearchKey(object? sender, KeyEventArgs e)
    {
        if (spacer is { IsHolding: true }) { spacer.ReleaseAll(); scroll.UpdateLayout(); } // ED-NEW-stutterfix-8: keys move through the items as laid out
        if (fill?.RealiseAll()==true) scroll.UpdateLayout(); // ED-NEW-stutterfix-2: rows just put in are laid out before one is scrolled to
        var visible=Visible.ToArray(); if (!Keyed || visible.Length==0) return;
        if (e.Key is Key.Down or Key.Up)
        {
            active=Math.Clamp(active+(e.Key==Key.Down?1:-1),0,visible.Length-1); UpdateActive(); BringNearest(visible[active]); e.Handled=true;
        }
        else if (e.Key==Key.Enter) { Pick(visible[active].Model.Id); e.Handled=true; }
    }
    // Source requests smooth nearest scrolling, leaving duration to the browser. On the port's
    // frame clock use the card's own .34 s ease; Windows live comparison remains required.
    private void BringNearest(PickerRow row)
    {
        var y=row.TranslatePoint(default,(Visual)scroll.Content!)?.Y??row.Bounds.Y;
        var at=scroll.Offset.Y;var target=y<at?y:y+row.Bounds.Height>at+scroll.Viewport.Height?y+row.Bounds.Height-scroll.Viewport.Height:at;
        TargetScrollOffset=Math.Clamp(target,0,Math.Max(0,scroll.Extent.Height-scroll.Viewport.Height));
        scrollOffset.Set(at,null);scrollOffset.Set(TargetScrollOffset,Ease);
    }
    private void UpdateActive() { foreach(var row in rows) row.SetActive(row.Model.Id==ActiveId,Keyed); }
    private void Edges()
    {
        var overflow=contentHeight>440;
        var top=overflow&&scroll.Offset.Y>1 ? 16d:0d;
        var bottom=overflow&&scroll.Offset.Y+scroll.Viewport.Height<scroll.Extent.Height-1 ? 22d:0d;
        topFade?.Set(top,Animation.TimingCurve(.25,.1,.25,1,.2));bottomFade?.Set(bottom,Animation.TimingCurve(.25,.1,.25,1,.2));UpdateMask();
    }
    private void UpdateMask()
    {
        if(scroll is null)return;var top=topFade?.Value??0;var bottom=bottomFade?.Value??0;var h=Math.Max(1,scroll.Height);
        scroll.OpacityMask = new LinearGradientBrush { StartPoint=new RelativePoint(0,0,RelativeUnit.Relative), EndPoint=new RelativePoint(0,1,RelativeUnit.Relative),
            GradientStops=[new(Colors.Transparent,0),new(Colors.Black,top/h),new(Colors.Black,Math.Max(top/h,1-bottom/h)),new(Colors.Transparent,1)] };
    }
    internal static Button Button(Control content,Action click)
    {
        var button=PlainControls.Button(content,click);
        // The plain-button helper draws only content: this design also paints the hover/press background and its padding.
        button.Template=new FuncControlTemplate<Button>((b,_)=>new Border
        {
            [!Border.BackgroundProperty]=b[!Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty],
            [!Border.PaddingProperty]=b[!Avalonia.Controls.Primitives.TemplatedControl.PaddingProperty],
            [!Border.CornerRadiusProperty]=b[!Avalonia.Controls.Primitives.TemplatedControl.CornerRadiusProperty],
            Child=new ContentPresenter{[!ContentPresenter.ContentProperty]=b[!ContentControl.ContentProperty]}
        });return button;
    }
    private static string Count(int n)=>n==1?"1 model":n.ToString(System.Globalization.CultureInfo.InvariantCulture)+" models";
    internal static TextBlock Label(string text,double size,double alpha)=>new() { Text=text, FontSize=size, Foreground=Brush(.957,.965,.984,alpha), VerticalAlignment=Avalonia.Layout.VerticalAlignment.Center, TextTrimming=TextTrimming.CharacterEllipsis };
    internal static SolidColorBrush Brush(double r,double g,double b,double a)=>new(Color.FromArgb((byte)Math.Round(a*255),(byte)Math.Round(r*255),(byte)Math.Round(g*255),(byte)Math.Round(b*255)));
    internal static Control Dot(string color,double size)
        => new Border { Width=size, Height=size, CornerRadius=new CornerRadius(size/2), HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Center, Background=new SolidColorBrush(Color.Parse(color)), BorderThickness=new Thickness(1), BorderBrush=Brush(1,1,1,.28), VerticalAlignment=Avalonia.Layout.VerticalAlignment.Center };
    internal void UpdateMakerDots(){foreach(var group in rows.GroupBy(r=>r.Model.Maker,StringComparer.Ordinal))makerDots[group.Key].To(group.First().TargetColour,Animation.Smooth(1.2));}
}

public sealed class PickerRow : PickerFold
{
    private readonly Marrow.UI.Port.Settings.CzColor colour;
    public Color Colour=>colour.Value;
    internal Color TargetColour=>colour.Target;
    internal void SetColour(string hex)=>colour.To(Color.Parse(hex),Animation.Smooth(1.2));
    private readonly AnimatedScalar checkOpacity, checkScale;
    private readonly AnimatedScalar background,pressedScale;
    private bool hovered,active,keyed,pressed;
    public PickerModel Model { get; }
    public Button Button { get; }
    public bool Shown { get; private set; }=true;
    internal PickerRow(PickerModel model,bool selected,Action pick):base(null,true)
    {
        Model=model;
        var grid=new Grid { ColumnDefinitions=new ColumnDefinitions("18,*,14"), ColumnSpacing=10 };
        var dot=(Border)OpenRouterPicker.Dot(model.Color,10);grid.Children.Add(dot);var brush=(SolidColorBrush)dot.Background!;
        colour=new(new Marrow.UI.Port.Settings.CzMotion(dot),brush.Color,c=>brush.Color=c);
        var labels=new StackPanel { Spacing=1, VerticalAlignment=Avalonia.Layout.VerticalAlignment.Center };
        labels.Children.Add(OpenRouterPicker.Label(model.Name,14,1)); labels.Children.Add(OpenRouterPicker.Label(model.Detail??model.Id+" · OpenRouter",12,.55)); Grid.SetColumn(labels,1); grid.Children.Add(labels);
        var check=new PickerGlyph("check"); Grid.SetColumn(check,2); grid.Children.Add(check);
        checkOpacity=new AnimatedScalar(check,selected?1:0,v=>check.Opacity=v);
        checkScale=new AnimatedScalar(check,selected?1:.6,v=>check.RenderTransform=new ScaleTransform(v,v));
        Button=OpenRouterPicker.Button(grid,pick); Button.Height=46; Button.Padding=new Thickness(10,0); Button.Margin=new Thickness(0,0,0,1); Button.CornerRadius=new CornerRadius(9); Button.IsEnabled=model.Enabled;
        background=new(Button,0,v=>Button.Background=OpenRouterPicker.Brush(1,1,1,v));
        pressedScale=new(Button,1,v=>Button.RenderTransform=new ScaleTransform(v,v));
        Button.PointerEntered+=(_,_)=>{hovered=true;Paint();};Button.PointerExited+=(_,_)=>{hovered=false;Paint();};
        Button.PropertyChanged+=(_,e)=>{if(e.Property==Avalonia.Controls.Button.IsPressedProperty){pressed=Button.IsPressed;Paint();}};
        AutomationProperties.SetName(Button,model.Name); Child=Button;
    }
    internal new void Show(bool shown,PickerHeldSpacer? hold=null) { Shown=shown; Button.IsHitTestVisible=shown; base.Show(shown,hold); }
    internal void SetActive(bool active,bool keyed){this.active=active;this.keyed=keyed;Paint();}
    private void Paint()
    {var ease=Animation.TimingCurve(.25,.1,.25,1,.12);background.Set(pressed?.14:active||hovered&&!keyed?.1:0,ease);pressedScale.Set(pressed?.99:1,ease);}
    internal void SetSelected(bool selected)
    {
        checkOpacity.Set(selected?1:0,Animation.TimingCurve(.25,.1,.25,1,.16));
        checkScale.Set(selected?1:.6,Animation.TimingCurve(.32,.72,0,1,.24));
    }
}

public class PickerFold : Decorator
{
    private readonly AnimatedScalar fold,opacity,scale;
    private PickerHeldSpacer? holder; // ED-NEW-stutterfix-8: folding hidden, its height in the holder
    private double foldTarget;
    public double FoldAmount=>fold.Value;
    public double FoldOpacity=>opacity.Value;
    internal bool Held=>holder is not null;
    public PickerFold(Control? child,bool shown)
    {
        Child=child; ClipToBounds=true;
        fold=new AnimatedScalar(this,shown?1:0,v=> { if(holder is {} h){h.InvalidateMeasure(); if(v==foldTarget)Unhold();} else InvalidateMeasure(); });
        opacity=new AnimatedScalar(this,shown?1:0,v=> { if(holder is null)Opacity=v; });
        scale=new AnimatedScalar(this,shown?1:.96,v=> { if(holder is null && Child is {} c)c.RenderTransform=new ScaleTransform(v,v); });
    }
    internal void Show(bool shown,PickerHeldSpacer? hold=null)
    {
        foldTarget=shown?1:0;
        fold.Set(shown?1:0,Animation.TimingCurve(.25,.1,.25,1,.34));
        opacity.Set(shown?1:0,Animation.Linear(.34)); scale.Set(shown?1:.96,Animation.TimingCurve(.25,.1,.25,1,.34));
        if (hold is not null && fold.Value!=foldTarget) { holder=hold; IsVisible=false; hold.Add(this); }
    }
    // Shows again with the values its folds have reached, as the plain fold has drawn them.
    internal void Unhold()
    {
        if (holder is not {} h) return;
        holder=null; h.Remove(this);
        IsVisible=true; Opacity=opacity.Value; if(Child is {} c)c.RenderTransform=new ScaleTransform(scale.Value,scale.Value); InvalidateMeasure();
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        Child?.Measure(new Size(availableSize.Width,double.PositiveInfinity));
        return new Size(Child?.DesiredSize.Width??0,(Child?.DesiredSize.Height??0)*FoldAmount);
    }
    protected override Size ArrangeOverride(Size finalSize) { Child?.Arrange(new Rect(0,(finalSize.Height-Child.DesiredSize.Height)/2,finalSize.Width,Child.DesiredSize.Height)); return finalSize; }
}

internal sealed class PickerCount : Decorator
{
    private readonly AnimatedScalar opacity,position;
    public PickerCount(string text,bool shown)
    {
        Child=OpenRouterPicker.Label(text,11,.45);
        SwiftFont.System(11,design:SwiftFont.Design.Monospaced).MonospacedDigit().Apply((TextBlock)Child);
        opacity=new AnimatedScalar(this,shown?1:0,v=>Opacity=v);
        position=new AnimatedScalar(this,shown?0:3,v=>RenderTransform=new TranslateTransform(0,v));
    }
    public void Set(bool shown)
    {
        opacity.Set(shown?1:0,Animation.TimingCurve(.25,.1,.25,1,shown?.16:.09).Delay(shown?.08:0));
        position.Set(shown?0:3,Animation.TimingCurve(shown?.32:.25,shown?.72:.1,shown?0:.25,1,shown?.24:.18).Delay(shown?.08:0));
    }
}

// ED-NEW-stutterfix-8: the height the held folds of a search add to the list at each frame, each as layout rounds a fold's height.
internal sealed class PickerHeldSpacer : Control
{
    private readonly List<(PickerFold Fold, double Height)> held = [];
    public bool IsHolding => held.Count > 0;
    public PickerHeldSpacer() { IsHitTestVisible = false; }
    internal void Add(PickerFold fold) { held.Add((fold, fold.Child?.DesiredSize.Height is > 0 and var h ? h : fold is PickerRow ? 47 : 24)); InvalidateMeasure(); }
    internal void Remove(PickerFold fold)
    {
        held.RemoveAll(x => x.Fold == fold); InvalidateMeasure();
        if (held.Count == 0 && Parent is Panel panel) panel.Children.Remove(this);
    }
    internal void ReleaseAll() { foreach (var (fold, _) in held.ToArray()) fold.Unhold(); }
    protected override Size MeasureOverride(Size availableSize)
    {
        var scale = LayoutHelper.GetLayoutScale(this); var sum = 0.0;
        foreach (var (fold, height) in held) sum += fold.UseLayoutRounding ? LayoutHelper.RoundLayoutValueUp(height * fold.FoldAmount, scale) : height * fold.FoldAmount;
        return new Size(0, sum);
    }
}
