using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces security review 2026-10-13, finding L5 (ED-NEW-web-sec-artifacts-6; DESIGN.md 5.4 "Risky types ... add
// 'This file can run code on your PC.' and Save needs a second click within 3 s"). Windows types that install an app, run a
// troubleshooter, open a search or library that reaches a remote share, run a script component or scriptlet, start a Java Web
// Start app, connect Remote Desktop to the sender's machine, open a Windows Sandbox with the sender's settings, install an update
// package, or run a legacy PowerShell, browser-app or Python file now count as risky, alone and inside an archive's name. The list
// stays a closed set: ordinary documents are untouched.
[Trait("Decision", "ED-NEW-web-sec-artifacts-6")]
public sealed class DownloadGateRiskyTypesTests
{
    [Theory]
    [InlineData("update.appinstaller")]
    [InlineData("fix-audio.diagcab")]
    [InlineData("fix-audio.diagcfg")]
    [InlineData("fix-audio.diagpkg")]
    [InlineData("Documents.library-ms")]
    [InlineData("invoices.searchConnector-ms")]
    [InlineData("INVOICES.SEARCHCONNECTOR-MS")]
    [InlineData("component.wsc")]
    [InlineData("scriptlet.sct")]
    [InlineData("viewer.jnlp")]
    [InlineData("support.rdp")]
    [InlineData("lab.wsb")]
    [InlineData("windows-kb.msu")]
    [InlineData("app.xbap")]
    [InlineData("pinned.website")]
    [InlineData("old.msh")]
    [InlineData("old.msh1")]
    [InlineData("old.msh2")]
    [InlineData("old.mshxml")]
    [InlineData("old.msh1xml")]
    [InlineData("old.msh2xml")]
    [InlineData("old.ps2")]
    [InlineData("old.ps2xml")]
    [InlineData("old.psc1")]
    [InlineData("old.psc2")]
    [InlineData("tool.py")]
    [InlineData("tool.pyw")]
    [InlineData("tool.pyz")]
    [InlineData("tool.pyzw")]
    [InlineData("tool.pyc")]
    [InlineData("tool.pyo")]
    public void A_windows_type_that_runs_code_or_reaches_out_is_risky_alone_and_inside_an_archives_name(string name)
    {
        Assert.True(DownloadGate.IsRisky(name, null), name);
        Assert.True(DownloadGate.IsRisky(name + ".zip", "application/zip"), name + ".zip");
        Assert.True(DownloadGate.RiskyExtensions.Contains(name[(name.LastIndexOf('.') + 1)..]), name);
    }

    [Theory]
    [InlineData("report.pdf")]
    [InlineData("notes.txt")]
    [InlineData("data.csv")]
    [InlineData("photo.png")]
    [InlineData("library.md")]
    [InlineData("sct-notes.docx")]
    public void An_ordinary_document_stays_one_click(string name) => Assert.False(DownloadGate.IsRisky(name, "application/octet-stream"), name);
}
