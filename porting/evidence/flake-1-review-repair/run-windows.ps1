# Prepared on Linux; not yet verified on Windows. No retry/skip path.
param(
    [string] $Repository = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path,
    [string] $Results = '',
    [ValidateSet('before', 'after')] [string] $Stage = 'after',
    [ValidateRange(1, 256)] [int] $LoadWorkers = 4
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'This verification needs Windows.' }
$Repository = (Resolve-Path -LiteralPath $Repository).ProviderPath
if (!$Results) { $Results = Join-Path $env:TEMP ('marrow-flake-1-' + $Stage + '-' + [Guid]::NewGuid().ToString('N')) }
$null = New-Item -ItemType Directory -Force -Path $Results
$Results = (Resolve-Path -LiteralPath $Results).ProviderPath
$priorData = $env:MARROW_PORT_DATA_DIR
$env:MARROW_PORT_DATA_DIR = Join-Path $Results 'data'
$null = New-Item -ItemType Directory -Force -Path $env:MARROW_PORT_DATA_DIR
$groups = [ordered]@{
    App = 'FullyQualifiedName~WallpaperCardTests.An_indeterminate|FullyQualifiedName~ComposerWorkingDotsEaseTests|FullyQualifiedName~AppModelTurnFinishedTests|FullyQualifiedName~ThreadRuntimeProviderLoadTests|FullyQualifiedName~FixtureLockReaderTests'
    UI = 'FullyQualifiedName~OrbRendererTests.The_runtimes_frame_allocates_nothing'
    Core = 'FullyQualifiedName~ShellTests.At_the_default_120|FullyQualifiedName~ShellTests.A_child_that_ignores|FullyQualifiedName~ShellTests.Cancelling_after|FullyQualifiedName~ShellTests.Cancelling_a_child_that_ignores|FullyQualifiedName~ShellTests.RunTool_forwards_its_timeout'
    Services = 'FullyQualifiedName~ProviderRegistrySignInTests.a_successful_login|FullyQualifiedName~GitTests.A_gitlab_merge|FullyQualifiedName~GitTests.The_retry_test_reads|FullyQualifiedName~GitTests.A_cancelled_retry_sleep|FullyQualifiedName~PiCLITests.A_probe_that_never|FullyQualifiedName~PiCLITests.The_probe_launches|FullyQualifiedName~REVIEW_FixtureLockTests|FullyQualifiedName~FixtureLockContentionTests'
}
$loadJobs = @()
$records = [System.Collections.Generic.List[object]]::new()
$failedAttempts = 0
Push-Location $Repository
try {
    $head = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve source HEAD.' }
    $dirty = @(& git status --porcelain -- src tests)
    $sdk = (& dotnet --version).Trim()
    $sourceDigests = [ordered]@{}
    $sourcePaths = @(& git ls-files -- src tests) + @(& git ls-files --others --exclude-standard -- src tests)
    foreach ($sourcePath in $sourcePaths) {
        $sourceDigests[$sourcePath] = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $Repository $sourcePath)).Hash
    }
    $sourceDigests | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 (Join-Path $Results 'source-sha256.json')
    @{ stage = $Stage; head = $head; sourceChanges = $dirty; sdk = $sdk; loadWorkers = $LoadWorkers; iterations = 50; filters = $groups } |
        ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 (Join-Path $Results 'environment.json')
    for ($flakeWorker = 0; $flakeWorker -lt $LoadWorkers; $flakeWorker++) {
        $loadJobs += Start-Job -ScriptBlock { while ($true) { for ($busyIndex = 0; $busyIndex -lt 10000; $busyIndex++) { $null = $busyIndex * $busyIndex } } }
    }
    & dotnet build Marrow.sln '-p:EnableWindowsTargeting=true' --nologo -v:q |
        Tee-Object -FilePath (Join-Path $Results 'build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Build failed; no passing run is claimed.' }
    for ($flakeIteration = 1; $flakeIteration -le 50; $flakeIteration++) {
        foreach ($group in $groups.Keys) {
            if (@($loadJobs | Where-Object State -ne 'Running').Count -ne 0) { throw 'An artificial CPU load worker stopped.' }
            $runFolder = Join-Path (Join-Path $Results $group) ('{0:d2}' -f $flakeIteration)
            $null = New-Item -ItemType Directory -Force -Path $runFolder
            $project = 'tests/Marrow.' + $group + '.Tests/Marrow.' + $group + '.Tests.csproj'
            $filter = 'FullyQualifiedName!~Native&(' + $groups[$group] + ')'
            & dotnet test $project --no-build --no-restore '-p:EnableWindowsTargeting=true' --filter $filter --logger 'trx;LogFileName=run.trx' --results-directory $runFolder --nologo -v:q |
                Tee-Object -FilePath (Join-Path $runFolder 'output.log')
            $attemptExit = $LASTEXITCODE
            $tests = @()
            $trx = Join-Path $runFolder 'run.trx'
            if (Test-Path $trx) {
                [xml] $document = Get-Content -Raw $trx
                $tests = @($document.SelectNodes("//*[local-name()='UnitTestResult']") | ForEach-Object {
                    @{ name = $_.GetAttribute('testName'); outcome = $_.GetAttribute('outcome') }
                })
            }
            if ($attemptExit -ne 0 -or $tests.Count -eq 0 -or @($tests | Where-Object { $_.outcome -ne 'Passed' }).Count -ne 0) { $failedAttempts++ }
            $records.Add(@{ iteration = $flakeIteration; project = $group; exit = $attemptExit; tests = $tests })
            $records | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 (Join-Path $Results 'results.json')
        }
    }
    Write-Output ('Evidence: ' + $Results + '; failed attempts: ' + $failedAttempts)
}
finally {
    if ($loadJobs.Count) { $loadJobs | Stop-Job; $loadJobs | Remove-Job }
    $env:MARROW_PORT_DATA_DIR = $priorData
    Pop-Location
}
if ($failedAttempts -ne 0) { exit 1 }
