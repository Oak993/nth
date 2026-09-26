// PORT-ONLY: the UserNotifications values DroppyCode/App/AppDelegate.swift (B3 52bc792) reads and returns in its
// notification-centre delegate method userNotificationCenter(_:willPresent:) (:63-75), which have no Windows type.
// No Windows toast pipeline exists in this tree yet (Port/UpdateNotificationCenter.cs, AppModel.Notify); the unit that
// builds it hands each toast's payload to AppDelegate before showing it (PR-LAUNCH-063 windows_path).
namespace Marrow.App.Port;

/// <summary>
/// <c>UNNotification</c>, as far as the delegate reads it: <c>notification.request.content.userInfo</c>
/// (AppDelegate.swift:67), flattened to the one dictionary the source reads. The payloads the app posts are
/// <c>["threadID": uuidString]</c> (AppModel.swift:1265) and <c>["update": version]</c> (UpdateChecker.swift:252).
/// </summary>
public sealed class UNNotification(IReadOnlyDictionary<string, object> userInfo)
{
    /// <summary><c>request.content.userInfo</c>, keyed by the payload's string keys.</summary>
    public IReadOnlyDictionary<string, object> UserInfo { get; } = userInfo;
}

/// <summary><c>UNNotificationPresentationOptions</c>: the two options AppDelegate.swift returns (:74
/// <c>[.banner, .sound]</c>), and none (:72 <c>[]</c>). The raw values are the port's own.</summary>
[Flags]
public enum UNNotificationPresentationOptions
{
    /// <summary><c>[]</c>: no banner, no sound.</summary>
    None = 0,

    /// <summary><c>.banner</c>.</summary>
    Banner = 1,

    /// <summary><c>.sound</c>.</summary>
    Sound = 2,
}
