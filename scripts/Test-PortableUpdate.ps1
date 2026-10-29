param([Parameter(Mandatory=$true)][string]$BaselineZip,
      [Parameter(Mandatory=$true)][string]$ExpectedVersion,
      [string]$ProjectRoot)

# Explicit online end-to-end test. Always extracts into its own new folder;
# never runs or writes settings into a user's existing portable directory.
$ErrorActionPreference = 'Stop'
if (!$ProjectRoot) { $ProjectRoot = Split-Path $PSScriptRoot -Parent }
if ($ExpectedVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid expected version.' }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, WindowsBase
$id = [Guid]::NewGuid().ToString('N')
$root = Join-Path $ProjectRoot ('artifacts/update-e2e-' + $id)
New-Item -ItemType Directory -Path $root | Out-Null
$result = [ordered]@{ Passed = $false; ExpectedVersion = $ExpectedVersion; TestDirectory = $root }
$report = Join-Path $root 'result.json'
$running = $null
$exe = $null
function Wait-Until([scriptblock]$Condition, [int]$Seconds, [string]$Message) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        $value = & $Condition
        if ($value) { return $value }
        [Threading.Thread]::Sleep(200)
    } while ([DateTime]::UtcNow -lt $deadline)
    throw $Message
}
try {
    Expand-Archive -LiteralPath (Resolve-Path -LiteralPath $BaselineZip).Path -DestinationPath (Join-Path $root 'extracted')
    $folders = @(Get-ChildItem -LiteralPath (Join-Path $root 'extracted') -Directory)
    if ($folders.Count -ne 1) { throw 'Baseline ZIP must contain one portable folder.' }
    $directory = $folders[0].FullName
    $exe = Join-Path $directory 'RadioMini.exe'
    $baseline = Get-Content -LiteralPath (Join-Path $directory 'radio.update-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([version]$baseline.version -ge [version]$ExpectedVersion) { throw 'Baseline must be older and contain the new updater.' }
    $result.BaselineVersion = $baseline.version
    $result.Mode = $baseline.mode
    $result.BaselineNote = 'Locally built test baseline containing updater; not the historical official v0.4.0 binary.'
    $settingsPath = Join-Path $directory 'radio.settings.json'
    # No favorites: the actual restarted app must not start a real radio stream.
    $settings = @{ SchemaVersion = 2; Theme = 'light'; Volume = 37; MyStationIds = @(); PlayCounts = @{ 'retained-test-id' = 9 }; LastStationId = 'retained-test-id'; StationSetupCompleted = $true }
    [IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 4))
    [IO.File]::WriteAllText((Join-Path $directory 'personal-sentinel.txt'), 'preserve user file')
    $started = [DateTime]::UtcNow
    $running = Start-Process -FilePath $exe -ArgumentList '--no-autoplay' -WorkingDirectory $directory -PassThru
    $result.OriginalPid = $running.Id
    $condition = [System.Windows.Automation.OrCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'UpdateButton'),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, [regex]::Unescape('\u4e0b\u8f09\u4e26\u66f4\u65b0')))
    $button = Wait-Until {
        $running.Refresh()
        if ($running.HasExited) { throw 'Baseline player exited before update.' }
        if ($running.MainWindowHandle -eq [IntPtr]::Zero) { return $null }
        $window = [System.Windows.Automation.AutomationElement]::FromHandle($running.MainWindowHandle)
        $candidate = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($candidate -and !$candidate.Current.IsOffscreen -and $candidate.Current.IsEnabled) { return $candidate }
    } 120 'No visible update arrow. Check the latest stable Release and manifest assets.'
    $invoke = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $invoke.Invoke()
    Wait-Until { $running.Refresh(); $running.HasExited } 180 'Player did not exit after clicking update.' | Out-Null
    $next = Wait-Until {
        Get-Process -Name RadioMini -ErrorAction SilentlyContinue | Where-Object { $_.Id -ne $running.Id -and $_.Path -ieq $exe } | Select-Object -First 1
    } 120 'Updated player did not restart.'
    $result.RestartedPid = $next.Id
    Wait-Until { $next.Refresh(); $next.MainWindowHandle -ne [IntPtr]::Zero } 30 'Restarted player has no window.' | Out-Null
    $version = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $directory 'RadioMini.dll')).Version.ToString(3)
    if ($version -ne $ExpectedVersion) { throw "Unexpected restarted version: $version" }
    $installed = Get-Content -LiteralPath (Join-Path $directory 'radio.update-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($installed.version -ne $ExpectedVersion -or $installed.mode -ne $baseline.mode) { throw 'Installed inventory version/mode mismatch.' }
    foreach ($file in $installed.files) {
        $path = Join-Path $directory $file.path
        if (!(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -ne $file.size -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $file.sha256) { throw "Installed file mismatch: $($file.path)" }
    }
    $saved = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($saved.Theme -ne 'light' -or $saved.Volume -ne 37 -or $saved.PlayCounts.'retained-test-id' -ne 9 -or $saved.LastStationId -ne 'retained-test-id' -or
        @($saved.MyStationIds).Count -ne 0 -or !$saved.StationSetupCompleted -or
        [IO.File]::ReadAllText((Join-Path $directory 'personal-sentinel.txt')) -ne 'preserve user file') { throw 'Preferences or personal file not preserved.' }
    $work = @(Get-ChildItem -LiteralPath ([IO.Path]::GetTempPath()) -Directory -Filter 'RadioMini-update-*' | Where-Object { $_.CreationTimeUtc -ge $started }) |
        Where-Object {
            $manifest = Join-Path $_.FullName 'manifest.json'
            if (Test-Path -LiteralPath $manifest) {
                $candidate = Get-Content -LiteralPath $manifest -Raw -Encoding UTF8 | ConvertFrom-Json
                $candidate.version -eq $ExpectedVersion -and $candidate.mode -eq $baseline.mode
            }
        } | Sort-Object CreationTimeUtc -Descending | Select-Object -First 1
    if (!$work) { throw 'Cannot locate actual update plan.' }
    Wait-Until { Test-Path -LiteralPath (Join-Path $work.FullName 'result.txt') } 30 'No installer completion log.' | Out-Null
    if ((Get-Content -LiteralPath (Join-Path $work.FullName 'result.txt') -Raw).Trim() -ne 'Update completed.') { throw 'Installer did not report success.' }
    $plan = Get-Content -LiteralPath (Join-Path $work.FullName 'plan.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $changes = @($plan.files | Where-Object { $_.changed -and !$_.delete -and $_.path -ne 'radio.update-manifest.json' })
    $result.ChangedFiles = @($changes.path)
    $result.PlannedObjectBytes = ($installed.files | Where-Object { $_.path -in $changes.path } | Measure-Object downloadSize -Sum).Sum
    $result.ManagedDeletions = @($plan.files | Where-Object { $_.delete } | ForEach-Object { $_.path })
    if ($baseline.mode -eq 'Full') {
        $runtime = $next.Modules | Where-Object { $_.ModuleName -ieq 'coreclr.dll' } | Select-Object -First 1
        if (!$runtime -or !$runtime.FileName.StartsWith($directory + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Full did not use the bundled runtime.' }
        $result.BundledRuntimeLoaded = $true
        $runtimeFiles = @($baseline.files | Where-Object { $_.path -notin @('RadioMini.exe', 'RadioMini.dll', 'RadioMini.deps.json', 'RadioMini.runtimeconfig.json', 'Radio.Core.dll', 'READ-ME.txt', 'THIRD-PARTY-NOTICES.txt') })
        $unchangedRuntime = @($runtimeFiles | Where-Object {
            $old = $_
            @($installed.files | Where-Object { $_.path -eq $old.path -and $_.sha256 -eq $old.sha256 }).Count -gt 0
        })
        if (@($unchangedRuntime | Where-Object { $_.path -in $changes.path }).Count) { throw 'Unchanged runtime was unnecessarily downloaded.' }
        $result.UnchangedRuntimeFilesSkipped = $unchangedRuntime.Count
    }
    if (!$next.CloseMainWindow()) { throw 'Cannot close restarted player normally.' }
    if (!$next.WaitForExit(15000)) { throw 'Restarted player did not exit normally.' }
    $result.Passed = $true
    $result.InstallerLog = Join-Path $work.FullName 'result.txt'
} catch {
    $result.Error = $_.Exception.Message
    throw
} finally {
    if ($exe) {
        Get-Process -Name RadioMini -ErrorAction SilentlyContinue | Where-Object { $_.Path -ieq $exe } | ForEach-Object {
            if (!$_.HasExited) { Stop-Process -Id $_.Id -Force }
        }
    }
    [IO.File]::WriteAllText($report, ($result | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
    Write-Output "E2E report: $report"
}
