using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// W22 Native (DESIGN 13.4, run alone by the lead at the native pass, never in the ordinary filters): the guard on a page written in Hindi and Urdu,
// read by the real engine. Edge's own headless mode (no window) loads a local page of Hindi, Urdu and mixed-direction controls and runs the engine's
// real facts script (WebAgentEngine.FactsJs) on each one in the real DOM, so what the page's own text, aria-label, title, button value and
// form address really give the guard is checked as Chromium produces it (percent-encoded form addresses, zero-width characters kept in the text).
// The verdicts then come from the ordinary WebAgentPolicy. The ordinary tests (WebAgentLocalisedTests, WebAgentLocalisedPagesTests) script the
// facts; this is the one place the facts come from a real page. Not run by the worker that wrote it (no browser is started in the ordinary pass).
[Trait("Decision", "ED-NEW-web-w22-1")]
[Trait("Feature", "W-22")]
public sealed class WebAgentLocalisedNativeTests
{
    // id, what the guard must say: "ask:<category>", "deny:<refusal>" or "none"
    private static readonly (string Id, string Expect)[] Cases =
    [
        ("hi-send", "ask:Send"),
        ("ur-delete", "ask:Delete"),
        ("aria-only", "ask:Send"),
        ("title-only", "ask:SignOut"),
        ("submit-value", "deny:payment"),
        ("form-address", "deny:payment"),
        ("zwj-inside", "ask:Send"),
        ("zwsp-inside", "ask:Send"),
        ("rtl-mixed", "ask:Delete"),
        ("plain-hi", "none"),
        ("plain-ur", "none"),
        ("hi-account", "deny:account"),
        ("ur-captcha", "deny:captcha"),
    ];

    private const string Html = """
        <!doctype html>
        <html lang="hi"><head><meta charset="utf-8"><title>सेवा</title></head>
        <body>
        <button id="hi-send">भेजें</button>
        <button id="ur-delete" dir="rtl" lang="ur">حذف کریں</button>
        <button id="aria-only" aria-label="संदेश भेजें"><svg width="8" height="8"></svg></button>
        <a id="title-only" role="button" href="#" title="لاگ آؤٹ"><svg width="8" height="8"></svg></a>
        <form action="/seva" method="post"><input id="submit-value" type="submit" value="भुगतान करें"></form>
        <form action="/भुगतान/पुष्टि" method="post"><button id="form-address">जारी रखें</button></form>
        <button id="zwj-inside">भे&#8205;जें</button>
        <button id="zwsp-inside">S&#8203;end</button>
        <button id="rtl-mixed" dir="rtl">حذف کریں (Delete)</button>
        <button id="plain-hi">खोजें</button>
        <button id="plain-ur" dir="rtl" lang="ur">تلاش کریں</button>
        <button id="hi-account">खाता बनाएं</button>
        <div id="ur-captcha" class="widget" role="button" aria-label="میں روبوٹ نہیں ہوں" tabindex="0">&#9744;</div>
        </body></html>
        """;

    private static string Edge()
    {
        foreach (var path in new[]
        {
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
        })
        {
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Microsoft Edge is not installed where the runtime installs it.");
    }

    private static string FactsScript()
    {
        var field = typeof(WebAgentEngine).GetField("FactsJs", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("WebAgentEngine.FactsJs was not found.");
        return (string)field.GetValue(null)!;
    }

    private static string? Text(JsonElement facts, string name) =>
        facts.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Flag(JsonElement facts, string name) =>
        facts.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True;

    private static WebElementFacts FactsOf(JsonElement v) => new()
    {
        Role = Text(v, "role") ?? "",
        Name = Text(v, "name") ?? "",
        Tag = Text(v, "tag") ?? "",
        Type = Text(v, "type"),
        AriaLabel = Text(v, "ariaLabel"),
        Id = Text(v, "id"),
        NameAttribute = Text(v, "nameAttr"),
        Label = Text(v, "label"),
        Title = Text(v, "title"),
        Autocomplete = Text(v, "autocomplete"),
        InForm = Flag(v, "inForm"),
        Submits = Flag(v, "submits"),
        FormAction = Text(v, "formAction"),
        FormMethod = Text(v, "formMethod"),
        Href = Text(v, "href"),
        FrameUrl = Text(v, "frameUrl"),
        ClassName = Text(v, "className"),
    };

    [Fact]
    public async Task The_real_pages_own_words_reach_the_guard_and_it_answers_as_the_localised_lists_say()
    {
        var work = Path.Combine(Path.GetTempPath(), "nth-w22-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var ids = JsonSerializer.Serialize(Cases.Select(c => c.Id));
            var harness = "<script>(function () { const facts = (" + FactsScript() + "); const out = {};"
                + " for (const id of " + ids + ") { const el = document.getElementById(id);"
                + " out[id] = el ? facts.call(el) : null; }"
                + " const pre = document.createElement('pre'); pre.id = 'nth-out'; pre.textContent = JSON.stringify(out);"
                + " document.body.appendChild(pre); })();</script>\n</body>";
            var file = Path.Combine(work, "page.html");
            await File.WriteAllTextAsync(file, Html.Replace("</body>", harness, StringComparison.Ordinal), new UTF8Encoding(false));

            var start = new ProcessStartInfo(Edge())
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            foreach (var argument in new[] { "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--user-data-dir=" + Path.Combine(work, "profile"), "--virtual-time-budget=20000", "--dump-dom", new Uri(file).AbsoluteUri })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Edge did not start.");
            var output = await process.StandardOutput.ReadToEndAsync();
            Assert.True(process.WaitForExit(60_000), "Edge did not finish in a minute");

            var match = Regex.Match(output, "<pre id=\"nth-out\">(.*?)</pre>", RegexOptions.Singleline);
            Assert.True(match.Success, "the facts script wrote nothing; the page said: " + output[..Math.Min(output.Length, 600)]);
            using var document = JsonDocument.Parse(WebUtility.HtmlDecode(match.Groups[1].Value));
            var failures = new List<string>();
            foreach (var (id, expect) in Cases)
            {
                var element = document.RootElement.GetProperty(id);
                if (element.ValueKind != JsonValueKind.Object)
                {
                    failures.Add(id + ": the facts script gave nothing");
                    continue;
                }
                var verdict = WebAgentPolicy.CheckPress(FactsOf(element), null);
                var got = verdict.Kind switch
                {
                    WebGuardKind.Confirm => "ask:" + verdict.Category,
                    WebGuardKind.Deny when verdict.Reason == WebAgentPolicy.PaymentRefusal => "deny:payment",
                    WebGuardKind.Deny when verdict.Reason == WebAgentPolicy.AccountRefusal => "deny:account",
                    WebGuardKind.Deny when verdict.Reason == WebAgentPolicy.CaptchaRefusal => "deny:captcha",
                    WebGuardKind.Deny => "deny:other",
                    _ => "none",
                };
                if (got != expect) failures.Add($"{id}: wanted {expect}, got {got} (name \"{Text(element, "name")}\", aria \"{Text(element, "ariaLabel")}\", action \"{Text(element, "formAction")}\")");
            }
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (IOException)
            {
                // Edge may hold its profile folder for a moment.
            }
        }
    }
}
