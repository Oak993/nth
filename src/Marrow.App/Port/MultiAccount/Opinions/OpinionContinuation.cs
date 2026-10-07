// PORT-ONLY: MA-3b-7, locked page :4871. W supplies the thread's actual current account and optional pill step.
using System.Globalization;
namespace Marrow.App.Port.MultiAccount.Opinions;
internal sealed record OpinionContinuation(string Account, string? Pill = null, int? Step = null)
{
    internal string Note => "That was for this message only · the thread carries on with " +
        (Pill is not null && Step is { } step ? Pill + ", step " + step.ToString(CultureInfo.InvariantCulture) + ", " : "") + Account;
}
