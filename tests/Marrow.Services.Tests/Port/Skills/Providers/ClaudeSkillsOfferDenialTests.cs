using System.Reflection;
using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
namespace Marrow.Services.Tests.Port.Skills.Providers;
public sealed class ClaudeSkillsOfferDenialTests
{
    [Fact, Trait("Feature", "SK-12"), Trait("Rule", "R-SKILLS-12")]
    public void S1_decline_reason_reaches_Claudes_control_response_byte_equal()
    {
        var configuration = new SessionConfiguration { Provider = ProviderKind.Claude, WorkingDirectory = SwiftURL.Create("/offline")!, Environment = ValueMap<string, string>.Empty, RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build };
        var session = new ClaudeSession(configuration);
        var process = new RecordingClaudeProcess([], new Dictionary<string, string>());
        typeof(ClaudeSession).GetField("process", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, process);
        session.HandleControlRequest(JSONValue.FromDictionaryLiteral(("request_id", "held"), ("request", JSONValue.FromDictionaryLiteral(("subtype", "can_use_tool"), ("tool_name", "Bash"), ("input", JSONValue.FromDictionaryLiteral(("command", "soffice --headless")))))));
        var resolve = typeof(ClaudeSession).GetMethod("ResolveApproval", [typeof(string), typeof(string), typeof(string)]);
        Assert.NotNull(resolve);
        resolve.Invoke(session, ["held", "deny", "LibreOffice isn't on this PC and the user chose not to download it now."]);
        Assert.Equal("{\"type\":\"control_response\",\"response\":{\"subtype\":\"success\",\"request_id\":\"held\",\"response\":{\"behavior\":\"deny\",\"message\":\"LibreOffice isn't on this PC and the user chose not to download it now.\"}}}\n", Encoding.UTF8.GetString(Assert.Single(process.Sent)));
    }
}
