using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Packaging;

public sealed class C4U7ConfigurationTests
{
    [Fact]
    [Trait("Decision", "ED-235")]
    public void Debug_is_rejected_with_a_plain_error_in_the_preflight_before_any_path_or_side_effect()
    {
        var path = Path.Combine(PackagingRig.Scripts, "package-windows.ps1").Replace("'", "''", StringComparison.Ordinal);
        var result = PackagingRig.RunJson($$"""
            $errors = $null; $tokens = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile('{{path}}', [ref]$tokens, [ref]$errors)
            if ($errors.Count) { throw 'The packaging script did not parse.' }
            $rootStatement = @($ast.EndBlock.Statements | Where-Object { $_ -is [System.Management.Automation.Language.AssignmentStatementAst] -and $_.Left.Extent.Text -eq '$root' })[0]
            if (-not $rootStatement) { throw 'Could not find the first package path assignment.' }
            $source = [IO.File]::ReadAllText('{{path}}')
            $preflightText = $ast.ParamBlock.Extent.Text + $source.Substring($ast.ParamBlock.Extent.EndOffset, $rootStatement.Extent.StartOffset - $ast.ParamBlock.Extent.EndOffset)
            $preflight = [scriptblock]::Create($preflightText)
            $message = ''
            try { & $preflight -Configuration Debug } catch { $message = $_.Exception.Message }
            & $preflight -Configuration Release
            Write-Output '#JSON'
            [pscustomobject]@{ message = $message } | ConvertTo-Json -Compress
            """);
        Assert.Contains("Only Release builds can be packaged", result.GetProperty("message").GetString(), StringComparison.Ordinal);
    }
}
