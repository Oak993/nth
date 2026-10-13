using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.UI.NthUi;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Fixtures;

internal static class WebReviewBrushReferences
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static WeakReference<IBrush>[] Drop(Control control, bool live)
    {
        var refs = new WeakReference<IBrush>[100];
        for (var i = 0; i < refs.Length; i++)
        {
            var color = live ? NthUiTokens.Accent.Opacity(.5) : SwiftColor.Dynamic(SwiftColor.Black, SwiftColor.White).InHighContrast(ContrastRole.Text);
            refs[i] = new WeakReference<IBrush>(color.Brush(control));
        }
        return refs;
    }
}
