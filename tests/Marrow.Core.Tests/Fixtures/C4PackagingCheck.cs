using System.Text.Json;

namespace Marrow.Core.Tests.Fixtures;

internal static class C4PackagingCheck
{
    public static JsonElement Invoke(string title, string mocks)
    {
        var path = Path.Combine(PackagingRig.Scripts, "package-windows.ps1").Replace("'", "''", StringComparison.Ordinal);
        return PackagingRig.RunJson($$"""
            $errors = $null; $tokens = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile('{{path}}', [ref]$tokens, [ref]$errors)
            if ($errors.Count) { throw 'The packaging script did not parse.' }
            $calls = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Invoke-NthCheck' }, $true)
            $call = @($calls | Where-Object { $_.CommandElements[1].Extent.Text -like '*{{title}}*' })[0]
            if (-not $call) { throw 'Requested packaging gate not found.' }
            $body = @($call.CommandElements | Where-Object { $_ -is [System.Management.Automation.Language.ScriptBlockExpressionAst] })[0].ScriptBlock.Extent.Text
            $check = [scriptblock]::Create($body.Substring(1, $body.Length - 2))
            $publishDir = 'C:\fixture\publish'; $checkDir = 'C:\fixture\check'; $MainExe = 'nth.exe'
            $stubName = 'lib/app/nth_ExecutionStub.exe'; $badStub = Join-Path $checkDir $stubName
            $setupFree = 'C:\fixture\free.exe'; $setupShared = 'C:\fixture\shared.exe'
            $velopackSetup = [pscustomobject]@{ FullName = 'C:\fixture\velopack.exe' }
            $identity = Get-NthIdentity -Publisher oak993 -Version 1.0.0 -Build 1 -AppCopyright copyright
            {{mocks}}
            $message = ''
            try { & $check } catch { $message = $_.Exception.Message }
            Write-Output '#JSON'
            [pscustomobject]@{ message = $message } | ConvertTo-Json -Compress
            """);
    }
}
