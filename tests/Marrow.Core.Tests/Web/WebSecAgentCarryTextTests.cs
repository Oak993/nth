using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces security review M3, second pass (the adversarial review of ED-NEW-web-sec-agent-5; DESIGN.md 5.10 "page text
// reaches the model, and a page can tell it to ... 'type this'"; ED-NEW-web-sec-agent-9). WebAgentText.Carries compares text as a page
// would read it back: full-width and other compatibility forms folded (NFKC), invisible format marks dropped, the digits of a number
// whatever single mark stands between them, and the letters and digits of a code or an address whatever stands between them. A code of
// four or five digits counts; a year does not, and neither does an address that only shares a mail domain.
[Trait("Decision", "ED-NEW-web-w11-1")]
public sealed class WebSecAgentCarryTextTests
{
    private const string Mail = "Inbox. Your sign-in code is 483920 and your PIN is 4831; ship to 90210. Reply to lee@example.com about key "
        + "sk-live-7f3a9c21d. Posted in 2026 by ops@gmail.com.";

    [Theory]
    [InlineData("４８３９２０")] // full-width digits
    [InlineData("4​8​3​9​2​0")] // zero-width spaces
    [InlineData("4⁠-8⁠-3-9-2-0")] // word joiners and hyphens
    [InlineData("4 8 3 9 2 0")]
    [InlineData("4.8.3.9.2.0")]
    [InlineData("483 920")] // two pieces, as the engine joins them
    [InlineData("s k - l i v e - 7 f 3 a 9 c 2 1 d")]
    [InlineData("SK_LIVE_7F3A9C21D")]
    [InlineData("l e e @ e x a m p l e . c o m")]
    [InlineData("lee​@​example.com")]
    [InlineData("4831")] // a PIN
    [InlineData("90210")] // five digits
    public void ReadTextDressedUpOrShortIsRecognised(string typed) => Assert.True(WebAgentText.Carries(typed, Mail));

    [Theory]
    [InlineData("2026")] // a year
    [InlineData("support@gmail.com")] // only the mail domain is shared
    [InlineData("Room 12 on floor 3")]
    [InlineData("483")] // three digits
    [InlineData("Hello from the team")]
    public void OwnWordsAYearAndAMailDomainAreNot(string typed) => Assert.False(WebAgentText.Carries(typed, Mail));
}
