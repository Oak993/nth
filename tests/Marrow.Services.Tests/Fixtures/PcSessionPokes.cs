using System.Reflection;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY: Tactus's provider wiring tests (ED-NEW-pcctl-41). Reaches a provider session's private handlers and fields,
// so a recorded request can be fed to a session that was built but never started (no process, no provider).
public static class PcSessionPokes
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    public static object? Call(object session, string method, params object?[] args) =>
        session.GetType().GetMethod(method, Instance)!.Invoke(session, args);

    public static T Get<T>(object session, string member) =>
        (T)(session.GetType().GetField(member, Instance) is { } field ? field.GetValue(session) : session.GetType().GetProperty(member, Instance)!.GetValue(session))!;

    public static void Set(object session, string field, object? value) => session.GetType().GetField(field, Instance)!.SetValue(session, value);
}
