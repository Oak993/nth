using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: security review of the web feature, S3 (ED-NEW-web-sec-rev-3). What nth puts first in a page's head (the export's
// Content-Security-Policy meta, the sandbox's import map) goes where the browser's HTML parser opens the real head: right after
// a head start tag that opens it, or else just before the first thing that makes the parser open the head itself. A "<head>"
// inside a comment, a CDATA-looking bogus comment, script or style text, an attribute value or a longer tag name is not the
// head, and casing changes nothing. "[X]" stands for the injection.
[Trait("Decision", "ED-NEW-web-sec-rev-3")]
public sealed class ArtifactHeadInjectionTests
{
    [Theory]
    // A comment that holds "<head>" (the reviewer's export repro), closed or left open at the end of the page.
    [InlineData("<!doctype html><!-- <head> --><html><head><title>t</title></head><body>x</body></html>", "<!doctype html><!-- <head> --><html><head>[X]<title>t</title></head><body>x</body></html>")]
    [InlineData("<!doctype html><!-- <head>", "<!doctype html>[X]<!-- <head>")]
    // Comments closed the ways HTML closes them: "<!-->" is a whole comment, and so is one ended by "--!>".
    [InlineData("<!doctype html><!--><script>steal()</script><!-- --><head><title>t</title>", "<!doctype html><!-->[X]<script>steal()</script><!-- --><head><title>t</title>")]
    [InlineData("<!doctype html><!-- a --!><img src=x><head><title>t</title>", "<!doctype html><!-- a --!>[X]<img src=x><head><title>t</title>")]
    // CDATA is not a section in an HTML document: the parser reads a bogus comment that ends at its first ">".
    [InlineData("<!doctype html><![CDATA[ 1 > 0 <head> ]]><html><head><title>t</title>", "<!doctype html><![CDATA[ 1 >[X] 0 <head> ]]><html><head><title>t</title>")]
    // Script and style text before the head: the parser has already opened the head for them.
    [InlineData("<!doctype html><script>var tag = \"<head>\";</script><head><title>t</title>", "<!doctype html>[X]<script>var tag = \"<head>\";</script><head><title>t</title>")]
    [InlineData("<!doctype html><style>/* <head> */ body { color: red }</style><head><title>t</title>", "<!doctype html>[X]<style>/* <head> */ body { color: red }</style><head><title>t</title>")]
    // Odd casing, and a quoted ">" inside the head tag itself.
    [InlineData("<!DOCTYPE html><HTML LANG=\"en\"><HeAd DATA-X=\">\"><TITLE>t</TITLE>", "<!DOCTYPE html><HTML LANG=\"en\"><HeAd DATA-X=\">\">[X]<TITLE>t</TITLE>")]
    // "<head>" in an attribute value of the html tag, or of an end tag the parser ignores before the head.
    [InlineData("<!doctype html><html data-note=\"<head>\"><head><title>t</title>", "<!doctype html><html data-note=\"<head>\"><head>[X]<title>t</title>")]
    [InlineData("<!doctype html></p data-x='<head>'><head><title>t</title>", "<!doctype html></p data-x='<head>'><head>[X]<title>t</title>")]
    // A longer tag name is another element, which the parser puts after a head it opens itself.
    [InlineData("<!doctype html><head-x></head-x><head><title>t</title>", "<!doctype html>[X]<head-x></head-x><head><title>t</title>")]
    // Text before the head.
    [InlineData("<!doctype html>Hello<head><title>t</title>", "<!doctype html>[X]Hello<head><title>t</title>")]
    // A byte order mark stays first, and so does the doctype after it.
    [InlineData("\uFEFF<!doctype html><p>x</p>", "\uFEFF<!doctype html>[X]<p>x</p>")]
    public void The_injection_is_first_in_the_head_the_parser_really_opens(string page, string expected) =>
        Assert.Equal(expected, ArtifactPages.InjectHead(page, "[X]"));

    [Fact]
    public void An_exported_page_whose_comment_holds_a_head_tag_carries_its_policy_first_in_the_real_head()
    {
        using var rig = new ArtifactRuntimeRig();
        var page = "<!doctype html><!-- <head> --><html><head><title>t</title></head><body><p>x</p><script>fetch('https://nth-sec-rev.invalid/')</script></body></html>";
        var html = Encoding.UTF8.GetString(rig.Runtime.Exporter.Export(rig.Create(ArtifactKind.Html, page), ArtifactExportFormat.Html));

        Assert.StartsWith("<!doctype html><!-- <head> --><html><head><meta http-equiv=\"Content-Security-Policy\" content=\"default-src &#39;none&#39;;", html, StringComparison.Ordinal);
        Assert.Contains("connect-src &#39;none&#39;", html, StringComparison.Ordinal);
    }
}
