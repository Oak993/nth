// PORT-ONLY: SK-4 / DL-4, OD-0099 amendments; skills-ONDEMAND-PLAN 3.3-3.4 and skills-PLAN 2.1.

namespace Marrow.Services.Port.Skills.Install;

public sealed record InstallCommand(string Executable, IReadOnlyList<string> Arguments, string WorkingDirectory)
{
    /// <summary>
    /// Windows Installer property syntax: an argument <c>NAME=value</c> (public property name) reaches the child as
    /// <c>NAME="value"</c>, the form "Setting Public Property Values on the Command Line" documents for values with
    /// spaces, instead of the whole-argument quoting ArgumentList applies (ED-NEW-integ-sk4-sk5 (1)).
    /// </summary>
    public bool QuotePropertyValues { get; init; }
}
