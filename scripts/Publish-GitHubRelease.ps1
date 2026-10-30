param([Parameter(Mandatory=$true)][string]$Tag, [string]$ProjectRoot)
$ErrorActionPreference = 'Stop'
if (!$ProjectRoot) { $ProjectRoot = Split-Path $PSScriptRoot -Parent }
[xml]$project = Get-Content -Raw (Join-Path $ProjectRoot 'src/Radio.Windows/Radio.Windows.csproj')
$version = [string]$project.Project.PropertyGroup.Version
if ($Tag -ne "v$version") { throw 'Tag does not match the app version.' }
$head = (& git -C $ProjectRoot rev-parse HEAD).Trim()
$tagCommit = (& git -C $ProjectRoot rev-parse "$Tag^{commit}").Trim()
if ($LASTEXITCODE -ne 0 -or $head -ne $tagCommit) { throw 'Tag must already point to this exact commit. This script never creates/moves tags.' }
& gh release view $Tag *> $null
if ($LASTEXITCODE -eq 0) { throw 'Release already exists. Review/remove the obsolete draft explicitly; do not mix protocols/assets.' }
$files = @()
foreach ($mode in @('Lite','Full')) {
    $candidates = @(Get-ChildItem (Join-Path $ProjectRoot 'dist') -File -Filter "Radio-Portable-$Tag-$mode-win-x64-*.zip")
    if ($candidates.Count -ne 1) { throw 'Expected exactly one ZIP per mode. Use a clean CI output directory.' }
    $files += $candidates[0].FullName
}
$files += Join-Path $ProjectRoot "dist/SHA256SUMS-$Tag.txt"
& gh release create $Tag --verify-tag --draft --title "Radio Mini $Tag" --generate-notes @files
if ($LASTEXITCODE -ne 0) { throw 'Draft release creation/upload failed; inspect GitHub before retrying.' }
Write-Output 'Draft created with only two ZIPs and SHA256SUMS. Review and authorize publication separately.'
