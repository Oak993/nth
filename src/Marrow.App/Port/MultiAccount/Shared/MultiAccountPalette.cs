// PORT-ONLY: the locked page's multi-account tokens, HTML 3-4. The shared app font is unchanged.
using Marrow.UI.Port;
namespace Marrow.App.Port.MultiAccount.Shared;
internal static class MultiAccountPalette
{
    internal static SwiftColor Primary => SwiftColor.Dynamic(SwiftColor.Rgb(0x1C1C1E), SwiftColor.Rgb(0xECECEC));
    internal static SwiftColor Secondary => Primary.Opacity(.66);
    internal static SwiftColor Faint => Primary.Opacity(.42);
    internal static SwiftColor Accent => SwiftColor.Dynamic(SwiftColor.Rgb(0x0067C0), SwiftColor.Rgb(0x60CDFF));
    internal static SwiftColor AccentInk => SwiftColor.Dynamic(SwiftColor.White, SwiftColor.Rgb(0x06121C));
    internal static SwiftColor Hot => SwiftColor.Dynamic(SwiftColor.Rgb(0xB25E00), SwiftColor.Rgb(0xFFB454));
}
