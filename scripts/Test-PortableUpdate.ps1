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
function Request-Switch($Parent) {
    $work = Join-Path $directory ('data/ota/workers/' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work | Out-Null
    $request = Join-Path $work 'request.json'
    [IO.File]::WriteAllText($request, (@{ Root=$directory; ParentId=$Parent.Id; ParentStart=$Parent.StartTime.ToUniversalTime().Ticks; Arguments=@('--no-autoplay','--no-update-check') } | ConvertTo-Json))
    Start-Process -FilePath (Join-Path $directory 'bin/updater.exe') -ArgumentList ('--switch "' + $request + '"') -WorkingDirectory $directory | Out-Null
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
    $mainExe = Join-Path $directory 'RadioMini.exe'
    $initialPlayer = Start-Process -FilePath $mainExe -ArgumentList '--no-autoplay --no-update-check' -WorkingDirectory $directory -PassThru
    $oldExe = $mainExe
    $player = Wait-Until {
        Get-Process -Name RadioMini -ErrorAction SilentlyContinue | Where-Object { $_.Path -ieq $oldExe -and $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
    } 40 'Baseline root apphost did not start the player.'
    if ($player.Id -ne $initialPlayer.Id) { throw 'Root apphost delegated to another player process.' }
    if (Test-Path (Join-Path $directory ($old.Current.Commit.Substring(0,12) + '/RadioMini.exe'))) { throw 'Inner player EXE must not exist.' }
    if (@(Get-Process -Name updater -ErrorAction SilentlyContinue | Where-Object { $_.Path.StartsWith($directory + '\',[StringComparison]::OrdinalIgnoreCase) }).Count) { throw 'Updater must not run during normal startup.' }
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
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo($mainExe).FileVersion -ne ($old.Current.Version + '.0')) { throw 'Background preparation replaced the running root apphost.' }
    $ready = Read-State
    $report.TargetVersion = $ready.Pending.Version
    # Exercise a second root invocation while the first player owns its data.
    $duplicate = Start-Process -FilePath $mainExe -ArgumentList '--no-autoplay --no-update-check' -WorkingDirectory $directory -PassThru
    if (!$duplicate.WaitForExit(10000)) { throw 'Duplicate main process failed to exit.' }
    $same = @(Get-Process -Name RadioMini -ErrorAction SilentlyContinue | Where-Object { $_.Path -ieq $oldExe })
    if ($same.Count -ne 1) { throw 'Multiple active instances own the same portable data.' }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    Request-Switch $player
    if (!$player.CloseMainWindow() -or !$player.WaitForExit(15000)) { throw 'Old player did not close normally.' }
    $player.Dispose(); $initialPlayer.Dispose(); $duplicate.Dispose()
    $targetExe = $mainExe
    $targetResources = Join-Path $directory $ready.Pending.Commit.Substring(0,12)
    $next = Wait-Until {
        Get-Process -Name RadioMini -ErrorAction SilentlyContinue | Where-Object { $_.Path -ieq $targetExe -and $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
    } 40 'Prepared version failed to open.'
    $report.RestartToWindowMs = $watch.Elapsed.TotalMilliseconds
    Wait-Until { $snapshot = Read-State; $snapshot -and $snapshot.Trial -eq '' } 10 'New version did not acknowledge healthy startup.' | Out-Null
    Wait-Until { !(Test-Path (Join-Path $directory $old.Current.Commit.Substring(0,12))) } 20 'Successful update did not remove the old resources.' | Out-Null
    $new = Read-State
    $actual = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $targetResources 'RadioMini.dll')).Version.ToString(3)
    if ($actual -ne $ready.Pending.Version -or $new.Current.Version -ne $actual -or $null -ne $new.Pending) { throw 'Version switch mismatch.' }
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($mainExe)
    if ($version.ProductName -ne 'Radio Mini' -or $version.FileDescription -ne 'Radio Mini' -or $version.FileVersion -ne ($actual + '.0')) { throw 'Root EXE product/description/version mismatch.' }
    if ($null -ne $new.Previous -or (Test-Path (Join-Path $targetResources 'RadioMini.exe'))) { throw 'Successful update retained rollback or an inner player EXE.' }
    $report.RootProduct = $version.ProductName; $report.RootDescription = $version.FileDescription; $report.RootVersion = $version.FileVersion
    $report.OldResourcesDeleted = $true
    $saved = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    if ($saved.Volume -ne 37 -or $saved.Theme -ne 'light' -or $saved.PlayCounts.retained -ne 9 -or $saved.LastStationId -ne 'retained' -or
        [IO.File]::ReadAllText((Join-Path $directory 'data/personal.txt')) -ne 'keep') { throw 'Portable preferences were not preserved.' }
    if ($new.Current.Mode -eq 'Full') {
        $runtime = $next.Modules | Where-Object ModuleName -ieq 'coreclr.dll' | Select-Object -First 1
        if (!$runtime.FileName.StartsWith($targetResources, [StringComparison]::OrdinalIgnoreCase)) { throw 'Full does not use its own runtime.' }
        $report.BundledRuntime = $true
    }
    if (!$next.CloseMainWindow() -or !$next.WaitForExit(15000)) { throw 'New player did not exit normally.' }
    $next.Dispose()
    $report.DiskBytesAfterSwitch = (Get-ChildItem -LiteralPath $directory -Recurse -File | Measure-Object Length -Sum).Sum
    # Inject a verified-but-invalid entry executable to exercise the actual
    # root EXE transaction and rollback, not an inner player launch.
    $report.RollbackCases = @()
    foreach ($failure in @('invalid-pe','wrong-package','missing-managed-dll','locked-updater')) {
    $new = Read-State
    $bad = $new.Current | ConvertTo-Json -Depth 10 | ConvertFrom-Json
    $bad.Commit = 'eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee'
    $bad.Version = '99.0.0'
    $badDir = Join-Path $directory $bad.Commit.Substring(0,12)
    New-Item -ItemType Directory -Path $badDir | Out-Null
    $entries = Join-Path $directory ('data/ota/entries/' + $bad.Commit.Substring(0,12))
    New-Item -ItemType Directory -Path $entries -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $entries 'app.bin'), 'intentionally invalid PE for rollback test')
    if ($failure -in @('wrong-package','locked-updater')) { Copy-Item -LiteralPath $mainExe -Destination (Join-Path $entries 'app.bin') -Force }
    if ($failure -eq 'missing-managed-dll') {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $BaselineZip).Path)
        try {
            $entry = $archive.Entries | Where-Object { $_.Name -eq 'RadioMini.exe' } | Select-Object -First 1
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $entries 'app.bin'), $true)
        } finally { $archive.Dispose() }
    }
    Copy-Item -LiteralPath (Join-Path $directory 'bin/updater.exe') -Destination (Join-Path $entries 'updater.bin')
    $bad.EntryFiles[0].Size = (Get-Item (Join-Path $entries 'app.bin')).Length
    $bad.EntryFiles[0].Sha256 = (Get-FileHash (Join-Path $entries 'app.bin') -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $badDir 'package.json'), ($bad | ConvertTo-Json -Depth 10))
    $new.Pending = $bad
    [IO.File]::WriteAllText($statePath, ($new | ConvertTo-Json -Depth 12))
    $lockedEntry = $null
    if ($failure -eq 'locked-updater') { $lockedEntry = [IO.FileStream]::new((Join-Path $directory 'bin/updater.exe'),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read) }
    $switchingPlayer = Start-Process -FilePath $mainExe -ArgumentList '--no-autoplay --no-update-check' -WorkingDirectory $directory -PassThru
    $fallback = Wait-Until {
        Get-Process -Name RadioMini -ErrorAction SilentlyContinue | Where-Object { $_.Path -ieq $targetExe -and $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
    } 40 'Failed startup did not restore the previous working player.'
    if ($lockedEntry) { $lockedEntry.Dispose(); $lockedEntry = $null }
    Wait-Until { $snapshot = Read-State; $snapshot -and $snapshot.Trial -eq '' } 10 'Rollback was not confirmed.' | Out-Null
    if (!$switchingPlayer.WaitForExit(30000)) { throw 'Switch-requesting apphost did not exit.' }
    if ((Read-State).Current.Version -ne $actual) { throw 'Rollback target mismatch.' }
    if ($failure -eq 'locked-updater' -and (Get-Content -Raw (Join-Path $directory 'data/ota/last-error.txt')) -notlike 'Could not promote*') { throw 'Locked updater did not exercise entry replacement failure.' }
    if ((Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json).Volume -ne 37) { throw 'Rollback lost preferences.' }
    $report.FailedStartupRollback = $true
    Wait-Until { !(Test-Path $badDir) -and !(Test-Path (Join-Path $directory 'data/ota/recovery')) } 20 'Rollback cleanup did not complete.' | Out-Null
    if (!$fallback.CloseMainWindow() -or !$fallback.WaitForExit(15000)) { throw 'Fallback player did not exit.' }
    $fallback.Dispose(); $switchingPlayer.Dispose()
    $report.RollbackCases += $failure
    }
    $report.Passed = $true
} catch { $report.Error = $_.Exception.Message; throw }
finally {
    if ($lockedEntry) { $lockedEntry.Dispose() }
    if ($directory) {
        Get-Process -Name RadioMini -ErrorAction SilentlyContinue | Where-Object { $_.Path.StartsWith($directory + '\',[StringComparison]::OrdinalIgnoreCase) } | ForEach-Object { Stop-Process -Id $_.Id -Force }
    }
    $path = Join-Path $root 'result.json'
    [IO.File]::WriteAllText($path, ($report | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
    Write-Output "OTA integration report: $path"
}
