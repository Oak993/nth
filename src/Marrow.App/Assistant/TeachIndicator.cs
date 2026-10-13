// PORT-ONLY: Dextra (DESIGN.md rev 5, 10 step 1, 12.6; unit AS-19; ED-NEW-dextra-as19-8, -10). No Swift twin.
// The one thing a recording may not run without: something on the owner's screen that says it is running. The controller starts a recording
// and then asks the indicator to show it; if the indicator cannot (no window can be made), the recording is ended and deleted and nothing is
// kept, so a recording can never go on unseen. The product's indicator is the recording strip (TeachStripHost).
namespace Marrow.App.Assistant;

public interface ITeachIndicator
{
    /// <summary>Shows that a recording runs, with its Pause and Stop. False when it cannot be shown.</summary>
    bool Show(TeachController controller);

    /// <summary>Takes the indication away (the recording ended).</summary>
    void Hide();
}
