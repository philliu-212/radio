param([string]$ProjectRoot, [switch]$SelfContained)

$ErrorActionPreference = 'Stop'
if (!$ProjectRoot) { $ProjectRoot = Split-Path $PSScriptRoot -Parent }
$mode = if ($SelfContained) { 'Full' } else { 'Lite' }
$name = 'Radio-Portable-' + $mode + '-win-x64-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
$output = Join-Path $ProjectRoot ('dist/' + $name)
if (Test-Path $output) { throw 'Output already exists. No files have been replaced.' }
$project = Join-Path $ProjectRoot 'src/Radio.Portable/Radio.Portable.csproj'
$arguments = @('publish', $project, '-c', 'Release', '-r', 'win-x64', '--self-contained',
    $SelfContained.IsPresent.ToString().ToLowerInvariant(), '-o', $output,
    '-p:DebugType=None', '-p:DebugSymbols=false', '-p:PublishSingleFile=false', '--nologo')
if ($SelfContained) {
    $runtime = Get-Content -Raw (Join-Path $ProjectRoot 'scripts/runtime-package.json') | ConvertFrom-Json
    $arguments += '-p:PortableRuntimeVersion=' + $runtime.version
}
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed.' }
$readme = if ($SelfContained) { 'docs/portable-full-readme.txt' } else { 'docs/portable-readme.txt' }
Copy-Item (Join-Path $ProjectRoot $readme) (Join-Path $output 'READ-ME.txt')
Copy-Item (Join-Path $ProjectRoot 'docs/third-party-notices.txt') (Join-Path $output 'THIRD-PARTY-NOTICES.txt')
if ($SelfContained) {
    $assets = Get-Content -Raw (Join-Path $ProjectRoot 'src/Radio.Portable/obj/project.assets.json') | ConvertFrom-Json
    $cache = @($assets.packageFolders.PSObject.Properties.Name)[0]
    foreach ($id in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64')) {
        $packagePath = Join-Path $cache ($id + '/' + $runtime.version)
        $notices = Get-ChildItem -LiteralPath $packagePath -File -ErrorAction Stop | Where-Object { $_.Name -match 'LICENSE|NOTICE' }
        if (!$notices) { throw "No original runtime notices found for $id." }
        $noticeOutput = Join-Path $output ('THIRD-PARTY/' + $id)
        New-Item -ItemType Directory -Path $noticeOutput -Force | Out-Null
        $notices | Copy-Item -Destination $noticeOutput
    }
}
$unexpected = if (!$SelfContained) { Get-ChildItem -LiteralPath $output -File | Where-Object {
    $_.Name -notin @('Radio.exe', 'Radio.dll', 'Radio.deps.json', 'Radio.runtimeconfig.json', 'Radio.Core.dll',
        'Microsoft.Windows.SDK.NET.dll', 'WinRT.Runtime.dll', 'READ-ME.txt', 'THIRD-PARTY-NOTICES.txt')
} }
if ($unexpected) { throw 'Unexpected package files found. Inspect the output before distributing it.' }
if (Test-Path (Join-Path $output 'mpv.exe')) { throw 'Portable packages must not contain mpv.' }
Compress-Archive -LiteralPath $output -DestinationPath ($output + '.zip')
$size = (Get-ChildItem -LiteralPath $output -File -Recurse | Measure-Object Length -Sum).Sum
Write-Output "Portable folder: $output"
Write-Output "ZIP: $output.zip"
Write-Output ('Unpacked files: {0:N2} MiB. Double-click Radio.exe.' -f ($size / 1MB))
if ($SelfContained) { Write-Output "Includes .NET Desktop Runtime $($runtime.version); no separate .NET install required." }
else { Write-Output 'Lite package requires an existing .NET 8 Desktop Runtime.' }
