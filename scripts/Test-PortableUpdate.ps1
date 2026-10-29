param([Parameter(Mandatory=$true)][string]$BaselineZip,
      [Parameter(Mandatory=$true)][string]$CandidateZip,
      [string]$SupersedingZip,
      [string]$ProjectRoot)
$ErrorActionPreference = 'Stop'
if (!$ProjectRoot) { $ProjectRoot = Split-Path $PSScriptRoot -Parent }
$root = Join-Path $ProjectRoot ('artifacts/ota-integration-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$report = [ordered]@{ Passed = $false; Scope = 'Actual WinForms portable over loopback HTTP; no public Release; no radio stream'; TestRoot = $root }
$directory = $null
function Wait-Until([scriptblock]$Predicate, [int]$Seconds, [string]$Message) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do { $value = & $Predicate; if ($value) { return $value }; [Threading.Thread]::Sleep(25) } while ([DateTime]::UtcNow -lt $deadline)
    throw $Message
}
function Read-State {
    $stream = $null; $reader = $null
    try {
        $stream = [IO.FileStream]::new($statePath,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
        $reader = [IO.StreamReader]::new($stream,[Text.Encoding]::UTF8)
        return ($reader.ReadToEnd() | ConvertFrom-Json)
    } catch [IO.IOException] { return $null }
    finally { if ($reader) { $reader.Dispose() } elseif ($stream) { $stream.Dispose() } }
}
try {
    Expand-Archive -LiteralPath (Resolve-Path -LiteralPath $BaselineZip).Path -DestinationPath (Join-Path $root 'extracted')
    $directory = (Get-ChildItem -LiteralPath (Join-Path $root 'extracted') -Directory | Select-Object -First 1).FullName
    $statePath = Join-Path $directory 'data/ota/state.json'
    $old = Read-State
    $settingsPath = Join-Path $directory 'data/radio.settings.json'
    $settings = '{"SchemaVersion":2,"Theme":"light","Volume":37,"MyStationIds":[],"PlayCounts":{"retained":9},"LastStationId":"retained","StationSetupCompleted":true}'
    [IO.File]::WriteAllText($settingsPath, $settings)
    [IO.File]::WriteAllText((Join-Path $directory 'data/personal.txt'), 'keep')
    $launcher = Join-Path $directory 'RadioMini.exe'
    $initialLauncher = Start-Process -FilePath $launcher -ArgumentList '--no-autoplay --no-update-check' -WorkingDirectory $directory -PassThru
    $oldExe = Join-Path $directory ($old.Current.Commit.Substring(0,12) + '/RadioMini.exe')
    $player = Wait-Until {
        Get-Process -Name RadioMini -ErrorAction SilentlyContinue | Where-Object { $_.Path -ieq $oldExe -and $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
    } 40 'Baseline launcher did not start the player.'
    if (!$initialLauncher.WaitForExit(30000)) { throw 'Initial launcher did not finish health confirmation.' }
    $report.Mode = $old.Current.Mode
    $report.BaselineVersion = $old.Current.Version
    $firstReport = Join-Path $root 'stage-first.json'
    & dotnet run --no-build --project (Join-Path $ProjectRoot 'tests/Radio.Windows.Tests') -c Release -- --ota-stage --root $directory --zip (Resolve-Path -LiteralPath $CandidateZip).Path --report $firstReport
    if ($LASTEXITCODE -ne 0) { throw 'Background OTA stage failed.' }
    $report.FirstPreparation = Get-Content -LiteralPath $firstReport -Raw | ConvertFrom-Json
    if ($SupersedingZip) {
        $latestReport = Join-Path $root 'stage-latest.json'
        & dotnet run --no-build --project (Join-Path $ProjectRoot 'tests/Radio.Windows.Tests') -c Release -- --ota-stage --root $directory --zip (Resolve-Path -LiteralPath $SupersedingZip).Path --report $latestReport
        if ($LASTEXITCODE -ne 0) { throw 'Latest-wins preparation failed.' }
        $report.LatestPreparation = Get-Content -LiteralPath $latestReport -Raw | ConvertFrom-Json
    }
    $player.Refresh()
    if ($player.HasExited -or (Read-State).Current.Version -ne $old.Current.Version) { throw 'Preparation interrupted the running player.' }
    $ready = Read-State
    $report.TargetVersion = $ready.Pending.Version
    # Exercise a second root invocation while the first player owns its data.
    $duplicate = Start-Process -FilePath $launcher -ArgumentList '--no-autoplay --no-update-check' -WorkingDirectory $directory -PassThru
    if (!$duplicate.WaitForExit(10000)) { throw 'Duplicate launcher failed to exit.' }
    $same = @(Get-Process -Name RadioMini -ErrorAction SilentlyContinue | Where-Object { $_.Path -ieq $oldExe })
    if ($same.Count -ne 1) { throw 'Multiple active instances own the same portable data.' }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $restartLauncher = Start-Process -FilePath $launcher -ArgumentList '--restart-update --no-autoplay --no-update-check' -WorkingDirectory $directory -PassThru
    if (!$player.CloseMainWindow() -or !$player.WaitForExit(15000)) { throw 'Old player did not close normally.' }
    $targetExe = Join-Path $directory ($ready.Pending.Commit.Substring(0,12) + '/RadioMini.exe')
    $next = Wait-Until {
        Get-Process -Name RadioMini -ErrorAction SilentlyContinue | Where-Object { $_.Path -ieq $targetExe -and $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
    } 40 'Prepared version failed to open.'
    $report.RestartToWindowMs = $watch.Elapsed.TotalMilliseconds
    Wait-Until { $snapshot = Read-State; $snapshot -and $snapshot.Trial -eq '' } 10 'New version did not acknowledge healthy startup.' | Out-Null
    if (!$restartLauncher.WaitForExit(30000)) { throw 'Restart launcher did not finish confirmation/cleanup.' }
    $new = Read-State
    $actual = [Reflection.AssemblyName]::GetAssemblyName((Join-Path (Split-Path $targetExe -Parent) 'RadioMini.dll')).Version.ToString(3)
    if ($actual -ne $ready.Pending.Version -or $new.Current.Version -ne $actual -or $null -ne $new.Pending) { throw 'Version switch mismatch.' }
    $saved = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    if ($saved.Volume -ne 37 -or $saved.Theme -ne 'light' -or $saved.PlayCounts.retained -ne 9 -or $saved.LastStationId -ne 'retained' -or
        [IO.File]::ReadAllText((Join-Path $directory 'data/personal.txt')) -ne 'keep') { throw 'Portable preferences were not preserved.' }
    if ($new.Current.Mode -eq 'Full') {
        $runtime = $next.Modules | Where-Object ModuleName -ieq 'coreclr.dll' | Select-Object -First 1
        if (!$runtime.FileName.StartsWith((Split-Path $targetExe -Parent), [StringComparison]::OrdinalIgnoreCase)) { throw 'Full does not use its own runtime.' }
        $report.BundledRuntime = $true
    }
    if (!$next.CloseMainWindow() -or !$next.WaitForExit(15000)) { throw 'New player did not exit normally.' }
    $report.DiskBytesAfterSwitch = (Get-ChildItem -LiteralPath $directory -Recurse -File | Measure-Object Length -Sum).Sum
    # Inject an OS-invalid executable into a separate new pending directory and
    # prove the real launcher restores the last working version and user data.
    $bad = $new.Current | ConvertTo-Json -Depth 10 | ConvertFrom-Json
    $bad.Commit = 'eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee'
    $bad.Version = '99.0.0'
    $badDir = Join-Path $directory $bad.Commit.Substring(0,12)
    New-Item -ItemType Directory -Path $badDir | Out-Null
    [IO.File]::WriteAllText((Join-Path $badDir 'package.json'), ($bad | ConvertTo-Json -Depth 10))
    [IO.File]::WriteAllText((Join-Path $badDir 'RadioMini.exe'), 'intentionally invalid PE for rollback test')
    $new.Pending = $bad
    [IO.File]::WriteAllText($statePath, ($new | ConvertTo-Json -Depth 12))
    $rollbackLauncher = Start-Process -FilePath $launcher -ArgumentList '--no-autoplay --no-update-check' -WorkingDirectory $directory -PassThru
    $fallback = Wait-Until {
        Get-Process -Name RadioMini -ErrorAction SilentlyContinue | Where-Object { $_.Path -ieq $targetExe -and $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
    } 40 'Failed startup did not restore the previous working player.'
    Wait-Until { $snapshot = Read-State; $snapshot -and $snapshot.Trial -eq '' } 10 'Rollback was not confirmed.' | Out-Null
    if (!$rollbackLauncher.WaitForExit(30000)) { throw 'Rollback launcher did not finish.' }
    if ((Read-State).Current.Version -ne $actual) { throw 'Rollback target mismatch.' }
    if ((Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json).Volume -ne 37) { throw 'Rollback lost preferences.' }
    $report.FailedStartupRollback = $true
    if (!$fallback.CloseMainWindow() -or !$fallback.WaitForExit(15000)) { throw 'Fallback player did not exit.' }
    $report.Passed = $true
} catch { $report.Error = $_.Exception.Message; throw }
finally {
    if ($directory) {
        Get-Process -Name RadioMini -ErrorAction SilentlyContinue | Where-Object { $_.Path.StartsWith($directory + '\',[StringComparison]::OrdinalIgnoreCase) } | ForEach-Object { Stop-Process -Id $_.Id -Force }
    }
    $path = Join-Path $root 'result.json'
    [IO.File]::WriteAllText($path, ($report | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
    Write-Output "OTA integration report: $path"
}
