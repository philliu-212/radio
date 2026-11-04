$ErrorActionPreference = 'Stop'
$root = Join-Path (Join-Path $env:LOCALAPPDATA 'Temp/opencode') ('radio-runtime-test-' + [Guid]::NewGuid().ToString('N'))
$payload = Join-Path $root 'payload'
$cache = Join-Path $root 'cache'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
function Write-Json($path, $value) { [IO.File]::WriteAllText($path, ($value | ConvertTo-Json -Depth 12), $utf8) }
$core = 'Microsoft.NETCore.App.Runtime.win-x64'
$desktop = 'Microsoft.WindowsDesktop.App.Runtime.win-x64'
foreach ($pair in @(@($core,'coreclr.dll','native'),@($desktop,'System.Windows.Forms.dll','lib/net8.0'))) {
    $directory = Join-Path $cache ($pair[0].ToLowerInvariant() + '/8.0.33/runtimes/win-x64/' + $pair[2])
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $directory $pair[1]), 'correct-runtime-' + $pair[1], $utf8)
    Copy-Item (Join-Path $directory $pair[1]) $payload
    [IO.File]::WriteAllText((Join-Path $directory 'SharedFacade.dll'), $pair[0], $utf8)
    Copy-Item (Join-Path $directory 'SharedFacade.dll') $payload -Force
}
$assetsPath = Join-Path $root 'assets.json'
$assets = @{ packageFolders = @{ $cache = @{} }; project = @{ frameworks = @{ net8 = @{ downloadDependencies = @(
    @{ name = $core; version = '[8.0.33, 8.0.33]' }, @{ name = $desktop; version = '[8.0.33, 8.0.33]' }
) } } } }
Write-Json $assetsPath $assets
Write-Json (Join-Path $payload 'RadioMini.runtimeconfig.json') @{ runtimeOptions = @{ includedFrameworks = @(
    @{ name = 'Microsoft.NETCore.App'; version = '8.0.33' }, @{ name = 'Microsoft.WindowsDesktop.App'; version = '8.0.33' }
) } }
function Assert-Runtime { & (Join-Path $PSScriptRoot 'Assert-FullRuntime.ps1') -PayloadDirectory $payload -AssetsPath $assetsPath -RuntimeVersion '8.0.33' }
function Expect-Failure([string]$name, [string]$message) {
    $failed = $false
    try { Assert-Runtime } catch { if ($_.Exception.Message -notlike "*$message*") { throw }; $failed = $true }
    if (!$failed) { throw "Expected failure: $name" }
    Write-Output "PASS: $name"
}
Assert-Runtime
Write-Output 'PASS: matching runtime packages and Desktop shared facade precedence accepted'
$forms = Join-Path $payload 'System.Windows.Forms.dll'
[IO.File]::WriteAllText($forms, 'old-8.0.6-runtime', $utf8)
Expect-Failure 'mixed runtime rejected despite correct runtimeconfig' 'Runtime file differs'
Remove-Item -LiteralPath $forms
Expect-Failure 'missing WinForms rejected' 'Missing required runtime file'
Copy-Item (Join-Path $cache 'microsoft.windowsdesktop.app.runtime.win-x64/8.0.33/runtimes/win-x64/lib/net8.0/System.Windows.Forms.dll') $forms
$assets.project.frameworks.net8.downloadDependencies[1].version = '[8.0.6, 8.0.6]'
Write-Json $assetsPath $assets
Expect-Failure 'old resolved Desktop package rejected' 'Resolved runtime mismatch'
$assets.project.frameworks.net8.downloadDependencies[1].version = '[8.0.33, 8.0.33]'
Write-Json $assetsPath $assets
[IO.File]::WriteAllText((Join-Path $payload 'coreclr.dll'), 'wrong-core', $utf8)
Expect-Failure 'mixed CoreCLR rejected' 'Runtime file differs'
Write-Output "All runtime validation fixtures passed. Isolated fixtures: $root"
