$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Test-Plan.ps1')
. (Join-Path $PSScriptRoot 'Test-Process.ps1')
$root = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path $root ('artifacts/runner-tests-' + [guid]::NewGuid().ToString('N') + ' space')
New-Item -ItemType Directory -Path $scratch | Out-Null
function Assert([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw $Message }
    Write-Host "PASS: $Message"
}
$all = @(Get-RadioTestPlan -Profiles @('Offline'))
Assert ($all.Count -eq $script:RadioTestGroups.Count) 'Offline includes every registered group once'
$combined = @(Get-RadioTestPlan -Profiles @('Fast','Audio','Fast'))
Assert ($combined.Count -eq @($combined.Name | Select-Object -Unique).Count) 'Overlapping profiles deduplicate'
$invalid = $false
try { Get-RadioTestPlan -Groups @('typo') | Out-Null } catch { $invalid = $true }
Assert $invalid 'Unknown groups rejected'
$catalog = Get-Content -Raw (Join-Path $root 'tests/Radio.Windows.Tests/TestCatalog.cs')
$catalogNames = @([regex]::Matches($catalog, 'new\("([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
$windowsNames = @($script:RadioTestGroups | Where-Object { $_.Project -eq 'tests/Radio.Windows.Tests' } | Select-Object -ExpandProperty Name)
Assert ((($catalogNames | Sort-Object) -join ',') -ceq (($windowsNames | Sort-Object) -join ',')) 'PowerShell and Windows suite catalogs agree'

# Native fixture also reproduces stderr / nonzero exit behavior on Windows PowerShell 5.
$source = @'
using System;
using System.IO;
using System.Linq;
using System.Threading;
class Fixture {
    static int Main(string[] args) {
        string log = Environment.GetEnvironmentVariable("RADIO_RUNNER_FIXTURE_LOG");
        if (!String.IsNullOrEmpty(log)) File.AppendAllText(log, String.Join("|", args) + "\n");
        if (args.Contains("hang")) { Thread.Sleep(30000); return 0; }
        if (args.Contains("stderr")) Console.Error.WriteLine("expected stderr");
        Console.WriteLine(String.Join("|", args));
        string fail = Environment.GetEnvironmentVariable("RADIO_RUNNER_FIXTURE_FAIL");
        return args.Contains("fail") || (!String.IsNullOrEmpty(fail) && args.Contains(fail)) ? 7 : 0;
    }
}
'@
$sourcePath = Join-Path $scratch 'Fixture.cs'
[IO.File]::WriteAllText($sourcePath, $source)
$exe = Join-Path $scratch 'dotnet.exe'
& "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /target:exe /r:System.Core.dll "/out:$exe" $sourcePath
if ($LASTEXITCODE -ne 0) { throw 'Runner fixture compilation failed.' }
$result = Invoke-RadioTestProcess -FilePath $exe -Arguments @('stderr','a path with spaces','ends\','a"quote') `
    -WorkingDirectory $root -LogBase (Join-Path $scratch 'success') -TimeoutSeconds 10
Assert ($result.ExitCode -eq 0 -and $result.Status -eq 'Passed') 'Stderr alone does not fail a successful process'
$output = Get-Content -Raw (Join-Path $scratch 'success.stdout.log')
Assert ($output.Trim() -ceq 'stderr|a path with spaces|ends\|a"quote') 'Windows argv quoting preserves spaces, trailing slash and quotes'
$result = Invoke-RadioTestProcess -FilePath $exe -Arguments @('fail') -WorkingDirectory $root -LogBase (Join-Path $scratch 'failure') -TimeoutSeconds 10
Assert ($result.ExitCode -eq 7 -and $result.Status -eq 'Failed') 'Native nonzero exit preserved'
$result = Invoke-RadioTestProcess -FilePath $exe -Arguments @('hang') -WorkingDirectory $root -LogBase (Join-Path $scratch 'timeout') -TimeoutSeconds 1
Assert ($result.ExitCode -eq 124 -and $result.Status -eq 'Timeout') 'Hung process has bounded timeout'

$oldPath = $env:PATH
$oldLog = $env:RADIO_RUNNER_FIXTURE_LOG
$oldFail = $env:RADIO_RUNNER_FIXTURE_FAIL
try {
    $env:PATH = "$scratch;$oldPath"
    $env:RADIO_RUNNER_FIXTURE_LOG = Join-Path $scratch 'commands.log'
    $env:RADIO_RUNNER_FIXTURE_FAIL = 'diagnostics'
    $report = Join-Path $scratch 'continue report'
    $result = Invoke-RadioTestProcess -FilePath 'powershell.exe' -Arguments @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',
        (Join-Path $PSScriptRoot 'Test.ps1'),'-ReportDirectory',$report) `
        -WorkingDirectory $root -LogBase (Join-Path $scratch 'continue') -TimeoutSeconds 60
    $summary = Get-Content -Raw (Join-Path $report 'summary.json') | ConvertFrom-Json
    Assert ($result.ExitCode -ne 0 -and !$summary.Passed -and $summary.NotRun.Count -eq 0) 'Failure returns nonzero but remaining groups still run'
    Assert (@($summary.Results | Where-Object Phase -eq 'Build').Count -eq 2) 'Each selected test project built once'
    Assert (@($summary.Results | Where-Object { $_.Name -eq 'diagnostics' -and $_.Status -eq 'Failed' }).Count -eq 1) 'Failure is recorded under the correct group'
    $commands = @(Get-Content -LiteralPath $env:RADIO_RUNNER_FIXTURE_LOG)
    Assert (@($commands | Where-Object { $_ -like 'run|*' -and $_ -notlike '*--no-build|--no-restore*' }).Count -eq 0) 'Every test process reuses the built output'
    $report = Join-Path $scratch 'failfast report'
    $result = Invoke-RadioTestProcess -FilePath 'powershell.exe' -Arguments @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',
        (Join-Path $PSScriptRoot 'Test.ps1'),'-NoBuild','-FailFast','-ReportDirectory',$report) `
        -WorkingDirectory $root -LogBase (Join-Path $scratch 'failfast') -TimeoutSeconds 60
    $summary = Get-Content -Raw (Join-Path $report 'summary.json') | ConvertFrom-Json
    Assert ($result.ExitCode -ne 0 -and $summary.NotRun.Count -gt 0 -and @($summary.Results | Where-Object Phase -eq 'Build').Count -eq 0) 'FailFast records unexecuted groups and NoBuild skips builds'
    $env:RADIO_RUNNER_FIXTURE_FAIL = 'build'
    $report = Join-Path $scratch 'buildfail report'
    $result = Invoke-RadioTestProcess -FilePath 'powershell.exe' -Arguments @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',
        (Join-Path $PSScriptRoot 'Test.ps1'),'-ReportDirectory',$report) `
        -WorkingDirectory $root -LogBase (Join-Path $scratch 'buildfail') -TimeoutSeconds 60
    $summary = Get-Content -Raw (Join-Path $report 'summary.json') | ConvertFrom-Json
    Assert ($result.ExitCode -ne 0 -and $summary.NotRun.Count -eq $summary.Selected.Count) 'Build failure prevents stale test execution'
    $env:RADIO_RUNNER_FIXTURE_FAIL = $null
    $report = Join-Path $scratch 'screenshots report'
    $result = Invoke-RadioTestProcess -FilePath 'powershell.exe' -Arguments @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',
        (Join-Path $PSScriptRoot 'Test.ps1'),'-Group','runner-contract','-NoBuild','-Screenshots','-ReportDirectory',$report) `
        -WorkingDirectory $root -LogBase (Join-Path $scratch 'screenshots') -TimeoutSeconds 60
    $summary = Get-Content -Raw (Join-Path $report 'summary.json') | ConvertFrom-Json
    $lastCommand = Get-Content -LiteralPath $env:RADIO_RUNNER_FIXTURE_LOG | Select-Object -Last 1
    Assert ($result.ExitCode -eq 0 -and $summary.Screenshots -and $lastCommand.EndsWith('|--suite|runner-contract|--screenshots')) 'Screenshots option reaches only the Windows runner and is recorded in the summary'
}
finally {
    $env:PATH = $oldPath
    $env:RADIO_RUNNER_FIXTURE_LOG = $oldLog
    $env:RADIO_RUNNER_FIXTURE_FAIL = $oldFail
}
Write-Host 'All runner regression checks passed.'
