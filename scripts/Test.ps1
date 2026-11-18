[CmdletBinding()]
param(
    [ValidateSet('Fast','UI','Audio','Updates','Tooling','Offline')][string[]]$Profile = @('Fast'),
    [string[]]$Group,
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [switch]$List,
    [switch]$NoBuild,
    [switch]$FailFast,
    [string]$ReportDirectory
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Test-Plan.ps1')
. (Join-Path $PSScriptRoot 'Test-Process.ps1')
if ($Group.Count -and $PSBoundParameters.ContainsKey('Profile')) { throw 'Choose -Profile or -Group, not both.' }
$plan = @(Get-RadioTestPlan -Profiles $Profile -Groups $Group)
if (!$plan.Count) { throw 'No test groups selected.' }
if ($List) {
    Write-Output 'Available profiles:'
    foreach ($name in $script:RadioTestProfiles.Keys) {
        Write-Output ("{0}: {1}" -f $name, ($script:RadioTestProfiles[$name] -join ', '))
    }
    Write-Output 'Selected plan (deduplicated):'
    $plan | Format-Table Name, Project, Script, Timeout
    return
}
$root = Split-Path $PSScriptRoot -Parent
if (!$ReportDirectory) { $ReportDirectory = Join-Path $root ('artifacts/test-results/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N')) }
$ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
if (Test-Path -LiteralPath $ReportDirectory) { throw 'Report directory already exists; refusing to overwrite results.' }
New-Item -ItemType Directory -Path $ReportDirectory | Out-Null
$results = [Collections.Generic.List[object]]::new()
$failed = $false
$clock = [Diagnostics.Stopwatch]::StartNew()
try {
    if (!$NoBuild) {
        $projects = @($plan | Where-Object Project | Select-Object -ExpandProperty Project -Unique)
        foreach ($project in $projects) {
            $name = 'build-' + (Split-Path $project -Leaf)
            Write-Host "BUILD $project"
            $result = Invoke-RadioTestProcess -FilePath 'dotnet' -Arguments @('build',(Join-Path $root $project),'-c',$Configuration,'--nologo') `
                -WorkingDirectory $root -LogBase (Join-Path $ReportDirectory $name) -TimeoutSeconds 300 -ShowOutput:($VerbosePreference -eq 'Continue')
            $results.Add([pscustomobject]@{ Name = $name; Phase = 'Build'; Status = $result.Status; ExitCode = $result.ExitCode; Seconds = $result.Seconds })
            if ($result.ExitCode -ne 0) { $failed = $true; break }
        }
    }
    if (!$failed) {
        foreach ($item in $plan) {
            Write-Host "TEST $($item.Name) (timeout $($item.Timeout)s)"
            if ($item.Project) {
                $file = 'dotnet'
                $arguments = @('run','--no-build','--no-restore','--project',(Join-Path $root $item.Project),'-c',$Configuration)
                if ($item.Name -ne 'core') { $arguments += @('--','--suite',$item.Name) }
            }
            else {
                $file = 'powershell.exe'
                $arguments = @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',(Join-Path $PSScriptRoot $item.Script))
            }
            $result = Invoke-RadioTestProcess -FilePath $file -Arguments $arguments -WorkingDirectory $root `
                -LogBase (Join-Path $ReportDirectory $item.Name) -TimeoutSeconds $item.Timeout -ShowOutput:($VerbosePreference -eq 'Continue')
            $results.Add([pscustomobject]@{ Name = $item.Name; Phase = 'Test'; Status = $result.Status; ExitCode = $result.ExitCode; Seconds = $result.Seconds })
            if ($result.ExitCode -ne 0) {
                $failed = $true
                if ($FailFast) { break }
            }
        }
    }
}
catch {
    $failed = $true
    $results.Add([pscustomobject]@{ Name = 'runner'; Phase = 'Infrastructure'; Status = 'Failed'; ExitCode = 1; Seconds = 0; Error = $_.Exception.Message })
    Write-Warning $_.Exception.Message
}
finally {
    $notRun = @($plan.Name | Where-Object { $_ -notin @($results | Where-Object Phase -eq 'Test' | Select-Object -ExpandProperty Name) })
    $report = [ordered]@{
        SchemaVersion = 1; Configuration = $Configuration; NoBuild = [bool]$NoBuild
        Seconds = [Math]::Round($clock.Elapsed.TotalSeconds, 3)
        Passed = !$failed; Selected = @($plan.Name); NotRun = $notRun; Results = @($results.ToArray())
    }
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $ReportDirectory 'summary.json') -Encoding UTF8
    $results | Format-Table Name, Phase, Status, Seconds, ExitCode | Out-Host
    if ($notRun.Count) { Write-Host ('NOT RUN: ' + ($notRun -join ', ')) }
    Write-Host "Results: $ReportDirectory"
}
if ($failed) { exit 1 }
