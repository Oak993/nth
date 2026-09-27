using System.Reflection;
using Marrow.App.Views.Chat;

namespace Marrow.App.Tests.Fixtures;

// Private ChatView helpers are exercised before its dependency-blocked body can be mounted.
public static class ChatViewReviewFixtures
{
    public static T Invoke<T>(string member, params object?[] arguments)
    {
        var method = typeof(ChatView).GetMethod(member, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (T)method.Invoke(null, arguments)!;
    }

    public static void Invoke(string member, params object?[] arguments)
    {
        var method = typeof(ChatView).GetMethod(member, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(null, arguments);
    }
}
