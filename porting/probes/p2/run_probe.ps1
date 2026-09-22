param([string]$Name, [string]$ProbeArgs, [switch]$Counters)
$ErrorActionPreference = 'Stop'
$p2 = 'E:\Droppy\port\porting\probes\p2'
$exe = "$p2\bin\Release\net10.0\p2.exe"
$out = "$p2\evidence\$Name"
$others = Get-Process | Where-Object { $_.ProcessName -match '^(p2|p3|p4|p7)$' }
if ($others) { "ABORT: other probe running: $($others.ProcessName -join ',')"; exit 9 }
$env:MARROW_PORT_DATA_DIR = "$env:TEMP\marrow-p2-$Name-$(Get-Random)"
New-Item -ItemType Directory $env:MARROW_PORT_DATA_DIR | Out-Null
$stdout = "$env:TEMP\p2-$Name-stdout.txt"
$start = [DateTime]::UtcNow
$argList = @('--out', $out) + ($ProbeArgs -split ' ' | Where-Object { $_ -ne '' })
$proc = Start-Process -FilePath $exe -ArgumentList $argList -NoNewWindow -PassThru -RedirectStandardOutput $stdout
$null = $proc.Handle
if ($Counters) {
    Start-Sleep -Seconds 5
    $path = "\GPU Engine(pid_$($proc.Id)_*engtype_3D)\Utilization Percentage"
    $s = Get-Counter -Counter $path -SampleInterval 1 -MaxSamples 8 -ErrorAction SilentlyContinue
    $lines = @("Get-Counter '$path' -SampleInterval 1 -MaxSamples 8   (pid $($proc.Id), started $($start.ToString('HH:mm:ss'))Z)")
    $agg = @{}
    foreach ($set in $s) { foreach ($x in $set.CounterSamples) {
        $inst = $x.InstanceName
        $luid = ([regex]::Match($inst, 'luid_0x[0-9a-f]+_0x[0-9a-f]+')).Value
        $eng = ([regex]::Match($inst, 'eng_\d+')).Value
        $k = "$luid $eng"
        if (-not $agg.ContainsKey($k)) { $agg[$k] = @() }
        $agg[$k] += [double]$x.CookedValue
    } }
    foreach ($k in ($agg.Keys | Sort-Object)) { $v = $agg[$k]; $lines += ("{0}  samples={1} mean={2:F1}% max={3:F1}%" -f $k, $v.Count, ($v | Measure-Object -Average).Average, ($v | Measure-Object -Maximum).Maximum) }
    New-Item -ItemType Directory -Force $out | Out-Null
    $lines | Set-Content "$out\gpu_engine_counters.txt" -Encoding utf8
    $lines
}
$proc.WaitForExit()
$code = $proc.ExitCode
Remove-Item -Recurse -Force $env:MARROW_PORT_DATA_DIR
"RUN $Name args='$ProbeArgs' start=$($start.ToString('HH:mm:ss'))Z end=$([DateTime]::UtcNow.ToString('HH:mm:ss'))Z exit=$code"
Get-Content $stdout | Select-String -Pattern 'render path|GL_RENDERER|adapter selection|SCREEN|FRAMES|CPU |GPU |UI  |FLUSH|POINTER|CHECKS|CONTROL|RESULT|cursor|CRASH'
Remove-Item $stdout
