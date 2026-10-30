param([Parameter(Mandatory=$true)][string]$InputDirectory,
      [Parameter(Mandatory=$true)][string]$OutputDirectory,
      [Parameter(Mandatory=$true)][string]$Version,
      [Parameter(Mandatory=$true)][string]$Commit,
      [Parameter(Mandatory=$true)][ValidateSet('Lite','Full')][string]$Mode)
$ErrorActionPreference = 'Stop'
if ($Commit -notmatch '^[a-f0-9]{40}$' -or $Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid build identity.' }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Output already exists; refusing to overwrite a portable folder.' }
$hash = $Commit.Substring(0,12)
$target = Join-Path $OutputDirectory $hash
New-Item -ItemType Directory -Path $target -Force | Out-Null
# The source is a fresh dotnet publish folder, never an installed user's app.
$queue = [Collections.Generic.Queue[string]]::new()
$queue.Enqueue([IO.Path]::GetFullPath($InputDirectory).TrimEnd('\'))
$source = $queue.Peek()
$entries = @()
while ($queue.Count) {
    $folder = $queue.Dequeue()
    if ((Get-Item -LiteralPath $folder -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Source contains a link.' }
    foreach ($file in Get-ChildItem -LiteralPath $folder -Force) {
        if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Source contains a link.' }
        if ($file.PSIsContainer) { $queue.Enqueue($file.FullName); continue }
        $relative = $file.FullName.Substring($source.Length + 1).Replace('\','/')
        if ($relative -eq 'RadioMini.exe') { continue }
        if ($relative.Length -gt 240 -or @($relative.Split('/') | Where-Object {
            $_ -notmatch '^[A-Za-z0-9_.-]+$' -or $_ -in @('.','..','radio.settings.json','package.json') -or $_.EndsWith('.') -or
            $_ -match '^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)'
        }).Count) { throw "Unsafe payload path: $relative" }
        $path = Join-Path $target $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $path
        $entries += [ordered]@{ Path = $relative; Size = $file.Length; Sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
}
foreach ($required in @('RadioMini.dll','RadioMini.deps.json','RadioMini.runtimeconfig.json','Radio.Core.dll')) {
    if ($required -notin $entries.Path) { throw "Missing resource: $required" }
}
if (($entries.Path -contains 'coreclr.dll') -ne ($Mode -eq 'Full')) { throw 'Publish mode mismatch.' }
if ([Reflection.AssemblyName]::GetAssemblyName((Join-Path $target 'RadioMini.dll')).Version.ToString(3) -ne $Version) { throw 'App assembly and requested package version differ.' }
& (Join-Path $PSScriptRoot 'Build-AppHost.ps1') -OutputPath (Join-Path $OutputDirectory 'RadioMini.exe') -AssemblyPath (Join-Path $target 'RadioMini.dll') -RelativeDll "$hash/RadioMini.dll"
& (Join-Path $PSScriptRoot 'Build-Updater.ps1') -OutputPath (Join-Path $OutputDirectory 'bin/updater.exe')
$entryFiles = @(foreach ($relative in @('RadioMini.exe','bin/updater.exe')) {
    $file = Get-Item -LiteralPath (Join-Path $OutputDirectory $relative)
    [ordered]@{ Path = $relative; Size = $file.Length; Sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$package = [ordered]@{ Protocol = 1; DataSchema = 2; Version = $Version; Commit = $Commit; Mode = $Mode; Files = @($entries | Sort-Object Path); EntryFiles = $entryFiles }
$state = [ordered]@{ Protocol = 1; Current = $package; Pending = $null; Previous = $null; Trial = '' }
$ota = Join-Path $OutputDirectory 'data/ota'
New-Item -ItemType Directory -Path $ota -Force | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $target 'package.json'), ($package | ConvertTo-Json -Depth 8), $utf8)
[IO.File]::WriteAllText((Join-Path $ota 'state.json'), ($state | ConvertTo-Json -Depth 10), $utf8)
foreach ($doc in @('READ-ME.txt','THIRD-PARTY-NOTICES.txt')) {
    if (Test-Path -LiteralPath (Join-Path $source $doc)) { Copy-Item -LiteralPath (Join-Path $source $doc) -Destination (Join-Path $OutputDirectory $doc) }
}
Write-Output "Portable layout: $OutputDirectory (build $hash)"
