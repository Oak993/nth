// PORT-ONLY: locked page account hues, same palettes as ComposerCapsuleModel :21-28. The ordered account's mark, never a provider capability.
using Avalonia.Media;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Thread;
namespace Marrow.App.Port.MultiAccount.Shared;
internal static class AccountMarkColors
{
    internal static Color Of(ThreadAccount? account)
    {
        var n = Math.Max(0, (account?.Number ?? 1) - 1);
        uint rgb = account?.Service switch
        {
            ProviderKind.Claude => (n % 3) switch { 0 => 0xD97757u, 1 => 0x8E7CFFu, _ => 0x2BB3A3u },
            ProviderKind.Codex => n % 2 == 0 ? 0x10A37Fu : 0x5B8DEFu,
            ProviderKind.OpenRouter => n % 2 == 0 ? 0x4D6BFEu : 0xE0A030u,
            ProviderKind.Local => n % 2 == 0 ? 0x8A94A6u : 0xB08D57u,
            _ => 0x8A94A6u,
        };
        return Color.FromRgb((byte)(rgb >> 16), (byte)((rgb >> 8) & 255), (byte)(rgb & 255));
    }
}
