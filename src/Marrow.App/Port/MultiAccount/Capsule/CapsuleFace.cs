// PORT-ONLY: multi-account step 3a. The capsule's face: a button with no template that draws the capsule (CapsulePainter). A Button gives it focus, Enter and Space, the screen reader's role and Click for free.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Marrow.App.Port.MultiAccount.Capsule;

internal sealed class CapsuleFace : Button
{
    private readonly AccountCapsule owner;

    public CapsuleFace(AccountCapsule owner)
    {
        this.owner = owner;
        Focusable = true;
        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        Padding = default;
        BorderThickness = default;
        Background = null;
        Template = null;
    }

    protected override Size MeasureOverride(Size availableSize) => owner.FaceSize;

    public override void Render(DrawingContext context) => owner.Paint(context);
}
