using System.Text;
using Marrow.Services.Web;

namespace Marrow.App.Tests.Fixtures;

// Every request is answered locally: these Native fixtures never reach a network or use an account.
public sealed class VisusWebDocument : IWebResourceHandler
{
    public WebResourceResponse Answer(WebResourceRequest request) => new(200, "OK",
        [new("Content-Type", "text/html; charset=utf-8"), new("Cache-Control", "no-store")],
        Encoding.UTF8.GetBytes("<!doctype html><html><body style='background:white;color:black;font:16px sans-serif'><h1>W8c fixture</h1><p>price Price price</p><p>cross <b>node</b> match</p></body></html>"));
}
