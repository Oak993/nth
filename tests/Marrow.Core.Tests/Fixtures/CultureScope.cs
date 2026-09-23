using System.Globalization;

namespace Marrow.Core.Tests.Fixtures;

/// <summary>
/// ED-094: the port's Calendar.current is CultureInfo.CurrentCulture.Calendar, so a formatter test pins the culture
/// (and with it the calendar) for its own async flow and restores the previous one when disposed. CurrentCulture is
/// per async flow in .NET, so parallel test classes never see each other's culture.
/// </summary>
public sealed class CultureScope : IDisposable
{
    private readonly CultureInfo previous;

    private CultureScope(CultureInfo culture)
    {
        previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
    }

    /// <summary>en-US, whose calendar is Gregorian: the culture ED-094 tells tests to pin.</summary>
    public static CultureScope Gregorian() => new(CultureInfo.GetCultureInfo("en-US"));

    /// <summary>Any named culture, for the tests that show the calendar following it.</summary>
    public static CultureScope Of(string name) => new(CultureInfo.GetCultureInfo(name));

    /// <summary>A given culture, such as a writable clone with a changed short-time pattern (ED-134).</summary>
    public static CultureScope Of(CultureInfo culture) => new(culture);

    public void Dispose() => CultureInfo.CurrentCulture = previous;
}
