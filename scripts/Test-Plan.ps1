# Single source of truth for offline profiles. No live streams or release packaging.
$coreProject = 'tests/Radio.Core.Tests'
$windowsProject = 'tests/Radio.Windows.Tests'
$script:RadioTestGroups = @(
    [pscustomobject]@{ Name = 'core'; Project = $coreProject; Script = $null; Timeout = 60 }
    foreach ($name in @('runner-contract','playback-policy','diagnostics','audio-buffer','allocations',
        'rfi-transport','buffered-audio','hls-recovery','backups','stability-ui',
        'ui-shell','ui-stations','ui-appearance','ui-focus','single-instance','system','updates')) {
        [pscustomobject]@{ Name = $name; Project = $windowsProject; Script = $null; Timeout = 180 }
    }
    foreach ($pair in @(
        @('runner-tools','Test-Runner.ps1'),
        @('runtime-tools','Test-FullRuntime.ps1'),
        @('release-tools','Test-GitHubReleaseHelper.ps1'),
        @('updater-paths','Test-UpdaterLongPaths.ps1')
    )) {
        [pscustomobject]@{ Name = $pair[0]; Project = $null; Script = $pair[1]; Timeout = 120 }
    }
)
$script:RadioTestProfiles = [ordered]@{
    Fast = @('core','runner-contract','playback-policy','diagnostics','audio-buffer','allocations','rfi-transport')
    UI = @('ui-shell','ui-stations','ui-appearance','ui-focus','single-instance')
    Audio = @('playback-policy','audio-buffer','rfi-transport','buffered-audio','hls-recovery','backups','stability-ui','system')
    Updates = @('updates','runtime-tools','release-tools','updater-paths')
    Tooling = @('runner-tools','runtime-tools','release-tools','updater-paths')
    Offline = @($script:RadioTestGroups.Name)
}

function Get-RadioTestPlan([string[]]$Profiles, [string[]]$Groups) {
    $names = if ($Groups.Count) { $Groups } else {
        foreach ($profile in $Profiles) {
            if (!$script:RadioTestProfiles.Contains($profile)) { throw "Unknown profile: $profile" }
            $script:RadioTestProfiles[$profile]
        }
    }
    $seen = @{}
    foreach ($name in $names) {
        $match = @($script:RadioTestGroups | Where-Object { $_.Name -ceq $name })
        if ($match.Count -ne 1) { throw "Unknown or ambiguous group: $name" }
        if (!$seen.ContainsKey($name)) { $seen[$name] = $true; $match[0] }
    }
}
