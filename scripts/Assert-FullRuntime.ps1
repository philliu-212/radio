param([Parameter(Mandatory=$true)][string]$PayloadDirectory,
      [Parameter(Mandatory=$true)][string]$AssetsPath,
      [Parameter(Mandatory=$true)][string]$RuntimeVersion)
$ErrorActionPreference = 'Stop'
if ($RuntimeVersion -notmatch '^8\.0\.\d+$') { throw 'Expected a pinned .NET 8 runtime version.' }
$assets = Get-Content -Raw -LiteralPath $AssetsPath | ConvertFrom-Json
$config = Get-Content -Raw -LiteralPath (Join-Path $PayloadDirectory 'RadioMini.runtimeconfig.json') | ConvertFrom-Json
$frameworks = @($config.runtimeOptions.includedFrameworks)
foreach ($name in @('Microsoft.NETCore.App','Microsoft.WindowsDesktop.App')) {
    $framework = @($frameworks | Where-Object name -eq $name)
    if ($framework.Count -ne 1 -or $framework[0].version -ne $RuntimeVersion) { throw "Runtime config mismatch: $name" }
}
$count = 0
$expected = @{}
foreach ($id in @('Microsoft.NETCore.App.Runtime.win-x64','Microsoft.WindowsDesktop.App.Runtime.win-x64')) {
    $dependencies = @($assets.project.frameworks.PSObject.Properties.Value.downloadDependencies | Where-Object name -eq $id)
    if ($dependencies.Count -ne 1 -or $dependencies[0].version -ne "[$RuntimeVersion, $RuntimeVersion]") {
        throw "Resolved runtime mismatch: $id must be $RuntimeVersion."
    }
    $packages = @(foreach ($cache in $assets.packageFolders.PSObject.Properties.Name) {
        $path = Join-Path $cache ($id.ToLowerInvariant() + '/' + $RuntimeVersion)
        if (Test-Path -LiteralPath $path) { $path }
    })
    if (!$packages.Count) { throw "Missing pinned runtime package: $id" }
    $package = $packages[0]
    $required = if ($id -like 'Microsoft.NETCore.*') { 'coreclr.dll' } else { 'System.Windows.Forms.dll' }
    $checkedRequired = $false
    foreach ($subdir in @('runtimes/win-x64/native','runtimes/win-x64/lib/net8.0')) {
        $sourceRoot = Join-Path $package $subdir
        if (!(Test-Path -LiteralPath $sourceRoot)) { continue }
        foreach ($source in Get-ChildItem -LiteralPath $sourceRoot -Recurse -File) {
            $relative = $source.FullName.Substring($sourceRoot.Length + 1)
            $destination = Join-Path $PayloadDirectory $relative
            # Desktop packs also contain WPF files that a WinForms publish need not include.
            if (!(Test-Path -LiteralPath $destination)) { continue }
            # Desktop's shared facade assemblies supersede the Core pack copies.
            $expected[$relative] = $source.FullName
            if ($relative -eq $required) { $checkedRequired = $true }
        }
    }
    if (!$checkedRequired) { throw "Missing required runtime file: $required" }
}
foreach ($relative in $expected.Keys) {
    if ((Get-FileHash -LiteralPath $expected[$relative] -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath (Join-Path $PayloadDirectory $relative) -Algorithm SHA256).Hash) {
        throw "Runtime file differs from pinned $RuntimeVersion packages: $relative"
    }
    $count++
}
Write-Output "Verified $count runtime files against pinned $RuntimeVersion NuGet packages (including CoreCLR and WinForms)."
