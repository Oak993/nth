using System.Globalization;
using Marrow.Core.Port.Voice;

namespace Marrow.Core.Tests.Port.Voice;

// VT-14 (first-release voice, voice-SPEC V-16.3.2, V-T93; Q-V-18 "Checked on {date}"): the words of the hardware card and when each is shown.
public sealed class HardwareCardTextTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly CultureInfo British = CultureInfo.GetCultureInfo("en-GB");
    private static readonly CultureInfo American = CultureInfo.GetCultureInfo("en-US");

    private static VoiceHardwareRecord Record(HardwareOrigin origin, DateTimeOffset at) => new() { Origin = origin, At = at };

    [Fact]
    [Trait("Feature", "VT-14")]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // V-16.3.2: never checked / running / done by itself at first run
    public void The_detail_says_not_checked_checking_or_checked_by_itself()
    {
        Assert.Equal("Not checked on this computer yet", HardwareCardText.Detail(HardwareCardPhase.Idle, null, At, British, TimeZoneInfo.Utc));
        Assert.Equal("Checking this computer", HardwareCardText.Detail(HardwareCardPhase.Running, null, At, British, TimeZoneInfo.Utc));
        Assert.Equal("Checking this computer", HardwareCardText.Detail(HardwareCardPhase.Running, Record(HardwareOrigin.Manual, At), At, British, TimeZoneInfo.Utc));
        Assert.Equal("Checked by itself when voice typing was set up", HardwareCardText.Detail(HardwareCardPhase.Idle, Record(HardwareOrigin.Auto, At.AddDays(-9)), At, British, TimeZoneInfo.Utc));
    }

    [Fact]
    [Trait("Feature", "VT-14")]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // V-16.3.2: a check run in this session reads `Checked just now`
    public void A_check_run_a_moment_ago_reads_Checked_just_now()
    {
        var record = Record(HardwareOrigin.Manual, At);
        Assert.Equal("Checked just now", HardwareCardText.Detail(HardwareCardPhase.Done, record, At, British, TimeZoneInfo.Utc));
        Assert.Equal("Checked just now", HardwareCardText.Detail(HardwareCardPhase.Idle, record, At.AddMinutes(4).AddSeconds(59), British, TimeZoneInfo.Utc));
    }

    [Fact]
    [Trait("Feature", "VT-14")]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // Q-V-18 (ED-NEW-ledger-5 item 7): a later visit reads `Checked on {date}`, because "just now" would be false
    public void A_later_visit_reads_Checked_on_the_date()
    {
        var record = Record(HardwareOrigin.Manual, At);
        Assert.Equal("Checked on 5 October 2026", HardwareCardText.Detail(HardwareCardPhase.Idle, record, At.AddMinutes(5), British, TimeZoneInfo.Utc));
        Assert.Equal("Checked on October 5, 2026", HardwareCardText.Detail(HardwareCardPhase.Idle, record, At.AddDays(30), American, TimeZoneInfo.Utc));
        Assert.Equal("Checked on 5 October 2026", HardwareCardText.Detail(HardwareCardPhase.Done, record, At.AddHours(3), British, TimeZoneInfo.Utc));
    }

    [Fact]
    [Trait("Feature", "VT-14")]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // the date is shown in the local day of the check
    public void The_date_is_the_local_day_of_the_check()
    {
        var evening = new DateTimeOffset(2026, 10, 5, 23, 30, 0, TimeSpan.Zero);
        var plusTwo = TimeZoneInfo.CreateCustomTimeZone("plus2", TimeSpan.FromHours(2), "plus2", "plus2");
        Assert.Equal("Checked on 6 October 2026", HardwareCardText.Detail(HardwareCardPhase.Idle, Record(HardwareOrigin.Manual, evening), evening.AddDays(2), British, plusTwo));
    }

    [Fact]
    [Trait("Feature", "VT-14")]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // V-16.3.2: the capsule reads `Check my hardware` / `Checking` / `Check again`
    public void The_button_reads_Check_my_hardware_Checking_or_Check_again()
    {
        Assert.Equal("Check my hardware", HardwareCardText.ButtonLabel(HardwareCardPhase.Idle, checkedBefore: false));
        Assert.Equal("Checking", HardwareCardText.ButtonLabel(HardwareCardPhase.Running, checkedBefore: false));
        Assert.Equal("Checking", HardwareCardText.ButtonLabel(HardwareCardPhase.Running, checkedBefore: true));
        Assert.Equal("Check again", HardwareCardText.ButtonLabel(HardwareCardPhase.Done, checkedBefore: true));
        Assert.Equal("Check again", HardwareCardText.ButtonLabel(HardwareCardPhase.Idle, checkedBefore: true));
    }

    [Fact]
    [Trait("Feature", "VT-14")]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // V-16.3.2: the recommendation box's buttons and state line, and the card's fixed words
    public void The_recommendation_box_and_the_card_use_the_words_of_V_16_3_2()
    {
        Assert.Equal("Recommended for this PC", HardwareCardText.Title);
        Assert.Equal("Use recommended", HardwareCardText.UseRecommended);
        Assert.Equal("Keep Large v3 Turbo", HardwareCardText.Keep("Large v3 Turbo"));
        Assert.Equal("In use", HardwareCardText.InUse);
        Assert.Equal("Downloading, then in use", HardwareCardText.DownloadingThenInUse);
        Assert.Equal("You kept Small", HardwareCardText.Kept("Small"));
        Assert.Equal("The first time voice typing is set up, this check runs by itself and installs the model it recommends. Run it again any time.", HardwareCardText.Note);
        Assert.Equal(["Processor", "Memory", "Graphics", "GPU backend"], HardwareCardText.ProbeKeys);
    }
}
