param([Parameter(Mandatory=$true)][string]$InputDirectory,
      [Parameter(Mandatory=$true)][string]$OutputDirectory,
      [Parameter(Mandatory=$true)][string]$Version,
      [Parameter(Mandatory=$true)][ValidateSet('Lite', 'Full')][string]$Mode)

# Also callable with tiny fixtures: no build, release, tag or network access.
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid update version.' }
$InputDirectory = [IO.Path]::GetFullPath($InputDirectory).TrimEnd('\')
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\')
if ($OutputDirectory.StartsWith($InputDirectory + '\', [StringComparison]::OrdinalIgnoreCase) -or $OutputDirectory -ieq $InputDirectory) {
    throw 'Update assets must be outside the portable folder.'
}
$manifestName = "Radio-Update-v$Version-$Mode-win-x64.json"
$manifestPath = Join-Path $OutputDirectory $manifestName
$installedManifest = Join-Path $InputDirectory 'radio.update-manifest.json'
if ((Test-Path -LiteralPath $manifestPath) -or (Test-Path -LiteralPath $installedManifest)) { throw 'Update manifest already exists; no files replaced.' }
$files = [System.Collections.Generic.List[object]]::new()
$queue = [System.Collections.Generic.Queue[string]]::new()
$queue.Enqueue($InputDirectory)
while ($queue.Count) {
    $directory = $queue.Dequeue()
    if ((Get-Item -LiteralPath $directory -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Links are not allowed.' }
    foreach ($file in Get-ChildItem -LiteralPath $directory -Force) {
        if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Links are not allowed.' }
        if ($file.PSIsContainer) { $queue.Enqueue($file.FullName); continue }
        $relative = $file.FullName.Substring($InputDirectory.Length + 1).Replace('\', '/')
        if ($relative.Length -gt 240 -or @($relative.Split('/') | Where-Object {
            $_ -notmatch '^[A-Za-z0-9_.-]+$' -or $_ -in @('.', '..', 'radio.settings.json', 'radio.update-manifest.json') -or
            $_.EndsWith('.') -or $_ -match '^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)'
        }).Count) { throw "Unsafe package path: $relative" }
        if ($file.Length -gt 300000000) { throw 'Package file too large.' }
        $files.Add(@{ File = $file; Path = $relative })
    }
}
$paths = @($files | ForEach-Object { $_.Path })
foreach ($required in @('RadioMini.exe', 'RadioMini.dll', 'RadioMini.deps.json', 'RadioMini.runtimeconfig.json', 'Radio.Core.dll')) {
    if ($required -notin $paths) { throw "Missing package file: $required" }
}
if (($paths -contains 'coreclr.dll') -ne ($Mode -eq 'Full')) { throw 'Runtime does not match package mode.' }
if ($files.Count -gt 5000 -or ($files.File | Measure-Object -Property Length -Sum).Sum -gt 1000000000) { throw 'Package too large.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$entries = @()
foreach ($entry in @($files | Sort-Object -Property Path)) {
    $file = $entry.File
    $temp = Join-Path $OutputDirectory ([Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        $input = [IO.File]::OpenRead($file.FullName)
        try {
            $output = [IO.File]::Create($temp)
            try {
                $gzip = [IO.Compression.GZipStream]::new($output, [IO.Compression.CompressionLevel]::Optimal, $true)
                try { $input.CopyTo($gzip) } finally { $gzip.Dispose() }
            } finally { $output.Dispose() }
        } finally { $input.Dispose() }
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $compressedHash = (Get-FileHash -LiteralPath $temp -Algorithm SHA256).Hash.ToLowerInvariant()
        $compressedSize = (Get-Item -LiteralPath $temp).Length
        $asset = "Radio-File-$compressedHash.gz"
        $assetPath = Join-Path $OutputDirectory $asset
        if (Test-Path -LiteralPath $assetPath) {
            if ((Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash -ine $compressedHash) { throw 'Existing asset is corrupt.' }
        } else { Move-Item -LiteralPath $temp -Destination $assetPath }
        $entries += [ordered]@{ path = $entry.Path; size = $file.Length; sha256 = $hash; asset = $asset; downloadSize = $compressedSize; downloadSha256 = $compressedHash }
    } finally { if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force } }
}
$json = [ordered]@{ schema = 1; version = $Version; mode = $Mode; architecture = 'win-x64'; files = @($entries) } | ConvertTo-Json -Depth 5
if ([Text.Encoding]::UTF8.GetByteCount($json) -gt 2000000) { throw 'Manifest too large.' }
[IO.File]::WriteAllText($manifestPath, $json, [Text.UTF8Encoding]::new($false))
# Baseline for safe managed deletions on the next update; not self-referential.
[IO.File]::WriteAllText($installedManifest, $json, [Text.UTF8Encoding]::new($false))
Write-Output "Update assets: $OutputDirectory"
