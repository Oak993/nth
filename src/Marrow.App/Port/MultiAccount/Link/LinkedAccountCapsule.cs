// PORT-ONLY: locked capsule A :2352-2366. Runtime augmentation of the existing plain capsule: one glass ground, ordered marks, no edits to base/T16 files.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Views.Settings;
using Marrow.App.Port.MultiAccount.Shared;
using Marrow.UI.Chrome;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount.Link;
using Marrow.UI.Port;
namespace Marrow.App.Port.MultiAccount.Link;
internal sealed class LinkedAccountCapsule : Decorator
{
    private readonly LinkUpModel model;
    private readonly Control existing;
    private readonly CrossfadeSlot marks = new();
    private readonly StackPanel fallback;
    private readonly AnimatedScalar width;
    private AccountCapsule? joined;
    private Control? ground;
    private Control[] oldLayers = [];
    private double[] oldOpacities = [];
    private string key = "", lastSuffix = "";
    private bool naming;
    internal IReadOnlyList<string> Marks => model.Linked.Select(a => a.Mono).ToArray();
    internal string Aria => "Account: " + model.Active?.Name + Suffix;
    private string Suffix => model.Linked.Count == 0 ? "" : ", linked with " + string.Join(" and ", model.Linked.Select(a => a.Name)) + " to check its work";
    internal LinkedAccountCapsule(Control capsule, LinkUpModel model)
    {
        this.model = model; existing = capsule; HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        marks.Show(new Border(), null);
        fallback = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { capsule, marks } }; Child = fallback;
        width = new AnimatedScalar(this, 34, v => { if (joined is not null) { joined.Width = v; joined.InvalidateMeasure(); if (model.Linked.Count == 0 && Math.Abs(v - joined.FaceSize.Width) < 1e-9) Restore(); } InvalidateMeasure(); });
        AttachedToVisualTree += (_, _) => { model.Changed += Changed; Refresh(); };
        DetachedFromVisualTree += (_, _) => { model.Changed -= Changed; Restore(); };
        LayoutUpdated += (_, _) => { if (model.Linked.Count > 0 && joined is null && FindCapsule() is not null) { key = ""; Refresh(); } };
    }
    private AccountCapsule? FindCapsule() => existing.GetVisualDescendants().OfType<AccountCapsule>().FirstOrDefault(c => c.Style == "plain");
    private void Changed() { if (Dispatcher.UIThread.CheckAccess()) Refresh(); else Dispatcher.UIThread.Post(Refresh); }
    private void Join(AccountCapsule capsule)
    {
        joined = capsule; oldLayers = capsule.Children.ToArray(); oldOpacities = oldLayers.Select(c => c.Opacity).ToArray();
        foreach (var layer in oldLayers) layer.Opacity = 0; // retained input/usage source; shared painter draws its expanded outline below.
        ground = new LinkedCapsulePaint(capsule).ChromeGlassCapsule(); ground.IsHitTestVisible = false;
        fallback.Children.Remove(marks); marks.Margin = new Thickness(8, 0); marks.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        capsule.Children.Add(ground); capsule.Children.Add(marks);
        capsule.Face.PropertyChanged += FaceChanged;
    }
    private void FaceChanged(object? sender, AvaloniaPropertyChangedEventArgs e) { if (e.Property == AutomationProperties.NameProperty && !naming) NameFace(); }
    private void NameFace()
    {
        if (joined is null || naming) return; naming = true;
        try
        {
            var name = AutomationProperties.GetName(joined.Face) ?? "";
            if (lastSuffix.Length > 0) name = name.Replace(lastSuffix, "", StringComparison.Ordinal);
            lastSuffix = Suffix; var at = name.IndexOf(", ", StringComparison.Ordinal);
            AutomationProperties.SetName(joined.Face, name.Insert(at < 0 ? name.Length : at, lastSuffix));
        }
        finally { naming = false; }
    }
    private void Restore()
    {
        if (joined is null) return;
        joined.Face.PropertyChanged -= FaceChanged;
        joined.Children.Remove(marks); if (ground is not null) joined.Children.Remove(ground);
        for (var i = 0; i < oldLayers.Length; i++) oldLayers[i].Opacity = oldOpacities[i];
        joined.Width = double.NaN;
        if (lastSuffix.Length > 0) AutomationProperties.SetName(joined.Face, (AutomationProperties.GetName(joined.Face) ?? "").Replace(lastSuffix, "", StringComparison.Ordinal));
        lastSuffix = ""; joined = null; ground = null; marks.Margin = default;
        marks.Show(new Border(), null); fallback.Children.Add(marks);
    }
    private void Refresh()
    {
        this.Named(Aria); NameFace();
        var nextKey = model.Active?.Id + ":" + model.Active?.Mono + ":" + model.Active?.Number + "|" + string.Join("|", model.Linked.Select(a => a.Id + ":" + a.Name + ":" + a.Mono + ":" + a.Number));
        if (nextKey == key) return; key = nextKey;
        if (joined is null && model.Linked.Count > 0 && FindCapsule() is { } actual) Join(actual);
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 0 };
        var desired = 34d;
        if (joined is not null)
        {
            var mono = model.Active?.Mono ?? joined.Mono;
            row.Children.Add(new Border { Padding = new Thickness(1, 2), Child = LibraryUi.Text(mono, 11, weight: SwiftFont.Weight.Semibold) });
            desired = 16 + CapsulePainter.TextWidth(mono, 11) + 2;
        }
        foreach (var a in model.Linked)
        {
            var id = a.Id;
            var label = new Border { Padding = new Thickness(1, 2), Child = LibraryUi.Text(a.Mono, 11, weight: SwiftFont.Weight.Semibold) }.RoundedRectangle(5, RoundedCornerStyle.Continuous);
            var mark = PlainControls.Button(label, () => model.Unlink(id)); mark.Focusable = true; mark.Named("Unlink " + a.Name, "capsule-unlink-" + id.ToString("N")); ToolTip.SetTip(mark, "Unlink " + a.Name);
            row.Children.Add(new LinkBridge(AccountMarkColors.Of(model.Active), AccountMarkColors.Of(a))); row.Children.Add(mark);
            desired += 20 + CapsulePainter.TextWidth(a.Mono, 11) + 2;
        }
        marks.Show(row, FrameClock.IsBound ? Animation.EaseOut(duration: CapsuleMotion.Reduce(this) ? .16 : .24) : null);
        if (joined is not null) { width.Set(model.Linked.Count == 0 ? joined.FaceSize.Width : Math.Max(34, desired), FrameClock.IsBound ? Animation.EaseOut(duration: .24) : null); NameFace(); }
        InvalidateMeasure();
    }
}
