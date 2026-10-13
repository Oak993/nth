using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Marrow.App.Tests.Fixtures;

/// <summary>OD-0147: a mounted view with only its glass pieces drawn (their glyphs, labels and every other visual set aside, at opacity 0), so a line
/// through a piece's middle reads its glass alone.</summary>
internal static class GlassAloneScene
{
    public static void Hide(Visual root, IReadOnlyCollection<Control> pieces)
    {
        var held = new HashSet<Visual>(pieces.SelectMany(piece => piece.GetSelfAndVisualAncestors()));
        foreach (var visual in root.GetVisualDescendants().ToArray())
            if (!held.Contains(visual) && visual.GetVisualParent() is { } parent && held.Contains(parent)) visual.Opacity = 0;
    }
}
