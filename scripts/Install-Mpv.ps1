param([string]$ProjectRoot)

$ErrorActionPreference = 'Stop'
if (!$ProjectRoot) { $ProjectRoot = Split-Path $PSScriptRoot -Parent }
$package = Get-Content -Raw (Join-Path $ProjectRoot 'scripts/mpv-package.json') | ConvertFrom-Json
$tools = Join-Path $ProjectRoot 'tools'
$destination = Join-Path $tools 'mpv'
if (Test-Path $destination) { throw 'tools/mpv already exists; inspect it before replacing anything.' }
New-Item -ItemType Directory -Path $tools -Force | Out-Null
$archive = Join-Path $tools $package.asset
Invoke-WebRequest -UseBasicParsing -Uri $package.url -OutFile $archive
$actual = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $package.sha256) { throw 'mpv archive checksum mismatch. Nothing has been extracted or executed.' }
New-Item -ItemType Directory -Path $destination | Out-Null
& tar.exe -xf $archive -C $destination
if ($LASTEXITCODE -ne 0) { throw 'Could not extract mpv with Windows tar.' }
if (!(Test-Path (Join-Path $destination 'mpv.exe'))) { throw 'mpv.exe not found in the verified archive.' }
Write-Output "Verified and extracted $($package.asset) to tools/mpv. No global installation or PATH changes."
