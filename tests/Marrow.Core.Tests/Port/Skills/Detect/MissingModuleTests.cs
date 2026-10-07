using Marrow.Core.Port.Skills.Detect;

namespace Marrow.Core.Tests.Port.Skills.Detect;

// Unit SK-10: S3, a failed command, missing MODULE (ONDEMAND-PLAN.md 5.3): "Python ModuleNotFoundError: No module named '<m>' (not localised); Node
// MODULE_NOT_FOUND / ERR_MODULE_NOT_FOUND with Cannot find module '<m>' or Cannot find package '<m>'; Playwright 'Executable doesn't exist at' naming its
// browsers path." Lookup: "for a module, the smallest set that provides it". The command need not name the module (a script imports it).
[Trait("Spec", "DL-10")]
public sealed class MissingModuleTests
{
    private static string[] Offered(string command, int? exit, string output, bool isError = true) =>
        DetectFixtures.Offered(DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed(command, exit, output, isError)));

    [Theory]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData("docx", "P1")]
    [InlineData("openpyxl", "P1")]
    [InlineData("pptx", "P1")]
    [InlineData("pypdf", "P2")]
    [InlineData("pdfplumber", "P2")]
    [InlineData("reportlab", "P2")]
    [InlineData("pdf2image", "P2")]
    [InlineData("PIL", "P3")]
    [InlineData("pandas", "P3")]
    [InlineData("playwright", "P4")]
    [InlineData("imageio", "P5")]
    public void A_python_module_the_plan_names_resolves_to_its_package_set(string module, string set)
    {
        var output = $"Traceback (most recent call last):\n  File \"script.py\", line 1, in <module>\n    import {module}\nModuleNotFoundError: No module named '{module}'";
        Assert.Equal([set], Offered("python script.py", 1, output, false));
        Assert.Equal([set], Offered("py -3 script.py", null, output));
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void A_dotted_python_module_is_looked_up_by_its_top_package_and_quotes_may_be_either_kind_or_absent()
    {
        Assert.Equal(["P1"], Offered("python s.py", 1, "ModuleNotFoundError: No module named 'docx.shared'", false));
        Assert.Equal(["P1"], Offered("python s.py", 1, "ModuleNotFoundError: No module named \"docx\"", false));
        Assert.Equal(["P1"], Offered("python s.py", 1, "ModuleNotFoundError: No module named docx", false));
        Assert.Equal(["T1p"], Offered("python -m pip list", 1, "C:\\Python314\\python.exe: No module named pip", false));
        Assert.Empty(Offered("python s.py", 1, "ModuleNotFoundError: No module named 'definitely_not_catalogued'", false));
        Assert.Empty(Offered("python s.py", 1, "ModuleNotFoundError: No module named ''", false));
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void A_python_module_error_on_a_command_that_succeeded_is_no_signal()
    {
        Assert.Empty(Offered("python s.py", 0, "ModuleNotFoundError: No module named 'docx'", false));
        Assert.Empty(Offered("python s.py", null, "ModuleNotFoundError: No module named 'docx'", false));
        Assert.Equal(["P1"], Offered("python s.py", null, "ModuleNotFoundError: No module named 'docx'", true));
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void Node_needs_its_error_code_and_the_message_and_names_a_module_or_a_package()
    {
        const string cjs = "node:internal/modules/cjs/loader:1228\n  throw err;\n  ^\n\nError: Cannot find module 'docx'\nRequire stack:\n- C:\\work\\make.js {\n  code: 'MODULE_NOT_FOUND',\n  requireStack: [ 'C:\\\\work\\\\make.js' ]\n}";
        const string esm = "node:internal/modules/esm/resolve:873\nError [ERR_MODULE_NOT_FOUND]: Cannot find package 'pptxgenjs' imported from C:\\work\\make.mjs";
        Assert.Equal(["P6"], Offered("node make.js", 1, cjs, false));
        Assert.Equal(["P6"], Offered("node make.mjs", 1, esm, false));
        Assert.Empty(Offered("node make.js", 1, "Error: Cannot find module 'docx'", false));
        Assert.Empty(Offered("node make.js", 1, "code: 'MODULE_NOT_FOUND'", false));
        Assert.Empty(Offered("node make.js", 0, cjs, false));
    }

    [Theory]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData("sharp", "P6")]
    [InlineData("react-icons/fa", "P6")]
    [InlineData("react-icons", "P6")]
    [InlineData("react-dom/server", "P6")]
    [InlineData("pptxgenjs", "P6")]
    public void A_node_specifier_is_reduced_to_its_package_name(string specifier, string set) =>
        Assert.Equal([set], Offered("node x.js", 1, $"Error: Cannot find module '{specifier}'\ncode: 'MODULE_NOT_FOUND'", false));

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void A_relative_absolute_or_built_in_node_specifier_is_not_a_catalogue_name_and_python_modules_are_not_node_modules()
    {
        foreach (var specifier in new[] { "./docx", "../docx", "C:\\work\\docx", "/work/docx", "\\\\server\\docx", "node:docx", "@scope/unknown-pkg" })
            Assert.Empty(Offered("node x.js", 1, $"Error: Cannot find module '{specifier}'\ncode: 'MODULE_NOT_FOUND'", false));
        Assert.Empty(Offered("node x.js", 1, "Error: Cannot find module 'openpyxl'\ncode: 'MODULE_NOT_FOUND'", false));
        Assert.Equal(["P6"], Offered("node x.js", 1, "Error: Cannot find module 'docx'\ncode: 'MODULE_NOT_FOUND'", false));
        Assert.Empty(Offered("python x.py", 1, "ModuleNotFoundError: No module named 'pptxgenjs'", false));
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void Playwright_s_missing_browser_message_offers_Chromium()
    {
        const string message = "playwright._impl._errors.Error: BrowserType.launch: Executable doesn't exist at C:\\Users\\Someone\\AppData\\Local\\ms-playwright\\chromium-1243\\chrome-win\\chrome.exe\n"
            + "╔═════════════════════════════════════════════════════════════╗\n║ Looks like Playwright was just installed or updated.        ║";
        var detection = DetectFixtures.Run(DetectFixtures.Ctx(), DetectFixtures.Failed("python test.py", 1, message, false));
        Assert.Equal(["T10"], DetectFixtures.Offered(detection));
        Assert.Equal("Executable doesn't exist at", detection.Findings.Single().Token);
        Assert.Equal(["T10"], Offered("node test.js", null, message));
        Assert.Empty(Offered("python test.py", 0, message, false));
        Assert.Empty(Offered("python test.py", 1, "Executable does not exist", false));
    }

    [Fact]
    [Trait("Feature", "SK-10")]
    [Trait("Rule", "R-TEST-4")]
    public void One_failure_that_shows_a_missing_module_and_a_missing_browser_gives_both_items()
    {
        var output = "ModuleNotFoundError: No module named 'pdfplumber'\nExecutable doesn't exist at C:\\x\\chrome.exe";
        Assert.Equal(["P2", "T10"], Offered("python s.py", 1, output, false));
    }
}
