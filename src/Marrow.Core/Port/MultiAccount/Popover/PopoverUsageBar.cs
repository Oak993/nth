// PORT-ONLY: AccountPanel :2710-2711, uAcc :4300-4301. Unknown usage stays unknown.
using System.Globalization;
namespace Marrow.Core.Port.MultiAccount.Popover;
public sealed record PopoverUsageBar(double? Fraction, string Percent, bool Hot, bool Out)
{
    public static PopoverUsageBar Of(double? fraction, bool outOfService = false)
    {
        var value = fraction is { } n && double.IsFinite(n) ? Math.Clamp(n, 0, 1) : (double?)null;
        return new(value, value is { } v ? Math.Floor(v * 100 + .5).ToString(CultureInfo.InvariantCulture) + "%" : "", value >= .9 && !outOfService, outOfService);
    }
}
