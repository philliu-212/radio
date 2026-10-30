param([Parameter(Mandatory=$true)][string]$OldDirectory,
      [Parameter(Mandatory=$true)][string]$NewDirectory)
$ErrorActionPreference = 'Stop'
$old = (Resolve-Path -LiteralPath $OldDirectory).Path.TrimEnd('\')
$new = (Resolve-Path -LiteralPath $NewDirectory).Path.TrimEnd('\')
if ($old -ieq $new -or $new.StartsWith($old + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Extract the new portable ZIP into a separate folder first.' }
if (!(Test-Path -LiteralPath (Join-Path $new 'data/ota/state.json')) -or !(Test-Path -LiteralPath (Join-Path $new 'RadioMini.exe'))) { throw 'Destination is not a portable package.' }
$state = Get-Content -LiteralPath (Join-Path $new 'data/ota/state.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($state.Protocol -ne 1 -or $state.Current.DataSchema -ne 2) { throw 'Unsupported destination data schema.' }
foreach ($process in Get-Process -Name RadioMini -ErrorAction SilentlyContinue) {
    if ($process.Path.StartsWith($old + '\',[StringComparison]::OrdinalIgnoreCase) -or $process.Path.StartsWith($new + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Close all old/new RadioMini instances before migrating.' }
}
$source = Join-Path $old 'radio.settings.json'
if (Test-Path -LiteralPath (Join-Path $old 'data/radio.settings.json')) { $source = Join-Path $old 'data/radio.settings.json' }
$target = Join-Path $new 'data/radio.settings.json'
if (Test-Path -LiteralPath $target) { throw 'Destination already has settings; nothing overwritten.' }
foreach ($path in @($source,$target)) {
    $cursor = $path
    while ($cursor) {
        if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Migration path contains a link.' }
        $cursor = Split-Path -Parent $cursor
    }
}
if (Test-Path -LiteralPath $source) {
    if ((Get-Item -LiteralPath $source).Length -gt 1048576) { throw 'Settings too large.' }
    $settings = Get-Content -LiteralPath $source -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($null -ne $settings.SchemaVersion -and $settings.SchemaVersion -notin @(1,2)) { throw 'Unsupported settings version; source left intact.' }
    Copy-Item -LiteralPath $source -Destination $target
    if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $target).Hash) { throw 'Settings copy verification failed.' }
}
Write-Output 'Migration complete. Source portable folder and settings were not changed.'
