using System.Reflection;
using Marrow.Core.Models;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Models;

// Source: DroppyCode/Core/Models/ProviderSettings.swift (B2 1fbf81e), :1-12.
//
// Tests for the four-method ProviderSettings protocol (PR-PROVREG-083).
//
// Where the expectations come from, never from the C# under test:
// - The Swift protocol (:6-12) declares exactly four requirements: isEnabled(_:) -> Bool,
//   binaryPath(for:) -> String, lastModel(for:) -> String?, apiKey(for:) -> String, each taking a single
//   ProviderKind parameter. R-PROTO-1 ports the protocol to an interface with the same four members and no
//   others; R-NAME-2/R-NAME-5 give isEnabled -> IsEnabled, binaryPath(for:) -> BinaryPath, lastModel(for:)
//   -> LastModel, apiKey(for:) -> ApiKey.
// - The record's expected_result ("The registry can call exactly these four methods and nothing else on
//   settings. It never writes settings back.") is proved by reflecting over the interface's declared
//   members: exactly four methods, no properties, no settable members, matching the signatures above.
public sealed class ProviderSettingsTests
{
    [Trait("Req", "PR-PROVREG-083")] // DroppyCode/Core/Models/ProviderSettings.swift:6-12
    [Fact]
    public void Interface_declares_exactly_the_four_protocol_methods_and_nothing_else()
    {
        var type = typeof(ProviderSettings);

        Assert.True(type.IsInterface);

        var members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        var methods = members.Where(m => m.MemberType == MemberTypes.Method).Cast<MethodInfo>().ToArray();

        // No properties or other member kinds: the registry never writes settings back.
        Assert.Equal(members.Length, methods.Length);
        Assert.Equal(4, methods.Length);

        var isEnabled = Assert.Single(methods, m => m.Name == "IsEnabled");
        Assert.Equal(typeof(bool), isEnabled.ReturnType);
        var isEnabledParams = isEnabled.GetParameters();
        Assert.Single(isEnabledParams);
        Assert.Equal(typeof(ProviderKind), isEnabledParams[0].ParameterType);

        var binaryPath = Assert.Single(methods, m => m.Name == "BinaryPath");
        Assert.Equal(typeof(string), binaryPath.ReturnType);
        var binaryPathParams = binaryPath.GetParameters();
        Assert.Single(binaryPathParams);
        Assert.Equal(typeof(ProviderKind), binaryPathParams[0].ParameterType);

        var lastModel = Assert.Single(methods, m => m.Name == "LastModel");
        Assert.Equal(typeof(string), lastModel.ReturnType);
        var nullabilityContext = new NullabilityInfoContext();
        Assert.Equal(NullabilityState.Nullable, nullabilityContext.Create(lastModel.ReturnParameter).ReadState);
        var lastModelParams = lastModel.GetParameters();
        Assert.Single(lastModelParams);
        Assert.Equal(typeof(ProviderKind), lastModelParams[0].ParameterType);

        var apiKey = Assert.Single(methods, m => m.Name == "ApiKey");
        Assert.Equal(typeof(string), apiKey.ReturnType);
        Assert.Equal(NullabilityState.NotNull, nullabilityContext.Create(apiKey.ReturnParameter).ReadState);
        var apiKeyParams = apiKey.GetParameters();
        Assert.Single(apiKeyParams);
        Assert.Equal(typeof(ProviderKind), apiKeyParams[0].ParameterType);
    }

    [Trait("Req", "PR-PROVREG-083")] // DroppyCode/Core/Models/ProviderSettings.swift:6-12
    [Fact]
    public void A_conforming_settings_object_answers_the_four_calls_the_registry_makes()
    {
        ProviderSettings settings = new FakeProviderSettings();

        Assert.True(settings.IsEnabled(ProviderKind.Claude));
        Assert.False(settings.IsEnabled(ProviderKind.Codex));
        Assert.Equal("/usr/local/bin/claude", settings.BinaryPath(ProviderKind.Claude));
        Assert.Equal("claude-sonnet", settings.LastModel(ProviderKind.Claude));
        Assert.Null(settings.LastModel(ProviderKind.Codex));
        Assert.Equal("sk-test", settings.ApiKey(ProviderKind.Claude));
    }
}
