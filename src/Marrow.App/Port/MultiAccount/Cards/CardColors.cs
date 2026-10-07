// PORT-ONLY: locked multi-account page light/dark tokens :3-4 for cards and notes.
using Marrow.UI.Port;
namespace Marrow.App.Port.MultiAccount.Cards;
internal static class CardColors
{
    internal static SwiftColor Divider => SwiftColor.Dynamic(SwiftColor.Black.Opacity(.1), SwiftColor.White.Opacity(.12));
    internal static SwiftColor Capsule => SwiftColor.Dynamic(SwiftColor.White.Opacity(.55), SwiftColor.Black.Opacity(.2));
    internal static SwiftColor Tile => SwiftColor.Dynamic(SwiftColor.Black.Opacity(.05), SwiftColor.White.Opacity(.07));
    internal static SwiftColor Rim => SwiftColor.Dynamic(SwiftColor.Black.Opacity(.22), SwiftColor.White.Opacity(.22));
}
