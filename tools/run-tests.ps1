<#
.SYNOPSIS
  Runs Unity tests (EditMode / PlayMode) headless from CLI and prints results to console.

.DESCRIPTION
  Headless runner for automation (Claude / CI). Unlike legacy run-editmode-tests.ps1
  (EditMode-only, results only to file) this one:
    - supports -Platform EditMode|PlayMode|Both
    - parses the result XML and prints PASS/FAIL + failed test list to stdout
      (the calling process does NOT see files opened in the editor - it needs stdout)
    - guard: Unity must not be open on this project (license/project lock)
    - -Filter passed to -testFilter (e.g. "RailwayManager.Tests.EditMode.DeliveryPipelineTests")
    - exit code: 0 = all pass, 1 = failures, 2 = launch error / timeout

  NOTE: ASCII-only on purpose - Windows PowerShell 5.1 misreads UTF-8-no-BOM diacritics
  and desyncs the parser. Keep this file ASCII.

.EXAMPLE
  powershell -File Tools\run-tests.ps1                       # EditMode, all tests
  powershell -File Tools\run-tests.ps1 -Platform PlayMode
  powershell -File Tools\run-tests.ps1 -Platform Both
  powershell -File Tools\run-tests.ps1 -Filter "RailwayManager.Tests.EditMode.DeliveryPipelineTests"
#>
param(
    [ValidateSet("EditMode", "PlayMode", "Both")]
    [string]$Platform = "EditMode",
    [string]$Filter = "",
    [string]$UnityPath = "D:\Unity\6000.4.0f1\Editor\Unity.exe",
    [string]$ProjectPath = (Resolve-Path "$PSScriptRoot\..").Path,
    [int]$TimeoutSec = 1200
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $UnityPath)) {
    Write-Host "[run-tests] ERROR: Unity.exe not found at '$UnityPath'. Pass -UnityPath." -ForegroundColor Red
    exit 2
}

# Guard: Unity open on this project blocks -runTests (Temp/UnityLockfile held).
$lock = Join-Path $ProjectPath "Temp\UnityLockfile"
if (Test-Path $lock) {
    try {
        $fs = [System.IO.File]::Open($lock, 'Open', 'ReadWrite', 'None')
        $fs.Close()
    } catch {
        Write-Host "[run-tests] ERROR: project is open in the Unity editor (UnityLockfile held). Close Unity and retry." -ForegroundColor Red
        exit 2
    }
}

$logsDir = Join-Path $ProjectPath "Logs"
New-Item -ItemType Directory -Force -Path $logsDir | Out-Null

function Invoke-Platform([string]$plat) {
    $results = Join-Path $logsDir "$($plat.ToLower())-results.xml"
    $log     = Join-Path $logsDir "$($plat.ToLower())-run.log"
    if (Test-Path $results) { Remove-Item $results -Force }

    $unityArgs = @(
        "-batchmode",
        "-projectPath", $ProjectPath,
        "-runTests",
        "-testPlatform", $plat,
        "-testResults", $results,
        "-logFile", $log
    )
    if ($Filter) { $unityArgs += @("-testFilter", $Filter) }

    Write-Host "[run-tests] $plat - start (timeout ${TimeoutSec}s)..." -ForegroundColor Cyan
    $proc = Start-Process -FilePath $UnityPath -ArgumentList $unityArgs -PassThru
    if (-not $proc.WaitForExit($TimeoutSec * 1000)) {
        try { $proc.Kill($true) } catch {}
        Write-Host "[run-tests] $plat - TIMEOUT after ${TimeoutSec}s. Log: $log" -ForegroundColor Red
        return 2
    }

    if (-not (Test-Path $results)) {
        Write-Host "[run-tests] $plat - no results file (process exit $($proc.ExitCode)). Last 40 log lines:" -ForegroundColor Red
        if (Test-Path $log) { Get-Content $log -Tail 40 }
        return 2
    }

    # Parse NUnit3 XML (Unity test results)
    [xml]$xml = Get-Content $results
    $run = $xml.'test-run'
    $total = [int]$run.total; $passed = [int]$run.passed
    $failed = [int]$run.failed; $skipped = [int]$run.skipped
    $color = if ($failed -gt 0) { "Red" } else { "Green" }
    Write-Host "[run-tests] $plat - total=$total passed=$passed failed=$failed skipped=$skipped" -ForegroundColor $color

    if ($failed -gt 0) {
        Write-Host "  Failed:" -ForegroundColor Red
        $xml.SelectNodes("//test-case[@result='Failed']") | ForEach-Object {
            Write-Host "   - $($_.fullname)" -ForegroundColor Red
            # NUnit3: <failure><message><![CDATA[...]]></message></failure> — InnerText, nie węzeł.
            $msgNode = $_.SelectSingleNode("failure/message")
            if ($msgNode) {
                $msg = ($msgNode.InnerText -replace '\s+', ' ').Trim()
                if ($msg) { Write-Host "       $msg" -ForegroundColor DarkYellow }
            }
        }
        return 1
    }
    return 0
}

$platforms = if ($Platform -eq "Both") { @("EditMode", "PlayMode") } else { @($Platform) }
$worst = 0
foreach ($p in $platforms) {
    $rc = Invoke-Platform $p
    if ($rc -gt $worst) { $worst = $rc }
}

$endColor = if ($worst -eq 0) { "Green" } else { "Red" }
Write-Host "[run-tests] DONE - exit $worst" -ForegroundColor $endColor
exit $worst
