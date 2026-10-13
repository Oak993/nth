using Marrow.Setup.Motion;
using Marrow.Setup.Shell;
using Marrow.Setup.Tests.Fakes;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Shell;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3, "Close", and 10, "Close and shutdown"; unit U1 (ED-NEW-inst-u1-1).
// Close works until the install starts. From then until it is done Close is paused, not hidden: the screen's line says
// why, every try is refused and brightens it, and sign-out and shutdown are held with a reason that is cleared at the end.
[Collection("SetupHeadless")]
public sealed class CloseGuardTests(SetupHeadlessSession headless)
{
    private const string Words = "You can close this window once nth is safely in place.";
    private const string Reason = "Finishing installing nth";

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Close_works_before_anything_has_started()
    {
        headless.OnUiThread(() =>
        {
            var guard = new CloseGuard(new FakeShutdownBlocker());

            Assert.False(guard.IsPaused);
            Assert.True(guard.TryClose());
            Assert.Equal(0, guard.Refusals);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Pausing_shows_the_line_and_holds_shutdown_with_the_reason()
    {
        headless.OnUiThread(() =>
        {
            var blocker = new FakeShutdownBlocker();
            var guard = new CloseGuard(blocker);
            var changes = new List<bool>();
            guard.PausedChanged += changes.Add;
            var line = new HoldLine(() => false);

            guard.Pause(line, Words, Reason);

            Assert.True(guard.IsPaused);
            Assert.True(line.IsWanted);
            Assert.Equal(Words, line.Words);
            Assert.Equal(Reason, blocker.Reason);
            Assert.Equal(new[] { true }, changes);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Every_try_while_paused_is_refused_and_brightens_the_line()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource();
            var guard = new CloseGuard(new FakeShutdownBlocker());
            var line = new HoldLine(() => false);
            line.Attach(new Animator(frames));
            var refused = 0;
            guard.Refused += () => refused++;
            guard.Pause(line, Words, Reason);
            frames.Run(1);

            Assert.False(guard.TryClose());
            Assert.False(guard.TryClose());
            frames.Run(0.3);

            Assert.Equal(2, guard.Refusals);
            Assert.Equal(2, refused);
            Assert.Equal(1, line.Brightness);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Resuming_clears_the_shutdown_reason_and_close_works_again()
    {
        headless.OnUiThread(() =>
        {
            var blocker = new FakeShutdownBlocker();
            var guard = new CloseGuard(blocker);
            var changes = new List<bool>();
            guard.PausedChanged += changes.Add;
            guard.Pause(new HoldLine(() => false), Words, Reason);

            guard.Resume();
            guard.Resume();

            Assert.False(guard.IsPaused);
            Assert.Null(blocker.Reason);
            Assert.Equal(new[] { "block:" + Reason, "release" }, blocker.Calls);
            Assert.Equal(new[] { true, false }, changes);
            Assert.True(guard.TryClose());
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Pausing_on_another_screen_moves_the_line_there()
    {
        headless.OnUiThread(() =>
        {
            var guard = new CloseGuard(new FakeShutdownBlocker());
            var first = new HoldLine(() => false);
            var second = new HoldLine(() => false);
            var changes = new List<bool>();
            guard.PausedChanged += changes.Add;

            guard.Pause(first, Words, Reason);
            guard.Pause(second, "You can close this window once the new version is in place.", Reason);

            Assert.False(first.IsWanted);
            Assert.True(second.IsWanted);
            Assert.Equal(new[] { true }, changes);
        });
    }
}
