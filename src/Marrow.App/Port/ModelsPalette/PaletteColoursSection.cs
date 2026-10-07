// PORT-ONLY: OD-0127 option A, existing Settings row/button and Updates progress style; ED-618.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Port.Settings;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
namespace Marrow.App.Port.ModelsPalette;
internal sealed class PaletteColoursSection : Decorator
{
    private readonly PaletteColoursController controller;
    private readonly StackPanel dots=new(){Orientation=Orientation.Horizontal,Spacing=4,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,14,0)};
    private readonly Dictionary<string,PaletteMakerDot> dotViews=new(StringComparer.Ordinal);
    private readonly WrapPanel summary=new(){Orientation=Orientation.Horizontal,Margin=new Thickness(14,0,14,12)};
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(30)};
    public string SectionTitle=>"Colours";
    public string RowTitle=>"Model colours";
    public UpdatesMainButton RefreshButton {get;}=new("Refresh",UpdatesButtonMode.Quiet);
    public UpdatesProgressBar ProgressBar {get;}=new(){IsVisible=false,Margin=new Thickness(14,0,14,12)};
    public PaletteDetailText DetailText {get;}=new();
    public IReadOnlyDictionary<string,PaletteMakerDot> DotViews=>dotViews;
    public PaletteColoursSection(PaletteColoursController controller)
    {
        this.controller=controller;
        var row=new ChromeRow(RowTitle,()=>RefreshButton);
        var grid=(Grid)((Border)row.Child!).Child!;var labels=(StackPanel)grid.Children[0];
        labels.Children.Add(DetailText);
        grid.Children.Remove(labels);var leading=new Grid{ColumnDefinitions=new ColumnDefinitions("Auto,*")};leading.Children.Add(dots);Grid.SetColumn(labels,1);leading.Children.Add(labels);grid.Children.Insert(0,leading);
        var content=new StackPanel{Children={row,ProgressBar,summary}};
        Child=new ChromeSection(SectionTitle,()=>new ChromeCard(()=>content));
        RefreshButton.Invoked+=()=>_ =controller.Refresh();controller.Changed+=Apply;
        timer.Tick+=(_,_)=>controller.Tick();
        AttachedToVisualTree+=(_,_)=>{controller.Attach();timer.Start();Apply();};
        DetachedFromVisualTree+=(_,_)=>{timer.Stop();controller.Detach();};Apply();
    }
    private void Apply()
    {
        var v=controller.View;DetailText.SetText(v.Detail);
        RefreshButton.Apply(v.Running?"Refreshing…":"Refresh",v.Running?UpdatesButtonMode.Busy:UpdatesButtonMode.Quiet);RefreshButton.IsEnabled=!v.Running;
        ProgressBar.IsVisible=ProgressBar.Active=v.Running;ProgressBar.Set(v.Total>0?(double)v.Completed/v.Total:null);
        var keys=v.Dots.Select(d=>d.Maker).ToHashSet(StringComparer.Ordinal);
        foreach(var id in dotViews.Keys.Where(id=>!keys.Contains(id)).ToArray()){dots.Children.Remove(dotViews[id]);dotViews.Remove(id);}
        foreach(var dot in v.Dots)
        {
            if(!dotViews.TryGetValue(dot.Maker,out var view)){view=new(dot.Colour,IsAttachedToVisualTree());dotViews.Add(dot.Maker,view);dots.Children.Add(view);ToolTip.SetTip(view,dot.Name);view.Arrive();}
            else view.Set(dot.Colour);
        }
        summary.IsVisible=v.Summary.Length>0;
        if(summary.Tag is string[] old&&old.SequenceEqual(v.Summary,StringComparer.Ordinal))return;
        summary.Tag=v.Summary;summary.Children.Clear();
        foreach(var text in v.Summary)
        {
            var line=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6,Margin=new Thickness(0,0,14,6)};
            if(text.StartsWith("New maker: ",StringComparison.Ordinal)||text.EndsWith("'s shades re-spaced",StringComparison.Ordinal))
            {
                var name=text.StartsWith("New maker: ",StringComparison.Ordinal)?text[11..]:text[..^19];var maker=controller.FindMaker(name);if(maker is not null)line.Children.Add(new PaletteMakerDot(maker.Colour,false){Width=9,Height=9});
            }
            else line.Children.Add(new TextBlock{Text="✓",FontSize=13,Foreground=new SolidColorBrush(Color.Parse("#64C98A")),VerticalAlignment=VerticalAlignment.Center});
            var label=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Foreground=Chrome.SecondaryText.Brush(this)};SwiftFont.System(12).Apply(label);line.Children.Add(label);summary.Children.Add(line);
        }
    }
    private bool IsAttachedToVisualTree()=>VisualRoot is not null;
}
internal sealed class PaletteMakerDot : Border
{
    private readonly CzColor colour;
    private readonly CzNumber scale;
    private readonly bool arriving;
    public Color Colour=>colour.Value;
    public Color TargetColour=>colour.Target;
    public double Scale=>scale.Value;
    public PaletteMakerDot(string hex,bool arriving)
    {
        Width=Height=12;CornerRadius=new CornerRadius(6);VerticalAlignment=VerticalAlignment.Center;IsHitTestVisible=false;
        var brush=new SolidColorBrush(Color.Parse(hex));Background=brush;var motion=new CzMotion(this);
        colour=new(motion,brush.Color,c=>brush.Color=c);this.arriving=arriving;
        var transform=new ScaleTransform(1,1);RenderTransform=transform;RenderTransformOrigin=RelativePoint.Center;
        scale=new(motion,arriving?0:1,v=>transform.ScaleX=transform.ScaleY=v);transform.ScaleX=transform.ScaleY=scale.Value;
    }
    public void Arrive(){if(arriving)scale.To(1,CzMotion.Ease(.6));}
    public void Set(string hex)=>colour.To(Color.Parse(hex),Animation.TimingCurve(.4,0,.2,1,1.1));
}
