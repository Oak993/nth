using Marrow.Core.Localisation;
using Marrow.Core.Tests.Fixtures;
using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces W22 (DESIGN.md 5.10 "Guarded actions ... localised lists", 13.2 "guarded-action matching (localised lists)", 14 W22;
// ED-NEW-web-w22-1). The web guard run on the localised label files of every language nth keeps a list for: the labels ask or are refused as the
// files say, ordinary labels pass, the word may sit in any name an element has, and a form whose address names the word asks too.
[Trait("Decision", "ED-NEW-web-w22-1")]
public sealed class WebAgentLocalisedTests
{
    private const string Site = "https://shop.example.org";

    private static WebElementFacts Button(string name) => new() { Role = "button", Name = name, Tag = "button" };

    private static WebGuardCategory CategoryNamed(string name) => Enum.Parse<WebGuardCategory>(name);

    private static string RefusalOf(string kind) => kind switch
    {
        "payment" => WebAgentPolicy.PaymentRefusal,
        "account" => WebAgentPolicy.AccountRefusal,
        "captcha" => WebAgentPolicy.CaptchaRefusal,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static bool IsPaymentWord(string word) => word is "buy" or "pay" or "purchase";

    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    [InlineData("ur")]
    public void AGuardedLabelAsksWithItsCardsCategory(string language)
    {
        var problems = new List<string>();
        foreach (var label in GuardedFixtures.Load(language).Guarded)
        {
            var verdict = WebAgentPolicy.CheckPress(Button(label.Name), Site);
            if (verdict.Kind != WebGuardKind.Confirm || verdict.Category != CategoryNamed(label.Category) || verdict.Word != label.Word)
                problems.Add($"{label.Name}: {verdict.Kind} {verdict.Category} {verdict.Word}");
        }
        Assert.Empty(problems);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    [InlineData("ur")]
    public void APaymentAnAccountOrACaptchaIsRefusedForItsReason(string language)
    {
        var problems = new List<string>();
        foreach (var denial in GuardedFixtures.Load(language).Denied)
        {
            var verdict = WebAgentPolicy.CheckPress(Button(denial.Name), Site);
            if (verdict.Kind != WebGuardKind.Deny || verdict.Reason != RefusalOf(denial.Refusal)) problems.Add($"{denial.Name}: {verdict.Kind} {verdict.Reason}");
        }
        Assert.Empty(problems);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    [InlineData("ur")]
    public void AnOrdinaryLabelRuns(string language)
    {
        var problems = GuardedFixtures.Load(language).Allowed
            .Where(name => WebAgentPolicy.CheckPress(Button(name), Site).Kind != WebGuardKind.None).ToList();
        Assert.Empty(problems);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    [InlineData("ur")]
    public void AWordWrittenAnotherWayAsksOrIsRefusedLikeTheWord(string language)
    {
        var problems = new List<string>();
        foreach (var variant in GuardedFixtures.Load(language).Variants.Concat(GuardedFixtures.Load(language).Mixed))
        {
            var verdict = WebAgentPolicy.CheckPress(Button(variant.Name), Site);
            var wanted = IsPaymentWord(variant.Word) ? WebGuardKind.Deny : WebGuardKind.Confirm;
            if (verdict.Kind != wanted) problems.Add($"{variant.What}: {verdict.Kind}");
        }
        Assert.Empty(problems);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    [InlineData("ur")]
    public void TheWordMayBeInTheAriaLabelTheLabelOrTheTitleAsWellAsTheName(string language)
    {
        var label = GuardedFixtures.Load(language).Guarded[0];
        var inName = Button(label.Name);
        Assert.Equal(WebGuardKind.Confirm, WebAgentPolicy.CheckPress(inName, Site).Kind);
        Assert.Equal(WebGuardKind.Confirm, WebAgentPolicy.CheckPress(inName with { Name = "", AriaLabel = label.Name }, Site).Kind);
        Assert.Equal(WebGuardKind.Confirm, WebAgentPolicy.CheckPress(inName with { Name = "", Label = label.Name }, Site).Kind);
        Assert.Equal(WebGuardKind.Confirm, WebAgentPolicy.CheckPress(inName with { Name = "", Title = label.Name }, Site).Kind);
        Assert.Equal(WebGuardKind.None, WebAgentPolicy.CheckPress(inName with { Name = "" }, Site).Kind);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    [InlineData("ur")]
    public void APostFormWhoseAddressNamesTheWordInThatLanguageAsksOrIsRefused(string language)
    {
        var list = GuardedWords.Shared.Lists.Single(l => l.Language == language);
        var delete = list.Words["delete"][0];
        var pay = list.Words["pay"][0];
        // Addresses reach the guard percent-encoded (the page's resolved form action); a Hindi or Urdu path must still be read.
        var encoded = "https://shop.example.org/" + Uri.EscapeDataString(delete) + "/confirm";
        var action = new WebElementFacts { Role = "button", Name = "Continue", Tag = "button", InForm = true, Submits = true, FormMethod = "post", FormAction = encoded };
        var asks = WebAgentPolicy.CheckPress(action, Site);
        Assert.Equal(WebGuardKind.Confirm, asks.Kind);
        Assert.Equal(WebGuardCategory.Delete, asks.Category);

        var payAction = action with { FormAction = "https://shop.example.org/" + Uri.EscapeDataString(pay) + "/confirm" };
        var refused = WebAgentPolicy.CheckPress(payAction, Site);
        Assert.Equal(WebGuardKind.Deny, refused.Kind);
        Assert.Equal(WebAgentPolicy.PaymentRefusal, refused.Reason);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    [InlineData("ur")]
    public void ACaptchaWidgetIsRecognisedByItsClassOrNameInEachLanguage(string language)
    {
        var phrase = GuardedWords.Shared.Lists.Single(l => l.Language == language).Captcha[0];
        var widget = new WebElementFacts { Role = "checkbox", Name = "", Tag = "div", ClassName = "widget " + phrase };
        Assert.True(WebAgentPolicy.IsCaptcha(widget));
        Assert.Equal(WebGuardKind.Deny, WebAgentPolicy.CheckType(widget).Kind);
        Assert.False(WebAgentPolicy.IsCaptcha(widget with { ClassName = "widget" }));
    }

    [Fact]
    public void TheWordsAnEnglishNameHoldsWinOverTheOtherLanguages()
    {
        // English is tried first, as before; a name holding both is judged by the English word (the same card either way here).
        var verdict = WebAgentPolicy.CheckPress(Button("Publish / भेजें"), Site);
        Assert.Equal("publish", verdict.Word);
    }

    [Fact]
    public void EveryGuardedWordHasACategoryTheCardUnderstands()
    {
        foreach (var word in GuardedWords.Canonical)
        {
            var category = WebAgentPolicy.CategoryOf(word);
            if (word is "send") Assert.Equal(WebGuardCategory.Send, category);
            else if (word is "post" or "publish") Assert.Equal(WebGuardCategory.Post, category);
            else if (word is "delete" or "delete account" or "uninstall") Assert.Equal(WebGuardCategory.Delete, category);
            else if (word is "install") Assert.Equal(WebGuardCategory.Install, category);
            else if (word is "sign out") Assert.Equal(WebGuardCategory.SignOut, category);
            else if (word is "submit") Assert.Equal(WebGuardCategory.Submit, category);
            else Assert.Equal(WebGuardCategory.Order, category);
        }
    }
}
